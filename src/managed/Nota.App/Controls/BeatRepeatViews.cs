// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Beat Repeat (device kind 11) card windows, read from the engine's telemetry
// (BeatRepeat.h scopeRead) so the picture is what the repeater does:
//  · BrTimelineView — the Timeline tab: two intervals as grid steps, each a bar of the input
//    level — dry steps dim, the captured slice in full brass, every repeat a step dimmer as it
//    decays — with the trigger points, the bar numbers and the playhead.
//  · BrSlicesView — the Slices tab: the last interval's input (teal) on the grid, the gate
//    window from the offset shaded, the captured slice in brass. Drag the offset line or the
//    gate's end sideways (16ths of the interval); double-click resets.
//  · BrFilterView — the Filter tab: the repeat filter (LP / BP / HP) for the first repeat in
//    brass and a later one (teal dashed) when the band narrows and follows the pitch. Drag
//    the node for the frequency, a band edge for the width.
// Drag contract from ShDragView: left button only, a gesture per drag, double-click resets
// the handle, right-click bubbles to the MIDI-learn menu.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class BrTimelineView : Control
{
    private float[] _lvl = Array.Empty<float>(), _kind = Array.Empty<float>(), _gain = Array.Empty<float>();
    private int _n;
    private double _winBeats = 8, _intervalBeats = 4, _gridBeats = 0.25, _barBeats = 4, _offset, _phase;
    private bool _playing;
    private string _title = "Capture and repeat";

    public BrTimelineView() { ClipToBounds = true; MinHeight = 36; }

    /// <summary>The cells (level dB · kind 0 dry / 1 capture / 2 repeat · repeat gain) over the
    /// window, its length and the interval / grid / bar in beats, the trigger offset (0..1 of the
    /// interval) and the playhead (0..1 of the window).</summary>
    public void Set(float[] src, int at, int n, double winBeats, double intervalBeats, double gridBeats, double barBeats,
        double offset, double phase, bool playing, string title)
    {
        if (_lvl.Length != n) { _lvl = new float[n]; _kind = new float[n]; _gain = new float[n]; }
        if (n > 0 && at + 3 * n <= src.Length)
        {
            Array.Copy(src, at, _lvl, 0, n);
            Array.Copy(src, at + n, _kind, 0, n);
            Array.Copy(src, at + 2 * n, _gain, 0, n);
        }
        _n = n; _winBeats = Math.Max(0.25, winBeats); _intervalBeats = Math.Max(0.125, intervalBeats);
        _gridBeats = Math.Max(1.0 / 64, gridBeats); _barBeats = Math.Max(1, barBeats); _offset = offset; _phase = phase;
        _playing = playing; _title = title;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double x0 = 6, x1 = w - 6, top = 16, bot = h - 13;
        double X(double beats) => x0 + beats / _winBeats * (x1 - x0);

        // Grid steps over the window (at most 64 drawn; finer grids share a column).
        int steps = (int)Math.Clamp(Math.Round(_winBeats / _gridBeats), 1, 64);
        double sw = (x1 - x0) / steps;
        double gap = sw > 8 ? 3 : sw > 4 ? 1 : 0.5;
        if (_n > 0)
            for (int s = 0; s < steps; s++)
            {
                int a = (int)Math.Floor((double)s * _n / steps), b = Math.Max(a + 1, (int)Math.Floor((double)(s + 1) * _n / steps));
                double lvl = -120, g = 0; int kind = 0;
                for (int i = a; i < b && i < _n; i++)
                {
                    lvl = Math.Max(lvl, _lvl[i]);
                    int k = (int)Math.Round(_kind[i]);
                    if (k > kind || k == kind && _gain[i] > g) { kind = Math.Max(kind, k); g = Math.Max(g, _gain[i]); }
                }
                double v = Math.Clamp((lvl + 48) / 48, 0, 1);
                double bh = Math.Max(2, v * (bot - top));
                var r = new Rect(x0 + s * sw + gap / 2, bot - bh, Math.Max(1, sw - gap), bh);
                IBrush fill = kind switch
                {
                    1 => NotaPalette.Accent,
                    2 => NotaPalette.Wash(NotaPalette.Accent, (byte)Math.Clamp(60 + 150 * g, 50, 210)),
                    _ => NotaPalette.Wash(NotaPalette.TextTertiary, 0x38),
                };
                ctx.DrawRectangle(fill, null, r, 2, 2);
            }

        // Interval boundaries (dashed) and the trigger points (brass ticks at the top).
        var dash = new Pen(NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 2, 3 }, 0));
        for (double b = _intervalBeats; b < _winBeats - 1e-6; b += _intervalBeats) ctx.DrawLine(dash, new Point(X(b), top - 2), new Point(X(b), bot));
        for (double b = _offset * _intervalBeats; b < _winBeats - 1e-6; b += _intervalBeats)
        {
            double x = X(b);
            ctx.DrawLine(new Pen(NotaPalette.Accent, 1.4), new Point(x, top - 3), new Point(x, top + 3));
        }

        // Playhead.
        if (_playing)
        {
            double px = x0 + Math.Clamp(_phase, 0, 1) * (x1 - x0);
            ctx.DrawLine(new Pen(NotaPalette.AccentBright, 2), new Point(px, top - 4), new Point(px, bot));
        }

        NotaGraph.Title(ctx, frame, _title);
        NotaGraph.Legend(ctx, w - 6, 3, ("capture", NotaPalette.AccentBright, NotaGraph.Mark.Area),
            ("repeat · decay", NotaPalette.AccentDim, NotaGraph.Mark.Area), ("dry", NotaPalette.TextTertiary, NotaGraph.Mark.Area));

        // Bar numbers along the bottom (the window starts on a bar when the interval is ≥ a bar).
        int bars = (int)Math.Ceiling(_winBeats / _barBeats - 1e-6);
        if (bars >= 1 && _winBeats >= _barBeats)
            for (int b = 0; b <= bars; b++)
            {
                double bx = X(b * _barBeats);
                if (bx > x1 - 4) { NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, NotaNum.F($"{b + 1}")); break; }
                ctx.DrawText(NotaGraph.AxisText(NotaNum.F($"{b + 1}")), new Point(bx, h - 11));
            }
        else
        {
            NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "beat 1");
            NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, NotaNum.F($"{_winBeats:0.##}\u2009beats"));
        }
    }
}

internal sealed class BrSlicesView : ShDragView
{
    public const int HOffset = 0, HGate = 1;
    private float[] _wave = Array.Empty<float>();
    private int _n;
    private double _offset, _gate = 0.5, _slice = 1.0 / 16, _gridFrac = 1.0 / 16;
    private bool _repeating;

    /// <summary>The interval's input peaks, the offset and gate as fractions of the interval
    /// (steps / 16), the slice and grid step as fractions of it, and whether a repeat plays.</summary>
    public void Set(float[] src, int at, int n, double offset, double gate, double slice, double gridFrac, bool repeating)
    {
        if (_wave.Length != n) _wave = new float[n];
        if (n > 0 && at + n <= src.Length) Array.Copy(src, at, _wave, 0, n);
        _n = n; _offset = offset; _gate = gate; _slice = slice; _gridFrac = Math.Max(1.0 / 256, gridFrac); _repeating = repeating;
        InvalidateVisual();
    }

    private double X0 => 4;
    private double W => Math.Max(1, Bounds.Width - 8);
    private double Xf(double frac) => X0 + Math.Clamp(frac, 0, 1) * W;
    private double GateEnd => Math.Min(1, _offset + Math.Max(_gate, _slice));

    protected override int Hit(Point p)
    {
        double dOff = Math.Abs(p.X - Xf(_offset)), dGate = Math.Abs(p.X - Xf(GateEnd));
        if (dGate <= 8 && dGate < dOff) return HGate;
        return HOffset;   // anywhere else: the offset, the window's main handle
    }
    protected override StandardCursorType CursorFor(int handle) => StandardCursorType.SizeWestEast;
    protected override double DragTo(int handle, Point start, Point p, double v0)
    {
        double f = Math.Clamp((p.X - X0) / W, 0, 1);
        double v = handle == HOffset ? f : f - _offset;
        return Math.Round(Math.Clamp(v, 0, 1) * 16) / 16;   // 16ths of the interval
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double top = 16, bot = h - 13, mid = (top + bot) / 2, amp = (bot - top) / 2;

        // Gate window shaded from the offset.
        ctx.FillRectangle(NotaPalette.AccentSubtle, new Rect(Xf(_offset), 0, Math.Max(1, Xf(GateEnd) - Xf(_offset)), h));

        // Grid steps (dim) and the 16ths of the interval inside the gate (dashed).
        int steps = (int)Math.Clamp(Math.Round(1 / _gridFrac), 1, 128);
        for (int s = 1; s < steps; s++)
        {
            double f = (double)s / steps;
            ctx.DrawLine(NotaGraph.GridPen, new Point(Xf(f), 0), new Point(Xf(f), h));
        }
        ctx.DrawLine(NotaGraph.GridPen, new Point(0, mid), new Point(w, mid));

        // The interval's input (teal), the captured slice (brass).
        if (_n > 1)
        {
            var inPen = new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xB0), Math.Max(1, Math.Min(2, W / _n * 0.6)));
            var slicePen = new Pen(NotaPalette.AccentBright, Math.Max(1.2, Math.Min(2.2, W / _n * 0.7)));
            double s0 = _offset, s1 = Math.Min(1, _offset + _slice);
            for (int i = 0; i < _n; i++)
            {
                double f = (i + 0.5) / _n;
                double a = Math.Clamp(_wave[i], 0, 1.2) / 1.2;
                a = a > 0 ? Math.Max(0.02, Math.Sqrt(a)) : 0;   // perceptual: quiet material stays visible
                if (a <= 0) continue;
                double x = Xf(f);
                bool inSlice = f >= s0 && f < s1;
                ctx.DrawLine(inSlice ? slicePen : inPen, new Point(x, mid - a * amp), new Point(x, mid + a * amp));
            }
        }

        bool hotO = Active == HOffset, hotG = Active == HGate;
        ctx.DrawLine(new Pen(NotaPalette.Accent, hotO ? 1.6 : 1.1), new Point(Xf(_offset), 0), new Point(Xf(_offset), h));
        ctx.DrawLine(new Pen(hotG ? NotaPalette.Accent : NotaPalette.AccentDim, hotG ? 1.6 : 1.1, new DashStyle(new double[] { 3, 3 }, 0)),
            new Point(Xf(GateEnd), 0), new Point(Xf(GateEnd), h));

        NotaGraph.Title(ctx, frame, _repeating ? "Captured sound · repeating" : "Captured sound");
        NotaGraph.Legend(ctx, w - 6, 3, ("slice", NotaPalette.AccentBright, NotaGraph.Mark.Line), ("input", NotaPalette.TealBright, NotaGraph.Mark.Line));
        int off = (int)Math.Round(_offset * 16), gate = (int)Math.Round(_gate * 16);
        double ox = Math.Clamp(Xf(_offset) + 3, 4, w - 110);
        Text(ctx, NotaNum.F($"offset {off}/16"), ox, h - 11, NotaPalette.AccentBright);
        double gx = Math.Clamp(Xf(GateEnd) + 3, ox + 62, w - 70);
        if (gx < w - 60) Text(ctx, NotaNum.F($"gate {gate}/16"), gx, h - 11, NotaPalette.AccentDim);
        TextR(ctx, "16/16", w - 4, h - 11);
    }
}

internal sealed class BrFilterView : ShDragView
{
    public const int HFreq = 0, HWidthLo = 1, HWidthHi = 2;
    private const double FMin = 50, FMax = 18000, DbTop = 12, DbBot = -36;
    private double _hz = 949, _oct = 2, _laterHz = 949, _laterOct = 2;
    private int _type = 1, _laterRepeat;
    private bool _on;

    /// <summary>The filter for the first repeat (Hz, octaves), the type 0 LP / 1 BP / 2 HP,
    /// and a later repeat's band (<paramref name="laterRepeat"/> = 0 hides it).</summary>
    public void Set(bool on, int type, double hz, double oct, int laterRepeat, double laterHz, double laterOct)
    {
        _on = on; _type = type; _hz = hz; _oct = oct; _laterRepeat = laterRepeat; _laterHz = laterHz; _laterOct = laterOct;
        InvalidateVisual();
    }

    private double X(double f) => Math.Log(Math.Clamp(f, FMin, FMax) / FMin) / Math.Log(FMax / FMin) * Bounds.Width;
    private double FAt(double x) => FMin * Math.Pow(FMax / FMin, Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1));
    private double Top => 16;
    private double Bot => Bounds.Height - 13;
    private double Yd(double db) => Top + Math.Clamp((DbTop - db) / (DbTop - DbBot), 0, 1) * (Bot - Top);

    private static double Q(double oct, int type)
    {
        double n2 = Math.Pow(2, oct), q = Math.Sqrt(n2) / (n2 - 1);
        return type == 1 ? q : Math.Max(0.5, q);
    }
    private static double Mag(double f, double fc, double oct, int type)
    {
        double x = f / fc, q = Q(oct, type);
        double den = Math.Sqrt(Math.Pow(1 - x * x, 2) + Math.Pow(x / q, 2));
        double m = type switch { 0 => 1 / den, 2 => x * x / den, _ => x / q / den };
        return 20 * Math.Log10(Math.Max(1e-6, m));
    }
    private (double lo, double hi) Band(double hz, double oct) => (hz / Math.Pow(2, oct / 2), hz * Math.Pow(2, oct / 2));

    protected override int Hit(Point p)
    {
        if (!_on) return -1;
        var (lo, hi) = Band(_hz, _oct);
        double dF = Math.Abs(p.X - X(_hz)), dL = Math.Abs(p.X - X(lo)), dH = Math.Abs(p.X - X(hi));
        if (_type == 1 && dL < dF && dL <= 8) return HWidthLo;
        if (_type == 1 && dH < dF && dH <= 8) return HWidthHi;
        return HFreq;
    }
    protected override StandardCursorType CursorFor(int handle) => StandardCursorType.SizeWestEast;
    protected override double DragTo(int handle, Point start, Point p, double v0)
    {
        double f = FAt(p.X);
        if (handle == HFreq) return Math.Log(f / FMin) / Math.Log(FMax / FMin);
        double oct = 2 * Math.Abs(Math.Log2(f / _hz));
        return (oct - 0.5) / 3;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        Grid(ctx, w, h, 5);
        ctx.DrawLine(new Pen(NotaPalette.GraphBorder, 1), new Point(0, Yd(0)), new Point(w, Yd(0)));

        StreamGeometry Curve(double hz, double oct)
        {
            var g = new StreamGeometry();
            int px = Math.Max(24, (int)(w / 2));
            using var c = g.Open();
            for (int i = 0; i <= px; i++)
            {
                double x = (double)i / px * w;
                var pt = new Point(x, Yd(Mag(FAt(x), hz, oct, _type)));
                if (i == 0) c.BeginFigure(pt, false); else c.LineTo(pt);
            }
            return g;
        }

        if (_on && _laterRepeat > 1)
            ctx.DrawGeometry(null, new Pen(NotaPalette.Teal, 1.2, new DashStyle(new double[] { 3, 3 }, 0)), Curve(_laterHz, _laterOct));
        ctx.DrawGeometry(null, _on ? NotaGraph.PrimaryPen : NotaGraph.SecondaryPen(NotaPalette.BorderStrong, 1.6), Curve(_hz, _oct));

        if (_on)
        {
            var node = new Point(X(_hz), Yd(Mag(_hz, _hz, _oct, _type)));
            if (_type == 1)
            {
                var (lo, hi) = Band(_hz, _oct);
                double y = Yd(-3);
                var edgePen = new Pen(Active is HWidthLo or HWidthHi ? NotaPalette.Accent : NotaPalette.AccentDim, 1, new DashStyle(new double[] { 2, 3 }, 0));
                ctx.DrawLine(edgePen, new Point(X(lo), y), new Point(X(hi), y));
                Text(ctx, NotaNum.F($"{_oct:0.0}\u2009oct"), Math.Clamp(X(lo) + 2, 4, w - 50), y + 2, NotaPalette.AccentDim);
            }
            NotaGraph.Node(ctx, node, Active == HFreq || Active < 0);
        }

        NotaGraph.Title(ctx, frame, _on ? "Repeat filter" : "Repeat filter · off");
        if (_on && _laterRepeat > 1)
            NotaGraph.Legend(ctx, w - 6, 3, ("repeat 1", NotaPalette.AccentBright, NotaGraph.Mark.Line),
                (NotaNum.F($"repeat {_laterRepeat}"), NotaPalette.TealBright, NotaGraph.Mark.Dashed));
        static string Hz(double f) => f >= 1000 ? NotaNum.F($"{f / 1000:0.0}\u2009k") : NotaNum.F($"{f:0}\u2009Hz");
        Text(ctx, "50", 4, h - 11);
        TextR(ctx, "18\u2009k", w - 4, h - 11);
        if (_on) Text(ctx, Hz(_hz), Math.Clamp(X(_hz) - 14, 22, w - 60), h - 11, NotaPalette.AccentBright);
    }
}
