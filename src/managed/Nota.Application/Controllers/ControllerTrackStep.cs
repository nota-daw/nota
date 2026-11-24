// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Track ◀ / ▶ on a controller select the previous / next track (specs/107 FR-006): the order
// they walk is the track list as the user sees it — groups followed by their members, members
// of a collapsed group left out, return tracks never in it (they sit apart, like the bank's).
// Pure, so the smoke test checks it without the app.

using System.Collections.Generic;

namespace Nota.Application.Controllers;

public static class ControllerTrackStep
{
    /// <summary>The walk order from the hierarchy rows (id, parent group id or −1, in display
    /// order; returns already left out): a row under a collapsed group is skipped.</summary>
    public static List<int> Order(IEnumerable<(int Id, int Parent)> rows, System.Func<int, bool> collapsed)
    {
        var hidden = new HashSet<int>();
        var order = new List<int>();
        foreach (var (id, parent) in rows)
        {
            if (parent > 0 && (hidden.Contains(parent) || collapsed(parent))) { hidden.Add(id); continue; }
            order.Add(id);
        }
        return order;
    }

    /// <summary>The track <paramref name="delta"/> steps from <paramref name="selected"/>; the
    /// first track when nothing in the order is selected; null past either end (FR-009).</summary>
    public static int? Step(IReadOnlyList<int> order, int selected, int delta)
    {
        if (order.Count == 0) return null;
        int at = -1;
        for (int i = 0; i < order.Count; i++) if (order[i] == selected) { at = i; break; }
        if (at < 0) return order[0];
        int to = at + delta;
        return to < 0 || to >= order.Count ? null : order[to];
    }
}
