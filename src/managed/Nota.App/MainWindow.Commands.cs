// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-24): what every catalog command does. The menu (app:Commands.Id), the
// keyboard shortcuts and the palette all run commands from this one registry by id; the
// handlers themselves are the existing menu / view methods. CanExecute answers the palette
// only — run from the menu or a key, a command explains in the status line why it did nothing.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Palette;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private readonly CommandRegistry _commands = new();

    /// <summary>The command registry (CP-24) — the palette and secondary windows run commands through it.</summary>
    internal ICommandRegistry CommandsRegistry => _commands;

    private void BindCommands()
    {
        var r = _commands;
        r.ContextProvider = () => SnapshotContext(null);
        void B(string id, Action run, Func<PaletteContext, Availability>? can = null) => r.Bind(id, (_, _) => run(), can);
        var e = new RoutedEventArgs();
        static Availability Need(bool ok, string why) => ok ? Availability.Yes : Availability.No(why);

        // ---- File
        B("file.new", () => OnMenuNew(this, e));
        B("file.open", () => OnMenuOpen(this, e));
        B("file.save", () => OnMenuSave(this, e));
        B("file.saveAs", () => OnMenuSaveAs(this, e));
        B("file.saveVersion", () => OnMenuSaveVersion(this, e));
        B("file.import", () => OnMenuImport(this, e));
        B("file.export", () => OnMenuExport(this, e));

        // ---- Edit
        B("edit.undo", () => OnMenuUndo(this, e), _ => Need(_vm?.Engine.CanUndo == true, "Nothing to undo"));
        B("edit.redo", () => OnMenuRedo(this, e));
        B("edit.duplicate", () => OnMenuDuplicate(this, e));
        B("edit.split", () => OnMenuSplit(this, e), c => Need(c.SelectedClips > 0 || c.HasTimeSelection, "Select a clip or a range first"));
        B("edit.consolidate", () => OnMenuConsolidate(this, e), c => Need(c.SelectedClips > 0 || c.HasTimeSelection, "Select clips or a range first"));
        B("edit.duplicateTime", () => OnMenuDuplicateTime(this, e), c => Need(c.HasTimeSelection, "Select a time range first"));
        B("edit.insertSilence", () => OnMenuInsertSilence(this, e), c => Need(c.HasTimeSelection, "Select a time range first"));
        B("edit.lockEnvelopes", ToggleLockEnvelopes);
        B("edit.cut", () => OnMenuCut(this, e));
        B("edit.copy", () => OnMenuCopy(this, e));
        B("edit.paste", () => OnMenuPaste(this, e));
        B("edit.pasteBounced", () => OnMenuPasteBounced(this, e));
        B("edit.copyToOtherView", () => OnMenuCopyToOtherView(this, e));
        B("edit.delete", () => OnMenuDeleteSel(this, e));
        B("edit.selectAll", () => Timeline.SelectAllClips());
        B("edit.toggleClipActive", () => { if (Timeline.ToggleSelectedClipsActive()) { _session?.Refresh(); if (_vm is not null) _vm.StatusText = "Toggled clip(s)"; } },
            c => Need(c.SelectedClips > 0, "Select a clip first"));

        // ---- View
        B("view.arrangement", () => SetView(MainView.Arrangement));
        B("view.session", () => SetView(MainView.Session));
        B("view.modular", () => SetView(MainView.Modular));
        B("view.mixer", ToggleMixerWindow);
        B("view.connectPhone", ShowConnectPhone);
        B("view.toggleBrowser", () => OnMenuToggleBrowser(this, e));
        B("view.toggleDetail", () => OnMenuToggleClip(this, e));
        B("view.showDevices", () => { int t = Timeline.SelectedTrackId > 0 ? Timeline.SelectedTrackId : LastInstrumentTrack(); if (t > 0) ShowDevices(t); },
            c => Need(c.HasTrack, "Select a track first"));
        B("view.popOutDetail", () => OnToggleDetailWindow(this, e));
        B("view.toggleOverview", () => OnMenuToggleOverview(this, e));
        B("view.toggleSections", () => OnMenuToggleSections(this, e));
        B("view.follow", () => OnMenuToggleFollow(this, e));
        B("view.showPlayhead", ShowPlayhead);
        B("view.clipNames", () => OnMenuCycleClipNames(this, e));
        B("view.automation", () =>
        {
            AutomationToggle.IsChecked = !(AutomationToggle.IsChecked == true);
            OnToggleAutomation(AutomationToggle, e);
        });
        B("view.history", () => Browser.ShowHistory());
        B("view.midiLearn", ToggleMidiLearn);
        B("view.fullscreen", ToggleFullscreen);
        r.Bind(CommandCatalog.Palette, (c, _) => TogglePalette(c.Window as Avalonia.Controls.Window));

        // ---- Transport
        B("transport.playStop", () => _vm?.Transport.PlayStopCommand.Execute(null));
        B("transport.playPause", () => _vm?.Transport.PlayPauseCommand.Execute(null));
        B("transport.stop", () => _vm?.Transport.StopCommand.Execute(null));
        B("transport.record", () => OnMenuRecord(this, e));
        B("transport.loop", () => OnMenuLoop(this, e));
        B("transport.metronome", () => OnMenuMetronome(this, e));
        B("transport.loopRecordMode", () => { if (_vm is not null) _vm.Transport.LoopRecordReplace = !_vm.Transport.LoopRecordReplace; });
        B("transport.tapTempo", TapTempoBeat);

        // ---- Track
        B("track.newInstrument", () => AddTrack(NewTrackKind.Instrument));
        B("track.newAudio", () => AddTrack(NewTrackKind.Audio));
        B("track.newReturn", () => OnAddReturnClicked(this, e));
        B("track.group", () => { if (Timeline.GroupSelection() && _vm is not null) _vm.StatusText = "Grouped tracks"; },
            c => Need(c.SelectedTrackCount > 0, "Select tracks first"));
        B("track.ungroup", () => { if (Timeline.UngroupSelection() && _vm is not null) _vm.StatusText = "Ungrouped"; });
        r.Bind("track.freeze", (c, m) => { if (c.TrackId > 0) _ = ToggleFreezeAsync(c.TrackId); },
            c => Need(c.HasTrack && IsFreezable(c.TrackId), c.HasTrack ? "This track can't be frozen" : "Select a track first"));

        // ---- Session
        B("session.launchScene", () => _session?.LaunchSelectedScene(), c => Need(c.View == "Session", "Open the Session view first"));
        B("session.stopAll", () => _session?.StopAll());
        B("session.insertScene", () => OnMenuInsertScene(this, e), c => Need(c.View == "Session", "Scenes live in the Session view"));

        // ---- Piano roll: the clip open in the editor
        Availability Roll(PaletteContext c) => Need(c.HasOpenMidiClip, "Open a MIDI clip first");
        // ---- Grid: the editor used last (piano roll or arrangement), status line names it
        void G(string id, Func<GridSetting, GridSetting> step) => B(id, () => StepGrid(step));
        G("grid.narrower", SnapGrid.Narrower);
        G("grid.wider", SnapGrid.Wider);
        G("grid.triplets", SnapGrid.ToggleTriplets);
        G("grid.snap", SnapGrid.ToggleSnap);
        G("grid.adaptive", SnapGrid.ToggleAdaptive);

        B("roll.quantize", () => { if (_editorRoll is { } roll) { roll.Quantize(1.0); if (_vm is not null) _vm.StatusText = "Quantized notes"; } }, Roll);
        B("roll.octaveUp", () => _editorRoll?.TransposeBy(12), Roll);
        B("roll.octaveDown", () => _editorRoll?.TransposeBy(-12), Roll);
        B("roll.fitNotes", () => _editorRoll?.FitToNotes(), Roll);
        B("roll.editSelected", () => EditSelectedClips(),
            _ => Need(Timeline.SelectedMidiClips().Count >= 2, "Select two or more MIDI clips"));
        B("roll.removeOutOfScale", () => _editorRoll?.RemoveOutOfScaleNotes(),
            c => Need(c.HasOpenMidiClip && _editorRoll?.HasOutOfScaleNotes == true, "No notes outside the scale"));

        // ---- Settings
        B("prefs.open", ShowPreferences);
        foreach (var (id, page) in new[]
        {
            ("prefs.audio", "Audio"), ("prefs.midi", "MIDI"), ("prefs.gamepads", "Gamepads"), ("prefs.remote", "Remote"),
            ("prefs.plugins", "Plug-ins"), ("prefs.downloads", "Downloads"), ("prefs.library", "Library"),
            ("prefs.appearance", "Appearance"), ("prefs.shortcuts", "Shortcuts"), ("prefs.updates", "Updates"),
        })
            B(id, () => ShowPreferencesPage(page));

        // ---- Help
        B("help.docs", () => OnDocumentation(this, e));
        B("help.whatsNew", ShowWhatsNew);
        B("help.checkUpdates", () => _ = CheckForUpdatesAsync());
        B("help.about", ShowAbout);

        // Every catalog command must have a handler — a missing one is a bug, not a quiet no-op.
        var unbound = r.All.Where(c => !c.IsBound).Select(c => c.Id).ToList();
        if (unbound.Count > 0) throw new InvalidOperationException("Unbound commands: " + string.Join(", ", unbound));
    }

    private void ShowPreferencesPage(string page)
    {
        foreach (var w in OwnedWindows)
            if (w is PreferencesWindow open) { open.ShowPage(page); open.Activate(); return; }
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var win = new PreferencesWindow(new SettingsViewModel(settings), _vm);
        win.ShowPage(page);
        win.Show(this);
    }

    // View ▸ Toggle Full Screen. The state to return to is remembered on the way in —
    // the app usually runs maximized, so leaving fullscreen must not un-maximize.
    // macOS-native entry (the green button) restores natively and never reads this.
    private WindowState? _stateBeforeFullscreen;

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _stateBeforeFullscreen ?? WindowState.Maximized;
            _stateBeforeFullscreen = null;
        }
        else
        {
            _stateBeforeFullscreen = WindowState == WindowState.Normal ? WindowState.Normal : WindowState.Maximized;
            WindowState = WindowState.FullScreen;
        }
    }

    // Edit ▸ Lock Envelopes: the engine flag and the menu checkmark move together.
    private void ToggleLockEnvelopes()
    {
        if (_vm is null) return;
        bool locked = !_vm.Engine.AutomationLock;
        _vm.Engine.SetAutomationLock(locked);
        if (MenuItemFor("edit.lockEnvelopes") is { } mi) mi.IsChecked = locked;
        _vm.StatusText = locked ? "Envelopes locked — clip moves keep automation in place"
                                : "Envelopes follow clips";
    }

    // The grid shortcuts act on the clip editor (piano roll or audio — they share one setting)
    // when the detail area (docked or floating) was used last and one is open in it; otherwise
    // on the arrangement. An audio editor whose current view follows no grid (ADSR, or an
    // unwarped clip) passes them on to the arrangement, where the change can be seen (#114).
    private EditorKind GridTarget()
    {
        bool clipOpen = (_editorRoll is { } roll && _clipEditor is not null && roll.IsEffectivelyVisible)
                        || _audioEditor is { IsEffectivelyVisible: true, GridInert: false };
        bool detailLast = _detailWasLastFocused || _detailWindow?.IsActive == true;
        return clipOpen && detailLast ? EditorKind.PianoRoll : EditorKind.Arrangement;
    }

    private void StepGrid(Func<GridSetting, GridSetting> step)
    {
        // Typing in a field: the macOS menu would still fire ⌘1–⌘5 — leave the grid alone.
        // The command palette's own field is the exception: running the command from there is
        // the point.
        var focused = (_detailWindow?.IsActive == true ? _detailWindow.FocusManager : FocusManager)?.GetFocusedElement() as Visual;
        bool typing = focused?.FindAncestorOfType<TextBox>(includeSelf: true) is not null
                      || focused?.FindAncestorOfType<NumericUpDown>(includeSelf: true) is not null;
        if (typing && focused?.FindAncestorOfType<CommandPaletteView>() is null) return;
        var kind = GridTarget();
        var grids = EditorGrids.Shared;
        grids.Set(kind, step(grids.Get(kind)));
        var r = kind == EditorKind.Arrangement ? Timeline.SnapGridState
            : _audioEditor is { IsEffectivelyVisible: true, GridInert: false } audio ? audio.SnapGridState
            : _editorRoll?.SnapGridState ?? Timeline.SnapGridState;
        string label = r.SnapOn ? SnapGrid.Label(grids.Get(kind), r) : "Snap off";
        if (_vm is not null) _vm.StatusText = $"{(kind == EditorKind.PianoRoll ? "Clip editor" : "Arrangement")} grid: {label}";
    }
}
