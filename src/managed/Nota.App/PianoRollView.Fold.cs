// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Fold (specs/045, #344 #345): one switch that folds the keyboard to the rows that matter —
// the scale's pitches on a melodic track, the pads on a Drum Rack — keeping every pitch a
// note sits on. The rows are a PianoRowMap; this part builds it and keeps it current. The
// map is never rebuilt mid-gesture, so a row under the pointer can't vanish during a drag.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Nota.Application.Midi;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class PianoRollView
{
    private const double FoldedPadKeysW = 96;   // room for "Hat closed" beside the rows
    private const double MaxPadRowH = 24;       // a fitted kit's rows stop growing here

    private PianoRowMap _rows = PianoRowMap.Chromatic(MinPitch, MaxPitch);
    private bool _rowsArePads;                   // the rows are a Drum Rack's pads
    private bool _fold;
    private double? _rowHBeforePadFit;          // the row height to go back to when unfolding
    private double? _padFitH;                   // the row height a kit was fitted to (null once zoomed)
    private readonly List<ColumnDefinition> _keyColumns = new();

    /// <summary>The host's pad scan for the clip's track: note → pad name on a Drum Rack
    /// track, null on any other track. Asked whenever the rows are rebuilt.</summary>
    public Func<IReadOnlyDictionary<int, string>?>? FoldPads { get; set; }

    /// <summary>Fires when the user toggles Fold, so the host stores it on the track.</summary>
    public event Action<bool>? FoldToggled;

    public bool Folded => _fold;
    /// <summary>The rows the keyboard shows now (for the toolbar's tooltip and the smoke test).</summary>
    internal PianoRowMap Rows => _rows;
    /// <summary>Whether a folded keyboard shows a Drum Rack's pads (else scale rows).</summary>
    public bool FoldShowsPads => FoldPads?.Invoke() is not null;
    /// <summary>Folded but with nothing to fold to: the keyboard stays whole, with a hint.</summary>
    public bool FoldHasNothing => _fold && _rows.IsChromatic;

    /// <summary>Sets Fold; <paramref name="user"/> raises <see cref="FoldToggled"/>.</summary>
    public void SetFold(bool on, bool user = false)
    {
        // Not mid-drag: the grabbed notes' rows would change under the pointer (as row zoom).
        if (on == _fold || _gridControl.Dragging) return;
        _fold = on;
        RebuildRows(force: true);
        // A kit is a handful of rows: when they would leave the editor mostly empty, fit them,
        // and give the keyboard its own row height back on unfolding.
        if (_rowsArePads && _bodyScroll.Viewport.Height <= 0) FitPadsWhenLaidOut();   // a reopened editor
        else if (_rowsArePads && RangeH < _bodyScroll.Viewport.Height * 0.75)
        {
            _rowHBeforePadFit = _rowH;
            FitPads();
        }
        else if (!on && _rowHBeforePadFit is { } h0)
        {
            _rowHBeforePadFit = null; _padFitH = null;
            SetRowHeightAt(h0, 0);
        }
        if (user) FoldToggled?.Invoke(on);
    }

    // Fold restored before the editor has a height (a reopened project or clip): fit the kit
    // on the first layout that gives it one.
    private void FitPadsWhenLaidOut()
    {
        void Once(object? sender, EventArgs e)
        {
            double vh = _bodyScroll.Viewport.Height;
            if (vh <= 0) return;
            _bodyScroll.LayoutUpdated -= Once;
            if (_fold && _rowsArePads && _rowHBeforePadFit is null && RangeH < vh * 0.75)
            {
                _rowHBeforePadFit = _rowH;
                FitPads();
            }
        }
        _bodyScroll.LayoutUpdated += Once;
    }

    private void FitPads() { FitToNotes(); _padFitH = _rowH; }

    // A fitted kit follows the editor's height (docked panel → pop-out) until the user zooms rows.
    private void RefitPadsOnResize()
    {
        if (!_fold || !_rowsArePads || _padFitH is not { } fitted || _gridControl.Dragging) return;
        if (Math.Abs(_rowH - fitted) > 0.01) { _padFitH = null; return; }   // zoomed by hand
        FitPads();
    }

    /// <summary>The rack's pads may have changed (renamed, added, a new sample): re-read them.</summary>
    public void RefreshFold() { if (_fold) Invalidate(); }

    internal bool HasRow(int pitch) => _rows.RowOf(pitch) >= 0;
    internal double PitchY(int pitch) => _rows.NearestRow(pitch) * RowH;
    internal int PitchAtY(double y) => _rows.PitchAt((int)Math.Floor(y / RowH));

    /// <summary>Rebuilds the rows from the scale, the pads and the notes, keeping the row at
    /// the top of the view on screen. Skipped mid-gesture unless forced.</summary>
    private void RebuildRows(bool force = false)
    {
        if (!force && _gridControl.Dragging) return;
        PianoRowMap next;
        var pads = _fold ? FoldPads?.Invoke() : null;
        if (!_fold) next = PianoRowMap.Chromatic(MinPitch, MaxPitch);
        else next = PianoRowMap.Folded(MinPitch, MaxPitch, pads,
            pads is null && _scaleOn ? ScaleMasks[_scaleIndex] : null, _scaleRoot, UsedPitches());
        bool padRows = pads is not null && !next.IsChromatic;
        if (!force && SameRows(next) && padRows == _rowsArePads) { _rows = next; return; }   // labels may change

        // Keep the pitch at the top of the view where it is.
        double rowH = RowH, top = _bodyScroll.Offset.Y;
        int anchor = _rows.PitchAt((int)Math.Floor(top / rowH));
        _rows = next;
        _rowsArePads = padRows;
        double keysW = padRows ? FoldedPadKeysW : KeysW;
        foreach (var c in _keyColumns) c.Width = new GridLength(keysW);
        _overlayHost.Margin = new Avalonia.Thickness(keysW, 0, 0, 0);
        _keys.Height = RangeH;
        _gridControl.Height = RangeH;
        _pendingCenter = false;
        _pendingVOffset = Math.Max(0, _rows.NearestRow(anchor) * rowH);
        ApplyPendingVOffset();
    }

    // The pitches a folded keyboard keeps: every note's, and the keys held into a recording
    // take, which aren't notes until they are released.
    private List<int> UsedPitches()
    {
        var ps = _notes.Select(n => n.Pitch).ToList();
        if (_recPreview is { } rp) foreach (var h in rp.Held) ps.Add(h.Pitch);
        return ps;
    }

    private bool SameRows(PianoRowMap m)
    {
        if (m.Count != _rows.Count) return false;
        for (int r = 0; r < m.Count; r++) if (m.PitchAt(r) != _rows.PitchAt(r)) return false;
        return true;
    }
}
