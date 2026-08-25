// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Hand-drawn vector glyphs shared by XAML (via {x:Static}) and code-behind. Kept as
// Geometry so they render crisp at any size and follow the theme brush they're stroked with.

using Avalonia;
using Avalonia.Media;

namespace Nota.App;

public static class NotaIcons
{
    /// <summary>A two-link chain (live-freeze "linked" marker): two rounded capsule rings laid
    /// along a 45° diagonal and overlapped so they read as interlocked. Drawn in a 14×14 box;
    /// stroke it (no fill) so each capsule shows as a ring. Used on the Live Freeze button and
    /// the arrangement header badge of a linked source.</summary>
    public static Geometry ChainLink { get; } = BuildChainLink();

    private static Geometry BuildChainLink()
    {
        var group = new GeometryGroup();
        group.Children.Add(Capsule(-1.8, -1.8));   // upper-left link
        group.Children.Add(Capsule(1.8, 1.8));     // lower-right link (overlaps the first)
        return group;
    }

    // One rounded capsule (long axis 8, thickness 3.6), rotated 45° and centred at
    // (7+dx, 7+dy) within the 14×14 box.
    private static Geometry Capsule(double dx, double dy)
    {
        var rect = new RectangleGeometry(new Rect(-4.0, -1.8, 8.0, 3.6))
        {
            RadiusX = 1.8,
            RadiusY = 1.8,
            Transform = new TransformGroup
            {
                Children =
                {
                    new RotateTransform(45),
                    new TranslateTransform(7.0 + dx, 7.0 + dy),
                },
            },
        };
        return rect;
    }
}
