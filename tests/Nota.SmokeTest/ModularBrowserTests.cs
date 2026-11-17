// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The browser's Modular tab (spec 095, #592): every catalogue node becomes a row, search
// finds a node by name, category or alias, and the browser remembers a separate last tab for
// Modular. Headless: no window.

using Nota.Application.Palette;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class ModularBrowserTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        var nodes = ModularCatalog.All;
        var rows = ModularBrowserItems.Build(nodes);
        yield return (rows.Count == nodes.Count, $"modular tab: one row per catalogue node ({rows.Count}/{nodes.Count})");
        bool inOrder = true;
        for (int i = 0; i < Math.Min(rows.Count, nodes.Count); i++)
            inOrder &= rows[i].Name == nodes[i].Name && rows[i].BuiltinKind == nodes[i].Kind
                       && rows[i].Kind == BrowserItemKind.ModularNode && rows[i].Sub == nodes[i].Sub
                       && rows[i].Tip.Length > 0;
        yield return (inOrder, "modular tab: rows keep catalogue order, kind, category and description");

        string[] Find(string q) => rows.Where(r => ModularBrowserItems.Matches(r, q)).Select(r => r.Name).ToArray();
        yield return (Find("").Length == rows.Count, "modular tab: an empty search lists every node");
        yield return (Find("wobble") is ["LFO"], $"modular tab: 'wobble' finds LFO ({string.Join(",", Find("wobble"))})");
        yield return (Find("sidechain").Contains("Envelope Follower"), "modular tab: 'sidechain' finds Envelope Follower");
        yield return (Find("monitor").Contains("Scope"), "modular tab: 'monitor' (category) finds Scope");
        yield return (Find("env foll") is ["Envelope Follower"], $"modular tab: every word must match ({string.Join(",", Find("env foll"))})");
        yield return (Find("ADSR") is ["ADSR"] && Find("adsr").Length == 1, "modular tab: search ignores case");
        yield return (Find("zzz").Length == 0, "modular tab: nonsense finds nothing");

        const int Start = 0, Modular = 9, Fx = 1, Files = 3;
        var m = new BrowserTabMemory(Start, Modular);
        yield return (m.Enter() == Modular, "tab memory: the first visit to Modular shows the Modular tab");
        m.Picked(Fx);
        yield return (m.Leave() == Start, "tab memory: leaving Modular returns to the tab picked outside");
        m.Picked(Files);
        yield return (m.Enter() == Fx, "tab memory: Modular remembers the tab picked there");
        yield return (m.Leave() == Files, "tab memory: outside remembers its own tab");
        m.Picked(Modular);   // can't happen outside (the tab isn't on the rail); ignored
        yield return (m.Leave() == Files, "tab memory: the Modular tab is never the outside tab");
    }
}
