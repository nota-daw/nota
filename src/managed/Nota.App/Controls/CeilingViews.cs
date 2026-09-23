// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Ceiling (device kind 14) card windows, read from the engine's telemetry
// (Ceiling.h scopeRead) so the picture is what the limiter does:
//  · CeLevelView — the Level tab: the last 4 s as columns — the input behind (teal), the part
//    over the ceiling (red), the limited output (brass) — with the ceiling line, and the
//    reduction hanging under it in a strip. Drag the ceiling line up or down.
//  · CeReductionView — the Reduction tab: the reduction over 4 s hanging from 0 dB, teal where
//    a transient got through to the clip, the window's mean dashed; the scale follows the
//    deepest reduction (3 / 6 / 12 / 24 dB).
//  · CeLoudnessView — the Loudness tab: momentary (teal), short-term (brass) and integrated
//    (Ink) over the last 60 s around the Target, with the ±1 LU band. Drag the target line.
// Drag contract from ShDragView: left button only, a gesture per drag, double-click resets
// the handle, right-click bubbles to the MIDI-learn menu.

using System;
using Avalonia;
using Avalonia.Media;

namespace Nota.App;

internal sealed class CeLevelView : ShDragView
{
    public const int HCeiling = 0;
    private float[] _in = Array.Empty<float>(), _out = Array.Empty<float>(), _gr = Array.Empty<float>();
    private int _n;
    private double _ceil = -1, _hi = 6, _heldHi = double.NaN;
    private const double Lo = -36, StripH = 16;
    /// <summary>Maps a ceiling (dB) to the handle's 0..1 value and back — set by the card.</summary>
    public Func<double, double> CeilToNorm { get; set; } = db => (db + 12) / 12;
    public Func<double, double> NormToCeil { get; set; } = n => -12 + 12 * n;
    public string Title { get; set; } = "Level and reduction";

    /// <summary>The level cells (input, output, max GR — each <paramref name="n"/> long from
    /// <paramref name="inAt"/>, <paramref name="outAt"/>, <paramref name="grAt"/>) and the ceiling in dB.</summary>
    public void Set(float[] src, int inAt, int outAt, int grAt, int n, double ceilDb)
    {
        if (_in.Length != n) { _in = new float[n]; _out = new float[n]; _gr = new float[n]; }
        if (n > 0 && Math.Max(inAt, Math.Max(outAt, grAt)) + n <= src.Length)
        {
            Array.Copy(src, inAt, _in, 0, n);
            Array.Copy(src, outAt, _out, 0, n);
            Array.Copy(src, grAt, _gr, 0, n);
        }
        _n = n; _ceil = ceilDb;
        // Top of the scale: 0 dBFS plus room for the loudest input, in 6 dB steps (+6 … +24);
        // held while the ceiling is dragged so the line doesn't jump.
        double pk = -120; for (int i = 0; i < _n; i++) pk = Math.Max(pk, _in[i]);
        double hi = Math.Clamp(Math.Ceiling(Math.Max(0, pk) / 6) * 6 + 6, 6, 30);
        _hi = Dragging && !double.IsNaN(_heldHi) ? _heldHi : hi;
        _heldHi = _hi;
        InvalidateVisual();
    }

    private (double top, double bot) Plot() => (16, Bounds.Height - StripH - 16);
    private double Y(double db) { var (t, b) = Plot(); return b - Math.Clamp((db - Lo) / (_hi - Lo), 0, 1) * (b - t); }

    protected override int Hit(Point p) => Math.Abs(p.Y - Y(_ceil)) <= 5 && p.Y < Bounds.Height - StripH - 12 ? HCeiling : -1;
    protected override double DragTo(int handle, Point start, Point p, double v0)
    {
        var (t, b) = Plot();
        double db = Lo + (b - p.Y) / Math.Max(1, b - t) * (_hi - Lo);
        return CeilToNorm(Math.Clamp(db, -12, 0));
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        var (top, bot) = Plot();
        double x0 = 6, x1 = w - 6;
        // 0 dBFS reference.
        ctx.DrawLine(NotaGraph.GridPen, new Point(x0, Y(0)), new Point(x1, Y(0)));
        double cy = Y(_ceil);
        if (_n > 0)
        {
            double cw = (x1 - x0) / _n, gap = cw > 5 ? 1 : 0.5;
            var inFill = NotaPalette.Wash(NotaPalette.Teal, 0x55);
            var overFill = NotaPalette.Danger;
            for (int i = 0; i < _n; i++)
            {
                double x = x0 + i * cw, bw = Math.Max(0.6, cw - gap);
                if (_in[i] > Lo)
                {
                    double iy = Y(_in[i]);
                    ctx.DrawRectangle(inFill, null, new Rect(x, iy, bw, bot - iy), 1, 1);
                    if (iy < cy - 0.5) ctx.DrawRectangle(overFill, null, new Rect(x, iy, bw, cy - iy), 1, 1);
                }
                if (_out[i] > Lo)
                {
                    double oy = Y(_out[i]);
                    ctx.DrawRectangle(NotaPalette.Accent, null, new Rect(x, oy, bw, bot - oy), 1, 1);
                }
                // Reduction strip under the plot (0 … −12 dB hanging).
                double g = Math.Clamp(_gr[i] / 12.0, 0, 1);
                if (g > 0.005)
                    ctx.FillRectangle(NotaPalette.AccentBright, new Rect(x, h - StripH - 2, bw, g * (StripH - 3)));
            }
        }
        ctx.DrawLine(NotaGraph.FramePen, new Point(x0, h - StripH - 4), new Point(x1, h - StripH - 4));
        // Ceiling line (drag it).
        ctx.DrawLine(new Pen(NotaPalette.AccentBright, Dragging ? 1.6 : 1), new Point(x0, cy), new Point(x1, cy));
        TextR(ctx, NotaNum.F($"{_ceil:0.0}"), x1 - 2, cy - 10, NotaPalette.AccentBright);
        Text(ctx, "0", x0 + 2, Y(0) - 9);
        NotaGraph.Title(ctx, frame, Title);
        NotaGraph.Legend(ctx, w - 6, 3, ("in", NotaPalette.TealBright, NotaGraph.Mark.Area),
            ("out", NotaPalette.AccentBright, NotaGraph.Mark.Area), ("over", NotaPalette.DangerBright, NotaGraph.Mark.Area));
        Text(ctx, "−4 s", x0 + 2, h - StripH - 15);
        TextR(ctx, "now", x1 - 2, h - StripH - 15);
    }
}

internal sealed class CeReductionView : Avalonia.Controls.Control
{
    private float[] _gr = Array.Empty<float>(), _clip = Array.Empty<float>();
    private int _n;
    private double _avg, _scale = 12;
    public double Scale => _scale;

    public CeReductionView() { ClipToBounds = true; MinHeight = 36; }

    public void Set(float[] src, int grAt, int clipAt, int n, double avg)
    {
        if (_gr.Length != n) { _gr = new float[n]; _clip = new float[n]; }
        if (n > 0 && Math.Max(grAt, clipAt) + n <= src.Length)
        {
            Array.Copy(src, grAt, _gr, 0, n);
            Array.Copy(src, clipAt, _clip, 0, n);
        }
        _n = n; _avg = avg;
        double mx = 0; for (int i = 0; i < _n; i++) mx = Math.Max(mx, _gr[i]);
        _scale = mx <= 2.7 ? 3 : mx <= 5.5 ? 6 : mx <= 11 ? 12 : 24;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double x0 = 6, x1 = w - 26, top = 16, bot = h - 13;
        double Y(double gr) => top + Math.Clamp(gr / _scale, 0, 1) * (bot - top);
        for (int q = 1; q < 4; q++)
        {
            double y = top + (bot - top) * q / 4;
            ctx.DrawLine(NotaGraph.GridPen, new Point(x0, y), new Point(x1, y));
            ctx.DrawText(NotaGraph.AxisText(NotaNum.F($"−{_scale * q / 4:0.#}")), new Point(x1 + 4, y - 5));
        }
        ctx.DrawText(NotaGraph.AxisText("0"), new Point(x1 + 4, top));
        if (_n > 0)
        {
            double cw = (x1 - x0) / _n, gap = cw > 5 ? 1 : 0.5;
            for (int i = 0; i < _n; i++)
            {
                if (_gr[i] < 0.02) continue;
                double x = x0 + i * cw;
                ctx.DrawRectangle(_clip[i] > 0.5f ? NotaPalette.Teal : NotaPalette.AccentBright, null,
                    new Rect(x, top, Math.Max(0.6, cw - gap), Y(_gr[i]) - top), 1, 1);
            }
        }
        if (_avg > 0.05)
        {
            double ay = Y(_avg);
            ctx.DrawLine(new Pen(NotaPalette.AccentDim, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(x0, ay), new Point(x1, ay));
        }
        NotaGraph.Title(ctx, frame, "Reduction");
        NotaGraph.Legend(ctx, w - 6, 3, ("GR", NotaPalette.AccentBright, NotaGraph.Mark.Area),
            ("to the clip", NotaPalette.TealBright, NotaGraph.Mark.Area), ("mean", NotaPalette.AccentDim, NotaGraph.Mark.Dashed));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "−4 s");
        ctx.DrawText(NotaGraph.AxisText("now"), new Point(x1 - 16, h - 11));
    }
}

internal sealed class CeLoudnessView : ShDragView
{
    public const int HTarget = 0;
    private float[] _m = Array.Empty<float>(), _s = Array.Empty<float>(), _i = Array.Empty<float>();
    private int _n;
    private double _target = -14, _center = -14;
    private const double Span = 10;   // ± LU around the target
    public Func<double, double> TargetToNorm { get; set; } = t => (t + 30) / 25;

    public void Set(float[] src, int mAt, int sAt, int iAt, int n, double target)
    {
        if (_m.Length != n) { _m = new float[n]; _s = new float[n]; _i = new float[n]; }
        if (n > 0 && Math.Max(mAt, Math.Max(sAt, iAt)) + n <= src.Length)
        {
            Array.Copy(src, mAt, _m, 0, n);
            Array.Copy(src, sAt, _s, 0, n);
            Array.Copy(src, iAt, _i, 0, n);
        }
        _n = n; _target = target;
        if (!Dragging) _center = target;
        InvalidateVisual();
    }

    private double Y(double lufs) { double h = Bounds.Height, t = 14, b = h - 13; return t + (b - t) * (0.5 - (lufs - _center) / (2 * Span)); }

    protected override int Hit(Point p) => Math.Abs(p.Y - Y(_target)) <= 5 ? HTarget : -1;
    protected override double DragTo(int handle, Point start, Point p, double v0)
    {
        double h = Bounds.Height, t = 14, b = h - 13;
        double lufs = _center + (0.5 - (p.Y - t) / Math.Max(1, b - t)) * 2 * Span;
        return TargetToNorm(Math.Clamp(Math.Round(lufs), -30, -5));
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double x0 = 6, x1 = w - 6;
        for (int q = 1; q < 4; q++) ctx.DrawLine(NotaGraph.GridPen, new Point(x0 + (x1 - x0) * q / 4, 14), new Point(x0 + (x1 - x0) * q / 4, h - 13));
        foreach (double d in new[] { -4.0, 4.0 }) ctx.DrawLine(NotaGraph.GridPen, new Point(x0, Y(_center + d)), new Point(x1, Y(_center + d)));
        // Tolerance band ±1 LU and the target.
        double ty = Y(_target);
        ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x14), new Rect(x0, Y(_target + 1), x1 - x0, Y(_target - 1) - Y(_target + 1)));
        ctx.DrawLine(new Pen(Dragging ? NotaPalette.AccentBright : NotaPalette.AccentDim, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(x0, ty), new Point(x1, ty));

        void Curve(float[] v, IPen pen)
        {
            if (_n < 2) return;
            StreamGeometry? g = null; StreamGeometryContext? gc = null;
            bool open = false;
            double cw = (x1 - x0) / (_n - 1);
            for (int i = 0; i < _n; i++)
            {
                if (v[i] <= -69f) { if (open) { gc!.EndFigure(false); open = false; } continue; }
                var pt = new Point(x0 + i * cw, Math.Clamp(Y(v[i]), 14, h - 13));
                if (g is null) { g = new StreamGeometry(); gc = g.Open(); }
                if (!open) { gc!.BeginFigure(pt, false); open = true; } else gc!.LineTo(pt);
            }
            if (g is null) return;
            if (open) gc!.EndFigure(false);
            gc!.Dispose();
            ctx.DrawGeometry(null, pen, g);
        }
        Curve(_m, NotaGraph.SecondaryPen(NotaPalette.Teal, 1));
        Curve(_s, NotaGraph.PrimaryPen);
        Curve(_i, NotaGraph.SecondaryPen(NotaPalette.TextPrimary, 1.2));
        if (_n > 0 && _s[_n - 1] > -69f) NotaGraph.Node(ctx, new Point(x1, Math.Clamp(Y(_s[_n - 1]), 14, h - 13)), true);

        Text(ctx, NotaNum.F($"{_center + 4:0}"), x0 + 2, Y(_center + 4) - 9);
        Text(ctx, NotaNum.F($"{_target:0}"), x0 + 2, ty - 9, NotaPalette.AccentBright);
        Text(ctx, NotaNum.F($"{_center - 4:0}"), x0 + 2, Y(_center - 4) - 9);
        NotaGraph.Title(ctx, frame, "Loudness");
        NotaGraph.Legend(ctx, w - 6, 3, ("M", NotaPalette.TealBright, NotaGraph.Mark.Line), ("S", NotaPalette.AccentBright, NotaGraph.Mark.Line),
            ("I", NotaPalette.TextPrimary, NotaGraph.Mark.Line), ("target", NotaPalette.AccentDim, NotaGraph.Mark.Dashed));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "−60 s");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "now");
    }
}
