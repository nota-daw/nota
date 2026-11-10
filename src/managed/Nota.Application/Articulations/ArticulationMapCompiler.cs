// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// An articulation map → the flat table the engine schedules from (specs/085, contracts/c-abi.md):
// articulations sorted by id, each pointing at its ordered actions in one array. Disabled
// articulations are left out, so a note that asks for one plays the default. The engine
// validates the table again; this only shapes it.

namespace Nota.Application.Articulations;

public sealed record CompiledArticulationMap(int DefaultId, NotaArticulationEntry[] Entries, NotaArticulationAction[] Actions);

public static class ArticulationMapCompiler
{
    public const double MaxOffsetMs = 2000, MaxLeadMs = 2000;

    public static CompiledArticulationMap Compile(ArticulationMap map)
    {
        var entries = new List<NotaArticulationEntry>();
        var actions = new List<NotaArticulationAction>();
        var seen = new HashSet<int>();
        foreach (var a in map.Articulations.Where(a => a.Enabled && a.Id > 0).OrderBy(a => a.Id))
        {
            if (!seen.Add(a.Id)) continue;   // a repeated id: the first one wins (the file reader repairs these)
            int start = actions.Count;
            foreach (var act in a.Actions)
            {
                if (!act.IsValid()) continue;
                actions.Add(new NotaArticulationAction
                {
                    Kind = (int)act.Kind,
                    Channel = act.Channel ?? 0,
                    Data1 = act.Kind is MidiActionKind.BankSelectMsb ? 0 : act.Kind is MidiActionKind.BankSelectLsb ? 32 : act.Data1,
                    Data2 = act.Kind is MidiActionKind.ProgramChange ? 0 : act.Data2,
                });
            }
            entries.Add(new NotaArticulationEntry
            {
                Id = a.Id,
                TriggerMode = a.Trigger == TriggerMode.Momentary ? 1 : 0,
                OffsetMs = Finite(a.NoteOffsetMs, -MaxOffsetMs, MaxOffsetMs),
                LeadMs = Finite(a.TriggerLeadMs, 0, MaxLeadMs),
                ActionStart = start,
                ActionCount = actions.Count - start,
            });
        }
        int def = map.DefaultArticulationId is int d && entries.Any(e => e.Id == d) ? d : 0;
        return new CompiledArticulationMap(def, entries.ToArray(), actions.ToArray());
    }

    private static double Finite(double v, double lo, double hi) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : 0;
}

/// <summary>Putting a map on a track and reading it back: compile, serialise, hand both to the engine.</summary>
public static class ArticulationMapAssignment
{
    /// <summary>One undo step. False when the engine refuses (no such instrument track).</summary>
    public static bool Assign(IAudioEngine engine, int trackId, ArticulationMap map)
    {
        var c = ArticulationMapCompiler.Compile(map);
        return engine.SetTrackArticulationMap(trackId, c.DefaultId, c.Entries, c.Actions, ArticulationMapFile.Write(map));
    }

    /// <summary>The track's map, or null when it has none (or what it holds can't be read).</summary>
    public static ArticulationMap? Read(IAudioEngine engine, int trackId)
    {
        var json = engine.GetTrackArticulationMapJson(trackId);
        return json is null ? null : ArticulationMapFile.Read(json, "").Map;
    }
}
