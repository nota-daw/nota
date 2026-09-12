// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Custom-drawn views for the Nota Prism card (multiband dynamics, device kind 21):
//   PrismSpectrum  — the input spectrum (fill) and output spectrum (line) under the three band
//                    tints; the crossover lines are drag handles (X = frequency).
//   PrismTransfer  — the selected band's static curve (compression above + expansion / upward
//                    compression below, knee, floor) with the live detector level riding on it;
//                    drag a threshold dot ← → for the threshold, ↑ ↓ for the ratio.
//   PrismEnvTrace  — the band's input peaks and its detector envelope against the threshold.
//   PrismGrMeter   — a gain-reduction bar that grows down from the top (boost grows up), with
//                    an optional peak-hold line.
// Band colours follow the mockup: Low = mauve, Mid = brass, High = teal.

using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal static class PrismInk
{
    public static readonly Color[] BandColor = { NotaPalette.TrackColors[7], NotaPalette.AccentColor, NotaPalette.TrackColors[4] };
    public static readonly IBrush[] Band = { new SolidColorBrush(BandColor[0]), NotaPalette.Accent, NotaPalette.Teal };
    // Text / handle tint for the selected band (the brass band lights up to AccentBright).
    public static readonly IBrush[] BandLit = { Band[0], NotaPalette.AccentBright, Band[2] };
    public static readonly string[] Names = { "Low", "Mid", "High" };
    public static readonly IBrush InnerBorder = NotaPalette.GraphBorder;
    public static readonly IPen InnerPen = new Pen(InnerBorder, 1);
    private static readonly Typeface Mono = new("ui-monospace, Menlo, monospace");
    private static readonly Typeface Sans = new("Inter, system-ui, sans-serif", FontStyle.Normal, FontWeight.Bold);

    public static IBrush Alpha(Color c, byte a) => new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
    public static FormattedText Text(string s, double size, IBrush b) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, b);
    public static FormattedText Caps(string s, double size, IBrush b) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, size, b);
    public static double Db(double lin) => lin > 1e-6 ? 20 * Math.Log10(lin) : -120;
    public static string HzShort(double hz) => hz >= 1000 ? (hz >= 10000 ? FormattableString.Invariant($"{hz / 1000:0}k") : FormattableString.Invariant($"{hz / 1000:0.0}k")) : FormattableString.Invariant($"{hz:0}");
}

// ---------------------------------------------------------------------------------------------
// Spectrum + crossovers
// ---------------------------------------------------------------------------------------------
internal sealed class PrismSpectrum : Control
{
    private const int FftN = 2048, Bins = FftN / 2;
    private const double TopDb = -6, BottomDb = -96;
    private const double MinGap = 0.0335;   // ⅓ octave in normalized crossover units (log 20 Hz … 20 kHz)
    private static readonly IBrush InFill = PrismInk.Alpha(((ISolidColorBrush)NotaPalette.TextPrimary).Color, 0x12);
    private readonly IAudioEngine _engine;
    private readonly int _track, _device;
    private readonly float[] _buf = new float[FftN];
    private readonly double[] _re = new double[FftN], _im = new double[FftN], _hann = new double[FftN];
    private readonly double[] _inDb = new double[Bins], _outDb = new double[Bins];
    private double _sr = 48000, _xl = 0.318, _xh = 0.693;
    private int _bands = 3, _sel = 1, _drag = -1, _hover = -1;

    public double DefaultLow { get; set; } = 0.318;
    public double DefaultHigh { get; set; } = 0.693;
    public event Action<int, double>? CrossoverChanged;   // (0 low / 1 high, normalized)
    public event Action<int>? DragStarted;
    public event Action<int>? DragEnded;
    public event Action<int>? BandClicked;

    public PrismSpectrum(IAudioEngine engine, int track, int device)
    {
        _engine = engine; _track = track; _device = device;
        for (int i = 0; i < FftN; i++) _hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftN - 1));
        Array.Fill(_inDb, -120); Array.Fill(_outDb, -120);
        ClipToBounds = true;
    }

    public void Set(double xl, double xh, int bands, int sel, double sr)
    {
        if (sr > 0) _sr = sr;
        if (_drag < 0) { _xl = xl; _xh = xh; }
        _bands = bands; _sel = sel;
    }

    public void Tick()
    {
        if (!IsEffectivelyVisible) return;
        Analyze(0, _inDb); Analyze(1, _outDb);
        InvalidateVisual();
    }

    private void Analyze(int layer, double[] db)
    {
        int n = _engine.DeviceLayerWave(_track, _device, layer, _buf, FftN);
        if (n < FftN) { for (int k = 1; k < Bins; k++) db[k] = Math.Max(-120, db[k] - 2.5); return; }
        for (int i = 0; i < FftN; i++) { _re[i] = _buf[i] * _hann[i]; _im[i] = 0; }
        Fft(_re, _im);
        const double refMag = FftN / 4.0;
        for (int k = 1; k < Bins; k++)
        {
            double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
            double d = 20 * Math.Log10(mag / refMag + 1e-9);
            db[k] = d > db[k] ? d : Math.Max(d, db[k] - 2.5);
        }
    }

    private double X(double v) => v * Bounds.Width;
    private int HitLine(Point p)
    {
        if (_bands >= 2 && Math.Abs(p.X - X(_xl)) <= 6) return 0;
        if (_bands >= 3 && Math.Abs(p.X - X(_xh)) <= 6) return 1;
        return -1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag >= 0)
        {
            double v = Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 1);
            if (_drag == 0) { v = Math.Min(v, _bands >= 3 ? _xh - MinGap : 0.97); _xl = v; }
            else { v = Math.Max(v, _xl + MinGap); _xh = v; }
            CrossoverChanged?.Invoke(_drag, v);
            InvalidateVisual();
            return;
        }
        int h = HitLine(p);
        if (h != _hover) { _hover = h; Cursor = h >= 0 ? new Cursor(StandardCursorType.SizeWestEast) : new Cursor(StandardCursorType.Hand); InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hover >= 0) { _hover = -1; InvalidateVisual(); } }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        int h = HitLine(p);
        if (h >= 0)
        {
            if (e.ClickCount == 2)
            {
                DragStarted?.Invoke(h);
                double v = h == 0 ? DefaultLow : DefaultHigh;
                if (h == 0) _xl = v; else _xh = v;
                CrossoverChanged?.Invoke(h, v);
                DragEnded?.Invoke(h);
            }
            else { _drag = h; DragStarted?.Invoke(h); e.Pointer.Capture(this); }
            e.Handled = true; InvalidateVisual();
            return;
        }
        double f = p.X / Math.Max(1, Bounds.Width);
        int band = _bands == 1 ? 1 : f < _xl ? 0 : (_bands == 2 || f < _xh) ? 1 : 2;
        BandClicked?.Invoke(band);
        e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        int d = _drag; _drag = -1; e.Pointer.Capture(null); DragEnded?.Invoke(d); InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 8 || h < 8) return;
        var rect = new Rect(0, 0, w, h);
        ctx.DrawRectangle(NotaPalette.BgSunken, null, rect, 4, 4);

        // Band tints (the selected band a little stronger).
        double[] edges = _bands switch { 1 => new[] { 0.0, 1.0 }, 2 => new[] { 0.0, _xl, 1.0 }, _ => new[] { 0.0, _xl, _xh, 1.0 } };
        int[] ids = _bands switch { 1 => new[] { 1 }, 2 => new[] { 0, 1 }, _ => new[] { 0, 1, 2 } };
        for (int i = 0; i < ids.Length; i++)
            ctx.FillRectangle(PrismInk.Alpha(PrismInk.BandColor[ids[i]], ids[i] == _sel ? (byte)0x22 : (byte)0x14), new Rect(X(edges[i]), 0, X(edges[i + 1]) - X(edges[i]), h));
        var grid = new Pen(NotaPalette.SurfaceCard, 1);
        foreach (double gf in new[] { 100.0, 1000.0, 10000.0 }) { double gx = X(Math.Log(gf / 20) / Math.Log(1000)); ctx.DrawLine(grid, new Point(gx, 0), new Point(gx, h)); }

        // Spectra: input filled, output as a line.
        double Y(double db) => Math.Clamp((TopDb - db) / (TopDb - BottomDb), 0, 1) * h;
        double DbAt(double[] a, double fx)
        {
            double f0 = 20 * Math.Pow(1000, fx), f1 = 20 * Math.Pow(1000, fx + 2 / w);
            double k0 = f0 * FftN / _sr, k1 = f1 * FftN / _sr;
            if (k1 - k0 < 1)
            {
                int i = Math.Clamp((int)k0, 1, Bins - 2); double t = Math.Clamp(k0 - i, 0, 1);
                return a[i] * (1 - t) + a[i + 1] * t;
            }
            double m = -120;
            for (int k = Math.Max(1, (int)k0); k <= Math.Min(Bins - 1, (int)k1); k++) m = Math.Max(m, a[k]);
            return m;
        }
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(0, h), true);
            for (double x = 0; x <= w; x += 2) g.LineTo(new Point(x, Y(DbAt(_inDb, x / w))));
            g.LineTo(new Point(w, h));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(InFill, new Pen(NotaPalette.BorderStrong, 1), fill);
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(new Point(0, Y(DbAt(_outDb, 0))), false);
            for (double x = 2; x <= w; x += 2) g.LineTo(new Point(x, Y(DbAt(_outDb, x / w))));
        }
        ctx.DrawGeometry(null, new Pen(NotaPalette.TextSecondary, 1.3), line);

        // Crossover lines + frequency tags (the line takes the colour of the band above it).
        void Cross(double v, int upper, int idx)
        {
            double x = Math.Round(X(v)) + 0.5;
            var col = PrismInk.Band[upper];
            bool hot = _hover == idx || _drag == idx;
            ctx.DrawLine(new Pen(col, hot ? 2 : 1), new Point(x, 0), new Point(x, h));
            var t = PrismInk.Text(PrismInk.HzShort(20 * Math.Pow(1000, v)), 7, upper == 1 ? NotaPalette.AccentBright : col);
            double tw = t.Width + 8, tx = Math.Clamp(x - tw / 2, 0, w - tw);
            var tag = new Rect(tx, 0, tw, 12);
            ctx.DrawRectangle(hot ? NotaPalette.SurfaceRaised : NotaPalette.SurfaceCard, new Pen(col, 1), tag, 3, 3);
            ctx.DrawText(t, new Point(tx + 4, 6 - t.Height / 2));
        }
        if (_bands >= 2) Cross(_xl, 1, 0);
        if (_bands >= 3) Cross(_xh, 2, 1);

        var l0 = PrismInk.Text("20", 7, NotaPalette.TextDisabled);
        ctx.DrawText(l0, new Point(5, h - l0.Height - 1));
        var l1 = PrismInk.Text("20k Hz", 7, NotaPalette.TextDisabled);
        ctx.DrawText(l1, new Point(w - l1.Width - 5, h - l1.Height - 1));
        ctx.DrawRectangle(null, PrismInk.InnerPen, rect.Deflate(0.5), 4, 4);
    }

    // In-place iterative radix-2 Cooley–Tukey FFT.
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

// ---------------------------------------------------------------------------------------------
// Transfer curve
// ---------------------------------------------------------------------------------------------
internal sealed class PrismTransfer : Control
{
    private const double Lo = -72;   // axes span −72 … 0 dB
    private double _thrA = -18, _slopeA = 0.5, _thrB = -40, _slopeB = 0.4, _floor = 24, _knee = 6, _level = -120;
    private bool _below;
    private int _band = 1, _drag, _hover;   // handle: 1 above, 2 below
    private Point _start;

    public event Action<int>? DragStarted;
    public event Action<int, double, double>? Dragged;   // (handle, dx fraction of width, dy px since press)
    public event Action<int>? DragEnded;
    public event Action<int>? ResetRequested;

    public PrismTransfer() { ClipToBounds = true; }

    public void Set(int band, double thrA, double slopeA, bool below, double thrB, double slopeB, double floor, double knee, double level)
    {
        _band = band; _thrA = thrA; _slopeA = slopeA; _below = below; _thrB = thrB; _slopeB = slopeB; _floor = floor; _knee = knee; _level = level;
        InvalidateVisual();
    }

    private static double Knee(double u, double w)
    {
        if (w > 0.01) { if (2 * u < -w) return 0; if (2 * u <= w) { double t = u + 0.5 * w; return t * t / (2 * w); } return u; }
        return u > 0 ? u : 0;
    }
    private double Out(double x)
    {
        double y = x - _slopeA * Knee(x - _thrA, _knee);
        if (_below) y += Math.Clamp(-_slopeB * Knee(_thrB - x, _knee), -_floor, _floor);
        return y;
    }
    private Point Pt(double inDb, double outDb)
    {
        double w = Bounds.Width, h = Bounds.Height;
        return new Point((inDb - Lo) / -Lo * w, h - Math.Clamp((outDb - Lo) / -Lo, -0.2, 1.2) * h);
    }
    private int Hit(Point p)
    {
        static double D(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        double da = D(p, Pt(_thrA, Out(_thrA))), db = D(p, Pt(_thrB, Out(_thrB)));
        if (da <= 8 && da <= db) return 1;
        if (db <= 8) return 2;
        return 0;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int hdl = Hit(e.GetPosition(this));
        if (hdl == 0) return;
        if (e.ClickCount == 2) { ResetRequested?.Invoke(hdl); e.Handled = true; return; }
        _drag = hdl; _start = e.GetPosition(this);
        DragStarted?.Invoke(hdl); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag > 0) { Dragged?.Invoke(_drag, (p.X - _start.X) / Math.Max(1, Bounds.Width), p.Y - _start.Y); return; }
        int h = Hit(p);
        if (h != _hover) { _hover = h; Cursor = h > 0 ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default; InvalidateVisual(); }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == 0) return;
        int d = _drag; _drag = 0; e.Pointer.Capture(null); DragEnded?.Invoke(d);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 8 || h < 8) return;
        var rect = new Rect(0, 0, w, h);
        ctx.DrawRectangle(NotaPalette.BgSunken, null, rect, 4, 4);
        var grid = new Pen(NotaPalette.SurfaceCard, 1);
        ctx.DrawLine(grid, new Point(0, h / 2), new Point(w, h / 2));
        ctx.DrawLine(grid, new Point(w / 2, 0), new Point(w / 2, h));
        ctx.DrawLine(new Pen(NotaPalette.SurfaceRaised, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(0, h), new Point(w, 0));

        var col = PrismInk.Band[_band];
        var curve = new StreamGeometry();
        using (var g = curve.Open())
        {
            g.BeginFigure(Pt(Lo, Out(Lo)), false);
            for (double x = Lo + 0.5; x <= 0; x += 0.5) g.LineTo(Pt(x, Out(x)));
        }
        using (ctx.PushClip(rect))
            ctx.DrawGeometry(null, new Pen(col, 2), curve);

        // Live detector level riding the curve.
        if (_level > Lo) ctx.DrawEllipse(NotaPalette.TextPrimary, null, Pt(_level, Out(_level)), 2.2, 2.2);
        // Threshold handles.
        var below = Pt(_thrB, Out(_thrB));
        if (_below || _hover == 2) ctx.DrawEllipse(_below ? NotaPalette.TextSecondary : NotaPalette.TextDisabled, _hover == 2 ? new Pen(NotaPalette.TextPrimary, 1) : null, below, 3, 3);
        ctx.DrawEllipse(PrismInk.BandLit[_band], _hover == 1 ? new Pen(NotaPalette.TextPrimary, 1) : null, Pt(_thrA, Out(_thrA)), 3.5, 3.5);

        var ta = PrismInk.Text(FormattableString.Invariant($"above {_thrA:0}"), 7, PrismInk.BandLit[_band]);
        ctx.DrawText(ta, new Point(4, 3));
        var tb = PrismInk.Text(_below ? FormattableString.Invariant($"below {_thrB:0}") : "below off", 7, _below ? NotaPalette.TextSecondary : NotaPalette.TextDisabled);
        ctx.DrawText(tb, new Point(4, h - tb.Height - 2));
        ctx.DrawRectangle(null, PrismInk.InnerPen, rect.Deflate(0.5), 4, 4);
    }
}

// ---------------------------------------------------------------------------------------------
// Detector envelope trace
// ---------------------------------------------------------------------------------------------
internal sealed class PrismEnvTrace : Control
{
    private float[] _in = Array.Empty<float>(), _det = Array.Empty<float>();
    private int _n, _band = 1;
    private double _thrA = -18, _thrB = -40;
    private bool _below;

    public PrismEnvTrace() { ClipToBounds = true; }

    public void Set(float[] input, float[] det, int n, int band, double thrA, bool below, double thrB)
    {
        _in = input; _det = det; _n = n; _band = band; _thrA = thrA; _below = below; _thrB = thrB;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 8 || h < 8) return;
        var rect = new Rect(0, 0, w, h);
        ctx.DrawRectangle(NotaPalette.BgSunken, null, rect, 4, 4);
        const double top = 14, range = 60;   // 0 dB at `top`, −60 dB at the bottom
        double Y(double db) => top + Math.Clamp(-db / range, 0, 1) * (h - top - 2);
        var grid = new Pen(NotaPalette.SurfaceCard, 1);
        foreach (int g in new[] { -12, -24, -36, -48 }) ctx.DrawLine(grid, new Point(0, Y(g)), new Point(w, Y(g)));
        var col = PrismInk.Band[_band];
        var dash = new DashStyle(new double[] { 3, 3 }, 0);
        ctx.DrawLine(new Pen(PrismInk.Alpha(PrismInk.BandColor[_band], 0x90), 1, dash), new Point(0, Y(_thrA)), new Point(w, Y(_thrA)));
        if (_below && _thrB > -range) ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1, dash), new Point(0, Y(_thrB)), new Point(w, Y(_thrB)));

        void Trace(float[] a, IPen pen)
        {
            if (_n < 2 || a.Length < _n) return;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (int i = 0; i < _n; i++)
                {
                    var p = new Point(w * i / (_n - 1), Y(PrismInk.Db(a[i])));
                    if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
                }
            }
            ctx.DrawGeometry(null, pen, geo);
        }
        Trace(_in, new Pen(NotaPalette.BorderStrong, 1.2));
        Trace(_det, new Pen(col, 1.7));

        var title = PrismInk.Caps("DETECTOR ENVELOPE · " + PrismInk.Names[_band].ToUpperInvariant(), 7, NotaPalette.TextTertiary);
        ctx.DrawText(title, new Point(5, 3));
        var l2 = PrismInk.Text("detector", 7, PrismInk.BandLit[_band]);
        var l1 = PrismInk.Text("input", 7, NotaPalette.TextTertiary);
        double x2 = w - l2.Width - 5, x1 = x2 - 14 - l1.Width - 12;
        ctx.FillRectangle(NotaPalette.BorderStrong, new Rect(x1, 7, 8, 2)); ctx.DrawText(l1, new Point(x1 + 11, 3));
        ctx.FillRectangle(col, new Rect(x2 - 11, 7, 8, 2)); ctx.DrawText(l2, new Point(x2, 3));
        ctx.DrawRectangle(null, PrismInk.InnerPen, rect.Deflate(0.5), 4, 4);
    }
}

// ---------------------------------------------------------------------------------------------
// Gain-reduction meter
// ---------------------------------------------------------------------------------------------
internal sealed class PrismGrMeter : Control
{
    private const double Scale = 12;   // full height = 12 dB
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private double _gr, _boost, _hold, _holdT;
    private bool _peakHold;
    public int Band { get; init; }
    public bool Soloed { get; set; }

    public PrismGrMeter() { Width = 18; }

    public void Set(double grDb, double boostDb, bool peakHold)
    {
        _gr = Math.Max(0, grDb); _boost = Math.Max(0, boostDb); _peakHold = peakHold;
        double now = Clock.Elapsed.TotalSeconds;
        if (_gr >= _hold) { _hold = _gr; _holdT = now; }
        else if (now - _holdT > 1.5) _hold = Math.Max(_gr, _hold - 0.2);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 4) return;
        var rect = new Rect(0.5, 0.5, w - 1, h - 1);
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(Soloed ? NotaPalette.Accent : PrismInk.InnerBorder, 1), rect, 2, 2);
        double ih = h - 3;
        var col = PrismInk.BandColor[Band];
        if (_gr > 0.01) ctx.DrawRectangle(PrismInk.Band[Band], null, new Rect(1.5, 1.5, w - 3, ih * Math.Min(1, _gr / Scale)), 2, 2);
        if (_boost > 0.01) { double bh = ih * Math.Min(1, _boost / Scale); ctx.FillRectangle(PrismInk.Alpha(col, 0x70), new Rect(1.5, h - 1.5 - bh, w - 3, bh)); }
        if (_peakHold && _hold > 0.05)
        {
            double y = 1.5 + ih * Math.Min(1, _hold / Scale);
            ctx.FillRectangle(NotaPalette.TextPrimary, new Rect(1.5, Math.Min(h - 2.5, y), w - 3, 1));
        }
    }
}
