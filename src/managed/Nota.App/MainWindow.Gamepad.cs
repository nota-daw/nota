// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System;
using System.Collections.Generic;
using Avalonia;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.App;

/// <summary>Gamepad-as-note-source: polls the queued button edges every UI tick
/// (hooked off <see cref="MainWindowViewModel.PlayheadUpdated"/>) and turns a
/// pressed button into <c>Engine.NoteOn</c>/<c>NoteOff</c> — the exact same entry
/// points as the computer keyboard, so armed-track recording, the piano-roll key
/// highlight, and the engine's velocity handling all apply without a parallel
/// path. Wired in MainWindow.</summary>
public partial class MainWindow
{
    // Base pitches (same shape as the computer keyboard's A-row = C4 major scale).
    // The pad is shifted by _gamepadOctave semitones (d-pad up/down) exactly like
    // _typingOctave shifts the A-row. Face buttons = low octave (C D E F),
    // shoulder buttons = high octave (G A B C).
    private static readonly int[] GamepadPitchByButton = { 60, 62, 64, 65, 67, 69, 71, 72 };

    private readonly Dictionary<(int pad, int button), int> _gamepadHeldPitch = new();
    private readonly GamepadButtonEvent[] _gamepadEventBuf = new GamepadButtonEvent[32]; // UI-tick batch
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
            if (count < _gamepadPadCount) ReleaseAllGamepadNotes();
            _gamepadPadCount = count;
            (_gamepads as Infrastructure.GamepadService)?.RaisePadsChanged();
        }

        if (!_vm.Settings.Current.GamepadEnabled) return;

        int n = Engine.PollGamepadEvents(_gamepadEventBuf);
        for (int i = 0; i < n; i++)
            HandleGamepadEdge(_gamepadEventBuf[i]);
    }

    private void HandleGamepadEdge(GamepadButtonEvent ev)
    {
        var key = (ev.Pad, ev.ButtonId);

        if (ev.Pressed == 0)
        {
            // Note-off at the exact pitch it started on (same rule as the keyboard,
            // so a d-pad octave shift mid-hold doesn't hang the note).
            if (_gamepadHeldPitch.Remove(key, out int pitch)) Engine.NoteOff(pitch);
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
                if (idx < 0 || idx >= GamepadPitchByButton.Length) { label = $"Button {ev.ButtonId}"; break; }
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
