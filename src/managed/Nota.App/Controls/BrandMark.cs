// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Nota mark drawn as geometry, so it follows the Graphite / Paper variant: a raised
// tile holding a knob — a 270° track, a brass arc over its first part and an Ink 1 needle.
// Proportions come from the start-screen mockup (a 52-unit glyph set at 40 in a 56 tile).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class BrandMark : Control
{
    private const double TileSize = 56, TileRadius = 12, GlyphSize = 40, Units = 52;
    private const double R = 17, Stroke = 5;
    private const double StartDeg = 135, TrackSweep = 270, ValueSweep = 167.5, NeedleDeg = 32, NeedleLen = 16;

    public BrandMark()
    {
        Width = TileSize;
        Height = TileSize;
    }

    public override void Render(DrawingContext ctx)
    {
        var tile = new Rect(0.5, 0.5, TileSize - 1, TileSize - 1);
        ctx.DrawRectangle(NotaPalette.SurfaceCard, new Pen(NotaPalette.BorderDefault, 1), new RoundedRect(tile, TileRadius));

        double k = GlyphSize / Units;
        var c = new Point(TileSize / 2, TileSize / 2);
        double r = R * k;

        ctx.DrawGeometry(null, RoundPen(NotaPalette.TrackOff, Stroke * k), Arc(c, r, StartDeg, TrackSweep));
        ctx.DrawGeometry(null, RoundPen(NotaPalette.Accent, Stroke * k), Arc(c, r, StartDeg, ValueSweep));

        // Needle: straight up from the centre, turned clockwise.
        double a = (NeedleDeg - 90) * Math.PI / 180;
        var tip = new Point(c.X + Math.Cos(a) * NeedleLen * k, c.Y + Math.Sin(a) * NeedleLen * k);
        ctx.DrawLine(RoundPen(NotaPalette.TextPrimary, Stroke * k), c, tip);
    }

    private static Pen RoundPen(IBrush brush, double w) => new(brush, w) { LineCap = PenLineCap.Round };

    // Clockwise arc in screen angles (0° = 3 o'clock).
    private static StreamGeometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        static Point At(Point c, double r, double deg)
        {
            double t = deg * Math.PI / 180;
            return new Point(c.X + Math.Cos(t) * r, c.Y + Math.Sin(t) * r);
        }
        var g = new StreamGeometry();
        using var s = g.Open();
        s.BeginFigure(At(c, r, startDeg), false);
        s.ArcTo(At(c, r, startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise);
        s.EndFigure(false);
        return g;
    }
}
