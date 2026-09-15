// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    // Computer-keyboard piano mapping, classic typing-keyboard layout: the A-row is the
    // white keys (A=C4=60 … K=C5) and the Q-row the black keys (W E T Y U). Z/X shift the
    // octave and C/V the velocity (see the handlers below); Automation mode moved to ⌘A.
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
        if (_vm is null || _vm.SuspendEnginePolling) return;
        if (e.Source is TextBox || e.Source is NumericUpDown) return;
        if ((e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control | KeyModifiers.Alt)) != 0) return;

        if (e.Key == Key.Space)
        {
            _vm.Transport.PlayStopCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Return)
        {
            _vm.Transport.StopCommand.Execute(null);   // stop; second press returns to 1.1
            e.Handled = true;
        }
    }

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
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0) OnMenuRedo(this, EventArgs.Empty);
            else OnMenuUndo(this, EventArgs.Empty);
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
        // tunnel phase (OnGlobalTransportKey) so a focused control can't steal them. R =
        // Record; M = Metronome (below) de-dupe auto-repeat via _heldKeys.

        // Esc: cancel an in-progress arrangement gesture, else clear the selection (req 1.2.6/2.11).
        if (!mod && e.Key == Key.Escape && Timeline.EscapePressed())
        {
            e.Handled = true;
            return;
        }

        // ⌘/⌃ + A: toggle Automation mode. Moved off plain 'A' (now a piano key) — gated to
        // when the piano-roll grid isn't focused, where ⌘A instead selects all notes.
        if (mod && e.Key == Key.A && (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Shift)) == 0
            && _editorRoll is not { GridFocused: true })
        {
            AutomationToggle.IsChecked = !(AutomationToggle.IsChecked == true);
            OnToggleAutomation(AutomationToggle, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (!mod && e.Key == Key.R && _heldKeys.Add(e.Key))
        {
            _vm.Transport.RecordOn = !_vm.Transport.RecordOn;
            e.Handled = true;
            return;
        }
        if (!mod && e.Key == Key.M && _heldKeys.Add(e.Key))
        {
            _vm.Transport.MetronomeOn = !_vm.Transport.MetronomeOn;
            e.Handled = true;
            return;
        }

        // ⌘M / ⌃M = open the Mixer window (or focus it if already open).
        if (mod && e.Key == Key.M)
        {
            ToggleMixerWindow();
            e.Handled = true;
            return;
        }

        // ⌘/⌃ + G = group the selected tracks; ⌘/⌃ + ⇧ + G = ungroup.
        if (mod && e.Key == Key.G && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            if (shift) { if (Timeline.UngroupSelection()) _vm.StatusText = "Ungrouped"; }
            else if (Timeline.GroupSelection()) _vm.StatusText = "Grouped tracks";
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

        if (!mod && KeyToPitch.TryGetValue(e.Key, out int basePitch) && _heldKeys.Add(e.Key))
        {
            int pitch = Math.Clamp(basePitch + _typingOctave * 12, 0, 127);
            _heldNotePitch[e.Key] = pitch;   // release at this exact pitch even if the octave changes
            Engine.NoteOn(pitch, _typingVelocity / 127f);
            e.Handled = true;
        }
        else base.OnKeyDown(e);
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
