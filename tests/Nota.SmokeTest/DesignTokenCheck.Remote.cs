// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Remote's page (src/web/remote, spec 014) held as the desktop is: no colour value of
// its own — every colour is a theme token as a custom property, its first-paint fallback
// built from the theme files — and the style guide's rules a source scan can see (spec 090):
// sentence case with no tracking, every corner on the skinnable scale (var(--r-*)), one
// typeface (no monospace), the picked ink for both theme kinds, every custom property it
// reads defined. A failure names the
// file and what it found.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nota.SmokeTest;

internal static partial class DesignTokenCheck
{
    private static readonly Regex HexColour = new(@"#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{4}|[0-9a-fA-F]{3})(?![0-9a-zA-Z_-])");
    private static readonly Regex CssColourFn = new(@"\b(?:rgba?|hsla?)\(\s*[\d.]");
    private static readonly Regex Tracking = new(@"letter-spacing\s*:\s*(?<v>[^;}""]+)");
    private static readonly Regex Radius = new(@"border-radius\s*:\s*(?<v>[^;}""]+)");
    private static readonly Regex VarUse = new(@"var\(\s*(?<n>--[a-z0-9-]+)");
    private static readonly Regex VarDef = new(@"(?<![\w-])(?<n>--[a-z0-9-]+)\s*:|style:(?<n>--[a-z0-9-]+)|['""](?<n>--[a-z0-9-]+)['""]");
    private static readonly HashSet<string> RadiusOk = new() { "0", "50%" };

    public static IEnumerable<(bool Ok, string Label)> RunRemote()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var web = Path.Combine(root, "src/web/remote");
        var src = Path.Combine(web, "src");
        var files = Files(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".svelte") || f.EndsWith(".ts") || f.EndsWith(".css"))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        yield return (files.Count > 10, $"phone sources found ({files.Count})");
        var text = files.ToDictionary(f => f, File.ReadAllText);

        // No colour value in the page: hex, rgb() or hsl() — index.html included.
        var withColour = text.Append(new(Path.Combine(web, "index.html").Replace('\\', '/'), File.ReadAllText(Path.Combine(web, "index.html"))))
            .SelectMany(kv => HexColour.Matches(kv.Value).Concat(CssColourFn.Matches(kv.Value)).Select(m => $"{Rel(web, kv.Key)}: {m.Value}"))
            .ToArray();
        yield return (withColour.Length == 0, withColour.Length == 0 ? "phone: no colour literal (every colour is a theme token)"
            : $"phone: colour literals — use a theme token: {string.Join(", ", withColour.Take(8))}");

        // The fallback comes from the theme files, not from values typed into the plugin.
        var plugin = File.ReadAllText(Path.Combine(web, "theme-plugin.ts"));
        var config = File.ReadAllText(Path.Combine(web, "vite.config.ts"));
        yield return (plugin.Contains("Theme/Themes/") && !HexColour.IsMatch(plugin) && config.Contains("notaTheme()"),
            "phone: the first-paint fallback is built from the theme files by the Vite plugin");

        // Sentence case, no tracking: no letter-spacing but 0, no forced capitals.
        var tracked = text.SelectMany(kv => Tracking.Matches(kv.Value).Where(m => m.Groups["v"].Value.Trim() is not ("0" or "normal"))
            .Select(m => $"{Rel(src, kv.Key)}: {m.Value.Trim()}")).ToArray();
        yield return (tracked.Length == 0, tracked.Length == 0 ? "phone: no tracked labels" : $"phone: letter-spacing — labels are not tracked: {string.Join(", ", tracked)}");
        var caps = text.Where(kv => kv.Value.Contains("text-transform: uppercase") || kv.Value.Contains("text-transform:uppercase")
                                    || (kv.Key.EndsWith(".svelte") && kv.Value.Contains(".toUpperCase()")) || Regex.IsMatch(kv.Value, @"class=""[^""]*\bcaps\b"))
            .Select(kv => Rel(src, kv.Key)).ToArray();
        yield return (caps.Length == 0, caps.Length == 0 ? "phone: no forced capitals" : $"phone: forced capitals (uppercase, toUpperCase, .caps): {string.Join(", ", caps)}");

        // Corners on the scale a skin can re-round: var(--r-*) (or built from it), 0 docked,
        // 50 % for round dots. A literal px radius would ignore the skin.
        var radii = text.SelectMany(kv => Radius.Matches(kv.Value)
                .Where(m => !m.Groups["v"].Value.Contains("var(--r-")
                            && m.Groups["v"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(v => !RadiusOk.Contains(v.Trim())))
                .Select(m => $"{Rel(src, kv.Key)}: {m.Value.Trim()}"))
            .ToArray();
        yield return (radii.Length == 0, radii.Length == 0 ? "phone: corners are on the skinnable scale (var(--r-*), 0, 50 %)" : $"phone: literal radii — use var(--r-*): {string.Join(", ", radii.Take(8))}");

        // One typeface with tabular figures: no monospace face, no .mono class, no font file for one.
        var mono = text.Where(kv => kv.Value.Contains("Geist Mono") || kv.Value.Contains("monospace") || Regex.IsMatch(kv.Value, @"class=""[^""]*\bmono\b|class:mono|\.mono\b"))
            .Select(kv => Rel(src, kv.Key)).ToArray();
        var monoFont = Directory.Exists(Path.Combine(web, "public/fonts")) && Directory.GetFiles(Path.Combine(web, "public/fonts"), "*Mono*").Length > 0;
        yield return (mono.Length == 0 && !monoFont, mono.Length == 0 && !monoFont ? "phone: one typeface, no monospace"
            : $"phone: monospace still used — numbers are tabular Geist: {string.Join(", ", mono)}{(monoFont ? " (and a Mono font file ships)" : "")}");

        // A picked option's text is the bright orange in dark themes and the pale one in light.
        var css = File.ReadAllText(Path.Combine(src, "app.css"));
        bool picked = Regex.IsMatch(css, @"\[data-theme='dark'\][^{]*\{[^}]*--picked-ink:\s*var\(--accent-bright\)")
                      && Regex.IsMatch(css, @"\[data-theme='light'\][^{]*\{[^}]*--picked-ink:\s*var\(--accent-pale\)");
        yield return (picked, "phone: --picked-ink defined for dark (accent-bright) and light (accent-pale) themes");

        // Every custom property read is a theme token or one the page defines — a renamed or
        // removed token fails here, not as a silently transparent fill on the phone.
        var themeFile = Path.Combine(root, "src/managed/Nota.App/Theme/Themes/studio-dark.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(themeFile));
        var tokens = doc.RootElement.GetProperty("colors").EnumerateObject()
            .Select(p => "--" + Regex.Replace(p.Name, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant()).ToHashSet();
        var defined = text.Values.SelectMany(t => VarDef.Matches(t).Select(m => m.Groups["n"].Value)).ToHashSet();
        var unknown = text.SelectMany(kv => VarUse.Matches(kv.Value).Select(m => m.Groups["n"].Value)
                .Where(n => !tokens.Contains(n) && !defined.Contains(n)).Select(n => $"{Rel(src, kv.Key)}: {n}"))
            .Distinct().ToArray();
        yield return (unknown.Length == 0, unknown.Length == 0 ? $"phone: every custom property read is a theme token ({tokens.Count}) or defined"
            : $"phone: custom properties that are neither a theme token nor defined: {string.Join(", ", unknown.Take(8))}");
    }
}
