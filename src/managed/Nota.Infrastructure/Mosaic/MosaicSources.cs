// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic's factory multisamples. Like Nota Grain's sources they ship as recipes, not
// audio: each instrument is a synthesis routine that MosaicSourceLibrary renders into the
// data folder the first time a preset needs it — one WAV per root × velocity layer (× round-
// robin step), plus release samples where the instrument has them — named the way sample
// libraries name theirs ("felt-piano_c4_f.wav", "_rr2", "_rel"), so Mosaic's own file-name
// mapper lays them out. They are real multisamples: a felt piano with soft and hard strikes and
// a damper thump per key, mallets and a nylon guitar with alternating strikes, a looping pad.

using System;
using System.Collections.Generic;
using Nota.Infrastructure.Kits;

namespace Nota.Infrastructure.Mosaic;

/// <summary>A factory multisample. <see cref="Rev"/> is bumped whenever its recipe changes, so the
/// rendered files are refreshed. Roots are MIDI notes; Layers are velocity tokens in dynamic order.</summary>
public sealed record MosaicSource(string Id, string Name, int[] Roots, string[] Layers, int RoundRobin, bool Releases, int Rev, string Blurb)
{
    /// <summary>Renders one sample: root, layer index (0 = softest), round-robin step (1-based),
    /// release; fills L / R (48 kHz). Returns the length used, in samples.</summary>
    internal Func<int, int, int, bool, Rng, (double[] L, double[] R)> Render { get; init; } = (_, _, _, _, _) => (new double[1], new double[1]);

    /// <summary>Loop the sustain (start, end in seconds) — a pad; null for decaying sounds.</summary>
    public (double Start, double End)? Loop { get; init; }

    public int Files => Roots.Length * Layers.Length * Math.Max(1, RoundRobin) + (Releases ? Roots.Length : 0);
}

public static class MosaicSources
{
    public const int Rate = 48000;
    private const double Tau = 2 * Math.PI;
    private static double Hz(double midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);
    private static int[] Steps(int from, int to, int step) { var l = new List<int>(); for (int n = from; n <= to; n += step) l.Add(n); return l.ToArray(); }

    private static IReadOnlyList<MosaicSource>? _all;
    public static IReadOnlyList<MosaicSource> All => _all ??= new List<MosaicSource>
    {
        new("felt-piano", "Felt Piano", Steps(24, 96, 4), new[] { "p", "f" }, 1, true, 1,
            "a felted upright: two strike strengths every major third, a damper thump per key") { Render = Piano },
        new("glass-mallets", "Glass Mallets", Steps(48, 96, 6), new[] { "p", "f" }, 2, false, 1,
            "a vibraphone-like bar, soft and hard mallets, two alternating strikes") { Render = Mallets },
        new("nylon", "Nylon Guitar", Steps(40, 79, 3), new[] { "p", "f" }, 2, false, 1,
            "a plucked nylon string with alternating up and down strokes") { Render = Nylon },
        new("brass-pluck", "Brass Pluck", Steps(36, 84, 6), new[] { "p", "f" }, 1, false, 1,
            "a short brassy stab that opens up when hit hard") { Render = Brass },
        new("warm-pad", "Warm Pad", Steps(36, 84, 12), new[] { "mf" }, 1, false, 1,
            "a detuned saw pad with a looping sustain") { Render = Pad, Loop = (1.2, 3.6) },
        new("music-box", "Music Box", Steps(60, 96, 4), new[] { "mf" }, 2, false, 1,
            "a music-box comb, two slightly different teeth per note") { Render = MusicBox },
    };

    public static MosaicSource? ById(string id)
    {
        foreach (var s in All) if (s.Id == id) return s;
        return null;
    }

    // ---- helpers --------------------------------------------------------------------------------
    private static (double[] L, double[] R) Buf(double seconds) { int n = Math.Max(1, (int)(seconds * Rate)); return (new double[n], new double[n]); }
    private static double Env(int i, double atk, double decay)
    {
        double t = i / (double)Rate;
        double a = atk <= 0 ? 1 : Math.Min(1, t / atk);
        return a * Math.Exp(-t / Math.Max(1e-4, decay));
    }
    // A one-pole low-pass over a buffer (in place).
    private static void Lp(double[] x, double hz)
    {
        double a = Math.Exp(-Tau * hz / Rate), y = 0;
        for (int i = 0; i < x.Length; i++) { y = (1 - a) * x[i] + a * y; x[i] = y; }
    }
    // A decaying sine added into l / r: a rotating phasor (no sin per sample) that stops once
    // it has died away — most of a piano's upper partials are gone in a fraction of the file.
    private static void Partial(double[] l, double[] r, double f, double amp, double decay, double atk, double gl, double gr, double phase, Func<int, double>? mod = null)
    {
        double w = Tau * f / Rate, c = Math.Cos(w), sn = Math.Sin(w);
        double x = Math.Cos(Tau * phase), y = Math.Sin(Tau * phase);
        double d = Math.Exp(-1.0 / (Math.Max(1e-4, decay) * Rate)), a = amp;
        int atkN = Math.Max(1, (int)(atk * Rate));
        for (int i = 0; i < l.Length; i++)
        {
            double v = y * a * (i < atkN ? i / (double)atkN : 1.0);
            if (mod is not null) { double m = mod(i); l[i] += v * gl * m; r[i] += v * gr * (2 - m); }
            else { l[i] += v * gl; r[i] += v * gr; }
            double nx = x * c - y * sn; y = x * sn + y * c; x = nx;
            a *= d;
            if ((i & 1023) == 0) { double norm = 1.0 / Math.Sqrt(x * x + y * y); x *= norm; y *= norm; if (a < 1e-5 * amp + 1e-9) break; }
        }
    }

    // A short fade at the end so nothing clicks.
    private static void Tail(double[] l, double[] r, double sec = 0.03)
    {
        int n = Math.Min(l.Length, (int)(sec * Rate));
        for (int i = 0; i < n; i++) { double g = i / (double)n; l[l.Length - 1 - i] *= g; r[r.Length - 1 - i] *= g; }
    }

    // ---- Felt Piano -------------------------------------------------------------------------------
    // Stretched partials of three slightly detuned strings, the upper ones decaying faster; a
    // felt hammer softens the top (less so when hit hard) and adds a short knock. The release
    // sample is the damper landing: a low thud and a little string buzz.
    private static (double[] L, double[] R) Piano(int note, int layer, int rr, bool release, Rng rng)
    {
        double f0 = Hz(note);
        if (release)
        {
            var (rl, rr2) = Buf(0.35);
            var n = new double[rl.Length];
            for (int i = 0; i < n.Length; i++) n[i] = rng.NextGauss() * Env(i, 0.002, 0.03);
            Lp(n, 900);
            for (int i = 0; i < rl.Length; i++)
            {
                double t = i / (double)Rate;
                double thud = Math.Sin(Tau * Math.Min(f0, 220) * 0.5 * t) * Env(i, 0.001, 0.05) * 0.5;
                double buzz = Math.Sin(Tau * f0 * t) * Env(i, 0.001, 0.08) * 0.15;
                double s = (thud + buzz + n[i] * 0.8) * 0.5;
                rl[i] = s; rr2[i] = s * 0.96;
            }
            Tail(rl, rr2);
            return (rl, rr2);
        }
        double hard = layer == 0 ? 0.25 : 0.9;
        double len = Math.Clamp(5.0 - (note - 24) * 0.045, 1.6, 5.0);
        var (l, r) = Buf(len);
        double B = 0.00025 * Math.Pow(2, (note - 60) / 18.0);           // inharmonicity
        double baseDecay = Math.Clamp(3.2 - (note - 24) * 0.03, 0.6, 3.2);
        int parts = Math.Clamp((int)(9000 / f0), 3, 24);
        double[] det = { -0.9, 0.0, 1.1 };                               // cents, three strings
        for (int k = 1; k <= parts; k++)
        {
            double fk = f0 * k * Math.Sqrt(1 + B * k * k);
            if (fk > 16000) break;
            double tilt = Math.Pow(k, -(1.55 - hard * 0.75));
            double strike = Math.Abs(Math.Sin(Math.PI * k / 7.3));       // hammer position comb
            double amp = tilt * (0.35 + 0.65 * strike);
            double dec = baseDecay / (1 + 0.22 * (k - 1) * (1.2 - hard * 0.4));
            for (int s = 0; s < 3; s++)
            {
                double pan = (s - 1) * 0.25;
                Partial(l, r, fk * Math.Pow(2, det[s] / 1200.0), amp, dec, 0.0015, 1 - pan, 1 + pan, rng.Next01());
            }
        }
        // The hammer knock.
        var knock = new double[Math.Min(l.Length, Rate / 25)];
        for (int i = 0; i < knock.Length; i++) knock[i] = rng.NextGauss() * Env(i, 0.0005, 0.006);
        Lp(knock, 1200 + hard * 2500);
        for (int i = 0; i < knock.Length; i++) { l[i] += knock[i] * (0.15 + hard * 0.35); r[i] += knock[i] * (0.15 + hard * 0.35); }
        double g = (layer == 0 ? 0.35 : 1.0) * 0.12;
        for (int i = 0; i < l.Length; i++) { l[i] *= g; r[i] *= g; }
        Tail(l, r);
        return (l, r);
    }

    // ---- Glass Mallets ----------------------------------------------------------------------------
    private static (double[] L, double[] R) Mallets(int note, int layer, int rr, bool release, Rng rng)
    {
        double f0 = Hz(note);
        var (l, r) = Buf(2.6);
        double hard = layer == 0 ? 0.2 : 1.0;
        (double Ratio, double Amp, double Dec)[] modes = { (1, 1, 1.6), (4.0, 0.35 * (0.3 + hard), 0.45), (10.0, 0.12 * hard, 0.12), (2.76, 0.05, 0.3) };
        double detune = rr == 2 ? 1.0015 : 1.0;
        foreach (var (ratio, amp, dec) in modes)
        {
            double f = f0 * ratio * detune;
            if (f > 18000) continue;
            Partial(l, r, f, amp, dec, 0.0008, 1, 1, rng.Next01(), i => 1 + 0.12 * Math.Sin(Tau * 5.2 * i / Rate));
        }
        double g = (layer == 0 ? 0.4 : 1.0) * 0.28;
        for (int i = 0; i < l.Length; i++) { l[i] *= g; r[i] *= g; }
        Tail(l, r);
        return (l, r);
    }

    // ---- Nylon Guitar (Karplus–Strong) ------------------------------------------------------------
    private static (double[] L, double[] R) Nylon(int note, int layer, int rr, bool release, Rng rng)
    {
        double f0 = Hz(note);
        double len = Math.Clamp(3.6 - (note - 40) * 0.04, 1.4, 3.6);
        var (l, r) = Buf(len);
        int period = Math.Max(2, (int)Math.Round(Rate / f0));
        var line = new double[period];
        double bright = layer == 0 ? 0.35 : 0.85;
        for (int i = 0; i < period; i++) line[i] = rng.Next() * (rr == 2 ? -1 : 1);
        var tmp = new double[period];
        for (int pass = 0; pass < (int)((1 - bright) * 6); pass++)   // soften the pluck
        {
            for (int i = 0; i < period; i++) tmp[i] = 0.5 * (line[i] + line[(i + 1) % period]);
            Array.Copy(tmp, line, period);
        }
        double loss = 0.4985 + 0.0013 * Math.Clamp((60 - note) / 30.0, 0, 1);
        int p = 0; double prev = 0;
        for (int i = 0; i < l.Length; i++)
        {
            double cur = line[p];
            double nxt = loss * (cur + line[(p + 1) % period]) * (0.996 + 0.004 * bright);
            line[p] = nxt;
            p = (p + 1) % period;
            double body = cur + 0.25 * (cur - prev); prev = cur;
            l[i] = body; r[i] = body * 0.92 + 0.08 * cur;
        }
        double g = (layer == 0 ? 0.45 : 1.0) * 0.5;
        for (int i = 0; i < l.Length; i++) { l[i] *= g; r[i] *= g; }
        Tail(l, r);
        return (l, r);
    }

    // ---- Brass Pluck --------------------------------------------------------------------------------
    private static (double[] L, double[] R) Brass(int note, int layer, int rr, bool release, Rng rng)
    {
        double f0 = Hz(note);
        var (l, r) = Buf(1.4);
        double hard = layer == 0 ? 0.3 : 1.0;
        double p1 = 0, p2 = 0, lp = 0, lp2 = 0;
        for (int i = 0; i < l.Length; i++)
        {
            double t = i / (double)Rate;
            double env = Math.Min(1, t / 0.012) * Math.Exp(-t / 0.5);
            double cut = 300 + (900 + 5200 * hard) * Math.Exp(-t / (0.08 + 0.12 * hard)) + 400 * env;
            double s1 = 2 * p1 - 1, s2 = 2 * p2 - 1;
            p1 += f0 / Rate; if (p1 >= 1) p1 -= 1;
            p2 += f0 * 1.003 / Rate; if (p2 >= 1) p2 -= 1;
            double a = Math.Exp(-Tau * cut / Rate);
            lp = (1 - a) * (s1 + s2) * 0.5 + a * lp;
            lp2 = (1 - a) * lp + a * lp2;
            double v = lp2 * env;
            l[i] = v; r[i] = v;
        }
        double g = (layer == 0 ? 0.45 : 1.0) * 0.5;
        for (int i = 0; i < l.Length; i++) { l[i] *= g; r[i] *= g; }
        Tail(l, r);
        return (l, r);
    }

    // ---- Warm Pad (loops 1.2 … 3.6 s) ----------------------------------------------------------------
    private static (double[] L, double[] R) Pad(int note, int layer, int rr, bool release, Rng rng)
    {
        double f0 = Hz(note);
        var (l, r) = Buf(4.0);
        double[] det = { -9, -3, 2, 8, 13 };
        var ph = new double[det.Length];
        for (int k = 0; k < ph.Length; k++) ph[k] = rng.Next01();
        double lpL = 0, lpR = 0, lpL2 = 0, lpR2 = 0;
        for (int i = 0; i < l.Length; i++)
        {
            double t = i / (double)Rate;
            double sl = 0, sr = 0;
            for (int k = 0; k < det.Length; k++)
            {
                double f = f0 * Math.Pow(2, det[k] / 1200.0);
                double s = 2 * ph[k] - 1;
                ph[k] += f / Rate; if (ph[k] >= 1) ph[k] -= 1;
                double pan = (k - 2) / 2.5;
                sl += s * (1 - pan * 0.5); sr += s * (1 + pan * 0.5);
            }
            double cut = 900 + 500 * Math.Sin(Tau * 0.25 * t);
            double a = Math.Exp(-Tau * cut / Rate);
            lpL = (1 - a) * sl + a * lpL; lpL2 = (1 - a) * lpL + a * lpL2;
            lpR = (1 - a) * sr + a * lpR; lpR2 = (1 - a) * lpR + a * lpR2;
            double env = Math.Min(1, t / 0.6);
            l[i] = lpL2 * env * 0.09; r[i] = lpR2 * env * 0.09;
        }
        Tail(l, r);
        return (l, r);
    }

    // ---- Music Box ----------------------------------------------------------------------------------
    private static (double[] L, double[] R) MusicBox(int note, int layer, int rr, bool release, Rng rng)
    {
        double f0 = Hz(note) * (rr == 2 ? 1.002 : 0.999);
        var (l, r) = Buf(2.2);
        (double Ratio, double Amp, double Dec)[] modes = { (1, 1, 1.1), (5.4, 0.25, 0.18), (11.2, 0.08, 0.05), (2.0, 0.1, 0.5) };
        foreach (var (ratio, amp, dec) in modes)
        {
            double f = f0 * ratio;
            if (f > 18000) continue;
            Partial(l, r, f, amp, dec, 0.0004, 1, rr == 2 ? 0.9 : 1.0, 0);
        }
        for (int i = 0; i < l.Length; i++) { l[i] *= 0.3; r[i] *= 0.3; }
        Tail(l, r);
        return (l, r);
    }
}
