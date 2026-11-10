// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Depth inside device cards for views that draw themselves (style guide, spec 038): the
// same grooves, pressed-in sections and raised parts the XAML styles get from the Shadow.*
// resources. Depth is a slightly different grey plus a 1–2px shadow, never a gradient.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal static class NotaDepth
{
    /// <summary>A Shadow.* resource (built per theme by NotaThemeService), or none.</summary>
    public static BoxShadows Shadow(StyledElement owner, string key)
        => owner.TryFindResource(key, out var o) && o is BoxShadows b ? b : default;

    /// <summary>A groove: the trough grey, pressed in by a 1px inner shadow along the top.</summary>
    public static void Groove(DrawingContext ctx, StyledElement owner, RoundedRect r)
        => ctx.DrawRectangle(NotaPalette.Groove, null, r, Shadow(owner, "Shadow.Groove"));

    /// <summary>A part raised out of its groove (a picked segment): the raised grey, a light
    /// top edge and a 1px drop.</summary>
    public static void Lifted(DrawingContext ctx, StyledElement owner, RoundedRect r)
        => ctx.DrawRectangle(NotaPalette.SurfaceHover, null, r, Shadow(owner, "Shadow.Lifted"));

    /// <summary>A pressed-in section of a device card.</summary>
    public static void Section(DrawingContext ctx, StyledElement owner, RoundedRect r)
        => ctx.DrawRectangle(NotaPalette.DeviceSection, null, r, Shadow(owner, "Shadow.Section"));
}
