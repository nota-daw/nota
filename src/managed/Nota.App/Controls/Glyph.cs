// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Small UI glyphs drawn as geometry. The almanac bans emoji and icon fonts: a shape is
// built from geometry or a thin stroke, never typed as a character — a character takes
// whatever face the OS falls back to (Geist has no ■ ▾ ✓), so its size and weight drift
// from the text around it. Use Glyph as a control, or Glyph.Draw inside a Render.
//
// Transport shapes follow the almanac exactly: Stop a 10px square with radius 1, Record an
// 11px circle, Play an 11×14 triangle. Everything else is a 1.5px stroke on the same grid.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Nota.App;

internal enum GlyphKind
{
    Play, Stop, Record, RecordRing, Close, Check, Cross,
    ChevronDown, ChevronUp, ChevronLeft, ChevronRight,
    StepLeft, StepRight,           // filled small triangles (◀ ▶ as "move")
    Edit, Freeze, PopOut, Cycle, Dot, Bypass, Plus, Grip, Minus,
}

internal sealed class Glyph : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Glyph>();

    private GlyphKind _kind;
    private readonly double _size;

    /// <summary>The shape. Setting it also sets the glyph's natural size (transport shapes
    /// have fixed almanac sizes; the rest are square at the size given).</summary>
    public GlyphKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            (Width, Height) = _size > 0 ? (_size, _size) : value switch
            {
                GlyphKind.Play => (11, 14),
                GlyphKind.Stop => (10, 10),
                GlyphKind.Record or GlyphKind.RecordRing => (11, 11),
                _ => (10, 10),
            };
            InvalidateVisual();
        }
    }

    /// <summary>The ink. Inherits the text foreground, so a glyph inside a button follows its state.</summary>
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    static Glyph() => AffectsRender<Glyph>(ForegroundProperty);

    public Glyph() : this(GlyphKind.Dot) { }

    /// <param name="size">0 = the natural (almanac) size; otherwise a square box of this size.</param>
    public Glyph(GlyphKind kind, double size = 0)
    {
        _size = size;
        Kind = kind;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext ctx)
        => Draw(ctx, Kind, new Rect(Bounds.Size), Foreground ?? NotaPalette.TextStrong);

    /// <summary>Draw <paramref name="kind"/> into <paramref name="r"/> — for custom-drawn views.</summary>
    public static void Draw(DrawingContext ctx, GlyphKind kind, Rect r, IBrush ink)
    {
        double w = r.Width, h = r.Height, x = r.X, y = r.Y, s = Math.Min(w, h);
        double cx = x + w / 2, cy = y + h / 2;
        var pen = new Pen(ink, Math.Max(1.2, s * 0.15), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        switch (kind)
        {
            case GlyphKind.Play:
            {
                // Keep the almanac's 11:14 proportion inside whatever box it is given.
                double ph = Math.Min(h, w * 14 / 11), pw = ph * 11 / 14, px = cx - pw / 2, py = cy - ph / 2;
                Fill(ctx, ink, new Point(px, py), new Point(px + pw, cy), new Point(px, py + ph));
                break;
            }
            case GlyphKind.Stop:
            {
                ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(cx - s / 2, cy - s / 2, s, s), 1));
                break;
            }
            case GlyphKind.Record:
                ctx.DrawEllipse(ink, null, new Point(cx, cy), s / 2, s / 2);
                break;
            case GlyphKind.RecordRing:
                ctx.DrawEllipse(null, new Pen(ink, 1.2), new Point(cx, cy), s / 2 - 0.6, s / 2 - 0.6);
                break;
            case GlyphKind.Bypass:
            {
                // A ring with a slash: signal passes by.
                double rr = s * 0.36;
                ctx.DrawEllipse(null, pen, new Point(cx, cy), rr, rr);
                ctx.DrawLine(pen, new Point(cx - rr * 0.7, cy + rr * 0.7), new Point(cx + rr * 0.7, cy - rr * 0.7));
                break;
            }
            case GlyphKind.Plus:
            {
                double d = s * 0.36;
                ctx.DrawLine(pen, new Point(cx - d, cy), new Point(cx + d, cy));
                ctx.DrawLine(pen, new Point(cx, cy - d), new Point(cx, cy + d));
                break;
            }
            case GlyphKind.Minus:
            {
                double d = s * 0.36;
                ctx.DrawLine(pen, new Point(cx - d, cy), new Point(cx + d, cy));
                break;
            }
            case GlyphKind.Grip:
            {
                // Two columns of three dots: a drag handle.
                double dr = Math.Max(0.8, s * 0.09), dx = s * 0.16, dy = s * 0.28;
                for (int i = -1; i <= 1; i++)
                    for (int j = -1; j <= 1; j += 2)
                        ctx.DrawEllipse(ink, null, new Point(cx + j * dx, cy + i * dy), dr, dr);
                break;
            }
            case GlyphKind.Dot:
                ctx.DrawEllipse(ink, null, new Point(cx, cy), s * 0.3, s * 0.3);
                break;
            case GlyphKind.Close:
            case GlyphKind.Cross:
            {
                double d = s * 0.32;
                ctx.DrawLine(pen, new Point(cx - d, cy - d), new Point(cx + d, cy + d));
                ctx.DrawLine(pen, new Point(cx + d, cy - d), new Point(cx - d, cy + d));
                break;
            }
            case GlyphKind.Check:
                Stroke(ctx, pen, new Point(cx - s * 0.36, cy + s * 0.02), new Point(cx - s * 0.1, cy + s * 0.28), new Point(cx + s * 0.38, cy - s * 0.28));
                break;
            case GlyphKind.ChevronDown:
                Stroke(ctx, pen, new Point(cx - s * 0.3, cy - s * 0.14), new Point(cx, cy + s * 0.16), new Point(cx + s * 0.3, cy - s * 0.14));
                break;
            case GlyphKind.ChevronUp:
                Stroke(ctx, pen, new Point(cx - s * 0.3, cy + s * 0.14), new Point(cx, cy - s * 0.16), new Point(cx + s * 0.3, cy + s * 0.14));
                break;
            case GlyphKind.ChevronRight:
                Stroke(ctx, pen, new Point(cx - s * 0.14, cy - s * 0.3), new Point(cx + s * 0.16, cy), new Point(cx - s * 0.14, cy + s * 0.3));
                break;
            case GlyphKind.ChevronLeft:
                Stroke(ctx, pen, new Point(cx + s * 0.14, cy - s * 0.3), new Point(cx - s * 0.16, cy), new Point(cx + s * 0.14, cy + s * 0.3));
                break;
            case GlyphKind.StepLeft:
                Fill(ctx, ink, new Point(cx + s * 0.28, cy - s * 0.34), new Point(cx - s * 0.3, cy), new Point(cx + s * 0.28, cy + s * 0.34));
                break;
            case GlyphKind.StepRight:
                Fill(ctx, ink, new Point(cx - s * 0.28, cy - s * 0.34), new Point(cx + s * 0.3, cy), new Point(cx - s * 0.28, cy + s * 0.34));
                break;
            case GlyphKind.Edit:
            {
                // A pencil: a slanted shaft and its tip.
                Stroke(ctx, pen, new Point(cx - s * 0.34, cy + s * 0.34), new Point(cx - s * 0.2, cy + s * 0.06), new Point(cx + s * 0.2, cy - s * 0.34),
                    new Point(cx + s * 0.34, cy - s * 0.2), new Point(cx - s * 0.06, cy + s * 0.2), new Point(cx - s * 0.34, cy + s * 0.34));
                break;
            }
            case GlyphKind.Freeze:
            {
                // Three crossing strokes: frozen.
                double d = s * 0.4;
                for (int i = 0; i < 3; i++)
                {
                    double a = Math.PI / 2 + i * Math.PI / 3;
                    ctx.DrawLine(pen, new Point(cx + Math.Cos(a) * d, cy + Math.Sin(a) * d), new Point(cx - Math.Cos(a) * d, cy - Math.Sin(a) * d));
                }
                break;
            }
            case GlyphKind.PopOut:
            {
                double d = s * 0.36;
                ctx.DrawRectangle(null, pen, new RoundedRect(new Rect(cx - d, cy - d * 0.6, d * 1.4, d * 1.6), 1));
                Stroke(ctx, pen, new Point(cx, cy - d), new Point(cx + d, cy - d), new Point(cx + d, cy));
                ctx.DrawLine(pen, new Point(cx + d, cy - d), new Point(cx + d * 0.1, cy - d * 0.1));
                break;
            }
            case GlyphKind.Cycle:
            {
                double rr = s * 0.34;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(cx + rr, cy), false);
                    c.ArcTo(new Point(cx, cy - rr), new Size(rr, rr), 0, true, SweepDirection.Clockwise);
                    c.EndFigure(false);
                }
                ctx.DrawGeometry(null, pen, g);
                Stroke(ctx, pen, new Point(cx - s * 0.12, cy - rr - s * 0.14), new Point(cx, cy - rr), new Point(cx - s * 0.12, cy - rr + s * 0.14));
                break;
            }
        }
    }

    private static void Fill(DrawingContext ctx, IBrush ink, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
            c.EndFigure(true);
        }
        ctx.DrawGeometry(ink, null, g);
    }

    private static void Stroke(DrawingContext ctx, IPen pen, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
    }

    /// <summary>An existing caption followed by a drawn chevron whose ink follows the text's
    /// foreground — for dropdown captions that used to end in a typed "▾".</summary>
    public static StackPanel WithChevron(TextBlock text, double glyphSize = 8)
    {
        var chev = new Glyph(GlyphKind.ChevronDown, glyphSize);
        chev.Bind(ForegroundProperty, text.GetObservable(TextBlock.ForegroundProperty));
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 5,
            HorizontalAlignment = text.HorizontalAlignment, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children = { text, chev },
        };
    }

    /// <summary>A label followed by a glyph (a "Volume ▾" style dropdown caption).</summary>
    public static StackPanel Labeled(string text, GlyphKind kind, double fontSize = 11, double glyphSize = 8, double spacing = 5)
        => new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = spacing,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children = { new TextBlock { Text = text, FontSize = fontSize, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, new Glyph(kind, glyphSize) },
        };
}
