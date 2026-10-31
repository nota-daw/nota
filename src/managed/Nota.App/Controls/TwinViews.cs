// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — Nota Twin's own views (specs/055).
//
// TwinExpressionPad: the three-axis expression pad (FR-031), a mouse player's stand-in for a
// pressure-sensitive surface. Drag moves X (left → right) and Y (bottom → top); holding the
// button down raises Z — the "press" — over half a second, and letting go lets it fall back over
// a quarter. The axes are ordinary params (pad X / Y / Z), so they automate and learn like any
// knob; the card names what each one drives. It is a graph window (NotaGraph): well, hairline,
// crosshair in the grid colour; the point is neutral ink at rest and the accent while held, with
// a ring that grows with Z. Left button only; double-click centres X / Y.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Nota.App;

internal sealed class TwinExpressionPad : Control
{
    private const double RiseSec = 0.5, FallSec = 0.25;
    private double _x = 0.5, _y = 0.5, _z;
    private bool _drag;
    private readonly DispatcherTimer _tick;
    private DateTime _last;

    /// <summary>X and Y moved by hand (0..1, Y up).</summary>
    public Action<double, double>? XyChanged;
    /// <summary>Z moved by the hold (0..1).</summary>
    public Action<double>? ZChanged;
    public Action? GestureBegin;
    public Action? GestureEnd;
    /// <summary>Double-click: X and Y back to the centre.</summary>
    public Action? Reset;

    public TwinExpressionPad()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        DoubleTapped += (_, e) => { Reset?.Invoke(); e.Handled = true; };
        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Step());
    }

    public bool Dragging => _drag || _tick.IsEnabled;

    /// <summary>Follow the params (automation, another view) without firing anything.</summary>
    public void SetValues(double x, double y, double z)
    {
        if (Dragging) return;
        _x = Math.Clamp(x, 0, 1); _y = Math.Clamp(y, 0, 1); _z = Math.Clamp(z, 0, 1);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click bubbles
        if (e.ClickCount > 1) return;                                           // DoubleTapped resets
        if (!_tick.IsEnabled) GestureBegin?.Invoke();
        _drag = true;
        e.Pointer.Capture(this);
        Apply(e.GetPosition(this));
        _last = DateTime.UtcNow;
        _tick.Start();
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
        e.Pointer.Capture(null);
        InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _drag = false;
        InvalidateVisual();
    }

    // Z rises while held and falls once let go; the gesture ends when it has settled at 0.
    private void Step()
    {
        var now = DateTime.UtcNow;
        double dt = (now - _last).TotalSeconds;
        _last = now;
        double z = _drag ? Math.Min(1, _z + dt / RiseSec) : Math.Max(0, _z - dt / FallSec);
        if (z != _z) { _z = z; ZChanged?.Invoke(z); InvalidateVisual(); }
        if (!_drag && _z <= 0)
        {
            _tick.Stop();
            GestureEnd?.Invoke();
        }
    }

    private void Apply(Point p)
    {
        double w = Math.Max(1, Bounds.Width), h = Math.Max(1, Bounds.Height);
        _x = Math.Clamp(p.X / w, 0, 1);
        _y = 1 - Math.Clamp(p.Y / h, 0, 1);
        InvalidateVisual();
        XyChanged?.Invoke(_x, _y);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 8 || r.Height < 8) return;
        NotaGraph.Window(ctx, r);
        ctx.DrawLine(NotaGraph.GridPen, new Point(Math.Round(r.Width / 2) + 0.5, 1), new Point(Math.Round(r.Width / 2) + 0.5, r.Height - 1));
        ctx.DrawLine(NotaGraph.GridPen, new Point(1, Math.Round(r.Height / 2) + 0.5), new Point(r.Width - 1, Math.Round(r.Height / 2) + 0.5));
        Label(ctx, "X", r.Width - 6, r.Height / 2 + 2, true);
        Label(ctx, "Y", r.Width / 2 + 4, 3, false);

        var p = new Point(_x * r.Width, (1 - _y) * r.Height);
        bool live = _drag || _z > 0;
        if (_z > 0.001)
            ctx.DrawEllipse(null, new Pen(NotaPalette.Accent, 1.5), p, 7 + 12 * _z, 7 + 12 * _z);
        ctx.DrawEllipse(live ? NotaPalette.Accent : NotaPalette.VizInk, new Pen(NotaPalette.BgSunken, 2), p, 6, 6);
    }

    private static void Label(DrawingContext ctx, string text, double x, double y, bool rightAlign)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.SansMedium, NotaType.KnobLabel, NotaPalette.TextTertiary);
        ctx.DrawText(ft, new Point(rightAlign ? x - ft.Width : x, y));
    }
}

// A graph on Twin params: reads and writes them by id (the card binds them to the side being
// edited) and frames each drag as an automation-write gesture.
internal abstract class TwinParamGraph : Control
{
    public Func<string, float> Get = _ => 0f;
    public Action<string, float>? Set;
    public Action<string>? Begin, End;
    protected string Prefix = "";
    private string[] _drag = Array.Empty<string>();

    protected TwinParamGraph() { ClipToBounds = true; }

    public void Bind(string prefix) { Prefix = prefix; InvalidateVisual(); }
    public void Refresh() => InvalidateVisual();
    public bool Dragging => _drag.Length > 0;
    protected float P(string id) => Get(Prefix + id);
    protected void Write(string id, double v) { Set?.Invoke(Prefix + id, (float)Math.Clamp(v, 0, 1)); InvalidateVisual(); }

    // Which params a press at `p` grabs (none = not interactive there).
    protected virtual string[] Grab(Point p) => Array.Empty<string>();
    protected virtual void Drag(Point p) { }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (Set is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        _drag = Grab(p);
        if (_drag.Length == 0) return;
        foreach (var id in _drag) Begin?.Invoke(Prefix + id);
        e.Pointer.Capture(this);
        Drag(p);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag.Length > 0) Drag(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) => Finish(e.Pointer);
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => Finish(null);
    private void Finish(IPointer? ptr)
    {
        if (_drag.Length == 0) return;
        foreach (var id in _drag) End?.Invoke(Prefix + id);
        _drag = Array.Empty<string>();
        ptr?.Capture(null);
        InvalidateVisual();
    }
    protected static StreamGeometry Path(Point[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(pts[0], false);
        for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
        c.EndFigure(false);
        return g;
    }
}

// One cycle of an oscillator as the voice makes it: triangle → saw → square by Wave, the pulse
// width, Metal folding the triangle share. Drag sideways to move Wave.
internal sealed class TwinWaveView : TwinParamGraph
{
    private readonly string _o;
    public TwinWaveView(int osc) { _o = $"o{osc}"; Cursor = new Cursor(StandardCursorType.SizeWestEast); }
    protected override string[] Grab(Point p) => new[] { _o + "wave" };
    protected override void Drag(Point p) => Write(_o + "wave", p.X / Math.Max(1, Bounds.Width));

    public static double Sample(double ph, double w, double pw, double metal)
    {
        double gt = Math.Max(0, 1 - 2 * w), gs = 1 - Math.Abs(2 * w - 1), gp = 0.6 * Math.Max(0, 2 * w - 1);
        double duty = 0.05 + 0.9 * pw;
        double tri = ph < 0.5 ? 4 * ph - 1 : 3 - 4 * ph, saw = 2 * ph - 1, pul = (ph < duty ? 1 : -1) - (2 * duty - 1);
        double y = gt * tri + gs * saw + gp * pul;
        double m = metal * gt;
        if (m > 0) y += m * (Math.Sin(Math.PI / 2 * (1 + 3 * m) * y) - y);
        return y;
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 8) return;
        NotaGraph.Window(ctx, r);
        double w = P(_o + "wave"), pw = P(_o + "pw"), mt = P(_o + "metal");
        int n = (int)r.Width;
        var pts = new Point[n];
        for (int i = 0; i < n; i++)
        {
            double y = Sample(i / (double)(n - 1), w, pw, mt);
            pts[i] = new Point(i, r.Height / 2 - y * (r.Height / 2 - 3) / 1.2);
        }
        ctx.DrawGeometry(null, Dragging ? NotaGraph.SecondaryPen(NotaPalette.Accent, 1.4) : NotaGraph.SecondaryPen(NotaPalette.VizInk, 1.2), Path(pts));
    }
}

// The two filters' combined response, as the route and Osc 1's balance put them together: the
// multimode (teal) and the ladder (rose) faint, their sum in viz ink. Drag sets the ladder's
// cutoff (x) and resonance (y).
internal sealed class TwinFilterView : TwinParamGraph
{
    private const double DbTop = 18, DbBottom = -36;
    public TwinFilterView() { Cursor = new Cursor(StandardCursorType.Hand); }
    protected override string[] Grab(Point p) => new[] { "ldcut", "ldres" };
    protected override void Drag(Point p)
    {
        Write("ldcut", p.X / Math.Max(1, Bounds.Width));
        Write("ldres", 1 - p.Y / Math.Max(1, Bounds.Height));
    }

    // Magnitudes at a frequency (normalized 0..1 on the 20 Hz … 20 kHz axis).
    private (double Mm, double Ld, double Sum) At(double x)
    {
        double f = 20 * Math.Pow(1000, x);
        double fm = 20 * Math.Pow(1000, P("mmcut")), fl = 20 * Math.Pow(1000, P("ldcut"));
        // Multimode: the 2-pole state-variable core, its mode a blend of LP / BP / HP / notch.
        var s = new System.Numerics.Complex(0, f / fm);
        double k = 2 - 2.04 * P("mmres");
        var den = s * s + k * s + 1;
        System.Numerics.Complex lp = 1 / den, bp = s / den, hp = s * s / den;
        double m = Math.Clamp(P("mmmode"), 0, 1) * 3;
        System.Numerics.Complex hm = m < 1 ? lp * (1 - m) + bp * (1.6 * m)
            : m < 2 ? bp * (1.6 * (2 - m)) + hp * (m - 1) : hp + lp * (m - 2);
        // Ladder: four one-poles in a resonant loop; the 12 dB tap after the second.
        var g = 1 / (1 + new System.Numerics.Complex(0, f / fl));
        double kl = 4 * Math.Clamp(P("ldres"), 0, 1);
        var g4 = g * g * g * g;
        var hl = (P("ldslope") >= 0.5f ? g4 : g * g) / (1 + kl * g4);
        double b = P("o1bal"), rt = P("route");
        var sum = hl * (b + (1 - rt) * (1 - b) * hm) + rt * (1 - b) * hm;
        return (hm.Magnitude, hl.Magnitude, sum.Magnitude);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 16) return;
        NotaGraph.Window(ctx, r);
        double Y(double mag) => Math.Clamp((DbTop - 20 * Math.Log10(Math.Max(mag, 1e-6))) / (DbTop - DbBottom), 0, 1) * r.Height;
        for (int dec = 1; dec < 3; dec++) // 200 Hz, 2 kHz
        {
            double gx = Math.Round(dec / 3.0 * r.Width) + 0.5;
            ctx.DrawLine(NotaGraph.GridPen, new Point(gx, 1), new Point(gx, r.Height - 1));
        }
        double y0 = Math.Round(Y(1)) + 0.5;
        ctx.DrawLine(NotaGraph.GridPen, new Point(1, y0), new Point(r.Width - 1, y0));
        int n = (int)r.Width;
        Point[] mm = new Point[n], ld = new Point[n], sum = new Point[n];
        for (int i = 0; i < n; i++)
        {
            var (a, bl, c) = At(i / (double)(n - 1));
            mm[i] = new Point(i, Y(a)); ld[i] = new Point(i, Y(bl)); sum[i] = new Point(i, Y(c));
        }
        ctx.DrawGeometry(null, NotaGraph.SecondaryPen(NotaPalette.Teal, 1.0), Path(mm));
        ctx.DrawGeometry(null, NotaGraph.SecondaryPen(NotaPalette.Rose, 1.0), Path(ld));
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, Path(sum));
        NotaGraph.Node(ctx, new Point(P("ldcut") * r.Width, (1 - P("ldres")) * r.Height), Dragging);
        NotaGraph.Axis(ctx, r, NotaGraph.Corner.BottomLeft, "20");
        NotaGraph.Axis(ctx, r, NotaGraph.Corner.BottomRight, "20k");
    }
}

// An envelope: delay (mod env), attack, decay to the sustain level, a held stretch, release —
// each time on its own stretch of the width, so a handle never jumps while another moves.
// Handles: the attack peak (A), the decay corner (D and S), the release end (R).
internal sealed class TwinEnvView : TwinParamGraph
{
    private readonly string _e;
    private readonly bool _delay;
    public TwinEnvView(string env, bool delay) { _e = env; _delay = delay; Cursor = new Cursor(StandardCursorType.Hand); }

    private double Seg => Bounds.Width * (_delay ? 0.22 : 0.27);
    private double Dly => _delay ? P(_e + "dly") * Bounds.Width * 0.12 : 0;
    private Point[] Handles()
    {
        double h = Bounds.Height - 4, top = 3;
        double xa = Dly + P(_e + "a") * Seg, xd = xa + P(_e + "d") * Seg, xs = xd + Bounds.Width * 0.16, xr = xs + P(_e + "r") * Seg;
        double ys = top + (1 - P(_e + "s")) * h;
        return new[] { new Point(xa, top), new Point(xd, ys), new Point(Math.Min(xr, Bounds.Width - 2), top + h) };
    }
    private int _which = -1;
    protected override string[] Grab(Point p)
    {
        var hs = Handles();
        _which = 0;
        double best = double.MaxValue;
        for (int i = 0; i < hs.Length; i++)
        {
            double d = Math.Abs(hs[i].X - p.X) + 0.5 * Math.Abs(hs[i].Y - p.Y);
            if (d < best) { best = d; _which = i; }
        }
        return _which switch { 0 => new[] { _e + "a" }, 1 => new[] { _e + "d", _e + "s" }, _ => new[] { _e + "r" } };
    }
    protected override void Drag(Point p)
    {
        var hs = Handles();
        double h = Bounds.Height - 4;
        switch (_which)
        {
            case 0: Write(_e + "a", (p.X - Dly) / Seg); break;
            case 1: Write(_e + "d", (p.X - hs[0].X) / Seg); Write(_e + "s", 1 - (p.Y - 3) / h); break;
            default: Write(_e + "r", (p.X - (hs[1].X + Bounds.Width * 0.16)) / Seg); break;
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 16) return;
        NotaGraph.Window(ctx, r);
        var hs = Handles();
        double bottom = r.Height - 1;
        double curve = _e == "menv" ? 0.5 : P(_e + "curve");
        double k = 0.4 + 5 * curve; // the attack's bow, as the curve sets the analog overshoot
        var pts = new System.Collections.Generic.List<Point> { new(1, bottom), new(Dly, bottom) };
        for (int i = 1; i <= 16; i++)
        {
            double t = i / 16.0;
            pts.Add(new Point(Dly + (hs[0].X - Dly) * t, bottom - (bottom - hs[0].Y) * (1 - Math.Exp(-k * t)) / (1 - Math.Exp(-k))));
        }
        for (int i = 1; i <= 16; i++)
        {
            double t = i / 16.0;
            pts.Add(new Point(hs[0].X + (hs[1].X - hs[0].X) * t, hs[1].Y + (hs[0].Y - hs[1].Y) * Math.Exp(-4 * t)));
        }
        double xs = hs[1].X + r.Width * 0.16;
        pts.Add(new Point(xs, hs[1].Y));
        for (int i = 1; i <= 16; i++)
        {
            double t = i / 16.0;
            pts.Add(new Point(xs + (hs[2].X - xs) * t, bottom - (bottom - hs[1].Y) * Math.Exp(-4 * t)));
        }
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, Path(pts.ToArray()));
        foreach (var hp in hs) NotaGraph.Node(ctx, hp, Dragging);
    }
}

// One cycle of an LFO's shape, with where it is now (the instrument's live phase).
internal sealed class TwinLfoView : TwinParamGraph
{
    private readonly int _l;
    public double Phase = -1;
    public TwinLfoView(int lfo) { _l = lfo; }

    private static double Shape(int shape, double p, double sym)
    {
        switch (shape)
        {
            case 0: return Math.Sin(2 * Math.PI * p);
            case 1: return p < sym ? 2 * p / Math.Max(sym, 1e-3) - 1 : 1 - 2 * (p - sym) / Math.Max(1 - sym, 1e-3);
            case 2: return 1 - 2 * p;
            case 3: return p < 0.5 ? 1 : -1;
            case 4: { int s = (int)(p * 4); return new[] { 0.6, -0.4, 0.9, -0.8 }[s % 4]; }
            default:
            {
                double[] v = { 0.6, -0.4, 0.9, -0.8, 0.6 };
                double x = p * 4; int s = (int)x; double t = x - s; t = t * t * (3 - 2 * t);
                return v[s] + (v[Math.Min(4, s + 1)] - v[s]) * t;
            }
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 16) return;
        NotaGraph.Window(ctx, r);
        string l = $"lfo{_l}";
        int shape = Math.Clamp((int)Math.Round(P(l + "shape") * 5), 0, 5);
        double sym = _l == 3 ? Math.Clamp(P("lfo3sym"), 0.02, 0.98) : 0.5;
        double ex = _l == 3 ? Math.Pow(2, (P("lfo3curve") - 0.5) * 4) : 1;
        ctx.DrawLine(NotaGraph.GridPen, new Point(1, Math.Round(r.Height / 2) + 0.5), new Point(r.Width - 1, Math.Round(r.Height / 2) + 0.5));
        int n = (int)r.Width;
        var pts = new Point[n];
        for (int i = 0; i < n; i++)
        {
            double y = Shape(shape, i / (double)(n - 1), sym);
            y = Math.Sign(y) * Math.Pow(Math.Abs(y), ex);
            pts[i] = new Point(i, r.Height / 2 - y * (r.Height / 2 - 4));
        }
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, Path(pts));
        if (Phase >= 0)
        {
            double x = Math.Round(Phase * (r.Width - 1)) + 0.5;
            ctx.DrawLine(new Pen(NotaPalette.Accent, 1), new Point(x, 2), new Point(x, r.Height - 2));
        }
    }
}

// The part's morph as it sounds now (the accent mark) and each sounding voice's own morph (dots),
// on a strip from A (left) to B (right).
internal sealed class TwinMorphStrip : Control
{
    private double _part = -1;
    private readonly double[] _voices = new double[12];
    public void Set(double part, ReadOnlySpan<float> voices)
    {
        _part = part;
        for (int i = 0; i < _voices.Length; i++) _voices[i] = i < voices.Length ? voices[i] : -1;
        InvalidateVisual();
    }
    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 8) return;
        double y = r.Height / 2;
        ctx.DrawLine(new Pen(NotaPalette.BgSunken, 3, lineCap: PenLineCap.Round), new Point(3, y), new Point(r.Width - 3, y));   // the groove
        foreach (var v in _voices)
            if (v >= 0) ctx.DrawEllipse(NotaPalette.VizInk, null, new Point(3 + v * (r.Width - 6), y), 2, 2);
        if (_part >= 0)
        {
            double x = 3 + _part * (r.Width - 6);
            ctx.DrawLine(new Pen(NotaPalette.Accent, 2, lineCap: PenLineCap.Round), new Point(x, 1), new Point(x, r.Height - 1));
        }
    }
}

// The sequencer's steps, a page of sixteen: each cell shows its notes, a velocity bar (drag up /
// down) and a gate bar along the bottom (drag sideways; past the edge it ties into the next step).
// A drag edits a preview and commits once, on release — one undo step per gesture. The playing
// step is marked in the accent, the step-record cursor and the selection in ink; a motion lane's
// values ride as teal ticks.
internal sealed class TwinStepGrid : Control
{
    public Nota.Application.Twin.TwinPatterns.Pattern? Pattern;
    public int Page, PlayStep = -1, RecStep = -1, Selected = -1, Lane = 0;
    public Action<int>? SelectStep;
    public Action<int, int>? CommitVelocity; // step, 1..127
    public Action<int, int>? CommitGate;     // step, 1..100 or GateTie

    private int _drag = -1;
    private bool _gate;
    private int _vel, _gt;

    public TwinStepGrid() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Hand); }

    private double CellW => Bounds.Width / 16.0;
    private int StepAt(Point p) => Page * 16 + Math.Clamp((int)(p.X / CellW), 0, 15);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (Pattern is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        int st = StepAt(p);
        if (st >= Pattern.Length) return;
        Selected = st;
        SelectStep?.Invoke(st);
        var s = Pattern.Steps[st];
        _gate = p.Y > Bounds.Height - 14;
        _vel = s.Velocity;
        _gt = s.Gate;
        _drag = s.IsRest ? -1 : st;
        if (_drag >= 0) { e.Pointer.Capture(this); Move(p); }
        InvalidateVisual();
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag >= 0) Move(e.GetPosition(this)); }
    private void Move(Point p)
    {
        double x0 = (_drag - Page * 16) * CellW;
        if (_gate)
        {
            double f = (p.X - x0) / CellW;
            _gt = f > 1.05 ? Nota.Application.Twin.TwinPatterns.GateTie : Math.Clamp((int)Math.Round(f * 100), 1, 100);
        }
        else
        {
            double top = 18, h = Bounds.Height - 18 - 16;
            _vel = Math.Clamp((int)Math.Round((1 - (p.Y - top) / h) * 127), 1, 127);
        }
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag < 0) return;
        int st = _drag;
        _drag = -1;
        e.Pointer.Capture(null);
        if (_gate) CommitGate?.Invoke(st, _gt); else CommitVelocity?.Invoke(st, _vel);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 32) return;
        NotaGraph.Window(ctx, r);
        if (Pattern is null) return;
        double cw = CellW, top = 18, barH = r.Height - top - 16;
        var mono = NotaFonts.Mono;
        for (int c = 0; c < 16; c++)
        {
            int st = Page * 16 + c;
            double x = c * cw;
            if (c > 0) ctx.DrawLine(NotaGraph.GridPen, new Point(Math.Round(x) + 0.5, 2), new Point(Math.Round(x) + 0.5, r.Height - 2));
            if (st >= Pattern.Length) { ctx.FillRectangle(NotaPalette.SurfaceAbyss, new Rect(x + 1, 1, cw - 1, r.Height - 2)); continue; }
            var s = Pattern.Steps[st];
            bool dragging = st == _drag;
            int vel = dragging && !_gate ? _vel : s.Velocity, gate = dragging && _gate ? _gt : s.Gate;
            if (st == PlayStep) ctx.FillRectangle(NotaPalette.Accent, new Rect(x + 2, 2, cw - 4, 2));
            if (!s.IsRest)
            {
                string label = Nota.Application.Twin.TwinPatterns.NoteName(s.Notes[0]) + (s.Notes.Count > 1 ? $"+{s.Notes.Count - 1}" : "");
                var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, mono, 7.5, NotaPalette.TextSecondary);
                ctx.DrawText(ft, new Point(x + Math.Max(1, (cw - ft.Width) / 2), 5));
                double vh = barH * vel / 127.0;
                ctx.FillRectangle(dragging ? NotaPalette.Accent : NotaPalette.VizInk, new Rect(x + cw * 0.3, top + barH - vh, cw * 0.4, vh));
                bool tie = gate >= Nota.Application.Twin.TwinPatterns.GateTie;
                double gw = tie ? cw : cw * gate / 100.0;
                ctx.FillRectangle(tie ? NotaPalette.Teal : NotaPalette.TextTertiary, new Rect(x + 1, r.Height - 9, Math.Max(2, gw - 1), 4));
            }
            var lane = Pattern.Lanes[Math.Clamp(Lane, 0, 3)];
            if (lane.Target >= 0 && lane.Values[st] != Nota.Application.Twin.TwinPatterns.MotionUnset)
            {
                double y = top + barH * (1 - lane.Values[st] / 65534.0);
                ctx.DrawLine(new Pen(NotaPalette.Teal, 1.5), new Point(x + 3, y), new Point(x + cw - 3, y));
            }
            if (st == RecStep || st == Selected)
                ctx.DrawRectangle(null, new Pen(st == RecStep ? NotaPalette.Accent : NotaPalette.TextSecondary, 1), new Rect(x + 1.5, 1.5, cw - 3, r.Height - 3));
        }
    }
}

// How the two filters are wired, as the route puts them: series (multi → ladder) on the left
// of the slider, parallel (both side by side, summed) on the right; between, the share that runs
// in parallel. Read-only — the route slider above it moves it.
internal sealed class TwinRouteView : TwinParamGraph
{
    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 40 || r.Height < 30) return;
        NotaGraph.Window(ctx, r);
        double rt = P("route");
        NotaGraph.Axis(ctx, r, NotaGraph.Corner.TopLeft, rt <= 0.01 ? "series" : rt >= 0.99 ? "parallel" : $"{NotaNum.Pct((float)rt)} parallel");
        var pen = NotaGraph.SecondaryPen(NotaPalette.VizInk, 1.0);
        const double bw = 30, bh = 13;
        double mid = Math.Round(r.Height / 2 + 4) + 0.5, cx = Math.Round(r.Width / 2);
        void Box(double x, double y, string name)
        {
            var b = new Rect(Math.Round(x) + 0.5, Math.Round(y) + 0.5, bw, bh);
            ctx.DrawRectangle(null, pen, b, 2, 2);
            var ft = NotaGraph.AxisText(name, NotaPalette.TextSecondary);
            ctx.DrawText(ft, new Point(b.X + (bw - ft.Width) / 2, b.Y + (bh - ft.Height) / 2));
        }
        if (rt >= 0.5)
        {
            double top = mid - 10, bot = mid + 10, l = 8, rr = r.Width - 8;
            ctx.DrawLine(pen, new Point(4, mid), new Point(l, mid));
            ctx.DrawLine(pen, new Point(l, top), new Point(l, bot));
            ctx.DrawLine(pen, new Point(rr, top), new Point(rr, bot));
            ctx.DrawLine(pen, new Point(rr, mid), new Point(r.Width - 4, mid));
            foreach (var y in new[] { top, bot })
            {
                ctx.DrawLine(pen, new Point(l, y), new Point(cx - bw / 2, y));
                ctx.DrawLine(pen, new Point(cx + bw / 2, y), new Point(rr, y));
            }
            Box(cx - bw / 2, top - bh / 2, "multi");
            Box(cx - bw / 2, bot - bh / 2, "ladder");
        }
        else
        {
            double a = cx - bw - 4, b = cx + 4;
            ctx.DrawLine(pen, new Point(4, mid), new Point(a, mid));
            ctx.DrawLine(pen, new Point(a + bw, mid), new Point(b, mid));
            ctx.DrawLine(pen, new Point(b + bw, mid), new Point(r.Width - 4, mid));
            Box(a, mid - bh / 2, "multi");
            Box(b, mid - bh / 2, "ladder");
        }
    }
}

// A read-only picture of one of the part's effects, from its own params: the mod-FX sweep
// (rate and depth; three phases for the ensemble), the delay's taps (time or division,
// feedback, ping-pong above / below the line for left / right) or the reverb's tail (size and
// damping). Off draws in the rule colour.
internal sealed class TwinFxView : TwinParamGraph
{
    public enum Kind { ModFx, Delay, Reverb }
    private static readonly double[] DivBeats = { 0.25, 1 / 3.0, 0.5, 0.75, 2 / 3.0, 1, 1.5, 2 };
    private readonly Kind _kind;
    public TwinFxView(Kind kind) { _kind = kind; }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 24 || r.Height < 16) return;
        NotaGraph.Window(ctx, r);
        string on = _kind switch { Kind.ModFx => "modfxon", Kind.Delay => "dlyon", _ => "revon" };
        IBrush ink = P(on) >= 0.5f ? NotaPalette.VizInk : NotaPalette.Rule;
        var pen = NotaGraph.SecondaryPen(ink, 1.2);
        double mid = Math.Round(r.Height / 2) + 0.5;
        switch (_kind)
        {
            case Kind.ModFx:
            {
                ctx.DrawLine(NotaGraph.GridPen, new Point(1, mid), new Point(r.Width - 1, mid));
                double cycles = 1 + 4 * P("modfxrate"), depth = 0.15 + 0.85 * P("modfxdepth");
                int type = Math.Clamp((int)Math.Round(P("modfxtype") * 3), 0, 3), phases = type == 3 ? 3 : 1;
                int n = Math.Max(2, (int)r.Width - 6);
                for (int k = 0; k < phases; k++)
                {
                    var pts = new Point[n];
                    for (int i = 0; i < n; i++)
                    {
                        double x = i / (double)(n - 1);
                        pts[i] = new Point(3 + i, mid - Math.Sin(2 * Math.PI * (x * cycles + k / 3.0)) * depth * (r.Height / 2 - 4));
                    }
                    ctx.DrawGeometry(null, k == 0 ? pen : NotaGraph.SecondaryPen(ink, 0.8), Path(pts));
                }
                NotaGraph.Axis(ctx, r, NotaGraph.Corner.TopRight, "sweep");
                break;
            }
            case Kind.Delay:
            {
                ctx.DrawLine(NotaGraph.GridPen, new Point(1, mid), new Point(r.Width - 1, mid));
                // Two seconds across, at 120 BPM for a synced division.
                double sec = P("dlysync") >= 0.5f ? DivBeats[Math.Clamp((int)Math.Round(P("dlydiv") * 7), 0, 7)] * 0.5 : Math.Max(0.001, P("dlytime") * 2.0);
                double step = Math.Max(4, sec / 2.0 * (r.Width - 8)), fb = P("dlyfb"), amp = 0.8, h = r.Height / 2 - 4;
                bool ping = P("dlypingpong") >= 0.5f;
                ctx.DrawRectangle(NotaPalette.TextFaint, null, new Rect(4, mid - h, 2, h), 1, 1);
                for (int i = 1; 4 + i * step < r.Width - 4 && amp > 0.03; i++, amp *= Math.Max(0.05, fb))
                {
                    double x = Math.Round(4 + i * step), hh = Math.Max(1, amp * h);
                    bool up = !ping || i % 2 == 1;
                    ctx.DrawRectangle(ink, null, new Rect(x, up ? mid - hh : mid, 2, hh), 1, 1);
                }
                NotaGraph.Axis(ctx, r, NotaGraph.Corner.TopRight, "L");
                NotaGraph.Axis(ctx, r, NotaGraph.Corner.BottomRight, "R");
                break;
            }
            default:
            {
                double size = 0.3 + 3 * P("revsize"), damp = P("revdamp");
                int n = Math.Max(2, (int)r.Width - 8);
                var pts = new Point[n];
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)(n - 1) * 4;                       // four seconds across
                    double lvl = Math.Exp(-t * (1 + 1.5 * damp) / size);
                    pts[i] = new Point(4 + i, 4 + (1 - lvl) * (r.Height - 8));
                }
                ctx.DrawGeometry(null, pen, Path(pts));
                NotaGraph.Axis(ctx, r, NotaGraph.Corner.TopRight, "tail");
                break;
            }
        }
    }
}

// A number in a well that drags like a knob: vertical, up increases, the full range over 140 px,
// Shift / Ctrl / ⌘ fine; a click never jumps it; double-click restores the default; left button
// only, so right-click reaches the parameter menu. The number is in Ink 1 at rest and the
// accent while hovered or dragged. Used where a row of four knobs will not fit — the ADSR times
// under Twin's envelope graphs.
internal sealed class TwinValueWell : Border
{
    private readonly TextBlock _text = new()
    {
        FontFamily = NotaFonts.MonoFamily, FontSize = NotaType.KnobValue, Foreground = NotaPalette.TextPrimary,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };
    private double _value, _lastY;
    private bool _drag;
    public Func<double, string> Format = v => NotaNum.Pct((float)v);
    public double Default = double.NaN;
    public event Action<double>? ValueChanged;
    public event Action? GestureBegin, GestureEnd;
    public bool Dragging => _drag;
    public TextBlock Readout => _text;

    public TwinValueWell()
    {
        Height = 15;
        Background = NotaPalette.BgSunken;
        BorderBrush = NotaPalette.Rule;
        BorderThickness = new Thickness(1);
        CornerRadius = NotaRadius.Clip;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        Child = _text;
        PointerEntered += (_, _) => Paint();
        PointerExited += (_, _) => Paint();
    }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); _text.Text = Format(_value); }
    }

    private void Paint() => _text.Foreground = _drag || IsPointerOver ? NotaPalette.Accent : NotaPalette.TextPrimary;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && !double.IsNaN(Default))
        {
            GestureBegin?.Invoke(); Value = Default; ValueChanged?.Invoke(_value); GestureEnd?.Invoke();
            e.Handled = true;
            return;
        }
        _drag = true; _lastY = pt.Position.Y;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
        Paint();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        double y = e.GetPosition(this).Y, dy = _lastY - y;
        _lastY = y;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        Value = _value + dy / 140.0 * (fine ? 0.1 : 1);
        ValueChanged?.Invoke(_value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) => Finish(e.Pointer);
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => Finish(null);
    private void Finish(IPointer? p)
    {
        if (!_drag) return;
        _drag = false;
        p?.Capture(null);
        GestureEnd?.Invoke();
        Paint();
    }
}

// The noise source's colour as a spectral tilt across 20 Hz … 20 kHz: flat for white, falling
// for pink and red. Read-only — the Colour knob sets it.
internal sealed class TwinNoiseView : TwinParamGraph
{
    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        if (r.Width < 24 || r.Height < 16) return;
        NotaGraph.Window(ctx, r);
        double c = P("noisecolor"), y0 = 6, y1 = 6 + c * (r.Height - 14);
        ctx.DrawLine(NotaGraph.SecondaryPen(NotaPalette.VizInk, 1.2), new Point(4, y0), new Point(r.Width - 4, y1));
        NotaGraph.Axis(ctx, r, NotaGraph.Corner.BottomLeft, "20");
        NotaGraph.Axis(ctx, r, NotaGraph.Corner.BottomRight, "20k");
    }
}
