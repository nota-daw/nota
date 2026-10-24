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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace Nota.App;

internal enum NotaThemeKind { Dark, Light }

internal sealed record NotaColourTheme(string Id, string Name, NotaThemeKind Kind, IReadOnlyDictionary<string, Color> Colors)
{
    public bool IsDark => Kind == NotaThemeKind.Dark;
    public Color this[string token] => Colors[token];
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
    private static readonly HashSet<string> AlphaTokens = new()
    {
        "ShadowBevel", "ShadowPopover", "ShadowModal",
        "Well", "WellTrack", "KnobTrack", "DeviceSectionEdge", "Bevel",
    };

    private static readonly Regex IdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex HexPattern = new("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

    private static IReadOnlyList<NotaColourTheme>? _builtIn;
    private static readonly List<string> _loadErrors = new();

    /// <summary>Why a built-in theme file was left out, if any was. Logged at startup.</summary>
    public static IReadOnlyList<string> LoadErrors { get { _ = BuiltIn; return _loadErrors; } }

    /// <summary>Every built-in theme that loaded and validated, in <see cref="BuiltInOrder"/>.</summary>
    public static IReadOnlyList<NotaColourTheme> BuiltIn => _builtIn ??= LoadBuiltIn();

    public static NotaColourTheme? Find(string? id) => id is null ? null : BuiltIn.FirstOrDefault(t => t.Id == id);

    public static IEnumerable<NotaColourTheme> OfKind(NotaThemeKind kind) => BuiltIn.Where(t => t.Kind == kind);

    /// <summary>Studio Dark, the default and the last resort.</summary>
    public static NotaColourTheme Default => Find("studio-dark") ?? BuiltIn[0];

    /// <summary>The token catalogue every theme must match exactly.</summary>
    public static IEnumerable<string> Catalogue => NotaPalette.TokenNames.Concat(ExtraTokens);

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
            list.Add(theme);
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

    /// <summary>Parse and validate one theme file (rules F1–F6). Returns null with a reason
    /// on the first problem; reasons name the token so a broken file is easy to fix.</summary>
    public static NotaColourTheme? Parse(string json, IReadOnlySet<string> catalogue, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("the file must hold one JSON object", out error);
            if (!root.TryGetProperty("formatVersion", out var fv) || fv.ValueKind != JsonValueKind.Number
                || !fv.TryGetInt32(out var version) || version != 1)
                return Fail("formatVersion must be 1", out error);
            // Metadata is read only when it is a string: a number or object here fails this
            // theme with a reason instead of throwing out of the loader.
            static string Str(JsonElement root, string key)
                => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var id = Str(root, "id");
            if (!IdPattern.IsMatch(id) || id == "system") return Fail($"invalid id '{id}'", out error);
            var name = Str(root, "name");
            if (name.Length is < 1 or > 40) return Fail("name must be 1–40 characters", out error);
            var kindText = Str(root, "kind");
            NotaThemeKind kind;
            if (kindText == "dark") kind = NotaThemeKind.Dark;
            else if (kindText == "light") kind = NotaThemeKind.Light;
            else return Fail($"kind must be dark or light, not '{kindText}'", out error);
            if (!root.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Object)
                return Fail("colors missing", out error);

            var map = new Dictionary<string, Color>();
            foreach (var p in colors.EnumerateObject())
            {
                if (!catalogue.Contains(p.Name)) return Fail($"unknown token '{p.Name}'", out error);
                var hex = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : "";
                if (!HexPattern.IsMatch(hex)) return Fail($"token '{p.Name}' has invalid colour '{hex}'", out error);
                var c = Color.Parse(hex);
                if (c.A != 0xFF && !AlphaTokens.Contains(p.Name)) return Fail($"token '{p.Name}' must be opaque", out error);
                map[p.Name] = c;
            }
            var missing = catalogue.Where(t => !map.ContainsKey(t)).ToList();
            if (missing.Count > 0) return Fail("missing token(s) " + string.Join(", ", missing), out error);
            return new NotaColourTheme(id, name, kind, map);
        }
        catch (JsonException e)
        {
            return Fail("not valid JSON: " + e.Message, out error);
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            // A JSON value of the wrong kind somewhere the checks above don't look: fail this
            // theme with a reason (the loader logs and skips it) rather than throw out of startup.
            return Fail("unexpected value: " + e.Message, out error);
        }
    }

    private static NotaColourTheme? Fail(string reason, out string? error) { error = reason; return null; }
}
