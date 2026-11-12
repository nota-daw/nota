// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Colour themes (spec 011). A theme is one fixed look — dark or light — that gives every
// colour token one value. Theme/Themes/<id>.json is the only place a colour value lives:
// NotaPalette re-tints its slot brushes from the active theme, and NotaThemeService
// publishes those same brushes to XAML. The file shape is the one a user-made theme will
// take later (specs/011-colour-themes/contracts/theme-file.md), so the loader validates
// rather than trusts: a file that fails is left out of the list and noted in LoadErrors,
// never thrown at the UI.
//
// User themes (spec 087) are the same files in <data>/Themes. They join the list after the
// built-ins and can also carry a typeface and corner radii (format 2). One validator —
// Nota.Application.Themes.ThemeFile — serves the built-ins, the folder, MCP and the tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Media;
using Nota.Application.Themes;
using Nota.Infrastructure;

namespace Nota.App;

internal enum NotaThemeKind { Dark, Light }

/// <summary>Where a worn theme came from. Try-on and preview themes are never in the list.</summary>
internal enum ThemeSource { BuiltIn, User, TryOn, Preview }

internal sealed record NotaColourTheme(string Id, string Name, NotaThemeKind Kind, IReadOnlyDictionary<string, Color> Colors)
{
    public bool IsDark => Kind == NotaThemeKind.Dark;
    public Color this[string token] => Colors[token];

    /// <summary>Typeface and corner radii (spec 087). Default for every built-in.</summary>
    public SkinGeometry Geometry { get; init; } = SkinGeometry.Default;
    public ThemeSource Source { get; init; } = ThemeSource.BuiltIn;
    /// <summary>The file a user theme was read from.</summary>
    public string? FilePath { get; init; }
    /// <summary>Readability pairings below their minimum, and a missing typeface.</summary>
    public IReadOnlyList<ThemeWarning> Warnings { get; init; } = Array.Empty<ThemeWarning>();

    public bool IsUser => Source == ThemeSource.User;
}

internal static class NotaThemes
{
    /// <summary>The built-in themes, in the order Preferences lists them. Adding a built-in
    /// theme is its JSON file plus one id here — nothing else names a theme.</summary>
    public static readonly string[] BuiltInOrder = { "studio-dark", "studio-light", "ember-graphite", "ember-paper" };

    /// <summary>Tokens a theme carries that are not palette slots: the shadow colours (their
    /// geometry is Studio structure, built in NotaThemeService) and Fluent's accent ramp.</summary>
    public static readonly string[] ExtraTokens =
    {
        "SystemAccent", "SystemAccentLight1", "SystemAccentLight2", "SystemAccentLight3",
        "SystemAccentDark1", "SystemAccentDark2", "SystemAccentDark3",
        "ShadowBevel", "ShadowPopover", "ShadowModal",
    };

    // Translucent by design; every other token must be opaque. The wells and the device
    // bevel are a faint white (dark themes) or black (light themes) over whatever they sit on.
    public static readonly IReadOnlySet<string> AlphaTokens = new HashSet<string>
    {
        "ShadowBevel", "ShadowPopover", "ShadowModal",
        "Well", "WellTrack", "KnobTrack", "DeviceSectionEdge", "Bevel",
    };

    private static IReadOnlyList<NotaColourTheme>? _builtIn;
    private static Dictionary<string, NotaColourTheme>? _classic;
    private static readonly List<string> _loadErrors = new();
    private static IReadOnlyList<NotaColourTheme> _user = Array.Empty<NotaColourTheme>();
    private static IReadOnlyList<NotaColourTheme>? _all;
    private static IReadOnlyList<(string FileName, string Reason)> _userProblems = Array.Empty<(string, string)>();
    private static IReadOnlyList<string>? _catalogueList;
    private static HashSet<string>? _systemFonts;

    /// <summary>Why a built-in theme file was left out, if any was. Logged at startup.</summary>
    public static IReadOnlyList<string> LoadErrors { get { _ = BuiltIn; _ = Classic(""); return _loadErrors; } }

    /// <summary>Every built-in theme that loaded and validated, in <see cref="BuiltInOrder"/>.</summary>
    public static IReadOnlyList<NotaColourTheme> BuiltIn => _builtIn ??= LoadBuiltIn();

    /// <summary>The user themes that loaded from <see cref="ThemesFolder"/>, by name.</summary>
    public static IReadOnlyList<NotaColourTheme> User => _user;

    /// <summary>Built-ins, then user themes — every theme Preferences lists and a choice can name.</summary>
    public static IReadOnlyList<NotaColourTheme> All => _all ??= BuiltIn.Concat(_user).ToList();

    /// <summary>User theme files that could not be loaded, with the reason in plain words.</summary>
    public static IReadOnlyList<(string FileName, string Reason)> UserLoadProblems => _userProblems;

    /// <summary>Where user themes live: Themes in the Nota data folder (NOTA_DATA_DIR moves it).</summary>
    public static string ThemesFolder => Path.Combine(NotaPaths.DataDir, "Themes");

    /// <summary>Raised after <see cref="ReloadUser"/> on the UI thread.</summary>
    public static event Action? ThemesChanged;

    public static NotaColourTheme? Find(string? id) => id is null ? null : All.FirstOrDefault(t => t.Id == id);

    public static bool IsBuiltIn(string id) => BuiltIn.Any(t => t.Id == id);

    /// <summary>A built-in theme's classic palette (spec 091): 0.59's colours, same id, name and
    /// kind. Never listed — the Look switch puts it on in place of the built-in. Null for a user
    /// theme or an id without one.</summary>
    public static NotaColourTheme? Classic(string id) =>
        (_classic ??= LoadClassic()).TryGetValue(id, out var t) ? t : null;

    private static Dictionary<string, NotaColourTheme> LoadClassic()
    {
        var catalogue = new HashSet<string>(Catalogue);
        var map = new Dictionary<string, NotaColourTheme>();
        foreach (var id in BuiltInOrder)
        {
            var json = ReadResource($"Classic.{id}");
            if (json is null) { _loadErrors.Add($"{id}: no classic palette"); continue; }
            var theme = Parse(json, catalogue, out var error);
            if (theme is null) { _loadErrors.Add($"{id}: {error}"); continue; }
            if (theme.Id != id) { _loadErrors.Add($"{id}: file declares id '{theme.Id}'"); continue; }
            map[id] = theme with { Warnings = Array.Empty<ThemeWarning>() };
        }
        return map;
    }

    public static IEnumerable<NotaColourTheme> OfKind(NotaThemeKind kind) => All.Where(t => t.Kind == kind);

    /// <summary>Studio Dark, the default and the last resort.</summary>
    public static NotaColourTheme Default => BuiltIn.FirstOrDefault(t => t.Id == "studio-dark") ?? BuiltIn[0];

    /// <summary>The token catalogue every theme must match exactly.</summary>
    public static IEnumerable<string> Catalogue => NotaPalette.TokenNames.Concat(ExtraTokens);

    /// <summary>The catalogue in file order — what the validator and the writer take.</summary>
    public static IReadOnlyList<string> CatalogueList => _catalogueList ??= Catalogue.ToList();

    /// <summary>Whether <paramref name="family"/> is a font installed on this system (case-insensitive).</summary>
    public static bool IsFontInstalled(string family)
    {
        if (_systemFonts is null)
        {
            _systemFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var f in FontManager.Current.SystemFonts) _systemFonts.Add(f.Name); }
            catch (Exception) { /* no font manager (tests): treat every family as missing */ }
        }
        return _systemFonts.Contains(family);
    }

    /// <summary>The font families installed on this system, sorted.</summary>
    public static IReadOnlyList<string> SystemFontNames()
    {
        _ = IsFontInstalled("");
        return _systemFonts!.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Validate a theme document with this app's catalogue and installed fonts.</summary>
    public static ThemeValidation Validate(string json) =>
        ThemeFile.Validate(json, CatalogueList, AlphaTokens, IsFontInstalled);

    /// <summary>A validated document as a theme the app can wear.</summary>
    public static NotaColourTheme FromParsed(ThemeValidation v, ThemeSource source, string? path = null)
    {
        var t = v.Theme ?? throw new ArgumentException("the theme is not valid", nameof(v));
        return new NotaColourTheme(t.Id, t.Name, t.IsDark ? NotaThemeKind.Dark : NotaThemeKind.Light,
            t.Colors.ToDictionary(kv => kv.Key, kv => Color.FromUInt32(kv.Value)))
        {
            Geometry = t.Geometry,
            Source = source,
            FilePath = path,
            Warnings = v.Warnings,
        };
    }

    /// <summary>The theme as a file (format 1 or 2), tokens in catalogue order.</summary>
    public static string ToFile(NotaColourTheme theme) =>
        ThemeFile.Write(new ParsedTheme(theme.Id, theme.Name, theme.IsDark,
            theme.Colors.ToDictionary(kv => kv.Key, kv => kv.Value.ToUInt32()), theme.Geometry, ThemeFile.FormatFor(theme.Geometry)),
            CatalogueList);

    /// <summary>Re-read the user themes folder. Never throws; a bad file goes to
    /// <see cref="UserLoadProblems"/>. Raises <see cref="ThemesChanged"/>.</summary>
    public static void ReloadUser()
    {
        _systemFonts = null;   // a font may have been installed or removed meanwhile
        var scan = ThemeFolder.Scan(ThemesFolder, CatalogueList, AlphaTokens, IsBuiltIn, IsFontInstalled);
        _user = scan.Loaded
            .Select(e => FromParsed(e.Validation, ThemeSource.User, e.Path))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _userProblems = scan.Problems;
        _all = null;
        NotaAccent.Forget();
        ThemesChanged?.Invoke();
    }

    private static IReadOnlyList<NotaColourTheme> LoadBuiltIn()
    {
        var catalogue = new HashSet<string>(Catalogue);
        var list = new List<NotaColourTheme>();
        foreach (var id in BuiltInOrder)
        {
            var json = ReadResource(id);
            if (json is null) { _loadErrors.Add($"{id}: no theme file"); continue; }
            var theme = Parse(json, catalogue, out var error);
            if (theme is null) { _loadErrors.Add($"{id}: {error}"); continue; }
            if (theme.Id != id) { _loadErrors.Add($"{id}: file declares id '{theme.Id}'"); continue; }
            // Built-ins are held to the readability bar by the smoke test, not warned about here.
            list.Add(theme with { Warnings = Array.Empty<ThemeWarning>() });
        }
        if (list.Count == 0) throw new InvalidOperationException("No colour theme could be loaded: " + string.Join("; ", _loadErrors));
        return list;
    }

    /// <summary>The raw colour map of a built-in theme, unvalidated — only for NotaPalette to
    /// seed its slots before the catalogue exists. Uses no static state of this class.</summary>
    internal static Dictionary<string, string> ReadSeed(string id)
    {
        var json = ReadResource(id) ?? throw new InvalidOperationException($"Theme file '{id}' is not embedded.");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("colors").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    private static string? ReadResource(string id)
    {
        using var s = typeof(NotaThemes).Assembly.GetManifestResourceStream($"Nota.Themes.{id}.json");
        if (s is null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Parse and validate one theme file (rules F1–F6, and the format-2 parts).
    /// Returns null with the first reason on a problem; reasons name the token so a broken
    /// file is easy to fix. <see cref="ThemeFile.Validate"/> gives every problem.</summary>
    public static NotaColourTheme? Parse(string json, IReadOnlySet<string> catalogue, out string? error)
    {
        var v = ThemeFile.Validate(json, catalogue.Count == CatalogueList.Count ? CatalogueList : catalogue.ToList(), AlphaTokens);
        if (!v.IsValid)
        {
            error = v.Problems.FirstOrDefault()?.Message ?? "";
            return null;
        }
        error = null;
        return FromParsed(v, ThemeSource.BuiltIn);
    }
}
