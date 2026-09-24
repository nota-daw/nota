// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The genre kits, 11-25: the second shelf of the factory library. Same rules as the
// first ten (KitCatalog.cs) — the General MIDI map on notes 36-51, the three hats in
// choke group 1 — and each kit keeps at least one pad name no other kit uses, so a
// loaded kit can be recognised from its pads (DrumKitService.Identify).
//
// Where a kit has a second pair that should cut each other (an open and a muted
// conga, a long and a short shaker), that pair shares choke group 2. A kit with no
// hats puts its own cut pair on the hat pads instead (Pit's triangles).

using System.Collections.Generic;

namespace Nota.Infrastructure.Kits;

public static partial class KitCatalog
{
    // ======================================================================
    // 11. Brass Room — a live rock kit in a big, bright tracking room. The
    //     room is on every pad, toms and cymbals are panned as the drummer
    //     sits, and the china is there for the last chorus.
    // ======================================================================
    private static KitDefinition BrassRoom() => new()
    {
        Id = "brass-room",
        Name = "Brass Room",
        Blurb = "live rock kit · big bright room, panned toms, china",
        FxSpace = 0.7,
        FxDrive = 0.8,
        Humanize = 0.1f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAcoustic, Note = 36, Freq = 52,  Decay = 0.55, PitchAmt = 0.8, PitchDecay = 0.022, Tone = 0.55, Body = 0.7, Click = 0.55, Drive = 0.3, Warmth = 0.55, Room = 0.3, RoomSize = 0.7, RoomDecay = 0.35, RoomDamp = 0.35, Width = 0.8, Drift = 0.2, Length = 1.4, PeakDb = -1.5 },
            new() { Name = "Side Stick", Model = DrumModel.Rim,          Note = 37, Freq = 510, Decay = 0.05, Tone = 0.55, Body = 0.5, Drive = 0.2, Warmth = 0.5, HpHz = 240, Room = 0.35, RoomSize = 0.7, RoomDecay = 0.35, Width = 0.8, Drift = 0.3, Length = 0.9, PeakDb = -9 },
            new() { Name = "Snare",      Model = DrumModel.SnareAcoustic,Note = 38, Freq = 194, Decay = 0.42, Tone = 0.62, Body = 0.62, Noise = 0.9, NoiseDecay = 0.22, Click = 0.55, Drive = 0.3, Warmth = 0.5, Room = 0.42, RoomSize = 0.75, RoomDecay = 0.4, RoomDamp = 0.3, Width = 0.85, Drift = 0.25, Length = 1.6, PeakDb = -2.5 },
            new() { Name = "Clap",       Model = DrumModel.Clap,         Note = 39, Freq = 1100,Decay = 0.22, Tone = 0.55, Body = 0.6, Drive = 0.2, Warmth = 0.5, Room = 0.4, RoomSize = 0.7, RoomDecay = 0.35, Width = 0.9, Drift = 0.5, Length = 1.2, PeakDb = -4.5 },
            new() { Name = "Snare Rim",  Model = DrumModel.SnareAcoustic,Note = 40, Freq = 228, Decay = 0.3, Tone = 0.7, Body = 0.55, Noise = 0.95, NoiseDecay = 0.15, Click = 0.7, Drive = 0.35, Warmth = 0.45, Room = 0.4, RoomSize = 0.75, RoomDecay = 0.4, Width = 0.85, Drift = 0.25, Length = 1.3, PeakDb = -3.5 },
            new() { Name = "Floor Tom",  Model = DrumModel.Tom,          Note = 41, Freq = 70,  Decay = 0.85, PitchAmt = 0.25, PitchDecay = 0.06, Tone = 0.45, Body = 0.7, Noise = 0.35, NoiseDecay = 0.08, Drive = 0.22, Warmth = 0.55, Room = 0.35, RoomSize = 0.75, RoomDecay = 0.4, Width = 0.85, Pan = -0.3f, Drift = 0.25, Length = 2.0, PeakDb = -4.5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 1300, Decay = 0.05, Tone = 0.68, Body = 0.55, Click = 0.45, Drive = 0.15, Warmth = 0.35, Room = 0.2, RoomSize = 0.6, Width = 0.7, Pan = 0.2f, Drift = 0.4, Length = 0.5, PeakDb = -10 },
            new() { Name = "Rack Tom Low",Model = DrumModel.Tom,         Note = 43, Freq = 98,  Decay = 0.7, PitchAmt = 0.25, PitchDecay = 0.055, Tone = 0.47, Body = 0.7, Noise = 0.35, NoiseDecay = 0.08, Drive = 0.22, Warmth = 0.55, Room = 0.35, RoomSize = 0.75, RoomDecay = 0.4, Width = 0.85, Pan = -0.1f, Drift = 0.25, Length = 1.8, PeakDb = -4.5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 1250, Decay = 0.035, Tone = 0.6, Body = 0.5, Click = 0.4, Drive = 0.12, Warmth = 0.35, Room = 0.18, RoomSize = 0.6, Width = 0.7, Pan = 0.2f, Drift = 0.4, Length = 0.4, PeakDb = -13 },
            new() { Name = "Rack Tom Hi",Model = DrumModel.Tom,          Note = 45, Freq = 136, Decay = 0.6, PitchAmt = 0.25, PitchDecay = 0.05, Tone = 0.5, Body = 0.7, Noise = 0.35, NoiseDecay = 0.08, Drive = 0.22, Warmth = 0.55, Room = 0.35, RoomSize = 0.75, RoomDecay = 0.4, Width = 0.85, Pan = 0.15f, Drift = 0.25, Length = 1.6, PeakDb = -4.5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 1300, Decay = 0.45, Tone = 0.72, Body = 0.55, Click = 0.4, Drive = 0.15, Warmth = 0.35, Room = 0.25, RoomSize = 0.6, Width = 0.7, Pan = 0.2f, Drift = 0.4, Length = 1.3, PeakDb = -9 },
            new() { Name = "Cowbell",    Model = DrumModel.Cowbell,      Note = 47, Freq = 560, Decay = 0.28, Tone = 0.5, Body = 0.5, Click = 0.35, Drive = 0.2, Warmth = 0.45, Room = 0.3, RoomSize = 0.7, Width = 0.8, Drift = 0.3, Length = 0.9, PeakDb = -9 },
            new() { Name = "Tambourine", Model = DrumModel.Shaker,       Note = 48, Freq = 7400,Decay = 0.2, Tone = 0.72, Body = 0.9, Click = 0.85, Drive = 0.1, Warmth = 0.35, Room = 0.3, RoomSize = 0.7, Width = 0.85, Pan = -0.25f, Drift = 0.6, Length = 0.8, PeakDb = -10 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,       Note = 49, Freq = 300, Decay = 3.0, Tone = 0.5, Body = 0.62, Noise = 0.55, Drive = 0.1, Warmth = 0.3, Room = 0.3, RoomSize = 0.75, RoomDecay = 0.4, Width = 0.9, Pan = -0.35f, Drift = 0.4, Length = 4.0, PeakDb = -7 },
            new() { Name = "China",      Model = DrumModel.Cymbal,       Note = 50, Freq = 380, Decay = 1.8, Tone = 0.35, Body = 0.75, Noise = 0.6, Drive = 0.3, Warmth = 0.3, Room = 0.28, RoomSize = 0.7, Width = 0.9, Pan = 0.4f, Drift = 0.4, Length = 2.6, PeakDb = -8 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,       Note = 51, Freq = 410, Decay = 1.7, Tone = 0.82, Body = 0.45, Noise = 0.3, Drive = 0.08, Warmth = 0.3, Room = 0.22, RoomSize = 0.7, Width = 0.8, Pan = 0.35f, Drift = 0.4, Length = 2.4, PeakDb = -10 },
        },
    };

    // ======================================================================
    // 12. Breakline — the chopped break of jungle and drum & bass: a small,
    //     bright, tuned-up acoustic kit through a 12-bit sampler, with ghost
    //     notes and flams on their own pads so a roll can be played.
    // ======================================================================
    private static KitDefinition Breakline() => new()
    {
        Id = "breakline",
        Name = "Breakline",
        Blurb = "jungle & breakbeat · tuned-up 12-bit break, ghosts and flams",
        FxSpace = 0.8,
        FxDrive = 1.1,
        FxTape = true,
        Humanize = 0.08f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAcoustic, Note = 36, Freq = 54,  Decay = 0.32, PitchAmt = 0.9, PitchDecay = 0.018, Tone = 0.6, Body = 0.5, Click = 0.6, Drive = 0.4, Warmth = 0.55, Bits = 12, CrushHz = 32000, HpHz = 35, Room = 0.15, RoomSize = 0.35, RoomDecay = 0.12, Width = 0.6, Drift = 0.2, Length = 0.7, PeakDb = -1.5 },
            new() { Name = "Ghost Snare",Model = DrumModel.SnareAcoustic,Note = 37, Freq = 250, Decay = 0.12, Tone = 0.6, Body = 0.4, Noise = 0.8, NoiseDecay = 0.06, Click = 0.3, Drive = 0.35, Warmth = 0.5, Bits = 12, CrushHz = 32000, HpHz = 120, Drift = 0.4, Length = 0.35, PeakDb = -9 },
            new() { Name = "Snare",      Model = DrumModel.SnareAcoustic,Note = 38, Freq = 238, Decay = 0.26, Tone = 0.72, Body = 0.55, Noise = 0.95, NoiseDecay = 0.13, Click = 0.7, Drive = 0.45, Warmth = 0.5, Bits = 12, CrushHz = 32000, Room = 0.22, RoomSize = 0.4, RoomDecay = 0.15, Width = 0.7, Drift = 0.25, Length = 0.7, PeakDb = -2 },
            new() { Name = "Clap",       Model = DrumModel.Clap,         Note = 39, Freq = 1250,Decay = 0.16, Tone = 0.62, Body = 0.45, Drive = 0.3, Warmth = 0.45, Bits = 12, CrushHz = 32000, Room = 0.18, Width = 0.7, Drift = 0.5, Length = 0.5, PeakDb = -5 },
            new() { Name = "Snare Crack",Model = DrumModel.SnarePunch,   Note = 40, Freq = 290, Decay = 0.14, Tone = 0.8, Body = 0.35, Noise = 1.0, NoiseDecay = 0.07, Drive = 0.5, Warmth = 0.4, Bits = 12, CrushHz = 32000, HpHz = 160, Drift = 0.3, Length = 0.4, PeakDb = -4 },
            new() { Name = "Tom Low",    Model = DrumModel.Tom,          Note = 41, Freq = 104, Decay = 0.36, PitchAmt = 0.35, PitchDecay = 0.035, Tone = 0.5, Body = 0.5, Noise = 0.3, NoiseDecay = 0.05, Drive = 0.3, Warmth = 0.5, Bits = 12, CrushHz = 32000, Room = 0.18, Width = 0.6, Drift = 0.3, Length = 0.8, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 1500, Decay = 0.035, Tone = 0.7, Body = 0.5, Click = 0.5, Drive = 0.25, Warmth = 0.35, Bits = 12, CrushHz = 32000, Drift = 0.5, Length = 0.2, PeakDb = -10 },
            new() { Name = "Tom Mid",    Model = DrumModel.Tom,          Note = 43, Freq = 140, Decay = 0.3, PitchAmt = 0.35, PitchDecay = 0.032, Tone = 0.5, Body = 0.5, Noise = 0.3, NoiseDecay = 0.05, Drive = 0.3, Warmth = 0.5, Bits = 12, CrushHz = 32000, Room = 0.18, Width = 0.6, Drift = 0.3, Length = 0.7, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 1450, Decay = 0.025, Tone = 0.65, Body = 0.45, Click = 0.45, Drive = 0.22, Warmth = 0.35, Bits = 12, CrushHz = 32000, Drift = 0.5, Length = 0.15, PeakDb = -13 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,          Note = 45, Freq = 186, Decay = 0.26, PitchAmt = 0.35, PitchDecay = 0.03, Tone = 0.5, Body = 0.5, Noise = 0.3, NoiseDecay = 0.05, Drive = 0.3, Warmth = 0.5, Bits = 12, CrushHz = 32000, Room = 0.18, Width = 0.6, Drift = 0.3, Length = 0.6, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 1500, Decay = 0.28, Tone = 0.72, Body = 0.5, Click = 0.45, Drive = 0.25, Warmth = 0.35, Bits = 12, CrushHz = 32000, Drift = 0.5, Length = 0.7, PeakDb = -9 },
            new() { Name = "Rim",        Model = DrumModel.Rim,          Note = 47, Freq = 560, Decay = 0.035, Tone = 0.65, Body = 0.5, Drive = 0.35, Warmth = 0.45, Bits = 12, CrushHz = 32000, HpHz = 300, Drift = 0.3, Length = 0.2, PeakDb = -9 },
            new() { Name = "Shaker",     Model = DrumModel.Shaker,       Note = 48, Freq = 5800,Decay = 0.07, Tone = 0.55, Body = 0.85, Click = 0.6, Drive = 0.15, Warmth = 0.4, Bits = 12, CrushHz = 32000, Drift = 0.6, Length = 0.25, PeakDb = -12 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,       Note = 49, Freq = 330, Decay = 1.6, Tone = 0.55, Body = 0.55, Noise = 0.5, Drive = 0.25, Warmth = 0.35, Bits = 12, CrushHz = 32000, Drift = 0.4, Length = 2.2, PeakDb = -8 },
            new() { Name = "Snare Flam", Model = DrumModel.SnareAcoustic,Note = 50, Freq = 262, Decay = 0.2, Tone = 0.7, Body = 0.5, Noise = 0.9, NoiseDecay = 0.1, Click = 0.8, Drive = 0.4, Warmth = 0.45, Bits = 12, CrushHz = 32000, Room = 0.18, Width = 0.7, Drift = 0.4, Length = 0.5, PeakDb = -5 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,       Note = 51, Freq = 440, Decay = 0.9, Tone = 0.8, Body = 0.4, Noise = 0.25, Drive = 0.15, Warmth = 0.3, Bits = 12, CrushHz = 32000, Drift = 0.4, Length = 1.3, PeakDb = -11 },
        },
    };

    // ======================================================================
    // 13. Bunker — warehouse techno. A driven kick with a rumble pad beside it
    //     (a low, dark, reverberant kick to layer under the main one), claps
    //     and snares with a concrete room, and hats cut high enough to tick.
    // ======================================================================
    private static KitDefinition Bunker() => new()
    {
        Id = "bunker",
        Name = "Bunker",
        Blurb = "warehouse techno · driven kick, rumble, concrete clap",
        FxSpace = 1.2,
        FxDrive = 1.3,
        Humanize = 0.02f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAnalog, Note = 36, Freq = 48,  Decay = 0.75, PitchAmt = 2.0, PitchDecay = 0.025, Tone = 0.3, Body = 0.8, Click = 0.4, Drive = 0.55, Warmth = 0.55, HpHz = 28, Drift = 0.15, Length = 1.2, PeakDb = -1.5 },
            new() { Name = "Rumble",     Model = DrumModel.KickAnalog, Note = 37, Freq = 44,  Decay = 1.6, PitchAmt = 0.6, PitchDecay = 0.05, Tone = 0.1, Body = 0.95, Click = 0.05, Drive = 0.5, Warmth = 0.7, LpHz = 900, Room = 0.45, RoomSize = 0.8, RoomDecay = 0.6, RoomDamp = 0.85, Width = 0.6, Drift = 0.2, Length = 2.6, PeakDb = -4 },
            new() { Name = "Snare",      Model = DrumModel.SnarePunch, Note = 38, Freq = 185, Decay = 0.22, Tone = 0.5, Body = 0.5, Noise = 0.9, NoiseDecay = 0.12, Drive = 0.55, Warmth = 0.45, Room = 0.25, RoomSize = 0.6, RoomDecay = 0.3, RoomDamp = 0.6, Width = 0.7, Drift = 0.2, Length = 0.9, PeakDb = -3 },
            new() { Name = "Clap",       Model = DrumModel.Clap,       Note = 39, Freq = 1050,Decay = 0.3, Tone = 0.55, Body = 0.7, Drive = 0.6, Warmth = 0.45, Room = 0.35, RoomSize = 0.7, RoomDecay = 0.45, RoomDamp = 0.5, Width = 0.85, Drift = 0.5, Length = 1.3, PeakDb = -4 },
            new() { Name = "Snare Noise",Model = DrumModel.SnarePunch, Note = 40, Freq = 240, Decay = 0.12, Tone = 0.85, Body = 0.2, Noise = 1.0, NoiseDecay = 0.08, Drive = 0.5, Warmth = 0.35, HpHz = 400, Drift = 0.2, Length = 0.4, PeakDb = -6 },
            new() { Name = "Tom Low",    Model = DrumModel.Tom,        Note = 41, Freq = 82,  Decay = 0.45, PitchAmt = 0.9, PitchDecay = 0.03, Tone = 0.3, Body = 0.35, Noise = 0.08, NoiseDecay = 0.03, Drive = 0.45, Warmth = 0.5, Drift = 0.2, Length = 0.9, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatMetal,   Note = 42, Choke = 1, Freq = 360, Decay = 0.04, Tone = 0.55, Body = 0.7, Noise = 0.45, Drive = 0.35, Warmth = 0.25, HpHz = 5000, Drift = 0.3, Length = 0.25, PeakDb = -10 },
            new() { Name = "Tom Mid",    Model = DrumModel.Tom,        Note = 43, Freq = 116, Decay = 0.38, PitchAmt = 0.9, PitchDecay = 0.028, Tone = 0.3, Body = 0.35, Noise = 0.08, NoiseDecay = 0.03, Drive = 0.45, Warmth = 0.5, Drift = 0.2, Length = 0.8, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatMetal,   Note = 44, Choke = 1, Freq = 360, Decay = 0.025, Tone = 0.5, Body = 0.65, Noise = 0.4, Drive = 0.3, Warmth = 0.25, HpHz = 5000, Drift = 0.3, Length = 0.18, PeakDb = -13 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,        Note = 45, Freq = 158, Decay = 0.32, PitchAmt = 0.9, PitchDecay = 0.025, Tone = 0.3, Body = 0.35, Noise = 0.08, NoiseDecay = 0.03, Drive = 0.45, Warmth = 0.5, Drift = 0.2, Length = 0.7, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatMetal,   Note = 46, Choke = 1, Freq = 360, Decay = 0.36, Tone = 0.6, Body = 0.75, Noise = 0.5, Drive = 0.35, Warmth = 0.25, HpHz = 4000, Drift = 0.3, Length = 0.9, PeakDb = -9 },
            new() { Name = "Rim Echo",   Model = DrumModel.Rim,        Note = 47, Freq = 620, Decay = 0.035, Tone = 0.7, Body = 0.55, Drive = 0.5, Warmth = 0.35, HpHz = 350, Room = 0.3, RoomSize = 0.7, RoomDecay = 0.4, Width = 0.8, Drift = 0.3, Length = 0.8, PeakDb = -8 },
            new() { Name = "Metal Hit",  Model = DrumModel.PercMetal,  Note = 48, Freq = 410, Decay = 0.4, Tone = 0.62, Body = 0.7, Drive = 0.45, Warmth = 0.35, HpHz = 300, Room = 0.2, Width = 0.7, Drift = 0.3, Length = 0.9, PeakDb = -9 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,     Note = 49, Freq = 330, Decay = 2.0, Tone = 0.45, Body = 0.65, Noise = 0.6, Drive = 0.4, Warmth = 0.3, HpHz = 600, Drift = 0.3, Length = 2.6, PeakDb = -8 },
            new() { Name = "Blip Low",   Model = DrumModel.Zap,        Note = 50, Freq = 95,  Decay = 0.18, PitchAmt = 6, PitchDecay = 0.03, Tone = 0.5, Body = 0.6, Drive = 0.55, Warmth = 0.4, Length = 0.45, PeakDb = -6 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,     Note = 51, Freq = 470, Decay = 0.8, Tone = 0.85, Body = 0.45, Noise = 0.3, Drive = 0.25, Warmth = 0.25, HpHz = 800, Drift = 0.3, Length = 1.2, PeakDb = -11 },
        },
    };

    // ======================================================================
    // 14. Pixel — minimal and micro-house: everything short, small and dry.
    //     Clicks instead of rims, three tuned blips instead of toms, and a
    //     few glassy ticks panned out wide for the offbeats.
    // ======================================================================
    private static KitDefinition Pixel() => new()
    {
        Id = "pixel",
        Name = "Pixel",
        Blurb = "minimal & micro-house · short clicks, blips and ticks",
        FxSpace = 0.8,
        FxDrive = 0.7,
        Swing = 0.08f,
        Humanize = 0.03f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickPunch,  Note = 36, Freq = 52,  Decay = 0.22, PitchAmt = 2.2, PitchDecay = 0.008, Tone = 0.7, Body = 0.3, Click = 0.75, Drive = 0.2, Warmth = 0.4, HpHz = 32, Length = 0.45, PeakDb = -1.5 },
            new() { Name = "Click",      Model = DrumModel.Block,      Note = 37, Freq = 3200,Decay = 0.012, Tone = 0.6, Body = 0.3, Drive = 0.1, Warmth = 0.3, HpHz = 800, Length = 0.08, PeakDb = -10 },
            new() { Name = "Snare",      Model = DrumModel.SnarePunch, Note = 38, Freq = 230, Decay = 0.12, Tone = 0.72, Body = 0.3, Noise = 0.9, NoiseDecay = 0.06, Drive = 0.2, Warmth = 0.35, HpHz = 180, Length = 0.3, PeakDb = -3 },
            new() { Name = "Clap",       Model = DrumModel.Clap,       Note = 39, Freq = 1400,Decay = 0.12, Tone = 0.72, Body = 0.3, Drive = 0.15, Warmth = 0.35, HpHz = 400, Room = 0.2, RoomSize = 0.3, RoomDecay = 0.1, Width = 0.9, Drift = 0.4, Length = 0.4, PeakDb = -5 },
            new() { Name = "Tick Snare", Model = DrumModel.Rim,        Note = 40, Freq = 820, Decay = 0.025, Tone = 0.8, Body = 0.6, Drive = 0.2, Warmth = 0.3, HpHz = 500, Length = 0.15, PeakDb = -7 },
            new() { Name = "Blip Low",   Model = DrumModel.Zap,        Note = 41, Freq = 160, Decay = 0.06, PitchAmt = 3, PitchDecay = 0.01, Tone = 0.3, Body = 0.4, Drive = 0.1, Warmth = 0.35, Length = 0.2, PeakDb = -6 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,   Note = 42, Choke = 1, Freq = 2000, Decay = 0.02, Tone = 0.75, Body = 0.4, Click = 0.5, Drive = 0.1, Warmth = 0.25, HpHz = 6000, Length = 0.12, PeakDb = -11 },
            new() { Name = "Blip Mid",   Model = DrumModel.Zap,        Note = 43, Freq = 260, Decay = 0.05, PitchAmt = 3, PitchDecay = 0.008, Tone = 0.3, Body = 0.4, Drive = 0.1, Warmth = 0.35, Length = 0.18, PeakDb = -7 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,   Note = 44, Choke = 1, Freq = 1900, Decay = 0.014, Tone = 0.7, Body = 0.4, Click = 0.45, Drive = 0.1, Warmth = 0.25, HpHz = 6000, Length = 0.1, PeakDb = -14 },
            new() { Name = "Blip Hi",    Model = DrumModel.Zap,        Note = 45, Freq = 420, Decay = 0.04, PitchAmt = 3, PitchDecay = 0.006, Tone = 0.3, Body = 0.4, Drive = 0.1, Warmth = 0.35, Length = 0.15, PeakDb = -8 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,   Note = 46, Choke = 1, Freq = 2000, Decay = 0.16, Tone = 0.78, Body = 0.45, Click = 0.4, Drive = 0.1, Warmth = 0.25, HpHz = 5000, Length = 0.4, PeakDb = -10 },
            new() { Name = "Wood",       Model = DrumModel.Block,      Note = 47, Freq = 1900,Decay = 0.03, Tone = 0.5, Body = 0.4, Drive = 0.1, Warmth = 0.4, Room = 0.2, RoomSize = 0.3, Width = 0.9, Pan = -0.4f, Drift = 0.4, Length = 0.3, PeakDb = -9 },
            new() { Name = "Glass Tick", Model = DrumModel.PercMetal,  Note = 48, Freq = 1850,Decay = 0.12, Tone = 0.75, Body = 0.5, Drive = 0.08, Warmth = 0.3, HpHz = 600, Pan = 0.4f, Drift = 0.4, Length = 0.35, PeakDb = -11 },
            new() { Name = "Noise Burst",Model = DrumModel.Cymbal,     Note = 49, Freq = 400, Decay = 0.6, Tone = 0.6, Body = 0.4, Noise = 0.7, Drive = 0.1, Warmth = 0.25, HpHz = 2000, Length = 0.9, PeakDb = -10 },
            new() { Name = "Dot Bell",   Model = DrumModel.PercMetal,  Note = 50, Freq = 880, Decay = 0.35, Tone = 0.6, Body = 0.55, Drive = 0.08, Warmth = 0.3, HpHz = 400, Room = 0.25, RoomSize = 0.4, Width = 0.9, Drift = 0.3, Length = 0.9, PeakDb = -10 },
            new() { Name = "Ping",       Model = DrumModel.Cymbal,     Note = 51, Freq = 560, Decay = 0.5, Tone = 0.9, Body = 0.35, Noise = 0.15, Drive = 0.05, Warmth = 0.25, HpHz = 1200, Length = 0.8, PeakDb = -12 },
        },
    };

    // ======================================================================
    // 15. Velvet — a jazz kit played with brushes. The snare is all wire and
    //     sweep, the kick is felt rather than heard, and the ride carries the
    //     time. Heavily swung out of the box.
    // ======================================================================
    private static KitDefinition Velvet() => new()
    {
        Id = "velvet",
        Name = "Velvet",
        Blurb = "jazz brushes · sweeps and taps, soft kick, singing ride",
        FxSpace = 0.8,
        FxDrive = 0.6,
        FxTape = true,
        Swing = 0.33f,
        Humanize = 0.18f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAcoustic, Note = 36, Freq = 52, Decay = 0.4, PitchAmt = 0.4, PitchDecay = 0.03, Tone = 0.3, Body = 0.7, Click = 0.2, Drive = 0.1, Warmth = 0.65, LpHz = 7000, Room = 0.25, RoomSize = 0.45, RoomDecay = 0.25, RoomDamp = 0.6, Width = 0.6, Drift = 0.25, Length = 1.2, PeakDb = -3 },
            new() { Name = "Cross Stick",Model = DrumModel.Rim,          Note = 37, Freq = 480, Decay = 0.06, Tone = 0.45, Body = 0.55, Drive = 0.1, Warmth = 0.55, HpHz = 220, Room = 0.25, RoomSize = 0.45, Width = 0.7, Drift = 0.3, Length = 0.6, PeakDb = -8 },
            new() { Name = "Brush Snare",Model = DrumModel.SnareAcoustic,Note = 38, Freq = 200, Decay = 0.3, Tone = 0.3, Body = 0.35, Noise = 0.95, NoiseDecay = 0.28, Click = 0.1, Drive = 0.08, Warmth = 0.6, LpHz = 9000, Room = 0.3, RoomSize = 0.45, RoomDecay = 0.25, Width = 0.7, Drift = 0.4, Length = 1.0, PeakDb = -4 },
            new() { Name = "Brush Sweep",Model = DrumModel.Shaker,       Note = 39, Freq = 3800,Decay = 0.22, Tone = 0.35, Body = 0.6, Click = 0.1, Drive = 0.05, Warmth = 0.5, LpHz = 8000, Room = 0.3, RoomSize = 0.45, Width = 0.8, Drift = 0.6, Length = 0.9, PeakDb = -9 },
            new() { Name = "Brush Slap", Model = DrumModel.SnareAcoustic,Note = 40, Freq = 220, Decay = 0.2, Tone = 0.4, Body = 0.45, Noise = 0.9, NoiseDecay = 0.12, Click = 0.35, Drive = 0.1, Warmth = 0.55, LpHz = 10000, Room = 0.3, RoomSize = 0.45, Width = 0.7, Drift = 0.35, Length = 0.8, PeakDb = -5 },
            new() { Name = "Floor Tom",  Model = DrumModel.Tom,          Note = 41, Freq = 82,  Decay = 0.8, PitchAmt = 0.15, PitchDecay = 0.06, Tone = 0.3, Body = 0.65, Noise = 0.4, NoiseDecay = 0.1, Drive = 0.08, Warmth = 0.6, LpHz = 6000, Room = 0.3, RoomSize = 0.45, Width = 0.7, Pan = -0.25f, Drift = 0.3, Length = 1.8, PeakDb = -6 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 1150, Decay = 0.06, Tone = 0.5, Body = 0.55, Click = 0.25, Drive = 0.05, Warmth = 0.45, Room = 0.2, RoomSize = 0.45, Width = 0.6, Pan = 0.25f, Drift = 0.5, Length = 0.5, PeakDb = -11 },
            new() { Name = "Rack Tom",   Model = DrumModel.Tom,          Note = 43, Freq = 124, Decay = 0.6, PitchAmt = 0.15, PitchDecay = 0.05, Tone = 0.32, Body = 0.65, Noise = 0.4, NoiseDecay = 0.1, Drive = 0.08, Warmth = 0.6, LpHz = 6500, Room = 0.3, RoomSize = 0.45, Width = 0.7, Pan = 0.1f, Drift = 0.3, Length = 1.5, PeakDb = -6 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 1100, Decay = 0.04, Tone = 0.45, Body = 0.5, Click = 0.2, Drive = 0.05, Warmth = 0.45, Room = 0.18, RoomSize = 0.45, Width = 0.6, Pan = 0.25f, Drift = 0.5, Length = 0.4, PeakDb = -13 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,          Note = 45, Freq = 168, Decay = 0.5, PitchAmt = 0.15, PitchDecay = 0.045, Tone = 0.34, Body = 0.65, Noise = 0.4, NoiseDecay = 0.1, Drive = 0.08, Warmth = 0.6, LpHz = 7000, Room = 0.3, RoomSize = 0.45, Width = 0.7, Pan = 0.2f, Drift = 0.3, Length = 1.3, PeakDb = -6 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 1150, Decay = 0.4, Tone = 0.55, Body = 0.55, Click = 0.2, Drive = 0.05, Warmth = 0.45, Room = 0.22, RoomSize = 0.45, Width = 0.6, Pan = 0.25f, Drift = 0.5, Length = 1.1, PeakDb = -10 },
            new() { Name = "Brush Tap",  Model = DrumModel.SnareAcoustic,Note = 47, Freq = 205, Decay = 0.1, Tone = 0.3, Body = 0.3, Noise = 0.9, NoiseDecay = 0.06, Click = 0.15, Drive = 0.05, Warmth = 0.6, LpHz = 9000, Room = 0.25, RoomSize = 0.45, Width = 0.7, Drift = 0.5, Length = 0.5, PeakDb = -10 },
            new() { Name = "Ride Bell",  Model = DrumModel.PercMetal,    Note = 48, Freq = 700, Decay = 0.9, Tone = 0.62, Body = 0.55, Drive = 0.05, Warmth = 0.35, HpHz = 300, Room = 0.25, RoomSize = 0.45, Width = 0.7, Pan = 0.35f, Drift = 0.3, Length = 1.6, PeakDb = -11 },
            new() { Name = "Sizzle",     Model = DrumModel.Cymbal,       Note = 49, Freq = 300, Decay = 3.2, Tone = 0.62, Body = 0.5, Noise = 0.6, Drive = 0.05, Warmth = 0.35, Room = 0.28, RoomSize = 0.5, Width = 0.85, Pan = -0.35f, Drift = 0.4, Length = 4.0, PeakDb = -9 },
            new() { Name = "Splash",     Model = DrumModel.Cymbal,       Note = 50, Freq = 520, Decay = 0.8, Tone = 0.6, Body = 0.45, Noise = 0.5, Drive = 0.05, Warmth = 0.35, Room = 0.25, RoomSize = 0.45, Width = 0.8, Pan = -0.2f, Drift = 0.4, Length = 1.3, PeakDb = -10 },
            new() { Name = "Jazz Ride",  Model = DrumModel.Cymbal,       Note = 51, Freq = 380, Decay = 2.4, Tone = 0.85, Body = 0.45, Noise = 0.3, Drive = 0.05, Warmth = 0.35, Room = 0.25, RoomSize = 0.5, Width = 0.8, Pan = 0.35f, Drift = 0.4, Length = 3.2, PeakDb = -9 },
        },
    };

    // ======================================================================
    // 16. Circuit — the thin, snappy analog box of early electro: a short
    //     round kick, a snare that is mostly noise, toms that are nearly
    //     sine, and a laser for the breaks.
    // ======================================================================
    private static KitDefinition Circuit() => new()
    {
        Id = "circuit",
        Name = "Circuit",
        Blurb = "electro box · snappy snare, sine toms, laser and clave",
        FxSpace = 1.0,
        FxDrive = 1.0,
        Humanize = 0.03f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAnalog, Note = 36, Freq = 51,  Decay = 0.4, PitchAmt = 1.4, PitchDecay = 0.018, Tone = 0.3, Body = 0.5, Click = 0.35, Drive = 0.3, Warmth = 0.5, Drift = 0.35, Length = 0.8, PeakDb = -1.5 },
            new() { Name = "Rim",        Model = DrumModel.Rim,        Note = 37, Freq = 610, Decay = 0.035, Tone = 0.65, Body = 0.5, Drive = 0.3, Warmth = 0.45, HpHz = 300, Drift = 0.35, Length = 0.2, PeakDb = -9 },
            new() { Name = "Snare",      Model = DrumModel.SnareAnalog,Note = 38, Freq = 260, Decay = 0.16, Tone = 0.72, Body = 0.35, Noise = 0.95, NoiseDecay = 0.08, Click = 0.45, Drive = 0.3, Warmth = 0.45, HpHz = 150, Drift = 0.35, Length = 0.4, PeakDb = -2.5 },
            new() { Name = "Clap",       Model = DrumModel.Clap,       Note = 39, Freq = 1300,Decay = 0.16, Tone = 0.65, Body = 0.45, Drive = 0.25, Warmth = 0.45, Drift = 0.5, Length = 0.5, PeakDb = -4 },
            new() { Name = "Snare Snap", Model = DrumModel.SnareAnalog,Note = 40, Freq = 330, Decay = 0.1, Tone = 0.85, Body = 0.25, Noise = 1.0, NoiseDecay = 0.05, Click = 0.55, Drive = 0.3, Warmth = 0.4, HpHz = 200, Drift = 0.35, Length = 0.3, PeakDb = -4 },
            new() { Name = "Tom Low",    Model = DrumModel.Tom,        Note = 41, Freq = 110, Decay = 0.3, PitchAmt = 0.7, PitchDecay = 0.025, Tone = 0.3, Body = 0.2, Noise = 0.05, NoiseDecay = 0.02, Drive = 0.3, Warmth = 0.5, Drift = 0.35, Length = 0.6, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatMetal,   Note = 42, Choke = 1, Freq = 470, Decay = 0.025, Tone = 0.3, Body = 0.8, Noise = 0.15, Drive = 0.25, Warmth = 0.3, HpHz = 6000, Drift = 0.35, Length = 0.15, PeakDb = -10 },
            new() { Name = "Tom Mid",    Model = DrumModel.Tom,        Note = 43, Freq = 150, Decay = 0.26, PitchAmt = 0.7, PitchDecay = 0.022, Tone = 0.3, Body = 0.2, Noise = 0.05, NoiseDecay = 0.02, Drive = 0.3, Warmth = 0.5, Drift = 0.35, Length = 0.55, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatMetal,   Note = 44, Choke = 1, Freq = 470, Decay = 0.018, Tone = 0.28, Body = 0.75, Noise = 0.12, Drive = 0.22, Warmth = 0.3, HpHz = 6000, Drift = 0.35, Length = 0.12, PeakDb = -13 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,        Note = 45, Freq = 205, Decay = 0.22, PitchAmt = 0.7, PitchDecay = 0.02, Tone = 0.3, Body = 0.2, Noise = 0.05, NoiseDecay = 0.02, Drive = 0.3, Warmth = 0.5, Drift = 0.35, Length = 0.5, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatMetal,   Note = 46, Choke = 1, Freq = 470, Decay = 0.22, Tone = 0.35, Body = 0.8, Noise = 0.2, Drive = 0.25, Warmth = 0.3, HpHz = 5000, Drift = 0.35, Length = 0.6, PeakDb = -9 },
            new() { Name = "Laser",      Model = DrumModel.Zap,        Note = 47, Freq = 140, Decay = 0.09, PitchAmt = 8, PitchDecay = 0.012, Tone = 0.2, Body = 0.5, Drive = 0.3, Warmth = 0.4, Length = 0.3, PeakDb = -7 },
            new() { Name = "Cowbell",    Model = DrumModel.Cowbell,    Note = 48, Freq = 590, Decay = 0.24, Tone = 0.5, Body = 0.45, Click = 0.3, Drive = 0.28, Warmth = 0.45, Drift = 0.35, Length = 0.5, PeakDb = -9 },
            new() { Name = "Cymbal",     Model = DrumModel.Cymbal,     Note = 49, Freq = 360, Decay = 1.2, Tone = 0.5, Body = 0.6, Noise = 0.5, Drive = 0.2, Warmth = 0.3, HpHz = 1500, Drift = 0.35, Length = 1.8, PeakDb = -9 },
            new() { Name = "Clave",      Model = DrumModel.Block,      Note = 50, Freq = 2500,Decay = 0.045, Tone = 0.35, Body = 0.4, Drive = 0.2, Warmth = 0.45, Drift = 0.35, Length = 0.25, PeakDb = -9 },
            new() { Name = "Metal Ride", Model = DrumModel.Cymbal,     Note = 51, Freq = 520, Decay = 0.7, Tone = 0.3, Body = 0.85, Noise = 0.1, Drive = 0.2, Warmth = 0.3, HpHz = 1500, Drift = 0.35, Length = 1.1, PeakDb = -11 },
        },
    };

    // ======================================================================
    // 17. Yard — dub and roots reggae. A warm, dark kick; rimshot and snare
    //     thrown into a long spring-ish room; toms that drop like a syndrum;
    //     an open and a muted conga that cut each other; and a dub siren.
    // ======================================================================
    private static KitDefinition Yard() => new()
    {
        Id = "yard",
        Name = "Yard",
        Blurb = "dub & reggae · rimshot in a long room, dropping toms, siren",
        FxSpace = 1.3,
        FxDrive = 0.9,
        FxTape = true,
        Swing = 0.1f,
        Humanize = 0.12f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAcoustic, Note = 36, Freq = 50,  Decay = 0.6, PitchAmt = 0.5, PitchDecay = 0.03, Tone = 0.25, Body = 0.8, Click = 0.3, Drive = 0.3, Warmth = 0.75, LpHz = 4500, Room = 0.18, RoomSize = 0.4, RoomDamp = 0.8, Width = 0.5, Drift = 0.25, Length = 1.4, PeakDb = -1.5 },
            new() { Name = "Rimshot",    Model = DrumModel.Rim,          Note = 37, Freq = 540, Decay = 0.06, Tone = 0.6, Body = 0.6, Drive = 0.3, Warmth = 0.6, HpHz = 250, Room = 0.45, RoomSize = 0.75, RoomDecay = 0.6, RoomDamp = 0.45, Width = 0.9, Drift = 0.3, Length = 1.8, PeakDb = -5 },
            new() { Name = "Snare",      Model = DrumModel.SnareAcoustic,Note = 38, Freq = 225, Decay = 0.26, Tone = 0.6, Body = 0.65, Noise = 0.7, NoiseDecay = 0.12, Click = 0.6, Drive = 0.35, Warmth = 0.65, Room = 0.4, RoomSize = 0.75, RoomDecay = 0.55, RoomDamp = 0.45, Width = 0.9, Drift = 0.3, Length = 1.8, PeakDb = -2.5 },
            new() { Name = "Clap",       Model = DrumModel.Clap,         Note = 39, Freq = 1000,Decay = 0.2, Tone = 0.45, Body = 0.55, Drive = 0.25, Warmth = 0.6, Room = 0.4, RoomSize = 0.75, RoomDecay = 0.55, Width = 0.9, Drift = 0.5, Length = 1.5, PeakDb = -5 },
            new() { Name = "Snare Dub",  Model = DrumModel.SnareAcoustic,Note = 40, Freq = 250, Decay = 0.2, Tone = 0.65, Body = 0.6, Noise = 0.8, NoiseDecay = 0.1, Click = 0.7, Drive = 0.4, Warmth = 0.6, HpHz = 200, Room = 0.6, RoomSize = 0.85, RoomDecay = 0.8, RoomDamp = 0.35, Width = 1.0, Drift = 0.3, Length = 3.0, PeakDb = -4 },
            new() { Name = "Tom Low",    Model = DrumModel.Tom,          Note = 41, Freq = 90,  Decay = 0.5, PitchAmt = 1.0, PitchDecay = 0.05, Tone = 0.35, Body = 0.6, Noise = 0.2, NoiseDecay = 0.05, Drive = 0.3, Warmth = 0.65, Room = 0.35, RoomSize = 0.7, RoomDecay = 0.5, Width = 0.85, Pan = -0.3f, Drift = 0.3, Length = 1.6, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 950, Decay = 0.05, Tone = 0.45, Body = 0.55, Click = 0.35, Drive = 0.2, Warmth = 0.55, LpHz = 9000, Drift = 0.5, Length = 0.3, PeakDb = -11 },
            new() { Name = "Tom Mid",    Model = DrumModel.Tom,          Note = 43, Freq = 128, Decay = 0.44, PitchAmt = 1.0, PitchDecay = 0.045, Tone = 0.35, Body = 0.6, Noise = 0.2, NoiseDecay = 0.05, Drive = 0.3, Warmth = 0.65, Room = 0.35, RoomSize = 0.7, RoomDecay = 0.5, Width = 0.85, Drift = 0.3, Length = 1.5, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 900, Decay = 0.03, Tone = 0.4, Body = 0.5, Click = 0.3, Drive = 0.2, Warmth = 0.55, LpHz = 8500, Drift = 0.5, Length = 0.2, PeakDb = -14 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,          Note = 45, Freq = 172, Decay = 0.38, PitchAmt = 1.0, PitchDecay = 0.04, Tone = 0.35, Body = 0.6, Noise = 0.2, NoiseDecay = 0.05, Drive = 0.3, Warmth = 0.65, Room = 0.35, RoomSize = 0.7, RoomDecay = 0.5, Width = 0.85, Pan = 0.3f, Drift = 0.3, Length = 1.4, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 950, Decay = 0.34, Tone = 0.5, Body = 0.55, Click = 0.3, Drive = 0.2, Warmth = 0.55, LpHz = 9000, Drift = 0.5, Length = 0.9, PeakDb = -10 },
            new() { Name = "Conga Open", Model = DrumModel.Conga,        Note = 47, Choke = 2, Freq = 250, Decay = 0.34, Tone = 0.4, Body = 0.5, Click = 0.35, Drive = 0.2, Warmth = 0.6, Room = 0.25, RoomSize = 0.6, Width = 0.8, Pan = -0.25f, Drift = 0.3, Length = 0.9, PeakDb = -5 },
            new() { Name = "Conga Mute", Model = DrumModel.Conga,        Note = 48, Choke = 2, Freq = 260, Decay = 0.08, Tone = 0.55, Body = 0.25, Click = 0.55, Drive = 0.2, Warmth = 0.6, Room = 0.2, RoomSize = 0.6, Width = 0.8, Pan = -0.25f, Drift = 0.3, Length = 0.3, PeakDb = -7 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,       Note = 49, Freq = 290, Decay = 2.4, Tone = 0.45, Body = 0.6, Noise = 0.5, Drive = 0.15, Warmth = 0.5, LpHz = 11000, Room = 0.3, RoomSize = 0.75, Width = 0.9, Drift = 0.4, Length = 3.2, PeakDb = -8 },
            new() { Name = "Dub Siren",  Model = DrumModel.Zap,          Note = 50, Freq = 220, Decay = 0.45, PitchAmt = 4, PitchDecay = 0.12, Tone = 0.6, Body = 0.7, Drive = 0.3, Warmth = 0.5, Room = 0.5, RoomSize = 0.85, RoomDecay = 0.8, Width = 1.0, Length = 2.4, PeakDb = -7 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,       Note = 51, Freq = 400, Decay = 1.2, Tone = 0.75, Body = 0.45, Noise = 0.3, Drive = 0.12, Warmth = 0.5, LpHz = 11000, Room = 0.2, RoomSize = 0.6, Width = 0.8, Drift = 0.4, Length = 1.8, PeakDb = -11 },
        },
    };

    // ======================================================================
    // 18. Byte — a game console's sound chip: everything through a 5-bit,
    //     11 kHz crusher, noise-channel hats, pitch-drop toms, a coin, a
    //     laser and a square bass to play the riff on.
    // ======================================================================
    private static KitDefinition Byte() => new()
    {
        Id = "byte",
        Name = "Byte",
        Blurb = "8-bit chip · crushed noise hats, pitch drops, coin, square bass",
        FxSpace = 0.7,
        FxDrive = 0.8,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickPunch,  Note = 36, Freq = 58,  Decay = 0.18, PitchAmt = 3.6, PitchDecay = 0.012, Tone = 0.6, Body = 0.3, Click = 0.5, Drive = 0.6, Warmth = 0.2, Bits = 5, CrushHz = 11025, HpHz = 30, Length = 0.35, PeakDb = -2 },
            new() { Name = "Blip",       Model = DrumModel.Zap,        Note = 37, Freq = 520, Decay = 0.05, PitchAmt = 1, PitchDecay = 0.01, Tone = 0.2, Body = 0.4, Drive = 0.5, Warmth = 0.2, Bits = 5, CrushHz = 11025, Length = 0.12, PeakDb = -8 },
            new() { Name = "Snare",      Model = DrumModel.SnarePunch, Note = 38, Freq = 240, Decay = 0.14, Tone = 0.6, Body = 0.3, Noise = 1.0, NoiseDecay = 0.1, Drive = 0.5, Warmth = 0.2, Bits = 5, CrushHz = 11025, Length = 0.3, PeakDb = -3 },
            new() { Name = "Noise Clap", Model = DrumModel.Clap,       Note = 39, Freq = 1200,Decay = 0.12, Tone = 0.6, Body = 0.35, Drive = 0.4, Warmth = 0.2, Bits = 5, CrushHz = 11025, Length = 0.3, PeakDb = -5 },
            new() { Name = "Snare Hi",   Model = DrumModel.SnarePunch, Note = 40, Freq = 360, Decay = 0.08, Tone = 0.8, Body = 0.2, Noise = 1.0, NoiseDecay = 0.05, Drive = 0.5, Warmth = 0.2, Bits = 5, CrushHz = 11025, Length = 0.2, PeakDb = -5 },
            new() { Name = "Drop Low",   Model = DrumModel.Tom,        Note = 41, Freq = 110, Decay = 0.25, PitchAmt = 1.5, PitchDecay = 0.05, Tone = 0.2, Body = 0.2, Drive = 0.5, Warmth = 0.2, Bits = 5, CrushHz = 11025, Length = 0.4, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,   Note = 42, Choke = 1, Freq = 3000, Decay = 0.02, Tone = 0.8, Body = 0.3, Click = 0.5, Drive = 0.3, Warmth = 0.1, Bits = 5, CrushHz = 11025, Length = 0.1, PeakDb = -11 },
            new() { Name = "Drop Mid",   Model = DrumModel.Tom,        Note = 43, Freq = 160, Decay = 0.2, PitchAmt = 1.5, PitchDecay = 0.045, Tone = 0.2, Body = 0.2, Drive = 0.5, Warmth = 0.2, Bits = 5, CrushHz = 11025, Length = 0.35, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,   Note = 44, Choke = 1, Freq = 2800, Decay = 0.012, Tone = 0.75, Body = 0.3, Click = 0.45, Drive = 0.3, Warmth = 0.1, Bits = 5, CrushHz = 11025, Length = 0.08, PeakDb = -14 },
            new() { Name = "Drop Hi",    Model = DrumModel.Tom,        Note = 45, Freq = 230, Decay = 0.16, PitchAmt = 1.5, PitchDecay = 0.04, Tone = 0.2, Body = 0.2, Drive = 0.5, Warmth = 0.2, Bits = 5, CrushHz = 11025, Length = 0.3, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,   Note = 46, Choke = 1, Freq = 3000, Decay = 0.14, Tone = 0.8, Body = 0.3, Click = 0.4, Drive = 0.3, Warmth = 0.1, Bits = 5, CrushHz = 11025, Length = 0.35, PeakDb = -10 },
            new() { Name = "Coin",       Model = DrumModel.PercMetal,  Note = 47, Freq = 1320,Decay = 0.25, Tone = 0.5, Body = 0.3, Drive = 0.4, Warmth = 0.15, Bits = 5, CrushHz = 11025, HpHz = 500, Length = 0.4, PeakDb = -9 },
            new() { Name = "Laser",      Model = DrumModel.Zap,        Note = 48, Freq = 110, Decay = 0.25, PitchAmt = 9, PitchDecay = 0.06, Tone = 0.4, Body = 0.5, Drive = 0.5, Warmth = 0.2, Bits = 5, CrushHz = 11025, Length = 0.5, PeakDb = -7 },
            new() { Name = "Noise Crash",Model = DrumModel.Cymbal,     Note = 49, Freq = 400, Decay = 0.9, Tone = 0.3, Body = 0.5, Noise = 0.8, Drive = 0.4, Warmth = 0.1, Bits = 5, CrushHz = 11025, Length = 1.2, PeakDb = -9 },
            new() { Name = "Square Bass",Model = DrumModel.SubHit,     Note = 50, Freq = 55,  Decay = 0.6, PitchAmt = 0.3, PitchDecay = 0.02, Tone = 0.7, Drive = 0.6, Warmth = 0.3, Bits = 5, CrushHz = 11025, Length = 0.9, PeakDb = -3 },
            new() { Name = "Chip Bell",  Model = DrumModel.Cowbell,    Note = 51, Freq = 880, Decay = 0.25, Tone = 0.5, Body = 0.4, Click = 0.2, Drive = 0.3, Warmth = 0.15, Bits = 5, CrushHz = 11025, Length = 0.5, PeakDb = -10 },
        },
    };

    // ======================================================================
    // 19. Lagoon — amapiano and afro-house. Three log drums (the pitched,
    //     woody bass hit the style is built on), a warm roomy clap, and a
    //     short and a long shaker that cut each other.
    // ======================================================================
    private static KitDefinition Lagoon() => new()
    {
        Id = "lagoon",
        Name = "Lagoon",
        Blurb = "amapiano & afro-house · log drums, shakers, wood and bongo",
        FxSpace = 1.2,
        FxDrive = 0.8,
        Swing = 0.12f,
        Humanize = 0.1f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickPunch,    Note = 36, Freq = 52,  Decay = 0.4, PitchAmt = 2.2, PitchDecay = 0.016, Tone = 0.45, Body = 0.6, Click = 0.45, Drive = 0.25, Warmth = 0.55, Drift = 0.2, Length = 0.9, PeakDb = -1.5 },
            new() { Name = "Log Drum",   Model = DrumModel.Tom,          Note = 37, Freq = 62,  Decay = 0.55, PitchAmt = 1.2, PitchDecay = 0.03, Tone = 0.2, Body = 0.35, Noise = 0.05, NoiseDecay = 0.02, Drive = 0.45, Warmth = 0.65, LpHz = 3500, Drift = 0.2, Length = 1.2, PeakDb = -1.5 },
            new() { Name = "Snare",      Model = DrumModel.SnareAcoustic,Note = 38, Freq = 240, Decay = 0.18, Tone = 0.6, Body = 0.45, Noise = 0.8, NoiseDecay = 0.09, Click = 0.5, Drive = 0.25, Warmth = 0.5, Room = 0.2, RoomSize = 0.5, Width = 0.8, Drift = 0.3, Length = 0.7, PeakDb = -3 },
            new() { Name = "Clap",       Model = DrumModel.Clap,         Note = 39, Freq = 1150,Decay = 0.22, Tone = 0.5, Body = 0.6, Drive = 0.2, Warmth = 0.5, Room = 0.3, RoomSize = 0.6, RoomDecay = 0.35, Width = 0.9, Drift = 0.5, Length = 1.1, PeakDb = -4 },
            new() { Name = "Log Drum Hi",Model = DrumModel.Tom,          Note = 40, Freq = 94,  Decay = 0.45, PitchAmt = 1.0, PitchDecay = 0.028, Tone = 0.22, Body = 0.35, Noise = 0.05, NoiseDecay = 0.02, Drive = 0.45, Warmth = 0.65, LpHz = 4000, Drift = 0.2, Length = 1.0, PeakDb = -2 },
            new() { Name = "Log Drum Low",Model = DrumModel.Tom,         Note = 41, Freq = 47,  Decay = 0.7, PitchAmt = 1.4, PitchDecay = 0.035, Tone = 0.18, Body = 0.35, Noise = 0.05, NoiseDecay = 0.02, Drive = 0.45, Warmth = 0.65, LpHz = 3000, Drift = 0.2, Length = 1.4, PeakDb = -1.5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 1400, Decay = 0.04, Tone = 0.62, Body = 0.5, Click = 0.4, Drive = 0.1, Warmth = 0.4, Drift = 0.5, Length = 0.25, PeakDb = -11 },
            new() { Name = "Shaker",     Model = DrumModel.Shaker,       Note = 43, Choke = 2, Freq = 5600, Decay = 0.07, Tone = 0.55, Body = 0.85, Click = 0.55, Drive = 0.1, Warmth = 0.4, Pan = 0.3f, Drift = 0.6, Length = 0.25, PeakDb = -11 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 1350, Decay = 0.025, Tone = 0.58, Body = 0.45, Click = 0.35, Drive = 0.1, Warmth = 0.4, Drift = 0.5, Length = 0.18, PeakDb = -14 },
            new() { Name = "Shaker Long",Model = DrumModel.Shaker,       Note = 45, Choke = 2, Freq = 5200, Decay = 0.2, Tone = 0.5, Body = 0.8, Click = 0.3, Drive = 0.1, Warmth = 0.4, Pan = 0.3f, Drift = 0.6, Length = 0.5, PeakDb = -11 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 1400, Decay = 0.3, Tone = 0.65, Body = 0.5, Click = 0.35, Drive = 0.1, Warmth = 0.4, Drift = 0.5, Length = 0.8, PeakDb = -10 },
            new() { Name = "Woodblock",  Model = DrumModel.Block,        Note = 47, Freq = 1650,Decay = 0.05, Tone = 0.45, Body = 0.5, Drive = 0.15, Warmth = 0.5, Room = 0.2, RoomSize = 0.5, Width = 0.8, Pan = -0.3f, Drift = 0.4, Length = 0.4, PeakDb = -8 },
            new() { Name = "Bongo",      Model = DrumModel.Conga,        Note = 48, Freq = 420, Decay = 0.18, Tone = 0.55, Body = 0.45, Click = 0.5, Drive = 0.2, Warmth = 0.55, Room = 0.18, Width = 0.7, Pan = 0.25f, Drift = 0.35, Length = 0.5, PeakDb = -6 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,       Note = 49, Freq = 320, Decay = 1.8, Tone = 0.5, Body = 0.55, Noise = 0.5, Drive = 0.1, Warmth = 0.35, Room = 0.2, RoomSize = 0.6, Width = 0.85, Drift = 0.4, Length = 2.4, PeakDb = -9 },
            new() { Name = "Thumb Bell", Model = DrumModel.PercMetal,    Note = 50, Freq = 740, Decay = 0.6, Tone = 0.45, Body = 0.6, Drive = 0.1, Warmth = 0.4, HpHz = 250, Room = 0.25, RoomSize = 0.6, Width = 0.85, Pan = -0.2f, Drift = 0.35, Length = 1.2, PeakDb = -9 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,       Note = 51, Freq = 430, Decay = 0.9, Tone = 0.78, Body = 0.45, Noise = 0.25, Drive = 0.08, Warmth = 0.35, Room = 0.15, Width = 0.8, Drift = 0.4, Length = 1.4, PeakDb = -11 },
        },
    };

    // ======================================================================
    // 20. Titan — trailer percussion. Everything is big: a booming low hit,
    //     three taiko, a war snare, steel and a braam in a hall, cymbal swells
    //     and a gong. Loud on purpose; the room is the instrument.
    // ======================================================================
    private static KitDefinition Titan() => new()
    {
        Id = "titan",
        Name = "Titan",
        Blurb = "cinematic trailer · boom, taiko, war snare, braam, gong",
        FxSpace = 1.5,
        FxDrive = 1.2,
        Humanize = 0.08f,
        Pads = new List<KitPad>
        {
            new() { Name = "Boom",       Model = DrumModel.KickAnalog,   Note = 36, Freq = 38,  Decay = 2.2, PitchAmt = 1.5, PitchDecay = 0.08, Tone = 0.2, Body = 0.95, Click = 0.3, Drive = 0.45, Warmth = 0.7, Room = 0.4, RoomSize = 0.9, RoomDecay = 0.8, RoomDamp = 0.6, Width = 1.0, Drift = 0.2, Length = 4.5, PeakDb = -1 },
            new() { Name = "Impact",     Model = DrumModel.Zap,          Note = 37, Freq = 58,  Decay = 0.8, PitchAmt = 4, PitchDecay = 0.08, Tone = 0.7, Body = 0.8, Drive = 0.7, Warmth = 0.55, Room = 0.45, RoomSize = 0.9, RoomDecay = 0.8, Width = 1.0, Length = 3.5, PeakDb = -2 },
            new() { Name = "War Snare",  Model = DrumModel.SnareAcoustic,Note = 38, Freq = 180, Decay = 0.5, Tone = 0.55, Body = 0.7, Noise = 0.9, NoiseDecay = 0.25, Click = 0.65, Drive = 0.4, Warmth = 0.55, Room = 0.55, RoomSize = 0.9, RoomDecay = 0.75, RoomDamp = 0.4, Width = 1.0, Drift = 0.25, Length = 3.0, PeakDb = -2.5 },
            new() { Name = "Stomp Clap", Model = DrumModel.Clap,         Note = 39, Freq = 700, Decay = 0.35, Tone = 0.3, Body = 0.8, Drive = 0.3, Warmth = 0.6, Room = 0.5, RoomSize = 0.85, RoomDecay = 0.7, Width = 1.0, Drift = 0.5, Length = 2.5, PeakDb = -4 },
            new() { Name = "Snare Crack",Model = DrumModel.SnareAcoustic,Note = 40, Freq = 220, Decay = 0.35, Tone = 0.7, Body = 0.6, Noise = 0.95, NoiseDecay = 0.18, Click = 0.85, Drive = 0.5, Warmth = 0.5, Room = 0.55, RoomSize = 0.9, RoomDecay = 0.75, Width = 1.0, Drift = 0.25, Length = 2.6, PeakDb = -3 },
            new() { Name = "Taiko Low",  Model = DrumModel.Tom,          Note = 41, Freq = 58,  Decay = 1.2, PitchAmt = 0.35, PitchDecay = 0.06, Tone = 0.35, Body = 0.85, Noise = 0.4, NoiseDecay = 0.08, Drive = 0.35, Warmth = 0.65, Room = 0.5, RoomSize = 0.9, RoomDecay = 0.8, Width = 1.0, Pan = -0.3f, Drift = 0.25, Length = 4.0, PeakDb = -2 },
            new() { Name = "Ticker",     Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 1600, Decay = 0.05, Tone = 0.6, Body = 0.5, Click = 0.5, Drive = 0.15, Warmth = 0.4, Room = 0.4, RoomSize = 0.8, RoomDecay = 0.6, Width = 1.0, Drift = 0.4, Length = 1.4, PeakDb = -10 },
            new() { Name = "Taiko Mid",  Model = DrumModel.Tom,          Note = 43, Freq = 82,  Decay = 1.0, PitchAmt = 0.35, PitchDecay = 0.055, Tone = 0.38, Body = 0.85, Noise = 0.4, NoiseDecay = 0.08, Drive = 0.35, Warmth = 0.65, Room = 0.5, RoomSize = 0.9, RoomDecay = 0.8, Width = 1.0, Drift = 0.25, Length = 3.6, PeakDb = -2 },
            new() { Name = "Ticker Soft",Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 1500, Decay = 0.035, Tone = 0.55, Body = 0.45, Click = 0.4, Drive = 0.12, Warmth = 0.4, Room = 0.35, RoomSize = 0.8, RoomDecay = 0.6, Width = 1.0, Drift = 0.4, Length = 1.2, PeakDb = -13 },
            new() { Name = "Taiko Hi",   Model = DrumModel.Tom,          Note = 45, Freq = 118, Decay = 0.8, PitchAmt = 0.35, PitchDecay = 0.05, Tone = 0.4, Body = 0.85, Noise = 0.4, NoiseDecay = 0.08, Drive = 0.35, Warmth = 0.65, Room = 0.5, RoomSize = 0.9, RoomDecay = 0.8, Width = 1.0, Pan = 0.3f, Drift = 0.25, Length = 3.2, PeakDb = -2 },
            new() { Name = "Hiss",       Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 1400, Decay = 0.6, Tone = 0.6, Body = 0.5, Click = 0.2, Drive = 0.12, Warmth = 0.4, Room = 0.45, RoomSize = 0.85, RoomDecay = 0.7, Width = 1.0, Drift = 0.4, Length = 2.4, PeakDb = -9 },
            new() { Name = "Steel Hit",  Model = DrumModel.PercMetal,    Note = 47, Freq = 470, Decay = 1.2, Tone = 0.7, Body = 0.8, Drive = 0.4, Warmth = 0.45, HpHz = 150, Room = 0.45, RoomSize = 0.9, RoomDecay = 0.8, Width = 1.0, Drift = 0.3, Length = 3.0, PeakDb = -5 },
            new() { Name = "Low Braam",  Model = DrumModel.Zap,          Note = 48, Freq = 45,  Decay = 1.8, PitchAmt = 0.5, PitchDecay = 0.3, Tone = 0.8, Body = 0.9, Drive = 0.8, Warmth = 0.6, Room = 0.4, RoomSize = 0.9, RoomDecay = 0.8, Width = 1.0, Length = 3.6, PeakDb = -3 },
            new() { Name = "Cymbal Swell",Model = DrumModel.Cymbal,      Note = 49, Freq = 270, Decay = 4.0, Tone = 0.4, Body = 0.7, Noise = 0.6, Drive = 0.15, Warmth = 0.4, Room = 0.5, RoomSize = 0.9, RoomDecay = 0.8, Width = 1.0, Drift = 0.4, Length = 5.0, PeakDb = -6 },
            new() { Name = "Sub Boom",   Model = DrumModel.SubHit,       Note = 50, Freq = 34,  Decay = 3.0, PitchAmt = 1.0, PitchDecay = 0.25, Tone = 0.3, Drive = 0.3, Warmth = 0.6, Length = 4.0, PeakDb = -2 },
            new() { Name = "Gong",       Model = DrumModel.Cymbal,       Note = 51, Freq = 180, Decay = 4.5, Tone = 0.25, Body = 0.9, Noise = 0.2, Drive = 0.15, Warmth = 0.5, Room = 0.45, RoomSize = 0.9, RoomDecay = 0.8, Width = 1.0, Drift = 0.3, Length = 5.5, PeakDb = -5 },
        },
    };

    // ======================================================================
    // 21. Shuffle — UK garage and 2-step: a clicky kick, a crisp snare and
    //     clap, bright tight hats, a sub hit for the bassline, and swing set
    //     where the genre sits.
    // ======================================================================
    private static KitDefinition Shuffle() => new()
    {
        Id = "shuffle",
        Name = "Shuffle",
        Blurb = "UK garage & 2-step · crisp snare, bright hats, sub, heavy swing",
        FxSpace = 1.0,
        FxDrive = 1.0,
        Swing = 0.28f,
        Humanize = 0.05f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickPunch,  Note = 36, Freq = 50,  Decay = 0.3, PitchAmt = 2.8, PitchDecay = 0.011, Tone = 0.6, Body = 0.45, Click = 0.6, Drive = 0.3, Warmth = 0.45, Length = 0.7, PeakDb = -1.5 },
            new() { Name = "Rim",        Model = DrumModel.Rim,        Note = 37, Freq = 700, Decay = 0.03, Tone = 0.75, Body = 0.55, Drive = 0.3, Warmth = 0.4, HpHz = 400, Room = 0.2, RoomSize = 0.4, Width = 0.8, Drift = 0.3, Length = 0.4, PeakDb = -8 },
            new() { Name = "Snare",      Model = DrumModel.SnarePunch, Note = 38, Freq = 215, Decay = 0.2, Tone = 0.68, Body = 0.4, Noise = 0.95, NoiseDecay = 0.1, Drive = 0.3, Warmth = 0.4, HpHz = 160, Room = 0.2, RoomSize = 0.4, RoomDecay = 0.2, Width = 0.8, Drift = 0.2, Length = 0.6, PeakDb = -2.5 },
            new() { Name = "Clap",       Model = DrumModel.Clap,       Note = 39, Freq = 1350,Decay = 0.2, Tone = 0.68, Body = 0.5, Drive = 0.25, Warmth = 0.4, HpHz = 300, Room = 0.25, RoomSize = 0.45, Width = 0.9, Drift = 0.5, Length = 0.7, PeakDb = -3.5 },
            new() { Name = "Snare Skip", Model = DrumModel.SnarePunch, Note = 40, Freq = 260, Decay = 0.1, Tone = 0.78, Body = 0.3, Noise = 1.0, NoiseDecay = 0.05, Drive = 0.3, Warmth = 0.4, HpHz = 200, Drift = 0.2, Length = 0.3, PeakDb = -7 },
            new() { Name = "Sub Bass",   Model = DrumModel.SubHit,     Note = 41, Freq = 49,  Decay = 1.2, PitchAmt = 0.3, PitchDecay = 0.02, Tone = 0.45, Drive = 0.35, Warmth = 0.5, Length = 1.8, PeakDb = -2 },
            new() { Name = "Closed Hat", Model = DrumModel.HatMetal,   Note = 42, Choke = 1, Freq = 400, Decay = 0.03, Tone = 0.62, Body = 0.65, Noise = 0.5, Drive = 0.2, Warmth = 0.25, HpHz = 5000, Drift = 0.3, Length = 0.2, PeakDb = -10 },
            new() { Name = "Shaker",     Model = DrumModel.Shaker,     Note = 43, Freq = 6000,Decay = 0.06, Tone = 0.6, Body = 0.85, Click = 0.6, Drive = 0.1, Warmth = 0.35, Pan = 0.3f, Drift = 0.6, Length = 0.25, PeakDb = -11 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatMetal,   Note = 44, Choke = 1, Freq = 400, Decay = 0.02, Tone = 0.58, Body = 0.6, Noise = 0.45, Drive = 0.18, Warmth = 0.25, HpHz = 5000, Drift = 0.3, Length = 0.15, PeakDb = -13 },
            new() { Name = "Tambourine", Model = DrumModel.Shaker,     Note = 45, Freq = 7200,Decay = 0.16, Tone = 0.7, Body = 0.9, Click = 0.8, Drive = 0.1, Warmth = 0.35, Pan = -0.3f, Drift = 0.6, Length = 0.45, PeakDb = -10 },
            new() { Name = "Open Hat",   Model = DrumModel.HatMetal,   Note = 46, Choke = 1, Freq = 400, Decay = 0.26, Tone = 0.65, Body = 0.65, Noise = 0.55, Drive = 0.2, Warmth = 0.25, HpHz = 4000, Drift = 0.3, Length = 0.7, PeakDb = -9 },
            new() { Name = "Clave",      Model = DrumModel.Block,      Note = 47, Freq = 2200,Decay = 0.045, Tone = 0.4, Body = 0.45, Drive = 0.15, Warmth = 0.4, Room = 0.15, Width = 0.8, Pan = -0.35f, Drift = 0.4, Length = 0.3, PeakDb = -9 },
            new() { Name = "Chime",      Model = DrumModel.PercMetal,  Note = 48, Freq = 620, Decay = 0.25, Tone = 0.55, Body = 0.5, Drive = 0.2, Warmth = 0.35, HpHz = 300, Room = 0.25, RoomSize = 0.5, Width = 0.9, Pan = 0.3f, Drift = 0.3, Length = 0.7, PeakDb = -9 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,     Note = 49, Freq = 340, Decay = 1.8, Tone = 0.55, Body = 0.55, Noise = 0.55, Drive = 0.15, Warmth = 0.3, Drift = 0.4, Length = 2.4, PeakDb = -8 },
            new() { Name = "Finger Snap",Model = DrumModel.Clap,       Note = 50, Freq = 1800,Decay = 0.06, Tone = 0.8, Body = 0.2, Drive = 0.2, Warmth = 0.35, HpHz = 600, Room = 0.2, RoomSize = 0.4, Width = 0.8, Drift = 0.4, Length = 0.35, PeakDb = -7 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,     Note = 51, Freq = 460, Decay = 0.8, Tone = 0.8, Body = 0.4, Noise = 0.25, Drive = 0.1, Warmth = 0.3, Drift = 0.4, Length = 1.2, PeakDb = -11 },
        },
    };

    // ======================================================================
    // 22. Mirrorball — the 70s disco kit: dry and damped (a towel in the
    //     kick), a sizzling open hat, tambourine and conga on the side, and
    //     three syndrums for the fills.
    // ======================================================================
    private static KitDefinition Mirrorball() => new()
    {
        Id = "mirrorball",
        Name = "Mirrorball",
        Blurb = "70s disco · damped dry kit, sizzling open hat, syndrums",
        FxSpace = 1.0,
        FxDrive = 0.8,
        Humanize = 0.1f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAcoustic, Note = 36, Freq = 52,  Decay = 0.3, PitchAmt = 0.7, PitchDecay = 0.018, Tone = 0.5, Body = 0.5, Click = 0.55, Drive = 0.2, Warmth = 0.6, LpHz = 9000, Room = 0.1, RoomSize = 0.25, RoomDamp = 0.8, Width = 0.4, Drift = 0.2, Length = 0.7, PeakDb = -1.5 },
            new() { Name = "Side Stick", Model = DrumModel.Rim,          Note = 37, Freq = 500, Decay = 0.045, Tone = 0.5, Body = 0.5, Drive = 0.15, Warmth = 0.55, HpHz = 240, Room = 0.12, RoomSize = 0.25, Width = 0.5, Drift = 0.3, Length = 0.3, PeakDb = -9 },
            new() { Name = "Snare",      Model = DrumModel.SnareAcoustic,Note = 38, Freq = 205, Decay = 0.28, Tone = 0.55, Body = 0.5, Noise = 0.85, NoiseDecay = 0.14, Click = 0.45, Drive = 0.2, Warmth = 0.55, LpHz = 11000, Room = 0.14, RoomSize = 0.3, RoomDamp = 0.7, Width = 0.6, Drift = 0.25, Length = 0.7, PeakDb = -2.5 },
            new() { Name = "Clap",       Model = DrumModel.Clap,         Note = 39, Freq = 1080,Decay = 0.2, Tone = 0.5, Body = 0.5, Drive = 0.15, Warmth = 0.5, Room = 0.2, RoomSize = 0.35, Width = 0.8, Drift = 0.5, Length = 0.7, PeakDb = -4 },
            new() { Name = "Snare Tight",Model = DrumModel.SnareAcoustic,Note = 40, Freq = 240, Decay = 0.16, Tone = 0.62, Body = 0.4, Noise = 0.9, NoiseDecay = 0.08, Click = 0.55, Drive = 0.2, Warmth = 0.5, LpHz = 11000, Room = 0.12, RoomSize = 0.3, Width = 0.6, Drift = 0.25, Length = 0.45, PeakDb = -4 },
            new() { Name = "Syndrum Low",Model = DrumModel.Tom,          Note = 41, Freq = 92,  Decay = 0.5, PitchAmt = 1.4, PitchDecay = 0.12, Tone = 0.35, Body = 0.3, Noise = 0.1, NoiseDecay = 0.03, Drive = 0.2, Warmth = 0.5, Room = 0.15, RoomSize = 0.35, Width = 0.7, Pan = -0.3f, Drift = 0.3, Length = 1.0, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 1350, Decay = 0.045, Tone = 0.68, Body = 0.55, Click = 0.4, Drive = 0.1, Warmth = 0.4, Drift = 0.4, Length = 0.3, PeakDb = -10 },
            new() { Name = "Syndrum Mid",Model = DrumModel.Tom,          Note = 43, Freq = 130, Decay = 0.44, PitchAmt = 1.4, PitchDecay = 0.11, Tone = 0.35, Body = 0.3, Noise = 0.1, NoiseDecay = 0.03, Drive = 0.2, Warmth = 0.5, Room = 0.15, RoomSize = 0.35, Width = 0.7, Drift = 0.3, Length = 0.9, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 1300, Decay = 0.03, Tone = 0.62, Body = 0.5, Click = 0.35, Drive = 0.1, Warmth = 0.4, Drift = 0.4, Length = 0.2, PeakDb = -13 },
            new() { Name = "Syndrum Hi", Model = DrumModel.Tom,          Note = 45, Freq = 180, Decay = 0.38, PitchAmt = 1.4, PitchDecay = 0.1, Tone = 0.35, Body = 0.3, Noise = 0.1, NoiseDecay = 0.03, Drive = 0.2, Warmth = 0.5, Room = 0.15, RoomSize = 0.35, Width = 0.7, Pan = 0.3f, Drift = 0.3, Length = 0.8, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 1350, Decay = 0.5, Tone = 0.72, Body = 0.55, Click = 0.35, Drive = 0.1, Warmth = 0.4, Drift = 0.4, Length = 1.3, PeakDb = -9 },
            new() { Name = "Tambourine", Model = DrumModel.Shaker,       Note = 47, Freq = 7500,Decay = 0.2, Tone = 0.72, Body = 0.9, Click = 0.85, Drive = 0.1, Warmth = 0.35, Room = 0.12, Width = 0.8, Pan = 0.3f, Drift = 0.6, Length = 0.5, PeakDb = -10 },
            new() { Name = "Conga",      Model = DrumModel.Conga,        Note = 48, Freq = 240, Decay = 0.3, Tone = 0.4, Body = 0.45, Click = 0.35, Drive = 0.15, Warmth = 0.55, Room = 0.12, Width = 0.7, Pan = -0.3f, Drift = 0.35, Length = 0.7, PeakDb = -6 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,       Note = 49, Freq = 300, Decay = 2.4, Tone = 0.5, Body = 0.55, Noise = 0.55, Drive = 0.08, Warmth = 0.35, Room = 0.12, RoomSize = 0.35, Width = 0.8, Drift = 0.4, Length = 3.0, PeakDb = -8 },
            new() { Name = "Cowbell",    Model = DrumModel.Cowbell,      Note = 50, Freq = 560, Decay = 0.26, Tone = 0.5, Body = 0.5, Click = 0.35, Drive = 0.2, Warmth = 0.45, Room = 0.1, Width = 0.6, Drift = 0.35, Length = 0.6, PeakDb = -9 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,       Note = 51, Freq = 420, Decay = 1.4, Tone = 0.8, Body = 0.45, Noise = 0.3, Drive = 0.08, Warmth = 0.35, Room = 0.1, RoomSize = 0.35, Width = 0.7, Drift = 0.4, Length = 2.0, PeakDb = -10 },
        },
    };

    // ======================================================================
    // 23. Pit — the orchestra's percussion section in a hall: gran cassa,
    //     three timpani (D, G, C), concert snare, slapstick, castanets,
    //     glockenspiel, tubular bell and clash cymbals. The open and the
    //     muted triangle share a choke group, so a mute stops the ring.
    // ======================================================================
    private static KitDefinition Pit() => new()
    {
        Id = "pit",
        Name = "Pit",
        Blurb = "orchestral percussion · timpani, gran cassa, triangle, clash cymbals",
        FxSpace = 0.5,
        FxDrive = 0.4,
        Humanize = 0.15f,
        Pads = new List<KitPad>
        {
            new() { Name = "Gran Cassa",   Model = DrumModel.KickAcoustic, Note = 36, Freq = 44,  Decay = 1.4, PitchAmt = 0.2, PitchDecay = 0.05, Tone = 0.2, Body = 0.9, Click = 0.15, Drive = 0.05, Warmth = 0.6, LpHz = 5000, Room = 0.45, RoomSize = 0.85, RoomDecay = 0.7, RoomDamp = 0.5, Width = 0.9, Drift = 0.2, Length = 3.5, PeakDb = -1.5 },
            new() { Name = "Rimshot",      Model = DrumModel.Rim,          Note = 37, Freq = 520, Decay = 0.05, Tone = 0.55, Body = 0.55, Drive = 0.05, Warmth = 0.5, HpHz = 220, Room = 0.4, RoomSize = 0.85, RoomDecay = 0.6, Width = 0.9, Drift = 0.3, Length = 1.8, PeakDb = -8 },
            new() { Name = "Concert Snare",Model = DrumModel.SnareAcoustic,Note = 38, Freq = 220, Decay = 0.3, Tone = 0.72, Body = 0.5, Noise = 0.95, NoiseDecay = 0.2, Click = 0.4, Drive = 0.05, Warmth = 0.45, Room = 0.42, RoomSize = 0.85, RoomDecay = 0.65, RoomDamp = 0.45, Width = 0.9, Drift = 0.3, Length = 2.2, PeakDb = -3 },
            new() { Name = "Slapstick",    Model = DrumModel.Clap,         Note = 39, Freq = 1600,Decay = 0.05, Tone = 0.8, Body = 0.2, Drive = 0.05, Warmth = 0.4, Room = 0.45, RoomSize = 0.85, RoomDecay = 0.6, Width = 0.9, Drift = 0.3, Length = 1.5, PeakDb = -5 },
            new() { Name = "Snare Soft",   Model = DrumModel.SnareAcoustic,Note = 40, Freq = 210, Decay = 0.25, Tone = 0.5, Body = 0.45, Noise = 0.9, NoiseDecay = 0.18, Click = 0.15, Drive = 0.05, Warmth = 0.5, Room = 0.42, RoomSize = 0.85, RoomDecay = 0.65, Width = 0.9, Drift = 0.35, Length = 2.0, PeakDb = -7 },
            new() { Name = "Timpani D",    Model = DrumModel.Tom,          Note = 41, Freq = 73,  Decay = 2.2, PitchAmt = 0.05, PitchDecay = 0.05, Tone = 0.25, Body = 0.85, Noise = 0.15, NoiseDecay = 0.05, Drive = 0.05, Warmth = 0.55, Room = 0.45, RoomSize = 0.85, RoomDecay = 0.7, Width = 0.9, Pan = -0.3f, Drift = 0.1, Length = 4.0, PeakDb = -2 },
            new() { Name = "Triangle Mute",Model = DrumModel.PercMetal,    Note = 42, Choke = 1, Freq = 2100, Decay = 0.12, Tone = 0.8, Body = 0.55, Drive = 0.05, Warmth = 0.3, Room = 0.35, RoomSize = 0.85, RoomDecay = 0.5, Width = 0.9, Pan = 0.4f, Drift = 0.4, Length = 1.0, PeakDb = -12 },
            new() { Name = "Timpani G",    Model = DrumModel.Tom,          Note = 43, Freq = 98,  Decay = 2.0, PitchAmt = 0.05, PitchDecay = 0.05, Tone = 0.27, Body = 0.85, Noise = 0.15, NoiseDecay = 0.05, Drive = 0.05, Warmth = 0.55, Room = 0.45, RoomSize = 0.85, RoomDecay = 0.7, Width = 0.9, Drift = 0.1, Length = 3.8, PeakDb = -2 },
            new() { Name = "Castanets",    Model = DrumModel.Block,        Note = 44, Freq = 2900,Decay = 0.025, Tone = 0.5, Body = 0.4, Drive = 0.05, Warmth = 0.4, Room = 0.3, RoomSize = 0.8, RoomDecay = 0.5, Width = 0.9, Pan = 0.3f, Drift = 0.5, Length = 0.8, PeakDb = -9 },
            new() { Name = "Timpani C",    Model = DrumModel.Tom,          Note = 45, Freq = 131, Decay = 1.8, PitchAmt = 0.05, PitchDecay = 0.045, Tone = 0.3, Body = 0.85, Noise = 0.15, NoiseDecay = 0.05, Drive = 0.05, Warmth = 0.55, Room = 0.45, RoomSize = 0.85, RoomDecay = 0.7, Width = 0.9, Pan = 0.3f, Drift = 0.1, Length = 3.5, PeakDb = -2 },
            new() { Name = "Triangle",     Model = DrumModel.PercMetal,    Note = 46, Choke = 1, Freq = 2100, Decay = 2.0, Tone = 0.8, Body = 0.55, Drive = 0.05, Warmth = 0.3, Room = 0.35, RoomSize = 0.85, RoomDecay = 0.6, Width = 0.9, Pan = 0.4f, Drift = 0.4, Length = 3.0, PeakDb = -11 },
            new() { Name = "Tambourine",   Model = DrumModel.Shaker,       Note = 47, Freq = 7200,Decay = 0.22, Tone = 0.7, Body = 0.9, Click = 0.8, Drive = 0.05, Warmth = 0.35, Room = 0.35, RoomSize = 0.85, RoomDecay = 0.5, Width = 0.9, Pan = -0.3f, Drift = 0.6, Length = 1.2, PeakDb = -10 },
            new() { Name = "Glock",        Model = DrumModel.PercMetal,    Note = 48, Freq = 1570,Decay = 1.4, Tone = 0.5, Body = 0.45, Drive = 0.05, Warmth = 0.3, HpHz = 400, Room = 0.35, RoomSize = 0.85, RoomDecay = 0.6, Width = 0.9, Pan = 0.2f, Drift = 0.2, Length = 2.4, PeakDb = -10 },
            new() { Name = "Clash Cymbals",Model = DrumModel.Cymbal,       Note = 49, Freq = 280, Decay = 3.5, Tone = 0.45, Body = 0.65, Noise = 0.6, Drive = 0.05, Warmth = 0.35, Room = 0.45, RoomSize = 0.85, RoomDecay = 0.7, Width = 1.0, Drift = 0.4, Length = 4.5, PeakDb = -6 },
            new() { Name = "Tubular Bell", Model = DrumModel.PercMetal,    Note = 50, Freq = 523, Decay = 3.0, Tone = 0.35, Body = 0.8, Drive = 0.05, Warmth = 0.4, HpHz = 150, Room = 0.4, RoomSize = 0.85, RoomDecay = 0.7, Width = 0.9, Pan = -0.2f, Drift = 0.2, Length = 4.5, PeakDb = -8 },
            new() { Name = "Suspended",    Model = DrumModel.Cymbal,       Note = 51, Freq = 360, Decay = 3.0, Tone = 0.7, Body = 0.5, Noise = 0.5, Drive = 0.05, Warmth = 0.35, Room = 0.4, RoomSize = 0.85, RoomDecay = 0.7, Width = 0.95, Drift = 0.4, Length = 4.0, PeakDb = -9 },
        },
    };

    // ======================================================================
    // 24. Hyper — hyperpop and festival EDM: everything clipped and bright.
    //     A kick that is mostly attack, a distorted 808, stacked clap, glitch
    //     and dive effects, and a crushed snare for the fills.
    // ======================================================================
    private static KitDefinition Hyper() => new()
    {
        Id = "hyper",
        Name = "Hyper",
        Blurb = "hyperpop & EDM · clipped kick, distorted 808, glitch and dive",
        FxSpace = 1.1,
        FxDrive = 1.3,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickPunch,  Note = 36, Freq = 52,  Decay = 0.35, PitchAmt = 3.5, PitchDecay = 0.01, Tone = 0.75, Body = 0.55, Click = 0.8, Drive = 0.8, Warmth = 0.3, HpHz = 30, Length = 0.7, PeakDb = -1 },
            new() { Name = "Zip",        Model = DrumModel.Zap,        Note = 37, Freq = 300, Decay = 0.06, PitchAmt = 6, PitchDecay = 0.008, Tone = 0.4, Body = 0.5, Drive = 0.6, Warmth = 0.2, HpHz = 300, Length = 0.2, PeakDb = -7 },
            new() { Name = "Snare",      Model = DrumModel.SnarePunch, Note = 38, Freq = 225, Decay = 0.22, Tone = 0.8, Body = 0.45, Noise = 1.0, NoiseDecay = 0.12, Drive = 0.75, Warmth = 0.3, HpHz = 180, Room = 0.25, RoomSize = 0.5, RoomDecay = 0.3, Width = 1.0, Length = 0.8, PeakDb = -2 },
            new() { Name = "Clap Stack", Model = DrumModel.Clap,       Note = 39, Freq = 1500,Decay = 0.25, Tone = 0.8, Body = 0.6, Drive = 0.7, Warmth = 0.3, HpHz = 400, Room = 0.35, RoomSize = 0.6, RoomDecay = 0.4, Width = 1.0, Drift = 0.5, Length = 1.1, PeakDb = -2.5 },
            new() { Name = "Snare Up",   Model = DrumModel.SnarePunch, Note = 40, Freq = 320, Decay = 0.16, Tone = 0.85, Body = 0.4, Noise = 1.0, NoiseDecay = 0.08, Drive = 0.7, Warmth = 0.3, HpHz = 220, Room = 0.2, RoomSize = 0.5, Width = 1.0, Length = 0.6, PeakDb = -3.5 },
            new() { Name = "808 Distort",Model = DrumModel.SubHit,     Note = 41, Freq = 44,  Decay = 2.0, PitchAmt = 0.8, PitchDecay = 0.03, Tone = 0.75, Drive = 0.8, Warmth = 0.4, Length = 2.8, PeakDb = -1.5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatMetal,   Note = 42, Choke = 1, Freq = 520, Decay = 0.025, Tone = 0.7, Body = 0.7, Noise = 0.6, Drive = 0.5, Warmth = 0.15, HpHz = 6000, Length = 0.15, PeakDb = -10 },
            new() { Name = "Glitch",     Model = DrumModel.Zap,        Note = 43, Freq = 700, Decay = 0.03, PitchAmt = 2, PitchDecay = 0.004, Tone = 0.8, Body = 0.3, Drive = 0.6, Warmth = 0.2, Bits = 6, CrushHz = 8000, Length = 0.1, PeakDb = -8 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatMetal,   Note = 44, Choke = 1, Freq = 520, Decay = 0.015, Tone = 0.65, Body = 0.65, Noise = 0.55, Drive = 0.45, Warmth = 0.15, HpHz = 6000, Length = 0.1, PeakDb = -13 },
            new() { Name = "Dive",       Model = DrumModel.Zap,        Note = 45, Freq = 80,  Decay = 0.6, PitchAmt = 9, PitchDecay = 0.2, Tone = 0.6, Body = 0.7, Drive = 0.6, Warmth = 0.3, Room = 0.3, RoomSize = 0.6, Width = 1.0, Length = 1.2, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatMetal,   Note = 46, Choke = 1, Freq = 520, Decay = 0.3, Tone = 0.72, Body = 0.7, Noise = 0.65, Drive = 0.5, Warmth = 0.15, HpHz = 5000, Length = 0.8, PeakDb = -9 },
            new() { Name = "Star Bell",  Model = DrumModel.PercMetal,  Note = 47, Freq = 1040,Decay = 0.4, Tone = 0.55, Body = 0.5, Drive = 0.3, Warmth = 0.2, HpHz = 400, Room = 0.3, RoomSize = 0.6, Width = 1.0, Length = 0.9, PeakDb = -8 },
            new() { Name = "Crush Snare",Model = DrumModel.SnarePunch, Note = 48, Freq = 280, Decay = 0.16, Tone = 0.8, Body = 0.4, Noise = 1.0, NoiseDecay = 0.08, Drive = 0.7, Warmth = 0.2, Bits = 6, CrushHz = 12000, Length = 0.4, PeakDb = -4 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,     Note = 49, Freq = 360, Decay = 2.0, Tone = 0.65, Body = 0.5, Noise = 0.7, Drive = 0.4, Warmth = 0.2, HpHz = 800, Room = 0.3, RoomSize = 0.6, Width = 1.0, Length = 2.6, PeakDb = -7 },
            new() { Name = "Sub Kick",   Model = DrumModel.KickAnalog, Note = 50, Freq = 42,  Decay = 0.9, PitchAmt = 1.2, PitchDecay = 0.03, Tone = 0.15, Body = 0.9, Click = 0.1, Drive = 0.6, Warmth = 0.5, Length = 1.4, PeakDb = -2 },
            new() { Name = "Ride Bright",Model = DrumModel.Cymbal,     Note = 51, Freq = 480, Decay = 0.9, Tone = 0.85, Body = 0.4, Noise = 0.35, Drive = 0.3, Warmth = 0.2, HpHz = 1000, Length = 1.3, PeakDb = -11 },
        },
    };

    // ======================================================================
    // 25. Crate — 90s boom bap, dug out of a record crate: a thumping kick
    //     and a cracking snare through a 12-bit sampler with a little hiss,
    //     swung the way the MPC swung it, and a scratch for the turnaround.
    // ======================================================================
    private static KitDefinition Crate() => new()
    {
        Id = "crate",
        Name = "Crate",
        Blurb = "90s boom bap · 12-bit thump and crack, swung, with a scratch",
        FxSpace = 0.8,
        FxDrive = 1.1,
        FxTape = true,
        Swing = 0.2f,
        Humanize = 0.1f,
        Pads = new List<KitPad>
        {
            new() { Name = "Kick",       Model = DrumModel.KickAcoustic, Note = 36, Freq = 51,  Decay = 0.45, PitchAmt = 0.9, PitchDecay = 0.02, Tone = 0.5, Body = 0.6, Click = 0.6, Drive = 0.5, Warmth = 0.7, Bits = 12, CrushHz = 26040, HpHz = 35, LpHz = 9000, Hiss = 0.2, Drift = 0.2, Length = 0.9, PeakDb = -1.5 },
            new() { Name = "Rim",        Model = DrumModel.Rim,          Note = 37, Freq = 500, Decay = 0.045, Tone = 0.5, Body = 0.5, Drive = 0.4, Warmth = 0.6, Bits = 12, CrushHz = 26040, HpHz = 220, LpHz = 9000, Hiss = 0.2, Drift = 0.3, Length = 0.3, PeakDb = -9 },
            new() { Name = "Snare",      Model = DrumModel.SnareAcoustic,Note = 38, Freq = 200, Decay = 0.32, Tone = 0.62, Body = 0.6, Noise = 0.9, NoiseDecay = 0.16, Click = 0.65, Drive = 0.55, Warmth = 0.65, Bits = 12, CrushHz = 26040, LpHz = 10000, Hiss = 0.2, Room = 0.2, RoomSize = 0.35, RoomDecay = 0.18, RoomDamp = 0.6, Width = 0.6, Drift = 0.25, Length = 0.8, PeakDb = -2 },
            new() { Name = "Clap",       Model = DrumModel.Clap,         Note = 39, Freq = 1000,Decay = 0.2, Tone = 0.45, Body = 0.55, Drive = 0.45, Warmth = 0.6, Bits = 12, CrushHz = 26040, LpHz = 9000, Hiss = 0.2, Room = 0.15, Width = 0.6, Drift = 0.5, Length = 0.6, PeakDb = -4 },
            new() { Name = "Snare Crack",Model = DrumModel.SnarePunch,   Note = 40, Freq = 235, Decay = 0.2, Tone = 0.7, Body = 0.5, Noise = 1.0, NoiseDecay = 0.1, Drive = 0.6, Warmth = 0.6, Bits = 12, CrushHz = 26040, LpHz = 10000, Hiss = 0.2, Drift = 0.25, Length = 0.5, PeakDb = -3 },
            new() { Name = "Tom Low",    Model = DrumModel.Tom,          Note = 41, Freq = 86,  Decay = 0.5, PitchAmt = 0.3, PitchDecay = 0.05, Tone = 0.4, Body = 0.55, Noise = 0.3, NoiseDecay = 0.06, Drive = 0.45, Warmth = 0.65, Bits = 12, CrushHz = 26040, LpHz = 7000, Hiss = 0.2, Drift = 0.3, Length = 1.1, PeakDb = -5 },
            new() { Name = "Closed Hat", Model = DrumModel.HatNoise,     Note = 42, Choke = 1, Freq = 1100, Decay = 0.045, Tone = 0.55, Body = 0.5, Click = 0.45, Drive = 0.4, Warmth = 0.6, Bits = 12, CrushHz = 26040, LpHz = 10000, Hiss = 0.2, Drift = 0.5, Length = 0.25, PeakDb = -10 },
            new() { Name = "Tom Mid",    Model = DrumModel.Tom,          Note = 43, Freq = 114, Decay = 0.44, PitchAmt = 0.3, PitchDecay = 0.045, Tone = 0.42, Body = 0.55, Noise = 0.3, NoiseDecay = 0.06, Drive = 0.45, Warmth = 0.65, Bits = 12, CrushHz = 26040, LpHz = 7500, Hiss = 0.2, Drift = 0.3, Length = 1.0, PeakDb = -5 },
            new() { Name = "Pedal Hat",  Model = DrumModel.HatNoise,     Note = 44, Choke = 1, Freq = 1050, Decay = 0.03, Tone = 0.5, Body = 0.45, Click = 0.4, Drive = 0.4, Warmth = 0.6, Bits = 12, CrushHz = 26040, LpHz = 9500, Hiss = 0.2, Drift = 0.5, Length = 0.2, PeakDb = -13 },
            new() { Name = "Tom Hi",     Model = DrumModel.Tom,          Note = 45, Freq = 150, Decay = 0.38, PitchAmt = 0.3, PitchDecay = 0.04, Tone = 0.44, Body = 0.55, Noise = 0.3, NoiseDecay = 0.06, Drive = 0.45, Warmth = 0.65, Bits = 12, CrushHz = 26040, LpHz = 8000, Hiss = 0.2, Drift = 0.3, Length = 0.9, PeakDb = -5 },
            new() { Name = "Open Hat",   Model = DrumModel.HatNoise,     Note = 46, Choke = 1, Freq = 1100, Decay = 0.3, Tone = 0.58, Body = 0.5, Click = 0.4, Drive = 0.4, Warmth = 0.6, Bits = 12, CrushHz = 26040, LpHz = 10000, Hiss = 0.2, Drift = 0.5, Length = 0.8, PeakDb = -9 },
            new() { Name = "Shaker",     Model = DrumModel.Shaker,       Note = 47, Freq = 5000,Decay = 0.08, Tone = 0.45, Body = 0.8, Click = 0.5, Drive = 0.3, Warmth = 0.55, Bits = 12, CrushHz = 26040, LpHz = 9000, Hiss = 0.2, Drift = 0.6, Length = 0.3, PeakDb = -12 },
            new() { Name = "Scratch",    Model = DrumModel.Zap,          Note = 48, Freq = 180, Decay = 0.25, PitchAmt = 2, PitchDecay = 0.12, Tone = 0.3, Body = 0.5, Drive = 0.4, Warmth = 0.55, Bits = 12, CrushHz = 26040, LpHz = 6000, Hiss = 0.25, Drift = 0.4, Length = 0.4, PeakDb = -8 },
            new() { Name = "Crash",      Model = DrumModel.Cymbal,       Note = 49, Freq = 280, Decay = 1.9, Tone = 0.45, Body = 0.55, Noise = 0.5, Drive = 0.35, Warmth = 0.6, Bits = 12, CrushHz = 26040, LpHz = 9000, Hiss = 0.2, Drift = 0.4, Length = 2.4, PeakDb = -8 },
            new() { Name = "Cowbell",    Model = DrumModel.Cowbell,      Note = 50, Freq = 540, Decay = 0.26, Tone = 0.45, Body = 0.5, Click = 0.3, Drive = 0.35, Warmth = 0.55, Bits = 12, CrushHz = 26040, LpHz = 9000, Hiss = 0.2, Drift = 0.35, Length = 0.6, PeakDb = -9 },
            new() { Name = "Ride",       Model = DrumModel.Cymbal,       Note = 51, Freq = 390, Decay = 1.0, Tone = 0.72, Body = 0.4, Noise = 0.3, Drive = 0.3, Warmth = 0.55, Bits = 12, CrushHz = 26040, LpHz = 9500, Hiss = 0.2, Drift = 0.4, Length = 1.5, PeakDb = -10 },
        },
    };
}
