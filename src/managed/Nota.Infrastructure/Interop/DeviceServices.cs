// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Infrastructure;

/// <summary>IAudioDeviceService over the engine's device enumeration.</summary>
public sealed class AudioDeviceService : IAudioDeviceService
{
    public IReadOnlyList<AudioDevice> OutputDevices() => NotaEngine.AudioOutputDevices();
    public IReadOnlyList<AudioDevice> InputDevices() => NotaEngine.AudioInputDevices();
}

/// <summary>IMidiDeviceService over the engine's MIDI enumeration.</summary>
public sealed class MidiDeviceService : IMidiDeviceService
{
    public IReadOnlyList<MidiDevice> InputDevices() => NotaEngine.MidiInputDevices();
}

/// <summary>IGamepadService over the engine's pad table. `PadsChanged` is fired
/// by the owning UI tick (MainWindow) after it polls events — pads connect/
/// disconnect events ride the edges, so there is no OS observer to bridge.</summary>
public sealed class GamepadService : IGamepadService
{
    private readonly NotaEngine _engine;
    public GamepadService(NotaEngine engine) => _engine = engine;

    public IReadOnlyList<GamepadDevice> Pads() => _engine.Gamepads();

    public event Action? PadsChanged;
    /// <summary>Hook for the UI tick: raise <see cref="PadsChanged"/>.</summary>
    public void RaisePadsChanged() => PadsChanged?.Invoke();

    public event Action<int, string>? Activity;
    /// <summary>Hook for the UI tick: raise <see cref="Activity"/> for a button press.</summary>
    public void RaiseActivity(int pad, string label) => Activity?.Invoke(pad, label);
}
