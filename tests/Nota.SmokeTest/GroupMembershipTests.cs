// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Taking one track out of its group (#488, #489): the track leaves, the group keeps its other
// members and its devices, the track lands right after the group, and it is one undo step —
// also when every track in the project is grouped. Dissolving a whole group can be undone with
// its devices.

using Nota.Application;
using Nota.Application.Editing;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class GroupMembershipTests
{
    private const int Utility = 4;

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        int a = e.AddAudioTrack(), b = e.AddAudioTrack(), c = e.AddAudioTrack();
        int g = e.CreateGroup(new[] { a, b, c });   // every track is grouped
        e.AddBuiltinDevice(g, Utility);
        int fx = e.TrackDeviceCount(g);

        yield return (!TrackGroups.LeaveGroup(e, g), "groups: a top-level group has no group to leave");
        yield return (TrackGroups.LeaveGroup(e, a), "groups: a member leaves its group even when every track is grouped");
        yield return (Info(e, a).GroupId == -1 && Info(e, b).GroupId == g && Info(e, c).GroupId == g,
            "groups: only that track leaves; the others stay members");
        yield return (Info(e, g).IsGroup && e.TrackDeviceCount(g) == fx, "groups: the group and its devices stay");
        yield return (Order(e).SequenceEqual(new[] { g, b, c, a }), "groups: the track lands right after the group");
        yield return (e.Undo() && Info(e, a).GroupId == g && Order(e).SequenceEqual(new[] { g, a, b, c }),
            "groups: leaving is one undo step");

        // Nested: a track leaves an inner group into the outer one, not to the top level.
        int inner = e.CreateGroup(new[] { b, c });
        yield return (Info(e, inner).GroupId == g, "groups: (an inner group nests in the outer one)");
        yield return (TrackGroups.LeaveGroup(e, b) && Info(e, b).GroupId == g && Info(e, c).GroupId == inner,
            "groups: leaving an inner group moves the track up one level");

        // Dissolving the whole group drops the group track; undo brings it back with its devices.
        e.Ungroup(g);
        yield return (!Order(e).Contains(g), "groups: Ungroup dissolves the group");
        yield return (e.Undo() && Order(e).Contains(g) && e.TrackDeviceCount(g) == fx && Info(e, inner).GroupId == g,
            "groups: undoing an Ungroup brings the group back with its devices");
    }

    private static NotaTrackInfo Info(NotaEngine e, int id)
    {
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti) && ti.Id == id) return ti;
        return default;
    }

    private static List<int> Order(NotaEngine e)
    {
        var l = new List<int>();
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti) && !ti.IsReturn) l.Add(ti.Id);
        return l;
    }
}
