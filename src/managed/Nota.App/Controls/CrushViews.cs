// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Crush (device kind 12) card windows:
//  · CrQuantView — the Quantiser tab: 10 ms of a 100 Hz sine through the crusher — the source
//    dashed, the held and quantised steps in brass over the level grid. The hold is drawn to
//    scale; above 4 bits the level step is enlarged (so it stays visible) and says so. Drag up /
//    down for Bits, left / right for Rate; double-click resets both.
//  · CrSpectrumView — the Spectrum tab, from the engine's FFT (Crush.h scopeRead): the crushed
//    output in 40 log bands, brass up to the (level-matched) input, teal where the crush added
//    content — bright above the reduced Nyquist (the images), dim below (harmonics, noise). A
//    ghost outline shows input the post filter took away. Drag the Nyquist line (Rate) or the
//    filter line (Post Filter).
//  · CrTransferView — the Transfer tab's curve: input → output through Drive, the mode's shape
//    and the quantiser, at the real level count. Drag up / down for Drive.
//  · CrWaveView — the Transfer tab's form: two cycles of a sine at the driven level and what
//    comes out.
// The shape functions mirror Crush.h so the pictures are the DSP.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal static class CrushMath
{
    public static readonly string[] Modes = { "Digital", "Analog", "Fold" };

    public static double Shape(int mode, double x) => mode switch
    {
        1 => Math.Tanh(x * 1.5) / 0.905,
        2 => Fold(x * 2.5, 0.7),
        _ => x,
    };

    public static double Fold(double x, double threshold)
    {
        double period = 4 * threshold;
        double u = (x + threshold) % period;
        if (u < 0) u += period;
        return 1 - Math.Abs(u - 2 * threshold) / threshold;
    }

    /// <summary>Quantise like the DSP: round(x · (L − 1)) / (L − 1).</summary>
    public static double Quant(double x, double levels)
    {
        double s = Math.Max(1, levels - 1);
        return Math.Round(x * s) / s;
    }

    /// <summary>The level count the Quantiser draws: exact up to 4 bits (16 levels), then
    /// enlarged — 16 … 40 levels over 4 … 24 bits — so a step stays visible.</summary>
    public static double ShownLevels(double bits) => bits <= 4 ? Math.Pow(2, bits) : 16 + (bits - 4) * 1.2;

    public static string Hz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0}\u2009k") : NotaNum.F($"{hz:0}\u2009Hz");
}

internal sealed class CrQuantView : Control
{
    private double _bits = 13.7, _hold = 24, _sr = 48000, _drive = 1;
    private int _mode;
    private bool _drag;
    private Point _start;
    private double _b0, _r0;

    /// <summary>Bits and Rate (both 0..1) at the start of a drag.</summary>
    public Func<(double Bits, double Rate)>? Value { get; set; }
    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action<double, double>? Changed;
    public event Action? ResetRequested;
    public bool Dragging => _drag;

    public CrQuantView() { ClipToBounds = true; MinHeight = 36; Cursor = new Cursor(StandardCursorType.SizeAll); }

    public void Set(double bits, double hold, double sr, int mode, double drive)
    {
        if (Math.Abs(bits - _bits) < 1e-4 && Math.Abs(hold - _hold) < 1e-4 && Math.Abs(sr - _sr) < 1 && mode == _mode && Math.Abs(drive - _drive) < 1e-4) return;
        _bits = bits; _hold = Math.Max(1, hold); _sr = sr > 0 ? sr : 48000; _mode = mode; _drive = drive;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { ResetRequested?.Invoke(); e.Handled = true; return; }
        var v = Value?.Invoke() ?? (0.5, 0.5);
        _b0 = v.Bits; _r0 = v.Rate; _start = e.GetPosition(this); _drag = true;
        GestureBegin?.Invoke(); e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        var p = e.GetPosition(this);
        double b = Math.Clamp(_b0 + (_start.Y - p.Y) / Math.Max(60, Bounds.Height * 1.6), 0, 1);
        double r = Math.Clamp(_r0 + (p.X - _start.X) / Math.Max(120, Bounds.Width * 1.2), 0, 1);
        Changed?.Invoke(b, r);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke(); InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double x0 = 6, x1 = w - 6, top = 16, bot = h - 12, mid = (top + bot) / 2, amp = (bot - top) / 2 * 0.86;
        double Y(double v) => mid - Math.Clamp(v, -1.12, 1.12) * amp;

        double levels = CrushMath.ShownLevels(_bits);
        bool enlarged = _bits > 4.0001;
        // Level grid: every level when there are few, else every n-th.
        int every = Math.Max(1, (int)Math.Ceiling((levels - 1) / 10.0));
        double s = Math.Max(1, levels - 1);
        for (int k = -(int)s; k <= (int)s; k += every)
        {
            double y = Y(k / s);
            if (y < top - 1 || y > bot + 1 || k == 0) continue;
            ctx.DrawLine(NotaGraph.GridPen, new Point(x0, y), new Point(x1, y));
        }
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(x0, mid), new Point(x1, mid));

        // 10 ms: one cycle of a 100 Hz sine at 0.8 × Drive.
        double win = _sr / 100.0;
        double pxPerSmp = (x1 - x0) / win;
        double Src(double smp) => Math.Sin(2 * Math.PI * smp / win - 0.1) * 0.8 * _drive;

        var src = new StreamGeometry();
        using (var g = src.Open())
        {
            for (double x = x0; x <= x1 + 0.1; x += 2)
            {
                var pt = new Point(x, Y(Src((x - x0) / pxPerSmp)));
                if (x == x0) g.BeginFigure(pt, false); else g.LineTo(pt);
            }
            g.EndFigure(false);
        }

        var step = new StreamGeometry();
        var area = new StreamGeometry();
        using (var g = step.Open())
        using (var a = area.Open())
        {
            bool first = true;
            for (double smp = 0; smp < win; smp += _hold)
            {
                double y = Y(CrushMath.Quant(CrushMath.Shape(_mode, Src(smp)), levels));
                double xa = x0 + smp * pxPerSmp, xb = Math.Min(x1, x0 + (smp + _hold) * pxPerSmp);
                if (first) { g.BeginFigure(new Point(xa, y), false); first = false; } else g.LineTo(new Point(xa, y));
                g.LineTo(new Point(xb, y));
                a.BeginFigure(new Point(xa, mid), true);
                a.LineTo(new Point(xa, y)); a.LineTo(new Point(xb, y)); a.LineTo(new Point(xb, mid));
                a.EndFigure(true);
            }
            if (!first) g.EndFigure(false);
        }
        ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.Accent, 0x29), null, area);
        ctx.DrawGeometry(null, new Pen(NotaPalette.TextTertiary, 1, new DashStyle(new double[] { 3, 3 }, 0)), src);
        ctx.DrawGeometry(null, new Pen(NotaPalette.Accent, _drag ? 2.2 : NotaGraph.PrimaryWidth, lineJoin: PenLineJoin.Round), step);

        NotaGraph.Title(ctx, frame, "Quantiser");
        double rate = _sr / _hold;
        NotaGraph.Legend(ctx, w - 6, 3, ("source", NotaPalette.TextSecondary, NotaGraph.Mark.Dashed),
            (NotaNum.F($"{_bits:0.0} bit @ {CrushMath.Hz(rate)}"), NotaPalette.AccentBright, NotaGraph.Mark.Line));
        if (enlarged) NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "level step shown enlarged");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "10\u2009ms");
    }
}

internal sealed class CrSpectrumView : ShDragView
{
    public const int HNyquist = 0, HFilter = 1;
    private readonly float[] _in = new float[40], _out = new float[40];
    private int _n;
    private double _nyq = 1000, _filt = 10000;
    private bool _valid;
    private const double FLo = 20, FHi = 20000, DbLo = -84, DbHi = 0;
    public Func<double, double> NyquistToNorm { get; set; } = hz => 0.5;
    public Func<double, double> FilterToNorm { get; set; } = hz => 0.5;

    public void Set(float[] src, int inAt, int outAt, int n, double nyqHz, double filterHz, bool valid)
    {
        n = Math.Min(n, _in.Length);
        if (n > 0 && Math.Max(inAt, outAt) + n <= src.Length)
        {
            Array.Copy(src, inAt, _in, 0, n);
            Array.Copy(src, outAt, _out, 0, n);
        }
        _n = n; _nyq = nyqHz; _filt = filterHz; _valid = valid && n > 0;
        InvalidateVisual();
    }

    private (double x0, double x1, double top, double bot) Plot() => (6, Bounds.Width - 6, 16, Bounds.Height - 12);
    private double X(double hz) { var (x0, x1, _, _) = Plot(); return x0 + Math.Log(Math.Clamp(hz, FLo, FHi) / FLo) / Math.Log(FHi / FLo) * (x1 - x0); }
    private double F(double x) { var (x0, x1, _, _) = Plot(); return FLo * Math.Pow(FHi / FLo, Math.Clamp((x - x0) / Math.Max(1, x1 - x0), 0, 1)); }

    protected override int Hit(Point p)
    {
        if (Math.Abs(p.X - X(_nyq)) <= 5) return HNyquist;
        if (Math.Abs(p.X - X(_filt)) <= 5) return HFilter;
        return -1;
    }
    protected override double DragTo(int handle, Point start, Point p, double v0)
        => handle == HNyquist ? NyquistToNorm(F(p.X)) : FilterToNorm(F(p.X));
    protected override StandardCursorType CursorFor(int handle) => StandardCursorType.SizeWestEast;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        var (x0, x1, top, bot) = Plot();
        double Y(double db) => bot - Math.Clamp((db - DbLo) / (DbHi - DbLo), 0, 1) * (bot - top);
        for (int q = 1; q < 3; q++) ctx.DrawLine(NotaGraph.GridPen, new Point(x0, top + (bot - top) * q / 3), new Point(x1, top + (bot - top) * q / 3));
        foreach (double f in new[] { 100.0, 1000.0, 10000.0 }) ctx.DrawLine(NotaGraph.GridPen, new Point(X(f), top), new Point(X(f), bot));

        double nx = X(_nyq);
        if (_valid)
        {
            double bw = (x1 - x0) / _n, gap = bw > 4 ? 1.5 : 0.5;
            var ghost = new Pen(NotaPalette.TextDisabled, 1, new DashStyle(new double[] { 2, 2 }, 0));
            var addLo = NotaPalette.Wash(NotaPalette.Teal, 0x66);
            var addHi = NotaPalette.Wash(NotaPalette.TealBright, 0xC0);
            for (int b = 0; b < _n; b++)
            {
                double x = x0 + b * bw, cw = Math.Max(0.6, bw - gap);
                double fc = FLo * Math.Pow(FHi / FLo, (b + 0.5) / _n);
                double yi = Y(_in[b]), yo = Y(_out[b]);
                double ys = Math.Max(yi, yo);   // the signal part: up to the lower of in and out
                if (ys < bot - 0.5) ctx.DrawRectangle(NotaPalette.Accent, null, new Rect(x, ys, cw, bot - ys), 1, 1);
                if (_out[b] > _in[b] + 1 && yo < ys - 0.5)
                    ctx.DrawRectangle(fc > _nyq ? addHi : addLo, null, new Rect(x, yo, cw, ys - yo), 1, 1);
                if (_in[b] > _out[b] + 3 && yi < bot - 1)
                    ctx.DrawRectangle(null, ghost, new Rect(x + 0.5, yi, cw - 1, yo - yi));
            }
        }
        else
        {
            var ft = NotaGraph.AxisText("no signal", NotaPalette.TextTertiary, 8);
            ctx.DrawText(ft, new Point((w - ft.Width) / 2, (top + bot - ft.Height) / 2));
        }

        // The post filter and the Nyquist of the reduced rate — both draggable.
        double fx = X(_filt);
        ctx.DrawLine(new Pen(Active == HFilter ? NotaPalette.AccentBright : NotaPalette.AccentDim, Active == HFilter ? 1.6 : 1, new DashStyle(new double[] { 2, 3 }, 0)),
            new Point(fx, top), new Point(fx, bot));
        ctx.DrawLine(new Pen(Active == HNyquist ? NotaPalette.TealBright : NotaPalette.Teal, Active == HNyquist ? 1.6 : 1, new DashStyle(new double[] { 3, 2 }, 0)),
            new Point(nx, top), new Point(nx, bot));
        Label(ctx, "Nyquist " + CrushMath.Hz(_nyq), nx, top + 2, NotaPalette.TealBright);
        Label(ctx, "filter " + CrushMath.Hz(_filt), fx, top + 12, NotaPalette.AccentBright);

        NotaGraph.Title(ctx, frame, "Spectrum");
        NotaGraph.Legend(ctx, w - 6, 3, ("signal", NotaPalette.AccentBright, NotaGraph.Mark.Area),
            ("added", NotaPalette.Teal, NotaGraph.Mark.Area), ("images", NotaPalette.TealBright, NotaGraph.Mark.Area));
        Text(ctx, "100", X(100) + 2, h - 11);
        Text(ctx, "1\u2009k", X(1000) + 2, h - 11);
        Text(ctx, "10\u2009k", X(10000) + 2, h - 11);
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "20");
    }

    // A marker label on the side of the line that has room.
    private void Label(DrawingContext ctx, string s, double x, double y, IBrush ink)
    {
        var ft = NotaGraph.AxisText(s, ink);
        var (x0, x1, _, _) = Plot();
        double lx = x + 3 + ft.Width > x1 ? x - 3 - ft.Width : x + 3;
        ctx.DrawText(ft, new Point(Math.Max(x0, lx), y));
    }
}

internal sealed class CrTransferView : ShDragView
{
    public const int HDrive = 0;
    private int _mode;
    private double _drive = 1, _levels = 16, _peak;

    public void Set(int mode, double drive, double levels, double drivenPeak)
    {
        if (mode == _mode && Math.Abs(drive - _drive) < 1e-4 && Math.Abs(levels - _levels) < 1e-3 && Math.Abs(drivenPeak - _peak) < 1e-3) return;
        _mode = mode; _drive = drive; _levels = levels; _peak = drivenPeak;
        InvalidateVisual();
    }

    protected override int Hit(Point p) => HDrive;
    protected override double DragTo(int handle, Point start, Point p, double v0) => v0 + (start.Y - p.Y) / Math.Max(80, Bounds.Height * 1.4);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double pad = 8, top = 16, x0 = pad, x1 = w - pad, bot = h - pad;
        double side = Math.Min(x1 - x0, bot - top);
        double cx = (x0 + x1) / 2, cy = (top + bot) / 2, r = side / 2;
        ctx.DrawLine(NotaGraph.GridPen, new Point(cx, cy - r), new Point(cx, cy + r));
        ctx.DrawLine(NotaGraph.GridPen, new Point(cx - r, cy), new Point(cx + r, cy));
        ctx.DrawLine(new Pen(NotaPalette.TextTertiary, 1, new DashStyle(new double[] { 2, 3 }, 0)), new Point(cx - r, cy + r), new Point(cx + r, cy - r));

        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            const int N = 240;
            for (int i = 0; i <= N; i++)
            {
                double x = -1 + 2.0 * i / N;
                double y = CrushMath.Quant(CrushMath.Shape(_mode, x * _drive), _levels);
                var pt = new Point(cx + x * r, cy - Math.Clamp(y, -1.1, 1.1) * r / 1.1);
                if (i == 0) gc.BeginFigure(pt, false); else gc.LineTo(pt);
            }
            gc.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(NotaPalette.Accent, Dragging ? 2.2 : NotaGraph.PrimaryWidth, lineJoin: PenLineJoin.Round), g);
        // Where the signal peaks on the curve (the driven peak back in input units).
        if (_peak > 1e-4 && _drive > 1e-6)
        {
            double xin = Math.Clamp(_peak / _drive, 0, 1);
            double y = CrushMath.Quant(CrushMath.Shape(_mode, xin * _drive), _levels);
            NotaGraph.Node(ctx, new Point(cx + xin * r, cy - Math.Clamp(y, -1.1, 1.1) * r / 1.1), true);
        }
        NotaGraph.Title(ctx, frame, "Curve");
    }
}

internal sealed class CrWaveView : Control
{
    private int _mode;
    private double _drive = 1, _levels = 16, _hold = 1, _sr = 48000, _driveDb;

    public CrWaveView() { ClipToBounds = true; MinHeight = 36; }

    public void Set(int mode, double drive, double driveDb, double levels, double hold, double sr)
    {
        if (mode == _mode && Math.Abs(drive - _drive) < 1e-4 && Math.Abs(levels - _levels) < 1e-3 && Math.Abs(hold - _hold) < 1e-4 && Math.Abs(sr - _sr) < 1) return;
        _mode = mode; _drive = drive; _driveDb = driveDb; _levels = levels; _hold = Math.Max(1, hold); _sr = sr > 0 ? sr : 48000;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double x0 = 6, x1 = w - 6, top = 16, bot = h - 12, mid = (top + bot) / 2, amp = (bot - top) / 2 * 0.8;
        double Y(double v) => mid - Math.Clamp(v, -1.22, 1.22) * amp;
        ctx.DrawLine(NotaGraph.GridPen, new Point(x0, mid), new Point(x1, mid));
        ctx.DrawLine(NotaGraph.GridPen, new Point(x0, Y(1)), new Point(x1, Y(1)));
        ctx.DrawLine(NotaGraph.GridPen, new Point(x0, Y(-1)), new Point(x1, Y(-1)));

        // 10 ms: two cycles of a 200 Hz sine at full scale × Drive, held and crushed.
        double win = _sr / 100.0, pxPerSmp = (x1 - x0) / win;
        double Src(double smp) => Math.Sin(4 * Math.PI * smp / win) * _drive;
        var src = new StreamGeometry();
        var outG = new StreamGeometry();
        using (var s = src.Open())
        using (var o = outG.Open())
        {
            double held = 0, next = 0;
            for (double x = x0; x <= x1 + 0.1; x += 1)
            {
                double smp = (x - x0) / pxPerSmp;
                if (smp >= next) { held = Src(Math.Floor(smp / _hold) * _hold); next = (Math.Floor(smp / _hold) + 1) * _hold; }
                var ps = new Point(x, Y(Src(smp)));
                var po = new Point(x, Y(CrushMath.Quant(CrushMath.Shape(_mode, held), _levels)));
                if (x == x0) { s.BeginFigure(ps, false); o.BeginFigure(po, false); }
                else { s.LineTo(ps); o.LineTo(po); }
            }
            s.EndFigure(false); o.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xB3), 1, new DashStyle(new double[] { 3, 3 }, 0)), src);
        ctx.DrawGeometry(null, new Pen(NotaPalette.Accent, 1.6, lineJoin: PenLineJoin.Round), outG);

        NotaGraph.Title(ctx, frame, "Form");
        NotaGraph.Legend(ctx, w - 6, 3, (NotaNum.F($"in {_driveDb:+0;−0;0}\u2009dB"), NotaPalette.TealBright, NotaGraph.Mark.Dashed),
            ("out", NotaPalette.AccentBright, NotaGraph.Mark.Line));
        string lv = _levels >= 1e6 ? NotaNum.F($"{_levels / 1e6:0.0}\u2009M levels") : _levels >= 1e4 ? NotaNum.F($"{_levels / 1e3:0}\u2009k levels") : NotaNum.F($"{_levels:0} levels");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, lv);
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "10\u2009ms");
    }
}
