// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The classic palettes (spec 091): one per built-in, the whole catalogue, 0.59.2's colour for
// every token 0.59.2 had — checked against a committed snapshot of the tag, since CI clones are
// shallow. Readability may warn only where 0.59.2 itself did (clarified: keep 0.59.2 exactly);
// a pairing that involves a token added since must pass.

using System.Text.Json;
using System.Text.RegularExpressions;
using Nota.Application.Themes;

namespace Nota.SmokeTest;

internal static partial class DesignTokenCheck
{
    public static IEnumerable<(bool Ok, string Label)> RunClassicPalettes()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");
        var palette = File.ReadAllText(Path.Combine(app, "Theme/NotaPalette.cs"));
        var catalogue = Regex.Matches(palette, @"\bT\((?:nameof\((?<n>\w+)\)|""(?<n>\w+)"")\)").Select(m => m.Groups["n"].Value)
            .Concat(ExtraTokens).ToList();
        var snapshot = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            File.ReadAllText(Path.Combine(root, "tests/Nota.SmokeTest/Fixtures/classic-0.59.2.json")))!;

        var builtIns = Directory.EnumerateFiles(Path.Combine(app, "Theme/Themes"), "*.json")
            .Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
        foreach (var id in builtIns)
        {
            var path = Path.Combine(app, "Theme/Themes/Classic", id + ".json");
            if (!File.Exists(path)) { yield return (false, $"classic: {id} has a classic palette"); continue; }
            var v = ThemeFile.Validate(File.ReadAllText(path), catalogue, AlphaTokens);
            yield return (v.IsValid && v.Theme!.Id == id && v.Theme.FormatVersion == 1,
                v.IsValid ? $"classic: {id} validates with the full catalogue (format 1, same id)"
                          : $"classic: {id} — {ThemeFolder.Describe(v.Problems)}");
            if (!v.IsValid || !snapshot.TryGetValue(id!, out var old)) continue;

            var colours = v.Theme!.Colors;
            var off = old.Where(kv => !colours.TryGetValue(kv.Key, out var c) || c != ThemeFile.ParseHex(kv.Value))
                .Select(kv => kv.Key).ToList();
            yield return (off.Count == 0, off.Count == 0 ? $"classic: {id} has 0.59.2's value for all {old.Count} of its tokens"
                                                         : $"classic: {id} differs from 0.59.2 in {string.Join(", ", off)}");

            // Readability: a warning stands only if every token in the pairing is 0.59.2's own.
            var since = v.Warnings.Where(w => w.Kind == ThemeWarning.Readability && w.Tokens.Any(t => !old.ContainsKey(t)))
                .Select(w => $"{w.Pairing} {string.Join("/", w.Tokens)}").ToList();
            yield return (since.Count == 0, since.Count == 0
                ? $"classic: {id} warns only where 0.59.2 did ({v.Warnings.Count(w => w.Kind == ThemeWarning.Readability)} inherited)"
                : $"classic: {id} fails readability with a new token: {string.Join(", ", since)}");
        }
        var stray = Directory.EnumerateFiles(Path.Combine(app, "Theme/Themes/Classic"), "*.json")
            .Select(Path.GetFileNameWithoutExtension).Except(builtIns).ToList();
        yield return (stray.Count == 0, stray.Count == 0 ? "classic: no palette without a built-in" : $"classic: stray palettes {string.Join(", ", stray)}");
    }
}
