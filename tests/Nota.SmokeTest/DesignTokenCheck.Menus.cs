// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Context-menu guard (spec 034). Every menu is drawn by the Nota MenuItem theme and built
// from MenuKit, so a menu stays consistent only if call sites don't route around it: a
// tinted icon control, a destructive command without danger ink, or the old Color / Track
// height submenus coming back. Source scan, counted per file against MenuAllowList.txt
// (a ratchet: counts may only go down).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nota.SmokeTest;

internal static partial class DesignTokenCheck
{
    // A menu icon that is not MenuKit.Icon(…): a MenuItem's Icon set to anything else (in its
    // initializer or as `x.Icon =` on a menu item variable), or a freeze-menu helper handed
    // some other control. Window icons and list-row icons are not menus.
    private static readonly Regex MenuIconOther = new(
        @"(?:\bnew\s+MenuItem\s*\{[^}]*\bIcon\s*=\s*(?!MenuKit\.Icon\b|null\b)[A-Za-z_])"
      + @"|(?:\b(?:mi|it|item|um|ri|ti|sub|baseItem|shade)\.Icon\s*=\s*(?!MenuKit\.Icon\b|null\b)[A-Za-z_])"
      + @"|(?:\bFreezeMenuItem\(\s*[^,]+,\s*(?!MenuKit\.Icon\b)[A-Za-z_])");

    // A destructive command written as a plain item instead of MenuKit.Danger.
    private static readonly Regex MenuDestructivePlain = new(
        @"(?:\bHeader\s*=\s*|\bMenuKit\.Item\(\s*|\bFreezeMenuItem\(\s*)\$?""(?:Delete|Remove|Clear|Discard|Flatten)\b");

    // The submenus the inline rows replaced.
    private static readonly Regex MenuOldSubmenu = new(@"\bHeader\s*=\s*""(?:Color|Colour|Track height)""");

    public static IEnumerable<(bool Ok, string Label)> RunMenus()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var app = Path.Combine(root, "src/managed/Nota.App");

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var samples = new List<string>();
        foreach (var f in SourceFiles(app, ".cs").Where(f => !Norm(f).EndsWith("/MenuKit.cs") && !Norm(f).EndsWith("/MenuKit.Rows.cs")))
        {
            int n = 0, lineNo = 0;
            foreach (var line in File.ReadLines(f))
            {
                lineNo++;
                var code = CodeOf(line);
                int hit = MenuIconOther.Matches(code).Count + MenuDestructivePlain.Matches(code).Count
                        + MenuOldSubmenu.Matches(code).Count;
                if (hit > 0 && samples.Count < 8) samples.Add($"{RelManaged(root, f)}:{lineNo}");
                n += hit;
            }
            if (n > 0) counts[RelManaged(root, f)] = n;
        }

        var allowed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (path, key) in ReadRatchet(Path.Combine(root, RatchetDir, "MenuAllowList.txt")))
            allowed[path] = int.TryParse(key, out var c) ? c : 0;
        var over = counts.Where(kv => kv.Value > allowed.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key} ({kv.Value}, allowed {allowed.GetValueOrDefault(kv.Key)})").ToArray();
        yield return (over.Length == 0,
            over.Length == 0 ? "menus: icons via MenuKit.Icon, destructive items via MenuKit.Danger, no Color / Track height submenus"
                             : $"menu items outside MenuKit conventions: {string.Join(", ", over.Take(8))} — e.g. {string.Join(", ", samples)}");
        var slack = allowed.Where(kv => kv.Value > counts.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key} {kv.Value} → {counts.GetValueOrDefault(kv.Key)}").ToArray();
        yield return (slack.Length == 0,
            slack.Length == 0 ? "menu allow-list counts are tight" : $"menu allow-list above today's count (lower it): {string.Join(", ", slack.Take(8))}");
    }
}
