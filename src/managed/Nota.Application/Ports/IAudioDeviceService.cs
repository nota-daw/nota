// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>Enumerates the connected audio devices (M7-1). Staging/applying the
/// chosen device lives on <see cref="IAudioEngine"/>.</summary>
public interface IAudioDeviceService
{
    IReadOnlyList<AudioDevice> OutputDevices();
    IReadOnlyList<AudioDevice> InputDevices();
}

/// <summary>Enumerates the connected MIDI input sources (M7-2). Enable/apply lives
/// on <see cref="IAudioEngine"/>.</summary>
public interface IMidiDeviceService
{
    IReadOnlyList<MidiDevice> InputDevices();
}

/// <summary>Enumerates game controllers usable as a live note source (macOS v1).
/// The poll loop lives on <see cref="IAudioEngine"/> — the UI ticks it and turns
/// raw button edges into noteOn/noteOff calls (same entry points as the computer
/// keyboard, so live play + armed-track recording reuse the existing pipeline).</summary>
public interface IGamepadService
{
    /// <summary>Currently connected pads (may be empty; empty on non-macOS in v1).</summary>
    IReadOnlyList<GamepadDevice> Pads();
    /// <summary>Fired from the UI tick when the pad list count changed — the
    /// Preferences pane rebuilds its rows off this.</summary>
    event Action? PadsChanged;

    /// <summary>Fired from the UI tick on each button press, carrying the pad slot
    /// and a display label for the pressed control ("C4", "Octave +", …). The
    /// Gamepads pane uses it for a live connection/activity readout.</summary>
    event Action<int, string>? Activity;
}
