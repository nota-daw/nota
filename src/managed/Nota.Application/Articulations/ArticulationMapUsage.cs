// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which tracks use a map, and putting an edited map on all of them (specs/085, FR-010, T062). A map
// is shared by id: editing it changes every track that holds that id, as one undo step; a duplicate
// has its own id and is left alone. The maps the manager lists are the library's and any project
// track's copy the library doesn't have (a project opened on another machine).

namespace Nota.Application.Articulations;

public static class ArticulationMapUsage
{
    /// <summary>Instrument tracks holding the map with id <paramref name="mapId"/>.</summary>
    public static List<int> TracksUsing(IAudioEngine engine, string mapId)
    {
        var ids = new List<int>();
        for (int i = 0; i < engine.TrackCount; i++)
            if (engine.TryGetTrackInfo(i, out var ti) && ti.IsInstrument
                && ArticulationMapAssignment.Read(engine, ti.Id) is { } m && m.Id == mapId)
                ids.Add(ti.Id);
        return ids;
    }

    /// <summary>The track MIDI offset an articulation of map <paramref name="mapId"/> adds to, for the
    /// manager's readout (FR-018): the selected track's when it holds the map, else the one offset
    /// every track holding it shares; null when no track holds it or their offsets differ.</summary>
    public static double? TrackOffsetFor(IAudioEngine engine, string mapId, int selectedTrack)
    {
        var tracks = TracksUsing(engine, mapId);
        if (tracks.Contains(selectedTrack)) return engine.GetTrackMidiOffsetMs(selectedTrack);
        var offsets = tracks.Select(engine.GetTrackMidiOffsetMs).Distinct().ToList();
        return offsets.Count == 1 ? offsets[0] : null;
    }

    /// <summary>Puts <paramref name="map"/> on every track that holds its id, as one undo step.
    /// Returns how many tracks took it.</summary>
    public static int ApplyEverywhere(IAudioEngine engine, ArticulationMap map)
    {
        var tracks = TracksUsing(engine, map.Id);
        if (tracks.Count == 0) return 0;
        int n = 0;
        engine.BeginUndoGroup();
        try
        {
            foreach (int t in tracks)
                if (ArticulationMapAssignment.Assign(engine, t, map)) n++;
        }
        finally { engine.EndUndoGroup(); }
        return n;
    }

    /// <summary>The maps the manager lists, by name: the library's, and every map a project track
    /// holds. When the project's copy of a map differs from the library's (the library was saved
    /// after an edit that was then undone, or on another machine), the project's copy is listed —
    /// it is what the tracks play, and saving an edit of it must not overwrite them with the
    /// library's version — flagged <c>DiffersFromLibrary</c>. A map no library holds is flagged
    /// <c>OnlyInProject</c>. Several tracks with different copies of one map: the first track's.</summary>
    public static List<(ArticulationMap Map, bool OnlyInProject, bool DiffersFromLibrary)> Catalog(IAudioEngine engine, IArticulationLibrary? library)
    {
        var lib = new Dictionary<string, ArticulationMap>(StringComparer.Ordinal);
        // A copied file can repeat an ID under another filename. Keep the first readable copy,
        // as with project tracks below, so a duplicate can't prevent the manager from opening.
        foreach (var m in library?.List() ?? Array.Empty<ArticulationMap>()) lib.TryAdd(m.Id, m);
        var list = new List<(ArticulationMap Map, bool OnlyInProject, bool DiffersFromLibrary)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < engine.TrackCount; i++)
            if (engine.TryGetTrackInfo(i, out var ti) && ti.IsInstrument
                && ArticulationMapAssignment.Read(engine, ti.Id) is { } m && seen.Add(m.Id))
            {
                if (!lib.TryGetValue(m.Id, out var saved)) list.Add((m, true, false));
                else if (ArticulationMapFile.Write(saved) != ArticulationMapFile.Write(m)) list.Add((m, false, true));
                else list.Add((saved, false, false));
            }
        foreach (var m in lib.Values)
            if (!seen.Contains(m.Id)) list.Add((m, false, false));
        return list.OrderBy(x => x.Map.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Map.Id, StringComparer.Ordinal).ToList();
    }
}
