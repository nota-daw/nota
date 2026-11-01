// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota's built-in devices as the browser and the command palette list them: name, kind,
// the short description the browser prints, and the semantic descriptor (CP-12/13.1) the
// palette's search reads. A new built-in device is registered here — and the palette
// coverage test fails until it has a role and aliases (see the nota-instrument skill).

using Nota.Application.Localization;
using Nota.Application.Palette;

namespace Nota.Application;

public enum BuiltinDeviceType { Instrument, AudioEffect, MidiEffect }

/// <summary>A built-in device. <see cref="Kind"/> is the engine kind for its type
/// (instrument kind / built-in effect kind / MIDI effect kind).</summary>
public sealed record BuiltinDevice(string Name, BuiltinDeviceType Type, int Kind, string Sub, SemanticDescriptor Descriptor)
{
    /// <summary>The browser library key ("bi:6", "be:2", "bm:0") — favourites, tags, frecency.</summary>
    public string LibraryKey => Type switch
    {
        BuiltinDeviceType.Instrument => $"bi:{Kind}",
        BuiltinDeviceType.AudioEffect => $"be:{Kind}",
        _ => $"bm:{Kind}",
    };
}

public static class BuiltinDeviceCatalog
{
    private static SemanticDescriptor D(string role, string aliases, string character = "", string sound = "",
        string source = "", string task = "", string genre = "", string description = "")
    {
        static string[] Split(string s) => s.Length == 0 ? [] : s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new SemanticDescriptor
        {
            Role = role, Aliases = Split(aliases), Character = Split(character), Sound = Split(sound),
            Source = Split(source), Task = Split(task), Genre = Split(genre), Description = description,
        };
    }

    private const BuiltinDeviceType I = BuiltinDeviceType.Instrument, A = BuiltinDeviceType.AudioEffect, M = BuiltinDeviceType.MidiEffect;

    public static IReadOnlyList<BuiltinDevice> All { get; } = new BuiltinDevice[]
    {
        // ---- instruments ----------------------------------------------------------------------
        new("Nota Physical", I, 2, Loc.T("physical synth"), D("synth.physical", "modal karplus string mallet",
            "metallic glassy", "pluck bell keys strings perc", "", "", "cinematic",
            Loc.T("Physical modelling of struck, plucked and blown bodies."))),
        new("Nota Aurora", I, 5, Loc.T("wavetable synth"), D("synth.wavetable", "wt serum",
            "bright airy evolving lush", "pad lead texture", "", "", "edm cinematic",
            Loc.T("Wavetable synth with warping oscillators — evolving pads and modern leads."))),
        // Nota Synth (kind 0) and Nota Bass (kind 7) folded into Volt (specs/026): their engines
        // still load saved tracks, but new ones are Volts, so they are not offered here.
        new("Nota Volt", I, 6, Loc.T("analog synth"), D("synth.subtractive", "moog analog poly va init basic supersaw 303 acid tb",
            "warm vintage clean bright gritty", "pad bass lead keys pluck", "", "", "synthwave techno acid house",
            Loc.T("Two-oscillator analog-style synth with drive and a unison stack — pads, basses, acid lines and leads."))),
        new("Nota Pendulum", I, 8, Loc.T("arp synth"), D("synth", "arpsynth sequencer",
            "evolving bright", "seq pluck", "", "", "synthwave techno",
            Loc.T("A synth with a built-in pattern engine for arpeggios and sequences."))),
        new("Nota Operator", I, 9, Loc.T("FM synth"), D("synth.fm", "dx7 operator fm",
            "bright metallic glassy", "bell keys bass pluck", "", "", "",
            Loc.T("FM synthesis — bells, electric pianos and hard basses."))),
        new("Nota Grain", I, 10, Loc.T("granular synth"), D("synth.granular", "granulator cloud",
            "airy evolving", "texture drone pad", "", "", "ambient cinematic",
            Loc.T("Granular synth that turns a sample into clouds, textures and drones."))),
        new("Nota Flux", I, 11, Loc.T("vector-morph synth"), D("synth.vector", "wavestation morph",
            "evolving lush", "pad texture", "", "", "ambient",
            Loc.T("Vector synth morphing between sources — moving pads and textures."))),
        new("Nota Rhythm", I, 12, Loc.T("drum machine"), D("drum", "808 909 drummachine groovebox",
            "punchy", "perc", "drums", "", "trap house techno hiphop",
            Loc.T("Eight-voice drum machine with a step sequencer."))),
        new("Nota Monolith", I, 13, Loc.T("mono synth"), D("synth.subtractive", "minimoog monosynth",
            "warm aggressive deep", "bass lead", "", "", "",
            Loc.T("Monophonic analog-style synth for fat basses and leads."))),
        new("Nota Pentad", I, 14, Loc.T("poly synth"), D("synth.subtractive", "prophet poly",
            "warm lush vintage", "pad keys lead brass", "", "", "synthwave",
            Loc.T("Five-voice vintage polysynth — lush pads, brass and keys."))),
        new("Nota Consort", I, 15, Loc.T("paraphonic synth"), D("synth.subtractive", "paraphonic string-machine",
            "lush vintage warm", "strings pad choir", "", "", "synthwave",
            Loc.T("Paraphonic ensemble synth — string machines and choirs."))),
        new("Nota Keys", I, 16, Loc.T("electric piano"), D("synth.physical", "epiano e-piano rhodes wurlitzer wurli clavinet clav tine reed suitcase",
            "warm vintage", "keys", "", "", "jazz funk",
            Loc.T("Modelled electric piano — tine, suitcase, reed and clav with tremolo, phaser and cabinet."))),
        new("Nota Mosaic", I, 17, Loc.T("multisample"), D("sampler", "multisample multisampler sfz soundfont piano grand acoustic orchestral orchestra zones velocity round-robin мультисэмпл пианино рояль",
            "", "keys strings brass choir", "", "", "cinematic",
            Loc.T("Multisample instrument — zones by key and velocity, round-robin and release samples; SFZ import and packs."))),
        new("Nota Sampler", I, 1, Loc.T("built-in"), D("sampler", "simpler oneshot",
            "", "keys perc", "", "", "",
            Loc.T("Plays a sample across the keyboard."))),
        new("Nota Instrument Rack", I, 3, Loc.T("built-in"), D("rack", "layer split instrumentrack",
            "", "", "", "", "",
            Loc.T("Layers several instruments in parallel chains."))),
        new("Nota Drum Rack", I, 4, Loc.T("built-in"), D("drum", "drumrack pads kit",
            "punchy", "perc", "drums", "", "trap hiphop house",
            Loc.T("Sixteen pads, each with its own sample or instrument chain."))),

        // ---- audio effects ------------------------------------------------------------------
        new("Nota EQ-3", A, 16, Loc.T("3-band EQ"), D("eq", "dj eq3 threeband",
            "", "", "master bus", "", "house techno",
            Loc.T("Three-band DJ-style EQ with kills."))),
        new("Nota EQ-8", A, 0, Loc.T("8-band EQ"), D("eq", "parametric eq8 proq",
            "clean", "", "vocals master bus drums", "repair", "",
            Loc.T("Eight-band parametric EQ with a live spectrum."))),
        new("Nota Compressor", A, 1, Loc.T("built-in"), D("compressor", "comp vca opto",
            "punchy", "", "drums vocals bus bass", "glue sidechain parallel", "",
            Loc.T("Compressor with sidechain and five characters — glue, punch and pumping."))),
        new("Nota Prism", A, 21, Loc.T("multiband dynamics"), D("multiband", "ott upward multibandcompressor",
            "bright aggressive", "", "master bus", "loudness", "edm dubstep",
            Loc.T("Three-band upward and downward compression — OTT-style density."))),
        new("Nota Lens", A, 22, Loc.T("analyzer / scope"), D("analyzer", "oscilloscope spectrum meter",
            "", "", "master", "", "",
            Loc.T("Spectrum analyser, oscilloscope and level meter."))),
        new("Nota Reverb", A, 2, Loc.T("built-in"), D("reverb", "verb hall room plate",
            "airy wide", "", "vocals bus drums", "", "ambient",
            Loc.T("Algorithmic reverb from small rooms to large halls."))),
        new("Nota Chamber", A, 20, Loc.T("hybrid reverb"), D("reverb", "verb convolution chamber",
            "dark lush wide", "", "vocals bus", "", "ambient cinematic",
            Loc.T("Hybrid reverb with early reflections and a long, dense tail."))),
        new("Nota Delay", A, 3, Loc.T("built-in"), D("delay", "echo pingpong",
            "wide", "", "vocals synth guitar", "", "",
            Loc.T("Stereo and ping-pong delay synced to the tempo."))),
        new("Nota Utility", A, 4, Loc.T("built-in"), D("utility", "gain width mono phase",
            "", "", "master bus", "widen", "",
            Loc.T("Gain, width, mono and phase — the housekeeping tool."))),
        new("Nota Level", A, 18, Loc.T("gain"), D("utility", "gain trim volume",
            "clean", "", "bus master", "loudness", "",
            Loc.T("A single gain stage with a big readout."))),
        new("Nota Shutter", A, 19, Loc.T("gate"), D("gate", "noisegate trancegate",
            "punchy", "", "drums vocals", "repair transient", "trance",
            Loc.T("Noise gate and rhythmic trance gate."))),
        new("Nota Valve", A, 6, Loc.T("amplifier"), D("amp", "guitaramp tube cabinet",
            "warm gritty aggressive", "", "guitar bass", "", "rock",
            Loc.T("Guitar amp and cabinet models from clean to high gain."))),
        new("Nota Auto Filter", A, 7, Loc.T("envelope / LFO"), D("filter", "autofilter wah sweep",
            "evolving", "", "synth drums bass", "modulation", "house techno",
            Loc.T("Resonant filter swept by an envelope follower or an LFO."))),
        new("Nota Vintage", A, 8, Loc.T("vintage saturator"), D("saturation", "tape tube saturator",
            "warm vintage lofi", "", "master bus drums", "", "lofi",
            Loc.T("Tape and tube saturation — warmth, wow and flutter."))),
        new("Nota Forge", A, 17, Loc.T("saturator"), D("saturation", "distortion waveshaper",
            "gritty aggressive warm", "", "drums bass synth", "", "",
            Loc.T("Waveshaping saturator from soft warmth to hard clipping."))),
        new("Nota Orbit", A, 9, Loc.T("auto-pan"), D("autopan", "panner tremolo",
            "wide evolving", "", "synth keys", "modulation", "",
            Loc.T("Auto-pan and tremolo synced to the tempo."))),
        new("Nota Flanger", A, 23, Loc.T("flanger"), D("flanger", "jet",
            "metallic evolving", "", "drums guitar synth", "modulation", "",
            Loc.T("Classic jet flanger."))),
        new("Nota Phaser", A, 24, Loc.T("phaser"), D("phaser", "phase",
            "evolving vintage", "", "keys guitar synth", "modulation", "funk",
            Loc.T("Multi-stage phaser."))),
        new("Nota Chorus", A, 25, Loc.T("chorus"), D("chorus", "ensemble dimension",
            "wide lush", "", "synth keys guitar vocals", "widen modulation", "",
            Loc.T("Chorus and ensemble for width and shimmer."))),
        new("Nota Vocoder", A, 26, Loc.T("vocoder"), D("vocoder", "talkbox robot choir channel",
            "metallic vintage", "", "vocals synth", "modulation", "synthwave",
            Loc.T("Shapes this track's sound with another track's voice."))),
        new("Nota Auto Shift", A, 10, Loc.T("pitch correction"), D("pitch", "autotune melodyne tune",
            "", "", "vocals", "tuning", "trap pop",
            Loc.T("Real-time pitch correction to a key and scale."))),
        new("Nota Beat Repeat", A, 11, Loc.T("glitch & repeats"), D("glitch", "stutter repeat buffer",
            "aggressive", "", "drums vocals", "tapestop", "edm dubstep",
            Loc.T("Captures and repeats slices for stutters and glitches."))),
        new("Nota Crush", A, 12, Loc.T("bit crusher"), D("bitcrusher", "decimator redux",
            "lofi gritty", "", "drums synth", "", "lofi hiphop",
            Loc.T("Bit-depth and sample-rate reduction."))),
        new("Nota Dynamic EQ-8", A, 13, Loc.T("dynamic EQ"), D("eq", "dynamiceq soothe deesser",
            "clean", "", "vocals master bus", "deess repair", "",
            Loc.T("EQ bands that react to the level — de-essing and resonance control."))),
        new("Nota Ceiling", A, 14, Loc.T("limiter"), D("limiter", "maximizer brickwall l2",
            "clean", "", "master bus", "loudness", "",
            Loc.T("Brickwall limiter for the master."))),
        new("Nota Strata", A, 15, Loc.T("looper"), D("looper", "loop overdub",
            "evolving", "", "guitar vocals synth", "", "ambient",
            Loc.T("Layered looper with overdubs."))),
        new("Nota Audio Effect Rack", A, 5, Loc.T("built-in"), D("rack", "effectrack chain parallel",
            "", "", "bus", "parallel", "",
            Loc.T("Runs several effect chains in parallel."))),

        // ---- MIDI effects -----------------------------------------------------------------
        new("Nota Arp", M, 0, Loc.T("step arpeggiator"), D("arp", "arpeggiator steps",
            "evolving", "seq", "", "", "synthwave trance",
            Loc.T("Step arpeggiator with groove lanes."))),
        new("Nota Chord", M, 1, Loc.T("stacked intervals"), D("chord", "harmonizer stack",
            "lush", "", "keys synth", "", "house",
            Loc.T("Turns single notes into chords."))),
        new("Nota Scale", M, 2, Loc.T("pitch quantize"), D("scale", "keylock quantizepitch",
            "", "", "synth keys", "tuning", "",
            Loc.T("Keeps notes in a key and scale."))),
        new("Nota Length", M, 3, Loc.T("force note length"), D("notelength", "gatelength legato",
            "", "", "synth", "", "",
            Loc.T("Forces note lengths — staccato, legato or fixed."))),
        new("Nota Velocity", M, 4, Loc.T("velocity shaping"), D("velocity", "velocitycurve touch",
            "", "", "drums keys", "", "",
            Loc.T("Reshapes, compresses or randomises note velocities."))),
        new("Nota Random", M, 5, Loc.T("random transpose"), D("random", "humanize chance",
            "evolving", "", "synth", "", "",
            Loc.T("Random transposition, timing and dropouts."))),
        new("Nota MIDI Effect Rack", M, 6, Loc.T("chains of MIDI effects"), D("rack", "midirack split layer zones",
            "", "", "keys synth", "", "",
            Loc.T("Splits notes across chains of MIDI effects by key, velocity or a selector."))),
    };

    public static IEnumerable<BuiltinDevice> OfType(BuiltinDeviceType t) => All.Where(d => d.Type == t);

    public static BuiltinDevice? Find(BuiltinDeviceType t, int kind) => All.FirstOrDefault(d => d.Type == t && d.Kind == kind);
}
