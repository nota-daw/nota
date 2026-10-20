// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The colour themes (spec 011), checked as data: every Theme/Themes/*.json file against
// the theme-file contract (F1–F6: shape, the exact token catalogue, valid colours) and
// the readability pairings every theme must meet (T3: contrast and colour distance,
// calibrated on Studio — specs/011-colour-themes/contracts/ui-contract.md). A failure
// names the theme and the token, so a broken or new theme is easy to fix. Plus the
// appearance resolver: old settings migrate and unknown ids fall back.

using System.Text.Json;
using System.Text.RegularExpressions;
using Nota.Application;

namespace Nota.SmokeTest;

internal static partial class DesignTokenCheck
{
    private static readonly string[] ExtraTokens =
    {
        "SystemAccent", "SystemAccentLight1", "SystemAccentLight2", "SystemAccentLight3",
        "SystemAccentDark1", "SystemAccentDark2", "SystemAccentDark3",
        "ShadowBevel", "ShadowPopover", "ShadowModal",
    };
    private static readonly HashSet<string> AlphaTokens = new() { "ShadowBevel", "ShadowPopover", "ShadowModal" };

    public static IEnumerable<(bool Ok, string Label)> RunThemes()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");
        var palette = File.ReadAllText(Path.Combine(app, "Theme/NotaPalette.cs"));

        // The catalogue: every slot NotaPalette declares by name, plus the non-slot tokens.
        var catalogue = Regex.Matches(palette, @"\bT\((?:nameof\((?<n>\w+)\)|""(?<n>\w+)"")\)").Select(m => m.Groups["n"].Value)
            .Concat(ExtraTokens).ToList();
        var dup = catalogue.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        yield return (catalogue.Count > 0 && dup.Length == 0,
            dup.Length == 0 ? $"token catalogue: {catalogue.Count} tokens" : $"tokens declared twice: {string.Join(", ", dup)}");
        var cat = catalogue.ToHashSet();

        // Built-in order names exactly the files there are (FR-012: file + one line).
        var themeSrc = File.ReadAllText(Path.Combine(app, "Theme/NotaColourTheme.cs"));
        var order = Regex.Match(themeSrc, @"BuiltInOrder\s*=\s*\{(?<b>[^}]*)\}").Groups["b"].Value;
        var ordered = Regex.Matches(order, @"""(?<id>[a-z0-9-]+)""").Select(m => m.Groups["id"].Value).ToList();
        var files = Directory.EnumerateFiles(Path.Combine(app, "Theme/Themes"), "*.json").OrderBy(f => f).ToList();
        var fileIds = files.Select(Path.GetFileNameWithoutExtension).ToList();
        var unlisted = fileIds.Except(ordered).ToArray();
        var noFile = ordered.Except(fileIds).ToArray();
        yield return (unlisted.Length == 0 && noFile.Length == 0,
            unlisted.Length == 0 && noFile.Length == 0 ? $"BuiltInOrder lists exactly the {files.Count} theme files"
                : $"theme files vs BuiltInOrder — not listed: [{string.Join(", ", unlisted)}]; no file: [{string.Join(", ", noFile)}]");

        // Nothing else names a theme: the built-in list, the palette's seed and the
        // appearance defaults are the only places a theme id may appear in code.
        var allowed = new[] { "Theme/NotaColourTheme.cs", "Theme/NotaPalette.cs" };
        var naming = Files(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !allowed.Any(a => f.EndsWith(a)))
            .Where(f => fileIds.Any(id => File.ReadAllText(f).Contains($"\"{id}\"")))
            .Select(f => Rel(app, f)).ToArray();
        yield return (naming.Length == 0,
            naming.Length == 0 ? "no view or control names a theme id (adding a theme is its file + BuiltInOrder)"
                               : $"theme ids named in: {string.Join(", ", naming)}");

        // A theme's preview shows that theme, whichever one is live (FR-015).
        var prefs = File.ReadAllText(Path.Combine(app, "PreferencesWindow.cs"));
        var chip = Regex.Match(prefs, @"class ThemePreview\b.*?\n    \}", RegexOptions.Singleline).Value;
        yield return (chip.Contains("NotaPalette.ColorIn(") && chip.Contains("_theme)") && !Regex.IsMatch(chip, @"\b(slot|track)\.Color\b"),
            "the theme card preview draws the theme's own colours (ColorIn), not the live slots");

        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file)!;
            var (theme, error) = LoadTheme(File.ReadAllText(file), cat);
            yield return (theme is not null && theme.Id == id,
                theme is null ? $"{id}: {error}" : theme.Id != id ? $"{id}: file declares id '{theme.Id}'"
                    : $"{id}: valid ({theme.Kind}, {theme.Colors.Count} tokens, all opaque but shadows)");
            if (theme is null) continue;

            var fails = Pairings(theme.Colors).Where(p => p.Value < p.Min - 1e-9).ToList();
            yield return (fails.Count == 0,
                fails.Count == 0 ? $"{id}: every readability pairing holds (T3.1–T3.9)"
                    : $"{id}: {fails.Count} pairing(s) below the bar — " +
                      string.Join("; ", fails.Take(6).Select(f => $"{f.Id} {f.What} {f.Value:0.00} < {f.Min}")));
        }
    }

    private sealed record ThemeData(string Id, string Kind, Dictionary<string, string> Colors);

    // F1–F6 of contracts/theme-file.md — the same rules NotaThemes.Parse applies at runtime.
    private static (ThemeData? Theme, string? Error) LoadTheme(string json, HashSet<string> catalogue)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("formatVersion", out var fv) || fv.ValueKind != JsonValueKind.Number || fv.GetInt32() != 1) return (null, "formatVersion must be 1");
            var id = r.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            if (!Regex.IsMatch(id, "^[a-z0-9]+(-[a-z0-9]+)*$") || id == "system") return (null, $"invalid id '{id}'");
            var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (name.Length is < 1 or > 40) return (null, "name must be 1–40 characters");
            var kind = r.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
            if (kind is not ("dark" or "light")) return (null, $"kind must be dark or light, not '{kind}'");
            if (!r.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Object) return (null, "colors missing");
            var map = new Dictionary<string, string>();
            foreach (var p in colors.EnumerateObject())
            {
                if (!catalogue.Contains(p.Name)) return (null, $"unknown token '{p.Name}'");
                var hex = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : "";
                if (!Regex.IsMatch(hex, "^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")) return (null, $"token '{p.Name}' has invalid colour '{hex}'");
                if (hex.Length == 9 && !hex[1..3].Equals("FF", StringComparison.OrdinalIgnoreCase) && !AlphaTokens.Contains(p.Name))
                    return (null, $"token '{p.Name}' must be opaque");
                map[p.Name] = hex;
            }
            var missing = catalogue.Where(t => !map.ContainsKey(t)).ToArray();
            if (missing.Length > 0) return (null, "missing token(s) " + string.Join(", ", missing));
            return (new ThemeData(id, kind, map), null);
        }
        catch (JsonException e) { return (null, "not valid JSON: " + e.Message); }
    }

    // ---- T3: readability pairings -------------------------------------------------------

    private sealed record Pairing(string Id, string What, double Value, double Min);

    private static readonly string[] FiveSurfaces = { "BgApp", "Panel", "SurfaceRaised", "SurfaceHover", "BgSunken" };

    private static IEnumerable<Pairing> Pairings(Dictionary<string, string> c)
    {
        Pairing Cr(string id, string a, string b, double min) => new(id, $"{a}/{b}", Contrast(c[a], c[b]), min);
        foreach (var t in new[] { "TextPrimary", "TextSecondary", "TextMuted" })
            foreach (var s in FiveSurfaces) yield return Cr("T3.1", t, s, 4.5);
        foreach (var s in FiveSurfaces) yield return Cr("T3.2", "TextTertiary", s, 3.0);
        foreach (var t in new[] { "TextFaint", "TextDisabled", "TextAxis" })
            foreach (var s in FiveSurfaces) yield return Cr("T3.3", t, s, 2.5);
        foreach (var s in new[] { "BgApp", "Panel", "BgSunken" }) yield return Cr("T3.4", "AccentBright", s, 2.3);
        foreach (var f in new[] { "Accent", "Mute", "Solo", "Record" })
            yield return new("T3.5", $"ink on {f}", Contrast(Ink(c[f]), c[f]), 4.5);
        // The ink tokens the theme itself sets for text and glyphs on a status fill.
        yield return Cr("T3.9", "RecordInk", "Record", 4.5);
        yield return Cr("T3.9", "SuccessInk", "Success", 4.5);
        foreach (var f in Enumerable.Range(1, 9).Select(i => $"Track{i}").Append("ReturnA").Append("ReturnB"))
            yield return new("T3.6", $"ink on {f}", Contrast(Ink(c[f]), c[f]), 3.0);
        var tracks = Enumerable.Range(1, 9).Select(i => $"Track{i}").ToArray();
        for (int i = 0; i < tracks.Length; i++)
            for (int j = i + 1; j < tracks.Length; j++)
                yield return new("T3.7", $"ΔE {tracks[i]}/{tracks[j]}", DeltaE(c[tracks[i]], c[tracks[j]]), 12);
        var states = new[] { "Accent", "Mute", "Solo", "Record" };
        for (int i = 0; i < states.Length; i++)
            for (int j = i + 1; j < states.Length; j++)
                yield return new("T3.8", $"ΔE {states[i]}/{states[j]}", DeltaE(c[states[i]], c[states[j]]), 15);
    }

    private static (double R, double G, double B) Rgb(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8) hex = hex[2..];
        return (Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..6], 16));
    }

    private static double Luminance(string hex)
    {
        static double Ch(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        var (r, g, b) = Rgb(hex);
        return 0.2126 * Ch(r) + 0.7152 * Ch(g) + 0.0722 * Ch(b);
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // NotaPalette.ReadableInkOn: dark ink or white, whichever reads better on the fill.
    private static string Ink(string fill) => Contrast("#17181A", fill) >= Contrast("#FFFFFF", fill) ? "#17181A" : "#FFFFFF";

    private static (double L, double A, double B) Lab(string hex)
    {
        static double Lin(double v) { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        var (r0, g0, b0) = Rgb(hex);
        double r = Lin(r0), g = Lin(g0), b = Lin(b0);
        double x = (r * .4124 + g * .3576 + b * .1805) / .95047, y = r * .2126 + g * .7152 + b * .0722, z = (r * .0193 + g * .1192 + b * .9505) / 1.08883;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    private static double DeltaE(string a, string b)
    {
        var (l1, a1, b1) = Lab(a); var (l2, a2, b2) = Lab(b);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    // ---- Interface scale (spec 027): presets and fallbacks --------------------------------

    public static IEnumerable<(bool Ok, string Label)> RunInterfaceScale()
    {
        yield return (InterfaceScale.Presets.SequenceEqual(new[] { 75, 100, 125, 150, 200 }), "scale presets are 75 / 100 / 125 / 150 / 200 %");
        yield return (InterfaceScale.Presets.All(p => InterfaceScale.Normalize(p) == p), "every preset is kept");
        var bad = new[] { 0, -100, 130, 300, int.MaxValue, int.MinValue };
        yield return (bad.All(v => InterfaceScale.Normalize(v) == 100), "an unknown scale reads as 100 %");
        yield return (InterfaceScale.Factor(125) == 1.25 && InterfaceScale.Factor(75) == 0.75 && InterfaceScale.Factor(130) == 1.0, "scale factor follows the normalised percent");
        yield return (new Settings().InterfaceScale == 100, "no setting → 100 %");
    }

    // ---- Appearance: migration and fallbacks (data-model.md § Resolution) ----------------

    public static IEnumerable<(bool Ok, string Label)> RunAppearance()
    {
        var known = new Dictionary<string, bool>
        {
            ["studio-dark"] = true, ["studio-light"] = false, ["ember-graphite"] = true, ["ember-paper"] = false,
        };
        AppearanceChoice R(Settings s) => Appearance.Resolve(s, known);

        var a = R(new Settings { Theme = AppTheme.Dark });
        yield return (!a.FollowSystem && a.Single == "studio-dark", "legacy Dark → Studio Dark");
        a = R(new Settings { Theme = AppTheme.Light });
        yield return (!a.FollowSystem && a.Single == "studio-light", "legacy Light → Studio Light");
        a = R(new Settings { Theme = AppTheme.System });
        yield return (a.FollowSystem && a.Dark == "studio-dark" && a.Light == "studio-light", "legacy System → follow system, Studio Dark / Studio Light");
        a = R(new Settings());
        yield return (!a.FollowSystem && a.Single == "studio-dark", "no setting → Studio Dark");
        a = R(new Settings { ColourTheme = "ember-paper" });
        yield return (!a.FollowSystem && a.Single == "ember-paper", "a stored theme is kept");
        a = R(new Settings { ColourTheme = "no-such-theme", Theme = AppTheme.Light });
        yield return (!a.FollowSystem && a.Single == "studio-dark", "unknown theme id → Studio Dark");
        a = R(new Settings { ColourTheme = "system", SystemDarkTheme = "gone", SystemLightTheme = "studio-dark" });
        yield return (a.FollowSystem && a.Dark == "studio-dark" && a.Light == "studio-light", "unknown dark-mode id and wrong-kind light-mode id fall back per mode");
        yield return (a.Active(osIsDark: true) == "studio-dark" && R(new Settings { ColourTheme = "system", SystemLightTheme = "ember-paper" }).Active(osIsDark: false) == "ember-paper",
            "follow system wears the theme for the OS mode");

        // The Appearance page: a mode first, then a theme of that kind.
        var m = new AppearanceChoice(false, "ember-graphite", "ember-graphite", "studio-light");
        yield return (Appearance.ModeOf(m, singleIsDark: true) == AppearanceMode.Dark
                      && Appearance.ModeOf(m with { FollowSystem = true }, singleIsDark: true) == AppearanceMode.FollowSystem,
            "the page shows Follow system while following, else the single theme's kind");
        var toLight = Appearance.WithMode(m, AppearanceMode.Light);
        yield return (!toLight.FollowSystem && toLight.Single == "studio-light" && Appearance.WithMode(toLight, AppearanceMode.Dark).Single == "ember-graphite",
            "Dark / Light wear that mode's remembered pick, and switching back returns to it");
        var picked = Appearance.WithPick(toLight, "ember-paper", isDark: false);
        yield return (picked.Single == "ember-paper" && picked.Light == "ember-paper" && picked.Dark == "ember-graphite",
            "picking a theme wears it and remembers it for its mode");
        var across = Appearance.WithPick(m, "studio-light", isDark: false);
        yield return (!across.FollowSystem && across.Single == "studio-light" && Appearance.ModeOf(across, singleIsDark: false) == AppearanceMode.Light && across.Dark == "ember-graphite",
            "picking a theme of the other kind switches mode and keeps the other mode's pick");
        var follow = Appearance.WithPick(Appearance.WithMode(picked, AppearanceMode.FollowSystem), "studio-dark", isDark: true);
        yield return (follow.FollowSystem && follow.Dark == "studio-dark" && follow.Light == "ember-paper" && follow.Single == "ember-paper",
            "while following, a pick sets only that mode's theme");

        var s = new Settings();
        Appearance.Store(s, new AppearanceChoice(false, "ember-paper", "ember-graphite", "studio-light"), singleIsDark: false);
        a = R(s);
        yield return (s.ColourTheme == "ember-paper" && s.Theme == AppTheme.Light && a.Single == "ember-paper" && a.Dark == "ember-graphite",
            "store round-trips and keeps the legacy Theme in step (Light)");
        Appearance.Store(s, a with { FollowSystem = true }, singleIsDark: false);
        yield return (s.ColourTheme == "system" && s.Theme == AppTheme.System && R(s).FollowSystem && R(s).Light == "studio-light",
            "follow system stores as \"system\", legacy Theme = System");
    }
}
