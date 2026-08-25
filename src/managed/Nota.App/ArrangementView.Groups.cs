// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Arrangement · track groups (submix buses). A Group track owns child tracks and its own
// device chain; groups may nest. This file holds the view-side glue: the hierarchical row
// ordering (DFS over the groupId forest, collapse-filtered), collapse state, the multi-track
// selection used to form a group, and the create/ungroup commands.

using System.Collections.Generic;
using System.Linq;

namespace Nota.App;

public sealed partial class ArrangementView
{
    // Collapsed group ids (subtree hidden in the arrangement). View state; persisted per project.
    private readonly HashSet<int> _collapsed = new();
    // Multi-track selection used for "Group" (Ctrl/Cmd-click headers). The primary SelTrackId
    // is treated as part of the set even when this is empty.
    private readonly HashSet<int> _selTracks = new();

    internal IReadOnlyCollection<int> CollapsedGroups => _collapsed;
    internal void SetCollapsedGroups(IEnumerable<int> ids)   // restored on project load
    {
        _collapsed.Clear();
        foreach (var id in ids) _collapsed.Add(id);
    }

    /// <summary>Reorders <c>_tracks</c> (currently flat engine order) into hierarchical DFS
    /// order — each group immediately followed by its children — assigning <c>Depth</c> for
    /// indentation and dropping the descendants of any collapsed group.</summary>
    private void ApplyHierarchy()
    {
        if (_tracks.Count == 0) return;
        var byId = new Dictionary<int, TrackVM>(_tracks.Count);
        foreach (var t in _tracks) byId[t.Id] = t;

        var children = new Dictionary<int, List<TrackVM>>();
        var roots = new List<TrackVM>();
        foreach (var t in _tracks)
        {
            if (t.GroupId >= 0 && byId.TryGetValue(t.GroupId, out var par) && par.IsGroup)
            {
                if (!children.TryGetValue(t.GroupId, out var kids)) children[t.GroupId] = kids = new();
                kids.Add(t);
            }
            else roots.Add(t);   // top-level (or an orphaned parent → treat as root)
        }

        var ordered = new List<TrackVM>(_tracks.Count);
        void Walk(TrackVM t, int depth)
        {
            t.Depth = depth;
            ordered.Add(t);
            if (t.IsGroup && !_collapsed.Contains(t.Id) && children.TryGetValue(t.Id, out var kids))
                foreach (var k in kids) Walk(k, depth + 1);
        }
        foreach (var r in roots) Walk(r, 0);

        // Group lane summaries: project every descendant clip onto its ancestor group so the
        // group row can preview them (stacked mini-clips collapsed, a span strip expanded).
        // Runs over the full child map, so a collapsed group still sees its hidden children.
        foreach (var g in ordered)
        {
            if (!g.IsGroup) continue;
            g.GroupMini.Clear();
            int slot = 0;
            void Collect(int parentId)
            {
                if (!children.TryGetValue(parentId, out var kids)) return;
                foreach (var k in kids)
                {
                    if (!k.IsGroup)   // only clip-bearing tracks take a stack sub-lane
                    {
                        int s = slot++;
                        foreach (var c in k.Clips)
                            g.GroupMini.Add(new GroupMiniClip
                            { Start = c.StartBeat, Length = c.LengthBeats, ColorIndex = k.ColorIndex, Slot = s });
                    }
                    Collect(k.Id);   // recurse into nested groups
                }
            }
            Collect(g.Id);
            g.GroupSlotCount = slot;
        }

        _tracks.Clear();
        _tracks.AddRange(ordered);
    }

    /// <summary>True if the group subtree is collapsed (children hidden). Used by the lane
    /// renderer to pick the stacked-clip vs. span-strip preview.</summary>
    internal bool IsGroupCollapsed(int groupId) => _collapsed.Contains(groupId);

    private void ToggleCollapse(int groupId)
    {
        if (!_collapsed.Remove(groupId)) _collapsed.Add(groupId);
        Refresh();
    }

    // ---- multi-track selection (for grouping) -----------------------------
    private bool IsTrackMultiSelected(int trackId) => _selTracks.Contains(trackId) || trackId == SelTrackId;

    private void ToggleTrackInSelection(int trackId)
    {
        // Seed the set with the current primary so Ctrl-click extends rather than replaces.
        if (_selTracks.Count == 0 && SelTrackId > 0) _selTracks.Add(SelTrackId);
        if (!_selTracks.Remove(trackId)) _selTracks.Add(trackId);
        Select(trackId, -1);   // make it primary + show its devices
    }

    private void ClearTrackMultiSelection() => _selTracks.Clear();

    // ---- create / ungroup -------------------------------------------------
    /// <summary>Group the current multi-selection (or <paramref name="fallbackTrackId"/> when
    /// nothing is multi-selected) under a new group track. Returns the group id or -1.</summary>
    private int GroupTracks(int fallbackTrackId)
    {
        if (_engine is null) return -1;
        var set = new HashSet<int>(_selTracks);
        if (SelTrackId > 0) set.Add(SelTrackId);
        if (fallbackTrackId > 0) set.Add(fallbackTrackId);
        // Exclude returns; keep the arrangement's row order stable for a tidy result.
        var ids = _tracks.Where(t => set.Contains(t.Id) && !t.IsReturn).Select(t => t.Id).ToArray();
        if (ids.Length == 0) return -1;
        int gid = _engine.CreateGroup(ids);
        if (gid <= 0) return -1;
        _selTracks.Clear();
        Refresh();
        Select(gid, -1);
        SessionChanged?.Invoke();
        TrackSelected?.Invoke(SelTrackId);
        return gid;
    }

    /// <summary>The group to ungroup for a header: the track itself if it is a group, else the
    /// group that contains it, else -1.</summary>
    private int GroupToUngroupFor(int trackId)
    {
        var t = _tracks.FirstOrDefault(x => x.Id == trackId);
        if (t is null) return -1;
        return t.IsGroup ? t.Id : t.GroupId;
    }

    private void UngroupGroup(int groupId)
    {
        if (_engine is null || groupId <= 0) return;
        _collapsed.Remove(groupId);
        _engine.Ungroup(groupId);
        Refresh();
        SessionChanged?.Invoke();
        TrackSelected?.Invoke(SelTrackId);
    }

    // ---- header drag drop (reorder + into/out of a group) -----------------
    /// <summary>Resolves a header drag-drop at viewport <paramref name="y"/>: joins the target
    /// group (drop onto a group or one of its members), leaves the group (drop on a top-level
    /// row), and reorders next to the target. Membership + order are separate engine edits.</summary>
    private void HandleHeaderDrop(int id, double y)
    {
        if (_engine is null || _tracks.Count == 0) { Select(id, -1); return; }
        int row = System.Math.Clamp((int)System.Math.Floor(y / RowHeight), 0, _tracks.Count - 1);
        var target = _tracks[row];
        var dragged = _tracks.FirstOrDefault(t => t.Id == id);
        if (dragged is null || target.Id == id) { Select(id, -1); return; }
        // Drop onto a group → join it; onto a member → join that member's group; else top-level.
        int desiredParent = target.IsGroup ? target.Id : target.GroupId;
        if (desiredParent != dragged.GroupId) _engine.SetTrackGroup(id, desiredParent);
        int destIdx = RegularIndexOf(target.Id);   // position next to the target in the flat list
        if (destIdx >= 0) _engine.MoveTrack(id, destIdx);
        Refresh();
        Select(id, -1);
        SessionChanged?.Invoke();
    }

    /// <summary>Index of a track among the engine's non-return tracks (the space MoveTrack uses).</summary>
    private int RegularIndexOf(int trackId)
    {
        if (_engine is null) return -1;
        int idx = 0;
        for (int i = 0; i < _engine.TrackCount; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti) || ti.IsReturn) continue;
            if (ti.Id == trackId) return idx;
            idx++;
        }
        return -1;
    }

    // ---- commands (keyboard: Cmd+G / Cmd+Shift+G) -------------------------
    /// <summary>Cmd+G — group the selected tracks. Returns true if a group was formed.</summary>
    public bool GroupSelection() => GroupTracks(-1) > 0;

    /// <summary>Cmd+Shift+G — ungroup the selected group (or the group of the selected track).</summary>
    public bool UngroupSelection()
    {
        int gid = GroupToUngroupFor(SelTrackId);
        if (gid <= 0) return false;
        UngroupGroup(gid);
        return true;
    }
}
