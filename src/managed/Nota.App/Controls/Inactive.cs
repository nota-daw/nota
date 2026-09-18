// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Inactive, drawn to the almanac (§ States): a section that is switched off loses its brass
// and its text drops to Ink 6 — its ground, geometry and numbers stay where they are, so the
// layout never jumps and a value stays readable. Opacity is never used for this: a faded
// control's contrast depends on whatever sits behind it.
//
// Set() recolours a subtree in place and remembers what it replaced, so switching back
// restores the exact colours. `interactive` keeps it usable — a bypassed device can still be
// tweaked — otherwise it also stops taking the pointer. Views that repaint a colour on every
// tick should call Set() again after their own paint.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Nota.App;

internal static class Inactive
{
    private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, object?>> Saved = new();

    public static void Set(Control root, bool inactive, bool interactive = false)
    {
        if (!interactive) root.IsHitTestVisible = !inactive;
        foreach (var v in root.GetVisualDescendants().Prepend(root)) Visit(v, inactive);
    }

    private static bool IsBrass(object? brush)
        => ReferenceEquals(brush, NotaPalette.Accent) || ReferenceEquals(brush, NotaPalette.AccentBright)
        || ReferenceEquals(brush, NotaPalette.AccentHover) || ReferenceEquals(brush, NotaPalette.BorderBrass);

    private static void Visit(Visual v, bool inactive)
    {
        switch (v)
        {
            case TextBlock tb:
                Swap(tb, TextBlock.ForegroundProperty, inactive, NotaPalette.TextDisabled);
                break;
            case Glyph g:
                Swap(g, Glyph.ForegroundProperty, inactive, NotaPalette.TextDisabled);
                break;
            case Knob k:
                k.IsDim = inactive;
                break;
            case SliderTrack st:
                st.IsDim = inactive;
                break;
            case SwitchTrack sw:
                sw.IsDim = inactive;
                break;
            case Border b:
                if (inactive ? IsBrass(b.Background) : true) Swap(b, Border.BackgroundProperty, inactive, NotaPalette.BorderStrong, onlyIf: IsBrass);
                if (inactive ? IsBrass(b.BorderBrush) : true) Swap(b, Border.BorderBrushProperty, inactive, NotaPalette.BorderDefault, onlyIf: IsBrass);
                break;
            case Shape sh:
                Swap(sh, Shape.FillProperty, inactive, NotaPalette.BorderStrong, onlyIf: IsBrass);
                Swap(sh, Shape.StrokeProperty, inactive, NotaPalette.BorderStrong, onlyIf: IsBrass);
                break;
        }
    }

    private static void Swap(AvaloniaObject o, AvaloniaProperty p, bool inactive, object dim, System.Func<object?, bool>? onlyIf = null)
    {
        var saved = Saved.GetOrCreateValue(o);
        if (inactive)
        {
            var cur = o.GetValue(p);
            if (ReferenceEquals(cur, dim)) return;
            if (onlyIf is not null && !onlyIf(cur)) return;
            saved[p] = cur;
            o.SetValue(p, dim);
        }
        else if (saved.Remove(p, out var orig))
        {
            if (ReferenceEquals(o.GetValue(p), dim)) o.SetValue(p, orig);
        }
    }
}
