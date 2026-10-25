// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Ceiling (device kind 14) card windows, read from the engine's telemetry
// (Ceiling.h scopeRead) so the picture is what the limiter does:
//  · CeLevelView — the Level tab: the last 4 s as columns — the input behind (grey), the part
//    over the ceiling (red), the limited output (neutral ink) — with the ceiling line. Drag the
//    ceiling line up or down. (The reduction has its own tab.)
//  · CeReductionView — the Reduction tab: the reduction over 4 s hanging from 0 dB, lighter grey where
//    a transient got through to the clip, the window's mean dashed; the scale follows the
//    deepest reduction (3 / 6 / 12 / 24 dB).
//  · CeLoudnessView — the Loudness tab: momentary (light grey), short-term (neutral ink) and
//    integrated (mid grey) over the last 60 s around the Target, with the ±1 LU band. Drag the target line.
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
    private const double Lo = -36;
    /// <summary>Maps a ceiling (dB) to the handle's 0..1 value and back — set by the card.</summary>
    public Func<double, double> CeilToNorm { get; set; } = db => (db + 12) / 12;
    public Func<double, double> NormToCeil { get; set; } = n => -12 + 12 * n;
    public string Title { get; set; } = "Level";

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

    private (double top, double bot) Plot() => (NotaGraph.PlotTop, Bounds.Height - NotaGraph.PlotFloor);
    private double Y(double db) { var (t, b) = Plot(); return b - Math.Clamp((db - Lo) / (_hi - Lo), 0, 1) * (b - t); }

    protected override int Hit(Point p) => Math.Abs(p.Y - Y(_ceil)) <= 5 ? HCeiling : -1;
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
            var inFill = NotaPalette.Wash(NotaPalette.TextTertiary, 0x66);
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
                    ctx.DrawRectangle(NotaPalette.VizInk, null, new Rect(x, oy, bw, bot - oy), 1, 1);
                }
            }
        }
        ctx.DrawLine(NotaGraph.FramePen, new Point(x0, bot + 0.5), new Point(x1, bot + 0.5));
        // Ceiling line (drag it).
        ctx.DrawLine(new Pen(Dragging ? NotaPalette.Accent : NotaPalette.VizInk, Dragging ? 1.6 : 1), new Point(x0, cy), new Point(x1, cy));
        // Both readouts hang under their lines, clear of the title and the legend above.
        TextR(ctx, NotaNum.F($"{_ceil:0.0}"), x1 - 2, cy + 1, Dragging ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
        Text(ctx, "0", x0 + 2, Y(0) + 1);
        NotaGraph.Title(ctx, frame, Title);
        NotaGraph.Legend(ctx, w - 6, 3, ("in", NotaPalette.Wash(NotaPalette.TextTertiary, 0x66), NotaGraph.Mark.Area),
            ("out", NotaPalette.VizInk, NotaGraph.Mark.Area), ("over", NotaPalette.DangerBright, NotaGraph.Mark.Area));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "−4 s");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "now");
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
        double x0 = 6, x1 = w - 26, top = NotaGraph.PlotTop, bot = h - NotaGraph.PlotFloor;
        double Y(double gr) => top + Math.Clamp(gr / _scale, 0, 1) * (bot - top);
        // Quarter lines; the scale is named at its two ends only (the plot is too short for four labels).
        for (int q = 1; q < 4; q++)
        {
            double y = top + (bot - top) * q / 4;
            ctx.DrawLine(NotaGraph.GridPen, new Point(x0, y), new Point(x1, y));
        }
        var z = NotaGraph.AxisText("0"); ctx.DrawText(z, new Point(x1 + 4, top - z.Height / 2));
        var fs = NotaGraph.AxisText(NotaNum.F($"−{_scale:0.#}")); ctx.DrawText(fs, new Point(x1 + 4, bot - fs.Height / 2));
        if (_n > 0)
        {
            double cw = (x1 - x0) / _n, gap = cw > 5 ? 1 : 0.5;
            for (int i = 0; i < _n; i++)
            {
                if (_gr[i] < 0.02) continue;
                double x = x0 + i * cw;
                ctx.DrawRectangle(_clip[i] > 0.5f ? NotaPalette.TextTertiary : NotaPalette.VizInk, null,
                    new Rect(x, top, Math.Max(0.6, cw - gap), Y(_gr[i]) - top), 1, 1);
            }
        }
        if (_avg > 0.05)
        {
            double ay = Y(_avg);
            ctx.DrawLine(new Pen(NotaPalette.TextSecondary, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(x0, ay), new Point(x1, ay));
        }
        NotaGraph.Title(ctx, frame, "Reduction");
        NotaGraph.Legend(ctx, w - 6, 3, ("GR", NotaPalette.VizInk, NotaGraph.Mark.Area),
            ("to the clip", NotaPalette.TextTertiary, NotaGraph.Mark.Area), ("mean", NotaPalette.TextSecondary, NotaGraph.Mark.Dashed));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "−4 s");
        var now = NotaGraph.AxisText("now"); ctx.DrawText(now, new Point(x1 - now.Width, h - 4 - now.Height + 1));
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

    private double Y(double lufs) { double h = Bounds.Height, t = NotaGraph.PlotTop, b = h - NotaGraph.PlotFloor; return t + (b - t) * (0.5 - (lufs - _center) / (2 * Span)); }

    protected override int Hit(Point p) => Math.Abs(p.Y - Y(_target)) <= 5 ? HTarget : -1;
    protected override double DragTo(int handle, Point start, Point p, double v0)
    {
        double h = Bounds.Height, t = NotaGraph.PlotTop, b = h - NotaGraph.PlotFloor;
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
        for (int q = 1; q < 4; q++) ctx.DrawLine(NotaGraph.GridPen, new Point(x0 + (x1 - x0) * q / 4, NotaGraph.PlotTop), new Point(x0 + (x1 - x0) * q / 4, h - NotaGraph.PlotFloor));
        foreach (double d in new[] { -4.0, 4.0 }) ctx.DrawLine(NotaGraph.GridPen, new Point(x0, Y(_center + d)), new Point(x1, Y(_center + d)));
        // Tolerance band ±1 LU and the target.
        double ty = Y(_target);
        ctx.FillRectangle(NotaPalette.Wash(NotaPalette.VizInk, 0x14), new Rect(x0, Y(_target + 1), x1 - x0, Y(_target - 1) - Y(_target + 1)));
        ctx.DrawLine(new Pen(Dragging ? NotaPalette.Accent : NotaPalette.TextSecondary, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(x0, ty), new Point(x1, ty));

        void Curve(float[] v, IPen pen)
        {
            if (_n < 2) return;
            StreamGeometry? g = null; StreamGeometryContext? gc = null;
            bool open = false;
            double cw = (x1 - x0) / (_n - 1);
            for (int i = 0; i < _n; i++)
            {
                if (v[i] <= -69f) { if (open) { gc!.EndFigure(false); open = false; } continue; }
                var pt = new Point(x0 + i * cw, Math.Clamp(Y(v[i]), NotaGraph.PlotTop, h - NotaGraph.PlotFloor));
                if (g is null) { g = new StreamGeometry(); gc = g.Open(); }
                if (!open) { gc!.BeginFigure(pt, false); open = true; } else gc!.LineTo(pt);
            }
            if (g is null) return;
            if (open) gc!.EndFigure(false);
            gc!.Dispose();
            ctx.DrawGeometry(null, pen, g);
        }
        Curve(_m, NotaGraph.SecondaryPen(NotaPalette.TextTertiary, 1));
        Curve(_s, NotaGraph.PrimaryPen);
        Curve(_i, NotaGraph.SecondaryPen(NotaPalette.TextSecondary, 1.2));
        if (_n > 0 && _s[_n - 1] > -69f) NotaGraph.Node(ctx, new Point(x1, Math.Clamp(Y(_s[_n - 1]), NotaGraph.PlotTop, h - NotaGraph.PlotFloor)), false);

        // The ±4 LU lines are named only when the plot leaves room between them for three labels.
        bool room = Y(_center - 4) - Y(_center + 4) >= 36;
        if (room) Text(ctx, NotaNum.F($"{_center + 4:0}"), x0 + 2, Y(_center + 4) - 9);
        // The target names itself at the right end, just over its line (the title holds the left).
        TextR(ctx, NotaNum.F($"{_target:0} LUFS"), x1 - 2, ty - 13, Dragging ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
        if (room) Text(ctx, NotaNum.F($"{_center - 4:0}"), x0 + 2, Y(_center - 4) - 9);
        NotaGraph.Title(ctx, frame, "Loudness");
        NotaGraph.Legend(ctx, w - 6, 3, ("M", NotaPalette.TextTertiary, NotaGraph.Mark.Line), ("S", NotaPalette.VizInk, NotaGraph.Mark.Line),
            ("I", NotaPalette.TextSecondary, NotaGraph.Mark.Line), ("target", NotaPalette.TextSecondary, NotaGraph.Mark.Dashed));
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "−60 s");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "now");
    }
}
