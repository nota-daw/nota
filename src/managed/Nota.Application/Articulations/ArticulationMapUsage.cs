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

    /// <summary>The library's maps plus every map a project track holds that the library doesn't
    /// (flagged), by name.</summary>
    public static List<(ArticulationMap Map, bool OnlyInProject)> Catalog(IAudioEngine engine, IArticulationLibrary? library)
    {
        var list = (library?.List() ?? Array.Empty<ArticulationMap>()).Select(m => (m, false)).ToList();
        var known = list.Select(x => x.m.Id).ToHashSet();
        for (int i = 0; i < engine.TrackCount; i++)
            if (engine.TryGetTrackInfo(i, out var ti) && ti.IsInstrument
                && ArticulationMapAssignment.Read(engine, ti.Id) is { } m && known.Add(m.Id))
                list.Add((m, true));
        return list.OrderBy(x => x.Item1.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Item1.Id, StringComparer.Ordinal).ToList();
    }
}
