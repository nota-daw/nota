// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Taking one track out of its group (#488, #489): the track moves up one level — into the
// group's own parent, or to the top level — and sits right after the group, which keeps its
// other members and its devices. Membership and order are two engine edits; this makes them
// one undo step. The header menu, ⌘⇧G on a member and a header drop above or below every row
// all go through here.

using System.Collections.Generic;

namespace Nota.Application.Editing;

public static class TrackGroups
{
    /// <summary>Moves <paramref name="trackId"/> out of the group that holds it, placing it right
    /// after the group's last track. False when the track isn't in a group.</summary>
    public static bool LeaveGroup(IAudioEngine eng, int trackId)
    {
        var regular = Regular(eng);
        int gid = -1;
        foreach (var t in regular) if (t.Id == trackId) gid = t.GroupId;
        if (gid < 0) return false;
        int parent = -1;
        foreach (var t in regular) if (t.Id == gid) parent = t.GroupId;

        // Where the track goes, as an index in engine order once it is out (MoveTrack takes the
        // track out, then inserts it there). Rows list a parent's children in engine order, and a
        // group's members need not be adjacent in it: an ungrouped track can sit between them.
        // So the slot is right after the group's last row, but never past the
        // group's next sibling, so nothing at the parent's level comes between the group and
        // the track.
        var inGroup = new HashSet<int> { gid };
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var t in regular)
                if (inGroup.Contains(t.GroupId) && inGroup.Add(t.Id)) grew = true;
        }
        regular.RemoveAll(t => t.Id == trackId);
        int groupAt = regular.FindIndex(t => t.Id == gid);
        int dest = groupAt + 1;
        for (int i = groupAt + 1; i < regular.Count; i++)
        {
            if (regular[i].GroupId == parent && !inGroup.Contains(regular[i].Id)) break;   // the next sibling
            if (inGroup.Contains(regular[i].Id)) dest = i + 1;
        }

        eng.BeginUndoGroup();
        try
        {
            eng.SetTrackGroup(trackId, parent);
            eng.MoveTrack(trackId, dest);
        }
        finally { eng.EndUndoGroup(); }
        return true;
    }

    /// <summary>The non-return tracks in engine order — the index space MoveTrack uses.</summary>
    private static List<NotaTrackInfo> Regular(IAudioEngine eng)
    {
        var list = new List<NotaTrackInfo>();
        for (int i = 0; i < eng.TrackCount; i++)
            if (eng.TryGetTrackInfo(i, out var ti) && !ti.IsReturn) list.Add(ti);
        return list;
    }
}
