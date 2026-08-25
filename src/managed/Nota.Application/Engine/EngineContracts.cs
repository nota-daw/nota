// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Engine contract data: the blittable value types the audio-engine port exchanges
// with callers. They mirror the native structs 1:1 and carry [StructLayout] so the
// Infrastructure P/Invoke layer can marshal them without a copy. Kept in the
// Application layer (not Infrastructure) so IAudioEngine — and the ViewModels that
// depend on it — never reach across into Infrastructure.

using System.Runtime.InteropServices;

namespace Nota.Application;

/// <summary>A MIDI note, blittable and laid out to match the native NotaNoteData
/// struct. Times are in beats, relative to the containing clip's start.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NotaNote(int pitch, double startBeat, double lengthBeats, float velocity)
{
    public int Pitch = pitch;
    public double StartBeat = startBeat;
    public double LengthBeats = lengthBeats;
    public float Velocity = velocity;
}

/// <summary>Track summary for the Arrangement View. Matches native NotaTrackInfo.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NotaTrackInfo
{
    public int Id;
    public int Type;        // 0 = audio, 1 = instrument, 2 = return bus (M6-1), 3 = group
    public int ClipCount;
    public int Muted;
    public int Soloed;
    public int Armed;
    public float Volume;
    public float Pan;
    public int GroupId;     // parent group track id, or -1 (top-level)

    public readonly bool IsInstrument => Type == 1;
    public readonly bool IsReturn => Type == 2;
    public readonly bool IsGroup => Type == 3;
}

/// <summary>Clip geometry on the timeline. Matches native NotaClipInfo.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NotaClipInfo
{
    public double StartBeat;
    public double LengthBeats;
    public int Kind;         // 0 = audio, 1 = midi
    public int Active;       // 1 = plays, 0 = deactivated (key 0): stays on the timeline but silent

    public readonly bool IsMidi => Kind == 1;
    public readonly bool IsActive => Active != 0;
}

/// <summary>Full audio-clip geometry for project save. Matches native NotaAudioClipInfo (M7-6b).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NotaAudioClipInfo
{
    public double StartBeat;
    public double SourceOffsetFrames;
    public long LengthFrames;   // 0 = to end of sample
    public float Gain;
    public long SampleId;       // 0 = none
    public float PitchSemitones; // varispeed transpose (M-audio-editor)
    public int WarpEnabled;      // 0/1
    public int WarpMode;         // 0 Beats,1 Tones,2 Texture,3 Complex,4 ComplexPro,5 RePitch
    public double WarpBeats;     // full warped material length in beats (0 when unwarped)
    public double WarpPlayStart; // trimmed play window start (beats within the warp)
    public double WarpPlayEnd;   // trimmed play window end (0 = to WarpBeats)
}

/// <summary>Decoded-sample metadata. Matches native NotaSampleInfo (M7-6b).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NotaSampleInfo
{
    public int Channels;
    public long Frames;
    public double SampleRate;
}

/// <summary>Built-in Sampler settings. Matches native NotaSamplerInfo (M7-6b).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NotaSamplerInfo
{
    public long SampleId;
    public int RootNote;
    public int Loop;            // 0/1
}

/// <summary>Captured session audio take. Matches native NotaSessionAudioSlot (M7-6b).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NotaSessionAudioSlot
{
    public long SampleId;
    public double LengthBeats;
    public double SourceOffsetFrames;
    public long LengthFrames;
    public float Gain;
}

/// <summary>Post-fader level meter (linear 0..1). Matches native NotaMeter (M6-2).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct NotaMeter
{
    public float PeakL;
    public float PeakR;
    public float RmsL;
    public float RmsR;

    public readonly float Peak => PeakL > PeakR ? PeakL : PeakR;
    public readonly float Rms => RmsL > RmsR ? RmsL : RmsR;
}

/// <summary>What a parameter-automation lane drives (M9). Matches native AutomationTarget.</summary>
public enum AutomationTarget { Volume = 0, Pan = 1, DeviceParam = 2, PluginParam = 3, MidiDeviceParam = 4 }

/// <summary>Automation record mode (M9-C). Read = play back only; Touch/Latch record
/// while a control is touched; Write records armed targets from playback.</summary>
public enum AutomationWriteMode { Read = 0, Touch = 1, Latch = 2, Write = 3 }

/// <summary>MIDI clip envelope target (M9 follow-up). Velocity scales note velocities;
/// Volume scales the instrument output during the clip.</summary>
public enum MidiClipEnvelope { Velocity = 0, Volume = 1 }

/// <summary>An automation breakpoint, blittable and laid out to match native
/// NotaAutomationPoint (M9). Value is in the target's native units; Curve (M9-D)
/// shapes the segment to the next point (0 linear, [-1,1]).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct AutomationPoint(double beat, float value, float curve = 0f)
{
    public double Beat = beat;
    public float Value = value;
    public float Curve = curve;
}

/// <summary>An automation lane's binding + point count (M9).</summary>
public readonly record struct AutomationLaneInfo(AutomationTarget Target, int DeviceIndex, int ParamIndex, int PointCount);

/// <summary>An audio device: a stable UID (persistence key) and a display name (M7-1).</summary>
public readonly record struct AudioDevice(string Uid, string Name);

/// <summary>A MIDI input source: a stable UID (persistence key) and a display name (M7-2).</summary>
public readonly record struct MidiDevice(string Uid, string Name);

/// <summary>A game controller usable as a live note source (macOS v1). The UID is
/// the stable IORegistryEntryID stringified; the name is the HID product string.</summary>
public readonly record struct GamepadDevice(string Uid, string Name);

/// <summary>One gamepad button edge, blittable and matching NotaGamepadButtonEvent
/// in the native C ABI. Pad is the slot for the pad that pressed it (a session-
/// local index; pads are matched back to <see cref="GamepadDevice"/> by it).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct GamepadButtonEvent
{
    public int Pressed;   // 1 = down, 0 = up
    public int ButtonId;  // 1..N = HID usage; GamepadButton.Dpad* for the d-pad
    public int Pad;       // pad slot at the time of the event
}

/// <summary>Stable ids for the gamepad d-pad (resolved from a hatswitch). All other
/// buttons carry their raw HID usage (1..N); the label a user sees is cosmetic and
/// lives in the mapping UI.</summary>
public enum GamepadButton
{
    DpadUp = -1000,
    DpadDown = -1001,
    DpadLeft = -1002,
    DpadRight = -1003,
}

/// <summary>An Instrument Rack macro mapping: macro <paramref name="Macro"/> drives the target
/// param over [<paramref name="RangeMin"/>, <paramref name="RangeMax"/>] in the target's own units.
/// <paramref name="DeviceIndex"/> &lt; 0 targets the chain's instrument (plugin-param index);
/// otherwise the device at that chain position (generic-param index).</summary>
public readonly record struct RackMacroMapping(
    int Macro, int Chain, int DeviceIndex, int ParamIndex, float RangeMin, float RangeMax);
