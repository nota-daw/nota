// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The pictures device pages are built from (specs/113): each draws one kind of view the desktop
// cards show — a filter response, an envelope, an oscillator, an LFO, an EQ curve, a dynamics
// transfer curve, a waveshaper, a reverb tail, delay taps, a phaser's notches, a keyboard, bars,
// meters — simplified to a 128 × 48 dot matrix. Brightness carries the hierarchy, as ink weight
// does on the cards: 15 for the set value (the line you change), 6–10 for secondary lines and
// markers, 2–4 for fills, grids and live data under the line. Everything draws inside the graph
// area (LedFrame.Top..Bottom) unless a rectangle is given.

namespace Nota.Application.Displays;

public static class Widgets
{
    public const int L0 = 0, Grid = 1, Fill = 2, Live = 3, Dim = 5, Mid = 8, Mark = 10, Hot = 15;
    public const double FMin = 20, FMax = 20000;

    // ---- axes -------------------------------------------------------------------------------

    public static double XOfHz(double hz, int x0 = 0, int x1 = LedFrame.W - 1)
        => x0 + Math.Log(Math.Clamp(hz, FMin, FMax) / FMin) / Math.Log(FMax / FMin) * (x1 - x0);

    public static double HzOfX(double x, int x0 = 0, int x1 = LedFrame.W - 1)
        => FMin * Math.Pow(FMax / FMin, (x - x0) / Math.Max(1.0, x1 - x0));

    /// <summary>dB mapped into the rows <paramref name="top"/>..<paramref name="bottom"/> (hi at top).</summary>
    public static double YOfDb(double db, double hi, double lo, int top = LedFrame.Top, int bottom = LedFrame.Bottom)
        => top + (hi - db) / (hi - lo) * (bottom - top);

    /// <summary>Frequency ticks at 100 Hz, 1 kHz and 10 kHz on the bottom row.</summary>
    public static void FreqTicks(LedFrame f, int y = LedFrame.Bottom)
    {
        foreach (double hz in new[] { 100.0, 1000, 10000 }) f.VLine(XOfHz(hz), y - 1, y, 4);
    }

    // ---- filters ------------------------------------------------------------------------------

    public enum FilterType { LowPass, HighPass, BandPass, Notch, Peak, LowShelf, HighShelf, AllPass }

    /// <summary>The magnitude (linear) of a 2-pole filter at <paramref name="hz"/>; <paramref name="poles"/>
    /// 4 squares it (24 dB/oct). <paramref name="q"/> ≥ 0.3.</summary>
    public static double FilterMag(FilterType t, double hz, double fc, double q, int poles = 2, double gainDb = 0)
    {
        double r = hz / Math.Max(1, fc);
        q = Math.Max(0.3, q);
        double den = Math.Sqrt((1 - r * r) * (1 - r * r) + (r / q) * (r / q));
        double m = t switch
        {
            FilterType.LowPass => 1 / den,
            FilterType.HighPass => r * r / den,
            FilterType.BandPass => r / q / den,
            FilterType.Notch => Math.Abs(1 - r * r) / den,
            FilterType.AllPass => 1,
            FilterType.Peak => PeakMag(r, q, gainDb),
            FilterType.LowShelf => ShelfMag(1 / r, gainDb),
            FilterType.HighShelf => ShelfMag(r, gainDb),
            _ => 1,
        };
        if (poles >= 4 && t is FilterType.LowPass or FilterType.HighPass or FilterType.BandPass) m *= m;
        return m;
    }

    private static double PeakMag(double r, double q, double gainDb)
    {
        // A bell: gain at r = 1, falling off with bandwidth 1/q on a log axis.
        double oct = Math.Log2(Math.Max(1e-6, r));
        double bw = 1.0 / Math.Max(0.1, q);
        double k = Math.Exp(-(oct * oct) / (2 * bw * bw * 0.36));
        return Math.Pow(10, gainDb * k / 20);
    }

    private static double ShelfMag(double r, double gainDb)
    {
        double k = 1 / (1 + Math.Pow(Math.Max(1e-6, r), -2.2));   // 0 below the corner, 1 above
        return Math.Pow(10, gainDb * k / 20);
    }

    public static FilterType FilterTypeFromName(string text) => text.ToLowerInvariant() switch
    {
        var s when s.Contains("notch") || s.Contains("band reject") || s.Contains("br") && s.Length <= 3 => FilterType.Notch,
        var s when s.StartsWith("hp") || s.Contains("high") && s.Contains("pass") || s.Contains("highpass") => FilterType.HighPass,
        var s when s.StartsWith("bp") || s.Contains("band") => FilterType.BandPass,
        _ => FilterType.LowPass,
    };

    /// <summary>A filter's response over a spectrum: the live bands (dim bars), the response curve
    /// on +18..−42 dB with a fill, and a dotted line at the cutoff.</summary>
    public static void FilterResponse(LedFrame f, double cutoffHz, double reso01, FilterType type = FilterType.LowPass,
        int poles = 2, double[]? spectrum = null, double drive01 = 0)
    {
        double q = 0.5 + Math.Clamp(reso01, 0, 1) * 11;
        const double hi = 18, lo = -42;
        f.HLine(0, 127, Math.Round(YOfDb(0, hi, lo)), Grid, dotted: true);
        FreqTicks(f);
        if (spectrum is not null) SpectrumBars(f, spectrum, hz => FilterMag(type, hz, cutoffHz, q, poles), Live);
        double boost = 1 + Math.Clamp(drive01, 0, 1) * 0.5;
        f.Curve(x => YOfDb(20 * Math.Log10(Math.Max(1e-5, FilterMag(type, HzOfX(x), cutoffHz, q, poles) * boost)), hi, lo), 0, 127, Hot, Fill);
        f.VLine(XOfHz(cutoffHz), LedFrame.Top, LedFrame.Bottom, Dim, dotted: true);
    }

    /// <summary>A spectrum as dim bars from the bottom, every other column, optionally shaped by a
    /// response (so it sits under a filter curve). Bands are dB, −90..0 maps to the graph height.</summary>
    public static void SpectrumBars(LedFrame f, double[] bands, Func<double, double>? shape = null, int level = Live,
        int x0 = 0, int x1 = LedFrame.W - 1, int top = LedFrame.Top, int bottom = LedFrame.Bottom)
    {
        if (bands.Length == 0) return;
        for (int x = x0; x <= x1; x += 2)
        {
            int b = Math.Clamp((int)((x - x0) / (double)(x1 - x0 + 1) * bands.Length), 0, bands.Length - 1);
            double db = bands[b];
            if (shape is not null) db += 20 * Math.Log10(Math.Max(1e-5, shape(HzOfX(x, x0, x1))));
            if (db <= -89) continue;
            double y = top + (0 - Math.Clamp(db, -90, 0)) / 90 * (bottom - top);
            f.VLine(x, y, bottom, level);
        }
    }

    // ---- envelopes ----------------------------------------------------------------------------

    /// <summary>The x of each ADSR corner for times in seconds, sharing the width log-fashion so a
    /// 2 ms attack and a 4 s release both read.</summary>
    private static double Seg(double sec) => 3 + 27 * Math.Clamp(Math.Log10(1 + Math.Max(0, sec) * 1000) / 4, 0, 1);

    /// <summary>An ADSR (times in s, sustain 0..1) across the graph, filled; a playhead dot loops
    /// through it when <paramref name="time"/> ≥ 0. Returns the corners' x.</summary>
    public static void Adsr(LedFrame f, double a, double d, double s, double r, int level = Hot, int fill = Fill,
        double time = -1, int x0 = 0, int x1 = LedFrame.W - 1, int top = LedFrame.Top + 1, int bottom = LedFrame.Bottom, bool dotted = false)
    {
        double wa = Seg(a), wd = Seg(d), wr = Seg(r), width = x1 - x0;
        double hold = Math.Max(8, width - wa - wd - wr);
        double k = width / (wa + wd + hold + wr);
        wa *= k; wd *= k; hold *= k; wr *= k;
        s = Math.Clamp(s, 0, 1);
        double Y(double v) => bottom - v * (bottom - top);
        var pts = new (double X, double Y)[]
        {
            (x0, Y(0)), (x0 + wa, Y(1)), (x0 + wa + wd, Y(s)), (x0 + wa + wd + hold, Y(s)), (x1, Y(0)),
        };
        if (fill > 0)
            for (int x = x0; x <= x1; x++)
            {
                double y = Interp(pts, x);
                for (int j = (int)Math.Round(y) + 1; j <= bottom; j++) f.Dot(x, j, fill);
            }
        for (int i = 0; i < pts.Length - 1; i++) f.Line(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, level, dotted);
        if (time >= 0)
        {
            double px = x0 + (time % 2.4) / 2.4 * width;
            f.VLine(px, top, bottom, Dim);
            f.Rect(px - 1, Interp(pts, px) - 1, 3, 3, Hot);
        }
    }

    private static double Interp((double X, double Y)[] pts, double x)
    {
        for (int i = 0; i < pts.Length - 1; i++)
            if (x >= pts[i].X && x <= pts[i + 1].X)
                return pts[i + 1].X - pts[i].X < 1e-6 ? pts[i + 1].Y : pts[i].Y + (pts[i + 1].Y - pts[i].Y) * (x - pts[i].X) / (pts[i + 1].X - pts[i].X);
        return pts[^1].Y;
    }

    /// <summary>A plain decay (a struck sound): attack then an exponential fall over <paramref name="decay"/> s.</summary>
    public static void Decay(LedFrame f, double attack, double decay, int level = Hot, int fill = Fill, double windowSec = 0,
        int x0 = 0, int x1 = LedFrame.W - 1, int top = LedFrame.Top + 1, int bottom = LedFrame.Bottom)
    {
        double win = windowSec > 0 ? windowSec : Math.Max(0.2, (attack + decay) * 1.4);
        double Y(double v) => bottom - v * (bottom - top);
        f.Curve(x =>
        {
            double t = (x - x0) / (double)(x1 - x0) * win;
            double v = t < attack ? t / Math.Max(1e-4, attack) : Math.Exp(-(t - attack) * 6.9 / Math.Max(0.01, decay));
            return Y(v);
        }, x0, x1, level, fill, bottom);
    }

    // ---- oscillators and LFOs ------------------------------------------------------------------

    public enum Wave { Saw, Square, Triangle, Sine, Noise, Pulse, Ramp }

    public static double WaveAt(Wave w, double phase, double pw = 0.5)
    {
        double p = phase - Math.Floor(phase);
        return w switch
        {
            Wave.Saw => 2 * p - 1,
            Wave.Ramp => 1 - 2 * p,
            Wave.Square => p < 0.5 ? 1 : -1,
            Wave.Pulse => p < Math.Clamp(pw, 0.02, 0.98) ? 1 : -1,
            Wave.Triangle => 1 - 4 * Math.Abs(p - 0.5),
            Wave.Sine => Math.Sin(2 * Math.PI * p),
            _ => Hash(Math.Floor(phase * 16)) * 2 - 1,
        };
    }

    public static Wave WaveFromStep(int step, params Wave[] order) => order.Length == 0 ? Wave.Saw : order[Math.Clamp(step, 0, order.Length - 1)];

    /// <summary>A waveform: <paramref name="wave"/> (phase → −1..1) over <paramref name="cycles"/>
    /// cycles about the middle of the area, drifting slowly with <paramref name="time"/>.</summary>
    public static void Waveform(LedFrame f, Func<double, double> wave, double cycles = 2, int level = Hot, double time = 0,
        double amp = 1, int x0 = 0, int x1 = LedFrame.W - 1, int top = LedFrame.Top, int bottom = LedFrame.Bottom, bool zero = true)
    {
        double mid = (top + bottom) / 2.0, h = (bottom - top) / 2.0 - 1;
        if (zero) f.HLine(x0, x1, Math.Round(mid), Grid, dotted: true);
        f.Curve(x => mid - h * Math.Clamp(amp, 0, 1.2) * Math.Clamp(wave((x - x0) / (double)(x1 - x0 + 1) * cycles + time * 0.1), -1.1, 1.1), x0, x1, level);
    }

    /// <summary>An LFO scrolling from right to left over a two-second window at its rate, with
    /// its current value as a block at the right edge.</summary>
    public static void Lfo(LedFrame f, Wave shape, double rateHz, double depth01, double time, int level = Hot,
        int x0 = 0, int x1 = LedFrame.W - 1, int top = LedFrame.Top, int bottom = LedFrame.Bottom, double window = 2, bool zero = true)
    {
        double mid = (top + bottom) / 2.0, h = (bottom - top) / 2.0 - 1;
        if (zero) f.HLine(x0, x1, Math.Round(mid), Grid, dotted: true);
        double V(int x) => WaveAt(shape, rateHz * (time - (x1 - 3 - x) / (double)(x1 - x0) * window));
        f.Curve(x => mid - h * depth01 * V(x), x0, x1 - 3, level);
        f.Rect(x1 - 2, Math.Round(mid - h * depth01 * V(x1 - 3)) - 1, 3, 3, level);
    }

    // ---- EQ ------------------------------------------------------------------------------------

    public readonly record struct EqBand(FilterType Type, double Hz, double GainDb, double Q, bool On = true, int Poles = 2);

    /// <summary>The summed response of <paramref name="bands"/> on ±<paramref name="rangeDb"/>, with a
    /// 0 dB rule, each band's centre as a dot (the turning one bright) and a fill to 0 dB.</summary>
    public static void EqCurve(LedFrame f, IReadOnlyList<EqBand> bands, double rangeDb = 18, int highlight = -1, double[]? spectrum = null)
    {
        double Y(double db) => YOfDb(db, rangeDb, -rangeDb);
        int zero = (int)Math.Round(Y(0));
        f.HLine(0, 127, zero, Mid, dotted: true);
        f.HLine(0, 127, Math.Round(Y(rangeDb / 2)), Grid, dotted: true);
        f.HLine(0, 127, Math.Round(Y(-rangeDb / 2)), Grid, dotted: true);
        FreqTicks(f);
        if (spectrum is not null) SpectrumBars(f, spectrum, null, Grid + 1);
        double Total(double hz)
        {
            double db = 0;
            foreach (var b in bands)
                if (b.On) db += 20 * Math.Log10(Math.Max(1e-5, FilterMag(b.Type, hz, b.Hz, b.Q, b.Poles, b.GainDb)));
            return db;
        }
        for (int x = 0; x < 128; x++)
        {
            double y = Y(Math.Clamp(Total(HzOfX(x)), -rangeDb - 6, rangeDb + 6));
            f.VLine(x, Math.Min(y, zero), Math.Max(y, zero), Fill);
        }
        f.Curve(x => Y(Total(HzOfX(x))), 0, 127, Hot);
        for (int i = 0; i < bands.Count; i++)
        {
            // A band that is off shows where it would sit, faintly, as the card's dimmed dot does.
            var b = bands[i];
            double gy = b.Type is FilterType.Peak or FilterType.LowShelf or FilterType.HighShelf ? Y(Math.Clamp(b.GainDb, -rangeDb, rangeDb)) : Y(0);
            int lv = i == highlight ? Hot : b.On ? Mark : Grid + 2;
            if (b.On) f.Rect(XOfHz(b.Hz) - 1, gy - 1, 3, 3, lv);
            else f.Box(XOfHz(b.Hz) - 1, gy - 1, 3, 3, lv);
        }
    }

    // ---- dynamics and shapers -------------------------------------------------------------------

    /// <summary>A transfer curve, input dB (x) against output dB (y) over −60..0, with the unity
    /// diagonal dotted, the threshold marked and the live input as a dot on the curve. A gain
    /// reduction bar runs down the right edge.</summary>
    public static void Transfer(LedFrame f, Func<double, double> outOfIn, double thresholdDb, double inputDb, double grDb = 0,
        int x0 = 20, int x1 = 107, double lo = -60, double hi = 0)
    {
        int top = LedFrame.Top, bottom = LedFrame.Bottom;
        double X(double db) => x0 + (Math.Clamp(db, lo, hi) - lo) / (hi - lo) * (x1 - x0);
        double Y(double db) => bottom - (Math.Clamp(db, lo - 20, hi + 6) - lo) / (hi - lo) * (bottom - top);
        f.Line(X(lo), Y(lo), X(hi), Y(hi), Grid, dotted: true);
        f.Box(x0 - 1, top - 1, x1 - x0 + 3, bottom - top + 2, Grid);
        f.VLine(X(thresholdDb), top, bottom, Dim, dotted: true);
        f.Curve(x => Y(outOfIn(lo + (x - x0) / (double)(x1 - x0) * (hi - lo))), x0, x1, Hot);
        if (inputDb > lo) f.Rect(X(inputDb) - 1, Y(outOfIn(inputDb)) - 1, 3, 3, Hot);
        Meter(f, x1 + 8, top, 4, bottom - top + 1, Math.Clamp(grDb / 24, 0, 1), Mark, fromTop: true);
        Meter(f, x0 - 12, top, 4, bottom - top + 1, Math.Clamp((inputDb - lo) / (hi - lo), 0, 1), Mid);
    }

    /// <summary>A compressor's static curve: threshold, ratio (∞ above 30), knee width, makeup.</summary>
    public static Func<double, double> CompCurve(double thresh, double ratio, double kneeDb = 0, double makeup = 0, bool below = false) => x =>
    {
        ratio = Math.Max(1, ratio);
        double slope = ratio >= 30 ? 0 : 1 / ratio;
        double over = x - thresh, k = Math.Max(0, kneeDb);
        double y;
        if (below)
        {
            // An expander / gate: below the threshold the level falls faster.
            y = over >= 0 ? x : thresh + over * ratio;
        }
        else if (k > 0 && Math.Abs(over) <= k / 2)
            y = x + (slope - 1) * (over + k / 2) * (over + k / 2) / (2 * k);
        else
            y = over > 0 ? thresh + over * slope : x;
        return y + makeup;
    };

    /// <summary>A waveshaper: −1..1 in (x) against out (y) in a square on the left, and the shaped
    /// sine it makes on the right. <paramref name="input01"/> lights the level reached.</summary>
    public static void Shaper(LedFrame f, Func<double, double> shape, double input01 = 0, double time = 0)
    {
        int top = LedFrame.Top, bottom = LedFrame.Bottom, s = bottom - top;
        int x0 = 2, x1 = x0 + s;
        double mid = (top + bottom) / 2.0;
        f.Box(x0 - 1, top - 1, s + 3, s + 2, Grid);
        f.Line(x0, bottom, x1, top, Grid, dotted: true);
        f.Curve(x => mid - s / 2.0 * Math.Clamp(shape((x - x0) / (double)s * 2 - 1), -1.05, 1.05), x0, x1, Hot);
        if (input01 > 0.01)
        {
            double xi = x0 + (input01 + 1) / 2 * s;
            f.Rect(xi - 1, mid - s / 2.0 * shape(input01) - 1, 3, 3, Hot);
        }
        int w0 = x1 + 8, w1 = LedFrame.W - 1;
        f.HLine(w0, w1, Math.Round(mid), Grid, dotted: true);
        f.Curve(x => mid - s / 2.0 * Math.Clamp(shape(Math.Sin(2 * Math.PI * ((x - w0) / (double)(w1 - w0) * 2 + time * 0.05))), -1.05, 1.05), w0, w1, Mark);
    }

    // ---- time-based effects ---------------------------------------------------------------------

    /// <summary>A reverb's decay in dB over time: the line from the pre-delay at 0 dB to −60 dB at
    /// RT60, a fainter high-frequency line, early reflection ticks, and the measured input wash /
    /// tail (dB, one point per <paramref name="pointSec"/>) of the latest hit under it.</summary>
    public static void Tail(LedFrame f, double rt60, double preSec, double rtHigh, IReadOnlyList<double>? erLevels = null, bool frozen = false,
        ReadOnlySpan<float> inputDb = default, ReadOnlySpan<float> tailDb = default, double pointSec = 0.004)
    {
        rt60 = Math.Clamp(rt60, 0.05, 60);
        double[] windows = { 0.5, 1, 2, 3, 4, 6, 8, 12, 16, 30, 60 };
        double win = windows.FirstOrDefault(w => w >= preSec + rt60 * 1.15, 60);
        double X(double s) => s / win * 127;
        double Y(double db) => YOfDb(Math.Max(db, -72), 0, -72);
        foreach (double db in new[] { -24.0, -48 }) f.HLine(0, 127, Math.Round(Y(db)), Grid, dotted: true);
        for (double s = 1; s < win; s += win > 8 ? 4 : win > 3 ? 2 : 1) f.VLine(X(s), LedFrame.Bottom - 1, LedFrame.Bottom, 4);

        // The measured hit, if any: input wash and the tail as dots.
        if (inputDb.Length > 0 && tailDb.Length == inputDb.Length && LatestHit(inputDb) is int hit and >= 0)
        {
            for (int k = hit; k < inputDb.Length; k++)
            {
                double s = (k - hit) * pointSec;
                if (s > win) break;
                double x = X(s);
                if (inputDb[k] > -72) f.VLine(x, Y(inputDb[k] - inputDb[hit]), LedFrame.Bottom, Live - 1);
                if (tailDb[k] > -100) f.Dot(x, Y(tailDb[k] - Peak(tailDb, hit)), Dim);
            }
        }
        if (frozen)
        {
            f.HLine(0, 127, LedFrame.Top, Hot);
            f.Box(0, LedFrame.Top, 128, LedFrame.Bottom - LedFrame.Top + 1, Mid);
            return;
        }
        double x0 = X(preSec);
        f.Curve(x => x < x0 ? double.NaN : Y(-60 * (x / 127.0 * win - preSec) / rt60), (int)Math.Ceiling(x0), 127, Hot, Fill);
        f.Line(x0, Y(0), X(preSec + Math.Max(0.05, rtHigh) * 72 / 60), Y(-72), 7, dotted: true);
        f.VLine(x0, LedFrame.Top, LedFrame.Top + 3, Hot);
        if (erLevels is not null)
            for (int i = 0; i < erLevels.Count; i++)
            {
                int h = (int)Math.Round(Math.Clamp(erLevels[i], 0, 1) * 11);
                if (h > 0) f.VLine(x0 + 2 + i * 2, LedFrame.Top + 2, LedFrame.Top + 2 + h, Mark);
            }
    }

    private static double Peak(ReadOnlySpan<float> s, int from)
    {
        double p = -120;
        for (int i = from; i < Math.Min(s.Length, from + 64); i++) p = Math.Max(p, s[i]);
        return p;
    }

    /// <summary>The latest hit in an input-level history (dB): a 9 dB jump within 12 ms above −50 dB,
    /// walked back to the start of its rise (as the Reverb card finds it); −1 for none.</summary>
    public static int LatestHit(ReadOnlySpan<float> s)
    {
        for (int k = s.Length - 1; k >= 3; k--)
        {
            float v = s[k];
            if (v > -50 && v - s[k - 3] > 9 && v >= s[k - 1])
            {
                while (k > 3 && s[k - 1] - s[k - 4] > 9) k--;
                return k;
            }
        }
        return -1;
    }

    /// <summary>Delay taps: echoes of a hit at multiples of the left (up) and right (down) times,
    /// each smaller by the feedback, over a window that shows a few of them.</summary>
    public static void Taps(LedFrame f, double leftSec, double rightSec, double feedback01, double time = -1, double filter01 = 1)
    {
        leftSec = Math.Clamp(leftSec, 0.005, 8); rightSec = Math.Clamp(rightSec, 0.005, 8);
        double win = Math.Max(leftSec, rightSec) * 5.2;
        int mid = (LedFrame.Top + LedFrame.Bottom) / 2;
        double X(double s) => 2 + s / win * 124;
        f.HLine(0, 127, mid, Grid, dotted: true);
        f.VLine(X(0), mid - 18, mid + 18, Hot);
        double fb = Math.Clamp(feedback01, 0, 0.99);
        for (int side = 0; side < 2; side++)
        {
            double t = side == 0 ? leftSec : rightSec, a = 1;
            for (int n = 1; n < 64; n++)
            {
                double at = t * n;
                if (at > win) break;
                a = n == 1 ? 1 : a * fb * (0.75 + 0.25 * filter01);
                if (a < 0.03) break;
                int h = (int)Math.Round(a * 17);
                if (side == 0) f.VLine(X(at), mid - h, mid - 1, n == 1 ? Hot : Mark);
                else f.VLine(X(at), mid + 1, mid + h, n == 1 ? Hot : Mark);
            }
        }
        if (time >= 0)
        {
            double px = X(time % win);
            f.VLine(px, LedFrame.Top, LedFrame.Bottom, Grid + 1, dotted: true);
        }
    }

    /// <summary>A modulated delay (chorus, flanger): the delay time over the last two seconds as a
    /// band whose width is the depth, centred on the base delay.</summary>
    public static void Sweep(LedFrame f, double rateHz, double depth01, double center01, double time, Wave shape = Wave.Sine, int voices = 1)
    {
        int top = LedFrame.Top, bottom = LedFrame.Bottom;
        double mid = bottom - Math.Clamp(center01, 0, 1) * (bottom - top - 8) - 4, h = (bottom - top) / 2.0 * Math.Clamp(depth01, 0, 1);
        f.HLine(0, 127, Math.Round(mid), Grid, dotted: true);
        for (int v = 0; v < Math.Max(1, voices); v++)
        {
            double ph = v / (double)Math.Max(1, voices);
            int lv = v == 0 ? Hot : Dim;
            f.Curve(x => mid - h * WaveAt(shape, rateHz * (time - (124 - x) / 124.0 * 2) + ph), 0, 124, lv);
        }
        f.Rect(125, Math.Round(mid - h * WaveAt(shape, rateHz * time)) - 1, 3, 3, Hot);
    }

    /// <summary>A phaser's notches: <paramref name="stages"/>/2 dips across the spectrum, swept
    /// around <paramref name="centerHz"/> by the LFO.</summary>
    public static void Notches(LedFrame f, double centerHz, double depth01, int stages, double rateHz, double feedback01, double time)
    {
        double sweep = Math.Pow(2, Math.Clamp(depth01, 0, 1) * 2 * Math.Sin(2 * Math.PI * rateHz * time));
        double fc = Math.Clamp(centerHz * sweep, 40, 16000);
        int notches = Math.Clamp(stages / 2, 1, 8);
        const double hi = 6, lo = -30;
        FreqTicks(f);
        f.HLine(0, 127, Math.Round(YOfDb(0, hi, lo)), Grid, dotted: true);
        double q = 0.6 + Math.Clamp(feedback01, 0, 1) * 4;
        f.Curve(x =>
        {
            double hz = HzOfX(x), db = 0;
            for (int n = 0; n < notches; n++)
            {
                double c = fc * Math.Pow(2.2, n - (notches - 1) / 2.0);
                db += 20 * Math.Log10(Math.Max(0.03, FilterMag(FilterType.Notch, hz, c, q)));
            }
            return YOfDb(db / Math.Max(1, notches * 0.6), hi, lo);
        }, 0, 127, Hot, Fill);
        f.VLine(XOfHz(fc), LedFrame.Top, LedFrame.Bottom, Dim, dotted: true);
    }

    // ---- notes ------------------------------------------------------------------------------

    private static readonly bool[] Black = { false, true, false, true, false, false, true, false, true, false, true, false };

    /// <summary>A keyboard of <paramref name="octaves"/> octaves from <paramref name="lowNote"/>
    /// (a C), with <paramref name="lit"/> notes bright and <paramref name="dim"/> notes faint.</summary>
    public static void Keyboard(LedFrame f, int lowNote, int octaves, ISet<int>? lit, ISet<int>? dim = null,
        int x0 = 0, int x1 = LedFrame.W - 1, int top = LedFrame.Top + 18, int bottom = LedFrame.Bottom)
    {
        int whites = octaves * 7;
        double ww = (x1 - x0 + 1) / (double)whites;
        int wi = 0;
        for (int n = lowNote; n < lowNote + octaves * 12; n++)
        {
            int pc = ((n % 12) + 12) % 12;
            if (Black[pc]) continue;
            double x = x0 + wi * ww;
            int lv = lit?.Contains(n) == true ? Hot : dim?.Contains(n) == true ? Dim : Grid + 1;
            f.Rect(x, top, Math.Max(1, ww - 1), bottom - top + 1, lv);
            wi++;
        }
        wi = 0;
        for (int n = lowNote; n < lowNote + octaves * 12; n++)
        {
            int pc = ((n % 12) + 12) % 12;
            if (!Black[pc]) { wi++; continue; }
            double x = x0 + wi * ww - ww * 0.3;
            int lv = lit?.Contains(n) == true ? Hot : dim?.Contains(n) == true ? Mid : 0;
            for (int j = top; j < top + (bottom - top) * 0.6; j++)
                for (int i = (int)Math.Round(x); i < (int)Math.Round(x + ww * 0.6); i++) f.Put(i, j, lv);
        }
    }

    public static string NoteName(int note)
    {
        if (note < 0) return "-";
        string[] n = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return n[note % 12] + (note / 12 - 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A step pattern: <paramref name="steps"/> cells, each lit to its value (0..1), the
    /// step at <paramref name="current"/> brightest.</summary>
    public static void Steps(LedFrame f, IReadOnlyList<double> values, int current = -1, int top = LedFrame.Top + 2, int bottom = LedFrame.Bottom)
    {
        int n = Math.Max(1, values.Count);
        double w = 128.0 / n;
        for (int i = 0; i < n; i++)
        {
            double v = Math.Clamp(values[i], 0, 1);
            int h = (int)Math.Round(v * (bottom - top));
            int x = (int)Math.Round(i * w);
            int cw = Math.Max(1, (int)Math.Round(w) - 1);
            f.Rect(x, bottom - Math.Max(1, h) + 1, cw, Math.Max(1, h), i == current ? Hot : v > 0 ? Mid : Grid);
        }
    }

    // ---- values -------------------------------------------------------------------------------

    /// <summary>Eight (or <paramref name="values"/>.Count) vertical bars with their numbers under
    /// them; <paramref name="highlight"/> brightest; null values are empty slots.</summary>
    public static void Bars(LedFrame f, IReadOnlyList<double?> values, int highlight = -1, IReadOnlyList<string>? labels = null, bool bipolar = false)
    {
        int n = Math.Max(1, values.Count);
        double cw = 128.0 / n;
        int top = LedFrame.Top + 1, bottom = LedFrame.Bottom - 7, h = bottom - top + 1;
        int zero = bipolar ? (top + bottom) / 2 : bottom;
        for (int i = 0; i < n; i++)
        {
            int x = (int)Math.Round(i * cw) + 2, w = Math.Max(2, (int)Math.Round(cw) - 4);
            string label = labels is not null && i < labels.Count ? labels[i] : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            label = LedFrame.Fit(label, w + 2);
            f.Text(label, x + Math.Max(0, (w - LedFrame.TextWidth(label)) / 2), LedFrame.Bottom - 4, i == highlight ? Hot : Dim);
            if (values[i] is not { } v) { f.HLine(x, x + w - 1, bottom, Grid); continue; }
            f.Rect(x, top, w, h, Grid);
            int lv = i == highlight ? Hot : Mark;
            if (bipolar)
            {
                double y = zero - (v * 2 - 1) * (h / 2.0);
                f.Rect(x, Math.Min(y, zero), w, Math.Abs(y - zero) + 1, lv);
            }
            else
            {
                int bh = (int)Math.Round(Math.Clamp(v, 0, 1) * h);
                if (bh > 0) f.Rect(x, bottom - bh + 1, w, bh, lv);
            }
        }
    }

    /// <summary>A meter bar filled to <paramref name="v"/> (0..1), from the bottom (or the top).</summary>
    public static void Meter(LedFrame f, int x, int y, int w, int h, double v, int level = Mark, bool fromTop = false)
    {
        f.Rect(x, y, w, h, Grid);
        int fh = (int)Math.Round(Math.Clamp(v, 0, 1) * h);
        if (fh <= 0) return;
        if (fromTop) f.Rect(x, y, w, fh, level);
        else f.Rect(x, y + h - fh, w, fh, level);
    }

    /// <summary>A horizontal bar across the width with a label on its left.</summary>
    public static void HBar(LedFrame f, string label, int y, double v, int level = Mark, bool bipolar = false)
    {
        f.Text(label, 0, y, Dim);
        int x0 = 34, x1 = 127;
        f.Rect(x0, y, x1 - x0 + 1, 5, Grid);
        if (bipolar)
        {
            double c = (x0 + x1) / 2.0, e = c + (Math.Clamp(v, 0, 1) * 2 - 1) * (x1 - x0) / 2.0;
            f.Rect(Math.Min(c, e), y, Math.Abs(e - c) + 1, 5, level);
            f.VLine(c, y - 1, y + 5, Mid);
        }
        else f.Rect(x0, y, Math.Round(Math.Clamp(v, 0, 1) * (x1 - x0 + 1)), 5, level);
    }

    /// <summary>A rolling level history (the track's output), newest at the right.</summary>
    public static void History(LedFrame f, IReadOnlyList<double> levels01, int level = Live, int top = LedFrame.Top, int bottom = LedFrame.Bottom)
    {
        int n = levels01.Count;
        for (int i = 0; i < Math.Min(n, 128); i++)
        {
            double v = levels01[n - 1 - i];
            if (v <= 0.01) continue;
            int x = 127 - i;
            double y = bottom - v * (bottom - top);
            f.VLine(x, y, bottom, level);
        }
    }

    /// <summary>An XY pad: a frame, crosshair rules and the point.</summary>
    public static void Xy(LedFrame f, double x01, double y01, int x0, int top, int size, int level = Hot)
    {
        f.Box(x0, top, size, size, Mid);
        double px = x0 + 1 + Math.Clamp(x01, 0, 1) * (size - 3), py = top + size - 2 - Math.Clamp(y01, 0, 1) * (size - 3);
        f.VLine(px, top + 1, top + size - 2, Grid, dotted: true);
        f.HLine(x0 + 1, x0 + size - 2, py, Grid, dotted: true);
        f.Rect(px - 1, py - 1, 3, 3, level);
    }

    /// <summary>Text twice the size (6 × 10 per glyph, 8 dots apart).</summary>
    public static int BigText(LedFrame f, string s, int x, int y, int level)
    {
        var tmp = new LedFrame();
        tmp.Text(s, 0, 0, Hot);
        int w = LedFrame.TextWidth(s);
        for (int j = 0; j < 5; j++)
            for (int i = 0; i < w; i++)
                if (tmp[i, j] > 0) f.Rect(x + i * 2, y + j * 2, 2, 2, level);
        return x + (w + 1) * 2;
    }

    public static int BigTextWidth(string s) => LedFrame.TextWidth(s) * 2;

    /// <summary>A stereo field: the balance dot and the width as a span around it.</summary>
    public static void Stereo(LedFrame f, double balance, double width01, double level01, int y = LedFrame.Top + 14)
    {
        int c = 64;
        f.HLine(4, 123, y, Grid, dotted: true);
        f.Text("L", 0, y - 2, Dim);
        f.Text("R", 125, y - 2, Dim);
        double bx = c + Math.Clamp(balance, -1, 1) * 56, half = Math.Clamp(width01, 0, 2) * 28;
        f.Rect(bx - half, y - 6, Math.Max(1, half * 2), 13, Fill + (int)Math.Round(level01 * 4));
        f.VLine(bx, y - 8, y + 8, Hot);
    }

    internal static double Hash(double v)
    {
        double s = Math.Sin(v * 12.9898 + 78.233) * 43758.5453;
        return s - Math.Floor(s);
    }
}
