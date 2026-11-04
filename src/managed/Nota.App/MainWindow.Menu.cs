// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    // --- menu (M4.1-B) -----------------------------------------------------

    private void OnAbout(object? sender, EventArgs e) => ShowAbout();
    public void ShowAbout() => new AboutWindow().Show(this);

    private void OnWhatsNew(object? sender, EventArgs e) => ShowWhatsNew();

    // Help ▸ Documentation: the user manual in the browser.
    private void OnDocumentation(object? sender, EventArgs e) => NotaDocs.Open(this);

    private void OnPreferences(object? sender, EventArgs e) => ShowPreferences();
    public void ShowPreferences()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        new PreferencesWindow(new SettingsViewModel(settings), _vm).Show(this);
    }

    private void OnStub(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.StatusText = Loc.T("main.menu.not_implemented_yet");
    }

    // --- undo / redo (M6-6) ------------------------------------------------

    private void OnMenuUndo(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        _editorRoll?.EndVelocityWheel();   // the burst is the step about to be undone
        if (Engine.Undo()) { RefreshAfterUndoRedo(); _vm.StatusText = Loc.T("common.undo"); }
        else _vm.StatusText = Loc.T("main.menu.nothing_to_undo");
    }

    private void OnMenuRedo(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        _editorRoll?.EndVelocityWheel();
        if (Engine.Redo()) { RefreshAfterUndoRedo(); _vm.StatusText = Loc.T("main.redo"); }
        else _vm.StatusText = Loc.T("main.menu.nothing_to_redo");
    }

    // Undo/redo swaps the whole graph snapshot, so refresh every view that reads it.
    private void RefreshAfterUndoRedo()
    {
        Timeline.Refresh();
        _session?.Refresh();
        // Reflect device-chain edits (a removed device coming back, #109) on the track the
        // chain is showing, which isn't always the selected one (a peek, the master, nothing
        // selected) — rebuilding the selected track's chain there showed the wrong devices.
        if (_deviceChain is { TrackId: > 0 } chain)
        {
            if (chain.TrackId == Engine.MasterTrackId || TrackExists(chain.TrackId)) chain.Show(chain.TrackId);
            // Its track was undone away: follow the selection, or empty the chain when that
            // track went too (undoing a drop onto a new track) instead of naming a dead one.
            else if (TrackExists(Timeline.SelectedTrackId)) chain.Show(Timeline.SelectedTrackId);
            else ShowDevices(-1);
        }
        if (_modular?.IsVisible == true) _modular.Refresh();
        FollowMoveThroughHistory();                      // undoing a clip move re-indexes clips
        ReloadEditorNotes();                             // reflect clip/note edits
        // Volume / pan / master steps (specs/009): the master volume lives on the view model
        // (MST, Master row, Mixer and Remote follow it), the track strips in the Mixer rebuild.
        if (_vm is { } vm && Math.Abs(vm.Transport.MasterVolume - Engine.MasterVolume) > 1e-6)
            vm.Transport.MasterVolume = Engine.MasterVolume;
        if (_mixer?.IsVisible == true) _mixer.Refresh();
    }

    private void OnMenuImport(object? sender, EventArgs e) => _ = DoImportAsync();
    private void OnMenuInsertScene(object? sender, EventArgs e)
    {
        if (_vm is null || _session is null) return;
        if (!_session.IsVisible) { _vm.StatusText = Loc.T("main.menu.scenes_live_in_the_session"); return; }
        _session.InsertSceneAtSelection();
    }
    private void OnMenuExport(object? sender, EventArgs e) => _ = DoExportAsync();
    private void OnMenuDuplicate(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        // The Session grid duplicates its selected clip or scene.
        if (SessionFocused && _session!.DuplicateSelection()) return;
        // While the piano roll has focus, Cmd+D duplicates the selected notes; otherwise
        // it duplicates the selected arrangement clip.
        if (_editorRoll is { GridFocused: true } roll && roll.DuplicateSelection())
        {
            _vm.StatusText = Loc.T("main.menu.duplicated_notes");
            return;
        }
        // Track headers focused: duplicate the selected tracks.
        if (_trackHeadersFocused && Timeline.DuplicateSelectedTracks()) { _vm.StatusText = Loc.T("main.duplicated_track_s"); return; }
        // In automation mode a selected range duplicates the envelope slice (chains on repeat).
        if (Timeline.AutomationMode && Timeline.DuplicateAutoSelection()) { _vm.StatusText = Loc.T("main.menu.duplicated_automation"); return; }
        // A time-range selection duplicates the covered slice (chains on repeat) before clips.
        if (Timeline.DuplicateTimeSelection()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.menu.duplicated_range"); return; }
        if (Timeline.DuplicateSelectedClip()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.menu.duplicated_clip_s"); }
        else _vm.StatusText = Loc.T("main.menu.select_a_clip_first");
    }

    // ⌘⇧D Duplicate Time / ⌘⇧I Insert Silence: whole-arrangement time ops on the time selection.
    private void OnMenuDuplicateTime(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (!Timeline.HasTimeSelection) { _vm.StatusText = Loc.T("main.menu.select_a_time_range_first"); return; }
        if (Timeline.DuplicateTimeInsert()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.menu.duplicated_time"); }
        else _vm.StatusText = Loc.T("main.menu.nothing_to_duplicate_in_the");
    }

    private void OnMenuInsertSilence(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (!Timeline.HasTimeSelection) { _vm.StatusText = Loc.T("main.menu.select_a_time_range_first"); return; }
        if (Timeline.InsertSilenceAtTimeSelection()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.menu.inserted_silence"); }
        else _vm.StatusText = Loc.T("main.menu.nothing_after_the_selection_to");
    }

    private void OnMenuSplit(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        _editorRoll?.SettlePlacement();   // lands in the clips as they are numbered now, before the split re-indexes them
        // In the piano roll Cmd+E has no note meaning; only split arrangement clips.
        // A time-range selection wins: split every covered track at the range edges.
        if (Timeline.SplitTimeSelection()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.menu.split_at_selection"); return; }
        if (Timeline.SplitSelectedAtPlayhead()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.menu.split_clip_s_at_playhead"); }
        else _vm.StatusText = Loc.T("main.menu.select_a_range_or_a");
    }

    private void OnMenuConsolidate(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        // A time-range selection wins; otherwise the span of the selected clips.
        if (Timeline.ConsolidateSelection()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.menu.consolidated"); }
        else _vm.StatusText = Loc.T("main.menu.select_a_range_or_clips");
    }

    // The piano roll currently on screen, or null. Edit-menu clipboard commands target it
    // (the Cmd+C/X/V keys are handled by the grid itself; these back the menu items).
    private PianoRollView? ActiveRoll()
        => DetailPanel.IsVisible && DetailBody.Content is ClipEditorView ? _editorRoll : null;

    private void OnMenuCopy(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (SessionFocused && _session!.CopySelection()) return;
        if (_trackHeadersFocused && Timeline.CopySelectedTracks()) { _vm.StatusText = Loc.T("main.copied_track_s"); return; }
        if (ActiveRoll() is { } roll && roll.CopySelection()) { _vm.StatusText = Loc.T("main.menu.copied_notes"); return; }
        if (Timeline.CopyTimeSelection()) { _vm.StatusText = Loc.T("main.copied_range"); return; }
        if (Timeline.CopySelectedClip()) _vm.StatusText = Loc.T("main.menu.copied_clip");
    }
    private void OnMenuCut(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (SessionFocused && _session!.CutSelection()) return;
        if (_trackHeadersFocused && Timeline.CutSelectedTracks()) { _vm.StatusText = Loc.T("main.cut_track_s"); return; }
        if (ActiveRoll() is { } roll && roll.CutSelection()) { _vm.StatusText = Loc.T("main.menu.cut_notes"); return; }
        if (Timeline.CutTimeSelection()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.cut_range"); return; }
        if (Timeline.CutSelectedClip()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.menu.cut_clip"); }
    }
    private void OnMenuPaste(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (SessionFocused && _session!.PasteSelection()) return;
        if (_trackHeadersFocused && Timeline.PasteTracks()) { _vm.StatusText = Loc.T("main.pasted_track_s"); return; }
        if (ActiveRoll() is { } roll && roll.PasteClipboard()) { _vm.StatusText = Loc.T("main.menu.pasted_notes"); return; }
        if (Timeline.PasteClipboard()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.menu.pasted_clip"); }
    }
    // ⌘⇧C: the Arrangement's selected clips go to session slots, the Session view's selected
    // slot / scene goes onto the timeline at the playhead — whichever view is on screen.
    private void OnMenuCopyToOtherView(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (_session?.IsVisible == true)
        {
            if (!_session.CopySelectionToArrangement()) _vm.StatusText = Loc.T("main.menu.select_a_clip_or_a");
        }
        else if (Timeline.IsVisible && Timeline.CopySelectionToSession() < 0)
            _vm.StatusText = Loc.T("main.menu.select_clips_to_copy_to");
    }
    private void OnMenuDeleteSel(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (SessionFocused && _session!.DeleteSelection()) return;
        if (_trackHeadersFocused && Timeline.DeleteSelectedTracks()) { _vm.StatusText = Loc.T("main.deleted_track_s"); return; }
        if (ActiveRoll() is { } roll && roll.DeleteSelection()) { _vm.StatusText = Loc.T("main.menu.deleted_notes"); return; }
        if (Timeline.DeleteSelectedClips()) { _session?.Refresh(); _vm.StatusText = Loc.T("main.deleted_clip_s"); }
    }
    private void OnMenuToggleBrowser(object? sender, EventArgs e)
    {
        if (_detailMaximized) { SetDetailMaximized(false); Browser.IsVisible = true; return; }
        Browser.IsVisible = !Browser.IsVisible;
    }
    private void OnMenuToggleClip(object? sender, EventArgs e)
    {
        if (DetailPanel.IsVisible || DetailFloating) { DetailPanel.IsVisible = !DetailPanel.IsVisible; return; }
        // A closed panel kept following the selection (#308): reopen it through its checked tab
        // so it comes back at that tab's height on the current selection.
        if (DetailClipBtn.IsChecked == true && DetailClipBtn.IsEnabled) OnDetailClip(this, new RoutedEventArgs());
        else if (DetailPatternBtn.IsChecked == true && DetailPatternBtn.IsEnabled) OnDetailPattern(this, new RoutedEventArgs());
        // Devices: the track the chain already shows (Modular can point it at a track without
        // moving the arrangement selection), else the arrangement's selected track.
        else if ((_deviceChain?.TrackId ?? 0) is int shown and > 0) ShowDevices(shown);
        else if (Timeline.SelectedTrackId > 0) ShowDevices(Timeline.SelectedTrackId);
        DetailPanel.IsVisible = true;
    }
    private void OnMenuToggleOverview(object? sender, EventArgs e)
    {
        Timeline.ShowOverview = !Timeline.ShowOverview;
        if (_vm is not null) _vm.StatusText = Timeline.ShowOverview ? Loc.T("main.menu.overview_strip_shown") : Loc.T("main.menu.overview_strip_hidden");
    }
    private void OnMenuToggleSections(object? sender, EventArgs e)
    {
        Timeline.ShowSections = !Timeline.ShowSections;
        if (_vm is null) return;
        _vm.Settings.Current.ArrangementShowSections = Timeline.ShowSections;
        _vm.Settings.Save();
        _vm.StatusText = Timeline.ShowSections ? Loc.T("main.menu.sections_lane_shown") : Loc.T("main.menu.sections_lane_hidden");
    }
    // Follow: mirrors the transport bar's Follow button — one state, two surfaces.
    private void OnMenuToggleFollow(object? sender, EventArgs e)
        => SetFollowPlayhead(!Timeline.FollowPlayhead);

    // Cycles how much of the arrangement prints clip names: every clip → the head of each run
    // (the default, so a repeated pattern reads as one block) → none.
    private void OnMenuCycleClipNames(object? sender, EventArgs e)
    {
        Timeline.ClipLabels = Timeline.ClipLabels switch
        {
            ClipLabelMode.Every => ClipLabelMode.RunStart,
            ClipLabelMode.RunStart => ClipLabelMode.None,
            _ => ClipLabelMode.Every,
        };
        if (_vm is not null)
        {
            _vm.Settings.Current.ArrangementClipLabels = (int)Timeline.ClipLabels;
            _vm.Settings.Save();
        }
        if (_vm is not null)
            _vm.StatusText = Timeline.ClipLabels switch
            {
                ClipLabelMode.Every => Loc.T("main.menu.clip_names_every_clip"),
                ClipLabelMode.RunStart => Loc.T("main.menu.clip_names_first_of_a"),
                _ => Loc.T("main.menu.clip_names_none"),
            };
    }
    private void OnMenuPlayStop(object? sender, EventArgs e) => _vm?.Transport.PlayStopCommand.Execute(null);
    private void OnMenuRecord(object? sender, EventArgs e) { if (_vm is not null) _vm.Transport.RecordOn = !_vm.Transport.RecordOn; }
    private void OnMenuLoop(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        // A selection (time range, else selected clips) sets the loop to exactly that
        // range and enables it; pressing again once it already loops it turns looping off.
        var t = _vm.Transport;
        if (Timeline.SelectionLoopRange() is { } r
            && !(t.LoopOn && Engine.LoopStart == r.Start && Engine.LoopEnd == r.End))
        {
            Timeline.SetLoopRegion(r.Start, r.End);
            _vm.StatusText = Loc.T("main.menu.loop_set_to_selection");
            return;
        }
        t.LoopOn = !t.LoopOn;
    }
    private void OnMenuMetronome(object? sender, EventArgs e) { if (_vm is not null) _vm.Transport.MetronomeOn = !_vm.Transport.MetronomeOn; }
}
