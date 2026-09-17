// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Shutter (device kind 19) visualisations:
//  · ShutterSignal — the signature SIGNAL graph. It scrolls the input level (olive area) and
//    the gate-gain curve (brass, high = open / low = shut) with the Threshold + Return lines
//    drawn where they act, so a crossing that opens the shutter and a soft hit that doesn't
//    are both visible. Read-only; samples the device meters each UI tick.
//  · ShutterDetectorEQ — the detector band-pass: a curve with two draggable handles (HP / LP)
//    setting which part of the key drives the gate.
//  · AHRGlyph — a tiny attack/hold/release trapezoid.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class ShutterSignal : Control
{
    private const int S_InDb = 0, S_GateGain = 1, kScope = 5;
    private const int N = 300;
    private const double DbTop = 0, DbBot = -60;

    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush BorderB = NotaPalette.BorderDefault;
    private static readonly IBrush InFill = NotaPalette.Wash(NotaPalette.SignalInFill, 0x8C);
    private static readonly IPen InPen = new Pen(NotaPalette.Wash(NotaPalette.SignalIn, 0xA0), 1);
    private static readonly IPen GatePen = new Pen(NotaPalette.Accent, 1.8);
    private static readonly IBrush GateFill = NotaPalette.Wash(NotaPalette.Accent, 0x12);
    private static readonly IPen ThrPen = new Pen(NotaPalette.Threshold, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
    private static readonly IPen RetPen = new Pen(NotaPalette.Wash(NotaPalette.TextTertiary, 0xB0), 1) { DashStyle = new DashStyle(new double[] { 2, 4 }, 0) };
    private static readonly IBrush Muted = NotaPalette.TextTertiary;
    private static readonly IBrush ThrC = NotaPalette.Threshold;
    private static readonly IBrush AxisB = NotaPalette.TextTertiary;
    private static readonly Typeface Face = NotaFonts.Mono;

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;
    private readonly float[] _in = new float[N], _gate = new float[N];
    private readonly float[] _scope = new float[kScope];
    private int _w; private bool _primed;
    // threshold/return in dB, pushed from the body.
    private double _thrDb = -38, _retDb = -41;

    public ShutterSignal(IAudioEngine engine, int track, int device)
    {
        _engine = engine; _track = track; _device = device;
        MinHeight = 60; ClipToBounds = true;
        for (int i = 0; i < N; i++) { _in[i] = -120; _gate[i] = 1; }
    }

    public void SetLines(double thrDb, double retDb) { _thrDb = thrDb; _retDb = retDb; }

    public void Tick()
    {
        int n = _engine.DeviceScope(_track, _device, _scope, kScope);
        if (n >= kScope)
        {
            _in[_w] = _scope[S_InDb]; _gate[_w] = _scope[S_GateGain];
            _w = (_w + 1) % N; _primed = true;
        }
        InvalidateVisual();
    }

    private static double YDb(double db, double gy, double gh) => gy + Math.Clamp((DbTop - db) / (DbTop - DbBot), 0, 1) * gh;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        double gx = 6, gy = 14, gw = w - 12, gh = h - gy - 12;
        if (gw <= 0 || gh <= 0) return;

        double X(int i) => gx + (double)i / (N - 1) * gw;
        int Idx(int i) => (_w + i) % N;
        double GateY(double g) => gy + gh * 0.08 + (1 - Math.Clamp(g, 0, 1)) * gh * 0.84;   // open=high, shut=low

        if (_primed)
        {
            // input level area (olive)
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(gx, gy + gh), true);
                for (int i = 0; i < N; i++) g.LineTo(new Point(X(i), YDb(_in[Idx(i)], gy, gh)));
                g.LineTo(new Point(gx + gw, gy + gh)); g.EndFigure(true);
            }
            ctx.DrawGeometry(null, InPen, area);
        }

        // threshold + return lines
        ctx.DrawLine(ThrPen, new Point(gx, YDb(_thrDb, gy, gh)), new Point(gx + gw, YDb(_thrDb, gy, gh)));
        ctx.DrawLine(RetPen, new Point(gx, YDb(_retDb, gy, gh)), new Point(gx + gw, YDb(_retDb, gy, gh)));

        if (_primed)
        {
            // gate-gain curve (brass) + faint fill under it
            var fill = new StreamGeometry();
            using (var g = fill.Open())
            {
                g.BeginFigure(new Point(gx, gy + gh), true);
                for (int i = 0; i < N; i++) g.LineTo(new Point(X(i), GateY(_gate[Idx(i)])));
                g.LineTo(new Point(gx + gw, gy + gh)); g.EndFigure(true);
            }
            Point gp = default; bool has = false;
            for (int i = 0; i < N; i++) { var p = new Point(X(i), GateY(_gate[Idx(i)])); if (has) ctx.DrawLine(GatePen, gp, p); gp = p; has = true; }
        }

        void Lbl(string s, double x, double y, IBrush b) => ctx.DrawText(new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, b), new Point(x, y));
        Lbl("SIGNAL", gx, 2, Muted);
        NotaGraph.Legend(ctx, gx + gw - 2, 2, ("input", NotaPalette.SignalIn, NotaGraph.Mark.Area),
            ("gate gain", NotaPalette.Accent, NotaGraph.Mark.Line), ("threshold", ThrC, NotaGraph.Mark.Dashed));
        Lbl($"THRESHOLD {_thrDb:0}", gx + 2, YDb(_thrDb, gy, gh) - 10, ThrC);
        Lbl("−250\u2009ms", gx, gy + gh + 1, AxisB);
        Lbl("now", gx + gw - 18, gy + gh + 1, AxisB);
    }
}

// Detector band-pass: HP (left) and LP (right) handles set which key band drives the gate.
internal sealed class ShutterDetectorEQ : Control
{
    private const double HpLo = 20, HpHi = 2000, LpLo = 200, LpHi = 20000;
    private const double FMin = 20, FMax = 20000;

    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush BorderB = NotaPalette.GraphBorder;
    private static readonly IPen CurvePen = new Pen(NotaPalette.Teal, 1.4);
    private static readonly IBrush CurveFill = NotaPalette.Wash(NotaPalette.Teal, 0x14);
    private static readonly IBrush Handle = NotaPalette.TealBright;
    private static readonly IBrush Axis = NotaPalette.TextAxis;
    private static readonly Typeface Face = NotaFonts.Mono;

    private double _hp = 0.301, _lp = 0.548;   // normalized DetHP / DetLP
    private int _drag;   // 0 none, 1 hp, 2 lp

    public event Action<double>? HpChanged;
    public event Action<double>? LpChanged;

    public ShutterDetectorEQ() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeWestEast); }
    public void Set(double hp, double lp) { _hp = hp; _lp = lp; InvalidateVisual(); }

    private static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
    private double XOf(double f, double w) { double lm = Math.Log10(FMin), s = Math.Log10(FMax) - lm; return (Math.Log10(f) - lm) / s * w; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        double w = Bounds.Width; double x = e.GetPosition(this).X;
        double hx = XOf(Exp(_hp, HpLo, HpHi), w), lx = XOf(Exp(_lp, LpLo, LpHi), w);
        _drag = Math.Abs(x - hx) <= Math.Abs(x - lx) ? 1 : 2;
        e.Pointer.Capture(this); Apply(x); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag != 0) Apply(e.GetPosition(this).X); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _drag = 0; e.Pointer.Capture(null); }

    private void Apply(double x)
    {
        double w = Bounds.Width; if (w <= 0) return;
        double lm = Math.Log10(FMin), s = Math.Log10(FMax) - lm;
        double f = Math.Pow(10, lm + Math.Clamp(x / w, 0, 1) * s);
        if (_drag == 1) { double v = Math.Clamp(Math.Log(f / HpLo) / Math.Log(HpHi / HpLo), 0, 1); _hp = v; HpChanged?.Invoke(v); }
        else if (_drag == 2) { double v = Math.Clamp(Math.Log(f / LpLo) / Math.Log(LpHi / LpLo), 0, 1); _lp = v; LpChanged?.Invoke(v); }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        double hpF = Exp(_hp, HpLo, HpHi), lpF = Exp(_lp, LpLo, LpHi);
        ctx.DrawLine(new Pen(NotaPalette.SurfaceCard, 1), new Point(0, h * 0.75), new Point(w, h * 0.75));

        // band-pass magnitude (1-pole HP × 1-pole LP), log-x.
        int n = (int)Math.Clamp(w, 24, 150);
        var pts = new Point[n];
        double lm = Math.Log10(FMin), sp = Math.Log10(FMax) - lm;
        for (int i = 0; i < n; i++)
        {
            double f = Math.Pow(10, lm + (double)i / (n - 1) * sp);
            double hp = (f / hpF) / Math.Sqrt(1 + (f / hpF) * (f / hpF));
            double lp = 1.0 / Math.Sqrt(1 + (f / lpF) * (f / lpF));
            double m = hp * lp;
            pts[i] = new Point((double)i / (n - 1) * w, h - 4 - m * (h - 10));
        }
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(0, h), true);
            foreach (var p in pts) g.LineTo(p);
            g.LineTo(new Point(w, h)); g.EndFigure(true);
        }
        for (int i = 1; i < n; i++) ctx.DrawLine(CurvePen, pts[i - 1], pts[i]);

        double hx = XOf(hpF, w), lx = XOf(lpF, w);
        ctx.DrawEllipse(Handle, null, new Point(hx, h * 0.5), 2.6, 2.6);
        ctx.DrawEllipse(Handle, null, new Point(lx, h * 0.5), 2.6, 2.6);
        void Lbl(string s, double x, double y) => ctx.DrawText(new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 7, Axis), new Point(x, y));
        Lbl(hpF >= 1000 ? $"{hpF / 1000:0.0}k" : $"{hpF:0}", 2, h - 9);
        Lbl(lpF >= 1000 ? $"{lpF / 1000:0.0}k" : $"{lpF:0}", w - 24, h - 9);
    }
}

// Tiny attack/hold/release trapezoid glyph.
internal sealed class AHRGlyph : Control
{
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush BorderB = NotaPalette.GraphBorder;
    private static readonly IPen Line = new Pen(NotaPalette.Accent, NotaGraph.PrimaryWidth) { LineJoin = PenLineJoin.Round };
    private static readonly IPen Dash = new Pen(NotaPalette.BorderStrong, 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        double x0 = 5, x1 = w * 0.28, x2 = w * 0.6, x3 = w - 5, top = 5, bot = h - 6;
        ctx.DrawLine(Line, new Point(x0, bot), new Point(x1, top));
        ctx.DrawLine(Line, new Point(x1, top), new Point(x2, top));
        ctx.DrawLine(Line, new Point(x2, top), new Point(x3, bot));
        ctx.DrawLine(Dash, new Point(x1, top), new Point(x1, bot));
        ctx.DrawLine(Dash, new Point(x2, top), new Point(x2, bot));
    }
}
