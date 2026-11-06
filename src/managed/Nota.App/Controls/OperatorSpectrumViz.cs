// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Operator spectrum viz (mockup 3g, OPS tab): the harmonic magnitude spectrum of the
// current FM patch at the played note, read live from the engine (OperatorSynth::scopeRead
// → Goertzel at k·f0). FM is defined by its partials, so moving FM depth or a ratio shifts
// the bars visibly. Neutral bars over a sunken well with a fundamental-relative axis.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class OperatorSpectrumViz : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static Color Dim => NotaPalette.TextTertiary.Color;
    private static Color Lit => NotaPalette.VizInk.Color;
    private static readonly IBrush Axis = NotaPalette.TextAxis;
    private static readonly IBrush NoteInk = NotaPalette.TextPrimary;
    private static readonly Typeface NumFace = NotaFonts.Sans;

    private float[] _bins = Array.Empty<float>();
    private int _n;
    private string _note = "—";

    public OperatorSpectrumViz() { MinHeight = 60; }

    public void Set(float[] bins, int n, string note)
    {
        _bins = bins; _n = Math.Max(0, n); _note = note;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));

        double padX = 5, padTop = 4, padBot = 13;
        double x0 = padX, x1 = w - padX, top = padTop, bot = h - padBot;
        double bw = (x1 - x0) / Math.Max(1, _n);

        // Bars — neutral ink, brighter the stronger the partial.
        for (int k = 0; k < _n; k++)
        {
            double v = Math.Clamp(_bins[k], 0, 1);
            double bh = v * (bot - top);
            if (bh < 0.5) bh = v > 1e-3 ? 0.5 : 0;
            var rx = x0 + k * bw;
            var rect = new Rect(rx + 0.5, bot - bh, Math.Max(1, bw - 1.2), bh);
            var col = Color.FromArgb(255,
                (byte)(Dim.R + (Lit.R - Dim.R) * v),
                (byte)(Dim.G + (Lit.G - Dim.G) * v),
                (byte)(Dim.B + (Lit.B - Dim.B) * v));
            ctx.DrawRectangle(new SolidColorBrush(col), null, rect, NotaRadius.FineValue, NotaRadius.FineValue);
        }

        // Baseline + fundamental-relative axis ticks (f · 10f · 20f · 30f).
        ctx.DrawLine(new Pen(Axis, 1), new Point(x0, bot), new Point(x1, bot));
        void Tick(int k, string lbl)
        {
            double tx = x0 + Math.Min(_n, k) * bw;
            var ft = new NotaFormattedText(lbl, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NumFace, NotaGraph.AxisSize, Axis, this);
            ctx.DrawText(ft, new Point(Math.Min(tx, x1 - ft.Width), bot + 2));
        }
        Tick(0, "f"); Tick(10, "10f"); Tick(20, "20f"); Tick(30, "30f");

        // Played-note badge top-right.
        var note = new NotaFormattedText(_note, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NumFace, 9, NoteInk, this);
        ctx.DrawText(note, new Point(x1 - note.Width - 1, top));
    }
}
