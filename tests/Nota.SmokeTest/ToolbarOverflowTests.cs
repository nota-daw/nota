// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The transport bar's overflow menu (specs/097, #520): for each fit, the shed parts the ⋯
// menu lists — in bar order, never twice, the purely informative ones left out. Headless.

using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class ToolbarOverflowTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        int all = ToolbarOverflow.ShedOrder.Count;

        yield return (all == Enum.GetValues<ToolbarPart>().Length
                      && ToolbarOverflow.ShedOrder.Distinct().Count() == all,
            "overflow: the shed order holds every part once");
        yield return (ToolbarOverflow.BarOrder.Count == all
                      && ToolbarOverflow.BarOrder.Distinct().Count() == all,
            "overflow: the bar order holds every part once");

        yield return (ToolbarOverflow.Entries(0).Count == 0, "overflow: nothing shed → no entries");
        yield return (ToolbarOverflow.Entries(-3).Count == 0, "overflow: a negative fit clamps to none");
        yield return (ToolbarOverflow.Entries(all + 5).SequenceEqual(ToolbarOverflow.Entries(all)),
            "overflow: an over-long fit clamps to all");

        // The first two steps are informative only: the DSP number and the Remote count stay.
        yield return (ToolbarOverflow.Entries(2).Count == 0, "overflow: CPU bar and Remote label need no entry");
        yield return (ToolbarOverflow.Entries(3).SequenceEqual([ToolbarPart.Tap]), "overflow: Tap is the first entry");

        var full = ToolbarOverflow.Entries(all);
        yield return (!full.Any(ToolbarOverflow.IsInfoOnly) && full.Count == all - 2,
            "overflow: everything shed → every part but the informative ones");
        yield return (full.SequenceEqual(ToolbarOverflow.BarOrder.Where(p => !ToolbarOverflow.IsInfoOnly(p))),
            "overflow: entries follow the bar, left to right");

        bool monotonic = true, unique = true;
        for (int n = 0; n < all; n++)
        {
            var a = ToolbarOverflow.Entries(n);
            var b = ToolbarOverflow.Entries(n + 1);
            if (!a.All(b.Contains)) monotonic = false;
            if (b.Distinct().Count() != b.Count) unique = false;
        }
        yield return (monotonic, "overflow: one more step shed never drops an entry");
        yield return (unique, "overflow: no entry twice");

        // US1 #1: down to Snap — Grid and Snap are listed, Grid first as in the bar.
        int snapStep = 0;
        for (int i = 0; i < all; i++) if (ToolbarOverflow.ShedOrder[i] == ToolbarPart.Snap) snapStep = i + 1;
        var bySnap = ToolbarOverflow.Entries(snapStep);
        int grid = IndexOf(bySnap, ToolbarPart.Grid), snap = IndexOf(bySnap, ToolbarPart.Snap);
        yield return (grid >= 0 && snap > grid && !bySnap.Contains(ToolbarPart.Loop) && !bySnap.Contains(ToolbarPart.Master),
            "overflow: Grid and Snap shed → listed in bar order, Loop and Master still in the bar");
    }

    private static int IndexOf(IReadOnlyList<ToolbarPart> list, ToolbarPart p)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == p) return i;
        return -1;
    }
}
