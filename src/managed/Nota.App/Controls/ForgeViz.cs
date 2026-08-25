// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Forge (device kind 17) visualisations, mockup 3m:
//  · ForgeTransferCurve — the multi-stage saturation transfer function. Two curves: the
//    static shape in brass, and the LFO→drive-modulated shape in teal dashes (it breathes
//    with the live LFO published by the DSP), so you see the range you sweep through.
//  · ForgeHarmonics — a bar chart of the harmonic spectrum produced by the current chain
//    (a test sine run through the replicated shapers, then FFT): odd partials full brass,
//    even ones dimmed, with a THD % / odd-vs-even readout.
// Both replicate Forge.h's shape()/driveGain() exactly so the picture matches the sound.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

// Shared param snapshot + shaper maths, read straight off the device params.
internal static class ForgeMath
{
    public const int Amount = 0, Tone = 1, Wet = 2, Output = 3, Bias = 4, Width = 5, Routing = 6,
                     LfoDrive = 7, EnvTone = 8, LfoRate = 9, LfoSync = 10,
                     S1Type = 11, S1Drive = 12, S1Out = 13, S1FB = 14, S1On = 15;
    public const int Stages = 3, Algos = 6;
    public static readonly string[] AlgoNames = { "Tube", "Diode", "Tape", "Fuzz", "Digital", "Fold" };

    public static float Shape(int type, float x, float bias)
    {
        float b = bias * 0.6f;
        return type switch
        {
            0 => MathF.Tanh(x + b) - MathF.Tanh(b),
            1 => (x + b) >= 0 ? MathF.Tanh((x + b) * 1.3f) : MathF.Tanh((x + b) * 0.55f),
            2 => MathF.Tanh(x + b * 0.4f),
            3 => MathF.Tanh((x + b) / (1 + MathF.Abs(x + b)) * 3f),
            4 => Math.Clamp(x + b, -1f, 1f),
            _ => MathF.Sin((x + b) * 0.9f),
        };
    }
    public static float DriveGain(float d) => 1 + d * d * 20f;

    // The full enabled-stage chain applied to one input sample (serial approximation for
    // the viz — feedback and inter-stage filters are omitted, the shape is what matters).
    public static float Chain(IAudioEngine e, int t, int d, float xin, float driveMod)
    {
        float P(int p) => e.DeviceGetParam(t, d, p);
        float amt = MathF.Pow(10, P(Amount) * 30f / 20f);
        float outGain = MathF.Pow(10, (P(Output) - 0.5f) * 48f / 20f);
        float bias = (P(Bias) - 0.5f) * 2f;
        float v = xin * amt;
        for (int s = 0; s < Stages; s++)
        {
            if (P(S1On + s * 5) < 0.5f) continue;
            int type = Math.Clamp((int)MathF.Round(P(S1Type + s * 5) * (Algos - 1)), 0, Algos - 1);
            float g = DriveGain(Math.Clamp(P(S1Drive + s * 5) + driveMod, 0f, 1f));
            float trim = MathF.Pow(10, (P(S1Out + s * 5) - 0.5f) * 24f / 20f);
            float y = Shape(type, v * g, bias) / MathF.Sqrt(g) * trim;
            v = y;
        }
        return Math.Clamp(v * outGain, -2f, 2f);
    }
}

internal sealed class ForgeTransferCurve : Control
{
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush BorderB = NotaPalette.BorderDefault;
    private static readonly IPen Grid = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x1E, 0x1C, 0x18)), 1);
    private static readonly IPen Diag = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x26, 0x23, 0x1E)), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
    private static readonly IPen CurvePen = new Pen(NotaPalette.Accent, 1.8);
    private static readonly IBrush CurveFill = new SolidColorBrush(Color.FromArgb(0x14, 0xD8, 0xA0, 0x3D));
    private static readonly IPen ModPen = new Pen(NotaPalette.Teal, 1.3) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
    private static readonly IBrush AxisB = NotaPalette.TextTertiary;
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly Typeface Face = new(FontFamily.Default);

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;
    private float _lfoLive = 0.5f;

    public ForgeTransferCurve(IAudioEngine engine, int track, int device)
    {
        _engine = engine; _track = track; _device = device;
        MinHeight = 60; ClipToBounds = true;
    }

    public void Tick() { _lfoLive = Math.Clamp(_engine.DeviceGainReduction(_track, _device), 0, 1); InvalidateVisual(); }
    public void Sync() => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Bg, new Pen(BorderB, 1), new Rect(0, 0, w, h), 6, 6);
        double pad = 6, gx = pad, gy = 14, gw = w - pad * 2, gh = h - gy - 12;
        if (gw <= 0 || gh <= 0) return;

        // grid + identity diagonal
        ctx.DrawLine(Grid, new Point(gx, gy + gh / 2), new Point(gx + gw, gy + gh / 2));
        ctx.DrawLine(Grid, new Point(gx + gw / 2, gy), new Point(gx + gw / 2, gy + gh));
        ctx.DrawLine(Diag, new Point(gx, gy + gh), new Point(gx + gw, gy));

        Point Map(double xin, double yout) => new Point(gx + (xin * 0.5 + 0.5) * gw, gy + (1 - (yout * 0.5 + 0.5)) * gh);

        void Draw(float driveMod, IPen pen, bool fill)
        {
            int n = (int)Math.Clamp(gw, 48, 260);
            var pts = new Point[n];
            for (int i = 0; i < n; i++)
            {
                double xin = -1.0 + 2.0 * i / (n - 1);
                double y = ForgeMath.Chain(_engine, _track, _device, (float)xin, driveMod);
                pts[i] = Map(xin, Math.Clamp(y, -1, 1));
            }
            if (fill)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(gx, gy + gh), true);
                    foreach (var p in pts) g.LineTo(p);
                    g.LineTo(new Point(gx + gw, gy + gh));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(CurveFill, null, geo);
            }
            for (int i = 1; i < n; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);
        }

        // modulated shape (behind), then static
        float depth = _engine.DeviceGetParam(_track, _device, ForgeMath.LfoDrive);
        float modAmt = depth * (_lfoLive - 0.5f) * 0.6f;
        if (depth > 0.01f) Draw(modAmt, ModPen, false);
        Draw(0f, CurvePen, true);

        void Lbl(string s, double x, double y, IBrush b) => ctx.DrawText(new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, b), new Point(x, y));
        Lbl("TRANSFER", gx, 2, Muted);
        Lbl("─ static", gx + gw - 78, 2, NotaPalette.Accent);
        Lbl("┄ modulated", gx + gw - 44, 2, NotaPalette.Teal);
        Lbl("in −60", gx, gy + gh + 1, AxisB);
        Lbl("0 dB", gx + gw - 22, gy + gh + 1, AxisB);
    }
}

internal sealed class ForgeHarmonics : Control
{
    private const int N = 1024, Bars = 14;
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush BorderB = NotaPalette.BorderDefault;
    private static readonly IBrush OddB = NotaPalette.Accent;
    private static readonly IBrush EvenB = new SolidColorBrush(Color.FromArgb(0x73, 0xD8, 0xA0, 0x3D)); // 45%
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush LabelC = new SolidColorBrush(Color.Parse("#A39D8F"));
    private static readonly Typeface Face = new(FontFamily.Default);

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;
    private readonly double[] _re = new double[N], _im = new double[N];
    private readonly double[] _harm = new double[Bars + 2];
    private double _thd; private bool _oddHeavy = true;

    public ForgeHarmonics(IAudioEngine engine, int track, int device)
    {
        _engine = engine; _track = track; _device = device;
        MinHeight = 40; ClipToBounds = true;
    }

    public void Sync()
    {
        // Run a unit sine (a few periods) through the static chain, FFT, read partials.
        const int periods = 16;
        for (int i = 0; i < N; i++)
        {
            double ph = 2 * Math.PI * periods * i / N;
            double x = 0.7 * Math.Sin(ph);
            _re[i] = ForgeMath.Chain(_engine, _track, _device, (float)x, 0f);
            _im[i] = 0;
        }
        Fft(_re, _im);
        double fund = Mag(periods);
        if (fund < 1e-6) fund = 1e-6;
        double odd = 0, even = 0, dist = 0;
        for (int k = 1; k <= Bars; k++)
        {
            double m = Mag(periods * (k + 1)) / fund;   // partials 2,3,4,...
            _harm[k] = m;
            if (k >= 1) { dist += m * m; if ((k + 1) % 2 == 1) odd += m; else even += m; }
        }
        _thd = Math.Sqrt(dist) * 100.0;
        _oddHeavy = odd >= even;
        InvalidateVisual();
    }

    private double Mag(int k) => k > 0 && k < N / 2 ? Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) : 0;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Bg, new Pen(BorderB, 1), new Rect(0, 0, w, h), 6, 6);
        void Lbl(string s, double x, double y, IBrush b) => ctx.DrawText(new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, b), new Point(x, y));
        Lbl("HARMONICS", 6, 3, Muted);
        Lbl($"THD {_thd:0.0} % · {(_oddHeavy ? "odd-heavy" : "even-heavy")}", w - 116, 3, LabelC);

        double gx = 6, gy = 15, gw = w - 12, gh = h - gy - 4;
        if (gh <= 0) return;
        double bw = gw / Bars;
        for (int k = 1; k <= Bars; k++)
        {
            double m = Math.Clamp(_harm[k] / 0.6, 0, 1);   // 0..1 normalized for display
            double bh = m * gh;
            double x = gx + (k - 1) * bw;
            bool odd = (k + 1) % 2 == 1;
            ctx.FillRectangle(odd ? OddB : EvenB, new Rect(x + 1, gy + gh - bh, Math.Max(1, bw - 2), bh), 1);
        }
    }

    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }
}
