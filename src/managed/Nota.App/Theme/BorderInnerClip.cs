// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A Border with ClipToBounds clips its child to its *outer* rounded edge. A child that paints
// its own square background (a graph well, a list, a panel) then fills the rounded corners
// right up to the outside and eats the border there: the 1px frame runs along the straight
// edges and vanishes on every corner.
//
// Install() fixes it app-wide: whenever a Border has a border, a corner radius and
// ClipToBounds, its child is also clipped to the border's *inner* edge (the outer radius
// less the thickness), so the frame stays whole all the way round.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

public static class BorderInnerClip
{
    // On the child: the Border whose inner edge clips it.
    private static readonly AttachedProperty<Border?> OwnerProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, Border?>("NotaInnerClipOwner");

    // On the Border: the child it currently clips.
    private static readonly AttachedProperty<Control?> ClippedProperty =
        AvaloniaProperty.RegisterAttached<Border, Border, Control?>("NotaInnerClipChild");

    /// <summary>Clip bordered, rounded, clipping Borders to their inner edge. Call once at start-up.</summary>
    public static void Install()
    {
        Visual.ClipToBoundsProperty.Changed.AddClassHandler<Border>((b, _) => Sync(b));
        Border.BorderThicknessProperty.Changed.AddClassHandler<Border>((b, _) => Sync(b));
        Border.CornerRadiusProperty.Changed.AddClassHandler<Border>((b, _) => Sync(b));
        Border.ChildProperty.Changed.AddClassHandler<Border>((b, _) => Sync(b));
        // The clip follows layout: the Border's size and where the child sits inside it.
        Visual.BoundsProperty.Changed.AddClassHandler<Control>((c, _) =>
        {
            if (c is Border b && b.GetValue(ClippedProperty) is not null) Apply(b);
            if (c.GetValue(OwnerProperty) is { } owner) Apply(owner);
        });
    }

    private static void Sync(Border b)
    {
        var child = b.Child;
        bool want = child is not null && b.ClipToBounds && b.BorderThickness != default && b.CornerRadius != default;
        var prev = b.GetValue(ClippedProperty);
        if (prev is not null && (!want || prev != child))
        {
            prev.ClearValue(Visual.ClipProperty);
            prev.ClearValue(OwnerProperty);
            b.ClearValue(ClippedProperty);
        }
        if (!want) return;
        child!.SetValue(OwnerProperty, b);
        b.SetValue(ClippedProperty, child);
        Apply(b);
    }

    private static void Apply(Border b)
    {
        if (b.GetValue(ClippedProperty) is not { } child) return;
        var t = b.BorderThickness;
        var r = b.CornerRadius;
        var inner = new Rect(b.Bounds.Size).Deflate(t).Translate(-child.Bounds.Position);
        if (inner.Width <= 0 || inner.Height <= 0) { child.Clip = new RectangleGeometry(default); return; }
        child.Clip = RoundedRect(inner,
            new Size(r.TopLeft - t.Left, r.TopLeft - t.Top),
            new Size(r.TopRight - t.Right, r.TopRight - t.Top),
            new Size(r.BottomRight - t.Right, r.BottomRight - t.Bottom),
            new Size(r.BottomLeft - t.Left, r.BottomLeft - t.Bottom));
    }

    // A rect with an elliptical corner each — the inner edge of a border whose sides differ
    // keeps the outer arc's centre, so its corners are ellipses, not circles.
    private static Geometry RoundedRect(Rect rc, Size tl, Size tr, Size br, Size bl)
    {
        Size Fit(Size s) => new(Math.Clamp(s.Width, 0, rc.Width / 2), Math.Clamp(s.Height, 0, rc.Height / 2));
        tl = Fit(tl); tr = Fit(tr); br = Fit(br); bl = Fit(bl);
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(rc.Left + tl.Width, rc.Top), true);
        ctx.LineTo(new Point(rc.Right - tr.Width, rc.Top));
        if (tr != default) ctx.ArcTo(new Point(rc.Right, rc.Top + tr.Height), tr, 0, false, SweepDirection.Clockwise);
        ctx.LineTo(new Point(rc.Right, rc.Bottom - br.Height));
        if (br != default) ctx.ArcTo(new Point(rc.Right - br.Width, rc.Bottom), br, 0, false, SweepDirection.Clockwise);
        ctx.LineTo(new Point(rc.Left + bl.Width, rc.Bottom));
        if (bl != default) ctx.ArcTo(new Point(rc.Left, rc.Bottom - bl.Height), bl, 0, false, SweepDirection.Clockwise);
        ctx.LineTo(new Point(rc.Left, rc.Top + tl.Height));
        if (tl != default) ctx.ArcTo(new Point(rc.Left + tl.Width, rc.Top), tl, 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(true);
        return g;
    }
}
