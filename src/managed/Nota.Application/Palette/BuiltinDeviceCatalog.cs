// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota's built-in devices as the browser and the command palette list them: name, kind,
// the short description the browser prints, and the semantic descriptor (CP-12/13.1) the
// palette's search reads. A new built-in device is registered here — and the palette
// coverage test fails until it has a role and aliases (see the nota-instrument skill).

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
        new("Nota Synth", I, 0, "subtractive synth", D("synth.subtractive", "poly va init basic",
            "clean bright", "pad bass lead keys pluck", "", "", "",
            "A straightforward subtractive polysynth: oscillators, a filter and envelopes.")),
        new("Nota Physical", I, 2, "physical synth", D("synth.physical", "modal karplus string mallet",
            "metallic glassy", "pluck bell keys strings perc", "", "", "cinematic",
            "Physical modelling of struck, plucked and blown bodies.")),
        new("Nota Aurora", I, 5, "wavetable synth", D("synth.wavetable", "wt serum",
            "bright airy evolving lush", "pad lead texture", "", "", "edm cinematic",
            "Wavetable synth with warping oscillators — evolving pads and modern leads.")),
        new("Nota Volt", I, 6, "analog synth", D("synth.subtractive", "moog analog poly",
            "warm vintage", "pad bass lead keys", "", "", "synthwave",
            "Warm analog-style polysynth for pads, basses and leads.")),
        new("Nota Bass", I, 7, "bass synth", D("synth.subtractive", "303 acid tb",
            "gritty", "bass", "", "", "techno acid house",
            "Mono bass synth with a squelchy filter — acid lines and techno basses.")),
        new("Nota Pendulum", I, 8, "arp synth", D("synth", "arpsynth sequencer",
            "evolving bright", "seq pluck", "", "", "synthwave techno",
            "A synth with a built-in pattern engine for arpeggios and sequences.")),
        new("Nota Operator", I, 9, "FM synth", D("synth.fm", "dx7 operator fm",
            "bright metallic glassy", "bell keys bass pluck", "", "", "",
            "FM synthesis — bells, electric pianos and hard basses.")),
        new("Nota Grain", I, 10, "granular synth", D("synth.granular", "granulator cloud",
            "airy evolving", "texture drone pad", "", "", "ambient cinematic",
            "Granular synth that turns a sample into clouds, textures and drones.")),
        new("Nota Flux", I, 11, "vector-morph synth", D("synth.vector", "wavestation morph",
            "evolving lush", "pad texture", "", "", "ambient",
            "Vector synth morphing between sources — moving pads and textures.")),
        new("Nota Rhythm", I, 12, "drum machine", D("drum", "808 909 drummachine groovebox",
            "punchy", "perc", "drums", "", "trap house techno hiphop",
            "Eight-voice drum machine with a step sequencer.")),
        new("Nota Monolith", I, 13, "mono synth", D("synth.subtractive", "minimoog monosynth",
            "warm aggressive deep", "bass lead", "", "", "",
            "Monophonic analog-style synth for fat basses and leads.")),
        new("Nota Pentad", I, 14, "poly synth", D("synth.subtractive", "prophet poly",
            "warm lush vintage", "pad keys lead brass", "", "", "synthwave",
            "Five-voice vintage polysynth — lush pads, brass and keys.")),
        new("Nota Consort", I, 15, "paraphonic synth", D("synth.subtractive", "paraphonic string-machine",
            "lush vintage warm", "strings pad choir", "", "", "synthwave",
            "Paraphonic ensemble synth — string machines and choirs.")),
        new("Nota Sampler", I, 1, "built-in", D("sampler", "simpler multisample",
            "", "keys perc", "", "", "",
            "Plays a sample across the keyboard.")),
        new("Nota Instrument Rack", I, 3, "built-in", D("rack", "layer split instrumentrack",
            "", "", "", "", "",
            "Layers several instruments in parallel chains.")),
        new("Nota Drum Rack", I, 4, "built-in", D("drum", "drumrack pads kit",
            "punchy", "perc", "drums", "", "trap hiphop house",
            "Sixteen pads, each with its own sample or instrument chain.")),

        // ---- audio effects ------------------------------------------------------------------
        new("Nota EQ-3", A, 16, "3-band EQ", D("eq", "dj eq3 threeband",
            "", "", "master bus", "", "house techno",
            "Three-band DJ-style EQ with kills.")),
        new("Nota EQ-8", A, 0, "8-band EQ", D("eq", "parametric eq8 proq",
            "clean", "", "vocals master bus drums", "repair", "",
            "Eight-band parametric EQ with a live spectrum.")),
        new("Nota Compressor", A, 1, "built-in", D("compressor", "comp vca opto",
            "punchy", "", "drums vocals bus bass", "glue sidechain parallel", "",
            "Compressor with sidechain and five characters — glue, punch and pumping.")),
        new("Nota Prism", A, 21, "multiband dynamics", D("multiband", "ott upward multibandcompressor",
            "bright aggressive", "", "master bus", "loudness", "edm dubstep",
            "Three-band upward and downward compression — OTT-style density.")),
        new("Nota Lens", A, 22, "analyzer / scope", D("analyzer", "oscilloscope spectrum meter",
            "", "", "master", "", "",
            "Spectrum analyser, oscilloscope and level meter.")),
        new("Nota Reverb", A, 2, "built-in", D("reverb", "verb hall room plate",
            "airy wide", "", "vocals bus drums", "", "ambient",
            "Algorithmic reverb from small rooms to large halls.")),
        new("Nota Chamber", A, 20, "hybrid reverb", D("reverb", "verb convolution chamber",
            "dark lush wide", "", "vocals bus", "", "ambient cinematic",
            "Hybrid reverb with early reflections and a long, dense tail.")),
        new("Nota Delay", A, 3, "built-in", D("delay", "echo pingpong",
            "wide", "", "vocals synth guitar", "", "",
            "Stereo and ping-pong delay synced to the tempo.")),
        new("Nota Utility", A, 4, "built-in", D("utility", "gain width mono phase",
            "", "", "master bus", "widen", "",
            "Gain, width, mono and phase — the housekeeping tool.")),
        new("Nota Level", A, 18, "gain", D("utility", "gain trim volume",
            "clean", "", "bus master", "loudness", "",
            "A single gain stage with a big readout.")),
        new("Nota Shutter", A, 19, "gate", D("gate", "noisegate trancegate",
            "punchy", "", "drums vocals", "repair transient", "trance",
            "Noise gate and rhythmic trance gate.")),
        new("Nota Valve", A, 6, "amplifier", D("amp", "guitaramp tube cabinet",
            "warm gritty aggressive", "", "guitar bass", "", "rock",
            "Guitar amp and cabinet models from clean to high gain.")),
        new("Nota Auto Filter", A, 7, "envelope / LFO", D("filter", "autofilter wah sweep",
            "evolving", "", "synth drums bass", "modulation", "house techno",
            "Resonant filter swept by an envelope follower or an LFO.")),
        new("Nota Vintage", A, 8, "vintage saturator", D("saturation", "tape tube saturator",
            "warm vintage lofi", "", "master bus drums", "", "lofi",
            "Tape and tube saturation — warmth, wow and flutter.")),
        new("Nota Forge", A, 17, "saturator", D("saturation", "distortion waveshaper",
            "gritty aggressive warm", "", "drums bass synth", "", "",
            "Waveshaping saturator from soft warmth to hard clipping.")),
        new("Nota Orbit", A, 9, "auto-pan", D("autopan", "panner tremolo",
            "wide evolving", "", "synth keys", "modulation", "",
            "Auto-pan and tremolo synced to the tempo.")),
        new("Nota Flanger", A, 23, "flanger", D("flanger", "jet",
            "metallic evolving", "", "drums guitar synth", "modulation", "",
            "Classic jet flanger.")),
        new("Nota Phaser", A, 24, "phaser", D("phaser", "phase",
            "evolving vintage", "", "keys guitar synth", "modulation", "funk",
            "Multi-stage phaser.")),
        new("Nota Chorus", A, 25, "chorus", D("chorus", "ensemble dimension",
            "wide lush", "", "synth keys guitar vocals", "widen modulation", "",
            "Chorus and ensemble for width and shimmer.")),
        new("Nota Auto Shift", A, 10, "pitch correction", D("pitch", "autotune melodyne tune",
            "", "", "vocals", "tuning", "trap pop",
            "Real-time pitch correction to a key and scale.")),
        new("Nota Beat Repeat", A, 11, "glitch & repeats", D("glitch", "stutter repeat buffer",
            "aggressive", "", "drums vocals", "tapestop", "edm dubstep",
            "Captures and repeats slices for stutters and glitches.")),
        new("Nota Crush", A, 12, "bit crusher", D("bitcrusher", "decimator redux",
            "lofi gritty", "", "drums synth", "", "lofi hiphop",
            "Bit-depth and sample-rate reduction.")),
        new("Nota Dynamic EQ-8", A, 13, "dynamic EQ", D("eq", "dynamiceq soothe deesser",
            "clean", "", "vocals master bus", "deess repair", "",
            "EQ bands that react to the level — de-essing and resonance control.")),
        new("Nota Ceiling", A, 14, "limiter", D("limiter", "maximizer brickwall l2",
            "clean", "", "master bus", "loudness", "",
            "Brickwall limiter for the master.")),
        new("Nota Strata", A, 15, "looper", D("looper", "loop overdub",
            "evolving", "", "guitar vocals synth", "", "ambient",
            "Layered looper with overdubs.")),
        new("Nota Audio Effect Rack", A, 5, "built-in", D("rack", "effectrack chain parallel",
            "", "", "bus", "parallel", "",
            "Runs several effect chains in parallel.")),

        // ---- MIDI effects -----------------------------------------------------------------
        new("Nota Arp", M, 0, "step arpeggiator", D("arp", "arpeggiator steps",
            "evolving", "seq", "", "", "synthwave trance",
            "Step arpeggiator with groove lanes.")),
        new("Nota Chord", M, 1, "stacked intervals", D("chord", "harmonizer stack",
            "lush", "", "keys synth", "", "house",
            "Turns single notes into chords.")),
        new("Nota Scale", M, 2, "pitch quantize", D("scale", "keylock quantizepitch",
            "", "", "synth keys", "tuning", "",
            "Keeps notes in a key and scale.")),
        new("Nota Length", M, 3, "force note length", D("notelength", "gatelength legato",
            "", "", "synth", "", "",
            "Forces note lengths — staccato, legato or fixed.")),
        new("Nota Velocity", M, 4, "velocity shaping", D("velocity", "velocitycurve touch",
            "", "", "drums keys", "", "",
            "Reshapes, compresses or randomises note velocities.")),
        new("Nota Random", M, 5, "random transpose", D("random", "humanize chance",
            "evolving", "", "synth", "", "",
            "Random transposition, timing and dropouts.")),
    };

    public static IEnumerable<BuiltinDevice> OfType(BuiltinDeviceType t) => All.Where(d => d.Type == t);

    public static BuiltinDevice? Find(BuiltinDeviceType t, int kind) => All.FirstOrDefault(d => d.Type == t && d.Kind == kind);
}
