// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The source samples Nota Grain's factory presets granulate. Like the drum kits they ship
// as recipes, not audio: each source is a small synthesis routine that GrainSourceLibrary
// renders to a WAV in the data folder the first time a preset (or its preview) needs it.
//
// A granular patch is mostly its source, so the set covers the ground a preset library
// needs: sustained tones to freeze (ensembles, choirs, strings, organ), struck and plucked
// bodies whose attack or ring can be picked out (piano tine, nylon, kalimba, marimba,
// bells), material that changes along the file for Scan and Key (a vowel morph, a harp
// run, a music-box tune, a drum break, an arpeggio) and noises (ocean, rain, radio).
//
// Every source is rendered at 48 kHz and levelled to the same loudness as the built-in
// pad (Level), so presets move between sources without jumps. A pitched source sits on
// its Root note: playing that key plays the recording at its own pitch.

using System;
using System.Collections.Generic;
using Nota.Infrastructure.Kits;

namespace Nota.Infrastructure.Grain;

/// <summary>A factory grain source. <see cref="Rev"/> is bumped whenever its recipe
/// changes, so the rendered file is refreshed.</summary>
public sealed record GrainSource(string Id, string Name, int Root, double Seconds, int Rev, string Blurb)
{
    internal Action<SourceBuffer> Render { get; init; } = _ => { };

    /// <summary>Level above the shared loudness, in dB (the basses: one note has to carry
    /// what a pad's chord does). The −1 dBFS ceiling still applies.</summary>
    public double GainDb { get; init; }
}

/// <summary>A stereo render target for one source.</summary>
internal sealed class SourceBuffer
{
    public const int Rate = 48000;
    public readonly double[] L, R;
    public readonly int N;
    public readonly Rng Rng;

    public SourceBuffer(double seconds, uint seed)
    {
        N = Math.Max(1, (int)(seconds * Rate));
        L = new double[N]; R = new double[N];
        Rng = new Rng(seed);
    }

    public static double T(int i) => i / (double)Rate;
    public int At(double seconds) => Math.Clamp((int)(seconds * Rate), 0, N);
}

public static class GrainSources
{
    private static IReadOnlyList<GrainSource>? _all;
    private static Dictionary<string, GrainSource>? _byId;

    /// <summary>Every shipped source, in catalog order.</summary>
    public static IReadOnlyList<GrainSource> All => _all ??= Build();

    public static GrainSource? ById(string id)
    {
        _byId ??= BuildIndex();
        return _byId.TryGetValue(id, out var s) ? s : null;
    }

    private static Dictionary<string, GrainSource> BuildIndex()
    {
        var d = new Dictionary<string, GrainSource>(StringComparer.Ordinal);
        foreach (var s in All) d[s.Id] = s;
        return d;
    }

    private const double Tau = 2 * Math.PI;
    private static double Hz(double midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);
    private static double Cents(double c) => Math.Pow(2, c / 1200.0);

    private static List<GrainSource> Build() => new()
    {
        // ---- sustained tones -----------------------------------------------------------
        new("saw-ensemble", "Saw Ensemble", 48, 6, 1, "seven detuned saws under a slowly breathing filter") { Render = SawEnsemble },
        new("glass-pad", "Glass Partials", 60, 6, 1, "shimmering sine partials that beat against each other") { Render = GlassPad },
        new("choir", "Choir Aah", 60, 5, 1, "a small ensemble singing 'aah'") { Render = Choir },
        new("vowels", "Vowel Morph", 48, 6, 1, "one voice moving a · e · i · o · u") { Render = Vowels },
        new("whisper", "Whisper", 60, 5, 1, "breath through moving vowel formants, unpitched") { Render = Whisper },
        new("strings", "Bowed Strings", 48, 6, 1, "a string section, bow noise and vibrato") { Render = Strings },
        new("flute", "Breath Flute", 72, 4, 1, "an airy flute tone with a late vibrato") { Render = Flute },
        new("brass", "Brass Swell", 48, 4, 1, "a brass section swelling open") { Render = Brass },
        new("organ", "Drawbar Organ", 48, 4, 1, "drawbars through a rotating speaker") { Render = Organ },
        new("tape-chord", "Tape Chord", 48, 6, 1, "a soft open chord with tape wow, flutter and hiss") { Render = TapeChord },
        // ---- bass -----------------------------------------------------------------------
        new("sub", "Sub Sine", 36, 3, 1, "a round, slightly driven sub") { Render = Sub, GainDb = 8 },
        new("saw-bass", "Saw Bass", 36, 3, 1, "a plucky filtered saw bass") { Render = SawBass, GainDb = 8 },
        new("fm-growl", "FM Growl", 36, 4, 1, "a wobbling FM bass") { Render = FmGrowl, GainDb = 8 },
        // ---- struck & plucked ------------------------------------------------------------
        new("epiano", "Tine Piano", 60, 4, 1, "an electric piano tine with tremolo") { Render = EPiano },
        new("nylon", "Nylon Pluck", 48, 3, 1, "a plucked nylon string") { Render = Nylon },
        new("harp-run", "Harp Run", 60, 5, 1, "a two-octave pentatonic harp run") { Render = HarpRun },
        new("kalimba", "Kalimba", 72, 3, 1, "a thumb-piano tine") { Render = Kalimba },
        new("marimba", "Marimba", 60, 2.5, 1, "a soft-mallet marimba bar") { Render = Marimba },
        new("bell", "Church Bell", 60, 6, 1, "a large bell with its hum and tierce") { Render = Bell },
        new("music-box", "Music Box Tune", 72, 6, 1, "a little music-box melody") { Render = MusicBox },
        new("chimes", "Wind Chimes", 84, 6, 1, "pentatonic chimes in the breeze") { Render = Chimes },
        // ---- textures ---------------------------------------------------------------------
        new("metal", "Bowed Metal", 48, 6, 1, "a bowed metal plate, inharmonic") { Render = Metal },
        new("drone", "Dark Drone", 36, 8, 1, "a low saw drone under a slow filter") { Render = Drone },
        new("ocean", "Ocean Air", 60, 8, 1, "waves of filtered noise, unpitched") { Render = Ocean },
        new("rain", "Rain & Crackle", 60, 6, 1, "droplets and vinyl crackle, unpitched") { Render = Rain },
        new("radio", "Shortwave", 60, 6, 1, "a radio between stations") { Render = Radio },
        // ---- rhythm & sequences -------------------------------------------------------------
        new("drums", "Break Loop", 60, 4, 1, "a two-bar breakbeat at 120 BPM") { Render = Drums },
        new("arp", "Synth Arp", 48, 4, 1, "a 16th-note minor arpeggio at 120 BPM") { Render = Arp },
        new("bits", "Digital Bits", 60, 4, 1, "crushed square blips and noise bursts") { Render = Bits },
    };

    // ======================================================================
    // Oscillators and filters
    // ======================================================================

    // PolyBLEP residual: removes most of the aliasing from a naive saw / pulse edge.
    private static double Blep(double t, double dt)
    {
        if (t < dt) { t /= dt; return t + t - t * t - 1; }
        if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
        return 0;
    }

    private static double Saw(ref double p, double f)
    {
        double dt = f / SourceBuffer.Rate;
        double v = 2 * p - 1 - Blep(p, dt);
        p += dt; if (p >= 1) p -= 1;
        return v;
    }

    private static double Pulse(ref double p, double f, double pw)
    {
        double dt = f / SourceBuffer.Rate;
        double q = p - pw; if (q < 0) q += 1;
        double v = (p < pw ? 1 : -1) + Blep(p, dt) - Blep(q, dt);
        p += dt; if (p >= 1) p -= 1;
        return v;
    }

    private static double Sine(ref double p, double f)
    {
        double v = Math.Sin(Tau * p);
        p += f / SourceBuffer.Rate; if (p >= 1) p -= 1;
        return v;
    }

    /// <summary>A zero-delay-feedback state-variable filter with a per-sample cutoff.</summary>
    private sealed class Svf
    {
        private double _s1, _s2;
        public double Lp, Bp, Hp;

        public void Run(double x, double fc, double q)
        {
            double g = Math.Tan(Math.PI * Math.Clamp(fc, 10, SourceBuffer.Rate * 0.45) / SourceBuffer.Rate);
            double k = 1 / Math.Max(0.1, q);
            double a1 = 1 / (1 + g * (g + k));
            double v3 = x - _s2;
            double v1 = a1 * _s1 + a1 * g * v3;
            double v2 = _s2 + g * v1;
            _s1 = 2 * v1 - _s1; _s2 = 2 * v2 - _s2;
            Lp = v2; Bp = v1; Hp = x - k * v1 - v2;
        }
    }

    // Vowel formants (F1..F3 Hz) and their levels.
    private static readonly double[][] VowelF =
    {
        new[] { 800.0, 1150, 2900 },   // a
        new[] { 400.0, 2000, 2700 },   // e
        new[] { 300.0, 2250, 3000 },   // i
        new[] { 450.0, 800, 2830 },    // o
        new[] { 330.0, 700, 2500 },    // u
    };
    private static readonly double[] FormantGain = { 1.0, 0.55, 0.3 };

    /// <summary>Three band-passes tuned to a vowel; <c>v</c> morphs 0 (a) … 4 (u).</summary>
    private sealed class Formants
    {
        private readonly Svf[] _f = { new(), new(), new() };

        public double Run(double x, double v)
        {
            int i0 = Math.Clamp((int)Math.Floor(v), 0, 4), i1 = Math.Min(4, i0 + 1);
            double fr = Math.Clamp(v - i0, 0, 1);
            double y = 0;
            for (int k = 0; k < 3; k++)
            {
                double f = VowelF[i0][k] + (VowelF[i1][k] - VowelF[i0][k]) * fr;
                _f[k].Run(x, f, f / (60 + 30 * k));
                y += _f[k].Bp * FormantGain[k];
            }
            return y;
        }
    }

    /// <summary>A simple ping-pong delay for width.</summary>
    private static void PingPong(SourceBuffer b, double seconds, double fb, double mix)
    {
        int d = Math.Max(1, (int)(seconds * SourceBuffer.Rate));
        var dl = new double[d]; var dr = new double[d];
        int p = 0;
        for (int i = 0; i < b.N; i++)
        {
            double ol = dl[p], or = dr[p];
            dl[p] = (b.L[i] + b.R[i]) * 0.5 + or * fb;
            dr[p] = ol * fb;
            if (++p >= d) p = 0;
            b.L[i] += ol * mix; b.R[i] += or * mix;
        }
    }

    /// <summary>Adds the shared small room (the drum kits' Room) as a stereo send.</summary>
    private static void Space(SourceBuffer b, double send, double size, double decay, double damp = 0.5)
    {
        var room = new Room(size, decay, damp, SourceBuffer.Rate);
        var hp = Biquad.HighPass(150, 0.7071, SourceBuffer.Rate);
        for (int i = 0; i < b.N; i++)
        {
            room.Process(hp.Process((b.L[i] + b.R[i]) * 0.5) * send, out double wl, out double wr);
            b.L[i] += wl; b.R[i] += wr;
        }
    }

    // Fades both ends so a Scan wrapping through the file never clicks.
    private static void Edges(SourceBuffer b, double fadeIn, double fadeOut)
    {
        int a = b.At(fadeIn), z = b.At(fadeOut);
        for (int i = 0; i < a; i++) { double g = 0.5 - 0.5 * Math.Cos(Math.PI * i / a); b.L[i] *= g; b.R[i] *= g; }
        for (int i = 0; i < z; i++) { double g = 0.5 - 0.5 * Math.Cos(Math.PI * i / z); b.L[b.N - 1 - i] *= g; b.R[b.N - 1 - i] *= g; }
    }

    // Soft-limits peaks to <paramref name="crestDb"/> above the file's RMS: clicks and droplets
    // would otherwise hold the whole source down under the ceiling.
    private static void Tame(SourceBuffer b, double crestDb)
    {
        double acc = 0;
        for (int i = 0; i < b.N; i++) acc += (b.L[i] * b.L[i] + b.R[i] * b.R[i]) * 0.5;
        double th = Math.Sqrt(acc / b.N) * KitDsp.Db(crestDb);
        if (th <= 1e-9) return;
        for (int i = 0; i < b.N; i++) { b.L[i] = th * Math.Tanh(b.L[i] / th); b.R[i] = th * Math.Tanh(b.R[i] / th); }
    }

    private static double Smooth(double t, double a) => t >= a ? 1 : 0.5 - 0.5 * Math.Cos(Math.PI * t / a);

    // ======================================================================
    // Sustained tones
    // ======================================================================

    private static void SawEnsemble(SourceBuffer b)
    {
        double f0 = Hz(48);
        double[] det = { -14, -9, -4, 0, 4, 9, 14 };
        var ph = new double[det.Length];
        for (int k = 0; k < ph.Length; k++) ph[k] = b.Rng.Next01();
        var fl = new Svf(); var fr = new Svf();
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i), l = 0, r = 0;
            for (int k = 0; k < det.Length; k++)
            {
                double drift = 1 + 0.0015 * Math.Sin(Tau * (0.13 + 0.05 * k) * t + k);
                double v = Saw(ref ph[k], f0 * Cents(det[k]) * drift);
                double pan = (k - 3) / 3.0;
                l += v * (1 - pan * 0.6); r += v * (1 + pan * 0.6);
            }
            double cut = 700 + 1500 * (0.5 - 0.5 * Math.Cos(Tau * t / 6.0)) + 150 * Math.Sin(Tau * 0.7 * t);
            fl.Run(l, cut, 0.9); fr.Run(r, cut * 1.03, 0.9);
            b.L[i] = fl.Lp * 0.12; b.R[i] = fr.Lp * 0.12;
        }
        Space(b, 0.35, 0.7, 0.6);
        Edges(b, 0.25, 0.4);
    }

    private static void GlassPad(SourceBuffer b)
    {
        double f0 = Hz(60);
        double[] ratio = { 1, 2, 3, 4, 5, 6, 8, 2.002, 4.003 };
        double[] amp = { 1, 0.5, 0.28, 0.32, 0.12, 0.1, 0.08, 0.35, 0.18 };
        var pl = new double[ratio.Length]; var pr = new double[ratio.Length];
        var lfo = new double[ratio.Length];
        for (int k = 0; k < ratio.Length; k++) { lfo[k] = b.Rng.Next01() * Tau; pl[k] = b.Rng.Next01(); pr[k] = b.Rng.Next01(); }
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i), l = 0, r = 0;
            for (int k = 0; k < ratio.Length; k++)
            {
                // Each partial swells on its own, differently left and right.
                double al = amp[k] * (0.65 + 0.35 * Math.Sin(Tau * (0.11 + 0.07 * k) * t + lfo[k]));
                double ar = amp[k] * (0.65 + 0.35 * Math.Cos(Tau * (0.09 + 0.05 * k) * t + lfo[k]));
                l += al * Sine(ref pl[k], f0 * ratio[k]);
                r += ar * Sine(ref pr[k], f0 * ratio[k] * 1.0007);
            }
            b.L[i] = l * 0.2; b.R[i] = r * 0.2;
        }
        Space(b, 0.4, 0.8, 0.7, 0.3);
        Edges(b, 0.3, 0.4);
    }

    private static void Choir(SourceBuffer b)
    {
        const int voices = 6;
        double f0 = Hz(60);
        var ph = new double[voices]; var det = new double[voices]; var vib = new double[voices];
        for (int k = 0; k < voices; k++) { ph[k] = b.Rng.Next01(); det[k] = b.Rng.Next() * 12; vib[k] = b.Rng.Next01() * Tau; }
        var fL = new Formants(); var fR = new Formants();
        var breath = new Svf();
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i), l = 0, r = 0;
            double vibDepth = 18 * Smooth(t, 0.8);
            for (int k = 0; k < voices; k++)
            {
                double f = f0 * Cents(det[k] + vibDepth * Math.Sin(Tau * (4.8 + 0.35 * k) * t + vib[k]));
                double v = Pulse(ref ph[k], f, 0.28 + 0.03 * k);
                if (k % 2 == 0) l += v; else r += v;
            }
            breath.Run(b.Rng.Next(), 3000, 0.7);
            double n = breath.Bp * 0.25;
            b.L[i] = fL.Run(l + n, 0.15) * 0.35; b.R[i] = fR.Run(r + n, 0.2) * 0.35;
        }
        Space(b, 0.45, 0.75, 0.65);
        Edges(b, 0.35, 0.4);
    }

    private static void Vowels(SourceBuffer b)
    {
        double f0 = Hz(48), p1 = 0, p2 = 0.3;
        var fl = new Formants(); var fr = new Formants();
        double len = SourceBuffer.T(b.N);
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            double f = f0 * Cents(14 * Smooth(t, 0.6) * Math.Sin(Tau * 5.1 * t));
            double x = Saw(ref p1, f) * 0.7 + Saw(ref p2, f * Cents(6)) * 0.3;
            double v = 4.0 * Math.Clamp((t - 0.25) / (len - 0.5), 0, 1);
            b.L[i] = fl.Run(x, v) * 0.5; b.R[i] = fr.Run(x, Math.Min(4, v + 0.05)) * 0.5;
        }
        Space(b, 0.25, 0.5, 0.4);
        Edges(b, 0.12, 0.3);
    }

    private static void Whisper(SourceBuffer b)
    {
        var fl = new Formants(); var fr = new Formants();
        double len = SourceBuffer.T(b.N);
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            double v = 2 + 2 * Math.Sin(Tau * t / len * 1.5 - 1.2);
            double syll = 0.55 + 0.45 * Math.Pow(Math.Abs(Math.Sin(Math.PI * 2.2 * t)), 0.6);
            b.L[i] = fl.Run(b.Rng.Next(), v) * syll * 0.9;
            b.R[i] = fr.Run(b.Rng.Next(), 4 - v) * syll * 0.9;
        }
        Space(b, 0.3, 0.5, 0.5);
        Edges(b, 0.2, 0.3);
    }

    private static void Strings(SourceBuffer b)
    {
        double f0 = Hz(48);
        const int n = 8;
        var ph = new double[n]; var det = new double[n]; var vr = new double[n];
        for (int k = 0; k < n; k++) { ph[k] = b.Rng.Next01(); det[k] = b.Rng.Next() * 10; vr[k] = b.Rng.Next01() * Tau; }
        var bodyL = new[] { Biquad.Peaking(280, 1.2, 5, SourceBuffer.Rate), Biquad.Peaking(1100, 1.5, 3, SourceBuffer.Rate), Biquad.Peaking(2800, 2, 2, SourceBuffer.Rate), Biquad.LowPass(5200, 0.7, SourceBuffer.Rate) };
        var bodyR = new[] { Biquad.Peaking(300, 1.2, 5, SourceBuffer.Rate), Biquad.Peaking(1180, 1.5, 3, SourceBuffer.Rate), Biquad.Peaking(2650, 2, 2, SourceBuffer.Rate), Biquad.LowPass(5000, 0.7, SourceBuffer.Rate) };
        var bow = new Svf();
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i), l = 0, r = 0;
            double swell = Smooth(t, 0.6);
            for (int k = 0; k < n; k++)
            {
                double f = f0 * (k >= 6 ? 2 : 1) * Cents(det[k] + 11 * Math.Sin(Tau * (5.2 + 0.3 * k) * t + vr[k]));
                double v = Saw(ref ph[k], f) * (k >= 6 ? 0.45 : 1);
                if (k % 2 == 0) l += v; else r += v;
            }
            bow.Run(b.Rng.Next(), 3200, 1.2);
            double noise = bow.Bp * 0.18 * (0.8 + 0.2 * Math.Sin(Tau * 1.3 * t));
            double xl = l + noise, xr = r + noise;
            foreach (var q in bodyL) xl = q.Process(xl);
            foreach (var q in bodyR) xr = q.Process(xr);
            b.L[i] = xl * swell * 0.08; b.R[i] = xr * swell * 0.08;
        }
        Space(b, 0.4, 0.75, 0.55);
        Edges(b, 0.05, 0.4);
    }

    private static void Flute(SourceBuffer b)
    {
        double f0 = Hz(72), p1 = 0, p2 = 0, p3 = 0;
        var breath = new Svf(); var air = new Svf();
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            double vib = 16 * Smooth(Math.Max(0, t - 0.6), 0.8) * Math.Sin(Tau * 5 * t);
            double f = f0 * Cents(vib);
            double env = Smooth(t, 0.12) * (0.9 + 0.1 * Math.Sin(Tau * 0.4 * t));
            double tone = Sine(ref p1, f) + 0.25 * Sine(ref p2, 2 * f) + 0.06 * Sine(ref p3, 3 * f);
            breath.Run(b.Rng.Next(), f * 2, 2.5);
            air.Run(b.Rng.Next(), 6000, 0.7);
            double chiff = Math.Exp(-t / 0.05) * 0.6;
            double x = env * (tone * 0.5 + breath.Bp * (0.25 + chiff)) + air.Hp * 0.03 * env;
            b.L[i] = x * 0.6; b.R[i] = x * 0.6;
        }
        Space(b, 0.35, 0.6, 0.55);
        Edges(b, 0.005, 0.3);
    }

    private static void Brass(SourceBuffer b)
    {
        double f0 = Hz(48);
        double[] det = { -7, -2, 3, 8, 1 };
        var ph = new double[det.Length];
        for (int k = 0; k < ph.Length; k++) ph[k] = b.Rng.Next01();
        var fl = new Svf(); var fr = new Svf();
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i), l = 0, r = 0;
            double swell = Smooth(t, 0.7);
            double vib = 9 * Smooth(Math.Max(0, t - 1.2), 1) * Math.Sin(Tau * 5.4 * t);
            for (int k = 0; k < det.Length; k++)
            {
                double v = Saw(ref ph[k], f0 * (k == 4 ? 2 : 1) * Cents(det[k] + vib)) * (k == 4 ? 0.4 : 1);
                if (k % 2 == 0) l += v; else r += v;
            }
            double cut = 300 + 2600 * swell * swell + 300 * Math.Sin(Tau * 0.5 * t);
            fl.Run(l, cut, 1.1); fr.Run(r, cut * 1.02, 1.1);
            b.L[i] = KitDsp.Saturate(fl.Lp * 0.2 * (0.2 + 0.8 * swell), 0.25, 0.6);
            b.R[i] = KitDsp.Saturate(fr.Lp * 0.2 * (0.2 + 0.8 * swell), 0.25, 0.6);
        }
        Space(b, 0.3, 0.6, 0.5);
        Edges(b, 0.01, 0.3);
    }

    private static void Organ(SourceBuffer b)
    {
        double f0 = Hz(48);
        double[] ratio = { 0.5, 1, 1.5, 2, 3, 4, 6 };
        double[] amp = { 0.25, 1, 0.55, 0.6, 0.35, 0.3, 0.12 };   // a light 16': the pitch stays on 8'
        var ph = new double[ratio.Length];
        var click = new Svf();
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i), x = 0;
            for (int k = 0; k < ratio.Length; k++) x += amp[k] * Sine(ref ph[k], f0 * ratio[k]);
            click.Run(b.Rng.Next(), 2500, 0.8);
            x += click.Bp * Math.Exp(-t / 0.008) * 1.5;
            // The rotor: horn and drum sweep the sound across the room at ~6 Hz.
            double rot = Tau * (5.6 + 0.4 * Math.Sin(Tau * 0.2 * t)) * t;
            double am = 0.22 * Math.Sin(rot);
            b.L[i] = x * (0.78 + am) * 0.2; b.R[i] = x * (0.78 - am) * 0.2;
        }
        Space(b, 0.25, 0.45, 0.35);
        Edges(b, 0.004, 0.3);
    }

    private static void TapeChord(SourceBuffer b)
    {
        int[] notes = { 48, 55, 64, 67 };
        var pl = new double[notes.Length * 2]; var pr = new double[notes.Length * 2];
        for (int k = 0; k < pl.Length; k++) { pl[k] = b.Rng.Next01(); pr[k] = b.Rng.Next01(); }
        var lpL = Biquad.LowPass(3200, 0.6, SourceBuffer.Rate); var lpR = Biquad.LowPass(3000, 0.6, SourceBuffer.Rate);
        var hiss = Biquad.HighPass(3000, 0.7, SourceBuffer.Rate);
        double w = b.Rng.Next01() * Tau;
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            double wow = Cents(11 * Math.Sin(Tau * 0.55 * t + w) + 3 * Math.Sin(Tau * 7.3 * t));
            double l = 0, r = 0;
            for (int k = 0; k < notes.Length; k++)
            {
                double f = Hz(notes[k]) * wow, a = k == 0 ? 1 : 0.7;
                l += a * (Tri(ref pl[2 * k], f) * 0.7 + Sine(ref pl[2 * k + 1], f * 2.001) * 0.2);
                r += a * (Tri(ref pr[2 * k], f * Cents(4)) * 0.7 + Sine(ref pr[2 * k + 1], f * 1.999) * 0.2);
            }
            double h = hiss.Process(b.Rng.Next()) * 0.012;
            b.L[i] = KitDsp.Saturate(lpL.Process(l * 0.22), 0.3, 0.8) + h;
            b.R[i] = KitDsp.Saturate(lpR.Process(r * 0.22), 0.3, 0.8) + h;
        }
        Space(b, 0.25, 0.55, 0.6, 0.6);
        Edges(b, 0.4, 0.5);
    }

    private static double Tri(ref double p, double f)
    {
        double v = 1 - 4 * Math.Abs(p - 0.5);
        p += f / SourceBuffer.Rate; if (p >= 1) p -= 1;
        return v;
    }

    // ======================================================================
    // Bass
    // ======================================================================

    private static void Sub(SourceBuffer b)
    {
        double f0 = Hz(36), p1 = 0, p2 = 0;
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            double x = Sine(ref p1, f0) + 0.18 * Sine(ref p2, 2 * f0);
            double v = KitDsp.Saturate(x * 0.5 * Smooth(t, 0.006), 0.35, 0.5);
            b.L[i] = v; b.R[i] = v;
        }
        Edges(b, 0.004, 0.25);
    }

    private static void SawBass(SourceBuffer b)
    {
        double f0 = Hz(36), p1 = 0, p2 = 0.5, p3 = 0;
        var f = new Svf();
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            double x = Saw(ref p1, f0) + 0.8 * Pulse(ref p2, f0 * Cents(6), 0.5) + 0.8 * Sine(ref p3, f0);
            double cut = 180 + 2600 * Math.Exp(-t / 0.22) + 220;
            f.Run(x, cut, 1.6);
            double amp = 0.72 + 0.28 * Math.Exp(-t / 0.3);
            double v = KitDsp.Saturate(f.Lp * 0.35 * amp * Smooth(t, 0.003), 0.4, 0.6);
            b.L[i] = v; b.R[i] = v;
        }
        Edges(b, 0.002, 0.25);
    }

    private static void FmGrowl(SourceBuffer b)
    {
        double f0 = Hz(36), pc = 0, pm = 0, ps = 0;
        var f = new Svf();
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            double index = 2.2 + 2.0 * Math.Sin(Tau * 1.5 * t - Math.PI / 2);
            double m = Math.Sin(Tau * pm) * index;
            pm += f0 * 1.0 / SourceBuffer.Rate; if (pm >= 1) pm -= 1;
            double c = Math.Sin(Tau * pc + m);
            pc += f0 / SourceBuffer.Rate; if (pc >= 1) pc -= 1;
            double sub = Sine(ref ps, f0);
            f.Run(c, 900 + 700 * Math.Sin(Tau * 1.5 * t), 2.2);
            double v = KitDsp.Saturate((f.Lp * 0.8 + c * 0.2 + sub * 0.45) * 0.4, 0.45, 0.4);
            b.L[i] = v; b.R[i] = v;
        }
        Edges(b, 0.004, 0.25);
    }

    // ======================================================================
    // Struck and plucked
    // ======================================================================

    private static void EPiano(SourceBuffer b)
    {
        double f0 = Hz(60), pc = 0, pm = 0, pt = 0;
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            double index = 1.9 * Math.Exp(-t / 0.5) + 0.35;
            double m = Math.Sin(Tau * pm) * index;
            pm += f0 / SourceBuffer.Rate; if (pm >= 1) pm -= 1;
            double c = Math.Sin(Tau * pc + m);
            pc += f0 / SourceBuffer.Rate; if (pc >= 1) pc -= 1;
            double tine = Sine(ref pt, f0 * 14.1) * 0.22 * Math.Exp(-t / 0.04);
            double x = (c + tine) * Math.Exp(-t / 1.7) * Smooth(t, 0.002) * 0.45;
            double trem = 0.25 * Math.Sin(Tau * 4.6 * t);
            b.L[i] = x * (1 + trem); b.R[i] = x * (1 - trem);
        }
        Space(b, 0.15, 0.4, 0.4);
        Edges(b, 0, 0.2);
    }

    /// <summary>A Karplus-Strong string plucked at <paramref name="at"/> into both channels.</summary>
    private static void Pluck(SourceBuffer b, double midi, double at, double gain, double decay, double pan, double bright)
    {
        double f = Hz(midi);
        int d = Math.Max(2, (int)Math.Round(SourceBuffer.Rate / f - 0.5));
        var line = new double[d];
        var ex = Biquad.LowPass(1500 + 6000 * bright, 0.7, SourceBuffer.Rate);
        for (int k = 0; k < d; k++) line[k] = ex.Process(b.Rng.Next());
        // Per-period loss that reaches -60 dB after `decay` seconds.
        double loss = Math.Pow(10, -3.0 / (decay * f));
        int start = b.At(at), p = 0;
        double prev = 0, gl = gain * Math.Sqrt(0.5 * (1 - pan)), gr = gain * Math.Sqrt(0.5 * (1 + pan));
        for (int i = start; i < b.N; i++)
        {
            double y = line[p];
            double nv = (y + prev) * 0.5 * loss;
            prev = y;
            line[p] = nv;
            if (++p >= d) p = 0;
            b.L[i] += y * gl; b.R[i] += y * gr;
        }
    }

    private static void Nylon(SourceBuffer b)
    {
        Pluck(b, 48, 0, 0.5, 2.8, -0.1, 0.35);
        Pluck(b, 48.03, 0.001, 0.25, 2.4, 0.3, 0.2);
        var bodyL = Biquad.Peaking(210, 1.4, 4, SourceBuffer.Rate); var bodyR = Biquad.Peaking(230, 1.4, 4, SourceBuffer.Rate);
        for (int i = 0; i < b.N; i++) { b.L[i] = bodyL.Process(b.L[i]); b.R[i] = bodyR.Process(b.R[i]); }
        Space(b, 0.15, 0.35, 0.3);
    }

    private static void HarpRun(SourceBuffer b)
    {
        int[] pent = { 0, 2, 4, 7, 9 };
        int n = 0;
        for (int oct = 0; oct < 2; oct++)
            foreach (int s in pent)
            {
                double at = 0.02 + n * 0.12;
                Pluck(b, 60 + oct * 12 + s, at, 0.32, 2.4, -0.6 + 0.12 * n, 0.5);
                n++;
            }
        Pluck(b, 84, 0.02 + n * 0.12, 0.32, 2.6, 0.6, 0.5);
        Space(b, 0.35, 0.65, 0.55);
    }

    /// <summary>Strikes a modal body: (ratio, amp, decay seconds) partials of a fundamental.</summary>
    private static void Strike(SourceBuffer b, double midi, double at, double gain, double pan, (double Ratio, double Amp, double Decay)[] modes,
        double malletMs = 1.5, double detune = 0)
    {
        double f0 = Hz(midi);
        int start = b.At(at);
        double gl = gain * Math.Sqrt(0.5 * (1 - pan)), gr = gain * Math.Sqrt(0.5 * (1 + pan));
        var ml = new Modal[modes.Length]; var mr = new Modal[modes.Length];
        for (int k = 0; k < modes.Length; k++)
        {
            ml[k] = new Modal(f0 * modes[k].Ratio, modes[k].Decay, SourceBuffer.Rate);
            mr[k] = new Modal(f0 * modes[k].Ratio * (1 + detune), modes[k].Decay, SourceBuffer.Rate);
        }
        int mallet = Math.Max(1, (int)(malletMs * 0.001 * SourceBuffer.Rate));
        for (int i = start; i < b.N; i++)
        {
            int j = i - start;
            double x = j < mallet ? Math.Sin(Math.PI * j / mallet) / mallet * 40 : 0;
            double l = 0, r = 0;
            for (int k = 0; k < modes.Length; k++) { l += ml[k].Process(x) * modes[k].Amp; r += mr[k].Process(x) * modes[k].Amp; }
            b.L[i] += l * gl; b.R[i] += r * gr;
            if (j > 4 * mallet && j % 4800 == 0 && Math.Abs(l) + Math.Abs(r) < 1e-7) break;
        }
    }

    private static readonly (double, double, double)[] KalimbaModes = { (1, 1, 1.8), (5.4, 0.3, 0.35), (11.2, 0.12, 0.12) };
    private static readonly (double, double, double)[] MarimbaModes = { (1, 1, 1.1), (3.93, 0.4, 0.35), (9.2, 0.15, 0.1), (0.5, 0.05, 0.6) };
    private static readonly (double, double, double)[] BellModes =
    {
        (0.5, 0.6, 6), (1, 1, 5), (1.19, 0.55, 4.2), (1.5, 0.35, 3.5), (2, 0.5, 3),
        (2.51, 0.25, 2), (3.01, 0.2, 1.5), (4.16, 0.14, 1), (5.43, 0.08, 0.6),
    };
    private static readonly (double, double, double)[] BoxModes = { (1, 1, 1.4), (2.76, 0.18, 0.3), (5.4, 0.08, 0.1) };
    private static readonly (double, double, double)[] ChimeModes = { (1, 1, 3.2), (2.76, 0.35, 1.2), (5.4, 0.18, 0.5), (8.93, 0.06, 0.2) };

    private static void Kalimba(SourceBuffer b)
    {
        Strike(b, 72, 0, 0.5, 0, KalimbaModes, 1.0, 0.0006);
        var box = Biquad.Peaking(380, 1.2, 4, SourceBuffer.Rate); var boxR = Biquad.Peaking(400, 1.2, 4, SourceBuffer.Rate);
        for (int i = 0; i < b.N; i++) { b.L[i] = box.Process(b.L[i]); b.R[i] = boxR.Process(b.R[i]); }
        Space(b, 0.2, 0.35, 0.35);
    }

    private static void Marimba(SourceBuffer b)
    {
        Strike(b, 60, 0, 0.5, 0, MarimbaModes, 3.0, 0.0004);
        Space(b, 0.2, 0.4, 0.3);
    }

    private static void Bell(SourceBuffer b)
    {
        Strike(b, 60, 0, 0.35, 0, BellModes, 1.2, 0.0012);
        Space(b, 0.35, 0.85, 0.75, 0.5);
    }

    private static void MusicBox(SourceBuffer b)
    {
        // A little tune in C: two phrases of eighth notes at 100 BPM.
        int[] tune = { 72, 76, 79, 84, 83, 79, 76, 74, 72, 74, 76, 79, 77, 74, 71, 72, 76, 72, 67, 72 };
        for (int k = 0; k < tune.Length; k++)
            Strike(b, tune[k], 0.02 + k * 0.28, 0.35, (k % 3 - 1) * 0.3, BoxModes, 0.6, 0.0008);
        Space(b, 0.3, 0.5, 0.5);
    }

    private static void Chimes(SourceBuffer b)
    {
        int[] notes = { 84, 86, 88, 91, 93 };
        double t = 0.01;
        while (t < b.N / (double)SourceBuffer.Rate - 0.5)
        {
            int k = (int)(b.Rng.Next01() * notes.Length) % notes.Length;
            Strike(b, notes[k], t, 0.12 + 0.12 * b.Rng.Next01(), (k - 2) * 0.4, ChimeModes, 0.8, 0.0015);
            t += 0.08 + 0.35 * b.Rng.Next01();
        }
        Space(b, 0.35, 0.7, 0.6, 0.4);
        Edges(b, 0, 0.8);
    }

    // ======================================================================
    // Textures
    // ======================================================================

    private static void Metal(SourceBuffer b)
    {
        double f0 = Hz(48);
        double[] ratio = { 1, 2.32, 4.25, 6.63, 9.38, 12.1 };
        var ml = new Modal[ratio.Length]; var mr = new Modal[ratio.Length];
        for (int k = 0; k < ratio.Length; k++)
        {
            ml[k] = new Modal(f0 * ratio[k], 3.5 - 0.4 * k, SourceBuffer.Rate);
            mr[k] = new Modal(f0 * ratio[k] * 1.0021, 3.5 - 0.4 * k, SourceBuffer.Rate);
        }
        var exL = new Svf(); var exR = new Svf();
        double w1 = b.Rng.Next01() * Tau, w2 = b.Rng.Next01() * Tau;
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            double bowAmt = Smooth(t, 0.8) * (0.6 + 0.25 * Math.Sin(Tau * 0.37 * t + w1) + 0.15 * Math.Sin(Tau * 1.1 * t + w2));
            exL.Run(b.Rng.Next(), 2500, 0.8); exR.Run(b.Rng.Next(), 2600, 0.8);
            double xl = exL.Lp * bowAmt * 0.02, xr = exR.Lp * bowAmt * 0.02;
            double l = 0, r = 0;
            for (int k = 0; k < ratio.Length; k++) { double a = 1.0 / (1 + k * 0.5); l += ml[k].Process(xl) * a; r += mr[k].Process(xr) * a; }
            b.L[i] = l; b.R[i] = r;
        }
        Space(b, 0.4, 0.8, 0.75, 0.4);
        Edges(b, 0.1, 0.6);
    }

    private static void Drone(SourceBuffer b)
    {
        double f0 = Hz(36);
        (double Ratio, double Cent, double Amp)[] osc = { (1, -6, 1), (1, 7, 1), (1.5, -3, 0.6), (2, 4, 0.4), (1, 0, 0.7) };
        var pl = new double[osc.Length]; var pr = new double[osc.Length];
        for (int k = 0; k < osc.Length; k++) { pl[k] = b.Rng.Next01(); pr[k] = b.Rng.Next01(); }
        var fl = new Svf(); var fr = new Svf(); var rumble = Biquad.LowPass(120, 0.7, SourceBuffer.Rate);
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i), l = 0, r = 0;
            for (int k = 0; k < osc.Length; k++)
            {
                double f = f0 * osc[k].Ratio * Cents(osc[k].Cent);
                if (k == 4) { l += osc[k].Amp * Sine(ref pl[k], f) * 2; r += osc[k].Amp * Sine(ref pr[k], f) * 2; continue; }
                l += osc[k].Amp * Saw(ref pl[k], f); r += osc[k].Amp * Saw(ref pr[k], f * Cents(3));
            }
            double cut = 220 + 700 * (0.5 - 0.5 * Math.Cos(Tau * t / 8.0 * 1.5)) + 60 * Math.Sin(Tau * 0.3 * t);
            fl.Run(l, cut, 2.0); fr.Run(r, cut * 1.05, 2.0);
            double rm = rumble.Process(b.Rng.Next()) * 0.6;
            b.L[i] = (fl.Lp + rm) * 0.2; b.R[i] = (fr.Lp + rm) * 0.2;
        }
        Space(b, 0.45, 0.9, 0.85, 0.6);
        Edges(b, 0.5, 0.6);
    }

    private static void Ocean(SourceBuffer b)
    {
        var fl = new Svf(); var fr = new Svf();
        double pinkL = 0, pinkR = 0;
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            double wave = 0.5 + 0.5 * Math.Sin(Tau * t / 3.6 - 1.2) * (0.7 + 0.3 * Math.Sin(Tau * t / 2.3));
            wave = wave * wave;
            pinkL = pinkL * 0.97 + b.Rng.Next() * 0.03 * 6; pinkR = pinkR * 0.97 + b.Rng.Next() * 0.03 * 6;
            double cut = 350 + 3200 * wave;
            fl.Run(pinkL + b.Rng.Next() * 0.15, cut, 0.8); fr.Run(pinkR + b.Rng.Next() * 0.15, cut * 1.1, 0.8);
            double g = 0.15 + 0.85 * wave;
            b.L[i] = fl.Lp * g * 0.8; b.R[i] = fr.Lp * g * 0.8;
        }
        Edges(b, 0.5, 0.6);
    }

    private static void Rain(SourceBuffer b)
    {
        var bed = new Svf(); var bedR = new Svf();
        for (int i = 0; i < b.N; i++)
        {
            bed.Run(b.Rng.Next(), 4500, 0.6); bedR.Run(b.Rng.Next(), 4500, 0.6);
            b.L[i] = bed.Bp * 0.08; b.R[i] = bedR.Bp * 0.08;
        }
        // Droplets: tiny pitched pings, then sparse crackle clicks.
        double t = 0;
        double end = b.N / (double)SourceBuffer.Rate;
        while ((t += 0.004 + 0.045 * b.Rng.Next01()) < end)
        {
            double f = 1800 + 4200 * b.Rng.Next01(), pan = b.Rng.Next();
            var m = new Modal(f, 0.02 + 0.04 * b.Rng.Next01(), SourceBuffer.Rate);
            int s = b.At(t), len = Math.Min(b.N - s, (int)(0.08 * SourceBuffer.Rate));
            double g = 0.04 + 0.12 * b.Rng.Next01() * b.Rng.Next01();
            for (int j = 0; j < len; j++)
            {
                double y = m.Process(j == 0 ? 1 : 0) * g;
                b.L[s + j] += y * (1 - pan) * 0.7; b.R[s + j] += y * (1 + pan) * 0.7;
            }
        }
        t = 0;
        while ((t += 0.02 + 0.2 * b.Rng.Next01()) < end)
        {
            int s = b.At(t);
            double g = 0.04 + 0.1 * b.Rng.Next01(), pan = b.Rng.Next();
            for (int j = 0; j < 24 && s + j < b.N; j++)
            {
                double y = b.Rng.Next() * g * Math.Exp(-j / 5.0);
                b.L[s + j] += y * (1 - pan); b.R[s + j] += y * (1 + pan);
            }
        }
        Tame(b, 12);
        Edges(b, 0.2, 0.3);
    }

    private static void Radio(SourceBuffer b)
    {
        var sweep = new Svf(); var voice = new Formants(); var band = new[] { Biquad.HighPass(350, 0.7, SourceBuffer.Rate), Biquad.LowPass(3200, 0.8, SourceBuffer.Rate) };
        double pw = 0, pv = 0;
        double end = b.N / (double)SourceBuffer.Rate;
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            // A tuning dial: a heterodyne whistle gliding past stations, noise in between.
            double whistle = Sine(ref pw, 700 + 900 * (0.5 + 0.5 * Math.Sin(Tau * t / end * 1.3))) * (0.3 + 0.3 * Math.Sin(Tau * 0.8 * t));
            sweep.Run(b.Rng.Next(), 600 + 2400 * (0.5 + 0.5 * Math.Sin(Tau * 0.45 * t)), 3);
            double station = Math.Pow(Math.Max(0, Math.Sin(Tau * t / end * 2.5)), 2);
            double syll = Math.Max(0, Math.Sin(Tau * 3.7 * t)) * Math.Max(0, Math.Sin(Tau * 0.9 * t + 1));
            double vox = voice.Run(Pulse(ref pv, 118 * Cents(60 * Math.Sin(Tau * 1.7 * t)), 0.2), 2 + 2 * Math.Sin(Tau * 2.9 * t)) * syll;
            double x = whistle * 0.25 * (1 - station) + sweep.Bp * 0.6 * (1 - 0.6 * station) + vox * 1.4 * station;
            foreach (var q in band) x = q.Process(x);
            x = KitDsp.Saturate(x * 0.8, 0.4, 0.2);
            b.L[i] = x; b.R[i] = x;
        }
        Space(b, 0.12, 0.3, 0.3);
        Edges(b, 0.1, 0.2);
    }

    // ======================================================================
    // Rhythm and sequences
    // ======================================================================

    private static void Drums(SourceBuffer b)
    {
        // A two-bar break played on the Kompakt kit's own voices.
        var kit = KitCatalog.ById("kompakt") ?? KitCatalog.All[0];
        KitPad? Pad(int note) { foreach (var p in kit.Pads) if (p.Note == note) return p; return null; }
        var cache = new Dictionary<int, RenderedSample>();
        const double step = 60.0 / 120 / 4;   // 16ths at 120 BPM
        (int Step, int Note, double Vel)[] hits =
        {
            (0, 36, 1), (3, 36, 0.6), (10, 36, 0.9), (16, 36, 1), (19, 36, 0.55), (24, 36, 0.8), (26, 36, 0.9),
            (4, 38, 1), (7, 38, 0.35), (12, 38, 1), (15, 38, 0.4), (20, 38, 1), (23, 38, 0.35), (28, 38, 1), (30, 38, 0.5), (31, 38, 0.4),
            (14, 46, 0.6), (30, 46, 0.5),
        };
        var all = new List<(int Step, int Note, double Vel)>(hits);
        for (int s = 0; s < 32; s += 2) if (s != 14 && s != 30) all.Add((s, 42, s % 4 == 0 ? 0.7 : 0.45));
        foreach (var (st, note, vel) in all)
        {
            var pad = Pad(note);
            if (pad is null) continue;
            if (!cache.TryGetValue(note, out var smp)) cache[note] = smp = KitRenderer.Render(pad, (uint)(0x51 + note));
            int at = b.At(st * step);
            double pan = note is 42 or 46 ? 0.25 : 0;
            for (int j = 0; j < smp.Frames && at + j < b.N; j++)
            {
                double x = smp.Channels == 2 ? (smp.Interleaved[2 * j] + smp.Interleaved[2 * j + 1]) * 0.5 : smp.Interleaved[j];
                b.L[at + j] += x * vel * (1 - pan) * 0.5; b.R[at + j] += x * vel * (1 + pan) * 0.5;
            }
        }
        Space(b, 0.15, 0.35, 0.25);
    }

    private static void Arp(SourceBuffer b)
    {
        int[] seq = { 48, 51, 55, 60, 63, 60, 55, 51, 48, 51, 55, 60, 63, 67, 63, 60,
                      44, 48, 51, 56, 60, 56, 51, 48, 46, 50, 53, 58, 62, 58, 53, 50 };
        const double step = 60.0 / 120 / 4;
        var f = new Svf();
        double ph = 0;
        for (int i = 0; i < b.N; i++)
        {
            double t = SourceBuffer.T(i);
            int k = Math.Min(seq.Length - 1, (int)(t / step));
            double local = t - k * step;
            double env = Math.Exp(-local / 0.09) * Smooth(local, 0.002);
            double x = Pulse(ref ph, Hz(seq[k]), 0.32);
            f.Run(x, 400 + 3200 * Math.Exp(-local / 0.06), 2.2);
            double v = f.Lp * env * 0.3;
            b.L[i] = v; b.R[i] = v;
        }
        PingPong(b, step * 3, 0.45, 0.45);
        Edges(b, 0, 0.05);
    }

    private static void Bits(SourceBuffer b)
    {
        int[] pent = { 0, 3, 5, 7, 10 };
        const double step = 60.0 / 120 / 4;
        int steps = (int)(b.N / (step * SourceBuffer.Rate));
        for (int k = 0; k < steps; k++)
        {
            if (b.Rng.Next01() < 0.22) continue;
            int s = b.At(k * step);
            int len = (int)((0.02 + 0.09 * b.Rng.Next01()) * SourceBuffer.Rate);
            bool noise = b.Rng.Next01() < 0.2;
            double f = Hz(60 + pent[(int)(b.Rng.Next01() * 5) % 5] + 12 * ((int)(b.Rng.Next01() * 3) - 1));
            double pan = b.Rng.Next() * 0.8, ph = 0, g = 0.25 + 0.2 * b.Rng.Next01();
            double glide = b.Rng.Next01() < 0.3 ? (b.Rng.Next01() < 0.5 ? 2 : 0.5) : 1;
            for (int j = 0; j < len && s + j < b.N; j++)
            {
                double u = j / (double)len;
                double x = noise ? (b.Rng.Next() > 0 ? 1 : -1) : (Pulse(ref ph, f * Math.Pow(glide, u), 0.5));
                double y = x * g * (1 - u);
                b.L[s + j] += y * (1 - pan); b.R[s + j] += y * (1 + pan);
            }
        }
        // 5-bit, 8 kHz: the grit is the point.
        foreach (var ch in new[] { b.L, b.R }) KitDsp.Crush(ch, 5, 6);
        var lp = Biquad.LowPass(9000, 0.7, SourceBuffer.Rate); var lpR = Biquad.LowPass(9000, 0.7, SourceBuffer.Rate);
        for (int i = 0; i < b.N; i++) { b.L[i] = lp.Process(b.L[i]) * 0.6; b.R[i] = lpR.Process(b.R[i]) * 0.6; }
    }
}
