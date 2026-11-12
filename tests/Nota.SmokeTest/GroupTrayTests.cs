// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement group trays (specs/094, #584): the per-row slices the header column draws
// groups from. New look: trays stepped in 6 per level, closing under their last row;
// classic look: 0.59.2's own spine stepped in 10 per level. Headless: no window.

using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class GroupTrayTests
{
    // Group ⊃ { DRUMS ⊃ { Drum Rack, Audio 4 }, Instruments (folded, 4 tracks) }, then Consort.
    private const int Group = 1, Drums = 2, Rack = 3, Audio4 = 4, Instr = 5, Consort = 6;

    private static TrayRow[] Project() =>
    [
        new(Group, -1, true, false, 0, 6),
        new(Drums, Group, true, false, 6, 2),
        new(Rack, Drums, false, false, 6, 0),
        new(Audio4, Drums, false, false, 6, 0),
        new(Instr, Group, true, true, 2, 4),
        new(Consort, -1, false, false, 0, 0),
    ];

    public static IEnumerable<(bool, string)> Run()
    {
        var rows = Project();
        var s = GroupTrayLayout.Compute(rows, classic: false);
        TraySlice Of(int id) => s[Array.FindIndex(rows, r => r.Id == id)];

        yield return (Of(Rack).ContentLeft - Of(Consort).ContentLeft <= 12,
            $"trays: a track two levels deep starts at most 12 px further in (got {Of(Rack).ContentLeft - Of(Consort).ContentLeft})");
        yield return (Of(Rack).Chain.Length == 2 && Of(Rack).Chain[0].GroupId == Group && Of(Rack).Chain[1].GroupId == Drums,
            "trays: a member sits in its groups' trays, outer first");
        yield return (Of(Drums).Chain.Length == 2 && Of(Drums).Chain[^1].GroupId == Drums && Of(Drums).Heads,
            "trays: a group's row heads its own tray, inside its parent's");
        yield return (Of(Rack).Closings == 0 && Of(Audio4).Closings == 1,
            "trays: DRUMS closes under its last track");
        yield return (Of(Instr).Closings == 2, "trays: a folded last group closes itself and its parent on its row");
        yield return (Of(Consort).Closings == 0 && Of(Consort).Chain.Length == 0 && Of(Consort).ContentLeft == 0,
            "trays: a top-level track after a group is outside every tray");
        yield return (s.Sum(x => x.Closings) == rows.Count(r => r.IsGroup), "trays: every visible group closes exactly once");
        yield return (Of(Instr).FoldedCount == 4 && Of(Drums).FoldedCount is null && Of(Rack).FoldedCount is null,
            "trays: only a folded group carries a count");

        // A folded group holding a nested group counts tracks only (the caller passes them).
        var nested = new TrayRow[] { new(10, -1, true, true, 1, 3) };
        yield return (GroupTrayLayout.Compute(nested, false)[0] is { FoldedCount: 3, Closings: 1 },
            "trays: a folded group alone closes on its own row with its track count");

        // An empty group closes on its own row.
        var empty = new TrayRow[] { new(20, -1, true, false, 1, 0), new(21, -1, false, false, 2, 0) };
        var e = GroupTrayLayout.Compute(empty, false);
        yield return (e[0].Closings == 1 && e[1].Closings == 0, "trays: an empty group's tray closes under its header");

        // Closing strips must leave the name row on the smallest rows, four levels deep: a Compact
        // track closing four trays keeps its natural 10 px band and 20 px; a slim group row (one
        // line of 18 px chips) closing four trays — the innermost folded or empty — keeps 18.
        int most = 0;
        var deep = new List<TrayRow>();
        for (int d = 0; d < 4; d++) deep.Add(new(100 + d, d == 0 ? -1 : 99 + d, true, false, d, 1));
        deep.Add(new(200, 103, false, false, 4, 0));
        foreach (var x in GroupTrayLayout.Compute(deep, false)) most = Math.Max(most, x.Closings);
        yield return (most == 4 && GroupTrayLayout.ClosingBand(most, 30) == 10 && 30 - GroupTrayLayout.ClosingBand(most, 30) >= 20,
            $"trays: a Compact track closing four trays keeps its name row (band {GroupTrayLayout.ClosingBand(most, 30)})");

        var slim = new List<TrayRow>();
        for (int d = 0; d < 4; d++) slim.Add(new(300 + d, d == 0 ? -1 : 299 + d, true, d == 3, d, 1));
        int slimClosings = GroupTrayLayout.Compute(slim, false)[3].Closings;
        var strips = GroupTrayLayout.ClosingStrips(slimClosings, 26);
        yield return (slimClosings == 4 && 26 - strips.Sum() >= 18 && strips.All(x => x >= 1) && strips[0] >= strips[^1],
            $"trays: a slim group row closing four trays keeps 18 px for its buttons (strips {string.Join("/", strips)})");
        var many = GroupTrayLayout.ClosingStrips(12, 26);
        yield return (many.Length == 12 && 26 - many.Sum() >= 18 && many.All(x => x >= 0),
            "trays: even a dozen closings never eat into a row's content");

        // Classic: 0.59.2's staircase of each row's own spine.
        var c = GroupTrayLayout.Compute(rows, classic: true);
        TraySlice C(int id) => c[Array.FindIndex(rows, r => r.Id == id)];
        yield return (C(Group).SpineX == 0 && C(Drums).SpineX == 10 && C(Rack).SpineX == 20 && C(Consort).SpineX == 0,
            "classic: the spine steps in 10 px per nesting level");
        yield return (C(Rack).ContentLeft == 23 && C(Instr).FoldedCount == 4,
            "classic: content starts past the spine, and a folded group still counts");
    }
}
