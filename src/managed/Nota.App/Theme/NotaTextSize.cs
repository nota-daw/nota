// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Text size (Preferences → Appearance, spec 038 FR-038): every text the app draws grows or
// shrinks by a whole point (−1 … +2), shell and devices alike, independent of the interface
// scale (which scales layout and text together). Controls get it by coercion: TextBlock and
// TextPresenter (a TextBox's text) add the step to whatever size a style, a class or code
// gave them. Neither ever holds the other, and what inherits from them are runs, so no size
// is stepped twice. The custom-drawn layer gets it from NotaFormattedText. A change is applied
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

    /// <summary>Hook the coercion in. Once, before the XAML loads.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        // The default has to be restated: metadata without one would override it with 0.
        TextBlock.FontSizeProperty.OverrideMetadata<TextBlock>(new StyledPropertyMetadata<double>(
            TextBlock.FontSizeProperty.GetDefaultValue(typeof(TextBlock)), coerce: (_, v) => Apply(v)));
        TextElement.FontSizeProperty.OverrideMetadata<TextPresenter>(new StyledPropertyMetadata<double>(
            TextElement.FontSizeProperty.GetDefaultValue(typeof(TextPresenter)), coerce: (_, v) => Apply(v)));
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
