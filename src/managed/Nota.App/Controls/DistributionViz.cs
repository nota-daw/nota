// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Random distribution shape (mockup 3b): the probability shape of the random offset —
// Gauss (bell), Even (flat) or Walk (drift / triangular) — drawn as teal bars over a −max …
// centre … +max span, so the choice is visible.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class DistributionViz : Control
{
    private static readonly IBrush Well = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush BorderIn = new SolidColorBrush(Color.Parse("#221F1A"));
    private static readonly Color Teal = Color.Parse("#5B9E9C");
    private const int N = 16;

    private int _dist;

    public DistributionViz() { MinHeight = 40; }
    public void Set(int dist) { _dist = Math.Clamp(dist, 0, 2); InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Well, new Pen(BorderIn, 1), new Rect(0, 0, w, h), 5, 5);
        double padX = 6, padY = 5;
        double x0 = padX, x1 = w - padX, top = padY, bot = h - padY;
        double bw = (x1 - x0) / N;
        for (int k = 0; k < N; k++)
        {
            double t = (k + 0.5) / N;          // 0..1 across the span
            double c = (t - 0.5) * 2;           // -1..1 (centre = 0)
            double hh = _dist switch
            {
                1 => 1.0,                                   // Even
                2 => 1.0 - Math.Abs(c),                     // Walk (triangular drift)
                _ => Math.Exp(-(c * c) * 4.0),              // Gauss
            };
            double bh = Math.Max(1, hh * (bot - top));
            var rect = new Rect(x0 + k * bw + 0.5, bot - bh, Math.Max(1, bw - 1.4), bh);
            ctx.DrawRectangle(new SolidColorBrush(Teal, 0.35 + 0.55 * hh), null, rect, 1, 1);
        }
    }
}
