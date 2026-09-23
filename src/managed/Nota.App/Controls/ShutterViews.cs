// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Shutter (device kind 19) card windows, all read from the engine's telemetry
// (Shutter.h scopeRead) so the picture is what the gate does:
//  · ShPill — a STATE column pill meter: a level rising from the bottom (input / key, with
//    the threshold tick) or a reduction hanging from the top.
//  · ShSignalView — the Signal tab: the input over the window as teal columns, the gated
//    output in brass, the threshold (brass dashed) and the close level (Ink dashed) where
//    they act. Drag a line up or down to set the threshold / the return.
//  · ShEnvelopeView — the Envelope tab: one opening drawn from the attack / hold / release /
//    shape / floor (inverted for Duck). Drag the nodes sideways for the times, the floor up
//    or down.
//  · ShKeyView — the Sidechain tab: the detector band-pass (teal, 12 dB/oct) with its HP /
//    LP nodes to drag, under the reduction over the window (brass).
// Shared drag contract (ShDragView): left button only, a gesture per drag, double-click
// resets the handle, right-click bubbles to the MIDI-learn menu.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class ShPill : Control
{
    private double _v, _tick = double.NaN;
    public bool FromTop { get; init; }
    public IBrush Ink { get; init; } = NotaPalette.Accent;

    public ShPill() { Width = 16; }

    /// <summary>The fill 0..1 and the threshold tick 0..1 (NaN = none).</summary>
    public void Set(double v, double tick)
    {
        v = Math.Clamp(v, 0, 1);
        if (Math.Abs(v - _v) < 1e-3 && (tick.Equals(_tick) || Math.Abs(tick - _tick) < 1e-3)) return;
        _v = v; _tick = tick; InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 8) return;
        var rect = new Rect(0.5, 0.5, w - 1, h - 1);
        double r = Math.Min(8, w / 2);
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.BorderDefault, 1), rect, r, r);
        using (ctx.PushClip(new RoundedRect(rect, r)))
        {
            double y = FromTop ? _v * h : (1 - _v) * h;
            var fill = FromTop ? new Rect(0, 0, w, y) : new Rect(0, y, w, h - y);
            ctx.FillRectangle(NotaPalette.Wash((SolidColorBrush)Ink, 0x48), fill);
            if (_v > 0.004) ctx.FillRectangle(Ink, new Rect(1.5, Math.Clamp(y - 1, 0, h - 2), w - 3, 2));
            if (!double.IsNaN(_tick))
            {
                double ty = (1 - Math.Clamp(_tick, 0, 1)) * h;
                ctx.DrawLine(new Pen(NotaPalette.Threshold, 1, new DashStyle(new double[] { 2, 2 }, 0)), new Point(0, ty), new Point(w, ty));
            }
        }
    }
}

/// <summary>A window with draggable handles: subclasses say which handle is under the pointer
/// and what value a drag gives it.</summary>
internal abstract class ShDragView : Control
{
    private int _drag = -1;
    private Point _start;
    private double _v0;

    public event Action<int>? GestureBegin;
    public event Action<int>? GestureEnd;
    public event Action<int, double>? Changed;
    public event Action<int>? ResetRequested;
    /// <summary>The handle's current value (0..1), read when a drag starts.</summary>
    public Func<int, double>? Value { get; set; }
    public bool Dragging => _drag >= 0;
    protected int Active => _drag;

    protected ShDragView() { ClipToBounds = true; MinHeight = 36; }

    /// <summary>The handle under <paramref name="p"/>, or −1.</summary>
    protected abstract int Hit(Point p);
    /// <summary>The handle's new value for a drag from <paramref name="start"/> (value v0) to <paramref name="p"/>.</summary>
    protected abstract double DragTo(int handle, Point start, Point p, double v0);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int hdl = Hit(e.GetPosition(this));
        if (hdl < 0) return;
        if (e.ClickCount == 2) { ResetRequested?.Invoke(hdl); InvalidateVisual(); e.Handled = true; return; }
        _drag = hdl; _start = e.GetPosition(this); _v0 = Value?.Invoke(hdl) ?? 0;
        GestureBegin?.Invoke(hdl); e.Pointer.Capture(this); e.Handled = true;
        Move(e.GetPosition(this));
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag >= 0) { Move(p); return; }
        Cursor = Hit(p) is var h && h >= 0 ? new Cursor(CursorFor(h)) : Cursor.Default;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        int h = _drag; _drag = -1; e.Pointer.Capture(null); GestureEnd?.Invoke(h); InvalidateVisual();
    }
    protected virtual StandardCursorType CursorFor(int handle) => StandardCursorType.SizeNorthSouth;

    private void Move(Point p)
    {
        double v = Math.Clamp(DragTo(_drag, _start, p, _v0), 0, 1);
        Changed?.Invoke(_drag, v);
        InvalidateVisual();
    }

    protected static void Text(DrawingContext ctx, string s, double x, double y, IBrush? ink = null)
        => ctx.DrawText(NotaGraph.AxisText(s, ink), new Point(x, y));
    protected static void TextR(DrawingContext ctx, string s, double right, double y, IBrush? ink = null)
    {
        var ft = NotaGraph.AxisText(s, ink);
        ctx.DrawText(ft, new Point(right - ft.Width, y));
    }
    protected static void Grid(DrawingContext ctx, double w, double h, int cols)
    {
        for (int i = 1; i < 4; i++) ctx.DrawLine(NotaGraph.GridPen, new Point(0, h * i / 4), new Point(w, h * i / 4));
        for (int i = 1; i < cols; i++) ctx.DrawLine(NotaGraph.GridPen, new Point(w * i / cols, 0), new Point(w * i / cols, h));
    }
}

internal sealed class ShSignalView : ShDragView
{
    public const int HThreshold = 0, HReturn = 1;
    private const double DbTop = 0, DbBot = -72;
    private float[] _in = Array.Empty<float>(), _gate = Array.Empty<float>();
    private int _n;
    private double _thr = -38, _ret = -41;
    private string _window = "";
    private int _closing = -1;
    private bool _duck;

    public void Set(float[] src, int inAt, int gateAt, int n, double thrDb, double retDb, bool duck, string window)
    {
        if (_in.Length != n) { _in = new float[n]; _gate = new float[n]; }
        if (n > 0 && inAt + n <= src.Length && gateAt + n <= src.Length) { Array.Copy(src, inAt, _in, 0, n); Array.Copy(src, gateAt, _gate, 0, n); }
        _n = n; _thr = thrDb; _ret = retDb; _duck = duck; _window = window;
        // Where the gate last closed: the newest column that went from open to shut.
        _closing = -1;
        for (int i = n - 1; i > 0; i--)
            if (_gate[i] < 0.5f && _gate[i - 1] >= 0.5f) { _closing = i; break; }
        InvalidateVisual();
    }

    private (double top, double bot) Band() => (14, Bounds.Height - 12);
    private double Y(double db) { var (t, b) = Band(); return t + Math.Clamp((DbTop - db) / (DbTop - DbBot), 0, 1) * (b - t); }
    private double DbAt(double y) { var (t, b) = Band(); return DbTop - Math.Clamp((y - t) / Math.Max(1, b - t), 0, 1) * (DbTop - DbBot); }

    protected override int Hit(Point p)
    {
        double dt = Math.Abs(p.Y - Y(_thr)), dr = Math.Abs(p.Y - Y(_ret));
        if (dt <= 6 && dt <= dr + 1) return HThreshold;
        if (dr <= 6) return HReturn;
        return HThreshold;   // anywhere else: the threshold, the graph's main handle
    }
    protected override double DragTo(int handle, Point start, Point p, double v0)
    {
        double db = DbAt(p.Y);
        return handle == HThreshold ? (db + 70) / 70 : (_thr - db) / 24;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        Grid(ctx, w, h, 6);
        double gx = 4, gw = w - 8;
        double X(int i) => gx + (i + 0.5) / Math.Max(1, _n) * gw;

        if (_n > 1)
        {
            // Input: a teal column per slice.
            var inPen = new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x70), Math.Max(1, Math.Min(2.4, gw / _n * 0.55)));
            double floorY = Y(DbBot);
            for (int i = 0; i < _n; i++)
                if (_in[i] > DbBot + 0.5) ctx.DrawLine(inPen, new Point(X(i), floorY), new Point(X(i), Y(_in[i])));

            // Output: the input times the gate, in brass.
            var g = new StreamGeometry();
            using (var c = g.Open())
                for (int i = 0; i < _n; i++)
                {
                    double gain = Math.Max(1e-6, _gate[i]);
                    double db = _in[i] + 20 * Math.Log10(gain);
                    var pt = new Point(X(i), Y(Math.Max(DbBot, db)));
                    if (i == 0) c.BeginFigure(pt, false); else c.LineTo(pt);
                }
            ctx.DrawGeometry(null, NotaGraph.PrimaryPen, g);
        }

        bool hotT = Active == HThreshold, hotR = Active == HReturn;
        ctx.DrawLine(new Pen(NotaPalette.Accent, hotT ? 1.4 : 1, new DashStyle(new double[] { 4, 4 }, 0)), new Point(0, Y(_thr)), new Point(w, Y(_thr)));
        ctx.DrawLine(new Pen(hotR ? NotaPalette.AccentDim : NotaPalette.BorderStrong, 1, new DashStyle(new double[] { 2, 4 }, 0)), new Point(0, Y(_ret)), new Point(w, Y(_ret)));

        NotaGraph.Title(ctx, frame, _duck ? "Signal and duck" : "Signal and gate");
        NotaGraph.Legend(ctx, w - 6, 3, ("input", NotaPalette.TealBright, NotaGraph.Mark.Area), ("output", NotaPalette.AccentBright, NotaGraph.Mark.Line));
        var thrTxt = NotaNum.F($"threshold {_thr:0.0}");
        double ty = Y(_thr) - 11;
        if (ty < 14) ty = Y(_thr) + 2;
        Text(ctx, thrTxt, 6, ty, NotaPalette.AccentBright);
        Text(ctx, "−" + _window, 6, h - 11);
        TextR(ctx, "now", w - 6, h - 11);
        if (_closing > _n * 0.12 && _closing < _n * 0.85)
            Text(ctx, _duck ? "released" : "closed", X(_closing) + 3, h - 11);
    }
}

internal sealed class ShEnvelopeView : ShDragView
{
    public const int HAttack = 0, HHold = 1, HRelease = 2, HFloor = 3;
    private double _atk = 1, _hold = 15, _rel = 120, _floor, _span = 250;
    private int _shape = 1;
    private bool _duck, _retrig;
    private string _floorText = "", _atkText = "", _holdText = "", _relText = "";
    private static readonly double[] Spans = { 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000 };

    /// <summary>Times in ms, the floor as linear gain (0 = −∞), the shape 0 linear / 1 log /
    /// 2 snap, duck = inverted, trigger mode, and the captions.</summary>
    public void Set(double atkMs, double holdMs, double relMs, double floorLin, int shape, bool duck, bool retrig,
        string floorText, string atkText, string holdText, string relText)
    {
        _atk = atkMs; _hold = holdMs; _rel = relMs; _floor = floorLin; _shape = shape; _duck = duck; _retrig = retrig;
        _floorText = floorText; _atkText = atkText; _holdText = holdText; _relText = relText;
        if (!Dragging)
        {
            double need = shape == 1 ? (atkMs * 3 + holdMs + relMs * 3) * 1.2 : (atkMs + holdMs + relMs) * 1.45;   // Log keeps approaching
            _span = Spans[^1];
            foreach (double s in Spans) if (s >= need) { _span = s; break; }
        }
        InvalidateVisual();
    }

    private double Top => 16;
    private double Bot => Bounds.Height - 14;
    private double X0 => Bounds.Width * 0.12;
    private double Xt(double ms) => X0 + ms / _span * Bounds.Width;
    private double Yg(double g) => Top + (1 - Math.Clamp(g, 0, 1)) * (Bot - Top);
    private double Gain(double engaged) => _duck ? 1 - (1 - _floor) * engaged : _floor + (1 - _floor) * engaged;
    private double Shape(double e) => _shape == 2 ? 1 - Math.Pow(1 - e, 3) : e;

    // Engaged fraction t ms after the opening: Linear / Snap ramp over exactly the times; Log is
    // the one-pole the engine runs (the times are its time constants), so it keeps approaching.
    private double E(double t)
    {
        if (t <= 0) return 0;
        double ah = _atk + _hold;
        if (_shape == 1)
        {
            double eh = 1 - Math.Exp(-ah / Math.Max(1e-3, _atk));
            return t < ah ? 1 - Math.Exp(-t / Math.Max(1e-3, _atk)) : eh * Math.Exp(-(t - ah) / Math.Max(1e-3, _rel));
        }
        if (t < _atk) return t / Math.Max(1e-3, _atk);
        if (t < ah) return 1;
        return Math.Max(0, 1 - (t - ah) / Math.Max(1e-3, _rel));
    }
    private double Yt(double t) => Yg(Gain(Shape(E(t))));

    private Point Node(int hdl) => hdl switch
    {
        HAttack => new Point(Xt(_atk), Yt(_atk)),
        HHold => new Point(Xt(_atk + _hold), Yt(_atk + _hold)),
        HRelease => new Point(Xt(_atk + _hold + _rel), Yt(_atk + _hold + _rel)),
        _ => new Point(Bounds.Width - 14, Yg(_floor)),
    };

    protected override int Hit(Point p)
    {
        int best = -1; double bd = 14;
        for (int k = 0; k < 4; k++)
        {
            var n = Node(k);
            double d = Math.Abs(p.X - n.X) + Math.Abs(p.Y - n.Y) * 0.5;
            if (d < bd) { bd = d; best = k; }
        }
        if (best >= 0) return best;
        return Math.Abs(p.Y - Yg(_floor)) < 6 ? HFloor : -1;
    }
    protected override StandardCursorType CursorFor(int handle) => handle == HFloor ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast;
    protected override double DragTo(int handle, Point start, Point p, double v0)
    {
        if (handle == HFloor)
        {
            double dy = (start.Y - p.Y) / Math.Max(40, Bot - Top);
            return v0 + (_duck ? -dy : dy) * 0.9;
        }
        return v0 + (p.X - start.X) / Math.Max(60, Bounds.Width) * 0.8;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        for (int i = 1; i < 4; i++) ctx.DrawLine(NotaGraph.GridPen, new Point(0, h * i / 4), new Point(w, h * i / 4));

        double xa = Xt(_atk), xh = Xt(_atk + _hold), xr = Xt(_atk + _hold + _rel);
        var mark = new Pen(NotaPalette.GraphBorder, 1, new DashStyle(new double[] { 3, 4 }, 0));
        foreach (double x in new[] { X0, xa, xh, xr }) if (x < w) ctx.DrawLine(mark, new Point(x, 0), new Point(x, h));

        // Floor (or the duck depth): teal dashed.
        double yf = Yg(_floor);
        ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xA0), 1, new DashStyle(new double[] { 4, 4 }, 0)), new Point(0, yf), new Point(w, yf));

        // One opening: rest → attack → hold → release → rest, sampled with the breakpoints kept exact.
        var ts = new List<double>();
        int px = Math.Max(40, (int)(w / 1.5));
        for (int k = 0; k <= px; k++) ts.Add(((double)k / px * w - X0) / w * _span);
        ts.Add(0); ts.Add(_atk); ts.Add(_atk + _hold); ts.Add(_atk + _hold + _rel);
        ts.Sort();
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            bool first = true;
            foreach (double t in ts)
            {
                var pt = new Point(Xt(Math.Max(t, -X0 / w * _span)), Yt(t));
                if (first) { c.BeginFigure(pt, false); first = false; } else c.LineTo(pt);
            }
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, g);
        for (int k = 0; k < 3; k++) { var n = Node(k); if (n.X < w - 2) NotaGraph.Node(ctx, n, Active == k || Active < 0 && k == 0); }
        NotaGraph.Node(ctx, Node(HFloor), Active == HFloor, NotaPalette.Teal);

        NotaGraph.Title(ctx, frame, _duck ? "Duck envelope" : _retrig ? "Gate envelope · trigger" : "Gate envelope");
        TextR(ctx, "open 0\u2009dB", w - 6, 3, NotaPalette.AccentBright);
        double fy = _duck ? yf + 2 : yf - 11;
        if (fy > h - 24) fy = yf - 11;
        TextR(ctx, (_duck ? "duck " : "floor ") + _floorText, w - 22, fy, NotaPalette.TealBright);
        double lx = Math.Max(4, X0 + (xa - X0) / 2 - 10);
        Text(ctx, "atk " + _atkText, Math.Min(lx, w * 0.2), h - 11);
        Text(ctx, "hold " + _holdText, Math.Clamp((xa + xh) / 2 - 12, w * 0.24, w * 0.5), h - 11);
        Text(ctx, "rel " + _relText, Math.Clamp((xh + xr) / 2 - 12, w * 0.52, w - 70), h - 11);
    }
}

internal sealed class ShKeyView : ShDragView
{
    public const int HHp = 0, HLp = 1;
    private const double FMin = 20, FMax = 20000, DbRange = 36;
    private double _hpHz = 80, _lpHz = 2500;
    private bool _on = true;
    private float[] _gate = Array.Empty<float>();
    private int _n;
    private string _legend = "";

    public void Set(double hpHz, double lpHz, bool filterOn, float[] src, int gateAt, int n, string legend)
    {
        _hpHz = hpHz; _lpHz = lpHz; _on = filterOn; _legend = legend;
        if (_gate.Length != n) _gate = new float[n];
        if (n > 0 && gateAt + n <= src.Length) Array.Copy(src, gateAt, _gate, 0, n);
        _n = n;
        InvalidateVisual();
    }

    private double X(double f) => Math.Log(f / FMin) / Math.Log(FMax / FMin) * Bounds.Width;
    private double FAt(double x) => FMin * Math.Pow(FMax / FMin, Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1));
    private double Top => 16;
    private double Bot => Bounds.Height - 14;
    private double Yd(double db) => Top + Math.Clamp(-db / DbRange, 0, 1) * (Bot - Top);
    private double Mag(double f)
    {
        if (!_on) return 0;
        double a = Math.Pow(f / _hpHz, 2), b = Math.Pow(f / _lpHz, 2);
        double hp = a / Math.Sqrt(1 + a * a), lp = 1 / Math.Sqrt(1 + b * b);
        return 20 * Math.Log10(Math.Max(1e-6, hp * lp));
    }

    protected override int Hit(Point p)
    {
        if (!_on) return -1;
        double dh = Math.Abs(p.X - X(_hpHz)), dl = Math.Abs(p.X - X(_lpHz));
        return dh <= dl ? HHp : HLp;
    }
    protected override StandardCursorType CursorFor(int handle) => StandardCursorType.SizeWestEast;
    protected override double DragTo(int handle, Point start, Point p, double v0)
    {
        double f = FAt(p.X);
        return handle == HHp ? Math.Log(f / 20) / Math.Log(100) : Math.Log(f / 200) / Math.Log(100);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        Grid(ctx, w, h, 5);

        // The reduction over the window (time runs left → right), under the filter.
        if (_n > 1)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
                for (int i = 0; i < _n; i++)
                {
                    double red = 1 - Math.Clamp(_gate[i], 0, 1);
                    var pt = new Point((i + 0.5) / _n * w, Bot - red * (Bot - Top) * 0.85);
                    if (i == 0) c.BeginFigure(pt, false); else c.LineTo(pt);
                }
            ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.Accent, 0xB0), 1.3, lineJoin: PenLineJoin.Round), g);
        }

        // The key band-pass.
        var curve = new StreamGeometry();
        int px = Math.Max(24, (int)(w / 2));
        using (var c = curve.Open())
            for (int i = 0; i <= px; i++)
            {
                double x = (double)i / px * w;
                var pt = new Point(x, Yd(Mag(FAt(x))));
                if (i == 0) c.BeginFigure(pt, false); else c.LineTo(pt);
            }
        ctx.DrawGeometry(null, NotaGraph.SecondaryPen(_on ? NotaPalette.Teal : NotaPalette.BorderStrong, 1.8), curve);
        if (_on)
        {
            NotaGraph.Node(ctx, new Point(X(_hpHz), Yd(Mag(_hpHz))), Active == HHp, NotaPalette.Teal);
            NotaGraph.Node(ctx, new Point(X(_lpHz), Yd(Mag(_lpHz))), Active == HLp, NotaPalette.Teal);
        }

        NotaGraph.Title(ctx, frame, _on ? "Detector filter" : "Detector · full band");
        NotaGraph.Legend(ctx, w - 6, 3, ("key", NotaPalette.TealBright, NotaGraph.Mark.Line), (_legend, NotaPalette.AccentBright, NotaGraph.Mark.Line));
        static string Hz(double f) => f >= 1000 ? NotaNum.F($"{f / 1000:0.0}\u2009k") : NotaNum.F($"{f:0}");
        Text(ctx, "20", 4, h - 11);
        TextR(ctx, "20\u2009k", w - 4, h - 11);
        if (_on)
        {
            double hx = Math.Clamp(X(_hpHz) - 8, 22, w - 90);
            double lx = Math.Clamp(X(_lpHz) - 8, hx + 30, w - 50);
            Text(ctx, Hz(_hpHz), hx, h - 11, NotaPalette.TealBright);
            Text(ctx, Hz(_lpHz), lx, h - 11, NotaPalette.TealBright);
        }
    }
}
