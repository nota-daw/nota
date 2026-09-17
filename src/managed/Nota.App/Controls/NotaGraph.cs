// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The graph window every visualiser lives in (almanac § Visualisations). One window, one
// set of rules, so forty devices read as one instrument:
//
//   ground Well · 1px Hairline frame · radius 4 · grid lines in the Grid colour
//   axis labels mono 7–8 in Ink 7, in the corners — never full axes with ticks
//   a title in caps 8 at the top-left, a legend inside the window with values
//   the primary curve 1.8px brass, secondary curves 1.2–1.6px in their chroma
//   nodes 7px with a 2px ground-coloured ring; the active one brass, the rest Ink 3
//   no fills under curves, no gradients
//
// Chromas are assigned in a fixed order when a graph carries several sources:
// brass (primary) · teal · rose · steel. They never mean state.

using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Nota.App;

internal static class NotaGraph
{
    public const double Radius = NotaRadius.ControlValue;   // 4
    public const double PrimaryWidth = 1.8, SecondaryWidth = 1.4;
    public const double NodeD = 7, NodeRing = 2;
    public const double AxisSize = 7, TitleSize = 8;

    public static IBrush Ground => NotaPalette.BgSunken;
    public static IBrush Frame => NotaPalette.GraphBorder;
    public static IBrush Grid => NotaPalette.GridBeat;
    public static IBrush AxisInk => NotaPalette.TextAxis;
    public static IBrush TitleInk => NotaPalette.TextTertiary;

    /// <summary>The chroma for the n-th source in a graph: brass, teal, rose, steel.</summary>
    public static IBrush Chroma(int n) => (n % 4) switch
    {
        0 => NotaPalette.Accent,
        1 => NotaPalette.Teal,
        2 => NotaPalette.Rose,
        _ => NotaPalette.Steel,
    };

    public static readonly IPen GridPen = new Pen(NotaPalette.GridBeat, 1);
    public static readonly IPen FramePen = new Pen(NotaPalette.GraphBorder, 1);
    public static readonly IPen PrimaryPen = new Pen(NotaPalette.Accent, PrimaryWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    /// <summary>A secondary curve in its chroma.</summary>
    public static IPen SecondaryPen(IBrush chroma, double width = SecondaryWidth)
        => new Pen(chroma, width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    /// <summary>The window: well ground, hairline frame, radius 4.</summary>
    public static void Window(DrawingContext ctx, Rect r)
    {
        var inner = r.Deflate(0.5);
        ctx.DrawRectangle(Ground, FramePen, new RoundedRect(inner, Radius));
    }

    /// <summary>An axis label in mono 7, Ink 7.</summary>
    public static FormattedText AxisText(string text, IBrush? ink = null, double size = AxisSize)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Mono, size, ink ?? AxisInk);

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

    /// <summary>The graph title: caps 8, top-left, inside the window.</summary>
    public static void Title(DrawingContext ctx, Rect r, string title)
    {
        var ft = new FormattedText(title.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            NotaFonts.SansBold, TitleSize, TitleInk) { };
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
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Mono, 8, ink);
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

    /// <summary>A legend row drawn inside the window: a swatch, a caps name and its value.</summary>
    public static double LegendRow(DrawingContext ctx, Point at, IBrush swatch, string name, string value)
    {
        ctx.DrawRectangle(swatch, null, new RoundedRect(new Rect(at.X, at.Y + 3, 8, 2), 1));
        var n = new FormattedText(name.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.SansBold, 7, TitleInk);
        var v = AxisText(value, NotaPalette.TextPrimary, 8);
        ctx.DrawText(n, new Point(at.X + 12, at.Y));
        ctx.DrawText(v, new Point(at.X + 12 + n.Width + 5, at.Y - 0.5));
        return n.Height + 2;
    }
}
