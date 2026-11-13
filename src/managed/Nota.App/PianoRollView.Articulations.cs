// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Per-note articulations in the piano roll (specs/085, US2): right-click a note → Articulation →
// one of the track's map. The choice goes on every selected note of that track (a chord, a
// phrase, overlapping notes — each keeps its own id) as one undo step. Notes of reference clips
// and of other tracks in a multi-track editor are left alone. Clear sets the note back to none,
// which plays as the map's default.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Nota.Application.Articulations;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PianoRollView
{
    /// <summary>The articulation map of a track, or null when it has none. Set by the host.</summary>
    public Func<int, ArticulationMap?>? ArticulationMapOf { get; set; }

    // ---- note colour by articulation (US4, FR-015) ------------------------------------------

    /// <summary>Notes take their articulation's colour instead of the track's. App-wide, like
    /// <see cref="AuditionEnabled"/>: seeded from and saved to Settings by the host; off by default.</summary>
    public static bool NoteColorByArticulation { get; set; }
    /// <summary>Fired when a clip editor's colour chip flips <see cref="NoteColorByArticulation"/>.</summary>
    public static event Action? NoteColorModeChanged;
    public static void SetNoteColorByArticulation(bool on)
    {
        if (NoteColorByArticulation == on) return;
        NoteColorByArticulation = on;
        NoteColorModeChanged?.Invoke();
    }

    // Follow the switch while on screen only: a roll is rebuilt for every clip, so a lasting
    // subscription would leak (as the keys column does with the label mode).
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        NoteColorModeChanged += Invalidate;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        NoteColorModeChanged -= Invalidate;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Note i's articulation colour (its own, or the default it plays as), or null for the
    /// track colour — no map, or an id the map doesn't hold (enabled) any more.</summary>
    private Avalonia.Media.Color? ArticulationColourOf(int i)
    {
        if (i < 0 || i >= _notes.Count) return null;
        var shown = ArticulationColours.Shown(CachedArticulationMap(TrackOfSlot(_owner[i])), _notes[i].ArticulationId);
        return shown is null ? null : ArticulationImportPreview.ColourOf(shown);
    }

    // Drawing reads the map on every frame; reading and parsing it each time would cost a frame's
    // worth of work. A track's map is fetched at most twice a second, and again at once after this
    // roll changes it. An undo or another view changing it shows within that half second.
    private readonly Dictionary<int, (long At, ArticulationMap? Map)> _artMaps = new();
    private const long ArtMapRefreshMs = 500;

    internal ArticulationMap? CachedArticulationMap(int track)
    {
        if (ArticulationMapOf is null || track <= 0) return null;
        long now = Environment.TickCount64;
        if (_artMaps.TryGetValue(track, out var c) && now - c.At < ArtMapRefreshMs) return c.Map;
        var map = ArticulationMapOf(track);
        _artMaps[track] = (now, map);
        return map;
    }

    /// <summary>FR-014: a note whose articulation the track's map doesn't hold (deleted, or
    /// disabled). It plays as the map's default; the roll marks it.</summary>
    internal bool HasMissingArticulation(int i)
    {
        if (i < 0 || i >= _notes.Count || _notes[i].ArticulationId <= 0) return false;
        var map = CachedArticulationMap(TrackOfSlot(_owner[i]));
        return map is not null && map.Find(_notes[i].ArticulationId) is not { Enabled: true };
    }

    /// <summary>The notes a menu opened on <paramref name="hit"/> would change: the selection's
    /// notes on the same track as the hit, in editable clips.</summary>
    internal List<int> ArticulationTargets(int hit)
    {
        int track = TrackOfSlot(_owner[hit]);
        var picked = _selection.Contains(hit) ? _selection.ToList() : new List<int> { hit };
        return picked.Where(i => i >= 0 && i < _notes.Count && !IsLocked(i) && TrackOfSlot(_owner[i]) == track).OrderBy(i => i).ToList();
    }

    /// <summary>Sets the articulation of <paramref name="targets"/> to <paramref name="id"/> (0 = none)
    /// as one undo step. False when nothing changed.</summary>
    internal bool SetArticulation(IReadOnlyList<int> targets, int id)
    {
        bool changed = false;
        foreach (int i in targets)
        {
            if (_notes[i].ArticulationId == id) continue;
            var n = _notes[i];   // copy the note: its expression and everything else travel with it
            n.ArticulationId = Math.Max(0, id);
            _notes[i] = n;
            changed = true;
        }
        if (!changed) return false;
        Invalidate();
        CommitNotes();
        Changed?.Invoke();
        return true;
    }

    internal void ShowArticulationMenu(Control anchor, int hit)
    {
        if (hit < 0 || hit >= _notes.Count) return;
        var targets = ArticulationTargets(hit);
        _artMaps.Remove(TrackOfSlot(_owner[hit]));   // the menu shows the map as it is now
        var map = CachedArticulationMap(TrackOfSlot(_owner[hit]));
        var flyout = new MenuFlyout();
        if (map is null || targets.Count == 0)
        {
            flyout.Items.Add(MenuKit.Item(map is null ? Loc.T("articulations.menu.no_map") : Loc.T("articulations.menu.locked"), enabled: false));
            flyout.ShowAt(anchor, showAtPointer: true);
            return;
        }

        var ids = targets.Select(i => _notes[i].ArticulationId).Distinct().ToList();
        string current = ids.Count > 1 ? Loc.T("articulations.menu.mixed") : Label(map, ids[0]);
        var sub = MenuKit.ValueItem(Loc.T("articulations.menu.articulation"), null, current);
        foreach (var a in map.Articulations.Where(a => a.Enabled))
        {
            int id = a.Id;
            var mi = MenuKit.Item(a.Name, click: () => SetArticulation(targets, id));
            mi.ToggleType = MenuItemToggleType.Radio;
            mi.IsChecked = ids.Count == 1 && ids[0] == id;
            MenuKit.SetTrailing(mi, MenuKit.Swatch(ArticulationImportPreview.Swatch(a)));
            sub.Items.Add(mi);
        }
        sub.Items.Add(new Separator());
        // Notes whose id is gone from the map (or disabled) play the default: say so (FR-014).
        bool missing = ids.Any(i => i > 0 && map.Find(i) is not { Enabled: true });
        sub.Items.Add(MenuKit.Item(missing ? Loc.T("articulations.menu.clear_missing") : Loc.T("articulations.menu.clear"),
            click: () => SetArticulation(targets, 0), enabled: ids.Any(i => i != 0)));
        flyout.Items.Add(sub);
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    /// <summary>How a note's articulation reads: its name; none → the default's ("Default · Long");
    /// an id the map doesn't hold (any more) → "Missing".</summary>
    internal static string Label(ArticulationMap map, int id)
    {
        if (id > 0) return map.Find(id) is { Enabled: true } a ? a.Name : Loc.T("articulations.menu.missing");
        return map.DefaultArticulationId is int d && map.Find(d) is { Enabled: true } def
            ? Loc.F("articulations.menu.default_named", def.Name)
            : Loc.T("articulations.menu.default");
    }
}
