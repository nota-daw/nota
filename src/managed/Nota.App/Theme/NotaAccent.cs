// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A colour theme wearing the user's accent (spec 084): the theme's own colours with the
// accent family replaced by Nota.Application.AccentColours. The result is a theme like any
// other, so the palette, XAML brushes, Fluent's accent ramp, the theme cards and Nota Remote
// take it through the paths spec 011 already has. Cached, so wearing the same choice again
// hands back the same object and NotaThemeService's "already worn" check holds.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal static class NotaAccent
{
    private static readonly Dictionary<(string Id, AccentChoice Choice), NotaColourTheme> Cache = new();

    /// <summary><paramref name="theme"/> wearing <paramref name="choice"/>. Orange — and a
    /// choice the theme cannot wear, which no preset is — is the theme itself.</summary>
    public static NotaColourTheme Wear(NotaColourTheme theme, AccentChoice choice)
    {
        // Always from the theme file's own colours: a theme already wearing another accent
        // would otherwise be re-coloured relative to that accent.
        theme = NotaThemes.Find(theme.Id) ?? theme;
        if (choice.IsDefault) return theme;
        if (Cache.TryGetValue((theme.Id, choice), out var worn)) return worn;
        var result = AccentColours.Wear(Rgb(theme), choice);
        if (result.Tokens is null) worn = theme;
        else
        {
            var colors = new Dictionary<string, Color>(theme.Colors);
            foreach (var (k, v) in result.Tokens) colors[k] = Color.FromUInt32(0xFF000000 | v);
            worn = theme with { Colors = colors };
        }
        // Presets are a fixed set; custom colours are not — a drag in the picker wears a new
        // one every step. Keep only the latest custom colour, so the cache stays at the
        // presets × themes plus one colour × themes.
        if (choice.Preset == Accent.Custom)
            foreach (var stale in Cache.Keys.Where(k => k.Choice.Preset == Accent.Custom && k.Choice != choice).ToList())
                Cache.Remove(stale);
        Cache[(theme.Id, choice)] = worn;
        return worn;
    }

    /// <summary>How each built-in theme would wear <paramref name="choice"/> — the picker's
    /// "In themes" strip and its note.</summary>
    public static IEnumerable<(NotaColourTheme Theme, AccentResult Result)> Preview(AccentChoice choice) =>
        NotaThemes.BuiltIn.Select(t => (t, AccentColours.Wear(Rgb(t), choice)));

    /// <summary>The accent <paramref name="theme"/> wears for <paramref name="choice"/>, as a colour.</summary>
    public static Color AccentIn(NotaColourTheme theme, AccentChoice choice) => Wear(theme, choice)["Accent"];

    private static readonly Dictionary<NotaColourTheme, IReadOnlyDictionary<string, uint>> RgbCache = new(ReferenceEqualityComparer.Instance);

    private static IReadOnlyDictionary<string, uint> Rgb(NotaColourTheme theme)
    {
        if (!RgbCache.TryGetValue(theme, out var m))
            RgbCache[theme] = m = Enumerable.ToDictionary(theme.Colors, c => c.Key, c => c.Value.ToUInt32() & 0xFFFFFF);
        return m;
    }

    public static uint ToRgb(Color c) => c.ToUInt32() & 0xFFFFFF;
    public static Color FromRgb(uint rgb) => Color.FromUInt32(0xFF000000 | rgb);
}
