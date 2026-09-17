// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the two Nota Synth graphs (mockup 2f). The envelope panel draws the
// live ADSR in teal (= modulation) with a handle at every breakpoint and a note-off line;
// the filter panel draws the low-pass response in brass (= audio) with a log Hz axis, a
// 0 dB reference and a cutoff handle + readout. Both read the same normalized param values
// the knobs write and denormalise them with the engine's exact maps, so the scales are real.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class SynthViz : Control
{
    public enum K { Adsr, Filter }

    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush TealFill = NotaPalette.Wash(NotaPalette.Teal, 0x1A);
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush BrassFill = NotaPalette.Wash(NotaPalette.Accent, 0x17);
    private static readonly IBrush Handle = NotaPalette.AccentBright;
    private static readonly IBrush DimMono = NotaPalette.TextAxis;
    private static readonly IBrush Grid = NotaGraph.Grid;
    private static readonly IBrush GridDash = NotaGraph.Grid;
    private static readonly Typeface Face = NotaFonts.Mono;

    private const double Pad = 8;

    private readonly K _k;
    private double _a, _d, _s, _r, _cut, _res;

    public SynthViz(K k) { _k = k; MinWidth = 180; MinHeight = 40; }

    public void Set(double a, double d, double s, double r, double cut, double res)
    { _a = a; _d = d; _s = s; _r = r; _cut = cut; _res = res; InvalidateVisual(); }

    // Engine's perceptual map (Synth.h): lo * (hi/lo)^v.
    private static double ExpMap(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
    private static string Secs(double s) => s < 1.0
        ? $"{(s * 1000).ToString("0", NotaNum.Culture)}\u2009ms"
        : $"{s.ToString("0.00", NotaNum.Culture)}\u2009s";

    private void Text(DrawingContext ctx, string t, double x, double y, IBrush brush, double size = 8, bool mono = false)
        => ctx.DrawText(new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            mono ? NotaFonts.Mono : Face, size, brush), new Point(x, y));

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));

        double x0 = Pad, x1 = w - Pad, top = Pad + 12, bot = h - Pad - 9;
        if (_k == K.Adsr) RenderEnvelope(ctx, x0, x1, top, bot);
        else RenderFilter(ctx, x0, x1, top, bot);
    }

    private void RenderEnvelope(DrawingContext ctx, double x0, double x1, double top, double bot)
    {
        var gridPen = new Pen(Grid, 1);
        for (int i = 1; i <= 2; i++) { double gy = top + (bot - top) * i / 3.0; ctx.DrawLine(gridPen, new Point(x0, gy), new Point(x1, gy)); }

        double span = x1 - x0;
        double aw = _a * 0.30 * span, dw = _d * 0.25 * span, hold = 0.20 * span, rw = _r * 0.25 * span;
        double sy = top + (1 - _s) * (bot - top);
        double xa = x0 + aw, xd = Math.Min(x1, xa + dw), xh = Math.Min(x1, xd + hold), xr = Math.Min(x1, xh + rw);

        // The envelope as a stroked curve (almanac: no fills under curves).
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(x0, bot), true);
            g.LineTo(new Point(xa, top)); g.LineTo(new Point(xd, sy)); g.LineTo(new Point(xh, sy)); g.LineTo(new Point(xr, bot));
            g.EndFigure(true);
        }
        var pen = new Pen(Teal, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        ctx.DrawLine(pen, new Point(x0, bot), new Point(xa, top));
        ctx.DrawLine(pen, new Point(xa, top), new Point(xd, sy));
        ctx.DrawLine(pen, new Point(xd, sy), new Point(xh, sy));
        ctx.DrawLine(pen, new Point(xh, sy), new Point(xr, bot));

        // Note-off line at the end of the sustain hold.
        ctx.DrawLine(new Pen(GridDash, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) }, new Point(xh, top - 6), new Point(xh, bot));

        // Breakpoint handles.
        Dot(ctx, xa, top); Dot(ctx, xd, sy); Dot(ctx, xh, sy); Dot(ctx, xr, bot);

        // Labels: title + real reach time (top-right), note-off caption.
        double reach = ExpMap(_a, 0.001, 2.0) + ExpMap(_d, 0.002, 2.0);
        string reachTxt = $"{Secs(reach)} + rel";
        Text(ctx, "ENVELOPE", x1 - 52, Pad - 2, TextTertiary);
        Text(ctx, reachTxt, x1 - 52 - MeasureW(reachTxt, 8, true) - 8, Pad - 2, DimMono, 8, true);
        Text(ctx, "note off", Math.Min(xh + 3, x1 - 40), bot - 10, DimMono, 8, true);
    }

    private void RenderFilter(DrawingContext ctx, double x0, double x1, double top, double bot)
    {
        double span = x1 - x0;
        // Log Hz axis over 20 .. 20k; cutoff denormalised with the engine's 20 .. 18k map.
        double lo = Math.Log(20), hi = Math.Log(20000), lspan = hi - lo;
        double HzToX(double hz) => x0 + (Math.Log(hz) - lo) / lspan * span;
        double cutHz = ExpMap(_cut, 20, 18000);
        double cx = HzToX(cutHz);

        // 0 dB reference (dashed) + three evenly-spaced vertical grid lines.
        double flatY = top + (bot - top) * 0.38;
        ctx.DrawLine(new Pen(GridDash, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) }, new Point(x0, flatY), new Point(x1, flatY));
        var gridPen = new Pen(Grid, 1);
        for (int i = 1; i <= 3; i++) { double gx = x0 + span * i / 4.0; ctx.DrawLine(gridPen, new Point(gx, top - 6), new Point(gx, bot)); }

        // Response: flat, resonance bump at cutoff, then roll-off to the floor.
        double bumpY = flatY - _res * (flatY - top) * 0.95;
        double kx = Math.Max(x0, cx - Math.Min(14, span * 0.05));
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(x0, flatY), true);
            g.LineTo(new Point(kx, flatY)); g.LineTo(new Point(cx, bumpY)); g.LineTo(new Point(x1, bot));
            g.LineTo(new Point(x1, bot)); g.LineTo(new Point(x0, bot));
            g.EndFigure(true);
        }
        var pen = new Pen(Brass, 1.8, lineJoin: PenLineJoin.Round);
        ctx.DrawLine(pen, new Point(x0, flatY), new Point(kx, flatY));
        ctx.DrawLine(pen, new Point(kx, flatY), new Point(cx, bumpY));
        ctx.DrawLine(pen, new Point(cx, bumpY), new Point(x1, bot));

        // Cutoff handle + guide line.
        ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x38), 1), new Point(cx, top - 6), new Point(cx, bot));
        NotaGraph.Node(ctx, new Point(cx, bumpY), active: true);

        // Labels: title, cutoff/Q readout, evenly-spaced Hz axis captions.
        Text(ctx, "FILTER", x0, Pad - 2, TextTertiary);
        var inv = CultureInfo.InvariantCulture;
        string hzTxt = cutHz >= 1000 ? $"{(cutHz / 1000).ToString("0.0", inv)}\u2009k" : $"{cutHz.ToString("0", inv)}\u2009Hz";
        string readout = $"{hzTxt}  Q {_res.ToString("0.00", inv)}";
        Text(ctx, readout, x1 - MeasureW(readout, 9, true), Pad - 2, Handle, 9, true);
        string[] ticks = { "20", "100", "1 k", "10 k", "20 k" };
        for (int i = 0; i < ticks.Length; i++)
        {
            double tx = x0 + span * i / (ticks.Length - 1);
            if (i == ticks.Length - 1) tx -= MeasureW(ticks[i], 8, true);   // last flush-right
            else if (i > 0) tx -= MeasureW(ticks[i], 8, true) / 2;          // interior centred
            Text(ctx, ticks[i], tx, bot + 1, DimMono, 8, true);
        }
    }

    private static void Dot(DrawingContext ctx, double x, double y)
        => ctx.DrawEllipse(Teal, new Pen(Sunken, 2), new Point(x, y), 4.5, 4.5);

    private static double MeasureW(string t, double size, bool mono)
        => new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            mono ? NotaFonts.Mono : Face, size, NotaPalette.TextPrimary).Width;
}
