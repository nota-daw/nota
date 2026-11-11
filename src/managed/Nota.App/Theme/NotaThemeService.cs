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
// The theme is worn with the user's accent (spec 084): NotaAccent overlays the accent family
// on the theme, and the accented theme goes through the same three layers.
//
// Custom-drawn Render() overrides read slot colours without subscribing to them, so every
// switch forces a repaint of every window.
//
// A theme also carries a typeface and corner radii (spec 087): NotaSkin wears them, and a
// change re-coerces every window. User themes join the list from the themes folder; a reload
// re-wears the active one. A try-on (MCP) is worn without being stored, and RenderScoped
// wears a theme for the length of one call — the skin previews — and puts the live one back.

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

    private static AccentChoice _accent = AccentChoice.Default;
    private static NotaColourTheme? _tryOn;

    /// <summary>The appearance being followed.</summary>
    public static AppearanceChoice Choice => _choice;

    /// <summary>The accent every theme is worn with.</summary>
    public static AccentChoice Accent => _accent;

    /// <summary>Raised after a theme or accent change has been applied and repainted.</summary>
    public static event Action? Changed;

    /// <summary><paramref name="theme"/> as it is worn with the current accent — for the theme
    /// cards and Nota Remote, so they show what the window would wear.</summary>
    public static NotaColourTheme Worn(NotaColourTheme theme) => NotaAccent.Wear(theme, _accent);

    /// <summary>Wear a new accent in whichever theme is active. Live; saving is the caller's.</summary>
    public static void SetAccent(AccentChoice accent)
    {
        if (accent == _accent) return;
        _accent = accent;
        ApplyActive();
    }

    /// <summary>Every loaded theme id (built-in and user) → whether it is dark — what
    /// <see cref="Appearance.Resolve"/> needs.</summary>
    public static IReadOnlyDictionary<string, bool> KnownThemes =>
        NotaThemes.All.ToDictionary(t => t.Id, t => t.IsDark);

    /// <summary>The choice the settings hold, resolved against the themes loaded now. Set by the
    /// app at startup; read again after each reload of the themes folder.</summary>
    public static Func<AppearanceChoice>? StoredChoice { get; set; }

    /// <summary>The theme being tried on (not stored), if any.</summary>
    public static NotaColourTheme? TryOnTheme => _tryOn;

    /// <summary>The theme the window wears now, accent included.</summary>
    public static NotaColourTheme? WornTheme => NotaPalette.Theme;

    /// <summary>Wear <paramref name="theme"/> live without storing it, until <see cref="RevertTryOn"/>,
    /// a <see cref="Set"/> (a pick in Preferences) or a restart.</summary>
    public static void TryOn(NotaColourTheme theme)
    {
        _tryOn = theme;
        ApplyActive();
    }

    /// <summary>End a try-on: back to the stored choice. No-op without one.</summary>
    public static void RevertTryOn()
    {
        if (_tryOn is null) return;
        _tryOn = null;
        ApplyActive();
    }

    /// <summary>Wear <paramref name="theme"/> (with the user's accent, or the theme's own) for the
    /// length of <paramref name="render"/>, then put the live theme back — all inside the caller's
    /// UI-thread job, so the compositor never sends a frame of it to the screen. Live windows are
    /// neither repainted nor re-coerced; <paramref name="render"/> re-coerces its own trees.</summary>
    public static void RenderScoped(NotaColourTheme theme, bool themeAccent, Action render)
    {
        var app = Avalonia.Application.Current;
        if (app is null) { render(); return; }
        var live = NotaPalette.Theme;
        var liveGeometry = NotaSkin.Geometry;
        var scoped = themeAccent ? theme : Worn(theme);
        InScope = true;
        try
        {
            NotaPalette.Apply(scoped);
            ApplyResources(app, scoped);
            NotaSkin.Set(scoped.Geometry);
            render();
        }
        finally
        {
            if (live is not null)
            {
                NotaPalette.Apply(live);
                ApplyResources(app, live);
            }
            // Resource changes inside the scope make live controls re-evaluate their typeface and
            // radii against the scoped skin; give every live window its own skin back.
            if (NotaSkin.Set(liveGeometry)) NotaSkin.Reapply();
            InScope = false;
        }
    }

    /// <summary>True while <see cref="RenderScoped"/> wears a theme for a picture: views built
    /// then must not reload or re-wear themes (that would put the live theme back mid-picture).</summary>
    public static bool InScope { get; private set; }

    /// <summary>Wear <paramref name="choice"/> and keep following it. Safe to call repeatedly.</summary>
    public static void Set(AppearanceChoice choice)
    {
        var app = Avalonia.Application.Current;
        if (app is null) return;
        Publish(app);
        _choice = choice;

        _tryOn = null;   // a pick replaces a try-on

        if (!_hooked)
        {
            app.ActualThemeVariantChanged += (_, _) => { if (_choice.FollowSystem) ApplyActive(); };
            // The folder changed under the worn theme: wear what it holds now (or the fallback).
            NotaThemes.ThemesChanged += () =>
            {
                // Re-resolve from the settings: a stored theme that had fallen back comes back
                // once its file loads again (the fallback was never stored).
                if (StoredChoice?.Invoke() is { } stored) _choice = stored;
                ApplyActive();
            };
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
        var theme = _tryOn is not null ? Worn(_tryOn) : Worn(NotaThemes.Find(_choice.Active(osDark)) ?? FallbackFor(osDark));

        if (!_choice.FollowSystem || _tryOn is not null)
            app.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        else
            app.RequestedThemeVariant = ThemeVariant.Default;
        if (ReferenceEquals(NotaPalette.Theme, theme)) return;

        NotaPalette.Apply(theme);
        ApplyResources(app, theme);
        if (NotaSkin.Set(theme.Geometry)) NotaSkin.Reapply();
        RepaintEverything();
        Changed?.Invoke();
    }

    // A stored theme that is gone (a user file removed or broken): the default for that mode,
    // as Appearance.Resolve falls back. The stored choice is kept, so fixing the file brings it back.
    private static NotaColourTheme FallbackFor(bool osDark)
    {
        bool dark = _choice.FollowSystem ? osDark : true;
        return dark ? NotaThemes.Default
                    : NotaThemes.BuiltIn.FirstOrDefault(t => t.Id == Appearance.StudioLight) ?? NotaThemes.Default;
    }

    // Put the slot brushes into the application resources once. They are re-tinted in
    // place afterwards, so the resource entries never need replacing.
    private static void Publish(Avalonia.Application app)
    {
        if (_published) return;
        foreach (var (key, brush) in NotaPalette.AllKeys) app.Resources[key] = brush;
        // The glyphs of the caption buttons Avalonia draws on Windows read this Fluent key;
        // point it at the secondary-text brush so they follow the theme like our own icons.
        app.Resources["CaptionButtonForeground"] = app.Resources["Brush.TextSecondary"];
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

        // Depth inside device cards (style guide, spec 038): 1px lines and 1–2px shadows, no
        // gradients. The dark inner shadows are plain black at a strength set by the theme's kind.
        Color Black(double dark, double light) => Color.FromArgb((byte)Math.Round((t.IsDark ? dark : light) * 255), 0x00, 0x00, 0x00);
        res["Shadow.SwitchInset"] = new BoxShadows(new BoxShadow { IsInset = true, OffsetY = 1, Blur = 2, Color = Black(.5, .15) });
        res["Shadow.Card"] = new BoxShadows(new BoxShadow { Spread = 1, Color = t["Seam"] },
            new[] { new BoxShadow { IsInset = true, OffsetY = 1, Color = t["Bevel"] } });
        res["Shadow.Section"] = new BoxShadows(new BoxShadow { IsInset = true, Spread = 1, Color = t["DeviceSectionEdge"] },
            new[] { new BoxShadow { IsInset = true, OffsetY = 1, Blur = t.IsDark ? 2 : 1, Color = Black(.25, .06) } });
        res["Shadow.Groove"] = new BoxShadows(new BoxShadow { IsInset = true, OffsetY = 1, Blur = 2, Color = Black(.45, .18) });
        res["Shadow.Lifted"] = t.IsDark
            ? new BoxShadows(new BoxShadow { IsInset = true, OffsetY = 1, Color = t["Bevel"] },
                new[] { new BoxShadow { OffsetY = 1, Blur = 2, Color = Black(.45, .45) } })
            : new BoxShadows(new BoxShadow { Spread = 1, Color = Black(.1, .1) },
                new[] { new BoxShadow { OffsetY = 1, Blur = 2, Color = Black(.15, .15) } });

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
