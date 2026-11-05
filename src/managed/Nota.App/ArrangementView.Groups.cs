// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement · track groups (submix buses). A Group track owns child tracks and its own
// device chain; groups may nest. This file holds the view-side glue: the hierarchical row
// ordering (DFS over the groupId forest, collapse-filtered; HierarchyOrder gives the same order
// to the Mixer), the collapse state (a GroupFold shared with the Mixer, specs/070), the multi-track
// selection (grouping + the track commands in ArrangementView.Tracks.cs), and the
// create/ungroup commands.

using System;
using System.Collections.Generic;
using System.Linq;
using Nota.Application;
using Nota.Application.Editing;

namespace Nota.App;

public sealed partial class ArrangementView
{
    // Collapsed group ids (subtree hidden here and in the Mixer). View state, shared with the
    // Mixer and saved in the track-view sidecar (specs/070). MainWindow hands in the shared one.
    private GroupFold _fold = new();
    internal GroupFold Fold
    {
        get => _fold;
        set
        {
            if (ReferenceEquals(_fold, value)) return;
            _fold.Changed -= OnFoldChanged;
            _fold = value;
            _fold.Changed += OnFoldChanged;
            Refresh();
        }
    }
    private void OnFoldChanged() => Refresh();
    // Multi-track header selection (Shift/Cmd-click, Cmd+A). The primary SelTrackId is treated
    // as part of the set even when this is empty; any other Select() clears it.
    private readonly HashSet<int> _selTracks = new();

    internal IReadOnlyCollection<int> CollapsedGroups => _fold.Folded;
    internal void SetCollapsedGroups(IEnumerable<int> ids) => _fold.Set(ids);   // restored on project load

    /// <summary>One row of <see cref="HierarchyOrder"/>: an engine track index and its place
    /// in the group forest.</summary>
    internal readonly record struct HierarchyRow(int Index, int Id, int Depth, int ParentGroupId);

    // The group forest, the one rule both orders use: a track whose GroupId names a group track
    // is that group's child (children keep engine order); anything else — no group, or a parent
    // that is missing or not a group — is a root.
    private static void BuildForest<T>(IReadOnlyList<T> items, Func<T, int> id, Func<T, int> groupId, Func<T, bool> isGroup,
        out Dictionary<int, List<T>> children, out List<T> roots)
    {
        var byId = new Dictionary<int, T>(items.Count);
        foreach (var t in items) byId[id(t)] = t;
        children = new Dictionary<int, List<T>>();
        roots = new List<T>();
        foreach (var t in items)
        {
            int g = groupId(t);
            if (g >= 0 && byId.TryGetValue(g, out var par) && isGroup(par))
            {
                if (!children.TryGetValue(g, out var kids)) children[g] = kids = new();
                kids.Add(t);
            }
            else roots.Add(t);
        }
    }

    /// <summary>Every non-return track in the order the arrangement lists its rows — each group
    /// immediately followed by its children — ignoring collapse. The Mixer lists its strips in
    /// this order (specs/070).</summary>
    internal static IReadOnlyList<HierarchyRow> HierarchyOrder(IAudioEngine eng)
    {
        var infos = new List<(int Index, NotaTrackInfo Info)>();
        int n = eng.TrackCount;
        for (int i = 0; i < n; i++)
            if (eng.TryGetTrackInfo(i, out var ti) && !ti.IsReturn) infos.Add((i, ti));
        BuildForest(infos, t => t.Info.Id, t => t.Info.GroupId, t => t.Info.IsGroup, out var children, out var roots);
        var rows = new List<HierarchyRow>(infos.Count);
        void Walk((int Index, NotaTrackInfo Info) t, int depth, int parent)
        {
            rows.Add(new HierarchyRow(t.Index, t.Info.Id, depth, parent));
            if (depth < 32 && t.Info.IsGroup && children.TryGetValue(t.Info.Id, out var kids))   // depth-guarded: a malformed chain must not hang
                foreach (var k in kids) Walk(k, depth + 1, t.Info.Id);
        }
        foreach (var r in roots) Walk(r, 0, -1);
        return rows;
    }

    /// <summary>Reorders <c>_tracks</c> (currently flat engine order) into hierarchical DFS
    /// order — each group immediately followed by its children — assigning <c>Depth</c> for
    /// indentation and dropping the descendants of any collapsed group.</summary>
    private void ApplyHierarchy()
    {
        if (_tracks.Count == 0) return;
        BuildForest(_tracks, t => t.Id, t => t.GroupId, t => t.IsGroup, out var children, out var roots);

        var ordered = new List<TrackVM>(_tracks.Count);
        void Walk(TrackVM t, int depth)
        {
            t.Depth = depth;
            ordered.Add(t);
            if (t.IsGroup && !_fold.IsFolded(t.Id) && children.TryGetValue(t.Id, out var kids))
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
    internal bool IsGroupCollapsed(int groupId) => _fold.IsFolded(groupId);

    private void ToggleCollapse(int groupId) => _fold.Toggle(groupId);   // Changed → Refresh

    // ---- multi-track selection (Shift/Cmd-click headers, Cmd+A) --------------
    private bool IsTrackMultiSelected(int trackId) => _selTracks.Contains(trackId) || trackId == SelTrackId;

    private void ToggleTrackInSelection(int trackId)
    {
        // A track selection never carries returns (they select among themselves in the footer).
        _selTracks.RemoveWhere(id => _returns.Any(r => r.Id == id));
        // Seed the set with the current primary so the click extends rather than replaces.
        if (_selTracks.Count == 0 && _tracks.Any(t => t.Id == SelTrackId)) _selTracks.Add(SelTrackId);
        if (_selTracks.Add(trackId)) { Select(trackId, -1, keepTrackSet: true); return; }   // make it primary + show its devices

        // Second click removes it; the primary (device panel) moves to another member, if any.
        _selTracks.Remove(trackId);
        int next = _tracks.FirstOrDefault(t => _selTracks.Contains(t.Id))?.Id ?? -1;
        if (next > 0) Select(next, -1, keepTrackSet: true);
        else Select(-1, -1);
    }

    private void ClearTrackMultiSelection() => _selTracks.Clear();

    // Returns select among themselves in the footer (specs/045): ⌘/Ctrl-click adds or removes
    // one, ⇧-click takes the range from the selected return. A selection never mixes returns
    // with tracks, so track commands (group, duplicate, delete …) never reach a return; the
    // selected returns resize together.
    private void ToggleReturnInSelection(int returnId)
    {
        bool IsReturn(int id) => _returns.Any(r => r.Id == id);
        _selTracks.RemoveWhere(id => !IsReturn(id));
        if (_selTracks.Count == 0 && IsReturn(SelTrackId)) _selTracks.Add(SelTrackId);
        if (_selTracks.Add(returnId)) { Select(returnId, -1, keepTrackSet: true); return; }
        _selTracks.Remove(returnId);
        int next = _returns.FirstOrDefault(r => _selTracks.Contains(r.Id))?.Id ?? -1;
        if (next > 0) Select(next, -1, keepTrackSet: true);
        else Select(-1, -1);
    }

    private void SelectReturnRange(int returnId)
    {
        int from = _returns.FindIndex(r => r.Id == SelTrackId), to = _returns.FindIndex(r => r.Id == returnId);
        if (to < 0) return;
        if (from < 0) from = to;
        _selTracks.Clear();
        for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) _selTracks.Add(_returns[i].Id);
        Select(returnId, -1, keepTrackSet: true);
    }

    // Where a Shift-click range starts: the last plain- or Ctrl/Cmd-clicked header (#249).
    private int _trackAnchorId = -1;

    // Shift-click: every row from the anchor to this one, as shown (a folded group is one row
    // that brings its children along); the clicked track becomes the primary.
    private void SelectTrackRange(int trackId)
    {
        int anchor = _tracks.Any(t => t.Id == _trackAnchorId) ? _trackAnchorId : SelTrackId;
        var ids = ArrangementMoves.Range(_tracks.Select(t => t.Id).ToList(), anchor, trackId);
        if (ids.Count == 0) return;
        _selTracks.Clear();
        foreach (int id in ids) _selTracks.Add(id);
        if (_trackAnchorId <= 0) _trackAnchorId = anchor;
        Select(trackId, -1, keepTrackSet: true);
    }

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

    /// <summary>True when the track sits inside a group (it can be taken out of it).</summary>
    private bool IsInGroup(int trackId) => _tracks.FirstOrDefault(x => x.Id == trackId) is { GroupId: > 0 };

    private bool IsGroupId(int trackId) => _tracks.FirstOrDefault(x => x.Id == trackId) is { IsGroup: true };

    /// <summary>Takes one track out of its group (#489): the group, its other members and its
    /// devices stay. One undo step.</summary>
    private void RemoveFromGroup(int trackId)
    {
        if (_engine is null || !TrackGroups.LeaveGroup(_engine, trackId)) return;
        Refresh();
        Select(trackId, -1);
        SessionChanged?.Invoke();
    }

    private void UngroupGroup(int groupId)
    {
        if (_engine is null || groupId <= 0) return;
        _engine.Ungroup(groupId);
        _fold.Remove(groupId);   // after the ungroup, so the refresh it triggers sees the new tree
        Refresh();
        SessionChanged?.Invoke();
        TrackSelected?.Invoke(SelTrackId);
    }

    // ---- header drag drop (reorder + into/out of a group) -----------------
    /// <summary>Resolves a header drag-drop at viewport <paramref name="y"/>: joins the target
    /// group (drop onto a group or one of its members), leaves the group (drop on a top-level
    /// row), and reorders next to the target. Above the first row or below the last one is the
    /// top level, first or last — the way out of a group when every track is grouped (#488).
    /// Membership + order are separate engine edits, made as one undo step.</summary>
    private void HandleHeaderDrop(int id, double y)
    {
        if (_engine is null || _tracks.Count == 0) { Select(id, -1); return; }
        var dragged = _tracks.FirstOrDefault(t => t.Id == id);
        if (dragged is null) { Select(id, -1); return; }
        int desiredParent, destIdx;
        if (RowGapAtY(y) == 0 || y >= RowsHeight)
        {
            desiredParent = -1;
            destIdx = y >= RowsHeight ? RegularCount() - 1 : 0;
        }
        else
        {
            var target = _tracks[RowAtYClamped(y)];
            if (target.Id == id) { Select(id, -1); return; }
            // Drop onto a group → join it; onto a member → join that member's group; else top-level.
            desiredParent = target.IsGroup ? target.Id : target.GroupId;
            destIdx = RegularIndexOf(target.Id);   // position next to the target in the flat list
        }
        if (desiredParent == dragged.GroupId && destIdx == RegularIndexOf(id)) { Select(id, -1); return; }
        _engine.BeginUndoGroup();
        try
        {
            if (desiredParent != dragged.GroupId) _engine.SetTrackGroup(id, desiredParent);
            if (destIdx >= 0) _engine.MoveTrack(id, destIdx);
        }
        finally { _engine.EndUndoGroup(); }
        Refresh();
        Select(id, -1);
        SessionChanged?.Invoke();
    }

    /// <summary>How many non-return tracks the engine holds (MoveTrack's index space).</summary>
    private int RegularCount()
    {
        if (_engine is null) return 0;
        int n = 0;
        for (int i = 0; i < _engine.TrackCount; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && !ti.IsReturn) n++;
        return n;
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

    /// <summary>Cmd+Shift+G — ungroup the selected group, or take the selected track out of its
    /// group (#489: the group and its devices stay).</summary>
    public bool UngroupSelection()
    {
        if (IsGroupId(SelTrackId)) { UngroupGroup(SelTrackId); return true; }
        if (!IsInGroup(SelTrackId)) return false;
        RemoveFromGroup(SelTrackId);
        return true;
    }
}
