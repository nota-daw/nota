// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The articulation map manager (specs/085, US5: T061–T063, T065). Three columns: the maps (the
// library's, plus any a project track holds that the library doesn't), the open map's
// articulations, and the selected articulation's details — names, colour, group, trigger, offset,
// lead, enabled, default and its MIDI actions in send order.
//
// Edits go to a working copy. Save writes it to the library and puts it on every track holding the
// map's id, as one undo step, and says how many (FR-010); Revert throws the copy away. Switching map
// or closing with unsaved changes asks first. The editing rules (ids never reused, a removed default
// cleared, actions kept in order, a map checked before saving) live in ArticulationMapEdits, where
// the smoke test checks them. Files dropped from Explorer import like Import… does.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nota.Application;
using Nota.Application.Articulations;
using Nota.Application.Localization;

namespace Nota.App;

public sealed class ArticulationMapManager : NotaWindow
{
    private static ArticulationMapManager? _open;

    /// <summary>Shows the manager (one per app), on the map of the track <paramref name="selectedTrack"/>
    /// names when it has one. <paramref name="selectedTrack"/> is asked again for Assign.</summary>
    // The open manager is brought back whatever state it is in (.specify/bugs/articulation-manager-reopen):
    // one that was shown and is no longer visible is replaced, a minimized one (owned, so it has no
    // taskbar button) is restored, and it is shown and activated after the click has finished, as
    // the Lens and Scope windows are, so the clicked window doesn't take activation back.
    internal static void Open(Window owner, IAudioEngine engine, Func<int> selectedTrack)
    {
        var log = App.Services?.GetService(typeof(ILogSink)) as ILogSink;
        if (_open is { _wasShown: true, IsVisible: false } gone)
        {
            log?.Info("Articulation maps: the open manager wasn't visible any more; opening a new one");
            if (_open == gone) _open = null;
        }
        if (_open is { } w)
        {
            log?.Info($"Articulation maps: bringing back the open manager ({w.WindowState}, visible {w.IsVisible})");
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            if (!w.Dirty) w.Reload(w.TrackMapId());
            Dispatcher.UIThread.Post(w.Activate, DispatcherPriority.Background);
            return;
        }
        var m = new ArticulationMapManager(engine, selectedTrack);
        _open = m;
        m.Opened += (_, _) => m._wasShown = true;
        m.Closed += (_, _) => { if (_open == m) _open = null; };
        Dispatcher.UIThread.Post(() => { m.Show(owner); m.Activate(); }, DispatcherPriority.Background);
    }

    private bool _wasShown;

    private const double OffsetRangeMs = ArticulationMapCompiler.MaxOffsetMs, LeadRangeMs = ArticulationMapCompiler.MaxLeadMs;

    private readonly IAudioEngine _engine;
    private readonly IArticulationLibrary? _lib;
    private readonly Func<int> _selectedTrack;

    private List<(ArticulationMap Map, bool OnlyInProject, bool DiffersFromLibrary)> _catalog = new();
    private int _mapIndex = -1;
    private ArticulationMap? _work;        // the open map, being edited
    private string? _savedJson;            // what it was when opened or last saved
    private int _artId;                    // the selected articulation, 0 = none
    private bool _building;                // lists being refilled: ignore their selection events
    private bool _closeConfirmed;
    private string? _flash;                // a one-off status ("Saved · 3 tracks updated") until the next edit

    private readonly ListBox _maps = new() { SelectionMode = SelectionMode.Single };
    private readonly ListBox _arts = new() { SelectionMode = SelectionMode.Single };
    private readonly TextBlock _mapsEmpty = Hint(Loc.T("articulations.manager.empty"));
    private readonly TextBox _mapName = Field();
    private readonly TextBox _instrument = Field();
    private readonly StackPanel _mapHead = new() { Spacing = 8 };
    private readonly ContentControl _details = new();
    private TextBlock? _effective;         // under Note offset: when this articulation's notes play (FR-018)
    private readonly TextBlock _status = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _duplicate, _delete, _export, _assign, _revert, _save;
    private readonly Button _artAdd, _artCopy, _artRemove, _artUp, _artDown;
    private readonly Dictionary<int, (Border Swatch, TextBlock Name, TextBlock Meta, TextBlock Badge)> _rows = new();

    private ArticulationMapManager(IAudioEngine engine, Func<int> selectedTrack)
    {
        _engine = engine;
        _selectedTrack = selectedTrack;
        _lib = App.Services?.GetService(typeof(IArticulationLibrary)) as IArticulationLibrary;

        Title = Loc.T("articulations.manager.title");
        Width = 920;
        Height = 640;
        MinWidth = 760;
        MinHeight = 440;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.Panel;

        // ---- maps column
        var newMap = TextButton(Loc.T("articulations.manager.new"), Loc.T("articulations.manager.new_tip"), NewMap);
        var import = TextButton(Loc.T("articulations.manager.import"), Loc.T("articulations.manager.import_tip"), () => _ = PickAndImport());
        _duplicate = TextButton(Loc.T("articulations.manager.duplicate"), Loc.T("articulations.manager.duplicate_tip"), () => _ = DuplicateMap());
        _delete = TextButton(Loc.T("articulations.manager.delete"), Loc.T("articulations.manager.delete_tip"), () => _ = DeleteMap());
        _maps.SelectionChanged += (_, _) => { if (!_building) _ = SwitchMap(_maps.SelectedIndex); };
        var mapsCol = Column(Loc.T("articulations.manager.maps"),
            new Panel { Children = { _maps, _mapsEmpty } },
            new WrapPanel { Children = { newMap, import, _duplicate, _delete } },
            Hint(Loc.T("articulations.manager.drop_hint")));

        // ---- articulations column
        _mapName.TextChanged += (_, _) => { if (_building || _work is null) return; _work.Name = _mapName.Text ?? ""; Edited(); };
        _instrument.TextChanged += (_, _) => { if (_building || _work is null) return; _work.InstrumentName = _instrument.Text ?? ""; Edited(); };
        ToolTip.SetTip(_mapName, Loc.T("articulations.manager.name_tip"));
        ToolTip.SetTip(_instrument, Loc.T("articulations.manager.instrument_tip"));
        _mapHead.Children.Add(Labelled(Loc.T("articulations.manager.name"), _mapName));
        _mapHead.Children.Add(Labelled(Loc.T("articulations.manager.instrument"), _instrument));
        _arts.SelectionChanged += (_, _) =>
        {
            if (_building || _work is null) return;
            int k = _arts.SelectedIndex;
            _artId = k >= 0 && k < _work.Articulations.Count ? _work.Articulations[k].Id : 0;
            BuildDetails();
            UpdateButtons();
        };
        _artAdd = IconButton(GlyphKind.Plus, Loc.T("articulations.manager.add_tip"), AddArticulation);
        _artCopy = IconButton(GlyphKind.Duplicate, Loc.T("articulations.manager.copy_tip"), CopyArticulation);
        _artRemove = IconButton(GlyphKind.Trash, Loc.T("articulations.manager.remove_tip"), RemoveArticulation);
        _artUp = IconButton(GlyphKind.ChevronUp, Loc.T("articulations.manager.up_tip"), () => MoveArticulation(-1));
        _artDown = IconButton(GlyphKind.ChevronDown, Loc.T("articulations.manager.down_tip"), () => MoveArticulation(1));
        var artsCol = Column(Loc.T("articulations.manager.articulations"),
            _arts,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _artAdd, _artCopy, _artRemove, _artUp, _artDown } },
            null, head: _mapHead);

        // ---- details column
        var detailsScroll = new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var detailsCol = Column(Loc.T("articulations.manager.details"), detailsScroll, null, null);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("220,280,*") };
        Grid.SetColumn(artsCol, 1);
        Grid.SetColumn(detailsCol, 2);
        artsCol.BorderBrush = detailsCol.BorderBrush = NotaPalette.Rule;
        artsCol.BorderThickness = detailsCol.BorderThickness = new Thickness(1, 0, 0, 0);
        columns.Children.Add(mapsCol);
        columns.Children.Add(artsCol);
        columns.Children.Add(detailsCol);

        // ---- footer
        _export = TextButton(Loc.T("articulations.manager.export"), Loc.T("articulations.manager.export_tip"), () => _ = Export());
        _assign = TextButton(Loc.T("articulations.manager.assign"), Loc.T("articulations.manager.assign_tip"), Assign);
        _revert = TextButton(Loc.T("articulations.manager.revert"), Loc.T("articulations.manager.revert_tip"), Revert);
        _save = TextButton(Loc.T("articulations.manager.save"), Loc.T("articulations.manager.save_tip"), Save);
        _save.Classes.Add("primary");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _export, _assign, _revert, _save } };
        var footer = new DockPanel { Margin = new Thickness(16, 10, 16, 14) };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_status);
        var footerHost = new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };

        var root = new DockPanel();
        DockPanel.SetDock(footerHost, Dock.Bottom);
        root.Children.Add(footerHost);
        root.Children.Add(columns);
        HookDrop(root);
        SetBody(root);

        // An undo, the MCP or the inspector can change a track's map while this is open; with no
        // edits pending, coming back to the window shows what is there now.
        Activated += (_, _) => { if (!Dirty) Reload(_work?.Id); else UpdateFooter(); };
        Reload(TrackMapId());
    }

    // ---- state ---------------------------------------------------------------------------------

    private bool Dirty => _work is not null && _savedJson != ArticulationMapFile.Write(_work);
    private bool OnlyInProject => _mapIndex >= 0 && _mapIndex < _catalog.Count && _catalog[_mapIndex].OnlyInProject;
    // The project's copy differs from the library's: Save writes the project's into the library.
    private bool DiffersFromLibrary => _mapIndex >= 0 && _mapIndex < _catalog.Count && _catalog[_mapIndex].DiffersFromLibrary;
    private Articulation? Selected => _work?.Find(_artId);

    // The map of the selected track, to open on.
    private string? TrackMapId()
    {
        int t = _selectedTrack();
        return t > 0 ? ArticulationMapAssignment.Read(_engine, t)?.Id : null;
    }

    // A deep copy through the file format, so the working copy shares nothing with the library's.
    private static ArticulationMap Copy(ArticulationMap m) => ArticulationMapFile.Read(ArticulationMapFile.Write(m), m.Name).Map ?? m.DuplicateAs(m.Name);

    // Refill the map list and open the map with id <paramref name="openId"/> (else the first).
    private void Reload(string? openId)
    {
        _catalog = ArticulationMapUsage.Catalog(_engine, _lib);
        int k = openId is null ? -1 : _catalog.FindIndex(x => x.Map.Id == openId);
        if (k < 0 && _catalog.Count > 0) k = Math.Clamp(_mapIndex, 0, _catalog.Count - 1);
        _building = true;
        _maps.Items.Clear();
        foreach (var (m, onlyInProject, differs) in _catalog) _maps.Items.Add(MapRow(m, onlyInProject, differs));
        _maps.SelectedIndex = k;
        _building = false;
        _mapsEmpty.IsVisible = _catalog.Count == 0;
        Load(k);
    }

    private void Load(int k)
    {
        _mapIndex = k;
        _work = k >= 0 && k < _catalog.Count ? Copy(_catalog[k].Map) : null;
        _savedJson = _work is null ? null : ArticulationMapFile.Write(_work);
        if (_work is not null && _work.Find(_artId) is null) _artId = _work.Articulations.FirstOrDefault()?.Id ?? 0;
        _flash = null;
        _building = true;
        _mapName.Text = _work?.Name ?? "";
        _instrument.Text = _work?.InstrumentName ?? "";
        _building = false;
        _mapHead.IsVisible = _work is not null;
        BuildArticulations();
        BuildDetails();
        UpdateFooter();
    }

    // Something in the working copy changed: the status and buttons follow.
    private void Edited()
    {
        _flash = null;
        UpdateFooter();
    }

    private async System.Threading.Tasks.Task<bool> ConfirmDiscard()
    {
        if (!Dirty || _work is null) return true;
        return await new ConfirmWindow(Loc.T("articulations.manager.title"),
            Loc.F("articulations.manager.discard_confirm", MapLabel(_work)), Loc.T("articulations.manager.discard"), Loc.T("common.cancel")).ShowDialog<bool>(this);
    }

    private async System.Threading.Tasks.Task SwitchMap(int k)
    {
        if (k == _mapIndex) return;
        if (Dirty)
        {
            _building = true;
            _maps.SelectedIndex = _mapIndex;   // stay put until the answer
            _building = false;
            if (!await ConfirmDiscard()) return;
            _building = true;
            _maps.SelectedIndex = k;
            _building = false;
        }
        Load(k);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || !Dirty) return;
        e.Cancel = true;
        Dispatcher.UIThread.Post(async () =>
        {
            if (!await ConfirmDiscard()) return;
            _closeConfirmed = true;
            Close();
        });
    }

    private static string MapLabel(ArticulationMap m) => m.Name.Length > 0 ? m.Name : Loc.T("articulations.manager.untitled");
    private static string ArtLabel(Articulation a) => a.Name.Length > 0 ? a.Name : "#" + a.Id.ToString(NotaNum.Culture);

    // ---- maps ----------------------------------------------------------------------------------

    private static Control MapRow(ArticulationMap m, bool onlyInProject, bool differs)
    {
        string meta = onlyInProject ? Loc.F("articulations.manager.meta_project", ArticulationText.Count(m))
            : differs ? Loc.F("articulations.manager.meta_differs", ArticulationText.Count(m))
            : ArticulationText.Count(m);
        return new StackPanel
        {
            Spacing = 1, Margin = new Thickness(0, 2),
            Children =
            {
                new TextBlock { Text = MapLabel(m), FontSize = 11, Foreground = NotaPalette.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = meta, FontSize = 10, Foreground = NotaPalette.TextTertiary, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
    }

    // Keeps a map in the library; says so when the library can't take it. True when kept.
    private async System.Threading.Tasks.Task<bool> Keep(ArticulationMap map)
    {
        if (_lib is null) return false;
        try { _lib.Save(map); return true; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            await Notice(Loc.F("articulations.manager.save_failed", ex.Message));
            return false;
        }
    }

    private System.Threading.Tasks.Task Notice(string text)
        => new ConfirmWindow(Loc.T("articulations.manager.title"), text, Loc.T("common.close"), null).ShowDialog<bool>(this);

    private async void NewMap()
    {
        if (!await ConfirmDiscard()) return;
        var map = new ArticulationMap { Name = Loc.T("articulations.manager.new_name"), Metadata = { ["source"] = "nota" } };
        if (await Keep(map)) Reload(map.Id);
    }

    private async System.Threading.Tasks.Task DuplicateMap()
    {
        if (_work is null || !await ConfirmDiscard()) return;
        var source = _catalog[_mapIndex].Map;   // the saved map, not unsaved edits that were just discarded
        var copy = source.DuplicateAs(Loc.F("articulations.manager.copy_name", MapLabel(source)));
        if (await Keep(copy)) Reload(copy.Id);
    }

    private async System.Threading.Tasks.Task DeleteMap()
    {
        if (_work is null || OnlyInProject || _lib is null) return;
        var map = _catalog[_mapIndex].Map;
        bool ok = await new ConfirmWindow(Loc.T("articulations.manager.title"),
            Loc.F("articulations.manager.delete_confirm", MapLabel(map)), Loc.T("articulations.manager.delete"), Loc.T("common.cancel")).ShowDialog<bool>(this);
        if (!ok) return;
        try { _lib.Delete(map.Id); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            await Notice(Loc.F("articulations.manager.delete_failed", ex.Message));
        }
        _savedJson = null;
        _work = null;   // nothing left to discard
        Reload(null);
    }

    private async System.Threading.Tasks.Task PickAndImport()
    {
        var files = await this.PickFilesAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("articulations.preview.title"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Loc.T("articulations.inspector.file_type")) { Patterns = ArticulationImport.PickerExtensions.Select(x => "*" + x).ToArray() },
                new FilePickerFileType(Loc.T("articulations.inspector.all_files")) { Patterns = new[] { "*" } },
            },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { Length: > 0 } path) await ImportFromFile(path);
    }

    // Import → preview → (accepted) into the library, then open it. The file is only read.
    private async System.Threading.Tasks.Task ImportFromFile(string path)
    {
        if (!await ConfirmDiscard()) return;
        var result = ArticulationImport.ImportFile(path);
        bool ok = await new ArticulationImportPreview(System.IO.Path.GetFileName(path), result, Loc.T("articulations.manager.add_to_library")).ShowDialog<bool>(this);
        if (!ok || result.Map is not { } map) return;
        if (await Keep(map)) Reload(map.Id);
    }

    // Explorer → window, like the inspector's drop (T033): accepted on sight, decided by content.
    private void HookDrop(Control target)
    {
        DragDrop.SetAllowDrop(target, true);
        DragDrop.AddDragOverHandler(target, (_, e) =>
        {
            if (BrowserView.CurrentDrag is not null || !BrowserView.IsAcceptableDrag(e)) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        });
        DragDrop.AddDropHandler(target, (_, e) =>
        {
            if (BrowserView.CurrentDrag is not null) return;
            var file = BrowserView.ExternalPaths(e).FirstOrDefault(System.IO.File.Exists);
            if (file is null) return;
            e.Handled = true;
            Dispatcher.UIThread.Post(() => _ = ImportFromFile(file));   // after the drop returns: the dialogs are modal
        });
    }

    // ---- articulations -------------------------------------------------------------------------

    private void BuildArticulations()
    {
        _building = true;
        _arts.Items.Clear();
        _rows.Clear();
        int sel = -1;
        if (_work is not null)
            for (int i = 0; i < _work.Articulations.Count; i++)
            {
                var a = _work.Articulations[i];
                _arts.Items.Add(ArtRow(a));
                if (a.Id == _artId) sel = i;
            }
        _arts.SelectedIndex = sel;
        _building = false;
        UpdateButtons();
    }

    private Control ArtRow(Articulation a)
    {
        var swatch = new Border { Width = 10, Height = 10, CornerRadius = NotaRadius.Fine, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 8, 0) };
        var name = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = ClipEditorKit.Mono("", 10, NotaPalette.TextTertiary);
        meta.TextTrimming = TextTrimming.CharacterEllipsis;
        var badge = new TextBlock { Text = Loc.T("articulations.manager.default_badge"), FontSize = 9, Foreground = NotaPalette.TextMuted, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 2, 0, 0) };
        _rows[a.Id] = (swatch, name, meta, badge);
        RefreshRow(a);
        var row = new DockPanel { Margin = new Thickness(0, 2) };
        DockPanel.SetDock(swatch, Dock.Left);
        DockPanel.SetDock(badge, Dock.Right);
        row.Children.Add(swatch);
        row.Children.Add(badge);
        row.Children.Add(new StackPanel { Spacing = 1, Children = { name, meta } });
        return row;
    }

    // An edit that doesn't change the list's shape: the row follows in place, so typing a name
    // neither rebuilds the list nor moves its scroll.
    private void RefreshRow(Articulation a)
    {
        if (!_rows.TryGetValue(a.Id, out var r)) return;
        r.Swatch.Background = ArticulationImportPreview.Swatch(a);
        r.Name.Text = ArtLabel(a);
        r.Name.Foreground = a.Enabled ? NotaPalette.TextPrimary : NotaPalette.TextDisabled;
        r.Meta.Text = ArticulationText.Actions(a);
        r.Badge.IsVisible = _work?.DefaultArticulationId == a.Id;
    }

    private void RefreshRows()
    {
        if (_work is null) return;
        foreach (var a in _work.Articulations) RefreshRow(a);
    }

    private void AddArticulation()
    {
        if (_work is null) return;
        _artId = ArticulationMapEdits.Add(_work, Loc.F("articulations.manager.add_name", ArticulationMapEdits.NextId(_work)));
        Structure();
    }

    private void CopyArticulation()
    {
        if (_work is null || Selected is not { } a) return;
        int id = ArticulationMapEdits.Copy(_work, a.Id, Loc.F("articulations.manager.copy_name", ArtLabel(a)));
        if (id > 0) _artId = id;
        Structure();
    }

    private void RemoveArticulation()
    {
        if (_work is null || Selected is not { } a) return;
        int k = _work.Articulations.IndexOf(a);
        ArticulationMapEdits.Remove(_work, a.Id);
        _artId = _work.Articulations.Count == 0 ? 0 : _work.Articulations[Math.Min(k, _work.Articulations.Count - 1)].Id;
        Structure();
    }

    private void MoveArticulation(int delta)
    {
        if (_work is null || Selected is not { } a) return;
        if (ArticulationMapEdits.Move(_work, a.Id, delta)) Structure();
    }

    // An edit that changes the list (add, copy, remove, reorder): rebuild it and the details.
    private void Structure()
    {
        BuildArticulations();
        BuildDetails();
        Edited();
    }

    // ---- details -------------------------------------------------------------------------------

    private void BuildDetails()
    {
        _effective = null;
        if (_work is null || Selected is not { } a)
        {
            _details.Content = Hint(_work is null ? "" : Loc.T("articulations.manager.no_selection"));
            return;
        }
        var map = _work;
        var s = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 8, 12) };

        var name = Field(a.Name);
        name.TextChanged += (_, _) => { a.Name = name.Text ?? ""; RefreshRow(a); Edited(); };
        ToolTip.SetTip(name, Loc.T("articulations.manager.art_name_tip"));
        var shortName = Field(a.ShortName);
        shortName.TextChanged += (_, _) => { a.ShortName = shortName.Text ?? ""; Edited(); };
        ToolTip.SetTip(shortName, Loc.T("articulations.manager.short_name_tip"));
        s.Children.Add(Labelled(Loc.T("articulations.manager.name"), name));
        s.Children.Add(Labelled(Loc.T("articulations.manager.short_name"), shortName));
        s.Children.Add(Labelled(Loc.T("articulations.manager.colour"), Colours(a)));

        // Group: only the map's groups (the formats define them); "No group" is always there.
        var groups = new List<(int Id, string Label)> { (0, Loc.T("articulations.manager.no_group")) };
        groups.AddRange(map.Groups.Select(g => (g.Id, ArticulationText.Group(g))));
        string groupLabel = groups.FirstOrDefault(g => g.Id == a.GroupId).Label ?? groups[0].Label;
        var groupChip = SessionView.Chip(groupLabel, false, b => SessionView.Pick(b, groups, v => v == a.GroupId, v => { a.GroupId = v; Edited(); BuildDetails(); }));
        ToolTip.SetTip(groupChip, Loc.T("articulations.manager.group_tip"));
        s.Children.Add(SessionView.Row(Loc.T("articulations.manager.group"), groupChip));

        var trigger = SessionView.Segmented(new[] { Loc.T("articulations.manager.latching"), Loc.T("articulations.manager.momentary") },
            a.Trigger == TriggerMode.Momentary ? 1 : 0, i => { a.Trigger = i == 1 ? TriggerMode.Momentary : TriggerMode.Latching; Edited(); }, compact: true);
        ToolTip.SetTip(trigger, Loc.T("articulations.manager.trigger_tip"));
        s.Children.Add(SessionView.Row(Loc.T("articulations.manager.trigger"), trigger));

        var enabled = new CheckBox { Content = Loc.T("articulations.manager.enabled"), IsChecked = a.Enabled, FontSize = 11 };
        enabled.IsCheckedChanged += (_, _) => { a.Enabled = enabled.IsChecked == true; RefreshRow(a); Edited(); };
        ToolTip.SetTip(enabled, Loc.T("articulations.manager.enabled_tip"));
        var isDefault = new CheckBox { Content = Loc.T("articulations.manager.make_default"), IsChecked = map.DefaultArticulationId == a.Id, FontSize = 11 };
        isDefault.IsCheckedChanged += (_, _) =>
        {
            if (isDefault.IsChecked == true) map.DefaultArticulationId = a.Id;
            else if (map.DefaultArticulationId == a.Id) map.DefaultArticulationId = null;
            RefreshRows();
            Edited();
        };
        ToolTip.SetTip(isDefault, Loc.T("articulations.manager.default_tip"));
        s.Children.Add(new WrapPanel { Children = { enabled, isDefault }, ItemSpacing = 16 });

        s.Children.Add(SessionView.Row(Loc.T("articulations.manager.note_offset"),
            MsBox(a.NoteOffsetMs, -OffsetRangeMs, OffsetRangeMs, Loc.T("articulations.manager.note_offset_tip"), v => a.NoteOffsetMs = v)));
        _effective = Hint("");
        s.Children.Add(_effective);
        RefreshEffective();
        s.Children.Add(SessionView.Row(Loc.T("articulations.manager.lead"),
            MsBox(a.TriggerLeadMs, 0, LeadRangeMs, Loc.T("articulations.manager.lead_tip"), v => a.TriggerLeadMs = v)));

        // Actions, in send order.
        var actions = new StackPanel { Spacing = 6 };
        for (int i = 0; i < a.Actions.Count; i++) actions.Children.Add(ActionRow(a, i));
        if (a.Actions.Count == 0) actions.Children.Add(Hint(Loc.T("articulations.action.none")));
        var addAction = TextButton(Loc.T("articulations.manager.add_action"), Loc.T("articulations.manager.add_action_tip"), () =>
        {
            a.Actions.Add(new MidiAction { Kind = MidiActionKind.Keyswitch, Data1 = 24, Data2 = 100 });
            RefreshRow(a);
            Edited();
            BuildDetails();
        });
        addAction.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(new StackPanel
        {
            Spacing = 8, Margin = new Thickness(0, 6, 0, 0),
            Children = { SessionView.SectionHead(Loc.T("articulations.manager.actions")), actions, addAction },
        });
        _details.Content = s;
    }

    // The track palette as swatches, the articulation's own colour ringed, and Auto (the colour its
    // palette index or id gives). A swatch stores the colour itself: a map carries RGB, not an index.
    private Control Colours(Articulation a)
    {
        int bases = ArrangementView.PaletteBases, shades = ArrangementView.PaletteShades;
        var grid = new UniformGrid { Columns = bases };
        for (int shade = 0; shade < shades; shade++)
            for (int b = 0; b < bases; b++)
            {
                int idx = b * shades + shade;
                uint argb = ArrangementView.TrackColorForIndex(idx).ToUInt32();
                bool on = a.ColorArgb == argb;
                var sw = new Border
                {
                    Height = 14, Margin = new Thickness(1.5), CornerRadius = NotaRadius.Fine, Background = ArrangementView.TrackBrush(idx),
                    BorderBrush = on ? NotaPalette.TextPrimary : Brushes.Transparent, BorderThickness = new Thickness(on ? 1.5 : 0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                sw.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(sw).Properties.IsLeftButtonPressed) return;
                    a.ColorArgb = argb;
                    RefreshRow(a);
                    Edited();
                    BuildDetails();
                };
                grid.Children.Add(sw);
            }
        var auto = SessionView.Chip(Loc.T("common.auto"), a.ColorArgb is null, _ =>
        {
            a.ColorArgb = null;
            RefreshRow(a);
            Edited();
            BuildDetails();
        });
        ToolTip.SetTip(auto, Loc.T("articulations.manager.colour_auto_tip"));
        auto.HorizontalAlignment = HorizontalAlignment.Left;
        return new StackPanel { Spacing = 6, Children = { grid, auto } };
    }

    // One action: kind · channel · its data · reorder and remove.
    private Control ActionRow(Articulation a, int index)
    {
        var x = a.Actions[index];
        var kinds = new List<(MidiActionKind Kind, string Label)>
        {
            (MidiActionKind.Keyswitch, Loc.T("articulations.manager.kind_keyswitch")),
            (MidiActionKind.ControlChange, Loc.T("articulations.manager.kind_cc")),
            (MidiActionKind.ProgramChange, Loc.T("articulations.manager.kind_program")),
            (MidiActionKind.BankSelectMsb, Loc.T("articulations.manager.kind_bank_msb")),
            (MidiActionKind.BankSelectLsb, Loc.T("articulations.manager.kind_bank_lsb")),
        };
        var kindChip = SessionView.Chip(kinds.First(k => k.Kind == x.Kind).Label, false, b => SessionView.Pick(b, kinds, v => v == x.Kind, v =>
        {
            x.Kind = v;
            x.Data1 = Math.Clamp(x.Data1, 0, 127);
            x.Data2 = Math.Clamp(x.Data2, 0, 127);
            RefreshRow(a);
            Edited();
            BuildDetails();
        }));
        ToolTip.SetTip(kindChip, Loc.T("articulations.manager.kind_tip"));

        var channels = new List<(int Ch, string Label)> { (0, Loc.T("articulations.manager.note_channel")) };
        for (int c = 1; c <= 16; c++) channels.Add((c, Loc.F("articulations.manager.channel_n", c)));
        var chanChip = SessionView.Chip(x.Channel is int ch ? Loc.F("articulations.manager.channel_n", ch) : Loc.T("articulations.manager.note_channel"), false,
            b => SessionView.Pick(b, channels, v => v == (x.Channel ?? 0), v =>
            {
                x.Channel = v == 0 ? null : v;
                RefreshRow(a);
                Edited();
                BuildDetails();
            }));
        ToolTip.SetTip(chanChip, Loc.T("articulations.manager.channel_tip"));

        var data = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        void Data(int value, bool noteName, string tip, Action<int> set) => data.Children.Add(MidiBox(value, noteName, tip, v => { set(v); RefreshRow(a); Edited(); }));
        switch (x.Kind)
        {
            case MidiActionKind.Keyswitch:
                Data(x.Data1, true, Loc.T("articulations.manager.key_tip"), v => x.Data1 = v);
                Data(x.Data2, false, Loc.T("articulations.manager.velocity_tip"), v => x.Data2 = v);
                break;
            case MidiActionKind.ControlChange:
                Data(x.Data1, false, Loc.T("articulations.manager.cc_tip"), v => x.Data1 = v);
                Data(x.Data2, false, Loc.T("articulations.manager.value_tip"), v => x.Data2 = v);
                break;
            case MidiActionKind.ProgramChange:
                Data(x.Data1, false, Loc.T("articulations.manager.program_tip"), v => x.Data1 = v);
                break;
            default:
                Data(x.Data2, false, Loc.T("articulations.manager.value_tip"), v => x.Data2 = v);
                break;
        }

        void Reorder(int delta) { if (ArticulationMapEdits.MoveAction(a, index, delta)) { RefreshRow(a); Edited(); BuildDetails(); } }
        var up = IconButton(GlyphKind.ChevronUp, Loc.T("articulations.manager.up_tip"), () => Reorder(-1));
        var down = IconButton(GlyphKind.ChevronDown, Loc.T("articulations.manager.down_tip"), () => Reorder(1));
        var remove = IconButton(GlyphKind.Trash, Loc.T("articulations.manager.remove_action_tip"), () =>
        {
            a.Actions.RemoveAt(index);
            RefreshRow(a);
            Edited();
            BuildDetails();
        });
        up.IsEnabled = index > 0;
        down.IsEnabled = index < a.Actions.Count - 1;

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { up, down, remove } };
        var row = new DockPanel();
        DockPanel.SetDock(tools, Dock.Right);
        row.Children.Add(tools);
        row.Children.Add(new WrapPanel { ItemSpacing = 4, LineSpacing = 4, Children = { kindChip, chanChip, data } });
        return row;
    }

    // A MIDI data value: a number 0–127 or, for a keyswitch, a note name. Enter or leaving sets it;
    // Esc puts it back; text that isn't a value is ignored.
    private static TextBox MidiBox(int value, bool noteName, string tip, Action<int> set)
    {
        string Show(int v) => noteName ? SamplerModel.NoteName(v) : v.ToString(NotaNum.Culture);
        string shown = Show(value);
        var box = new TextBox
        {
            Text = shown, FontSize = 11, FontFamily = NotaFonts.ValuesFamily, Height = NotaSize.Shell, MinHeight = 0, Width = 52,
            VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Right,
        };
        ToolTip.SetTip(box, tip);
        void Commit()
        {
            string text = (box.Text ?? "").Trim();
            if (text == shown) return;
            if (ArticulationMapEdits.ParseMidiValue(text, noteName) is not int v) { box.Text = shown; return; }
            shown = Show(v);
            box.Text = shown;
            set(v);
        }
        Keys(box, () => shown);
        box.LostFocus += (_, _) => Commit();
        return box;
    }

    private static string Ms(double ms) => NotaNum.Unit(Math.Round(ms), "0", "ms");

    // Milliseconds typed like the inspector's MIDI offset ("−80", "-80 ms", "−0.08 s"), held to the range.
    private TextBox MsBox(double value, double min, double max, string tip, Action<double> set)
    {
        string shown = Ms(value);
        var box = new TextBox
        {
            Text = shown, FontSize = 11, Height = NotaSize.Shell, MinHeight = 0, Width = 96,
            VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Right,
        };
        ToolTip.SetTip(box, tip);
        void Commit()
        {
            string text = (box.Text ?? "").Trim();
            if (text == shown) return;
            double? pos = ValueText.Solve(text, shown, p => Ms(min + (max - min) * p));
            if (pos is not double p) { box.Text = shown; return; }
            double ms = Math.Round(min + (max - min) * p);
            shown = Ms(ms);
            box.Text = shown;
            set(ms);
            Edited();
        }
        Keys(box, () => shown);
        box.LostFocus += (_, _) => Commit();
        return box;
    }

    // Enter commits (by leaving the box), Esc restores what it showed.
    private static void Keys(TextBox box, Func<string> shown)
    {
        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            if (e.Key == Key.Escape) box.Text = shown();
            e.Handled = true;
            TopLevel.GetTopLevel(box)?.FocusManager?.Focus(null);
        };
    }

    // ---- footer --------------------------------------------------------------------------------

    private void UpdateButtons()
    {
        bool open = _work is not null;
        _duplicate.IsEnabled = open;
        _delete.IsEnabled = open && !OnlyInProject && _lib is not null;
        ToolTip.SetTip(_delete, OnlyInProject ? Loc.T("articulations.manager.delete_project_tip") : Loc.T("articulations.manager.delete_tip"));
        _artAdd.IsEnabled = open;
        bool sel = Selected is not null;
        int k = sel ? _work!.Articulations.IndexOf(Selected!) : -1;
        _artCopy.IsEnabled = _artRemove.IsEnabled = sel;
        _artUp.IsEnabled = sel && k > 0;
        _artDown.IsEnabled = sel && k < _work!.Articulations.Count - 1;
    }

    // The status line (what stops a save, unsaved changes, or how many tracks use the map) and
    // the buttons that depend on it.
    private void UpdateFooter()
    {
        UpdateButtons();
        RefreshEffective();
        bool open = _work is not null, dirty = Dirty;
        var problems = open ? ArticulationMapEdits.Check(_work!) : new();
        _status.Foreground = problems.Count > 0 ? NotaPalette.Warning : NotaPalette.TextTertiary;
        _status.Text = !open ? ""
            : problems.Count > 0 ? ProblemText(problems[0])
            : _flash ?? (dirty ? Loc.T("articulations.manager.unsaved")
                : Loc.Plural("articulations.manager.used_on", ArticulationMapUsage.TracksUsing(_engine, _work!.Id).Count));
        _export.IsEnabled = open && problems.Count == 0;
        _save.IsEnabled = open && problems.Count == 0 && (dirty || OnlyInProject || DiffersFromLibrary) && _lib is not null;
        _revert.IsEnabled = open && dirty;
        int track = InstrumentTrack();
        _assign.IsEnabled = open && !dirty && problems.Count == 0 && track > 0;
        ToolTip.SetTip(_assign, dirty ? Loc.T("articulations.manager.assign_dirty_tip")
            : track <= 0 ? Loc.T("articulations.manager.assign_no_track_tip") : Loc.T("articulations.manager.assign_tip"));
    }

    // Under Note offset: when the selected articulation's notes play — the track offset plus its
    // own, as the inspector says it (FR-018). Follows typing, Save, and the track offset changing.
    private void RefreshEffective()
    {
        if (_effective is null || _work is null || Selected is not { } a) return;
        if (!a.Enabled) { _effective.Text = Loc.T("articulations.manager.effective_disabled"); return; }
        _effective.Text = ArticulationMapUsage.TrackOffsetFor(_engine, _work.Id, _selectedTrack()) is double trackMs
            ? Loc.F("articulations.manager.effective", Ms(trackMs + a.NoteOffsetMs), Ms(trackMs), Ms(a.NoteOffsetMs))
            : Loc.T("articulations.manager.effective_each");
    }

    private static string ProblemText((ArticulationMapEdits.Problem Problem, string Subject) p) => p.Problem switch
    {
        ArticulationMapEdits.Problem.NoName => Loc.T("articulations.manager.problem_no_name"),
        ArticulationMapEdits.Problem.ArticulationWithoutName => Loc.F("articulations.manager.problem_articulation_name", p.Subject),
        ArticulationMapEdits.Problem.DuplicateId => Loc.F("articulations.manager.problem_duplicate_id", p.Subject),
        ArticulationMapEdits.Problem.InvalidAction => Loc.F("articulations.manager.problem_invalid_action", p.Subject),
        _ => Loc.T("articulations.manager.problem_default_missing"),
    };

    // The selected track when it is an instrument track, else 0.
    private int InstrumentTrack()
    {
        int t = _selectedTrack();
        if (t <= 0) return 0;
        for (int i = 0; i < _engine.TrackCount; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.Id == t) return ti.IsInstrument ? t : 0;
        return 0;
    }

    private async void Save()
    {
        if (_work is null || ArticulationMapEdits.Check(_work).Count > 0) return;
        if (!await Keep(_work)) return;
        int n = ArticulationMapUsage.ApplyEverywhere(_engine, _work);   // one undo step (FR-010)
        string id = _work.Id;
        Reload(id);
        _flash = Loc.Plural("articulations.manager.saved", n);
        UpdateFooter();
    }

    private void Revert()
    {
        if (_work is null) return;
        Load(_mapIndex);
    }

    private void Assign()
    {
        int t = InstrumentTrack();
        if (_work is null || t <= 0 || Dirty) return;
        _flash = ArticulationMapAssignment.Assign(_engine, t, _work)
            ? Loc.T("articulations.manager.assigned") : Loc.T("articulations.inspector.assign_failed");
        UpdateFooter();
    }

    private async System.Threading.Tasks.Task Export()
    {
        if (_work is null) return;
        var file = await this.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("articulations.manager.export_title"),
            DefaultExtension = ArticulationMapFile.Extension.TrimStart('.'),
            SuggestedFileName = MapLabel(_work) + ArticulationMapFile.Extension,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(Loc.T("articulations.manager.file_type")) { Patterns = new[] { "*" + ArticulationMapFile.Extension } },
            },
        });
        var path = file?.TryGetLocalPath();
        if (path is null) return;
        if (!path.EndsWith(ArticulationMapFile.Extension, StringComparison.OrdinalIgnoreCase)) path += ArticulationMapFile.Extension;
        try
        {
            System.IO.File.WriteAllText(path, ArticulationMapFile.Write(_work));
            _flash = Loc.F("articulations.manager.exported", System.IO.Path.GetFileName(path));
            UpdateFooter();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            await Notice(Loc.F("articulations.manager.export_failed", ex.Message));
        }
    }

    // ---- building blocks -----------------------------------------------------------------------

    // A titled column: an optional head above, the body filling, tools and a hint below.
    private static Border Column(string title, Control body, Control? tools, Control? hint, Control? head = null)
    {
        var top = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 10), Children = { SessionView.SectionHead(title) } };
        if (head is not null) top.Children.Insert(0, head);
        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        if (tools is not null) bottom.Children.Add(tools);
        if (hint is not null) bottom.Children.Add(hint);
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(bottom);
        dock.Children.Add(body);
        return new Border { Padding = new Thickness(16, 14, 12, 0), Child = dock };
    }

    private static Control Labelled(string label, Control field) => new StackPanel
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextTertiary }, field },
    };

    private static TextBox Field(string text = "") => new()
    {
        Text = text, FontSize = 11, Height = NotaSize.Shell, MinHeight = 0, VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text, FontSize = 11, LineHeight = 16, Foreground = NotaPalette.TextTertiary, TextWrapping = TextWrapping.Wrap,
    };

    private static Button TextButton(string text, string tip, Action click)
    {
        var b = new Button { Height = NotaSize.Shell, Padding = new Thickness(10, 0), Margin = new Thickness(0, 0, 6, 6), Content = text };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private static Button IconButton(GlyphKind kind, string tip, Action click)
    {
        var b = new Button
        {
            Width = NotaSize.Shell, Height = NotaSize.Shell, Padding = new Thickness(0), Content = new Glyph(kind, 12),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
}
