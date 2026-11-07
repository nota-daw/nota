// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Text size (Preferences → Appearance, spec 038 FR-038): every text the app draws grows or
// shrinks by a whole point (−1 … +2) across the shell, independent of the interface scale
// (which scales layout and text together). Device cards are the exception: their layouts are
// fixed at 700 × 260, so a card is marked Fixed and its text keeps its size. Controls get the
// step by coercion: TextBlock and TextPresenter (a TextBox's text) add it to whatever size a
// style, a class or code gave them. Neither ever holds the other, and what inherits from them
// are runs, so no size is stepped twice. The custom-drawn layer gets it from NotaFormattedText
// (passing itself as the owner, so a view inside a card is left alone). A change is applied
// live: every open window re-coerces its text and redraws.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Nota.Application;

namespace Nota.App;

internal static class NotaTextSize
{
    private static int _step;
    private static bool _installed;

    /// <summary>The step in points (one of <see cref="TextSize.Steps"/>).</summary>
    public static int Step => _step;

    /// <summary>A size as drawn: <paramref name="size"/> plus the step, never under the floor.</summary>
    public static double Apply(double size) => _step == 0 || size <= 0 ? size : Math.Max(NotaType.Floor, size + _step);

    /// <summary>Device cards keep develop's fixed 700 × 260 layouts, so their text keeps its own
    /// size: set on a card (inherited by everything in it), the step is not applied there.</summary>
    public static readonly AttachedProperty<bool> FixedProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Fixed", typeof(NotaTextSize), inherits: true);

    public static void SetFixed(Visual v, bool value) => v.SetValue(FixedProperty, value);

    /// <summary>The owner for text a static helper draws inside a card (it has no control to pass).</summary>
    public static readonly AvaloniaObject InCard = new Border { [FixedProperty] = true };

    /// <summary><see cref="Apply(double)"/> for text drawn by (or inside) <paramref name="owner"/>.</summary>
    public static double Apply(double size, AvaloniaObject? owner) => owner?.GetValue(FixedProperty) == true ? size : Apply(size);

    /// <summary>Hook the coercion in. Once, before the XAML loads.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        // The default has to be restated: metadata without one would override it with 0.
        TextBlock.FontSizeProperty.OverrideMetadata<TextBlock>(new StyledPropertyMetadata<double>(
            TextBlock.FontSizeProperty.GetDefaultValue(typeof(TextBlock)), coerce: (o, v) => Apply(v, o)));
        TextElement.FontSizeProperty.OverrideMetadata<TextPresenter>(new StyledPropertyMetadata<double>(
            TextElement.FontSizeProperty.GetDefaultValue(typeof(TextPresenter)), coerce: (o, v) => Apply(v, o)));
        // A text that moves into (or out of) a device card re-coerces with the card's rule.
        FixedProperty.Changed.AddClassHandler<TextBlock>((tb, _) => tb.CoerceValue(TextBlock.FontSizeProperty));
        FixedProperty.Changed.AddClassHandler<TextPresenter>((tp, _) => tp.CoerceValue(TextElement.FontSizeProperty));
    }

    /// <summary>Wear <paramref name="step"/> (normalised) everywhere, live.</summary>
    public static void Set(int step)
    {
        int s = TextSize.Normalize(step);
        if (s == _step) return;
        _step = s;
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        foreach (var window in desktop.Windows) Reapply(window);
    }

    private static void Reapply(Visual root)
    {
        foreach (var v in root.GetSelfAndVisualDescendants())
        {
            if (v is TextBlock tb) tb.CoerceValue(TextBlock.FontSizeProperty);
            else if (v is TextPresenter tp) tp.CoerceValue(TextElement.FontSizeProperty);
            if (v is Layoutable l) l.InvalidateMeasure();
            v.InvalidateVisual();   // custom-drawn text (NotaFormattedText) re-shapes on its next render
        }
    }
}
