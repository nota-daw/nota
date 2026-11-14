// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;
using Nota.Application.Localization;
using Nota.Application.Input;
using Nota.App.Input;
using Nota.Application.Palette;

namespace Nota.App;

public partial class MainWindow
{
    // Computer-keyboard piano: the note-map actions (notes.c … notes.c2, specs/096) play C4 up
    // to C5 — by default the A-row is the white keys and the Q-row the black keys. Z/X shift
    // the octave and C/V the velocity. Pitches are C4-based; _typingOctave*12 is added.
    private const int TypingBasePitch = 60;

    // Tunnel-phase handler for the two transport keys that a focused control would
    // otherwise steal (Space activates a focused Button/CheckBox/ToggleButton; Space/Enter
    // open a focused ComboBox; a focused Slider/knob may swallow them too). Running before
    // any focused control guarantees Space = Play/Stop and Return = Stop everywhere — the
    // cause of "the transport buttons periodically don't work". Text entry is exempt so
    // spaces/newlines still type. Everything else stays in the bubble-phase OnKeyDown.
    private void OnGlobalTransportKey(object? sender, KeyEventArgs e)
    {
        if (_vm is null || Keys.Capturing) return;   // the Shortcuts editor is recording a key
        // ⌘⇧P / Ctrl+Shift+P: the command palette, from any window and any focus — a text
        // field included, as the chord types nothing (CP-1). Again closes it.
        // A palette key that types something (a bare or ⇧ key) leaves text fields alone.
        if (Keys.Is(e, CommandCatalog.Palette)
            && (KeyTokens.Chord(e) is { IsChord: true } || e.Source is not (TextBox or NumericUpDown)))
        {
            TogglePalette(e.Source is Avalonia.Visual v ? TopLevel.GetTopLevel(v) as Window : null);
            e.Handled = true;
            return;
        }
        if (_vm.SuspendEnginePolling || PaletteOpen) return;   // the palette has the keyboard
        // The Assistant's key is a chord, so it types nothing: it closes the panel from its own
        // input too (specs/098). Every other key stays with a focused text field (FR-005).
        if (Keys.Is(e, "view.assistant") && KeyTokens.Chord(e) is { IsChord: true })
        {
            ToggleAssistant();
            e.Handled = true;
            return;
        }
        if (e.Source is TextBox || e.Source is NumericUpDown) return;
        // Keys in the Assistant panel (its links, Back / Next / Done) are the panel's: Return or
        // Space on a focused link runs the link, not Stop or Play (specs/098).
        if (InAssistant(e.Source)) return;
        if (TakesOverflowKey(e)) { e.Handled = true; return; }   // ⋯ focused by Tab (#520)

        if (Keys.Is(e, "session.launchSelection") && SessionFocused)
        {
            // In the Session grid Return launches the selected slot or scene (design 1a).
            _session!.LaunchSelection();
            e.Handled = true;
            return;
        }
        var transport = Keys.Which(e, "transport.playStop", "transport.playPause", "transport.stop");
        if (transport is null) return;
        // A focused view that binds this key itself wins (specs/096 R3): leave it to the view.
        if (KeyTokens.Chord(e) is { } chord && ScopeOf(e.Source) is { } scope && Keys.Bindings.Resolve(scope, chord) is not null) return;
        if (transport == "transport.stop" && Browser.TakesReturn(e.Source))
        {
            // A Modular node selected in the browser: Return adds it (specs/095), so leave the
            // key to the list rather than stopping the transport.
            return;
        }
        // Space = Play/Stop, ⇧Space pauses (the playhead stays, #51), Return = Stop (second
        // press returns to 1.1).
        _commands.Run(transport);
        e.Handled = true;
    }

    // The focus scope the key came from (specs/096): the view that handles its own keys
    // before the window does, or null for the window itself.
    private KeyScope? ScopeOf(object? source)
    {
        if (source is not Visual v) return null;
        foreach (var a in v.GetSelfAndVisualAncestors())
            switch (a)
            {
                case SessionView: return KeyScope.Session;
                case PianoRollView: return _editorRoll is { GridFocused: true } ? KeyScope.PianoRoll : null;
                case ModularView: return KeyScope.Modular;
                case DeviceChainView: return KeyScope.Devices;
                case BrowserView b: return b.FilesFocused ? KeyScope.Browser : null;
                case AudioClipEditorView: return KeyScope.AudioEditor;
            }
        return null;
    }

    // A text field keeps the keyboard until focus moves elsewhere — but the arrangement, piano
    // roll, session grid etc. are custom-drawn and don't take focus on click, so after typing
    // into e.g. the browser search every key kept going to the field. Any press outside the
    // focused text field (or its NumericUpDown, spinners included) now drops its focus; a press
    // on another focusable control still focuses that control as usual. Tunnel +
    // handledEventsToo so canvases that consume the press can't keep it from running.
    internal static void ReleaseTextFocusOnOutsidePress(TopLevel top)
    {
        top.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (top.FocusManager?.GetFocusedElement() is not Visual focused) return;
            Visual? field = focused.FindAncestorOfType<TextBox>(includeSelf: true);
            if (field is null) return;
            field = field.FindAncestorOfType<NumericUpDown>() ?? field;
            if (e.Source is Visual src && src.GetSelfAndVisualAncestors().Contains(field)) return;
            top.FocusManager.Focus(null);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    // The Session grid has the keyboard: its arrows / Return / ⌘C… act on its selection.
    private bool SessionFocused => _session is { IsVisible: true, IsKeyboardFocusWithin: true };

    // Route a floating detail window's key presses through the same handling. Undo/redo are
    // handled explicitly here because their native-menu accelerators only target the main
    // window (on macOS AppKit usually consumes them first, so this is a cross-platform
    // fallback that won't double up).
    internal void HandleTransportKeyTunnel(KeyEventArgs e) => OnGlobalTransportKey(this, e);
    internal void HandleFloatingKeyDown(KeyEventArgs e)
    {
        if (Keys.Capturing) return;
        if (Keys.Which(e, "edit.undo", "edit.redo") is { } undo)
        {
            _commands.Run(undo);
            e.Handled = true;
            return;
        }
        // The floating window has no native menu, so ⌘1–⌘5 (the grid) never arrive as menu key
        // equivalents there: run them here. Windows / Linux stop here too, so their in-window
        // menu path below doesn't run them a second time.
        if (Keys.Which(e, GridCommands) is { } grid)
        {
            _commands.Run(grid);
            e.Handled = true;
            return;
        }
        HandleKeyDown(e);
    }

    private static readonly string[] GridCommands = { "grid.narrower", "grid.wider", "grid.triplets", "grid.snap", "grid.adaptive" };

    // --- live play from the computer keyboard + basic shortcuts (M7-3) ---
    protected override void OnKeyDown(KeyEventArgs e) => HandleKeyDown(e);

    // Shared with the floating detail window (see HandleFloatingKeyDown).
    internal void HandleKeyDown(KeyEventArgs e)
    {
        if (_vm is null || Keys.Capturing || _vm.SuspendEnginePolling || e.Source is TextBox || e.Source is NumericUpDown
            || InAssistant(e.Source)) { base.OnKeyDown(e); return; }

        // Every key here is a binding (specs/096): Keys.Is asks the user's keys, so the gates
        // and the order below are what decides between actions, never a hard-coded key.

        // Cycle the detail panel's Devices/Pattern/Clip tabs when it was the last area used.
        if (Keys.Is(e, "view.cycleDetailTab") && DetailPanel.IsVisible && _detailWasLastFocused)
        {
            ToggleDetailTab();
            e.Handled = true;
            return;
        }

        // Transport hotkeys: Play/Stop, Pause and Stop are handled earlier in the tunnel phase
        // (OnGlobalTransportKey) so a focused control can't steal them. Record, retry take,
        // loop record mode and the metronome (below) are menu accelerators too, so on macOS the
        // native menu usually handles them first; de-duped against auto-repeat via _heldKeys.

        // Cancel (Esc): an in-progress gesture, else the selection (req 1.2.6/2.11). A warp
        // marker / segment tempo mid-drag in the audio clip editor drops first (#140).
        bool cancel = Keys.Is(e, "edit.cancel");
        if (cancel && _audioEditor is { IsEffectivelyVisible: true } audioEd && audioEd.CancelGesture())
        {
            e.Handled = true;
            return;
        }
        // Cancel ends macro mapping on a rack card (specs/040) before it touches the arrangement.
        if (cancel && RackCardState.EndAllMapping())
        {
            if (DetailBody.Content is DeviceChainView chain) chain.Refresh();
            e.Handled = true;
            return;
        }
        if (cancel && Timeline.EscapePressed())
        {
            e.Handled = true;
            return;
        }

        // Track headers focused (the last click landed in the header column): select all,
        // copy / cut / paste / duplicate the selected tracks, delete them. Anything with nothing
        // to act on falls through to the clip handling below.
        if (_trackHeadersFocused && Timeline.IsVisible && HandleTrackKey(e))
        {
            e.Handled = true;
            return;
        }

        if (Keys.Is(e, "view.automation"))
        {
            _commands.Run("view.automation");
            e.Handled = true;
            return;
        }
        // Select all arrangement clips — not while the piano-roll grid is focused, where it
        // selects all notes instead.
        if (Keys.Is(e, "edit.selectAll") && _editorRoll is not { GridFocused: true } && Timeline.SelectAllClips())
        {
            e.Handled = true;
            return;
        }
        // Held down these run once, not on every auto-repeat (the repeat is swallowed, not
        // passed on to the menu). Retry take: discard the take and record it again (#185).
        if (Keys.Which(e, "transport.record", "transport.retryTake", "transport.loopRecordMode", "transport.metronome") is { } held)
        {
            if (_heldKeys.Add(e.Key)) _commands.Run(held);
            e.Handled = true;
            return;
        }

        // Show or hide the Mixer, docked or in its window (specs/054); group / ungroup the
        // selected tracks.
        if (Keys.Which(e, "view.mixer", "track.group", "track.ungroup") is { } win)
        {
            _commands.Run(win);
            e.Handled = true;
            return;
        }

        // Paste Bounced Audio: render the last time selection through its track's chain and
        // paste it onto the focused audio track at the playhead (MainWindow.PasteBounced).
        if (Keys.Is(e, "edit.pasteBounced") && _editorRoll is not { GridFocused: true })
        {
            _commands.Run("edit.pasteBounced");
            e.Handled = true;
            return;
        }

        // Copy to Session (Arrangement) / Copy to Arrangement (Session) — also the Edit menu's
        // accelerator, which gets there first on macOS. The focused Session grid handles the
        // key itself; the piano-roll grid keeps it for its notes.
        if (Keys.Is(e, "edit.copyToOtherView") && _editorRoll is not { GridFocused: true })
        {
            _commands.Run("edit.copyToOtherView");
            e.Handled = true;
            return;
        }

        // Duplicate Time, Insert Silence over the arrangement time selection. On macOS the Edit
        // menu's accelerators get there first.
        if (!OperatingSystem.IsMacOS() && Timeline.HasTimeSelection
            && Keys.Which(e, "edit.duplicateTime", "edit.insertSilence") is { } timeOp)
        {
            _commands.Run(timeOp);
            e.Handled = true;
            return;
        }

        // Copy / cut / paste the selected arrangement clip. Skipped while the piano-roll grid is
        // focused (it copies / pastes notes) and when nothing is actionable, so the event falls
        // through to other handlers.
        var clip = _editorRoll is not { GridFocused: true } ? Keys.Which(e, "edit.copy", "edit.cut", "edit.paste") : null;
        if (clip is not null)
        {
            // In automation mode a selected time-range takes priority: the keys move the
            // envelope, not the clip. Each returns false when there's nothing to act on, so the
            // shortcut falls through to the clip clipboard below.
            if (Timeline.AutomationMode)
            {
                if (clip == "edit.copy" && Timeline.CopyAutoSelection()) { _vm.StatusText = Loc.T("main.input.copied_automation"); e.Handled = true; return; }
                if (clip == "edit.cut" && Timeline.CutAutoSelection()) { _vm.StatusText = Loc.T("main.input.cut_automation"); e.Handled = true; return; }
                if (clip == "edit.paste" && Timeline.PasteAuto()) { _vm.StatusText = Loc.T("main.input.pasted_automation"); e.Handled = true; return; }
            }
            // A time range (Shift+drag) copies / cuts the covered slice; it and a clip selection
            // are exclusive, so these return false whenever clips are selected instead.
            if (clip == "edit.copy" && Timeline.CopyTimeSelection()) { _vm.StatusText = Loc.T("main.copied_range"); e.Handled = true; return; }
            if (clip == "edit.cut" && Timeline.CutTimeSelection()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.cut_range"); e.Handled = true; return; }
            if (clip == "edit.copy" && Timeline.CopySelectedClip()) { _vm.StatusText = Loc.T("main.input.copied_clip_s"); e.Handled = true; return; }
            if (clip == "edit.cut" && Timeline.CutSelectedClip()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.input.cut_clip_s"); e.Handled = true; return; }
            if (clip == "edit.paste" && Timeline.PasteClipboard()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.input.pasted_clip_s"); e.Handled = true; return; }
        }

        // Nudge the selected arrangement clips by the grid step (fine: a 64th); move them to the
        // next track that takes them (#302). Only with clips selected, and not while the piano
        // roll, the detail panel or the floating detail window has the keys.
        if (ArrangementShown && !_detailWasLastFocused && _detailWindow?.IsActive != true && _editorRoll is not { GridFocused: true }
            && Keys.Which(e, ClipMoveKeys) is { } move)
        {
            bool done = move switch
            {
                "arr.nudgeLeft" => Timeline.NudgeSelectedClips(-1, fine: false),
                "arr.nudgeRight" => Timeline.NudgeSelectedClips(1, fine: false),
                "arr.nudgeFineLeft" => Timeline.NudgeSelectedClips(-1, fine: true),
                "arr.nudgeFineRight" => Timeline.NudgeSelectedClips(1, fine: true),
                "arr.moveToTrackUp" => Timeline.MoveSelectedClipsToTrack(-1),
                _ => Timeline.MoveSelectedClipsToTrack(1),
            };
            if (done) { _session?.Refresh(); e.Handled = true; return; }
        }

        // Delete = clear the selected automation range (automation mode), else remove the
        // selected arrangement clip. Only consumed when something is actually selected, so the
        // piano roll (which has focus + its own delete handler) keeps note-deletion.
        bool delete = Keys.Is(e, "edit.delete");
        if (delete && Timeline.AutomationMode && Timeline.DeleteAutoSelection())
        {
            _vm.StatusText = Loc.T("main.input.deleted_automation");
            e.Handled = true;
            return;
        }
        if (delete && Timeline.DeleteTimeSelection())
        {
            _session?.Refresh();
            _vm.StatusText = Loc.T("main.input.deleted_range");
            e.Handled = true;
            return;
        }
        if (delete && Timeline.DeleteSelectedClips())
        {
            _session?.Refresh();
            _vm.StatusText = Loc.T("main.deleted_clip_s");
            e.Handled = true;
            return;
        }

        // Toggle the selected clip(s) active/inactive (clip deactivate). De-duped against
        // auto-repeat via _heldKeys; skipped while the piano-roll grid is focused.
        if (Keys.Is(e, "edit.toggleClipActive") && _editorRoll is not { GridFocused: true } && _heldKeys.Add(e.Key))
        {
            if (Timeline.ToggleSelectedClipsActive()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.toggled_clip_s"); }
            e.Handled = true;
            return;
        }

        // Show playhead (#105) — scroll the arrangement to it, without moving it.
        if (Keys.Is(e, "view.showPlayhead") && _heldKeys.Add(e.Key))
        {
            _commands.Run("view.showPlayhead");
            e.Handled = true;
            return;
        }

        // Shift the typing-keyboard octave; nudge the typing velocity. De-duped against
        // auto-repeat so one press = one step; skipped in the piano-roll grid.
        if (_editorRoll is not { GridFocused: true }
            && Keys.Which(e, "notes.octaveDown", "notes.octaveUp", "notes.velocityDown", "notes.velocityUp") is { } typing
            && _heldKeys.Add(e.Key))
        {
            switch (typing)
            {
                case "notes.octaveDown": ShiftTypingOctave(-1); break;
                case "notes.octaveUp": ShiftTypingOctave(+1); break;
                case "notes.velocityDown": ShiftTypingVelocity(-10); break;
                default: ShiftTypingVelocity(+10); break;
            }
            e.Handled = true;
            return;
        }

        // Nothing above took it: the menu's shortcuts (Ctrl+S, Ctrl+Z…) on Windows / Linux —
        // chords and bare function keys (F11 = full screen), the menu gestures — then any other
        // command key: an alternative key, a bare key the menu can't carry, a palette-only command.
        if (TryMenuShortcut(e) || TryCommandKey(e))
        {
            e.Handled = true;
            return;
        }

        if (Keys.NoteIndex(e) is int note and >= 0 && _heldKeys.Add(e.Key))
        {
            int pitch = Math.Clamp(TypingBasePitch + note + _typingOctave * 12, 0, 127);
            _heldNotePitch[e.Key] = pitch;   // release at this exact pitch even if the octave changes
            Engine.NoteOn(pitch, _typingVelocity / 127f);
            e.Handled = true;
        }
        else base.OnKeyDown(e);
    }

    // A palette command bound to this key (specs/096) that no handler above and no menu item took:
    // window-wide commands anywhere, a view's commands while that view has the keyboard.
    private bool TryCommandKey(KeyEventArgs e)
    {
        if (KeyTokens.Chord(e) is not { } chord || Keys.Claimed(e)) return false;
        var focus = ScopeOf(e.Source);
        foreach (var a in Keys.Bindings.Actions)
        {
            if (!a.IsCommand || !(a.Scope.IsWindowLayer() || a.Scope == focus)) continue;
            // Copy, cut, paste and delete are always handled by the window and the focused views
            // above, which know what is selected; their menu handlers act on the clip editor even
            // when it no longer has focus. (Duplicate and Select all go to theirs: those route by
            // focus — Session, the piano-roll grid, track headers, the arrangement.)
            if (a.NoMenuKey) continue;
            if (!Keys.Bindings.Matches(a.Id, chord) || _commands.Get(a.Id) is not { IsBound: true }) continue;
            if (_heldKeys.Contains(e.Key)) return true;   // one run per press, not per auto-repeat
            _heldKeys.Add(e.Key);
            _commands.Run(a.Id);
            return true;
        }
        return false;
    }

    private static readonly string[] ClipMoveKeys =
        { "arr.nudgeLeft", "arr.nudgeRight", "arr.nudgeFineLeft", "arr.nudgeFineRight", "arr.moveToTrackUp", "arr.moveToTrackDown" };

    // Track-header keys (see HandleKeyDown). True when the key acted on tracks.
    private bool HandleTrackKey(KeyEventArgs e)
    {
        if (_vm is null) return false;
        switch (Keys.Which(e, "edit.selectAll", "edit.copy", "edit.cut", "edit.paste", "edit.duplicate", "edit.delete"))
        {
            case "edit.selectAll": return Timeline.SelectAllTracks();
            case "edit.copy": return Report(Timeline.CopySelectedTracks(), Loc.T("main.copied_track_s"));
            case "edit.cut": return Report(Timeline.CutSelectedTracks(), Loc.T("main.cut_track_s"));
            case "edit.paste": return Report(Timeline.PasteTracks(), Loc.T("main.pasted_track_s"));
            // macOS: the Edit menu's Duplicate accelerator gets there first (OnMenuDuplicate).
            case "edit.duplicate" when !OperatingSystem.IsMacOS(): return Report(Timeline.DuplicateSelectedTracks(), Loc.T("main.duplicated_track_s"));
            case "edit.delete": return Report(Timeline.DeleteSelectedTracks(), Loc.T("main.deleted_track_s"));
        }
        return false;

        bool Report(bool done, string status)
        {
            if (done) _vm.StatusText = status;
            return done;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e) => HandleKeyUp(e);

    internal void HandleKeyUp(KeyEventArgs e)
    {
        // Clear the held-key latch for every key (piano notes + toggles like record, metronome
        // and the octave / velocity keys that de-dupe auto-repeat), and release the note at the exact pitch it started on.
        _heldKeys.Remove(e.Key);
        if (_vm is not null && _heldNotePitch.Remove(e.Key, out int pitch))
        {
            Engine.NoteOff(pitch);
            e.Handled = true;
        }
        else base.OnKeyUp(e);
    }

    // Shift the computer-keyboard octave (typing keyboard). Clamped so the whole
    // A–K range stays within MIDI 0..127. Shows the resulting octave of the base 'A' (C) key.
    private void ShiftTypingOctave(int delta)
    {
        int prev = _typingOctave;
        _typingOctave = Math.Clamp(_typingOctave + delta, -4, 4);
        if (_vm is not null && _typingOctave != prev)
            _vm.StatusText = Loc.F("main.input.keyboard_octave_c", 4 + _typingOctave);
    }

    // Nudge the velocity typed notes play at (1..127, typing keyboard).
    private void ShiftTypingVelocity(int delta)
    {
        int prev = _typingVelocity;
        _typingVelocity = Math.Clamp(_typingVelocity + delta, 1, 127);
        if (_vm is not null && _typingVelocity != prev)
            _vm.StatusText = Loc.F("main.input.keyboard_velocity", _typingVelocity);
    }
}
