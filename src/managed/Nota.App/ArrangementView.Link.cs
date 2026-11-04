// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement · group Link (specs/076, #184). A linked group ties its tracks together: picking
// a clip also picks the clips at the same place on the other tracks (GroupLink.Matches), so
// the block edits that act on the selection — move, nudge, duplicate, copy, cut, delete — take
// them along. This file holds that selection rule and the helpers the edits that act on one
// clip (split, trim, fades) and on time ranges use. The link sets come from the engine, not the
// shown rows, so a folded group's tracks still follow.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Nota.Application.Localization;
using Nota.Application.Editing;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class ArrangementView
{
    // The clips in _sel that are there because they match a clip the user picked, not because
    // they were picked: they take a move's time shift but keep their own track, and they are
    // the partners a trim or a fade on the picked clip passes on to. Read through LinkedSel.
    private readonly HashSet<(int track, int clip)> _selLinked = new();

    // Rebuilt lazily after every Refresh (a toggle, a regroup and undo all refresh). _linkSeen
    // keeps the last one built, so a Refresh can tell when the link sets changed.
    private GroupLink? _link, _linkSeen;
    private GroupLink Link => _link ??= _linkSeen = _engine is { } e ? TrackArm.Links(e) : GroupLink.None;

    // Clips picked with ⌥ on a linked track (one track alone): split, trim and fades on them stay
    // on their track, and no partners join them. Per clip, so a ⌘-click on another clip still
    // brings that clip's partners along.
    private readonly HashSet<(int track, int clip)> _selAlone = new();

    private void ClearLinkState() { _selLinked.Clear(); _selAlone.Clear(); }

    /// <summary>True when the clip is selected and was picked alone with ⌥ on a linked track.</summary>
    internal bool IsPickedAlone(int trackId, int clipIndex)
        => _selAlone.Contains((trackId, clipIndex)) && _sel.Contains((trackId, clipIndex));

    // The link sets changed under a selection: drop the partners that no longer pair with a picked
    // clip (only those — other groups keep theirs), and add the new ones unless the selection was
    // picked alone.
    private void ReconcileLinkedSelection()
    {
        var picked = _sel.Where(c => !_selLinked.Contains(c)).ToList();
        var pickedSet = new HashSet<(int track, int clip)>(picked);
        var want = new HashSet<(int track, int clip)>();
        foreach (var p in picked)
            if (!_selAlone.Contains(p))
                foreach (var m in MatchesOf(p.track, p.clip))
                    if (!pickedSet.Contains(m)) want.Add(m);
        foreach (var c in _selLinked.ToList())
            if (!want.Contains(c)) { _selLinked.Remove(c); _sel.Remove(c); }
        foreach (var m in want)
            if (_sel.Add(m)) _selLinked.Add(m);
        UpdateHeaderSelection();
        Redraw();
    }

    /// <summary>True when the clip is in the selection because the link added it.</summary>
    internal bool IsLinkedPartner(int trackId, int clipIndex)
        => _selLinked.Contains((trackId, clipIndex)) && _sel.Contains((trackId, clipIndex));

    /// <summary>The clips on the other tracks of a linked group that are the same material as
    /// this one (same start and end). Empty when the track isn't linked.</summary>
    internal List<(int track, int clip)> MatchesOf(int trackId, int clipIndex)
    {
        var found = new List<(int, int)>();
        if (_engine is not { } eng) return found;
        var members = Link.Members(trackId);
        if (members.Count == 0 || !eng.TryGetClipInfo(trackId, clipIndex, out var src)) return found;
        foreach (var m in members)
            for (int i = 0; eng.TryGetClipInfo(m, i, out var ci); i++)
                if (GroupLink.Matches(src.StartBeat, src.LengthBeats, ci.StartBeat, ci.LengthBeats))
                    found.Add((m, i));
        return found;
    }

    // Adds the clip's matches to the selection as linked partners.
    private void AddMatches(int trackId, int clipIndex)
    {
        foreach (var m in MatchesOf(trackId, clipIndex))
            if (_sel.Add(m)) _selLinked.Add(m);
    }

    /// <summary>The grabbed clip's partners that are selected with it — what a trim or a fade on
    /// it passes on to.</summary>
    internal List<(int track, int clip)> SelectedPartnersOf(int trackId, int clipIndex)
        => MatchesOf(trackId, clipIndex).Where(m => _sel.Contains(m)).ToList();

    /// <summary>The given tracks widened to every track of their link sets (a time range or a
    /// split acts on all of them).</summary>
    internal int[] LinkTracks(IEnumerable<int> trackIds) => Link.ExpandTracks(trackIds).ToArray();

    /// <summary>Clips on the given tracks that strictly span <paramref name="beat"/>.</summary>
    private List<(int track, int clip)> ClipsSpanning(IEnumerable<int> trackIds, double beat)
    {
        var found = new List<(int, int)>();
        if (_engine is not { } eng) return found;
        foreach (var t in trackIds)
            for (int i = 0; eng.TryGetClipInfo(t, i, out var ci); i++)
                if (beat > ci.StartBeat + 1e-6 && beat < ci.StartBeat + ci.LengthBeats - 1e-6) found.Add((t, i));
        return found;
    }

    /// <summary>Splits the given clips at <paramref name="beat"/>, plus — on linked tracks —
    /// whatever the other tracks of the set have there, as one undo step. True when anything split.</summary>
    internal bool SplitLinkedAt(IEnumerable<(int track, int clip)> clips, double beat, bool linked = true)
    {
        if (_engine is not { } eng) return false;
        var targets = new HashSet<(int track, int clip)>(clips);
        if (linked)
        {
            // Clips picked alone with ⌥ cut only their own track.
            var linkedTracks = targets.Where(c => !_selAlone.Contains(c)).Select(c => c.track).Distinct().Where(Link.IsLinked).ToList();
            if (linkedTracks.Count > 0)
                foreach (var c in ClipsSpanning(LinkTracks(linkedTracks).Except(linkedTracks), beat)) targets.Add(c);
        }
        bool any = false;
        eng.BeginUndoGroup();
        try
        {
            // Descending clip index: SplitClip inserts a clip, so a higher index first never
            // shifts a lower one still to visit on the same track.
            foreach (var (track, clip) in targets.OrderByDescending(c => c.clip).ToList())
                if (eng.TryGetClipInfo(track, clip, out var ci)
                    && beat > ci.StartBeat + 1e-6 && beat < ci.StartBeat + ci.LengthBeats - 1e-6)
                { eng.SplitClip(track, clip, beat); any = true; }
        }
        finally { eng.EndUndoGroup(); }
        return any;
    }

    // A time range drawn over a linked group's track covers every shown row of the group: a
    // group's subtree is one run of rows, so widening to its first and last member row is enough.
    private void WidenRangeToLinkSets()
    {
        if (_timeSelRowLo < 0 || _timeSelRowHi < _timeSelRowLo || !Link.AnyLinked) return;
        var ids = new List<int>();
        for (int r = _timeSelRowLo; r <= _timeSelRowHi && r < _tracks.Count; r++) ids.Add(_tracks[r].Id);
        foreach (var id in LinkTracks(ids))
        {
            int row = RowOfTrack(id);
            if (row < 0) continue;   // a folded group's track: no row, still in TimeSelectionTrackIds
            if (row < _timeSelRowLo) _timeSelRowLo = row;
            if (row > _timeSelRowHi) _timeSelRowHi = row;
        }
    }

    // After an edit re-selects what it placed, tell the picked clips from their partners again:
    // per link set, the clips on the primary's track (or the set's first selected track) are the
    // picked ones, and a clip on another member that matches one of them is a partner.
    private void RelinkSelection()
    {
        _selLinked.Clear();
        if (!Link.AnyLinked || _engine is not { } eng) return;
        foreach (var bySet in _sel.Where(c => Link.IsLinked(c.track)).GroupBy(c => Link.RootOf(c.track)))
        {
            int pickedTrack = bySet.Any(c => c.track == SelTrackId) ? SelTrackId : bySet.First().track;
            foreach (var picked in bySet.Where(c => c.track == pickedTrack))
                foreach (var m in MatchesOf(picked.track, picked.clip))
                    if (_sel.Contains(m)) _selLinked.Add(m);
        }
    }

    /// <summary>The tooltip of every Link key (Arrangement and Mixer).</summary>
    internal static string LinkTip => MenuKit.Keys(Loc.T("Link tracks — arming and clip edits act on every track of the group · ⌥-click a clip or an arm to act on one track"));

    /// <summary>Turns a group's Link on or off (one undo step) and says so.</summary>
    internal void SetGroupLink(int groupId, bool on)
    {
        if (_engine is null) return;
        _ = Link;   // what the selection was linked by, so Refresh can reconcile it with the new sets
        if (!_engine.SetTrackEditLink(groupId, on)) return;
        Refresh();
        if (on) StatusMessage?.Invoke(Loc.T("The group's tracks are linked"));
    }

    /// <summary>The group header's Link key: a chain-link chip, accent-filled while on.</summary>
    private Control LinkToggle(TrackVM t)
    {
        var glyph = new Glyph(GlyphKind.ChainLink, 10);
        var chip = new Border
        {
            Width = 18, Height = 16, CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1),
            Child = glyph, Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(chip, LinkTip);
        PaintLinkChip(chip, glyph, t.EditLink);
        chip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            SetGroupLink(t.Id, !t.EditLink);
        };
        return chip;
    }

    internal static void PaintLinkChip(Border chip, Glyph glyph, bool on)
    {
        chip.Background = on ? NotaPalette.Accent : NotaPalette.SurfaceRaised;
        chip.BorderBrush = on ? NotaPalette.Accent : NotaPalette.Seam;
        glyph.Foreground = on ? NotaPalette.TextOnAccent : NotaPalette.TextSecondary;
    }

    /// <summary>A linked track's header mark: a small chain-link before the name (membership, so no accent).</summary>
    private Control? LinkMemberMark(TrackVM t)
    {
        if (t.IsGroup || t.IsReturn || !Link.IsLinked(t.Id)) return null;
        var mark = new Glyph(GlyphKind.ChainLink, 10)
        {
            Foreground = NotaPalette.TextSecondary, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        string group = _engine is { } eng ? eng.GetTrackName(Link.RootOf(t.Id)) : "";
        ToolTip.SetTip(mark, Loc.F("Linked with {0}", group.Length > 0 ? group : Loc.T("Group")));
        return mark;
    }

    /// <summary>Runs <paramref name="edit"/> as one undo step.</summary>
    internal void OneUndoStep(Action edit)
    {
        if (_engine is not { } eng) { edit(); return; }
        eng.BeginUndoGroup();
        try { edit(); }
        finally { eng.EndUndoGroup(); }
    }

    /// <summary>Whether the click modifiers bypass the link (⌥ / Alt: act on one track).</summary>
    internal static bool IsLinkBypass(Avalonia.Input.KeyModifiers mods) => TrackArming.IsBypass(mods);
}
