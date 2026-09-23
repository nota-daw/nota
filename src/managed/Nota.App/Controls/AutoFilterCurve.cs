// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Auto Filter response curve (the Filter tab). Layers over a log-frequency grid:
//  1) a real-time spectrum of the pre-filter signal (FFT of the device's scope ring, fed
//     by the card each tick);
//  2) the teal range the modulation can walk the cutoff over (from the settings), and the
//     filter's magnitude response at the BASE cutoff — interactive: drag the handle (or
//     anywhere) to set cutoff (X) + resonance (Y), recording an automation gesture;
//  3) the response at the LIVE modulated cutoff and resonance, dashed, so you see the
//     envelope / LFO / sidechain working.
// Frequency axis == the Freq param range (30..18000 Hz) so handle-X ↔ normalized cutoff.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AutoFilterCurve : Control
{
    private static readonly IPen CurvePen = new Pen(NotaPalette.Accent, 1.8); // solid brass = current response
    private static readonly IPen SpecPen = new Pen(NotaPalette.Wash(NotaPalette.Ink("#9CB4CC"), 0x44));
    private static readonly IPen ModPen = new Pen(NotaPalette.Teal, 1.2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }; // dashed teal = at modulation
    private static readonly IPen RangePen = new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x8C), 1);
    private static readonly IPen RangePen2 = new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x4C), 1);
    private static readonly IBrush RangeFill = NotaPalette.Wash(NotaPalette.Teal, 0x10);

    private const double FMin = 30.0, FMax = 18000.0;
    private const double DbTop = 18.0, DbBot = -36.0;
    public const int FftN = 2048;
    private const int Bins = FftN / 2;

    private double _fcNorm = 0.5, _res;
    private int _type;
    private float _morph;
    private bool _slope24;
    private bool _drag;
    private double _liveCut = 0.5, _liveRes;       // live modulated cutoff / resonance (normalized)
    private double _rangeLo = 0.5, _rangeHi = 0.5; // where the modulation can take the cutoff
    private bool _modAssigned;

    private readonly double[] _re = new double[FftN], _im = new double[FftN];
    private readonly double[] _specDb = new double[Bins];
    private readonly double[] _hann = new double[FftN];
    private double _sr = 48000;

    public event Action<double>? CutoffChanged;
    public event Action<double>? ResChanged;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public AutoFilterCurve()
    {
        MinHeight = 40;
        MinWidth = 120;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        for (var i = 0; i < FftN; i++) _hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftN - 1));
        for (var i = 0; i < Bins; i++) _specDb[i] = -120;
    }

    public bool Dragging => _drag;

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

    // Live modulation: where the cutoff / resonance are now, and the range the settings allow.
    public void SetLive(double cutNorm, double resNorm, double rangeLo, double rangeHi, bool assigned)
    {
        _liveCut = Math.Clamp(cutNorm, 0, 1);
        _liveRes = Math.Clamp(resNorm, 0, 1);
        _rangeLo = Math.Clamp(rangeLo, 0, 1);
        _rangeHi = Math.Clamp(rangeHi, 0, 1);
        _modAssigned = assigned;
        InvalidateVisual();
    }

    // The pre-filter ring (oldest → newest), FftN samples from `offset`; n < FftN clears it.
    public void FeedSpectrum(float[] buf, int offset, int n, double sampleRate)
    {
        if (n < FftN || offset + FftN > buf.Length)
        {
            for (var k = 1; k < Bins; k++) _specDb[k] = -120;
            return;
        }
        for (var i = 0; i < FftN; i++) { _re[i] = buf[offset + i] * _hann[i]; _im[i] = 0; }
        Fft(_re, _im);
        _sr = sampleRate > 0 ? sampleRate : 48000;
        var refMag = FftN / 4.0;
        for (var k = 1; k < Bins; k++)
        {
            var mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
            _specDb[k] = 20 * Math.Log10(mag / refMag + 1e-9);   // this frame, no ballistics (almanac)
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
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
        if (!_drag) return;
        _drag = false;
        GestureEnd?.Invoke();
        e.Pointer.Capture(null);
    }

    private void Apply(Point p)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        _fcNorm = Math.Clamp(p.X / w, 0, 1);
        _res = Math.Clamp(1 - p.Y / h, 0, 1);
        CutoffChanged?.Invoke(_fcNorm);
        ResChanged?.Invoke(_res);
        InvalidateVisual();
    }

    private static double HzOf(double norm) => FMin * Math.Pow(FMax / FMin, norm);

    private double Mag(int type, double f, double fc, double res)
    {
        double q = 0.5 + res * 14.5;
        double w = f / fc, w2 = w * w;
        var den = Math.Sqrt((1 - w2) * (1 - w2) + w / q * (w / q));
        if (den < 1e-9) den = 1e-9;
        var m = type switch { 0 => 1.0 / den, 1 => w / q / den, 2 => w2 / den, _ => Math.Abs(1 - w2) / den };
        if (_slope24) m *= m;
        return m;
    }

    private double MagDb(double f, double fc, double res)
    {
        var m = Mag(_type, f, fc, res);
        if (_morph > 0f) m = m * (1 - _morph) + Mag((_type + 1) & 3, f, fc, res) * _morph;
        return 20.0 * Math.Log10(Math.Max(m, 1e-5));
    }

    private static double YForDb(double db, double h) => Math.Clamp((DbTop - db) / (DbTop - DbBot) * h, 0, h);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);

        double logMin = Math.Log10(FMin), logSpan = Math.Log10(FMax) - logMin;
        double X(double f) => (Math.Log10(f) - logMin) / logSpan * w;

        foreach (var f in new[] { 100.0, 1000, 10000 }) ctx.DrawLine(NotaGraph.GridPen, new Point(X(f), 0), new Point(X(f), h));
        foreach (var db in new[] { 0.0, -18 }) ctx.DrawLine(NotaGraph.GridPen, new Point(0, YForDb(db, h)), new Point(w, YForDb(db, h)));

        // 1) spectrum analyzer (behind)
        var spec = new StreamGeometry();
        using (var g = spec.Open())
        {
            g.BeginFigure(new Point(0, h), false);
            for (var k = 1; k < Bins; k++)
            {
                var f = k * _sr / FftN;
                if (f < FMin || f > FMax) continue;
                var norm = Math.Clamp((_specDb[k] + 84) / 84, 0, 1);
                g.LineTo(new Point(X(f), h - norm * h));
            }
            g.LineTo(new Point(w, h));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, SpecPen, spec);

        // 2a) the range the modulation can walk the cutoff over (two teal rules)
        if (_modAssigned && _rangeHi - _rangeLo > 0.004)
        {
            double x0 = _rangeLo * w, x1 = _rangeHi * w;
            ctx.FillRectangle(RangeFill, new Rect(x0, 0, x1 - x0, h));
            ctx.DrawLine(RangePen, new Point(x0, 0), new Point(x0, h));
            ctx.DrawLine(RangePen2, new Point(x1, 0), new Point(x1, h));
        }

        int n = (int)Math.Max(24, Math.Min(400, w));
        Point[] Curve(double fc, double res)
        {
            var pts = new Point[n];
            for (var i = 0; i < n; i++)
            {
                var fx = (double)i / (n - 1);
                var f = Math.Pow(10, logMin + fx * logSpan);
                pts[i] = new Point(fx * w, YForDb(MagDb(f, fc, res), h));
            }
            return pts;
        }
        void Stroke(IPen pen, Point[] pts)
        {
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(pts[0], false);
                for (int i = 1; i < pts.Length; i++) gc.LineTo(pts[i]);
                gc.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
        }

        // 3) dashed response at the live modulated cutoff / resonance (under the base curve)
        bool moving = Math.Abs(_liveCut - _fcNorm) > 0.004 || Math.Abs(_liveRes - _res) > 0.01;
        if (moving) Stroke(ModPen, Curve(HzOf(_liveCut), _liveRes));

        // 2b) base response curve (interactive) with its handle
        var baseFc = HzOf(_fcNorm);
        Stroke(CurvePen, Curve(baseFc, _res));
        double cx = _fcNorm * w, cy = YForDb(MagDb(baseFc, baseFc, _res), h);
        NotaGraph.Node(ctx, new Point(cx, cy), active: true);

        // Labels: title, legend, the range corners and the cutoff under its handle.
        NotaGraph.Title(ctx, frame, "Response");
        if (moving || _modAssigned) NotaGraph.Legend(ctx, w - 5, 3, ("modulated", NotaPalette.TealBright, NotaGraph.Mark.Dashed));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "30");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "18k Hz");
        var hz = baseFc;
        string cut = hz >= 1000 ? NotaNum.Str(hz / 1000.0, "0.0") + "k" : NotaNum.Str(hz, "0");
        var cf = NotaGraph.AxisText(cut, NotaPalette.AccentBright);
        double lx = Math.Clamp(cx - cf.Width / 2, 24, Math.Max(24, w - 44 - cf.Width));
        ctx.DrawText(cf, new Point(lx, h - cf.Height - 3));
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
