// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The accent colour (spec 084, #311), checked as data: Orange is every theme file untouched,
// every preset passes the accent rows of the readability bar plus the meter-green, chromatic
// and track-colour rules in every theme (research R3), custom colours are worn, tuned or
// refused as specified, settings resolve and round-trip, and the app wears the accent
// everywhere a theme goes (the service, startup, the theme cards, Nota Remote).

using System.Globalization;
using System.Text.Json;
using Nota.Application;

namespace Nota.SmokeTest;

internal static partial class DesignTokenCheck
{
    public static IEnumerable<(bool Ok, string Label)> RunAccent()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");
        var themes = Directory.EnumerateFiles(Path.Combine(app, "Theme/Themes"), "*.json").OrderBy(f => f)
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f)!, f => ReadRgb(File.ReadAllText(f)));

        foreach (var (id, t) in themes)
        {
            // Orange: the theme file exactly (FR-004, SC-003).
            var o = AccentColours.Wear(t, AccentChoice.Default);
            yield return (o.Tokens is not null && !o.Adjusted && AccentColours.Tokens.All(k => o.Tokens[k] == t[k]),
                $"{id}: Orange is the theme file's own accent family");
            // The theme as shipped already meets the new rules (A1, A2), so they tighten nothing today.
            var shipped = AccentColours.Failures(t, t, includeTracks: true);
            yield return (shipped.Count == 0, shipped.Count == 0 ? $"{id}: shipped orange passes T3.4/T3.5/T3.8/A1/A2/A3"
                : $"{id}: shipped orange fails {string.Join(", ", shipped)}");

            foreach (var p in Accent.Presets.Where(p => p.Id != Accent.Orange))
            {
                var r = AccentColours.Wear(t, new AccentChoice(p.Id));
                var fails = r.Tokens is null ? new List<string> { r.Refusal?.Reason.ToString() ?? "?" } : AccentColours.Failures(t, r.Tokens, includeTracks: true);
                yield return (fails.Count == 0,
                    fails.Count == 0 ? $"{id}: {p.Id} → {Accent.Hex(r.Tokens!["Accent"])}{(r.Adjusted ? " (lightness adjusted)" : "")} passes every accent rule"
                        : $"{id}: {p.Id} fails {string.Join(", ", fails)}");
            }
        }

        // Custom colours (research R3; mockup § 2).
        AccentResult Custom(string theme, string hex) { Accent.TryParseHex(hex, out var rgb); return AccentColours.Wear(themes[theme], new AccentChoice(Accent.Custom, rgb)); }
        bool AllThemes(string hex, Func<AccentResult, bool> ok) => themes.Keys.All(k => ok(Custom(k, hex)));
        Accent.TryParseHex("#00B5D8", out var cyan);
        yield return (AllThemes("#00B5D8", r => r.Tokens is not null && !r.Adjusted && r.Tokens["Accent"] == cyan),
            "custom #00B5D8 is worn exactly in every theme");
        yield return (AllThemes("#4C6FFF", r => r.Tokens is not null && r.Adjusted),
            "custom #4C6FFF is worn with its lightness adjusted (ink on it) in every theme");
        yield return (Custom("studio-light", "#3FA7FF") is { Refusal: { Reason: AccentRefusalReason.Clash, ClashWith: "Solo" } }
                      && Custom("studio-dark", "#3FA7FF").Tokens is not null,
            "custom #3FA7FF is refused in Studio Light only — too close to solo");
        yield return (AllThemes("#8C8C8C", r => r.Refusal?.Reason == AccentRefusalReason.Grey), "custom #8C8C8C is refused as a grey");
        yield return (Custom("studio-dark", "#F06A6E").Refusal is { Reason: AccentRefusalReason.Clash, ClashWith: "Record" },
            "custom = the record red is refused where it is the record red, whatever its lightness");
        yield return (themes.Keys.All(k => { var r = Custom(k, "#5276FE"); return r.Tokens is null || r.Tokens["TextOnAccent"] == AccentColours.InkOn(r.Tokens["Accent"]); }),
            "the ink on a custom accent is derived (dark or white)");

        // Settings (data-model.md).
        yield return (Accent.Resolve(new Settings()).IsDefault, "no setting → Orange");
        yield return (Accent.Resolve(new Settings { Accent = "nope" }).IsDefault, "an unknown preset → Orange");
        yield return (Accent.Resolve(new Settings { Accent = "custom", CustomAccent = "zz" }).IsDefault, "custom with a malformed colour → Orange");
        yield return (Accent.Resolve(new Settings { Accent = "violet" }) == new AccentChoice("violet"), "a stored preset is kept");
        var s = new Settings();
        Accent.Store(s, new AccentChoice(Accent.Custom, 0x4C6FFF));
        Accent.Store(s, new AccentChoice("lime"));
        yield return (Accent.Resolve(s) == new AccentChoice("lime") && Accent.RememberedCustom(s) == 0x4C6FFF,
            "picking a preset keeps the last custom colour for Custom");
        Accent.Store(s, AccentChoice.Default);
        yield return (s.Accent == "" && Accent.Resolve(s).IsDefault, "Orange stores as empty");
        yield return (Accent.TryParseHex("4c6", out var short3) && short3 == 0x44CC66 && Accent.TryParseHex("#4C6FFF", out _) && !Accent.TryParseHex("#4C6FF", out _),
            "hex entry takes #RGB and #RRGGBB, # optional");

        // Wiring: everything a theme reaches wears the accent.
        var service = File.ReadAllText(Path.Combine(app, "Theme/NotaThemeService.cs"));
        yield return (service.Contains("Worn(NotaThemes.Find(_choice.Active(osDark))"), "the theme service wears the accented theme");
        var startup = File.ReadAllText(Path.Combine(app, "App.axaml.cs"));
        yield return (startup.Contains("Accent.Resolve("), "the accent is resolved before the first window paints");
        var remote = File.ReadAllText(Path.Combine(app, "MainWindow.Remote.cs"));
        yield return (remote.Contains("NotaThemeService.Worn(") && remote.Contains("NotaThemeService.Accent"),
            "Nota Remote is sent the accented themes and re-sent on an accent change");
        var prefs = string.Concat(Directory.EnumerateFiles(app, "PreferencesWindow*.cs").Select(File.ReadAllText));
        yield return (prefs.Contains("NotaThemeService.Worn("), "the theme cards preview each theme with the chosen accent");
    }

    private static Dictionary<string, uint> ReadRgb(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("colors").EnumerateObject()
            .ToDictionary(p => p.Name, p => uint.Parse(p.Value.GetString()![^6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}
