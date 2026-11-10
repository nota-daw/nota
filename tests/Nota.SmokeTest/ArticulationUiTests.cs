// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Note colour by articulation (specs/085, US4, T059): which articulation's colour a note shows —
// its own, else the default it plays as; a missing or disabled id keeps the track colour (it is
// marked missing instead, FR-014) — and the palette slot of an articulation without a colour of
// its own. Runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --articulations`.

using Nota.Application.Articulations;

namespace Nota.SmokeTest;

internal static class ArticulationUiTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        var m = ArticulationFileTests.Full();   // ids 2 (default), 7, and 9 (disabled)
        int?[] Shown(params int[] ids) => ids.Select(id => ArticulationColours.Shown(m, id)?.Id).ToArray();

        yield return (Shown(2, 7).SequenceEqual(new int?[] { 2, 7 }),
            "articulations: a note shows its own articulation's colour");
        yield return (ArticulationColours.Shown(m, 9) is null,
            "articulations: a note on a disabled articulation keeps the track colour (it plays the default and is marked missing)");
        yield return (ArticulationColours.Shown(m, 0)?.Id == m.DefaultArticulationId && m.DefaultArticulationId is not null,
            $"articulations: a note without one shows the default it plays as ({m.DefaultArticulationId})");
        yield return (ArticulationColours.Shown(m, 999) is null,
            "articulations: a note whose articulation the map doesn't hold keeps the track colour (it is marked missing)");

        var off = ArticulationFileTests.Full();
        off.Find(7)!.Enabled = false;
        off.Find(2)!.Enabled = false;
        yield return (ArticulationColours.Shown(off, 7) is null && ArticulationColours.Shown(off, 0) is null,
            "articulations: a disabled articulation, or a disabled default, shows the track colour");

        var none = ArticulationFileTests.Full();
        none.DefaultArticulationId = null;
        yield return (ArticulationColours.Shown(none, 0) is null && ArticulationColours.Shown(null, 7) is null,
            "articulations: no default, or no map, shows the track colour");

        // The palette slot: the source's index when it gave one, else the id; stable and in range.
        var a = new Articulation { Id = 23 };
        var cub = new Articulation { Id = 23, SourceColorIndex = 4 };
        var neg = new Articulation { Id = 1, SourceColorIndex = -3 };
        yield return (ArticulationColours.PaletteSlot(a, 10) == 3 && ArticulationColours.PaletteSlot(cub, 10) == 4
                      && ArticulationColours.PaletteSlot(neg, 10) == 7 && ArticulationColours.PaletteSlot(a, 0) == 0,
            "articulations: an articulation without a colour takes a palette slot from its source index, else its id (always in range)");

        // Reordering the map doesn't change a note's colour: the slot follows the id, not the position.
        var r = ArticulationFileTests.Full();
        int before = ArticulationColours.PaletteSlot(r.Find(9)!, 10);
        ArticulationMapEdits.Move(r, 9, -2);
        yield return (ArticulationColours.PaletteSlot(r.Find(9)!, 10) == before,
            "articulations: moving an articulation in the list keeps its notes' colour");
    }
}
