// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Auto Filter response curve. Three layers over a log-frequency grid:
//  1) a real-time spectrum analyzer of the pre-filter signal (FFT of the device scope);
//  2) the filter's magnitude response at the BASE cutoff — interactive: drag the handle
//     (or anywhere) to set cutoff (X) + resonance (Y), recording an automation gesture;
//  3) a live modulation indicator — a bright marker that rides the current MODULATED
//     cutoff (base + envelope incl. sidechain + LFO), so you see the sidechain working.
// Frequency axis == the Freq param range (30..18000 Hz) so handle-X ↔ normalized cutoff.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class AutoFilterCurve : Control
{
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush BorderB = NotaPalette.BorderDefault;
    private static readonly IPen GridPen = new Pen(NotaPalette.Wash(NotaPalette.BorderStrong, 0x22));
    private static readonly IPen CurvePen = new Pen(NotaPalette.Accent, 1.8); // solid brass = current response
    private static readonly IBrush CurveFill = NotaPalette.Wash(NotaPalette.Accent, 0x18);
    private static readonly IBrush SpecFill = NotaPalette.Wash(NotaPalette.Ink("#8AA6C0"), 0x22);
    private static readonly IPen SpecPen = new Pen(NotaPalette.Wash(NotaPalette.Ink("#9CB4CC"), 0x44));
    private static readonly IBrush HandleB = NotaPalette.AccentBright;

    private static readonly IPen ModPen = new Pen(NotaPalette.Teal, 1.2)
        { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }; // dashed teal = at modulation

    private static readonly IBrush SweepFill = NotaPalette.Wash(NotaPalette.Teal, 0x12);

    private static readonly IPen SweepEdge = new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x55))
        { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };

    private static readonly IBrush AxisB = NotaPalette.TextTertiary;
    private static readonly Typeface Face = new(FontFamily.Default);

    private const double FMin = 30.0, FMax = 18000.0;
    private const double DbTop = 18.0, DbBot = -36.0;
    private const int FftN = 2048, Bins = FftN / 2;

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;

    private double _fcNorm = 0.5, _res;
    private int _type;
    private float _morph;
    private bool _slope24;
    private bool _drag;
    private double _liveCut = 0.5; // live modulated cutoff (normalized), from the DSP
    private double _sweepLo = 0.5, _sweepHi = 0.5; // decaying min/max of the modulated cutoff → sweep band

    // Analyzer state.
    private readonly float[] _scope = new float[FftN];
    private readonly double[] _re = new double[FftN], _im = new double[FftN];
    private readonly double[] _specDb = new double[Bins];
    private readonly double[] _hann = new double[FftN];
    private double _sr = 48000;

    public event Action<double>? CutoffChanged;
    public event Action<double>? ResChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public AutoFilterCurve(IAudioEngine engine, int track, int device)
    {
        _engine = engine;
        _track = track;
        _device = device;
        MinHeight = 90;
        MinWidth = 180;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        for (var i = 0; i < FftN; i++) _hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftN - 1));
        for (var i = 0; i < Bins; i++) _specDb[i] = -120;
    }

    // Base (user-set) filter params — drives the drawn response + the handle.
    public void Set(double freqNorm, double res, int type, float morph, bool slope24)
    {
        _fcNorm = Math.Clamp(freqNorm, 0, 1);
        _res = Math.Clamp(res, 0, 1);
        _type = type;
        _morph = morph;
        _slope24 = slope24;
        InvalidateVisual();
    }

    // UI tick: pull the scope → FFT → spectrum, and the live modulated cutoff.
    public void Tick()
    {
        _liveCut = Math.Clamp(_engine.DeviceGainReduction(_track, _device), 0, 1);
        // Sweep band: grow to include the live cutoff, then relax slowly toward it.
        _sweepLo = Math.Min(_sweepLo, _liveCut) + (_liveCut - Math.Min(_sweepLo, _liveCut)) * 0.006;
        _sweepHi = Math.Max(_sweepHi, _liveCut) + (_liveCut - Math.Max(_sweepHi, _liveCut)) * 0.006;
        var n = _engine.DeviceScope(_track, _device, _scope, FftN);
        if (n < FftN)
        {
            for (var k = 1; k < Bins; k++) _specDb[k] = Math.Max(-120, _specDb[k] - 2.5);
            InvalidateVisual();
            return;
        }

        for (var i = 0; i < FftN; i++)
        {
            _re[i] = _scope[i] * _hann[i];
            _im[i] = 0;
        }

        Fft(_re, _im);
        _sr = _engine.SampleRate > 0 ? _engine.SampleRate : 48000;
        var refMag = FftN / 4.0;
        for (var k = 1; k < Bins; k++)
        {
            var mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
            var db = 20 * Math.Log10(mag / refMag + 1e-9);
            _specDb[k] = db > _specDb[k] ? db : Math.Max(db, _specDb[k] - 2.5);
        }

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _drag = true;
        GestureBegin?.Invoke();
        e.Pointer.Capture(this);
        Apply(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag) Apply(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag)
        {
            _drag = false;
            GestureEnd?.Invoke();
            e.Pointer.Capture(null);
        }
    }

    private void Apply(Point p)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        CutoffChanged?.Invoke(Math.Clamp(p.X / w, 0, 1));
        ResChanged?.Invoke(Math.Clamp(1 - p.Y / h, 0, 1));
    }

    private static double HzOf(double norm)
    {
        return FMin * Math.Pow(FMax / FMin, norm);
    }

    private double Q => 0.5 + _res * 14.5;

    private double Mag(int type, double f, double fc)
    {
        double w = f / fc, w2 = w * w, q = Q;
        var den = Math.Sqrt((1 - w2) * (1 - w2) + w / q * (w / q));
        if (den < 1e-9) den = 1e-9;
        var m = type switch { 0 => 1.0 / den, 1 => w / q / den, 2 => w2 / den, _ => Math.Abs(1 - w2) / den };
        if (_slope24) m *= m;
        return m;
    }

    private double MagDb(double f, double fc)
    {
        var m = Mag(_type, f, fc);
        if (_morph > 0f) m = m * (1 - _morph) + Mag((_type + 1) & 3, f, fc) * _morph;
        return 20.0 * Math.Log10(Math.Max(m, 1e-5));
    }

    private double YForDb(double db, double h)
    {
        return Math.Clamp((DbTop - db) / (DbTop - DbBot) * h, 0, h);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Bg, new Pen(BorderB), new Rect(0, 0, w, h), 5, 5);

        double logMin = Math.Log10(FMin), logMax = Math.Log10(FMax), logSpan = logMax - logMin;

        double X(double f)
        {
            return (Math.Log10(f) - logMin) / logSpan * w;
        }

        foreach (var f in new[] { 100.0, 1000, 10000 })
        {
            var p1 = new Point(X(f), 0);
            var p2 = new Point(X(f), h);
            ctx.DrawLine(GridPen, p1, p2);
        }

        ctx.DrawLine(GridPen, new Point(0, YForDb(0, h)), new Point(w, YForDb(0, h)));

        // --- 1) spectrum analyzer (behind) ---
        var spec = new StreamGeometry();
        using (var g = spec.Open())
        {
            g.BeginFigure(new Point(0, h));
            for (var k = 1; k < Bins; k++)
            {
                var f = k * _sr / FftN;
                if (f < FMin || f > FMax) continue;
                var norm = Math.Clamp((_specDb[k] + 84) / 84, 0, 1);
                g.LineTo(new Point(X(f), h - norm * h));
            }

            g.LineTo(new Point(w, h));
            g.EndFigure(true);
        }

        ctx.DrawGeometry(SpecFill, SpecPen, spec);

        // --- teal sweep band: the range the modulation walks the cutoff over ---
        if (_sweepHi - _sweepLo > 0.004)
        {
            double bx0 = _sweepLo * w, bx1 = _sweepHi * w;
            ctx.FillRectangle(SweepFill, new Rect(bx0, 0, bx1 - bx0, h));
            ctx.DrawLine(SweepEdge, new Point(bx0, 0), new Point(bx0, h));
            ctx.DrawLine(SweepEdge, new Point(bx1, 0), new Point(bx1, h));
        }

        // --- 2) base response curve (interactive) ---
        var baseFc = HzOf(_fcNorm);
        var n = (int)Math.Max(24, Math.Min(400, w));
        var pts = new Point[n];
        for (var i = 0; i < n; i++)
        {
            var fx = (double)i / (n - 1);
            var f = Math.Pow(10, logMin + fx * logSpan);
            pts[i] = new Point(fx * w, YForDb(MagDb(f, baseFc), h));
        }

        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(new Point(0, h));
            foreach (var pt in pts) gc.LineTo(pt);
            gc.LineTo(new Point(w, h));
            gc.EndFigure(true);
        }

        ctx.DrawGeometry(CurveFill, null, geo);
        for (var i = 1; i < n; i++) ctx.DrawLine(CurvePen, pts[i - 1], pts[i]);

        // handle at the base cutoff, sitting on the curve.
        double cx = _fcNorm * w, cy = YForDb(MagDb(baseFc, baseFc), h);
        ctx.DrawEllipse(HandleB, new Pen(Bg, 2), new Point(cx, cy), 5, 5);

        // --- 3) dashed teal response at the live modulated cutoff (envelope + LFO) ---
        if (Math.Abs(_liveCut - _fcNorm) > 0.004)
        {
            var modFc = HzOf(_liveCut);
            Point prev = default;
            var has = false;
            for (var i = 0; i < n; i++)
            {
                var fx = (double)i / (n - 1);
                var f = Math.Pow(10, logMin + fx * logSpan);
                var pt = new Point(fx * w, YForDb(MagDb(f, modFc), h));
                if (has) ctx.DrawLine(ModPen, prev, pt);
                prev = pt;
                has = true;
            }
        }

        // frequency scale + live cutoff readout + legend.
        void Lbl(string t, double x, double y, IBrush b)
        {
            ctx.DrawText(
                new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face,
                    8, b), new Point(x, y));
        }

        foreach (var (f, s) in new[] { (100.0, "100"), (1000.0, "1k"), (10000.0, "10k") })
            Lbl(s, Math.Clamp(X(f) - 6, 2, w - 16), h - 11, AxisB);
        var hz = HzOf(_liveCut);
        Lbl(hz >= 1000 ? $"{hz / 1000.0:0.00} kHz" : $"{hz:0} Hz", 4, 2, HandleB);
        var leg = new FormattedText("┄ modulated", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8,
            NotaPalette.Teal);
        ctx.DrawText(leg, new Point(w - leg.Width - 4, 2));
    }

    // In-place iterative radix-2 Cooley–Tukey FFT.
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    var ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }
    }
}