// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The graph window every visualiser lives in (one recessed display per device).
// One window, one set of rules, so forty devices read as one instrument:
//
//   ground Well · 1px rule frame · radius 6 · grid lines in the viz-grid colour
//   axis labels 7.5 in ink 4, in the corners — never full axes with ticks
//   a title (sentence case, 8) at the top-left, a legend inside the window with values
//   the primary curve 1.8px in viz ink; a second source a darker grey (shade before chroma)
//   nodes 7px with a 2px ground-coloured ring; the active one the accent, the rest secondary
//   no fills under curves, no gradients
//
// Chroma only where shade cannot carry it, in a fixed order: viz ink · teal · rose · blue.
// They never mean state — the accent alone does.

using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Nota.App;

internal static class NotaGraph
{
    public const double Radius = NotaRadius.FieldValue;   // 6
    public const double PrimaryWidth = 1.8, SecondaryWidth = 1.4;
    public const double NodeD = 7, NodeRing = 2;
    public const double AxisSize = NotaType.Axis, TitleSize = 8;   // axis labels 7.5 in ink 4; a graph title 8
    /// <summary>Where a plot starts under the title row (title ink ends near 18), and how far its floor
    /// sits above the bottom edge, clear of the corner axis labels (which take the last 16 px) — so no
    /// grid line, frame line or resting trace runs through a label.</summary>
    public const double PlotTop = 22, PlotFloor = 18;

    public static IBrush Ground => NotaPalette.BgSunken;
    public static IBrush Frame => NotaPalette.GraphBorder;
    public static IBrush Grid => NotaPalette.VizGrid;
    public static IBrush AxisInk => NotaPalette.TextAxis;
    public static IBrush TitleInk => NotaPalette.TextMuted;

    /// <summary>The chroma for the n-th source in a graph: viz ink, teal, rose, blue.</summary>
    public static IBrush Chroma(int n) => (n % 4) switch
    {
        0 => NotaPalette.VizInk,
        1 => NotaPalette.Teal,
        2 => NotaPalette.Rose,
        _ => NotaPalette.Steel,
    };

    public static readonly IPen GridPen = new Pen(NotaPalette.VizGrid, 1);
    public static readonly IPen FramePen = new Pen(NotaPalette.GraphBorder, 1);
    public static readonly IPen PrimaryPen = new Pen(NotaPalette.VizInk, PrimaryWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    /// <summary>A secondary curve in its chroma.</summary>
    public static IPen SecondaryPen(IBrush chroma, double width = SecondaryWidth)
        => new Pen(chroma, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    /// <summary>The window: well ground, rule frame, radius 6.</summary>
    public static void Window(DrawingContext ctx, Rect r)
    {
        var inner = r.Deflate(0.5);
        ctx.DrawRectangle(Ground, FramePen, new RoundedRect(inner, Radius));
    }

    /// <summary>An axis label at the column-head size, faint ink. Text size leaves it alone (devices'
    /// graphs); a view outside a card passes itself as <paramref name="owner"/> to take the step.</summary>
    public static FormattedText AxisText(string text, IBrush? ink = null, double size = AxisSize, AvaloniaObject? owner = null)
        => new NotaFormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Sans, size, ink ?? AxisInk, owner ?? NotaTextSize.InCard);

    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>Place an axis label in a corner of <paramref name="r"/>, inset 4px.</summary>
    public static void Axis(DrawingContext ctx, Rect r, Corner corner, string text, IBrush? ink = null)
    {
        var ft = AxisText(text, ink);
        const double pad = 4;
        double x = corner is Corner.TopLeft or Corner.BottomLeft ? r.X + pad : r.Right - pad - ft.Width;
        double y = corner is Corner.TopLeft or Corner.TopRight ? r.Y + pad - 1 : r.Bottom - pad - ft.Height + 1;
        ctx.DrawText(ft, new Point(x, y));
    }

    /// <summary>How wide <see cref="Title"/> draws <paramref name="title"/>.</summary>
    public static double TitleWidth(string title) => new NotaFormattedText(NotaText.Label(title), CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, NotaFonts.SansMedium, TitleSize, TitleInk, NotaTextSize.InCard).Width;

    /// <summary>Scale labels up the left (or right) edge of <paramref name="r"/>, each centred on its y.
    /// A label that would crowd the one placed before it, run into the title row or sit on the bottom
    /// corner labels is left out — so a short window names fewer marks instead of stacking them.</summary>
    public static void EdgeLabels(DrawingContext ctx, Rect r, bool right, params (double Y, string Text)[] marks)
    {
        var placed = new List<(double A, double B)>();
        foreach (var (y, text) in marks)
        {
            var ft = AxisText(text);
            double y0 = y - ft.Height / 2, y1 = y0 + ft.Height;
            if (y0 < r.Y + 18 || y1 > r.Bottom - 16) continue;
            if (placed.Exists(p => y0 < p.B + 1 && y1 > p.A - 1)) continue;
            placed.Add((y0, y1));
            ctx.DrawText(ft, new Point(right ? r.Right - 4 - ft.Width : r.X + 4, y0));
        }
    }

    /// <summary>An axis label on a ground-coloured plate, for a window too short to keep its labels
    /// off the trace: the trace passes behind the plate instead of through the text.</summary>
    public static void PlateText(DrawingContext ctx, FormattedText ft, Point at)
    {
        ctx.DrawRectangle(Ground, null, new RoundedRect(new Rect(at.X - 2, at.Y, ft.Width + 4, ft.Height), 3));
        ctx.DrawText(ft, at);
    }

    /// <summary>The graph title: sentence case, top-left, inside the window.</summary>
    public static void Title(DrawingContext ctx, Rect r, string title)
    {
        var ft = new NotaFormattedText(NotaText.Label(title), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            NotaFonts.SansMedium, TitleSize, TitleInk, NotaTextSize.InCard) { };
        ctx.DrawText(ft, new Point(r.X + 6, r.Y + 4));
    }

    /// <summary>A curve node: 7px, ringed in the ground colour so the curve does not cut it.</summary>
    public static void Node(DrawingContext ctx, Point p, bool active, IBrush? ink = null)
    {
        var fill = ink ?? (active ? NotaPalette.Accent : NotaPalette.TextSecondary);
        ctx.DrawEllipse(fill, new Pen(Ground, NodeRing), p, NodeD / 2 + NodeRing / 2, NodeD / 2 + NodeRing / 2);
    }

    public enum Mark { Line, Dashed, Area }

    /// <summary>A legend inside the window, right-aligned at <paramref name="right"/>: each item is a
    /// drawn sample (solid line, dashed line or area swatch) followed by its word — never a typed
    /// ─ ┄ ▩ character.</summary>
    public static void Legend(DrawingContext ctx, double right, double y, params (string Label, IBrush Ink, Mark Mark)[] items)
    {
        double x = right;
        for (int i = items.Length - 1; i >= 0; i--)
        {
            var (label, ink, mark) = items[i];
            var ft = new NotaFormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Sans, AxisSize, ink, NotaTextSize.InCard);
            x -= ft.Width;
            ctx.DrawText(ft, new Point(x, y));
            double cy = y + ft.Height / 2;
            x -= 14;
            switch (mark)
            {
                case Mark.Line: ctx.DrawLine(new Pen(ink, 1.4), new Point(x, cy), new Point(x + 10, cy)); break;
                case Mark.Dashed: ctx.DrawLine(new Pen(ink, 1.2, new DashStyle(new double[] { 2, 2 }, 0)), new Point(x, cy), new Point(x + 10, cy)); break;
                default: ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(x + 2, cy - 3, 6, 6), 1)); break;
            }
            x -= 8;
        }
    }

    /// <summary>A legend row drawn inside the window: a swatch, a name and its value.</summary>
    public static double LegendRow(DrawingContext ctx, Point at, IBrush swatch, string name, string value)
    {
        ctx.DrawRectangle(swatch, null, new RoundedRect(new Rect(at.X, at.Y + 5, 8, 2), 1));
        var n = new NotaFormattedText(NotaText.Label(name), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.SansMedium, AxisSize, TitleInk, NotaTextSize.InCard);
        var v = AxisText(value, NotaPalette.TextPrimary);
        ctx.DrawText(n, new Point(at.X + 12, at.Y));
        ctx.DrawText(v, new Point(at.X + 12 + n.Width + 5, at.Y - 0.5));
        return n.Height + 2;
    }
}
