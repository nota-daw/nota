// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Group Link (specs/076, #184): which tracks a linked group ties together, and which clips on
// them count as the same material. The Arrangement (selection, edits, ranges) and the arm
// buttons (TrackArm) both ask this one rule. Pure and UI-free: the smoke test drives it directly.

using System;
using System.Collections.Generic;

namespace Nota.Application.Editing;

/// <summary>What the rule needs to know about a track.</summary>
public readonly record struct LinkTrack(int Id, int GroupId, bool IsGroup, bool IsReturn, bool EditLink);

public sealed class GroupLink
{
    /// <summary>Two clips match when their starts and their ends lie within this many beats
    /// (1/256 of a beat, about 2 ms at 120 BPM — far finer than any grid line).</summary>
    public const double MatchTolerance = 1.0 / 256;

    public static readonly GroupLink None = new(new Dictionary<int, int>(), new Dictionary<int, List<int>>());

    private readonly IReadOnlyDictionary<int, int> _rootOf;              // member track → link root group
    private readonly IReadOnlyDictionary<int, List<int>> _members;       // link root → members, track order

    private GroupLink(IReadOnlyDictionary<int, int> rootOf, IReadOnlyDictionary<int, List<int>> members)
    {
        _rootOf = rootOf;
        _members = members;
    }

    /// <summary>Builds the link sets: a non-group, non-return track belongs to the set of its
    /// outermost ancestor group whose Link is on; members keep the given track order.</summary>
    public static GroupLink Build(IEnumerable<LinkTrack> tracks)
    {
        var list = new List<LinkTrack>(tracks);
        var byId = new Dictionary<int, LinkTrack>(list.Count);
        foreach (var t in list) byId[t.Id] = t;
        var rootOf = new Dictionary<int, int>();
        var members = new Dictionary<int, List<int>>();
        foreach (var t in list)
        {
            if (t.IsGroup || t.IsReturn) continue;
            int root = -1;
            int g = t.GroupId;
            for (int guard = 0; g >= 0 && guard < list.Count && byId.TryGetValue(g, out var p) && p.IsGroup; guard++)
            {
                if (p.EditLink) root = p.Id;   // keep climbing: the outermost linked group wins
                g = p.GroupId;
            }
            if (root < 0) continue;
            rootOf[t.Id] = root;
            if (!members.TryGetValue(root, out var m)) members[root] = m = new List<int>();
            m.Add(t.Id);
        }
        return new GroupLink(rootOf, members);
    }

    /// <summary>True when some group links two or more tracks.</summary>
    public bool AnyLinked
    {
        get { foreach (var m in _members.Values) if (m.Count > 1) return true; return false; }
    }

    /// <summary>True when both tie the same tracks to the same link roots.</summary>
    public bool SameSets(GroupLink other)
    {
        if (_rootOf.Count != other._rootOf.Count) return false;
        foreach (var (t, r) in _rootOf)
            if (!other._rootOf.TryGetValue(t, out var o) || o != r) return false;
        return true;
    }

    /// <summary>The track's link root group id, or -1 when it isn't linked.</summary>
    public int RootOf(int trackId) => _rootOf.TryGetValue(trackId, out var r) ? r : -1;

    /// <summary>True when the track shares a link set with at least one other track.</summary>
    public bool IsLinked(int trackId) => Members(trackId).Count > 0;

    /// <summary>The other tracks of the track's link set (empty when it isn't linked).</summary>
    public IReadOnlyList<int> Members(int trackId)
    {
        if (!_rootOf.TryGetValue(trackId, out var root)) return Array.Empty<int>();
        var all = _members[root];
        var others = new List<int>(all.Count - 1);
        foreach (var id in all) if (id != trackId) others.Add(id);
        return others;
    }

    /// <summary>The given tracks plus every member of their link sets: the given ones first, in
    /// their order, then the added members, each once.</summary>
    public IReadOnlyList<int> ExpandTracks(IEnumerable<int> trackIds)
    {
        var seen = new HashSet<int>();
        var result = new List<int>();
        var given = new List<int>();
        foreach (var id in trackIds) if (seen.Add(id)) { result.Add(id); given.Add(id); }
        foreach (var id in given)
            foreach (var m in Members(id))
                if (seen.Add(m)) result.Add(m);
        return result;
    }

    /// <summary>Whether clip B is the same material as clip A on a linked track: the same start
    /// and the same end, within <see cref="MatchTolerance"/>.</summary>
    public static bool Matches(double startA, double lengthA, double startB, double lengthB)
        => Math.Abs(startA - startB) <= MatchTolerance
        && Math.Abs(startA + lengthA - (startB + lengthB)) <= MatchTolerance;
}
