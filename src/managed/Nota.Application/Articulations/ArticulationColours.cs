// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which colour a note shows when the piano roll colours notes by articulation (specs/085, US4,
// FR-015). Pure, so the smoke test checks it: a note shows the articulation it plays — its own, or
// the map's default when it has none. A note whose articulation the map no longer holds (or holds
// disabled) plays the default but keeps the track colour, so it doesn't pass for a default note;
// the roll's missing mark says why (FR-014). The view turns the answer into a colour: the
// articulation's own RGB, else Nota's palette slot for it — never a guess at Cubase's palette.

namespace Nota.Application.Articulations;

public static class ArticulationColours
{
    /// <summary>The articulation whose colour a note with <paramref name="articulationId"/> shows,
    /// or null for the track colour (no map, a missing or disabled id, or no default to play).</summary>
    public static Articulation? Shown(ArticulationMap? map, int articulationId)
    {
        if (map is null) return null;
        if (articulationId > 0) return map.Find(articulationId) is { Enabled: true } a ? a : null;
        return map.DefaultArticulationId is int d && map.Find(d) is { Enabled: true } def ? def : null;
    }

    /// <summary>The palette base (0 … <paramref name="bases"/> − 1) for an articulation without a
    /// colour of its own: the source file's palette index when it gave one (Cubase), else its id —
    /// stable, so a note keeps its colour when the map is reordered.</summary>
    public static int PaletteSlot(Articulation a, int bases)
    {
        if (bases <= 0) return 0;
        int k = a.SourceColorIndex ?? a.Id;
        return (k % bases + bases) % bases;
    }
}
