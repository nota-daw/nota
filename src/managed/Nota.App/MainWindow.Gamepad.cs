// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using Avalonia;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.App;

/// <summary>Gamepad input, on the UI tick (hooked off
/// <see cref="MainWindowViewModel.PlayheadUpdated"/>). Two shapes, one destination:
/// button edges are drained from the engine's queue, and stick/trigger positions are
/// sampled from its latest-value table. Both go to MIDI Learn first. A control the user
/// has mapped drives that control; every unmapped button falls through to the built-in
/// layout, turning a press into <c>Engine.NoteOn</c>/<c>NoteOff</c> — the exact same
/// entry points as the computer keyboard, so armed-track recording, the piano-roll key
/// highlight, and the engine's velocity handling all apply without a parallel path.
/// Axes have no built-in behaviour: unmapped, they do nothing. Wired in MainWindow.</summary>
public partial class MainWindow
{
    // Base pitches (same shape as the computer keyboard's A-row = C4 major scale).
    // The pad is shifted by _gamepadOctave semitones (d-pad up/down) exactly like
    // _typingOctave shifts the A-row. Face buttons = low octave (C D E F),
    // shoulder buttons = high octave (G A B C).
    private static readonly int[] GamepadPitchByButton = { 60, 62, 64, 65, 67, 69, 71, 72 };

    private readonly Dictionary<(int pad, int button), int> _gamepadHeldPitch = new();
    private readonly GamepadButtonEvent[] _gamepadEventBuf = new GamepadButtonEvent[32]; // UI-tick batch
    private readonly int[] _gamepadAxisBuf = new int[(int)GamepadAxis.Count];
    private readonly Dictionary<(int pad, int axis), int> _gamepadAxisLast = new();      // last value dispatched
    private IGamepadService? _gamepads;
    private int _gamepadOctave;          // semitone shift = *12, mirrored into Settings
    private int _gamepadPadCount;        // last seen pad count — fires PadsChanged on diff

    // Wire the per-tick poll after the VM attaches. Called from OnDataContextChanged.
    private void InitGamepad()
    {
        if (_vm is null) return;
        _gamepads = App.Services.GetService(typeof(IGamepadService)) as IGamepadService;
        Engine.GamepadStart();
        _gamepadOctave = _vm.Settings.Current.GamepadOctave;

        // If the window loses focus mid-held-note (alt-tab), release everything so a
        // pad note never drones while the user is in another app.
        Deactivated += (_, _) => ReleaseAllGamepadNotes();
        _gamepadPadCount = -1; // force the PadsChanged fire on the first tick
        _vm.PlayheadUpdated += OnGamepadTick;
    }

    private void OnGamepadTick(double _beats)
    {
        if (_vm is null) return;

        // Pad hot-plug: fire the service event so Preferences rebuilds its rows.
        int count = Engine.GamepadCount;
        if (count != _gamepadPadCount)
        {
            // A pad left: the native slots reshuffle (slot N+1 becomes N), so a
            // pending note-off would arrive under a different pad index and never
            // match its held entry — release everything to avoid a hung note. A pad
            // joining only appends, so held notes stay valid and keep ringing.
            // …and the same reshuffle would leave the axis cache pointing at the wrong
            // pad, so drop it and let the axes re-seed from their new slots.
            if (count < _gamepadPadCount) { ReleaseAllGamepadNotes(); _gamepadAxisLast.Clear(); }
            _gamepadPadCount = count;
            (_gamepads as Infrastructure.GamepadService)?.RaisePadsChanged();
        }

        if (!_vm.Settings.Current.GamepadEnabled) return;

        int n = Engine.PollGamepadEvents(_gamepadEventBuf);
        for (int i = 0; i < n; i++)
            HandleGamepadEdge(_gamepadEventBuf[i]);

        PollGamepadAxes(count);
    }

    // Sticks and triggers are sampled, not queued: the engine keeps their latest position
    // and we read it here, so a stick sweep costs one dispatch per tick instead of a
    // hundred queued events. Only a value that actually moved is forwarded, which also
    // keeps a resting pad silent.
    private void PollGamepadAxes(int padCount)
    {
        if (_learn is null) return;
        for (int pad = 0; pad < padCount; pad++)
        {
            int n = Engine.GamepadAxisValues(pad, _gamepadAxisBuf);
            for (int a = 0; a < n; a++)
            {
                int value = _gamepadAxisBuf[a];
                var key = (pad, a);
                // First sight of an axis seeds the last value without dispatching, so a
                // pad that connects with a trigger already held does not fire on arrival.
                if (!_gamepadAxisLast.TryGetValue(key, out int prev)) { _gamepadAxisLast[key] = value; continue; }
                if (value == prev) continue;
                _gamepadAxisLast[key] = value;

                int id = GamepadControls.AxisId((GamepadAxis)a);
                bool consumed = _learn.HandleGamepadAxis(id, value);
                // Flash the Preferences readout as the control leaves rest, not on every
                // sample — a swept stick would otherwise rewrite the label 30 times a second.
                if (prev == GamepadControls.Rest(id) && consumed)
                {
                    var mapped = _learn.GamepadMappingFor(id);
                    (_gamepads as Infrastructure.GamepadService)?.RaiseActivity(
                        pad, mapped is null ? GamepadControls.Name(id) : $"→ {mapped.DisplayName}");
                }
            }
        }
    }

    private void HandleGamepadEdge(GamepadButtonEvent ev)
    {
        var key = (ev.Pad, ev.ButtonId);

        if (ev.Pressed == 0)
        {
            // Note-off at the exact pitch it started on (same rule as the keyboard,
            // so a d-pad octave shift mid-hold doesn't hang the note). A button with
            // no held pitch was either mapped or idle — give the release to Learn so a
            // hold-to-open mapping closes again.
            if (_gamepadHeldPitch.Remove(key, out int pitch)) Engine.NoteOff(pitch);
            else _learn?.HandleGamepadButton(ev.ButtonId, false);
            return;
        }

        // MIDI Learn gets first refusal: while a control is pending this press binds it,
        // and a button that already has a mapping drives that control instead of playing.
        // Nothing is reserved — mapping the d-pad takes over from octave/velocity too —
        // and every unmapped button keeps the built-in layout below.
        if (_learn is not null && _learn.HandleGamepadButton(ev.ButtonId, true))
        {
            var mapped = _learn.GamepadMappingFor(ev.ButtonId);
            (_gamepads as Infrastructure.GamepadService)?.RaiseActivity(
                ev.Pad, mapped is null ? GamepadControls.Name(ev.ButtonId) : $"→ {mapped.DisplayName}");
            return;
        }

        // A press: act on it and build a display label for the Preferences activity
        // readout (so the pane shows the pad is live even for unmapped buttons).
        string label;
        switch ((GamepadButton)ev.ButtonId)
        {
            case GamepadButton.DpadUp:    ShiftGamepadOctave(+1);   label = "Octave +";   break;
            case GamepadButton.DpadDown:  ShiftGamepadOctave(-1);   label = "Octave −";   break;
            case GamepadButton.DpadLeft:  ShiftGamepadVelocity(-10); label = "Velocity −"; break;
            case GamepadButton.DpadRight: ShiftGamepadVelocity(+10); label = "Velocity +"; break;
            default:
            {
                // Generic button: index 1..N → a scale tone if within the 8-note
                // layout. Buttons beyond that are shown but play nothing (options/
                // share/system buttons).
                int idx = ev.ButtonId - 1;
                if (idx < 0 || idx >= GamepadPitchByButton.Length) { label = GamepadControls.Name(ev.ButtonId); break; }
                if (_gamepadHeldPitch.ContainsKey(key)) return; // auto-repeat guard

                int note = Math.Clamp(GamepadPitchByButton[idx] + _gamepadOctave * 12, 0, 127);
                _gamepadHeldPitch[key] = note;
                Engine.NoteOn(note, _typingVelocity / 127f);
                label = DeviceCardKit.NoteName(note);
                break;
            }
        }

        (_gamepads as Infrastructure.GamepadService)?.RaiseActivity(ev.Pad, label);
    }

    // D-pad up/down: shift the gamepad octave (persisted via Settings). The status
    // readout matches the typing-keyboard convention ("C4" style anchor).
    private void ShiftGamepadOctave(int delta)
    {
        int prev = _gamepadOctave;
        _gamepadOctave = Math.Clamp(_gamepadOctave + delta, -4, 4);
        if (_vm is not null && _gamepadOctave != prev)
        {
            _vm.Settings.Current.GamepadOctave = _gamepadOctave;
            _vm.Settings.Save();
            _vm.StatusText = $"Gamepad octave: C{4 + _gamepadOctave}";
        }
    }

    // D-pad left/right: nudge velocity — shares the typing keyboard's value so a
    // user switching between keys and pad doesn't juggle two velocities.
    private void ShiftGamepadVelocity(int delta) => ShiftTypingVelocity(delta);

    // Stop the pad thread on shutdown so a background thread never outlives the
    // engine handle. Closing is wired in OnDataContextChanged.
    private void ShutdownGamepad()
    {
        if (_vm is null) return;
        ReleaseAllGamepadNotes();
        Engine.GamepadStop();
    }

    private void ReleaseAllGamepadNotes()
    {
        foreach (var kv in _gamepadHeldPitch) Engine.NoteOff(kv.Value);
        _gamepadHeldPitch.Clear();
    }
}
