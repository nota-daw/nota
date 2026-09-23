// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The custom-drawn views of the Nota Compressor card: the LEVEL column's faders (with the
// live level the fader is set against), the transfer curve (drag the threshold node), the
// gain-reduction history, the Motion envelope (reduction against the input, at a time scale
// where attack and release can be read) and the sidechain key spectrum with its filters
// (drag the HP / LP handles; up/down sets Q). They draw what the card hands them and own
// no engine state — every edit goes out through events.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal static class CompDraw
{
    public static FormattedText Text(string t, IBrush ink, double size = 7)
        => new(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Mono, size, ink);

    public static void Polyline(DrawingContext ctx, IPen pen, IReadOnlyList<Point> pts)
    {
        if (pts.Count < 2) return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Count; i++) g.LineTo(pts[i]);
        }
        ctx.DrawGeometry(null, pen, geo);
    }

    /// <summary>The soft-knee static curve, in dB: the reduction at input level <paramref name="inDb"/>.</summary>
    public static double Reduction(double inDb, double thr, double ratio, double knee, double range)
    {
        double over = inDb - thr, slope = 1.0 - 1.0 / Math.Max(1, ratio), gr;
        if (knee > 0.01 && 2 * over > -knee && 2 * over < knee) { double t = over + knee / 2; gr = slope * t * t / (2 * knee); }
        else gr = over > 0 ? slope * over : 0;
        return Math.Min(gr, range);
    }

    public static string Db(double db) => db <= -99 ? "−∞" : NotaNum.F($"{db:0.0}");
}

// A vertical fader in the LEVEL column: a groove, a wash fill in its chroma up to the value
// and a bright handle, plus a hairline tick at the live level it works against (the input
// for the threshold). Drag moves the value relative to where it was — a click never jumps it;
// Shift / Ctrl / ⌘ drag fine; double-click restores the default.
internal sealed class CompFader : Control
{
    private double _v, _live = double.NaN, _startV, _startY;
    private bool _drag;

    public IBrush Ink { get; set; } = NotaPalette.Accent;
    public IBrush Handle { get; set; } = NotaPalette.AccentBright;
    public double Default { get; set; } = double.NaN;
    public bool Dragging => _drag;

    public event Action<double>? Changed;
    public event Action? GestureBegin;
    public event Action? GestureEnd;

    public CompFader() { Width = 16; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    public void Set(double v, double live = double.NaN)
    {
        double n = Math.Clamp(v, 0, 1);
        bool dirty = !_drag && Math.Abs(n - _v) > 1e-4;
        if (!_drag) _v = n;
        if (!(double.IsNaN(live) && double.IsNaN(_live)) && Math.Abs(live - _live) > 1e-3) { _live = live; dirty = true; }
        if (dirty) InvalidateVisual();
    }

    public void Recolor(IBrush ink, IBrush handle) { Ink = ink; Handle = handle; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && !double.IsNaN(Default))
        {
            GestureBegin?.Invoke(); _v = Default; Changed?.Invoke(_v); GestureEnd?.Invoke();
            InvalidateVisual(); e.Handled = true; return;
        }
        _drag = true; _startV = _v; _startY = e.GetPosition(this).Y;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        double h = Math.Max(20, Bounds.Height);
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double v = Math.Clamp(_startV + (_startY - e.GetPosition(this).Y) / h * (fine ? 0.2 : 1.0), 0, 1);
        if (Math.Abs(v - _v) < 1e-6) return;
        _v = v; Changed?.Invoke(_v); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 4) return;
        var rect = new Rect(0.5, 0.5, w - 1, h - 1);
        double r = Math.Min(8, w / 2);
        ctx.DrawRectangle(NotaPalette.BgSunken, null, rect, r, r);
        double y = (1 - _v) * h;
        using (ctx.PushClip(new RoundedRect(rect, r)))
        {
            ctx.FillRectangle(NotaPalette.Wash((SolidColorBrush)Ink, 0x48), new Rect(0, y, w, h - y));
            if (!double.IsNaN(_live) && _live > 0.001)
            {
                double ly = (1 - Math.Clamp(_live, 0, 1)) * h;
                ctx.DrawLine(new Pen(NotaPalette.TextSecondary, 1), new Point(3, ly), new Point(w - 3, ly));
            }
        }
        ctx.DrawRectangle(null, new Pen(NotaPalette.BorderDefault, 1), rect, r, r);
        double hy = Math.Clamp(y, 1.5, h - 2.5);
        ctx.DrawRectangle(Handle, null, new Rect(1.5, hy - 1, w - 3, 2), 1, 1);
    }
}

// The transfer curve: input dB (x, −60…0) against output dB (y), unity dashed, the knee drawn
// as a curve, the threshold node brass. Drag anywhere: left / right moves the threshold,
// up / down the ratio; double-click restores both. The live input level rides the curve as a small dot.
internal sealed class CompTransferView : Control
{
    private const double Lo = -60, Hi = 0;
    private double _thr = -18, _ratio = 3, _knee = 6, _range = 48, _live = double.NaN;
    private bool _drag;
    private double _startX, _startY, _startRatio, _startThr;

    public event Action<double, double>? Changed;   // threshold dB, ratio
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action? Reset;
    public bool Dragging => _drag;

    public CompTransferView() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Hand); }

    public void Set(double thr, double ratio, double knee, double range, double liveInDb)
    {
        _thr = thr; _ratio = Math.Max(1, ratio); _knee = knee; _range = range; _live = liveInDb;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available)
    {
        double s = double.IsInfinity(available.Height) ? 100 : available.Height;
        return new Size(s, s);
    }

    private (double x0, double x1, double top, double bot) Geo() => (4, Bounds.Width - 4, 14, Bounds.Height - 12);
    private static double Map(double db, double a, double b) => a + (db - Lo) / (Hi - Lo) * (b - a);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { Reset?.Invoke(); e.Handled = true; return; }
        _drag = true; _startX = e.GetPosition(this).X; _startY = e.GetPosition(this).Y; _startRatio = _ratio; _startThr = _thr;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke();
    }
    private void Apply(Point p)
    {
        var (x0, x1, _, _) = Geo();
        // Relative to where the drag began, so a click never jumps the curve.
        double thr = Math.Clamp(_startThr + (p.X - _startX) / Math.Max(1, x1 - x0) * (Hi - Lo), Lo, Hi);
        // Ratio on a log feel: 40 px up doubles it, down halves it.
        double ratio = Math.Clamp(_startRatio * Math.Pow(2, (_startY - p.Y) / 40.0), 1, 20);
        _thr = thr; _ratio = ratio;
        Changed?.Invoke(thr, ratio);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 20) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        var (x0, x1, top, bot) = Geo();
        double X(double db) => Map(db, x0, x1);
        double Y(double db) => Map(db, bot, top);
        for (int i = 1; i <= 3; i++)
        {
            double gx = x0 + (x1 - x0) * i / 4.0, gy = top + (bot - top) * i / 4.0;
            ctx.DrawLine(NotaGraph.GridPen, new Point(gx, top), new Point(gx, bot));
            ctx.DrawLine(NotaGraph.GridPen, new Point(x0, gy), new Point(x1, gy));
        }
        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) }, new Point(X(Lo), Y(Lo)), new Point(X(Hi), Y(Hi)));
        double tx = X(_thr);
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(tx, top), new Point(tx, bot));

        var pts = new List<Point>();
        for (double db = Lo; db <= Hi + 0.01; db += 0.75)
            pts.Add(new Point(X(db), Y(db - CompDraw.Reduction(db, _thr, _ratio, _knee, _range))));
        CompDraw.Polyline(ctx, NotaGraph.PrimaryPen, pts);

        // The live operating point: where the signal sits on the curve right now.
        if (!double.IsNaN(_live) && _live > Lo)
        {
            double li = Math.Min(Hi, _live);
            ctx.DrawEllipse(NotaPalette.TextPrimary, null, new Point(X(li), Y(li - CompDraw.Reduction(li, _thr, _ratio, _knee, _range))), 2, 2);
        }
        NotaGraph.Node(ctx, new Point(tx, Y(_thr - CompDraw.Reduction(_thr, _thr, _ratio, _knee, _range))), active: true);

        NotaGraph.Title(ctx, rect, "Transfer");
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.TopRight, NotaNum.F($"{_ratio:0.#}:1"), NotaPalette.AccentBright);
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomLeft, "−60");
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomRight, "0 in");
    }
}

// The reduction history: four seconds of gain reduction hanging from the top edge (depth =
// reduction), a teal dashed line at the held peak. Push() once per UI tick; a click clears
// the held peak.
internal sealed class CompGrHistoryView : Control
{
    private const int N = 240;                 // 4 s at the 60 Hz UI tick
    private readonly double[] _h = new double[N];
    private int _w, _count;
    public double Current { get; private set; }
    public double Peak { get; private set; }
    public double Average { get; private set; }

    public CompGrHistoryView() => ClipToBounds = true;

    public void Push(double grDb)
    {
        _h[_w] = Math.Max(0, grDb); _w = (_w + 1) % N; _count = Math.Min(N, _count + 1);
        Current = Math.Max(0, grDb);
        double pk = 0, sum = 0; int active = 0;
        for (int i = 0; i < _count; i++) { double v = _h[i]; pk = Math.Max(pk, v); if (v > 0.05) { sum += v; active++; } }
        Peak = pk;
        Average = active > 0 ? sum / active : 0;
        InvalidateVisual();
    }

    public void Clear() { Array.Clear(_h); _count = 0; Current = Peak = Average = 0; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Clear(); e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 20) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        double top = 14, bot = h - 12, x0 = 3, x1 = w - 3;
        double scale = Peak > 18 ? 36 : Peak > 9 ? 18 : 12;   // dB at the bottom edge
        for (int i = 1; i <= 3; i++) { double gy = top + (bot - top) * i / 4.0; ctx.DrawLine(NotaGraph.GridPen, new Point(x0, gy), new Point(x1, gy)); }
        ctx.DrawLine(new Pen(NotaPalette.GraphBorder, 1), new Point(x0, top), new Point(x1, top));

        if (_count > 1)
        {
            var pts = new List<Point>(_count);
            for (int i = 0; i < _count; i++)
            {
                int idx = (_w - _count + i + N) % N;
                double x = x1 - (_count - 1 - i) * (x1 - x0) / (N - 1);
                pts.Add(new Point(x, top + Math.Clamp(_h[idx] / scale, 0, 1) * (bot - top)));
            }
            CompDraw.Polyline(ctx, NotaGraph.PrimaryPen, pts);
        }
        if (Peak > 0.05)
        {
            double py = top + Math.Clamp(Peak / scale, 0, 1) * (bot - top);
            ctx.DrawLine(new Pen(NotaPalette.Teal, 1) { DashStyle = new DashStyle(new double[] { 2, 4 }, 0) }, new Point(x0, py), new Point(x1, py));
            var ft = CompDraw.Text(NotaNum.F($"peak −{Peak:0.0}"), NotaPalette.TealBright);
            ctx.DrawText(ft, new Point(x1 - ft.Width - 2, Math.Clamp(py - ft.Height - 1, top, bot - ft.Height)));
        }
        NotaGraph.Title(ctx, rect, "Gain reduction");
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.TopRight, Current > 0.05 ? NotaNum.F($"−{Current:0.0} dB") : "0.0 dB", NotaPalette.AccentBright);
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomLeft, "−4 s");
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomRight, "now");
    }
}

// Motion: the reduction envelope (brass, hanging from 0 dB at the top) over the input level
// (teal, −60…0 dB bottom→top) with the threshold dashed on the input scale — a window short
// enough that attack and release read as shapes. Fed from the device's 1 ms envelope ring.
internal sealed class CompMotionView : Control
{
    private float[] _in = Array.Empty<float>(), _gr = Array.Empty<float>();
    private int _points;
    private double _thr = -18, _windowMs = 600;
    private double _grScale = 12;

    public CompMotionView() => ClipToBounds = true;

    /// <summary>Feed the rings (oldest→newest, one point per ms) and the window to show.</summary>
    public void Set(float[] input, float[] gr, int count, double threshold, double windowMs)
    {
        _in = input; _gr = gr; _points = count; _thr = threshold; _windowMs = windowMs;
        double pk = 0;
        int n = Math.Min(count, (int)windowMs);
        for (int i = count - n; i < count; i++) if (i >= 0) pk = Math.Max(pk, gr[i]);
        _grScale = pk > 24 ? 48 : pk > 12 ? 24 : pk > 6 ? 12 : 6;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 20) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        double top = 14, bot = h - 12, x0 = 3, x1 = w - 3;
        for (int i = 1; i < 6; i++) { double gx = x0 + (x1 - x0) * i / 6.0; ctx.DrawLine(NotaGraph.GridPen, new Point(gx, top), new Point(gx, bot)); }
        ctx.DrawLine(NotaGraph.GridPen, new Point(x0, (top + bot) / 2), new Point(x1, (top + bot) / 2));

        double YIn(double db) => bot - Math.Clamp((db + 60) / 60, 0, 1) * (bot - top);
        double YGr(double db) => top + Math.Clamp(db / _grScale, 0, 1) * (bot - top);

        int n = Math.Min(_points, (int)Math.Round(_windowMs));
        if (n > 1 && _in.Length >= _points && _gr.Length >= _points)
        {
            int start = _points - n;
            // Decimate to about one point per pixel, keeping each column's extreme.
            int cols = Math.Max(2, (int)(x1 - x0));
            var inPts = new List<Point>(cols); var grPts = new List<Point>(cols);
            for (int c = 0; c < cols; c++)
            {
                int a = start + (int)((long)c * n / cols), b = start + (int)((long)(c + 1) * n / cols);
                if (b <= a) b = a + 1;
                double mi = -120, mg = 0;
                for (int k = a; k < b && k < _points; k++) { mi = Math.Max(mi, _in[k]); mg = Math.Max(mg, _gr[k]); }
                double x = x0 + (double)c / (cols - 1) * (x1 - x0);
                inPts.Add(new Point(x, YIn(mi))); grPts.Add(new Point(x, YGr(mg)));
            }
            CompDraw.Polyline(ctx, NotaGraph.SecondaryPen(NotaPalette.Teal, 1.1), inPts);
            CompDraw.Polyline(ctx, NotaGraph.PrimaryPen, grPts);
        }
        double ty = YIn(_thr);
        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1) { DashStyle = new DashStyle(new double[] { 2, 4 }, 0) }, new Point(x0, ty), new Point(x1, ty));

        NotaGraph.Title(ctx, rect, "Reduction envelope");
        NotaGraph.Legend(ctx, x1 - 2, 3, ("reduction", NotaPalette.AccentBright, NotaGraph.Mark.Line), ("input", NotaPalette.TealBright, NotaGraph.Mark.Line));
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomLeft, "0");
        NotaGraph.Axis(ctx, rect, NotaGraph.Corner.BottomRight, _windowMs >= 1000 ? NotaNum.F($"{_windowMs / 1000:0.#} s") : NotaNum.F($"{_windowMs:0} ms"));
        var sc = CompDraw.Text(NotaNum.F($"−{_grScale:0} dB"), NotaPalette.TextAxis);
        ctx.DrawText(sc, new Point(x1 - sc.Width - 2, bot - sc.Height - 11));
    }
}

// The sidechain key: the spectrum of the unfiltered key (brass) with the detector filters
// laid over it (teal dashed — the 2-pole HP × LP response with its Q). Drag a handle
// sideways to move its corner, up / down to change Q; double-click opens that filter.
// Framed brass while Listen is on (the key is what you hear).
internal sealed class CompKeyView : Control
{
    private const int FftN = 2048, Bins = FftN / 2;
    private const double FMin = 20, FMax = 20000;
    private readonly double[] _re = new double[FftN], _im = new double[FftN], _hann = new double[FftN], _db = new double[Bins];
    private double _sr = 48000, _hp = 20, _lp = 20000, _q = 0.707;
    private bool _listen, _has;
    private int _drag, _hover;       // 0 none, 1 HP, 2 LP
    private double _startY, _startQ;
    private bool _qGesture;

    public double PeakHz { get; private set; }
    public event Action<int, double>? FreqChanged;     // handle (0 HP / 1 LP), Hz
    public event Action<double>? QChanged;
    public event Action<int>? GestureBegin;            // 0 HP, 1 LP, 2 Q
    public event Action<int>? GestureEnd;
    public event Action<int>? Reset;
    public bool Dragging => _drag != 0;

    public CompKeyView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        for (int i = 0; i < FftN; i++) _hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftN - 1));
        Array.Fill(_db, -120);
    }

    public void SetFilter(double hpHz, double lpHz, double q, bool listen)
    {
        _hp = hpHz; _lp = lpHz; _q = q; _listen = listen;
        InvalidateVisual();
    }

    private int _silentTicks;

    /// <summary>Analyse the key samples (oldest→newest, at least 2048). A silent frame keeps the
    /// last frame that had signal for about a second, so a drum key does not blink out between hits.</summary>
    public void SetKey(float[] src, int offset, int count, double sampleRate)
    {
        if (count < FftN) { _has = false; InvalidateVisual(); return; }
        _sr = sampleRate > 0 ? sampleRate : 48000;
        int s0 = offset + count - FftN;
        double energy = 0;
        for (int i = 0; i < FftN; i++) { double v = src[s0 + i]; energy += v * v; _re[i] = v * _hann[i]; _im[i] = 0; }
        if (energy <= 1e-10)
        {
            if (++_silentTicks > 60 && _has) { _has = false; PeakHz = 0; InvalidateVisual(); }
            return;
        }
        _silentTicks = 0;
        _has = true;
        Fft(_re, _im);
        double refMag = FftN / 4.0, best = -200; int bestK = 0;
        for (int k = 1; k < Bins; k++)
        {
            double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
            double db = 20 * Math.Log10(mag / refMag + 1e-9);
            _db[k] = db;
            double hz = k * _sr / FftN;
            if (hz >= FMin && db > best) { best = db; bestK = k; }
        }
        PeakHz = bestK * _sr / FftN;
        InvalidateVisual();
    }

    private static double XOf(double hz, double w) => Math.Log(Math.Clamp(hz, FMin, FMax) / FMin) / Math.Log(FMax / FMin) * w;
    private static double HzOf(double x, double w) => FMin * Math.Pow(FMax / FMin, Math.Clamp(x / Math.Max(1, w), 0, 1));
    private int Nearest(double x) => Math.Abs(x - XOf(_hp, Bounds.Width)) <= Math.Abs(x - XOf(_lp, Bounds.Width)) ? 1 : 2;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int hnd = Nearest(e.GetPosition(this).X);
        if (e.ClickCount == 2) { Reset?.Invoke(hnd - 1); e.Handled = true; return; }
        _drag = hnd; _startY = e.GetPosition(this).Y; _startQ = _q;
        GestureBegin?.Invoke(hnd - 1); _qGesture = false;
        e.Pointer.Capture(this); Apply(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag != 0) { Apply(e.GetPosition(this)); return; }
        int hnd = Nearest(e.GetPosition(this).X);
        if (hnd != _hover) { _hover = hnd; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hover != 0) { _hover = 0; InvalidateVisual(); } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == 0) return;
        GestureEnd?.Invoke(_drag - 1);
        if (_qGesture) { GestureEnd?.Invoke(2); _qGesture = false; }
        _drag = 0; e.Pointer.Capture(null);
    }
    private void Apply(Point p)
    {
        double hz = HzOf(p.X, Bounds.Width);
        if (_drag == 1) { _hp = Math.Clamp(hz, 20, 2000); FreqChanged?.Invoke(0, _hp); }
        else { _lp = Math.Clamp(hz, 200, 20000); FreqChanged?.Invoke(1, _lp); }
        double q = Math.Clamp(_startQ * Math.Pow(2, (_startY - p.Y) / 50.0), 0.5, 4);
        if (Math.Abs(q - _q) > 1e-4 && Math.Abs(p.Y - _startY) > 3)
        {
            if (!_qGesture) { GestureBegin?.Invoke(2); _qGesture = true; }
            _q = q; QChanged?.Invoke(q);
        }
        InvalidateVisual();
    }

    // |H| of a 2-pole high-pass / low-pass at f, corner fc, quality q.
    private static double Mag2(double f, double fc, double q, bool high)
    {
        double r = f / fc, re = 1 - r * r, im = r / q;
        double den = Math.Sqrt(re * re + im * im);
        return (high ? r * r : 1) / Math.Max(den, 1e-9);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 20) return;
        var rect = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, rect);
        if (_listen) ctx.DrawRectangle(null, new Pen(NotaPalette.BorderBrass, 1), new RoundedRect(rect.Deflate(0.5), NotaGraph.Radius));
        double top = 14, bot = h - 12;
        const double floor = -84, ceil = 0;
        double Y(double db) => top + Math.Clamp((ceil - db) / (ceil - floor), 0, 1) * (bot - top);
        foreach (double f in new[] { 100.0, 1000, 10000 }) { double gx = XOf(f, w); ctx.DrawLine(NotaGraph.GridPen, new Point(gx, top), new Point(gx, bot)); }
        for (int i = 1; i <= 3; i++) { double gy = top + (bot - top) * i / 4.0; ctx.DrawLine(NotaGraph.GridPen, new Point(2, gy), new Point(w - 2, gy)); }

        if (_has)
        {
            var pts = new List<Point>();
            int cols = Math.Max(8, (int)(w / 2));
            for (int c = 0; c <= cols; c++)
            {
                double f0 = HzOf((c - 0.5) * w / cols, w), f1 = HzOf((c + 0.5) * w / cols, w);
                int k0 = Math.Max(1, (int)(f0 * FftN / _sr)), k1 = Math.Min(Bins - 1, Math.Max(k0, (int)(f1 * FftN / _sr)));
                double db = -120; for (int k = k0; k <= k1; k++) db = Math.Max(db, _db[k]);
                pts.Add(new Point(c * w / cols, Y(db)));
            }
            CompDraw.Polyline(ctx, NotaGraph.PrimaryPen, pts);
        }

        // The key filter response, pinned at 0 dB = the top grid line.
        bool hpOn = _hp > 20.5, lpOn = _lp < 19999;
        var fp = new List<Point>();
        for (int i = 0; i <= 96; i++)
        {
            double x = w * i / 96.0, f = HzOf(x, w);
            double m = (hpOn ? Mag2(f, _hp, _q, true) : 1) * (lpOn ? Mag2(f, _lp, _q, false) : 1);
            fp.Add(new Point(x, Y(20 * Math.Log10(Math.Max(m, 1e-6)) - 6)));
        }
        CompDraw.Polyline(ctx, new Pen(NotaPalette.Teal, 1.2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, fp);

        foreach (var (hz, idx, on) in new[] { (_hp, 1, hpOn), (_lp, 2, lpOn) })
        {
            double x = Math.Clamp(XOf(hz, w), 3, w - 3);
            bool hot = _drag == idx || (_drag == 0 && _hover == idx);
            ctx.DrawLine(new Pen(hot ? NotaPalette.AccentBright : NotaPalette.BorderStrong, 1), new Point(x, top), new Point(x, bot));
            NotaGraph.Node(ctx, new Point(x, Y(-6)), active: hot || on);
            string lbl = !on ? "off" : hz >= 1000 ? NotaNum.F($"{hz / 1000:0.#}k") : NotaNum.F($"{hz:0}");
            var ft = CompDraw.Text(lbl, on ? NotaPalette.AccentBright : NotaPalette.TextAxis);
            double lx = Math.Clamp(x - ft.Width / 2, 3, w - 3 - ft.Width);
            ctx.DrawText(ft, new Point(lx, h - ft.Height - 2));
        }

        NotaGraph.Title(ctx, rect, "Sidechain key");
        NotaGraph.Legend(ctx, w - 6, 3, ("key", NotaPalette.AccentBright, NotaGraph.Mark.Line), ("key filter", NotaPalette.TealBright, NotaGraph.Mark.Dashed));
        if (!_has)
        {
            var ft = CompDraw.Text("no key signal", NotaPalette.TextTertiary, 8);
            ctx.DrawText(ft, new Point((w - ft.Width) / 2, (top + bot - ft.Height) / 2));
        }
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
                    double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
    }
}
