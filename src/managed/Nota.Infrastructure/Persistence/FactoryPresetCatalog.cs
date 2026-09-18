// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The shipped (read-only) factory preset library for Nota's built-in instruments
// and effects. Presets are defined in code as PresetDocuments (kind + named param
// values) so they always track the engine and need no on-disk files. Instrument
// values are normalized 0..1 (plugin-param ids); effect values are in real units
// (param names). Apply reuses PresetService.Apply — the same path as user presets.

using System.Collections.Generic;
using Nota.Application;

namespace Nota.Infrastructure;

public sealed class FactoryPresetCatalog : IFactoryPresets
{
    // Built-in instrument kinds: Synth 0, Physical 2, Aurora 5, Volt 6, Bass 7.
    // Built-in effect kinds: EQ 0, Compressor 1, Reverb 2, Delay 3, Utility 4.
    private readonly List<(FactoryPresetInfo Info, PresetDocument Doc)> _all = new();
    private readonly Dictionary<string, PresetDocument> _byId = new();

    public FactoryPresetCatalog()
    {
        // ---- Nota Synth (kind 0) — 25 patches across pads, basses, leads, keys and motion.
        //      wave: Saw 0 / Square .333 / Triangle .667 / Sine 1 · filtype: Off 0 / LP .333 /
        //      HP .667 / BP 1 · unison: 1 / 2 / 4 / 7 · voicemode: Poly 0 / Mono .5 / Legato 1.
        //      Bipolar params (detune, octave, pan, filenv) are neutral at 0.5; anything a
        //      preset does not name goes back to the instrument's default.
        Inst("synth", 0, "Init Saw",       ("wave", 0f));
        // Pads
        Inst("synth", 0, "Warm Pad",       ("wave", 0f),     ("attack", 0.55f), ("decay", 0.55f), ("sustain", 0.85f), ("release", 0.78f), ("cutoff", 0.46f), ("resonance", 0.10f), ("filenv", 0.60f), ("unison", 0.667f), ("detune", 0.60f), ("spread", 0.60f), ("velamp", 0.50f), ("gain", 0.78f));
        Inst("synth", 0, "Glass Pad",      ("wave", 0.667f), ("attack", 0.45f), ("decay", 0.60f), ("sustain", 0.70f), ("release", 0.82f), ("cutoff", 0.84f), ("resonance", 0.08f), ("unison", 0.667f), ("detune", 0.58f), ("spread", 0.70f), ("gain", 0.76f));
        Inst("synth", 0, "String Ensemble",("wave", 0f),     ("attack", 0.42f), ("decay", 0.50f), ("sustain", 0.88f), ("release", 0.70f), ("cutoff", 0.62f), ("resonance", 0.12f), ("unison", 1f),     ("detune", 0.62f), ("spread", 0.80f), ("gain", 0.72f));
        Inst("synth", 0, "Choir Pad",      ("wave", 0.667f), ("attack", 0.58f), ("decay", 0.55f), ("sustain", 0.90f), ("release", 0.85f), ("cutoff", 0.55f), ("resonance", 0.10f), ("unison", 0.667f), ("detune", 0.56f), ("spread", 0.75f), ("gain", 0.75f));
        Inst("synth", 0, "Drone",          ("wave", 0f),     ("attack", 0.70f), ("decay", 0.80f), ("sustain", 1f),    ("release", 0.88f), ("cutoff", 0.50f), ("resonance", 0.20f), ("unison", 1f),     ("detune", 0.72f), ("spread", 1f),    ("octave", 0.25f), ("gain", 0.68f));
        // Basses
        Inst("synth", 0, "Rubber Bass",    ("wave", 0f),     ("attack", 0f),    ("decay", 0.50f), ("sustain", 0.45f), ("release", 0.37f), ("cutoff", 0.40f), ("resonance", 0.28f), ("filenv", 0.70f), ("octave", 0.25f), ("voicemode", 0.5f), ("gain", 0.85f));
        Inst("synth", 0, "Sub Bass",       ("wave", 1f),     ("attack", 0f),    ("decay", 0.60f), ("sustain", 0.70f), ("release", 0.37f), ("cutoff", 0.34f), ("resonance", 0.05f), ("octave", 0.25f), ("voicemode", 0.5f), ("gain", 0.88f));
        Inst("synth", 0, "Acid Bass",      ("wave", 0f),     ("attack", 0f),    ("decay", 0.42f), ("sustain", 0.15f), ("release", 0.37f), ("cutoff", 0.35f), ("resonance", 0.78f), ("filenv", 0.85f), ("octave", 0.25f), ("voicemode", 0.5f), ("glide", 0.18f), ("gain", 0.82f));
        Inst("synth", 0, "Square Bass",    ("wave", 0.333f), ("attack", 0f),    ("decay", 0.55f), ("sustain", 0.55f), ("release", 0.37f), ("cutoff", 0.44f), ("resonance", 0.20f), ("filenv", 0.62f), ("octave", 0.25f), ("voicemode", 0.5f), ("gain", 0.85f));
        Inst("synth", 0, "Growl Bass",     ("wave", 0f),     ("attack", 0f),    ("decay", 0.50f), ("sustain", 0.50f), ("release", 0.40f), ("cutoff", 0.38f), ("resonance", 0.55f), ("filenv", 0.72f), ("unison", 0.333f), ("detune", 0.58f), ("octave", 0.25f), ("voicemode", 0.5f), ("gain", 0.82f));
        // Leads
        Inst("synth", 0, "Bright Lead",    ("wave", 0f),     ("attack", 0.06f), ("decay", 0.55f), ("sustain", 0.75f), ("release", 0.45f), ("cutoff", 0.82f), ("resonance", 0.28f), ("filenv", 0.58f), ("unison", 0.333f), ("detune", 0.55f), ("voicemode", 0.5f), ("gain", 0.78f));
        Inst("synth", 0, "Super Saw",      ("wave", 0f),     ("attack", 0.10f), ("decay", 0.60f), ("sustain", 0.85f), ("release", 0.60f), ("cutoff", 0.80f), ("resonance", 0.14f), ("unison", 1f),     ("detune", 0.68f), ("spread", 0.90f), ("gain", 0.70f));
        Inst("synth", 0, "Solo Lead",      ("wave", 0f),     ("attack", 0.02f), ("decay", 0.50f), ("sustain", 0.80f), ("release", 0.40f), ("cutoff", 0.72f), ("resonance", 0.30f), ("voicemode", 0.5f), ("glide", 0.16f), ("gain", 0.78f));
        Inst("synth", 0, "Slide Lead",     ("wave", 0.333f), ("pulsewidth", 0.35f), ("attack", 0.05f), ("decay", 0.55f), ("sustain", 0.80f), ("release", 0.45f), ("cutoff", 0.70f), ("resonance", 0.25f), ("voicemode", 1f), ("glide", 0.30f), ("gain", 0.78f));
        Inst("synth", 0, "Whistle",        ("wave", 1f),     ("attack", 0.30f), ("decay", 0.50f), ("sustain", 0.85f), ("release", 0.50f), ("cutoff", 0.90f), ("resonance", 0.05f), ("octave", 0.75f), ("voicemode", 0.5f), ("glide", 0.12f), ("gain", 0.74f));
        // Keys and plucks
        Inst("synth", 0, "Soft Pluck",     ("wave", 0.667f), ("attack", 0f),    ("decay", 0.60f), ("sustain", 0f),    ("release", 0.45f), ("cutoff", 0.66f), ("resonance", 0.18f), ("filenv", 0.55f), ("gain", 0.80f));
        Inst("synth", 0, "Glass Bell",     ("wave", 1f),     ("attack", 0f),    ("decay", 0.78f), ("sustain", 0f),    ("release", 0.70f), ("cutoff", 0.90f), ("resonance", 0.10f), ("octave", 0.75f), ("gain", 0.76f));
        Inst("synth", 0, "Electric Piano", ("wave", 1f),     ("attack", 0f),    ("decay", 0.70f), ("sustain", 0.25f), ("release", 0.55f), ("cutoff", 0.74f), ("resonance", 0.10f), ("filenv", 0.45f), ("gain", 0.80f));
        Inst("synth", 0, "Music Box",      ("wave", 1f),     ("attack", 0f),    ("decay", 0.62f), ("sustain", 0f),    ("release", 0.50f), ("cutoff", 0.88f), ("resonance", 0.06f), ("octave", 0.75f), ("gain", 0.74f));
        Inst("synth", 0, "Hollow Keys",    ("wave", 0.333f), ("pulsewidth", 0.22f), ("attack", 0.02f), ("decay", 0.58f), ("sustain", 0.45f), ("release", 0.48f), ("cutoff", 0.64f), ("resonance", 0.18f), ("gain", 0.78f));
        Inst("synth", 0, "Organ Tone",     ("wave", 0.333f), ("attack", 0f),    ("decay", 0f),    ("sustain", 1f),    ("release", 0.30f), ("filtype", 0f),   ("velamp", 0.20f), ("gain", 0.76f));
        // Motion
        Inst("synth", 0, "Air Sweep",      ("wave", 0f),     ("attack", 0.62f), ("decay", 0.65f), ("sustain", 0.80f), ("release", 0.80f), ("filtype", 0.667f), ("cutoff", 0.55f), ("resonance", 0.30f), ("filenv", 0.78f), ("unison", 0.667f), ("detune", 0.58f), ("spread", 0.80f), ("gain", 0.70f));
        Inst("synth", 0, "Filter Sweep",   ("wave", 0f),     ("attack", 0.35f), ("decay", 0.70f), ("sustain", 0.75f), ("release", 0.70f), ("cutoff", 0.30f), ("resonance", 0.45f), ("filenv", 0.92f), ("unison", 0.333f), ("detune", 0.57f), ("gain", 0.74f));
        Inst("synth", 0, "Band Motion",    ("wave", 0.333f), ("pulsewidth", 0.44f), ("attack", 0.28f), ("decay", 0.62f), ("sustain", 0.70f), ("release", 0.62f), ("filtype", 1f), ("cutoff", 0.58f), ("resonance", 0.50f), ("filenv", 0.74f), ("unison", 0.333f), ("detune", 0.60f), ("spread", 0.65f), ("gain", 0.74f));

        // ---- Nota Physical (kind 2) — modal percussion. Res1 Type: Beam 0 / Marimba .2 /
        //      String .4 / Membrane .6 / Plate .8 / Pipe 1. Tune/Ratio are neutral at 0.5.
        Inst("physical", 2, "Marimba",     ("malletvol", 0.85f), ("malletstiff", 0.45f), ("r1type", 0.2f), ("r1decay", 0.38f), ("r1material", 0.62f), ("r1bright", 0.55f), ("r1hit", 0.25f), ("volume", 0.8f));
        Inst("physical", 2, "Vibraphone",  ("malletvol", 0.8f),  ("malletstiff", 0.5f),  ("r1type", 0.2f), ("r1decay", 0.78f), ("r1material", 0.22f), ("r1bright", 0.6f),  ("r1hit", 0.3f),  ("volume", 0.78f));
        Inst("physical", 2, "Glass Bell",  ("malletvol", 0.8f),  ("malletstiff", 0.62f), ("r1type", 0.0f), ("r1decay", 0.82f), ("r1material", 0.2f),  ("r1bright", 0.72f), ("r1inharm", 0.22f), ("r1hit", 0.5f), ("volume", 0.75f));
        Inst("physical", 2, "Tubular",     ("malletvol", 0.8f),  ("malletstiff", 0.55f), ("r1type", 1.0f), ("r1decay", 0.8f),  ("r1material", 0.3f),  ("r1bright", 0.55f), ("r1hit", 0.2f),  ("volume", 0.72f));
        Inst("physical", 2, "Wood Block",  ("malletvol", 0.9f),  ("malletstiff", 0.7f),  ("r1type", 0.0f), ("r1decay", 0.14f), ("r1material", 0.82f), ("r1bright", 0.5f),  ("r1hit", 0.3f),  ("volume", 0.82f));
        Inst("physical", 2, "Membrane",    ("malletvol", 0.85f), ("malletstiff", 0.5f),  ("r1type", 0.6f), ("r1decay", 0.32f), ("r1material", 0.62f), ("r1bright", 0.5f),  ("r1inharm", 0.1f), ("r1hit", 0.4f), ("volume", 0.8f));

        // ---- Nota Aurora (kind 5) — table: Analog 0 / Pulse .33 / Formant .66 / Chroma 1
        Inst("aurora", 5, "Warm Pad",    ("table", 0f),    ("position", 0.30f), ("warp", 0.50f), ("unison", 0.60f), ("attack", 0.50f), ("decay", 0.40f), ("sustain", 0.85f), ("release", 0.60f), ("cutoff", 0.50f), ("resonance", 0.10f), ("gain", 0.80f));
        Inst("aurora", 5, "Glass Bells", ("table", 0.66f), ("position", 0.70f), ("warp", 0.55f), ("unison", 0.20f), ("attack", 0.02f), ("decay", 0.50f), ("sustain", 0.20f), ("release", 0.42f), ("cutoff", 0.80f), ("resonance", 0.16f), ("gain", 0.80f));
        Inst("aurora", 5, "Detuned Lead",("table", 0.33f), ("position", 0.40f), ("warp", 0.60f), ("unison", 0.90f), ("attack", 0.02f), ("decay", 0.30f), ("sustain", 0.75f), ("release", 0.25f), ("cutoff", 0.76f), ("resonance", 0.20f), ("gain", 0.78f));
        Inst("aurora", 5, "Motion Keys", ("table", 1f),    ("position", 0.50f), ("warp", 0.70f), ("unison", 0.50f), ("attack", 0.05f), ("decay", 0.42f), ("sustain", 0.60f), ("release", 0.40f), ("cutoff", 0.66f), ("resonance", 0.20f), ("gain", 0.80f));
        // v2: two oscillators + sub + FX + Env2/LFO modulation. Warp mode: Off 0 / Sync .25 / Bend .5 / PWM .75 / Fold 1.
        Inst("aurora", 5, "Glass Choir", ("table", 0.66f), ("position", 0.55f), ("osc2on", 1f), ("osc2table", 0.0f), ("osc2level", 0.55f), ("osc2oct", 0.667f), ("osc2detune", 0.56f),
                                         ("unison", 0.5f), ("unidetune", 0.4f), ("cutoff", 0.72f), ("resonance", 0.12f), ("fil1env", 0.62f),
                                         ("attack", 0.35f), ("decay", 0.5f), ("sustain", 0.8f), ("release", 0.6f), ("env2attack", 0.4f), ("env2sustain", 0.6f),
                                         ("mtx1_2", 0.78f), ("fxchorus", 0.4f), ("fxreverb", 0.4f), ("gain", 0.78f));
        Inst("aurora", 5, "Super Saw",   ("table", 0.0f), ("position", 0.9f), ("osc2on", 1f), ("osc2table", 0.0f), ("osc2position", 0.9f), ("osc2level", 0.8f), ("osc2detune", 0.62f),
                                         ("unison", 1f), ("unidetune", 0.7f), ("unispread", 0.9f), ("cutoff", 0.8f), ("resonance", 0.14f), ("fil1env", 0.55f),
                                         ("attack", 0.0f), ("decay", 0.4f), ("sustain", 0.85f), ("release", 0.28f), ("fxdrive", 0.2f), ("gain", 0.72f));
        Inst("aurora", 5, "Sub Bass",    ("table", 0.0f), ("position", 0.6f), ("osc1oct", 0.333f), ("sublevel", 0.85f), ("suboct", 0.5f),
                                         ("cutoff", 0.42f), ("resonance", 0.2f), ("fil1env", 0.72f), ("fil1slope", 1f),
                                         ("attack", 0.0f), ("decay", 0.3f), ("sustain", 0.6f), ("release", 0.18f), ("mono", 1f), ("gain", 0.85f));
        Inst("aurora", 5, "Fold Lead",   ("table", 0.33f), ("position", 0.5f), ("osc1warpmode", 1f), ("warp", 0.6f), ("cutoff", 0.82f), ("resonance", 0.22f),
                                         ("attack", 0.01f), ("decay", 0.3f), ("sustain", 0.7f), ("release", 0.22f), ("lfo1depth", 0.6f), ("mtx2_3", 0.35f), ("fxdrive", 0.3f), ("gain", 0.72f));

        // ---- Nota Volt (kind 6) — 25 patches across basses, leads, pads, keys and motion.
        //      Bipolar knobs (octave/semi/detune/env/lfo/pan/macro amount) are neutral at
        //      0.5; octave spans ±3 (.333 = −1 oct), semi ±12 (.5 + n/24), detune ±50 cents.
        //      Filter type: LP 0 / HP .33 / BP .66 / Notch 1 · slope 12 dB 0 / 24 dB 1 ·
        //      wave Saw 0 / Square .333 / Tri .667 / Sine 1 · LFO shape sin 0 / tri .333 /
        //      sqr .667 / S&H 1 · LFO sync free 0 then 1 bar … 1/64 at n/7 (1/4 = .4286,
        //      1/8 = .5714, 1/16 = .7143) · route F1 0 / F2 1 · noise dark 0 / pink .5 /
        //      white 1. Matrix cells are mtx{src}_{dst}: src 0 amp env · 1 filter env ·
        //      2 LFO 1 · 3 LFO 2 · 4 velocity · 5 key · 6 mod wheel; dst 0 pitch ·
        //      1 osc 2 pitch · 2 cutoff · 3 reso · 4 level · 5 pan. A macro's dest is
        //      n/11 over the twelve targets (3 = osc 2 level, 4 = cutoff, 8 = LFO 1 rate).
        Inst("volt", 6, "Fat Bass",   ("osc1octave", 0.333f), ("osc2octave", 0.333f), ("osc2detune", 0.58f), ("osc1level", 0.9f), ("osc2level", 0.8f),
                                      ("fil1freq", 0.34f), ("fil1reso", 0.26f), ("fil1env", 0.74f), ("fattack", 0.0f), ("fdecay", 0.28f), ("fsustain", 0.2f),
                                      ("attack", 0.0f), ("decay", 0.3f), ("sustain", 0.5f), ("release", 0.15f), ("amp1level", 0.9f), ("unison", 0.3f), ("velamp", 0.5f), ("volume", 0.85f));
        Inst("volt", 6, "Bright Lead",("osc2detune", 0.56f), ("fil1freq", 0.72f), ("fil1reso", 0.3f), ("fil1env", 0.55f), ("unison", 0.5f),
                                      ("vibrate", 0.45f), ("vibamt", 0.12f), ("attack", 0.02f), ("decay", 0.25f), ("sustain", 0.78f), ("release", 0.2f), ("amp1level", 0.82f), ("volume", 0.8f));
        Inst("volt", 6, "Warm Pad",   ("osc2detune", 0.55f), ("fil1freq", 0.5f), ("fil1reso", 0.1f), ("fil1env", 0.58f), ("unison", 0.6f),
                                      ("fattack", 0.5f), ("fdecay", 0.5f), ("fsustain", 0.6f), ("frelease", 0.6f),
                                      ("attack", 0.55f), ("decay", 0.4f), ("sustain", 0.85f), ("release", 0.65f), ("amp1level", 0.8f), ("volume", 0.8f));
        Inst("volt", 6, "Soft Pluck", ("osc1wave", 0.66f), ("osc2detune", 0.54f), ("fil1freq", 0.56f), ("fil1reso", 0.2f), ("fil1env", 0.8f),
                                      ("fattack", 0.0f), ("fdecay", 0.35f), ("fsustain", 0.0f), ("frelease", 0.2f),
                                      ("attack", 0.0f), ("decay", 0.4f), ("sustain", 0.0f), ("release", 0.25f), ("amp1level", 0.85f), ("unison", 0.2f), ("volume", 0.8f));
        Inst("volt", 6, "Split Filter",("osc1route", 0.0f), ("osc2route", 1.0f), ("osc2detune", 0.6f), ("fil1type", 0.0f), ("fil1freq", 0.45f), ("fil2type", 0.33f), ("fil2freq", 0.5f),
                                      ("amp1level", 0.8f), ("amp1pan", 0.32f), ("amp2level", 0.8f), ("amp2pan", 0.68f), ("unison", 0.3f), ("volume", 0.75f));
        Inst("volt", 6, "Sub Sine",   ("osc1wave", 1.0f), ("osc1octave", 0.333f), ("osc2level", 0.4f), ("fil1freq", 0.4f), ("fil1reso", 0.1f), ("fil1env", 0.4f),
                                      ("attack", 0.0f), ("decay", 0.3f), ("sustain", 0.7f), ("release", 0.2f), ("amp1level", 0.9f), ("unison", 0.0f), ("volume", 0.85f));
        // v4: tempo-synced LFO 1 → Cutoff via the mod matrix, mono + glide.
        Inst("volt", 6, "Wobble Bass",("osc1octave", 0.333f), ("osc2octave", 0.333f), ("osc2detune", 0.58f), ("osc1level", 0.9f), ("osc2level", 0.85f),
                                      ("fil1freq", 0.32f), ("fil1reso", 0.42f), ("fil1slope", 1.0f), ("mono", 1.0f), ("glide", 0.28f),
                                      ("lfo1sync", 0.571f), ("lfo1shape", 0.0f), ("lfo1depth", 0.8f), ("mtx2_2", 0.9f),
                                      ("attack", 0.0f), ("decay", 0.3f), ("sustain", 0.6f), ("release", 0.18f), ("amp1level", 0.9f), ("volume", 0.85f));
        // v4: slow LFO 2 → Pan auto-panning warm keys.
        Inst("volt", 6, "Drifting Keys",("osc1wave", 0.0f), ("osc2wave", 0.33f), ("osc2detune", 0.55f), ("fil1freq", 0.56f), ("fil1reso", 0.16f), ("fil1env", 0.5f),
                                      ("lfo2rate", 0.18f), ("lfo2depth", 0.7f), ("mtx3_5", 0.85f), ("fattack", 0.2f), ("fdecay", 0.5f), ("fsustain", 0.4f),
                                      ("attack", 0.1f), ("decay", 0.4f), ("sustain", 0.7f), ("release", 0.45f), ("amp1level", 0.82f), ("unison", 0.35f), ("volume", 0.8f));

        // v5: the rest of the library — basses, leads, pads, keys and motion patches.
        // Basses
        Inst("volt", 6, "Acid Bass",    ("osc1wave", 0.0f), ("osc1octave", 0.333f), ("osc2level", 0.0f), ("osc1level", 0.95f),
                                      ("fil1freq", 0.30f), ("fil1reso", 0.72f), ("fil1env", 0.90f), ("fil1slope", 1.0f),
                                      ("fattack", 0.0f), ("fdecay", 0.26f), ("fsustain", 0.04f), ("frelease", 0.16f),
                                      ("attack", 0.0f), ("decay", 0.3f), ("sustain", 0.45f), ("release", 0.14f),
                                      ("mono", 1.0f), ("glide", 0.2f), ("amp1level", 0.9f), ("velfilter", 0.6f), ("volume", 0.85f));
        Inst("volt", 6, "Reese Bass",   ("osc1octave", 0.333f), ("osc2octave", 0.333f), ("osc2detune", 0.66f), ("osc1level", 0.9f), ("osc2level", 0.9f),
                                      ("fil1freq", 0.36f), ("fil1reso", 0.18f), ("fil1env", 0.60f), ("fil1slope", 1.0f), ("unison", 0.55f),
                                      ("attack", 0.0f), ("decay", 0.4f), ("sustain", 0.8f), ("release", 0.2f), ("amp1level", 0.9f),
                                      ("mac0val", 0.5f), ("mac0dest", 0.2727f), ("mac0amt", 0.78f), ("volume", 0.82f));
        Inst("volt", 6, "Growl Bass",   ("osc1octave", 0.333f), ("osc2octave", 0.333f), ("osc2detune", 0.60f), ("osc2wave", 0.333f),
                                      ("fil1freq", 0.30f), ("fil1reso", 0.50f), ("fil1env", 0.72f), ("fil1slope", 1.0f),
                                      ("lfo1shape", 0.333f), ("lfo1sync", 0.5714f), ("lfo1depth", 0.75f), ("mtx2_2", 0.78f),
                                      ("attack", 0.0f), ("decay", 0.35f), ("sustain", 0.6f), ("release", 0.18f),
                                      ("mono", 1.0f), ("amp1level", 0.88f), ("volume", 0.82f));
        // Leads
        Inst("volt", 6, "Super Saw",    ("osc2detune", 0.64f), ("osc1level", 0.85f), ("osc2level", 0.85f), ("unison", 0.9f),
                                      ("fil1freq", 0.80f), ("fil1reso", 0.14f), ("fil1env", 0.56f),
                                      ("attack", 0.08f), ("decay", 0.5f), ("sustain", 0.88f), ("release", 0.4f),
                                      ("amp1level", 0.8f), ("volume", 0.72f));
        Inst("volt", 6, "Solo Lead",    ("osc2detune", 0.55f), ("fil1freq", 0.70f), ("fil1reso", 0.28f), ("fil1env", 0.60f),
                                      ("attack", 0.02f), ("decay", 0.4f), ("sustain", 0.82f), ("release", 0.25f),
                                      ("mono", 1.0f), ("glide", 0.18f), ("vibrate", 0.45f), ("vibamt", 0.30f), ("vibwheel", 1.0f),
                                      ("bendrange", 0.3636f), ("amp1level", 0.85f), ("volume", 0.8f));
        Inst("volt", 6, "Hard Lead",    ("osc1wave", 0.333f), ("osc2wave", 0.333f), ("osc2detune", 0.58f), ("osc2semi", 0.5417f),
                                      ("fil1freq", 0.66f), ("fil1reso", 0.42f), ("fil1env", 0.66f), ("fil1slope", 1.0f),
                                      ("attack", 0.0f), ("decay", 0.35f), ("sustain", 0.72f), ("release", 0.2f),
                                      ("mono", 1.0f), ("velfilter", 0.5f), ("amp1level", 0.85f), ("volume", 0.78f));
        // Pads
        Inst("volt", 6, "String Machine",("osc2detune", 0.60f), ("osc1level", 0.85f), ("osc2level", 0.85f), ("unison", 0.8f),
                                      ("fil1freq", 0.60f), ("fil1reso", 0.12f), ("fil1env", 0.58f),
                                      ("fattack", 0.35f), ("fdecay", 0.55f), ("fsustain", 0.55f), ("frelease", 0.6f),
                                      ("attack", 0.42f), ("decay", 0.5f), ("sustain", 0.9f), ("release", 0.7f),
                                      ("lfo2rate", 0.16f), ("lfo2depth", 0.5f), ("mtx3_5", 0.72f), ("amp1level", 0.8f), ("volume", 0.72f));
        Inst("volt", 6, "Choir Pad",    ("osc1wave", 0.667f), ("osc2wave", 0.667f), ("osc2detune", 0.56f), ("noise", 0.06f), ("noisecolor", 1.0f),
                                      ("fil1freq", 0.54f), ("fil1reso", 0.10f), ("fil1env", 0.56f), ("unison", 0.6f),
                                      ("fattack", 0.45f), ("fdecay", 0.5f), ("fsustain", 0.6f), ("frelease", 0.6f),
                                      ("attack", 0.55f), ("decay", 0.5f), ("sustain", 0.92f), ("release", 0.78f),
                                      ("amp1level", 0.8f), ("volume", 0.74f));
        Inst("volt", 6, "Dark Drone",   ("osc1octave", 0.333f), ("osc2octave", 0.333f), ("osc2detune", 0.70f), ("unison", 0.9f),
                                      ("fil1freq", 0.30f), ("fil1reso", 0.24f), ("fil1env", 0.56f), ("fil1slope", 1.0f),
                                      ("fattack", 0.6f), ("fdecay", 0.7f), ("fsustain", 0.7f), ("frelease", 0.8f),
                                      ("attack", 0.68f), ("decay", 0.7f), ("sustain", 1.0f), ("release", 0.85f),
                                      ("lfo1rate", 0.10f), ("lfo1depth", 0.6f), ("mtx2_2", 0.68f), ("amp1level", 0.78f), ("volume", 0.7f));
        Inst("volt", 6, "Glass Pad",    ("osc1wave", 1.0f), ("osc2wave", 0.667f), ("osc2octave", 0.667f), ("osc2detune", 0.54f), ("osc2level", 0.45f),
                                      ("fil1freq", 0.86f), ("fil1reso", 0.08f), ("fil1env", 0.5f), ("unison", 0.5f),
                                      ("attack", 0.45f), ("decay", 0.6f), ("sustain", 0.75f), ("release", 0.82f),
                                      ("mac0val", 0.4f), ("mac0dest", 0.3636f), ("mac0amt", 0.72f), ("amp1level", 0.8f), ("volume", 0.74f));
        // Keys and plucks
        Inst("volt", 6, "Electric Keys",("osc1wave", 0.667f), ("osc2wave", 1.0f), ("osc2detune", 0.53f), ("osc2level", 0.5f),
                                      ("fil1freq", 0.66f), ("fil1reso", 0.12f), ("fil1env", 0.66f),
                                      ("fattack", 0.0f), ("fdecay", 0.45f), ("fsustain", 0.1f), ("frelease", 0.3f),
                                      ("attack", 0.0f), ("decay", 0.62f), ("sustain", 0.28f), ("release", 0.45f),
                                      ("velamp", 0.7f), ("amp1level", 0.85f), ("volume", 0.8f));
        Inst("volt", 6, "Bell Tone",    ("osc1wave", 1.0f), ("osc2wave", 1.0f), ("osc2semi", 0.7917f), ("osc2level", 0.55f), ("osc1octave", 0.667f),
                                      ("fil1freq", 0.90f), ("fil1reso", 0.10f), ("fil1env", 0.5f),
                                      ("attack", 0.0f), ("decay", 0.80f), ("sustain", 0.0f), ("release", 0.72f),
                                      ("velamp", 0.6f), ("amp1level", 0.82f), ("volume", 0.76f));
        Inst("volt", 6, "Clav Stab",    ("osc1wave", 0.333f), ("osc2wave", 0.333f), ("osc2detune", 0.54f), ("osc1phase", 0.25f), ("osc2phase", 0.25f),
                                      ("fil1freq", 0.58f), ("fil1reso", 0.38f), ("fil1env", 0.82f), ("fil1key", 0.5f),
                                      ("fattack", 0.0f), ("fdecay", 0.22f), ("fsustain", 0.0f), ("frelease", 0.14f),
                                      ("attack", 0.0f), ("decay", 0.3f), ("sustain", 0.0f), ("release", 0.18f),
                                      ("velfilter", 0.6f), ("amp1level", 0.88f), ("volume", 0.82f));
        // Motion and noise
        Inst("volt", 6, "Sample & Hold",("osc1wave", 0.333f), ("osc2detune", 0.57f),
                                      ("fil1freq", 0.48f), ("fil1reso", 0.44f), ("fil1env", 0.5f),
                                      ("lfo1shape", 1.0f), ("lfo1sync", 0.7143f), ("lfo1depth", 0.9f), ("mtx2_2", 0.88f),
                                      ("attack", 0.0f), ("decay", 0.4f), ("sustain", 0.75f), ("release", 0.3f),
                                      ("mac0val", 0.5f), ("mac0dest", 0.7273f), ("mac0amt", 0.70f), ("amp1level", 0.82f), ("volume", 0.78f));
        Inst("volt", 6, "Noise Sweep",  ("osc1level", 0.0f), ("osc2level", 0.0f), ("noise", 0.95f), ("noisecolor", 0.85f),
                                      ("fil1type", 0.667f), ("fil1freq", 0.35f), ("fil1reso", 0.62f), ("fil1env", 0.95f), ("fil1slope", 1.0f),
                                      ("fattack", 0.55f), ("fdecay", 0.6f), ("fsustain", 0.8f), ("frelease", 0.6f),
                                      ("attack", 0.4f), ("decay", 0.5f), ("sustain", 0.9f), ("release", 0.6f),
                                      ("amp1level", 0.8f), ("volume", 0.7f));
        Inst("volt", 6, "Siren",        ("osc1wave", 0.667f), ("osc2level", 0.0f),
                                      ("fil1freq", 0.70f), ("fil1reso", 0.20f),
                                      ("lfo1shape", 0.333f), ("lfo1rate", 0.18f), ("lfo1depth", 0.8f), ("mtx2_0", 0.62f),
                                      ("attack", 0.05f), ("decay", 0.4f), ("sustain", 0.9f), ("release", 0.25f),
                                      ("mono", 1.0f), ("amp1level", 0.82f), ("volume", 0.76f));
        // Both filters in series — Filter 1 spills into Filter 2, which is what "To F2" is for.
        Inst("volt", 6, "Serial Filters",("osc1route", 0.0f), ("osc2route", 0.0f), ("noiseroute", 0.0f), ("osc2detune", 0.57f),
                                      ("fil1type", 0.0f), ("fil1freq", 0.58f), ("fil1reso", 0.30f), ("fil1env", 0.72f), ("fil1tof2", 1.0f),
                                      ("fil2type", 0.333f), ("fil2freq", 0.26f), ("fil2reso", 0.24f),
                                      ("amp1level", 0.0f), ("amp2level", 0.9f),
                                      ("attack", 0.0f), ("decay", 0.4f), ("sustain", 0.7f), ("release", 0.25f), ("volume", 0.82f));

        // ---- Nota Bass (kind 7) — bass synth. Osc Shape morphs sine 0 → tri → saw → pulse 1.
        //      Bipolar knobs (octave/semi/env/lfo-amt/pitch) are neutral at 0.5. Filter type:
        //      LP 0 / HP .33 / BP .66 / Notch 1; slope 12 = 0 / 24 = 1; mono Poly 0 / Mono 1.
        Inst("bass", 7, "Sub Rumble",  ("oscshape", 0.0f), ("osclevel", 0.5f), ("subwave", 0.0f), ("sublevel", 0.95f),
                                       ("filfreq", 0.3f), ("filreso", 0.1f), ("filenv", 0.55f), ("fildrive", 0.1f),
                                       ("attack", 0.0f), ("decay", 0.5f), ("sustain", 0.9f), ("release", 0.2f), ("drive", 0.1f), ("volume", 0.9f), ("mono", 1.0f));
        Inst("bass", 7, "Reese Bass",  ("oscshape", 0.7f), ("osclevel", 0.9f), ("sublevel", 0.4f),
                                       ("filslope", 1.0f), ("filfreq", 0.45f), ("filreso", 0.4f), ("fildrive", 0.3f), ("fillfo", 0.62f), ("lforate", 0.3f),
                                       ("unison", 0.7f), ("drive", 0.3f), ("volume", 0.82f), ("mono", 0.0f));
        Inst("bass", 7, "Acid 303",    ("oscshape", 0.66f), ("osclevel", 0.9f), ("sublevel", 0.2f),
                                       ("filslope", 1.0f), ("filfreq", 0.32f), ("filreso", 0.75f), ("fildrive", 0.4f), ("filenv", 0.86f), ("filkey", 0.4f),
                                       ("fdecay", 0.25f), ("fsustain", 0.1f), ("attack", 0.0f), ("decay", 0.35f), ("sustain", 0.6f), ("release", 0.12f),
                                       ("glide", 0.2f), ("drive", 0.4f), ("volume", 0.82f), ("mono", 1.0f));
        Inst("bass", 7, "Growl Bass",  ("oscshape", 0.88f), ("oscpw", 0.35f), ("osclevel", 0.85f), ("sublevel", 0.5f),
                                       ("filfreq", 0.4f), ("filreso", 0.5f), ("fildrive", 0.5f), ("fillfo", 0.72f), ("lforate", 0.45f), ("lfowave", 0.25f),
                                       ("drive", 0.55f), ("volume", 0.8f), ("mono", 1.0f));
        Inst("bass", 7, "Pluck Bass",  ("oscshape", 0.6f), ("osclevel", 0.85f), ("sublevel", 0.55f),
                                       ("filfreq", 0.5f), ("filreso", 0.3f), ("filenv", 0.8f), ("fattack", 0.0f), ("fdecay", 0.3f), ("fsustain", 0.0f), ("frelease", 0.2f),
                                       ("attack", 0.0f), ("decay", 0.35f), ("sustain", 0.0f), ("release", 0.18f), ("drive", 0.2f), ("volume", 0.82f), ("mono", 1.0f));
        Inst("bass", 7, "Wobble Bass", ("oscshape", 0.7f), ("osclevel", 0.85f), ("sublevel", 0.4f),
                                       ("filslope", 1.0f), ("filfreq", 0.35f), ("filreso", 0.55f), ("fillfo", 0.85f), ("lforate", 0.6f), ("lfowave", 0.0f),
                                       ("drive", 0.35f), ("volume", 0.8f), ("mono", 1.0f));

        // ---- Nota Pendulum (kind 8) — generative keys. Sync Free 0 / Sync 1; Division 0 1/1..
        //      1 1/16; Rate 0.5 = stop, <0.5 reverse; Motion 0 Linear/.33 Pendulum/.67 Ease/
        //      1 Bounce; Quantize 0 Off/.5 1/16/1 1/8; Chord Sort Up 0 / Down 1; Scale 0 Off.
        Inst("pendulum", 8, "Warm Cascade",  ("balls", 0.4f), ("rate", 0.72f), ("sync", 1f), ("division", 0.25f), ("motion", 0.33f), ("quantize", 0.5f), ("spread", 0.25f), ("notelen", 0.4f), ("tone", 0.42f), ("attack", 0.14f), ("decay", 0.45f), ("release", 0.45f), ("detune", 0.3f), ("volume", 0.8f), ("panspread", 0.4f), ("humanize", 0.2f));
        Inst("pendulum", 8, "Slow Bloom",    ("balls", 0.2f), ("rate", 0.62f), ("sync", 0f), ("freerate", 0.7f), ("motion", 0.67f), ("quantize", 0f), ("spread", 0.4f), ("notelen", 0.7f), ("tone", 0.35f), ("attack", 0.3f), ("decay", 0.6f), ("release", 0.7f), ("detune", 0.45f), ("volume", 0.78f), ("wave", 0.5f), ("bright", 0.3f), ("panspread", 0.55f), ("humanize", 0.35f));
        Inst("pendulum", 8, "Fast Sparkle",  ("balls", 1.0f), ("rate", 0.85f), ("sync", 1f), ("division", 0.5f), ("motion", 0.33f), ("quantize", 0.5f), ("spread", 0.3f), ("notelen", 0.25f), ("tone", 0.6f), ("attack", 0.05f), ("decay", 0.3f), ("release", 0.3f), ("detune", 0.25f), ("volume", 0.8f), ("wave", 0.25f), ("bright", 0.7f), ("panspread", 0.5f));
        Inst("pendulum", 8, "Reverse Drift", ("balls", 0.6f), ("rate", 0.28f), ("sync", 1f), ("division", 0.25f), ("motion", 0.67f), ("quantize", 0.5f), ("spread", 0.6f), ("notelen", 0.45f), ("tone", 0.4f), ("attack", 0.18f), ("decay", 0.5f), ("release", 0.5f), ("detune", 0.35f), ("volume", 0.8f), ("wave", 1f), ("bright", 0.6f), ("fm", 0.4f), ("humanize", 0.3f));
        Inst("pendulum", 8, "Down Runs",     ("balls", 0.4f), ("rate", 0.75f), ("sync", 1f), ("division", 0.25f), ("motion", 0f), ("quantize", 1f), ("chordsort", 1f), ("spread", 0.2f), ("notelen", 0.4f), ("tone", 0.48f), ("attack", 0.1f), ("decay", 0.4f), ("release", 0.4f), ("detune", 0.3f), ("volume", 0.8f), ("panspread", 0.3f));
        Inst("pendulum", 8, "Glass Bounce",  ("balls", 0.8f), ("rate", 0.8f), ("sync", 1f), ("division", 0.5f), ("motion", 1f), ("quantize", 0.5f), ("spread", 0.35f), ("notelen", 0.2f), ("tone", 0.7f), ("attack", 0.02f), ("decay", 0.35f), ("release", 0.35f), ("detune", 0.2f), ("volume", 0.8f), ("wave", 0.25f), ("bright", 0.8f), ("fm", 0.15f), ("panspread", 0.6f), ("humanize", 0.25f));

        // ---- Nota Operator (kind 9) — 4-op FM, 25 patches across keys, bells, basses,
        //      leads, pads and percussion. Coarse = ratio index / 15 (0 ×0.5 · .0667 ×1 ·
        //      .1333 ×2 · .2 ×3 · .2667 ×4 · .3333 ×5 · .4 ×6 · .4667 ×7 · .5333 ×8 ·
        //      .9333 ×14); Fine is neutral at 0.5. Algo 0..1 = the 11 topologies
        //      (index = round(v*10)); Wave 0 Sine / .33 Tri / .67 Saw / 1 Sqr. A modulator's
        //      Level is its FM index, a carrier's is its amplitude.
        // Keys
        Inst("operator", 9, "E-Piano",      ("algo", 0f),    ("ccoarse", 0.0667f), ("clevel", 0.4f), ("cdec", 0.5f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.55f), ("dsus", 0.55f), ("drel", 0.35f),
                                             ("filfreq", 0.85f), ("keylevel", 0.25f), ("volume", 0.8f));
        Inst("operator", 9, "Tine Keys",    ("algo", 0.2f),  ("bcoarse", 0.0667f), ("blevel", 0.5f), ("bdec", 0.45f), ("bsus", 0.1f),
                                             ("ccoarse", 0.9333f), ("clevel", 0.12f), ("cdec", 0.12f), ("csus", 0f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.6f), ("dsus", 0.35f), ("drel", 0.4f),
                                             ("veltofm", 0.6f), ("keylevel", 0.3f), ("filfreq", 0.88f), ("volume", 0.8f));
        Inst("operator", 9, "Wurly",        ("algo", 0f),    ("ccoarse", 0.2f), ("clevel", 0.5f), ("cdec", 0.28f), ("csus", 0.05f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.55f), ("dsus", 0.3f), ("drel", 0.3f),
                                             ("feedback", 0.12f), ("veltofm", 0.7f), ("filfreq", 0.72f), ("volume", 0.82f));
        Inst("operator", 9, "Clav",         ("algo", 0f),    ("ccoarse", 0.133f), ("clevel", 0.65f), ("cdec", 0.35f), ("csus", 0.1f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.3f), ("dsus", 0.2f), ("drel", 0.2f),
                                             ("feedback", 0.15f), ("filfreq", 0.8f), ("filkeytrk", 0.5f), ("volume", 0.82f));
        Inst("operator", 9, "Harpsichord",  ("algo", 0f),    ("ccoarse", 0.1333f), ("clevel", 0.55f), ("cdec", 0.25f), ("csus", 0.05f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.35f), ("dsus", 0f), ("drel", 0.18f),
                                             ("feedback", 0.1f), ("veltolevel", 0.3f), ("filfreq", 0.9f), ("volume", 0.8f));
        // Bells and mallets
        Inst("operator", 9, "FM Bell",      ("algo", 0.4f),  ("ccoarse", 0.2f), ("clevel", 0.7f), ("cdec", 0.3f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.25f), ("dsus", 0f), ("drel", 0.5f),
                                             ("feedback", 0.2f), ("filfreq", 1f), ("volume", 0.78f));
        Inst("operator", 9, "Tubular Bell", ("algo", 0.3f),  ("acoarse", 0.2667f), ("alevel", 0.3f), ("adec", 0.4f), ("asus", 0f),
                                             ("bcoarse", 0.4667f), ("blevel", 0.5f), ("bdec", 0.35f), ("bsus", 0f),
                                             ("ccoarse", 0.1333f), ("clevel", 0.4f), ("cdec", 0.85f), ("csus", 0f), ("crel", 0.7f),
                                             ("dcoarse", 0.0667f), ("dlevel", 0.9f), ("ddec", 0.9f), ("dsus", 0f), ("drel", 0.75f),
                                             ("filfreq", 1f), ("volume", 0.74f));
        Inst("operator", 9, "Music Box",    ("algo", 0.5f),  ("acoarse", 0.9333f), ("alevel", 0.25f), ("adec", 0.12f), ("asus", 0f),
                                             ("bcoarse", 0.0667f), ("blevel", 0.55f), ("bdec", 0.6f), ("bsus", 0f), ("brel", 0.5f),
                                             ("ccoarse", 0.2f), ("clevel", 0.2f), ("cdec", 0.15f), ("csus", 0f),
                                             ("dcoarse", 0.1333f), ("dlevel", 0.7f), ("ddec", 0.65f), ("dsus", 0f), ("drel", 0.55f),
                                             ("filfreq", 1f), ("volume", 0.72f));
        Inst("operator", 9, "Marimba",      ("algo", 0f),    ("ccoarse", 0.2667f), ("clevel", 0.45f), ("cdec", 0.14f), ("csus", 0f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.42f), ("dsus", 0f), ("drel", 0.3f),
                                             ("veltofm", 0.5f), ("filfreq", 0.8f), ("volume", 0.82f));
        Inst("operator", 9, "Kalimba",      ("algo", 0f),    ("ccoarse", 0.4f), ("clevel", 0.35f), ("cdec", 0.1f), ("csus", 0f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.38f), ("dsus", 0f), ("drel", 0.3f),
                                             ("keylevel", 0.4f), ("filfreq", 0.85f), ("volume", 0.8f));
        // Basses
        Inst("operator", 9, "FM Bass",      ("algo", 0f),    ("ccoarse", 0.0667f), ("clevel", 0.6f), ("cdec", 0.3f), ("csus", 0.2f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.4f), ("dsus", 0.7f), ("drel", 0.2f),
                                             ("mono", 1f), ("glide", 0.35f), ("filfreq", 0.5f), ("filreso", 0.2f), ("volume", 0.85f));
        Inst("operator", 9, "Slap Bass",    ("algo", 0.2f),  ("bcoarse", 0.0667f), ("blevel", 0.55f), ("bdec", 0.3f), ("bsus", 0.15f),
                                             ("ccoarse", 0.4667f), ("clevel", 0.2f), ("cdec", 0.1f), ("csus", 0f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.4f), ("dsus", 0.4f), ("drel", 0.2f),
                                             ("mono", 1f), ("veltofm", 0.8f), ("filfreq", 0.6f), ("filreso", 0.2f), ("volume", 0.85f));
        Inst("operator", 9, "Sub Sine",     ("algo", 1f),    ("alevel", 0f), ("blevel", 0f), ("clevel", 0f),
                                             ("dcoarse", 0f), ("dlevel", 1f), ("ddec", 0.5f), ("dsus", 0.85f), ("drel", 0.25f),
                                             ("mono", 1f), ("glide", 0.2f), ("filfreq", 0.4f), ("volume", 0.88f));
        Inst("operator", 9, "Growl Bass",   ("algo", 0.4f),  ("acoarse", 0.0667f), ("alevel", 0.35f), ("adec", 0.4f), ("asus", 0.3f),
                                             ("bcoarse", 0.1333f), ("blevel", 0.3f), ("bdec", 0.35f), ("bsus", 0.2f),
                                             ("ccoarse", 0.2f), ("clevel", 0.25f), ("cdec", 0.3f), ("csus", 0.15f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("ddec", 0.45f), ("dsus", 0.6f), ("drel", 0.25f),
                                             ("feedback", 0.3f), ("mono", 1f), ("filfreq", 0.5f), ("filreso", 0.3f), ("volume", 0.84f));
        // Leads
        Inst("operator", 9, "Bright Lead",  ("algo", 0.3f),  ("bcoarse", 0.133f), ("blevel", 0.5f), ("ccoarse", 0.0667f), ("clevel", 0.55f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("dsus", 0.8f), ("drel", 0.25f),
                                             ("veltofm", 0.8f), ("feedback", 0.35f), ("filfreq", 0.9f), ("volume", 0.78f));
        Inst("operator", 9, "Reed Lead",    ("algo", 0f),    ("ccoarse", 0.0667f), ("clevel", 0.5f), ("catk", 0.12f), ("cdec", 0.4f), ("csus", 0.5f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("datk", 0.06f), ("dsus", 0.85f), ("drel", 0.25f),
                                             ("mono", 1f), ("glide", 0.12f), ("veltofm", 0.5f), ("bendrange", 0.0909f),
                                             ("filfreq", 0.78f), ("volume", 0.8f));
        Inst("operator", 9, "Metal Lead",   ("algo", 0.4f),  ("acoarse", 0.5333f), ("alevel", 0.22f), ("adec", 0.5f), ("asus", 0.6f),
                                             ("bcoarse", 0.2f), ("blevel", 0.3f), ("bdec", 0.45f), ("bsus", 0.5f),
                                             ("ccoarse", 0.1333f), ("clevel", 0.35f), ("cdec", 0.45f), ("csus", 0.6f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("dsus", 0.8f), ("drel", 0.25f),
                                             ("feedback", 0.4f), ("mono", 1f), ("bendrange", 0.3636f), ("filfreq", 0.85f), ("volume", 0.76f));
        Inst("operator", 9, "Whistle",      ("algo", 1f),    ("alevel", 0f), ("blevel", 0f), ("clevel", 0f),
                                             ("dcoarse", 0.1333f), ("dlevel", 1f), ("datk", 0.25f), ("ddec", 0.5f), ("dsus", 0.9f), ("drel", 0.3f),
                                             ("mono", 1f), ("glide", 0.15f), ("filfreq", 0.95f), ("volume", 0.74f));
        // Pads and strings
        Inst("operator", 9, "Glass Pad",    ("algo", 1f),    ("acoarse", 0.2f), ("alevel", 0.4f), ("bcoarse", 0.133f), ("blevel", 0.4f),
                                             ("ccoarse", 0.0667f), ("clevel", 0.6f), ("dcoarse", 0.0667f), ("dlevel", 0.7f),
                                             ("aatk", 0.4f), ("batk", 0.4f), ("catk", 0.35f), ("datk", 0.4f), ("dsus", 0.8f), ("drel", 0.6f),
                                             ("filfreq", 0.75f), ("volume", 0.75f));
        Inst("operator", 9, "Air Pad",      ("algo", 0.9f),  ("acoarse", 0.4667f), ("alevel", 0.18f), ("aatk", 0.5f), ("adec", 0.6f), ("asus", 0.5f), ("arel", 0.7f),
                                             ("bcoarse", 0.0667f), ("blevel", 0.5f), ("batk", 0.55f), ("bsus", 0.85f), ("brel", 0.75f),
                                             ("ccoarse", 0.1333f), ("clevel", 0.4f), ("catk", 0.5f), ("csus", 0.8f), ("crel", 0.72f),
                                             ("dcoarse", 0.0667f), ("dlevel", 0.7f), ("datk", 0.6f), ("dsus", 0.9f), ("drel", 0.8f),
                                             ("filfreq", 0.8f), ("filkeytrk", 0.5f), ("volume", 0.72f));
        Inst("operator", 9, "Choir Pad",    ("algo", 0.6f),  ("acoarse", 0.0667f), ("alevel", 0.2f), ("aatk", 0.45f), ("asus", 0.6f), ("arel", 0.6f),
                                             ("bcoarse", 0.0667f), ("bfine", 0.56f), ("blevel", 0.5f), ("batk", 0.5f), ("bsus", 0.85f), ("brel", 0.7f),
                                             ("ccoarse", 0.1333f), ("clevel", 0.35f), ("catk", 0.5f), ("csus", 0.8f), ("crel", 0.7f),
                                             ("dcoarse", 0.0667f), ("dfine", 0.45f), ("dlevel", 0.8f), ("datk", 0.5f), ("dsus", 0.9f), ("drel", 0.75f),
                                             ("filfreq", 0.7f), ("volume", 0.74f));
        Inst("operator", 9, "Warm Strings", ("algo", 0.5f),  ("acoarse", 0.0667f), ("alevel", 0.3f), ("aatk", 0.35f), ("asus", 0.6f), ("arel", 0.6f),
                                             ("bcoarse", 0.0667f), ("bfine", 0.54f), ("blevel", 0.7f), ("batk", 0.4f), ("bsus", 0.9f), ("brel", 0.7f),
                                             ("ccoarse", 0.0667f), ("clevel", 0.28f), ("catk", 0.35f), ("csus", 0.6f), ("crel", 0.6f),
                                             ("dcoarse", 0.0667f), ("dfine", 0.46f), ("dlevel", 0.7f), ("datk", 0.42f), ("dsus", 0.9f), ("drel", 0.72f),
                                             ("filfreq", 0.66f), ("volume", 0.72f));
        // Brass, organ, percussion
        Inst("operator", 9, "Brass Section",("algo", 0f),    ("ccoarse", 0.0667f), ("clevel", 0.6f), ("catk", 0.2f), ("cdec", 0.4f), ("csus", 0.55f),
                                             ("dcoarse", 0.0667f), ("dlevel", 1f), ("datk", 0.14f), ("dsus", 0.85f), ("drel", 0.3f),
                                             ("veltofm", 0.85f), ("filfreq", 0.7f), ("volume", 0.8f));
        Inst("operator", 9, "Drawbar Organ",("algo", 1f),    ("acoarse", 0.0667f), ("alevel", 0.5f), ("aatk", 0f), ("adec", 0f), ("asus", 1f), ("arel", 0.12f),
                                             ("bcoarse", 0.1333f), ("blevel", 0.35f), ("batk", 0f), ("bdec", 0f), ("bsus", 1f), ("brel", 0.12f),
                                             ("ccoarse", 0.2f), ("clevel", 0.22f), ("catk", 0f), ("cdec", 0f), ("csus", 1f), ("crel", 0.12f),
                                             ("dcoarse", 0.2667f), ("dlevel", 0.6f), ("datk", 0f), ("ddec", 0f), ("dsus", 1f), ("drel", 0.12f),
                                             ("veltolevel", 0.2f), ("filfreq", 0.8f), ("volume", 0.76f));
        Inst("operator", 9, "Log Drum",     ("algo", 0f),    ("ccoarse", 0.1333f), ("clevel", 0.6f), ("cdec", 0.08f), ("csus", 0f),
                                             ("dcoarse", 0f), ("dlevel", 1f), ("ddec", 0.3f), ("dsus", 0f), ("drel", 0.22f),
                                             ("veltofm", 0.6f), ("filfreq", 0.55f), ("volume", 0.85f));

        // ---- Nota Grain (kind 10) — granular. Scan Mode 0 Scan/.5 Freeze/1 Key; Coarse 0.5 = 0 st
        //      (±24); presets shape the built-in default sample (drop your own to replace).
        Inst("grain", 10, "Frozen Choir", ("scanmode", 0.5f), ("position", 0.3f), ("spray", 0.2f), ("grainsize", 0.5f), ("density", 0.8f), ("spread", 0.6f), ("posrand", 0.15f), ("attack", 0.3f), ("release", 0.6f), ("filfreq", 0.9f), ("volume", 0.8f));
        Inst("grain", 10, "Drift Cloud",  ("scanmode", 0f), ("scan", 0.6f), ("grainsize", 0.6f), ("density", 0.7f), ("spread", 0.7f), ("posrand", 0.3f), ("panrand", 0.5f), ("attack", 0.2f), ("release", 0.7f), ("volume", 0.78f));
        Inst("grain", 10, "Glitch Spray", ("scanmode", 0.5f), ("grainsize", 0.15f), ("density", 0.9f), ("spray", 0.6f), ("posrand", 0.5f), ("pitchrand", 0.3f), ("attack", 0.02f), ("release", 0.2f), ("volume", 0.8f));
        Inst("grain", 10, "Key Scan",     ("scanmode", 1f), ("grainsize", 0.4f), ("density", 0.6f), ("spread", 0.4f), ("attack", 0.05f), ("release", 0.4f), ("volume", 0.82f));
        Inst("grain", 10, "Shimmer",      ("scanmode", 0.5f), ("coarse", 0.75f), ("grainsize", 0.5f), ("density", 0.8f), ("pitchrand", 0.1f), ("spread", 0.6f), ("filfreq", 1f), ("release", 0.6f), ("volume", 0.76f));
        Inst("grain", 10, "Sub Grain",    ("scanmode", 0.5f), ("coarse", 0.25f), ("grainsize", 0.6f), ("density", 0.7f), ("filfreq", 0.4f), ("filreso", 0.2f), ("volume", 0.85f));

        // ---- Nota Flux (kind 11) — vector-morph synth. Vector (X,Y) blends WARM/GLASS/
        //      MOOG/GRAIN; target: 0 Filter, 1 Pitch, 2 Space, 3 Vector (=1.0).
        Inst("flux", 11, "Warm Drift",     ("vecx", 0.34f), ("vecy", 0.28f), ("age", 0.30f), ("motion", 0.45f), ("motrate", 0.4f), ("filter", 0.62f), ("env", 0.55f), ("space", 0.40f), ("listen", 0.72f), ("target", 1f));
        Inst("flux", 11, "Glass Keys",     ("vecx", 0.85f), ("vecy", 0.15f), ("age", 0.10f), ("motion", 0.20f), ("filter", 0.72f), ("env", 0.82f), ("space", 0.30f), ("listen", 0.4f), ("target", 0f));
        Inst("flux", 11, "Moog Bass",      ("vecx", 0.05f), ("vecy", 0.90f), ("age", 0.35f), ("motion", 0.10f), ("filter", 0.45f), ("env", 0.70f), ("space", 0.15f), ("listen", 0.3f), ("target", 0f));
        Inst("flux", 11, "Grain Pad",      ("vecx", 0.80f), ("vecy", 0.85f), ("age", 0.60f), ("motion", 0.50f), ("motrate", 0.2f), ("filter", 0.50f), ("env", 0.35f), ("space", 0.55f), ("listen", 0.5f), ("target", 3f / 3f));
        Inst("flux", 11, "Reactive Vector",("vecx", 0.30f), ("vecy", 0.30f), ("age", 0.25f), ("motion", 0.30f), ("filter", 0.6f), ("env", 0.5f), ("space", 0.50f), ("listen", 0.90f), ("target", 1f));
        Inst("flux", 11, "Filter Chase",   ("vecx", 0.45f), ("vecy", 0.50f), ("age", 0.20f), ("motion", 0.25f), ("filter", 0.55f), ("env", 0.5f), ("space", 0.35f), ("listen", 0.85f), ("target", 0f));

        // ---- Nota Rhythm (kind 12) — drum-machine kit voicings. Voices: 0 Kick, 1 Snare,
        //      2 Clap, 3 Rim, 4 Closed Hat, 5 Open Hat, 6 Tom, 7 Perc. Param id "v{n}_{p}"
        //      (tune/decay/punch/tone/drive/level) + globals swing/humanize/accent.
        Inst("drums", 12, "808 Kit",
            ("v0_tune", 0.16f), ("v0_decay", 0.72f), ("v0_drive", 0.18f),
            ("v1_tune", 0.35f), ("v1_decay", 0.4f), ("v1_tone", 0.45f),
            ("v2_decay", 0.42f), ("v4_decay", 0.12f), ("v5_decay", 0.55f),
            ("swing", 0f), ("accent", 0.7f));
        Inst("drums", 12, "909 Kit",
            ("v0_tune", 0.3f), ("v0_decay", 0.5f), ("v0_punch", 0.7f), ("v0_drive", 0.3f),
            ("v1_tune", 0.5f), ("v1_decay", 0.35f), ("v1_tone", 0.6f),
            ("v4_decay", 0.14f), ("v5_decay", 0.45f), ("swing", 0.08f), ("accent", 0.75f));
        Inst("drums", 12, "Trap",
            ("v0_tune", 0.1f), ("v0_decay", 0.85f), ("v0_drive", 0.35f),
            ("v1_tune", 0.4f), ("v1_decay", 0.3f), ("v4_decay", 0.08f), ("v5_decay", 0.6f),
            ("swing", 0.12f), ("accent", 0.85f));
        Inst("drums", 12, "House",
            ("v0_tune", 0.28f), ("v0_decay", 0.45f), ("v0_punch", 0.6f),
            ("v2_decay", 0.35f), ("v4_decay", 0.16f), ("v5_decay", 0.4f),
            ("swing", 0.18f), ("accent", 0.6f));
        Inst("drums", 12, "Lo-Fi",
            ("v0_tune", 0.2f), ("v0_decay", 0.55f), ("v0_tone", 0.3f), ("v0_drive", 0.45f),
            ("v1_tune", 0.32f), ("v1_decay", 0.42f), ("v1_tone", 0.35f),
            ("v4_decay", 0.14f), ("swing", 0.22f), ("humanize", 0.3f), ("accent", 0.5f));
        Inst("drums", 12, "Techno",
            ("v0_tune", 0.24f), ("v0_decay", 0.5f), ("v0_punch", 0.7f), ("v0_drive", 0.4f),
            ("v4_decay", 0.1f), ("v5_decay", 0.5f), ("v7_tune", 0.5f), ("v7_decay", 0.4f),
            ("swing", 0f), ("accent", 0.8f));

        // ---- Nota Monolith (kind 13) — mono Model-D synth. Ranges LO..2' = 0/.2/.4/.6/.8/1;
        //      waves tri/shark/saw/square/wide/narrow = 0/.2/.4/.6/.8/1; tunes bipolar (0.5 = 0).
        Inst("monolith", 13, "Taurus Bass",   ("o1range", 0.2f), ("o1wave", 0.4f), ("o2range", 0.2f), ("o2tune", 0.5f), ("o2wave", 0.4f), ("mix2on", 1f), ("mix2lvl", 0.7f), ("cutoff", 0.42f), ("emph", 0.2f), ("contour", 0.5f), ("fdecay", 0.4f), ("fsustain", 0.4f), ("adecay", 0.4f), ("decayon", 1f), ("glideon", 1f), ("glide", 0.25f), ("volume", 0.85f));
        Inst("monolith", 13, "Fat Bass",      ("o1range", 0.4f), ("o1wave", 0.4f), ("o2range", 0.4f), ("o2tune", 0.54f), ("o2wave", 0.4f), ("mix2on", 1f), ("mix2lvl", 0.8f), ("cutoff", 0.48f), ("emph", 0.25f), ("contour", 0.55f), ("fdecay", 0.35f), ("fsustain", 0.35f), ("adecay", 0.35f), ("decayon", 1f), ("volume", 0.85f));
        Inst("monolith", 13, "Reese Bass",    ("o1range", 0.4f), ("o1wave", 0.4f), ("o2range", 0.4f), ("o2tune", 0.58f), ("o2wave", 0.4f), ("mix2on", 1f), ("mix2lvl", 0.9f), ("cutoff", 0.46f), ("emph", 0.3f), ("contour", 0.4f), ("feedback", 0.2f), ("decayon", 1f), ("adecay", 0.5f), ("volume", 0.82f));
        Inst("monolith", 13, "Funk Square",   ("o1range", 0.4f), ("o1wave", 0.6f), ("cutoff", 0.5f), ("emph", 0.35f), ("contour", 0.75f), ("fdecay", 0.25f), ("fsustain", 0.15f), ("adecay", 0.24f), ("asustain", 0.35f), ("decayon", 1f), ("volume", 0.82f));
        Inst("monolith", 13, "Rubber Bass",   ("o1range", 0.4f), ("o1wave", 1f), ("cutoff", 0.46f), ("emph", 0.3f), ("contour", 0.65f), ("fdecay", 0.3f), ("fsustain", 0.2f), ("adecay", 0.3f), ("asustain", 0.5f), ("decayon", 1f), ("volume", 0.83f));
        Inst("monolith", 13, "Sub Boom",      ("o1range", 0.2f), ("o1wave", 0f), ("cutoff", 0.4f), ("emph", 0.1f), ("contour", 0.4f), ("adecay", 0.45f), ("asustain", 0.6f), ("decayon", 1f), ("volume", 0.9f));
        Inst("monolith", 13, "Classic Lead",  ("o1range", 0.6f), ("o1wave", 0.4f), ("o2range", 0.6f), ("o2tune", 0.52f), ("o2wave", 0.4f), ("o3range", 0.6f), ("o3wave", 0.4f), ("mix2on", 1f), ("mix2lvl", 0.75f), ("mix3on", 1f), ("mix3lvl", 0.6f), ("cutoff", 0.7f), ("emph", 0.25f), ("contour", 0.5f), ("fsustain", 0.5f), ("glideon", 1f), ("glide", 0.15f), ("unison", 0.33f), ("volume", 0.78f));
        Inst("monolith", 13, "Hollow Lead",   ("o1range", 0.6f), ("o1wave", 0.8f), ("o2range", 0.6f), ("o2tune", 0.56f), ("o2wave", 0.6f), ("mix2on", 1f), ("mix2lvl", 0.7f), ("cutoff", 0.72f), ("emph", 0.3f), ("contour", 0.45f), ("fsustain", 0.5f), ("legato", 1f), ("volume", 0.78f));
        Inst("monolith", 13, "Brass Section", ("o1range", 0.6f), ("o1wave", 0.4f), ("o2range", 0.6f), ("o2tune", 0.53f), ("o2wave", 0.4f), ("o3range", 0.4f), ("o3wave", 0.4f), ("mix2on", 1f), ("mix2lvl", 0.8f), ("mix3on", 1f), ("mix3lvl", 0.5f), ("cutoff", 0.58f), ("emph", 0.2f), ("contour", 0.72f), ("fattack", 0.12f), ("fdecay", 0.45f), ("fsustain", 0.45f), ("aattack", 0.08f), ("asustain", 0.8f), ("decayon", 1f), ("volume", 0.76f));
        Inst("monolith", 13, "Screaming Lead",("o1range", 0.6f), ("o1wave", 0.4f), ("o2range", 0.6f), ("o2tune", 0.55f), ("o2wave", 0.4f), ("mix2on", 1f), ("mix2lvl", 0.8f), ("cutoff", 0.8f), ("emph", 0.55f), ("contour", 0.5f), ("fsustain", 0.6f), ("feedback", 0.25f), ("kbd1", 1f), ("kbd2", 1f), ("glideon", 1f), ("glide", 0.1f), ("volume", 0.72f));
        Inst("monolith", 13, "Portamento",    ("o1range", 0.6f), ("o1wave", 0.4f), ("o2range", 0.6f), ("o2tune", 0.52f), ("o2wave", 0.2f), ("mix2on", 1f), ("mix2lvl", 0.7f), ("cutoff", 0.66f), ("emph", 0.28f), ("contour", 0.5f), ("fsustain", 0.5f), ("glideon", 1f), ("glide", 0.4f), ("legato", 1f), ("volume", 0.78f));
        Inst("monolith", 13, "Bright Lead",   ("o1range", 0.8f), ("o1wave", 0.4f), ("o2range", 0.6f), ("o2tune", 0.5f), ("o2wave", 0.4f), ("mix2on", 1f), ("mix2lvl", 0.6f), ("cutoff", 0.85f), ("emph", 0.3f), ("contour", 0.4f), ("fsustain", 0.6f), ("unison", 0.33f), ("volume", 0.75f));
        Inst("monolith", 13, "Soft Flute",    ("o1range", 0.6f), ("o1wave", 0f), ("cutoff", 0.62f), ("emph", 0.1f), ("contour", 0.3f), ("fsustain", 0.6f), ("noiseon", 1f), ("noiselvl", 0.08f), ("aattack", 0.15f), ("asustain", 0.85f), ("volume", 0.8f));
        Inst("monolith", 13, "Whistle",       ("o1range", 0.8f), ("o1wave", 0f), ("cutoff", 0.82f), ("emph", 0.4f), ("contour", 0.2f), ("fsustain", 0.7f), ("aattack", 0.1f), ("asustain", 0.9f), ("volume", 0.72f));
        Inst("monolith", 13, "Fifths Stack",  ("o1range", 0.6f), ("o1wave", 0.4f), ("o2range", 0.6f), ("o2tune", 0.75f), ("o2wave", 0.4f), ("mix2on", 1f), ("mix2lvl", 0.7f), ("cutoff", 0.72f), ("emph", 0.25f), ("contour", 0.5f), ("fsustain", 0.55f), ("volume", 0.76f));
        Inst("monolith", 13, "Detune Unison", ("o1range", 0.6f), ("o1wave", 0.4f), ("cutoff", 0.74f), ("emph", 0.2f), ("contour", 0.45f), ("fsustain", 0.6f), ("unison", 0.66f), ("unidetune", 0.5f), ("volume", 0.72f));
        Inst("monolith", 13, "Pulse Pad",     ("o1range", 0.6f), ("o1wave", 0.8f), ("o2range", 0.4f), ("o2tune", 0.55f), ("o2wave", 0.8f), ("mix2on", 1f), ("mix2lvl", 0.7f), ("cutoff", 0.56f), ("emph", 0.2f), ("contour", 0.55f), ("fsustain", 0.45f), ("fattack", 0.3f), ("aattack", 0.3f), ("asustain", 0.85f), ("adecay", 0.6f), ("decayon", 1f), ("volume", 0.75f));
        Inst("monolith", 13, "Growl Overload",("o1range", 0.4f), ("o1wave", 0.4f), ("o2range", 0.4f), ("o2tune", 0.57f), ("o2wave", 0.6f), ("mix2on", 1f), ("mix2lvl", 0.85f), ("cutoff", 0.5f), ("emph", 0.4f), ("contour", 0.5f), ("fsustain", 0.4f), ("feedback", 0.4f), ("exton", 1f), ("extlvl", 0.3f), ("decayon", 1f), ("adecay", 0.5f), ("volume", 0.7f));
        Inst("monolith", 13, "Sci-Fi Mod",    ("o1range", 0.6f), ("o1wave", 0.4f), ("o3range", 0.0f), ("o3wave", 0.4f), ("osc3kbd", 0f), ("oscmodon", 1f), ("modwheel", 0.5f), ("modmix", 0f), ("cutoff", 0.65f), ("emph", 0.35f), ("contour", 0.5f), ("fsustain", 0.55f), ("volume", 0.72f));
        Inst("monolith", 13, "Random Bleeps",  ("o1range", 0.8f), ("o1wave", 1f), ("filtmodon", 1f), ("modwheel", 0.4f), ("o3range", 0.0f), ("osc3kbd", 0f), ("cutoff", 0.7f), ("emph", 0.4f), ("contour", 0.4f), ("fdecay", 0.2f), ("fsustain", 0.3f), ("adecay", 0.2f), ("asustain", 0.2f), ("decayon", 1f), ("volume", 0.74f));

        // ---- Nota Pentad (kind 14) — 5-voice Prophet-5-style poly. Octave 32'/16'/8'/4' =
        //      0/.333/.667/1; semitone .5 = 0 (±12 over 0..1, +7 = .7917); fine .5 = 0 (±50 c);
        //      cutoff v = log10(Hz/20)/3; voice mode Poly/Uni/Mono = 0/.5/1; toggles 0/1.
        //      Unlisted params keep the init patch (two saws, half-open filter).
        // Brass
        Inst("pentad", 14, "Poly Brass",      ("obfine", 0.53f), ("cutoff", 0.42f), ("reso", 0.15f), ("fenvamt", 0.55f), ("fattack", 0.45f), ("fdecay", 0.55f), ("fsustain", 0.45f), ("frelease", 0.45f), ("aattack", 0.35f), ("adecay", 0.5f), ("asustain", 0.9f), ("arelease", 0.45f));
        Inst("pentad", 14, "Soft Horns",      ("obfine", 0.52f), ("cutoff", 0.38f), ("fenvamt", 0.45f), ("fattack", 0.6f), ("fdecay", 0.6f), ("fsustain", 0.5f), ("aattack", 0.55f), ("asustain", 0.9f), ("arelease", 0.5f), ("drift", 0.35f));
        Inst("pentad", 14, "Brass Stab",      ("cutoff", 0.40f), ("reso", 0.2f), ("fenvamt", 0.6f), ("fattack", 0.2f), ("fdecay", 0.45f), ("fsustain", 0.15f), ("aattack", 0.05f), ("adecay", 0.5f), ("asustain", 0.4f), ("arelease", 0.3f));
        Inst("pentad", 14, "Octave Brass",    ("oboct", 0.333f), ("mixb", 0.7f), ("cutoff", 0.45f), ("fenvamt", 0.5f), ("fattack", 0.4f), ("fdecay", 0.55f), ("fsustain", 0.5f), ("aattack", 0.3f), ("asustain", 0.9f));
        Inst("pentad", 14, "Sync Brass",      ("oasync", 1f), ("oasemi", 0.625f), ("pmenv", 0.35f), ("pmfreqa", 1f), ("cutoff", 0.5f), ("fenvamt", 0.4f), ("fattack", 0.3f), ("fdecay", 0.55f), ("fsustain", 0.4f), ("aattack", 0.2f));
        // Strings / pads
        Inst("pentad", 14, "Glass Strings",   ("oapulse", 1f), ("oapw", 0.38f), ("obsaw", 0f), ("obpulse", 1f), ("obpw", 0.52f), ("oboct", 0.333f), ("obfine", 0.57f),
                                              ("mixa", 0.82f), ("mixb", 0.7f), ("mixnoise", 0.12f), ("noisecolor", 1f), ("drift", 0.28f), ("glide", 0.418f), ("glidemode", 0.5f),
                                              ("cutoff", 0.656f), ("reso", 0.34f), ("fenvamt", 0.5f), ("velfilt", 0.5f), ("fattack", 0.362f), ("fdecay", 0.648f), ("fsustain", 0.52f), ("frelease", 0.6f),
                                              ("aattack", 0.624f), ("adecay", 0.736f), ("asustain", 0.74f), ("arelease", 0.707f), ("spread", 0.44f), ("volume", 0.574f), ("unidetune", 0.18f));
        Inst("pentad", 14, "Analog Strings",  ("obfine", 0.56f), ("cutoff", 0.5f), ("reso", 0.05f), ("fenvamt", 0.15f), ("aattack", 0.62f), ("adecay", 0.6f), ("asustain", 0.9f), ("arelease", 0.65f), ("lfoamt", 0.12f), ("lforate", 0.62f), ("drift", 0.35f), ("spread", 0.6f));
        Inst("pentad", 14, "Warm Pad",        ("oasaw", 0f), ("oapulse", 1f), ("oapw", 0.45f), ("obfine", 0.55f), ("cutoff", 0.42f), ("fenvamt", 0.3f), ("fattack", 0.7f), ("fdecay", 0.7f), ("fsustain", 0.6f), ("frelease", 0.7f), ("aattack", 0.7f), ("asustain", 0.95f), ("arelease", 0.75f), ("drift", 0.4f));
        Inst("pentad", 14, "PWM Pad",         ("oasaw", 0f), ("oapulse", 1f), ("obsaw", 0f), ("obpulse", 1f), ("wmpwa", 1f), ("wmpwb", 1f), ("wmfreqa", 0f), ("wmfreqb", 0f), ("lfoamt", 0.35f), ("lforate", 0.52f), ("cutoff", 0.52f), ("aattack", 0.6f), ("asustain", 0.9f), ("arelease", 0.6f));
        Inst("pentad", 14, "Choir Vox",       ("oasaw", 0f), ("oapulse", 1f), ("oapw", 0.3f), ("obsaw", 0f), ("obtri", 1f), ("oboct", 1f), ("cutoff", 0.48f), ("reso", 0.35f), ("fenvamt", 0.2f), ("keytrk", 0.7f), ("aattack", 0.6f), ("asustain", 0.9f), ("arelease", 0.6f), ("mixnoise", 0.08f), ("noisecolor", 1f), ("lfoamt", 0.08f), ("lforate", 0.6f));
        Inst("pentad", 14, "Sweep Pad",       ("cutoff", 0.3f), ("reso", 0.45f), ("fenvamt", 0.55f), ("fattack", 0.85f), ("fdecay", 0.85f), ("fsustain", 0.3f), ("frelease", 0.8f), ("aattack", 0.6f), ("asustain", 0.9f), ("arelease", 0.8f), ("drift", 0.3f));
        Inst("pentad", 14, "Per-Voice PWM",   ("oasaw", 0f), ("oapulse", 1f), ("oapw", 0.5f), ("obsaw", 0f), ("obtri", 1f), ("oblofreq", 1f), ("mixb", 0f), ("pmoscb", 0.55f), ("pmpwa", 1f), ("cutoff", 0.55f), ("aattack", 0.5f), ("asustain", 0.9f), ("arelease", 0.6f), ("drift", 0.4f));
        Inst("pentad", 14, "Solar Drift",     ("drift", 0.8f), ("oasaw", 0f), ("oapulse", 1f), ("oapw", 0.3f), ("obsaw", 0f), ("obtri", 1f), ("obsemi", 0.7917f), ("cutoff", 0.5f), ("fenvamt", 0.25f), ("aattack", 0.65f), ("asustain", 0.9f), ("arelease", 0.75f), ("spread", 0.8f));
        // Leads
        Inst("pentad", 14, "Sync Lead",       ("oasync", 1f), ("pmenv", 0.45f), ("pmfreqa", 1f), ("mixb", 0f), ("cutoff", 0.75f), ("reso", 0.2f), ("fenvamt", 0.25f), ("fdecay", 0.55f), ("fsustain", 0.4f), ("aattack", 0.1f), ("asustain", 0.9f), ("arelease", 0.35f), ("voicemode", 1f), ("glide", 0.3f));
        Inst("pentad", 14, "Unison Lead",     ("voicemode", 0.5f), ("unidetune", 0.35f), ("cutoff", 0.62f), ("reso", 0.25f), ("fenvamt", 0.4f), ("fdecay", 0.5f), ("fsustain", 0.5f), ("glide", 0.32f));
        Inst("pentad", 14, "Pulse Lead",      ("oasaw", 0f), ("oapulse", 1f), ("oapw", 0.3f), ("mixb", 0f), ("voicemode", 1f), ("cutoff", 0.66f), ("reso", 0.3f), ("fenvamt", 0.35f), ("glide", 0.3f));
        Inst("pentad", 14, "Screamer",        ("voicemode", 1f), ("reso", 0.75f), ("cutoff", 0.55f), ("fenvamt", 0.5f), ("keytrk", 1f), ("mixa", 1f), ("mixb", 0.9f), ("glide", 0.28f));
        Inst("pentad", 14, "Fifth Lead",      ("obsemi", 0.7917f), ("voicemode", 0.5f), ("unidetune", 0.2f), ("cutoff", 0.64f), ("fenvamt", 0.35f), ("glide", 0.3f));
        Inst("pentad", 14, "Whistle Lead",    ("mixa", 0f), ("mixb", 0f), ("mixnoise", 0.01f), ("reso", 0.93f), ("keytrk", 1f), ("cutoff", 0.62f), ("fenvamt", 0.05f), ("voicemode", 1f), ("glide", 0.3f), ("lfoamt", 0.1f), ("lforate", 0.72f));
        Inst("pentad", 14, "Soft Lead",       ("oasaw", 0f), ("oapulse", 1f), ("oapw", 0.5f), ("obsaw", 0f), ("obtri", 1f), ("cutoff", 0.55f), ("fenvamt", 0.2f), ("aattack", 0.35f), ("glide", 0.25f), ("voicemode", 1f));
        // Bass
        Inst("pentad", 14, "Pentad Bass",     ("oaoct", 0.333f), ("oboct", 0.333f), ("obfine", 0.52f), ("cutoff", 0.33f), ("reso", 0.25f), ("fenvamt", 0.55f), ("fdecay", 0.42f), ("fsustain", 0.15f), ("adecay", 0.5f), ("asustain", 0.7f), ("arelease", 0.25f), ("voicemode", 1f));
        Inst("pentad", 14, "Sync Bass",       ("oaoct", 0.333f), ("oboct", 0f), ("oasync", 1f), ("oasemi", 0.7917f), ("mixb", 0.4f), ("cutoff", 0.4f), ("fenvamt", 0.5f), ("fdecay", 0.4f), ("fsustain", 0.2f), ("voicemode", 1f));
        Inst("pentad", 14, "Unison Bass",     ("oaoct", 0.333f), ("oboct", 0.333f), ("voicemode", 0.5f), ("unidetune", 0.25f), ("cutoff", 0.36f), ("reso", 0.2f), ("fenvamt", 0.5f), ("fdecay", 0.45f), ("fsustain", 0.25f), ("asustain", 0.8f), ("arelease", 0.25f));
        Inst("pentad", 14, "Pluck Bass",      ("oaoct", 0.333f), ("oasaw", 0f), ("oapulse", 1f), ("oapw", 0.3f), ("mixb", 0f), ("cutoff", 0.3f), ("reso", 0.35f), ("fenvamt", 0.6f), ("fdecay", 0.35f), ("fsustain", 0f), ("adecay", 0.45f), ("asustain", 0f), ("arelease", 0.3f), ("voicemode", 1f));
        Inst("pentad", 14, "Rubber Bass",     ("oaoct", 0.333f), ("oboct", 0.333f), ("cutoff", 0.28f), ("reso", 0.6f), ("fenvamt", 0.55f), ("fdecay", 0.38f), ("fsustain", 0.1f), ("voicemode", 1f), ("glide", 0.25f));
        Inst("pentad", 14, "Sub Drone",       ("oaoct", 0f), ("oboct", 0f), ("obsaw", 0f), ("obtri", 1f), ("cutoff", 0.3f), ("fenvamt", 0.1f), ("asustain", 1f), ("arelease", 0.5f));
        // Keys / plucks
        Inst("pentad", 14, "Poly Keys",       ("cutoff", 0.48f), ("fenvamt", 0.4f), ("fdecay", 0.55f), ("fsustain", 0.2f), ("adecay", 0.65f), ("asustain", 0.3f), ("arelease", 0.5f), ("velfilt", 0.4f), ("velamp", 0.3f));
        Inst("pentad", 14, "Riley Organ",     ("oasaw", 0f), ("oapulse", 1f), ("oapw", 0.5f), ("obsaw", 0f), ("obpulse", 1f), ("oboct", 1f), ("obpw", 0.35f), ("mixb", 0.5f), ("cutoff", 0.6f), ("reso", 0.15f), ("fenvamt", 0.25f), ("fdecay", 0.35f), ("fsustain", 0.3f), ("adecay", 0.4f), ("asustain", 0.6f), ("arelease", 0.2f), ("drift", 0.15f));
        Inst("pentad", 14, "Clavi",           ("oasaw", 0f), ("oapulse", 1f), ("oapw", 0.2f), ("mixb", 0f), ("cutoff", 0.55f), ("reso", 0.3f), ("fenvamt", 0.5f), ("fdecay", 0.3f), ("fsustain", 0f), ("adecay", 0.45f), ("asustain", 0f), ("arelease", 0.15f), ("keytrk", 0.8f), ("velfilt", 0.5f));
        Inst("pentad", 14, "Bell Keys",       ("obsaw", 0f), ("obtri", 1f), ("oboct", 1f), ("obsemi", 0.7917f), ("mixb", 0f), ("pmoscb", 0.35f), ("pmfreqa", 1f), ("cutoff", 0.7f), ("fenvamt", 0.2f), ("adecay", 0.7f), ("asustain", 0f), ("arelease", 0.6f));
        Inst("pentad", 14, "Harpsi",          ("oapulse", 1f), ("oapw", 0.15f), ("oboct", 1f), ("mixb", 0.4f), ("cutoff", 0.62f), ("reso", 0.2f), ("fenvamt", 0.35f), ("fdecay", 0.45f), ("fsustain", 0f), ("adecay", 0.55f), ("asustain", 0f), ("arelease", 0.45f));
        Inst("pentad", 14, "Marimba Pluck",   ("oasaw", 0f), ("obsaw", 0f), ("obtri", 1f), ("mixa", 0f), ("mixb", 1f), ("cutoff", 0.45f), ("reso", 0.5f), ("fenvamt", 0.5f), ("fdecay", 0.3f), ("fsustain", 0f), ("adecay", 0.45f), ("asustain", 0f), ("arelease", 0.35f), ("keytrk", 1f));
        // Poly-Mod / SFX
        Inst("pentad", 14, "Poly-Mod Bells",  ("obsaw", 0f), ("obtri", 1f), ("oboct", 1f), ("obfine", 0.6f), ("mixb", 0f), ("pmoscb", 0.45f), ("pmfreqa", 1f), ("cutoff", 0.75f), ("fenvamt", 0.1f), ("adecay", 0.7f), ("asustain", 0.1f), ("arelease", 0.7f));
        Inst("pentad", 14, "Metallic Clang",  ("obsaw", 0f), ("obpulse", 1f), ("obsemi", 0.75f), ("mixb", 0.2f), ("pmoscb", 0.7f), ("pmfreqa", 1f), ("pmfilter", 1f), ("cutoff", 0.6f), ("reso", 0.4f), ("fenvamt", 0.3f), ("fdecay", 0.5f), ("fsustain", 0f), ("adecay", 0.55f), ("asustain", 0f), ("arelease", 0.5f));
        Inst("pentad", 14, "Env Sync Sweep",  ("oasync", 1f), ("pmenv", 0.6f), ("pmfreqa", 1f), ("mixb", 0f), ("fattack", 0.5f), ("fdecay", 0.7f), ("fsustain", 0f), ("cutoff", 0.7f), ("fenvamt", 0.2f), ("asustain", 0.9f));
        Inst("pentad", 14, "Laser Zap",       ("pmenv", 0.8f), ("pmfreqa", 1f), ("mixb", 0f), ("fdecay", 0.35f), ("fsustain", 0f), ("adecay", 0.4f), ("asustain", 0f), ("cutoff", 0.8f), ("fenvamt", 0.1f));
        Inst("pentad", 14, "Wind & Surf",     ("mixa", 0f), ("mixb", 0f), ("mixnoise", 1f), ("noisecolor", 1f), ("cutoff", 0.45f), ("reso", 0.6f), ("keytrk", 0f), ("wmfilter", 1f), ("wmfreqa", 0f), ("wmfreqb", 0f), ("lfoamt", 0.5f), ("lforate", 0.35f), ("aattack", 0.75f), ("asustain", 1f), ("arelease", 0.8f));
        Inst("pentad", 14, "Lo-Freq Growl",   ("oblofreq", 1f), ("obkbd", 0f), ("obsaw", 0f), ("obtri", 1f), ("mixb", 0f), ("pmoscb", 0.35f), ("pmfreqa", 1f), ("pmfilter", 1f), ("cutoff", 0.5f), ("reso", 0.4f));
        Inst("pentad", 14, "Random Computer", ("oasaw", 0f), ("oapulse", 1f), ("lfotri", 0f), ("lfosaw", 1f), ("lfosquare", 1f), ("lforate", 0.75f), ("lfoamt", 0.8f), ("wmfilter", 1f), ("cutoff", 0.6f), ("reso", 0.5f));
        Inst("pentad", 14, "Dive Bomb",       ("oasync", 1f), ("pmenv", 1f), ("pmfreqa", 1f), ("mixb", 0f), ("fattack", 0.8f), ("fdecay", 0.85f), ("fsustain", 0f), ("cutoff", 0.6f), ("reso", 0.3f), ("glide", 0.8f), ("glidemode", 0.5f));

        // ---- Nota Consort (kind 15) — paraphonic semi-modular. Octave 32'/16'/8'/4'/2' =
        //      0/.25/.5/.75/1; freq .5 = 0 (±7 st); wave tri/saw/square/pulse = 0/.333/.667/1;
        //      voice mode Mono/Duo/Para = 0/.5/1 (+ truepoly); filter HP/LP ser, LP/LP st, HP/LP st
        //      = 0/.5/1; cutoff v = log10(Hz/20)/3; spacing .5 + oct/6; env amount .5 + d/20.
        //      ConsortSeq(types, pitches) writes the 16 steps ('n' note, 'r' ratchet, 't' tie,
        //      '.' rest); Cab(slot, source, dest, depth) patches a cable (jack indices, Consort.h).
        // Paraphonic
        Inst("consort", 15, "Stereo Consort", Cat(P(("o2freq", 0.505f), ("o3oct", 0.25f), ("o3wave", 0.667f), ("o4oct", 0.75f), ("o4wave", 1f), ("pw", 0.42f),
            ("mix1", 0.88f), ("mix2", 0.74f), ("mix3", 0.64f), ("mix4", 0.5f), ("filtmode", 0.5f), ("cutoff", 0.497f), ("reso", 0.35f), ("spacing", 0.667f), ("fenvamt", 0.72f),
            ("fattack", 0.25f), ("fdecay", 0.6f), ("fsustain", 0.46f), ("frelease", 0.6f), ("aattack", 0.3f), ("adecay", 0.68f), ("asustain", 0.72f), ("arelease", 0.66f),
            ("drift", 0.34f), ("glide", 0.519f), ("glidetype", 1f), ("lfopitch", 0.546f), ("lforate", 0.6f), ("lfodest", 1f),
            ("dlymix", 0.3f), ("dlyfb", 0.5f), ("dlytime", 0.678f), ("dlyspacing", 0.63f), ("dlyping", 1f))));
        Inst("consort", 15, "Para Strings", ("o2freq", 0.507f), ("o3freq", 0.493f), ("o4freq", 0.511f), ("filtmode", 0.5f), ("cutoff", 0.56f), ("reso", 0.1f), ("spacing", 0.583f),
            ("fenvamt", 0.62f), ("fattack", 0.7f), ("fdecay", 0.75f), ("fsustain", 0.6f), ("frelease", 0.75f), ("aattack", 0.72f), ("adecay", 0.7f), ("asustain", 0.9f), ("arelease", 0.78f),
            ("drift", 0.45f), ("lforate", 0.36f), ("lfopitch", 0.54f), ("dlymix", 0.22f), ("dlydigital", 1f), ("dlyping", 0f), ("dlytime", 0.72f), ("dlyfb", 0.35f));
        Inst("consort", 15, "Glass Chords", ("o1wave", 0f), ("o2wave", 1f), ("o2oct", 0.75f), ("o3wave", 0f), ("o4wave", 1f), ("pw", 0.6f), ("filtmode", 1f), ("cutoff", 0.46f),
            ("spacing", 0.833f), ("reso", 0.25f), ("fenvamt", 0.75f), ("fattack", 0f), ("fdecay", 0.55f), ("fsustain", 0.2f), ("frelease", 0.6f),
            ("aattack", 0f), ("adecay", 0.7f), ("asustain", 0.3f), ("arelease", 0.7f), ("lfopwm", 0.3f), ("lforate", 0.45f), ("dlymix", 0.35f), ("dlyfb", 0.45f), ("dlytime", 0.585f));
        Inst("consort", 15, "Consort Brass", ("o2freq", 0.509f), ("o3freq", 0.491f), ("o4freq", 0.505f), ("cutoff", 0.42f), ("reso", 0.15f), ("fenvamt", 0.78f),
            ("fattack", 0.55f), ("fdecay", 0.6f), ("fsustain", 0.5f), ("frelease", 0.5f), ("aattack", 0.45f), ("asustain", 0.9f), ("arelease", 0.5f),
            ("mixdrive", 1f), ("spacing", 0.55f), ("drift", 0.3f));
        Inst("consort", 15, "Spread Voicing", ("o1oct", 0.25f), ("o4oct", 0.75f), ("o3wave", 0.667f), ("cutoff", 0.6f), ("fenvamt", 0.62f), ("fdecay", 0.62f), ("fsustain", 0.45f),
            ("aattack", 0.2f), ("adecay", 0.7f), ("asustain", 0.8f), ("arelease", 0.62f), ("dlymix", 0.25f), ("drift", 0.3f));
        Inst("consort", 15, "Duo Fifths", ("voicemode", 0.5f), ("o2freq", 1f), ("o4freq", 1f), ("cutoff", 0.62f), ("reso", 0.3f), ("fenvamt", 0.68f), ("fdecay", 0.55f), ("fsustain", 0.45f),
            ("glide", 0.434f), ("glidetype", 1f), ("dlymix", 0.28f), ("dlytime", 0.627f));
        Inst("consort", 15, "Duo Sync Pad", Cat(P(("voicemode", 0.5f), ("o2sync", 1f), ("o2freq", 0.893f), ("o4freq", 0.505f), ("mix1", 0.5f), ("mix2", 0.85f), ("mix3", 0.7f), ("mix4", 0.6f),
            ("cutoff", 0.58f), ("reso", 0.2f), ("fenvamt", 0.6f), ("aattack", 0.62f), ("asustain", 0.9f), ("arelease", 0.72f), ("lfowave", 0f), ("lforate", 0.25f), ("dlymix", 0.3f)),
            Cab(1, 1, 6, 0.3f)));
        // True poly
        Inst("consort", 15, "Poly Pad", ("truepoly", 1f), ("o2freq", 0.507f), ("o3oct", 0.25f), ("o3wave", 0.667f), ("o4oct", 0.75f), ("o4wave", 0f), ("mix3", 0.55f), ("mix4", 0.45f),
            ("cutoff", 0.5f), ("fenvamt", 0.64f), ("fattack", 0.6f), ("fdecay", 0.7f), ("fsustain", 0.55f), ("frelease", 0.7f), ("aattack", 0.7f), ("asustain", 0.9f), ("arelease", 0.8f),
            ("lfocut", 0.25f), ("lforate", 0.3f), ("drift", 0.4f), ("dlymix", 0.2f));
        Inst("consort", 15, "Poly Keys", ("truepoly", 1f), ("o1wave", 1f), ("o3wave", 1f), ("o3oct", 0.25f), ("mix3", 0.5f), ("mix4", 0.4f), ("pw", 0.5f), ("cutoff", 0.55f), ("fenvamt", 0.75f),
            ("fdecay", 0.55f), ("fsustain", 0.25f), ("aattack", 0.1f), ("adecay", 0.65f), ("asustain", 0.5f), ("arelease", 0.55f), ("velvca", 1f), ("dlymix", 0.25f));
        // Mono
        Inst("consort", 15, "Ladder Bass", ("voicemode", 0f), ("unison", 0f), ("o1oct", 0.25f), ("o2oct", 0.25f), ("o2freq", 0.507f), ("o3oct", 0f), ("o3wave", 0.667f), ("mix1", 0.9f), ("mix2", 0.8f),
            ("mix3", 0.6f), ("mix4", 0f), ("cutoff", 0.36f), ("reso", 0.3f), ("fenvamt", 0.72f), ("fdecay", 0.5f), ("fsustain", 0.2f), ("frelease", 0.4f),
            ("adecay", 0.55f), ("asustain", 0.8f), ("arelease", 0.35f), ("mixdrive", 1f), ("spacing", 0.5f));
        Inst("consort", 15, "Deep Sub", ("voicemode", 0f), ("unison", 0f), ("o1oct", 0.25f), ("o1wave", 0f), ("o2oct", 0f), ("o2wave", 0.667f), ("mix1", 0.9f), ("mix2", 0.6f), ("mix3", 0f), ("mix4", 0f),
            ("cutoff", 0.3f), ("reso", 0.1f), ("fenvamt", 0.6f), ("fdecay", 0.45f), ("fsustain", 0.3f), ("basscomp", 1f), ("spacing", 0.5f), ("arelease", 0.3f));
        Inst("consort", 15, "Unison Lead", ("voicemode", 0f), ("unison", 1f), ("drift", 0.45f), ("o4oct", 0.75f), ("cutoff", 0.62f), ("reso", 0.35f), ("fenvamt", 0.65f), ("fdecay", 0.55f),
            ("fsustain", 0.5f), ("glide", 0.36f), ("glidetype", 0.5f), ("glidegated", 1f), ("lfopitch", 0.565f), ("lforate", 0.64f), ("dlymix", 0.3f), ("dlytime", 0.627f));
        Inst("consort", 15, "Sync Lead", Cat(P(("voicemode", 0f), ("unison", 0f), ("o2sync", 1f), ("o2freq", 0.857f), ("mix1", 0.4f), ("mix2", 0.9f), ("mix3", 0f), ("mix4", 0f),
            ("cutoff", 0.7f), ("fenvamt", 0.62f), ("fdecay", 0.55f), ("fsustain", 0.25f), ("glide", 0.3f), ("dlymix", 0.25f)),
            Cab(1, 2, 6, 0.35f)));
        Inst("consort", 15, "Feedback Growl", ("voicemode", 0f), ("unison", 0f), ("o1oct", 0.25f), ("o2oct", 0.25f), ("o2freq", 0.505f), ("mix3", 0f), ("mix4", 0f), ("mixext", 0.6f),
            ("mixdrive", 1f), ("cutoff", 0.42f), ("reso", 0.45f), ("fenvamt", 0.7f), ("fdecay", 0.45f), ("fsustain", 0.35f), ("spacing", 0.55f));
        Inst("consort", 15, "Ladder Whistle", ("voicemode", 0f), ("unison", 0f), ("mix1", 0f), ("mix2", 0f), ("mix3", 0f), ("mix4", 0f), ("mixnoise", 0.02f), ("reso", 1f), ("kbdtrk", 1f),
            ("cutoff", 0.55f), ("fenvamt", 0.5f), ("spacing", 0.667f), ("glide", 0.36f), ("lfocut", 0.12f), ("lforate", 0.7f), ("aattack", 0.3f), ("arelease", 0.5f));
        Inst("consort", 15, "Wheel Wah", Cat(P(("voicemode", 0f), ("cutoff", 0.35f), ("reso", 0.6f), ("fenvamt", 0.55f), ("glide", 0.3f), ("modwheel", 0.4f)),
            Cab(1, 17, 10, 0.8f), Cab(2, 17, 11, 0.6f), Cab(3, 16, 4, 0.1f)));
        // Sequencer / arpeggiator
        Inst("consort", 15, "Ratchet Run", Cat(P(("seqmode", 0.5f), ("seqrate", 0.6f), ("seqswing", 0.24f), ("cutoff", 0.5f), ("reso", 0.4f), ("fenvamt", 0.72f), ("fdecay", 0.45f),
            ("fsustain", 0.15f), ("adecay", 0.5f), ("asustain", 0.3f), ("arelease", 0.35f), ("dlymix", 0.3f), ("dlyfb", 0.45f)),
            ConsortSeq("n.nrnt.nn.rnnt.n", 0, 0, 5, -2, 7, 7, 0, 10, 12, 0, -2, 5, 7, 7, 0, 3)));
        Inst("consort", 15, "Acid Run", Cat(P(("voicemode", 0f), ("unison", 0f), ("seqmode", 0.5f), ("seqrate", 0.6f), ("seqswing", 0.2f), ("o1oct", 0.25f), ("mix1", 0.9f), ("mix2", 0f),
            ("mix3", 0f), ("mix4", 0f), ("cutoff", 0.38f), ("reso", 0.82f), ("fenvamt", 0.82f), ("fdecay", 0.45f), ("fsustain", 0.05f), ("adecay", 0.5f), ("asustain", 0.6f),
            ("glide", 0.3f), ("glidetype", 0.5f), ("glidegated", 1f), ("mixdrive", 1f), ("spacing", 0.5f), ("dlymix", 0.2f)),
            ConsortSeq("nntnrn.nntn.nrtn", 0, 0, 0, 12, 0, 3, 0, -2, 0, 0, 10, 0, 7, 12, 12, 0)));
        Inst("consort", 15, "Arp Cascade", ("seqmode", 1f), ("arpoct", 0.5f), ("seqrate", 0.6f), ("o1wave", 0f), ("o3wave", 0f), ("cutoff", 0.6f), ("fenvamt", 0.7f), ("fdecay", 0.42f),
            ("fsustain", 0.1f), ("adecay", 0.45f), ("asustain", 0.2f), ("arelease", 0.45f), ("dlymix", 0.38f), ("dlyfb", 0.55f), ("dlyping", 1f));
        Inst("consort", 15, "Random Arp", ("seqmode", 1f), ("seqorder", 1f), ("arpoct", 1f), ("seqrate", 0.8f), ("lfowave", 0.8f), ("lfosync", 1f), ("lforate", 0.846f), ("lfocut", 0.45f),
            ("cutoff", 0.5f), ("reso", 0.35f), ("fdecay", 0.4f), ("fsustain", 0.1f), ("adecay", 0.42f), ("asustain", 0.15f), ("dlymix", 0.35f), ("dlydigital", 1f));
        Inst("consort", 15, "Latch Sequence", Cat(P(("seqmode", 0.5f), ("seqlatch", 1f), ("seqrate", 0.2f), ("seqlen", 0.733f), ("cutoff", 0.52f), ("fenvamt", 0.62f), ("fattack", 0.3f),
            ("fdecay", 0.6f), ("aattack", 0.3f), ("adecay", 0.65f), ("asustain", 0.5f), ("arelease", 0.6f), ("dlymix", 0.3f), ("drift", 0.4f)),
            ConsortSeq("ntn.nrntn.nt....", 0, 0, 7, 0, 3, 5, 10, 10, 12, 0, 7, 7, 0, 0, 0, 0)));
        // Modular (patch bay)
        Inst("consort", 15, "Patched Drift", Cat(P(("lfowave", 1f), ("lforate", 0.36f), ("drift", 0.6f), ("cutoff", 0.52f), ("fenvamt", 0.62f), ("aattack", 0.4f), ("adecay", 0.7f),
            ("asustain", 0.8f), ("arelease", 0.7f), ("dlymix", 0.35f), ("dlyfb", 0.5f)),
            Cab(1, 1, 7, 0.14f), Cab(2, 2, 16, 0.38f), Cab(3, 6, 14, 0.48f)));
        Inst("consort", 15, "Ring Bells", Cat(P(("truepoly", 1f), ("o1wave", 0f), ("o3wave", 0f), ("o3oct", 0.75f), ("o3freq", 0.736f), ("mix1", 0.9f), ("mix2", 0f), ("mix3", 0f), ("mix4", 0f),
            ("cutoff", 0.8f), ("fenvamt", 0.5f), ("adecay", 0.7f), ("asustain", 0f), ("arelease", 0.7f), ("dlymix", 0.3f)),
            Cab(1, 6, 14, 0.95f)));
        Inst("consort", 15, "FM Growl", Cat(P(("voicemode", 0f), ("unison", 0f), ("o1oct", 0.25f), ("o4oct", 0.25f), ("mix2", 0f), ("mix3", 0f), ("mix4", 0f), ("cutoff", 0.45f), ("reso", 0.3f),
            ("fenvamt", 0.7f), ("fdecay", 0.5f), ("fsustain", 0.3f), ("mixdrive", 1f)),
            Cab(1, 7, 5, 0.28f)));
        Inst("consort", 15, "Trance Gate", Cat(P(("lfowave", 0.6f), ("lfosync", 1f), ("lforate", 0.846f), ("cutoff", 0.55f), ("fenvamt", 0.6f), ("aattack", 0.2f), ("adecay", 0.45f),
            ("asustain", 0.6f), ("arelease", 0.3f), ("dlymix", 0.3f)),
            Cab(1, 1, 3, 1f)));
        Inst("consort", 15, "Wind Noise", Cat(P(("reso", 0.85f), ("kbdtrk", 1f), ("lfowave", 1f), ("lforate", 0.3f), ("lfocut", 0.55f), ("aattack", 0.6f), ("arelease", 0.75f),
            ("cutoff", 0.5f), ("fenvamt", 0.5f), ("spacing", 0.667f), ("dlymix", 0.3f)),
            Cab(1, 8, 13, 1f)));
        Inst("consort", 15, "Touch Stereo", Cat(P(("truepoly", 1f), ("velvca", 1f), ("spacing", 0.5f), ("cutoff", 0.35f), ("fenvamt", 0.6f), ("fdecay", 0.5f), ("fsustain", 0.2f),
            ("adecay", 0.6f), ("asustain", 0.4f), ("arelease", 0.5f)),
            Cab(1, 13, 11, 1f), Cab(2, 15, 10, 0.7f)));
        Inst("consort", 15, "Delay Warp", Cat(P(("lfowave", 0f), ("lforate", 0.28f), ("dlymix", 0.5f), ("dlyfb", 0.7f), ("dlytime", 0.72f), ("cutoff", 0.58f), ("fenvamt", 0.7f),
            ("fdecay", 0.5f), ("fsustain", 0.15f), ("adecay", 0.55f), ("asustain", 0.3f)),
            Cab(1, 1, 16, 0.4f)));

        // ---- EQ-8 (kind 0) — 8 bands × (On/Type/Freq/Gain/Q), param names "<band> <field>",
        //      band 1 = low shelf, 2/3 = bells, 4 = high shelf (defaults). Type: 0 LowCut,
        //      1 LowShelf, 2 Bell, 3 Notch, 4 HighShelf, 5 HighCut.
        Fx("eq", 0, "Bass Boost",     ("1 Type", 1f), ("1 Freq", 110f), ("1 Gain", 5f));
        Fx("eq", 0, "Air & Presence", ("3 Freq", 3000f), ("3 Gain", 2.5f), ("4 Type", 4f), ("4 Freq", 9000f), ("4 Gain", 5f));
        Fx("eq", 0, "Telephone",      ("1 Type", 0f), ("1 Freq", 450f), ("2 Freq", 1200f), ("2 Gain", 6f), ("2 Q", 1.2f), ("4 Type", 5f), ("4 Freq", 3000f));
        Fx("eq", 0, "Low Cut",        ("1 Type", 0f), ("1 Freq", 80f), ("1 Q", 0.7f));

        // ---- EQ-3 (kind 16) — performance EQ, all params normalized 0..1. Band gains are
        //      bipolar (0.5 = 0 dB, ±15 dB); "<band> Kill" = 1 mutes the band; crossovers
        //      "Low Freq" (50..2000 Hz) / "High Freq" (500..18000 Hz); "Slope" 0 = 24 / 1 = 48 dB.
        Fx("eq3", 16, "DJ Kill Bass",  ("Low Kill", 1f));
        Fx("eq3", 16, "Kill Highs",    ("High Kill", 1f));
        Fx("eq3", 16, "Bass Boost",    ("Low", 0.7f), ("High", 0.57f));
        Fx("eq3", 16, "Mid Scoop",     ("Mid", 0.3f), ("Low", 0.6f), ("High", 0.6f));
        Fx("eq3", 16, "Telephone",     ("Low Kill", 1f), ("High Kill", 1f), ("Mid", 0.6f), ("Low Freq", 0.49f), ("High Freq", 0.5f));
        Fx("eq3", 16, "Warm Up",       ("Low", 0.6f), ("High", 0.36f), ("Slope", 1f));

        // ---- Nota Forge (kind 17) — multi-stage saturation, all params normalized 0..1.
        //      Stage type: 0 Tube/0.2 Diode/0.4 Tape/0.6 Fuzz/0.8 Digital/1 Fold; gains/bias
        //      bipolar (0.5 = neutral); Routing 0 Serial/0.33 Par/0.67 M-S/1 Multiband.
        Fx("forge", 17, "Cabinet Heat",     ("Amount", 0.42f), ("Tone", 0.6f), ("Bias", 0.6f), ("S1 Type", 0f),   ("S1 Drive", 0.5f), ("S1 On", 1f), ("S2 On", 0f), ("S3 On", 0f));
        Fx("forge", 17, "Diode Crunch",     ("Amount", 0.5f),  ("S1 Type", 0.2f), ("S1 Drive", 0.68f), ("S1 On", 1f), ("S2 Type", 0.6f), ("S2 Drive", 0.4f), ("S2 On", 1f), ("S3 On", 0f));
        Fx("forge", 17, "Tape Glue",        ("Amount", 0.3f),  ("Wet", 0.85f), ("S1 Type", 0.4f), ("S1 Drive", 0.32f), ("S1 On", 1f), ("S2 On", 0f), ("S3 On", 0f));
        Fx("forge", 17, "Fuzz Wall",        ("Amount", 0.62f), ("S1 Type", 0.6f), ("S1 Drive", 0.85f), ("S1 FB", 0.3f), ("S1 On", 1f), ("S2 Type", 0.2f), ("S2 Drive", 0.5f), ("S2 On", 1f), ("S3 On", 0f));
        Fx("forge", 17, "Parallel Warmth",  ("Routing", 0.333f), ("Amount", 0.4f), ("S1 Type", 0f), ("S1 Drive", 0.5f), ("S1 On", 1f), ("S2 Type", 0.4f), ("S2 Drive", 0.35f), ("S2 On", 1f), ("S3 On", 0f));
        Fx("forge", 17, "Multiband Drive",  ("Routing", 1f), ("Amount", 0.45f), ("S1 Type", 0f), ("S1 Drive", 0.45f), ("S1 On", 1f), ("S2 Type", 0.4f), ("S2 Drive", 0.4f), ("S2 On", 1f), ("S3 Type", 0.8f), ("S3 Drive", 0.3f), ("S3 On", 1f), ("LFO Drive", 0.3f));

        // ---- Nota AutoGain (kind 18) — loudness matching, all params normalized 0..1.
        //      Target 0..1 → −36..0 LUFS; Scale 0 Mom/0.5 Short/1 Integ; Response 0 Fast/1 Slow.
        Fx("autogain", 18, "Stream −14 LUFS",   ("Target", 0.611f), ("Scale", 0.5f), ("Response", 1f), ("Safe", 1f));
        Fx("autogain", 18, "Podcast −16 LUFS",  ("Target", 0.556f), ("Scale", 1f),   ("Response", 1f), ("Window", 0.84f), ("Safe", 1f));
        Fx("autogain", 18, "Broadcast −23 LUFS",("Target", 0.361f), ("Scale", 1f),   ("Response", 1f), ("Max Gain", 0.6f), ("Safe", 1f));
        Fx("autogain", 18, "Club −9 LUFS",      ("Target", 0.75f),  ("Scale", 0.5f), ("Response", 0f), ("Max Gain", 0.75f), ("Safe", 1f));
        Fx("autogain", 18, "Fast Leveler",      ("Target", 0.611f), ("Scale", 0f),   ("Response", 0f), ("Max Gain", 0.5f), ("Safe", 1f));
        Fx("autogain", 18, "Match Reference",   ("Scale", 1f), ("Response", 1f), ("Window", 0.84f), ("Max Gain", 0.75f), ("Safe", 1f));

        // ---- Nota Shutter (kind 19) — noise gate, all params normalized 0..1. Threshold
        //      0..1 → −70..0 dB; Return 0..1 → 0..24 dB; Floor 0 = mute; Lookahead 0/0.5/1 = 0/1/5 ms.
        Fx("shutter", 19, "Tight Drums",     ("Threshold", 0.457f), ("Return", 0.125f), ("Attack", 0.175f), ("Hold", 0.588f), ("Release", 0.63f), ("Floor", 0f));
        Fx("shutter", 19, "Gentle Cleanup",  ("Threshold", 0.34f),  ("Return", 0.25f),  ("Attack", 0.4f),   ("Hold", 0.7f),   ("Release", 0.8f),  ("Floor", 0.4f));
        Fx("shutter", 19, "Vocal Gate",      ("Threshold", 0.5f),   ("Return", 0.3f),   ("Attack", 0.3f),   ("Hold", 0.75f),  ("Release", 0.72f), ("Floor", 0.3f), ("Det HP", 0.45f));
        Fx("shutter", 19, "Hard Slice",      ("Threshold", 0.6f),   ("Return", 0.1f),   ("Attack", 0.05f),  ("Hold", 0.3f),   ("Release", 0.3f),  ("Floor", 0f));
        Fx("shutter", 19, "Kick Duck",       ("Threshold", 0.42f),  ("Flip", 1f),       ("Attack", 0.2f),   ("Hold", 0.5f),   ("Release", 0.55f), ("Floor", 0.55f));
        Fx("shutter", 19, "Trance Gate",     ("Threshold", 0.55f),  ("Return", 0.08f),  ("Attack", 0.15f),  ("Hold", 0.45f),  ("Release", 0.4f),  ("Floor", 0f), ("Lookahead", 1f));

        // ---- Nota Chamber (kind 20) — hybrid reverb, all params normalized 0..1 (unnamed params
        //      reset to their defaults). IR = index/16 (0 Concert Hall · .0625 Stone Vault · .125
        //      Cathedral · .1875 Scoring Stage · .25 Wood Chamber · .3125 Live Room · .375 Drum Room ·
        //      .4375 Tiled Bathroom · .5 Vocal Plate · .5625 Bright Plate · .625 Spring Tank · .6875
        //      Car Park · .75 Stairwell · .8125 Forest · .875 Gated Room · .9375 Metal Tank · 1 user).
        //      Blend = algorithm share; Algo Mode 0 Dark Hall/.333 Plate/.667 Quartz/1 Shimmer;
        //      Algo Decay 0.2·100^v s; predelays 500·v² ms; IR Size 0.5·4^v; Algo Size 0.4·6.25^v;
        //      Damping 500·36^v Hz; Low/High Decay ×0.25·16^v; EQ Low Cut 20·100^v, High Cut 1k·20^v,
        //      gains (v−.5)·36 dB; Duck Amount v·24 dB; Output −24+36·v dB; Width v·200 %.
        Fx("chamber", 20, "Stone Vault",       ("IR", 0.0625f), ("Blend", 0.62f), ("Dry/Wet", 0.42f), ("Conv Predelay", 0.219f), ("IR Size", 0.619f), ("IR Attack", 0.19f), ("IR Decay", 0.74f),
                                               ("Algo Decay", 0.681f), ("Algo Size", 0.405f), ("Algo Diffusion", 0.62f), ("Algo Damping", 0.518f), ("Algo Predelay", 0.268f), ("Algo Low Decay", 0.595f), ("Algo High Decay", 0.235f),
                                               ("Duck Amount", 0.142f), ("Duck Release", 0.602f), ("EQ Low Cut", 0.389f), ("EQ High Cut", 0.8f));
        Fx("chamber", 20, "Concert Hall",      ("IR", 0f), ("Blend", 0.45f), ("Dry/Wet", 0.3f), ("Conv Predelay", 0.2f), ("Algo Decay", 0.6f), ("Algo Size", 0.6f), ("Algo Damping", 0.65f), ("EQ Low Cut", 0.301f));
        Fx("chamber", 20, "Cathedral Wash",    ("IR", 0.125f), ("Blend", 0.35f), ("Dry/Wet", 0.45f), ("Conv Predelay", 0.3f), ("IR Size", 0.6f), ("Algo Decay", 0.8f), ("Algo Size", 0.878f),
                                               ("Algo Diffusion", 0.85f), ("Algo Damping", 0.45f), ("Algo Low Decay", 0.646f), ("Mod Depth", 0.45f), ("Width", 0.7f));
        Fx("chamber", 20, "Scoring Stage",     ("IR", 0.1875f), ("Blend", 0.3f), ("Dry/Wet", 0.25f), ("Conv Predelay", 0.141f), ("Algo Decay", 0.5f), ("Algo Size", 0.55f));
        Fx("chamber", 20, "Wood Chamber",      ("IR", 0.25f), ("Blend", 0.25f), ("Dry/Wet", 0.25f), ("Conv Predelay", 0.1f), ("Algo Mode", 0.667f), ("Algo Decay", 0.4f), ("Algo Size", 0.3f));
        Fx("chamber", 20, "Drum Room",         ("IR", 0.375f), ("Blend", 0.1f), ("Dry/Wet", 0.22f), ("Conv Predelay", 0f), ("Algo Decay", 0.3f), ("EQ Low Cut", 0.437f), ("Duck Amount", 0.2f), ("Duck Release", 0.4f));
        Fx("chamber", 20, "Live Room",         ("IR", 0.3125f), ("Blend", 0.2f), ("Dry/Wet", 0.2f), ("Algo Decay", 0.35f), ("EQ Low Cut", 0.5f), ("EQ High Cut", 0.8f));
        Fx("chamber", 20, "Vocal Plate",       ("IR", 0.5f), ("Blend", 0.5f), ("Dry/Wet", 0.28f), ("Conv Predelay", 0.245f), ("Algo Mode", 0.333f), ("Algo Decay", 0.5f), ("Algo Size", 0.45f),
                                               ("Algo Diffusion", 0.8f), ("Algo Damping", 0.8f), ("Algo High Decay", 0.45f), ("Algo Predelay", 0.245f), ("EQ Low Cut", 0.5f), ("Duck Amount", 0.25f), ("Duck Release", 0.477f));
        Fx("chamber", 20, "Snare Plate",       ("IR", 0.5625f), ("Blend", 0.55f), ("Dry/Wet", 0.3f), ("Algo Mode", 0.333f), ("Algo Decay", 0.42f), ("Algo Damping", 0.85f), ("EQ Low Cut", 0.588f));
        Fx("chamber", 20, "Spring Box",        ("IR", 0.625f), ("Blend", 0f), ("Algo On", 0f), ("Dry/Wet", 0.3f), ("EQ Low Cut", 0.5f), ("EQ High Cut", 0.6f));
        Fx("chamber", 20, "Wide Shimmer",      ("IR", 0.125f), ("Blend", 0.48f), ("Dry/Wet", 0.55f), ("Algo Mode", 1f), ("Algo Decay", 0.8f), ("Algo Diffusion", 0.9f),
                                               ("Mod Rate", 0.419f), ("Mod Depth", 0.38f), ("Shimmer Amount", 0.52f), ("Shimmer Pitch", 1f), ("Shimmer Feedback", 1f),
                                               ("EQ Low Cut", 0.389f), ("EQ Low Gain", 0.389f), ("EQ High Shelf", 0.589f), ("EQ High Cut", 0.8f), ("Width", 0.64f), ("Bass Mono", 0.548f), ("Output", 0.611f));
        Fx("chamber", 20, "Octave Down Bloom", ("Blend", 1f), ("Conv On", 0f), ("Dry/Wet", 0.45f), ("Algo Mode", 1f), ("Algo Decay", 0.75f), ("Shimmer Amount", 0.45f), ("Shimmer Pitch", 0f),
                                               ("Shimmer Feedback", 0f), ("Algo Damping", 0.45f), ("Mod Depth", 0.4f));
        Fx("chamber", 20, "Endless Pad",       ("Blend", 1f), ("Conv On", 0f), ("Dry/Wet", 0.5f), ("Algo Mode", 1f), ("Algo Decay", 1f), ("Algo Size", 0.8f), ("Algo Diffusion", 0.9f),
                                               ("Shimmer Amount", 0.6f), ("Mod Depth", 0.5f), ("Width", 0.75f), ("EQ Low Cut", 0.5f));
        Fx("chamber", 20, "Reverse Swell",     ("IR", 0.0625f), ("IR Reverse", 1f), ("IR Decay", 0.55f), ("Blend", 0.15f), ("Dry/Wet", 0.4f), ("Conv Predelay", 0f), ("Algo Decay", 0.5f));
        Fx("chamber", 20, "Car Park",          ("IR", 0.6875f), ("Blend", 0.3f), ("Dry/Wet", 0.3f), ("Algo Mode", 0.667f), ("Algo Decay", 0.62f));
        Fx("chamber", 20, "Forest Echoes",     ("IR", 0.8125f), ("Blend", 0.2f), ("Dry/Wet", 0.3f), ("Algo Mode", 0.667f), ("Algo Decay", 0.45f), ("Algo Diffusion", 0.3f));
        Fx("chamber", 20, "Gated Snare",       ("IR", 0.875f), ("Blend", 0f), ("Algo On", 0f), ("Dry/Wet", 0.35f), ("EQ Low Cut", 0.5f));
        Fx("chamber", 20, "Metal Tank",        ("IR", 0.9375f), ("Blend", 0.2f), ("Dry/Wet", 0.3f), ("IR Size", 0.4f));
        Fx("chamber", 20, "Vintage Hall",      ("Blend", 0.85f), ("Dry/Wet", 0.3f), ("Algo Vintage", 1f), ("Algo Decay", 0.65f), ("Mod Rate", 0.55f), ("Mod Depth", 0.5f), ("EQ High Cut", 0.694f));
        Fx("chamber", 20, "Ducked Vocal Hall", ("IR", 0.1875f), ("Blend", 0.55f), ("Dry/Wet", 0.35f), ("Conv Predelay", 0.3f), ("Algo Decay", 0.62f), ("Duck Amount", 0.375f), ("Duck Release", 0.548f));
        Fx("chamber", 20, "Send · Big Hall",   ("IR", 0f), ("Blend", 0.5f), ("Wet Only", 1f), ("Conv Predelay", 0.2f), ("Algo Decay", 0.65f), ("EQ Low Cut", 0.437f), ("EQ High Cut", 0.8f), ("Bass Mono", 0.548f));
        Fx("chamber", 20, "Send · Plate",      ("IR", 0.5f), ("Blend", 0.5f), ("Wet Only", 1f), ("Algo Mode", 0.333f), ("Algo Decay", 0.5f), ("Conv Predelay", 0.2f), ("Algo Predelay", 0.2f), ("EQ Low Cut", 0.5f));

        // ---- Nota Prism (kind 21) — three-band dynamics, all params normalized 0..1 (unnamed params
        //      reset to their defaults: every band compresses −18 dB 2:1). Bands 0 = 3 / .5 = 2 / 1 = 1;
        //      Crossover 20·1000^v Hz (180 Hz .318, 2.4 kHz .693); Above Thresh −60+60·v dB; Above Ratio
        //      = 1 − 1/ratio (.5 = 2:1, .75 = 4:1, 1 = ∞:1, 0 = off); Below Thresh −80+80·v dB; Below Ratio
        //      4^((v−.5)·2) (.5 = 1:1, .75 = 2:1 expansion, .25 = 2:1 upward); Attack 0.1·3000^v ms;
        //      Release 5·600^v ms; Gain / Output (v−.5)·48 dB; Floor v·48 dB; Knee v·24 dB; Lookahead v·10 ms.
        Fx("prism", 21, "Bus Glue",           ("Amount", 0.74f), ("Output", 0.525f), ("Soft Clip", 1f),
                                              ("Low Above Thresh", 0.767f), ("Low Above Ratio", 0.667f), ("Low Gain", 0.529f),
                                              ("Mid Below On", 1f), ("Mid Below Thresh", 0.475f), ("Mid Below Ratio", 0.6214f),
                                              ("High Above Thresh", 0.6f), ("High Above Ratio", 0.75f), ("High Gain", 0.483f));
        Fx("prism", 21, "Upward Squash",      ("Amount", 0.8f), ("Mix", 0.65f), ("Output", 0.4375f), ("Soft Clip", 1f),
                                              ("Low Above Thresh", 0.533f), ("Low Above Ratio", 0.9f), ("Low Attack", 0.4248f), ("Low Release", 0.4334f), ("Low Gain", 0.5625f),
                                              ("Low Below On", 1f), ("Low Below Ratio", 0f), ("Low Below Attack", 0.4248f), ("Low Below Release", 0.4334f), ("Low Floor", 0.75f),
                                              ("Mid Above Thresh", 0.533f), ("Mid Above Ratio", 0.9f), ("Mid Attack", 0.4248f), ("Mid Release", 0.4334f),
                                              ("Mid Below On", 1f), ("Mid Below Ratio", 0f), ("Mid Below Attack", 0.4248f), ("Mid Below Release", 0.4334f), ("Mid Floor", 0.75f),
                                              ("High Above Thresh", 0.533f), ("High Above Ratio", 0.9f), ("High Attack", 0.4248f), ("High Release", 0.4334f), ("High Gain", 0.5625f),
                                              ("High Below On", 1f), ("High Below Ratio", 0f), ("High Below Attack", 0.4248f), ("High Below Release", 0.4334f), ("High Floor", 0.75f));
        Fx("prism", 21, "Vocal Control",      ("Amount", 0.8f), ("Detect", 1f), ("Lookahead", 0.3f), ("Output", 0.55f), ("Auto Makeup", 1f),
                                              ("Crossover Low", 0.2917f), ("Crossover High", 0.7993f),
                                              ("Low Above Thresh", 0.6f), ("Low Above Ratio", 0.75f), ("Low Attack", 0.7038f), ("Low Release", 0.6596f), ("Low Auto Release", 1f), ("Low Gain", 0.479f),
                                              ("Mid Above Thresh", 0.667f), ("Mid Attack", 0.5752f), ("Mid Release", 0.6052f),
                                              ("High Above Thresh", 0.5f), ("High Above Ratio", 0.667f), ("High Attack", 0.4248f), ("High Release", 0.4968f), ("High Auto Release", 1f));
        Fx("prism", 21, "De-Esser",           ("Crossover Low", 0.534f), ("Crossover High", 0.8257f), ("Lookahead", 0.2f),
                                              ("Low Above Ratio", 0f), ("Mid Above Ratio", 0f),
                                              ("High Above Thresh", 0.5f), ("High Above Ratio", 0.833f), ("High Attack", 0.2876f), ("High Release", 0.36f), ("High Knee", 0.125f));
        Fx("prism", 21, "Low-End Tamer",      ("Crossover Low", 0.2594f), ("Crossover High", 0.699f),
                                              ("Low Above Thresh", 0.6f), ("Low Above Ratio", 0.75f), ("Low Attack", 0.6618f), ("Low Release", 0.5317f),
                                              ("Mid Above Ratio", 0f), ("High Above Ratio", 0f));
        Fx("prism", 21, "Drum Punch",         ("Crossover Low", 0.3333f), ("Crossover High", 0.7253f), ("Output", 0.52f),
                                              ("Low Above Thresh", 0.8f), ("Low Attack", 0.7124f), ("Low Release", 0.4683f), ("Low Gain", 0.5417f),
                                              ("Low Below On", 1f), ("Low Below Thresh", 0.5625f), ("Low Below Ratio", 0.75f), ("Low Floor", 0.25f),
                                              ("Mid Above Thresh", 0.8f), ("Mid Attack", 0.6618f), ("Mid Release", 0.4334f),
                                              ("Mid Below On", 1f), ("Mid Below Thresh", 0.5625f), ("Mid Below Ratio", 0.75f), ("Mid Floor", 0.25f),
                                              ("High Above Thresh", 0.8f), ("High Attack", 0.5752f), ("High Release", 0.36f),
                                              ("High Below On", 1f), ("High Below Thresh", 0.5f), ("High Below Ratio", 0.75f), ("High Floor", 0.25f));
        Fx("prism", 21, "Gentle Master",      ("Lookahead", 0.5f), ("Soft Clip", 1f), ("Crossover Low", 0.3333f), ("Crossover High", 0.7253f),
                                              ("Low Above Thresh", 0.667f), ("Low Above Ratio", 0.333f), ("Low Attack", 0.7124f), ("Low Release", 0.6401f), ("Low Knee", 0.5f), ("Low Auto Release", 1f),
                                              ("Mid Above Thresh", 0.667f), ("Mid Above Ratio", 0.333f), ("Mid Attack", 0.7124f), ("Mid Release", 0.6401f), ("Mid Knee", 0.5f), ("Mid Auto Release", 1f),
                                              ("High Above Thresh", 0.667f), ("High Above Ratio", 0.333f), ("High Attack", 0.6618f), ("High Release", 0.5767f), ("High Knee", 0.5f), ("High Auto Release", 1f));
        Fx("prism", 21, "Noise Floor Cleanup",("Low Above Ratio", 0f), ("Mid Above Ratio", 0f), ("High Above Ratio", 0f),
                                              ("Low Below On", 1f), ("Low Below Thresh", 0.375f), ("Low Below Ratio", 0.8962f),
                                              ("Mid Below On", 1f), ("Mid Below Thresh", 0.375f), ("Mid Below Ratio", 0.8962f),
                                              ("High Below On", 1f), ("High Below Thresh", 0.4375f), ("High Below Ratio", 0.8962f), ("High Floor", 0.625f));
        Fx("prism", 21, "Air Lift",           ("Crossover High", 0.8257f), ("Low Above Ratio", 0f), ("Mid Above Ratio", 0f),
                                              ("High Above Thresh", 0.6f), ("High Above Ratio", 0.5f), ("High Gain", 0.5417f),
                                              ("High Below On", 1f), ("High Below Thresh", 0.4375f), ("High Below Ratio", 0.25f), ("High Floor", 0.25f));
        Fx("prism", 21, "Two-Band Bass",      ("Bands", 0.5f), ("Crossover Low", 0.3333f), ("Detect", 1f),
                                              ("Low Above Thresh", 0.667f), ("Low Above Ratio", 0.75f), ("Low Attack", 0.6618f), ("Low Release", 0.5767f),
                                              ("Mid Above Thresh", 0.6f), ("Mid Attack", 0.4886f), ("Mid Release", 0.4683f));
        Fx("prism", 21, "Single-Band Leveler",("Bands", 1f), ("Detect", 1f), ("Auto Makeup", 1f),
                                              ("Mid Above Thresh", 0.6f), ("Mid Above Ratio", 0.667f), ("Mid Attack", 0.7124f), ("Mid Release", 0.6401f), ("Mid Auto Release", 1f), ("Mid Knee", 0.5f));
        Fx("prism", 21, "Parallel Crush",     ("Mix", 0.4f), ("Output", 0.54f),
                                              ("Low Above Thresh", 0.5f), ("Low Above Ratio", 0.9f), ("Low Attack", 0.4248f), ("Low Release", 0.4334f), ("Low Gain", 0.625f),
                                              ("Mid Above Thresh", 0.5f), ("Mid Above Ratio", 0.9f), ("Mid Attack", 0.4248f), ("Mid Release", 0.4334f), ("Mid Gain", 0.625f),
                                              ("High Above Thresh", 0.5f), ("High Above Ratio", 0.9f), ("High Attack", 0.4248f), ("High Release", 0.4334f), ("High Gain", 0.625f));

        // ---- Compressor (kind 1) — Thresh -60..0, Ratio 1..20, Attack .1..100 ms, Release 5..1000 ms, Makeup 0..24 dB
        // Character: Clean 0 / Glue 1 / Punch 2 / Opto 3 / FET 4.
        Fx("comp", 1, "Drum Glue",   ("Character", 1f), ("Thresh", -18f), ("Ratio", 3f),  ("Attack", 30f),  ("Release", 200f), ("Knee", 8f),  ("AutoGain", 1f), ("Mix", 100f));
        Fx("comp", 1, "Drum Punch",  ("Character", 2f), ("Thresh", -24f), ("Ratio", 4f),  ("Attack", 8f),   ("Release", 90f),  ("Knee", 2f),  ("Makeup", 5f));
        Fx("comp", 1, "Vocal Opto",  ("Character", 3f), ("Thresh", -20f), ("Ratio", 3f),  ("Attack", 12f),  ("Release", 180f), ("Knee", 8f),  ("AutoRelease", 1f), ("Makeup", 4f));
        Fx("comp", 1, "Bus Parallel",("Character", 0f), ("Thresh", -30f), ("Ratio", 6f),  ("Attack", 5f),   ("Release", 120f), ("Mix", 45f),  ("Makeup", 3f));
        Fx("comp", 1, "FET Slam",    ("Character", 4f), ("Thresh", -16f), ("Ratio", 8f),  ("Attack", 1f),   ("Release", 80f),  ("Knee", 1f),  ("Lookahead", 2f), ("Makeup", 4f));
        Fx("comp", 1, "Brick Limit", ("Character", 4f), ("Thresh", -8f),  ("Ratio", 20f), ("Attack", 0.5f), ("Release", 50f),  ("Lookahead", 4f), ("Range", 12f), ("Makeup", 2f));

        // ---- Reverb (kind 2) — all params normalized 0..1. Algorithm: Hall 0 / Room .33 / Plate .66 / Chamber 1.
        Fx("reverb", 2, "Small Room",  ("Algorithm", 0.33f), ("Decay", 0.32f), ("HF Damp", 0.55f), ("Size", 0.35f), ("Diffusion", 0.6f), ("Dry/Wet", 0.2f));
        Fx("reverb", 2, "Long Hall",   ("Algorithm", 0.0f),  ("Decay", 0.72f), ("HF Damp", 0.4f),  ("Pre-Delay", 0.12f), ("Size", 0.85f), ("Width", 0.8f), ("Dry/Wet", 0.32f));
        Fx("reverb", 2, "Bright Plate",("Algorithm", 0.66f), ("Decay", 0.5f),  ("HF Damp", 0.15f), ("High Cut", 1.0f), ("Diffusion", 0.8f), ("Dry/Wet", 0.3f));
        Fx("reverb", 2, "Shimmer Freeze",("Algorithm", 0.0f), ("Freeze", 1.0f), ("Mod Rate", 0.4f), ("Mod Depth", 0.6f), ("Width", 1.0f), ("Dry/Wet", 0.5f));

        // ---- Delay (kind 3) — all params normalized 0..1. Div: 1/16 0 … 1/8 .29 … 1/4 .71 … 1/2 1.
        Fx("delay", 3, "Slapback",    ("Sync", 0.0f), ("Time L", 0.06f), ("Time R", 0.06f), ("Feedback", 0.12f), ("Dry/Wet", 0.25f));
        Fx("delay", 3, "Dub Eighths", ("Sync", 1.0f), ("Div L", 0.286f), ("Div R", 0.286f), ("Feedback", 0.58f), ("Ping-Pong", 1.0f), ("Wow Depth", 0.3f), ("Dry/Wet", 0.35f));
        Fx("delay", 3, "Ping Quarter",("Sync", 1.0f), ("Div L", 0.714f), ("Div R", 0.714f), ("Feedback", 0.45f), ("Ping-Pong", 1.0f), ("Spread", 0.3f), ("Dry/Wet", 0.3f));

        // ---- Utility (kind 4) — Gain -24..24 dB · Balance -1..1 · Width 0..400% ·
        //      Channel Mode 0..3 (Stereo/Left/Right/Swap) · Mono Freq 20..2000 Hz · toggles 0/1
        Fx("util", 4, "Stereo Widener", ("Gain", 0f),  ("Width", 175f), ("Balance", 0f));
        Fx("util", 4, "Bass Mono",      ("Width", 130f), ("Mono Below", 1f), ("Mono Freq", 140f));
        Fx("util", 4, "Mono Maker",     ("Width", 100f), ("Mono Below", 1f), ("Mono Freq", 500f));
        Fx("util", 4, "Narrow",         ("Width", 55f));
        Fx("util", 4, "Trim −6 dB",     ("Gain", -6f));
        Fx("util", 4, "Swap L/R",       ("Channel Mode", 3f));

        // ---- Amplifier (kind 6) — Model 0..6, Gain/tone/Output 0..10, Mix 0..1;
        //      Cabinet 0 Match/1 1×12/2 2×12/3 4×12/4 1×15; Mic 0 Dyn/1 Cond/2 Rib; Axis/Gate 0..1
        Fx("amp", 6, "Clean Combo",   ("Model", 0f), ("Gain", 2.0f), ("Bass", 5f), ("Middle", 5f),   ("Treble", 6f), ("Presence", 5f), ("Output", 5f),   ("Mix", 1f), ("Cabinet", 1f), ("Mic", 1f));
        Fx("amp", 6, "Blues Breakup", ("Model", 2f), ("Gain", 5.0f), ("Bass", 5f), ("Middle", 6f),   ("Treble", 5f), ("Presence", 5f), ("Output", 5f),   ("Mix", 1f), ("Cabinet", 2f));
        Fx("amp", 6, "Rock Crunch",   ("Model", 3f), ("Gain", 6.0f), ("Bass", 6f), ("Middle", 5f),   ("Treble", 6f), ("Presence", 6f), ("Output", 4.5f), ("Mix", 1f), ("Cabinet", 3f));
        Fx("amp", 6, "Lead Sustain",  ("Model", 4f), ("Gain", 7.5f), ("Bass", 5f), ("Middle", 7f),   ("Treble", 6f), ("Presence", 6f), ("Output", 4.5f), ("Mix", 1f), ("Cabinet", 3f), ("Gate", 0.25f));
        Fx("amp", 6, "Heavy Chug",    ("Model", 5f), ("Gain", 8.0f), ("Bass", 7f), ("Middle", 3.5f), ("Treble", 6f), ("Presence", 7f), ("Output", 4f),   ("Mix", 1f), ("Cabinet", 3f), ("Gate", 0.4f), ("Axis", 0.3f));
        Fx("amp", 6, "Bass Amp",      ("Model", 6f), ("Gain", 3.0f), ("Bass", 7f), ("Middle", 5f),   ("Treble", 4f), ("Presence", 3f), ("Output", 5f),   ("Mix", 1f), ("Cabinet", 4f));

        // ---- Auto Filter (kind 7) — all params normalized 0..1. Type 0 LP/.33 BP/.67 HP/
        //      1 Notch; Slope 0=12dB/1=24dB; Env Amt 0.5=0 (bipolar); LFO Wave 0..1 in .25 steps.
        Fx("autofilter", 7, "Envelope Wah",   ("Type", 0f),    ("Freq", 0.32f), ("Res", 0.55f), ("Env Amt", 0.85f), ("Env Attack", 0.08f), ("Env Release", 0.40f), ("Drive", 0.18f), ("Dry/Wet", 1f));
        Fx("autofilter", 7, "Slow LFO Sweep", ("Type", 0f),    ("Freq", 0.48f), ("Res", 0.35f), ("Env Amt", 0.5f),  ("LFO Amt", 0.7f),  ("LFO Rate", 0.22f), ("LFO Wave", 0f),   ("Dry/Wet", 1f));
        Fx("autofilter", 7, "24 dB Low Cut",  ("Type", 0.667f),("Slope", 1f),   ("Freq", 0.22f), ("Res", 0.12f),    ("Env Amt", 0.5f),  ("Dry/Wet", 1f));
        Fx("autofilter", 7, "Sidechain Duck", ("Type", 0f),    ("Freq", 0.72f), ("Res", 0.20f), ("Env Amt", 0.14f), ("Env Attack", 0.05f), ("Env Release", 0.45f), ("Dry/Wet", 1f));
        Fx("autofilter", 7, "S&H Random",     ("Type", 0f),    ("Freq", 0.50f), ("Res", 0.42f), ("LFO Amt", 0.62f), ("LFO Rate", 0.42f), ("LFO Wave", 1f),   ("Dry/Wet", 1f));
        Fx("autofilter", 7, "Notch Motion",   ("Type", 1f),    ("Freq", 0.55f), ("Res", 0.30f), ("Morph", 0.4f),   ("LFO Amt", 0.5f),  ("LFO Rate", 0.3f),  ("LFO Wave", 1f), ("Dry/Wet", 1f));

        // ---- Nota Vintage (kind 8) — all params normalized 0..1. Mode 0 Vinyl/.2 Cassette/
        //      .4 Reel/.6 VHS/.8 Tube/1 Analog; Tone 0.5 = neutral tilt; Output 0.5 = 0 dB.
        Fx("vintage", 8, "Dusty Vinyl",   ("Mode", 0.0f), ("Drive", 0.35f), ("Tone", 0.42f), ("Wow", 0.35f), ("Flutter", 0.20f), ("Noise", 0.30f), ("Crackle", 0.55f), ("Wear", 0.25f), ("Mix", 1f), ("Output", 0.5f));
        Fx("vintage", 8, "Warped Cassette",("Mode", 0.2f), ("Drive", 0.45f), ("Tone", 0.40f), ("Wow", 0.45f), ("Flutter", 0.55f), ("Noise", 0.45f), ("Crackle", 0.10f), ("Wear", 0.40f), ("Mix", 1f), ("Output", 0.5f));
        Fx("vintage", 8, "Reel Warmth",   ("Mode", 0.4f), ("Drive", 0.55f), ("Tone", 0.52f), ("Wow", 0.25f), ("Flutter", 0.20f), ("Noise", 0.18f), ("Crackle", 0.05f), ("Wear", 0.15f), ("Mix", 1f), ("Output", 0.5f));
        Fx("vintage", 8, "VHS Fever",     ("Mode", 0.6f), ("Drive", 0.40f), ("Tone", 0.32f), ("Wow", 0.45f), ("Flutter", 0.70f), ("Noise", 0.50f), ("Crackle", 0.30f), ("Wear", 0.55f), ("Mix", 1f), ("Output", 0.55f));
        Fx("vintage", 8, "Tube Glow",     ("Mode", 0.8f), ("Drive", 0.60f), ("Tone", 0.58f), ("Wow", 0f),     ("Flutter", 0f),     ("Noise", 0.08f), ("Crackle", 0f),    ("Wear", 0.05f), ("Mix", 1f), ("Output", 0.5f));
        Fx("vintage", 8, "Analog Glue",   ("Mode", 1.0f), ("Drive", 0.45f), ("Tone", 0.50f), ("Wow", 0f),     ("Flutter", 0f),     ("Noise", 0.05f), ("Crackle", 0f),    ("Wear", 0f),    ("Mix", 1f), ("Output", 0.5f));

        // ---- Nota Auto Pan (kind 9) — all params normalized 0..1. Waveform 0 Sine/.25 Tri/
        //      .5 Saw/.75 Sqr/1 S&H; Phase 0.5 = 180° (pan), 0 = tremolo; Rate exp 0.01..40 Hz.
        Fx("autopan", 9, "Classic Pan",   ("Rate", 0.60f), ("Amount", 0.80f), ("Waveform", 0f),    ("Shape", 0f),    ("Phase", 0.5f), ("Mix", 1f));
        Fx("autopan", 9, "Tremolo",       ("Rate", 0.70f), ("Amount", 0.70f), ("Waveform", 0f),    ("Shape", 0f),    ("Phase", 0f),   ("Mix", 1f));
        Fx("autopan", 9, "Chop Gate",     ("Rate", 0.72f), ("Amount", 1.0f),  ("Waveform", 0.75f), ("Shape", 0f),    ("Phase", 0f),   ("Mix", 1f));
        Fx("autopan", 9, "Slow Sweep",    ("Rate", 0.40f), ("Amount", 0.90f), ("Waveform", 0f),    ("Shape", 0f),    ("Phase", 0.5f), ("Mix", 1f));
        Fx("autopan", 9, "Random Space",  ("Rate", 0.62f), ("Amount", 0.75f), ("Waveform", 1f),    ("Shape", 0f),    ("Phase", 0.5f), ("Mix", 0.85f));
        Fx("autopan", 9, "Hard Square",   ("Rate", 0.66f), ("Amount", 0.85f), ("Waveform", 0f),    ("Shape", 0.9f),  ("Phase", 0.5f), ("Mix", 1f));

        // ---- Nota Auto Shift (kind 10) — all params normalized 0..1. Key 0 C..1 B;
        //      Scale 0 Chromatic/.25 Major/.5 Minor/.75 Pent Maj/1 Pent Min; Shift 0.5 = 0 st.
        Fx("autoshift", 10, "Hard Tune",      ("Key", 0f),     ("Scale", 0.25f), ("Amount", 1.0f), ("Speed", 0.0f),  ("Shift", 0.5f), ("Mix", 1f));
        Fx("autoshift", 10, "Natural Vocal",  ("Key", 0f),     ("Scale", 0.25f), ("Amount", 0.7f), ("Speed", 0.45f), ("Shift", 0.5f), ("Mix", 1f));
        Fx("autoshift", 10, "Minor Key",      ("Key", 0.818f), ("Scale", 0.5f),  ("Amount", 0.9f), ("Speed", 0.2f),  ("Shift", 0.5f), ("Mix", 1f));
        Fx("autoshift", 10, "Chromatic Fix",  ("Key", 0f),     ("Scale", 0f),    ("Amount", 0.8f), ("Speed", 0.25f), ("Shift", 0.5f), ("Mix", 1f));
        Fx("autoshift", 10, "Octave Up",      ("Key", 0f),     ("Scale", 0.25f), ("Amount", 0.6f), ("Speed", 0.3f),  ("Shift", 1.0f), ("Mix", 0.5f));
        Fx("autoshift", 10, "Pentatonic Pop", ("Key", 0.583f), ("Scale", 0.75f), ("Amount", 1.0f), ("Speed", 0.1f),  ("Shift", 0.5f), ("Mix", 1f));

        // ---- Nota Beat Repeat (kind 11) — all params normalized 0..1. Interval 0 1/8..1 4 Bar
        //      (.6=1 Bar); Grid 0 1/4..1 1/16T (.4=1/16); Mode 0 Mix/.5 Insert/1 Gate.
        Fx("beatrepeat", 11, "Classic Stutter", ("Interval", 0.6f), ("Grid", 0.4f), ("Gate", 0.5f), ("Chance", 1f),   ("Mode", 0.5f), ("Volume", 0.5f));
        Fx("beatrepeat", 11, "Glitch Gate",     ("Interval", 0.4f), ("Grid", 0.6f), ("Gate", 0.7f), ("Chance", 0.6f), ("Variation", 0.3f), ("Mode", 1f), ("Volume", 0.5f));
        Fx("beatrepeat", 11, "Pitch Drop",      ("Interval", 0.6f), ("Grid", 0.4f), ("Gate", 0.6f), ("Chance", 1f),   ("Pitch", 0.5f), ("Pitch Decay", 0.6f), ("Mode", 0.5f), ("Volume", 0.5f));
        Fx("beatrepeat", 11, "Filtered Chops",  ("Interval", 0.6f), ("Grid", 0.4f), ("Gate", 0.5f), ("Chance", 1f),   ("Filter On", 1f), ("Filter Freq", 0.4f), ("Filter Width", 0.6f), ("Mode", 0.5f), ("Volume", 0.5f));
        Fx("beatrepeat", 11, "Triplet Tumble",  ("Interval", 0.6f), ("Grid", 0.8f), ("Gate", 0.6f), ("Chance", 0.8f), ("Variation", 0.4f), ("Decay", 0.3f), ("Mode", 0.5f), ("Volume", 0.5f));
        Fx("beatrepeat", 11, "Half-Bar Roll",   ("Interval", 0.4f), ("Grid", 0.2f), ("Gate", 1f),   ("Chance", 1f),   ("Decay", 0.4f), ("Mode", 0f), ("Volume", 0.5f));

        // ---- Nota Crush (kind 12) — all params normalized 0..1. Mode 0 Digital/.5 Analog/1 Fold.
        Fx("crush", 12, "Broken Radio",    ("Bits", 0.35f), ("Rate", 0.30f), ("Mode", 0f),    ("Dither", 0.15f), ("Jitter", 0.10f), ("Noise Floor", 0.05f), ("Post Filter", 0.70f), ("Dry/Wet", 0.78f), ("Output", 0.5f));
        Fx("crush", 12, "8-Bit Arcade",    ("Bits", 0.30f), ("Rate", 0.25f), ("Mode", 0f),    ("Dither", 0.05f), ("Jitter", 0f),    ("Noise Floor", 0f),    ("Post Filter", 0.85f), ("Dry/Wet", 1f),   ("Output", 0.5f));
        Fx("crush", 12, "Lo-Fi Warmth",    ("Bits", 0.45f), ("Rate", 0.40f), ("Mode", 1f),    ("Dither", 0.30f), ("Jitter", 0.15f), ("Noise Floor", 0.10f), ("Post Filter", 0.60f), ("Dry/Wet", 0.65f), ("Output", 0.5f));
        Fx("crush", 12, "Digital Grit",    ("Bits", 0.20f), ("Rate", 0.15f), ("Mode", 0f),    ("Dither", 0f),    ("Jitter", 0.25f), ("Noise Floor", 0.15f), ("Post Filter", 0.50f), ("Dry/Wet", 0.85f), ("Output", 0.5f));
        Fx("crush", 12, "Folded Crunch",   ("Bits", 0.40f), ("Rate", 0.50f), ("Mode", 1f),    ("Dither", 0.10f), ("Jitter", 0.05f), ("Noise Floor", 0.05f), ("Post Filter", 0.75f), ("Dry/Wet", 0.70f), ("Output", 0.5f));
        Fx("crush", 12, "Telephone Line",  ("Bits", 0.15f), ("Rate", 0.10f), ("Mode", 0f),    ("Dither", 0f),    ("Jitter", 0f),    ("Noise Floor", 0.20f), ("Post Filter", 0.40f), ("Dry/Wet", 1f),   ("Output", 0.5f));

        // ---- Nota Dynamic EQ-8 (kind 13) — raw units, param names "<band> <field>".
        //      "n Mode": 0 Static / 1 Above (duck) / 2 Below (lift). "n Rng" signed dB.
        Fx("dyneq", 13, "De-Ess",         ("6 On", 1f), ("6 Type", 2f), ("6 Freq", 7000f), ("6 Q", 3.5f), ("6 Mode", 1f), ("6 Thr", -28f), ("6 Rng", -8f), ("6 Atk", 1f),  ("6 Rel", 60f));
        Fx("dyneq", 13, "De-Harsh Vox",   ("4 Freq", 3000f), ("4 Q", 2.4f), ("4 Mode", 1f), ("4 Thr", -24f), ("4 Rng", -6f), ("4 Atk", 5f), ("4 Rel", 120f));
        Fx("dyneq", 13, "Bass Control",   ("2 Freq", 90f), ("2 Q", 1.0f), ("2 Mode", 1f), ("2 Thr", -20f), ("2 Rng", -6f), ("2 Atk", 8f), ("2 Rel", 140f));
        Fx("dyneq", 13, "Vocal Presence", ("4 Freq", 3500f), ("4 Q", 1.2f), ("4 Mode", 2f), ("4 Thr", -30f), ("4 Rng", 4f), ("4 Atk", 12f), ("4 Rel", 180f), ("7 Type", 4f), ("7 Freq", 11000f), ("7 Gain", 2f));
        Fx("dyneq", 13, "Warm Master",    ("2 Type", 1f), ("2 Freq", 110f), ("2 Gain", 1.8f), ("7 Freq", 10000f), ("7 Mode", 1f), ("7 Thr", -18f), ("7 Rng", -3f), ("7 Atk", 20f), ("7 Rel", 250f));
        Fx("dyneq", 13, "Punch Tighten",  ("3 Freq", 800f), ("3 Q", 1.2f), ("3 Mode", 1f), ("3 Thr", -22f), ("3 Rng", -5f), ("3 Atk", 3f), ("3 Rel", 90f));

        // ---- Nota Ceiling (kind 14) — raw units. Character 0 Clean/1 Punch/2 Glue.
        Fx("ceiling", 14, "Master Safe",   ("Ceiling", -1.0f), ("Gain", 0f),   ("Release", 120f), ("Character", 0f), ("Lookahead", 3f),   ("StereoLink", 100f));
        Fx("ceiling", 14, "Loud Master",   ("Ceiling", -1.0f), ("Gain", 4.5f), ("Release", 100f), ("Character", 0f), ("Lookahead", 3f),   ("StereoLink", 100f));
        Fx("ceiling", 14, "Streaming −1",  ("Ceiling", -1.0f), ("Gain", 2f),   ("Release", 150f), ("Character", 2f), ("Lookahead", 4f),   ("StereoLink", 100f), ("AutoRelease", 1f));
        Fx("ceiling", 14, "Drum Punch",    ("Ceiling", -0.3f), ("Gain", 6f),   ("Release", 60f),  ("Character", 1f), ("Lookahead", 1.5f), ("StereoLink", 60f));
        Fx("ceiling", 14, "Glue Bus",      ("Ceiling", -0.5f), ("Gain", 3f),   ("Release", 250f), ("Character", 2f), ("Lookahead", 5f),   ("StereoLink", 100f), ("AutoRelease", 1f));
        Fx("ceiling", 14, "Transparent Safety", ("Ceiling", 0f), ("Gain", 0f), ("Release", 80f),  ("Character", 0f), ("Lookahead", 2f),   ("StereoLink", 100f));

        // ---- Nota Strata (kind 15) — looper settings only (recorded audio isn't a preset).
        //      Quantize 0 Off / 1 Bar / 2 1-4.
        Fx("strata", 15, "Live Set",     ("Feedback", 100f), ("InputGain", 0f), ("Speed", 1f),   ("Quantize", 1f), ("CountIn", 0f), ("SetTempo", 0f), ("Reverse", 0f));
        Fx("strata", 15, "Count-In Jam",  ("Feedback", 100f), ("InputGain", 2f), ("Speed", 1f),   ("Quantize", 1f), ("CountIn", 1f), ("SetTempo", 1f), ("Reverse", 0f));
        Fx("strata", 15, "Ambient Decay", ("Feedback", 70f),  ("InputGain", 0f), ("Speed", 1f),   ("Quantize", 1f), ("CountIn", 0f), ("SetTempo", 0f), ("Reverse", 0f));
        Fx("strata", 15, "Octave Down",   ("Feedback", 100f), ("InputGain", 0f), ("Speed", 0.5f), ("Quantize", 1f), ("CountIn", 0f), ("SetTempo", 0f), ("Reverse", 0f));
        Fx("strata", 15, "Reverse Tape",  ("Feedback", 90f),  ("InputGain", 0f), ("Speed", 1f),   ("Quantize", 1f), ("CountIn", 0f), ("SetTempo", 0f), ("Reverse", 1f));
        Fx("strata", 15, "Free Overdub",  ("Feedback", 100f), ("InputGain", 0f), ("Speed", 1f),   ("Quantize", 0f), ("CountIn", 0f), ("SetTempo", 0f), ("Reverse", 0f));

        // ---- Nota Arp (MIDI, kind 0) — Rate idx (5=1/16, 6=1/16T); Order 0 Up/1 Down/
        //      2 UpDown/3 Converge/4 AsPlayed/5 Chord/6 Random; OctMode/LoopMode 0..3.
        Midi("arp", 0, "Up 1/16",       ("Rate", 5f), ("Order", 0f), ("Octaves", 1f), ("Gate", 0.9f), ("Loop", 16f));
        Midi("arp", 0, "Octave Up-Down",("Rate", 5f), ("Order", 2f), ("Octaves", 2f), ("OctMode", 2f), ("Gate", 0.85f));
        Midi("arp", 0, "Triplet Roll",  ("Rate", 6f), ("Order", 0f), ("Octaves", 1f), ("Gate", 0.8f));
        Midi("arp", 0, "Trance Gate",   ("Rate", 5f), ("Order", 5f), ("Gate", 0.5f), ("Loop", 16f),
                                        ("On 2", 0f), ("On 4", 0f), ("On 6", 0f), ("On 8", 0f),
                                        ("On 10", 0f), ("On 12", 0f), ("On 14", 0f), ("On 16", 0f));
        Midi("arp", 0, "Random Walk",   ("Rate", 5f), ("Order", 6f), ("Octaves", 2f), ("OctMode", 3f), ("LoopMode", 3f), ("Gate", 0.8f));
        Midi("arp", 0, "Ratchet Build", ("Rate", 5f), ("Order", 0f), ("Octaves", 1f), ("Gate", 0.9f), ("Loop", 8f),
                                        ("Rat 1", 1f), ("Rat 2", 1f), ("Rat 3", 2f), ("Rat 4", 2f),
                                        ("Rat 5", 3f), ("Rat 6", 3f), ("Rat 7", 4f), ("Rat 8", 4f));

        // ---- Nota Chord (MIDI, kind 1) — Voice N semitone offsets (0 = off) + Strum/Spread/Fold
        Midi("chord", 1, "Major Triad",  ("Voice 1", 4f), ("Voice 2", 7f));
        Midi("chord", 1, "Minor Triad",  ("Voice 1", 3f), ("Voice 2", 7f));
        Midi("chord", 1, "Power Chord",  ("Voice 1", 7f), ("Voice 2", 12f));
        Midi("chord", 1, "Octaves",      ("Voice 1", 12f), ("Voice 2", -12f));
        Midi("chord", 1, "Maj7 Wide",    ("Voice 1", 4f), ("Voice 2", 7f), ("Voice 3", 11f), ("Voice 4", 16f), ("Spread", 40f));
        Midi("chord", 1, "Strummed Guitar", ("Voice 1", 7f), ("Voice 2", 12f), ("Voice 3", 16f), ("Voice 4", 19f), ("Strum", 22f), ("Vel 4", -20f));
        // ---- Nota Scale (MIDI, kind 2) — Root 0..11, Scale 0..9 preset / 10 Custom; Fold 0 Near/1 Down/2 Up
        Midi("scale", 2, "C Minor",      ("Root", 0f), ("Scale", 1f));
        Midi("scale", 2, "Penta Minor",  ("Root", 0f), ("Scale", 8f), ("Fold", 1f));
        Midi("scale", 2, "Dorian Up",    ("Root", 0f), ("Scale", 3f), ("Fold", 2f));
        // ---- Nota Length (MIDI, kind 3) — Mode 0 Sync/1 ms/2 Gate%; Rate 0 1/16..3 1/4
        Midi("notelength", 3, "Staccato",    ("Mode", 0f), ("Rate", 0f), ("Gate", 1f));
        Midi("notelength", 3, "Tenuto 1/4",  ("Mode", 0f), ("Rate", 3f), ("Gate", 1f));
        Midi("notelength", 3, "Half Gate",   ("Mode", 2f), ("Percent", 50f));
        Midi("notelength", 3, "Fixed 120ms", ("Mode", 1f), ("Ms", 120f));
        // ---- Nota Velocity (MIDI, kind 4) — Mode 0 Curve/1 Compand/2 Fixed; Drive 1 = linear
        Midi("velocity", 4, "Soft Hands", ("Mode", 0f), ("Drive", 1.4f));
        Midi("velocity", 4, "Humanize",   ("Mode", 0f), ("Drive", 1f), ("Random", 0.25f));
        Midi("velocity", 4, "Compress",   ("Mode", 1f), ("Drive", 0.6f));
        Midi("velocity", 4, "Fixed 100",  ("Mode", 2f), ("Fixed", 0.79f));
        // ---- Nota Random (MIDI, kind 5) — Dist 0 Gauss/1 Even/2 Walk; Rate 0 note/1 bar
        Midi("random", 5, "Human Drift",   ("Chance", 0.7f), ("Note Range", 1f), ("Time Amt", 0.3f), ("Vel Amt", 0.3f), ("Dist", 2f));
        Midi("random", 5, "Pitch Roulette",("Chance", 0.5f), ("Note Range", 12f), ("Dist", 1f), ("Stay In Scale", 1f));
        Midi("random", 5, "Ghost Notes",   ("Chance", 1f), ("Note Range", 0f), ("Skip", 0.35f), ("Vel Amt", 0.4f));
        Midi("random", 5, "Octave Jumps",  ("Chance", 0.4f), ("Note Range", 0f), ("Oct Amt", 0.5f), ("Rate", 1f));
    }

    public IReadOnlyList<FactoryPresetInfo> All()
    {
        var list = new List<FactoryPresetInfo>(_all.Count);
        foreach (var (info, _) in _all) list.Add(info);
        return list;
    }

    /// <summary>The preset's document (named params), or null — for validation and tests.</summary>
    public PresetDocument? Document(string id) => _byId.TryGetValue(id, out var doc) ? doc : null;

    public string Apply(IAudioEngine engine, string id, int targetTrackId)
        => _byId.TryGetValue(id, out var doc)
            ? PresetService.Apply(doc, engine, targetTrackId)
            : "Unknown factory preset.";

    public string ApplyInPlace(IAudioEngine engine, string id, int trackId, int deviceIndex)
        => _byId.TryGetValue(id, out var doc)
            ? PresetService.ApplyInPlace(doc, engine, trackId, deviceIndex)
            : "Unknown factory preset.";

    // Nota Consort preset helpers (see the Consort block above).
    private static (string, float)[] P(params (string, float)[] ps) => ps;
    private static (string, float)[] Cat(params (string, float)[][] parts)
    {
        var l = new List<(string, float)>();
        foreach (var p in parts) l.AddRange(p);
        return l.ToArray();
    }
    private static (string, float)[] ConsortSeq(string types, params int[] pitches)
    {
        var l = new List<(string, float)>();
        for (int s = 0; s < 16; s++)
        {
            char c = s < types.Length ? types[s] : 'n';
            l.Add(($"st{s + 1}", c switch { 'r' => 1f / 3f, 't' => 2f / 3f, '.' => 1f, _ => 0f }));
            l.Add(($"sp{s + 1}", 0.5f + (s < pitches.Length ? pitches[s] : 0) / 48f));
        }
        return l.ToArray();
    }
    private static (string, float)[] Cab(int slot, int src, int dst, float depth)
        => new[] { ($"c{slot}src", src / 63f), ($"c{slot}dst", dst / 63f), ($"c{slot}amt", 0.5f + depth / 2f) };

    private void Inst(string group, int kind, string name, params (string Id, float Value)[] ps)
        => Add(group, name, "builtin-instrument", kind, isInstrument: true, isMidi: false, ps);

    private void Fx(string group, int kind, string name, params (string Name, float Value)[] ps)
        => Add(group, name, "builtin-effect", kind, isInstrument: false, isMidi: false, ps);

    private void Midi(string group, int kind, string name, params (string Name, float Value)[] ps)
        => Add(group, name, "builtin-midi-effect", kind, isInstrument: false, isMidi: true, ps);

    private void Add(string group, string name, string type, int kind, bool isInstrument, bool isMidi, (string Key, float Value)[] ps)
    {
        string id = group + "/" + name;
        var map = new Dictionary<string, float>(ps.Length);
        foreach (var (key, value) in ps) map[key] = value;
        var doc = new PresetDocument { DisplayName = name, Type = type, BuiltinKind = kind, NamedParams = map };
        _all.Add((new FactoryPresetInfo(id, name, isInstrument, kind, isMidi), doc));
        _byId[id] = doc;
    }
}
