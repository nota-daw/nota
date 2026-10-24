// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Owns which colour theme the app is wearing (spec 011). Three layers have to agree:
//
//   • Slots — NotaPalette.Apply() re-tints the shared brush objects. The same objects are
//             published once into Application.Resources under their Brush.* keys, so
//             every {DynamicResource Brush.*} in XAML follows along with no lookup.
//   • Non-brush resources — shadows (BoxShadows) and Fluent's SystemAccentColor ramp are
//             values, not brushes, so they are re-set from the theme on every switch.
//   • Variant — Application.RequestedThemeVariant is the theme's kind (Dark/Light), which
//             drives Fluent's own internals and the OS window chrome. In follow-system mode
//             it is Default: Avalonia resolves it from the OS and raises
//             ActualThemeVariantChanged when the OS flips, which swaps the theme.
//
// Custom-drawn Render() overrides read slot colours without subscribing to them, so every
// switch forces a repaint of every window.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Nota.Application;

namespace Nota.App;

internal static class NotaThemeService
{
    private static bool _hooked;
    private static bool _published;
    private static AppearanceChoice _choice = new(false, Appearance.StudioDark, Appearance.StudioDark, Appearance.StudioLight);

    /// <summary>The appearance being followed.</summary>
    public static AppearanceChoice Choice => _choice;

    /// <summary>Every loaded theme id → whether it is dark — what <see cref="Appearance.Resolve"/> needs.</summary>
    public static IReadOnlyDictionary<string, bool> KnownThemes =>
        NotaThemes.BuiltIn.ToDictionary(t => t.Id, t => t.IsDark);

    /// <summary>Wear <paramref name="choice"/> and keep following it. Safe to call repeatedly.</summary>
    public static void Set(AppearanceChoice choice)
    {
        var app = Avalonia.Application.Current;
        if (app is null) return;
        Publish(app);
        _choice = choice;

        if (!_hooked)
        {
            app.ActualThemeVariantChanged += (_, _) => { if (_choice.FollowSystem) ApplyActive(); };
            _hooked = true;
        }

        if (choice.FollowSystem)
            app.RequestedThemeVariant = ThemeVariant.Default;   // follow the OS
        // A single theme sets its own kind inside ApplyActive.
        ApplyActive();
    }

    // The theme the choice names right now — for follow system, the one for the OS mode.
    private static void ApplyActive()
    {
        var app = Avalonia.Application.Current;
        if (app is null) return;
        // With no OS signal Avalonia reports Light, which is the spec's fallback too.
        bool osDark = app.ActualThemeVariant == ThemeVariant.Dark;
        var theme = NotaThemes.Find(_choice.Active(osDark)) ?? NotaThemes.Default;

        if (!_choice.FollowSystem)
            app.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (ReferenceEquals(NotaPalette.Theme, theme)) return;

        NotaPalette.Apply(theme);
        ApplyResources(app, theme);
        RepaintEverything();
    }

    // Put the slot brushes into the application resources once. They are re-tinted in
    // place afterwards, so the resource entries never need replacing.
    private static void Publish(Avalonia.Application app)
    {
        if (_published) return;
        foreach (var (key, brush) in NotaPalette.AllKeys) app.Resources[key] = brush;
        _published = true;
    }

    // The resources that are values rather than brushes: Studio's elevation, built from the
    // theme's colours (geometry is structure — specs/010 data-model § Elevation), and the
    // accent ramp Fluent's own templates read.
    private static void ApplyResources(Avalonia.Application app, NotaColourTheme t)
    {
        var res = app.Resources;
        res["Shadow.Raised"] = new BoxShadows(new BoxShadow { IsInset = true, OffsetY = 1, Color = t["ShadowBevel"] });
        res["Shadow.Sunken"] = new BoxShadows(new BoxShadow { IsInset = true, Spread = 1, Color = t["Rule"] });
        res["Shadow.Popover"] = new BoxShadows(new BoxShadow { Spread = 1, Color = t["Seam"] },
            new[] { new BoxShadow { OffsetY = 10, Blur = 28, Color = t["ShadowPopover"] } });
        res["Shadow.Modal"] = new BoxShadows(new BoxShadow { Spread = 1, Color = t["Seam"] },
            new[] { new BoxShadow { OffsetY = 24, Blur = 64, Color = t["ShadowModal"] } });

        // Depth inside device cards (style guide, spec 038): 1px lines and offsets only, no
        // blur. The dark inner edge is plain black at a strength set by the theme's kind.
        var press = Color.FromArgb(t.IsDark ? (byte)0x47 : (byte)0x14, 0x00, 0x00, 0x00);
        res["Shadow.SwitchInset"] = new BoxShadows(new BoxShadow { IsInset = true, OffsetY = 1, Color = press });
        res["Shadow.Card"] = new BoxShadows(new BoxShadow { Spread = 1, Color = t["Seam"] },
            new[] { new BoxShadow { IsInset = true, OffsetY = 1, Color = t["Bevel"] } });
        res["Shadow.Section"] = new BoxShadows(new BoxShadow { IsInset = true, Spread = 1, Color = t["DeviceSectionEdge"] },
            new[] { new BoxShadow { IsInset = true, OffsetY = 1, Color = press } });
        res["Shadow.Groove"] = new BoxShadows(new BoxShadow { IsInset = true, OffsetY = 1, Color = press });
        res["Shadow.Lifted"] = new BoxShadows(new BoxShadow { OffsetY = 1, Color = press },
            new[] { new BoxShadow { IsInset = true, OffsetY = 1, Color = t["Bevel"] } });

        res["SystemAccentColor"] = t["SystemAccent"];
        for (int i = 1; i <= 3; i++)
        {
            res[$"SystemAccentColorLight{i}"] = t[$"SystemAccentLight{i}"];
            res[$"SystemAccentColorDark{i}"] = t[$"SystemAccentDark{i}"];
        }
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
        // An open popup's content lives in its own popup root, outside the window's visual
        // tree — reach it through the popup, or an open ComboBox list stays in the old colours.
        if (v is Avalonia.Controls.Primitives.Popup { IsOpen: true, Child: { } content }) Invalidate(content);
        foreach (var child in v.GetVisualChildren()) Invalidate(child);
    }
}
