// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Velocity transfer curve (mockup 3b): input velocity (x) → output velocity (y), with a
// dashed identity diagonal, the brass transfer curve, teal dots for the last few notes' in→out
// points (showing the random spread) and a bright dot for the most recent note.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class VelocityCurveViz : Control
{
    private static readonly IBrush GridB = NotaPalette.SurfaceCard;
    private static readonly IBrush Ident = NotaPalette.SurfaceRaised;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush Teal = NotaPalette.Wash(NotaPalette.Teal, 0x80);
    private static readonly IBrush Bright = NotaPalette.AccentBright;
    private static readonly IBrush Ink = NotaPalette.BgSunken;

    private Func<double, double>? _f;
    private readonly List<(double x, double y)> _dots = new();
    private (double x, double y)? _last;

    public VelocityCurveViz() { MinHeight = 60; }

    public void Set(Func<double, double> f, IReadOnlyList<(double inN, double outN)> dots, (double inN, double outN)? last)
    {
        _f = f;
        _dots.Clear(); foreach (var d in dots) _dots.Add(d);
        _last = last;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0 || _f is null) return;
        Point P(double xn, double yn) => new(xn * w, (1 - yn) * h);

        // Grid: centre cross-hair.
        ctx.DrawLine(new Pen(GridB, 1), new Point(0, h / 2), new Point(w, h / 2));
        ctx.DrawLine(new Pen(GridB, 1), new Point(w / 2, 0), new Point(w / 2, h));
        // Identity diagonal (dashed).
        ctx.DrawLine(new Pen(Ident, 1, dashStyle: new DashStyle(new double[] { 3, 3 }, 0)), P(0, 0), P(1, 1));

        // Transfer curve.
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(P(0, Math.Clamp(_f(0), 0, 1)), false);
            for (int i = 1; i <= 64; i++) { double xn = i / 64.0; g.LineTo(P(xn, Math.Clamp(_f(xn), 0, 1))); }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(Brass, 1.8, lineJoin: PenLineJoin.Round), geo);

        // Recent-note dots.
        foreach (var d in _dots)
        {
            if (d.x <= 0 && d.y <= 0) continue;   // empty slot
            ctx.DrawEllipse(Teal, null, P(d.x, d.y), 2, 2);
        }
        // Last note — a glowing dot.
        if (_last is { } l && (l.x > 0 || l.y > 0))
        {
            var p = P(l.x, l.y);
            ctx.DrawEllipse(new SolidColorBrush(NotaPalette.AccentBright.Color, 0.28), null, p, 7, 7);
            ctx.DrawEllipse(Bright, new Pen(Ink, 2), p, 4.5, 4.5);
        }
    }
}
