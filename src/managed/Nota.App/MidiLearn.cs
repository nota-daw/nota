// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI Learn — map a hardware controller's CCs/notes, or a gamepad button, stick
// or trigger, onto Nota's on-screen controls. The mapping *target* reuses the engine's automation addressing
// (track + AutomationTarget + device/param index) plus a few globals (master
// volume, transport, per-track mute/solo), so an incoming MIDI message can be
// applied straight through the existing engine setters, whether or not the
// control's card is on screen.
//
// A control opts in by carrying a MidiBinding attached property; that's all a
// build site needs. The learn overlay finds those bindings by walking the visual
// tree while armed, and MidiLearnService owns the mapping table, the learn state
// machine, and the per-tick drain/apply of native MIDI events. Gamepad buttons are
// fed into the same machine by MainWindow.Gamepad, so one table holds both.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Nota.Application;

namespace Nota.App;

/// <summary>What a mapping listens to: a CC (continuous), a MIDI note (a controller
/// pad/button), or a gamepad button. Gamepad sources ride the same table and the same
/// apply path — a button edge is just a 0/127 momentary source.</summary>
public enum MidiSourceKind { Cc = 0, Note = 1, Gamepad = 2 }

/// <summary>The gamepad's mappable controls, flattened into one id space so a mapping
/// needs no extra field to say which kind it is. Buttons keep the ids the native queue
/// emits (1..N HID usage, d-pad sentinels at -1000); the six continuous axes sit below
/// them at -2000. A mapping stores the id in <see cref="MidiMapping.Number"/>.</summary>
public static class GamepadControls
{
    private const int AxisBase = -2000;

    public static int AxisId(GamepadAxis axis) => AxisBase - (int)axis;
    public static bool IsAxis(int controlId) => controlId <= AxisBase && controlId > AxisBase - (int)GamepadAxis.Count;
    public static GamepadAxis Axis(int controlId) => (GamepadAxis)(AxisBase - controlId);

    /// <summary>Where an idle control sits, in the 0..127 scale the engine reports:
    /// a spring-centred stick returns to the middle, a trigger falls back to zero.
    /// Used to tell a deliberate move from a control settling back to rest.</summary>
    public static int Rest(int controlId)
        => IsAxis(controlId) && Axis(controlId) is not (GamepadAxis.LeftTrigger or GamepadAxis.RightTrigger) ? 64 : 0;

    public static string Name(int controlId)
    {
        if (IsAxis(controlId))
            return Axis(controlId) switch
            {
                GamepadAxis.LeftX => "Left stick ←→",
                GamepadAxis.LeftY => "Left stick ↑↓",
                GamepadAxis.RightX => "Right stick ←→",
                GamepadAxis.RightY => "Right stick ↑↓",
                GamepadAxis.LeftTrigger => "Left trigger",
                _ => "Right trigger",
            };
        return (GamepadButton)controlId switch
        {
            GamepadButton.DpadUp => "D-pad ↑",
            GamepadButton.DpadDown => "D-pad ↓",
            GamepadButton.DpadLeft => "D-pad ←",
            GamepadButton.DpadRight => "D-pad →",
            _ => $"Button {controlId}",
        };
    }
}

/// <summary>What a mapping drives. The first three mirror <see cref="AutomationTarget"/>;
/// the rest are Nota globals with no automation lane.</summary>
public enum MidiTargetKind
{
    DeviceParam, PluginParam, MidiDeviceParam,
    TrackVolume, TrackPan, TrackMute, TrackSolo,
    MasterVolume, TransportPlay, TransportStop, TransportRecord,
    // Rack controls. DeviceIndex encodes the rack: -1 = the track's Instrument Rack,
    // >= 0 = an Audio Effect Rack device at that index. ParamIndex = chain (or unused).
    RackVolume, RackChainGain, RackChainMute, RackChainSolo,
}

/// <summary>Stable identity of a mappable control. TrackId is a runtime engine id;
/// persistence translates it to/from a track position (see the project DTOs).</summary>
public readonly record struct MidiTarget(MidiTargetKind Kind, int TrackId, int DeviceIndex, int ParamIndex)
{
    public static MidiTarget DeviceParam(int track, int dev, int p) => new(MidiTargetKind.DeviceParam, track, dev, p);
    public static MidiTarget PluginParam(int track, int dev, int p) => new(MidiTargetKind.PluginParam, track, dev, p);
    public static MidiTarget MidiDeviceParam(int track, int dev, int p) => new(MidiTargetKind.MidiDeviceParam, track, dev, p);
    public static MidiTarget TrackVolume(int track) => new(MidiTargetKind.TrackVolume, track, -1, -1);
    public static MidiTarget TrackPan(int track) => new(MidiTargetKind.TrackPan, track, -1, -1);
    public static MidiTarget TrackMute(int track) => new(MidiTargetKind.TrackMute, track, -1, -1);
    public static MidiTarget TrackSolo(int track) => new(MidiTargetKind.TrackSolo, track, -1, -1);
    public static readonly MidiTarget MasterVolume = new(MidiTargetKind.MasterVolume, -1, -1, -1);
    public static readonly MidiTarget TransportPlay = new(MidiTargetKind.TransportPlay, -1, -1, -1);
    public static readonly MidiTarget TransportStop = new(MidiTargetKind.TransportStop, -1, -1, -1);
    public static readonly MidiTarget TransportRecord = new(MidiTargetKind.TransportRecord, -1, -1, -1);
    // Rack controls (di = -1 Instrument Rack / >= 0 Audio Effect Rack device index).
    public static MidiTarget RackVolume(int track, int di) => new(MidiTargetKind.RackVolume, track, di, -1);
    public static MidiTarget RackChainGain(int track, int di, int chain) => new(MidiTargetKind.RackChainGain, track, di, chain);
    public static MidiTarget RackChainMute(int track, int di, int chain) => new(MidiTargetKind.RackChainMute, track, di, chain);
    public static MidiTarget RackChainSolo(int track, int di, int chain) => new(MidiTargetKind.RackChainSolo, track, di, chain);

    /// <summary>True for on/off targets (buttons) — applied as a trigger, not a value.</summary>
    public bool IsButton => Kind is MidiTargetKind.TrackMute or MidiTargetKind.TrackSolo
        or MidiTargetKind.TransportPlay or MidiTargetKind.TransportStop or MidiTargetKind.TransportRecord
        or MidiTargetKind.RackChainMute or MidiTargetKind.RackChainSolo;
}

/// <summary>Attached to a control so the learn overlay can find it and label it, and
/// so an applied value can move the control's visuals live. Only <see cref="Target"/>
/// is required — apply-to-engine works from the target alone.</summary>
public sealed class MidiBinding
{
    public MidiTarget Target { get; }
    public string Name { get; }
    /// <summary>Optional: push a normalized 0..1 value to the control's visuals.</summary>
    public Action<double>? SetNormalized { get; }

    public MidiBinding(MidiTarget target, string name, Action<double>? setNormalized = null)
    { Target = target; Name = name ?? ""; SetNormalized = setNormalized; }
}

/// <summary>Implemented by a single custom-drawn control that packs several mappable
/// params (e.g. an ADSR editor or an XY filter pad) with no separate knobs. While armed,
/// the overlay highlights + hit-tests each returned region instead of the whole control,
/// so every param inside gets its own learn target. Rects are in the control's own
/// coordinate space.</summary>
public interface IMidiLearnRegions
{
    System.Collections.Generic.IReadOnlyList<(Rect rect, MidiTarget target, string name)> GetMidiLearnRegions();
}

/// <summary>Host for the <c>MidiLearn.Binding</c> attached property.</summary>
public static class MidiLearn
{
    public static readonly AttachedProperty<MidiBinding?> BindingProperty =
        AvaloniaProperty.RegisterAttached<Control, MidiBinding?>("Binding", typeof(MidiLearn));

    public static MidiBinding? GetBinding(Control c) => c.GetValue(BindingProperty);
    public static void SetBinding(Control c, MidiBinding? v) => c.SetValue(BindingProperty, v);

    /// <summary>Convenience: tag <paramref name="c"/> as mappable to <paramref name="target"/>.</summary>
    public static void Bind(Control c, MidiTarget target, string name, Action<double>? setNormalized = null)
        => SetBinding(c, new MidiBinding(target, name, setNormalized));
}

/// <summary>One learned control → MIDI source mapping.</summary>
public sealed class MidiMapping
{
    public MidiTarget Target { get; init; }
    public string DisplayName { get; set; } = "";
    public MidiSourceKind SourceKind { get; init; }
    public int Channel { get; init; }         // 0..15 (unused, and always 0, for Gamepad)
    public int Number { get; init; }          // CC number, note pitch, or GamepadControls id
    public double RangeMin { get; set; }       // normalized output floor
    public double RangeMax { get; set; } = 1;  // normalized output ceiling
    public bool Invert { get; set; }

    /// <summary>Last on/off state applied to a button target, so only the crossing fires.
    /// Runtime state, not part of the mapping — a swept CC or a squeezed trigger sends a
    /// run of values past the threshold, and every one of them used to toggle.</summary>
    internal bool TriggerLatch { get; set; }

    public string SourceLabel => SourceKind switch
    {
        MidiSourceKind.Cc => $"CC {Number} · ch{Channel + 1}",
        MidiSourceKind.Gamepad => $"Pad · {GamepadControls.Name(Number)}",
        _ => $"Note {Number} · ch{Channel + 1}",
    };
}
