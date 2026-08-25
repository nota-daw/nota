// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Modular editor (mockup "Modular Editor.dc.html", variant 1a) — a signal-graph
// view of the selected track alongside Arrangement / Session / Mixer. Phase 1:
// the track's existing linear chain (MIDI FX → instrument → insert effects →
// track output) rendered as draggable knob-nodes on a pannable/zoomable canvas
// with a dot grid, audio (Sage) + MIDI (Slate) edges, a minimap and a status
// bar. Knobs write straight through to the engine (same automation/live-follow
// plumbing as the Detail device cards). CV modulation, node reordering and the
// Global view are later phases; their chrome is present but inert.
//
// Signal-type palette (mockup): audio = Sage, MIDI = Slate, CV = Brass.

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
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed class ModularView : UserControl
{
    // ---- palette --------------------------------------------------------
    private static readonly IBrush CanvasBg   = new SolidColorBrush(Color.Parse("#121110"));
    private static readonly IBrush Dot        = NotaPalette.SurfaceRaised;         // #26231E
    private static readonly IBrush Card       = NotaPalette.SurfaceCard;           // #1E1C18
    private static readonly IBrush Sunken     = NotaPalette.BgSunken;              // #100F0D
    private static readonly IBrush Rail       = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush BorderDef   = NotaPalette.BorderDefault;         // #2C2923
    private static readonly IBrush BorderInner = NotaPalette.SurfaceRaised;
    private static readonly IBrush BorderStrong = NotaPalette.BorderStrong;         // #3A362D
    private static readonly IBrush BorderHover = new SolidColorBrush(Color.Parse("#444038"));
    private static readonly IBrush Text1      = NotaPalette.TextPrimary;
    private static readonly IBrush Text2      = NotaPalette.TextSecondary;
    private static readonly IBrush Text3      = NotaPalette.TextTertiary;
    private static readonly IBrush Text4      = NotaPalette.TextDisabled;
    private static readonly IBrush Brass      = NotaPalette.Accent;
    private static readonly IBrush BrassBright = NotaPalette.AccentBright;
    private static readonly IBrush OnAccent   = NotaPalette.TextOnAccent;
    private static readonly IBrush Teal       = NotaPalette.Teal;                  // modulation accent
    private static readonly IBrush Sage       = NotaPalette.Sage;                  // audio
    private static readonly IBrush Slate      = new SolidColorBrush(Color.Parse("#6D8FB5")); // MIDI
    private static readonly IBrush Mauve      = new SolidColorBrush(Color.Parse("#9B7FA6")); // MIDI FX
    private static readonly IBrush Success    = NotaPalette.Success;

    private const double NodeW = 214;
    private const double GapX  = 50;   // horizontal spacing between chained nodes
    private const double PortRow = 20; // height of one port row inside a node's port block

    private readonly IAudioEngine _engine;

    // toolbar / status widgets
    private readonly ToggleButton _trackBtn = Seg("Track", true);
    private readonly ToggleButton _globalBtn = Seg("Global", false);
    private readonly TextBlock _zoomLabel = new() { Text = "100 %", FontSize = 10, Foreground = Text3, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Text = "", FontSize = 10, Foreground = Text2, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statusRight = new() { Text = "", FontSize = 10, Foreground = Text2, VerticalAlignment = VerticalAlignment.Center };

    // track-list sidebar (Track view)
    private readonly StackPanel _sidebar = new();
    private readonly Border _sidebarPanel;

    /// <summary>Raised when a browser item is dropped on the canvas (adds it to the current track).</summary>
    public event Action<BrowserItem>? ItemDropped;
    /// <summary>The track whose graph is shown (Track view); browser drops target it.</summary>
    public int TrackId => _trackId;
    private readonly Border _dropGlow = new()
    {
        BorderBrush = NotaPalette.Accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4),
        Background = NotaPalette.AccentSubtle, IsVisible = false, IsHitTestVisible = false,
    };

    // canvas layers
    private readonly Canvas _world = new();                 // node host (transformed)
    private readonly GridLayer _grid;
    private readonly EdgeLayer _edges;
    private readonly MiniMap _mini;
    private readonly Panel _viewport = new() { ClipToBounds = true, Background = CanvasBg };

    // view transform: screen = world * _scale + _pan
    private double _scale = 1.0;
    private Vector _pan = new(0, 0);

    // pan-drag state
    private bool _panning;
    private Point _panStart;
    private Vector _panOrigin;
    private bool _spaceDown;

    private int _trackId = -1;
    private bool _global;   // Global view (islands) vs Track view (chain graph)
    private readonly List<GNode> _nodes = new();
    private readonly List<GEdge> _edgeList = new();
    private readonly List<Action> _liveKnobs = new();       // per-tick automation follow
    private readonly Dictionary<string, Point> _savedPos = new(); // node id → world pos (session-only)
    private double _pulse;
    private double _cvPhase;

    public ModularView(IAudioEngine engine)
    {
        _engine = engine;
        _grid = new GridLayer(this);
        _edges = new EdgeLayer(this);
        _mini = new MiniMap(this);

        _viewport.Children.Add(_grid);
        _viewport.Children.Add(_edges);
        _viewport.Children.Add(_world);

        // hint + minimap overlays (screen-space, not transformed)
        var hint = new TextBlock
        {
            Text = "Space + drag — pan · ⌘ + wheel — zoom · F — fit",
            FontSize = 9, Foreground = Text4,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(14, 0, 0, 14),
        };
        hint.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        _viewport.Children.Add(hint);
        _viewport.Children.Add(_mini);
        _edgeTip.Child = _edgeTipText;
        _edgeTipText.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        _viewport.Children.Add(_edgeTip);
        _viewport.Children.Add(_dropGlow);

        // Accept instrument/effect/MIDI-FX/sample drops from the browser (targets the current track).
        DragDrop.SetAllowDrop(_viewport, true);
        DragDrop.AddDragOverHandler(_viewport, (_, e) =>
        {
            bool ok = BrowserView.IsAcceptableDrag(e) && _trackId > 0;
            e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            _dropGlow.IsVisible = ok;
        });
        DragDrop.AddDragLeaveHandler(_viewport, (_, _) => _dropGlow.IsVisible = false);
        DragDrop.AddDropHandler(_viewport, (_, e) =>
        {
            _dropGlow.IsVisible = false;
            foreach (var item in BrowserView.DroppedItems(e)) ItemDropped?.Invoke(item);
            e.Handled = true;
        });

        // Middle row: track-list sidebar (Track view) + the canvas.
        var header = new Border
        {
            Height = 26, Child = new TextBlock { Text = "TRACKS", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Text3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) },
            BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1),
        };
        var listCol = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        listCol.Children.Add(header);
        listCol.Children.Add(new ScrollViewer { Content = _sidebar, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        _sidebarPanel = new Border { Width = 176, Background = Rail, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 1, 0), Child = listCol };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        body.Children.Add(_sidebarPanel);
        Grid.SetColumn(_viewport, 1);
        body.Children.Add(_viewport);

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("34,*,22"),
            Children = { Toolbar(), Row(body, 1), StatusBar() },
        };

        _zoomLabel.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        _statusRight.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");

        _viewport.PointerPressed += OnViewportPressed;
        _viewport.PointerMoved += OnViewportMoved;
        _viewport.PointerReleased += OnViewportReleased;
        _viewport.PointerWheelChanged += OnWheel;
        Focusable = true;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
    }

    // ---- public API (mirrors DeviceChainView.Show / MixerView.Refresh) ---

    /// <summary>Point the graph at a track and rebuild it.</summary>
    public void Show(int trackId)
    {
        if (trackId <= 0) return;
        bool changed = trackId != _trackId;
        _trackId = trackId;
        Rebuild();
        RefreshSidebar();
        if (changed) FitToScreen();
    }

    public void Refresh() { if (_global || _trackId > 0) { Rebuild(); RefreshSidebar(); } }

    /// <summary>Raised when an island is opened in the Global view, so the host can select
    /// the track in the arrangement.</summary>
    public event Action<int>? TrackActivated;

    /// <summary>Switch to the Global (islands) view. Public for the debug seed hook.</summary>
    public void ShowGlobal() => SetGlobal(true);

    private void SetGlobal(bool global)
    {
        _trackBtn.IsChecked = !global;
        _globalBtn.IsChecked = global;
        if (_global == global) return;
        _global = global;
        Rebuild();           // island / chain-node keys don't collide, so kept positions survive
        RefreshSidebar();
        FitToScreen();
    }

    private void OpenTrack(int trackId)
    {
        TrackActivated?.Invoke(trackId);
        if (_global) { _trackId = trackId; SetGlobal(false); }
        else Show(trackId);   // already in Track view → just switch the graph
    }

    // ---- track-list sidebar (Track view) --------------------------------
    private void RefreshSidebar()
    {
        _sidebarPanel.IsVisible = !_global;   // collapses in Global view (islands are the tracks)
        if (_global) return;
        _sidebar.Children.Clear();
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && !ti.IsGroup)
                _sidebar.Children.Add(TrackRow(ti.Id, ti));
    }

    private Control TrackRow(int trackId, NotaTrackInfo ti)
    {
        bool active = trackId == _trackId;
        string name = _engine.GetTrackName(trackId);
        if (string.IsNullOrEmpty(name)) name = (ti.IsReturn ? "Return " : ti.IsInstrument ? "Inst " : "Audio ") + trackId;
        var color = new SolidColorBrush(ArrangementView.TrackColorForIndex(ArrangementView.EffectiveColorIndex(_engine, trackId)));
        var row = new Border
        {
            Height = 34, Background = active ? NotaPalette.AccentSubtle : Brushes.Transparent,
            BorderBrush = color, BorderThickness = new Thickness(3, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand), Tag = trackId,   // Tag → cross-track CV drop target
            Child = new TextBlock { Text = name, FontSize = 11, FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal, Foreground = active ? Text1 : Text2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) },
        };
        row.PointerPressed += (_, e) => { OpenTrack(trackId); e.Handled = true; };
        return row;
    }

    // ---- layout persistence (UI-only data; sidecar in the .nota bundle) ----
    private const string LayoutSidecar = "modular-layout.json";

    /// <summary>Persist node/island positions to the bundle sidecar. Runtime keys carry
    /// track ids; on disk they're doc-indices so they survive id reassignment.</summary>
    public void SaveLayout(string bundleDir)
    {
        if (_savedPos.Count == 0) return;
        var outMap = new Dictionary<string, double[]>();
        foreach (var (key, pt) in _savedPos)
            if (ToDocKey(key) is { } dk) outMap[dk] = new[] { pt.X, pt.Y };
        if (outMap.Count == 0) return;
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(bundleDir, LayoutSidecar), System.Text.Json.JsonSerializer.Serialize(outMap)); }
        catch { /* layout is cosmetic — never block a save */ }
    }

    public void LoadLayout(string bundleDir)
    {
        _savedPos.Clear();
        var path = System.IO.Path.Combine(bundleDir, LayoutSidecar);
        if (!System.IO.File.Exists(path)) return;
        try
        {
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double[]>>(System.IO.File.ReadAllText(path));
            if (map == null) return;
            foreach (var (dk, xy) in map)
                if (xy.Length >= 2 && FromDocKey(dk) is { } key) _savedPos[key] = new Point(xy[0], xy[1]);
        }
        catch { /* a corrupt sidecar just means default layout */ }
    }

    private string? ToDocKey(string key)   // "isl:{trackId}" / "{trackId}/{nodeId}" → doc-index form
    {
        if (key.StartsWith("isl:", StringComparison.Ordinal))
            return int.TryParse(key.AsSpan(4), out int tid) && DocIndexOf(tid) is var di && di >= 0 ? $"isl:{di}" : null;
        int s = key.IndexOf('/');
        if (s <= 0 || !int.TryParse(key.AsSpan(0, s), out int t)) return null;
        int d = DocIndexOf(t);
        return d < 0 ? null : $"{d}/{key[(s + 1)..]}";
    }

    private string? FromDocKey(string dk)
    {
        if (dk.StartsWith("isl:", StringComparison.Ordinal))
            return int.TryParse(dk.AsSpan(4), out int di) && TrackIdAt(di) is var tid && tid >= 0 ? $"isl:{tid}" : null;
        int s = dk.IndexOf('/');
        if (s <= 0 || !int.TryParse(dk.AsSpan(0, s), out int di2)) return null;
        int t = TrackIdAt(di2);
        return t < 0 ? null : $"{t}/{dk[(s + 1)..]}";
    }

    private int DocIndexOf(int trackId)
    {
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++) if (_engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return i;
        return -1;
    }
    private int TrackIdAt(int docIndex) => _engine.TryGetTrackInfo(docIndex, out var ti) ? ti.Id : -1;

    /// <summary>Per-UI-tick: follow automation on the knobs and animate the playback pulse.</summary>
    public void Tick(bool playing)
    {
        for (int i = 0; i < _liveKnobs.Count; i++) _liveKnobs[i]();
        _pulse = playing ? (_pulse + 0.06) % 1.0 : 0;
        _cvPhase = (_cvPhase + 0.03) % 1.0;   // CV dash-flow runs even when stopped
        if (_edgeList.Count > 0) _edges.InvalidateVisual();
    }

    internal double Scale => _scale;
    internal Vector Pan => _pan;
    internal IReadOnlyList<GNode> Nodes => _nodes;
    internal IReadOnlyList<GEdge> EdgeList => _edgeList;
    internal double Pulse => _pulse;
    internal double CvPhase => _cvPhase;

    // ---- graph construction --------------------------------------------

    // Layout key for a node's saved position — scoped so "dev0" on different tracks
    // (and islands vs chain nodes) don't collide. Islands carry their track id in SlotIndex.
    private string PosKey(GNode n) => n.Slot == Slot.Island ? $"isl:{n.SlotIndex}" : $"{_trackId}/{n.Id}";

    private void Rebuild()
    {
        // Positions are persisted on drag-end (EndNodeDrag), so no pre-save is needed here.
        _world.Children.Clear();
        _nodes.Clear();
        _edgeList.Clear();
        _liveKnobs.Clear();
        if (_global) { RebuildGlobal(); return; }
        if (_trackId <= 0) { _grid.InvalidateVisual(); _edges.InvalidateVisual(); _mini.InvalidateVisual(); return; }

        bool isInstrument = _engine.TryGetTrackInfo(TrackIndex(_trackId), out var ti) && ti.IsInstrument;
        string trackName = _engine.GetTrackName(_trackId);
        if (string.IsNullOrEmpty(trackName)) trackName = (isInstrument ? "Instrument " : "Audio ") + _trackId;

        // Pre-scan incoming CV links (this track) so each modulated param gets its own input
        // port on its node — built before the nodes so their port blocks know the count.
        _cvTargets.Clear();
        int lc0 = _engine.CvLinkCount(_trackId);
        for (int i = 0; i < lc0; i++)
        {
            int tk = _engine.CvLinkTargetKind(_trackId, i);
            int dev = _engine.CvLinkDevice(_trackId, i);
            int prm = _engine.CvLinkParam(_trackId, i);
            string pname = tk == 1 ? _engine.PluginParamName(_trackId, -1, prm)
                         : tk == 2 ? _engine.MidiEffectParamName(_trackId, dev, prm)
                                   : _engine.DeviceParamName(_trackId, dev, prm);
            var key = (tk, dev);
            if (!_cvTargets.TryGetValue(key, out var list)) { list = new List<string>(); _cvTargets[key] = list; }
            list.Add(pname);
        }

        double x = 40, y = 96;

        GNode? prevAudio = null;   // last node emitting audio (chain source)
        GNode? prevMidi = null;    // last node emitting MIDI

        // 1) MIDI FX chain (instrument tracks only) — before the instrument.
        int mfx = isInstrument ? _engine.TrackMidiEffectCount(_trackId) : 0;
        for (int i = 0; i < mfx; i++)
        {
            var n = MidiEffectNode(i, new Point(x, y));
            AddNode(n);
            if (prevMidi != null) _edgeList.Add(new GEdge(prevMidi, PortKind.Midi, n, PortKind.Midi));
            prevMidi = n;
            x += NodeW + GapX;
        }

        // 2) Source: instrument (instrument track) or a clip/input node (audio track).
        if (isInstrument)
        {
            var inst = InstrumentNode(new Point(x, y), trackName);
            AddNode(inst);
            if (prevMidi != null) _edgeList.Add(new GEdge(prevMidi, PortKind.Midi, inst, PortKind.Midi));
            prevAudio = inst;
            x += NodeW + GapX;
        }
        else
        {
            var src = SimpleNode("input", "Audio in", Sage, new Point(x, y),
                audioIn: false, audioOut: true, midiIn: false, midiOut: false,
                badge: "CLIPS", slot: Slot.Input);
            AddNode(src);
            prevAudio = src;
            x += NodeW + GapX;
        }

        // 3) Insert-effect chain.
        int dc = _engine.TrackDeviceCount(_trackId);
        for (int i = 0; i < dc; i++)
        {
            var n = DeviceNode(i, new Point(x, y));
            AddNode(n);
            if (prevAudio != null) _edgeList.Add(new GEdge(prevAudio, PortKind.Audio, n, PortKind.Audio));
            prevAudio = n;
            x += NodeW + GapX;
        }

        // 4) Track output.
        var outNode = SimpleNode("output", "Track output", Text1, new Point(x, y + 28),
            audioIn: true, audioOut: false, midiIn: false, midiOut: false, badge: "MASTER", slot: Slot.Output);
        AddNode(outNode);
        if (prevAudio != null) _edgeList.Add(new GEdge(prevAudio, PortKind.Audio, outNode, PortKind.Audio));

        // Modulator nodes (Phase 3) — a row below the signal chain. Keyed by mod id so
        // CV links can find their source node.
        var modNodes = new Dictionary<int, GNode>();
        int mc = _engine.ModulatorCount(_trackId);
        double mx = 40, my = y + 300;
        for (int i = 0; i < mc; i++)
        {
            int modId = _engine.ModulatorIdAt(_trackId, i);
            if (modId < 0) continue;
            var mn = ModulatorNode(modId, new Point(mx, my));
            AddNode(mn);
            modNodes[modId] = mn;
            mx += NodeW + GapX;
        }

        // Math input edges: modulator CV-out → a Math node's A/B input port.
        foreach (var mn in modNodes.Values)
        {
            if (mn.IsMathNode)
            {
                int ia = (int)Math.Round(_engine.ModulatorGet(_trackId, mn.ModId, 10));
                int ib = (int)Math.Round(_engine.ModulatorGet(_trackId, mn.ModId, 11));
                if (ia >= 0 && modNodes.TryGetValue(ia, out var sa) && sa != mn)
                    _edgeList.Add(new GEdge(sa, PortKind.Cv, mn, PortKind.Cv) { ToMathInput = 0, ModInputField = 10, Tip = $"{sa.Title} → {mn.Title}.A" });
                if (ib >= 0 && modNodes.TryGetValue(ib, out var sb) && sb != mn)
                    _edgeList.Add(new GEdge(sb, PortKind.Cv, mn, PortKind.Cv) { ToMathInput = 1, ModInputField = 11, Tip = $"{sb.Title} → {mn.Title}.B" });
            }
            else if (mn.IsScopeNode)
            {
                // Scope input edge: modulator CV-out → the scope's single CV-in port.
                int ia = (int)Math.Round(_engine.ModulatorGet(_trackId, mn.ModId, 10));
                if (ia >= 0 && modNodes.TryGetValue(ia, out var sa) && sa != mn)
                    _edgeList.Add(new GEdge(sa, PortKind.Cv, mn, PortKind.Cv) { ModInputField = 10, Tip = $"{sa.Title} → {mn.Title}" });
            }
        }

        // CV links (Phase 3/14): dashed brass edges. Source is a modulator node or,
        // for param → param links, the source device node.
        int lc = _engine.CvLinkCount(_trackId);
        var cvSeen = new Dictionary<(int, int), int>();   // (targetKind,dev) → next input-port index
        for (int i = 0; i < lc; i++)
        {
            int dev = _engine.CvLinkDevice(_trackId, i);
            int tk = _engine.CvLinkTargetKind(_trackId, i);
            GNode? sNode;
            if (_engine.CvLinkSourceKind(_trackId, i) == 1)
                sNode = _nodes.FirstOrDefault(n => n.Slot == Slot.Device && n.SlotIndex == _engine.CvLinkSourceDevice(_trackId, i));
            else
                sNode = modNodes.TryGetValue(_engine.CvLinkSource(_trackId, i), out var mn2) ? mn2 : null;
            if (sNode == null) continue;
            var tNode = tk == 1 ? _nodes.FirstOrDefault(n => n.Slot == Slot.Instrument)
                      : tk == 2 ? _nodes.FirstOrDefault(n => n.Slot == Slot.MidiFx && n.SlotIndex == dev)
                                : _nodes.FirstOrDefault(n => n.Slot == Slot.Device && n.SlotIndex == dev);
            if (tNode == null || tNode == sNode) continue;
            int prm = _engine.CvLinkParam(_trackId, i);
            string pname = tk == 1 ? _engine.PluginParamName(_trackId, -1, prm)
                         : tk == 2 ? _engine.MidiEffectParamName(_trackId, dev, prm)
                                   : _engine.DeviceParamName(_trackId, dev, prm);
            int cvIdx = cvSeen.TryGetValue((tk, dev), out var seen) ? seen : 0;   // matches _cvTargets order
            cvSeen[(tk, dev)] = cvIdx + 1;
            _edgeList.Add(new GEdge(sNode, PortKind.Cv, tNode, PortKind.Cv)
            { LinkOwner = _trackId, LinkIndex = i, ToCvInput = cvIdx, Tip = $"{sNode.Title} → {tNode.Title}.{pname}" });
        }

        int edges = _edgeList.Count;
        _status.Text = $"{trackName} · {_nodes.Count} nodes · {edges} edge{(edges == 1 ? "" : "s")}";
        var cfg = _engine.GetAudioConfig();
        double ms = cfg.BufferFrames > 0 && cfg.SampleRate > 0 ? cfg.BufferFrames / cfg.SampleRate * 1000.0 : 0;
        _statusRight.Text = $"{_engine.SampleRate / 1000.0:0.0} kHz · {cfg.BufferFrames} · {ms:0.0} ms";

        // Re-apply selection to the surviving node (structural edits rebuild the nodes).
        if (_selId != null)
        {
            var s = _nodes.FirstOrDefault(x => x.Id == _selId);
            if (s != null) SelectNode(s); else _selId = null;
        }

        _grid.InvalidateVisual();
        _edges.InvalidateVisual();
        _mini.InvalidateVisual();
    }

    private void AddNode(GNode n)
    {
        if (_savedPos.TryGetValue(PosKey(n), out var p)) n.World = p;
        _nodes.Add(n);
        Canvas.SetLeft(n.View, n.World.X);
        Canvas.SetTop(n.View, n.World.Y);
        _world.Children.Add(n.View);
    }

    // ---- Global view (islands, mockup 1c) ------------------------------

    private void RebuildGlobal()
    {
        double y = 60;
        int islands = 0;
        int n = _engine.TrackCount;
        var islandByTrack = new Dictionary<int, GNode>();
        var trackIds = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti) || ti.IsGroup) continue;
            var isl = IslandNode(ti.Id, i, ti);
            isl.World = new Point(40, y);
            AddNode(isl);
            islandByTrack[ti.Id] = isl;
            trackIds.Add(ti.Id);
            y += isl.IslandHeight + 24;
            islands++;
        }

        // Cross-track CV edges (mockup 1c): a modulator on one island driving a param
        // on another → a full-length dashed brass edge between the islands.
        int xlinks = 0;
        foreach (int owner in trackIds)
        {
            int lc = _engine.CvLinkCount(owner);
            for (int c = 0; c < lc; c++)
            {
                int tgt = _engine.CvLinkTargetTrack(owner, c);
                if (tgt < 0 || tgt == owner) continue;
                if (islandByTrack.TryGetValue(owner, out var a) && islandByTrack.TryGetValue(tgt, out var b))
                { _edgeList.Add(new GEdge(a, PortKind.Cv, b, PortKind.Cv) { LinkOwner = owner, LinkIndex = c }); xlinks++; }
            }
        }

        _status.Text = $"Global · {islands} island{(islands == 1 ? "" : "s")}"
            + (xlinks > 0 ? $" · {xlinks} cross-track CV link{(xlinks == 1 ? "" : "s")}" : "");
        var cfg = _engine.GetAudioConfig();
        double ms = cfg.BufferFrames > 0 && cfg.SampleRate > 0 ? cfg.BufferFrames / cfg.SampleRate * 1000.0 : 0;
        _statusRight.Text = $"{_engine.SampleRate / 1000.0:0.0} kHz · {cfg.BufferFrames} · {ms:0.0} ms";
        if (_selId != null) { var s = _nodes.FirstOrDefault(x => x.Id == _selId); if (s != null) SelectNode(s); }
        _grid.InvalidateVisual();
        _edges.InvalidateVisual();
        _mini.InvalidateVisual();
    }

    private const double IslandW = 460;

    private GNode IslandNode(int trackId, int trackIndex, NotaTrackInfo ti)
    {
        string name = _engine.GetTrackName(trackId);
        if (string.IsNullOrEmpty(name)) name = "Track " + trackId;
        var color = new SolidColorBrush(ArrangementView.TrackColorForIndex(ArrangementView.EffectiveColorIndex(_engine, trackId)));
        int mfx = ti.IsInstrument ? _engine.TrackMidiEffectCount(trackId) : 0;
        int dev = _engine.TrackDeviceCount(trackId);
        int mods = _engine.ModulatorCount(trackId);
        int cv = _engine.CvLinkCount(trackId);
        int nodes = mfx + 1 /* source */ + dev + mods + 1 /* out */;

        var n = new GNode($"isl{trackId}", name, color, new Point(40, 60)) { Slot = Slot.Island, SlotIndex = trackId };
        BuildIslandView(n, ti, nodes, dev, mods, cv);
        return n;
    }

    private void BuildIslandView(GNode n, NotaTrackInfo ti, int nodeCount, int dev, int mods, int cv)
    {
        int trackId = n.SlotIndex;
        bool hasMods = mods > 0;
        double h = 2 + 30 + 54 + (hasMods ? 34 : 0) + 4;
        n.IslandHeight = h;

        var col = new Grid { RowDefinitions = new RowDefinitions(hasMods ? "2,30,54,34" : "2,30,54") };
        col.Children.Add(Row(new Border { Background = n.Stripe, CornerRadius = new CornerRadius(9, 9, 0, 0) }, 0));

        // header
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 0) };
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(meta, Dock.Left);
        meta.Children.Add(new TextBlock { Text = n.Title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Text1, VerticalAlignment = VerticalAlignment.Center });
        meta.Children.Add(new TextBlock { Text = $"{nodeCount} NODES", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Text3, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(meta);
        if (cv > 0)
        {
            var cvLabel = new TextBlock { Text = $"{cv} CV", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Brass, VerticalAlignment = VerticalAlignment.Center };
            cvLabel.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            DockPanel.SetDock(cvLabel, Dock.Right);
            head.Children.Add(cvLabel);
        }
        var headBorder = new Border { Height = 30, Child = head, Background = Brushes.Transparent, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1) };
        col.Children.Add(Row(headBorder, 1));

        // collapsed pipe: [source] —bar— [N devices ▸] —bar— [Out]
        string srcName = ti.IsInstrument ? (_engine.TrackInstrumentKind(trackId) >= 0 ? _engine.DeviceName(trackId, -1) : "Instrument") : "Audio in";
        var pipe = new Grid { Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center };
        if (dev > 0)
        {
            pipe.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto");
            var pill = IslandPill($"{dev} device{(dev == 1 ? "" : "s")} ▸");
            pill.PointerPressed += (_, e) => { OpenTrack(trackId); e.Handled = true; };
            Grid.SetColumn(pill, 2);
            pipe.Children.Add(IslandChip(srcName, n.Stripe, 0));
            pipe.Children.Add(Pipe(1));
            pipe.Children.Add(pill);
            pipe.Children.Add(Pipe(3));
            pipe.Children.Add(IslandChip("Out", Text1, 4));
        }
        else
        {
            pipe.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
            pipe.Children.Add(IslandChip(srcName, n.Stripe, 0));
            pipe.Children.Add(Pipe(1));
            pipe.Children.Add(IslandChip("Out", Text1, 2));
        }
        col.Children.Add(Row(pipe, 2));

        // modulator chip row
        if (hasMods)
        {
            var mrow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center };
            int show = Math.Min(mods, 4);
            for (int i = 0; i < show; i++)
            {
                int id = _engine.ModulatorIdAt(trackId, i);
                mrow.Children.Add(IslandChip($"LFO {id}", Brass, -1));
            }
            if (mods > show) mrow.Children.Add(new TextBlock { Text = $"+{mods - show}", FontSize = 9, Foreground = Text3, VerticalAlignment = VerticalAlignment.Center });
            col.Children.Add(Row(mrow, 3));
        }

        var frame = new Border
        {
            Width = IslandW, Height = h, Background = Rail,
            BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Child = col,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 10, Blur = 28, Color = Color.FromArgb(115, 0, 0, 0) }),
        };
        n.View = frame;
        frame.PointerEntered += (_, _) => { if (!n.Selected) frame.BorderBrush = BorderHover; };
        frame.PointerExited += (_, _) => { if (!n.Selected) frame.BorderBrush = BorderDef; };
        headBorder.Cursor = new Cursor(StandardCursorType.SizeAll);
        headBorder.PointerPressed += (s, e) => BeginNodeDrag(n, headBorder, e);
        headBorder.PointerMoved += (s, e) => MoveNodeDrag(n, e);
        headBorder.PointerReleased += (s, e) => EndNodeDrag(n, e);
    }

    private static Border IslandChip(string text, IBrush topColor, int column)
    {
        // Type accent as a 2px top border (matching the mockup's border-top stripe).
        var b = new Border
        {
            Height = 30, Background = Card, BorderBrush = topColor, BorderThickness = new Thickness(1, 2, 1, 1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 0),
            Child = new TextBlock { Text = text, FontSize = 10, Foreground = Text1, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (column >= 0) Grid.SetColumn(b, column);
        return b;
    }

    private static Border IslandPill(string text)
    {
        var b = new Border
        {
            Height = 22, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 0), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = 9, Foreground = Text2, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };
        return b;
    }

    private static Border Pipe(int column)
    {
        var b = new Border { Height = 6, Background = Sage, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(b, column);
        return b;
    }

    // ---- node builders --------------------------------------------------

    private GNode InstrumentNode(Point at, string trackName)
    {
        int kind = _engine.TrackInstrumentKind(_trackId);
        string name = kind >= 0 ? _engine.DeviceName(_trackId, -1) : "Instrument";
        var n = new GNode($"inst", name, Slate, at)
        {
            AudioOut = true, MidiIn = true, CvIn = true, Slot = Slot.Instrument,   // params are CV targets
        };
        n.CvInputLabels = CvInputsFor(1, -1);
        PopulateParams(n, _engine.PluginParamCount(_trackId, -1),
            p => _engine.ParamModulated(1, _trackId, -1, p), p => InstrumentParam(p));
        BuildNodeView(n);
        return n;
    }

    private GNode DeviceNode(int index, Point at)
    {
        string name = _engine.DeviceName(_trackId, index);
        bool bypassed = _engine.DeviceBypassed(_trackId, index);
        var n = new GNode($"dev{index}", name, Sage, at)
        {
            AudioIn = true, AudioOut = true, CvIn = true, Bypassed = bypassed, Slot = Slot.Device, SlotIndex = index,
            CvOut = _exposedParams.Any(p => p.track == _trackId && p.dev == index),   // exposed param → CV source
        };
        n.CvInputLabels = CvInputsFor(0, index);
        PopulateParams(n, _engine.DeviceParamCount(_trackId, index),
            p => _engine.DeviceParamModulated(_trackId, index, p), p => DeviceParam(index, p));
        BuildNodeView(n);
        return n;
    }

    private GNode MidiEffectNode(int index, Point at)
    {
        string name = _engine.MidiEffectName(_trackId, index);
        bool bypassed = _engine.MidiEffectBypassed(_trackId, index);
        var n = new GNode($"mfx{index}", name, Mauve, at)
        {
            MidiIn = true, MidiOut = true, CvIn = true, Bypassed = bypassed, Slot = Slot.MidiFx, SlotIndex = index,   // params are CV targets
        };
        n.CvInputLabels = CvInputsFor(2, index);
        PopulateParams(n, _engine.MidiEffectParamCount(_trackId, index),
            p => _engine.ParamModulated(2, _trackId, index, p), p => MidiEffectParam(index, p));
        BuildNodeView(n);
        return n;
    }

    private GNode SimpleNode(string id, string name, IBrush stripe, Point at,
        bool audioIn, bool audioOut, bool midiIn, bool midiOut, string badge, Slot slot)
    {
        var n = new GNode(id, name, stripe, at)
        {
            AudioIn = audioIn, AudioOut = audioOut, MidiIn = midiIn, MidiOut = midiOut, Badge = badge, Slot = slot,
        };
        BuildNodeView(n);
        return n;
    }

    // Params the user exposed as CV sources (per track). Transient UI state; a linked
    // source stays wired via its persisted CvLink regardless.
    private readonly HashSet<(int track, int dev, int param)> _exposedParams = new();

    private readonly HashSet<string> _expanded = new();

    // Per-node incoming CV targets on this track: (targetKind, device) → the modulated param
    // names, in link order. Built at the top of Rebuild; each entry becomes one CV input port.
    private readonly Dictionary<(int kind, int dev), List<string>> _cvTargets = new();
    private List<string> CvInputsFor(int kind, int dev)
        => _cvTargets.TryGetValue((kind, dev), out var l) ? l : _emptyLabels;
    private static readonly List<string> _emptyLabels = new();

    // Populate a node's visible param cells. Collapsed, a node shows only its CV-patched
    // params, so the card reflects what's being modulated; expanded (↕), it shows every
    // param with the patched ones kept on top. Records HiddenParamCount so the header knows
    // whether the expand affordance would actually reveal anything.
    private void PopulateParams(GNode n, int count, Func<int, bool> modulated, Func<int, Control> cell)
    {
        var patched = new List<int>();
        var rest = new List<int>();
        for (int p = 0; p < count; p++) (modulated(p) ? patched : rest).Add(p);
        foreach (int p in patched) n.Params.Add(cell(p));   // patched knobs first (at the top)
        bool expanded = _expanded.Contains(n.Id);
        if (expanded) foreach (int p in rest) n.Params.Add(cell(p));
        n.HiddenParamCount = expanded ? 0 : rest.Count;
    }
    private bool CanExpand(GNode n) => n.HiddenParamCount > 0;

    // ---- param → knob cell (device/midi raw floats, instrument normalized) ----

    private static string Fmt(float v) => Math.Abs(v) >= 100
        ? v.ToString("0", CultureInfo.InvariantCulture)
        : v.ToString("0.0", CultureInfo.InvariantCulture);

    private Control DeviceParam(int device, int param)
    {
        var e = _engine; int t = _trackId, d = device, pp = param;
        string name = e.DeviceParamName(t, d, pp);
        float min = e.DeviceParamMin(t, d, pp), max = e.DeviceParamMax(t, d, pp);
        double span = Math.Max(1e-6, max - min);
        float val = e.DeviceGetParam(t, d, pp);
        bool modulated = e.DeviceParamModulated(t, d, pp);
        bool exposed = _exposedParams.Contains((t, d, pp));
        var value = MonoValue(Fmt(val));
        var knob = new Knob((val - min) / span, 1.0) { Accent = true, ArcColor = modulated ? Teal : null, Default = (e.DeviceParamDefault(t, d, pp) - min) / span };
        knob.ValueChanged += frac => { double v = min + frac * span; e.DeviceSetParam(t, d, pp, (float)v); value.Text = Fmt((float)v); };
        knob.GestureBegin += () => e.BeginAutomationWrite(t, AutomationTarget.DeviceParam, d, pp, "");
        knob.GestureEnd += () => e.EndAutomationWrite(t, AutomationTarget.DeviceParam, d, pp, "");
        MidiLearn.Bind(knob, MidiTarget.DeviceParam(t, d, pp), name);
        // Right-click → CV modulation menu (create / edit / remove a link).
        knob.ContextRequested += (_, ev) => { ShowModulateMenu(knob, 0, d, pp, name); ev.Handled = true; };
        _liveKnobs.Add(() =>
        {
            if (knob.Dragging) return;
            float cur = e.DeviceGetParam(t, d, pp); double frac = (cur - min) / span;
            if (Math.Abs(frac - knob.Value) > 1e-3) { knob.Value = frac; value.Text = Fmt(cur); }
        });
        return KnobCell(name, knob, value, exposed ? BrassBright : null);
    }

    private Control MidiEffectParam(int index, int param)
    {
        var e = _engine; int t = _trackId, ix = index, pp = param;
        string name = e.MidiEffectParamName(t, ix, pp);
        float min = e.MidiEffectParamMin(t, ix, pp), max = e.MidiEffectParamMax(t, ix, pp);
        double span = Math.Max(1e-6, max - min);
        float val = e.MidiEffectGetParam(t, ix, pp);
        bool modulated = e.ParamModulated(2, t, ix, pp);
        var value = MonoValue(Fmt(val));
        var knob = new Knob((val - min) / span, 1.0) { Accent = true, ArcColor = modulated ? Teal : null, Default = (e.MidiEffectParamDefault(t, ix, pp) - min) / span };
        knob.ValueChanged += frac => { double v = min + frac * span; e.MidiEffectSetParam(t, ix, pp, (float)v); value.Text = Fmt((float)v); };
        MidiLearn.Bind(knob, MidiTarget.MidiDeviceParam(t, ix, pp), name);
        knob.ContextRequested += (_, ev) => { ShowModulateMenu(knob, 2, ix, pp, name); ev.Handled = true; };
        _liveKnobs.Add(() =>
        {
            if (knob.Dragging) return;
            float cur = e.MidiEffectGetParam(t, ix, pp); double frac = (cur - min) / span;
            if (Math.Abs(frac - knob.Value) > 1e-3) { knob.Value = frac; value.Text = Fmt(cur); }
        });
        return KnobCell(name, knob, value, modulated ? Teal : null);
    }

    private Control InstrumentParam(int param)
    {
        var e = _engine; int t = _trackId, pp = param;
        string name = e.PluginParamName(t, -1, pp);
        string pid = e.PluginParamId(t, -1, pp);
        float val = e.PluginParamGet(t, -1, pp);   // normalized 0..1
        bool modulated = e.ParamModulated(1, t, -1, pp);
        var value = MonoValue(val.ToString("0.00", CultureInfo.InvariantCulture));
        var knob = new Knob(val, 1.0) { Accent = true, ArcColor = modulated ? Teal : null, Default = e.InstrumentParamDefault(t, pp) };
        knob.ValueChanged += frac => { e.PluginParamSet(t, -1, pp, (float)frac); value.Text = frac.ToString("0.00", CultureInfo.InvariantCulture); };
        knob.GestureBegin += () => e.BeginAutomationWrite(t, AutomationTarget.PluginParam, -1, -1, pid);
        knob.GestureEnd += () => e.EndAutomationWrite(t, AutomationTarget.PluginParam, -1, -1, pid);
        // Instrument params are modulated by dragging a CV cable onto the node (no right-click menu).
        _liveKnobs.Add(() =>
        {
            if (knob.Dragging) return;
            float cur = e.PluginParamGet(t, -1, pp);
            if (Math.Abs(cur - knob.Value) > 1e-3) { knob.Value = cur; value.Text = cur.ToString("0.00", CultureInfo.InvariantCulture); }
        });
        return KnobCell(name, knob, value, modulated ? Teal : null);
    }

    // ---- modulators (Phase 3: LFO node + CV links) ---------------------

    private void AddModulator(int kind)
    {
        if (_trackId <= 0) return;
        _engine.ModulatorAdd(_trackId, kind);   // 0 = LFO, 1 = envelope follower
        Rebuild(); Changed?.Invoke();
    }

    private void RemoveModulatorNode(GNode n)
    {
        _engine.ModulatorRemove(_trackId, n.ModId);
        _savedPos.Clear();
        Rebuild(); Changed?.Invoke();
    }

    private static (string title, string badge) ModKindLabel(int kind) => kind switch
    {
        1 => ("Env", "ENV"),
        2 => ("MIDI", "MIDI"),
        3 => ("ADSR", "ADSR"),
        4 => ("Macro", "MACRO"),
        5 => ("Math", "MATH"),
        6 => ("Scope", "SCOPE"),
        _ => ("LFO", "LFO"),
    };

    private GNode ModulatorNode(int modId, Point at)
    {
        int kind = _engine.ModulatorKind(_trackId, modId);
        var (title, badge) = ModKindLabel(kind);
        var n = new GNode($"mod{modId}", $"{title} {modId}", Brass, at)
        { Slot = Slot.Modulator, ModId = modId, ModKind = kind, CvOut = true, CvIn = kind == 6, Badge = badge };
        BuildModulatorView(n, modId, kind);
        return n;
    }

    private void BuildModulatorView(GNode n, int modId, int kind)
    {
        if (kind == 5) { BuildMathView(n, modId); return; }
        if (kind == 6) { BuildScopeView(n, modId); return; }

        // A top "select" row (waveform for LFO, MIDI source for MIDI→CV) then a knob grid.
        bool hasTopRow = kind == 0 || kind == 2;
        var col = new Grid { RowDefinitions = hasTopRow ? new RowDefinitions("3,32,34,Auto,Auto") : new RowDefinitions("3,32,Auto,Auto") };
        col.Children.Add(Row(new Border { Background = n.Stripe, CornerRadius = new CornerRadius(5, 5, 0, 0) }, 0));
        var headBorder = NodeHeader(n);
        col.Children.Add(Row(headBorder, 1));

        int gridRow = hasTopRow ? 3 : 2;
        Grid grid;
        if (kind == 4)   // Macro: one manual knob (its value is the CV output)
        {
            grid = KnobGrid(ModFieldCell("MACRO", modId, 4, 0f, 1f, v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %"));
        }
        else if (kind == 3)   // ADSR: Attack / Decay / Sustain / Release (+ Depth)
        {
            grid = KnobGrid(
                ModFieldCell("ATTACK", modId, 6, 0.1f, 2000f, v => v.ToString("0", CultureInfo.InvariantCulture) + " ms"),
                ModFieldCell("DECAY", modId, 8, 1f, 2000f, v => v.ToString("0", CultureInfo.InvariantCulture) + " ms"),
                ModFieldCell("SUSTAIN", modId, 9, 0f, 1f, v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %"),
                ModFieldCell("RELEASE", modId, 7, 1f, 4000f, v => v.ToString("0", CultureInfo.InvariantCulture) + " ms"),
                ModFieldCell("DEPTH", modId, 4, 0f, 1f, v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %"));
        }
        else if (kind == 1)   // envelope follower: Attack / Release / Depth
        {
            grid = KnobGrid(
                ModFieldCell("ATTACK", modId, 6, 0.1f, 500f, v => v.ToString("0", CultureInfo.InvariantCulture) + " ms"),
                ModFieldCell("RELEASE", modId, 7, 1f, 2000f, v => v.ToString("0", CultureInfo.InvariantCulture) + " ms"),
                ModFieldCell("DEPTH", modId, 4, 0f, 1f, v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %"));
        }
        else if (kind == 2)   // MIDI→CV: source select + Smooth / Depth
        {
            col.Children.Add(Row(MidiSourceRow(modId), 2));
            grid = KnobGrid(
                ModFieldCell("SMOOTH", modId, 6, 0.1f, 500f, v => v.ToString("0", CultureInfo.InvariantCulture) + " ms"),
                ModFieldCell("DEPTH", modId, 4, 0f, 1f, v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %"));
        }
        else   // LFO: waveform + sync, then Rate / Depth / Phase
        {
            col.Children.Add(Row(WaveSyncRow(modId), 2));
            grid = KnobGrid(
                ModRateCell(modId),
                ModFieldCell("DEPTH", modId, 4, 0f, 1f, v => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %"),
                ModFieldCell("PHASE", modId, 5, 0f, 1f, v => (v * 360).ToString("0", CultureInfo.InvariantCulture) + "°"));
        }
        col.Children.Add(Row(grid, gridRow));
        col.Children.Add(Row(PortsBlock(n), gridRow + 1));
        WrapFrame(n, col, headBorder);
    }

    private static readonly string[] MathOpNames = { "Add", "Subtract", "Multiply", "Invert", "Scale", "Min", "Max" };

    private void BuildMathView(GNode n, int modId)
    {
        var col = new Grid { RowDefinitions = new RowDefinitions("3,32,Auto,Auto") };
        col.Children.Add(Row(new Border { Background = n.Stripe, CornerRadius = new CornerRadius(5, 5, 0, 0) }, 0));
        var headBorder = NodeHeader(n);
        col.Children.Add(Row(headBorder, 1));

        var body = new StackPanel { Spacing = 4, Margin = new Thickness(9, 6) };
        body.Children.Add(MathLabeledRow("OP", MathOpCombo(modId)));
        body.Children.Add(KnobGrid(
            ModFieldCell("GAIN", modId, 4, 0f, 2f, v => v.ToString("0.00", CultureInfo.InvariantCulture)),
            ModFieldCell("OFFSET", modId, 12, -1f, 1f, v => v.ToString("0.00", CultureInfo.InvariantCulture))));
        col.Children.Add(Row(body, 2));
        col.Children.Add(Row(MathPortsBlock(n), 3));   // A/B CV inputs (drag a cable in) + CV out
        WrapFrame(n, col, headBorder);
    }

    // Math ports: two CV inputs (A above B) on the left, CV-out on the right (row 0).
    private Control MathPortsBlock(GNode n)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        stack.Children.Add(PortRowUI(left: ("A", Brass, true), right: ("CV out", Brass, true), rightPress: e => BeginConnect(n, e)));
        stack.Children.Add(PortRowUI(left: ("B", Brass, true), right: null));
        return new Border { Child = stack, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0) };
    }

    // Scope: a CV monitor — one CV input (patch a cable in) drives a rolling
    // oscilloscope; the incoming CV also passes straight through to CV-out.
    private void BuildScopeView(GNode n, int modId)
    {
        var col = new Grid { RowDefinitions = new RowDefinitions("3,32,Auto,Auto") };
        col.Children.Add(Row(new Border { Background = n.Stripe, CornerRadius = new CornerRadius(5, 5, 0, 0) }, 0));
        var headBorder = NodeHeader(n);
        col.Children.Add(Row(headBorder, 1));

        var viz = new ScopeViz();
        int t = _trackId; var e = _engine;
        var buf = new float[128];
        _liveKnobs.Add(() =>
        {
            int m = e.ModulatorScope(t, modId, buf);
            viz.Set(buf, m);
        });
        var vizBox = new Border { Height = 76, Margin = new Thickness(9, 6), Child = viz };
        col.Children.Add(Row(vizBox, 2));
        col.Children.Add(Row(PortsBlock(n), 3));
        WrapFrame(n, col, headBorder);
    }

    private static Control MathLabeledRow(string label, Control field)
    {
        var dock = new DockPanel { LastChildFill = true };
        var lbl = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = Text3, VerticalAlignment = VerticalAlignment.Center, Width = 24 };
        DockPanel.SetDock(lbl, Dock.Left);
        dock.Children.Add(lbl);
        dock.Children.Add(field);
        return dock;
    }

    private Control MathOpCombo(int modId)
    {
        int t = _trackId; var e = _engine;
        var combo = new ComboBox { FontSize = 9, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 22 };
        foreach (var s in MathOpNames) combo.Items.Add(s);
        combo.SelectedIndex = Math.Clamp((int)Math.Round(e.ModulatorGet(t, modId, 0)), 0, MathOpNames.Length - 1);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { e.ModulatorSet(t, modId, 0, combo.SelectedIndex); Rebuild(); } };
        return combo;
    }

    private static readonly string[] MidiSourceNames = { "Vel", "Gate", "Note" };

    private Control MidiSourceRow(int modId)
    {
        int t = _trackId; var e = _engine;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(9, 0), VerticalAlignment = VerticalAlignment.Center };
        int cur = (int)Math.Round(e.ModulatorGet(t, modId, 0));   // field 0 = source select
        for (int i = 0; i < MidiSourceNames.Length; i++)
        {
            int sel = i;
            row.Children.Add(SegChip(MidiSourceNames[i], cur == sel, () => { e.ModulatorSet(t, modId, 0, sel); Rebuild(); }));
        }
        return new Border { Height = 34, Child = row, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    // 2-column knob grid with inner separators (N cells → ceil(N/2) rows of 56px).
    private static Grid KnobGrid(params Control[] cells)
    {
        int rows = (cells.Length + 1) / 2;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("56", rows))),
        };
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = new Border { Child = cells[i], BorderBrush = BorderInner, BorderThickness = new Thickness(i % 2 == 1 ? 1 : 0, i >= 2 ? 1 : 0, 0, 0) };
            Grid.SetColumn(cell, i % 2);
            Grid.SetRow(cell, i / 2);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private static readonly string[] WaveNames = { "Sine", "Tri", "Saw", "Sqr", "Rnd" };

    // LFO waveform selector. The Sync/Free toggle used to live here too, but with five
    // waveform chips it overflowed the node width — it now sits above the Rate knob (ModRateCell).
    private Control WaveSyncRow(int modId)
    {
        int t = _trackId; var e = _engine;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(9, 0), VerticalAlignment = VerticalAlignment.Center };
        int cur = (int)Math.Round(e.ModulatorGet(t, modId, 0));
        for (int i = 0; i < WaveNames.Length; i++)
        {
            int wf = i;
            row.Children.Add(SegChip(WaveNames[i], cur == wf, () => { e.ModulatorSet(t, modId, 0, wf); Rebuild(); }));
        }
        return new Border { Height = 34, Child = row, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    // A compact Sync/Free toggle used as the Rate cell's label (14px so it fits the knob row).
    private Border SyncToggle(int modId, bool synced)
    {
        var b = new Border
        {
            Height = 13, CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0),
            Background = NotaPalette.AccentSubtle, BorderBrush = NotaPalette.Accent, BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = synced ? "SYNC" : "FREE", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = BrassBright, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        b.PointerPressed += (_, e) => { _engine.ModulatorSet(_trackId, modId, 1, synced ? 0f : 1f); Rebuild(); e.Handled = true; };
        return b;
    }

    private static Border SegChip(string text, bool active, Action onClick)
    {
        var b = new Border
        {
            Height = 20, MinWidth = 30, CornerRadius = new CornerRadius(3),
            BorderBrush = active ? NotaPalette.Accent : BorderStrong, BorderThickness = new Thickness(1),
            Background = active ? NotaPalette.AccentSubtle : Brushes.Transparent,
            Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = text, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = active ? BrassBright : Text2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        b.PointerPressed += (_, e) => { onClick(); e.Handled = true; };
        return b;
    }

    // A modulator field as a labelled knob (0..1 knob → [min,max] field value).
    private Control ModFieldCell(string label, int modId, int field, float min, float max, Func<double, string> fmt)
    {
        int t = _trackId; var e = _engine;
        double span = Math.Max(1e-6, max - min);
        float v0 = e.ModulatorGet(t, modId, field);
        var value = MonoValue(fmt((v0 - min) / span * span + min));
        var knob = new Knob((v0 - min) / span, 1.0) { Accent = true };
        knob.ValueChanged += frac => { float v = (float)(min + frac * span); e.ModulatorSet(t, modId, field, v); value.Text = fmt(v); };
        _liveKnobs.Add(() =>
        {
            if (knob.Dragging) return;
            float cur = e.ModulatorGet(t, modId, field); double frac = (cur - min) / span;
            if (Math.Abs(frac - knob.Value) > 1e-3) { knob.Value = frac; value.Text = fmt(cur); }
        });
        return KnobCell(label, knob, value);
    }

    // Rate cell: musical divisions when synced, Hz when free. The Sync/Free toggle sits where
    // a param label normally goes (right above the knob), so it no longer crowds the wave row.
    private Control ModRateCell(int modId)
    {
        int t = _trackId; var e = _engine;
        bool synced = e.ModulatorGet(t, modId, 1) > 0.5f;
        int field = synced ? 3 : 2;
        float min = synced ? 0.0625f : 0.01f, max = synced ? 4f : 20f;
        Func<double, string> fmt = synced ? DivisionLabel : v => v.ToString("0.00", CultureInfo.InvariantCulture) + " Hz";
        double span = Math.Max(1e-6, max - min);
        float v0 = e.ModulatorGet(t, modId, field);
        var value = MonoValue(fmt(v0));
        var knob = new Knob((v0 - min) / span, 1.0) { Accent = true, Width = 30, Height = 30 };
        knob.ValueChanged += frac => { float v = (float)(min + frac * span); e.ModulatorSet(t, modId, field, v); value.Text = fmt(v); };
        _liveKnobs.Add(() =>
        {
            if (knob.Dragging) return;
            float cur = e.ModulatorGet(t, modId, field); double frac = (cur - min) / span;
            if (Math.Abs(frac - knob.Value) > 1e-3) { knob.Value = frac; value.Text = fmt(cur); }
        });
        return new StackPanel
        {
            Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { SyncToggle(modId, synced), knob, value },
        };
    }

    private static string DivisionLabel(double beats)
    {
        if (beats >= 3.5) return "1 bar";
        int denom = (int)Math.Round(4.0 / Math.Max(0.0625, beats));
        return "1/" + Math.Clamp(denom, 1, 64);
    }

    // Right-click a param knob: create / edit / remove a CV link (same or cross-track).
    // targetKind: 0 device, 1 instrument (device = -1), 2 MIDI-FX (device = chain index).
    private void ShowModulateMenu(Control anchor, int targetKind, int device, int param, string paramName)
    {
        int t = _trackId; var e = _engine;
        var flyout = new MenuFlyout();

        // Already modulated? Offer edit + remove (the link may be owned by another track).
        var (owner, li) = FindModulation(targetKind, device, param);
        if (owner >= 0)
        {
            bool cross = e.CvLinkTargetTrack(owner, li) >= 0;
            string srcLabel = e.CvLinkSourceKind(owner, li) == 1
                ? e.DeviceParamName(owner, e.CvLinkSourceDevice(owner, li), e.CvLinkSourceParam(owner, li))
                : "LFO " + e.CvLinkSource(owner, li);
            var edit = new MenuItem { Header = $"Edit modulation · {srcLabel}{(cross ? " (" + TrackName(owner) + ")" : "")}…" };
            edit.Click += (_, _) => ShowLinkEditor(anchor, owner, li);
            var rm = new MenuItem { Header = "Remove modulation" };
            rm.Click += (_, _) => { e.CvLinkRemove(owner, li); Rebuild(); Changed?.Invoke(); };
            flyout.Items.Add(edit);
            flyout.Items.Add(rm);
        }
        else
        {
            int sources = 0;
            // Same-track modulators.
            int mc = e.ModulatorCount(t);
            for (int i = 0; i < mc; i++)
            {
                int modId = e.ModulatorIdAt(t, i);
                var mi = new MenuItem { Header = $"Modulate with {(e.ModulatorKind(t, modId) == 1 ? "Env" : "LFO")} {modId}" };
                mi.Click += (_, _) => { e.CvLinkAddToTarget(t, modId, targetKind, -1, device, param); Rebuild(); Changed?.Invoke(); };
                flyout.Items.Add(mi); sources++;
            }
            // Exposed parameters on this track as sources (param → param).
            var paramSrc = new List<MenuItem>();
            foreach (var (xt, xd, xp) in _exposedParams)
            {
                if (xt != t || (xd == device && xp == param)) continue;
                int sd = xd, sp = xp;
                var mi = new MenuItem { Header = $"From {e.DeviceName(t, sd)} · {e.DeviceParamName(t, sd, sp)}" };
                mi.Click += (_, _) => { e.CvLinkAddFromParamToTarget(t, sd, sp, targetKind, -1, device, param); Rebuild(); Changed?.Invoke(); };
                paramSrc.Add(mi);
            }
            if (paramSrc.Count > 0)
            {
                var sub = new MenuItem { Header = "Modulate from a parameter" };
                foreach (var it in paramSrc) sub.Items.Add(it);
                flyout.Items.Add(sub); sources++;
            }
            // Other tracks' modulators (cross-track): the link is owned by the source track.
            var cross_ = new List<MenuItem>();
            int tn = e.TrackCount;
            for (int i = 0; i < tn; i++)
            {
                if (!e.TryGetTrackInfo(i, out var ti) || ti.Id == t) continue;
                int omc = e.ModulatorCount(ti.Id);
                for (int m = 0; m < omc; m++)
                {
                    int modId = e.ModulatorIdAt(ti.Id, m), src = ti.Id;
                    var mi = new MenuItem { Header = $"{(e.ModulatorKind(src, modId) == 1 ? "Env" : "LFO")} {modId} · {TrackName(src)}" };
                    mi.Click += (_, _) => { e.CvLinkAddToTarget(src, modId, targetKind, t, device, param); Rebuild(); Changed?.Invoke(); };
                    cross_.Add(mi);
                }
            }
            if (cross_.Count > 0)
            {
                var sub = new MenuItem { Header = "Modulate from another track" };
                foreach (var it in cross_) sub.Items.Add(it);
                flyout.Items.Add(sub); sources++;
            }
            if (sources == 0)
                flyout.Items.Add(new MenuItem { Header = "Add an LFO / expose a param first", IsEnabled = false });
        }

        // Expose toggle — only device params can be CV sources (param source is device-only).
        if (targetKind == 0)
        {
            flyout.Items.Add(new Separator());
            bool exposed = _exposedParams.Contains((t, device, param));
            var ex = new MenuItem { Header = exposed ? "Unexpose CV output" : "Expose as CV output" };
            ex.Click += (_, _) => { if (!_exposedParams.Remove((t, device, param))) _exposedParams.Add((t, device, param)); Rebuild(); };
            flyout.Items.Add(ex);
        }

        flyout.ShowAt(anchor, showAtPointer: true);
    }

    // (ownerTrack, linkIndex) of the link modulating (targetKind, _trackId, device, param),
    // scanning every track so cross-track links (owned elsewhere) are found. (-1,-1) if none.
    private (int owner, int index) FindModulation(int targetKind, int device, int param)
    {
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti)) continue;
            int owner = ti.Id, lc = _engine.CvLinkCount(owner);
            for (int c = 0; c < lc; c++)
            {
                int tgt = _engine.CvLinkTargetTrack(owner, c);
                int real = tgt < 0 ? owner : tgt;
                if (real == _trackId && _engine.CvLinkTargetKind(owner, c) == targetKind
                    && _engine.CvLinkDevice(owner, c) == device && _engine.CvLinkParam(owner, c) == param)
                    return (owner, c);
            }
        }
        return (-1, -1);
    }

    private string TrackName(int trackId)
    {
        string nm = _engine.GetTrackName(trackId);
        return string.IsNullOrEmpty(nm) ? "Track " + trackId : nm;
    }

    // CV-link depth + mode popover (mockup 1d). The link is owned by ownerTrack.
    // Open the depth/mode popover for the CV edge (P1 #16), anchored at the pointer.
    internal void EditCvEdge(GEdge e)
    {
        if (!e.IsCv) return;
        if (e.LinkOwner >= 0) { ShowLinkEditor(_edges, e.LinkOwner, e.LinkIndex, atPointer: true); return; }
        if (e.ModInputField >= 0) ShowModInputEditor(e);   // modulator→Math/Scope input: disconnect only
    }

    // Small popover for a modulator→modulator input edge (Math A/B, Scope in): no
    // depth/mode to tune, just a disconnect button (clears the target's input field).
    private void ShowModInputEditor(GEdge e)
    {
        var title = new TextBlock { Text = e.Tip, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Text3, TextWrapping = TextWrapping.Wrap };
        var flyout = new Flyout { Placement = Avalonia.Controls.PlacementMode.Pointer };
        var remove = new Button { Content = "Remove connection", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        remove.Click += (_, _) => { flyout.Hide(); RemoveModInputEdge(e); };
        flyout.Content = new StackPanel { Spacing = 8, Width = 200, Children = { title, remove } };
        flyout.ShowAt(_edges);
    }

    private void RemoveModInputEdge(GEdge e)
    {
        _engine.ModulatorSet(_trackId, e.To.ModId, e.ModInputField, -1);
        _selectedEdge = null; Rebuild(); Changed?.Invoke();
    }

    private void ShowLinkEditor(Control anchor, int ownerTrack, int linkIndex, bool atPointer = false)
    {
        int t = ownerTrack; var e = _engine;
        int modId = e.CvLinkSource(t, linkIndex);
        var title = new TextBlock { Text = $"CV LINK · LFO {modId}", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Text3 };

        var depth = new Slider { Minimum = -1, Maximum = 1, Value = e.CvLinkDepth(t, linkIndex), Width = 180, SmallChange = 0.05, LargeChange = 0.2 };
        var depthVal = MonoValue(((int)Math.Round(e.CvLinkDepth(t, linkIndex) * 100)) + " %");
        depth.PropertyChanged += (_, ev) => { if (ev.Property == Slider.ValueProperty) { e.SetCvLinkDepth(t, linkIndex, (float)depth.Value); depthVal.Text = ((int)Math.Round(depth.Value * 100)) + " %"; } };
        var depthRow = new DockPanel { LastChildFill = true };
        var dl = new TextBlock { Text = "DEPTH", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = Text3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(dl, Dock.Left); DockPanel.SetDock(depthVal, Dock.Right);
        depthRow.Children.Add(dl); depthRow.Children.Add(depthVal); depthRow.Children.Add(depth);

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        var modeNames = new[] { "Add", "Multiply", "Override" };
        int curMode = e.CvLinkMode(t, linkIndex);
        var chips = new Border[3];
        void SyncModes(int sel) { for (int i = 0; i < 3; i++) { bool on = i == sel; chips[i].Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent; chips[i].BorderBrush = on ? NotaPalette.Accent : BorderStrong; ((TextBlock)chips[i].Child!).Foreground = on ? BrassBright : Text2; } }
        for (int i = 0; i < 3; i++)
        {
            int mode = i;
            chips[i] = SegChip(modeNames[i], i == curMode, () => { e.SetCvLinkMode(t, linkIndex, mode); SyncModes(mode); });
            modeRow.Children.Add(chips[i]);
        }

        var flyout = new Flyout { Placement = atPointer ? Avalonia.Controls.PlacementMode.Pointer : Avalonia.Controls.PlacementMode.Bottom };
        var remove = new Button { Content = "Remove modulation", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        remove.Click += (_, _) => { flyout.Hide(); e.CvLinkRemove(t, linkIndex); Rebuild(); Changed?.Invoke(); };

        var panel = new StackPanel { Spacing = 8, Width = 220, Children = { title, depthRow, new TextBlock { Text = "MODE", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = Text3 }, modeRow, remove } };
        flyout.Content = panel;
        flyout.ShowAt(anchor);
    }

    private static TextBlock MonoValue(string s)
    {
        var t = new TextBlock { Text = s, FontSize = 9, Foreground = Text1, HorizontalAlignment = HorizontalAlignment.Center };
        t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        return t;
    }

    private static Control KnobCell(string name, Knob knob, TextBlock value, IBrush? labelColor = null)
    {
        knob.Width = 30; knob.Height = 30;
        return new StackPanel
        {
            Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = name.ToUpperInvariant(), FontSize = 8, FontWeight = FontWeight.Bold, Foreground = labelColor ?? Text3, HorizontalAlignment = HorizontalAlignment.Center },
                knob, value,
            },
        };
    }

    // ---- node visual ----------------------------------------------------

    // Builds the 32px header strip (status dot · title · badge/action cluster). The
    // returned Border is the drag handle; WrapFrame wires it.
    private Border NodeHeader(GNode n)
    {
        var title = new TextBlock { Text = n.Title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Text1, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(9, 0) };
        var stripeDot = n.Slot == Slot.Modulator ? (IBrush)Brass : (n.Bypassed ? BorderStrong : Success);
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = stripeDot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(dot, Dock.Left);
        head.Children.Add(dot);
        // Right cluster: chain devices get bypass / expand / delete; modulators get a
        // MODULATOR badge + delete; everyone else just a badge.
        if (n.IsChainDevice)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(HeaderGlyph("⊘", n.Bypassed ? BrassBright : Text3, "Bypass (B)", () => ToggleBypass(n)));
            // Only offer expand when there are more params than the headline row shows,
            // otherwise the button reveals nothing (e.g. a plugin exposing few params).
            if (CanExpand(n))
                actions.Children.Add(HeaderGlyph("↕", _expanded.Contains(n.Id) ? BrassBright : Text3, "Expand", () => ToggleExpand(n)));
            actions.Children.Add(HeaderGlyph("✕", Text3, "Delete", () => DeleteNode(n)));
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
        }
        else if (n.Slot == Slot.Instrument && CanExpand(n))
        {
            // The instrument can't be bypassed/removed, but its params are expandable too.
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(HeaderGlyph("↕", _expanded.Contains(n.Id) ? BrassBright : Text3, "Expand", () => ToggleExpand(n)));
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
        }
        else if (n.Slot == Slot.Modulator)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(n.Badge) ? "LFO" : n.Badge, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Text3, VerticalAlignment = VerticalAlignment.Center });
            actions.Children.Add(HeaderGlyph("✕", Text3, "Delete", () => RemoveModulatorNode(n)));
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
        }
        else if (!string.IsNullOrEmpty(n.Badge))
        {
            var badge = new TextBlock { Text = n.Badge, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Text3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);
        }
        head.Children.Add(title);
        // Transparent (not null) background so the whole header strip is hit-testable —
        // otherwise only the title text / status dot catch the drag, not the gaps.
        return new Border { Height = 32, Child = head, Background = Brushes.Transparent, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    // Wraps a node's inner column in the card frame + wires hover and header-drag.
    private void WrapFrame(GNode n, Control col, Border headBorder)
    {
        var frame = new Border
        {
            Width = NodeW, Background = Card,
            BorderBrush = n.Bypassed ? BorderStrong : BorderDef,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Opacity = n.Bypassed ? 0.6 : 1.0, Child = col,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 8, Blur = 22, Color = Color.FromArgb(128, 0, 0, 0) }),
        };
        n.View = frame;
        frame.PointerEntered += (_, _) => { if (!n.Selected && !n.Bypassed) frame.BorderBrush = BorderHover; };
        frame.PointerExited += (_, _) => { if (!n.Selected) frame.BorderBrush = n.Bypassed ? BorderStrong : BorderDef; };
        headBorder.Cursor = new Cursor(StandardCursorType.SizeAll);
        headBorder.PointerPressed += (s, e) => BeginNodeDrag(n, headBorder, e);
        headBorder.PointerMoved += (s, e) => MoveNodeDrag(n, e);
        headBorder.PointerReleased += (s, e) => EndNodeDrag(n, e);
    }

    private void BuildNodeView(GNode n)
    {
        var col = new Grid { RowDefinitions = new RowDefinitions("3,32,Auto,Auto") };

        // type stripe
        col.Children.Add(Row(new Border { Background = n.Stripe, CornerRadius = new CornerRadius(5, 5, 0, 0) }, 0));

        var headBorder = NodeHeader(n);
        col.Children.Add(Row(headBorder, 1));

        // knob grid (2×2)
        if (n.Params.Count > 0)
        {
            int rows = (n.Params.Count + 1) / 2;
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("56", rows))),
            };
            for (int i = 0; i < n.Params.Count; i++)
            {
                var cell = new Border
                {
                    Child = n.Params[i],
                    BorderBrush = BorderInner,
                    BorderThickness = new Thickness(i % 2 == 1 ? 1 : 0, i >= 2 ? 1 : 0, 0, 0),
                };
                Grid.SetColumn(cell, i % 2);
                Grid.SetRow(cell, i / 2);
                grid.Children.Add(cell);
            }
            col.Children.Add(Row(grid, 2));
        }

        // ports block
        col.Children.Add(Row(PortsBlock(n), 3));

        WrapFrame(n, col, headBorder);
    }

    private Control PortsBlock(GNode n)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        // audio row (skipped for CV-only nodes like modulators)
        if (n.AudioIn || n.AudioOut)
            stack.Children.Add(PortRowUI(
                left: n.AudioIn ? ("Audio in", Sage, true) : null,
                right: n.AudioOut ? ("Audio out", Sage, true) : null));
        // midi row
        if (n.MidiIn || n.MidiOut)
            stack.Children.Add(PortRowUI(
                left: n.MidiIn ? ("MIDI in", Slate, false) : null,
                right: n.MidiOut ? ("MIDI out", Slate, false) : null));
        // cv row (Phase 3): only drawn for ports that actually function — a real CV
        // target (CvIn) and/or a real CV source (CvOut). Nodes with no CV role (LFO
        // input, audio-only nodes) show nothing, so the graph never lies. A
        // modulator's CV-out dot is a drag handle to wire a CV link (P1 #13).
        if (n.CvIn)
        {
            // One input row per incoming CV link (labelled with the driven param), or a single
            // generic "CV" row when nothing is patched yet.
            int rows = n.CvInputRows;
            for (int i = 0; i < rows; i++)
            {
                string lbl = i < n.CvInputLabels.Count ? n.CvInputLabels[i] : "CV";
                stack.Children.Add(PortRowUI(left: (lbl, Brass, true), right: null));
            }
        }
        if (n.CvOut)
        {
            Action<PointerPressedEventArgs>? cvOutPress = n.Slot == Slot.Modulator ? e => BeginConnect(n, e) : null;
            stack.Children.Add(PortRowUI(left: null, right: ("CV out", Brass, true), rightPress: cvOutPress));
        }
        return new Border { Child = stack, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0) };
    }

    private static Control PortRowUI((string label, IBrush color, bool round)? left,
        (string label, IBrush color, bool round)? right, bool muted = false,
        Action<PointerPressedEventArgs>? rightPress = null)
    {
        var grid = new Grid { Height = PortRow, Margin = new Thickness(12, 0) };
        if (left is { } l)
        {
            var tb = new TextBlock { Text = l.label, FontSize = 9, Foreground = muted ? Text4 : Text3, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            grid.Children.Add(tb);
            grid.Children.Add(PortDot(l.color, l.round, left: true, null));
        }
        if (right is { } r)
        {
            var tb = new TextBlock { Text = r.label, FontSize = 9, Foreground = muted ? Text4 : Text3, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            grid.Children.Add(tb);
            grid.Children.Add(PortDot(r.color, r.round, left: false, rightPress));
        }
        return grid;
    }

    private static Control PortDot(IBrush color, bool round, bool left, Action<PointerPressedEventArgs>? onPress)
    {
        double d = round ? 8 : 7;
        var dot = new Border
        {
            Width = d, Height = d, Background = color,
            CornerRadius = new CornerRadius(round ? d / 2 : 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Margin = left ? new Thickness(-16, 0, 0, 0) : new Thickness(0, 0, -16, 0),
        };
        if (onPress != null)
        {
            // Enlarge the hit target and make it a grab handle for wiring.
            var hit = new Border
            {
                Width = 16, Height = 16, Background = Brushes.Transparent, Child = dot,
                Cursor = new Cursor(StandardCursorType.Cross),
                HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                Margin = left ? new Thickness(-20, 0, 0, 0) : new Thickness(0, 0, -20, 0),
            };
            dot.Margin = new Thickness(0);
            dot.HorizontalAlignment = HorizontalAlignment.Center;
            hit.PointerPressed += (_, e) => { onPress(e); e.Handled = true; };
            return hit;
        }
        return dot;
    }

    // ---- node dragging --------------------------------------------------

    private bool _nodeDrag;
    private bool _nodeMoved;
    private Point _nodeDragStart;
    private Point _nodeOrigin;
    private readonly Border _dropBar = new()
    {
        Width = 3, Background = NotaPalette.AccentBright, CornerRadius = new CornerRadius(2), IsHitTestVisible = false,
    };

    private void BeginNodeDrag(GNode n, Control captureTarget, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(captureTarget).Properties.IsLeftButtonPressed) return;
        // Double-click: expand a chain device, or open an island's track in Track view.
        if (e.ClickCount == 2 && (n.IsChainDevice || n.Slot == Slot.Instrument) && CanExpand(n)) { ToggleExpand(n); e.Handled = true; return; }
        if (e.ClickCount == 2 && n.Slot == Slot.Island) { OpenTrack(n.SlotIndex); e.Handled = true; return; }
        Focus();          // route B / ⌘D / Delete to this view
        SelectNode(n);
        _nodeDrag = true;
        _nodeMoved = false;
        _nodeDragStart = e.GetPosition(_viewport);
        _nodeOrigin = n.World;
        e.Pointer.Capture(captureTarget);
        e.Handled = true;
    }

    private void MoveNodeDrag(GNode n, PointerEventArgs e)
    {
        if (!_nodeDrag) return;
        var p = e.GetPosition(_viewport);
        var dWorld = (p - _nodeDragStart) / _scale;
        n.World = new Point(_nodeOrigin.X + dWorld.X, _nodeOrigin.Y + dWorld.Y);
        if (Math.Abs(dWorld.X) + Math.Abs(dWorld.Y) > 2) _nodeMoved = true;
        Canvas.SetLeft(n.View, n.World.X);
        Canvas.SetTop(n.View, n.World.Y);
        if (n.IsChainDevice && _nodeMoved) ShowDropBar(n);
        _edges.InvalidateVisual();
        _mini.InvalidateVisual();
    }

    private void EndNodeDrag(GNode n, PointerReleasedEventArgs e)
    {
        _nodeDrag = false;
        _world.Children.Remove(_dropBar);
        e.Pointer.Capture(null);

        // A drag that crosses a sibling reorders the chain (snaps back to auto-layout).
        if (n.IsChainDevice && _nodeMoved)
        {
            int target = LaneTarget(n);
            if (target != n.SlotIndex)
            {
                if (n.Slot == Slot.MidiFx) _engine.MoveMidiEffect(_trackId, n.SlotIndex, target);
                else _engine.MoveDevice(_trackId, n.SlotIndex, target);
                _savedPos.Clear();
                Rebuild(); Changed?.Invoke();
                return;
            }
        }
        _savedPos[PosKey(n)] = n.World;   // free reposition
    }

    // Target index within the node's lane, from its current X among same-slot siblings.
    private int LaneTarget(GNode n)
    {
        double cx = n.World.X + NodeW / 2;
        int t = 0;
        foreach (var s in _nodes)
            if (s != n && s.Slot == n.Slot && s.World.X + NodeW / 2 < cx) t++;
        return t;
    }

    private void ShowDropBar(GNode n)
    {
        var others = _nodes.Where(x => x != n && x.Slot == n.Slot).OrderBy(x => x.World.X).ToList();
        _world.Children.Remove(_dropBar);
        if (others.Count == 0) return;
        int t = LaneTarget(n);
        double barX = t <= 0 ? others[0].World.X - GapX / 2
            : t >= others.Count ? others[^1].World.X + NodeW + GapX / 2
            : (others[t - 1].World.X + NodeW + others[t].World.X) / 2;
        double h = n.View.Bounds.Height > 0 ? n.View.Bounds.Height : 120;
        _dropBar.Height = h;
        Canvas.SetLeft(_dropBar, barX - 1.5);
        Canvas.SetTop(_dropBar, others[0].World.Y);
        _world.Children.Add(_dropBar);
    }

    // ---- connection drag (P1 #13): drag a modulator's CV-out onto a device node ----
    private bool _connecting;
    private int _connectMod;
    private Point _connectStartWorld;
    private Point _connectCursor;   // viewport-space
    internal bool Connecting => _connecting;
    internal Point ConnectStartWorld => _connectStartWorld;
    internal Point ConnectCursor => _connectCursor;

    private void BeginConnect(GNode modNode, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_viewport).Properties.IsLeftButtonPressed) return;
        Focus();
        _connecting = true;
        _connectMod = modNode.ModId;
        _connectStartWorld = modNode.Anchor(PortKind.Cv, output: true);
        _connectCursor = e.GetPosition(_viewport);
        e.Pointer.Capture(_viewport);
        HighlightTargets(true);
        _sidebarPanel.BorderBrush = Brass;   // hint: drop on a track here to modulate across tracks
        _edges.InvalidateVisual();
    }

    private void FinishConnect(PointerReleasedEventArgs e)
    {
        _connecting = false;
        e.Pointer.Capture(null);
        HighlightTargets(false);
        _sidebarPanel.BorderBrush = BorderDef;   // clear the cross-track drop hint

        // Dropped on the track list (left) → wire the modulator into a param on THAT track.
        var sp = e.GetPosition(_sidebar);
        if (sp.X >= 0 && sp.X <= _sidebar.Bounds.Width)
            foreach (var child in _sidebar.Children)
                if (child is Control c && c.Tag is int otherTrack && otherTrack != _trackId && c.Bounds.Contains(sp))
                { ShowCrossTrackParamMenu(c, otherTrack, _connectMod); return; }

        var scr = e.GetPosition(_viewport);
        var world = new Point((scr.X - _pan.X) / _scale, (scr.Y - _pan.Y) / _scale);
        GNode? target = null;
        for (int i = _nodes.Count - 1; i >= 0; i--)
        {
            var n = _nodes[i];
            if (!IsConnectTarget(n)) continue;
            double h = n.View.Bounds.Height > 0 ? n.View.Bounds.Height : 120;
            if (world.X >= n.World.X && world.X <= n.World.X + NodeW && world.Y >= n.World.Y && world.Y <= n.World.Y + h) { target = n; break; }
        }
        _edges.InvalidateVisual();
        if (target == null) return;
        if (target.IsMathNode)
        {
            // Wire the dragged modulator into the Math node's nearest input (A or B).
            if (target.ModId == _connectMod) return;   // no self-input
            bool b = Math.Abs(world.Y - target.MathInAnchor(true).Y) < Math.Abs(world.Y - target.MathInAnchor(false).Y);
            _engine.ModulatorSet(_trackId, target.ModId, b ? 11 : 10, _connectMod);
            Rebuild(); Changed?.Invoke();
        }
        else if (target.IsScopeNode)
        {
            // Wire the dragged modulator into the Scope's single CV input (field 10).
            if (target.ModId == _connectMod) return;   // no self-input
            _engine.ModulatorSet(_trackId, target.ModId, 10, _connectMod);
            Rebuild(); Changed?.Invoke();
        }
        else ShowConnectParamMenu(target, _connectMod);
    }

    // Nodes a CV cable can drop onto: insert effects, MIDI FX, the instrument, and Math inputs.
    private static bool IsConnectTarget(GNode n) => n.Slot is Slot.Device or Slot.MidiFx or Slot.Instrument || n.IsMathNode || n.IsScopeNode;

    // Pick which param the dragged CV link should drive (device / MIDI-FX / instrument).
    private void ShowConnectParamMenu(GNode node, int modId)
    {
        int t = _trackId;
        int kind = node.Slot == Slot.MidiFx ? 2 : node.Slot == Slot.Instrument ? 1 : 0;
        int di = node.Slot == Slot.Instrument ? -1 : node.SlotIndex;
        int pc = kind == 2 ? _engine.MidiEffectParamCount(t, di)
               : kind == 1 ? _engine.PluginParamCount(t, -1)
                           : _engine.DeviceParamCount(t, di);
        var flyout = new MenuFlyout();
        for (int p = 0; p < pc; p++)
        {
            int prm = p;
            string pn = kind == 2 ? _engine.MidiEffectParamName(t, di, prm)
                      : kind == 1 ? _engine.PluginParamName(t, -1, prm)
                                  : _engine.DeviceParamName(t, di, prm);
            var mi = new MenuItem { Header = pn };
            mi.Click += (_, _) => { _engine.CvLinkAddToTarget(t, modId, kind, -1, di, prm); Rebuild(); Changed?.Invoke(); };
            flyout.Items.Add(mi);
        }
        if (pc == 0) flyout.Items.Add(new MenuItem { Header = "No parameters", IsEnabled = false });
        flyout.ShowAt(node.View, showAtPointer: true);
    }

    // Cross-track wiring: a CV cable dropped on a track in the sidebar picks a param on THAT
    // track (device / instrument / MIDI-FX). The link is owned by the modulator's track and
    // targets the other one — the same thing the knob right-click "Modulate from another track"
    // does, but reachable by dragging.
    private void ShowCrossTrackParamMenu(Control anchor, int targetTrackId, int modId)
    {
        var e = _engine;
        var flyout = new MenuFlyout();

        void AddDeviceSub(string name, int kind, int di, int pc)
        {
            if (pc <= 0) return;
            var sub = new MenuItem { Header = name };
            for (int p = 0; p < pc; p++)
            {
                int prm = p;
                string pn = kind == 2 ? e.MidiEffectParamName(targetTrackId, di, prm)
                          : kind == 1 ? e.PluginParamName(targetTrackId, -1, prm)
                                      : e.DeviceParamName(targetTrackId, di, prm);
                var mi = new MenuItem { Header = pn };
                mi.Click += (_, _) => { e.CvLinkAddToTarget(_trackId, modId, kind, targetTrackId, di, prm); Rebuild(); Changed?.Invoke(); };
                sub.Items.Add(mi);
            }
            flyout.Items.Add(sub);
        }

        bool inst = e.TryGetTrackInfo(TrackIndex(targetTrackId), out var ti) && ti.IsInstrument;
        if (inst)
        {
            int mc = e.TrackMidiEffectCount(targetTrackId);
            for (int m = 0; m < mc; m++) AddDeviceSub(e.MidiEffectName(targetTrackId, m), 2, m, e.MidiEffectParamCount(targetTrackId, m));
            AddDeviceSub(e.DeviceName(targetTrackId, -1), 1, -1, e.PluginParamCount(targetTrackId, -1));
        }
        int dc = e.TrackDeviceCount(targetTrackId);
        for (int d = 0; d < dc; d++) AddDeviceSub(e.DeviceName(targetTrackId, d), 0, d, e.DeviceParamCount(targetTrackId, d));

        if (flyout.Items.Count == 0) flyout.Items.Add(new MenuItem { Header = "No modulatable parameters on that track", IsEnabled = false });
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    // While wiring, outline valid target nodes in brass (thicker so it's obvious).
    private void HighlightTargets(bool on)
    {
        foreach (var n in _nodes)
            if (IsConnectTarget(n) && n.View is Border b)
            {
                b.BorderBrush = on ? BrassBright : (n.Selected ? n.Stripe : (n.Bypassed ? BorderStrong : BorderDef));
                b.BorderThickness = new Thickness(on ? 2 : (n.Selected ? 1.5 : 1));
            }
    }

    private string? _selId;
    private GEdge? _hoverEdge, _selectedEdge;
    private readonly Border _edgeTip = new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = NotaPalette.BorderStrong, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 5), IsVisible = false, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 20, Color = Color.FromArgb(153, 0, 0, 0) }),
    };
    private readonly TextBlock _edgeTipText = new() { FontSize = 9, Foreground = NotaPalette.TextPrimary };

    internal void ShowEdgeTip(string text, Point screen)
    {
        _edgeTipText.Text = text;
        _edgeTip.Margin = new Thickness(screen.X + 12, screen.Y + 12, 0, 0);
        _edgeTip.IsVisible = true;
    }
    internal void HideEdgeTip() => _edgeTip.IsVisible = false;
    internal void SelectEdge(GEdge? e) { SelectNone(); _selectedEdge = e; _edges.InvalidateVisual(); }
    internal GEdge? HoverEdge { get => _hoverEdge; set { if (!ReferenceEquals(_hoverEdge, value)) { _hoverEdge = value; _edges.InvalidateVisual(); } } }
    internal GEdge? SelectedEdge => _selectedEdge;

    private void SelectNode(GNode sel)
    {
        _selId = sel.Id;
        _selectedEdge = null;
        foreach (var n in _nodes)
        {
            n.Selected = n == sel;
            if (n.View is Border b)
            {
                b.BorderBrush = n.Selected ? n.Stripe : (n.Bypassed ? BorderStrong : BorderDef);
                b.BorderThickness = new Thickness(n.Selected ? 1.5 : 1);
            }
        }
    }

    // ---- node operations (Phase 2) -------------------------------------

    /// <summary>Raised after a structural chain edit (bypass/reorder/delete/duplicate)
    /// so the host can refresh the arrangement + detail panel.</summary>
    public event Action? Changed;

    private static Control HeaderGlyph(string glyph, IBrush color, string tip, Action act)
    {
        var tb = new TextBlock { Text = glyph, FontSize = 11, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(3), Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand), Child = tb, VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, tip);
        b.PointerEntered += (_, _) => b.Background = BorderInner;
        b.PointerExited += (_, _) => b.Background = Brushes.Transparent;
        b.PointerPressed += (_, e) => { act(); e.Handled = true; };   // Handled so header drag doesn't start
        return b;
    }

    private GNode? Selected() { foreach (var n in _nodes) if (n.Selected) return n; return null; }

    private void ToggleBypass(GNode n)
    {
        if (n.Slot == Slot.MidiFx) _engine.SetMidiEffectBypassed(_trackId, n.SlotIndex, !_engine.MidiEffectBypassed(_trackId, n.SlotIndex));
        else if (n.Slot == Slot.Device) _engine.SetDeviceBypassed(_trackId, n.SlotIndex, !_engine.DeviceBypassed(_trackId, n.SlotIndex));
        else return;
        Rebuild(); Changed?.Invoke();
    }

    private void ToggleExpand(GNode n)
    {
        if (!_expanded.Remove(n.Id)) _expanded.Add(n.Id);
        Rebuild();
    }

    private void DeleteNode(GNode n)
    {
        if (n.Slot == Slot.MidiFx) _engine.RemoveMidiEffect(_trackId, n.SlotIndex);
        else if (n.Slot == Slot.Device) _engine.RemoveDevice(_trackId, n.SlotIndex);
        else return;
        _expanded.Remove(n.Id);
        _savedPos.Clear();   // indices shift → re-lay-out cleanly
        Rebuild(); Changed?.Invoke();
    }

    private void DuplicateNode(GNode n)
    {
        if (n.Slot == Slot.Device)
        {
            int kind = _engine.TrackDeviceBuiltinKind(_trackId, n.SlotIndex);
            if (kind < 0) return;   // hosted plugins: not duplicated from the graph (Phase 2)
            var state = _engine.DeviceGetState(_trackId, n.SlotIndex);
            bool byp = _engine.DeviceBypassed(_trackId, n.SlotIndex);
            int ni = _engine.AddBuiltinDevice(_trackId, kind);
            if (ni < 0) return;
            if (state != null) _engine.DeviceSetState(_trackId, ni, state);
            if (byp) _engine.SetDeviceBypassed(_trackId, ni, true);
            if (ni > n.SlotIndex) _engine.MoveDevice(_trackId, ni, n.SlotIndex + 1);
        }
        else if (n.Slot == Slot.MidiFx)
        {
            int kind = _engine.MidiEffectKind(_trackId, n.SlotIndex);
            int pc = _engine.MidiEffectParamCount(_trackId, n.SlotIndex);
            var p = new float[pc];
            for (int i = 0; i < pc; i++) p[i] = _engine.MidiEffectGetParam(_trackId, n.SlotIndex, i);
            bool byp = _engine.MidiEffectBypassed(_trackId, n.SlotIndex);
            int ni = _engine.AddMidiEffect(_trackId, kind);
            if (ni < 0) return;
            for (int i = 0; i < Math.Min(pc, _engine.MidiEffectParamCount(_trackId, ni)); i++) _engine.MidiEffectSetParam(_trackId, ni, i, p[i]);
            if (byp) _engine.SetMidiEffectBypassed(_trackId, ni, true);
            if (ni > n.SlotIndex) _engine.MoveMidiEffect(_trackId, ni, n.SlotIndex + 1);
        }
        else return;
        _savedPos.Clear();
        Rebuild(); Changed?.Invoke();
    }

    // ---- pan / zoom -----------------------------------------------------

    private void OnViewportPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        var pt = e.GetCurrentPoint(_viewport);
        bool onEmpty = e.Source == _viewport || e.Source == _grid || e.Source == _edges;
        if (pt.Properties.IsMiddleButtonPressed || (_spaceDown && pt.Properties.IsLeftButtonPressed) || onEmpty)
        {
            _panning = true;
            _panStart = pt.Position;
            _panOrigin = _pan;
            e.Pointer.Capture(_viewport);
            if (onEmpty) SelectNone();
        }
    }

    private void OnViewportMoved(object? sender, PointerEventArgs e)
    {
        if (_connecting) { _connectCursor = e.GetPosition(_viewport); _edges.InvalidateVisual(); return; }
        if (!_panning) return;
        var p = e.GetPosition(_viewport);
        _pan = _panOrigin + (p - _panStart);
        ApplyTransform();
    }

    private void OnViewportReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_connecting) { FinishConnect(e); return; }
        _panning = false;
        e.Pointer.Capture(null);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        bool zoomMod = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (zoomMod)
        {
            var cursor = e.GetPosition(_viewport);
            double factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
            ZoomAround(cursor, factor);
        }
        else
        {
            // plain wheel pans; shift swaps axes
            var d = e.Delta;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) d = new Vector(d.Y, d.X);
            _pan += new Vector(d.X * 40, d.Y * 40);
            ApplyTransform();
        }
        e.Handled = true;
    }

    private void ZoomAround(Point screen, double factor)
    {
        double newScale = Math.Clamp(_scale * factor, 0.25, 2.5);
        factor = newScale / _scale;
        // keep the world point under the cursor fixed
        _pan = new Vector(
            screen.X - (screen.X - _pan.X) * factor,
            screen.Y - (screen.Y - _pan.Y) * factor);
        _scale = newScale;
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        _world.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(_scale, _scale), new TranslateTransform(_pan.X, _pan.Y) },
        };
        _world.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        _zoomLabel.Text = $"{_scale * 100:0} %";
        _grid.InvalidateVisual();
        _edges.InvalidateVisual();
        _mini.InvalidateVisual();
    }

    private void FitToScreen()
    {
        // Deferred: viewport bounds may be 0 until first layout.
        Avalonia.Threading.Dispatcher.UIThread.Post(FitNow, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void FitNow()
    {
        if (_nodes.Count == 0) return;
        double vw = _viewport.Bounds.Width, vh = _viewport.Bounds.Height;
        if (vw < 10 || vh < 10) { FitToScreen(); return; }
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var n in _nodes)
        {
            minX = Math.Min(minX, n.World.X); minY = Math.Min(minY, n.World.Y);
            maxX = Math.Max(maxX, n.World.X + n.NodeWidth); maxY = Math.Max(maxY, n.World.Y + n.View.Bounds.Height);
        }
        double gw = maxX - minX, gh = maxY - minY;
        if (gw < 1 || gh < 1) return;
        const double pad = 60;
        double s = Math.Min((vw - pad * 2) / gw, (vh - pad * 2) / gh);
        _scale = Math.Clamp(s, 0.25, 1.4);
        _pan = new Vector(
            (vw - gw * _scale) / 2 - minX * _scale,
            (vh - gh * _scale) / 2 - minY * _scale);
        ApplyTransform();
    }

    private void SelectNone()
    {
        _selId = null;
        _selectedEdge = null;
        foreach (var n in _nodes)
        {
            n.Selected = false;
            if (n.View is Border b) { b.BorderBrush = n.Bypassed ? BorderStrong : BorderDef; b.BorderThickness = new Thickness(1); }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool mod = (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0;
        var sel = Selected();
        if (e.Key == Key.Space) { _spaceDown = true; _viewport.Cursor = new Cursor(StandardCursorType.SizeAll); }
        else if (e.Key == Key.F && !mod) { FitNow(); e.Handled = true; }
        else if (mod && e.Key == Key.D && sel is { IsChainDevice: true }) { DuplicateNode(sel); e.Handled = true; }
        else if (!mod && e.Key == Key.B && sel is { IsChainDevice: true }) { ToggleBypass(sel); e.Handled = true; }
        else if (!mod && (e.Key == Key.Delete || e.Key == Key.Back) && _selectedEdge is { IsCv: true, LinkOwner: >= 0 } ce)
        {
            _engine.CvLinkRemove(ce.LinkOwner, ce.LinkIndex); _selectedEdge = null;
            Rebuild(); Changed?.Invoke(); e.Handled = true;
        }
        else if (!mod && (e.Key == Key.Delete || e.Key == Key.Back) && _selectedEdge is { IsCv: true, ModInputField: >= 0 } me)
        {
            RemoveModInputEdge(me); e.Handled = true;
        }
        else if (!mod && (e.Key == Key.Delete || e.Key == Key.Back) && sel is { IsChainDevice: true }) { DeleteNode(sel); e.Handled = true; }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) { _spaceDown = false; _viewport.Cursor = Cursor.Default; }
    }

    // ---- chrome ---------------------------------------------------------

    private Control Toolbar()
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        _trackBtn.Click += (_, _) => SetGlobal(false);
        _globalBtn.Click += (_, _) => SetGlobal(true);
        var seg = new Border
        {
            Background = Sunken, CornerRadius = new CornerRadius(5), Padding = new Thickness(2),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _trackBtn, _globalBtn } },
        };
        // All the modulator sources collapse into one "+ Add" dropdown (a normal chip
        // button + a menu flyout) so the toolbar stays compact.
        var addMenu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        void AddItem(string label, int kind)
        {
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) => AddModulator(kind);
            addMenu.Items.Add(mi);
        }
        AddItem("LFO", 0);
        AddItem("Envelope Follower", 1);
        AddItem("MIDI → CV", 2);
        AddItem("ADSR", 3);
        AddItem("Macro", 4);
        AddItem("Math", 5);
        AddItem("Scope", 6);
        var addBtn = Chip("+ Add  ▾");
        addBtn.Flyout = addMenu;
        left.Children.Add(seg);
        left.Children.Add(addBtn);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var zoomOut = ZoomBtn("−"); zoomOut.Click += (_, _) => ZoomAround(Center(), 1 / 1.15);
        var zoomIn = ZoomBtn("+"); zoomIn.Click += (_, _) => ZoomAround(Center(), 1.15);
        var zoomSeg = new Border
        {
            Background = Sunken, CornerRadius = new CornerRadius(5), Padding = new Thickness(2),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { zoomOut, zoomIn } },
        };
        var fit = Chip("Fit"); fit.Click += (_, _) => FitNow();
        right.Children.Add(zoomSeg);
        right.Children.Add(fit);
        right.Children.Add(_zoomLabel);

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = NotaPalette.SurfaceCard };
        var lb = new Border { Child = left, Padding = new Thickness(12, 0), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1) };
        var rb = new Border { Child = right, Padding = new Thickness(12, 0), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1) };
        Grid.SetColumn(rb, 1);
        bar.Children.Add(lb); bar.Children.Add(rb);
        return Row(bar, 0);
    }

    private Point Center() => new(_viewport.Bounds.Width / 2, _viewport.Bounds.Height / 2);

    private Control StatusBar()
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Sunken,
        };
        var lb = new Border { Child = _status, Padding = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0) };
        var rb = new Border { Child = _statusRight, Padding = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0) };
        lb.Height = 22; rb.Height = 22;
        Grid.SetColumn(rb, 1);
        bar.Children.Add(lb); bar.Children.Add(rb);
        return Row(bar, 2);
    }

    private static ToggleButton Seg(string text, bool on) => new()
    {
        Content = text, IsChecked = on, Classes = { "seg" },
        FontSize = 11, Padding = new Thickness(10, 0), Height = 22,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static Button Chip(string text) => new()
    {
        Content = text, Classes = { "chip" }, FontSize = 11, Height = 28,
        Padding = new Thickness(9, 0), VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static Button ZoomBtn(string text) => new()
    {
        // Padding must be 0 — the base Button style sets 14,0, which on a 24px-wide button
        // squeezes the +/− glyph out of view entirely.
        Content = text, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        Foreground = Text2, Width = 24, Height = 22, FontSize = 14, Padding = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static Control Row(Control c, int row) { Grid.SetRow(c, row); return c; }

    private int TrackIndex(int trackId)
    {
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++) if (_engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) return i;
        return 0;
    }

    // ===================================================================
    //  Inner layers
    // ===================================================================

    internal enum PortKind { Audio, Midi, Cv }

    // Which part of the track chain a node maps to, for reorder/bypass/delete ops.
    internal enum Slot { Input, MidiFx, Instrument, Device, Output, Modulator, Island }

    internal sealed class GNode
    {
        public string Id;
        public string Title;
        public IBrush Stripe;
        public Point World;
        public Border View = null!;
        public readonly List<Control> Params = new();
        public bool AudioIn, AudioOut, MidiIn, MidiOut;
        public bool CvIn, CvOut;
        public bool Bypassed, Selected;
        public string Badge = "";
        public Slot Slot = Slot.Input;
        public int SlotIndex = -1;   // device / MIDI-FX index within its lane; track id for islands
        public int ModId = -1;       // modulator id (Slot.Modulator)
        public int ModKind = -1;     // modulator kind (Slot.Modulator)
        public int HiddenParamCount; // unshown params (collapsed) → whether ↕ reveals anything
        public List<string> CvInputLabels = new();   // one CV input port per incoming CV link (param name)
        public double IslandHeight;  // explicit height for Global-view island layout

        // CV input rows this node draws: one per incoming link, or a single generic port when
        // nothing is patched yet (so the card still shows it accepts CV).
        public int CvInputRows => CvIn ? Math.Max(1, CvInputLabels.Count) : 0;

        /// <summary>True for nodes that map to a movable/removable chain device.</summary>
        public bool IsChainDevice => Slot == Slot.Device || Slot == Slot.MidiFx;

        /// <summary>Math node: has two CV inputs (A/B) wired from other modulators.</summary>
        public bool IsMathNode => Slot == Slot.Modulator && ModKind == 5;

        /// <summary>Scope node: one CV input (patch cable) monitored on an oscilloscope.</summary>
        public bool IsScopeNode => Slot == Slot.Modulator && ModKind == 6;

        // The two CV-in port anchors for a Math node (A above B on the left side).
        public Point MathInAnchor(bool b)
        {
            double h = View.Bounds.Height > 0 ? View.Bounds.Height : 120;
            double portsTop = h - 6 - 2 * PortRow;   // rows: [A | out] then [B]
            return new Point(World.X, World.Y + portsTop + (b ? 1 : 0) * PortRow + PortRow / 2);
        }

        /// <summary>Laid-out width (islands are wider than chain nodes).</summary>
        public double NodeWidth => Slot == Slot.Island ? IslandW : NodeW;

        public GNode(string id, string title, IBrush stripe, Point world)
        { Id = id; Title = title; Stripe = stripe; World = world; }

        // Port anchor in world coordinates. Ports live in the bottom block; we lay
        // them out audio→midi→cv, 20px rows, ending 6px above the node's bottom.
        public Point Anchor(PortKind kind, bool output, int cvInput = 0)
        {
            double h = View.Bounds.Height > 0 ? View.Bounds.Height : 120;
            // Islands have no per-port rows — anchor cross-track CV edges at the side middles.
            if (Slot == Slot.Island)
            {
                double hh = IslandHeight > 0 ? IslandHeight : h;
                return new Point(World.X + (output ? IslandW : 0), World.Y + hh / 2);
            }
            // Math node: CV-out sits on the first of two port rows ([A | out], [B]).
            if (IsMathNode)
            {
                double mathTop = h - 6 - 2 * PortRow;
                return new Point(World.X + (output ? NodeW : 0), World.Y + mathTop + PortRow / 2);
            }
            // Port rows are laid out top-to-bottom, drawn only when present:
            // [audio?] [midi?] [cv in × N] [cv out?]. Must match PortsBlock so cables land on dots.
            int rows = 0, audioIdx = -1, midiIdx = -1;
            if (AudioIn || AudioOut) audioIdx = rows++;
            if (MidiIn || MidiOut) midiIdx = rows++;
            int cvInBase = rows;
            rows += CvInputRows;
            int cvOutIdx = CvOut ? rows++ : -1;
            double portsTop = h - 6 - rows * PortRow;
            int idx;
            if (kind == PortKind.Audio) idx = audioIdx;
            else if (kind == PortKind.Midi) idx = midiIdx;
            else idx = output ? cvOutIdx : cvInBase + Math.Clamp(cvInput, 0, Math.Max(0, CvInputRows - 1));
            if (idx < 0) idx = rows > 0 ? rows - 1 : 0;
            double y = World.Y + portsTop + idx * PortRow + PortRow / 2;
            double x = World.X + (output ? NodeW : 0);
            return new Point(x, y);
        }
    }

    internal sealed class GEdge
    {
        public GNode From; public PortKind FromKind;
        public GNode To; public PortKind ToKind;
        public int LinkOwner = -1;   // CV edges: owning track + link index (for delete/edit)
        public int LinkIndex = -1;
        public int ToMathInput = -1; // Math-input edge: 0 = A, 1 = B (anchors at that port)
        public int ToCvInput = -1;   // which CV input port on a regular target node this edge lands on
        public int ModInputField = -1; // modulator→modulator input edge: the To.ModId field to clear on remove (10/11)
        public string Tip = "";
        public bool IsCv => FromKind == PortKind.Cv;
        public GEdge(GNode from, PortKind fromKind, GNode to, PortKind toKind)
        { From = from; FromKind = fromKind; To = to; ToKind = toKind; Tip = $"{from.Title} → {to.Title}"; }
    }

    // Rolling CV oscilloscope for a Scope node. Fed a snapshot (oldest→newest)
    // each Tick; draws a brass trace over a zero line in the -1..1 CV range.
    private sealed class ScopeViz : Control
    {
        private float[] _buf = Array.Empty<float>();
        private int _n;
        public ScopeViz() { IsHitTestVisible = false; }
        public void Set(float[] buf, int n)
        {
            if (_buf.Length < n) _buf = new float[n];
            Array.Copy(buf, _buf, n);
            _n = n;
            InvalidateVisual();
        }
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            ctx.DrawRectangle(ScopeBg, null, new Rect(0, 0, w, h), 4, 4);
            double mid = h / 2;
            ctx.DrawLine(new Pen(BorderInner, 1), new Point(0, mid), new Point(w, mid));
            if (_n < 2) return;
            const double range = 1.15;   // headroom past unity
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (int i = 0; i < _n; i++)
                {
                    double x = _n == 1 ? 0 : i / (double)(_n - 1) * w;
                    double v = Math.Clamp(_buf[i] / range, -1, 1);
                    double y = mid - v * (mid - 2);
                    var p = new Point(x, y);
                    if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
                }
            }
            ctx.DrawGeometry(null, new Pen(Brass, 1.5), geo);
        }
    }

    private static readonly IBrush ScopeBg = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x0d));

    private sealed class GridLayer : Control
    {
        private readonly ModularView _v;
        public GridLayer(ModularView v) { _v = v; IsHitTestVisible = false; }
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            double step = 22 * _v._scale;
            if (step < 6) return;   // too dense to be useful
            double ox = _v._pan.X % step, oy = _v._pan.Y % step;
            double r = Math.Clamp(_v._scale, 0.6, 1.2);
            var brush = Dot;
            for (double y = oy; y < h; y += step)
                for (double x = ox; x < w; x += step)
                    ctx.DrawEllipse(brush, null, new Point(x, y), r, r);
        }
    }

    private sealed class EdgeLayer : Control
    {
        private readonly ModularView _v;
        public EdgeLayer(ModularView v)
        {
            _v = v; IsHitTestVisible = true;   // edges are selectable; empty areas fall through to pan
            PointerMoved += OnMoved;
            PointerPressed += OnPressed;
            PointerExited += (_, _) => { _v.HoverEdge = null; _v.HideEdgeTip(); };
        }

        // Bezier control points for an edge in screen space.
        private (Point a, Point c1, Point c2, Point b) Curve(GEdge e)
        {
            var a = ToScreen(e.From.Anchor(e.FromKind, output: true));
            var b = ToScreen(e.ToMathInput >= 0 ? e.To.MathInAnchor(e.ToMathInput == 1)
                                                 : e.To.Anchor(e.ToKind, output: false, e.ToCvInput < 0 ? 0 : e.ToCvInput));
            double dx = Math.Max(30, Math.Abs(b.X - a.X) * 0.5);
            return (a, new Point(a.X + dx, a.Y), new Point(b.X - dx, b.Y), b);
        }

        private static Point Bez(Point a, Point c1, Point c2, Point b, double t)
        {
            double u = 1 - t, w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
            return new Point(w0 * a.X + w1 * c1.X + w2 * c2.X + w3 * b.X,
                             w0 * a.Y + w1 * c1.Y + w2 * c2.Y + w3 * b.Y);
        }

        // Nearest edge to a screen point, if within the pick threshold.
        private GEdge? Pick(Point p, out Point at)
        {
            at = p;
            GEdge? best = null; double bestD = 14.0;   // px — generous band so the dashed cable is easy to grab
            foreach (var e in _v._edgeList)
            {
                var (a, c1, c2, b) = Curve(e);
                Point prev = a;
                for (int i = 1; i <= 40; i++)
                {
                    Point cur = Bez(a, c1, c2, b, i / 40.0);
                    double d = DistToSeg(p, prev, cur);
                    if (d < bestD) { bestD = d; best = e; at = cur; }
                    prev = cur;
                }
            }
            return best;
        }

        private static double DistToSeg(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            double t = len2 <= 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
            double cx = a.X + t * dx, cy = a.Y + t * dy;
            return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
        }

        private void OnMoved(object? s, PointerEventArgs e)
        {
            var edge = Pick(e.GetPosition(this), out _);
            _v.HoverEdge = edge;
            if (edge != null) { _v.ShowEdgeTip(edge.Tip, e.GetPosition(_v._viewport)); Cursor = new Cursor(StandardCursorType.Hand); }
            else { _v.HideEdgeTip(); Cursor = Cursor.Default; }
        }

        private void OnPressed(object? s, PointerPressedEventArgs e)
        {
            var edge = Pick(e.GetPosition(this), out _);
            if (edge == null) return;   // bubble to viewport → pan
            _v.Focus(); _v.SelectEdge(edge); e.Handled = true;
            if (edge.IsCv) _v.EditCvEdge(edge);   // CV edges open their depth/mode popover
        }

        public override void Render(DrawingContext ctx)
        {
            // Fill the whole layer transparent so hit-testing covers it edge-to-edge. Without
            // this a bare Control is only hittable where it actually drew pixels — i.e. on the
            // dashes of a CV cable, so its gaps (and the animation) made edges hard to grab.
            // Pick() then decides within a generous band; empty clicks bubble through to pan.
            ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

            foreach (var e in _v._edgeList)
            {
                var (a, c1, c2, b) = Curve(e);
                bool audio = e.FromKind == PortKind.Audio;
                bool cv = e.FromKind == PortKind.Cv;
                bool hot = ReferenceEquals(e, _v._selectedEdge) || ReferenceEquals(e, _v._hoverEdge);
                double sc = Math.Clamp(_v._scale, 0.6, 1.4);
                IBrush stroke = cv ? Brass : audio ? Sage : Slate;
                if (hot) stroke = cv ? BrassBright : Text1;   // lighter when hover/selected
                double width = ((cv ? 1.5 : audio ? 3 : 2) + (hot ? 1 : 0)) * sc;
                double opacity = 1.0;
                if (audio && _v.Pulse > 0) opacity = 0.6 + 0.4 * (0.5 + 0.5 * Math.Sin(_v.Pulse * Math.PI * 2));
                var pen = new Pen(stroke, width) { LineCap = PenLineCap.Round };
                if (cv) pen.DashStyle = new DashStyle(new double[] { 4, 4 }, -_v.CvPhase * 24);   // dash-flow
                var geo = new StreamGeometry();
                using (var g = geo.Open()) { g.BeginFigure(a, false); g.CubicBezierTo(c1, c2, b); g.EndFigure(false); }
                using (ctx.PushOpacity(opacity)) ctx.DrawGeometry(null, pen, geo);
            }

            // In-progress connection drag (P1 #13): a dashed brass rubber-band + target ring.
            if (_v._connecting)
            {
                var a = ToScreen(_v._connectStartWorld);
                var b = _v._connectCursor;
                var pen = new Pen(BrassBright, 1.5) { LineCap = PenLineCap.Round, DashStyle = new DashStyle(new double[] { 4, 4 }, -_v.CvPhase * 24) };
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(a, false);
                    double dx = Math.Max(30, Math.Abs(b.X - a.X) * 0.5);
                    g.CubicBezierTo(new Point(a.X + dx, a.Y), new Point(b.X - dx, b.Y), b);
                    g.EndFigure(false);
                }
                ctx.DrawGeometry(null, pen, geo);
                ctx.DrawEllipse(null, new Pen(BrassBright, 1.5), b, 7, 7);
            }
        }

        private Point ToScreen(Point world) => new(world.X * _v._scale + _v._pan.X, world.Y * _v._scale + _v._pan.Y);
    }

    private sealed class MiniMap : Control
    {
        private readonly ModularView _v;
        public MiniMap(ModularView v)
        {
            _v = v; IsHitTestVisible = false;
            Width = 160; Height = 100;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Bottom;
            Margin = new Thickness(0, 0, 14, 14);
        }

        public override void Render(DrawingContext ctx)
        {
            if (_v._nodes.Count == 0) return;
            var bg = new SolidColorBrush(Color.FromArgb(178, 0, 0, 0));
            ctx.DrawRectangle(bg, new Pen(BorderDef, 1), new Rect(0, 0, Width, Height), 5, 5);

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var n in _v._nodes)
            {
                minX = Math.Min(minX, n.World.X); minY = Math.Min(minY, n.World.Y);
                maxX = Math.Max(maxX, n.World.X + n.NodeWidth); maxY = Math.Max(maxY, n.World.Y + (n.View.Bounds.Height > 0 ? n.View.Bounds.Height : 120));
            }
            double gw = maxX - minX, gh = maxY - minY;
            if (gw < 1 || gh < 1) return;
            const double pad = 8;
            double s = Math.Min((Width - pad * 2) / gw, (Height - pad * 2) / gh);
            double offX = (Width - gw * s) / 2, offY = (Height - gh * s) / 2;
            Point M(double wx, double wy) => new((wx - minX) * s + offX, (wy - minY) * s + offY);

            foreach (var n in _v._nodes)
            {
                var p = M(n.World.X, n.World.Y);
                double bw = n.NodeWidth * s, bh = (n.View.Bounds.Height > 0 ? n.View.Bounds.Height : 120) * s;
                var b = new SolidColorBrush(((SolidColorBrush)n.Stripe).Color, 0.7);
                ctx.DrawRectangle(b, null, new Rect(p.X, p.Y, bw, bh), 2, 2);
            }

            // viewport rectangle (world region currently visible)
            double vw = _v._viewport.Bounds.Width, vh = _v._viewport.Bounds.Height;
            if (vw > 1 && vh > 1)
            {
                double wx0 = (0 - _v._pan.X) / _v._scale, wy0 = (0 - _v._pan.Y) / _v._scale;
                double wx1 = (vw - _v._pan.X) / _v._scale, wy1 = (vh - _v._pan.Y) / _v._scale;
                var a = M(wx0, wy0); var c = M(wx1, wy1);
                var rect = new Rect(a, c).Intersect(new Rect(0, 0, Width, Height));
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)), 1), rect, 3, 3);
            }
        }
    }
}
