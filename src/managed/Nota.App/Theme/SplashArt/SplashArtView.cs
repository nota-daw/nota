// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Draws a SplashScene — the release's art on the launch splash (specs/074). Colour roles
// resolve against the live NotaPalette slots inside Render, so the art follows the theme.
// This is the one view-like class allowed gradients and strokes heavier than 1px: the
// splash exception to DESIGN.md rule 6, kept in Theme/ beside the plug-in preview wash
// and pinned there by DesignTokenCheck.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class SplashArtView : Control
{
    private readonly SplashScene _scene;

    public SplashArtView(SplashScene scene)
    {
        _scene = scene;
        Width = SplashScene.Width;
        Height = SplashScene.Height;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext ctx)
    {
        var card = new Rect(0, 0, SplashScene.Width, SplashScene.Height);
        var ground = Resolve(new ColorRef(SplashRole.Ground));
        using (ctx.PushClip(new RoundedRect(card, NotaRadius.CardValue)))
        {
            ctx.FillRectangle(new SolidColorBrush(ground), card);
            var a = _scene.ArtRect;
            using (ctx.PushClip(new Rect(a.X, a.Y, a.W, a.H)))
                foreach (var p in _scene.Items) Draw(ctx, p);

            // The seam between the art and the text's plain ground.
            var seam = new Pen(new SolidColorBrush(Resolve(new ColorRef(SplashRole.Seam))), 1);
            switch (_scene.Layout)
            {
                case SplashLayout.TextLeft: ctx.DrawLine(seam, new Point(a.X + 0.5, 0), new Point(a.X + 0.5, card.Height)); break;
                case SplashLayout.TextRight: ctx.DrawLine(seam, new Point(a.X + a.W - 0.5, 0), new Point(a.X + a.W - 0.5, card.Height)); break;
                default: ctx.DrawLine(seam, new Point(0, a.Y + a.H - 0.5), new Point(card.Width, a.Y + a.H - 0.5)); break;
            }
        }
    }

    private static void Draw(DrawingContext ctx, Prim p)
    {
        switch (p)
        {
            case RectPrim r:
                ctx.DrawRectangle(Brush(r.Fill), Pen(r.Stroke, r.StrokeWidth), new Rect(r.R.X, r.R.Y, r.R.W, r.R.H), r.Radius, r.Radius);
                break;
            case LinePrim l:
                ctx.DrawLine(Pen(l.Stroke, l.StrokeWidth)!, new Point(l.A.X, l.A.Y), new Point(l.B.X, l.B.Y));
                break;
            case PolylinePrim pl:
                ctx.DrawGeometry(Brush(pl.Fill), Pen(pl.Stroke, pl.StrokeWidth), Geometry(pl.Points, closed: pl.Fill is not null));
                break;
            case PolygonPrim pg:
                ctx.DrawGeometry(Brush(pg.Fill), null, Geometry(pg.Points, closed: true));
                break;
            case CirclePrim c:
                ctx.DrawEllipse(Brush(c.Fill), Pen(c.Stroke, c.StrokeWidth), new Point(c.C.X, c.C.Y), c.R, c.R);
                break;
        }
    }

    private static StreamGeometry Geometry(EqList<SPoint> pts, bool closed)
    {
        var g = new StreamGeometry();
        using var s = g.Open();
        if (pts.Count == 0) return g;
        s.BeginFigure(new Point(pts[0].X, pts[0].Y), closed);
        for (int i = 1; i < pts.Count; i++) s.LineTo(new Point(pts[i].X, pts[i].Y));
        s.EndFigure(closed);
        return g;
    }

    private static IPen? Pen(Paint? paint, double width) =>
        paint is null ? null : new Pen(Brush(paint), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    /// <summary>A brush for one paint, built per render from the live palette — never stored,
    /// since a gradient brush snapshots its stop colours.</summary>
    private static IBrush? Brush(Paint? paint)
    {
        switch (paint)
        {
            case null: return null;
            case SolidPaint s: return new SolidColorBrush(Resolve(s.Color));
            case LinearPaint l:
            {
                var unit = l.Space == PaintSpace.Art ? RelativeUnit.Absolute : RelativeUnit.Relative;
                var g = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(l.Start.X, l.Start.Y, unit),
                    EndPoint = new RelativePoint(l.End.X, l.End.Y, unit),
                };
                foreach (var st in l.Stops) g.GradientStops.Add(new GradientStop(Resolve(st.Color), st.Offset));
                return g;
            }
            case RadialPaint r:
            {
                var unit = r.Space == PaintSpace.Art ? RelativeUnit.Absolute : RelativeUnit.Relative;
                var centre = new RelativePoint(r.Center.X, r.Center.Y, unit);
                var g = new RadialGradientBrush
                {
                    Center = centre,
                    GradientOrigin = centre,
                    RadiusX = new RelativeScalar(r.Radius, unit),
                    RadiusY = new RelativeScalar(r.Radius, unit),
                };
                foreach (var st in r.Stops) g.GradientStops.Add(new GradientStop(Resolve(st.Color), st.Offset));
                return g;
            }
            default: return null;
        }
    }

    private static Color Resolve(ColorRef c)
    {
        static (byte, byte, byte) Rgb(SolidColorBrush slot) => (slot.Color.R, slot.Color.G, slot.Color.B);
        var (a, r, g, b) = SplashTokens.Shade(c, Rgb(Slot(c)), Rgb(NotaPalette.BgSunken));
        return new Color(a, r, g, b);
    }

    /// <summary>The live palette slot of a role, found by the token name
    /// <see cref="SplashTokens.Name"/> gives it — the same table the review sheet reads the
    /// theme files with, so the app and the sheet cannot disagree.</summary>
    private static SolidColorBrush Slot(ColorRef c) =>
        SlotsByToken.TryGetValue(SplashTokens.Name(c), out var slot) ? slot : NotaPalette.BgSunken;

    /// <summary>Palette slots by token name (the published key without its "Brush." prefix).
    /// Holds the live slots themselves, so it follows every theme change.</summary>
    private static readonly Dictionary<string, SolidColorBrush> SlotsByToken = NotaPalette.AllKeys
        .Where(kv => kv.Key.StartsWith(nameof(Brush) + ".", StringComparison.Ordinal))
        .GroupBy(kv => kv.Key[(kv.Key.IndexOf('.') + 1)..], StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
}
