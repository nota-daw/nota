// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Owns which palette variant the app is wearing. Two layers have to agree:
//
//   • XAML  — Application.RequestedThemeVariant picks the Light/Dark branch of the
//             ThemeDictionaries in NotaTheme.axaml (and of FluentTheme's own resources),
//             and every {DynamicResource Brush.*} re-resolves on its own.
//   • C#    — NotaPalette.Apply() re-tints the shared brush objects that the
//             custom-drawn layer holds. Those changes don't reach a Render() override
//             that never subscribed to the brush, so we force a repaint of every window.
//
// "System" is expressed as ThemeVariant.Default: Avalonia resolves it from the OS and
// raises ActualThemeVariantChanged when the OS flips, which is the single place the
// managed palette follows from.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nota.Application;

namespace Nota.App;

internal static class NotaThemeService
{
    private static bool _hooked;

    /// <summary>Apply a mode and keep following it. Safe to call repeatedly.</summary>
    public static void Set(AppTheme mode)
    {
        var app = Avalonia.Application.Current;
        if (app is null) return;

        if (!_hooked)
        {
            app.ActualThemeVariantChanged += (_, _) => SyncManagedPalette();
            _hooked = true;
        }

        app.RequestedThemeVariant = mode switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,   // follow the OS
        };

        // ActualThemeVariantChanged only fires when the resolved variant actually moved;
        // sync unconditionally so the first call (and a no-op re-apply) still lands.
        SyncManagedPalette();
    }

    private static void SyncManagedPalette()
    {
        var app = Avalonia.Application.Current;
        if (app is null) return;

        var want = app.ActualThemeVariant == ThemeVariant.Light ? NotaThemeVariant.Light : NotaThemeVariant.Dark;
        if (NotaPalette.Variant == want) return;

        NotaPalette.Apply(want);
        RepaintEverything();
    }

    // Custom-drawn views read palette brushes inside Render() without ever binding to
    // them, so a colour change is invisible until the visual is asked to draw again.
    private static void RepaintEverything()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        foreach (var w in desktop.Windows) Invalidate(w);
    }

    private static void Invalidate(Visual v)
    {
        v.InvalidateVisual();
        foreach (var child in v.GetVisualChildren()) Invalidate(child);
    }
}
