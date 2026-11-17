// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Show me's highlight (specs/098, research R7): an accent outline around the control a step
// names — an outline only, as the adorner sits above the control and a fill would hide it —
// drawn on the control's adorner layer so it follows the control as the layout moves. It never
// takes a click: the user does the step on the control itself (US3 #5).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Nota.App;

internal static class HighlightAdorner
{
    private static Border? _current;
    private static Visual? _target;

    /// <summary>The control outlined now, or null (tests).</summary>
    public static Visual? Current => _target;

    /// <summary>Outline <paramref name="target"/>; the previous highlight goes.</summary>
    public static void Show(Control target)
    {
        Hide();
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer) return;
        var ring = new Border
        {
            BorderBrush = NotaPalette.Accent,
            BorderThickness = new Thickness(2),
            CornerRadius = target is TemplatedControl t && t.CornerRadius != default ? Grow(t.CornerRadius) : NotaRadius.Group,
            Margin = new Thickness(-3),
            IsHitTestVisible = false,
        };
        AdornerLayer.SetAdornedElement(ring, target);
        AdornerLayer.SetIsClipEnabled(ring, false);   // the ring sits 3px outside the control
        layer.Children.Add(ring);
        _current = ring;
        _target = target;
    }

    public static void Hide()
    {
        if (_current is not null && _target is not null && AdornerLayer.GetAdornerLayer(_target) is { } layer)
            layer.Children.Remove(_current);
        _current = null;
        _target = null;
    }

    private static CornerRadius Grow(CornerRadius r) => new(r.TopLeft + 3, r.TopRight + 3, r.BottomRight + 3, r.BottomLeft + 3);
}
