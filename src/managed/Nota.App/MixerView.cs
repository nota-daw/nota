// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Mixer (mockup 1f): one strip per track (+ returns + master): name with a 2px colour
// top, I/O rows, A/B sends, a pan knob, a vertical fader with a stereo meter, a dB
// read-out and M/S/Arm. All strips ride the existing engine ops (vol/pan/mute/solo/send/
// meter); meters are pushed each UI tick by MainWindow, which hosts this one instance
// docked under the main view or in the Mixer window (specs/054). Strips fit the height
// they get (I/O, then sends hide, then the fader shortens). Crossfader is M7+. The footer
// also shows and switches the project pan law (specs/006).
//
// Groups (specs/070): strips follow the Arrangement's hierarchy — a group strip, then its
// members — under a named, tinted band per group (GroupBandsLayer). A group folds from the
// triangle on its strip; the fold state is the Arrangement's (GroupFold), so folding hides the
// subtree in both views, and only toggles visibility here. The Mixer follows the selected
// track with an outline. Compact strips (MixerView.Compact.cs) are the same strips at 72 px.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Localization;

namespace Nota.App;

/// <summary>The Mixer window's two views (specs/008).</summary>
public enum MixerViewMode { Strips, StereoMap }

public sealed partial class MixerView : UserControl
{
    private static readonly IBrush Card = NotaPalette.SurfaceCard;
    private static readonly IBrush MasterBg = NotaPalette.SurfaceCard;   // every strip is the panel tone
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush BorderInner = NotaPalette.Rule;       // the rule that divides inside a strip
    private static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush Danger = NotaPalette.Danger;
    private static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush TextDisabled = NotaPalette.TextDisabled;
    private static readonly IBrush OnAccent = NotaPalette.TextOnAccent;

    private readonly IAudioEngine _engine;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(10) };
    private readonly GroupBandsLayer _bands = new();
    // Meters fed each UI tick: the control (to skip hidden strips) and its Push.
    private readonly List<(int trackId, Control ctl, Action<NotaMeter> push)> _meters = new();
    private readonly List<(int trackId, int bus, MiniFader fader)> _sendFaders = new();
    // Sends / I/O sections are collapsible across every strip (mockup 1f header
    // toggles); we keep the section borders so a toggle can hide them all at once.
    private readonly List<Control> _sendSections = new();
    private readonly List<Control> _ioSections = new();
    private bool _sendsVisible = true, _ioVisible = true;

    // Height-adaptive strips (specs/054): the strips fit the height they get, docked or in the
    // window. As it shrinks, I/O rows hide first, then sends, then the fader shortens to
    // FaderMin; below that the host can't get smaller (MinContentHeight). A footer toggle can
    // pin an auto-hidden section back on — the strip row then scrolls vertically.
    // Section heights per layout (specs/070): the height fitting is the same for both.
    private sealed record StripMetrics(double Width, double Header, double Io, double Sends, double Pan,
        double FaderMin, double FaderComfort, double FaderMax, double FaderPad, double Buttons);
    private static readonly StripMetrics Regular = new(154, 24, 34, 42, 38, 72, 120, 190, 16, 28);
    private static readonly StripMetrics CompactMetrics = new(72, 24, 18, 42, 22, 72, 120, 220, 8, 66);
    private StripMetrics M => _compact ? CompactMetrics : Regular;
    private double FaderMax => M.FaderMax;
    private double FaderMin => M.FaderMin;
    private double FaderComfort => M.FaderComfort;   // a section hides only if the fader would get shorter
    private double HeaderH => M.Header;
    private double IoH => M.Io;
    private double SendsH => M.Sends;
    private double PanH => M.Pan;
    private double ButtonsH => M.Buttons;
    private double FaderPad => M.FaderPad;
    private const double RowPad = 20, StripBorder = 2, FooterH = 44;
    private bool _compact;
    private readonly List<Control> _faderGrids = new();
    private double _faderH = Regular.FaderMax;
    private double _dbH = 18;   // the dB read-out's height, measured once built
    private bool _ioAuto, _sendsAuto;        // hidden for lack of height
    private bool _ioPinned, _sendsPinned;    // shown anyway by the user
    private Action? _paintIoToggle, _paintSendsToggle;

    // Each strip's controls that follow the engine, so a change made elsewhere — track
    // headers, Session's mixer sections, automation, undo, a MIDI controller — shows here
    // too. The docked Mixer sits right next to those (specs/054).
    private sealed class StripSync
    {
        public VFader? Fader;
        public MeterFader? MeterFader;   // compact strips
        public DragNumber? Db;
        public PanKnob? Pan;
        public PanBar? PanBar;           // compact strips
        public Action<bool>? Mute, Solo, Arm, Link;   // Link: a group's Link key (specs/076)
        public Action<bool>? Cue;                     // the Cue key (specs/089)
    }
    private readonly Dictionary<int, StripSync> _syncs = new();
    private StripSync SyncOf(int id) => _syncs.TryGetValue(id, out var s) ? s : _syncs[id] = new StripSync();
    // The master volume lives in TransportViewModel.MasterVolume (saved with the project, shown
    // in the MST module and the arrangement's Master row); the master strip goes through these.
    public Func<float>? GetMasterVolume { get; set; }
    public Action<float>? SetMasterVolume { get; set; }
    private VFader? _masterFader;
    private MeterFader? _masterMeterFader;
    private DragNumber? _masterDb;
    // Project pan law (specs/006): the footer's Pan law control, its hint, the Older project
    // badge and a BAL badge on every group strip — all re-synced from the engine, so undo/redo
    // and project load show up without a rebuild.
    public Func<bool>? IsOlderProject { get; set; }
    private Action<int>? _setLawSegment;
    private TextBlock? _lawHint;
    private Control? _olderBadge;
    private readonly List<Control> _balBadges = new();
    private PanLaw? _shownLaw;
    private bool _shownOlder;

    // Strips or the Stereo Map beside a narrower strip column (specs/008). The strips scroller
    // is one instance moved between the two layouts.
    private readonly ScrollViewer _stripsScroller;
    private readonly ContentControl _main = new();
    private MixerViewMode _view = MixerViewMode.Strips;
    /// <summary>The Stereo Map, created the first time the view is shown.</summary>
    public StereoMapView? Map { get; private set; }
    /// <summary>Called once when the map is created, to wire its callbacks.</summary>
    public Action<StereoMapView>? MapCreated { get; set; }

    private int _builtTrackCount = -1;
    private int _builtShape;   // group membership, names and colours the strips were built for

    // One built strip in display order: its place in the group forest and the bits folding,
    // bands and the selection outline touch (specs/070).
    private sealed class StripEntry
    {
        public int Id, Depth, Parent, ColorIndex;
        public bool IsGroup;
        public string Name = "";
        public Border Strip = null!;
        public Border Outline = null!;
        public Action<bool>? PaintFold;             // group strips: the triangle
        public Control? SoloDot, ArmDot, ClipDot;   // group strips: hidden-member state, when folded
        public int[] HiddenIds = Array.Empty<int>();       // a group's descendants (ids)
        public int[] HiddenIndices = Array.Empty<int>();   // … and their engine indices
    }
    private readonly List<StripEntry> _strips = new();
    private readonly Dictionary<int, StripEntry> _stripById = new();
    private int _maxDepth;
    private int _selectedId = -1;
    // Hidden-track clip warnings by group id: a rebuild (Compact, a rename, a regroup) makes new
    // StripEntries but must not drop a warning; only unfolding, the dot or a new project clears it.
    private readonly HashSet<int> _clipLatched = new();

    /// <summary>The fold state shared with the Arrangement (specs/070).</summary>
    internal GroupFold? Fold
    {
        get => _fold;
        set
        {
            if (ReferenceEquals(_fold, value)) return;
            if (_fold is not null) _fold.Changed -= ApplyFold;
            _fold = value;
            if (_fold is not null) _fold.Changed += ApplyFold;
            ApplyFold();
        }
    }
    private GroupFold? _fold;

    /// <summary>Compact strips (specs/070). Switching rebuilds the strips.</summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            if (_compact == value) return;
            _compact = value;
            _paintCompactToggle?.Invoke();
            Refresh();
            MinContentHeightChanged?.Invoke();
        }
    }
    /// <summary>Raised when the user flips the footer's Compact toggle.</summary>
    public event Action<bool>? CompactChanged;
    private Action? _paintCompactToggle;

    /// <summary>Raised after <see cref="Refresh"/> rebuilds the strips, so a host window can
    /// fit its width to them.</summary>
    public event Action? StripsChanged;

    /// <summary>Raised after a group strip's Link key changed the group's Link (specs/076).</summary>
    public event Action? GroupLinkChanged;

    // A group strip's Link key: the chain-link chip of the Arrangement's group header.
    private Border LinkKey(int id, out Action<bool> sync)
    {
        bool on = _engine.GetTrackEditLink(id);
        var glyph = new Glyph(GlyphKind.ChainLink, 10);
        var b = new Border { Width = 20, Height = 17, CornerRadius = NotaRadius.Field, BorderThickness = new Thickness(1), Child = glyph, Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(b, ArrangementView.LinkTip);
        ArrangementView.PaintLinkChip(b, glyph, on);
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (!_engine.SetTrackEditLink(id, !on)) return;
            on = !on; ArrangementView.PaintLinkChip(b, glyph, on);
            GroupLinkChanged?.Invoke();
        };
        sync = v => { if (v == on) return; on = v; ArrangementView.PaintLinkChip(b, glyph, on); };
        return b;
    }

    /// <summary>Raised when <see cref="MinContentHeight"/> changed, so a host can update its
    /// own minimum.</summary>
    public event Action? MinContentHeightChanged;

    /// <summary>The width the strip row wants (all strips + master + margin), unclipped.</summary>
    public double StripsWidth
    {
        get { _row.Measure(Size.Infinity); return _row.DesiredSize.Width; }
    }

    // A strip's always-shown part (header, pan, dB, M/S/Arm) without the fader, plus the group
    // bars above the strips, which always stay (specs/070).
    private double CoreH => BandsH + HeaderH + PanH + FaderPad + _dbH + ButtonsH + StripBorder;
    private double BandsH => _maxDepth * GroupBandsLayer.BarH;

    /// <summary>The smallest height (design units) at which a strip's core still fits — header,
    /// pan, a FaderMin fader, dB, M/S/Arm — plus the footer. Hosts don't go below it.</summary>
    public double MinContentHeight => CoreH + FaderMin + RowPad + FooterH + MapPad;

    /// <summary>The height at which every strip shows in full (I/O, sends, the full fader).</summary>
    public double NaturalContentHeight => CoreH + FaderMax + IoH + SendsH + RowPad + FooterH + MapPad;

    // The Stereo Map layout's grid margin (8 above and below the strip column).
    private const double MapMargin = 8;
    private double MapPad => _view == MixerViewMode.StereoMap ? 2 * MapMargin : 0;

    public MixerView(IAudioEngine engine)
    {
        ValueEntry.SetScope(this, true);   // Tab in a value box walks the values shown here
        _engine = engine;
        _stripsScroller = new ScrollViewer
        {
            Content = new Panel { Children = { _bands, _row } },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            // A short window scrolls the strips rather than cutting off M / S / arm.
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _stripsScroller.SizeChanged += (_, e) => { if (e.HeightChanged) FitHeight(); };
        _row.LayoutUpdated += (_, _) => _bands.InvalidateVisual();   // bands follow the strips' bounds
        var dock = new DockPanel();
        var footer = Footer();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(_main);
        Content = dock;
        ApplyView();
    }

    public MixerViewMode View
    {
        get => _view;
        set { if (_view == value) return; _view = value; ApplyView(); MinContentHeightChanged?.Invoke(); }
    }

    private void ApplyView()
    {
        // Detach the scroller from whichever layout held it before re-parenting.
        if (_stripsScroller.Parent is Panel p) p.Children.Remove(_stripsScroller);
        else if (_stripsScroller.Parent is ContentControl c) c.Content = null;
        if (Map?.Parent is Panel mp) mp.Children.Remove(Map);
        _main.Content = null;

        if (_view == MixerViewMode.Strips)
        {
            _stripsScroller.MaxWidth = double.PositiveInfinity;
            _main.Content = _stripsScroller;
            return;
        }
        if (Map is null) { Map = new StereoMapView(_engine) { MinWidth = 520 }; MapCreated?.Invoke(Map); }
        _stripsScroller.MaxWidth = 440;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(MapMargin) };
        grid.Children.Add(Map);
        Grid.SetColumn(_stripsScroller, 1);
        grid.Children.Add(_stripsScroller);
        _main.Content = grid;
    }

    public void Refresh()
    {
        _row.Children.Clear();
        _meters.Clear();
        _sendFaders.Clear();
        _sendSections.Clear();
        _ioSections.Clear();
        _faderGrids.Clear();
        _syncs.Clear();
        _balBadges.Clear();
        _strips.Clear();
        _stripById.Clear();
        _masterFader = null; _masterMeterFader = null;
        _cueFader = null; _cueMeterFader = null; _cueDb = null; _cueOutput = _cueSources = null;
        _builtCueShown = CueShown();   // Cue keys and the Cue strip (specs/089)
        int returns = _engine.ReturnTrackCount;
        int n = _engine.TrackCount;
        // As in the arrangement: the tracks in its hierarchical order (each group, then its
        // members — specs/070), then the returns, then the master.
        var rows = ArrangementView.HierarchyOrder(_engine);
        _maxDepth = 0;
        foreach (var r in rows)
            if (_engine.TryGetTrackInfo(r.Index, out var gi) && gi.IsGroup) _maxDepth = Math.Max(_maxDepth, r.Depth + 1);
        foreach (var r in rows)
        {
            if (!_engine.TryGetTrackInfo(r.Index, out var ti)) continue;
            AddStrip(ti, r.Depth, r.ParentGroupId, returns);
        }
        foreach (int i in ArrangementView.DisplayOrder(_engine))
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.IsReturn) AddStrip(ti, 0, -1, returns);
        _row.Children.Add(_compact ? CompactMasterStrip() : MasterStrip());
        if (_builtCueShown) _row.Children.Add(_compact ? CompactCueStrip() : CueStrip());
        CollectDescendants(rows);
        _clipLatched.RemoveWhere(id => !_stripById.TryGetValue(id, out var g) || !g.IsGroup);   // a group that is gone
        _row.Margin = new Thickness(10, 10 + BandsH, 10, 10);
        _bands.Top = 10;
        _shownLaw = null;   // the strips are new: repaint the law state onto them
        SyncPanLaw();
        _builtTrackCount = n;
        _builtShape = Shape();
        _builtNames = GroupNames();
        MeasureDb();
        ApplyFold();
        FitHeight();
        MinContentHeightChanged?.Invoke();   // the bar rows may have changed
        StripsChanged?.Invoke();
    }

    private void AddStrip(NotaTrackInfo ti, int depth, int parent, int returns)
    {
        int colorIdx = ArrangementView.EffectiveColorIndex(_engine, ti.Id);
        var c = ArrangementView.TrackBrush(colorIdx);
        var e = new StripEntry { Id = ti.Id, Depth = depth, Parent = parent, IsGroup = ti.IsGroup, ColorIndex = colorIdx, Name = TrackNames.Of(_engine, ti) };
        var body = _compact ? CompactStrip(ti, c, returns, e) : Strip(ti, c, returns, e);
        // The selection outline sits over the strip's content, so selecting never resizes it.
        e.Outline = new Border { BorderBrush = NotaPalette.Accent, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Group, IsHitTestVisible = false, IsVisible = false };
        var inner = (Control)body.Child!;
        body.Child = null;   // detach before re-parenting
        body.Child = new Panel { Children = { inner, e.Outline } };
        e.Strip = body;
        _strips.Add(e);
        _stripById[e.Id] = e;
        _row.Children.Add(body);
    }

    // Each group's descendants (ids and engine indices), for the hidden count and the folded
    // group's solo / arm / clip dots. Built once per Refresh, so the tick allocates nothing.
    private void CollectDescendants(IReadOnlyList<ArrangementView.HierarchyRow> rows)
    {
        foreach (var e in _strips)
        {
            if (!e.IsGroup) continue;
            var ids = new List<int>(); var idx = new List<int>();
            foreach (var r in rows)
                if (IsDescendant(r.Id, e.Id)) { ids.Add(r.Id); idx.Add(r.Index); }
            e.HiddenIds = ids.ToArray();
            e.HiddenIndices = idx.ToArray();
        }
    }

    private bool IsDescendant(int id, int groupId)
    {
        for (int guard = 0; guard < 32 && _stripById.TryGetValue(id, out var e); guard++)
        {
            if (e.Parent == groupId) return true;
            if (e.Parent < 0) return false;
            id = e.Parent;
        }
        return false;
    }

    // A strip is hidden when any group above it is folded.
    private bool HiddenByFold(StripEntry e)
    {
        if (_fold is null) return false;
        int p = e.Parent;
        for (int guard = 0; guard < 32 && p >= 0; guard++)
        {
            if (_fold.IsFolded(p)) return true;
            p = _stripById.TryGetValue(p, out var pe) ? pe.Parent : -1;
        }
        return false;
    }

    /// <summary>Shows / hides strips for the fold state and repaints the bands — no rebuild
    /// (specs/070 R4).</summary>
    private void ApplyFold()
    {
        foreach (var e in _strips)
        {
            e.Strip.IsVisible = !HiddenByFold(e);
            bool folded = e.IsGroup && _fold?.IsFolded(e.Id) == true;
            e.PaintFold?.Invoke(folded);
            if (!folded) _clipLatched.Remove(e.Id);
        }
        var bands = new List<GroupBandsLayer.Band>();
        for (int i = 0; i < _strips.Count; i++)
        {
            var g = _strips[i];
            if (!g.IsGroup || !g.Strip.IsVisible) continue;
            var last = g;
            for (int j = i + 1; j < _strips.Count && IsDescendant(_strips[j].Id, g.Id); j++)
                if (_strips[j].Strip.IsVisible) last = _strips[j];
            int hidden = _fold?.IsFolded(g.Id) == true ? g.HiddenIds.Length : 0;
            bands.Add(new GroupBandsLayer.Band(g.Name, g.ColorIndex, g.Depth, g.Strip, last.Strip, hidden));
        }
        _bands.SetBands(bands);
        FollowSelection(_selectedId, bringIntoView: false);
        UpdateHiddenState();
        StripsChanged?.Invoke();   // the strip row's width changed
    }

    /// <summary>Outlines the selected track's strip and brings it into view; a track hidden by
    /// folding outlines its outermost folded group instead. Never unfolds (specs/070).</summary>
    public void FollowSelection(int trackId) => FollowSelection(trackId, bringIntoView: true);

    private void FollowSelection(int trackId, bool bringIntoView)
    {
        _selectedId = trackId;
        StripEntry? target = null;
        if (_stripById.TryGetValue(trackId, out var e))
        {
            target = e;
            for (int p = e.Parent, guard = 0; p >= 0 && guard < 32 && _stripById.TryGetValue(p, out var pe); p = pe.Parent, guard++)
                if (_fold?.IsFolded(p) == true) target = pe;   // keeps climbing: the outermost wins
        }
        foreach (var s in _strips) s.Outline.IsVisible = ReferenceEquals(s, target);
        if (bringIntoView) target?.Strip.BringIntoView();
    }

    // Group membership, names and colours: a change rebuilds the strips so the bands follow
    // moves into / out of groups, renames, recolours and undo (FR-004). Names cross the C ABI
    // as new strings, so they are read every NameEvery ticks rather than on each one.
    private const int NameEvery = 15;
    private int _shapeTick, _builtNames;
    private int Shape()
    {
        var h = new HashCode();
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti)) continue;
            h.Add(ti.Id); h.Add(ti.GroupId); h.Add(ti.IsGroup);
            h.Add(_engine.GetTrackColorIndex(ti.Id));
        }
        return h.ToHashCode();
    }

    private int GroupNames()
    {
        var h = new HashCode();
        foreach (var e in _strips) if (e.IsGroup) h.Add(TrackNames.Of(_engine, e.Id));
        return h.ToHashCode();
    }

    private bool ShapeChanged()
    {
        if (Shape() != _builtShape) return true;
        if (++_shapeTick < NameEvery) return false;
        _shapeTick = 0;
        return GroupNames() != _builtNames;
    }

    // A folded group's dots: a hidden member is soloed, armed, or peaked at 0 dBFS since the fold.
    private void UpdateHiddenState()
    {
        foreach (var g in _strips)
        {
            if (g.SoloDot is null) continue;
            bool folded = _fold?.IsFolded(g.Id) == true;
            bool solo = false, arm = false;
            if (folded)
            {
                foreach (int idx in g.HiddenIndices)
                    if (_engine.TryGetTrackInfo(idx, out var ti)) { solo |= ti.Soloed != 0; arm |= ti.Armed != 0; }
                if (!_clipLatched.Contains(g.Id))
                    foreach (int id in g.HiddenIds)
                        if (_engine.TryGetTrackMeter(id, out var m) && (m.PeakL >= 1f || m.PeakR >= 1f)) { _clipLatched.Add(g.Id); break; }
            }
            g.SoloDot.IsVisible = solo;
            g.ArmDot!.IsVisible = arm;
            g.ClipDot!.IsVisible = folded && _clipLatched.Contains(g.Id);
        }
    }

    /// <summary>Pushes fresh meter readings to every strip (called at the UI clock).</summary>
    public void UpdateMeters()
    {
        // A track added or removed while the mixer is open gets (or loses) its strip; a track
        // moved into or out of a group, or a group renamed or recoloured, moves its band.
        if (_builtTrackCount >= 0 && (_engine.TrackCount != _builtTrackCount || ShapeChanged())) Refresh();
        foreach (var (id, ctl, push) in _meters)
        {
            if (!ctl.IsEffectivelyVisible) continue;   // a folded strip reads nothing
            if (id == CueMeterId) push(_engine.CueMeter());
            else if (id < 0) push(_engine.MasterMeter());
            else if (_engine.TryGetTrackMeter(id, out var m)) push(m);
        }
        UpdateCue();
        // Follow master volume changes made elsewhere (MST, Master row, Remote, project load).
        if (GetMasterVolume is { } get && _masterDb is { } db)
        {
            double v = get();
            if (_masterFader is { Dragging: false } f && Math.Abs(f.Value - v) > 1e-3) { f.SetValueExternal(v); db.Value = LinToDb(v); }
            if (_masterMeterFader is { Dragging: false } mf && Math.Abs(mf.Value - v) > 1e-3) { mf.SetValueExternal(v); db.Value = LinToDb(v); }
        }
        // Sends follow the Devices panel's Sends card, the Session mixer and automation (#138).
        foreach (var (id, bus, send) in _sendFaders)
        {
            if (send.Dragging) continue;
            double v = _engine.GetTrackSend(id, bus);
            if (Math.Abs(send.Value - v) > 1e-3) send.Value = v;
        }
        FollowStrips();
        UpdateHiddenState();
        SyncPanLaw();   // undo/redo or a load elsewhere may have changed it
    }

    // Volume, pan and M/S/Arm follow the engine (see StripSync); a control being dragged is
    // left to the hand on it.
    private void FollowStrips()
    {
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti) || !_syncs.TryGetValue(ti.Id, out var s)) continue;
            // A strip hidden by folding catches up on the first tick after it is shown again.
            if (_stripById.TryGetValue(ti.Id, out var entry) && !entry.Strip.IsVisible) continue;
            if (s.Fader is { Dragging: false } f && Math.Abs(f.Value - ti.Volume) > 1e-3)
            {
                f.SetValueExternal(ti.Volume);
                if (s.Db is { } db) db.Value = LinToDb(ti.Volume);
            }
            if (s.MeterFader is { Dragging: false } mf && Math.Abs(mf.Value - ti.Volume) > 1e-3)
            {
                mf.SetValueExternal(ti.Volume);
                if (s.Db is { } db) db.Value = LinToDb(ti.Volume);
            }
            if (s.Pan is { Dragging: false } k && Math.Abs(k.Pan - ti.Pan) > 1e-3) k.SetPanExternal(ti.Pan);
            if (s.PanBar is { Dragging: false } pb && Math.Abs(pb.Pan - ti.Pan) > 1e-3) pb.Pan = ti.Pan;
            s.Mute?.Invoke(ti.Muted != 0);
            s.Solo?.Invoke(ti.Soloed != 0);
            s.Arm?.Invoke(ti.Armed != 0);
            s.Cue?.Invoke(_engine.GetTrackCue(ti.Id));
            if (ti.IsGroup) s.Link?.Invoke(_engine.GetTrackEditLink(ti.Id));
        }
    }

    // ---- strips -----------------------------------------------------------

    private Border Strip(NotaTrackInfo ti, IBrush color, int returns, StripEntry entry)
    {
        int id = ti.Id;
        bool isReturn = ti.IsReturn;
        var col = new StackPanel();

        col.Children.Add(Header(TrackNames.Of(_engine, ti), color, Card, ti.IsGroup ? entry : null));

        string io1 = isReturn ? Loc.T("mixer.return_in") : ti.IsGroup ? Loc.T("mixer.group_in") : ti.IsInstrument ? Loc.T("mixer.midi_in") : Loc.T("mixer.in_1");
        string io2 = isReturn ? Loc.T("mixer.master")
                   : ti.IsGroup ? "→ " + (ti.GroupId >= 0 ? TrackNames.Of(_engine, ti.GroupId) : Loc.T("common.master"))
                   : ti.IsInstrument ? Loc.T("mixer.monitor_auto") : Loc.T("mixer.monitor_in");
        // Instrument tracks get a live "MIDI To" selector in place of the static I/O text.
        var ioRow = ti.IsInstrument ? MidiIoRow(id) : IoRow(io1, io2);
        ioRow.IsVisible = IoShown;
        _ioSections.Add(ioRow);
        col.Children.Add(ioRow);

        // A return has no sends; a blank of the same height keeps its fader level with the rest.
        var sends = isReturn ? Blank(SendsH) : SendsRow(id, returns);
        sends.IsVisible = SendsShown;
        _sendSections.Add(sends);
        col.Children.Add(sends);

        col.Children.Add(PanRow(id, ti.Pan, ti.IsGroup));
        col.Children.Add(FaderRow(id, ti.Volume, color, out var db, out var meter));
        _meters.Add((id, meter, meter.Push));
        col.Children.Add(db);
        col.Children.Add(ButtonsRow(id, ti, isReturn));

        return new Border
        {
            Width = 154, Background = Card, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Group, ClipToBounds = true, Child = col,
        };
    }

    private Border MasterStrip()
    {
        var col = new StackPanel();
        col.Children.Add(Header(Loc.T("common.master"), NotaPalette.Master, MasterBg));
        _mainOutputShown = MainOutputText();   // specs/089: the pair Main plays on
        var ioRow = IoRow(Loc.T("mixer.sum_bus"), _mainOutputShown);
        ioRow.IsVisible = IoShown;
        _ioSections.Add(ioRow);
        col.Children.Add(ioRow);

        // Master has no sends or pan: blanks of their height line its fader up with the strips.
        var sends = Blank(SendsH);
        sends.IsVisible = SendsShown;
        _sendSections.Add(sends);
        col.Children.Add(sends);
        col.Children.Add(Blank(38));

        Control db;
        var faderRow = FaderRow(-1, GetMasterVolume?.Invoke() ?? 1f, NotaPalette.Master, out db, out var meter);
        _meters.Add((-1, meter, meter.Push));
        col.Children.Add(faderRow);
        col.Children.Add(db);
        // Master has no sends / pan / arm; keep the strip visually aligned with a
        // spacer where the buttons row sits.
        col.Children.Add(new Border { Height = 28, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 1, 0, 0) });

        return new Border
        {
            Width = 154, Background = MasterBg, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Group, ClipToBounds = true, Child = col,
        };
    }

    private static Border Blank(double height) =>
        new() { Height = height, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1) };

    // ---- footer: section toggles + pan law + crossfader placeholder -------

    private Control Footer()
    {
        var sendsToggle = SectionToggle(Loc.T("common.sends"), () => _sendsVisible, () => _sendsAuto, () => _sendsPinned,
            (on, pinned) => { _sendsVisible = on; _sendsPinned = pinned; FitHeight(); }, out _paintSendsToggle);
        var ioToggle = SectionToggle("I/O", () => _ioVisible, () => _ioAuto, () => _ioPinned,
            (on, pinned) => { _ioVisible = on; _ioPinned = pinned; FitHeight(); }, out _paintIoToggle);
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = Loc.T("common.show"), FontSize = NotaType.Caption, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center }, sendsToggle, ioToggle, CompactToggle() },
        };

        var divider = new Border { Width = 1, Height = 20, Background = BorderDef, VerticalAlignment = VerticalAlignment.Center };
        var leftSide = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center,
            Children = { left, divider, PanLawGroup() },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(leftSide);
        var xf = Crossfader();
        Grid.SetColumn(xf, 2);
        grid.Children.Add(xf);
        return new Border { Height = 44, Background = MasterBg, BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 0), Child = grid };
    }

    // A footer section toggle. On = "show when it fits"; when the section is hidden for lack of
    // height it shows muted, and a click pins it on anyway (the strips then scroll); a click on
    // a shown or pinned section turns it off.
    private Border SectionToggle(string label, Func<bool> on, Func<bool> auto, Func<bool> pinned,
        Action<bool, bool> set, out Action paint)
    {
        var t = new TextBlock { Text = label, FontSize = NotaType.Body, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Height = NotaSize.Chip, Padding = new Thickness(10, 0), CornerRadius = NotaRadius.Group, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Child = t };
        // A chip (style guide): shown is picked, so the soft tint and orange text; hidden sits on
        // the well; auto-hidden for lack of height sits on the well with an orange outline.
        void Paint()
        {
            bool hiddenByHeight = on() && auto() && !pinned();
            bool lit = on() && !hiddenByHeight;
            b.Background = lit ? NotaPalette.AccentSoft : NotaPalette.Well;
            b.BorderBrush = hiddenByHeight ? Brass : Brushes.Transparent;
            t.Foreground = lit ? NotaPalette.AccentBright : hiddenByHeight ? TextTertiary : TextSecondary;
            ToolTip.SetTip(b, hiddenByHeight ? Loc.T("mixer.hidden_not_enough_height_click") : null);
        }
        b.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (on() && auto() && !pinned()) set(true, true);
            else if (on()) set(false, false);
            else set(true, false);
            Paint();
        };
        paint = Paint;
        Paint();
        return b;
    }

    // Compact strips (specs/070): a two-state key painted like the section toggles.
    private Border CompactToggle()
    {
        var t = new TextBlock { Text = Loc.T("common.compact"), FontSize = NotaType.Body, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Height = NotaSize.Chip, Padding = new Thickness(10, 0), CornerRadius = NotaRadius.Group, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Child = t };
        ToolTip.SetTip(b, Loc.T("mixer.show_compact_strips_more_tracks"));
        void Paint()
        {
            b.Background = _compact ? NotaPalette.AccentSoft : NotaPalette.Well;
            b.BorderBrush = Brushes.Transparent;
            t.Foreground = _compact ? NotaPalette.AccentBright : TextSecondary;
        }
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Compact = !_compact;
            CompactChanged?.Invoke(_compact);
        };
        _paintCompactToggle = Paint;
        Paint();
        return b;
    }

    private bool IoShown => _ioVisible && (!_ioAuto || _ioPinned);
    private bool SendsShown => _sendsVisible && (!_sendsAuto || _sendsPinned);

    // The dB read-out's height comes from its font; measure it once there is one, so
    // MinContentHeight matches what is drawn.
    private void MeasureDb()
    {
        if (_masterDb is null) return;
        _masterDb.Measure(Size.Infinity);
        double h = Math.Ceiling(_masterDb.DesiredSize.Height);
        if (h <= 0 || Math.Abs(h - _dbH) < 0.5) return;
        _dbH = h;
        MinContentHeightChanged?.Invoke();
    }

    // Fit the strips to the strip area's height: while the fader would drop below FaderComfort,
    // I/O hides first, then sends; the fader takes the rest between FaderMin and FaderMax. Only toggles visibility / height — no rebuild.
    private void FitHeight()
    {
        // Not laid out yet: show everything the toggles allow; the first size pass refits.
        double h = _stripsScroller.Bounds.Height;
        double avail = h > 0 ? h - RowPad - CoreH : double.PositiveInfinity;
        double sends = _sendsVisible ? SendsH : 0, io = _ioVisible ? IoH : 0;
        double pinnedIo = _ioVisible && _ioPinned ? IoH : 0;
        bool ioFits = avail >= FaderComfort + sends + io;
        bool sendsFits = avail - pinnedIo >= FaderComfort + sends;
        _ioAuto = _ioVisible && !ioFits;
        _sendsAuto = _sendsVisible && !sendsFits;
        double used = (IoShown ? IoH : 0) + (SendsShown ? SendsH : 0);
        double fader = Math.Clamp(Math.Floor(avail - used), FaderMin, FaderMax);

        foreach (var s in _ioSections) s.IsVisible = IoShown;
        foreach (var s in _sendSections) s.IsVisible = SendsShown;
        if (Math.Abs(fader - _faderH) >= 0.5)
        {
            _faderH = fader;
            foreach (var g in _faderGrids) g.Height = fader;
        }
        // A pinned section may not fit: let the strip row scroll then.
        _stripsScroller.VerticalScrollBarVisibility = (_ioPinned && _ioAuto) || (_sendsPinned && _sendsAuto)
            ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        _paintIoToggle?.Invoke();
        _paintSendsToggle?.Invoke();
    }

    // Pan law: the project's pan law as shell segments, a one-line hint, and the Older project
    // badge for projects made before the law existed (they keep Balance until switched).
    private Control PanLawGroup()
    {
        var label = new TextBlock { Text = Loc.T("mixer.pan_law"), FontSize = NotaType.Caption, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var seg = ClipEditorKit.Segments(new[] { Loc.T("mixer.stereo"), Loc.T("mixer.balance") }, _engine.PanLaw == PanLaw.Stereo ? 0 : 1, i =>
        {
            var law = i == 0 ? PanLaw.Stereo : PanLaw.Balance;
            if (_engine.PanLaw != law) _engine.SetPanLaw(law);   // one undo step
            SyncPanLaw();
        }, out var set);
        seg.VerticalAlignment = VerticalAlignment.Center;
        _setLawSegment = set;
        if (seg.Child is Panel buttons)
        {
            string[] tips = { Loc.T("mixer.panning_moves_the_whole_stereo"), Loc.T("mixer.panning_attenuates_the_opposite_channel") };
            for (int i = 0; i < buttons.Children.Count && i < tips.Length; i++) ToolTip.SetTip(buttons.Children[i], tips[i]);
        }
        _lawHint = new TextBlock { FontSize = 10, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        _olderBadge = Tag(Loc.T("mixer.older_project"), 9, 0, 18, Loc.T("mixer.projects_saved_before_stereo_pan"));
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { label, seg, _lawHint, _olderBadge },
        };
    }

    // Repaints the law state (segment, hint, badges) when the engine's law or the
    // older-project mark changed since the last call.
    private void SyncPanLaw()
    {
        var law = _engine.PanLaw;
        bool older = IsOlderProject?.Invoke() == true;
        if (law == _shownLaw && older == _shownOlder) return;
        _shownLaw = law;
        _shownOlder = older;
        bool stereo = law == PanLaw.Stereo;
        _setLawSegment?.Invoke(stereo ? 0 : 1);
        if (_lawHint is not null)
            _lawHint.Text = stereo ? Loc.T("mixer.pan_moves_the_whole_image")
                                   : Loc.T("mixer.pan_attenuates_the_opposite_channel");
        if (_olderBadge is not null) _olderBadge.IsVisible = older && !stereo;
        foreach (var b in _balBadges) b.IsVisible = stereo;
    }

    // A small mono tag in a hairline frame (Older project, BAL), with a tooltip.
    private static Border Tag(string text, double size, double tracking, double height, string tip)
    {
        var t = new TextBlock { Text = text, FontSize = size, LetterSpacing = tracking, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        t.BindResource(TextBlock.FontFamilyProperty, "Font.Measure");
        var b = new Border
        {
            Height = height, Padding = new Thickness(size >= 9 ? 6 : 3, 0), BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Cell, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = t,
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    // Crossfader is not in the engine yet — a disabled A–B value bar with an M7+ badge.
    private Control Crossfader()
    {
        var host = new MiniFader(0.5, 1.0) { Width = 120, VerticalAlignment = VerticalAlignment.Center, Text = _ => Loc.T("mixer.centre") };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, IsEnabled = false,
            Children =
            {
                new TextBlock { Text = "A", FontSize = NotaType.Caption, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center },
                host,
                new TextBlock { Text = "B", FontSize = NotaType.Caption, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { row, new NaBadge { Kind = NaBadgeKind.Future } },
        };
    }

    private Control Header(string name, IBrush topColor, IBrush bg, StripEntry? group = null)
    {
        // The strip header: a 3px bar in the track colour on top (muted grey for master). A group
        // strip leads with its fold triangle and, folded, ends with its hidden members' state.
        var grid = new Grid { RowDefinitions = new RowDefinitions("3,*") };
        grid.Children.Add(new Border { Background = topColor, Height = 3, VerticalAlignment = VerticalAlignment.Top });
        double fs = _compact ? 8.5 : 9, pad = _compact ? 4 : 7;
        var label = new TextBlock { Text = name, FontSize = fs, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(label, name);
        var line = new DockPanel { Margin = new Thickness(pad, 0, pad, 0), LastChildFill = true };
        if (group is not null)
        {
            var fold = FoldKey(group, name);
            DockPanel.SetDock(fold, Dock.Left);
            line.Children.Add(fold);
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
            group.SoloDot = Dot(NotaPalette.Solo, Loc.T("mixer.a_hidden_track_is_soloed"));
            group.ArmDot = Dot(NotaPalette.Record, Loc.T("mixer.a_hidden_track_is_armed"));
            group.ClipDot = Dot(NotaPalette.Danger, Loc.T("mixer.a_hidden_track_clipped_click"));
            group.ClipDot.PointerPressed += (_, e) => { e.Handled = true; _clipLatched.Remove(group.Id); group.ClipDot.IsVisible = false; };
            dots.Children.Add(group.SoloDot); dots.Children.Add(group.ArmDot); dots.Children.Add(group.ClipDot);
            DockPanel.SetDock(dots, Dock.Right);
            line.Children.Add(dots);
        }
        line.Children.Add(label);
        Grid.SetRow(line, 1);
        grid.Children.Add(line);
        return new Border { Height = 24, Background = bg, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    // A group's fold triangle: the Arrangement's disclosure glyph; click, Space or Enter folds the
    // group in both views (GroupFold). View state — no undo step.
    private Control FoldKey(StripEntry group, string name)
    {
        var glyph = new Glyph(GlyphKind.ChevronDown, 8) { Foreground = TextSecondary };
        var key = new Border
        {
            Width = 12, Height = 14, Margin = new Thickness(0, 0, 3, 0), Background = Brushes.Transparent, Focusable = true,
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = glyph,
        };
        void Toggle() { if (_fold is { } f) f.Toggle(group.Id); }
        key.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(key).Properties.IsLeftButtonPressed) return; e.Handled = true; Toggle(); };
        key.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) { e.Handled = true; Toggle(); } };
        group.PaintFold = folded =>
        {
            glyph.Kind = folded ? GlyphKind.ChevronRight : GlyphKind.ChevronDown;
            string tip = folded ? Loc.F("mixer.unfold_group", name) : Loc.F("mixer.fold_group", name);
            ToolTip.SetTip(key, tip);
            Avalonia.Automation.AutomationProperties.SetName(key, tip);
        };
        group.PaintFold(false);
        return key;
    }

    private static Border Dot(IBrush fill, string tip)
    {
        const double size = 6;
        var d = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = fill, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        ToolTip.SetTip(d, tip);
        return d;
    }

    private Control IoRow(string io1, string io2) => new Border
    {
        Height = 34, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 4),
        Child = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = io1, FontSize = 8, Foreground = TextTertiary },
                new TextBlock { Text = io2, FontSize = 8, Foreground = TextTertiary },
            },
        },
    };

    // Instrument-track I/O row: a "MIDI In" selector (None, or another instrument track whose
    // MIDI this one receives) over the label. Writes/reads via the engine.
    private Control MidiIoRow(int id, bool compact = false)
    {
        var cb = new ComboBox
        {
            FontSize = 8, Height = 16, MinHeight = 0, Padding = new Thickness(5, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        if (compact)
        {
            // Plain strings drawn by a template, so the closed box builds a live TextBlock that
            // trims with "…". A TextBlock handed over as the item's Content is shown there as a
            // picture of itself, centred, and a name wider than the box lost both ends (#550).
            // No recycling: the closed box would keep the first TextBlock, whose Text is set once, and
            // go on showing the previous name after another source is picked.
            cb.ItemTemplate = new FuncDataTemplate<string>((label, _) => new TextBlock { Text = label, FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis }, supportsRecycling: false);
            var item = new Style(x => x.OfType<ComboBoxItem>());
            item.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(5, 1)));
            item.Setters.Add(new Setter(Layoutable.MinHeightProperty, 0.0));
            cb.Styles.Add(item);
        }
        // The live input (All inputs / a device) or the track it takes MIDI from (specs/086).
        var instruments = new List<(int, string)>();
        for (int i = 0; i < _engine.TrackCount; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.IsInstrument) instruments.Add((ti.Id, TrackNames.Of(_engine, ti)));
        var (entries, cur) = MidiInputPick.Compact(_engine, id, instruments);
        foreach (var en in entries)
            if (compact) cb.Items.Add(en.Label);
            else cb.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = en.Label, FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis }, Padding = new Thickness(5, 1), MinHeight = 0 });
        cb.SelectedIndex = cur;
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedIndex >= 0 && cb.SelectedIndex < entries.Count)
                MidiInputPick.Apply(_engine, id, entries[cb.SelectedIndex]);
        };
        if (compact)   // one line: the selector alone, its label in the tooltip (specs/070)
        {
            cb.Height = 14;
            cb.Padding = new Thickness(1, 0);   // the arrow column keeps 32 px of the 62; leave the text what is left
            ToolTip.SetTip(cb, Loc.T("mixer.midi_in"));
            return new Border { Height = IoH, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 2), Child = cb };
        }
        return new Border
        {
            Height = 34, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 4),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = Loc.T("mixer.midi_in"), FontSize = 8, Foreground = TextTertiary },
                    cb,
                },
            },
        };
    }

    private Control SendsRow(int id, int returns)
    {
        var body = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        for (int bus = 0; bus < 2; bus++)
        {
            char letter = (char)('A' + bus);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(new TextBlock { Text = letter.ToString(), FontSize = NotaType.Eyebrow, Foreground = TextTertiary, Width = 10, VerticalAlignment = VerticalAlignment.Center });
            if (bus < returns)
            {
                var f = new MiniFader(_engine.GetTrackSend(id, bus), 1.0) { VerticalAlignment = VerticalAlignment.Center, Text = NotaNum.Pct };
                int b = bus;
                SendControl.Attach(f, _engine, id, b);   // undoable, writes automation (specs/072)
                ValueEntry.Attach(f, f.EntryField(Loc.F("common.send", (char)('A' + b)), NotaNum.Pct));
                _sendFaders.Add((id, b, f));
                Grid.SetColumn(f, 1); grid.Children.Add(f);
            }
            else
            {
                var flat = new Border { Height = 3, Background = NotaPalette.TrackOff, CornerRadius = NotaRadius.Field, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(flat, 1); grid.Children.Add(flat);
            }
            body.Children.Add(grid);
        }
        return new Border { Height = 42, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 5), Child = body };
    }

    private Control PanRow(int id, float pan, bool isGroup = false)
    {
        var knob = new PanKnob(pan);
        if (id > 0) SyncOf(id).Pan = knob;
        // One undo step per gesture (specs/009); opened before the automation write so a
        // recorded lane and the value undo together, closed after it.
        var undo = new MixGesture(_engine, MixTarget.Pan(id));
        knob.GestureBegin += undo.Begin;
        Action<double> setPan = p => _engine.SetTrackPan(id, (float)p);
        knob.PanChanged += p => undo.Change(p, setPan);
        ValueEntry.Attach(knob, knob.EntryField(Loc.T("common.pan"), NotaNum.Pan));
        if (id > 0)   // M9-C: record pan moves (master pan isn't an automation target)
        {
            knob.GestureBegin += () => _engine.BeginAutomationWrite(id, AutomationTarget.Pan, -1, -1, "");
            knob.GestureEnd   += () => _engine.EndAutomationWrite(id, AutomationTarget.Pan, -1, -1, "");
            MidiLearn.Bind(knob, MidiTarget.TrackPan(id), "Track Pan");
        }
        knob.GestureEnd += undo.End;
        Control label = Mono(Loc.T("common.pan"), TextTertiary);
        if (isGroup)   // groups keep their unity-centre balance whatever the project law (specs/006)
        {
            var bal = Tag("BAL", 7, 0.84, 11, Loc.T("mixer.groups_keep_a_unity_centre"));
            bal.IsVisible = _engine.PanLaw == PanLaw.Stereo;
            _balBadges.Add(bal);
            label = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { label, bal } };
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { knob, label } };
        return new Border { Height = 38, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
    }

    // Fader travel is 0..1.5 linear (VFader.Max), i.e. up to ~+3.5 dB; the dB
    // read-out is drag-editable and double-click-typeable (HANDOFF §4), kept in
    // two-way sync with the fader.
    private const double MaxLin = 1.5;
    private static readonly double MaxDb = AudioMath.LinToDb(MaxLin);
    // The field's floor is silence: the fader's bottom, a drag to it or a typed "-inf" all read
    // "−∞" (#453), and anything quieter than −60 dB reads −∞ too, as the track headers do.
    private const double MinDb = -60.0;
    private static double LinToDb(double v) => v <= 1e-3 ? MinDb : Math.Clamp(AudioMath.LinToDb(v), MinDb, MaxDb);
    private static double DbToLin(double db) => db <= MinDb + 1e-6 ? 0.0 : Math.Clamp(AudioMath.DbToLin(db), 0, MaxLin);
    private static DragNumber DbField(float vol) => new(LinToDb(vol), MinDb, MaxDb, 0.3, "+0.0;−0.0;0.0", 8)
    {
        Width = 52, HorizontalAlignment = HorizontalAlignment.Center, FloorText = "−∞",
    };

    private Control FaderRow(int id, float vol, IBrush color, out Control db, out MeterBar meter)
    {
        var fader = new VFader(vol, color);
        var dbNum = DbField(vol);
        ToolTip.SetTip(dbNum, Loc.T("mixer.set_the_gain_drag_or"));
        void SetVolume(double v) { if (id < 0) SetMasterVolume?.Invoke((float)v); else _engine.SetTrackVolume(id, (float)v); }
        if (id < 0) { _masterFader = fader; _masterDb = dbNum; }
        else { var sync = SyncOf(id); sync.Fader = fader; sync.Db = dbNum; }
        // One undo step per drag of the fader or the dB number, and per reset / typed value
        // (specs/009). Opened before the automation write, closed after it.
        var undo = new MixGesture(_engine, id < 0 ? MixTarget.Master : MixTarget.Volume(id));
        fader.GestureBegin += undo.Begin;
        dbNum.GestureBegin += undo.Begin;
        Action<double> setVol = SetVolume;
        fader.ValueChanged += v => { undo.Change(v, setVol); dbNum.Value = LinToDb(v); };
        dbNum.ValueChanged += d => { double v = DbToLin(d); undo.Change(v, setVol); fader.SetValueExternal(v); };
        if (id > 0)   // M9-C: record volume moves (master volume isn't a track target)
        {
            fader.GestureBegin += () => _engine.BeginAutomationWrite(id, AutomationTarget.Volume, -1, -1, "");
            fader.GestureEnd   += () => _engine.EndAutomationWrite(id, AutomationTarget.Volume, -1, -1, "");
        }
        fader.GestureEnd += undo.End;
        dbNum.GestureEnd += undo.End;
        MidiLearn.Bind(fader, id > 0 ? MidiTarget.TrackVolume(id) : MidiTarget.MasterVolume, id > 0 ? "Track Volume" : "Master Volume");
        ValueEntry.Attach(fader, fader.EntryField(Loc.T("common.volume"), v => NotaNum.GainDb(v, signed: true), 1.0));
        db = dbNum;

        meter = new MeterBar();   // the almanac stereo meter: 11 + 5 + 11
        var scaleGrid = new Grid { Width = 16, RowDefinitions = new RowDefinitions("*,*,*,*") };
        string[] marks = { "+6", "0", "−12", "−48" };
        for (int i = 0; i < 4; i++)
        {
            var t = new TextBlock { Text = marks[i], FontSize = 7, Foreground = TextDisabled, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = i == 0 ? VerticalAlignment.Top : i == 3 ? VerticalAlignment.Bottom : VerticalAlignment.Center };
            t.BindResource(TextBlock.FontFamilyProperty, "Font.Measure");
            Grid.SetRow(t, i);
            scaleGrid.Children.Add(t);
        }

        var grid = new Grid { Height = _faderH, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(7, 8) };
        grid.Children.Add(scaleGrid);
        Grid.SetColumn(fader, 1); grid.Children.Add(fader);
        // Channels lettered under the meter (almanac § level meters), one letter per 11px column.
        var letters = new Grid { ColumnDefinitions = new ColumnDefinitions($"{MeterScale.ChannelW},{MeterScale.Step},{MeterScale.ChannelW}"), Margin = new Thickness(0, 3, 0, 0) };
        foreach (var (txt, col) in new[] { ("L", 0), ("R", 2) })
        {
            // The letter is wider than its 3px column, so it borrows room either side
            // (a cell clips what it can't fit, which cut R down to an F).
            var t = new TextBlock { Text = txt, FontSize = NotaType.Axis, FontFamily = NotaFonts.ValuesFamily, Foreground = NotaPalette.TextTertiary, TextAlignment = TextAlignment.Center, Margin = new Thickness(-5, 0, -5, 0) };
            Grid.SetColumn(t, col); letters.Children.Add(t);
        }
        DockPanel.SetDock(letters, Dock.Bottom);
        var meterCol = new DockPanel { Margin = new Thickness(4, 0, 0, 0), Children = { letters, meter } };
        Grid.SetColumn(meterCol, 2); grid.Children.Add(meterCol);
        _faderGrids.Add(grid);
        return grid;
    }

    private Control ButtonsRow(int id, NotaTrackInfo ti, bool isReturn)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var sync = SyncOf(id);
        var mute = Toggle("M", false, ti.Muted != 0, v => _engine.SetTrackMute(id, v), out sync.Mute);
        var solo = Toggle("S", false, ti.Soloed != 0, v => _engine.SetTrackSolo(id, v), out sync.Solo);
        MidiLearn.Bind(mute, MidiTarget.TrackMute(id), "Mute");
        MidiLearn.Bind(solo, MidiTarget.TrackSolo(id), "Solo");
        row.Children.Add(mute);
        row.Children.Add(solo);
        if (_builtCueShown) row.Children.Add(CueToggle(id, out sync.Cue));
        if (!isReturn) row.Children.Add(ArmToggle(ti.Armed != 0, (v, additive) => TrackArming.Set(_engine, id, v, additive), out sync.Arm));
        if (ti.IsGroup) { row.Children.Add(LinkKey(id, out var linkSync)); sync.Link = linkSync; }   // group Link (specs/076)
        else sync.Link = null;
        return new Border { Height = 28, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 1, 0, 0), Child = row };
    }

    // Record-arm: neutral with a red disc at rest, solid record red with a pale disc when armed.
    private Border ArmToggle(bool initial, Action<bool, KeyModifiers> set, out Action<bool> sync)   // (armed, modifiers) — #332, specs/076
    {
        bool on = initial;
        var disc = new Glyph(GlyphKind.Record, 7);
        var b = new Border { Width = 20, Height = 17, CornerRadius = NotaRadius.Field, BorderThickness = new Thickness(1), Child = disc };
        ToolTip.SetTip(b, TrackArming.Tip);
        void Paint()
        {
            b.Background = on ? NotaPalette.Record : MasterBg;
            b.BorderBrush = on ? NotaPalette.Record : NotaPalette.Seam;
            disc.Foreground = on ? NotaPalette.RecordInk : NotaPalette.Record;
        }
        b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; e.Handled = true; on = !on; set(on, e.KeyModifiers); Paint(); };
        sync = v => { if (v == on) return; on = v; Paint(); };
        Paint();
        return b;
    }

    private Border Toggle(string label, bool danger, bool initial, Action<bool> set, out Action<bool> sync)
    {
        bool on = initial;
        var t = new TextBlock { Text = label, FontSize = 8.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Width = 20, Height = 17, CornerRadius = NotaRadius.Field, BorderThickness = new Thickness(1), Child = t };
        void Paint()
        {
            // On: mute amber, solo blue, with derived ink.
            var fill = danger ? NotaPalette.Danger : label == "S" ? NotaPalette.Solo : NotaPalette.Mute;
            b.Background = on ? fill : NotaPalette.SurfaceRaised;
            b.BorderBrush = on ? fill : NotaPalette.Seam;
            t.Foreground = on ? NotaPalette.InkOn(fill) : TextSecondary;
        }
        b.PointerPressed += (_, e) => { e.Handled = true; on = !on; set(on); Paint(); };
        sync = v => { if (v == on) return; on = v; Paint(); };
        Paint();
        return b;
    }

    private TextBlock Mono(string text, IBrush fg)
    {
        var t = new TextBlock { Text = text, FontSize = 8, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
        t.BindResource(TextBlock.FontFamilyProperty, "Font.Measure");
        return t;
    }

}
