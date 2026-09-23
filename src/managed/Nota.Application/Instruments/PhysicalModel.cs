// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Physical (built-in instrument kind 2) — the engine's own value maps, in one place
// for the editor card and the MCP server: what a normalized 0..1 parameter means in
// seconds / semitones / cents, how the engine's scope telemetry is laid out, a one-line
// summary of the patch and a guide to every parameter. Mirrors PhysicalSynth.h.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nota.Application;

public static class PhysicalModel
{
    public const int Kind = 2;
    public const int Voices = 8;
    public const int Modes = 16;

    // scopeRead layout: 4 head floats, then per bank 3 rows of Modes (ratio, amp, ring s).
    public const int ScopeHead = 4;
    public const int ScopeLength = ScopeHead + 2 * 3 * Modes;

    public static readonly string[] TypeNames = { "Beam", "Marimba", "String", "Membrane", "Plate", "Pipe" };
    public static readonly string[] NoiseTypes = { "LP", "BP", "HP" };

    public static double ExpMap(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0.0, 1.0));

    public static int TypeIndex(float v) => Math.Clamp((int)Math.Round(v * 5), 0, 5);
    public static int NoiseTypeIndex(float v) => Math.Clamp((int)Math.Round(v * 2), 0, 2);

    /// <summary>Resonator Decay → the fundamental's ring time (s, e-folding).</summary>
    public static double DecaySeconds(float v) => ExpMap(v, 0.04, 18.0);
    /// <summary>Resonator Tune → semitones (±24).</summary>
    public static double BankSemis(float v) => (v - 0.5) * 48.0;
    /// <summary>Resonator Ratio → the partial-spacing exponent (0.5 squeezed … 1 natural … 1.5 spread).</summary>
    public static double RatioExponent(float v) => 0.5 + v;
    /// <summary>Global Tune → semitones (±24).</summary>
    public static double TuneSemis(float v) => (v - 0.5) * 48.0;
    /// <summary>Global Fine → cents (±100).</summary>
    public static double FineCents(float v) => (v - 0.5) * 200.0;
    /// <summary>Note Off → how long a released note takes to die (s): 0 = rings on, 1 = damped at once.</summary>
    public static double NoteOffSeconds(float v) => ExpMap(1.0 - v, 0.006, 4.0);
    /// <summary>Mallet Stiffness → contact time (ms): soft = long and dark, hard = short and bright.</summary>
    public static double ContactMs(float v) => ExpMap(1.0 - v, 0.0004, 0.010) * 1000.0;
    public static double NoiseFreqHz(float v) => ExpMap(v, 60.0, 12000.0);
    public static double NoiseAttack(float v) => ExpMap(v, 0.001, 2.0);
    public static double NoiseDecay(float v) => ExpMap(v, 0.002, 4.0);
    public static double NoiseRelease(float v) => ExpMap(v, 0.002, 5.0);
    /// <summary>Noise Env → how far the envelope sweeps the noise filter (±3 octaves).</summary>
    public static double NoiseEnvOct(float v) => (v - 0.5) * 6.0;
    /// <summary>Res Mix → (Res 1 gain, Res 2 gain): equal-gain, both 1 in the middle.</summary>
    public static (double G1, double G2) MixGains(float v) => (Math.Min(1.0, 2.0 * (1.0 - v)), Math.Min(1.0, 2.0 * v));

    /// <summary>One bank's modes from the engine scope: frequency ratio (bank Tune included),
    /// struck amplitude and ring time in seconds. Empty when the scope is too short.</summary>
    public static (double Ratio, double Amp, double Tau)[] Bank(ReadOnlySpan<float> scope, int bank)
    {
        int at = ScopeHead + bank * 3 * Modes;
        if (scope.Length < at + 3 * Modes) return Array.Empty<(double, double, double)>();
        var r = new (double, double, double)[Modes];
        for (int m = 0; m < Modes; m++) r[m] = (scope[at + m], scope[at + Modes + m], scope[at + 2 * Modes + m]);
        return r;
    }

    /// <summary>How many of a bank's partials actually sound for a fundamental f0 at the
    /// sample rate: below Nyquist and at least 2 % of the loudest.</summary>
    public static int AudiblePartials((double Ratio, double Amp, double Tau)[] bank, double f0, double sampleRate)
    {
        double max = 1e-9;
        foreach (var p in bank) max = Math.Max(max, p.Amp);
        int n = 0;
        foreach (var p in bank) if (f0 * p.Ratio < sampleRate * 0.49 && p.Amp >= max * 0.02) n++;
        return n;
    }

    private static string S(double v, string f) => v.ToString(f, CultureInfo.InvariantCulture);
    private static string Secs(double s) => s < 1 ? S(s * 1000, "0") + " ms" : S(s, "0.00") + " s";
    private static string Pct(float v) => S(v * 100, "0") + " %";
    private static string Signed(double v, string f) => (v > 0 ? "+" : "") + S(v, f);

    /// <summary>A one-line summary of the patch, from a param-id getter.</summary>
    public static string Summary(Func<string, float> g)
    {
        var sb = new StringBuilder();
        sb.Append(g("mono") >= 0.5f ? "Mono" : "Poly " + Voices).Append(" · ");
        var ex = new List<string>();
        if (g("malletvol") > 0.001f) ex.Add($"mallet {Pct(g("malletvol"))} (stiff {Pct(g("malletstiff"))}, contact {S(ContactMs(g("malletstiff")), "0.0")} ms)");
        if (g("noisevol") > 0.001f) ex.Add($"noise {Pct(g("noisevol"))} {NoiseTypes[NoiseTypeIndex(g("noisetype"))]} {S(NoiseFreqHz(g("noisefreq")), "0")} Hz");
        sb.Append(ex.Count == 0 ? "no exciter (silent)" : string.Join(" + ", ex)).Append(" → ");
        string Bank(string p) => $"{TypeNames[TypeIndex(g(p + "type"))].ToLowerInvariant()} ring {Secs(DecaySeconds(g(p + "decay")))}"
            + (Math.Abs(BankSemis(g(p + "tune"))) >= 0.05 ? $" {Signed(BankSemis(g(p + "tune")), "0.0")} st" : "");
        sb.Append("res 1 ").Append(Bank("r1"));
        if (g("r2on") >= 0.5f)
        {
            bool serial = g("structure") < 0.5f;
            sb.Append(serial ? " → res 2 " : " + res 2 ").Append(Bank("r2"));
            var (g1, g2) = MixGains(g("resmix"));
            sb.Append($" (mix {S(g1 * 100, "0")}/{S(g2 * 100, "0")})");
        }
        sb.Append($" · tune {Signed(TuneSemis(g("tune")), "0")} st {Signed(FineCents(g("fine")), "0")} c");
        sb.Append($" · note off {Secs(NoteOffSeconds(g("noteoff")))} · vol {Pct(g("volume"))}");
        return sb.ToString();
    }

    /// <summary>What each 0..1 parameter value means, one line per id.</summary>
    public const string Guide =
        "All params are normalized 0..1 (set_instrument_param_by_id).\n"
        + "malletvol / malletnoise / malletcolor: level, strike noise, dark (0) … raw bright (1). malletstiff: soft 10 ms contact (0) … hard 0.4 ms (1), exp.\n"
        + "noisevol: noise-burst exciter level (0 = off). noisetype: LP 0 / BP 0.5 / HP 1. noisefreq: 60 Hz … 12 kHz, exp. noisereso: 0 … high Q. "
        + "noiseenv: envelope → filter, 0.5 = none, ±3 oct at the ends. noisea 1 ms … 2 s, noised 2 ms … 4 s, noiser 2 ms … 5 s (exp), noises sustain level.\n"
        + "r1type / r2type: Beam 0 / Marimba 0.2 / String 0.4 / Membrane 0.6 / Plate 0.8 / Pipe 1 — the partial series. "
        + "r*decay: fundamental ring 40 ms … 18 s, exp. r*material: how much faster high partials die (0 metal = all ring, 1 wood = highs damped). "
        + "r*bright: high-partial level tilt. r*inharm: stretches the series (bell-like). r*ratio: partial-spacing exponent 0.5 + v (0.5 = natural). "
        + "r*hit: strike position along the body (comb over the partials). r*tune: ±24 st, 0.5 = 0.\n"
        + "r2on: second resonator off (<0.5) / on. structure: 1→2 serial (<0.5, res 1 feeds res 2) / 1+2 parallel. "
        + "resmix: balance of res 1 against res 2, 0 = res 1 only, 0.5 = both full, 1 = res 2 only (in 1→2: res 1's own sound against the body it rings, res 2 +12 dB).\n"
        + "tune: ±24 st (0.5 = 0). fine: ±100 cents. noteoff: 0 = released notes ring on (4 s), 1 = damped at once (6 ms). "
        + "pan: 0 L … 0.5 C … 1 R. volume: output level. mono: 0 poly (8 voices) / 1 mono (a new strike chokes the sounding note).";
}
