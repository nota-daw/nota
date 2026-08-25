// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A tiny glyph of a grain amplitude window (Hann / Gaussian / Tukey / Triangle) for the
// Nota Grain shape selector — mirrors GrainSynth::grainWindow.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class GrainShapeIcon : Control
{
    private readonly int _shape;
    public IBrush Stroke { get; set; } = Brushes.Gray;
    public GrainShapeIcon(int shape) { _shape = shape; Width = 19; Height = 13; }

    private static double Win(int shape, double x) => shape switch
    {
        1 => Math.Exp(-Math.Pow((x - 0.5) * 4.0, 2)),                         // Gaussian
        2 => x < 0.25 ? 0.5 - 0.5 * Math.Cos(Math.PI * x / 0.25) : x > 0.75 ? 0.5 - 0.5 * Math.Cos(Math.PI * (1 - x) / 0.25) : 1.0,  // Tukey
        3 => 1.0 - Math.Abs(2.0 * x - 1.0),                                   // Triangle
        _ => 0.5 - 0.5 * Math.Cos(2 * Math.PI * x),                           // Hann
    };

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0) return;
        var pen = new Pen(Stroke, 1.5, lineJoin: PenLineJoin.Round);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            int n = 20;
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                var p = new Point(1 + t * (w - 2), h - 1 - Win(_shape, t) * (h - 3));
                if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
            }
        }
        ctx.DrawGeometry(null, pen, geo);
    }
}
