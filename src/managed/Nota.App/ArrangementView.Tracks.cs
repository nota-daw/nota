// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement · track commands on the header selection: select all, copy / cut / paste,
// duplicate, delete (keys while the track headers have focus, and the header menus), the
// multi-track context menu, and where a context-menu "Add track" lands. The engine expands
// a set with every descendant of a group in it, so a group always travels with its children,
// and each command is a single undo step.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Nota.Application;

namespace Nota.App;

public sealed partial class ArrangementView
{
    /// <summary>The selected tracks (multi-selection + primary) in row order. Returns and the
    /// master live in the footer, not in the rows, so they never take part.</summary>
    internal List<int> SelectedTrackIds()
        => _tracks.Where(t => IsTrackMultiSelected(t.Id)).Select(t => t.Id).ToList();

    /// <summary>True when the pointer event landed in the track-header column (the header
    /// cards or the empty column below them) — MainWindow routes the track keys there.</summary>
    internal bool IsInTrackHeaders(PointerEventArgs e)
    {
        if (e.Source is not Visual v || !v.GetSelfAndVisualAncestors().Contains(_scroller)) return false;
        double x = e.GetPosition(_headers).X;
        return x >= 0 && x < HeaderW;
    }

    // Select exactly these tracks; the first becomes the primary (device panel).
    private void SetTrackSelection(IReadOnlyList<int> ids)
    {
        var shown = ids.Where(id => _tracks.Any(t => t.Id == id)).ToList();
        if (shown.Count == 0) { Select(-1, -1); return; }
        Select(shown[0], -1);
        if (shown.Count > 1) foreach (var id in shown) _selTracks.Add(id);
        UpdateHeaderSelection();
    }

    // After a structural edit (and the Refresh): let the session grid + device panel follow.
    private void AfterTrackEdit()
    {
        SessionChanged?.Invoke();
        TrackSelected?.Invoke(SelTrackId);
    }

    // Select the pasted/duplicated set by its top-level members (their children come along).
    private void SelectNewTracks(int[] newIds)
    {
        var set = new HashSet<int>(newIds);
        SetTrackSelection(_tracks.Where(t => set.Contains(t.Id) && !set.Contains(t.GroupId)).Select(t => t.Id).ToList());
    }

    // ---- commands (keys: Cmd+A / C / X / V / D, Delete; header menus) ------
    /// <summary>Cmd+A over the track headers — select every track row.</summary>
    public bool SelectAllTracks()
    {
        if (_tracks.Count == 0) return false;
        var ids = _tracks.Select(t => t.Id).ToList();
        // Keep the current primary (and its device panel) when it's a row.
        int primary = ids.Contains(SelTrackId) ? SelTrackId : ids[0];
        ids.Remove(primary);
        ids.Insert(0, primary);
        SetTrackSelection(ids);
        return true;
    }

    public bool CopySelectedTracks()
    {
        var ids = SelectedTrackIds();
        return ids.Count > 0 && _engine is not null && _engine.CopyTracks(ids.ToArray());
    }

    public bool CutSelectedTracks() => CutTracks(SelectedTrackIds());

    /// <summary>Pastes the copied tracks after the last selected one (in its group), or at
    /// the end when nothing is selected.</summary>
    public bool PasteTracks()
    {
        var ids = SelectedTrackIds();
        return PasteTracksAfter(ids.Count > 0 ? ids[^1] : -1);
    }

    public bool DuplicateSelectedTracks() => DuplicateTracks(SelectedTrackIds());

    public bool DeleteSelectedTracks() => DeleteTracks(SelectedTrackIds());

    private bool CutTracks(IReadOnlyList<int> ids)
    {
        if (_engine is null || ids.Count == 0 || !_engine.CopyTracks(ids.ToArray())) return false;
        return DeleteTracks(ids);
    }

    private bool PasteTracksAfter(int anchorTrackId)
    {
        if (_engine is null || !_engine.HasTrackClipboard()) return false;
        var newIds = _engine.PasteTracks(anchorTrackId);
        if (newIds.Length == 0) return false;
        Refresh();
        SelectNewTracks(newIds);
        AfterTrackEdit();
        return true;
    }

    private bool DuplicateTracks(IReadOnlyList<int> ids)
    {
        if (_engine is null || ids.Count == 0) return false;
        var newIds = _engine.DuplicateTracks(ids.ToArray());
        if (newIds.Length == 0) return false;
        Refresh();
        SelectNewTracks(newIds);
        AfterTrackEdit();
        return true;
    }

    private bool DeleteTracks(IReadOnlyList<int> ids)
    {
        if (_engine is null || ids.Count == 0 || !_engine.RemoveTracks(ids.ToArray())) return false;
        Refresh();
        // Drop what went with them (a group takes its children); a surviving primary stays.
        bool Alive(int id) => _tracks.Any(t => t.Id == id) || _returns.Any(t => t.Id == id);
        if (!Alive(SelTrackId)) Select(-1, -1);
        else { _selTracks.RemoveWhere(id => !Alive(id)); UpdateHeaderSelection(); }
        AfterTrackEdit();
        return true;
    }

    // ---- "Add track" placement ---------------------------------------------
    /// <summary>Moves a freshly added (appended) track next to the row its context menu was
    /// opened on: right after a track, in that track's group; for a group, into it as its
    /// last child. No-op without an anchor row. Call inside the caller's undo group.</summary>
    internal void PlaceNewTrack(int newTrackId, int anchorTrackId)
    {
        if (_engine is null || anchorTrackId <= 0) return;
        var anchor = _tracks.FirstOrDefault(t => t.Id == anchorTrackId);
        if (anchor is null) return;   // a return / the master: new tracks stay at the end
        int parent = anchor.IsGroup ? anchor.Id : anchor.GroupId;
        int after = anchor.IsGroup ? LastDescendantOf(anchor.Id) : anchor.Id;
        if (parent >= 0) _engine.SetTrackGroup(newTrackId, parent);
        // The new track is the last regular one, so every other index is unaffected by its
        // removal inside MoveTrack: after's index + 1 is the slot right behind it.
        int idx = RegularIndexOf(after);
        if (idx >= 0) _engine.MoveTrack(newTrackId, idx + 1);
    }

    // The group's last descendant in engine order (collapsed children included), or the
    // group itself when it's empty — the new child goes behind it to become the last one.
    private int LastDescendantOf(int groupId)
    {
        if (_engine is null) return groupId;
        var parents = new Dictionary<int, int>();
        var order = new List<int>();
        for (int i = 0; i < _engine.TrackCount; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti) || ti.IsReturn) continue;
            parents[ti.Id] = ti.GroupId;
            order.Add(ti.Id);
        }
        bool Inside(int id)
        {
            for (int a = parents.GetValueOrDefault(id, -1), hops = 0; a >= 0 && hops < 64; hops++)
            {
                if (a == groupId) return true;
                a = parents.GetValueOrDefault(a, -1);
            }
            return false;
        }
        int last = groupId;
        foreach (var id in order) if (Inside(id)) last = id;
        return last;
    }

    // ---- multi-track context menu ------------------------------------------
    private void ShowTracksMenu(Control anchor, List<int> ids)
    {
        if (_engine is null) return;
        var flyout = new MenuFlyout();
        string n = $"{ids.Count} tracks";

        var group = MenuKit.Item($"Group {n}", GlyphKind.Folder, () => GroupTracks(-1), MenuKit.GroupKey);
        var color = BuildColorSubmenu(ids);
        var freeze = FreezeTracksMenuItems?.Invoke(ids) ?? Array.Empty<Control>();
        var copy = MenuKit.Item($"Copy {n}", GlyphKind.Copy, () => _engine.CopyTracks(ids.ToArray()), MenuKit.CopyKey);
        var cut = MenuKit.Item($"Cut {n}", GlyphKind.Cut, () => CutTracks(ids), MenuKit.CutKey);
        var paste = MenuKit.Item("Paste track", GlyphKind.Paste, () => PasteTracksAfter(ids[^1]), MenuKit.PasteKey,
                                 enabled: _engine.HasTrackClipboard());
        var dup = MenuKit.Item($"Duplicate {n}", GlyphKind.Duplicate, () => DuplicateTracks(ids), MenuKit.DuplicateKey);
        var del = MenuKit.Item($"Delete {n}", GlyphKind.Trash, () => DeleteTracks(ids), MenuKit.DeleteKey);

        flyout.Items.Add(group);
        flyout.Items.Add(color);
        if (freeze.Count > 0)
        {
            flyout.Items.Add(new Separator());
            foreach (var item in freeze) flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        flyout.Items.Add(copy);
        flyout.Items.Add(cut);
        flyout.Items.Add(paste);
        flyout.Items.Add(dup);
        flyout.Items.Add(new Separator());
        flyout.Items.Add(del);
        flyout.ShowAt(anchor, showAtPointer: true);
    }
}
