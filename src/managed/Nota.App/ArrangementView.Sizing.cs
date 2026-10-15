// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement · track-list sizing (specs/015-track-list-sizing). Track rows can be resized
// one by one, as a multi-selection or all at once, and the header column can be widened or
// narrowed. Both are view state: they never reach the engine or the undo history, and they
// ride in the bundle as a sidecar (track-view.json) so a project reopens as it was laid out.
// This file holds the size state, the snap/clamp rules, the two drag controllers (row seam,
// column seam), the live relayout that keeps a drag smooth, and the sidecar.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Nota.App;

public sealed partial class ArrangementView
{
    // Row heights: a track row is 30–256 px; drags snap lightly to the three presets.
    internal const double DefaultRowHeight = 64;
    internal const double MinRowHeight = 30;
    internal const double MaxRowHeight = 256;
    internal const double CompactRowHeight = 30;
    internal const double StandardRowHeight = 64;
    internal const double LargeRowHeight = 128;
    private const double SnapPx = 4;
    private static readonly double[] RowPresets = { CompactRowHeight, StandardRowHeight, LargeRowHeight };

    // Header column: 160–480 px, and never so wide that the lanes drop under 240 px.
    internal const double DefaultHeaderW = 228;
    internal const double MinHeaderW = 160;
    internal const double MaxHeaderW = 480;
    private const double MinLanesW = 240;

    private const double SeamGrabPx = 6;   // the band around a seam that grabs a resize

    // Track id → row height; absent = the default. Survives Refresh (TrackVMs don't).
    private readonly Dictionary<int, double> _trackHeights = new();
    // The width the user chose; the effective one (_headerW) may be clamped by a narrow window.
    private double _userHeaderW = DefaultHeaderW;
    private double _headerW = DefaultHeaderW;
    // Every fixed-width part of the header column (canvas, chrome strips, footer, markers).
    private readonly List<Control> _headerColumnParts = new();
    // Header-side row seam grips, one per resizable row, kept so a live drag can move them.
    private readonly List<(Border grip, int trackId)> _rowSeamGrips = new();
    // Layout tier each live card was built at, so a drag rebuilds a card only when it crosses one.
    private readonly Dictionary<int, HeaderTier> _cardTiers = new();
    // Heights of the tracks last copied/cut, in order — the engine's track clipboard knows no view sizes.
    private List<double?>? _clipboardHeights;
    private Border _columnSeam = null!;

    internal enum HeaderTier { Compact, Standard, Large }

    // The standard header needs ~61 px of visible content (three rows), the large one ~106,
    // so each tier starts where its layout fits — below 62 a row is compact.
    internal static HeaderTier TierFor(double height)
        => height < 62 ? HeaderTier.Compact : height < 112 ? HeaderTier.Standard : HeaderTier.Large;

    internal static double ClampRowHeight(double h) => Math.Clamp(h, MinRowHeight, MaxRowHeight);

    internal static double SnapRowHeight(double h)
    {
        foreach (var p in RowPresets)
            if (Math.Abs(h - p) <= SnapPx) return p;
        return ClampRowHeight(Math.Round(h));
    }

    private double TrackRowHeight(TrackVM t) => StoredRowHeight(t.Id);

    private double StoredRowHeight(int trackId)
        => _trackHeights.TryGetValue(trackId, out var h) ? h : DefaultRowHeight;

    private void StoreRowHeight(int trackId, double h)
    {
        h = ClampRowHeight(h);
        if (Math.Abs(h - DefaultRowHeight) < 0.5) _trackHeights.Remove(trackId);
        else _trackHeights[trackId] = h;
    }

    /// <summary>Every non-return track id in the engine, hidden (collapsed) children too, so
    /// "all tracks" still holds when a group is expanded later.</summary>
    private List<int> AllTrackRowIds()
    {
        var ids = new List<int>();
        if (_engine is null) return ids;
        for (int i = 0; i < _engine.TrackCount; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && !ti.IsReturn) ids.Add(ti.Id);
        return ids;
    }

    /// <summary>Drop heights of tracks that no longer exist (deleted, or undone away).</summary>
    private void PruneTrackHeights()
    {
        if (_engine is null || _trackHeights.Count == 0) return;
        var alive = new HashSet<int>(AllTrackRowIds());
        foreach (var id in _trackHeights.Keys.Where(id => !alive.Contains(id)).ToList())
            _trackHeights.Remove(id);
    }

    // ---- row resize (drag a row's bottom edge) -------------------------------

    private bool _rowResizing, _rowResizeChanged;
    private int[] _rowResizeTargets = Array.Empty<int>();
    private double _rowResizeStartH, _rowResizeStartY;
    private int _rowResizeRowsAbove;   // target rows above the dragged one (they grow too)
    private bool _relayoutPending;

    internal bool IsRowResizing => _rowResizing;

    /// <summary>Row <c>i</c> whose bottom edge is within the grab band of <paramref name="y"/>
    /// (slim group bars aren't resizable), or -1.</summary>
    internal int RowSeamAtY(double y)
    {
        for (int i = 0; i < _tracks.Count; i++)
        {
            double bottom = _rowTops[i + 1];
            if (Math.Abs(y - bottom) <= SeamGrabPx / 2) return IsSlimRow(_tracks[i]) ? -1 : i;
            if (bottom > y + SeamGrabPx) break;
        }
        return -1;
    }

    // Alt/Option → every track; a row inside a multi-selection → the selection; else the row.
    private int[] RowResizeTargets(int trackId, bool all)
    {
        if (all) return AllTrackRowIds().ToArray();
        var sel = SelectedTrackIds();
        return sel.Count > 1 && sel.Contains(trackId) ? sel.ToArray() : new[] { trackId };
    }

    internal void BeginRowResize(int trackId, double pointerY, KeyModifiers mods)
    {
        int row = _tracks.FindIndex(t => t.Id == trackId);
        if (row < 0) return;
        _rowResizing = true;
        _rowResizeChanged = false;
        _rowResizeTargets = RowResizeTargets(trackId, (mods & KeyModifiers.Alt) != 0);
        _rowResizeStartH = StoredRowHeight(trackId);
        _rowResizeStartY = pointerY;
        // Rows above the dragged one that resize with it push its edge down too; dividing the
        // pointer travel among them keeps the dragged edge under the pointer.
        var targets = new HashSet<int>(_rowResizeTargets);
        _rowResizeRowsAbove = 0;
        for (int i = 0; i < row; i++)
            if (targets.Contains(_tracks[i].Id) && !IsSlimRow(_tracks[i])) _rowResizeRowsAbove++;
    }

    internal void UpdateRowResize(double pointerY)
    {
        if (!_rowResizing) return;
        // Growing a row at the bottom of the view: scroll so the edge stays reachable.
        double inView = pointerY - _scroller.Offset.Y;
        if (pointerY > _rowResizeStartY && inView > _scroller.Viewport.Height - 16)
            _scroller.Offset = new Vector(_scroller.Offset.X, _scroller.Offset.Y + 6);
        double h = SnapRowHeight(_rowResizeStartH + (pointerY - _rowResizeStartY) / (_rowResizeRowsAbove + 1));
        bool changed = false;
        foreach (var id in _rowResizeTargets)
        {
            if (Math.Abs(StoredRowHeight(id) - h) < 0.5) continue;
            StoreRowHeight(id, h);
            changed = true;
        }
        if (changed) { _rowResizeChanged = true; ScheduleLiveRelayout(); }
    }

    internal void EndRowResize()
    {
        if (!_rowResizing) return;
        _rowResizing = false;
        _rowResizeTargets = Array.Empty<int>();
        // A plain click on the seam changed nothing; after a drag, one full rebuild settles the cards.
        if (_rowResizeChanged) Dispatcher.UIThread.Post(RelayoutRows, DispatcherPriority.Render);
    }

    /// <summary>Double-click on a row seam: the row (or the selection it is in) back to 64 px.</summary>
    internal void ResetRowHeight(int trackId)
    {
        _rowResizing = false;
        SetTrackHeights(RowResizeTargets(trackId, all: false), DefaultRowHeight);
    }

    internal void SetTrackHeights(IEnumerable<int> ids, double h)
    {
        foreach (var id in ids) StoreRowHeight(id, h);
        RelayoutRows();
    }

    internal void SetAllTrackHeights(double h) => SetTrackHeights(AllTrackRowIds(), h);

    // One relayout per frame however fast pointer moves arrive.
    private void ScheduleLiveRelayout()
    {
        if (_relayoutPending) return;
        _relayoutPending = true;
        Dispatcher.UIThread.Post(() => { _relayoutPending = false; RelayoutRowsLive(); }, DispatcherPriority.Render);
    }

    // Re-measure rows and move the existing header cards instead of rebuilding them all —
    // a full rebuild makes dozens of controls per track, too slow per pointer move on a big
    // song. A card is rebuilt only when its height crosses a layout tier, and only while few
    // cards cross at once: an Alt-drag over 40 tracks crossing together would stall a frame
    // (~7 ms a card), so those cards keep their old layout, clipped, until the release.
    private const int LiveTierRebuildLimit = 4;

    private void RelayoutRowsLive()
    {
        RebuildRowGeometry();
        var children = _headers.Children;
        int crossing = 0;
        for (int i = 0; i < _tracks.Count; i++)
            if (!IsSlimRow(_tracks[i]) && _cardTiers.TryGetValue(_tracks[i].Id, out var b) && b != TierFor(RowHeightAt(i))) crossing++;
        bool rebuildTiers = crossing <= LiveTierRebuildLimit;
        for (int i = 0; i < _tracks.Count; i++)
        {
            var t = _tracks[i];
            if (!_headerCards.TryGetValue(t.Id, out var hc)) continue;
            Control card = hc.card;
            double h = RowHeightAt(i);
            if (rebuildTiers && !IsSlimRow(t) && _cardTiers.TryGetValue(t.Id, out var built) && built != TierFor(h))
            {
                int at = children.IndexOf(card);
                var fresh = BuildHeaderCard(t);
                if (at >= 0) children[at] = fresh; else children.Insert(0, fresh);
                Canvas.SetLeft(fresh, 0);
                card = fresh;
            }
            Canvas.SetTop(card, RowTop(i));
            card.Height = h;
        }
        PlaceRowSeamGrips();
        _headers.Height = Math.Max(DefaultRowHeight, RowsHeight);
        _lanes.Height = _headers.Height;
        _overlay.Height = _lanes.Height;
        _lanes.InvalidateVisual();
        _overlay.InvalidateVisual();
    }

    // Header side of the row seams: a thin grip on each resizable row's bottom edge, above
    // the cards so it wins over the card's select/reorder gestures in that band only.
    private void AddRowSeamGrips()
    {
        _rowSeamGrips.Clear();
        for (int i = 0; i < _tracks.Count; i++)
        {
            var t = _tracks[i];
            if (IsSlimRow(t)) continue;
            var grip = RowSeamGrip(t.Id);
            _rowSeamGrips.Add((grip, t.Id));
            _headers.Children.Add(grip);
        }
        PlaceRowSeamGrips();
    }

    private void PlaceRowSeamGrips()
    {
        foreach (var (grip, id) in _rowSeamGrips)
        {
            int i = _tracks.FindIndex(t => t.Id == id);
            grip.IsVisible = i >= 0;
            if (i < 0) continue;
            grip.Width = _headerW;
            Canvas.SetLeft(grip, 0);
            Canvas.SetTop(grip, RowTop(i + 1) - SeamGrabPx / 2);
        }
    }

    private Border RowSeamGrip(int trackId)
    {
        var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        var grip = new Border
        {
            Height = SeamGrabPx, Background = Brushes.Transparent, Child = line,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
        };
        grip.PointerEntered += (_, _) => line.Background = Brush("Brush.BorderStrong");
        grip.PointerExited += (_, _) => { if (!_rowResizing) line.Background = Brushes.Transparent; };
        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (e.ClickCount == 2) { ResetRowHeight(trackId); return; }
            BeginRowResize(trackId, e.GetPosition(_headers).Y, e.KeyModifiers);
            e.Pointer.Capture(grip);
        };
        grip.PointerMoved += (_, e) => { if (_rowResizing) { UpdateRowResize(e.GetPosition(_headers).Y); e.Handled = true; } };
        grip.PointerReleased += (_, e) =>
        {
            if (!_rowResizing) return;
            e.Handled = true;
            e.Pointer.Capture(null);
            line.Background = Brushes.Transparent;
            EndRowResize();
        };
        grip.PointerCaptureLost += (_, _) => { line.Background = Brushes.Transparent; EndRowResize(); };
        return grip;
    }

    // ---- column resize (drag the seam between headers and lanes) -------------

    private bool _colResizing;
    private double _colResizeStartW, _colResizeStartX;

    private double EffectiveHeaderW(double wanted)
    {
        double w = Math.Clamp(wanted, MinHeaderW, MaxHeaderW);
        double room = Bounds.Width - MinLanesW;   // a narrow window squeezes the lanes first, then the headers
        if (Bounds.Width > 0 && w > room) w = Math.Max(MinHeaderW, room);
        return Math.Round(w);
    }

    /// <summary>Apply a header width the user chose. <paramref name="final"/> rebuilds the
    /// cards so their inner layouts settle; during a drag only widths move.</summary>
    internal void SetHeaderWidth(double wanted, bool final)
    {
        _userHeaderW = Math.Round(Math.Clamp(wanted, MinHeaderW, MaxHeaderW));
        ApplyHeaderWidth(final);
    }

    private void ApplyHeaderWidth(bool final)
    {
        double w = EffectiveHeaderW(_userHeaderW);
        bool moved = Math.Abs(w - _headerW) >= 0.5;
        _headerW = w;
        foreach (var part in _headerColumnParts) part.Width = w;
        foreach (var hc in _headerCards.Values) hc.card.Width = w;
        foreach (var c in _footerHeaders.Children) c.Width = w;
        PlaceRowSeamGrips();
        if (_columnSeam is not null) _columnSeam.Margin = new Thickness(w - SeamGrabPx / 2, 0, 0, 0);
        if (final)
        {
            RebuildHeaders();
            RebuildFooterHeaders();
        }
        if (moved) { _lanes.InvalidateVisual(); _overlay.InvalidateVisual(); }
    }

    // The window resized: re-clamp the effective width (the user's choice is kept).
    private void OnViewSizeChanged()
    {
        if (Math.Abs(EffectiveHeaderW(_userHeaderW) - _headerW) >= 0.5) ApplyHeaderWidth(final: true);
    }

    private Border BuildColumnSeam()
    {
        var line = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent };
        var seam = new Border
        {
            Width = SeamGrabPx, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(_headerW - SeamGrabPx / 2, 0, 0, 0),
            Background = Brushes.Transparent, Child = line,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
        };
        seam.PointerEntered += (_, _) => line.Background = Brush("Brush.BorderStrong");
        seam.PointerExited += (_, _) => { if (!_colResizing) line.Background = Brushes.Transparent; };
        seam.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(seam).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (e.ClickCount == 2) { _colResizing = false; SetHeaderWidth(DefaultHeaderW, final: true); return; }
            _colResizing = true;
            _colResizeStartW = _userHeaderW;
            _colResizeStartX = e.GetPosition(this).X;
            e.Pointer.Capture(seam);
        };
        seam.PointerMoved += (_, e) =>
        {
            if (!_colResizing) return;
            e.Handled = true;
            SetHeaderWidth(_colResizeStartW + e.GetPosition(this).X - _colResizeStartX, final: false);
        };
        void End()
        {
            if (!_colResizing) return;
            _colResizing = false;
            line.Background = Brushes.Transparent;
            ApplyHeaderWidth(final: true);
        }
        seam.PointerReleased += (_, e) => { if (_colResizing) { e.Handled = true; e.Pointer.Capture(null); End(); } };
        seam.PointerCaptureLost += (_, _) => End();
        return seam;
    }

    // ---- presets (track context menu) -----------------------------------------

    private MenuItem BuildTrackHeightSubmenu(IReadOnlyList<int> ids)
    {
        var menu = new MenuItem { Header = "Track height" };
        (string name, double h)[] presets = { ("Compact", CompactRowHeight), ("Standard", StandardRowHeight), ("Large", LargeRowHeight) };
        foreach (var (name, h) in presets)
        {
            var mi = new MenuItem
            {
                Header = name, ToggleType = MenuItemToggleType.Radio,
                IsChecked = ids.Count > 0 && ids.All(id => Math.Abs(StoredRowHeight(id) - h) < 0.5),
            };
            mi.Click += (_, _) => SetTrackHeights(ids, h);
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        foreach (var (name, h) in presets)
        {
            var mi = new MenuItem { Header = $"All tracks: {name.ToLowerInvariant()}" };
            mi.Click += (_, _) => SetAllTrackHeights(h);
            menu.Items.Add(mi);
        }
        return menu;
    }

    // ---- duplicate / copy / paste carry the height ----------------------------

    private bool CopyTracksToClipboard(IReadOnlyList<int> ids)
    {
        if (_engine is null || ids.Count == 0 || !_engine.CopyTracks(ids.ToArray())) return false;
        _clipboardHeights = ids.Select(id => _trackHeights.TryGetValue(id, out var h) ? h : (double?)null).ToList();
        return true;
    }

    // Engine copies come back in the order asked; when a group brought its children along the
    // counts differ and the pairing is unknown, so those copies keep the default height.
    private void CarryHeights(IReadOnlyList<double?> heights, IReadOnlyList<int> newIds)
    {
        if (heights.Count != newIds.Count) return;
        for (int i = 0; i < newIds.Count; i++)
            if (heights[i] is { } h) StoreRowHeight(newIds[i], h);
    }

    private void CarryHeightsFrom(IReadOnlyList<int> sourceIds, IReadOnlyList<int> newIds)
        => CarryHeights(sourceIds.Select(id => _trackHeights.TryGetValue(id, out var h) ? h : (double?)null).ToList(), newIds);

    // ---- sidecar persistence --------------------------------------------------

    private const string TrackViewSidecar = "track-view.json";

    private sealed class TrackViewDto
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("headerWidth")] public double? HeaderWidth { get; set; }
        [JsonPropertyName("trackHeights")] public List<TrackHeightDto> TrackHeights { get; set; } = new();
    }

    private sealed class TrackHeightDto
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("height")] public double Height { get; set; }
    }

    /// <summary>Write the track heights and header width into a .nota bundle, keyed by track
    /// index (ids only last a session). Cosmetic — a failure here never fails the save.</summary>
    public void SaveTrackView(string bundleDir)
    {
        var path = System.IO.Path.Combine(bundleDir, TrackViewSidecar);
        try
        {
            var dto = new TrackViewDto();
            if (Math.Abs(_userHeaderW - DefaultHeaderW) >= 0.5) dto.HeaderWidth = _userHeaderW;
            if (_engine is not null)
                for (int i = 0; i < _engine.TrackCount; i++)
                    if (_engine.TryGetTrackInfo(i, out var ti) && _trackHeights.TryGetValue(ti.Id, out var h))
                        dto.TrackHeights.Add(new TrackHeightDto { Index = i, Height = h });
            if (dto.HeaderWidth is null && dto.TrackHeights.Count == 0)
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                return;
            }
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* layout is cosmetic — never block a save */ }
    }

    /// <summary>Read the layout back out of a bundle (absent / corrupt / newer → defaults).
    /// Call after the engine holds the loaded project, before the arrangement refreshes.</summary>
    public void LoadTrackView(string bundleDir)
    {
        ResetTrackView();
        var path = System.IO.Path.Combine(bundleDir, TrackViewSidecar);
        if (!System.IO.File.Exists(path) || _engine is null) return;
        try
        {
            var dto = JsonSerializer.Deserialize<TrackViewDto>(System.IO.File.ReadAllText(path));
            if (dto is null || dto.Version > 1) return;
            if (dto.HeaderWidth is { } w) _userHeaderW = Math.Round(Math.Clamp(w, MinHeaderW, MaxHeaderW));
            foreach (var th in dto.TrackHeights)
                if (th.Index >= 0 && _engine.TryGetTrackInfo(th.Index, out var ti) && !ti.IsReturn)
                    StoreRowHeight(ti.Id, th.Height);
        }
        catch { /* a corrupt sidecar just means the default layout */ }
        finally { ApplyHeaderWidth(final: false); }
    }

    /// <summary>Back to the default layout (New project, or before a load).</summary>
    public void ResetTrackView()
    {
        _trackHeights.Clear();
        _clipboardHeights = null;
        _userHeaderW = DefaultHeaderW;
        ApplyHeaderWidth(final: false);
    }
}
