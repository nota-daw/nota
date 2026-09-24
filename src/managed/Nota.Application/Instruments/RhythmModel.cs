// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Rhythm (built-in instrument kind 12) — the engine's own value maps, in one place for
// the editor card and the MCP server: the kit (names, engines, MIDI map), param ids, the
// action ids of the pattern channel, the state-blob layout (params, then the four banks of
// steps), the scope telemetry, what a normalized knob means in seconds / hertz / semitones,
// a one-line summary and a guide to every parameter. Mirrors RhythmMachine.h.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nota.Application;

public static class RhythmModel
{
    public const int Kind = 12;
    public const int Voices = 8, Steps = 16, Banks = 4;

    // RhythmMachine::Act.
    public const int A_ToggleStep = 0, A_SetVel = 1, A_ToggleAccent = 2, A_SelectBank = 3, A_ClearBank = 4,
        A_SelectVoice = 5, A_SetSource = 6, A_CopyBank = 7, A_Audition = 8, A_ClearVoice = 9;

    // RhythmMachine::Scope.
    public const int S_Step = 0, S_Bank = 1, S_Rev = 2, S_Active = 3, S_Glue = 4, S_Flash0 = 5, S_Pos0 = S_Flash0 + Voices,
        ScopeLength = S_Pos0 + Voices;

    // State magics: RTH1 carries the first 60 params, RTH2 all of them.
    private const uint MagicV1 = 0x31485452, MagicV2 = 0x32485452;
    public const int LegacyParams = Voices * 7 + 4;

    public static readonly string[] VoiceNames = { "Kick", "Snare", "Clap", "Rim", "Closed Hat", "Open Hat", "Tom", "Perc" };
    public static readonly string[] ShortNames = { "KICK", "SNR", "CLAP", "RIM", "CH", "OH", "TOM", "PERC" };
    /// <summary>The synth engine each voice runs (lower-case, as the status line prints it).</summary>
    public static readonly string[] Engines = { "analog", "noise", "clap", "rim", "metal", "metal", "analog", "ring" };
    /// <summary>The MIDI note that plays each voice live (GM drum map).</summary>
    public static readonly int[] MidiNotes = { 36, 38, 39, 37, 42, 46, 45, 41 };
    public static readonly string[] BankNames = { "A", "B", "C", "D" };

    /// <summary>A step below this velocity (and not accented) is a quiet step.</summary>
    public const float QuietBelow = 0.45f;
    public const float QuietVel = 0.3f, NormalVel = 0.7f;

    public static string Id(int voice, string p) => $"v{voice}_{p}";
    public static readonly string[] VoiceParams = { "tune", "decay", "punch", "tone", "drive", "level", "pan", "start", "length", "reverse" };
    public static readonly string[] GlobalParams = { "swing", "humanize", "accent", "volume", "glue" };

    public static double ExpMap(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0.0, 1.0));

    // ---- what a knob means ---------------------------------------------------------------

    /// <summary>The amp decay (to −60 dB) of a synth voice, in seconds.</summary>
    public static double DecaySeconds(int voice, float decay, float punch = 0.5f) => voice switch
    {
        0 or 6 => ExpMap(decay, 0.06, 1.2),
        1 => ExpMap(decay, 0.05, 0.5) * (0.4 + 0.6 * (1.0 - punch)),
        2 => ExpMap(decay, 0.05, 0.4),
        3 => ExpMap(decay, 0.02, 0.12),
        4 => ExpMap(decay, 0.02, 0.16),
        5 => ExpMap(decay, 0.08, 0.7),
        _ => ExpMap(decay, 0.05, 0.6),
    };
    /// <summary>A sample voice's amp decay, in seconds.</summary>
    public static double SampleDecaySeconds(float decay) => ExpMap(decay, 0.05, 2.0);
    /// <summary>The pitch drop of the Kick / Tom (to −60 dB), in seconds; 0 for voices without one.</summary>
    public static double PitchSeconds(int voice, float tune) => voice is 0 or 6 ? 0.03 + 0.05 * tune : 0;
    /// <summary>The voice's base frequency (or its noise colour), in Hz.</summary>
    public static double TuneHz(int voice, float tune) => voice switch
    {
        0 => ExpMap(tune, 30, 120),
        6 => ExpMap(tune, 80, 300),
        1 => ExpMap(tune, 140, 330),
        2 => ExpMap(tune, 700, 1800),
        3 => ExpMap(tune, 900, 2600),
        4 or 5 => ExpMap(tune, 320, 900),
        _ => ExpMap(tune, 200, 1200),
    };
    /// <summary>A sample voice's transpose, −12 … +12 semitones.</summary>
    public static double SampleSemis(float tune) => (tune - 0.5) * 24.0;
    public static float SampleSemisNorm(double st) => (float)Math.Clamp(0.5 + st / 24.0, 0, 1);
    /// <summary>The sample region's end, 0..1 of the file.</summary>
    public static double RegionEnd(float start, float length) => Math.Min(1.0, start + length);

    // ---- the state blob -------------------------------------------------------------------

    /// <summary>The pattern part of a Rhythm state blob (plus the bank / voice the editor shows).</summary>
    public sealed class Pattern
    {
        public int CurrentBank, SelectedVoice;
        public readonly bool[,,] On = new bool[Banks, Voices, Steps];
        public readonly float[,,] Vel = new float[Banks, Voices, Steps];
        public readonly bool[,,] Acc = new bool[Banks, Voices, Steps];

        public bool Quiet(int b, int v, int s) => On[b, v, s] && !Acc[b, v, s] && Vel[b, v, s] < QuietBelow;
        public int Count(int b, int v) { int n = 0; for (int s = 0; s < Steps; s++) if (On[b, v, s]) n++; return n; }
    }

    /// <summary>Read the blob: [magic][params][bank, voice, 2][on, vel, acc per bank/voice/step].
    /// <paramref name="paramCount"/> is the instrument's current count (RTH2); an RTH1 blob holds 60.</summary>
    public static Pattern Parse(byte[] st, int paramCount)
    {
        var p = new Pattern();
        if (st.Length < 4) return p;
        uint magic = BitConverter.ToUInt32(st, 0);
        int n = magic == MagicV1 ? LegacyParams : magic == MagicV2 ? paramCount : -1;
        if (n < 0) return p;
        int off = 4 + n * 4;
        if (off + 2 <= st.Length) { p.CurrentBank = Math.Min((int)st[off], Banks - 1); p.SelectedVoice = Math.Min((int)st[off + 1], Voices - 1); }
        off += 4;
        for (int b = 0; b < Banks; b++)
            for (int v = 0; v < Voices; v++)
                for (int s = 0; s < Steps; s++, off += 3)
                {
                    if (off + 3 > st.Length) return p;
                    p.On[b, v, s] = st[off] != 0; p.Vel[b, v, s] = st[off + 1] / 255f; p.Acc[b, v, s] = st[off + 2] != 0;
                }
        return p;
    }

    // ---- words ------------------------------------------------------------------------------

    private static string S(double v, string f) => v.ToString(f, CultureInfo.InvariantCulture);
    public static string Pct(double v) => S(v * 100, "0") + "\u2009%";
    public static string Ms(double s) => s < 0.9995 ? S(s * 1000, "0") + "\u2009ms" : S(s, "0.00") + "\u2009s";
    public static string Semis(double st) => S(Math.Round(st), "+0;\u22120;0") + "\u2009st";

    /// <summary>The steps of a voice as the status line lists them: "1 · 5 · 9 accent · 13 quiet".</summary>
    public static string StepList(Pattern p, int bank, int v)
    {
        var parts = new List<string>();
        for (int s = 0; s < Steps; s++)
        {
            if (!p.On[bank, v, s]) continue;
            string t = (s + 1).ToString(CultureInfo.InvariantCulture);
            if (p.Acc[bank, v, s]) t += " accent";
            else if (p.Quiet(bank, v, s)) t += " quiet";
            parts.Add(t);
        }
        return parts.Count == 0 ? "no steps" : "steps " + string.Join(" · ", parts);
    }

    /// <summary>One line about the selected voice and the groove.</summary>
    public static string Summary(Func<string, float> g, Pattern p, int voice, bool sample, string sampleName)
    {
        int v = Math.Clamp(voice, 0, Voices - 1);
        var sb = new StringBuilder(VoiceNames[v]);
        float F(string id) => g(Id(v, id));
        if (sample)
        {
            sb.Append(" · sample ").Append(sampleName.Length > 0 ? sampleName : "—")
              .Append(" · ").Append(Pct(F("start"))).Append(" → ").Append(Pct(RegionEnd(F("start"), F("length"))));
            if (F("reverse") >= 0.5f) sb.Append(" reversed");
            sb.Append(" · tune ").Append(Semis(SampleSemis(F("tune"))));
        }
        else
            sb.Append(" · synth ").Append(Engines[v]).Append(" · tune ").Append(Pct(F("tune")))
              .Append(" · decay ").Append(Pct(F("decay")));
        sb.Append(" · level ").Append(Pct(F("level")));
        if (g("swing") > 0.005f) sb.Append(" · swing ").Append(Pct(g("swing")));
        if (g("humanize") > 0.005f) sb.Append(" · human ").Append(Pct(g("humanize")));
        if (g("glue") > 0.005f) sb.Append(" · glue ").Append(Pct(g("glue")));
        sb.Append(" · ").Append(StepList(p, p.CurrentBank, v));
        return sb.ToString();
    }

    public const string Guide =
        "Voices 0..7: Kick, Snare, Clap, Rim, Closed Hat, Open Hat, Tom, Perc (MIDI 36, 38, 39, 37, 42, 46, 45, 41 play them live). "
        + "Per-voice param ids v{n}_{p}, all 0..1: tune (synth: pitch or noise colour; sample: .5 = original, ±12 st at 0/1), "
        + "decay (amp decay; Kick 60 ms..1.2 s, Snare 50..500 ms, Closed Hat 20..160 ms, Open Hat 80..700 ms; sample 50 ms..2 s), "
        + "punch (attack click / snap), tone (Snare body↔noise, Hats metal↔noise, sample low-pass), drive (saturation), "
        + "level, pan (.5 = centre), start / length (the sample region, fractions of the file; end = start + length), "
        + "reverse (≥ .5 plays the region backwards). Globals: swing (delays odd 16ths, 1 = half a step), humanize (velocity "
        + "jitter), accent (how much louder an accented step plays, up to 2×), glue (bus compressor: 0 off … 1 heavy), "
        + "volume (master). Steps: 16 per bar at 1/16 in four banks A–D; a step has a velocity 0..1 (below 0.45 it is a quiet "
        + "step) and an accent flag. Closed Hat chokes Open Hat.";
}
