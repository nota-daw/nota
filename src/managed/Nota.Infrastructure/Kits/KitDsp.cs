// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DSP primitives for the offline drum-kit renderer (see KitRenderer). Nothing here
// runs on the audio thread — it renders one-shots into arrays, so the code trades
// speed for quality: every voice is synthesized at 4x the target rate, saturated
// there, and decimated through a zero-phase Butterworth, which is what keeps the
// results free of the aliasing "fizz" that makes cheap synthetic drums sound plastic.
// Pure, dependency-free and deterministic: the same seed always yields the same file.

using System;

namespace Nota.Infrastructure.Kits;

/// <summary>Deterministic xorshift32 noise source — one per voice, seeded from the
/// sample's identity so regenerating a kit reproduces it bit-for-bit.</summary>
internal sealed class Rng
{
    private uint _s;
    public Rng(uint seed) => _s = seed == 0 ? 0x9E3779B9u : seed;

    public uint NextUInt() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
    /// <summary>Uniform white noise in [-1, 1).</summary>
    public double Next() => (NextUInt() & 0xFFFFFF) / 8388608.0 - 1.0;
    public double Next01() => (NextUInt() & 0xFFFFFF) / 16777216.0;
    /// <summary>Gaussian-ish noise (sum of three uniforms) — a denser, less "crackly"
    /// noise floor than a raw uniform draw, closer to the analog noise it stands in for.</summary>
    public double NextGauss() => (Next() + Next() + Next()) * 0.5774;
}

/// <summary>Direct-form-I biquad with the usual RBJ cookbook designs. Offline, so it
/// keeps double state and is re-used by the tone stage, the room and the decimator.</summary>
internal sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _x1, _x2, _y1, _y2;

    public void Reset() { _x1 = _x2 = _y1 = _y2 = 0; }

    public double Process(double x)
    {
        double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
        return y;
    }

    private void Set(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0; _a1 = a1 / a0; _a2 = a2 / a0;
        Reset();
    }

    public static Biquad LowPass(double freq, double q, double sr)
    {
        var f = new Biquad();
        double w = 2 * Math.PI * Clamp(freq, sr) / sr, cs = Math.Cos(w), sn = Math.Sin(w), al = sn / (2 * q);
        f.Set((1 - cs) / 2, 1 - cs, (1 - cs) / 2, 1 + al, -2 * cs, 1 - al);
        return f;
    }

    public static Biquad HighPass(double freq, double q, double sr)
    {
        var f = new Biquad();
        double w = 2 * Math.PI * Clamp(freq, sr) / sr, cs = Math.Cos(w), sn = Math.Sin(w), al = sn / (2 * q);
        f.Set((1 + cs) / 2, -(1 + cs), (1 + cs) / 2, 1 + al, -2 * cs, 1 - al);
        return f;
    }

    /// <summary>Constant-peak-gain band-pass (0 dB at centre).</summary>
    public static Biquad BandPass(double freq, double q, double sr)
    {
        var f = new Biquad();
        double w = 2 * Math.PI * Clamp(freq, sr) / sr, cs = Math.Cos(w), sn = Math.Sin(w), al = sn / (2 * q);
        f.Set(al, 0, -al, 1 + al, -2 * cs, 1 - al);
        return f;
    }

    public static Biquad Peaking(double freq, double q, double gainDb, double sr)
    {
        var f = new Biquad();
        double a = Math.Pow(10, gainDb / 40);
        double w = 2 * Math.PI * Clamp(freq, sr) / sr, cs = Math.Cos(w), sn = Math.Sin(w), al = sn / (2 * q);
        f.Set(1 + al * a, -2 * cs, 1 - al * a, 1 + al / a, -2 * cs, 1 - al / a);
        return f;
    }

    public static Biquad LowShelf(double freq, double gainDb, double sr, double slope = 0.9)
    {
        var f = new Biquad();
        double a = Math.Pow(10, gainDb / 40);
        double w = 2 * Math.PI * Clamp(freq, sr) / sr, cs = Math.Cos(w), sn = Math.Sin(w);
        double al = sn / 2 * Math.Sqrt((a + 1 / a) * (1 / slope - 1) + 2);
        double t = 2 * Math.Sqrt(a) * al;
        f.Set(a * ((a + 1) - (a - 1) * cs + t), 2 * a * ((a - 1) - (a + 1) * cs), a * ((a + 1) - (a - 1) * cs - t),
              (a + 1) + (a - 1) * cs + t, -2 * ((a - 1) + (a + 1) * cs), (a + 1) + (a - 1) * cs - t);
        return f;
    }

    public static Biquad HighShelf(double freq, double gainDb, double sr, double slope = 0.9)
    {
        var f = new Biquad();
        double a = Math.Pow(10, gainDb / 40);
        double w = 2 * Math.PI * Clamp(freq, sr) / sr, cs = Math.Cos(w), sn = Math.Sin(w);
        double al = sn / 2 * Math.Sqrt((a + 1 / a) * (1 / slope - 1) + 2);
        double t = 2 * Math.Sqrt(a) * al;
        f.Set(a * ((a + 1) + (a - 1) * cs + t), -2 * a * ((a - 1) + (a + 1) * cs), a * ((a + 1) + (a - 1) * cs - t),
              (a + 1) - (a - 1) * cs + t, 2 * ((a - 1) - (a + 1) * cs), (a + 1) - (a - 1) * cs - t);
        return f;
    }

    // Keep the design frequency inside the stable range of the bilinear transform.
    private static double Clamp(double f, double sr) => Math.Clamp(f, 5.0, sr * 0.49);
}

/// <summary>A decaying modal resonator: a two-pole filter whose impulse response is an
/// exponentially decaying sinusoid. Excite it with a click and it rings — the model
/// behind the analog kick's bridged-T, the snare's shell tones and every metal voice.</summary>
internal sealed class Modal
{
    private readonly double _a1, _a2, _gain;
    private double _y1, _y2;

    /// <param name="decay">Time to -60 dB, in seconds.</param>
    public Modal(double freq, double decay, double sr)
    {
        double r = Math.Exp(-6.907755 / Math.Max(1e-4, decay) / sr);
        double w = 2 * Math.PI * Math.Clamp(freq, 1.0, sr * 0.48) / sr;
        _a1 = 2 * r * Math.Cos(w);
        _a2 = -r * r;
        // Normalize for an impulse: the two-pole response peaks at 1/sin(w), so sin(w)
        // brings every mode to unity whatever its frequency or ring time. (Scaling by
        // (1-r) as well would be the steady-state normalization, and would bury the
        // low, long-ringing modes — the body of the drum — under everything else.)
        _gain = Math.Sin(w);
    }

    public double Process(double x)
    {
        double y = _gain * x + _a1 * _y1 + _a2 * _y2;
        _y2 = _y1; _y1 = y;
        return y;
    }
}

/// <summary>Subsonic cleanup. Asymmetric saturation rectifies part of the signal into a
/// slow, envelope-shaped offset, which lands below 20 Hz: inaudible, but it eats headroom
/// and makes the normalizer under-level the hit. A second-order 18 Hz high-pass removes
/// it and still leaves the deepest sub voices (36 Hz) untouched.</summary>
internal sealed class DcBlock
{
    private readonly Biquad _hp;
    public DcBlock(double sr) => _hp = Biquad.HighPass(18, 0.7071, sr);
    public double Process(double x) => _hp.Process(x);
}

internal static class KitDsp
{
    /// <summary>Per-sample coefficient of an exponential decay reaching -60 dB in
    /// <paramref name="seconds"/>.</summary>
    public static double DecayCoef(double seconds, double sr)
        => Math.Exp(-6.907755 / (Math.Max(1e-4, seconds) * sr));

    public static double Db(double db) => Math.Pow(10, db / 20);

    /// <summary>Asymmetric soft saturation. The cubic term adds the even harmonics a
    /// symmetric tanh cannot produce — that second harmonic is most of what "warm" means
    /// here; <paramref name="drive"/> 0 leaves the signal untouched.</summary>
    public static double Saturate(double x, double drive, double asym)
    {
        if (drive <= 1e-6) return x;
        double g = 1 + drive * 6;
        double v = x * g;
        v += asym * drive * v * v * 0.35;          // even-order bias
        double y = Math.Tanh(v);
        return y / (1 + drive * 2.2);              // keep the level roughly constant
    }

    /// <summary>Bit + sample-rate reduction, for the lo-fi kits. <paramref name="bits"/>
    /// under 24 quantizes; <paramref name="hold"/> is the sample-and-hold period in frames.</summary>
    public static void Crush(double[] buf, double bits, int hold)
    {
        if (bits < 24)
        {
            double steps = Math.Pow(2, Math.Max(2, bits) - 1);
            for (int i = 0; i < buf.Length; i++) buf[i] = Math.Round(buf[i] * steps) / steps;
        }
        if (hold > 1)
        {
            double held = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                if (i % hold == 0) held = buf[i];
                buf[i] = held;
            }
        }
    }

    /// <summary>Zero-phase filtering (forward then reverse). Used for the decimation
    /// low-pass so the transient keeps its shape instead of being smeared by phase.</summary>
    public static void FiltFilt(double[] buf, Func<Biquad> make)
    {
        var f = make();
        for (int i = 0; i < buf.Length; i++) buf[i] = f.Process(buf[i]);
        var b = make();
        for (int i = buf.Length - 1; i >= 0; i--) buf[i] = b.Process(buf[i]);
    }

    /// <summary>Decimates an oversampled buffer by <paramref name="factor"/> after a
    /// zero-phase 4th-order low-pass just under the target Nyquist.</summary>
    public static double[] Decimate(double[] src, int factor, double targetSr)
    {
        double osSr = targetSr * factor;
        double cut = targetSr * 0.455;
        KitDsp.FiltFilt(src, () => Biquad.LowPass(cut, 0.7071, osSr));
        KitDsp.FiltFilt(src, () => Biquad.LowPass(cut, 0.7071, osSr));
        var dst = new double[src.Length / factor];
        for (int i = 0; i < dst.Length; i++) dst[i] = src[i * factor];
        return dst;
    }

    /// <summary>Peak level of a buffer.</summary>
    public static double Peak(double[] buf)
    {
        double p = 0;
        foreach (double v in buf) { double a = Math.Abs(v); if (a > p) p = a; }
        return p;
    }

    public static void Scale(double[] buf, double g)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] *= g;
    }

    /// <summary>Trims the decayed tail (everything after the last frame above
    /// <paramref name="floorDb"/>) and fades the new end over ~6 ms so the file ends
    /// silently instead of on a click. Returns the kept length in frames.</summary>
    public static int TrimTail(double[][] chans, double floorDb, double sr)
    {
        int n = chans[0].Length;
        double floor = Db(floorDb);
        int last = 0;
        for (int i = 0; i < n; i++)
            foreach (var c in chans)
                if (Math.Abs(c[i]) > floor) { last = i; break; }
        // 8 ms of air after the last audible frame, then the fade.
        int keep = Math.Min(n, last + (int)(sr * 0.008) + 2);
        int fade = Math.Min(keep, (int)(sr * 0.006));
        foreach (var c in chans)
            for (int i = 0; i < fade; i++)
            {
                int idx = keep - fade + i;
                double t = 1.0 - (double)i / fade;
                c[idx] *= t * t;
            }
        return Math.Max(1, keep);
    }
}

/// <summary>A small stereo room: four parallel combs into two allpasses per side, with
/// slightly different delays left and right. Not a showcase reverb — just the early
/// energy that stops an acoustic-style hit from sounding like it was recorded in a
/// vacuum, and the width source for the ambient kits.</summary>
internal sealed class Room
{
    private readonly double[][] _comb = new double[8][];
    private readonly int[] _combPos = new int[8];
    private readonly double[] _combFb = new double[8];
    private readonly double[] _combLp = new double[8];
    private readonly double[][] _ap = new double[4][];
    private readonly int[] _apPos = new int[4];
    private readonly double _damp;

    /// <param name="size">0..1 — small booth to large room.</param>
    /// <param name="decay">0..1 — tail length, mapped to an RT60 of 0.15 s .. 2.5 s.</param>
    /// <param name="damp">0..1 — high-frequency absorption in the tail.</param>
    public Room(double size, double decay, double damp, double sr)
    {
        _damp = Math.Clamp(damp, 0, 0.95);
        // Mutually prime-ish comb delays; the right channel is stretched ~1.6 % for width.
        double[] baseMs = { 29.7, 37.1, 41.1, 43.7 };
        double scale = 0.45 + size * 1.25;
        // Each comb's feedback is derived from its own delay so they all reach -60 dB at
        // the same time: a single shared feedback value would make the long combs ring far
        // longer than the short ones, and "decay" would barely change the tail at all.
        double rt60 = 0.15 + Math.Clamp(decay, 0, 1) * 2.35;
        for (int i = 0; i < 8; i++)
        {
            double ms = baseMs[i % 4] * scale * (i < 4 ? 1.0 : 1.016);
            _comb[i] = new double[Math.Max(4, (int)(ms * 0.001 * sr))];
            _combFb[i] = Math.Clamp(Math.Pow(10, -3.0 * (ms * 0.001) / rt60), 0.0, 0.93);
        }
        double[] apMs = { 5.0, 1.7 };
        for (int i = 0; i < 4; i++)
        {
            double ms = apMs[i % 2] * (i < 2 ? 1.0 : 1.11);
            _ap[i] = new double[Math.Max(4, (int)(ms * 0.001 * sr))];
        }
    }

    public void Process(double x, out double l, out double r)
    {
        l = Tank(x, 0); r = Tank(x, 4);
    }

    private double Tank(double x, int off)
    {
        double sum = 0;
        for (int i = off; i < off + 4; i++)
        {
            double y = _comb[i][_combPos[i]];
            _combLp[i] = y * (1 - _damp) + _combLp[i] * _damp;
            _comb[i][_combPos[i]] = x + _combLp[i] * _combFb[i];
            if (++_combPos[i] >= _comb[i].Length) _combPos[i] = 0;
            sum += y;
        }
        sum *= 0.25;
        int a0 = off == 0 ? 0 : 2;
        for (int i = a0; i < a0 + 2; i++)
        {
            double d = _ap[i][_apPos[i]];
            double y = -sum + d;
            _ap[i][_apPos[i]] = sum + d * 0.5;
            if (++_apPos[i] >= _ap[i].Length) _apPos[i] = 0;
            sum = y;
        }
        return sum;
    }
}
