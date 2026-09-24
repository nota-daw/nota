// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M7-6a: the on-disk document model for the `.nota` project bundle. These DTOs
// are the JSON shape of `project.json`; ProjectService captures them from the
// engine and replays them back. Phase 1 covers built-in content only (Synth,
// built-in devices, MIDI clips/notes, session MIDI slots, mixer, sends/returns,
// transport). Audio samples (phase b) and hosted plugins (phase c) come later.
//
// Forward-compat (NFR-8): unknown JSON members are preserved on round-trip via
// [JsonExtensionData] so a newer file opened by an older build keeps its extra
// fields when re-saved.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nota.Infrastructure;

public sealed class ProjectDocument
{
    /// <summary>Bumped on breaking format changes; older builds refuse newer files.</summary>
    public int FormatVersion { get; set; } = ProjectService.CurrentFormatVersion;
    public string App { get; set; } = "Nota";
    public TransportDto Transport { get; set; } = new();
    public int SceneCount { get; set; } = 8;
    public List<TrackDto> Tracks { get; set; } = new();
    /// <summary>Master-volume automation points (graph-level, format v7).</summary>
    public List<AutomationPointDto> MasterVolumeAutomation { get; set; } = new();
    /// <summary>Master effect chain (devices on the master bus) + its name/colour.</summary>
    public MasterDto Master { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Samples referenced by this document: relative bundle path -&gt; live
    /// engine sample id. Populated by Capture, consumed by Save to write
    /// <c>samples/</c>; not serialized (paths already live in the DTOs).</summary>
    [JsonIgnore] public Dictionary<string, long> SampleRefs { get; } = new();

    /// <summary>Plugin state blobs: relative bundle path -&gt; bytes. Populated by
    /// Capture, written to <c>plugin-states/</c> by Save; not serialized.</summary>
    [JsonIgnore] public Dictionary<string, byte[]> StateBlobs { get; } = new();
}

public sealed class MasterDto
{
    public string? Name { get; set; }
    public int ColorIndex { get; set; } = -1;
    public List<DeviceDto> Devices { get; set; } = new();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class TransportDto
{
    public double Bpm { get; set; } = 120.0;
    public double MasterVolume { get; set; } = 1.0;
    public bool MetronomeOn { get; set; }
    public bool LoopOn { get; set; }
    public int TimeSigNumerator { get; set; } = 4;
    public int TimeSigDenominator { get; set; } = 4;

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class TrackDto
{
    public int Type { get; set; }             // 0 = audio, 1 = instrument, 2 = return, 3 = group
    public string? Name { get; set; }
    public float Volume { get; set; } = 1.0f;
    public float Pan { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public bool Armed { get; set; }
    public int ReturnIndex { get; set; } = -1; // >=0 for return tracks
    public int GroupId { get; set; } = -1;     // parent group's doc index, or -1 (top-level)
    public int ColorIndex { get; set; } = -1;  // palette slot; -1 = auto by position
    // Record input: 0 hardware, -1 master, or (2 + track-list index) for another track's
    // output (encoded so it survives id reassignment across save/load).
    public int RecordInput { get; set; }
    // MIDI routing source ("MIDI In") as a doc-list index (survives id reassignment), or -1 = off.
    public int MidiFrom { get; set; } = -1;
    public float[] Sends { get; set; } = new float[4];
    public InstrumentDto? Instrument { get; set; }
    public List<MidiDeviceDto> MidiEffects { get; set; } = new();   // MIDI FX chain, before the instrument
    public List<DeviceDto> Devices { get; set; } = new();
    public List<MidiClipDto> MidiClips { get; set; } = new();
    public List<AudioClipDto> AudioClips { get; set; } = new();
    public List<SessionSlotDto> SessionSlots { get; set; } = new();
    public List<AutomationLaneDto> Automation { get; set; } = new();   // M9-A4 (format v2)
    public List<ModulatorDto> Modulators { get; set; } = new();        // CV modulation sources (Phase 3)
    public List<CvLinkDto> CvLinks { get; set; } = new();              // CV modulation edges (Phase 3)
    /// <summary>Freeze (M7): relative path to the track's frozen-audio blob in
    /// <c>plugin-states/</c> (null = not frozen). Restored + re-flagged frozen on load.</summary>
    public string? FrozenState { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A CV modulation source (Phase 3, Modular editor). Kind 0 = LFO.</summary>
public sealed class ModulatorDto
{
    public int Id { get; set; }
    public int Kind { get; set; }
    public int Waveform { get; set; }
    public bool TempoSync { get; set; } = true;
    public float RateHz { get; set; } = 1.0f;
    public float RateSyncBeats { get; set; } = 0.5f;
    public float Depth { get; set; } = 1.0f;
    public float Phase { get; set; }
    public float Attack { get; set; } = 10.0f;    // env follower / ADSR (ms)
    public float Release { get; set; } = 120.0f;
    public float Decay { get; set; } = 200.0f;    // ADSR (ms)
    public float Sustain { get; set; } = 0.7f;    // ADSR (0..1)
    public int InputA { get; set; } = -1;         // Math: source modulator ids (remapped on load)
    public int InputB { get; set; } = -1;
    public float MathOffset { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A CV modulation edge: modulator (Source id) → built-in device param.
/// Mode: 0 Add, 1 Multiply, 2 Override.</summary>
public sealed class CvLinkDto
{
    public int Source { get; set; }                 // modulator id (SourceKind 0)
    public int SourceKind { get; set; }             // 0 = modulator, 1 = parameter
    public int SourceDevice { get; set; } = -1;     // param source: device/param on the owner track
    public int SourceParam { get; set; } = -1;
    public int TargetKind { get; set; }             // 0 device, 1 instrument, 2 MIDI-FX
    /// <summary>Target track as a doc-list index (survives id reassignment); -1 = the modulator's own track.</summary>
    public int TargetTrack { get; set; } = -1;
    public int Device { get; set; } = -1;
    public int Param { get; set; } = -1;
    public float Depth { get; set; } = 1.0f;
    public int Mode { get; set; }
    /// <summary>Modulation centre (base) value — the device param value the link swings around.</summary>
    public float Base { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A parameter-automation lane and its breakpoints (M9-A4, format v2).
/// Target: 0 = track volume, 1 = pan, 2 = built-in device param (Device/Param set).</summary>
public sealed class AutomationLaneDto
{
    public int Target { get; set; }
    public int DeviceIndex { get; set; } = -1;
    public int ParamIndex { get; set; } = -1;
    /// <summary>Stable hosted-plugin parameter id when Target == 3 (PluginParam, M9-B4).
    /// Persisted instead of the version-unstable index; resolved on load.</summary>
    public string? PluginParamId { get; set; }
    public AutomationPointDto[] Points { get; set; } = System.Array.Empty<AutomationPointDto>();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AutomationPointDto
{
    public double Beat { get; set; }
    public float Value { get; set; }
    public float Curve { get; set; }   // segment shape to next point (M9-D4, format v4)

    public AutomationPointDto() { }
    public AutomationPointDto(AutomationPoint p) { Beat = p.Beat; Value = p.Value; Curve = p.Curve; }
    public AutomationPoint ToPoint() => new(Beat, Value, Curve);
}

public sealed class InstrumentDto
{
    /// <summary>0 = Nota Synth, 1 = Sampler, 2 = Nota Physical, -1 = hosted plugin.</summary>
    public int Kind { get; set; }
    /// <summary>Set when Kind == 1 (built-in Sampler).</summary>
    public SamplerDto? Sampler { get; set; }
    /// <summary>Stable plugin identifier when Kind == -1 (M7-6c).</summary>
    public string? PluginId { get; set; }
    /// <summary>Relative path to the plugin's state blob in <c>plugin-states/</c>.</summary>
    public string? State { get; set; }
    /// <summary>Sidechain source ("React", Nota Flux) as a track INDEX in this document
    /// (-1 = none). Resolved to a live track id on load, since ids are reassigned.</summary>
    public int SidechainSource { get; set; } = -1;
    /// <summary>Nota Rhythm (Kind == 12) per-voice one-shots (only sample-source voices).</summary>
    public List<VoiceSampleDto>? VoiceSamples { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>One drum voice's sample for Nota Rhythm: the voice index + a bundle-relative
/// path into <c>samples/</c>.</summary>
public sealed class VoiceSampleDto
{
    public int Voice { get; set; }
    public string Sample { get; set; } = "";
    /// <summary>The voice plays its synth engine; the sample is kept for switching back.</summary>
    public bool Synth { get; set; }
}

public sealed class SamplerDto
{
    public string Sample { get; set; } = "";  // relative bundle path ("" = empty Sampler)
    public int RootNote { get; set; } = 60;
    public bool Loop { get; set; }
    /// <summary>The file name the sample came from (no extension) — the bundle stores it as
    /// sample-N.wav, so this keeps the Sampler's "Detect from file name" working after a reload.</summary>
    public string Name { get; set; } = "";
    /// <summary>Normalized plugin-param values (vol/pan/start/end/loop/ADSR/filter…). Empty = defaults.</summary>
    public float[] Params { get; set; } = System.Array.Empty<float>();

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A placed audio clip (arrangement) or a session take. `Sample` is a
/// bundle-relative path into <c>samples/</c>.</summary>
public sealed class AudioClipDto
{
    public string Sample { get; set; } = "";
    public string? Name { get; set; }
    public bool Active { get; set; } = true;   // clip deactivate / key 0 (v17); default true = plays
    public double StartBeat { get; set; }
    public double SourceOffsetFrames { get; set; }
    public long LengthFrames { get; set; }     // 0 = to end of sample
    public float Gain { get; set; } = 1.0f;
    public float PitchSemitones { get; set; }  // varispeed transpose (v5)
    public int WarpEnabled { get; set; }       // 0/1 (v5)
    public int WarpMode { get; set; }          // WarpMode enum (v5)
    public double WarpBeats { get; set; }      // full warped material length in beats (v5)
    public List<WarpMarkerDto>? WarpMarkers { get; set; }  // source↔beat anchors (v6)
    public AutomationPointDto[]? VolumeEnvelope { get; set; }  // clip volume envelope, clip-local beats (v8)
    public AutomationPointDto[]? PanEnvelope { get; set; }     // clip pan envelope, clip-local beats (v9)
    public double WarpPlayStart { get; set; }  // warped clip trim window start (beats, v11)
    public double WarpPlayEnd { get; set; }    // warped clip trim window end (0 = full, v11)
    public bool Reversed { get; set; }         // plays back-to-front (v19); default false

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A warp marker: source-frame position tied to a musical beat (v6).</summary>
public sealed class WarpMarkerDto
{
    public double Src { get; set; }
    public double Beat { get; set; }
}

/// <summary>A built-in MIDI effect (e.g. the arpeggiator), captured by kind + generic
/// param values. Persisted before the instrument in the chain.</summary>
public sealed class MidiDeviceDto
{
    public int Kind { get; set; }        // 0 = Arpeggiator
    public bool Bypassed { get; set; }
    public float[] Params { get; set; } = System.Array.Empty<float>();
    // Map/CC routing (arp): CC lane → an audio-device param on the same track.
    public int CcDestDevice { get; set; } = -2;   // -2 = no routing
    public int CcDestParam { get; set; } = -1;
    public float CcDepth { get; set; } = 1.0f;

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class DeviceDto
{
    /// <summary>Built-in kind: 0=EQ, 1=Compressor, 2=Reverb, 3=Delay, 4=Utility; -1 = hosted plugin.</summary>
    public int BuiltinKind { get; set; }
    public bool Bypassed { get; set; }
    public float[] Params { get; set; } = System.Array.Empty<float>();
    /// <summary>Stable plugin identifier when BuiltinKind == -1 (M7-6c).</summary>
    public string? PluginId { get; set; }
    /// <summary>Relative path to the plugin's state blob in <c>plugin-states/</c>.</summary>
    public string? State { get; set; }
    /// <summary>Sidechain source as a track INDEX in this document (-1 = none). Resolved to a
    /// live track id on load, since ids are reassigned. Phase B (format v15).</summary>
    public int SidechainSource { get; set; } = -1;
    /// <summary>Sidechain detector gain in dB (Phase D, format v16).</summary>
    public float SidechainGain { get; set; }
    /// <summary>Sidechain dry/wet, 0..1 (Phase D, format v16). Default 1 = fully wet.</summary>
    public float SidechainMix { get; set; } = 1.0f;
    /// <summary>Sidechain source tap: true = pre-FX pre-fader (Phase D, format v16).</summary>
    public bool SidechainTapPre { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class MidiClipDto
{
    public string? Name { get; set; }
    public bool Active { get; set; } = true;   // clip deactivate / key 0 (v17); default true = plays
    public double StartBeat { get; set; }
    public double LengthBeats { get; set; }
    public NoteDto[] Notes { get; set; } = System.Array.Empty<NoteDto>();
    public AutomationPointDto[]? VelocityEnvelope { get; set; }  // clip velocity envelope (v10)
    public AutomationPointDto[]? VolumeEnvelope { get; set; }    // clip volume envelope (v10)

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class SessionSlotDto
{
    public int Scene { get; set; }
    public double LengthBeats { get; set; } = 4.0;
    public NoteDto[] Notes { get; set; } = System.Array.Empty<NoteDto>();
    /// <summary>Set for audio-track slots (a captured take); Notes is empty then.</summary>
    public AudioClipDto? Audio { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class NoteDto
{
    public int Pitch { get; set; }
    public double Start { get; set; }
    public double Length { get; set; }
    public float Velocity { get; set; } = 1.0f;

    public NoteDto() { }
    public NoteDto(NotaNote n) { Pitch = n.Pitch; Start = n.StartBeat; Length = n.LengthBeats; Velocity = n.Velocity; }
    public NotaNote ToNote() => new(Pitch, Start, Length, Velocity);
}
