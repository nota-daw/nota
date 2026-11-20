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
using Nota.App.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Presentation;
using Nota.Application.Localization;

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
    private const double InspectorW = 260;

    private readonly IAudioEngine _engine;

    // Three aligned strips (same column widths + spacing) so a column's header, cells and mixer
    // line up: headers pin to the top, the cells scroll vertically in the middle, the stop row +
    // mixer pin to the bottom, all three scroll sideways together. Scenes across (specs/108)
    // turns them: headers pin left, cells scroll sideways, stop + mixer pin right.
    private readonly StackPanel _headerRow = new() { Orientation = Orientation.Horizontal, Spacing = Gap, Margin = new Thickness(GridPad, GridPad, GridPad, 0) };
    private readonly StackPanel _cellsRow = new() { Orientation = Orientation.Horizontal, Spacing = Gap, Margin = new Thickness(GridPad, Gap, GridPad, 0) };
    private readonly StackPanel _footerRow = new() { Orientation = Orientation.Horizontal, Spacing = Gap, Margin = new Thickness(GridPad, Gap, GridPad, GridPad) };
    private readonly Border _inspectorHost = new() { Width = InspectorW };
    private readonly ScrollViewer _cellScroll = new();
    private readonly ScrollViewer _outerScroll = new();
    private readonly Grid _inner = new();

    // Whether the inspector column shows (Settings.SessionInspectorOpen, remembered per machine).
    private static bool InspectorOpen =>
        App.Services?.GetService(typeof(ISettingsService)) is ISettingsService s ? s.Current.SessionInspectorOpen : true;

    private void SetInspectorOpen(bool open)
    {
        _inspectorHost.IsVisible = open;
        if (App.Services?.GetService(typeof(ISettingsService)) is ISettingsService s && s.Current.SessionInspectorOpen != open)
        {
            s.Current.SessionInspectorOpen = open;
            s.Save();
        }
    }

    private readonly List<Column> _cols = new();
    private readonly List<SceneCell> _sceneCells = new();
    private int _sceneCount;

    // UI-only state: which group columns are folded, which mixer sections show, and the
    // selection (a slot on a track or group column, a scene row, or a Return / Master column).
    private readonly HashSet<int> _collapsed = new();
    private bool _showIO, _showSends = true, _showMixer = true;
    private bool _selectNextOnLaunch;
    private Selection _sel = new(SelKind.Slot, 0, 0);

    private int _blinkFrame;
    private bool _blinkOn = true;
    private bool _sessionActive;

    internal enum SelKind { Slot, Scene, Bus }   // Bus: a Return / Master column, which has no slots (#314)
    internal readonly record struct Selection(SelKind Kind, int TrackId, int Scene);

    /// <summary>Raised to edit a filled slot's notes / audio (trackId, scene).</summary>
    public event Action<int, int>? SlotEditRequested;
    /// <summary>Raised when the selection lands on another slot, or on a Return / Master column
    /// (trackId, scene).</summary>
    public event Action<int, int>? SlotSelected;
    /// <summary>Set by the main window: what an audio take's Locate submenu does (spec 092).</summary>
    internal LocateHooks? Locate { get; set; }
    /// <summary>Raised after slots are copied / moved into the arrangement (M5-6) or a track is renamed.</summary>
    public event Action? ArrangementChanged;
    /// <summary>A column's menu asked for Show in Modular (specs/095).</summary>
    public event Action<int>? ShowInModularRequested;
    /// <summary>A context menu asked for a new track (kind, the track to place it after, or -1 = at the end).</summary>
    public event Action<NewTrackKind, int>? AddTrackRequested;
    /// <summary>A one-line status message for the main window's status bar.</summary>
    public event Action<string>? Status;
    /// <summary>Raised when a browser item is dropped on a slot (M7-5): item, track id,
    /// scene, and whether the slot's track is an instrument track.</summary>
    public event Action<BrowserItem, int, int, bool>? ItemDropped;
    private void RaiseDrop(BrowserItem item, int trackId, int scene, bool instrument)
        => ItemDropped?.Invoke(item, trackId, scene, instrument);

    /// <summary>A device card dropped on a column (specs/043): the host moves it there.</summary>
    public event Action<DeviceMoveRequest>? DeviceDropped;

    // What a drag over a column's header or slot may do: a Browser drag copies in (a header takes
    // anything but the master; a slot what BrowserView accepts), a device card moves (⌥ copies)
    // onto a track that can take it.
    private DragDropEffects DragEffectFor(DragEventArgs e, Column c, bool header)
    {
        if (DeviceDrag.IsActive)
        {
            int id = c.Kind == ColKind.Master ? _engine.MasterTrackId : c.TrackId;
            if (!DeviceDrag.CanTake(_engine, id)) return DragDropEffects.None;
            return (e.KeyModifiers & KeyModifiers.Alt) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
        }
        if (header) return BrowserView.IsBrowserDrag && c.Kind != ColKind.Master ? DragDropEffects.Copy : DragDropEffects.None;
        return BrowserView.IsAcceptableDrag(e) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private bool TryDeviceDrop(DragEventArgs e, Column c)
    {
        if (!DeviceDrag.IsActive) return false;
        int id = c.Kind == ColKind.Master ? _engine.MasterTrackId : c.TrackId;
        if (DeviceDrag.CanTake(_engine, id) && DeviceDrag.RequestFor(id, e.KeyModifiers) is { } r) DeviceDropped?.Invoke(r);
        e.Handled = true;
        return true;
    }

    // What can start a track from a drop off the columns: an instrument or an instrument preset.
    private static bool StartsTrack(BrowserItem item)
        => item.Kind is BrowserItemKind.BuiltinInstrument or BrowserItemKind.PluginInstrument or BrowserItemKind.Preset;

    public SessionView(IAudioEngine engine)
    {
        ValueEntry.SetScope(this, true);   // Tab in a value box walks the values shown here
        _engine = engine;
        // The inspector's buttons name their keys (specs/096): redraw them when a key changes.
        AttachedToVisualTree += (_, _) => Nota.App.Input.Keys.Bindings.Changed += RebuildInspectorKeys;
        DetachedFromVisualTree += (_, _) => Nota.App.Input.Keys.Bindings.Changed -= RebuildInspectorKeys;
        // Preferences → Session grid turns the view at once (specs/108).
        AttachedToVisualTree += (_, _) => { if (SettingsSvc is { } st) st.Changed += OnSettingsChanged; if (AcrossSetting != _across) Refresh(); };
        DetachedFromVisualTree += (_, _) => { if (SettingsSvc is { } st) st.Changed -= OnSettingsChanged; };
        Focusable = true;
        FocusAdorner = null;

        // The cells scroll along the scenes' axis between the pinned strips; the whole grid scrolls
        // the other way (SessionView.Axis.cs lays them out per orientation, specs/108).
        _cellScroll.Content = _cellsRow;
        _inner.Children.Add(_headerRow);
        _inner.Children.Add(_cellScroll);
        _inner.Children.Add(_footerRow);
        _outerScroll.Content = _inner;
        ApplyAxis();
        var gridArea = new Border { Background = NotaPalette.BgSunken, Child = _outerScroll, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
        // A Browser drop on the empty part of the grid — past the last column (the last row when
        // the grid is turned), beyond the scenes — starts a new track (#313); cells and headers
        // take their own drops first.
        DragDrop.SetAllowDrop(gridArea, true);
        DragDrop.AddDragOverHandler(gridArea, (_, e) =>
        {
            bool ok = BrowserView.IsBrowserDrag && BrowserView.CurrentDrag is { } it && StartsTrack(it);
            e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            gridArea.BorderBrush = ok ? NotaPalette.AccentDim : Brushes.Transparent;
        });
        DragDrop.AddDragLeaveHandler(gridArea, (_, _) => gridArea.BorderBrush = Brushes.Transparent);
        DragDrop.AddDropHandler(gridArea, (_, e) =>
        {
            gridArea.BorderBrush = Brushes.Transparent;
            var items = BrowserView.DroppedItems(e);
            if (items.Count > 0 && StartsTrack(items[0])) { RaiseDrop(items[0], -1, -1, false); e.Handled = true; }
        });
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

        _inspectorHost.IsVisible = InspectorOpen;

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
    // A Return / Master column's track id (the Master column itself is built with id 0).
    private int BusId(Column c) => c.Kind == ColKind.Master ? _engine.MasterTrackId : c.TrackId;
    private Column? BusColumn(int trackId) => _cols.FirstOrDefault(c => c.Kind is ColKind.Return or ColKind.Master && BusId(c) == trackId);

    public void Refresh()
    {
        _headerRow.Children.Clear();
        _cellsRow.Children.Clear();
        _footerRow.Children.Clear();
        _cols.Clear();
        _sceneCells.Clear();
        _sceneCount = _engine.SceneCount;
        ApplyAxis();

        BuildColumns();
        AddSceneRail();
        foreach (var c in _cols) AddColumn(c);

        // Keep the selection on something that still exists.
        if (_sel.Scene >= _sceneCount) _sel = _sel with { Scene = Math.Max(0, _sceneCount - 1) };
        if (_sel.Kind == SelKind.Bus && BusColumn(_sel.TrackId) is null) _sel = new Selection(SelKind.Slot, 0, _sel.Scene);
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
            => t.GroupId > 0 && byId.TryGetValue(t.GroupId, out var g) ? " · " + TrackNames.Of(_engine, g) : "";

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
                col.KindLabel = Loc.F("session.group", col.Children.Count, Parent(ti));
            }
            else
            {
                col.Kind = ColKind.Track;
                col.IsInstrument = ti.IsInstrument;
                col.IsAudio = ti.Type == 0;
                col.KindLabel = (ti.IsInstrument ? "MIDI" : Loc.T("common.audio")) + Parent(ti);
            }
            _cols.Add(col);
        }
        int r = 0;
        foreach (var ti in infos.Where(t => t.IsReturn))
        {
            _cols.Add(new Column
            {
                Kind = ColKind.Return, TrackId = ti.Id, Name = TrackNames.Of(_engine, ti), KindLabel = Loc.T("common.return"),
                ColorIndex = ArrangementView.ReturnColorIndex(r++), Width = ReturnColW,
            });
        }
        _cols.Add(new Column { Kind = ColKind.Master, TrackId = 0, Name = Loc.T("common.master"), KindLabel = "Out 1/2", ColorIndex = -1, Width = MasterColW });
    }

    // ---- live state ---------------------------------------------------------------

    /// <summary>Re-reads live slot state, progress and meters and repaints (no rebuild).</summary>
    public void UpdateStates()
    {
        // A take that just ended (Stop / Space in the transport, a fixed length running out)
        // leaves its cell drawing the notes it was built with — none, for a new slot — so
        // rebuild that slot from what was recorded (#156).
        int endedTrack = _recTrack, endedScene = _recScene;
        ReadRecordTarget();
        if (endedScene >= 0 && (endedTrack != _recTrack || endedScene != _recScene)) RefreshSlot(endedTrack, endedScene);
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

        SnapshotMixInfo();   // mixer sections and armed slots follow the engine (specs/054)
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

    // renotify: a click on the slot that is already selected still raises SlotSelected, so a
    // filled slot can reopen its clip after the detail panel moved to Devices (#334).
    private void Select(Selection sel, bool renotify = false)
    {
        var prev = _sel;
        _sel = sel;
        InvalidateAll();
        RebuildInspector();
        bool selectable = sel.Kind == SelKind.Slot ? ColumnFor(sel.TrackId) is not null : sel.Kind == SelKind.Bus && BusColumn(sel.TrackId) is not null;
        if (selectable && (prev != sel || renotify))
            SlotSelected?.Invoke(sel.TrackId, sel.Scene);
    }

    /// <summary>What is selected: a slot, a scene row or a Return / Master column (specs/107: the
    /// controller's track step keeps the row, Remote outlines it).</summary>
    internal Selection CurrentSelection => _sel;

    /// <summary>The track and group columns in order, as shown — a group folded here hides its
    /// tracks (the Session keeps its own folds): what Track ◀ / ▶ walk while the Session shows.</summary>
    internal List<int> ColumnTrackIds() => NavCols.Select(c => c.TrackId).ToList();

    /// <summary>Selects the slot at (<paramref name="trackId"/>, <paramref name="scene"/>) — e.g. where a browser drop landed.</summary>
    internal void SelectSlot(int trackId, int scene)
    {
        if (ColumnFor(trackId) is not null) Select(new Selection(SelKind.Slot, trackId, scene));
    }

    private void InvalidateAll()
    {
        foreach (var c in _cols) { foreach (var cell in c.Cells) cell.InvalidateVisual(); c.Header?.Sync(); }
        foreach (var sc in _sceneCells) sc.InvalidateVisual();
    }

    // Rebuild one slot's cell (after an edit to it) instead of the whole grid, plus the group
    // slots that summarise it.
    // inspector: false while the inspector itself is being edited (a drag would lose its control).
    private void RefreshSlot(int trackId, int scene, bool inspector = true)
    {
        if (ColumnFor(trackId) is not { Kind: ColKind.Track } c || scene < 0 || scene >= c.Cells.Count) { Refresh(); return; }
        ReplaceCell(c.Cells, scene, new SlotCell(this, c, scene));
        foreach (var g in _cols)
            if (g.Kind == ColKind.Group && g.Children.Contains(trackId)) ReplaceCell(g.Cells, scene, new GroupCell(this, g, scene));
        if (inspector) RebuildInspector();
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

    // #480: withdraw a queued launch before its boundary — Alt+click on a queued launch button, or
    // "Cancel launch" in the menu. A second plain click keeps it queued, as it always did.
    // Asked of the engine, not inferred from slot state 2: a row that queued only stops, or a queued
    // restart of a slot already playing, is queued too.
    private bool SceneQueued(int scene) => _engine.SessionSceneQueued(scene);

    private void CancelSlotLaunch(int trackId, int scene)
    {
        if (_engine.CancelSlotLaunch(trackId, scene)) Say(Loc.T("session.launch_cancelled"));
        UpdateStates();
    }

    // One call: the engine withdraws every track below the group together, in the same block.
    private void CancelGroupLaunch(Column g, int scene) => CancelSlotLaunch(g.TrackId, scene);

    private void CancelSceneLaunch(int scene)
    {
        if (_engine.CancelSceneLaunch(scene)) Say(Loc.T("session.launch_cancelled"));
        UpdateStates();
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
    /// <summary>The selected slot's track (-1 for a scene) and scene — the palette's context.</summary>
    internal (int TrackId, int Scene) SelectedSlot => (_sel.Kind == SelKind.Slot ? _sel.TrackId : -1, _sel.Scene);

    /// <summary>Launches the selected scene row (or the selected slot's row).</summary>
    internal void LaunchSelectedScene() { LaunchSceneRow(_sel.Scene); UpdateStates(); }

    /// <summary>Stops every playing clip, as the toolbar's Stop All does.</summary>
    internal void StopAll() { _engine.StopAllSession(); UpdateStates(); }

    /// <summary>A step by scene or by track, for the Session commands — a controller's buttons and
    /// encoder (specs/051) — whichever way the grid is turned (specs/108).</summary>
    internal void MoveSelection(SessionStep step) => Move(step, NavCols);

    /// <summary>A controller's launch button: the selected clip starts — or, playing or queued, its
    /// track stops; a scene row launches; an empty slot launches (stops the track or records, as a click).</summary>
    internal void ToggleSelection()
    {
        if (_sel.Kind == SelKind.Slot && ColumnFor(_sel.TrackId) is { Kind: ColKind.Track } c)
        {
            int state = _engine.SessionSlotState(c.TrackId, _sel.Scene);
            if (state == 2) { CancelSlotLaunch(c.TrackId, _sel.Scene); return; }   // queued: withdraw it
            if (state == 3) { StopColumn(c); UpdateStates(); return; }
        }
        LaunchSelection();
    }

    /// <summary>Stop what plays in the selected slot's track (or group).</summary>
    internal void StopSelectedTrack()
    {
        if (_sel.Kind == SelKind.Slot && ColumnFor(_sel.TrackId) is { } c) { StopColumn(c); UpdateStates(); }
    }

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
        if (_sel.Kind == SelKind.Bus) return true;   // a Return / Master has no clip to act on; keep it from reaching the Arrangement
        if (_sel.Kind != SelKind.Slot || !SlotFilled(_sel.TrackId, _sel.Scene)) return false;
        ClipTransfer.TookSlot(_sel.TrackId, _sel.Scene, cut: false);
        Say(Loc.T("session.copied_clip"));
        return true;
    }

    /// <summary>⌘X: the clip moves to where it is pasted next.</summary>
    public bool CutSelection()
    {
        if (_sel.Kind == SelKind.Bus) return true;   // a Return / Master has no clip to act on; keep it from reaching the Arrangement
        if (_sel.Kind != SelKind.Slot || !SlotFilled(_sel.TrackId, _sel.Scene)) return false;
        ClipTransfer.TookSlot(_sel.TrackId, _sel.Scene, cut: true);
        Say(Loc.T("session.cut_clip_paste_to_move"));
        return true;
    }

    /// <summary>True when ⌘V has something to paste: a slot, or clips copied in the arrangement.</summary>
    private bool CanPaste => ClipTransfer.FromArrangement(_engine) || ClipTransfer.Slot is not null;

    /// <summary>⌘V onto the selected slot (clips copied in the arrangement: also onto a scene row).</summary>
    public bool PasteSelection()
    {
        if (_sel.Kind == SelKind.Bus) return true;   // a Return / Master has no clip to act on; keep it from reaching the Arrangement
        if (ClipTransfer.FromArrangement(_engine)) return PasteFromArrangement();
        if (_sel.Kind != SelKind.Slot || ClipTransfer.Slot is not { } src) return false;
        if (ColumnFor(_sel.TrackId) is not { Kind: ColKind.Track }) return false;
        bool move = src.Cut && (src.TrackId, src.Scene) != (_sel.TrackId, _sel.Scene);
        if (!MoveOrCopySlot(src.TrackId, src.Scene, _sel.TrackId, _sel.Scene, move))
        {
            Say(Loc.T("session.paste_a_midi_clip_onto"));
            return true;
        }
        if (move) ClipTransfer.SlotMoved((_sel.TrackId, _sel.Scene));
        Say(move ? Loc.T("session.moved_clip") : Loc.T("session.pasted_clip"));
        return true;
    }

    // The arrangement's clip clipboard into slots from the selected scene down: its top track
    // lands on the selected track column (a scene row or group selected: the clips' own tracks),
    // each track's clips in time order down consecutive slots.
    private bool PasteFromArrangement()
    {
        int dest = _sel.Kind == SelKind.Slot && ColumnFor(_sel.TrackId) is { Kind: ColKind.Track } c ? c.TrackId : -1;
        int n = _engine.ClipboardBlockCount();
        int first = _engine.PasteClipBlockToSession(dest, _sel.Scene);
        if (first < 0)
        {
            Say(Loc.T("session.paste_a_midi_clip_onto"));
            return true;
        }
        Refresh();   // the paste may have added scene rows
        if (dest > 0) Select(new Selection(SelKind.Slot, dest, first));
        Say(n == 1 ? Loc.T("session.pasted_clip_from_the_arrangement") : Loc.F("session.pasted_clips_from_the_arrangement", n));
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
        if (_sel.Kind == SelKind.Bus) return true;   // a Return / Master has no clip to act on; keep it from reaching the Arrangement
        if (_sel.Kind == SelKind.Scene)
        {
            int s = _engine.DuplicateScene(_sel.Scene);
            if (s < 0) return false;
            Refresh();
            Select(new Selection(SelKind.Scene, 0, s));
            Say(Loc.T("session.duplicated_scene"));
            return true;
        }
        int below = _sel.Scene + 1;
        if (_sel.Kind != SelKind.Slot || !SlotFilled(_sel.TrackId, _sel.Scene)) return false;
        if (below >= _sceneCount || SlotFilled(_sel.TrackId, below)) { Say(Loc.T("session.the_slot_below_is_taken")); return true; }
        MoveOrCopySlot(_sel.TrackId, _sel.Scene, _sel.TrackId, below, move: false);
        Select(_sel with { Scene = below });
        Say(Loc.T("session.duplicated_clip"));
        return true;
    }

    /// <summary>⌫: delete the selected clip or scene.</summary>
    public bool DeleteSelection()
    {
        if (_sel.Kind == SelKind.Bus) return true;   // a Return / Master has no clip to act on; keep it from reaching the Arrangement
        if (_sel.Kind == SelKind.Scene)
        {
            if (!_engine.RemoveScene(_sel.Scene)) return false;
            Refresh();
            Say(Loc.T("session.deleted_scene"));
            return true;
        }
        if (_sel.Kind != SelKind.Slot || !_engine.ClearSessionSlot(_sel.TrackId, _sel.Scene)) return false;
        RefreshSlot(_sel.TrackId, _sel.Scene);
        Say(Loc.T("session.deleted_clip"));
        return true;
    }

    /// <summary>⌘I: insert an empty scene below the selection.</summary>
    public bool InsertSceneAtSelection()
    {
        int s = _engine.InsertScene(Math.Min(_sel.Scene + 1, _sceneCount));
        if (s < 0) return false;
        Refresh();
        Select(new Selection(SelKind.Scene, 0, s));
        Say(Loc.T("session.inserted_scene"));
        return true;
    }

    private void CaptureScene()
    {
        int s = _engine.CaptureScene(Math.Min(_sel.Scene + 1, _sceneCount));
        if (s < 0) return;
        if (string.IsNullOrEmpty(_engine.GetSceneName(s))) _engine.SetSceneName(s, Loc.T("session.captured"));
        Refresh();
        Select(new Selection(SelKind.Scene, 0, s));
        Say(Loc.T("session.captured_the_playing_clips_into"));
    }

    // The filled slots a selection covers: a track's slot, a group's slots in that scene, or a
    // whole scene row (every track, folded groups' tracks included).
    private (int trackId, int scene)[] SelectedSlots()
    {
        IEnumerable<int> tracks = _sel.Kind == SelKind.Scene
            ? NavCols.SelectMany(c => c.Kind == ColKind.Group ? c.Children : new List<int> { c.TrackId })
            : ColumnFor(_sel.TrackId) is { } col ? (col.Kind == ColKind.Group ? col.Children : new List<int> { col.TrackId }) : Enumerable.Empty<int>();
        return tracks.Distinct().Where(t => SlotFilled(t, _sel.Scene)).Select(t => (t, _sel.Scene)).ToArray();
    }

    /// <summary>Copy to Arrangement (⌘⇧C): the selected slot, group slot or scene onto the timeline
    /// at the playhead, each clip on its own track — pasted, so it never covers existing clips.</summary>
    public bool CopySelectionToArrangement()
    {
        var slots = SelectedSlots();
        if (slots.Length == 0) return false;
        int placed = _engine.SessionSlotsToArrangement(slots, _engine.PositionBeats);
        if (placed <= 0) return false;
        ArrangementChanged?.Invoke();
        Say(placed == 1 ? Loc.T("session.copied_clip_to_the_arrangement")
                        : Loc.F("session.copied_clips_to_the_arrangement", placed));
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (HandleKey(e)) { e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    private bool HandleKey(KeyEventArgs e)
    {
        if (e.Source is TextBox || Keys.Capturing) return false;
        bool plain = (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) == 0;
        // The arrows walk the selection — fixed list navigation (specs/096 FR-011).
        // Turned by the grid's orientation: they follow what is on screen (specs/108).
        if (plain && e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
        {
            var arrow = e.Key switch { Key.Up => GridArrow.Up, Key.Down => GridArrow.Down, Key.Left => GridArrow.Left, _ => GridArrow.Right };
            Move(SessionGridNav.FromArrow(arrow, _across), NavCols);
            return true;
        }
        // The rest are bindings: the Session's own and the shared editing actions, which act on
        // the selected clip or scene here. Launch (Return) is handled in the window's tunnel.
        return Keys.OwnedBy(e, "edit.delete", "session.rename", "edit.copyToOtherView", "edit.copy", "edit.cut", "edit.paste",
                   "edit.duplicate", "session.insertScene") switch
        {
            "edit.delete" => DeleteSelection() || true,
            "session.rename" => RenameSelection() || true,
            "edit.copyToOtherView" => CopySelectionToArrangement() || true,
            "edit.copy" => CopySelection(),
            "edit.cut" => CutSelection(),
            "edit.paste" => PasteSelection(),
            "edit.duplicate" => DuplicateSelection(),
            "session.insertScene" => InsertSceneAtSelection(),
            _ => false,
        };
    }

    // A selection step by grid meaning (specs/108): the arrows arrive here already turned for the
    // orientation, the Session commands (and controllers) as scene / track steps. The rules live
    // in SessionGridNav; this maps the selection to positions and back.
    private void Move(SessionStep step, List<Column> nav)
    {
        var buses = _cols.Where(c => c.Kind is ColKind.Return or ColKind.Master).ToList();
        var at = _sel.Kind switch
        {
            SelKind.Scene => new GridPos(GridSelKind.Scene, 0, _sel.Scene),
            SelKind.Bus => new GridPos(GridSelKind.Bus, buses.FindIndex(c => BusId(c) == _sel.TrackId), _sel.Scene),
            _ => new GridPos(GridSelKind.Slot, Math.Max(0, nav.FindIndex(c => c.TrackId == _sel.TrackId)), _sel.Scene),
        };
        var to = SessionGridNav.Step(at, step, nav.Count, buses.Count, _sceneCount);
        if (to == at && _sel.Kind != SelKind.Slot) return;
        switch (to.Kind)
        {
            case GridSelKind.Scene: Select(new Selection(SelKind.Scene, 0, to.Scene)); break;
            case GridSelKind.Bus: Select(new Selection(SelKind.Bus, BusId(buses[to.Index]), to.Scene)); break;
            default: if (nav.Count > 0) Select(new Selection(SelKind.Slot, nav[to.Index].TrackId, to.Scene)); break;
        }
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
        return n.Length > 0 ? n : Loc.F("session.scene", scene + 1);
    }

    /// <summary>"1 beat", "0.5 beats", "2.5 beats": a number of beats, fractions allowed.</summary>
    internal static string BeatsText(double beats)
        => Math.Abs(beats - 1) < 1e-6 ? Loc.T("session.one_beat") : Loc.F("session.beats", NotaNum.Str(beats, "0.##"));

    /// <summary>"2 bars", "3 beats", "2.2 bars" — a clip length as the grid shows it.</summary>
    /// <summary>A slot's own next-take length as the grid and inspector show it (#503): "3 bars",
    /// "2 bars 2", "Open"; empty for Global.</summary>
    internal static string TakeLengthText(NotaSlotTakeLength take) => take.TakeMode switch
    {
        SlotTakeMode.Open => Loc.T("session.take_length.open"),
        SlotTakeMode.Fixed => FormatLength(take.Bars * Meter.BeatsPerBar + take.Beats),
        _ => "",
    };

    internal static string FormatLength(double beats)
    {
        if (beats <= 0) return "";
        int bpb = Meter.BeatsPerBar;
        if (Math.Abs(beats % bpb) < 1e-6) { int n = (int)Math.Round(beats / bpb); return Loc.Plural("session.bars", n); }
        if (beats < bpb) return BeatsText(beats);
        return Loc.F("session.bars_n", (int)(beats / bpb), NotaNum.Str(beats % bpb, "0.##"));
    }

    /// <summary>bars.beats.sixteenths, 1-based, the way positions read in the transport.</summary>
    internal static string FormatPosition(double beats)
    {
        beats = Math.Max(0, beats);
        int bpb = Meter.BeatsPerBar;
        int bar = (int)(beats / bpb) + 1, beat = (int)(beats % bpb) + 1, six = (int)(beats % 1 * 4) + 1;
        return $"{bar}.{beat}.{six}";
    }

    /// <summary>A duration as bars.beats.sixteenths (0-based), like a follow time "2.0.0".</summary>
    internal static string FormatDuration(double beats)
    {
        beats = Math.Max(0, beats);
        int bpb = Meter.BeatsPerBar;
        return $"{(int)(beats / bpb)}.{(int)(beats % bpb)}.{(int)Math.Round(beats % 1 * 4)}";
    }
}
