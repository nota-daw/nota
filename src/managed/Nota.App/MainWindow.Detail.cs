// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
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
using Avalonia.Threading;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    // Base track colour for a track id (mirrors the arrangement palette, 1e).
    private SolidColorBrush TrackBrush(int trackId)
        => ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(Engine, trackId));

    // Detail-header track chip (1d/1e): colour dot · name · live routing/PDC summary.
    // trackId <= 0 shows a plain mode label (e.g. the full-width Mixer).
    // The summary reads the track's real state (record input, input monitor, MIDI source,
    // reported latency) and a light poll re-renders it when any of that changes elsewhere.
    private int _chipTrackId;
    private string _chipKey = "";
    private Avalonia.Threading.DispatcherTimer? _chipTimer;

    private void SetDetailChip(int trackId, string? modeLabel = null)
    {
        UpdateFreezeButton(trackId);   // M7: header Freeze button follows the shown track
        _chipTrackId = trackId;
        _chipKey = "";
        if (trackId <= 0)
        {
            DetailChipHost.Content = new TextBlock
            {
                Text = modeLabel ?? "", Classes = { "SectionLabel" }, VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }
        if (_chipTimer is null)
        {
            _chipTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _chipTimer.Tick += (_, _) => { if (_chipTrackId > 0 && DetailPanel.IsVisible) RenderDetailChip(); };
            _chipTimer.Start();
        }
        RenderDetailChip();
    }

    private void RenderDetailChip()
    {
        int trackId = _chipTrackId;
        string name = ChipTrackName(trackId);
        string summary = ChipSummary(trackId);
        string key = string.Concat(name, "\n", summary, "\n", Engine.GetTrackColorIndex(trackId).ToString(NotaNum.Culture));
        if (key == _chipKey) return;
        _chipKey = key;

        var dot = new Rectangle { Width = 8, Height = 8, RadiusX = 2, RadiusY = 2, Fill = TrackBrush(trackId), VerticalAlignment = VerticalAlignment.Center };
        var nameText = new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        var sumText = new TextBlock { Text = summary, Classes = { "Caption" }, VerticalAlignment = VerticalAlignment.Center };
        DetailChipHost.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { dot, nameText, sumText } };
    }

    private bool ChipTrackInfo(int trackId, out NotaTrackInfo info)
    {
        for (int i = 0; i < Engine.TrackCount; i++)
            if (Engine.TryGetTrackInfo(i, out info) && info.Id == trackId) return true;
        info = default;
        return false;
    }

    private string ChipTrackName(int trackId)
    {
        if (trackId == Engine.MasterTrackId) return Engine.GetTrackName(trackId) is { Length: > 0 } m ? m : "Master";
        string stored = Engine.GetTrackName(trackId);
        if (stored.Length > 0) return stored;
        ChipTrackInfo(trackId, out var ti);
        return ti.IsReturn ? $"Return {Engine.TrackReturnIndex(trackId) + 1}"
             : ti.IsGroup ? $"Group {trackId}"
             : (ti.IsInstrument ? "Inst " : "Audio ") + trackId;
    }

    // "· instrument · MIDI from Inst 1 · PDC 2.7 ms" / "· audio · In Ext · Monitor In · PDC 0.0 ms".
    private string ChipSummary(int trackId)
    {
        var parts = new List<string>();
        if (trackId == Engine.MasterTrackId) parts.Add("master");
        else if (ChipTrackInfo(trackId, out var ti))
        {
            if (ti.IsReturn) parts.Add("return");
            else if (ti.IsGroup) parts.Add("group");
            else if (ti.IsInstrument)
            {
                parts.Add("instrument");
                int src = Engine.GetTrackMidiSource(trackId);
                if (src > 0) parts.Add("MIDI from " + ChipTrackName(src));
            }
            else
            {
                parts.Add("audio");
                int src = Engine.GetTrackRecordInput(trackId);
                parts.Add("In " + (src == 0 ? "Ext" : src == -1 ? "Master" : ChipTrackName(src)));
                // A Master source stays silent while monitored (it would feed back).
                if (src != -1 && Engine.GetTrackMonitor(trackId)) parts.Add("Monitor in");
            }
        }
        double ms = Engine.SampleRate > 0 ? Engine.TrackLatencySamples(trackId) / Engine.SampleRate * 1000.0 : 0;
        parts.Add(string.Format(NotaNum.Culture, "PDC {0:0.0}\u2009ms", ms));
        return "· " + string.Join(" · ", parts);
    }

    // --- bottom detail panel (M4.1-D) --------------------------------------

    // Open the detail panel at the given height (its row is user-resizable via the
    // GridSplitter above it, so this just sets the initial/default height per mode).
    // honorPersist: the Clip tab restores the user's dragged height; the Devices tab always
    // snaps back to its natural card-fitting height, so switching to Devices resets it and
    // switching back to Clip restores the persisted size. The Devices tab also can't be
    // resized at all (no splitter): its cards have a fixed layout, so extra height is wasted
    // and less clips them.
    private void ShowDetail(double height, bool honorPersist, bool resizable = true)
    {
        var row = BodyGrid.RowDefinitions[2];
        row.MinHeight = 120;
        double h = honorPersist && _detailHeight > 120 ? _detailHeight : height;
        row.Height = new GridLength(h);
        DetailSplitter.IsVisible = resizable;
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
                           && DetailBody.Content is ClipEditorView or AudioClipEditorView or DrumPatternView
                           && SelectedClip(out _, out _, out _);
        bool wasPattern = keepClipTab && DetailBody.Content is DrumPatternView;

        ShowDevices(trackId, takeOverPanel: !keepClipTab);
        if (_modular?.IsVisible == true) _modular.Show(trackId);   // keep the graph on the selected track
        SyncClipTab();
        // The new clip may be on a track with no Drum Rack, so a pattern user falls back to
        // the piano roll rather than to Devices.
        if (wasPattern && DetailPatternBtn.IsEnabled) OnDetailPattern(this, new RoutedEventArgs());
        else if (keepClipTab) OnDetailClip(this, new RoutedEventArgs());
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
        _patternView = null;
        _patternTrackId = -1;
        _patternClipIndex = -1;
        _patternScene = -1;
        _sessionSlotTrack = -1;
        _sessionSlotScene = -1;
        if (DetailFloating) SetHost(_detailWindow!.ClipHost, ClipPlaceholder());
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
    // clip is selected; in Session, when a slot editor has been opened. The Pattern tab
    // rides along on the same target, narrowed to Drum Rack MIDI.
    internal void SyncClipTab()
    {
        DetailClipBtn.IsEnabled = _session?.IsVisible == true
            ? _clipEditor is not null || SessionAudioEditorOpen
            : SelectedClip(out _, out _, out _);
        DetailPatternBtn.IsEnabled = PatternTarget(out _, out _, out _);
        // Pattern only means something on a Drum Rack track, so elsewhere the tab is hidden
        // rather than greyed out. A step grid left on screen by a track that lost its rack
        // (or a selection that moved off it) hands the panel back to Devices.
        int contextTrack = _session?.IsVisible == true && _sessionSlotTrack > 0 ? _sessionSlotTrack
                         : _deviceChain is { TrackId: > 0 } dc ? dc.TrackId
                         : Timeline.SelectedTrackId;
        bool drumRack = IsDrumRackTrack(contextTrack);
        DetailPatternBtn.IsVisible = drumRack;
        if (!drumRack && !DetailFloating && DetailPanel.IsVisible && DetailBody.Content is DrumPatternView)
            ShowDevices(contextTrack);
    }

    // --- Detail · Pattern (Drum Rack step grid) -----------------------------

    // The Pattern tab edits the same notes as the Clip tab, so it targets the same clip —
    // but only when those notes drive a Drum Rack, since the rows are its pads.
    private bool PatternTarget(out int trackId, out int clipIndex, out int scene)
    {
        trackId = -1; clipIndex = -1; scene = -1;
        if (_session?.IsVisible == true)
        {
            if (_sessionSlotTrack <= 0 || _sessionSlotScene < 0 || !IsDrumRackTrack(_sessionSlotTrack)) return false;
            trackId = _sessionSlotTrack; scene = _sessionSlotScene;
            return true;
        }
        if (!SelectedClip(out int tid, out int ci, out bool isMidi) || !isMidi || !IsDrumRackTrack(tid)) return false;
        trackId = tid; clipIndex = ci;
        return true;
    }

    // The audio clip editor is open on a Session slot's take.
    private bool SessionAudioEditorOpen => _audioEditor is not null && SessionClip.SceneOf(_audioEditorClipIndex) >= 0;

    // A Session slot's loop-local playback position in beats, or -1 (hidden) unless that
    // slot is the one playing on its track — the slot loops on its own clock, not the song's.
    private double SessionSlotPlayhead(int trackId, int scene) =>
        Engine.SessionSlotState(trackId, scene) == 3 ? Engine.SessionSlotPosition(trackId) : -1;

    private bool IsDrumRackTrack(int trackId) => trackId > 0 && Engine.TrackInstrumentKind(trackId) == 4;

    // Build the step grid over one clip (scene >= 0: a Session slot). Both note sources are
    // wrapped here rather than inside the view, so the view stays a plain editor of "some
    // notes over some beats" and the staleness guard lives with the other editors.
    private void BuildPatternView(int trackId, int clipIndex, int scene)
    {
        Func<NotaNote[]> get;
        Action<NotaNote[]> set;
        Action<NotaNote[]>? setLive = null;
        Func<double> length;
        string title;
        if (scene >= 0)
        {
            title = $"Track {trackId} · Scene {scene + 1}";
            get = () => Engine.GetSessionNotes(trackId, scene);
            set = notes => { Engine.SetSessionNotes(trackId, scene, notes); _session?.Refresh(); };
            length = () => { double l = Engine.SessionSlotLength(trackId, scene); return l > 0 ? l : 4; };
        }
        else
        {
            title = $"Track {trackId} clip";
            get = () => Engine.GetClipNotes(trackId, clipIndex);
            // Same staleness guard as the piano roll: the clip can be deleted, undone or
            // reloaded under an editor that is still on screen.
            set = notes =>
            {
                if (!Engine.TryGetClipInfo(trackId, clipIndex, out var live) || !live.IsMidi) { InvalidatePatternView(); return; }
                Engine.SetClipNotes(trackId, clipIndex, notes);
            };
            // Painting a run of steps / sweeping the velocity lane streams through the
            // no-checkpoint write, so the whole gesture is one undo step (as in the roll).
            setLive = notes =>
            {
                if (!Engine.TryGetClipInfo(trackId, clipIndex, out var live) || !live.IsMidi) { InvalidatePatternView(); return; }
                Engine.SetClipNotesLive(trackId, clipIndex, notes);
            };
            length = () => Engine.TryGetClipInfo(trackId, clipIndex, out var ci) && ci.LengthBeats > 0 ? ci.LengthBeats : 4;
        }

        var view = new DrumPatternView(Engine, trackId, title, get, set, length, setLive);
        // A step written here is a note written to the clip: show it on the timeline and in
        // the piano roll straight away, the same way a roll edit shows up in the grid.
        view.Changed += () => { Timeline.Refresh(rebuildHeaders: false); ReloadEditorNotes(); };
        _patternView = view;
        _patternTrackId = trackId;
        _patternClipIndex = clipIndex;
        _patternScene = scene;
    }

    private void ShowPatternPanel()
    {
        _lastClipEditor = _patternView;
        SetHost(ClipDetailHost, _patternView);
        SetDetailChip(_patternTrackId);
        if (DetailFloating) return;   // floating: top pane only
        DetailPatternBtn.IsEnabled = true;
        DetailPatternBtn.IsChecked = true;
        DetailDevicesBtn.IsChecked = false;
        DetailClipBtn.IsChecked = false;
        ShowDetail(260, honorPersist: true);
    }

    private void OnDetailPattern(object? sender, RoutedEventArgs e)
    {
        if (!PatternTarget(out int tid, out int ci, out int scene))
        {
            DetailPatternBtn.IsChecked = false;
            if (_vm is not null) _vm.StatusText = "Select a MIDI clip on a Drum Rack track to step it.";
            return;
        }
        if (_patternView is null || _patternTrackId != tid || _patternClipIndex != ci || _patternScene != scene)
            BuildPatternView(tid, ci, scene);
        else
            _patternView.Reload();   // follow anything the piano roll / arrangement changed
        ShowPatternPanel();
    }

    // Drop a pattern grid whose clip no longer exists, mirroring InvalidateClipEditor.
    private void InvalidatePatternView()
    {
        bool onScreen = DetailBody.Content is DrumPatternView;
        _patternView = null;
        _patternTrackId = -1;
        _patternClipIndex = -1;
        _patternScene = -1;
        DetailPatternBtn.IsEnabled = false;
        DetailPatternBtn.IsChecked = false;
        if (onScreen) DetailPanel.IsVisible = false;
    }

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
        DetailPatternBtn.IsChecked = false;
        DetailClipBtn.IsChecked = false;
        // Devices: exactly the cards' height — the 1px seam, the 32px toolbar, the chain inset
        // above and below a card. Nothing floats under the chain.
        ShowDetail(1 + 32 + 2 * DeviceChainView.ChainInset + DeviceCardKit.CardH, honorPersist: false, resizable: false);
    }

    // Double-click on a MIDI clip: that clip — or, when it is one of two or more selected MIDI
    // clips, all of them together with it active (specs/029-multi-clip-editing).
    private void OpenClipEditor(int trackId, int clipIndex)
    {
        var sel = Timeline.SelectedMidiClips();
        if (sel.Count >= 2 && sel.Contains((trackId, clipIndex))) BuildClipEditor(sel, trackId, clipIndex);
        else BuildClipEditor(trackId, clipIndex);
        ShowClipPanel();
    }

    /// <summary>"Edit selected clips": every selected MIDI clip in one editor, the first in time
    /// active. False with fewer than two selected.</summary>
    internal bool EditSelectedClips()
    {
        var sel = Timeline.SelectedMidiClips();
        if (sel.Count < 2) return false;
        BuildClipEditor(sel, sel[0].TrackId, sel[0].ClipIndex);
        ShowClipPanel();
        return true;
    }

    /// <summary>A Clip editor with several arrangement clips open.</summary>
    private bool MultiClipEditorOpen => _editorRoll is { Clips.IsMulti: true } && _clipEditor is not null && _editorTrackId > 0;

    // The Clip editor follows the arrangement selection while it is on screen (specs/029
    // FR-001a): two or more selected MIDI clips open together — the clicked clip active when
    // it is one of them — and narrowing back to one clip returns to the single-clip editor.
    // Selecting a lone clip on another track is already followed through OnTrackSelected.
    private void FollowClipSelection()
    {
        if (_session?.IsVisible == true || _vm is null) return;
        bool shown = DetailFloating
            ? _clipEditor is not null && ReferenceEquals(_lastClipEditor, _clipEditor)
            : DetailPanel.IsVisible && DetailBody.Content is ClipEditorView;
        if (!shown) return;
        var midi = Timeline.SelectedMidiClips();
        if (midi.Count >= 2)
        {
            var open = _editorRoll?.Clips.Clips.Select(c => (c.TrackId, c.ClipIndex)).OrderBy(x => x).ToList();
            if (open is not null && open.SequenceEqual(midi.OrderBy(x => x))) return;   // already showing these
            var primary = (Timeline.SelectedTrackId, Timeline.SelectedClipIndex);
            var active = midi.Contains(primary) ? primary : midi[0];
            BuildClipEditor(midi, active.Item1, active.Item2);
            ShowClipPanel();
        }
        else if (midi.Count == 1 && MultiClipEditorOpen)
        {
            BuildClipEditor(midi[0].TrackId, midi[0].ClipIndex);
            ShowClipPanel();
        }
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
        // A Session slot's take edits through the same clip API (SessionClip index); its
        // edits repaint the Session grid instead of the timeline.
        bool slot = SessionClip.SceneOf(clipIndex) >= 0;
        _audioEditor = new AudioClipEditorView(Engine, trackId, clipIndex,
            TrackBrush(trackId), slot ? () => _session?.Refresh() : () => Timeline.Refresh());
        _audioEditor.WarpBuildRequested = () => _ = PollWarpBuildAsync();
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
        DetailPatternBtn.IsChecked = false;
        ShowDetail(250, honorPersist: true);
    }

    // Piano-roll audition (#46/#47) plays into the clip's own track, armed or not. Not while
    // a take is recording: a track's live notes are recorded, and a tap on a key mid-take
    // must not land in the clip.
    private void AuditionNoteOn(int trackId, int pitch, float velocity)
    {
        if (Engine.IsRecording) return;
        Engine.TrackNoteOn(trackId, pitch, velocity);
    }

    // Build the piano-roll editor for a specific arrangement clip (no panel switch).
    private void BuildClipEditor(int trackId, int clipIndex)
        => BuildClipEditor(new[] { (trackId, clipIndex) }, trackId, clipIndex);

    // Build the piano-roll editor for a set of arrangement MIDI clips, (activeTrack, activeClip)
    // active (no panel switch). One clip is the classic single-clip editor.
    private void BuildClipEditor(IReadOnlyList<(int TrackId, int ClipIndex)> clips, int activeTrack, int activeClip)
    {
        if (_vm is null) return;
        var open = new List<OpenClip>();
        foreach (var (t, c) in clips)
            if (Engine.TryGetClipInfo(t, c, out var info) && info.IsMidi)
                open.Add(new OpenClip(t, c, info.StartBeat, info.LengthBeats > 0 ? info.LengthBeats : 4));
        if (open.Count == 0) return;
        // The editor being replaced lets its pending placement go into its own clips first (it
        // may never have had focus to lose, e.g. after a menu paste).
        _editorRoll?.SettlePlacement();
        var set = new ClipSet(open, activeTrack, activeClip, Timeline.BeatsPerBar);
        PianoRollView roll = null!;
        roll = new PianoRollView
        {
            BeatsPerBar = Timeline.BeatsPerBar,   // bar lines and bar steps match the arrangement
            // Each clip whose notes changed, as one undo step. The roll can outlive a clip: the
            // track/clip may have been removed, shifted by a lower-index delete, undone, or
            // replaced by a project load while this editor stayed on screen. Pushing a stale
            // (trackId, clipIndex) throws InvalidArg — verify each is still a live MIDI clip
            // first, drop the dead ones, and drop the editor when none is left.
            CommitClips = changed =>
            {
                var live = LiveClips(roll, changed);
                if (live is null) return;
                Engine.BeginUndoGroup();
                try
                {
                    foreach (var cn in live) Engine.SetClipNotes(cn.TrackId, cn.ClipIndex, cn.Notes.ToArray());
                }
                finally { Engine.EndUndoGroup(); }
                Timeline.Refresh();
            },
            // Live drag stream: push every frame with no undo checkpoint so playback follows the
            // note instantly (the roll seeds one undo step at the gesture start).
            CommitClipsLive = changed =>
            {
                var live = LiveClips(roll, changed);
                if (live is null) return;
                foreach (var cn in live) Engine.SetClipNotesLive(cn.TrackId, cn.ClipIndex, cn.Notes.ToArray());
                Timeline.Refresh(rebuildHeaders: false);
            },
            // Light up the key being played (computer keyboard S–K / MIDI), regardless of routing.
            PollHeldNotes = buf => Engine.LiveHeldNotes(buf),
            ExpressionOf = id => Engine.GetNoteExpression(id),
            CreateExpression = pts => Engine.CreateNoteExpression(pts),
            // A note sounds on its own clip's track; the keys column on the active clip's.
            AuditionOn = (pitch, vel) => AuditionNoteOn(roll.AuditionTrackId > 0 ? roll.AuditionTrackId : _editorTrackId, pitch, vel),
            AuditionOff = pitch => Engine.TrackNoteOff(roll.AuditionTrackId > 0 ? roll.AuditionTrackId : _editorTrackId, pitch),
            ClipNameOf = (t, c) => Engine.GetClipName(t, c) is { Length: > 0 } n ? n : $"Track {t} clip",
            // The ruler moves the transport (#50): editor beat → arrangement beat.
            Seek = local =>
            {
                double beat = Math.Max(0, roll.Origin + local);
                Engine.Seek(beat);
                Timeline.SetPlayhead(beat);
            },
        };
        roll.SetTrackColor(TrackBrush(set.ActiveClip.TrackId));
        if (!set.IsMulti) roll.SetClipIdentity(set.ActiveClip.TrackId, set.ActiveClip.ClipIndex);
        roll.SetClips(set, ClipNotesOf(set), TrackBrush);

        _editorTrackId = set.ActiveClip.TrackId;
        _editorClipIndex = set.ActiveClip.ClipIndex;
        _editorRoll = roll;
        _clipEditor = new ClipEditorView(roll, $"Track {_editorTrackId} clip", set.ActiveClip.StartBeat, Engine,
                                         _editorTrackId, _editorClipIndex, set.ActiveClip.LengthBeats);
        // The active clip drives the detail chip, the playhead's live check and the envelopes.
        roll.ActiveClipChanged += () =>
        {
            if (!ReferenceEquals(_editorRoll, roll)) return;
            _editorTrackId = roll.ActiveClip.TrackId;
            _editorClipIndex = roll.ActiveClip.ClipIndex;
            SetDetailChip(_editorTrackId);
        };
    }

    // Every open clip's notes, as the engine holds them.
    private List<ClipNotes> ClipNotesOf(ClipSet set)
        => set.Clips.Select(c => new ClipNotes(c.TrackId, c.ClipIndex, Engine.GetClipNotes(c.TrackId, c.ClipIndex))).ToList();

    // The entries whose clip is still a live MIDI clip; dead clips leave the editor (reloaded
    // without them), and null — after dropping the editor — when none of its clips is left.
    private List<ClipNotes>? LiveClips(PianoRollView roll, IReadOnlyList<ClipNotes> changed)
    {
        var live = new List<ClipNotes>(changed.Count);
        bool lost = false;
        foreach (var cn in changed)
        {
            if (Engine.TryGetClipInfo(cn.TrackId, cn.ClipIndex, out var info) && info.IsMidi) live.Add(cn);
            else lost = true;
        }
        if (lost && !PruneDeadClips(roll)) return null;
        return live;
    }

    // Removes clips that no longer exist from the open set. False (and the editor dropped)
    // when none is left.
    private bool PruneDeadClips(PianoRollView roll)
    {
        var set = roll.Clips;
        bool removed = false;
        foreach (var c in set.Clips.ToList())
            if (!Engine.TryGetClipInfo(c.TrackId, c.ClipIndex, out var info) || !info.IsMidi)
            { set.Remove(c.TrackId, c.ClipIndex); removed = true; }
        if (set.IsEmpty)
        {
            if (ReferenceEquals(roll, _editorRoll)) InvalidateClipEditor();
            return false;
        }
        if (removed && ReferenceEquals(roll, _editorRoll))
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(roll, _editorRoll)) roll.SetClips(set, ClipNotesOf(set), TrackBrush); });
        return true;
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
        DetailPatternBtn.IsChecked = false;
        ShowDetail(250, honorPersist: true);
    }

    // A clip was trimmed / stretched / moved in the arrangement. Both clip editors snapshot the
    // clip's geometry when they're built, so push the new start+length into whichever one is
    // open on that clip — otherwise the editor keeps drawing the pre-drag length until it's
    // rebuilt (double-click, track switch, undo).
    private void OnClipGeometryChanged(int trackId, int clipIndex)
    {
        if (trackId <= 0 || clipIndex < 0 || !Engine.TryGetClipInfo(trackId, clipIndex, out var ci)) return;

        // The step grid snapshots the clip length too — a trim adds or drops steps.
        if (_patternView is not null && _patternTrackId == trackId && _patternClipIndex == clipIndex && _patternScene < 0)
            _patternView.Reload();

        if (_editorRoll is { } roll && _editorTrackId > 0 && roll.Clips.IndexOf(trackId, clipIndex) >= 0)
        {
            double len = ci.LengthBeats > 0 ? ci.LengthBeats : 4;
            var set = roll.Clips;
            var before = set.Clips[set.IndexOf(trackId, clipIndex)];
            set.UpdateGeometry(trackId, clipIndex, ci.StartBeat, len);
            // A reload resets the roll's selection, so only re-seed it when the clips really
            // moved on the editor's axis; a lone clip's pure move leaves its notes untouched.
            bool lengthChanged = Math.Abs(len - before.LengthBeats) > 1e-9;
            bool axisMoved = set.IsMulti && Math.Abs(ci.StartBeat - before.StartBeat) > 1e-9;
            if (lengthChanged || axisMoved) roll.SetClips(set, ClipNotesOf(set), TrackBrush);
            if (set.ActiveClip.TrackId == trackId && set.ActiveClip.ClipIndex == clipIndex)
                _clipEditor?.SetClipBounds(ci.StartBeat, len);
        }
        else if (_audioEditor is not null && _audioEditorTrackId == trackId && _audioEditorClipIndex == clipIndex)
        {
            _audioEditor.Reload();
        }
    }

    private void ReloadEditorNotes()
    {
        _patternView?.Reload();   // the step grid is a second view of the same notes
        if (_vm is null || _editorRoll is null) return;
        if (_editorTrackId < 0)
        {
            // A Session slot's piano roll follows a take recorded into it the same way (#156).
            if (_sessionSlotTrack <= 0 || _sessionSlotScene < 0) return;
            double slotLen = Engine.SessionSlotLength(_sessionSlotTrack, _sessionSlotScene);
            _editorRoll.SetNotes(Engine.GetSessionNotes(_sessionSlotTrack, _sessionSlotScene),
                slotLen > 0 ? slotLen : _editorRoll.LengthBeats);
            return;
        }
        // Every open clip, as the engine now holds it (undo/redo, recording); clips that are
        // gone leave the set, and their lengths/starts are re-read.
        var roll = _editorRoll;
        if (!PruneDeadClips(roll)) return;
        var set = roll.Clips;
        foreach (var c in set.Clips.ToList())
            if (Engine.TryGetClipInfo(c.TrackId, c.ClipIndex, out var info))
                set.UpdateGeometry(c.TrackId, c.ClipIndex, info.StartBeat, info.LengthBeats > 0 ? info.LengthBeats : c.LengthBeats);
        roll.SetClips(set, ClipNotesOf(set), TrackBrush);
        // The roll shows the recording take: draw its held notes too (#56).
        bool take = _recordPreview is { Active: true, SessionScene: < 0 } rp
                    && set.IndexOf(rp.TrackId, rp.TakeClipIndex) >= 0;
        roll.SetRecordTake(take ? _recordPreview : null, roll.Origin);
    }

    // Tab (when the detail panel was last used) flips between Devices and Clip.
    private void ToggleDetailTab()
    {
        var e = new RoutedEventArgs();
        // Devices → Pattern → Clip → Devices, skipping tabs the current selection can't fill.
        if (DetailBody.Content is DeviceChainView)
        {
            if (DetailPatternBtn.IsEnabled) OnDetailPattern(this, e);
            else if (DetailClipBtn.IsEnabled) OnDetailClip(this, e);
        }
        else if (DetailBody.Content is DrumPatternView)
        {
            if (DetailClipBtn.IsEnabled) OnDetailClip(this, e);
            else OnDetailDevices(this, e);
        }
        else
        {
            OnDetailDevices(this, e);
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
            else if (SessionAudioEditorOpen) ShowAudioClipPanel();
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

    private void OnCloseDetail(object? sender, RoutedEventArgs e) => CloseDetail();

    private void CloseDetail()
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
            // Audio slot: the full audio clip editor (region, warp, pitch, envelopes, ADSR).
            _sessionSlotTrack = -1; _sessionSlotScene = -1;   // audio slot: no pattern to step
            _editorRoll = null; _clipEditor = null;
            _editorTrackId = -1; _editorClipIndex = -1;
            BuildAudioClipEditor(trackId, SessionClip.Index(scene));
            SyncClipTab();
            ShowAudioClipPanel();
            return;
        }

        _sessionSlotTrack = trackId;   // so the Pattern tab can target this slot
        _sessionSlotScene = scene;
        SyncClipTab();                 // a Drum Rack slot lights the Pattern tab up
        // The editor being replaced lets its pending placement go into its own clip first (it
        // may never have had focus to lose, e.g. after a menu paste).
        _editorRoll?.SettlePlacement();
        var roll = new PianoRollView
        {
            BeatsPerBar = Timeline.BeatsPerBar,   // bar lines and bar steps match the arrangement
            Commit = notes =>
            {
                var arr = new NotaNote[notes.Count];
                for (int i = 0; i < notes.Count; i++) arr[i] = notes[i];
                Engine.SetSessionNotes(trackId, scene, arr);
                _session?.Refresh();
                _patternView?.Reload();
            },
            PollHeldNotes = buf => Engine.LiveHeldNotes(buf),
            ExpressionOf = id => Engine.GetNoteExpression(id),
            CreateExpression = pts => Engine.CreateNoteExpression(pts),
            AuditionOn = (pitch, vel) => AuditionNoteOn(trackId, pitch, vel),
            AuditionOff = pitch => Engine.TrackNoteOff(trackId, pitch),
        };
        double slotLen = Engine.SessionSlotLength(trackId, scene);
        roll.SetTrackColor(TrackBrush(trackId));
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
        DetailPatternBtn.IsChecked = false;
        ShowDetail(250, honorPersist: true);
    }
}
