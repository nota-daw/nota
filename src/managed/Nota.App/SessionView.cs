// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session view (nota-design "Nota Session View", 1a grid + inspector): a clip-launch grid of
// tracks × scenes. Columns take the track's name and colour; a clip shows its name, colour,
// a note / waveform preview and its length in bars, and its progress runs on the slot's own
// clock. Selecting is separate from launching: a click on a cell selects it, a click on its
// triangle launches it. The inspector on the right edits the selected clip, scene or empty
// slot. Groups fold their children into one launchable column; returns and the master close
// the row with mixer-only columns. Live state (queued / playing / recording, meters) is
// polled at the UI clock by MainWindow through UpdateStates. See ARCHITECTURE.md § UI.
//
// Partials: SessionView.Grid.cs (cells, headers, scene rail), SessionView.Mixer.cs (I/O,
// sends, mixer rows), SessionView.Toolbar.cs, SessionView.Inspector.cs.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class SessionView : UserControl
{
    // Geometry (design 1a, Normal density).
    private const double SceneRailW = 176;
    private const double TrackColW = 124;
    private const double ReturnColW = 96;
    private const double MasterColW = 104;
    private const double CellH = 58;
    private const double HeaderH = 44;
    private const double Gap = 3;
    private const double GridPad = 10;
    private const double InspectorW = 300;

    private readonly IAudioEngine _engine;

    // Three horizontally aligned strips (same column widths + spacing) so a column's header,
    // cells and mixer line up: headers pin to the top, the cells scroll vertically in the
    // middle, the stop row + mixer pin to the bottom. All three scroll sideways together.
    private readonly StackPanel _headerRow = new() { Orientation = Orientation.Horizontal, Spacing = Gap, Margin = new Thickness(GridPad, GridPad, GridPad, 0) };
    private readonly StackPanel _cellsRow = new() { Orientation = Orientation.Horizontal, Spacing = Gap, Margin = new Thickness(GridPad, Gap, GridPad, 0) };
    private readonly StackPanel _footerRow = new() { Orientation = Orientation.Horizontal, Spacing = Gap, Margin = new Thickness(GridPad, Gap, GridPad, GridPad) };
    private readonly Border _inspectorHost = new() { Width = InspectorW };

    private readonly List<Column> _cols = new();
    private readonly List<SceneCell> _sceneCells = new();
    private int _sceneCount;

    // UI-only state: which group columns are folded, which mixer sections show, and the
    // selection (a slot on a track or group column, or a scene row).
    private readonly HashSet<int> _collapsed = new();
    private bool _showIO, _showSends = true, _showMixer = true;
    private bool _selectNextOnLaunch;
    private Selection _sel = new(SelKind.Slot, 0, 0);

    // Clipboard: a reference to a slot; a cut moves it on paste.
    private (int TrackId, int Scene, bool Cut)? _clip;

    private int _blinkFrame;
    private bool _blinkOn = true;
    private bool _sessionActive;

    internal enum SelKind { Slot, Scene }
    internal readonly record struct Selection(SelKind Kind, int TrackId, int Scene);

    /// <summary>Raised to edit a filled slot's notes / audio (trackId, scene).</summary>
    public event Action<int, int>? SlotEditRequested;
    /// <summary>Raised when the selection lands on a track's slot (trackId, scene).</summary>
    public event Action<int, int>? SlotSelected;
    /// <summary>Raised after a slot is copied into the arrangement (M5-6) or a track is renamed.</summary>
    public event Action? ArrangementChanged;
    /// <summary>A context menu asked for a new track (kind, the track to place it after, or -1 = at the end).</summary>
    public event Action<NewTrackKind, int>? AddTrackRequested;
    /// <summary>A one-line status message for the main window's status bar.</summary>
    public event Action<string>? Status;
    /// <summary>Raised when a browser item is dropped on a slot (M7-5): item, track id,
    /// scene, and whether the slot's track is an instrument track.</summary>
    public event Action<BrowserItem, int, int, bool>? ItemDropped;
    private void RaiseDrop(BrowserItem item, int trackId, int scene, bool instrument)
        => ItemDropped?.Invoke(item, trackId, scene, instrument);

    public SessionView(IAudioEngine engine)
    {
        _engine = engine;
        Focusable = true;
        FocusAdorner = null;

        var vScroll = new ScrollViewer
        {
            Content = _cellsRow,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var inner = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        inner.Children.Add(_headerRow);
        Grid.SetRow(vScroll, 1); inner.Children.Add(vScroll);
        Grid.SetRow(_footerRow, 2); inner.Children.Add(_footerRow);
        var hScroll = new ScrollViewer
        {
            Content = inner,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var gridArea = new Border { Background = NotaPalette.BgSunken, Child = hScroll };
        // Right-click on the empty part of the grid: add a track (cells and headers handle their own).
        gridArea.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(gridArea).Properties.IsRightButtonPressed) return;
            // Only on the background — a fader's right-click belongs to MIDI Learn.
            if (e.Source is not (Panel or Border or Avalonia.Controls.Presenters.ScrollContentPresenter or ScrollViewer or BlankCell)) return;
            e.Handled = true;
            ShowAddTrackMenu(gridArea, -1);
        };

        _inspectorHost.Background = NotaPalette.Panel;
        _inspectorHost.BorderBrush = NotaPalette.BorderDefault;
        _inspectorHost.BorderThickness = new Thickness(1, 0, 0, 0);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        body.Children.Add(gridArea);
        Grid.SetColumn(_inspectorHost, 1); body.Children.Add(_inspectorHost);

        var root = new DockPanel();
        var bar = BuildToolbar();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(body);
        Content = root;

        // A click anywhere in the view takes keyboard focus, so arrows / Enter / ⌘C… reach it.
        AddHandler(PointerPressedEvent, (_, _) => Focus(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    // ---- model ------------------------------------------------------------------

    internal enum ColKind { Track, Group, Return, Master }

    private sealed class Column
    {
        public ColKind Kind;
        public int TrackId;                 // master: 0
        public string Name = "";
        public string KindLabel = "";
        public int ColorIndex;
        public bool IsInstrument;
        public bool IsAudio;
        public int GroupId = -1;
        public List<int> Children = new();  // group: every launchable track below it
        public double Width;
        public ColumnHeader? Header;
        public readonly List<Control> Cells = new();
        public MixerParts? Mixer;
        public SolidColorBrush Brush => ArrangementView.TrackBrush(ColorIndex);
    }

    private Column? ColumnFor(int trackId) => _cols.FirstOrDefault(c => c.TrackId == trackId && c.Kind is ColKind.Track or ColKind.Group);
    private IEnumerable<Column> LaunchCols => _cols.Where(c => c.Kind == ColKind.Track);
    private List<Column> NavCols => _cols.Where(c => c.Kind is ColKind.Track or ColKind.Group).ToList();

    public void Refresh()
    {
        _headerRow.Children.Clear();
        _cellsRow.Children.Clear();
        _footerRow.Children.Clear();
        _cols.Clear();
        _sceneCells.Clear();
        _sceneCount = _engine.SceneCount;

        BuildColumns();
        AddSceneRail();
        foreach (var c in _cols) AddColumn(c);

        // Keep the selection on something that still exists.
        if (_sel.Scene >= _sceneCount) _sel = _sel with { Scene = Math.Max(0, _sceneCount - 1) };
        if (_sel.Kind == SelKind.Slot && ColumnFor(_sel.TrackId) is null)
            _sel = NavCols.FirstOrDefault() is { } first ? _sel with { TrackId = first.TrackId } : new Selection(SelKind.Scene, 0, _sel.Scene);

        SyncToolbar();
        UpdateStates();
        RebuildInspector();
    }

    private void BuildColumns()
    {
        int n = _engine.TrackCount;
        var infos = new List<NotaTrackInfo>();
        for (int i = 0; i < n; i++) if (_engine.TryGetTrackInfo(i, out var ti)) infos.Add(ti);
        var byId = infos.ToDictionary(t => t.Id);

        bool Hidden(NotaTrackInfo t)
        {
            int g = t.GroupId, guard = 0;
            while (g > 0 && guard++ < 32)
            {
                if (_collapsed.Contains(g)) return true;
                g = byId.TryGetValue(g, out var p) ? p.GroupId : -1;
            }
            return false;
        }
        bool Under(NotaTrackInfo t, int groupId)
        {
            int g = t.GroupId, guard = 0;
            while (g > 0 && guard++ < 32) { if (g == groupId) return true; g = byId.TryGetValue(g, out var p) ? p.GroupId : -1; }
            return false;
        }
        string Parent(NotaTrackInfo t)
            => t.GroupId > 0 && byId.TryGetValue(t.GroupId, out var g) ? " · " + TrackNames.Of(_engine, g).ToUpperInvariant() : "";

        foreach (var ti in infos)
        {
            if (ti.IsReturn || Hidden(ti)) continue;
            var col = new Column
            {
                TrackId = ti.Id,
                Name = TrackNames.Of(_engine, ti),
                ColorIndex = ArrangementView.EffectiveColorIndex(_engine, ti.Id),
                GroupId = ti.GroupId,
                Width = TrackColW,
            };
            if (ti.IsGroup)
            {
                col.Kind = ColKind.Group;
                col.Children = infos.Where(c => !c.IsGroup && !c.IsReturn && Under(c, ti.Id)).Select(c => c.Id).ToList();
                col.KindLabel = $"GROUP · {col.Children.Count}{Parent(ti)}";
            }
            else
            {
                col.Kind = ColKind.Track;
                col.IsInstrument = ti.IsInstrument;
                col.IsAudio = ti.Type == 0;
                col.KindLabel = (ti.IsInstrument ? "MIDI" : "AUDIO") + Parent(ti);
            }
            _cols.Add(col);
        }
        int r = 0;
        foreach (var ti in infos.Where(t => t.IsReturn))
        {
            _cols.Add(new Column
            {
                Kind = ColKind.Return, TrackId = ti.Id, Name = TrackNames.Of(_engine, ti), KindLabel = "RETURN",
                ColorIndex = ArrangementView.ReturnColorIndex(r++), Width = ReturnColW,
            });
        }
        _cols.Add(new Column { Kind = ColKind.Master, TrackId = 0, Name = "Master", KindLabel = "OUT 1/2", ColorIndex = -1, Width = MasterColW });
    }

    // ---- live state ---------------------------------------------------------------

    /// <summary>Re-reads live slot state, progress and meters and repaints (no rebuild).</summary>
    public void UpdateStates()
    {
        ReadRecordTarget();
        _blinkFrame++;
        _blinkOn = (_blinkFrame / 8) % 2 == 0;   // ~2 Hz at the 30 Hz UI clock
        bool active = false;
        foreach (var c in _cols)
        {
            if (c.Kind != ColKind.Track) continue;
            if (_engine.SessionPlayingSlot(c.TrackId) >= 0) active = true;
        }
        for (int s = 0; s < _sceneCount && !active; s++)
            foreach (var c in LaunchCols)
                if (_engine.SessionSlotState(c.TrackId, s) >= 2) { active = true; break; }
        _sessionActive = active;

        // Cells repaint only when their state moved or while they animate (progress, blink).
        foreach (var c in _cols)
        {
            foreach (var cell in c.Cells) (cell as ILiveCell)?.Tick();
            c.Header?.Sync();
            c.Mixer?.UpdateMeter();
        }
        foreach (var sc in _sceneCells) sc.Tick();
        UpdateToolbar();
        UpdateInspectorLive();
    }

    // ---- selection + commands -----------------------------------------------------

    private void Select(Selection sel)
    {
        var prev = _sel;
        _sel = sel;
        InvalidateAll();
        RebuildInspector();
        if (sel.Kind == SelKind.Slot && (prev.Kind != SelKind.Slot || prev.TrackId != sel.TrackId) && ColumnFor(sel.TrackId) is not null)
            SlotSelected?.Invoke(sel.TrackId, sel.Scene);
    }

    private void InvalidateAll()
    {
        foreach (var c in _cols) { foreach (var cell in c.Cells) cell.InvalidateVisual(); c.Header?.Sync(); }
        foreach (var sc in _sceneCells) sc.InvalidateVisual();
    }

    // Rebuild one slot's cell (after an edit to it) instead of the whole grid, plus the group
    // slots that summarise it.
    private void RefreshSlot(int trackId, int scene)
    {
        if (ColumnFor(trackId) is not { Kind: ColKind.Track } c || scene < 0 || scene >= c.Cells.Count) { Refresh(); return; }
        ReplaceCell(c.Cells, scene, new SlotCell(this, c, scene));
        foreach (var g in _cols)
            if (g.Kind == ColKind.Group && g.Children.Contains(trackId)) ReplaceCell(g.Cells, scene, new GroupCell(this, g, scene));
        RebuildInspector();
    }

    private void RefreshScene(int scene)
    {
        if (scene < 0 || scene >= _sceneCells.Count) { Refresh(); return; }
        var cell = new SceneCell(this, scene);
        var panel = (Panel)_sceneCells[scene].Parent!;
        panel.Children[panel.Children.IndexOf(_sceneCells[scene])] = cell;
        _sceneCells[scene] = cell;
        RebuildInspector();
    }

    private static void ReplaceCell(List<Control> cells, int index, Control cell)
    {
        var panel = (Panel)cells[index].Parent!;
        panel.Children[panel.Children.IndexOf(cells[index])] = cell;
        cells[index] = cell;
    }

    private void Say(string text) => Status?.Invoke(text);

    private bool SlotFilled(int trackId, int scene) => _engine.SessionSlotState(trackId, scene) != 0;

    private void LaunchSceneRow(int scene)
    {
        _engine.LaunchScene(scene);
        Select(new Selection(SelKind.Scene, 0, _selectNextOnLaunch ? Math.Min(scene + 1, _sceneCount - 1) : scene));
    }

    private void LaunchGroup(Column g, int scene)
    {
        foreach (int id in g.Children) _engine.LaunchSlot(id, scene);
    }

    private void StopColumn(Column c)
    {
        if (c.Kind == ColKind.Group) foreach (int id in c.Children) _engine.StopSlot(id);
        else _engine.StopSlot(c.TrackId);
    }

    private void InsertMidiClip(int trackId, int scene)
    {
        _engine.AddSessionMidiClip(trackId, scene, 4.0);
        RefreshSlot(trackId, scene);
    }

    /// <summary>Enter: launch the selected slot or scene.</summary>
    public void LaunchSelection()
    {
        if (_sel.Kind == SelKind.Scene) { LaunchSceneRow(_sel.Scene); return; }
        if (ColumnFor(_sel.TrackId) is { } c)
        {
            if (c.Kind == ColKind.Group) LaunchGroup(c, _sel.Scene);
            else _engine.LaunchSlot(c.TrackId, _sel.Scene);
        }
    }

    /// <summary>⌘C on the selected slot.</summary>
    public bool CopySelection()
    {
        if (_sel.Kind != SelKind.Slot || !SlotFilled(_sel.TrackId, _sel.Scene)) return false;
        _clip = (_sel.TrackId, _sel.Scene, false);
        Say("Copied clip");
        return true;
    }

    /// <summary>⌘X: the clip moves to where it is pasted next.</summary>
    public bool CutSelection()
    {
        if (_sel.Kind != SelKind.Slot || !SlotFilled(_sel.TrackId, _sel.Scene)) return false;
        _clip = (_sel.TrackId, _sel.Scene, true);
        Say("Cut clip — paste to move it");
        return true;
    }

    /// <summary>⌘V onto the selected slot.</summary>
    public bool PasteSelection()
    {
        if (_sel.Kind != SelKind.Slot || _clip is not { } src) return false;
        if (ColumnFor(_sel.TrackId) is not { Kind: ColKind.Track }) return false;
        bool move = src.Cut && (src.TrackId, src.Scene) != (_sel.TrackId, _sel.Scene);
        if (!MoveOrCopySlot(src.TrackId, src.Scene, _sel.TrackId, _sel.Scene, move))
        {
            Say("Paste a MIDI clip onto a MIDI track, audio onto audio.");
            return true;
        }
        if (move) _clip = (_sel.TrackId, _sel.Scene, false);
        Say(move ? "Moved clip" : "Pasted clip");
        return true;
    }

    // Copy a slot onto another (same track kind), and for a move clear the source — one undo
    // step either way. Repaints just the two cells.
    private bool MoveOrCopySlot(int fromTrack, int fromScene, int toTrack, int toScene, bool move)
    {
        _engine.BeginUndoGroup();
        bool ok;
        try
        {
            ok = _engine.CopySessionSlot(fromTrack, fromScene, toTrack, toScene);
            if (ok && move) _engine.ClearSessionSlot(fromTrack, fromScene);
        }
        finally { _engine.EndUndoGroup(); }
        if (!ok) return false;
        if (move) RefreshSlot(fromTrack, fromScene);
        RefreshSlot(toTrack, toScene);
        return true;
    }

    /// <summary>F2: rename the selected clip or scene.</summary>
    public bool RenameSelection()
    {
        if (_sel.Kind == SelKind.Scene)
        {
            int sc = _sel.Scene;
            PromptText(_sceneCells.Count > sc ? _sceneCells[sc] : this, _engine.GetSceneName(sc), t => { _engine.SetSceneName(sc, t); RefreshScene(sc); });
            return true;
        }
        if (ColumnFor(_sel.TrackId) is not { Kind: ColKind.Track } c || !SlotFilled(c.TrackId, _sel.Scene)) return false;
        int s = _sel.Scene, id = c.TrackId;
        PromptText(c.Cells[s], _engine.GetSessionClipName(id, s), t => { _engine.SetSessionClipName(id, s, t); RefreshSlot(id, s); });
        return true;
    }

    /// <summary>⌘D: a scene duplicates below itself; a clip copies into the empty slot below.</summary>
    public bool DuplicateSelection()
    {
        if (_sel.Kind == SelKind.Scene)
        {
            int s = _engine.DuplicateScene(_sel.Scene);
            if (s < 0) return false;
            Refresh();
            Select(new Selection(SelKind.Scene, 0, s));
            Say("Duplicated scene");
            return true;
        }
        int below = _sel.Scene + 1;
        if (!SlotFilled(_sel.TrackId, _sel.Scene)) return false;
        if (below >= _sceneCount || SlotFilled(_sel.TrackId, below)) { Say("The slot below is taken."); return true; }
        MoveOrCopySlot(_sel.TrackId, _sel.Scene, _sel.TrackId, below, move: false);
        Select(_sel with { Scene = below });
        Say("Duplicated clip");
        return true;
    }

    /// <summary>⌫: delete the selected clip or scene.</summary>
    public bool DeleteSelection()
    {
        if (_sel.Kind == SelKind.Scene)
        {
            if (!_engine.RemoveScene(_sel.Scene)) return false;
            Refresh();
            Say("Deleted scene");
            return true;
        }
        if (!_engine.ClearSessionSlot(_sel.TrackId, _sel.Scene)) return false;
        RefreshSlot(_sel.TrackId, _sel.Scene);
        Say("Deleted clip");
        return true;
    }

    /// <summary>⌘I: insert an empty scene below the selection.</summary>
    public bool InsertSceneAtSelection()
    {
        int s = _engine.InsertScene(Math.Min(_sel.Scene + 1, _sceneCount));
        if (s < 0) return false;
        Refresh();
        Select(new Selection(SelKind.Scene, 0, s));
        Say("Inserted scene");
        return true;
    }

    private void CaptureScene()
    {
        int s = _engine.CaptureScene(Math.Min(_sel.Scene + 1, _sceneCount));
        if (s < 0) return;
        if (string.IsNullOrEmpty(_engine.GetSceneName(s))) _engine.SetSceneName(s, "Captured");
        Refresh();
        Select(new Selection(SelKind.Scene, 0, s));
        Say("Captured the playing clips into a new scene");
    }

    private void ToArrangement(int trackId, int scene)
    {
        if (_engine.SessionSlotToArrangement(trackId, scene, _engine.PositionBeats) < 0) return;
        ArrangementChanged?.Invoke();
        Say("Copied clip to the arrangement at the playhead");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (HandleKey(e)) { e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    private bool HandleKey(KeyEventArgs e)
    {
        if (e.Source is TextBox) return false;
        bool mod = ArrangementView.IsPrimaryDown(e.KeyModifiers);
        bool other = (e.KeyModifiers & (KeyModifiers.Alt | KeyModifiers.Shift)) != 0;
        var nav = NavCols;
        switch (e.Key)
        {
            case Key.Up or Key.Down or Key.Left or Key.Right when !mod:
                Move(e.Key, nav);
                return true;
            case Key.Back or Key.Delete when !mod:
                DeleteSelection();
                return true;
            case Key.F2 when !mod:
                RenameSelection();
                return true;
        }
        if (!mod || other) return false;
        return e.Key switch
        {
            Key.C => CopySelection(),
            Key.X => CutSelection(),
            Key.V => PasteSelection(),
            Key.D => DuplicateSelection(),
            Key.I => InsertSceneAtSelection(),
            _ => false,
        };
    }

    private void Move(Key key, List<Column> nav)
    {
        int s = _sel.Scene, last = Math.Max(0, _sceneCount - 1);
        if (_sel.Kind == SelKind.Scene)
        {
            if (key == Key.Up) Select(_sel with { Scene = Math.Max(0, s - 1) });
            else if (key == Key.Down) Select(_sel with { Scene = Math.Min(last, s + 1) });
            else if (key == Key.Right && nav.Count > 0) Select(new Selection(SelKind.Slot, nav[0].TrackId, s));
            return;
        }
        int i = Math.Max(0, nav.FindIndex(c => c.TrackId == _sel.TrackId));
        if (key == Key.Up) s = Math.Max(0, s - 1);
        if (key == Key.Down) s = Math.Min(last, s + 1);
        if (key == Key.Left) { if (i == 0) { Select(new Selection(SelKind.Scene, 0, s)); return; } i--; }
        if (key == Key.Right) i = Math.Min(nav.Count - 1, i + 1);
        if (nav.Count > 0) Select(new Selection(SelKind.Slot, nav[i].TrackId, s));
    }

    // ---- shared paint helpers -------------------------------------------------------

    // A clip's colour: its own palette index, else its track's.
    private SolidColorBrush ClipBrush(Column c, int scene)
        => _engine.TryGetSessionClipProps(c.TrackId, scene, out var p) && p.Color >= 0 ? ArrangementView.TrackBrush(p.Color) : c.Brush;

    private string ClipName(Column c, int scene)
    {
        string n = _engine.GetSessionClipName(c.TrackId, scene);
        return n.Length > 0 ? n : $"{c.Name} {scene + 1}";
    }

    private string SceneName(int scene)
    {
        string n = _engine.GetSceneName(scene);
        return n.Length > 0 ? n : $"Scene {scene + 1}";
    }

    /// <summary>"2 bars", "3 beats", "2.2 bars" — a clip length as the grid shows it.</summary>
    internal static string FormatLength(double beats)
    {
        if (beats <= 0) return "";
        if (Math.Abs(beats % 4) < 1e-6) { int n = (int)Math.Round(beats / 4); return n == 1 ? "1 bar" : $"{n} bars"; }
        if (beats < 4) return NotaNum.Str(beats, "0.##") + (Math.Abs(beats - 1) < 1e-6 ? " beat" : " beats");
        return $"{(int)(beats / 4)}.{NotaNum.Str(beats % 4, "0.##")} bars";
    }

    /// <summary>bars.beats.sixteenths, 1-based, the way positions read in the transport.</summary>
    internal static string FormatPosition(double beats)
    {
        beats = Math.Max(0, beats);
        int bar = (int)(beats / 4) + 1, beat = (int)(beats % 4) + 1, six = (int)(beats % 1 * 4) + 1;
        return $"{bar}.{beat}.{six}";
    }

    /// <summary>A duration as bars.beats.sixteenths (0-based), like a follow time "2.0.0".</summary>
    internal static string FormatDuration(double beats)
    {
        beats = Math.Max(0, beats);
        return $"{(int)(beats / 4)}.{(int)(beats % 4)}.{(int)Math.Round(beats % 1 * 4)}";
    }
}
