// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The browser's Modular tab (spec 095, #592): one row per Modular-only node, built from the
// same catalogue the command palette reads, so a node kind added there shows up here as is.
// Search matches a node's name, its category and its aliases ("wobble" finds LFO).

using Nota.Application.Palette;

namespace Nota.Presentation;

public static class ModularBrowserItems
{
    /// <summary>One row per node, in catalogue order.</summary>
    public static IReadOnlyList<BrowserItem> Build(IReadOnlyList<ModularNode> nodes)
    {
        var rows = new List<BrowserItem>(nodes.Count);
        foreach (var n in nodes)
            rows.Add(new BrowserItem
            {
                Name = n.Name,
                Kind = BrowserItemKind.ModularNode,
                BuiltinKind = n.Kind,
                Sub = n.Sub,
                Tip = n.Descriptor.Description,
                Keywords = string.Join(' ', n.Descriptor.Aliases),
            });
        return rows;
    }

    /// <summary>True when every word of <paramref name="query"/> occurs (any case) in the
    /// row's name, category or keywords. An empty query matches everything.</summary>
    public static bool Matches(BrowserItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        string hay = item.Name + " " + item.Sub + " " + item.Keywords;
        foreach (var word in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!hay.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
