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
        new("Nota Physical", I, 2, Loc.T("palette.devices.physical_synth"), D("synth.physical", "modal karplus string mallet",
            "metallic glassy", "pluck bell keys strings perc", "", "", "cinematic",
            Loc.T("palette.devices.physical_modelling_of_struck_plucked"))),
        new("Nota Aurora", I, 5, Loc.T("palette.devices.wavetable_synth"), D("synth.wavetable", "wt serum",
            "bright airy evolving lush", "pad lead texture", "", "", "edm cinematic",
            Loc.T("palette.devices.wavetable_synth_with_warping_oscillators"))),
        // Nota Synth (kind 0) and Nota Bass (kind 7) folded into Volt (specs/026): their engines
        // still load saved tracks, but new ones are Volts, so they are not offered here.
        new("Nota Volt", I, 6, Loc.T("palette.devices.analog_synth"), D("synth.subtractive", "moog analog poly va init basic supersaw 303 acid tb",
            "warm vintage clean bright gritty", "pad bass lead keys pluck", "", "", "synthwave techno acid house",
            Loc.T("palette.devices.two_oscillator_analog_style_synth"))),
        new("Nota Pendulum", I, 8, Loc.T("palette.devices.arp_synth"), D("synth", "arpsynth sequencer",
            "evolving bright", "seq pluck", "", "", "synthwave techno",
            Loc.T("palette.devices.a_synth_with_a_built"))),
        new("Nota Operator", I, 9, Loc.T("palette.devices.fm_synth"), D("synth.fm", "dx7 operator fm",
            "bright metallic glassy", "bell keys bass pluck", "", "", "",
            Loc.T("palette.devices.fm_synthesis_bells_electric_pianos"))),
        new("Nota Grain", I, 10, Loc.T("palette.devices.granular_synth"), D("synth.granular", "granulator cloud",
            "airy evolving", "texture drone pad", "", "", "ambient cinematic",
            Loc.T("palette.devices.granular_synth_that_turns_a"))),
        new("Nota Flux", I, 11, Loc.T("palette.devices.vector_morph_synth"), D("synth.vector", "wavestation morph",
            "evolving lush", "pad texture", "", "", "ambient",
            Loc.T("palette.devices.vector_synth_morphing_between_sources"))),
        new("Nota Rhythm", I, 12, Loc.T("palette.devices.drum_machine"), D("drum", "808 909 drummachine groovebox",
            "punchy", "perc", "drums", "", "trap house techno hiphop",
            Loc.T("palette.devices.eight_voice_drum_machine_with"))),
        new("Nota Monolith", I, 13, Loc.T("palette.devices.mono_synth"), D("synth.subtractive", "minimoog monosynth",
            "warm aggressive deep", "bass lead", "", "", "",
            Loc.T("palette.devices.monophonic_analog_style_synth_for"))),
        new("Nota Pentad", I, 14, Loc.T("palette.devices.poly_synth"), D("synth.subtractive", "prophet poly",
            "warm lush vintage", "pad keys lead brass", "", "", "synthwave",
            Loc.T("palette.devices.five_voice_vintage_polysynth_lush"))),
        new("Nota Consort", I, 15, Loc.T("palette.devices.paraphonic_synth"), D("synth.subtractive", "paraphonic string-machine",
            "lush vintage warm", "strings pad choir", "", "", "synthwave",
            Loc.T("palette.devices.paraphonic_ensemble_synth_string_machines"))),
        new("Nota Keys", I, 16, Loc.T("palette.devices.electric_piano"), D("synth.physical", "epiano e-piano rhodes wurlitzer wurli clavinet clav tine reed suitcase",
            "warm vintage", "keys", "", "", "jazz funk",
            Loc.T("palette.devices.modelled_electric_piano_tine_suitcase"))),
        new("Nota Mosaic", I, 17, Loc.T("palette.devices.multisample"), D("sampler", "multisample multisampler sfz soundfont piano grand acoustic orchestral orchestra zones velocity round-robin мультисэмпл пианино рояль",
            "", "keys strings brass choir", "", "", "cinematic",
            Loc.T("palette.devices.multisample_instrument_zones_by_key"))),
        new("Nota Sampler", I, 1, Loc.T("palette.devices.built_in"), D("sampler", "simpler oneshot",
            "", "keys perc", "", "", "",
            Loc.T("palette.devices.plays_a_sample_across_the"))),
        new("Nota Instrument Rack", I, 3, Loc.T("palette.devices.built_in"), D("rack", "layer split instrumentrack",
            "", "", "", "", "",
            Loc.T("palette.devices.layers_several_instruments_in_parallel"))),
        new("Nota Drum Rack", I, 4, Loc.T("palette.devices.built_in"), D("drum", "drumrack pads kit",
            "punchy", "perc", "drums", "", "trap hiphop house",
            Loc.T("palette.devices.sixteen_pads_each_with_its"))),

        // ---- audio effects ------------------------------------------------------------------
        new("Nota EQ-3", A, 16, Loc.T("palette.devices.3_band_eq"), D("eq", "dj eq3 threeband",
            "", "", "master bus", "", "house techno",
            Loc.T("palette.devices.three_band_dj_style_eq"))),
        new("Nota EQ-8", A, 0, Loc.T("palette.devices.8_band_eq"), D("eq", "parametric eq8 proq",
            "clean", "", "vocals master bus drums", "repair", "",
            Loc.T("palette.devices.eight_band_parametric_eq_with"))),
        new("Nota Compressor", A, 1, Loc.T("palette.devices.built_in"), D("compressor", "comp vca opto",
            "punchy", "", "drums vocals bus bass", "glue sidechain parallel", "",
            Loc.T("palette.devices.compressor_with_sidechain_and_five"))),
        new("Nota Prism", A, 21, Loc.T("palette.devices.multiband_dynamics"), D("multiband", "ott upward multibandcompressor",
            "bright aggressive", "", "master bus", "loudness", "edm dubstep",
            Loc.T("palette.devices.three_band_upward_and_downward"))),
        new("Nota Lens", A, 22, Loc.T("palette.devices.analyzer_scope"), D("analyzer", "oscilloscope spectrum meter",
            "", "", "master", "", "",
            Loc.T("palette.devices.spectrum_analyser_oscilloscope_and_level"))),
        new("Nota Reverb", A, 2, Loc.T("palette.devices.built_in"), D("reverb", "verb hall room plate",
            "airy wide", "", "vocals bus drums", "", "ambient",
            Loc.T("palette.devices.algorithmic_reverb_from_small_rooms"))),
        new("Nota Chamber", A, 20, Loc.T("palette.devices.hybrid_reverb"), D("reverb", "verb convolution chamber",
            "dark lush wide", "", "vocals bus", "", "ambient cinematic",
            Loc.T("palette.devices.hybrid_reverb_with_early_reflections"))),
        new("Nota Delay", A, 3, Loc.T("palette.devices.built_in"), D("delay", "echo pingpong",
            "wide", "", "vocals synth guitar", "", "",
            Loc.T("palette.devices.stereo_and_ping_pong_delay"))),
        new("Nota Utility", A, 4, Loc.T("palette.devices.built_in"), D("utility", "gain width mono phase",
            "", "", "master bus", "widen", "",
            Loc.T("palette.devices.gain_width_mono_and_phase"))),
        new("Nota Level", A, 18, Loc.T("palette.devices.gain"), D("utility", "gain trim volume",
            "clean", "", "bus master", "loudness", "",
            Loc.T("palette.devices.a_single_gain_stage_with"))),
        new("Nota Shutter", A, 19, Loc.T("palette.devices.gate"), D("gate", "noisegate trancegate",
            "punchy", "", "drums vocals", "repair transient", "trance",
            Loc.T("palette.devices.noise_gate_and_rhythmic_trance"))),
        new("Nota Valve", A, 6, Loc.T("palette.devices.amplifier"), D("amp", "guitaramp tube cabinet",
            "warm gritty aggressive", "", "guitar bass", "", "rock",
            Loc.T("palette.devices.guitar_amp_and_cabinet_models"))),
        new("Nota Auto Filter", A, 7, Loc.T("palette.devices.envelope_lfo"), D("filter", "autofilter wah sweep",
            "evolving", "", "synth drums bass", "modulation", "house techno",
            Loc.T("palette.devices.resonant_filter_swept_by_an"))),
        new("Nota Vintage", A, 8, Loc.T("palette.devices.vintage_saturator"), D("saturation", "tape tube saturator",
            "warm vintage lofi", "", "master bus drums", "", "lofi",
            Loc.T("palette.devices.tape_and_tube_saturation_warmth"))),
        new("Nota Forge", A, 17, Loc.T("palette.devices.saturator"), D("saturation", "distortion waveshaper",
            "gritty aggressive warm", "", "drums bass synth", "", "",
            Loc.T("palette.devices.waveshaping_saturator_from_soft_warmth"))),
        new("Nota Orbit", A, 9, Loc.T("palette.devices.auto_pan"), D("autopan", "panner tremolo",
            "wide evolving", "", "synth keys", "modulation", "",
            Loc.T("palette.devices.auto_pan_and_tremolo_synced"))),
        new("Nota Flanger", A, 23, Loc.T("palette.devices.flanger"), D("flanger", "jet",
            "metallic evolving", "", "drums guitar synth", "modulation", "",
            Loc.T("palette.devices.classic_jet_flanger"))),
        new("Nota Phaser", A, 24, Loc.T("palette.devices.phaser"), D("phaser", "phase",
            "evolving vintage", "", "keys guitar synth", "modulation", "funk",
            Loc.T("palette.devices.multi_stage_phaser"))),
        new("Nota Chorus", A, 25, Loc.T("palette.devices.chorus"), D("chorus", "ensemble dimension",
            "wide lush", "", "synth keys guitar vocals", "widen modulation", "",
            Loc.T("palette.devices.chorus_and_ensemble_for_width"))),
        new("Nota Vocoder", A, 26, Loc.T("palette.devices.vocoder"), D("vocoder", "talkbox robot choir channel",
            "metallic vintage", "", "vocals synth", "modulation", "synthwave",
            Loc.T("palette.devices.shapes_this_tracks_sound_with"))),
        new("Nota Auto Shift", A, 10, Loc.T("palette.devices.pitch_correction"), D("pitch", "autotune melodyne tune",
            "", "", "vocals", "tuning", "trap pop",
            Loc.T("palette.devices.real_time_pitch_correction_to"))),
        new("Nota Beat Repeat", A, 11, Loc.T("palette.devices.glitch_repeats"), D("glitch", "stutter repeat buffer",
            "aggressive", "", "drums vocals", "tapestop", "edm dubstep",
            Loc.T("palette.devices.captures_and_repeats_slices_for"))),
        new("Nota Crush", A, 12, Loc.T("palette.devices.bit_crusher"), D("bitcrusher", "decimator redux",
            "lofi gritty", "", "drums synth", "", "lofi hiphop",
            Loc.T("palette.devices.bit_depth_and_sample_rate"))),
        new("Nota Dynamic EQ-8", A, 13, Loc.T("palette.devices.dynamic_eq"), D("eq", "dynamiceq soothe deesser",
            "clean", "", "vocals master bus", "deess repair", "",
            Loc.T("palette.devices.eq_bands_that_react_to"))),
        new("Nota Ceiling", A, 14, Loc.T("palette.devices.limiter"), D("limiter", "maximizer brickwall l2",
            "clean", "", "master bus", "loudness", "",
            Loc.T("palette.devices.brickwall_limiter_for_the_master"))),
        new("Nota Strata", A, 15, Loc.T("palette.devices.looper"), D("looper", "loop overdub",
            "evolving", "", "guitar vocals synth", "", "ambient",
            Loc.T("palette.devices.layered_looper_with_overdubs"))),
        new("Nota Audio Effect Rack", A, 5, Loc.T("palette.devices.built_in"), D("rack", "effectrack chain parallel",
            "", "", "bus", "parallel", "",
            Loc.T("palette.devices.runs_several_effect_chains_in"))),

        // ---- MIDI effects -----------------------------------------------------------------
        new("Nota Arp", M, 0, Loc.T("palette.devices.step_arpeggiator"), D("arp", "arpeggiator steps",
            "evolving", "seq", "", "", "synthwave trance",
            Loc.T("palette.devices.step_arpeggiator_with_groove_lanes"))),
        new("Nota Chord", M, 1, Loc.T("palette.devices.stacked_intervals"), D("chord", "harmonizer stack",
            "lush", "", "keys synth", "", "house",
            Loc.T("palette.devices.turns_single_notes_into_chords"))),
        new("Nota Scale", M, 2, Loc.T("palette.devices.pitch_quantize"), D("scale", "keylock quantizepitch",
            "", "", "synth keys", "tuning", "",
            Loc.T("palette.devices.keeps_notes_in_a_key"))),
        new("Nota Length", M, 3, Loc.T("palette.devices.force_note_length"), D("notelength", "gatelength legato",
            "", "", "synth", "", "",
            Loc.T("palette.devices.forces_note_lengths_staccato_legato"))),
        new("Nota Velocity", M, 4, Loc.T("palette.devices.velocity_shaping"), D("velocity", "velocitycurve touch",
            "", "", "drums keys", "", "",
            Loc.T("palette.devices.reshapes_compresses_or_randomises_note"))),
        new("Nota Random", M, 5, Loc.T("palette.devices.random_transpose"), D("random", "humanize chance",
            "evolving", "", "synth", "", "",
            Loc.T("palette.devices.random_transposition_timing_and_dropouts"))),
        new("Nota MIDI Effect Rack", M, 6, Loc.T("palette.devices.chains_of_midi_effects"), D("rack", "midirack split layer zones",
            "", "", "keys synth", "", "",
            Loc.T("palette.devices.splits_notes_across_chains_of"))),
    };

    public static IEnumerable<BuiltinDevice> OfType(BuiltinDeviceType t) => All.Where(d => d.Type == t);

    public static BuiltinDevice? Find(BuiltinDeviceType t, int kind) => All.FirstOrDefault(d => d.Type == t && d.Kind == kind);
}
