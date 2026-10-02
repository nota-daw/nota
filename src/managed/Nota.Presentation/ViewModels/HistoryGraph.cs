// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Lays a project's version tree out as rows, newest first, the way `git log --graph`
// does: each branch keeps a lane (a column), a version sits on its lane, lanes that
// branch off it join it from above. Pure — the History tab only draws the result.

using Nota.Application;

namespace Nota.Presentation;

/// <summary>One row of the version graph.</summary>
/// <param name="Lane">Column of this version's dot.</param>
/// <param name="Through">Lanes that pass this row top to bottom (other branches).</param>
/// <param name="Joins">Lanes that end here, coming in from above: branches started on this version.</param>
/// <param name="FromAbove">A newer version on the same lane continues from this one.</param>
/// <param name="ToBelow">This version has a parent (the lane continues down).</param>
/// <param name="LaneCount">Width of the graph in lanes (same for every row).</param>
public sealed record HistoryRow(
    ProjectVersion Version, bool IsHead, int Lane,
    IReadOnlyList<int> Through, IReadOnlyList<int> Joins,
    bool FromAbove, bool ToBelow, int LaneCount);

public static class HistoryGraph
{
    public static IReadOnlyList<HistoryRow> Layout(ProjectHistoryState state)
    {
        var order = NewestFirst(state.Versions);
        var lanes = new List<string?>();   // lane -> the version id it is waiting for
        var rows = new List<(ProjectVersion V, int Lane, List<int> Through, List<int> Joins, bool Above)>();

        foreach (var v in order)
        {
            var waiting = new List<int>();
            for (int i = 0; i < lanes.Count; i++)
                if (lanes[i] == v.Id) waiting.Add(i);

            int lane;
            bool above = waiting.Count > 0;
            if (above) lane = waiting[0];
            else
            {
                lane = lanes.IndexOf(null);   // a tip: first free lane
                if (lane < 0) { lane = lanes.Count; lanes.Add(null); }
            }
            var joins = waiting.Skip(1).ToList();
            foreach (int j in joins) lanes[j] = null;

            var through = new List<int>();
            for (int i = 0; i < lanes.Count; i++)
                if (i != lane && lanes[i] is not null) through.Add(i);

            // A parent that isn't in the list (deleted out from under) ends the lane here.
            lanes[lane] = v.Parent is { } p && state.Versions.Any(x => x.Id == p) ? p : null;
            rows.Add((v, lane, through, joins, above));
        }

        int width = Math.Max(1, lanes.Count);
        return rows.Select(r => new HistoryRow(
                r.V, r.V.Id == state.Head, r.Lane, r.Through, r.Joins,
                r.Above, ToBelow: r.V.Parent is { } p && state.Versions.Any(x => x.Id == p), width))
            .ToList();
    }

    // Newest first, but never a parent above its child (a clock change can put a child's
    // time before its parent's): repeatedly take the newest version none of whose
    // children are still waiting.
    private static List<ProjectVersion> NewestFirst(IReadOnlyList<ProjectVersion> versions)
    {
        var ids = versions.Select(v => v.Id).ToHashSet();
        var pendingChildren = versions.ToDictionary(v => v.Id, _ => 0);
        foreach (var v in versions)
            if (v.Parent is { } p && ids.Contains(p)) pendingChildren[p]++;

        var ready = new PriorityQueue<ProjectVersion, (long, int)>();
        var index = versions.Select((v, i) => (v.Id, i)).ToDictionary(x => x.Id, x => x.i);
        void Offer(ProjectVersion v) => ready.Enqueue(v, (-v.CreatedAt.UtcTicks, -index[v.Id]));
        foreach (var v in versions)
            if (pendingChildren[v.Id] == 0) Offer(v);

        var byId = versions.ToDictionary(v => v.Id);
        var order = new List<ProjectVersion>(versions.Count);
        while (ready.TryDequeue(out var v, out _))
        {
            order.Add(v);
            if (v.Parent is { } p && byId.TryGetValue(p, out var parent) && --pendingChildren[p] == 0) Offer(parent);
        }
        return order;
    }
}
