// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

/// <summary>Audio-clip → MIDI conversion modes (arrangement clip context menu).</summary>
public enum ClipConvertMode { Drums, Slice, Melody, Harmony }

/// <summary>Which kind of track an arrangement context menu asked for.</summary>
public enum NewTrackKind { Instrument, Audio, Return }

/// <summary>How much of the arrangement names its clips (View ▸ Clip names).</summary>
public enum ClipLabelMode { Every, RunStart, None }

public sealed partial class ArrangementView : UserControl
{
    // --- coordinate model / shared state ---
    private double _pixelsPerBeat = 28;
    private double _scrollBeats;
    private double _playheadBeats;
    private double _totalBeats = 64;
    private int _beatsPerBar = 4;
    private double _snapBeats = 1.0;               // snap grid (1 beat) — M4-2
    private bool _snapEnabled = true;              // transport magnet toggle (off = free positioning)
    internal int SelTrackId = -1, SelClipIndex = -1;   // "primary" selection (Clip tab, effect target, trim)
    // Multi-selection of clips across tracks (marquee / group move + delete). The
    // primary (SelTrackId/SelClipIndex) is always one of these when non-empty.
    private readonly HashSet<(int track, int clip)> _sel = new();
    // General time-range selection (arrangement, not automation): a beat range over a span
    // of visual rows. Mutually exclusive with the clip selection (req 1.2.5). Operations act
    // on the covered clip *parts*, splitting at the range edges.
    internal double _timeSelStart, _timeSelEnd;
    internal int _timeSelRowLo = -1, _timeSelRowHi = -1;
    internal bool HasTimeSelection => _timeSelRowLo >= 0 && _timeSelEnd - _timeSelStart > 1e-6;
    private bool _automationMode;                     // toolbar Automation toggle (M9-A3)
    private double _autoBaselineSig = double.NaN;      // signature of visible empty-lane baselines (redraw on change)
    // Loop region highlight: cached from the engine (message-thread mirror) and
    // drawn as a brace in the ruler + a wash across the lanes. During a ruler
    // drag we show a live preview before committing to the engine on release.
    private bool _loopActive;
    private double _loopS, _loopE;
    private bool _loopDragging;
    /// <summary>Raised after the arrangement authors a loop region (ruler drag /
    /// "Loop selection"), so the transport bar can reflect it.</summary>
    public event Action? LoopChanged;
    private readonly List<TrackVM> _tracks = new();   // audio + instrument (scrolling)
    private readonly List<TrackVM> _returns = new();  // return buses (pinned footer)
    private readonly Dictionary<int, MeterBar> _meters = new(); // per-track header meters (M6-2)
    // Per-track header vol/pan controls, refreshed each tick so automation moves them live.
    private readonly Dictionary<int, MiniFader> _volFaders = new();
    private readonly Dictionary<int, PanBar> _panBars = new();
    private readonly Dictionary<int, TextBlock> _volDb = new();
    // Per-track header card + its name label, so a selection change repaints just those two
    // properties instead of tearing down and rebuilding every header control (click latency).
    private readonly Dictionary<int, (Border card, TextBlock name, bool isGroup, Border spine, IBrush spineIdle)> _headerCards = new();
    // Per-track chosen automation target, kept across Refresh (which rebuilds VMs).
    private readonly Dictionary<int, (AutomationTarget target, int dev, int param, string paramId)> _autoTargets = new();

    private const double HeaderW = 228;
    private const double RowHeight = 64;         // a track row: header carries every control
    private const double GroupRowHeight = 26;    // a group row: a slim titled bar, no controls
    private const double RulerH = 24;
    private const double SectionsH = 22;    // song-structure lane above the ruler
    private const double FooterRowH = 40;   // slim return/master rows

    // Row geometry. Rows are no longer a fixed pitch — a group weighs a fraction of a track
    // (design 1a), so every y↔row conversion goes through the prefix table below instead of
    // multiplying/dividing by one constant. Rebuilt on every Refresh, after ApplyHierarchy.
    private double[] _rowTops = { 0 };

    private void RebuildRowGeometry()
    {
        var tops = new double[_tracks.Count + 1];
        double y = 0;
        for (int i = 0; i < _tracks.Count; i++)
        {
            tops[i] = y;
            y += IsSlimRow(_tracks[i]) ? GroupRowHeight : RowHeight;
        }
        tops[_tracks.Count] = y;
        _rowTops = tops;
    }

    /// <summary>Height of arrangement row <paramref name="i"/> — a slim bar for a group.</summary>
    internal double RowHeightAt(int i)
        => i >= 0 && i < _tracks.Count && IsSlimRow(_tracks[i]) ? GroupRowHeight : RowHeight;

    /// <summary>A group is a slim titled bar (1a: "groups weigh the same as their children"
    /// was the problem) — until it is the selected track, where it opens to a full row so its
    /// own mute/solo, fader, pan and meter stay reachable.</summary>
    private bool IsSlimRow(TrackVM t) => t.IsGroup && t.Id != SelTrackId;
    internal bool IsSlimRowAt(int i) => i >= 0 && i < _tracks.Count && IsSlimRow(_tracks[i]);

    /// <summary>Top edge of row <paramref name="i"/> in lane coordinates. Indices past the
    /// last row extrapolate by a full track row, so "the gap after the end" still has a y.</summary>
    internal double RowTop(int i)
    {
        if (i <= 0) return 0;
        int n = _tracks.Count;
        return i <= n ? _rowTops[i] : _rowTops[n] + (i - n) * RowHeight;
    }

    /// <summary>Combined height of every visible row.</summary>
    internal double RowsHeight => _rowTops[_tracks.Count];

    /// <summary>Row containing <paramref name="y"/>, or -1 when it falls outside the rows.</summary>
    internal int RowAtY(double y)
    {
        if (y < 0 || _tracks.Count == 0 || y >= _rowTops[_tracks.Count]) return -1;
        for (int i = 0; i < _tracks.Count; i++)
            if (y < _rowTops[i + 1]) return i;
        return -1;
    }

    /// <summary>Like <see cref="RowAtY"/> but clamped to the first/last row (-1 only when
    /// there are no rows at all) — for gestures that must always land somewhere.</summary>
    internal int RowAtYClamped(double y)
    {
        if (_tracks.Count == 0) return -1;
        int r = RowAtY(y);
        return r >= 0 ? r : (y < 0 ? 0 : _tracks.Count - 1);
    }

    /// <summary>Insertion gap nearest <paramref name="y"/> (0.._tracks.Count) — where a
    /// header-reorder drop would land.</summary>
    internal int RowGapAtY(double y)
    {
        int n = _tracks.Count;
        if (n == 0) return 0;
        for (int i = 0; i < n; i++)
        {
            double top = _rowTops[i], bot = _rowTops[i + 1];
            if (y < (top + bot) / 2) return i;
            if (y < bot) return i + 1;
        }
        return n;
    }

    private IAudioEngine? _engine;

    private readonly RulerControl _ruler;
    private readonly LaneControl _lanes;
    // Playhead + loop band live on a thin, hit-transparent overlay ABOVE the lanes so the
    // 30 Hz transport tick repaints only these few strokes instead of the whole clip/waveform
    // surface underneath (which is static during playback). See SetPlayhead / Redraw.
    private readonly LaneOverlayControl _overlay;
    private readonly Canvas _headers;
    private readonly ScrollBar _hScroll;
    // The vertical scroller wrapping [headers | lanes]; its offset/viewport drive row culling
    // in LaneControl.Render so off-screen tracks skip their clip/waveform work.
    private ScrollViewer _scroller = null!;

    // Track reorder: grab a header's title strip to drag it to a new slot. Manual
    // capture-drag (headers live in a Canvas); an accent insertion line marks the drop gap.
    private int _hdrDragId = -1, _hdrDragFrom = -1, _hdrDropGap = -1;
    private bool _hdrDragging;
    private Point _hdrDragStart;
    private readonly Border _trackDropLine = new()
    {
        IsVisible = false, IsHitTestVisible = false, Height = 2, Width = HeaderW,
        Background = NotaPalette.AccentBright,
    };

    // Return/Master section pinned under the scrolling tracks (no scroll).
    private readonly Canvas _footerHeaders;
    private readonly FooterLaneControl _footerLanes;
    private readonly Grid _footer;

    /// <summary>Raised when a MIDI clip is double-clicked (trackId, clipIndex).</summary>
    public event Action<int, int>? MidiClipActivated;
    /// <summary>Raised when an audio clip is double-clicked (trackId, clipIndex).</summary>
    public event Action<int, int>? AudioClipActivated;
    /// <summary>Raised when a track becomes selected (clip or header click).</summary>
    public event Action<int>? TrackSelected;
    /// <summary>Live-freeze (v1.1): per-track role for the header badge — 0 none, 1 sleeping
    /// source, 2 linked frozen. Set by MainWindow; queried while rebuilding headers.</summary>
    public Func<int, int>? FreezeRole;
    /// <summary>Freeze / Live Freeze entries for a track's context menu (empty when the track
    /// can't be frozen). Built by MainWindow, which owns the freeze state and commands.</summary>
    public Func<int, IReadOnlyList<Control>>? FreezeMenuItems;
    /// <summary>Called at the start of a full <see cref="Refresh"/>, before the track model is
    /// rebuilt — MainWindow drops live-freeze links whose tracks were deleted or undone.</summary>
    public Action? RefreshStarting;
    /// <summary>A transient status line the arrangement wants shown (e.g. an automation-follow hint).</summary>
    public event Action<string>? StatusMessage;
    /// <summary>Raised after a clip is copied into a session slot (M5-6).</summary>
    public event Action? SessionChanged;
    /// <summary>Raised when a browser item is dropped on a lane (M7-5): item, target
    /// track id (-1 if past the last track), and the snapped drop beat.</summary>
    public event Action<BrowserItem, int, double>? ItemDropped;
    /// <summary>Audio-clip context-menu "Convert / Slice to New MIDI Track" (track id, clip index, mode).</summary>
    public event Action<int, int, ClipConvertMode>? ConvertClipRequested;
    /// <summary>A context menu asked for a new track. MainWindow owns creation — the seed
    /// MIDI clip, the session / modular / device-chain refreshes and the status line — so the
    /// menu only asks, and the toolbar's + buttons and these entries stay one behaviour.</summary>
    public event Action<NewTrackKind>? AddTrackRequested;
    /// <summary>Raised after a clip's start/length changed in place (edge-drag trim or
    /// stretch): track id, clip index. An open clip editor re-reads its geometry from this.</summary>
    public event Action<int, int>? ClipGeometryChanged;

    internal void RaiseClipGeometryChanged(int trackId, int clipIndex)
        => ClipGeometryChanged?.Invoke(trackId, clipIndex);

    public ArrangementView()
    {
        _ruler = new RulerControl(this) { Height = RulerH };
        _overview = new OverviewControl(this);
        _sectionsLane = new SectionsControl(this);
        _lanes = new LaneControl(this) { VerticalAlignment = VerticalAlignment.Top };
        // Sits in the same grid cell as _lanes, on top; hit-transparent so all pointer
        // gestures pass through to the lanes beneath.
        _overlay = new LaneOverlayControl(this)
        {
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        // Drop target is the whole scroller (below), not _lanes — _lanes is only as
        // tall as the tracks, so instruments dropped in the empty space beneath them
        // (the natural "make a new track" gesture) would otherwise miss.
        _headers = new Canvas { Width = HeaderW, VerticalAlignment = VerticalAlignment.Top };
        _hScroll = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Minimum = 0,
            AllowAutoHide = false,
        };
        _hScroll.Scroll += (_, _) => { _scrollBeats = _hScroll.Value; Redraw(); };
        // Keep the scroll range in sync with the visible width — outside the
        // render pass (mutating controls during Render() is illegal / crashes).
        _lanes.SizeChanged += (_, _) => SyncScroll(_lanes.Bounds.Width);

        // Top: zoom controls over the header spacer, ruler over the lanes.
        var zoomOut = ZoomChip("−");
        var zoomIn = ZoomChip("+");
        zoomOut.Click += (_, _) => Zoom(1 / 1.25);
        zoomIn.Click += (_, _) => Zoom(1.25);
        var zoomBar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, Width = HeaderW,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Children =
            {
                new TextBlock { Text = "ZOOM", Classes = { "SectionLabel" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) },
                zoomOut, zoomIn,
            },
        };
        var topLeft = HeaderPanel(zoomBar);
        var top = new Grid { Height = RulerH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        top.Children.Add(topLeft);
        Grid.SetColumn(_ruler, 1);
        top.Children.Add(_ruler);

        // Above the ruler: the Overview strip (whole project + a draggable viewport window),
        // then the Sections lane — song structure sits directly over the bar numbers it names.
        _overviewRow = BuildOverviewRow();
        _sectionsRow = BuildSectionsRow();

        // Center: vertical scroll over [headers | lanes].
        // Top-anchored: the ScrollViewer would otherwise stretch the short
        // content to the viewport and centre the fixed-height lanes.
        var center = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        center.Children.Add(_headers);
        Grid.SetColumn(_lanes, 1);
        center.Children.Add(_lanes);
        Grid.SetColumn(_overlay, 1);
        center.Children.Add(_overlay);   // above _lanes (added later = on top)
        var scroller = _scroller = new ScrollViewer
        {
            Content = center,
            Background = Brushes.Transparent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        // Repaint the lanes/overlay when the vertical scroll changes so culling re-evaluates
        // which rows are on-screen (the ScrollViewer moves the retained content under a fixed
        // viewport; without a repaint the culled off-screen rows would scroll in blank).
        scroller.ScrollChanged += (_, _) =>
        {
            _lanes.InvalidateVisual();
            _overlay.InvalidateVisual();
        };
        // Browser drag & drop covers the whole scroll viewport (incl. empty space
        // below the tracks) so instruments/samples can be dropped anywhere.
        DragDrop.SetAllowDrop(scroller, true);
        DragDrop.AddDragOverHandler(scroller, OnLaneDragOver);
        DragDrop.AddDragLeaveHandler(scroller, (_, _) => { DropTrackIndex = -1; _lanes.InvalidateVisual(); });
        DragDrop.AddDropHandler(scroller, OnLaneDrop);
        // Right-click the empty area below the tracks → paste a copied track there (the
        // header cards / lanes are top-anchored, so clicks below them land on the scroller).
        scroller.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(scroller).Properties.IsRightButtonPressed) return;
            double y = e.GetPosition(_headers).Y;                     // shared vertical (scrolls with content)
            if (y >= 0 && y < RowsHeight) return;                     // on a track → it shows its own menu
            ShowEmptyAreaMenu(scroller);
        };
        // Wheel over the empty area below the tracks (top-anchored lanes leave a gap when few
        // tracks) still zooms/scrolls the timeline. Events over the lanes are handled there
        // first (Handled), so this only picks up the gap.
        scroller.PointerWheelChanged += (_, e) => { if (!e.Handled) HandleLaneWheel(e, e.GetPosition(_lanes).X); };
        // Full-height header-column backdrop behind the scroller so the header
        // column reads as a distinct panel even with no tracks.
        var leftBackdrop = new Border
        {
            Width = HeaderW,
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        leftBackdrop.BindResource(Border.BackgroundProperty, "Brush.SurfaceCard");
        leftBackdrop.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        var centerArea = new Grid();
        centerArea.Children.Add(leftBackdrop);
        centerArea.Children.Add(scroller);

        // Return / Master: a slim section pinned under the tracks (no scroll).
        _footerHeaders = new Canvas { Width = HeaderW };
        var footerHeaderPanel = new Border { Width = HeaderW, BorderThickness = new Thickness(0, 0, 1, 0), Child = _footerHeaders };
        footerHeaderPanel.BindResource(Border.BackgroundProperty, "Brush.SurfaceCard");
        footerHeaderPanel.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        _footerLanes = new FooterLaneControl(this);
        _footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(footerHeaderPanel, 0);
        Grid.SetColumn(_footerLanes, 1);
        _footer.Children.Add(footerHeaderPanel);
        _footer.Children.Add(_footerLanes);

        // Bottom: horizontal scrollbar under the lanes.
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        bottom.Children.Add(HeaderPanel(null));
        Grid.SetColumn(_hScroll, 1);
        bottom.Children.Add(_hScroll);

        var root = new DockPanel();
        DockPanel.SetDock(_overviewRow, Dock.Top);
        DockPanel.SetDock(_sectionsRow, Dock.Top);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(_footer, Dock.Bottom);
        root.Children.Add(_overviewRow);   // Overview sits above the ruler
        root.Children.Add(_sectionsRow);   // …then the named song sections
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(_footer);
        root.Children.Add(centerArea);

        // The island: like the browser, the arrangement is a rounded card floating on the app
        // ground rather than a full-bleed region. ClipToBounds keeps the ruler, the header
        // column and the pinned footer inside the rounded corners.
        var island = new Border
        {
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = root,
        };
        island.BindResource(Border.CornerRadiusProperty, "Radius.Panel");
        island.BindResource(Border.BackgroundProperty, "Brush.SurfaceCard");
        island.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        Content = island;
    }

    public IAudioEngine? Engine
    {
        get => _engine;
        set
        {
            if (_engine is not null) _engine.AutomationTouched -= OnAutomationTouched;
            _engine = value;
            if (_engine is not null) _engine.AutomationTouched += OnAutomationTouched;
            Refresh();
        }
    }

    public double PixelsPerBeat => _pixelsPerBeat;
    public double ScrollBeats => _scrollBeats;
    public double PlayheadBeats => _playheadBeats;
    public int BeatsPerBar { get => _beatsPerBar; set { _beatsPerBar = value; Redraw(); } }
    /// <summary>Clip-drag snap grid in beats (transport GRID cell, 1b).</summary>
    public double SnapBeats { get => _snapBeats; set { _snapBeats = Math.Max(1.0 / 32, value); Redraw(); } }

    /// <summary>Master snap on/off (the transport magnet). Off positions freely — the
    /// same effect as holding Alt, but latched. The grid itself keeps its spacing, so
    /// switching back resumes on the denomination the GRID cell shows.</summary>
    public bool SnapEnabled { get => _snapEnabled; set { _snapEnabled = value; Redraw(); } }

    /// <summary>How many clips print their name on the lane (View ▸ Clip names). The default
    /// names only the head of each run, so a repeating pattern reads as one block.</summary>
    public ClipLabelMode ClipLabels
    {
        get => _clipLabels;
        set { if (_clipLabels == value) return; _clipLabels = value; _lanes.InvalidateVisual(); }
    }
    private ClipLabelMode _clipLabels = ClipLabelMode.RunStart;
    /// <summary>Show/edit parameter-automation envelopes over the lanes (M9-A3).</summary>
    public bool AutomationMode
    {
        get => _automationMode;
        set { if (_automationMode == value) return; _automationMode = value; Refresh(); }
    }
    /// <summary>While recording, reload the visible envelopes so writes appear live (M9-C).</summary>
    public void RefreshAutomationLive()
    {
        // Only reload while actually recording: automation record on + playing + no
        // hand-drag in progress. Otherwise this would clobber manual envelope editing.
        if (!_automationMode) return;
        var eng = _engine;
        if (eng is not { IsPlaying: true } || !eng.AutomationRecording) return;
        if (_lanes.IsEditingPoint) return;
        foreach (var t in _tracks) LoadAutoPoints(t);
        _lanes.InvalidateVisual();
    }
    internal IReadOnlyList<TrackVM> Tracks => _tracks;

    /// <summary>When on, the grid scrolls to keep the playhead centred during playback
    /// ("Follow"); the transport-bar Follow toggle drives this.</summary>
    public bool FollowPlayhead { get; set; }

    /// <summary>Snap the view to the playhead now (called when Follow is switched on so the
    /// grid jumps to the cursor immediately, without waiting for the next playback tick).</summary>
    public void RecenterOnPlayhead()
    {
        CenterOnPlayhead();
        _ruler.InvalidateVisual();
        _sectionsLane.InvalidateVisual();
        _lanes.InvalidateVisual();
        _overlay.InvalidateVisual();
        _footerLanes.InvalidateVisual();
        _overview.InvalidateVisual();
    }

    // Recentre the horizontal scroll on the playhead (called each tick while following +
    // playing). Clamped to the scroll range, so near the start/end the playhead just sits
    // where it lands instead of the view over-scrolling.
    // Returns true when the scroll actually moved, so the caller can repaint the lane
    // surface (a programmatic _hScroll.Value change does NOT raise the Scroll event that
    // otherwise redraws the lanes).
    private bool CenterOnPlayhead()
    {
        double laneWidth = _lanes.Bounds.Width;
        if (laneWidth <= 0 || _pixelsPerBeat <= 0) return false;
        double viewportBeats = laneWidth / _pixelsPerBeat;
        double target = Math.Clamp(_playheadBeats - viewportBeats / 2.0, 0, Math.Max(0, _hScroll.Maximum));
        if (Math.Abs(target - _scrollBeats) < 1e-6) return false;
        _scrollBeats = target;
        _hScroll.Value = target;   // the InvalidateVisual calls below repaint with the new scroll
        return true;
    }

    public void SetPlayhead(double beats)
    {
        _playheadBeats = beats;
        // While following, the view scrolls under the playhead — the lane surface (clips /
        // waveforms) must repaint along with the ruler+overlay, not just the overlay.
        bool scrolled = FollowPlayhead && _engine is { IsPlaying: true } && CenterOnPlayhead();
        if (scrolled) _lanes.InvalidateVisual();
        // Converge the loop highlight with the engine (~30 Hz) so transport-bar
        // toggles/changes reflect here too — except mid-drag, where we show a preview.
        if (!_loopDragging && _engine is { } eng)
        {
            bool on = eng.LoopEnabled; double s = eng.LoopStart, e = eng.LoopEnd;
            if (on != _loopActive || s != _loopS || e != _loopE)
            { _loopActive = on; _loopS = s; _loopE = e; }
        }
        // Only the overlay (playhead + loop band) moves each tick — the lane surface
        // beneath is unchanged, so leave it be to avoid a full clip/waveform repaint.
        _ruler.InvalidateVisual();
        if (_sectionsRow.IsVisible) _sectionsLane.InvalidateVisual();
        _overlay.InvalidateVisual();
        _footerLanes.InvalidateVisual();
        TickOverviewPlayhead();   // only when the marker crossed a pixel of the strip

        // Automation mode: an empty lane draws a baseline at the param's live value, so it must
        // follow a knob turned in the device. Redraw the lanes only when a visible empty lane's
        // baseline actually changed (params with points are automation-driven — no baseline).
        if (_automationMode) RefreshAutoBaselines();
    }

    // Repaint the lane surface when a visible un-automated (empty-lane) target's live value
    // changed, so the dashed baseline follows the device knob in real time.
    private void RefreshAutoBaselines()
    {
        double sig = 17.0;
        var (rLo, rHi) = VisibleRowRange();
        for (int i = rLo; i <= rHi && i < _tracks.Count; i++)
        {
            var t = _tracks[i];
            if (t.AutoPoints.Count != 0) continue;   // only empty lanes show a baseline
            sig = sig * 31.0 + (AutoCurrent(t) + 3.0) * (i * 7 + 1);
        }
        if (sig != _autoBaselineSig) { _autoBaselineSig = sig; _lanes.InvalidateVisual(); }
    }

    // ---- loop region authoring -------------------------------------------

    /// <summary>Live preview while dragging a loop range on the ruler (no engine write).</summary>
    private void SetLoopPreview(double a, double b)
    {
        _loopDragging = true;
        _loopActive = true;
        _loopS = Math.Max(0, Snap(Math.Min(a, b)));
        _loopE = Math.Max(_loopS + _snapBeats, Snap(Math.Max(a, b)));
        Redraw();
    }

    /// <summary>Commit the current drag preview to the engine + notify the transport bar.</summary>
    private void CommitLoopPreview()
    {
        _loopDragging = false;
        SetLoopRegion(_loopS, _loopE);
    }

    private void CancelLoopPreview() { _loopDragging = false; }

    /// <summary>Enable looping over [start,end] (snapped). Notifies the transport bar.</summary>
    internal void SetLoopRegion(double startBeat, double endBeat)
    {
        if (_engine is null) return;
        double s = Math.Max(0, Snap(Math.Min(startBeat, endBeat)));
        double e = Math.Max(s + _snapBeats, Snap(Math.Max(startBeat, endBeat)));
        _engine.SetLoop(true, s, e);
        _loopActive = true; _loopS = s; _loopE = e; _loopDragging = false;
        Redraw();
        LoopChanged?.Invoke();
    }

    /// <summary>Loop over the beat span of the current clip selection. False if nothing selected.</summary>
    public bool LoopSelection()
    {
        if (_engine is null || _sel.Count == 0) return false;
        double min = double.MaxValue, max = double.MinValue;
        foreach (var t in _tracks)
            foreach (var c in t.Clips)
                if (_sel.Contains((t.Id, c.ClipIndex)))
                { min = Math.Min(min, c.StartBeat); max = Math.Max(max, c.StartBeat + c.LengthBeats); }
        if (max <= min) return false;
        SetLoopRegion(min, max);
        return true;
    }

    /// <summary>Pushes fresh per-track meter readings into the header meters (~30 Hz, M6-2),
    /// and refreshes the volume/pan controls so automation (playback or scrub) moves them live.</summary>
    public void UpdateMeters()
    {
        if (_engine is not { } eng) return;
        foreach (var (id, bar) in _meters)
        {
            if (eng.TryGetTrackMeter(id, out var m)) bar.Push(m);
            else bar.Reset();
        }
        int n = eng.TrackCount;
        for (int i = 0; i < n; i++)
        {
            if (!eng.TryGetTrackInfo(i, out var ti)) continue;
            // Follow the live (automated) value; don't fight the user mid-drag.
            if (_volFaders.TryGetValue(ti.Id, out var f) && !f.Dragging && Math.Abs(f.Value - ti.Volume) > 1e-3)
            {
                f.Value = ti.Volume;
                if (_volDb.TryGetValue(ti.Id, out var db))
                    db.Text = ti.Volume <= 0.0011 ? "−∞" : AudioMath.LinToDb(ti.Volume).ToString("0.0");
            }
            if (_panBars.TryGetValue(ti.Id, out var p) && !p.Dragging && Math.Abs(p.Pan - ti.Pan) > 1e-3)
                p.Pan = ti.Pan;
        }
    }

    private void Zoom(double factor)
    {
        _pixelsPerBeat = Math.Clamp(_pixelsPerBeat * factor, 4, 240);
        SyncScroll(_lanes.Bounds.Width);
        Redraw();
    }

    // Zoom horizontally while keeping the beat under the cursor fixed (⌘/Ctrl+wheel).
    internal void ZoomAtBeat(double factor, double beatAnchor)
    {
        double old = _pixelsPerBeat;
        _pixelsPerBeat = Math.Clamp(_pixelsPerBeat * factor, 4, 240);
        if (Math.Abs(_pixelsPerBeat - old) < 1e-9) return;
        _scrollBeats = Math.Max(0, beatAnchor - (beatAnchor - _scrollBeats) * (old / _pixelsPerBeat));
        SyncScroll(_lanes.Bounds.Width);   // clamps _scrollBeats to range + syncs the scrollbar
        Redraw();
    }

    // Inclusive track-row range currently on screen, given the vertical scroll — the lane
    // render skips rows outside it so off-screen tracks pay no clip/waveform cost. A one-row
    // bleed each side keeps partially-scrolled rows fully painted. Returns (0,-1) when empty.
    internal (int lo, int hi) VisibleRowRange()
    {
        if (_tracks.Count == 0) return (0, -1);
        double offY = _scroller?.Offset.Y ?? 0;
        double vpH = _scroller is { } s && s.Viewport.Height > 0 ? s.Viewport.Height : _lanes.Bounds.Height;
        int lo = Math.Max(0, (RowAtYClamped(offY)) - 1);
        int hi = Math.Min(_tracks.Count - 1, RowAtYClamped(offY + vpH) + 1);
        return (lo, hi);
    }

    private void Redraw()
    {
        _ruler.InvalidateVisual();
        _sectionsLane.InvalidateVisual();   // spans follow scroll + zoom
        _lanes.InvalidateVisual();
        _overlay.InvalidateVisual();   // playhead/loop band track scroll+zoom+loop edits
        _footerLanes.InvalidateVisual();
        _overview.InvalidateVisual();  // the viewport window follows scroll+zoom
        UpdateOverviewRange();
    }

    /// <summary>Rebuilds the track/clip model + headers from the engine. Pass
    /// <paramref name="rebuildHeaders"/>=false to refresh only the clip data + lane drawing
    /// (used during recording, so the header controls — the input combobox — and any open
    /// context menu survive the 60 Hz tick instead of being torn down each frame).</summary>
    public void Refresh(bool rebuildHeaders = true)
    {
        if (rebuildHeaders) RefreshStarting?.Invoke();
        // Peak reuse (⑤): on a live refresh (no header rebuild) the audio content of existing
        // clips can't change — recording appends a separate take drawn elsewhere, and the live
        // MIDI-drag path only touches a MIDI clip. So carry each audio clip's already-fetched
        // peaks over instead of recomputing 2048 buckets per clip on every 60 Hz tick. A full
        // Refresh (after an edit) always refetches, so trims/reverses/warps stay correct.
        Dictionary<(int track, int clip), ClipVM>? oldClips = null;
        if (!rebuildHeaders)
        {
            oldClips = new();
            foreach (var t in _tracks)
                foreach (var c in t.Clips)
                    if (!c.IsMidi && c.Peaks is not null) oldClips[(t.Id, c.ClipIndex)] = c;
        }
        _tracks.Clear();
        _returns.Clear();
        if (_engine is { } eng)
        {
            int n = eng.TrackCount;
            for (int i = 0; i < n; i++)
            {
                if (!eng.TryGetTrackInfo(i, out var ti)) continue;
                // Stored colour (set via the header menu) wins; otherwise auto by position.
                int effColor = EffectiveColorIndex(eng, ti.Id);
                var tvm = new TrackVM
                {
                    Id = ti.Id,
                    IsInstrument = ti.IsInstrument,
                    IsReturn = ti.IsReturn,
                    IsGroup = ti.IsGroup,
                    GroupId = ti.GroupId,
                    ColorIndex = effColor,
                    Muted = ti.Muted != 0,
                    Soloed = ti.Soloed != 0,
                    Armed = ti.Armed != 0,
                    Frozen = !ti.IsReturn && !ti.IsGroup && eng.IsTrackFrozen(ti.Id),   // M7
                    LiveRole = FreezeRole?.Invoke(ti.Id) ?? 0,                          // live-freeze (v1.1)
                    // A stored name (set via the header menu) wins; otherwise the derived default.
                    Name = IsProcessingTrack(ti.Id) ? "Processing…" : TrackNames.Of(eng, ti),
                    Volume = ti.Volume,
                    Pan = ti.Pan,
                };
                if (_automationMode)
                {
                    if (_autoTargets.TryGetValue(ti.Id, out var sel))
                        (tvm.AutoTarget, tvm.AutoDeviceIndex, tvm.AutoParamIndex, tvm.AutoParamId) = sel;
                    tvm.AutoLabel = AutoLabelFor(tvm);
                    LoadAutoPoints(tvm);
                }
                for (int c = 0; c < ti.ClipCount; c++)
                {
                    if (!eng.TryGetClipInfo(ti.Id, c, out var ci)) continue;
                    var cvm = new ClipVM
                    {
                        ClipIndex = c,
                        StartBeat = ci.StartBeat,
                        LengthBeats = ci.LengthBeats,
                        IsMidi = ci.IsMidi,
                        Active = ci.IsActive,
                        Name = eng.GetClipName(ti.Id, c),
                    };
                    if (ci.IsMidi)
                    {
                        cvm.Notes = eng.GetClipNotes(ti.Id, c);
                    }
                    else if (oldClips is not null
                             && oldClips.TryGetValue((ti.Id, c), out var prev)
                             && Math.Abs(prev.StartBeat - ci.StartBeat) < 1e-9
                             && Math.Abs(prev.LengthBeats - ci.LengthBeats) < 1e-9)
                    {
                        // Unchanged audio clip on a live refresh: reuse the peaks (no native recompute).
                        cvm.Peaks = prev.Peaks;
                        cvm.PeakCount = prev.PeakCount;
                    }
                    else
                    {
                        // Request enough buckets that even wide/zoomed clips stay detailed
                        // (the lane draws one aggregated column per pixel).
                        const int buckets = 2048;
                        var peaks = new float[buckets * 2];
                        cvm.PeakCount = eng.GetClipPeaks(ti.Id, c, peaks, buckets);
                        cvm.Peaks = peaks;
                    }
                    if (!ci.IsMidi && eng.TryGetAudioClipInfo(ti.Id, c, out var ai)) cvm.Gain = ai.Gain;
                    tvm.Clips.Add(cvm);
                }
                MarkClipRuns(tvm);
                (tvm.IsReturn ? _returns : _tracks).Add(tvm);
            }
        }

        ApplyHierarchy();   // reorder _tracks into group DFS order (depth + collapse-filtered)
        RebuildRowGeometry();   // row tops depend on which rows are groups → after the reorder
        RecomputeTotalBeats();
        LoadMasterAuto();   // master-volume automation for the footer row (M9 follow-up)
        if (rebuildHeaders)
        {
            RebuildHeaders();
            RebuildFooterHeaders();
        }
        _lanes.Height = Math.Max(RowHeight, RowsHeight);
        _overlay.Height = _lanes.Height;   // keep the playhead/loop overlay the same span
        double footerH = (_returns.Count + 1) * FooterRowH;   // returns + master
        _footer.Height = footerH;
        _footerHeaders.Height = footerH;
        _footerLanes.InvalidateVisual();
        SyncScroll(_lanes.Bounds.Width);
        Redraw();
    }

    // A clip heads a run when nothing on the track ends where it begins. Engine clip order
    // is not sorted by time, so this walks a start-ordered view and leaves the VM order alone
    // (clip indices there address the engine).
    private static void MarkClipRuns(TrackVM t)
    {
        if (t.Clips.Count == 0) return;
        var byStart = t.Clips.OrderBy(c => c.StartBeat).ToList();
        double runEnd = double.NegativeInfinity;
        foreach (var c in byStart)
        {
            c.RunStart = c.StartBeat > runEnd + 1e-6;
            runEnd = Math.Max(runEnd, c.StartBeat + c.LengthBeats);
        }
    }

    private void RecomputeTotalBeats()
    {
        double end = 0;
        foreach (var t in _tracks)
            foreach (var c in t.Clips)
                end = Math.Max(end, c.StartBeat + c.LengthBeats);
        _totalBeats = Math.Max(64, Math.Ceiling(end / _beatsPerBar) * _beatsPerBar + 16);
    }

    // Keep the horizontal scrollbar's range in sync with the visible width.
    internal void SyncScroll(double laneWidth)
    {
        double viewportBeats = laneWidth > 0 ? laneWidth / _pixelsPerBeat : _totalBeats;
        _hScroll.ViewportSize = viewportBeats;
        _hScroll.Maximum = Math.Max(0, _totalBeats - viewportBeats);
        if (_scrollBeats > _hScroll.Maximum) { _scrollBeats = _hScroll.Maximum; }
        _hScroll.Value = _scrollBeats;
    }

    internal void ScrollByBeats(double delta)
    {
        _scrollBeats = Math.Clamp(_scrollBeats + delta, 0, _hScroll.Maximum);
        _hScroll.Value = _scrollBeats;
        Redraw();
    }

    // Wheel zoom/scroll for the timeline. Shared by the lanes and the scroller so the empty
    // area below the tracks (when few lanes) reacts too. cursorX is the horizontal cursor
    // position in lane coordinates (for zoom-toward-cursor). Plain vertical is left unhandled
    // so the enclosing ScrollViewer can scroll the tracks.
    internal void HandleLaneWheel(PointerWheelEventArgs e, double cursorX)
    {
        var mods = e.KeyModifiers;

        // ⌘/Ctrl + wheel → horizontal zoom toward the cursor (DAW-standard).
        if ((mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            double beatAtCursor = _scrollBeats + cursorX / _pixelsPerBeat;
            ZoomAtBeat(WheelInput.ZoomFactor(e.Delta.Y, 1.25), beatAtCursor);
            e.Handled = true;
            return;
        }

        // Horizontal intent: a horizontal-dominant trackpad swipe, or Shift + wheel. Delta is
        // device-normalized to a pixel amount, then converted to beats at the current zoom so
        // it scrolls a consistent distance and can't fly to the ends.
        if (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
        {
            ScrollByBeats(-WheelInput.Pixels(e.Delta.X) / _pixelsPerBeat);
            e.Handled = true;
            return;
        }
        if ((mods & KeyModifiers.Shift) != 0)
        {
            ScrollByBeats(-WheelInput.Pixels(e.Delta.Y) / _pixelsPerBeat);
            e.Handled = true;
            return;
        }

        // Plain vertical wheel / trackpad → let the enclosing ScrollViewer scroll the tracks
        // vertically (don't handle, so the event bubbles up to it).
    }

    internal void OnClipDoubleClicked(int trackId, int clipIndex, bool isMidi)
    {
        if (isMidi) MidiClipActivated?.Invoke(trackId, clipIndex);
        else AudioClipActivated?.Invoke(trackId, clipIndex);
    }

    /// <summary>Currently selected track id, or -1 (M4.1-C: effect target).</summary>
    public int SelectedTrackId => SelTrackId;

    /// <summary>Currently selected clip index within the selected track, or -1.</summary>
    public int SelectedClipIndex => SelClipIndex;

    /// <summary>Best track to auto-arm when Record is pressed with nothing armed:
    /// the selected track if it's a recordable (audio/instrument) row, else the
    /// last such row, else 0. Return buses (in <c>_returns</c>) are never a target.</summary>
    public int RecordArmTarget()
    {
        foreach (var t in _tracks)
            if (t.Id == SelTrackId) return t.Id;
        return _tracks.Count > 0 ? _tracks[^1].Id : 0;
    }

    /// <summary>Creates an empty MIDI clip on an instrument track at the bar containing
    /// <paramref name="beat"/> (double-click on empty lane space). Selects the new clip.</summary>
    internal void AddMidiClipAt(int trackId, double beat)
    {
        if (_engine is null) return;
        double bar = Math.Max(1, _beatsPerBar);
        double start = Math.Max(0, Math.Floor(beat / bar) * bar);
        int idx = _engine.AddMidiClip(trackId, start, bar);
        Refresh();
        if (idx >= 0) { SelTrackId = trackId; SelClipIndex = idx; UpdateHeaderSelection(); Redraw(); }
    }

    // The whole clip selection as an engine block: the multi-selection when non-empty,
    // else the primary clip, else empty. Copy/Cut/Duplicate/Paste all operate on this
    // block so a group of clips travels together (with its automation) preserving geometry.
    private (int trackId, int clipIndex)[] SelectionBlock()
    {
        if (_sel.Count > 0) return _sel.Select(s => (s.track, s.clip)).ToArray();
        if (SelTrackId > 0 && SelClipIndex >= 0) return new[] { (SelTrackId, SelClipIndex) };
        return Array.Empty<(int, int)>();
    }

    // Re-select exactly the clips the last block paste/duplicate produced.
    private void ReselectPlaced()
    {
        if (_engine is null) return;
        var placed = _engine.LastPlacedClips();
        if (placed.Length > 0) SetSelection(placed);
    }

    /// <summary>Duplicates the whole clip selection as one block, placed right after itself
    /// (keyboard Cmd+D / context menu). The copies become the new selection. False if none.</summary>
    public bool DuplicateSelectedClip()
    {
        if (_engine is null) return false;
        var sel = SelectionBlock();
        if (sel.Length == 0 || _engine.DuplicateClipBlock(sel) < 0) return false;
        Refresh();
        ReselectPlaced();
        return true;
    }

    /// <summary>Copies the whole clip selection (with its automation) to the block clipboard.</summary>
    public bool CopySelectedClip()
    {
        if (_engine is null) return false;
        var sel = SelectionBlock();
        return sel.Length > 0 && _engine.CopyClipBlock(sel);
    }

    /// <summary>Cuts the whole clip selection: copies the block to the clipboard, then removes
    /// the clips and their track automation.</summary>
    public bool CutSelectedClip()
    {
        if (_engine is null) return false;
        var sel = SelectionBlock();
        if (sel.Length == 0 || !_engine.CutClipBlock(sel)) return false;
        Select(-1, -1);
        Refresh();
        return true;
    }

    /// <summary>Pastes the block clipboard at <paramref name="atBeat"/>, remapped so its top
    /// track lands on <paramref name="trackId"/> (single clip → that track). Selects the paste.</summary>
    public bool PasteClipboardAt(int trackId, double atBeat)
    {
        if (_engine is null || trackId <= 0) return false;
        if (_engine.PasteClipBlock(atBeat, trackId) <= 0) return false;
        Refresh();
        ReselectPlaced();
        return true;
    }

    /// <summary>Keyboard paste: the block lands on its source tracks at the playhead.</summary>
    public bool PasteClipboard()
    {
        if (_engine is null || _engine.ClipboardBlockCount() == 0) return false;
        if (_engine.PasteClipBlock(Math.Max(0.0, _playheadBeats), -1) <= 0) return false;
        Refresh();
        ReselectPlaced();
        return true;
    }

    /// <summary>True when the block clipboard holds at least one clip (drives menu enablement).</summary>
    public bool HasClipClipboard => _engine?.ClipboardBlockCount() > 0;

    /// <summary>Clipboard clip kind: -1 empty, else 0 (block clipboard has content).</summary>
    public int ClipboardClipKind() => HasClipClipboard ? 0 : -1;

    internal double Snap(double beat)
        => _snapEnabled && _snapBeats > 0 ? Math.Round(beat / _snapBeats) * _snapBeats : beat;

    /// <summary>Snap unless Alt is held (Alt = free/fine positioning). Reads the live
    /// modifier state so it can be toggled mid-drag (req 1.2.1/2.4/3.1/8.2.1, 7.10).</summary>
    internal double SnapMaybe(double beat, KeyModifiers mods)
        => (mods & KeyModifiers.Alt) != 0 ? beat : Snap(beat);

    /// <summary>Snap to the grid, but let a nearby snap target (a neighbouring clip edge or
    /// marker within ~8px) win when it is closer — a magnetic snap (req 2.4). Alt bypasses all
    /// snapping. <paramref name="targets"/> are absolute beats.</summary>
    internal double SnapMagnetic(double raw, KeyModifiers mods, IReadOnlyList<double> targets)
    {
        if ((mods & KeyModifiers.Alt) != 0) return raw;
        double best = Snap(raw);
        double bestDist = Math.Abs(raw - best);
        double thresh = _pixelsPerBeat > 0 ? 8.0 / _pixelsPerBeat : 0;   // 8 logical px
        foreach (var t in targets)
        {
            double d = Math.Abs(raw - t);
            if (d <= thresh && d < bestDist) { best = t; bestDist = d; }
        }
        return best;
    }

    /// <summary>Absolute beats of every clip edge (start/end) plus the loop bounds, excluding
    /// the given clips — the magnetic-snap targets for a drag (req 2.4).</summary>
    internal List<double> SnapTargets(ISet<(int track, int clip)> exclude)
    {
        var list = new List<double>();
        foreach (var t in _tracks)
            foreach (var c in t.Clips)
            {
                if (exclude.Contains((t.Id, c.ClipIndex))) continue;
                list.Add(c.StartBeat);
                list.Add(c.StartBeat + c.LengthBeats);
            }
        if (_loopE > _loopS) { list.Add(_loopS); list.Add(_loopE); }
        return list;
    }

    /// <summary>Primary selection modifier: Cmd on macOS, Ctrl on Windows/Linux (req 7.11).
    /// On macOS Ctrl is reserved for the system context-menu click, so it never toggles.</summary>
    internal static bool IsPrimaryDown(KeyModifiers mods)
        => OperatingSystem.IsMacOS() ? (mods & KeyModifiers.Meta) != 0
                                     : (mods & KeyModifiers.Control) != 0;

    /// <summary>"bar.beat" label (1-indexed) for a beat position — drag tooltips (req 2.9).</summary>
    internal string FormatBarBeat(double beat)
    {
        double bpb = _beatsPerBar > 0 ? _beatsPerBar : 4;
        int bar = (int)Math.Floor(beat / bpb) + 1;
        double inBar = beat - (bar - 1) * bpb + 1;
        return $"{bar}.{inBar:0.##}";
    }

    // --- browser drag & drop (M7-5) ---
    private void OnLaneDragOver(object? sender, DragEventArgs e)
    {
        bool ok = BrowserView.IsAcceptableDrag(e);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        int ti = ok ? RowAtY(e.GetPosition(_lanes).Y) : -1;
        if (ti != DropTrackIndex) { DropTrackIndex = ti; _lanes.InvalidateVisual(); }
    }

    private void OnLaneDrop(object? sender, DragEventArgs e)
    {
        DropTrackIndex = -1; _lanes.InvalidateVisual();
        var items = BrowserView.DroppedItems(e);
        if (items.Count == 0) return;
        var p = e.GetPosition(_lanes);
        int ti = RowAtY(p.Y);
        int trackId = ti >= 0 ? _tracks[ti].Id : -1;
        double beat = Math.Max(0.0, Snap(_scrollBeats + p.X / _pixelsPerBeat));
        // One internal item drops as-is; multiple external files land the first on the
        // target track and each of the rest on its own new track (trackId -1), so they
        // never stack on top of each other.
        for (int i = 0; i < items.Count; i++)
            ItemDropped?.Invoke(items[i], i == 0 ? trackId : -1, beat);
        e.Handled = true;
    }
    internal void Select(int trackId, int clipIndex)
    {
        if (HasTimeSelection) ClearTimeSelection();   // clip- and time-selection are exclusive (1.2.5)
        int prev = SelTrackId;
        SelTrackId = trackId;
        SelClipIndex = clipIndex;
        if (trackId > 0) FocusTrackId = trackId;
        _sel.Clear();
        if (trackId > 0 && clipIndex >= 0) _sel.Add((trackId, clipIndex));
        // Selecting into or out of a group changes that row's height, so the rows below it
        // move: re-lay them before the headers repaint their selection.
        if (prev != trackId && (IsGroupTrack(prev) || IsGroupTrack(trackId))) RelayoutRows();
        UpdateHeaderSelection();
        RebuildFooterHeaders();   // returns/master carry the selection highlight too (1e)
        Redraw();
        if (trackId > 0) TrackSelected?.Invoke(trackId);
    }

    private bool IsGroupTrack(int id)
    {
        if (id <= 0) return false;
        foreach (var t in _tracks) if (t.Id == id) return t.IsGroup;
        return false;
    }

    // Re-measure the rows and rebuild the header cards against the new heights. Cheaper than a
    // full Refresh (no engine read, no peak fetch) and enough for a pure layout change.
    private void RelayoutRows()
    {
        RebuildRowGeometry();
        RebuildHeaders();
        _lanes.Height = Math.Max(RowHeight, RowsHeight);
        _overlay.Height = _lanes.Height;
    }

    /// <summary>True if the clip is part of the current multi-selection.</summary>
    internal bool IsSelected(int trackId, int clipIndex) => _sel.Contains((trackId, clipIndex));

    /// <summary>Whole current selection (for group move).</summary>
    internal IReadOnlyCollection<(int track, int clip)> Selection => _sel;

    /// <summary>Number of clips in the current multi-selection.</summary>
    internal int SelectionCount => _sel.Count;

    /// <summary>Cmd/Ctrl+click: toggle a clip in the multi-selection, leaving the rest intact
    /// (req 1.1.2). Clip- and automation-selection are mutually exclusive (req 1.2.5).</summary>
    internal void ToggleSelect(int trackId, int clipIndex)
    {
        if (trackId <= 0 || clipIndex < 0) return;
        if (HasTimeSelection) ClearTimeSelection();
        if (HasAutoSelection) ClearAutoSelection();
        if (_sel.Remove((trackId, clipIndex)))
        {
            // Removed: keep the primary stable unless we just removed it.
            if (SelTrackId == trackId && SelClipIndex == clipIndex)
            {
                if (_sel.Count > 0) { var f = _sel.First(); SelTrackId = f.track; SelClipIndex = f.clip; }
                else { SelTrackId = -1; SelClipIndex = -1; }
            }
        }
        else
        {
            _sel.Add((trackId, clipIndex));
            SelTrackId = trackId; SelClipIndex = clipIndex; FocusTrackId = trackId;   // the just-clicked clip is the primary
        }
        UpdateHeaderSelection();
        Redraw();
    }

    /// <summary>The live ClipVM for a (track, clip) pair, or null if it no longer exists.</summary>
    internal ClipVM? FindClipVM(int trackId, int clipIndex)
    {
        foreach (var t in _tracks)
            if (t.Id == trackId)
                foreach (var c in t.Clips)
                    if (c.ClipIndex == clipIndex) return c;
        return null;
    }

    /// <summary>Esc: cancel an in-progress lane gesture if one is active (no model change),
    /// otherwise clear any selection (req 1.2.6 / 2.11). Returns true if it did something.</summary>
    /// <summary>Abort any in-progress lane gesture with no model change (window focus loss).</summary>
    public void CancelActiveGesture() => _lanes.CancelGesture();

    internal bool EscapePressed()
    {
        if (_lanes.CancelGesture()) return true;
        bool had = _sel.Count > 0 || SelTrackId > 0 || HasAutoSelection || HasTimeSelection;
        if (HasAutoSelection) ClearAutoSelection();
        if (HasTimeSelection) ClearTimeSelection();
        if (_sel.Count > 0 || SelTrackId > 0) Select(-1, -1);
        return had;
    }

    // --- general time-range selection (req 1.2 / 1.2.3 / 5.3 / 4.1-range) -------
    private int RowOfTrack(int trackId)
    {
        for (int r = 0; r < _tracks.Count; r++) if (_tracks[r].Id == trackId) return r;
        return -1;
    }

    /// <summary>Sets (live, during a drag) the time-range selection over [a,b] beats and the
    /// visual rows spanned. Clears any clip selection — the two are mutually exclusive.</summary>
    internal void SetTimeSelection(double a, double b, int rowA, int rowB)
    {
        _timeSelStart = Math.Max(0, Math.Min(a, b));
        _timeSelEnd = Math.Max(a, b);
        _timeSelRowLo = Math.Max(0, Math.Min(rowA, rowB));
        _timeSelRowHi = Math.Min(Math.Max(0, _tracks.Count - 1), Math.Max(rowA, rowB));
        if (_sel.Count > 0) { _sel.Clear(); SelClipIndex = -1; UpdateHeaderSelection(); }
        if (HasTimeSelection) BounceSource = (TimeSelectionTrackIds(), _timeSelStart, _timeSelEnd);
        Redraw();
    }

    /// <summary>Paste Bounced Audio (⌘⇧V) source: the most recent time selection's tracks and
    /// range. Outlives the selection itself, so the user can click over to the target track.</summary>
    public (int[] TrackIds, double Start, double End)? BounceSource { get; private set; }

    /// <summary>The track the user last clicked (clip, header, or empty lane space) — the target
    /// of Paste Bounced Audio. Unlike <see cref="SelectedTrackId"/>, a click on empty lane space
    /// keeps it, since that click is how the paste position (the playhead) is placed.</summary>
    public int FocusTrackId { get; internal set; } = -1;

    /// <summary>Context-menu Paste Bounced Audio onto (track id, beat).</summary>
    public event Action<int, double>? PasteBouncedRequested;
    internal void RequestPasteBounced(int trackId, double beat) => PasteBouncedRequested?.Invoke(trackId, beat);

    /// <summary>True when Paste Bounced Audio can land on <paramref name="trackId"/>: it's an audio
    /// track and the remembered range covers exactly one audio/instrument track that still exists.</summary>
    internal bool CanPasteBouncedOnto(int trackId)
    {
        if (BounceSource is not { } src) return false;
        var target = _tracks.Find(t => t.Id == trackId);
        if (target is null || target.IsInstrument || target.IsReturn || target.IsGroup) return false;
        int sources = 0;
        foreach (int id in src.TrackIds)
            if (_tracks.Find(t => t.Id == id) is { IsReturn: false, IsGroup: false }) sources++;
        return sources == 1;
    }

    /// <summary>Refreshes and selects the clips the engine placed last (e.g. a bounced paste).</summary>
    public void RefreshAndSelectPlaced()
    {
        Refresh();
        ReselectPlaced();
    }

    internal void ClearTimeSelection()
    {
        if (_timeSelRowLo < 0) return;
        _timeSelRowLo = _timeSelRowHi = -1;
        Redraw();
    }

    // Non-return track IDs the time selection covers (target of range ops).
    private int[] TimeSelectionTrackIds()
    {
        var ids = new List<int>();
        for (int r = _timeSelRowLo; r <= _timeSelRowHi && r >= 0 && r < _tracks.Count; r++)
            if (!_tracks[r].IsReturn) ids.Add(_tracks[r].Id);
        return ids.ToArray();
    }


    /// <summary>True when the time selection spans this track's row at this beat (right-click
    /// inside the range acts on the range).</summary>
    internal bool TimeSelectionCovers(int trackId, double beat)
    {
        if (!HasTimeSelection) return false;
        int r = RowOfTrack(trackId);
        return r >= _timeSelRowLo && r <= _timeSelRowHi && beat >= _timeSelStart && beat <= _timeSelEnd;
    }

    /// <summary>Delete the covered clip content in the time selection (split at edges, no
    /// ripple; req 5.3). Consumes the key whenever a range is active.</summary>
    public bool DeleteTimeSelection()
    {
        if (_engine is null || !HasTimeSelection) return false;
        var ids = TimeSelectionTrackIds();
        if (ids.Length > 0) _engine.DeleteClipsInRange(ids, _timeSelStart, _timeSelEnd);
        Refresh();
        return true;
    }

    /// <summary>Cmd+E over a time selection: split every covered track's clips at the range
    /// edges, so the selected slice becomes its own clip(s). False when there's no range.</summary>
    public bool SplitTimeSelection()
    {
        if (_engine is null || !HasTimeSelection) return false;
        var ids = TimeSelectionTrackIds();
        if (ids.Length == 0 || !_engine.SplitClipsInRange(ids, _timeSelStart, _timeSelEnd)) return false;
        Refresh();
        return true;
    }

    /// <summary>Cmd+J Consolidate: merge the time selection — or, without one, the span of the
    /// clip selection on the selected clips' tracks — into one clip per track (MIDI notes merged,
    /// audio rendered to a new sample). The new clips become the selection. False when there's
    /// nothing to consolidate.</summary>
    public bool ConsolidateSelection()
    {
        if (_engine is null) return false;
        int[] ids;
        double start, end;
        if (HasTimeSelection)
        {
            ids = TimeSelectionTrackIds();
            start = _timeSelStart; end = _timeSelEnd;
        }
        else
        {
            var sel = SelectionBlock();
            start = double.MaxValue; end = double.MinValue;
            foreach (var (track, clip) in sel)
                if (FindClipVM(track, clip) is { } c)
                { start = Math.Min(start, c.StartBeat); end = Math.Max(end, c.StartBeat + c.LengthBeats); }
            ids = sel.Select(s => s.trackId).Distinct().ToArray();
        }
        if (ids.Length == 0 || end <= start || !_engine.ConsolidateRange(ids, start, end)) return false;
        Refresh();
        ReselectPlaced();
        return true;
    }

    /// <summary>Cmd+L over a time selection: loop exactly that range. False when there's no range.</summary>
    public bool LoopTimeSelection()
    {
        if (_engine is null || !HasTimeSelection) return false;
        SetLoopRegion(_timeSelStart, _timeSelEnd);
        return true;
    }

    /// <summary>Duplicate the time selection right after its end and move the selection onto
    /// the copy, so repeated presses chain (req 4.1-range).</summary>
    public bool DuplicateTimeSelection()
    {
        if (_engine is null || !HasTimeSelection) return false;
        var ids = TimeSelectionTrackIds();
        double len = ids.Length > 0 ? _engine.DuplicateRange(ids, _timeSelStart, _timeSelEnd) : 0;
        if (len <= 0) return false;
        _timeSelStart = _timeSelEnd;
        _timeSelEnd = _timeSelStart + len;
        Refresh();
        return true;
    }

    /// <summary>Shift+click rectangular clip selection: every clip between the primary-selection
    /// anchor and the clicked clip, across rows and beats (req 1.1.3).</summary>
    internal void ShiftSelectTo(int trackId, int clipIndex)
    {
        var target = FindClipVM(trackId, clipIndex);
        if (target is null) return;
        var anchor = SelClipIndex >= 0 ? FindClipVM(SelTrackId, SelClipIndex) : null;
        if (anchor is null) { Select(trackId, clipIndex); return; }
        int ra = RowOfTrack(SelTrackId), rt = RowOfTrack(trackId);
        if (ra < 0 || rt < 0) { Select(trackId, clipIndex); return; }
        int rlo = Math.Min(ra, rt), rhi = Math.Max(ra, rt);
        double blo = Math.Min(anchor.StartBeat, target.StartBeat);
        double bhi = Math.Max(anchor.StartBeat + anchor.LengthBeats, target.StartBeat + target.LengthBeats);
        ClearTimeSelection();
        _sel.Clear();
        for (int r = rlo; r <= rhi && r < _tracks.Count; r++)
            foreach (var c in _tracks[r].Clips)
                if (c.StartBeat < bhi - 1e-6 && c.StartBeat + c.LengthBeats > blo + 1e-6)
                    _sel.Add((_tracks[r].Id, c.ClipIndex));
        SelTrackId = trackId; SelClipIndex = clipIndex; FocusTrackId = trackId;
        UpdateHeaderSelection();
        Redraw();
    }

    /// <summary>Cmd+E: split every selected clip that spans the playhead, at the playhead
    /// (req 5.1). Returns false when nothing was actually split.</summary>
    public bool SplitSelectedAtPlayhead()
    {
        if (_engine is null || _sel.Count == 0) return false;
        double at = Math.Max(0, _playheadBeats);
        bool any = false;
        // Descending clip index per track: SplitClip inserts a new clip, so splitting a
        // higher index first never invalidates a lower one we still need to visit.
        foreach (var (track, clip) in _sel.OrderByDescending(c => c.clip).ToList())
        {
            var vm = FindClipVM(track, clip);
            if (vm is null) continue;
            if (at > vm.StartBeat + 1e-6 && at < vm.StartBeat + vm.LengthBeats - 1e-6)
            { _engine.SplitClip(track, clip, at); any = true; }
        }
        if (any) { Select(-1, -1); Refresh(); }
        return any;
    }

    /// <summary>Replaces the selection with the given clips (marquee). Does not open
    /// the detail panel — it only highlights; the primary becomes the first clip.</summary>
    internal void SetSelection(IEnumerable<(int track, int clip)> clips)
    {
        if (HasTimeSelection) ClearTimeSelection();
        _sel.Clear();
        foreach (var c in clips) _sel.Add(c);
        if (_sel.Count > 0) { var f = _sel.First(); SelTrackId = f.track; SelClipIndex = f.clip; FocusTrackId = f.track; }
        else { SelTrackId = -1; SelClipIndex = -1; }
        UpdateHeaderSelection();
        Redraw();
    }

    /// <summary>Moves the transport playhead to <paramref name="beat"/> (ruler / grid
    /// click), snapped to the current snap grid.</summary>
    internal void SeekTo(double beat)
    {
        beat = Math.Max(0, Snap(beat));
        _engine?.Seek(beat);
        SetPlayhead(beat);   // immediate visual feedback (the timer keeps it in sync)
    }

    /// <summary>Deletes every clip in the multi-selection. False if nothing selected.</summary>
    /// <summary>Key 0: toggles the selected clip(s) active/inactive (clip deactivate).
    /// If any selected clip is active they all deactivate; otherwise they all activate. A
    /// deactivated clip stays on the timeline but plays nothing and is greyed. False if none.</summary>
    public bool ToggleSelectedClipsActive()
    {
        if (_engine is null) return false;
        var sel = SelectionBlock();
        if (sel.Length == 0) return false;
        // Deactivate if any is currently active, so a mixed selection turns fully off first.
        bool anyActive = false;
        foreach (var (track, clip) in sel)
            if (_engine.TryGetClipInfo(track, clip, out var ci) && ci.IsActive) { anyActive = true; break; }
        bool target = !anyActive, any = false;
        foreach (var (track, clip) in sel)
        {
            if (!_engine.TryGetClipInfo(track, clip, out _)) continue;   // tolerate a stale entry
            _engine.SetClipActive(track, clip, target);
            any = true;
        }
        if (any) Refresh();
        return any;
    }

    /// <summary>Reverse (non-destructive) on every selected AUDIO clip. Mixed selections
    /// reverse the audio clips and leave MIDI alone; if any is still forwards the whole
    /// selection turns on first, so a mixed state resolves to "all reversed".</summary>
    public bool ToggleSelectedClipsReverse()
    {
        if (_engine is null) return false;
        var sel = SelectionBlock();
        if (sel.Length == 0) return false;
        bool anyForward = false, anyAudio = false;
        foreach (var (track, clip) in sel)
            if (_engine.TryGetAudioClipInfo(track, clip, out var ai))
            { anyAudio = true; if (ai.Reversed == 0) anyForward = true; }
        if (!anyAudio) return false;
        bool target = anyForward, any = false;
        foreach (var (track, clip) in sel)
        {
            if (!_engine.TryGetAudioClipInfo(track, clip, out _)) continue;   // MIDI / stale entry
            _engine.SetClipReverse(track, clip, target);
            RaiseClipGeometryChanged(track, clip);   // an open clip editor re-reads the new direction
            any = true;
        }
        if (any) Refresh();
        return any;
    }

    public bool DeleteSelectedClips()
    {
        if (_engine is null || _sel.Count == 0) return false;
        // Delete highest clip index first: DeleteClip shifts later indices within a
        // track, and cross-track order is irrelevant. A selection can hold stale entries
        // (it survives edits and isn't cleared in automation mode), so tolerate an index
        // the engine rejects rather than throwing out of the keystroke handler.
        bool any = false;
        foreach (var (track, clip) in _sel.OrderByDescending(c => c.clip))
            any |= _engine.TryDeleteClip(track, clip);
        _sel.Clear();
        SelTrackId = -1; SelClipIndex = -1;
        Refresh();
        return any;   // false when the selection was entirely stale — don't claim a delete
    }

    // Selection changed but the track set didn't: repaint only the two properties that
    // depend on selection (card background + name colour) on each existing header card,
    // instead of rebuilding the whole header column (faders, meters, combos) on every click.
    private void UpdateHeaderSelection()
    {
        foreach (var (id, h) in _headerCards)
        {
            // Selected row (almanac § States): brass wash, the left stripe turns brass, name in Ink 1.
            bool selected = IsTrackMultiSelected(id);
            h.card.Background = selected ? SelHeaderBg
                : h.isGroup ? Brush("Brush.SurfaceRaised") : Brush("Brush.SurfaceCard");
            h.name.Foreground = Brush("Brush.TextPrimary");
            h.spine.Background = selected ? NotaPalette.Accent : h.spineIdle;
        }
    }

    private void RebuildHeaders()
    {
        _headers.Children.Clear();
        _meters.Clear();
        _volFaders.Clear();
        _panBars.Clear();
        _volDb.Clear();
        _headerCards.Clear();
        for (int i = 0; i < _tracks.Count; i++)
        {
            var card = BuildHeaderCard(_tracks[i]);
            Canvas.SetLeft(card, 0);
            Canvas.SetTop(card, RowTop(i));     // pin to the same Y as the lane row
            _headers.Children.Add(card);
        }
        _headers.Children.Add(_trackDropLine);   // insertion marker (kept invisible until a drag)
        _headers.Height = Math.Max(RowHeight, RowsHeight);
    }

    private Control BuildHeaderCard(TrackVM t)
    {
        if (IsSlimRow(t)) return BuildGroupBar(t);
        return BuildTrackCard(t);
    }

    // A group's row, collapsed to a title bar: disclosure, name, its own mute/solo, kind tag.
    // The fader, pan and meter live on the same card when the group is selected (BuildTrackCard),
    // so nothing is lost — it just stops costing a full row while you work on its children.
    private Control BuildGroupBar(TrackVM t)
    {
        bool selected = IsTrackMultiSelected(t.Id);
        var (_, _, _, spineBrush) = ClipColors(t.ColorIndex);

        var tri = new Border
        {
            Background = Brushes.Transparent, Padding = new Thickness(2, 0), Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Glyph(_collapsed.Contains(t.Id) ? GlyphKind.ChevronRight : GlyphKind.ChevronDown, 8) { Foreground = Brush("Brush.TextSecondary") },
        };
        tri.PointerPressed += (_, e) => { e.Handled = true; ToggleCollapse(t.Id); };

        var name = new TextBlock
        {
            Text = t.Name, FontSize = 11, FontWeight = FontWeight.SemiBold,
            Foreground = Brush("Brush.TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 0, 0),
        };

        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                ChipToggle("M", t.Muted, danger: false, v => _engine?.SetTrackMute(t.Id, v)),
                ChipToggle("S", t.Soloed, danger: false, v => _engine?.SetTrackSolo(t.Id, v)),
            },
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        Grid.SetColumn(tri, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(chips, 2);
        var kind = TypeTag("GROUP");
        Grid.SetColumn(kind, 3);
        row.Children.Add(tri);
        row.Children.Add(name);
        row.Children.Add(chips);
        row.Children.Add(kind);

        // A group keeps its level readable at 26px as a 2px rail under the title.
        var meter = new MeterBar(horizontal: true) { Height = 4, MinWidth = 0 };
        _meters[t.Id] = meter;
        var body = new DockPanel { LastChildFill = true, Margin = new Thickness(8 + t.Depth * 14, 0, 8, 0) };
        DockPanel.SetDock(meter, Dock.Bottom);
        meter.Margin = new Thickness(0, 0, 0, 3);
        body.Children.Add(meter);
        body.Children.Add(row);

        var spine = new Border { Width = 3, Background = spineBrush };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3,*") };
        Grid.SetColumn(spine, 0);
        Grid.SetColumn(body, 1);
        grid.Children.Add(spine);
        grid.Children.Add(body);

        var card = new Border
        {
            Width = HeaderW, Height = GroupRowHeight, ClipToBounds = true,
            Background = selected ? SelHeaderBg : Brush("Brush.SurfaceRaised"),
            BorderBrush = Brush("Brush.BorderDefault"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = grid,
        };
        _headerCards[t.Id] = (card, name, true, spine, spineBrush);
        if (selected) spine.Background = NotaPalette.Accent;
        AttachHeaderGestures(card, t);
        return card;
    }

    private Control BuildTrackCard(TrackVM t)
    {
        bool selected = IsTrackMultiSelected(t.Id);
        var (_, _, _, spineBrush) = ClipColors(t.ColorIndex);

        // Row 1: [group ▸/▾] name (ellipsised) + type-tag chip.
        var name = new TextBlock
        {
            Text = t.Name, FontSize = 11, FontWeight = FontWeight.SemiBold,
            Foreground = Brush("Brush.TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var typeTag = TypeTag(t.IsGroup ? "GROUP" : t.IsInstrument ? "MIDI" : "AUDIO");
        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        if (t.IsGroup)
        {
            // Collapse triangle — hides/shows the group's child rows.
            var tri = new Border
            {
                Background = Brushes.Transparent, Padding = new Thickness(2, 0), Margin = new Thickness(0, 0, 4, 0),
                Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new Glyph(_collapsed.Contains(t.Id) ? GlyphKind.ChevronRight : GlyphKind.ChevronDown, 8) { Foreground = Brush("Brush.TextSecondary") },
            };
            tri.PointerPressed += (_, e) => { e.Handled = true; ToggleCollapse(t.Id); };
            Grid.SetColumn(tri, 0);
            nameRow.Children.Add(tri);
        }
        else if (t.Frozen || t.LiveRole != 0)
        {
            // Freeze badge: a drawn chain-link for a sleeping live-freeze source (1);
            // a drawn snowflake for an in-place frozen track or a linked frozen track (2).
            Control badge = t.LiveRole == 1
                ? new Avalonia.Controls.Shapes.Path
                {
                    Data = NotaIcons.ChainLink, Stretch = Stretch.None,
                    Stroke = FrozenAccent, StrokeThickness = 1.4,
                    Width = 14, Height = 14,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
                }
                : new Glyph(GlyphKind.Freeze, 10) { Foreground = FrozenAccent, Margin = new Thickness(0, 0, 4, 0) };
            Grid.SetColumn(badge, 0);
            nameRow.Children.Add(badge);
        }
        Grid.SetColumn(name, 1);
        Grid.SetColumn(typeTag, 2);
        nameRow.Children.Add(name);
        nameRow.Children.Add(typeTag);

        // Row 2: M / S / ● (record-arm) — 18×16 chips.
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(0, 5, 0, 0) };
        btnRow.Children.Add(ChipToggle("M", t.Muted, danger: false, v => _engine?.SetTrackMute(t.Id, v)));
        btnRow.Children.Add(ChipToggle("S", t.Soloed, danger: false, v => _engine?.SetTrackSolo(t.Id, v)));
        // Record-arm applies to instrument (MIDI capture) + audio (input capture) tracks;
        // return and group tracks have no input, so they get no arm chip.
        if (!t.IsReturn && !t.IsGroup)
            btnRow.Children.Add(ArmToggle(t.Armed, v => _engine?.SetTrackArmed(t.Id, v)));

        // A compact routing selector sits with the arm button (the 64px row leaves no room
        // for a 4th row): audio tracks get record-input, instrument tracks get "MIDI To".
        bool isAudioTrack = !t.IsInstrument && !t.IsReturn && !t.IsGroup;
        Control row2 = btnRow;
        if (isAudioTrack || t.IsInstrument)
        {
            // Audio tracks: a monitor chip right of the input selector hears that input live.
            var monitor = isAudioTrack ? MonitorToggle(t.Id) : null;
            var combo = isAudioTrack ? BuildInputCombo(t.Id, monitor) : BuildMidiSourceCombo(t.Id);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 5, 0, 0), ColumnSpacing = 3};
            btnRow.Margin = new Thickness(0);
            Grid.SetColumn(btnRow, 0);
            Grid.SetColumn(combo, 1);
            g.Children.Add(btnRow);
            g.Children.Add(combo);
            if (monitor is not null)
            {
                combo.Margin = new Thickness(0);
                monitor.Chip.Margin = new Thickness(0, 0, 12, 0);   // the row's right inset moves to the chip
                Grid.SetColumn(monitor.Chip, 2);
                g.Children.Add(monitor.Chip);
            }
            row2 = g;
        }

        // Row 3: volume (mini fader + dB readout) and a bipolar pan bar in one row —
        // grid 2× volume / 1× pan. Double-click either to reset (0 dB / centre).
        int volTrackId = t.Id;   // M9-C: record volume moves while playing
        var fader = new MiniFader(t.Volume) { VerticalAlignment = VerticalAlignment.Center, Default = 1.0 };
        var db = new TextBlock
        {
            FontSize = 9, Classes = { "Mono" }, Foreground = Brush("Brush.TextTertiary"),
            VerticalAlignment = VerticalAlignment.Center, Width = 30, TextAlignment = TextAlignment.Right,
            Margin = new Thickness(6, 0, 0, 0),
        };
        void ShowDb(double v) => db.Text = v <= 0.0011 ? "−∞"
            : AudioMath.LinToDb(v).ToString("0.0", NotaNum.Culture);
        ShowDb(t.Volume);
        fader.ValueChanged += v => { _engine?.SetTrackVolume(volTrackId, (float)v); ShowDb(v); };
        fader.GestureBegin += () => _engine?.BeginAutomationWrite(volTrackId, AutomationTarget.Volume, -1, -1, "");
        fader.GestureEnd   += () => _engine?.EndAutomationWrite(volTrackId, AutomationTarget.Volume, -1, -1, "");
        _volFaders[t.Id] = fader; _volDb[t.Id] = db;

        var volCell = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(fader, 0);
        Grid.SetColumn(db, 1);
        volCell.Children.Add(fader);
        volCell.Children.Add(db);

        var pan = new PanBar(t.Pan) { VerticalAlignment = VerticalAlignment.Center };
        pan.PanChanged  += p => _engine?.SetTrackPan(volTrackId, (float)p);
        pan.GestureBegin += () => _engine?.BeginAutomationWrite(volTrackId, AutomationTarget.Pan, -1, -1, "");
        pan.GestureEnd   += () => _engine?.EndAutomationWrite(volTrackId, AutomationTarget.Pan, -1, -1, "");
        _panBars[t.Id] = pan;

        var volRow = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetColumn(volCell, 0);
        Grid.SetColumn(pan, 1);
        volRow.Children.Add(volCell);
        volRow.Children.Add(pan);

        // Indent nested rows so the group hierarchy reads at a glance.
        var stack = new StackPanel { Margin = new Thickness(8 + t.Depth * 14, 6, 8, 6), Children = { nameRow, row2, volRow } };

        var meter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = 8, Margin = new Thickness(0, 6, 3, 6) };
        _meters[t.Id] = meter;
        DockPanel.SetDock(meter, Dock.Right);
        var body = new DockPanel { LastChildFill = true, Children = { meter, stack } };

        // 3px track-colour spine on the left edge.
        var spine = new Border { Width = 3, Background = spineBrush };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3,*") };
        Grid.SetColumn(spine, 0);
        Grid.SetColumn(body, 1);
        grid.Children.Add(spine);
        grid.Children.Add(body);

        var card = new Border
        {
            Width = HeaderW,
            Height = RowHeight,
            ClipToBounds = true,
            Background = selected ? SelHeaderBg : (t.Frozen || t.LiveRole != 0) ? FrozenHeaderBg : t.IsGroup ? Brush("Brush.SurfaceRaised") : Brush("Brush.SurfaceCard"),
            BorderBrush = Brush("Brush.BorderDefault"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = grid,
        };
        // A background import is still filling the track: the disabled look (by colour, not
        // opacity), still clickable so the track can be deleted to cancel.
        if (IsProcessingTrack(t.Id)) card.AttachedToVisualTree += (_, _) => Inactive.Set(card, true, interactive: true);
        _headerCards[t.Id] = (card, name, t.IsGroup, spine, spineBrush);   // for in-place selection repaint
        if (selected) spine.Background = NotaPalette.Accent;
        AttachHeaderGestures(card, t);
        return card;
    }

    // Select / reorder / context-menu gestures shared by a full track card and a slim group bar.
    private void AttachHeaderGestures(Border card, TrackVM t)
    {
        card.PointerPressed += (_, e) =>
        {
            // A click inside the input ComboBox must reach it (and not reselect the track,
            // which would rebuild the header and destroy the combo before its dropdown opens).
            if (e.Source is Visual vsrc && vsrc.FindAncestorOfType<ComboBox>(includeSelf: true) is not null) return;
            if (e.GetCurrentPoint(card).Properties.IsRightButtonPressed) { ShowTrackMenu(card, t.Id); return; }
            // Double-click an instrument track → open its instrument/plugin GUI (if any).
            if (e.ClickCount == 2 && t.IsInstrument)
            {
                try { _engine?.OpenPluginEditor(t.Id, -1); } catch { /* builtin/no editor: no-op */ }
                return;
            }
            // Ctrl/Cmd-click extends the multi-track selection (used to form a group).
            if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed
                && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            {
                ToggleTrackInSelection(t.Id); e.Handled = true; return;
            }
            _selTracks.Clear();   // a plain click resets the multi-selection to just this track
            // Grab the title strip (top row, clear of the M/S/arm chips + faders below) to
            // start a reorder drag; selection is deferred to release so the capture survives
            // (Select rebuilds the headers). Returns aren't reorderable — select them outright.
            if (!t.IsReturn && e.GetCurrentPoint(card).Properties.IsLeftButtonPressed
                && e.GetPosition(card).Y < 24)
            {
                _hdrDragId = t.Id;
                _hdrDragFrom = _tracks.FindIndex(x => x.Id == t.Id);
                _hdrDragging = false;
                _hdrDragStart = e.GetPosition(_headers);
                e.Pointer.Capture(card);
                return;
            }
            Select(t.Id, -1);
        };
        card.PointerMoved += (_, e) =>
        {
            if (_hdrDragId < 0) return;
            var p = e.GetPosition(_headers);
            if (!_hdrDragging && Math.Abs(p.Y - _hdrDragStart.Y) < 5) return;
            _hdrDragging = true;
            card.Cursor = new Cursor(StandardCursorType.SizeAll);
            card.Opacity = 0.55;
            int gap = RowGapAtY(p.Y);
            _hdrDropGap = gap;
            Canvas.SetTop(_trackDropLine, RowTop(gap) - 1);
            _trackDropLine.IsVisible = true;
        };
        card.PointerReleased += (_, e) =>
        {
            if (_hdrDragId < 0) return;
            int id = _hdrDragId;
            bool dragged = _hdrDragging;
            double dropY = e.GetPosition(_headers).Y;
            _hdrDragId = _hdrDragFrom = _hdrDropGap = -1;
            _hdrDragging = false;
            _trackDropLine.IsVisible = false;
            e.Pointer.Capture(null);
            card.Cursor = null;
            card.Opacity = 1.0;
            if (!dragged) { Select(id, -1); return; }   // never crossed the threshold → plain select
            HandleHeaderDrop(id, dropY);
        };
    }

    // Track context menu (Duplicate / Delete). Both go through the engine's
    // snapshot ops, so undo/redo covers them; the whole view refreshes after.
    private static readonly string[] TrackColorNames = { "Drums", "Perc", "Bass", "Keys", "Texture", "FX", "Brass", "Vox", "Return" };
    private static readonly string[] ShadeNames = { "", " (light)", " (dark)" };

    // "Add …" entries shared by the arrangement's track-level menus. Return is left enabled
    // when the buses are full, like the toolbar's + Return — MainWindow says so in the status
    // line rather than the menu going quietly dead.
    private IEnumerable<MenuItem> AddTrackItems()
    {
        MenuItem Item(string header, NewTrackKind kind)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => AddTrackRequested?.Invoke(kind);
            return mi;
        }
        yield return Item("Add instrument track", NewTrackKind.Instrument);
        yield return Item("Add audio track", NewTrackKind.Audio);
        yield return Item("Add return track", NewTrackKind.Return);
    }

    // Right-click on the empty space below the tracks: add a track (the main thing to do
    // down there) or paste a copied one.
    private void ShowEmptyAreaMenu(Control anchor)
    {
        if (_engine is null) return;
        var flyout = new MenuFlyout();
        foreach (var mi in AddTrackItems()) flyout.Items.Add(mi);
        flyout.Items.Add(new Separator());
        var paste = new MenuItem { Header = "Paste track", IsEnabled = _engine.HasTrackClipboard() };
        paste.Click += (_, _) =>
        {
            int nid = _engine.PasteTrack();
            if (nid > 0) Select(nid, -1);
            Refresh();
            SessionChanged?.Invoke();
            TrackSelected?.Invoke(SelTrackId);
        };
        flyout.Items.Add(paste);
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    private void ShowTrackMenu(Control anchor, int trackId)
    {
        if (_engine is null) return;
        var flyout = new MenuFlyout();

        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) => PromptRenameTrack(anchor, trackId);
        var color = BuildColorSubmenu(trackId);

        // Record input ▸ (audio tracks only): hardware, master, or another track's output
        // (internal resampling). The active source is ticked.
        bool isAudio = _tracks.Any(v => v.Id == trackId && !v.IsInstrument && !v.IsReturn);
        MenuItem? recInput = null;
        if (isAudio)
        {
            int cur = _engine.GetTrackRecordInput(trackId);
            recInput = new MenuItem { Header = "Record input" };
            MenuItem Src(string label, int source)
            {
                var mi = new MenuItem { Header = label, ToggleType = MenuItemToggleType.Radio, IsChecked = cur == source };
                mi.Click += (_, _) => _engine.SetTrackRecordInput(trackId, source);
                return mi;
            }
            recInput.Items.Add(Src("Audio input (hardware)", 0));
            recInput.Items.Add(Src("Master", -1));
            recInput.Items.Add(new Separator());
            foreach (var v in _tracks)
                if (v.Id != trackId)
                    recInput.Items.Add(Src(v.Name.Length > 0 ? v.Name : $"Track {v.Id}", v.Id));
            foreach (var v in _returns)
                recInput.Items.Add(Src(v.Name.Length > 0 ? v.Name : $"Return {v.Id}", v.Id));
        }

        // MIDI from ▸ (instrument tracks only): play another instrument track's MIDI through
        // this track's instrument. The current source is ticked.
        bool isInstr = _tracks.Any(v => v.Id == trackId && v.IsInstrument);
        MenuItem? midiFrom = null;
        if (isInstr)
        {
            int curSrc = _engine.GetTrackMidiSource(trackId);
            midiFrom = new MenuItem { Header = "MIDI from" };
            MenuItem Src(string label, int src)
            {
                var mi = new MenuItem { Header = label, ToggleType = MenuItemToggleType.Radio, IsChecked = curSrc == src };
                mi.Click += (_, _) => _engine.SetTrackMidiSource(trackId, src);
                return mi;
            }
            midiFrom.Items.Add(Src("None", -1));
            var others = _tracks.Where(v => v.Id != trackId && v.IsInstrument).ToList();
            if (others.Count > 0) midiFrom.Items.Add(new Separator());
            foreach (var v in others)
                midiFrom.Items.Add(Src(v.Name.Length > 0 ? v.Name : $"Track {v.Id}", v.Id));
        }

        var copy = new MenuItem { Header = "Copy track" };
        copy.Click += (_, _) => _engine.CopyTrack(trackId);
        var cut = new MenuItem { Header = "Cut track" };
        cut.Click += (_, _) =>
        {
            if (!_engine.CopyTrack(trackId)) return;
            _engine.RemoveTrack(trackId);
            if (SelTrackId == trackId) SelTrackId = -1;
            Refresh(); SessionChanged?.Invoke(); TrackSelected?.Invoke(SelTrackId);
        };
        var paste = new MenuItem { Header = "Paste track", IsEnabled = _engine.HasTrackClipboard() };
        paste.Click += (_, _) =>
        {
            int nid = _engine.PasteTrack();
            if (nid > 0) Select(nid, -1);
            Refresh(); SessionChanged?.Invoke(); TrackSelected?.Invoke(SelTrackId);
        };

        var dup = new MenuItem { Header = "Duplicate track" };
        dup.Click += (_, _) =>
        {
            int nid = _engine.DuplicateTrack(trackId);
            if (nid > 0) Select(nid, -1);
            Refresh();
            SessionChanged?.Invoke();
            TrackSelected?.Invoke(SelTrackId);
        };
        var del = new MenuItem { Header = "Delete track" };
        del.Click += (_, _) =>
        {
            _engine.RemoveTrack(trackId);
            if (SelTrackId == trackId) SelTrackId = -1;
            Refresh();
            SessionChanged?.Invoke();
            TrackSelected?.Invoke(SelTrackId);
        };

        // Group / Ungroup (submix). "Group" folds the multi-selection (or this track) into a
        // new group; "Ungroup" dissolves the group this track is/belongs to.
        var group = new MenuItem { Header = "Group tracks" };
        group.Click += (_, _) => GroupTracks(trackId);
        int ungroupId = GroupToUngroupFor(trackId);
        MenuItem? ungroup = null;
        if (ungroupId > 0)
        {
            ungroup = new MenuItem { Header = "Ungroup" };
            ungroup.Click += (_, _) => UngroupGroup(ungroupId);
        }

        // Freeze ▸ state-dependent entries (Freeze / Live Freeze, or Unfreeze / Edit / Flatten).
        var freeze = FreezeMenuItems?.Invoke(trackId) ?? Array.Empty<Control>();

        flyout.Items.Add(rename);
        flyout.Items.Add(color);
        if (recInput is not null) flyout.Items.Add(recInput);
        if (midiFrom is not null) flyout.Items.Add(midiFrom);
        if (freeze.Count > 0)
        {
            flyout.Items.Add(new Separator());
            foreach (var item in freeze) flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        flyout.Items.Add(group);
        if (ungroup is not null) flyout.Items.Add(ungroup);
        var addTrack = new MenuItem { Header = "Add track" };   // submenu: this menu is long already
        foreach (var mi in AddTrackItems()) addTrack.Items.Add(mi);

        flyout.Items.Add(new Separator());
        flyout.Items.Add(addTrack);
        flyout.Items.Add(copy);
        flyout.Items.Add(cut);
        flyout.Items.Add(paste);
        flyout.Items.Add(dup);
        flyout.Items.Add(new Separator());
        flyout.Items.Add(del);
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    // Compact record-input selector for an audio track header: Ext (hardware), Master,
    // or another track / send. Reflects and writes the track's stored input source.
    private ComboBox BuildInputCombo(int trackId, MonitorChip? monitor = null)
    {
        var cb = new ComboBox
        {
            FontSize = 9, Height = 16, MinHeight = 0, Padding = new Thickness(6, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,   // let the item span the box (In left / name right)
            Margin = new Thickness(0, 0, 12, 0),   // a touch shorter than the row
        };
        var sources = new List<int>();
        // "In" pinned left (faded), the source name pinned right, so the name stands out.
        void Add(string value, int src)
        {
            var inTb = new TextBlock { Text = "In", FontSize = 9, Foreground = Brush("Brush.TextDisabled"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(monitor is null ? 6 : 4, 0) };
            var valTb = new TextBlock { Text = value, FontSize = 9, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(monitor is null ? 6 : 4, 0) };
            // The closed box hoists this content out of its item, where it stops following the
            // theme foreground and keeps whatever it was built with — so on the paper variant
            // the name rendered near-white. A NotaPalette brush re-points on a variant change,
            // so setting it locally holds in both.
            valTb.Foreground = Brush("Brush.TextPrimary");
            DockPanel.SetDock(inTb, Dock.Left);
            // MinWidth so the closed selection box (which doesn't stretch its content in
            // Avalonia) still spreads In to the left edge and the name to the right.
            // Narrower with the monitor chip beside it, so the closed box still shows In + name.
            var content = new DockPanel { LastChildFill = true, MinWidth = monitor is null ? 84 : 58, Children = { inTb, valTb } };
            cb.Items.Add(new ComboBoxItem { Content = content, Padding = new Thickness(6, 2), MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            sources.Add(src);
        }
        Add("Ext", 0);
        Add("Master", -1);
        foreach (var v in _tracks) if (v.Id != trackId) Add(v.Name.Length > 0 ? v.Name : $"Track {v.Id}", v.Id);
        foreach (var v in _returns) Add(v.Name.Length > 0 ? v.Name : $"Return {v.Id}", v.Id);
        int cur = _engine?.GetTrackRecordInput(trackId) ?? 0;
        int sel = sources.IndexOf(cur);
        cb.SelectedIndex = sel >= 0 ? sel : 0;
        monitor?.SetSource(sel >= 0 ? cur : 0);
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedIndex < 0 || cb.SelectedIndex >= sources.Count) return;
            _engine?.SetTrackRecordInput(trackId, sources[cb.SelectedIndex]);
            monitor?.SetSource(sources[cb.SelectedIndex]);
        };
        return cb;
    }

    // Input-monitor chip (Monitor "In"): while lit, the audio track plays whatever arrives on
    // its input — hardware, another track or a return — live through its devices and fader,
    // in place of its clips. Master can't be monitored (it would feed back), so the chip
    // fades out and explains itself while Master is the selected input.
    private sealed class MonitorChip
    {
        public required Border Chip { get; init; }
        public required Action<int> SetSource { get; init; }
    }

    private MonitorChip MonitorToggle(int trackId)
    {
        bool state = _engine?.GetTrackMonitor(trackId) ?? false;
        bool usable = true;
        var glyph = new Glyph(GlyphKind.Headphones, 10);
        var chip = new Border
        {
            Width = 18, Height = 16, CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1),
            Child = glyph, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
        };
        void Paint()
        {
            // Engaged: the brass wash + edge of M/S; at rest a neutral chip.
            chip.Background = Brush(state ? "Brush.AccentSubtle" : "Brush.SurfaceRaised");
            chip.BorderBrush = Brush(state ? "Brush.BorderBrass" : "Brush.BorderDefault");
            glyph.Foreground = Brush(state ? "Brush.AccentHover" : "Brush.TextStrong");
            chip.Opacity = usable ? 1.0 : 0.4;
            ToolTip.SetTip(chip, usable
                ? (state ? "Monitoring input — click to stop" : "Monitor input: hear it live through this track")
                : "Master can't be monitored (it would feed back)");
        }
        Paint();
        chip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            state = !state; Paint();
            _engine?.SetTrackMonitor(trackId, state);
        };
        return new MonitorChip { Chip = chip, SetSource = src => { usable = src != -1; Paint(); } };
    }

    // Compact MIDI-input selector for an instrument track header: None, or another instrument
    // track whose MIDI this one receives ("MIDI In"). Mirrors the record-input combo.
    private ComboBox BuildMidiSourceCombo(int trackId)
    {
        var cb = new ComboBox
        {
            FontSize = 9, Height = 16, MinHeight = 0, Padding = new Thickness(6, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 12, 0),
        };
        var sources = new List<int>();
        // "In" pinned left (faded), the source name pinned right.
        void Add(string value, int src)
        {
            var inTb = new TextBlock { Text = "In", FontSize = 9, Foreground = Brush("Brush.TextDisabled"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
            var valTb = new TextBlock { Text = value, FontSize = 9, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0) };
            // The closed box hoists this content out of its item, where it stops following the
            // theme foreground and keeps whatever it was built with — so on the paper variant
            // the name rendered near-white. A NotaPalette brush re-points on a variant change,
            // so setting it locally holds in both.
            valTb.Foreground = Brush("Brush.TextPrimary");
            DockPanel.SetDock(inTb, Dock.Left);
            var content = new DockPanel { LastChildFill = true, MinWidth = 84, Children = { inTb, valTb } };
            cb.Items.Add(new ComboBoxItem { Content = content, Padding = new Thickness(6, 2), MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            sources.Add(src);
        }
        Add("None", -1);
        foreach (var v in _tracks) if (v.Id != trackId && v.IsInstrument) Add(v.Name.Length > 0 ? v.Name : $"Track {v.Id}", v.Id);
        int cur = _engine?.GetTrackMidiSource(trackId) ?? -1;
        int sel = sources.IndexOf(cur);
        cb.SelectedIndex = sel >= 0 ? sel : 0;
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedIndex >= 0 && cb.SelectedIndex < sources.Count)
                _engine?.SetTrackMidiSource(trackId, sources[cb.SelectedIndex]);
        };
        return cb;
    }

    // Colour ▸ one submenu per base hue, each with normal / light / dark shades shown as
    // swatches, plus Auto. Picking sets the track's stored palette index.
    private MenuItem BuildColorSubmenu(int trackId)
    {
        var color = new MenuItem { Header = "Color" };
        for (int b = 0; b < PaletteBases; b++)
        {
            var baseItem = new MenuItem { Header = TrackColorNames[b], Icon = Swatch(b * PaletteShades) };
            for (int s = 0; s < PaletteShades; s++)
            {
                int ci = b * PaletteShades + s;
                var shade = new MenuItem { Header = TrackColorNames[b] + ShadeNames[s], Icon = Swatch(ci) };
                shade.Click += (_, _) => { _engine?.SetTrackColorIndex(trackId, ci); Refresh(); SessionChanged?.Invoke(); };
                baseItem.Items.Add(shade);
            }
            color.Items.Add(baseItem);
        }
        var autoColor = new MenuItem { Header = "Auto" };
        autoColor.Click += (_, _) => { _engine?.SetTrackColorIndex(trackId, -1); Refresh(); SessionChanged?.Invoke(); };
        color.Items.Add(new Separator());
        color.Items.Add(autoColor);
        return color;
    }

    // A small rounded colour swatch for the given palette index (menu icon).
    private static Control Swatch(int colorIndex)
        => new Border { Width = 13, Height = 13, CornerRadius = NotaRadius.Badge,
                        Background = TrackBrush(colorIndex) };

    // Inline rename popup for a track header (Enter commits, Esc cancels).
    private void PromptRenameTrack(Control anchor, int trackId)
    {
        if (_engine is null) return;
        var box = new TextBox { Text = _engine.GetTrackName(trackId), Width = 180, FontSize = 12 };
        var flyout = new Flyout { Content = box, Placement = PlacementMode.Bottom };
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) { _engine.SetTrackName(trackId, box.Text ?? ""); Refresh(); flyout.Hide(); ke.Handled = true; }
            else if (ke.Key == Key.Escape) { flyout.Hide(); ke.Handled = true; }
        };
        flyout.ShowAt(anchor);
        Dispatcher.UIThread.Post(() => { box.SelectAll(); box.Focus(); }, DispatcherPriority.Input);
    }

    // MIDI/AUDIO/GROUP kind tag: a quiet mono label, not a chip. It names the row's kind,
    // which is reference information — a border would give it the weight of a control (1a).
    private Control TypeTag(string text)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 8, FontWeight = FontWeight.Medium, Classes = { "Mono" },
            Foreground = Brush("Brush.TextDisabled"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        return tb;
    }

    // A small 18×16 stateful chip toggle (M/S/●). Danger variant reds when active.
    // Record-arm, drawn to the almanac's record rule: at rest a neutral chip with a red
    // disc; armed, solid record red with a pale disc.
    private Control ArmToggle(bool initial, Action<bool> onChanged)
    {
        bool state = initial;
        var disc = new Glyph(GlyphKind.Record, 7);
        var chip = new Border { Width = 18, Height = 16, CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1), Child = disc, Cursor = new Cursor(StandardCursorType.Hand) };
        void Paint()
        {
            chip.Background = state ? NotaPalette.Record : NotaPalette.SurfaceRaised;
            chip.BorderBrush = state ? NotaPalette.Record : NotaPalette.BorderDefault;
            disc.Foreground = state ? NotaPalette.RecordInk : NotaPalette.Record;
        }
        Paint();
        chip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
            e.Handled = true; state = !state; Paint(); onChanged(state);
        };
        return chip;
    }

    private Control ChipToggle(string text, bool initial, bool danger, Action<bool> onChanged)
    {
        bool state = initial;
        var tb = new TextBlock
        {
            Text = text, FontSize = 9, FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var chip = new Border
        {
            Width = 18, Height = 16, CornerRadius = NotaRadius.Control,
            BorderThickness = new Thickness(1), Child = tb,
        };
        void Paint()
        {
            if (state)
            {
                // Engaged (mute / solo): brass edge and brass ink on the wash.
                chip.Background = Brush(danger ? "Brush.Record" : "Brush.AccentSubtle");
                chip.BorderBrush = Brush(danger ? "Brush.Record" : "Brush.BorderBrass");
                tb.Foreground = Brush(danger ? "Brush.RecordInk" : "Brush.AccentHover");
            }
            else
            {
                chip.Background = Brush("Brush.SurfaceRaised");
                chip.BorderBrush = Brush("Brush.BorderDefault");
                tb.Foreground = Brush("Brush.TextStrong");
            }
        }
        Paint();
        chip.PointerPressed += (_, e) => { e.Handled = true; state = !state; Paint(); onChanged(state); };
        return chip;
    }

    // ---- Return / Master pinned footer -----------------------------------
    private void RebuildFooterHeaders()
    {
        _footerHeaders.Children.Clear();
        double y = 0;
        foreach (var r in _returns)
        {
            var row = BuildSlimRow(r.Name, "", r.ColorIndex, r.Id, "Brush.SurfaceCard");
            Canvas.SetLeft(row, 0);
            Canvas.SetTop(row, y);
            _footerHeaders.Children.Add(row);
            y += FooterRowH;
        }
        // Master row: selectable (opens the master effect chain) via the reserved id.
        int masterId = _engine?.MasterTrackId ?? -1;
        string masterName = _engine is { } me2 && me2.GetTrackName(masterId) is { Length: > 0 } mn ? mn : "Master";
        var master = BuildSlimRow(masterName, "", -1, masterId, "Brush.SurfaceRaised", masterSpine: true);
        Canvas.SetLeft(master, 0);
        Canvas.SetTop(master, y);
        _footerHeaders.Children.Add(master);
    }

    private Control BuildSlimRow(string title, string sub, int colorIndex, int trackId, string bgKey, bool masterSpine = false)
    {
        var spineBrush = masterSpine ? NotaPalette.Accent : ClipColors(colorIndex).content;
        var name = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush("Brush.TextPrimary"), VerticalAlignment = VerticalAlignment.Center };
        var db = new TextBlock { Text = masterSpine ? "0.0" : "−∞", FontSize = 9, Classes = { "Mono" }, Foreground = Brush("Brush.TextTertiary"), VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(db, 1);
        row.Children.Add(name);
        row.Children.Add(db);

        var spine = new Border { Width = 3, Background = spineBrush };
        var content = new Border { Padding = new Thickness(8, 0), Child = row };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3,*") };
        Grid.SetColumn(spine, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(spine);
        grid.Children.Add(content);

        bool sel = trackId > 0 && trackId == SelTrackId;
        if (sel) spine.Background = NotaPalette.Accent;   // selected: brass stripe, name stays Ink 1
        var card = new Border
        {
            Width = HeaderW, Height = FooterRowH, ClipToBounds = true,
            Background = sel ? SelHeaderBg : Brush(bgKey),
            BorderBrush = Brush("Brush.BorderDefault"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = grid,
        };
        if (trackId > 0)
            card.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(card).Properties.IsRightButtonPressed) { ShowFooterMenu(card, trackId); return; }
                Select(trackId, -1);
            };
        return card;
    }

    // Context menu for a send/return or the master row: rename + colour (the same edits
    // regular tracks get; returns/master have no clips to copy/duplicate).
    private void ShowFooterMenu(Control anchor, int trackId)
    {
        if (_engine is null) return;
        var flyout = new MenuFlyout();
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) => PromptRenameTrack(anchor, trackId);
        flyout.Items.Add(rename);
        flyout.Items.Add(BuildColorSubmenu(trackId));
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    private static IBrush Brush(string key) => (IBrush?)NotaPalette.ByKey(key) ?? NotaPalette.TextPrimary;

    // Small bordered "−"/"+" zoom chip (HANDOFF 1b ruler: 11px, bordered, radius 4).
    private static Button ZoomChip(string text) => new()
    {
        Content = text, Width = 24, Height = 18, MinWidth = 0, FontSize = 11,
        Padding = new Thickness(0),
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    // Fixed-width header-column chrome (SurfaceCard + right divider).
    private Border HeaderPanel(Control? child)
    {
        var b = new Border { Width = HeaderW, BorderThickness = new Thickness(0, 0, 1, 0), Child = child };
        b.BindResource(Border.BackgroundProperty, "Brush.SurfaceCard");
        b.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        return b;
    }

    // Power-curve shaping, mirroring native AutomationLane::shape (M9-D).
    internal static double Shape(double t, float curve)
        => curve == 0f ? t : Math.Pow(t, Math.Pow(2.0, -curve * 4.0));

    // ---- data colours (mirror the "Ember Graphite" tokens in NotaTheme.axaml) --
    private static readonly IBrush LaneBgA = NotaPalette.BgSunken; // the canvas is sunken, not the app ground
    private static readonly IBrush LaneBgB = NotaPalette.LaneB; // Brush.LaneB
    private static readonly IBrush ChromeBg = NotaPalette.BgSunken; // Brush.BgSunken
    private static readonly IPen BeatPen = new Pen(NotaPalette.GridBeat, 1); // Brush.GridBeat
    private static readonly IPen BarPen = new Pen(NotaPalette.GridBar, 1);   // Brush.GridBar
    // Playhead: a 1px brass line with a 7×5 flag on the ruler, no glow (almanac § timeline).
    // The brush is the palette slot itself — a SolidColorBrush built from its Color froze the
    // playhead in one theme.
    private static readonly IBrush PlayheadBrush = NotaPalette.Accent;
    private static readonly IPen PlayheadPen = new Pen(PlayheadBrush, 1);
    // Selection edges a clip with 1px brass — a state never thickens a border to 2px.
    private static readonly IPen ClipSelBorder = new Pen(NotaPalette.AccentBright, 1);
    private static readonly IBrush EdgeHighlight = NotaPalette.MarkerHot; // resize-edge affordance
    private static readonly IBrush InactiveVeil = NotaPalette.Wash(NotaPalette.Veil, 0xB0); // deactivated-clip scrim
    private static readonly IBrush RulerText = NotaPalette.TextSecondary; // Brush.TextSecondary
    // Drag position tooltip (req 2.9): dark pill + bright text near the cursor.
    private static readonly IBrush TooltipBg = NotaPalette.Wash(NotaPalette.TooltipBg, 0xDC);
    private static readonly IBrush TooltipText = NotaPalette.TooltipText;
    // Selected-track highlight: just a soft brass wash on the lane row — no edge
    // lines, so it never fights the grid. Header uses a warm tinted background.
    private static readonly IBrush SelWash = NotaPalette.Wash(NotaPalette.Accent, 0x20); // Accent @ ~12%
    // A group's lane is chrome between its children, not another lane to scan.
    internal static readonly IBrush GroupLaneBg = NotaPalette.Wash(NotaPalette.BgSunken, 0xE0);
    // Plate behind a clip's name so it survives a dense waveform without a full-width strip.
    internal static readonly IBrush LabelPlate = NotaPalette.Wash(NotaPalette.BgSunken, 0x99);
    // Browser drag-over: the lane the drop would land on glows (bright accent wash + edge).
    private static readonly IBrush DropWash = NotaPalette.Wash(NotaPalette.AccentBright, 0x28);
    private static readonly IBrush DropEdge = NotaPalette.Wash(NotaPalette.AccentBright, 0xC0);
    internal int DropTrackIndex = -1;   // -1 = no drag over; set by OnLaneDragOver, drawn by LaneControl
    // In-progress audio take (M-fix): audio clips only materialise on stop, so a
    // translucent red region grows from the take start to the playhead as feedback.
    private static readonly IBrush RecFill = NotaPalette.Wash(NotaPalette.Record, 0x33);
    private static readonly IPen   RecBorder = new Pen(NotaPalette.Wash(NotaPalette.Record, 0xC0), 1.5);
    private static readonly IBrush RecText = NotaPalette.Record;   // a take being recorded: the record red
    // Live capture waveform inside the growing take region — a brighter red so it
    // reads clearly against the translucent RecFill.
    private static readonly IBrush RecWave = NotaPalette.Wash(NotaPalette.Record, 0xE0);
    // Marquee rubber-band (multi-select): accent wash + accent border.
    private static readonly IBrush MarqueeFill = NotaPalette.Wash(NotaPalette.Marker, 0x28);
    private static readonly IPen   MarqueePen = new Pen(NotaPalette.Wash(NotaPalette.Marker, 0xC0), 1);

    // Loop region: the transport-accent hue (AccentBright #F0C060). A solid brace
    // fills the ruler strip; a faint wash + edge lines mark the span over the lanes.
    private static readonly IBrush LoopBrace = NotaPalette.Wash(NotaPalette.AccentBright, 0x66);
    private static readonly IBrush LoopBand = NotaPalette.Wash(NotaPalette.AccentBright, 0x14);
    private static readonly IPen   LoopEdge = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0xA0), 1);
    // Automation overlay (M9-A3): a scrim mutes the clip content, a dashed line marks
    // the flat baseline of an empty lane.
    private static readonly IBrush AutoScrim = NotaPalette.Wash(NotaPalette.BgSunken, 0xC0);
    private static readonly IPen AutoBasePen = new Pen(NotaPalette.TextTertiary, 1)
        { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
    // Write-arm REC button (M9-C): red dot + "REC", lit when armed.
    // Hover feedback (M9-D): brass-accent brighter line + point ring under the cursor.
    private static readonly IPen   AutoHoverPen = new Pen(PlayheadBrush, 2.6);
    private static readonly IPen   AutoHoverRing = new Pen(PlayheadBrush, 1.5);
    // Automation range selection (Phase 2): teal band + edges over the picked time span,
    // distinct from the brass loop band so the two never read as the same thing.
    private static readonly IBrush AutoSelBand = NotaPalette.Wash(NotaPalette.Ink("#4CC2B0"), 0x30);
    private static readonly IPen   AutoSelEdge = new Pen(NotaPalette.Wash(NotaPalette.Ink("#4CC2B0"), 0xC0), 1);
    private static readonly IBrush SelHeaderBg = NotaPalette.SelHeaderBg; // warm accent-tinted raised surface
    // Freeze (M7): cool ice tint for a frozen track's header + its snowflake glyph.
    private static readonly IBrush FrozenHeaderBg = NotaPalette.FrozenHeaderBg;
    private static readonly IBrush FrozenAccent   = NotaPalette.Frozen;

    // Track palette (mirror Brush.Track1..8 + ReturnA/B), each base offered in 3 shades
    // (normal / lighter / darker) so a colour index is base*Shades + shade. Return buses
    // sit at ReturnColorBase..; clips derive fill @16%, border @50%, strip @20%, content full.
    internal const int PaletteShades = 3;
    internal static readonly int PaletteBases = NotaPalette.TrackColors.Length;   // 8
    internal static readonly int ReturnColorBase = PaletteBases * PaletteShades;  // 24
    /// <summary>Auto colour index (shade 0) for the Nth non-return track.</summary>
    internal static int AutoTrackColorIndex(int nth) => (nth % PaletteBases) * PaletteShades;
    /// <summary>Colour index for a return bus (0/1).</summary>
    internal static int ReturnColorIndex(int returnIdx) => ReturnColorBase + (Math.Max(0, returnIdx) % NotaPalette.ReturnColors.Length);

    /// <summary>The effective palette index for a track: its stored colour if set, else its
    /// group's hue (shaded per sibling), else the automatic colour for its position. Shared by
    /// every view so a user-picked colour shows consistently in the arrangement, mixer and
    /// detail — and so a grouped session reads as a handful of families rather than one
    /// unrelated hue per track (design 1a).</summary>
    public static int EffectiveColorIndex(IAudioEngine eng, int trackId)
        => EffectiveColorIndex(eng, trackId, 0);

    private static int EffectiveColorIndex(IAudioEngine eng, int trackId, int depth)
    {
        int stored = eng.GetTrackColorIndex(trackId);
        if (stored >= 0) return stored;
        if (depth < 8 && GroupShadeFor(eng, trackId, depth) is { } inherited) return inherited;
        int nth = 0;
        for (int i = 0; i < eng.TrackCount; i++)
        {
            if (!eng.TryGetTrackInfo(i, out var ti)) continue;
            if (ti.IsReturn) { if (ti.Id == trackId) return ReturnColorIndex(eng.TrackReturnIndex(ti.Id)); continue; }
            if (ti.Id == trackId) return AutoTrackColorIndex(nth);
            nth++;
        }
        return 0;
    }

    // An uncoloured member of a group takes the group's base hue; siblings walk the three
    // shades of that base so they stay apart without leaving the family. Nested groups inherit
    // the same way (depth-guarded — a malformed parent chain must not hang the paint).
    private static int? GroupShadeFor(IAudioEngine eng, int trackId, int depth)
    {
        int groupId = -1;
        for (int i = 0; i < eng.TrackCount; i++)
        {
            if (!eng.TryGetTrackInfo(i, out var ti) || ti.Id != trackId) continue;
            if (ti.IsReturn) return null;
            groupId = ti.GroupId;
            break;
        }
        if (groupId < 0) return null;
        int parent = EffectiveColorIndex(eng, groupId, depth + 1);
        if (parent >= ReturnColorBase) return null;
        int nth = 0;
        for (int i = 0; i < eng.TrackCount; i++)
        {
            if (!eng.TryGetTrackInfo(i, out var ti) || ti.GroupId != groupId) continue;
            if (ti.Id == trackId) break;
            nth++;
        }
        return (parent / PaletteShades) * PaletteShades + nth % PaletteShades;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    /// <summary>The track colour for a palette index (shared with the clip editor, 1e).</summary>
    public static Color TrackColorForIndex(int idx)
    {
        if (idx >= ReturnColorBase)
            return NotaPalette.ReturnColors[(idx - ReturnColorBase) % NotaPalette.ReturnColors.Length];
        idx = ((idx % ReturnColorBase) + ReturnColorBase) % ReturnColorBase;
        var baseColor = NotaPalette.TrackColors[(idx / PaletteShades) % PaletteBases];
        return (idx % PaletteShades) switch
        {
            1 => Mix(baseColor, NotaPalette.ShadeUp.Color, 0.24),    // lighter
            2 => Mix(baseColor, NotaPalette.ShadeDown.Color, 0.30),  // darker
            _ => baseColor,
        };
    }

    // Clip brushes are alpha-derived from a track colour rather than being a slot, so they
    // are registered as derivations: the cache survives a variant change and re-tints with it,
    // which also keeps an already-open clip editor (handed `content`) honest.
    private static readonly Dictionary<int, (IBrush fill, IPen border, IBrush strip, IBrush content)> _clipColors = new();
    private static (IBrush fill, IPen border, IBrush strip, IBrush content) ClipColors(int idx)
    {
        if (_clipColors.TryGetValue(idx, out var v)) return v;
        v = (ClipTint(idx, 0.16), new Pen(ClipTint(idx, 0.50), 1), ClipTint(idx, 0.20),
             NotaPalette.Derived(() => TrackColorForIndex(idx)));
        _clipColors[idx] = v;
        return v;
    }
    /// <summary>The theme-following brush for a palette index. A view that wants a track's
    /// colour takes this; wrapping <see cref="TrackColorForIndex"/> in a new brush freezes it
    /// in the variant that was active when the view was built.</summary>
    public static SolidColorBrush TrackBrush(int idx) => (SolidColorBrush)ClipColors(idx).content;

    /// <summary>A cached, theme-following tint of a palette index. Registers a derivation,
    /// so only call it behind a per-index cache — never per frame; <see cref="Alpha"/> is the
    /// throwaway form for a Render pass.</summary>
    private static SolidColorBrush ClipTint(int idx, double a) => NotaPalette.Derived(() =>
    {
        var c = TrackColorForIndex(idx);
        return Color.FromArgb((byte)(a * 255), c.R, c.G, c.B);
    });

    private static IBrush Alpha(Color c, double a) => new SolidColorBrush(Color.FromArgb((byte)(a * 255), c.R, c.G, c.B));

    private double BeatToX(double beat) => (beat - _scrollBeats) * _pixelsPerBeat;
}
