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

namespace Nota.App;

public partial class MainWindow
{
    // --- menu (M4.1-B) -----------------------------------------------------

    private void OnAbout(object? sender, EventArgs e) => ShowAbout();
    public void ShowAbout() => new AboutWindow().Show(this);

    private void OnWhatsNew(object? sender, EventArgs e) => ShowWhatsNew();

    private void OnPreferences(object? sender, EventArgs e) => ShowPreferences();
    public void ShowPreferences()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        new PreferencesWindow(new SettingsViewModel(settings), _vm).Show(this);
    }

    private void OnStub(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.StatusText = "Not implemented yet.";
    }

    // --- undo / redo (M6-6) ------------------------------------------------

    private void OnMenuUndo(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (Engine.Undo()) { RefreshAfterUndoRedo(); _vm.StatusText = "Undo"; }
        else _vm.StatusText = "Nothing to undo.";
    }

    private void OnMenuRedo(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (Engine.Redo()) { RefreshAfterUndoRedo(); _vm.StatusText = "Redo"; }
        else _vm.StatusText = "Nothing to redo.";
    }

    // Undo/redo swaps the whole graph snapshot, so refresh every view that reads it.
    private void RefreshAfterUndoRedo()
    {
        Timeline.Refresh();
        _session?.Refresh();
        if (_deviceChain?.IsVisible == true && Timeline.SelectedTrackId > 0)
            _deviceChain.Show(Timeline.SelectedTrackId); // reflect device-chain edits
        ReloadEditorNotes();                             // reflect clip/note edits
    }

    private void OnMenuImport(object? sender, EventArgs e) => _ = DoImportAsync();
    private void OnMenuExport(object? sender, EventArgs e) => _ = DoExportAsync();
    private void OnMenuDuplicate(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        // While the piano roll has focus, Cmd+D duplicates the selected notes; otherwise
        // it duplicates the selected arrangement clip.
        if (_editorRoll is { GridFocused: true } roll && roll.DuplicateSelection())
        {
            _vm.StatusText = "Duplicated notes";
            return;
        }
        // In automation mode a selected range duplicates the envelope slice (chains on repeat).
        if (Timeline.AutomationMode && Timeline.DuplicateAutoSelection()) { _vm.StatusText = "Duplicated automation"; return; }
        // A time-range selection duplicates the covered slice (chains on repeat) before clips.
        if (Timeline.DuplicateTimeSelection()) { _session?.Refresh(); _vm.StatusText = "Duplicated range"; return; }
        if (Timeline.DuplicateSelectedClip()) { _session?.Refresh(); _vm.StatusText = "Duplicated clip(s)"; }
        else _vm.StatusText = "Select a clip first.";
    }

    private void OnMenuSplit(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        // In the piano roll Cmd+E has no note meaning; only split arrangement clips.
        // A time-range selection wins: split every covered track at the range edges.
        if (Timeline.SplitTimeSelection()) { _session?.Refresh(); _vm.StatusText = "Split at selection"; return; }
        if (Timeline.SplitSelectedAtPlayhead()) { _session?.Refresh(); _vm.StatusText = "Split clip(s) at playhead"; }
        else _vm.StatusText = "Select a range, or a clip the playhead crosses.";
    }

    private void OnMenuConsolidate(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        // A time-range selection wins; otherwise the span of the selected clips.
        if (Timeline.ConsolidateSelection()) { _session?.Refresh(); _vm.StatusText = "Consolidated"; }
        else _vm.StatusText = "Select a range or clips to consolidate.";
    }

    private void OnToggleLockEnvelopes(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        bool locked = !_vm.Engine.AutomationLock;
        _vm.Engine.SetAutomationLock(locked);
        if (sender is NativeMenuItem mi) mi.IsChecked = locked;
        _vm.StatusText = locked ? "Envelopes locked — clip moves keep automation in place"
                                : "Envelopes follow clips";
    }

    // The piano roll currently on screen, or null. Edit-menu clipboard commands target it
    // (the Cmd+C/X/V keys are handled by the grid itself; these back the menu items).
    private PianoRollView? ActiveRoll()
        => DetailPanel.IsVisible && DetailBody.Content is ClipEditorView ? _editorRoll : null;

    private void OnMenuCopy(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (ActiveRoll() is { } roll && roll.CopySelection()) { _vm.StatusText = "Copied notes"; return; }
        if (Timeline.CopySelectedClip()) _vm.StatusText = "Copied clip";
    }
    private void OnMenuCut(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (ActiveRoll() is { } roll && roll.CutSelection()) { _vm.StatusText = "Cut notes"; return; }
        if (Timeline.CutSelectedClip()) { _session?.Refresh(); _vm.StatusText = "Cut clip"; }
    }
    private void OnMenuPaste(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (ActiveRoll() is { } roll && roll.PasteClipboard()) { _vm.StatusText = "Pasted notes"; return; }
        if (Timeline.PasteClipboard()) { _session?.Refresh(); _vm.StatusText = "Pasted clip"; }
    }
    private void OnMenuDeleteSel(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        if (ActiveRoll() is { } roll && roll.DeleteSelection()) { _vm.StatusText = "Deleted notes"; return; }
        if (Timeline.DeleteSelectedClips()) { _session?.Refresh(); _vm.StatusText = "Deleted clip(s)"; }
    }
    private void OnMenuToggleBrowser(object? sender, EventArgs e) => Browser.IsVisible = !Browser.IsVisible;
    private void OnMenuToggleClip(object? sender, EventArgs e) => DetailPanel.IsVisible = !DetailPanel.IsVisible;
    private void OnMenuToggleOverview(object? sender, EventArgs e)
    {
        Timeline.ShowOverview = !Timeline.ShowOverview;
        if (_vm is not null) _vm.StatusText = Timeline.ShowOverview ? "Overview strip shown" : "Overview strip hidden";
    }
    private void OnMenuToggleSections(object? sender, EventArgs e)
    {
        Timeline.ShowSections = !Timeline.ShowSections;
        if (_vm is null) return;
        _vm.Settings.Current.ArrangementShowSections = Timeline.ShowSections;
        _vm.Settings.Save();
        _vm.StatusText = Timeline.ShowSections ? "Sections lane shown" : "Sections lane hidden";
    }
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
                ClipLabelMode.Every => "Clip names: every clip",
                ClipLabelMode.RunStart => "Clip names: first of a run",
                _ => "Clip names: none",
            };
    }
    private void OnMenuPlayStop(object? sender, EventArgs e) => _vm?.Transport.PlayStopCommand.Execute(null);
    private void OnMenuRecord(object? sender, EventArgs e) { if (_vm is not null) _vm.Transport.RecordOn = !_vm.Transport.RecordOn; }
    private void OnMenuLoop(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        // A time-range selection wins: loop exactly that range (and enable looping).
        if (Timeline.LoopTimeSelection()) { _vm.StatusText = "Loop set to selection"; return; }
        _vm.Transport.LoopOn = !_vm.Transport.LoopOn;
    }
    private void OnMenuMetronome(object? sender, EventArgs e) { if (_vm is not null) _vm.Transport.MetronomeOn = !_vm.Transport.MetronomeOn; }
}
