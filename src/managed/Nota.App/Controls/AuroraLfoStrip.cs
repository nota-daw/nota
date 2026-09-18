// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The shape window under Nota Aurora's two LFO rows: one LFO's waveform drawn across a
// couple of seconds of its own rate, so "sin at 40 %" is a picture rather than a number.
// Depth scales the height, which is what makes a modulator at zero depth read as the flat
// line it is. Repaints only when the shape, the rate or the depth actually move.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AuroraLfoStrip : Control
{
    private static readonly Pen Curve = new(NotaPalette.AccentBright, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly Pen Zero = new(NotaGraph.Grid, 1);
    private const int Samples = 180;

    private int _shape = -1;
    private double _cycles, _depth = -1;

    public AuroraLfoStrip() { ClipToBounds = true; MinHeight = 22; }

    /// <summary>Shape (0 sin · 1 tri · 2 sqr · 3 S&amp;H), how many cycles fill the window,
    /// and the depth that sets the height.</summary>
    public void Set(int shape, double cycles, double depth)
    {
        cycles = Math.Clamp(cycles, 0.5, 16);
        if (shape == _shape && Math.Abs(cycles - _cycles) < 0.01 && Math.Abs(depth - _depth) < 0.005) return;
        _shape = shape; _cycles = cycles; _depth = depth;
        InvalidateVisual();
    }

    // The engine's own shapes (WavetableSynth::lfoValue); S&H is seeded so the window
    // shows the same staircase every time rather than flickering on each repaint.
    private static double Value(int shape, double ph, uint seed) => shape switch
    {
        1 => ph < 0.5 ? 4 * ph - 1 : 3 - 4 * ph,
        2 => ph < 0.5 ? 1 : -1,
        3 => ((seed * 1664525u + 1013904223u) >> 8) / 8388608.0 - 1.0,
        _ => Math.Sin(2 * Math.PI * ph),
    };

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 2 || h <= 2 || _shape < 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        double mid = h / 2, amp = (h / 2 - 3) * Math.Clamp(_depth, 0, 1);
        ctx.DrawLine(Zero, new Point(2, mid), new Point(w - 2, mid));
        if (amp < 0.5) return;

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i <= Samples; i++)
            {
                double t = (double)i / Samples * _cycles;
                double ph = t - Math.Floor(t);
                double v = Value(_shape, ph, (uint)(int)Math.Floor(t) + 7u);
                var p = new Point(2 + (w - 4) * i / Samples, mid - v * amp);
                if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
            }
        }
        ctx.DrawGeometry(null, Curve, geo);
    }
}
