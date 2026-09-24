// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The shipped factory kits, as recipes. 25 kits x 16 pads, defined here rather than
// bundled as audio: the installer carries a few kilobytes of parameters and the WAVs
// are rendered on the user's machine the first time Nota runs (KitLibrary).
//
// The first ten kits live here; the fifteen genre kits that followed are in
// KitCatalog.Genres.cs. Browser order is the Build() list below.
//
// Pad layout follows the General MIDI drum map for notes 36-51, so a pattern written
// for any other drum instrument lands on the right pads here:
//
//   36 Kick    37 Rim      38 Snare     39 Clap
//   40 Snare 2 41 Tom Low  42 Cl Hat    43 Tom Mid
//   44 Pd Hat  45 Tom Hi   46 Op Hat    47 Perc 1
//   48 Perc 2  49 Crash    50 Perc 3    51 Ride
//
// GM reserves 47/48/50 for further toms; kits that only have three toms use those
// slots for their own character percussion instead, and the pad name says which.
// All three hats share choke group 1, so closed cuts open the way hardware does.
//
// The sound design aims at warm rather than clean: most voices carry some saturation
// and the Warmth glue stage, and none of them are normalized to the ceiling — there is
// headroom left for the user to drive them further.

using System.Collections.Generic;

namespace Nota.Infrastructure.Kits;

public static partial class KitCatalog
{
    private static IReadOnlyList<KitDefinition>? _all;

    /// <summary>Every shipped kit, in browser order.</summary>
    public static IReadOnlyList<KitDefinition> All => _all ??= Build();

    public static KitDefinition? ById(string id)
    {
        foreach (var k in All) if (k.Id == id) return k;
        return null;
    }

    private static List<KitDefinition> Build() => new()
    {
        Volta(), Kompakt(), Micron(), Linnwood(), Atelier(),
        Cellar(), Neon(), Foundry(), Terra(), Aether(),
        BrassRoom(), Breakline(), Bunker(), Pixel(), Velvet(),
        Circuit(), Yard(), Byte(), Lagoon(), Titan(),
        Shuffle(), Mirrorball(), Pit(), Hyper(), Crate(),
    };

    // ======================================================================
    // 1. Volta — the long, round analog boom of the first programmable boxes.
    //    Everything is a rung circuit: a sub that decays for two seconds, a
    //    noise-and-tone snare, and six squares of metal for the hats.
    // ======================================================================
    private static KitDefinition Volta() => new()
    {
        Id = "volta",
        Name = "Volta",
        Blurb = "warm analog boom · long sub kick, metal hats, cowbell",
        FxSpace = 1.0,
        FxDrive = 1.0,
        Swing = 0f,
        Humanize = 0.05f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAnalog, Note = 36, Freq = 47,  Decay = 1.55, PitchAmt = 1.3, PitchDecay = 0.04,  Tone = 0.16, Body = 0.85, Click = 0.20, Drive = 0.22, Warmth = 0.6,  Drift = 0.3, Length = 2.6, PeakDb = -1.5 },
            new() { Name = "Rim",        Model = DrumModel.Rim,        Note = 37, Freq = 372, Decay = 0.05, Tone = 0.55, Body = 0.4,  Drive = 0.25, Warmth = 0.5, HpHz = 200, Drift = 0.4, Length = 0.3,  PeakDb = -9 },
            new() { Name = "Snare",      Model = DrumModel.SnareAnalog,Note = 38, Freq = 184, Decay = 0.36, Tone = 0.60, Body = 0.55, Noise = 0.9, NoiseDecay = 0.18, Click = 0.40, Drive = 0.25, Warmth = 0.55, Drift = 0.3, Length = 0.8, PeakDb = -2.5 },
            new() { Name = "Clap",       Model = DrumModel.Clap,       Note = 39, Freq = 1040,Decay = 0.24, Tone = 0.5,  Body = 0.55, Drive = 0.2,  Warmth = 0.5, Drift = 0.5, Length = 0.7, PeakDb = -4 },
            new() { Name = "Snare Tight",Model = DrumModel.SnareAnalog,Note = 40, Freq = 246, Decay = 0.20, Tone = 0.74, Body = 0.4,  Noise = 1.0, NoiseDecay = 0.10, Click = 0.52, Drive = 0.28, Warmth = 0.5,  Drift = 0.3, Length = 0.5, PeakDb = -4 },
            new() { Name = "Tom Low",    Model = DrumModel.Tom,        Note = 41, Freq = 78,  Decay = 0.62, PitchAmt = 0.32, PitchDecay = 0.05, Tone = 0.35, Body = 0.30, Noise = 0.10, NoiseDecay = 0.04, Drive = 0.18, Warmth = 0.55, Drift = 0.3, Length = 1.1, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatMetal,   Note = 42, Choke = 1, Freq = 205, Decay = 0.055, Tone = 0.22, Body = 0.55, Noise = 0.15, Drive = 0.15, Warmth = 0.4, Drift = 0.4, Length = 0.3, PeakDb = -10 },
            new() { Name = "Tom Mid",    Model = DrumModel.Tom,        Note = 43, Freq = 104, Decay = 0.52, PitchAmt = 0.32, PitchDecay = 0.045,Tone = 0.35, Body = 0.30, Noise = 0.10, NoiseDecay = 0.04, Drive = 0.18, Warmth = 0.55, Drift = 0.3, Length = 0.95,PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatMetal,   Note = 44, Choke = 1, Freq = 205, Decay = 0.035, Tone = 0.20, Body = 0.5,  Noise = 0.12, Drive = 0.12, Warmth = 0.4, Drift = 0.4, Length = 0.2, PeakDb = -13 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,        Note = 45, Freq = 142, Decay = 0.44, PitchAmt = 0.32, PitchDecay = 0.04, Tone = 0.35, Body = 0.30, Noise = 0.10, NoiseDecay = 0.04, Drive = 0.18, Warmth = 0.55, Drift = 0.3, Length = 0.85,PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatMetal,   Note = 46, Choke = 1, Freq = 205, Decay = 0.55,  Tone = 0.24, Body = 0.55, Noise = 0.18, Drive = 0.15, Warmth = 0.4, Drift = 0.4, Length = 1.2, PeakDb = -9 },
            new() { Name = "Conga Low",  Model = DrumModel.Conga,      Note = 47, Freq = 180, Decay = 0.34, Tone = 0.35, Body = 0.35, Click = 0.30, Drive = 0.2, Warmth = 0.55, Drift = 0.3, Length = 0.7, PeakDb = -6 },
            new() { Name = "Conga Mid",  Model = DrumModel.Conga,      Note = 48, Freq = 245, Decay = 0.30, Tone = 0.38, Body = 0.35, Click = 0.30, Drive = 0.2, Warmth = 0.55, Drift = 0.3, Length = 0.6, PeakDb = -6 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,     Note = 49, Freq = 296, Decay = 2.6,  Tone = 0.42, Body = 0.6,  Noise = 0.35, Drive = 0.12, Warmth = 0.35, Drift = 0.4, Length = 3.2, PeakDb = -7 },
            new() { Name = "Conga Hi",   Model = DrumModel.Conga,      Note = 50, Freq = 330, Decay = 0.26, Tone = 0.42, Body = 0.35, Click = 0.30, Drive = 0.2, Warmth = 0.55, Drift = 0.3, Length = 0.55,PeakDb = -6 },
            new() { Name = "Cowbell",    Model = DrumModel.Cowbell,    Note = 51, Freq = 540, Decay = 0.30, Tone = 0.45, Body = 0.5,  Click = 0.35, Drive = 0.25, Warmth = 0.5, Drift = 0.3, Length = 0.7, PeakDb = -8 },
        },
    };

    // ======================================================================
    // 2. Kompakt — the club machine: short, hard kick with a steep pitch drop,
    //    noise-forward snare, hats bright enough to cut through a mix.
    // ======================================================================
    private static KitDefinition Kompakt() => new()
    {
        Id = "kompakt",
        Name = "Kompakt",
        Blurb = "punchy analog house · clicky kick, noisy snare, bright hats",
        FxSpace = 0.9,
        FxDrive = 1.0,
        Humanize = 0.04f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickPunch,  Note = 36, Freq = 51,  Decay = 0.48, PitchAmt = 3.0, PitchDecay = 0.013, Tone = 0.55, Body = 0.55, Click = 0.55, Drive = 0.35, Warmth = 0.5, Drift = 0.2, Length = 1.1, PeakDb = -1.5 },
            new() { Name = "Rim",        Model = DrumModel.Rim,        Note = 37, Freq = 448, Decay = 0.04, Tone = 0.62, Body = 0.5,  Drive = 0.3,  Warmth = 0.45, HpHz = 240, Drift = 0.3, Length = 0.25, PeakDb = -9 },
            new() { Name = "Snare",      Model = DrumModel.SnarePunch, Note = 38, Freq = 192, Decay = 0.26, Tone = 0.55, Body = 0.45, Noise = 0.9, NoiseDecay = 0.13, Drive = 0.32, Warmth = 0.45, Drift = 0.2, Length = 0.6, PeakDb = -2.5 },
            new() { Name = "Clap",       Model = DrumModel.Clap,       Note = 39, Freq = 1180,Decay = 0.26, Tone = 0.6,  Body = 0.6,  Drive = 0.25, Warmth = 0.45, Drift = 0.5, Length = 0.7, PeakDb = -4 },
            new() { Name = "Snare Tight",Model = DrumModel.SnarePunch, Note = 40, Freq = 238, Decay = 0.16, Tone = 0.7,  Body = 0.35, Noise = 1.0, NoiseDecay = 0.07, Drive = 0.35, Warmth = 0.4,  Drift = 0.2, Length = 0.4, PeakDb = -4 },
            new() { Name = "Tom Low",    Model = DrumModel.Tom,        Note = 41, Freq = 90,  Decay = 0.40, PitchAmt = 0.55, PitchDecay = 0.035, Tone = 0.45, Body = 0.4, Noise = 0.22, NoiseDecay = 0.05, Drive = 0.25, Warmth = 0.45, Drift = 0.2, Length = 0.8, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatMetal,   Note = 42, Choke = 1, Freq = 318, Decay = 0.045, Tone = 0.45, Body = 0.6, Noise = 0.35, Drive = 0.2, Warmth = 0.3, Drift = 0.3, Length = 0.25, PeakDb = -10 },
            new() { Name = "Tom Mid",    Model = DrumModel.Tom,        Note = 43, Freq = 124, Decay = 0.34, PitchAmt = 0.55, PitchDecay = 0.032, Tone = 0.45, Body = 0.4, Noise = 0.22, NoiseDecay = 0.05, Drive = 0.25, Warmth = 0.45, Drift = 0.2, Length = 0.7, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatMetal,   Note = 44, Choke = 1, Freq = 318, Decay = 0.028, Tone = 0.42, Body = 0.55,Noise = 0.3, Drive = 0.18, Warmth = 0.3, Drift = 0.3, Length = 0.2, PeakDb = -13 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,        Note = 45, Freq = 168, Decay = 0.28, PitchAmt = 0.55, PitchDecay = 0.03,  Tone = 0.45, Body = 0.4, Noise = 0.22, NoiseDecay = 0.05, Drive = 0.25, Warmth = 0.45, Drift = 0.2, Length = 0.6, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatMetal,   Note = 46, Choke = 1, Freq = 318, Decay = 0.40,  Tone = 0.48, Body = 0.6, Noise = 0.4, Drive = 0.2, Warmth = 0.3, Drift = 0.3, Length = 1.0, PeakDb = -9 },
            new() { Name = "Clap Tight", Model = DrumModel.Clap,       Note = 47, Freq = 1500,Decay = 0.10, Tone = 0.7,  Body = 0.3,  Drive = 0.3, Warmth = 0.35, Drift = 0.4, Length = 0.35, PeakDb = -6 },
            new() { Name = "Rim Hard",   Model = DrumModel.Rim,        Note = 48, Freq = 690, Decay = 0.03, Tone = 0.75, Body = 0.6,  Drive = 0.4, Warmth = 0.35, HpHz = 400, Drift = 0.3, Length = 0.2, PeakDb = -8 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,     Note = 49, Freq = 336, Decay = 2.2,  Tone = 0.55, Body = 0.55, Noise = 0.5, Drive = 0.15, Warmth = 0.3, Drift = 0.3, Length = 2.8, PeakDb = -7 },
            new() { Name = "Zap",        Model = DrumModel.Zap,        Note = 50, Freq = 120, Decay = 0.12, PitchAmt = 9, PitchDecay = 0.02, Tone = 0.3, Body = 0.5, Drive = 0.3, Warmth = 0.4, Length = 0.35, PeakDb = -7 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,     Note = 51, Freq = 420, Decay = 1.1,  Tone = 0.75, Body = 0.4,  Noise = 0.25, Drive = 0.12, Warmth = 0.3, Drift = 0.3, Length = 1.6, PeakDb = -10 },
        },
    };

    // ======================================================================
    // 3. Micron — the small, cheap rhythm box: thin, band-limited and hissy,
    //    which is exactly its charm. Everything is low-passed and a little
    //    noisy, and the kit is quieter overall than the big machines.
    // ======================================================================
    private static KitDefinition Micron() => new()
    {
        Id = "micron",
        Name = "Micron",
        Blurb = "small vintage rhythm box · thin, hissy, band-limited",
        FxSpace = 0.7,
        FxDrive = 0.8,
        FxTape = true,
        Humanize = 0.08f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAnalog, Note = 36, Freq = 54,  Decay = 0.42, PitchAmt = 1.5, PitchDecay = 0.02, Tone = 0.35, Body = 0.5, Click = 0.35, Drive = 0.28, Warmth = 0.7, LpHz = 6500, Hiss = 0.35, Drift = 0.6, Length = 0.9, PeakDb = -2 },
            new() { Name = "Rim",        Model = DrumModel.Rim,        Note = 37, Freq = 530, Decay = 0.03, Tone = 0.6,  Body = 0.35, Drive = 0.3, Warmth = 0.6, HpHz = 300, LpHz = 9000, Hiss = 0.3, Drift = 0.7, Length = 0.2, PeakDb = -10 },
            new() { Name = "Snare",      Model = DrumModel.SnareAnalog,Note = 38, Freq = 238, Decay = 0.19, Tone = 0.72, Body = 0.35, Noise = 0.85, NoiseDecay = 0.09, Click = 0.35, Drive = 0.3, Warmth = 0.65, LpHz = 8500, Hiss = 0.4, Drift = 0.5, Length = 0.5, PeakDb = -3 },
            new() { Name = "Clap",       Model = DrumModel.Clap,       Note = 39, Freq = 1250,Decay = 0.14, Tone = 0.45, Body = 0.4, Drive = 0.25, Warmth = 0.6, LpHz = 8000, Hiss = 0.35, Drift = 0.6, Length = 0.45, PeakDb = -5 },
            new() { Name = "Snare Alt",  Model = DrumModel.SnareAnalog,Note = 40, Freq = 300, Decay = 0.13, Tone = 0.8,  Body = 0.28, Noise = 0.95, NoiseDecay = 0.06, Click = 0.45, Drive = 0.3, Warmth = 0.6, LpHz = 9000, Hiss = 0.4, Drift = 0.5, Length = 0.4, PeakDb = -5 },
            new() { Name = "Tom Low",    Model = DrumModel.Tom,        Note = 41, Freq = 96,  Decay = 0.30, PitchAmt = 0.4, PitchDecay = 0.03, Tone = 0.4, Body = 0.25, Noise = 0.18, NoiseDecay = 0.035, Drive = 0.25, Warmth = 0.65, LpHz = 6000, Hiss = 0.3, Drift = 0.5, Length = 0.6, PeakDb = -6 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,   Note = 42, Choke = 1, Freq = 900, Decay = 0.035, Tone = 0.5, Body = 0.45, Click = 0.3, Drive = 0.2, Warmth = 0.5, LpHz = 11000, Hiss = 0.3, Drift = 0.6, Length = 0.2, PeakDb = -11 },
            new() { Name = "Tom Mid",    Model = DrumModel.Tom,        Note = 43, Freq = 132, Decay = 0.26, PitchAmt = 0.4, PitchDecay = 0.028,Tone = 0.4, Body = 0.25, Noise = 0.18, NoiseDecay = 0.035, Drive = 0.25, Warmth = 0.65, LpHz = 6500, Hiss = 0.3, Drift = 0.5, Length = 0.55, PeakDb = -6 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,   Note = 44, Choke = 1, Freq = 900, Decay = 0.022, Tone = 0.45, Body = 0.4, Click = 0.25, Drive = 0.18, Warmth = 0.5, LpHz = 10000, Hiss = 0.25, Drift = 0.6, Length = 0.15, PeakDb = -14 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,        Note = 45, Freq = 178, Decay = 0.22, PitchAmt = 0.4, PitchDecay = 0.026,Tone = 0.4, Body = 0.25, Noise = 0.18, NoiseDecay = 0.035, Drive = 0.25, Warmth = 0.65, LpHz = 7000, Hiss = 0.3, Drift = 0.5, Length = 0.5, PeakDb = -6 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,   Note = 46, Choke = 1, Freq = 900, Decay = 0.26, Tone = 0.55, Body = 0.45, Click = 0.25, Drive = 0.2, Warmth = 0.5, LpHz = 11000, Hiss = 0.35, Drift = 0.6, Length = 0.7, PeakDb = -10 },
            new() { Name = "Maraca",     Model = DrumModel.Shaker,     Note = 47, Freq = 5200,Decay = 0.075,Tone = 0.5, Body = 0.7, Click = 0.7, Drive = 0.15, Warmth = 0.45, LpHz = 12000, Hiss = 0.25, Drift = 0.6, Length = 0.3, PeakDb = -11 },
            new() { Name = "Claves",     Model = DrumModel.Block,      Note = 48, Freq = 2350,Decay = 0.05, Tone = 0.35, Body = 0.4, Drive = 0.2, Warmth = 0.5, LpHz = 12000, Hiss = 0.2, Drift = 0.5, Length = 0.25, PeakDb = -9 },
            new() { Name = "Cymbal",     Model = DrumModel.Cymbal,     Note = 49, Freq = 340, Decay = 0.9,  Tone = 0.55, Body = 0.4, Noise = 0.45, Drive = 0.18, Warmth = 0.5, LpHz = 11000, Hiss = 0.4, Drift = 0.5, Length = 1.4, PeakDb = -9 },
            new() { Name = "Cowbell",    Model = DrumModel.Cowbell,    Note = 50, Freq = 600, Decay = 0.22, Tone = 0.5, Body = 0.45, Click = 0.3, Drive = 0.28, Warmth = 0.55, LpHz = 9000, Hiss = 0.3, Drift = 0.5, Length = 0.5, PeakDb = -9 },
            new() { Name = "Hi Conga",   Model = DrumModel.Conga,      Note = 51, Freq = 340, Decay = 0.2,  Tone = 0.4, Body = 0.3, Click = 0.3, Drive = 0.25, Warmth = 0.6, LpHz = 8000, Hiss = 0.3, Drift = 0.5, Length = 0.45, PeakDb = -8 },
        },
    };

    // ======================================================================
    // 4. Linnwood — the 80s PCM machine: real drums, sampled at 8 bits and a
    //    low rate. The crusher is the point, not a defect; the gated snare is
    //    the sound of the decade.
    // ======================================================================
    private static KitDefinition Linnwood() => new()
    {
        Id = "linnwood",
        Name = "Linnwood",
        Blurb = "80s PCM drum machine · 8-bit sampled kit, gated snare",
        FxSpace = 1.1,
        FxDrive = 0.9,
        Humanize = 0.03f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAcoustic, Note = 36, Freq = 52, Decay = 0.42, PitchAmt = 0.7, PitchDecay = 0.02, Tone = 0.5, Body = 0.55, Click = 0.45, Drive = 0.25, Warmth = 0.6, Bits = 9, CrushHz = 30000, LpHz = 11000, Drift = 0.15, Length = 0.9, PeakDb = -1.5 },
            new() { Name = "Side Stick", Model = DrumModel.Rim,          Note = 37, Freq = 520, Decay = 0.045, Tone = 0.5, Body = 0.45, Drive = 0.25, Warmth = 0.5, Bits = 9, CrushHz = 30000, HpHz = 260, Length = 0.25, PeakDb = -9 },
            new() { Name = "Snare",      Model = DrumModel.SnareAcoustic,Note = 38, Freq = 196, Decay = 0.38, Tone = 0.5, Body = 0.6, Noise = 0.85, NoiseDecay = 0.2, Click = 0.45, Drive = 0.28, Warmth = 0.55, Bits = 9, CrushHz = 30000, LpHz = 12000, Room = 0.34, RoomSize = 0.55, RoomDecay = 0.20, RoomDamp = 0.4, Width = 0.6, Length = 1.0, PeakDb = -2.5 },
            new() { Name = "Clap",       Model = DrumModel.Clap,         Note = 39, Freq = 1150,Decay = 0.24, Tone = 0.5, Body = 0.55, Drive = 0.22, Warmth = 0.5, Bits = 9, CrushHz = 30000, Room = 0.22, RoomSize = 0.45, Width = 0.7, Length = 0.8, PeakDb = -4 },
            new() { Name = "Snare Gate", Model = DrumModel.SnareAcoustic,Note = 40, Freq = 210, Decay = 0.30, Tone = 0.55, Body = 0.6, Noise = 0.95, NoiseDecay = 0.16, Click = 0.5, Drive = 0.35, Warmth = 0.5, Bits = 9, CrushHz = 30000, Room = 0.55, RoomSize = 0.7, RoomDecay = 0.55, RoomDamp = 0.3, RoomGate = 0.22, Width = 0.8, Length = 0.85, PeakDb = -3 },
            new() { Name = "Tom Low",    Model = DrumModel.Tom,          Note = 41, Freq = 84,  Decay = 0.55, PitchAmt = 0.3, PitchDecay = 0.05, Tone = 0.45, Body = 0.5, Noise = 0.3, NoiseDecay = 0.06, Drive = 0.22, Warmth = 0.55, Bits = 9, CrushHz = 30000, Room = 0.25, RoomSize = 0.5, Width = 0.6, Length = 1.2, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 1100, Decay = 0.05, Tone = 0.6, Body = 0.5, Click = 0.35, Drive = 0.15, Warmth = 0.4, Bits = 8, CrushHz = 26000, Length = 0.25, PeakDb = -10 },
            new() { Name = "Tom Mid",    Model = DrumModel.Tom,          Note = 43, Freq = 112, Decay = 0.48, PitchAmt = 0.3, PitchDecay = 0.045,Tone = 0.45, Body = 0.5, Noise = 0.3, NoiseDecay = 0.06, Drive = 0.22, Warmth = 0.55, Bits = 9, CrushHz = 30000, Room = 0.25, RoomSize = 0.5, Width = 0.6, Length = 1.1, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 1100, Decay = 0.03, Tone = 0.55, Body = 0.45, Click = 0.3, Drive = 0.15, Warmth = 0.4, Bits = 8, CrushHz = 26000, Length = 0.2, PeakDb = -13 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,          Note = 45, Freq = 152, Decay = 0.42, PitchAmt = 0.3, PitchDecay = 0.04, Tone = 0.45, Body = 0.5, Noise = 0.3, NoiseDecay = 0.06, Drive = 0.22, Warmth = 0.55, Bits = 9, CrushHz = 30000, Room = 0.25, RoomSize = 0.5, Width = 0.6, Length = 1.0, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 1100, Decay = 0.32, Tone = 0.62, Body = 0.5, Click = 0.3, Drive = 0.15, Warmth = 0.4, Bits = 8, CrushHz = 26000, Length = 0.85, PeakDb = -9 },
            new() { Name = "Tambourine", Model = DrumModel.Shaker,       Note = 47, Freq = 7000,Decay = 0.16, Tone = 0.65, Body = 0.85, Click = 0.8, Drive = 0.15, Warmth = 0.35, Bits = 8, CrushHz = 26000, Length = 0.45, PeakDb = -10 },
            new() { Name = "Cabasa",     Model = DrumModel.Shaker,       Note = 48, Freq = 5600,Decay = 0.08, Tone = 0.5, Body = 0.9, Click = 0.5, Drive = 0.12, Warmth = 0.35, Bits = 8, CrushHz = 26000, Length = 0.3, PeakDb = -11 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,       Note = 49, Freq = 310, Decay = 2.0, Tone = 0.5, Body = 0.55, Noise = 0.5, Drive = 0.12, Warmth = 0.35, Bits = 9, CrushHz = 30000, Length = 2.6, PeakDb = -7 },
            new() { Name = "Cowbell",    Model = DrumModel.Cowbell,      Note = 50, Freq = 560, Decay = 0.26, Tone = 0.5, Body = 0.5, Click = 0.35, Drive = 0.25, Warmth = 0.45, Bits = 9, CrushHz = 30000, Length = 0.6, PeakDb = -9 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,       Note = 51, Freq = 430, Decay = 1.0, Tone = 0.78, Body = 0.4, Noise = 0.25, Drive = 0.12, Warmth = 0.35, Bits = 9, CrushHz = 30000, Length = 1.5, PeakDb = -10 },
        },
    };

    // ======================================================================
    // 5. Atelier — an acoustic kit in a small treated room. Struck bodies with
    //    real mode spacing, a stick crack on the snare, and just enough room
    //    on every pad that they sound like they were recorded together.
    // ======================================================================
    private static KitDefinition Atelier() => new()
    {
        Id = "atelier",
        Name = "Atelier",
        Blurb = "acoustic studio kit · close-miked, small warm room",
        FxSpace = 0.7,
        FxDrive = 0.7,
        Humanize = 0.14f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAcoustic, Note = 36, Freq = 50, Decay = 0.5, PitchAmt = 0.75, PitchDecay = 0.022, Tone = 0.45, Body = 0.65, Click = 0.5, Drive = 0.18, Warmth = 0.6, Room = 0.18, RoomSize = 0.35, RoomDecay = 0.12, RoomDamp = 0.65, Width = 0.5, Drift = 0.2, Length = 1.1, PeakDb = -1.5 },
            new() { Name = "Side Stick", Model = DrumModel.Rim,          Note = 37, Freq = 495, Decay = 0.05, Tone = 0.45, Body = 0.5, Drive = 0.15, Warmth = 0.55, HpHz = 240, Room = 0.2, RoomSize = 0.35, Width = 0.6, Drift = 0.3, Length = 0.35, PeakDb = -9 },
            new() { Name = "Snare",      Model = DrumModel.SnareAcoustic,Note = 38, Freq = 188, Decay = 0.4, Tone = 0.5, Body = 0.6, Noise = 0.8, NoiseDecay = 0.19, Click = 0.42, Drive = 0.2, Warmth = 0.55, Room = 0.26, RoomSize = 0.38, RoomDecay = 0.14, RoomDamp = 0.55, Width = 0.65, Drift = 0.25, Length = 1.1, PeakDb = -2.5 },
            new() { Name = "Clap",       Model = DrumModel.Clap,         Note = 39, Freq = 1080,Decay = 0.2, Tone = 0.45, Body = 0.5, Drive = 0.15, Warmth = 0.5, Room = 0.25, RoomSize = 0.4, Width = 0.8, Drift = 0.5, Length = 0.8, PeakDb = -4.5 },
            new() { Name = "Snare Rim",  Model = DrumModel.SnareAcoustic,Note = 40, Freq = 236, Decay = 0.26, Tone = 0.62, Body = 0.5, Noise = 0.9, NoiseDecay = 0.12, Click = 0.6, Drive = 0.25, Warmth = 0.5, Room = 0.22, RoomSize = 0.38, Width = 0.65, Drift = 0.25, Length = 0.8, PeakDb = -4 },
            new() { Name = "Floor Tom",  Model = DrumModel.Tom,          Note = 41, Freq = 72,  Decay = 0.75, PitchAmt = 0.22, PitchDecay = 0.06, Tone = 0.4, Body = 0.6, Noise = 0.35, NoiseDecay = 0.07, Drive = 0.15, Warmth = 0.55, Room = 0.24, RoomSize = 0.42, Width = 0.7, Drift = 0.25, Length = 1.5, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 1250, Decay = 0.055, Tone = 0.6, Body = 0.55, Click = 0.4, Drive = 0.1, Warmth = 0.4, Room = 0.12, RoomSize = 0.3, Width = 0.5, Drift = 0.4, Length = 0.3, PeakDb = -10 },
            new() { Name = "Low Tom",    Model = DrumModel.Tom,          Note = 43, Freq = 96,  Decay = 0.65, PitchAmt = 0.22, PitchDecay = 0.055,Tone = 0.42, Body = 0.6, Noise = 0.35, NoiseDecay = 0.07, Drive = 0.15, Warmth = 0.55, Room = 0.24, RoomSize = 0.42, Width = 0.7, Drift = 0.25, Length = 1.4, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 1150, Decay = 0.035, Tone = 0.5, Body = 0.5, Click = 0.35, Drive = 0.1, Warmth = 0.4, Room = 0.1, Width = 0.5, Drift = 0.4, Length = 0.25, PeakDb = -13 },
            new() { Name = "Hi Tom",     Model = DrumModel.Tom,          Note = 45, Freq = 134, Decay = 0.55, PitchAmt = 0.22, PitchDecay = 0.05, Tone = 0.44, Body = 0.6, Noise = 0.35, NoiseDecay = 0.07, Drive = 0.15, Warmth = 0.55, Room = 0.24, RoomSize = 0.42, Width = 0.7, Drift = 0.25, Length = 1.25, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 1250, Decay = 0.38, Tone = 0.65, Body = 0.55, Click = 0.35, Drive = 0.1, Warmth = 0.4, Room = 0.16, RoomSize = 0.35, Width = 0.6, Drift = 0.4, Length = 1.0, PeakDb = -9 },
            new() { Name = "Tambourine", Model = DrumModel.Shaker,       Note = 47, Freq = 7400,Decay = 0.18, Tone = 0.7, Body = 0.85, Click = 0.85, Drive = 0.1, Warmth = 0.35, Room = 0.16, Width = 0.8, Drift = 0.5, Length = 0.5, PeakDb = -10 },
            new() { Name = "Sticks",     Model = DrumModel.Block,        Note = 48, Freq = 2700,Decay = 0.035,Tone = 0.4, Body = 0.35, Drive = 0.12, Warmth = 0.45, Room = 0.14, Width = 0.6, Drift = 0.5, Length = 0.25, PeakDb = -11 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,       Note = 49, Freq = 288, Decay = 2.8, Tone = 0.45, Body = 0.6, Noise = 0.55, Drive = 0.08, Warmth = 0.35, Room = 0.18, RoomSize = 0.5, RoomDecay = 0.22, Width = 0.85, Drift = 0.4, Length = 3.6, PeakDb = -7 },
            new() { Name = "Splash",     Model = DrumModel.Cymbal,       Note = 50, Freq = 520, Decay = 0.9, Tone = 0.6, Body = 0.45, Noise = 0.5, Drive = 0.08, Warmth = 0.35, Room = 0.16, Width = 0.8, Drift = 0.4, Length = 1.4, PeakDb = -9 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,       Note = 51, Freq = 396, Decay = 1.5, Tone = 0.8, Body = 0.45, Noise = 0.3, Drive = 0.08, Warmth = 0.35, Room = 0.14, RoomSize = 0.45, Width = 0.7, Drift = 0.4, Length = 2.2, PeakDb = -10 },
        },
    };

    // ======================================================================
    // 6. Cellar — the dusty sampled break: an acoustic kit put through a low
    //    bit depth, a narrow band and tape-style saturation, with a noise bed
    //    under every hit. Aimed at slow, heavy, sample-based music.
    // ======================================================================
    private static KitDefinition Cellar() => new()
    {
        Id = "cellar",
        Name = "Cellar",
        Blurb = "dusty vinyl break · filtered, crushed, tape-saturated",
        FxSpace = 0.8,
        FxDrive = 1.1,
        FxTape = true,
        Swing = 0.16f,
        Humanize = 0.12f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAcoustic, Note = 36, Freq = 52, Decay = 0.42, PitchAmt = 0.6, PitchDecay = 0.025, Tone = 0.35, Body = 0.6, Click = 0.35, Drive = 0.45, Warmth = 0.85, Lofi = 0.35, CrushHz = 22050, HpHz = 38, LpHz = 6200, Hiss = 0.45, Room = 0.12, RoomSize = 0.3, RoomDamp = 0.8, Width = 0.4, Drift = 0.5, Length = 0.9, PeakDb = -2 },
            new() { Name = "Side Stick", Model = DrumModel.Rim,          Note = 37, Freq = 460, Decay = 0.05, Tone = 0.4, Body = 0.45, Drive = 0.4, Warmth = 0.8, Lofi = 0.3, CrushHz = 22050, HpHz = 200, LpHz = 7000, Hiss = 0.4, Drift = 0.6, Length = 0.3, PeakDb = -9 },
            new() { Name = "Snare",      Model = DrumModel.SnareAcoustic,Note = 38, Freq = 180, Decay = 0.34, Tone = 0.38, Body = 0.65, Noise = 0.75, NoiseDecay = 0.16, Click = 0.35, Drive = 0.45, Warmth = 0.85, Lofi = 0.35, CrushHz = 22050, HpHz = 90, LpHz = 6800, Hiss = 0.5, Room = 0.2, RoomSize = 0.35, RoomDamp = 0.8, Width = 0.55, Drift = 0.5, Length = 0.9, PeakDb = -3 },
            new() { Name = "Clap",       Model = DrumModel.Clap,         Note = 39, Freq = 950, Decay = 0.2, Tone = 0.35, Body = 0.5, Drive = 0.4, Warmth = 0.8, Lofi = 0.35, CrushHz = 22050, LpHz = 6500, Hiss = 0.45, Room = 0.18, Width = 0.6, Drift = 0.6, Length = 0.7, PeakDb = -5 },
            new() { Name = "Snare Ghost",Model = DrumModel.SnareAcoustic,Note = 40, Freq = 215, Decay = 0.16, Tone = 0.45, Body = 0.45, Noise = 0.8, NoiseDecay = 0.07, Click = 0.3, Drive = 0.4, Warmth = 0.8, Lofi = 0.35, CrushHz = 22050, HpHz = 110, LpHz = 6500, Hiss = 0.45, Drift = 0.5, Length = 0.45, PeakDb = -8 },
            new() { Name = "Tom Low",    Model = DrumModel.Tom,          Note = 41, Freq = 76,  Decay = 0.6, PitchAmt = 0.25, PitchDecay = 0.055, Tone = 0.3, Body = 0.55, Noise = 0.3, NoiseDecay = 0.06, Drive = 0.4, Warmth = 0.85, Lofi = 0.3, CrushHz = 22050, LpHz = 5500, Hiss = 0.4, Room = 0.16, Width = 0.5, Drift = 0.5, Length = 1.2, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 1000, Decay = 0.045, Tone = 0.4, Body = 0.5, Click = 0.35, Drive = 0.35, Warmth = 0.7, Lofi = 0.35, CrushHz = 22050, LpHz = 8000, Hiss = 0.4, Drift = 0.6, Length = 0.25, PeakDb = -11 },
            new() { Name = "Tom Mid",    Model = DrumModel.Tom,          Note = 43, Freq = 102, Decay = 0.52, PitchAmt = 0.25, PitchDecay = 0.05, Tone = 0.32, Body = 0.55, Noise = 0.3, NoiseDecay = 0.06, Drive = 0.4, Warmth = 0.85, Lofi = 0.3, CrushHz = 22050, LpHz = 5800, Hiss = 0.4, Room = 0.16, Width = 0.5, Drift = 0.5, Length = 1.1, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 950, Decay = 0.028, Tone = 0.35, Body = 0.45, Click = 0.3, Drive = 0.35, Warmth = 0.7, Lofi = 0.35, CrushHz = 22050, LpHz = 7500, Hiss = 0.35, Drift = 0.6, Length = 0.2, PeakDb = -14 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,          Note = 45, Freq = 140, Decay = 0.45, PitchAmt = 0.25, PitchDecay = 0.045,Tone = 0.34, Body = 0.55, Noise = 0.3, NoiseDecay = 0.06, Drive = 0.4, Warmth = 0.85, Lofi = 0.3, CrushHz = 22050, LpHz = 6000, Hiss = 0.4, Room = 0.16, Width = 0.5, Drift = 0.5, Length = 1.0, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 1000, Decay = 0.3, Tone = 0.45, Body = 0.5, Click = 0.3, Drive = 0.35, Warmth = 0.7, Lofi = 0.35, CrushHz = 22050, LpHz = 8000, Hiss = 0.45, Drift = 0.6, Length = 0.8, PeakDb = -10 },
            new() { Name = "Shaker",     Model = DrumModel.Shaker,       Note = 47, Freq = 4600,Decay = 0.09, Tone = 0.4, Body = 0.8, Click = 0.5, Drive = 0.3, Warmth = 0.6, Lofi = 0.35, CrushHz = 22050, LpHz = 8500, Hiss = 0.4, Drift = 0.6, Length = 0.3, PeakDb = -12 },
            new() { Name = "Rim Click",  Model = DrumModel.Block,        Note = 48, Freq = 1900,Decay = 0.04, Tone = 0.35, Body = 0.35, Drive = 0.35, Warmth = 0.7, Lofi = 0.35, CrushHz = 22050, LpHz = 7000, Hiss = 0.35, Drift = 0.6, Length = 0.25, PeakDb = -10 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,       Note = 49, Freq = 270, Decay = 1.8, Tone = 0.4, Body = 0.55, Noise = 0.5, Drive = 0.3, Warmth = 0.7, Lofi = 0.3, CrushHz = 22050, LpHz = 7500, Hiss = 0.45, Drift = 0.5, Length = 2.4, PeakDb = -8 },
            new() { Name = "Conga",      Model = DrumModel.Conga,        Note = 50, Freq = 260, Decay = 0.28, Tone = 0.35, Body = 0.4, Click = 0.3, Drive = 0.35, Warmth = 0.8, Lofi = 0.3, CrushHz = 22050, LpHz = 6000, Hiss = 0.4, Drift = 0.5, Length = 0.6, PeakDb = -7 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,       Note = 51, Freq = 380, Decay = 0.95,Tone = 0.72, Body = 0.4, Noise = 0.3, Drive = 0.28, Warmth = 0.7, Lofi = 0.3, CrushHz = 22050, LpHz = 8000, Hiss = 0.4, Drift = 0.5, Length = 1.4, PeakDb = -10 },
        },
    };

    // ======================================================================
    // 7. Neon — modern sub-forward production: a tuned 808 bass on the kick
    //    slot's neighbour, a tight layered kick, and the fast, thin hats that
    //    rolls are built from.
    // ======================================================================
    private static KitDefinition Neon() => new()
    {
        Id = "neon",
        Name = "Neon",
        Blurb = "modern sub-forward kit · tuned 808 bass, tight snares, roll hats",
        FxSpace = 1.1,
        FxDrive = 0.9,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickPunch,  Note = 36, Freq = 51, Decay = 0.26, PitchAmt = 3.2, PitchDecay = 0.009, Tone = 0.65, Body = 0.4, Click = 0.7, Drive = 0.4, Warmth = 0.45, HpHz = 30, Length = 0.6, PeakDb = -1.5 },
            new() { Name = "808 Bass",   Model = DrumModel.SubHit,     Note = 37, Freq = 46, Decay = 1.9, PitchAmt = 0.45, PitchDecay = 0.022, Tone = 0.42, Drive = 0.4, Warmth = 0.55, Length = 3.0, PeakDb = -1.5 },
            new() { Name = "Snare",      Model = DrumModel.SnarePunch, Note = 38, Freq = 210, Decay = 0.2, Tone = 0.62, Body = 0.4, Noise = 0.95, NoiseDecay = 0.1, Drive = 0.3, Warmth = 0.4, HpHz = 140, Length = 0.5, PeakDb = -2.5 },
            new() { Name = "Clap",       Model = DrumModel.Clap,       Note = 39, Freq = 1320,Decay = 0.18, Tone = 0.7, Body = 0.45, Drive = 0.3, Warmth = 0.4, HpHz = 300, Length = 0.55, PeakDb = -3.5 },
            new() { Name = "Rim Snare",  Model = DrumModel.Rim,        Note = 40, Freq = 780, Decay = 0.035, Tone = 0.8, Body = 0.6, Drive = 0.4, Warmth = 0.35, HpHz = 420, Length = 0.2, PeakDb = -7 },
            new() { Name = "808 Low",    Model = DrumModel.SubHit,     Note = 41, Freq = 36, Decay = 2.4, PitchAmt = 0.4, PitchDecay = 0.025, Tone = 0.34, Drive = 0.45, Warmth = 0.6, Length = 3.4, PeakDb = -2 },
            new() { Name = "Closed Hat", Model = DrumModel.HatMetal,   Note = 42, Choke = 1, Freq = 420, Decay = 0.03, Tone = 0.5, Body = 0.7, Noise = 0.4, Drive = 0.25, Warmth = 0.25, HpHz = 3000, Length = 0.2, PeakDb = -10 },
            new() { Name = "Hat Roll",   Model = DrumModel.HatMetal,   Note = 43, Choke = 1, Freq = 480, Decay = 0.018, Tone = 0.55, Body = 0.7, Noise = 0.45, Drive = 0.25, Warmth = 0.25, HpHz = 4000, Length = 0.15, PeakDb = -12 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatMetal,   Note = 44, Choke = 1, Freq = 420, Decay = 0.022, Tone = 0.45, Body = 0.65,Noise = 0.35, Drive = 0.22, Warmth = 0.25, HpHz = 3000, Length = 0.18, PeakDb = -13 },
            new() { Name = "808 Hi",     Model = DrumModel.SubHit,     Note = 45, Freq = 62, Decay = 1.3, PitchAmt = 0.5, PitchDecay = 0.018, Tone = 0.5, Drive = 0.4, Warmth = 0.5, Length = 2.2, PeakDb = -2.5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatMetal,   Note = 46, Choke = 1, Freq = 420, Decay = 0.3, Tone = 0.55, Body = 0.7, Noise = 0.45, Drive = 0.25, Warmth = 0.25, HpHz = 3000, Length = 0.8, PeakDb = -10 },
            new() { Name = "Perc Zap",   Model = DrumModel.Zap,        Note = 47, Freq = 180, Decay = 0.1, PitchAmt = 7, PitchDecay = 0.015, Tone = 0.45, Body = 0.6, Drive = 0.35, Warmth = 0.35, Length = 0.3, PeakDb = -7 },
            new() { Name = "Bell",       Model = DrumModel.PercMetal,  Note = 48, Freq = 640, Decay = 0.55, Tone = 0.62, Body = 0.55, Drive = 0.2, Warmth = 0.35, HpHz = 300, Length = 1.0, PeakDb = -9 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,     Note = 49, Freq = 350, Decay = 1.9, Tone = 0.6, Body = 0.5, Noise = 0.55, Drive = 0.15, Warmth = 0.25, Length = 2.4, PeakDb = -8 },
            new() { Name = "Snare Alt",  Model = DrumModel.SnarePunch, Note = 50, Freq = 260, Decay = 0.14, Tone = 0.75, Body = 0.3, Noise = 1.0, NoiseDecay = 0.06, Drive = 0.35, Warmth = 0.35, HpHz = 200, Length = 0.4, PeakDb = -5 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,     Note = 51, Freq = 450, Decay = 0.85, Tone = 0.8, Body = 0.4, Noise = 0.25, Drive = 0.12, Warmth = 0.25, Length = 1.3, PeakDb = -11 },
        },
    };

    // ======================================================================
    // 8. Foundry — struck metal and hard-clipped low end. The percussion is
    //    all high-Q modes with inharmonic spacing, i.e. plate and pipe rather
    //    than drum; the kicks are driven until they distort.
    // ======================================================================
    private static KitDefinition Foundry() => new()
    {
        Id = "foundry",
        Name = "Foundry",
        Blurb = "industrial metal · struck plate, pipe and hard-clipped kicks",
        FxSpace = 1.2,
        FxDrive = 1.4,
        Humanize = 0.06f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickPunch,  Note = 36, Freq = 50, Decay = 0.4, PitchAmt = 3.0, PitchDecay = 0.016, Tone = 0.6, Body = 0.6, Click = 0.6, Drive = 0.75, Warmth = 0.5, Length = 0.9, PeakDb = -1.5 },
            new() { Name = "Anvil",      Model = DrumModel.PercMetal,  Note = 37, Freq = 520, Decay = 0.35, Tone = 0.72, Body = 0.75, Drive = 0.5, Warmth = 0.4, HpHz = 280, Length = 0.7, PeakDb = -7 },
            new() { Name = "Snare",      Model = DrumModel.SnarePunch, Note = 38, Freq = 200, Decay = 0.24, Tone = 0.6, Body = 0.55, Noise = 0.9, NoiseDecay = 0.12, Drive = 0.65, Warmth = 0.45, Length = 0.6, PeakDb = -2.5 },
            new() { Name = "Clap Metal", Model = DrumModel.Clap,       Note = 39, Freq = 1450,Decay = 0.22, Tone = 0.75, Body = 0.6, Drive = 0.55, Warmth = 0.4, Length = 0.7, PeakDb = -5 },
            new() { Name = "Snare Trash",Model = DrumModel.SnarePunch, Note = 40, Freq = 275, Decay = 0.18, Tone = 0.78, Body = 0.4, Noise = 1.0, NoiseDecay = 0.09, Drive = 0.8, Warmth = 0.4, Length = 0.5, PeakDb = -4 },
            new() { Name = "Pipe Low",   Model = DrumModel.PercMetal,  Note = 41, Freq = 148, Decay = 0.8, Tone = 0.45, Body = 0.8, Drive = 0.45, Warmth = 0.5, Length = 1.4, PeakDb = -6 },
            new() { Name = "Closed Hat", Model = DrumModel.HatMetal,   Note = 42, Choke = 1, Freq = 350, Decay = 0.04, Tone = 0.3, Body = 0.8, Noise = 0.2, Drive = 0.45, Warmth = 0.3, Length = 0.25, PeakDb = -10 },
            new() { Name = "Pipe Mid",   Model = DrumModel.PercMetal,  Note = 43, Freq = 224, Decay = 0.65, Tone = 0.5, Body = 0.8, Drive = 0.45, Warmth = 0.5, Length = 1.2, PeakDb = -6 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatMetal,   Note = 44, Choke = 1, Freq = 350, Decay = 0.025, Tone = 0.28, Body = 0.75,Noise = 0.18, Drive = 0.4, Warmth = 0.3, Length = 0.2, PeakDb = -13 },
            new() { Name = "Pipe Hi",    Model = DrumModel.PercMetal,  Note = 45, Freq = 336, Decay = 0.5, Tone = 0.58, Body = 0.8, Drive = 0.45, Warmth = 0.5, Length = 1.0, PeakDb = -6 },
            new() { Name = "Open Hat",   Model = DrumModel.HatMetal,   Note = 46, Choke = 1, Freq = 350, Decay = 0.42, Tone = 0.32, Body = 0.8, Noise = 0.25, Drive = 0.45, Warmth = 0.3, Length = 1.0, PeakDb = -9 },
            new() { Name = "Scrap",      Model = DrumModel.Block,      Note = 47, Freq = 1650,Decay = 0.07, Tone = 0.85, Body = 0.8, Drive = 0.6, Warmth = 0.35, HpHz = 500, Length = 0.3, PeakDb = -9 },
            new() { Name = "Plate",      Model = DrumModel.Cymbal,     Note = 48, Freq = 240, Decay = 1.4, Tone = 0.3, Body = 0.7, Noise = 0.2, Drive = 0.4, Warmth = 0.4, Length = 2.0, PeakDb = -8 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,     Note = 49, Freq = 320, Decay = 2.4, Tone = 0.5, Body = 0.6, Noise = 0.45, Drive = 0.35, Warmth = 0.35, Length = 3.0, PeakDb = -7 },
            new() { Name = "Impact",     Model = DrumModel.Zap,        Note = 50, Freq = 60, Decay = 0.5, PitchAmt = 5, PitchDecay = 0.06, Tone = 0.6, Body = 0.8, Drive = 0.7, Warmth = 0.5, Length = 1.0, PeakDb = -4 },
            new() { Name = "Ride Bell",  Model = DrumModel.PercMetal,  Note = 51, Freq = 760, Decay = 0.7, Tone = 0.66, Body = 0.6, Drive = 0.3, Warmth = 0.35, HpHz = 350, Length = 1.2, PeakDb = -10 },
        },
    };

    // ======================================================================
    // 9. Terra — hands, wood and skin. Membrane modes for the drums, high-Q
    //    wood for the sticks, grainy noise for the shakers, and a wide, dry
    //    room so an ensemble spreads across the stereo field.
    // ======================================================================
    private static KitDefinition Terra() => new()
    {
        Id = "terra",
        Name = "Terra",
        Blurb = "hand percussion · congas, bongos, wood and shakers",
        FxSpace = 0.9,
        FxDrive = 0.6,
        Swing = 0.1f,
        Humanize = 0.2f,
        Pads = new List<KitPad>
        {
            new() { Name = "Cajon Bass", Model = DrumModel.KickAcoustic, Note = 36, Freq = 68, Decay = 0.32, PitchAmt = 0.5, PitchDecay = 0.02, Tone = 0.4, Body = 0.7, Click = 0.45, Drive = 0.2, Warmth = 0.6, Room = 0.2, RoomSize = 0.35, Width = 0.5, Drift = 0.3, Length = 0.8, PeakDb = -2 },
            new() { Name = "Clave",      Model = DrumModel.Block,        Note = 37, Freq = 2400, Decay = 0.05, Tone = 0.3, Body = 0.45, Drive = 0.15, Warmth = 0.5, Room = 0.16, Width = 0.6, Pan = -0.25f, Drift = 0.4, Length = 0.3, PeakDb = -8 },
            new() { Name = "Cajon Slap", Model = DrumModel.SnareAcoustic,Note = 38, Freq = 230, Decay = 0.2, Tone = 0.55, Body = 0.5, Noise = 0.55, NoiseDecay = 0.09, Click = 0.6, Drive = 0.22, Warmth = 0.55, Room = 0.22, RoomSize = 0.35, Width = 0.6, Drift = 0.3, Length = 0.6, PeakDb = -3 },
            new() { Name = "Clap",       Model = DrumModel.Clap,         Note = 39, Freq = 980, Decay = 0.18, Tone = 0.4, Body = 0.5, Drive = 0.15, Warmth = 0.5, Room = 0.26, RoomSize = 0.45, Width = 0.85, Drift = 0.6, Length = 0.7, PeakDb = -5 },
            new() { Name = "Tabla",      Model = DrumModel.Conga,        Note = 40, Freq = 290, Decay = 0.4, Tone = 0.55, Body = 0.75, Click = 0.45, Drive = 0.18, Warmth = 0.5, Room = 0.18, Width = 0.6, Pan = 0.2f, Drift = 0.35, Length = 0.9, PeakDb = -4 },
            new() { Name = "Conga Low",  Model = DrumModel.Conga,        Note = 41, Freq = 148, Decay = 0.44, Tone = 0.32, Body = 0.5, Click = 0.3, Drive = 0.2, Warmth = 0.6, Room = 0.2, RoomSize = 0.4, Width = 0.7, Pan = -0.3f, Drift = 0.3, Length = 1.0, PeakDb = -4 },
            new() { Name = "Shaker",     Model = DrumModel.Shaker,       Note = 42, Freq = 5400, Decay = 0.085, Tone = 0.5, Body = 0.85, Click = 0.55, Drive = 0.1, Warmth = 0.4, Room = 0.12, Width = 0.7, Pan = 0.3f, Drift = 0.6, Length = 0.3, PeakDb = -10 },
            new() { Name = "Conga Mid",  Model = DrumModel.Conga,        Note = 43, Freq = 208, Decay = 0.38, Tone = 0.38, Body = 0.5, Click = 0.32, Drive = 0.2, Warmth = 0.6, Room = 0.2, RoomSize = 0.4, Width = 0.7, Pan = -0.1f, Drift = 0.3, Length = 0.9, PeakDb = -4 },
            new() { Name = "Cabasa",     Model = DrumModel.Shaker,       Note = 44, Freq = 6600, Decay = 0.055, Tone = 0.6, Body = 0.95, Click = 0.7, Drive = 0.1, Warmth = 0.35, Room = 0.1, Width = 0.7, Pan = 0.4f, Drift = 0.6, Length = 0.25, PeakDb = -11 },
            new() { Name = "Bongo Low",  Model = DrumModel.Conga,        Note = 45, Freq = 330, Decay = 0.22, Tone = 0.5, Body = 0.45, Click = 0.5, Drive = 0.2, Warmth = 0.55, Room = 0.18, Width = 0.7, Pan = 0.15f, Drift = 0.35, Length = 0.55, PeakDb = -5 },
            new() { Name = "Tambourine", Model = DrumModel.Shaker,       Note = 46, Freq = 7600, Decay = 0.22, Tone = 0.72, Body = 0.9, Click = 0.85, Drive = 0.1, Warmth = 0.35, Room = 0.18, Width = 0.85, Drift = 0.6, Length = 0.6, PeakDb = -9 },
            new() { Name = "Bongo Hi",   Model = DrumModel.Conga,        Note = 47, Freq = 452, Decay = 0.18, Tone = 0.56, Body = 0.45, Click = 0.55, Drive = 0.2, Warmth = 0.55, Room = 0.18, Width = 0.7, Pan = 0.35f, Drift = 0.35, Length = 0.5, PeakDb = -5 },
            new() { Name = "Woodblock",  Model = DrumModel.Block,        Note = 48, Freq = 1750, Decay = 0.06, Tone = 0.45, Body = 0.5, Drive = 0.15, Warmth = 0.5, Room = 0.16, Width = 0.6, Pan = -0.35f, Drift = 0.4, Length = 0.3, PeakDb = -8 },
            new() { Name = "Triangle",   Model = DrumModel.PercMetal,    Note = 49, Freq = 2100, Decay = 1.6, Tone = 0.8, Body = 0.55, Drive = 0.08, Warmth = 0.3, Room = 0.16, Width = 0.8, Pan = 0.45f, Drift = 0.5, Length = 2.2, PeakDb = -11 },
            new() { Name = "Conga Hi",   Model = DrumModel.Conga,        Note = 50, Freq = 268, Decay = 0.32, Tone = 0.45, Body = 0.5, Click = 0.35, Drive = 0.2, Warmth = 0.6, Room = 0.2, RoomSize = 0.4, Width = 0.7, Pan = 0.1f, Drift = 0.3, Length = 0.8, PeakDb = -4 },
            new() { Name = "Agogo",      Model = DrumModel.Cowbell,      Note = 51, Freq = 720, Decay = 0.3, Tone = 0.55, Body = 0.5, Click = 0.3, Drive = 0.2, Warmth = 0.45, Room = 0.16, Width = 0.6, Pan = -0.2f, Drift = 0.35, Length = 0.7, PeakDb = -9 },
        },
    };

    // ======================================================================
    // 10. Aether — percussion as texture. Long decays, heavy room on nearly
    //     every pad, softened transients: for beds and slow music where the
    //     hits are part of the harmony rather than the grid.
    // ======================================================================
    private static KitDefinition Aether() => new()
    {
        Id = "aether",
        Name = "Aether",
        Blurb = "ambient & cinematic · soft, spacious, long tails",
        FxSpace = 1.6,
        FxDrive = 0.6,
        Humanize = 0.18f,
        Pads = new List<KitPad>
        {
            new() { Name = "Deep Kick",  Model = DrumModel.KickAnalog, Note = 36, Freq = 42, Decay = 1.3, PitchAmt = 1.0, PitchDecay = 0.06, Tone = 0.12, Body = 0.9, Click = 0.1, Drive = 0.15, Warmth = 0.7, LpHz = 3200, Room = 0.3, RoomSize = 0.7, RoomDecay = 0.6, RoomDamp = 0.7, Width = 0.8, Drift = 0.3, Length = 3.0, PeakDb = -2 },
            new() { Name = "Wood Tap",   Model = DrumModel.Block,      Note = 37, Freq = 1450,Decay = 0.09, Tone = 0.4, Body = 0.4, Drive = 0.1, Warmth = 0.55, Room = 0.5, RoomSize = 0.65, RoomDecay = 0.6, Width = 0.9, Drift = 0.5, Length = 1.4, PeakDb = -9 },
            new() { Name = "Soft Snare", Model = DrumModel.SnareAcoustic,Note = 38, Freq = 172, Decay = 0.42, Tone = 0.4, Body = 0.6, Noise = 0.6, NoiseDecay = 0.22, Click = 0.2, Drive = 0.12, Warmth = 0.6, LpHz = 9000, Room = 0.55, RoomSize = 0.75, RoomDecay = 0.65, RoomDamp = 0.5, Width = 0.9, Drift = 0.3, Length = 2.4, PeakDb = -3 },
            new() { Name = "Air Clap",   Model = DrumModel.Clap,       Note = 39, Freq = 900, Decay = 0.3, Tone = 0.4, Body = 0.6, Drive = 0.1, Warmth = 0.55, LpHz = 8000, Room = 0.6, RoomSize = 0.8, RoomDecay = 0.7, Width = 1.0, Drift = 0.6, Length = 2.6, PeakDb = -5 },
            new() { Name = "Rim Bloom",  Model = DrumModel.Rim,        Note = 40, Freq = 420, Decay = 0.09, Tone = 0.5, Body = 0.5, Drive = 0.1, Warmth = 0.5, Room = 0.62, RoomSize = 0.8, RoomDecay = 0.7, Width = 0.95, Drift = 0.5, Length = 2.4, PeakDb = -7 },
            new() { Name = "Low Drum",   Model = DrumModel.Tom,        Note = 41, Freq = 66, Decay = 0.9, PitchAmt = 0.2, PitchDecay = 0.07, Tone = 0.3, Body = 0.7, Noise = 0.25, NoiseDecay = 0.09, Drive = 0.12, Warmth = 0.6, LpHz = 5000, Room = 0.42, RoomSize = 0.7, RoomDecay = 0.6, Width = 0.85, Drift = 0.3, Length = 2.6, PeakDb = -4 },
            new() { Name = "Tick",       Model = DrumModel.HatNoise,   Note = 42, Choke = 1, Freq = 1600, Decay = 0.045, Tone = 0.6, Body = 0.5, Click = 0.3, Drive = 0.08, Warmth = 0.4, Room = 0.4, RoomSize = 0.6, Width = 0.9, Drift = 0.5, Length = 1.0, PeakDb = -11 },
            new() { Name = "Mid Drum",   Model = DrumModel.Tom,        Note = 43, Freq = 98, Decay = 0.75, PitchAmt = 0.2, PitchDecay = 0.06, Tone = 0.32, Body = 0.7, Noise = 0.25, NoiseDecay = 0.09, Drive = 0.12, Warmth = 0.6, LpHz = 5500, Room = 0.42, RoomSize = 0.7, RoomDecay = 0.6, Width = 0.85, Drift = 0.3, Length = 2.4, PeakDb = -4 },
            new() { Name = "Breath",     Model = DrumModel.Shaker,     Note = 44, Choke = 1, Freq = 3200, Decay = 0.2, Tone = 0.4, Body = 0.5, Click = 0.15, Drive = 0.08, Warmth = 0.45, LpHz = 9000, Room = 0.5, RoomSize = 0.7, Width = 1.0, Drift = 0.6, Length = 1.6, PeakDb = -12 },
            new() { Name = "Hi Drum",    Model = DrumModel.Tom,        Note = 45, Freq = 142, Decay = 0.62, PitchAmt = 0.2, PitchDecay = 0.055,Tone = 0.34, Body = 0.7, Noise = 0.25, NoiseDecay = 0.09, Drive = 0.12, Warmth = 0.6, LpHz = 6000, Room = 0.42, RoomSize = 0.7, RoomDecay = 0.6, Width = 0.85, Drift = 0.3, Length = 2.2, PeakDb = -4 },
            new() { Name = "Wash",       Model = DrumModel.HatNoise,   Note = 46, Choke = 1, Freq = 1400, Decay = 0.5, Tone = 0.6, Body = 0.5, Click = 0.15, Drive = 0.08, Warmth = 0.4, Room = 0.55, RoomSize = 0.75, RoomDecay = 0.7, Width = 1.0, Drift = 0.5, Length = 2.4, PeakDb = -10 },
            new() { Name = "Glass",      Model = DrumModel.PercMetal,  Note = 47, Freq = 1180,Decay = 1.5, Tone = 0.72, Body = 0.6, Drive = 0.08, Warmth = 0.35, Room = 0.5, RoomSize = 0.75, RoomDecay = 0.7, Width = 0.95, Pan = 0.3f, Drift = 0.5, Length = 3.4, PeakDb = -9 },
            new() { Name = "Bell",       Model = DrumModel.PercMetal,  Note = 48, Freq = 580, Decay = 2.0, Tone = 0.55, Body = 0.7, Drive = 0.1, Warmth = 0.4, Room = 0.45, RoomSize = 0.75, RoomDecay = 0.7, Width = 0.9, Pan = -0.3f, Drift = 0.5, Length = 4.0, PeakDb = -8 },
            new() { Name = "Swell",      Model = DrumModel.Cymbal,     Note = 49, Freq = 260, Decay = 3.2, Tone = 0.4, Body = 0.6, Noise = 0.6, Drive = 0.08, Warmth = 0.4, LpHz = 12000, Room = 0.55, RoomSize = 0.85, RoomDecay = 0.8, Width = 1.0, Drift = 0.4, Length = 4.5, PeakDb = -8 },
            new() { Name = "Sub Drop",   Model = DrumModel.SubHit,     Note = 50, Freq = 38, Decay = 2.2, PitchAmt = 0.6, PitchDecay = 0.1, Tone = 0.3, Drive = 0.25, Warmth = 0.6, Length = 3.4, PeakDb = -3 },
            new() { Name = "Shimmer",    Model = DrumModel.Cymbal,     Note = 51, Freq = 520, Decay = 2.2, Tone = 0.78, Body = 0.45, Noise = 0.35, Drive = 0.06, Warmth = 0.3, Room = 0.5, RoomSize = 0.8, RoomDecay = 0.75, Width = 1.0, Drift = 0.4, Length = 3.4, PeakDb = -11 },
        },
    };
}
