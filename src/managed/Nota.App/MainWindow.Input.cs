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

namespace Nota.App;

public partial class MainWindow
{
    // Computer-keyboard piano mapping, classic typing-keyboard layout: the A-row is the
    // white keys (A=C4=60 … K=C5) and the Q-row the black keys (W E T Y U). Z/X shift the
    // octave and C/V the velocity (see the handlers below); Automation mode moved to ⌘⇧A.
    // These are BASE pitches at octave 0 — _typingOctave*12 is added when a note plays.
    private static readonly Dictionary<Key, int> KeyToPitch = new()
    {
        [Key.A] = 60, [Key.W] = 61, [Key.S] = 62, [Key.E] = 63, [Key.D] = 64,
        [Key.F] = 65, [Key.T] = 66, [Key.G] = 67, [Key.Y] = 68, [Key.H] = 69,
        [Key.U] = 70, [Key.J] = 71, [Key.K] = 72,
    };

    // Tunnel-phase handler for the two transport keys that a focused control would
    // otherwise steal (Space activates a focused Button/CheckBox/ToggleButton; Space/Enter
    // open a focused ComboBox; a focused Slider/knob may swallow them too). Running before
    // any focused control guarantees Space = Play/Stop and Return = Stop everywhere — the
    // cause of "the transport buttons periodically don't work". Text entry is exempt so
    // spaces/newlines still type. Everything else stays in the bubble-phase OnKeyDown.
    private void OnGlobalTransportKey(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        // ⌘⇧P / Ctrl+Shift+P: the command palette, from any window and any focus — a text
        // field included, as the chord types nothing (CP-1). Again closes it.
        if (e.Key == Key.P && ArrangementView.IsPrimaryDown(e.KeyModifiers)
            && (e.KeyModifiers & KeyModifiers.Shift) != 0 && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            TogglePalette(e.Source is Avalonia.Visual v ? TopLevel.GetTopLevel(v) as Window : null);
            e.Handled = true;
            return;
        }
        if (_vm.SuspendEnginePolling || PaletteOpen) return;   // the palette has the keyboard
        if (e.Source is TextBox || e.Source is NumericUpDown) return;
        if ((e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control | KeyModifiers.Alt)) != 0) return;

        if (e.Key == Key.Space)
        {
            _commands.Run("transport.playStop");
            e.Handled = true;
        }
        else if (e.Key == Key.Return)
        {
            // In the Session grid Return launches the selected slot or scene (design 1a);
            // everywhere else it stops (second press returns to 1.1).
            if (SessionFocused) _session!.LaunchSelection();
            else _commands.Run("transport.stop");
            e.Handled = true;
        }
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
        bool meta = (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0;
        if (meta && e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0) _commands.Run("edit.redo");
            else _commands.Run("edit.undo");
            e.Handled = true;
            return;
        }
        HandleKeyDown(e);
    }

    // --- live play from the computer keyboard + basic shortcuts (M7-3) ---
    protected override void OnKeyDown(KeyEventArgs e) => HandleKeyDown(e);

    // Shared with the floating detail window (see HandleFloatingKeyDown).
    internal void HandleKeyDown(KeyEventArgs e)
    {
        if (_vm is null || _vm.SuspendEnginePolling || e.Source is TextBox || e.Source is NumericUpDown) { base.OnKeyDown(e); return; }

        // ⌘/⌃/⌥ combos are shortcuts (menu accelerators handle them) — never
        // sound a note. Bare letters still play via the KeyToPitch map below.
        bool mod = (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control | KeyModifiers.Alt)) != 0;

        // Tab cycles the detail panel's Devices/Pattern/Clip tabs when it was the last area used.
        if (!mod && e.Key == Key.Tab && DetailPanel.IsVisible && _detailWasLastFocused)
        {
            ToggleDetailTab();
            e.Handled = true;
            return;
        }

        // Transport hotkeys: Space = Play/Stop and Return = Stop are handled earlier in the
        // tunnel phase (OnGlobalTransportKey) so a focused control can't steal them. ⌘R =
        // Record; ⌘M = Metronome (below) — also menu accelerators, so on macOS the native
        // menu usually handles them first; de-duped against auto-repeat via _heldKeys.

        // Esc: cancel an in-progress arrangement gesture, else clear the selection (req 1.2.6/2.11).
        if (!mod && e.Key == Key.Escape && Timeline.EscapePressed())
        {
            e.Handled = true;
            return;
        }

        // Track headers focused (the last click landed in the header column): ⌘A selects every
        // track, ⌘C/X/V/D copy/cut/paste/duplicate the selected tracks, Delete removes them.
        // Anything with nothing to act on falls through to the clip handling below.
        if (_trackHeadersFocused && Timeline.IsVisible && HandleTrackKey(e))
        {
            e.Handled = true;
            return;
        }

        // ⌘/⌃ + ⇧ + A: toggle Automation mode. ⌘/⌃ + A: select all arrangement clips —
        // gated to when the piano-roll grid isn't focused, where ⌘A instead selects all notes.
        if (mod && e.Key == Key.A && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
            {
                _commands.Run("view.automation");
                e.Handled = true;
                return;
            }
            if (_editorRoll is not { GridFocused: true } && Timeline.SelectAllClips())
            {
                e.Handled = true;
                return;
            }
        }
        bool plainMod = mod && (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Shift)) == 0;
        if (plainMod && e.Key == Key.R)
        {
            if (_heldKeys.Add(e.Key)) _commands.Run("transport.record");
            e.Handled = true;   // an auto-repeat is swallowed, not passed on to the menu
            return;
        }
        if (plainMod && e.Key == Key.M)
        {
            if (_heldKeys.Add(e.Key)) _commands.Run("transport.metronome");
            e.Handled = true;   // an auto-repeat is swallowed, not passed on to the menu
            return;
        }

        // ⌘⇧M / ⌃⇧M = open the Mixer window (or focus it if already open).
        if (mod && e.Key == Key.M && (e.KeyModifiers & KeyModifiers.Shift) != 0 && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            _commands.Run("view.mixer");
            e.Handled = true;
            return;
        }

        // ⌘/⌃ + G = group the selected tracks; ⌘/⌃ + ⇧ + G = ungroup.
        if (mod && e.Key == Key.G && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            _commands.Run((e.KeyModifiers & KeyModifiers.Shift) != 0 ? "track.ungroup" : "track.group");
            e.Handled = true;
            return;
        }

        // ⌘⇧V / ⌃⇧V = Paste Bounced Audio: render the last time selection through its track's
        // chain and paste it onto the focused audio track at the playhead (MainWindow.PasteBounced).
        if (e.Key == Key.V && ArrangementView.IsPrimaryDown(e.KeyModifiers)
            && (e.KeyModifiers & KeyModifiers.Shift) != 0 && (e.KeyModifiers & KeyModifiers.Alt) == 0
            && _editorRoll is not { GridFocused: true })
        {
            _commands.Run("edit.pasteBounced");
            e.Handled = true;
            return;
        }

        // ⌘⇧C / ⌃⇧C = Copy to Session (Arrangement) / Copy to Arrangement (Session) — also the
        // Edit menu's accelerator, which gets there first on macOS. The focused Session grid
        // handles the key itself; the piano-roll grid keeps it for its notes.
        if (e.Key == Key.C && ArrangementView.IsPrimaryDown(e.KeyModifiers)
            && (e.KeyModifiers & KeyModifiers.Shift) != 0 && (e.KeyModifiers & KeyModifiers.Alt) == 0
            && _editorRoll is not { GridFocused: true })
        {
            _commands.Run("edit.copyToOtherView");
            e.Handled = true;
            return;
        }

        // ⌘⇧D / ⌃⇧D = Duplicate Time, ⌘⇧I / ⌃⇧I = Insert Silence over the arrangement time
        // selection. On macOS the Edit menu's accelerators get there first.
        if ((e.Key == Key.D || e.Key == Key.I) && !OperatingSystem.IsMacOS()
            && ArrangementView.IsPrimaryDown(e.KeyModifiers)
            && (e.KeyModifiers & KeyModifiers.Shift) != 0 && (e.KeyModifiers & KeyModifiers.Alt) == 0
            && Timeline.HasTimeSelection)
        {
            _commands.Run(e.Key == Key.D ? "edit.duplicateTime" : "edit.insertSilence");
            e.Handled = true;
            return;
        }

        // ⌘/⌃ + C/X/V = copy/cut/paste the selected arrangement clip. Skipped while the
        // piano-roll grid is focused (it copies/pastes notes) and when nothing is
        // actionable, so the event falls through to other handlers.
        if (mod && (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Shift)) == 0
            && _editorRoll is not { GridFocused: true })
        {
            // In automation mode a selected time-range takes priority: C/X/V move the
            // envelope, not the clip. Each returns false when there's nothing to act on,
            // so the shortcut falls through to the clip clipboard below.
            if (Timeline.AutomationMode)
            {
                if (e.Key == Key.C && Timeline.CopyAutoSelection()) { _vm.StatusText = "Copied automation"; e.Handled = true; return; }
                if (e.Key == Key.X && Timeline.CutAutoSelection()) { _vm.StatusText = "Cut automation"; e.Handled = true; return; }
                if (e.Key == Key.V && Timeline.PasteAuto()) { _vm.StatusText = "Pasted automation"; e.Handled = true; return; }
            }
            if (e.Key == Key.C && Timeline.CopySelectedClip()) { _vm.StatusText = "Copied clip(s)"; e.Handled = true; return; }
            if (e.Key == Key.X && Timeline.CutSelectedClip()) { _session?.Refresh(); _vm.StatusText = "Cut clip(s)"; e.Handled = true; return; }
            if (e.Key == Key.V && Timeline.PasteClipboard()) { _session?.Refresh(); _vm.StatusText = "Pasted clip(s)"; e.Handled = true; return; }
        }

        // Delete/Backspace = clear the selected automation range (automation mode), else
        // remove the selected arrangement clip. Only consumed when something is actually
        // selected, so the piano roll (which has focus + its own Delete handler) keeps
        // note-deletion.
        if (!mod && (e.Key == Key.Delete || e.Key == Key.Back) && Timeline.AutomationMode && Timeline.DeleteAutoSelection())
        {
            _vm.StatusText = "Deleted automation";
            e.Handled = true;
            return;
        }
        if (!mod && (e.Key == Key.Delete || e.Key == Key.Back) && Timeline.DeleteTimeSelection())
        {
            _session?.Refresh();
            _vm.StatusText = "Deleted range";
            e.Handled = true;
            return;
        }
        if (!mod && (e.Key == Key.Delete || e.Key == Key.Back) && Timeline.DeleteSelectedClips())
        {
            _session?.Refresh();
            _vm.StatusText = "Deleted clip(s)";
            e.Handled = true;
            return;
        }

        // 0 = toggle the selected clip(s) active/inactive (clip deactivate). De-duped
        // against auto-repeat via _heldKeys; skipped while the piano-roll grid is focused.
        if (!mod && (e.Key == Key.D0 || e.Key == Key.NumPad0)
            && _editorRoll is not { GridFocused: true } && _heldKeys.Add(e.Key))
        {
            if (Timeline.ToggleSelectedClipsActive()) { _session?.Refresh(); _vm.StatusText = "Toggled clip(s)"; }
            e.Handled = true;
            return;
        }

        // Z / X: shift the typing-keyboard octave; C / V: nudge the typing velocity.
        // De-duped against auto-repeat so one press = one step; skipped in the piano-roll grid.
        if (!mod && _editorRoll is not { GridFocused: true }
            && (e.Key == Key.Z || e.Key == Key.X || e.Key == Key.C || e.Key == Key.V) && _heldKeys.Add(e.Key))
        {
            if (e.Key == Key.Z) ShiftTypingOctave(-1);
            else if (e.Key == Key.X) ShiftTypingOctave(+1);
            else if (e.Key == Key.C) ShiftTypingVelocity(-10);
            else ShiftTypingVelocity(+10);
            e.Handled = true;
            return;
        }

        // Nothing above took it: the menu's shortcuts (Ctrl+S, Ctrl+Z…) on Windows / Linux.
        if (mod && TryMenuShortcut(e))
        {
            e.Handled = true;
            return;
        }

        if (!mod && KeyToPitch.TryGetValue(e.Key, out int basePitch) && _heldKeys.Add(e.Key))
        {
            int pitch = Math.Clamp(basePitch + _typingOctave * 12, 0, 127);
            _heldNotePitch[e.Key] = pitch;   // release at this exact pitch even if the octave changes
            Engine.NoteOn(pitch, _typingVelocity / 127f);
            e.Handled = true;
        }
        else base.OnKeyDown(e);
    }

    // Track-header keys (see HandleKeyDown). True when the key acted on tracks.
    private bool HandleTrackKey(KeyEventArgs e)
    {
        if (_vm is null) return false;
        bool extra = (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Shift)) != 0;
        if (ArrangementView.IsPrimaryDown(e.KeyModifiers) && !extra)
        {
            switch (e.Key)
            {
                case Key.A: return Timeline.SelectAllTracks();
                case Key.C: return Report(Timeline.CopySelectedTracks(), "Copied track(s)");
                case Key.X: return Report(Timeline.CutSelectedTracks(), "Cut track(s)");
                case Key.V: return Report(Timeline.PasteTracks(), "Pasted track(s)");
                // macOS: the Edit menu's ⌘D accelerator gets there first (OnMenuDuplicate).
                case Key.D when !OperatingSystem.IsMacOS(): return Report(Timeline.DuplicateSelectedTracks(), "Duplicated track(s)");
            }
            return false;
        }
        if (e.KeyModifiers == KeyModifiers.None && (e.Key == Key.Delete || e.Key == Key.Back))
            return Report(Timeline.DeleteSelectedTracks(), "Deleted track(s)");
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
        // Clear the held-key latch for every key (piano notes + toggles like R/M/Z/X/C/V that
        // de-dupe auto-repeat), and release the note at the exact pitch it started on.
        _heldKeys.Remove(e.Key);
        if (_vm is not null && _heldNotePitch.Remove(e.Key, out int pitch))
        {
            Engine.NoteOff(pitch);
            e.Handled = true;
        }
        else base.OnKeyUp(e);
    }

    // Z/X: shift the computer-keyboard octave (typing keyboard). Clamped so the whole
    // A–K range stays within MIDI 0..127. Shows the resulting octave of the base 'A' (C) key.
    private void ShiftTypingOctave(int delta)
    {
        int prev = _typingOctave;
        _typingOctave = Math.Clamp(_typingOctave + delta, -4, 4);
        if (_vm is not null && _typingOctave != prev)
            _vm.StatusText = $"Keyboard octave: C{4 + _typingOctave}";
    }

    // C/V: nudge the velocity typed notes play at (1..127, typing keyboard).
    private void ShiftTypingVelocity(int delta)
    {
        int prev = _typingVelocity;
        _typingVelocity = Math.Clamp(_typingVelocity + delta, 1, 127);
        if (_vm is not null && _typingVelocity != prev)
            _vm.StatusText = $"Keyboard velocity: {_typingVelocity}";
    }
}
