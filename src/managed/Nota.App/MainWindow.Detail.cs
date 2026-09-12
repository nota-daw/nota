// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
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
    // Base track colour for a track id (mirrors the arrangement palette, 1e).
    private Color TrackColor(int trackId)
        => ArrangementView.TrackColorForIndex(ArrangementView.EffectiveColorIndex(Engine, trackId));

    // Detail-header track chip (1d/1e): colour dot · name · routing/PDC summary.
    // trackId <= 0 shows a plain mode label (e.g. the full-width Mixer).
    private void SetDetailChip(int trackId, string? modeLabel = null)
    {
        UpdateFreezeButton(trackId);   // M7: header Freeze button follows the shown track
        if (trackId <= 0)
        {
            DetailChipHost.Content = new TextBlock
            {
                Text = modeLabel ?? "", Classes = { "SectionLabel" }, VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }

        bool master = trackId == Engine.MasterTrackId;
        bool inst = false, ret = false;
        if (!master)
            for (int i = 0; i < Engine.TrackCount; i++)
                if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) { inst = ti.IsInstrument; ret = ti.IsReturn; break; }

        string storedName = Engine.GetTrackName(trackId);
        string name = storedName is { Length: > 0 } ? storedName
            : master ? "Master" : ret ? $"Return {Engine.TrackReturnIndex(trackId) + 1}" : (inst ? "Inst " : "Audio ") + trackId;
        string type = master ? "master" : ret ? "return" : inst ? "instrument · MIDI in" : "audio · In 1";
        double ms = Engine.SampleRate > 0 ? Engine.TrackLatencySamples(trackId) / Engine.SampleRate * 1000.0 : 0;
        string summary = string.Format(System.Globalization.CultureInfo.InvariantCulture, "· {0} · Monitor Auto · PDC {1:0.0} ms", type, ms);

        var dot = new Rectangle { Width = 8, Height = 8, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(TrackColor(trackId)), VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock { Text = name, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        var sumText = new TextBlock { Text = summary, Classes = { "Caption" }, VerticalAlignment = VerticalAlignment.Center };
        DetailChipHost.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { dot, nameText, sumText } };
    }

    // --- bottom detail panel (M4.1-D) --------------------------------------

    // Open the detail panel at the given height (its row is user-resizable via the
    // GridSplitter above it, so this just sets the initial/default height per mode).
    // honorPersist: the Clip tab restores the user's dragged height; the Devices tab always
    // snaps back to its natural card-fitting height, so switching to Devices resets it and
    // switching back to Clip restores the persisted size.
    private void ShowDetail(double height, bool honorPersist)
    {
        var row = BodyGrid.RowDefinitions[2];
        row.MinHeight = 120;
        double h = honorPersist && _detailHeight > 120 ? _detailHeight : height;
        _lastSetDetailHeight = h;
        row.Height = new GridLength(h);
        DetailSplitter.IsVisible = true;
        DetailPanel.IsVisible = true;
    }

    private void OnTrackSelected(int trackId)
    {
        // Switching to a different track abandons the previously edited clip. Reset the clip
        // editor to empty so a stale editor from the old track doesn't linger — most visible
        // in the popped-out Devices/Clip window, whose top pane isn't touched by ShowDevices.
        if (_editorTrackId != trackId && _audioEditorTrackId != trackId)
            ResetClipEditor();

        // Selecting a clip in the arrangement also selects its track — including the select
        // that an edge-drag does before it trims. That must not pull a user who is working in
        // the Clip tab over to Devices, so refresh the chain underneath and re-point the Clip
        // tab at the newly selected clip. With no clip to show, Devices takes the panel as before.
        bool keepClipTab = !DetailFloating && DetailPanel.IsVisible
                           && DetailBody.Content is ClipEditorView or AudioClipEditorView
                           && SelectedClip(out _, out _, out _);

        ShowDevices(trackId, takeOverPanel: !keepClipTab);
        if (_modular?.IsVisible == true) _modular.Show(trackId);   // keep the graph on the selected track
        SyncClipTab();
        if (keepClipTab) OnDetailClip(this, new RoutedEventArgs());
    }

    // Clear all clip-editor state and blank the clip pane (placeholder while floating, so the
    // popped-out window shows "select a clip" instead of the previous track's editor).
    private void ResetClipEditor()
    {
        _editorRoll = null;
        _clipEditor = null;
        _audioEditor = null;
        _lastClipEditor = null;
        _editorTrackId = -1;
        _editorClipIndex = -1;
        _audioEditorTrackId = -1;
        _audioEditorClipIndex = -1;
        if (DetailFloating) _detailWindow!.ClipHost.Content = ClipPlaceholder();
    }

    // The Clip tab targets the *currently selected* arrangement clip (MIDI or audio),
    // so it never opens a stale clip from a previously edited track.
    private bool SelectedClip(out int trackId, out int clipIndex, out bool isMidi)
    {
        trackId = Timeline.SelectedTrackId;
        clipIndex = Timeline.SelectedClipIndex;
        isMidi = false;
        if (trackId > 0 && clipIndex >= 0 && Engine.TryGetClipInfo(trackId, clipIndex, out var ci))
        { isMidi = ci.IsMidi; return true; }
        return false;
    }

    // Enable the Clip tab for the current context: in the arrangement, whenever a
    // clip is selected; in Session, when a slot editor has been opened.
    internal void SyncClipTab()
        => DetailClipBtn.IsEnabled = _session?.IsVisible == true
            ? _clipEditor is not null
            : SelectedClip(out _, out _, out _);

    // takeOverPanel: false shows the chain without claiming the docked panel, for callers that
    // only follow the selection (see OnTrackSelected). Explicit "show me the devices" callers
    // — a browser drop, Convert, the Devices tab — leave it true.
    private void ShowDevices(int trackId, bool takeOverPanel = true)
    {
        if (_vm is null || _deviceChain is null) return;
        // No track (the selected one was just deleted): empty the chain instead of leaving the
        // dead track's instrument/effect cards on screen — they'd still edit a removed track.
        // Don't pop the panel open for it; just clear whatever is already showing.
        if (trackId <= 0)
        {
            _deviceChain.Show(-1);
            SetDetailChip(-1, "Devices");
            ApplyFrozenChainDim(-1);
            return;
        }
        _deviceChain.Show(trackId);
        SetDetailChip(trackId);
        ApplyFrozenChainDim(trackId);      // M7: dim the chain while frozen
        // Docked, both tabs share one host, so hosting the chain *is* the switch to Devices —
        // skip it when we're only following the selection. Floating has its own pane, so the
        // chain always goes there and no tab is at stake.
        if (DetailFloating || takeOverPanel) SetHost(DeviceHost, _deviceChain);
        if (DetailFloating || !takeOverPanel) return;   // floating: bottom pane only, don't touch the docked row/tabs
        DetailDevicesBtn.IsChecked = true;
        DetailClipBtn.IsChecked = false;
        ShowDetail(320, honorPersist: false);   // Devices: natural card-fitting height
    }

    private void OpenClipEditor(int trackId, int clipIndex)
    {
        BuildClipEditor(trackId, clipIndex);
        ShowClipPanel();
    }

    // --- audio clip editor (M-audio-editor) --------------------------------
    private void OpenAudioClipEditor(int trackId, int clipIndex)
    {
        BuildAudioClipEditor(trackId, clipIndex);
        ShowAudioClipPanel();
    }

    private void BuildAudioClipEditor(int trackId, int clipIndex)
    {
        if (_vm is null) return;
        _audioEditor = new AudioClipEditorView(Engine, trackId, clipIndex,
            new SolidColorBrush(TrackColor(trackId)), () => Timeline.Refresh());
        _audioEditorTrackId = trackId;
        _audioEditorClipIndex = clipIndex;
    }

    private void ShowAudioClipPanel()
    {
        _lastClipEditor = _audioEditor;
        SetHost(ClipDetailHost, _audioEditor);
        SetDetailChip(_audioEditorTrackId);
        if (DetailFloating) return;   // floating: top pane only
        DetailClipBtn.IsEnabled = true;
        DetailClipBtn.IsChecked = true;
        DetailDevicesBtn.IsChecked = false;
        ShowDetail(250, honorPersist: true);
    }

    // Build the piano-roll editor for a specific arrangement clip (no panel switch).
    private void BuildClipEditor(int trackId, int clipIndex)
    {
        if (_vm is null) return;
        var roll = new PianoRollView
        {
            Commit = notes =>
            {
                // The roll can outlive its clip: the target track/clip may have been
                // removed, shifted by a lower-index delete, undone, or replaced by a
                // project load while this editor stayed on screen. Pushing a stale
                // (trackId, clipIndex) throws InvalidArg — verify it's still a live
                // MIDI clip first, otherwise drop the dead editor.
                if (!Engine.TryGetClipInfo(trackId, clipIndex, out var live) || !live.IsMidi)
                {
                    InvalidateClipEditor();
                    return;
                }
                var arr = new NotaNote[notes.Count];
                for (int i = 0; i < notes.Count; i++) arr[i] = notes[i];
                Engine.SetClipNotes(trackId, clipIndex, arr);
                Timeline.Refresh();
            },
            // Live drag stream: push every frame with no undo checkpoint so playback follows the
            // note instantly (the grid seeds one undo entry at the gesture start).
            CommitLive = notes =>
            {
                if (!Engine.TryGetClipInfo(trackId, clipIndex, out var live) || !live.IsMidi) { InvalidateClipEditor(); return; }
                var arr = new NotaNote[notes.Count];
                for (int i = 0; i < notes.Count; i++) arr[i] = notes[i];
                Engine.SetClipNotesLive(trackId, clipIndex, arr);
                Timeline.Refresh(rebuildHeaders: false);
            },
            // Light up the key being played (computer keyboard S–K / MIDI), regardless of routing.
            PollHeldNotes = buf => Engine.LiveHeldNotes(buf),
        };
        double length = Engine.TryGetClipInfo(trackId, clipIndex, out var ci) && ci.LengthBeats > 0 ? ci.LengthBeats : 4;
        double start = ci.StartBeat;
        roll.SetTrackColor(TrackColor(trackId));
        roll.SetClipIdentity(trackId, clipIndex);
        roll.SetNotes(Engine.GetClipNotes(trackId, clipIndex), length);

        _editorTrackId = trackId;
        _editorClipIndex = clipIndex;
        _editorRoll = roll;
        _clipEditor = new ClipEditorView(roll, $"Track {trackId} clip", start, Engine, trackId, clipIndex, length);
    }

    // Drop a clip editor whose target no longer exists (removed/undone/reloaded) and
    // hide the Clip tab if it's the one on screen, so no further gesture pushes stale ids.
    private void InvalidateClipEditor()
    {
        _editorRoll = null;
        _clipEditor = null;
        _editorTrackId = -1;
        _editorClipIndex = -1;
        DetailClipBtn.IsEnabled = false;
        DetailClipBtn.IsChecked = false;
        if (DetailBody.Content is ClipEditorView) DetailPanel.IsVisible = false;
    }

    // Show whatever _clipEditor currently holds in the Clip tab (or the floating top pane).
    private void ShowClipPanel()
    {
        _lastClipEditor = _clipEditor;
        SetHost(ClipDetailHost, _clipEditor);
        SetDetailChip(_editorTrackId);
        if (DetailFloating) return;   // floating: top pane only
        DetailClipBtn.IsEnabled = true;
        DetailClipBtn.IsChecked = true;
        DetailDevicesBtn.IsChecked = false;
        ShowDetail(250, honorPersist: true);
    }

    // A clip was trimmed / stretched / moved in the arrangement. Both clip editors snapshot the
    // clip's geometry when they're built, so push the new start+length into whichever one is
    // open on that clip — otherwise the editor keeps drawing the pre-drag length until it's
    // rebuilt (double-click, track switch, undo).
    private void OnClipGeometryChanged(int trackId, int clipIndex)
    {
        if (trackId <= 0 || clipIndex < 0 || !Engine.TryGetClipInfo(trackId, clipIndex, out var ci)) return;

        if (_editorRoll is not null && _editorTrackId == trackId && _editorClipIndex == clipIndex)
        {
            double len = ci.LengthBeats > 0 ? ci.LengthBeats : _editorRoll.LengthBeats;
            // SetNotes resets the roll's selection, so only re-seed it when the span really moved;
            // a pure move leaves the notes (clip-local) untouched.
            if (Math.Abs(len - _editorRoll.LengthBeats) > 1e-9)
                _editorRoll.SetNotes(Engine.GetClipNotes(trackId, clipIndex), len);
            _clipEditor?.SetClipBounds(ci.StartBeat, len);
        }
        else if (_audioEditor is not null && _audioEditorTrackId == trackId && _audioEditorClipIndex == clipIndex)
        {
            _audioEditor.Reload();
        }
    }

    private void ReloadEditorNotes()
    {
        if (_vm is null || _editorRoll is null || _editorTrackId < 0) return;
        double len = Engine.TryGetClipInfo(_editorTrackId, _editorClipIndex, out var ci) && ci.LengthBeats > 0 ? ci.LengthBeats : _editorRoll.LengthBeats;
        _editorRoll.SetNotes(Engine.GetClipNotes(_editorTrackId, _editorClipIndex), len);
    }

    // Tab (when the detail panel was last used) flips between Devices and Clip.
    private void ToggleDetailTab()
    {
        if (DetailBody.Content is DeviceChainView)
        {
            if (DetailClipBtn.IsEnabled) OnDetailClip(this, new RoutedEventArgs());
        }
        else
        {
            OnDetailDevices(this, new RoutedEventArgs());
        }
    }

    private void OnDetailDevices(object? sender, RoutedEventArgs e)
    {
        int t = Timeline.SelectedTrackId > 0 ? Timeline.SelectedTrackId : LastInstrumentTrack();
        if (t > 0) ShowDevices(t);
        else { DetailDevicesBtn.IsChecked = false; if (_vm is not null) _vm.StatusText = "Select a track first."; }
    }

    private void OnDetailClip(object? sender, RoutedEventArgs e)
    {
        // Session: show the opened slot editor. Arrangement: always target the
        // currently selected clip, rebuilding if the selection moved tracks.
        if (_session?.IsVisible == true)
        {
            if (_clipEditor is not null) ShowClipPanel();
            else RejectClipTab("Double-click a slot to edit it.");
            return;
        }
        if (!SelectedClip(out int tid, out int ci, out bool isMidi))
        {
            RejectClipTab("Select a clip to edit it.");
            return;
        }
        if (isMidi)
        {
            if (_clipEditor is null || _editorTrackId != tid || _editorClipIndex != ci)
                BuildClipEditor(tid, ci);
            ShowClipPanel();
        }
        else
        {
            if (_audioEditor is null || _audioEditorTrackId != tid || _audioEditorClipIndex != ci)
                BuildAudioClipEditor(tid, ci);
            ShowAudioClipPanel();
        }
    }

    private void RejectClipTab(string message)
    {
        DetailClipBtn.IsChecked = false;
        if (_vm is not null) _vm.StatusText = message;
    }

    private void OnCloseDetail(object? sender, RoutedEventArgs e)
    {
        DetailPanel.IsVisible = false;
        DetailSplitter.IsVisible = false;
        var row = BodyGrid.RowDefinitions[2];
        row.MinHeight = 0;
        row.Height = new GridLength(0);
    }

    private void OpenSessionClipEditor(int trackId, int scene)
    {
        if (_vm is null) return;

        // Audio slot → the compact audio-slot editor; MIDI slot → the piano roll.
        if (Engine.TryGetSessionAudioSlot(trackId, scene, out var _))
        {
            var audioEd = new SessionAudioSlotEditor(Engine, trackId, scene, new SolidColorBrush(TrackColor(trackId)));
            _editorRoll = null; _clipEditor = null;
            _editorTrackId = -1; _editorClipIndex = -1;
            _lastClipEditor = audioEd;
            SetHost(ClipDetailHost, audioEd);
            SetDetailChip(trackId);
            if (DetailFloating) return;
            DetailClipBtn.IsEnabled = true;
            DetailClipBtn.IsChecked = true;
            DetailDevicesBtn.IsChecked = false;
            ShowDetail(250, honorPersist: true);
            return;
        }

        var roll = new PianoRollView
        {
            Commit = notes =>
            {
                var arr = new NotaNote[notes.Count];
                for (int i = 0; i < notes.Count; i++) arr[i] = notes[i];
                Engine.SetSessionNotes(trackId, scene, arr);
                _session?.Refresh();
            },
            PollHeldNotes = buf => Engine.LiveHeldNotes(buf),
        };
        double slotLen = Engine.SessionSlotLength(trackId, scene);
        roll.SetTrackColor(TrackColor(trackId));
        roll.SetClipIdentity(trackId, -(scene + 2));   // distinct key from arrangement clips
        roll.SetNotes(Engine.GetSessionNotes(trackId, scene), slotLen > 0 ? slotLen : 4);

        _editorRoll = roll;
        _editorTrackId = -1;  // session slot: recording-tick reload N/A
        _editorClipIndex = -1;
        _clipEditor = new ClipEditorView(roll, $"Track {trackId} · Scene {scene + 1}", 0);
        _lastClipEditor = _clipEditor;
        SetHost(ClipDetailHost, _clipEditor);
        SetDetailChip(trackId);
        if (DetailFloating) return;   // floating: top pane only
        DetailClipBtn.IsEnabled = true;   // a clip is now available to edit
        DetailClipBtn.IsChecked = true;
        DetailDevicesBtn.IsChecked = false;
        ShowDetail(250, honorPersist: true);
    }
}
