// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Operator spectrum viz (mockup 3g, OPS tab): the harmonic magnitude spectrum of the
// current FM patch at the played note, read live from the engine (OperatorSynth::scopeRead
// → Goertzel at k·f0). FM is defined by its partials, so moving FM depth or a ratio shifts
// the bars visibly. Brass bars over a sunken well with a fundamental-relative axis.

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
    private static readonly Color Brass = ((SolidColorBrush)NotaPalette.Accent).Color;
    private static readonly Color BrassLit = ((SolidColorBrush)NotaPalette.AccentBright).Color;
    private static readonly IBrush Axis = NotaPalette.TextDisabled;
    private static readonly IBrush Amber = NotaPalette.AccentBright;
    private static readonly Typeface Mono = new(new FontFamily("Geist Mono, monospace"));

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
        ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 5, 5);

        double padX = 5, padTop = 4, padBot = 13;
        double x0 = padX, x1 = w - padX, top = padTop, bot = h - padBot;
        double bw = (x1 - x0) / Math.Max(1, _n);

        // Bars — brass, brighter toward the top of each partial.
        for (int k = 0; k < _n; k++)
        {
            double v = Math.Clamp(_bins[k], 0, 1);
            double bh = v * (bot - top);
            if (bh < 0.5) bh = v > 1e-3 ? 0.5 : 0;
            var rx = x0 + k * bw;
            var rect = new Rect(rx + 0.5, bot - bh, Math.Max(1, bw - 1.2), bh);
            var col = Color.FromArgb(255,
                (byte)(Brass.R + (BrassLit.R - Brass.R) * v),
                (byte)(Brass.G + (BrassLit.G - Brass.G) * v),
                (byte)(Brass.B + (BrassLit.B - Brass.B) * v));
            ctx.DrawRectangle(new SolidColorBrush(col, 0.35 + 0.65 * v), null, rect, 1, 1);
        }

        // Baseline + fundamental-relative axis ticks (f · 10f · 20f · 30f).
        ctx.DrawLine(new Pen(Axis, 1), new Point(x0, bot), new Point(x1, bot));
        void Tick(int k, string lbl)
        {
            double tx = x0 + Math.Min(_n, k) * bw;
            var ft = new FormattedText(lbl, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, Axis);
            ctx.DrawText(ft, new Point(Math.Min(tx, x1 - ft.Width), bot + 2));
        }
        Tick(0, "f"); Tick(10, "10f"); Tick(20, "20f"); Tick(30, "30f");

        // Played-note badge top-right.
        var note = new FormattedText(_note, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 9, Amber);
        ctx.DrawText(note, new Point(x1 - note.Width - 1, top));
    }
}
