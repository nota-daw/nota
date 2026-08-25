// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EQ-3 response curve (device kind 16). Display-only — the EQ-3 is *played* on the band
// faders, not edited on the curve — so this just visualises what the faders do. Layers,
// over a log-frequency grid:
//  1) a real-time spectrum analyzer of the pre-EQ signal (FFT of the device scope), in
//     muted olive so it reads as material, not a control;
//  2) the summed three-band magnitude response (brass line + fill), rebuilt from the two
//     crossover frequencies, the slope, and the per-band gains/kills;
//  3) the two crossover frequencies as amber vertical markers.
// Band colours (rust / amber / slate) match the fader strips so a band reads as one thing.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class EqThreeCurve : Control
{
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush BorderB = NotaPalette.BorderDefault;
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0x3A, 0x36, 0x2D)), 1);
    private static readonly IPen CurvePen = new Pen(NotaPalette.Accent, 1.8);
    private static readonly IBrush CurveFill = new SolidColorBrush(Color.FromArgb(0x18, 0xD8, 0xA0, 0x3D));
    private static readonly IBrush SpecFill = new SolidColorBrush(Color.FromArgb(0x22, 0x3E, 0x4A, 0x3C));
    private static readonly IPen SpecPen = new Pen(new SolidColorBrush(Color.FromArgb(0x44, 0x52, 0x60, 0x50)), 1);
    private static readonly IPen XoverPen = new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xD8, 0xA0, 0x3D)), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
    private static readonly IBrush AxisB = NotaPalette.TextTertiary;
    private static readonly IBrush LowB = new SolidColorBrush(Color.Parse("#C4756A"));
    private static readonly IBrush MidB = new SolidColorBrush(Color.Parse("#C99C55"));
    private static readonly IBrush HighB = new SolidColorBrush(Color.Parse("#6D8FB5"));
    private static readonly IBrush KillB = new SolidColorBrush(Color.Parse("#D95F4C"));
    private static readonly Typeface Face = new(FontFamily.Default);

    private const double FMin = 20.0, FMax = 20000.0;
    private const double DbTop = 18.0, DbBot = -24.0;
    private const int FftN = 2048, Bins = FftN / 2;

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;

    // Band state (linear gains after kill), crossovers (Hz), cascade depth.
    private double _gLow = 1, _gMid = 1, _gHigh = 1;
    private bool _kLow, _kMid, _kHigh;
    private double _f1 = 250, _f2 = 2500;
    private int _nSec = 2;

    private readonly float[] _scope = new float[FftN];
    private readonly double[] _re = new double[FftN], _im = new double[FftN];
    private readonly double[] _specDb = new double[Bins];
    private readonly double[] _hann = new double[FftN];
    private double _sr = 48000;

    public EqThreeCurve(IAudioEngine engine, int track, int device)
    {
        _engine = engine; _track = track; _device = device;
        MinHeight = 90; MinWidth = 180; ClipToBounds = true;
        for (int i = 0; i < FftN; i++) _hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftN - 1));
        for (int i = 0; i < Bins; i++) _specDb[i] = -120;
    }

    // Band gains/kills + crossovers + slope from the device params.
    public void Set(double lowDbNorm, double midDbNorm, double highDbNorm,
                    bool kLow, bool kMid, bool kHigh, double f1, double f2, bool slope48)
    {
        _gLow = kLow ? 0 : DbToLin((lowDbNorm - 0.5) * 30);
        _gMid = kMid ? 0 : DbToLin((midDbNorm - 0.5) * 30);
        _gHigh = kHigh ? 0 : DbToLin((highDbNorm - 0.5) * 30);
        _kLow = kLow; _kMid = kMid; _kHigh = kHigh;
        _f1 = f1; _f2 = f2; _nSec = slope48 ? 4 : 2;
        InvalidateVisual();
    }

    public void Tick()
    {
        int n = _engine.DeviceScope(_track, _device, _scope, FftN);
        if (n < FftN) { for (int k = 1; k < Bins; k++) _specDb[k] = Math.Max(-120, _specDb[k] - 2.5); InvalidateVisual(); return; }
        for (int i = 0; i < FftN; i++) { _re[i] = _scope[i] * _hann[i]; _im[i] = 0; }
        Fft(_re, _im);
        _sr = _engine.SampleRate > 0 ? _engine.SampleRate : 48000;
        double refMag = FftN / 4.0;
        for (int k = 1; k < Bins; k++)
        {
            double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
            double db = 20 * Math.Log10(mag / refMag + 1e-9);
            _specDb[k] = db > _specDb[k] ? db : Math.Max(db, _specDb[k] - 2.5);
        }
        InvalidateVisual();
    }

    private static double DbToLin(double db) => Math.Pow(10, db / 20);

    // n cascaded Butterworth-2 sections → 2n-th-order Linkwitz-Riley magnitude.
    private double LpMag(double f, double fc) { double w = f / fc; return Math.Pow(1.0 / Math.Sqrt(1 + w * w * w * w), _nSec); }
    private double HpMag(double f, double fc) { double w = f / fc; return Math.Pow(w * w / Math.Sqrt(1 + w * w * w * w), _nSec); }

    private double RespDb(double f)
    {
        double rest = HpMag(f, _f1);
        double low = LpMag(f, _f1);
        double mid = rest * LpMag(f, _f2);
        double high = rest * HpMag(f, _f2);
        double m = _gLow * low + _gMid * mid + _gHigh * high;
        return 20 * Math.Log10(Math.Max(m, 1e-5));
    }

    private double YForDb(double db, double h) => Math.Clamp((DbTop - db) / (DbTop - DbBot) * h, 0, h);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Bg, new Pen(BorderB, 1), new Rect(0, 0, w, h), 5, 5);

        double logMin = Math.Log10(FMin), logMax = Math.Log10(FMax), logSpan = logMax - logMin;
        double X(double f) => (Math.Log10(f) - logMin) / logSpan * w;
        foreach (double f in new[] { 100.0, 1000, 10000 }) ctx.DrawLine(GridPen, new Point(X(f), 0), new Point(X(f), h));
        ctx.DrawLine(GridPen, new Point(0, YForDb(0, h)), new Point(w, YForDb(0, h)));

        // --- 1) spectrum analyzer (behind) ---
        var spec = new StreamGeometry();
        using (var g = spec.Open())
        {
            g.BeginFigure(new Point(0, h), true);
            for (int k = 1; k < Bins; k++)
            {
                double f = k * _sr / FftN;
                if (f < FMin || f > FMax) continue;
                double norm = Math.Clamp((_specDb[k] + 84) / 84, 0, 1);
                g.LineTo(new Point(X(f), h - norm * h));
            }
            g.LineTo(new Point(w, h));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(SpecFill, SpecPen, spec);

        // --- crossover markers ---
        ctx.DrawLine(XoverPen, new Point(X(_f1), 0), new Point(X(_f1), h));
        ctx.DrawLine(XoverPen, new Point(X(_f2), 0), new Point(X(_f2), h));

        // --- 2) summed band response (interactive-looking, but display-only) ---
        int n = (int)Math.Max(48, Math.Min(480, w));
        var pts = new Point[n];
        for (int i = 0; i < n; i++)
        {
            double fx = (double)i / (n - 1);
            double f = Math.Pow(10, logMin + fx * logSpan);
            pts[i] = new Point(fx * w, YForDb(RespDb(f), h));
        }
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(new Point(0, h), true);
            foreach (var pt in pts) gc.LineTo(pt);
            gc.LineTo(new Point(w, h));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(CurveFill, null, geo);
        for (int i = 1; i < n; i++) ctx.DrawLine(CurvePen, pts[i - 1], pts[i]);

        // --- 3) band labels sitting in each band's frequency zone ---
        void Lbl(string t, double x, double y, IBrush b) => ctx.DrawText(new FormattedText(t, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, b), new Point(x, y));
        double xLow = X(Math.Sqrt(FMin * _f1)), xMid = X(Math.Sqrt(_f1 * _f2)), xHigh = X(Math.Sqrt(_f2 * FMax));
        Lbl(_kLow ? "LOW kill" : $"LOW {(_gLow > 0 ? 20 * Math.Log10(_gLow) : 0):+0.0;-0.0;0.0}", Math.Clamp(xLow - 14, 3, w - 40), 3, _kLow ? KillB : LowB);
        Lbl(_kMid ? "MID kill" : $"MID {(_gMid > 0 ? 20 * Math.Log10(_gMid) : 0):+0.0;-0.0;0.0}", Math.Clamp(xMid - 14, 3, w - 40), 3, _kMid ? KillB : MidB);
        Lbl(_kHigh ? "HIGH kill" : $"HIGH {(_gHigh > 0 ? 20 * Math.Log10(_gHigh) : 0):+0.0;-0.0;0.0}", Math.Clamp(xHigh - 14, 3, w - 44), 3, _kHigh ? KillB : HighB);

        // frequency scale.
        foreach (var (f, s) in new[] { (100.0, "100"), (1000.0, "1k"), (10000.0, "10k") })
            Lbl(s, Math.Clamp(X(f) - 6, 2, w - 16), h - 11, AxisB);
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
