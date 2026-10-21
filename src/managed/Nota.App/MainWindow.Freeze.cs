// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Track freeze (M7). The header Freeze button bounces the selected track's pre-fader
// chain (instrument + MIDI FX + devices) into an audio buffer that plays in place of
// the live chain — the mixer strip stays live. Right-click offers Flatten, which
// replaces the track with a plain audio clip of the frozen sound. Frozen state shows
// as an ice tint + snowflake on the arrangement header and a dimmed device chain.
// The arrangement track context menu exposes the same commands (plus Live Freeze).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.App;

public partial class MainWindow
{
    private readonly TrackFreezer _freezer = new();
    private int _freezeTrackId = -1;
    private bool _freezeMenuBuilt;
    private MenuItem? _flattenItem;

    // Instrument + audio tracks can be frozen; returns, groups and the master can't.
    private bool IsFreezable(int trackId)
    {
        if (trackId <= 0 || trackId == Engine.MasterTrackId) return false;
        for (int i = 0; i < Engine.TrackCount; i++)
            if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId)
                return !ti.IsReturn && !ti.IsGroup;
        return false;
    }

    // Reflect the current track's freeze state onto the header button (called from ShowDevices).
    private void UpdateFreezeButton(int trackId)
    {
        _freezeTrackId = trackId;
        if (FreezeBtn is null) return;
        // A live-freeze source/frozen track owns the header cluster (Edit/Done); the plain
        // in-place Freeze button steps aside so the two features never overlap on one track.
        bool linked = LinkForSource(trackId) is not null || LinkForFrozen(trackId) is not null;
        bool freezable = IsFreezable(trackId) && !linked;
        FreezeBtn.IsVisible = freezable;
        UpdateLiveFreezeButtons(trackId);
        EnsureLiveFreezeMenu();
        if (!freezable) return;

        bool frozen = Engine.IsTrackFrozen(trackId);
        FreezeLabel.Text = frozen ? "Frozen" : "Freeze";
        IBrush idle = NotaPalette.TextSecondary;
        FreezeIcon.Foreground = frozen ? IceBrush : idle;
        FreezeLabel.Foreground = frozen ? IceBrush : idle;
        EnsureFreezeMenu();
    }

    // Dim the device chain while frozen — its instrument/devices are inactive until unfreeze.
    private void ApplyFrozenChainDim(int trackId)
    {
        if (_deviceChain is null) return;
        // Dim when in-place frozen, or when this is a sleeping live-freeze source (press Edit
        // to wake it). An open edit session leaves the chain fully interactive.
        bool inPlace = IsFreezable(trackId) && Engine.IsTrackFrozen(trackId);
        bool sleeping = LinkForSource(trackId) is { State: LinkState.Frozen };
        bool dim = inPlace || sleeping;
        Inactive.Set(_deviceChain, dim);
    }

    private void EnsureFreezeMenu()
    {
        if (_freezeMenuBuilt) return;
        _freezeMenuBuilt = true;
        _flattenItem = MenuKit.Danger("Flatten to audio track", GlyphKind.Flatten,
            () => { if (_freezeTrackId > 0) _ = FlattenTrackAsync(_freezeTrackId); });
        var menu = new ContextMenu();
        menu.Items.Add(_flattenItem);
        // Flatten needs a frozen buffer to bounce, so gate it on the current state.
        menu.Opening += (_, _) =>
        { if (_flattenItem is not null) _flattenItem.IsEnabled = _freezeTrackId > 0 && Engine.IsTrackFrozen(_freezeTrackId); };
        FreezeBtn.ContextMenu = menu;
    }

    private async void OnFreezeClick(object? sender, RoutedEventArgs e)
    {
        if (_freezeTrackId > 0) await ToggleFreezeAsync(_freezeTrackId);
    }

    private async Task ToggleFreezeAsync(int trackId)
    {
        if (_vm is null || !IsFreezable(trackId)) return;
        if (Engine.IsTrackFrozen(trackId)) UnfreezeTrack(trackId);
        else await FreezeTrackAsync(trackId);
    }

    private async Task FreezeTrackAsync(int trackId)
    {
        if (_vm is null) return;
        double endBeats = ProjectEndBeats();
        if (endBeats <= 0.0) { _vm.StatusText = "Nothing to freeze — the arrangement is empty."; return; }

        // Freeze takes over the engine (backend stopped) exactly like an export bounce, so
        // suspend the UI clock and show the modal progress dialog while it renders.
        _vm.SuspendEnginePolling = true;
        try
        {
            bool ok = false;
            await RunBlockingAsync("Freeze", "Freezing track…", async prog =>
            {
                var frac = new Progress<double>(f => prog.Report(ProgressReport.At(f, $"Freezing track… {f * 100:0}\u2009%")));
                ok = await Task.Run(() => _freezer.Freeze(Engine, trackId, endBeats,
                    _vm.Transport.LoopOn, _vm.Transport.MetronomeOn, frac));
            });
            _vm.StatusText = ok ? "Track frozen — its devices are idle until you unfreeze." : "Couldn't freeze this track.";
        }
        catch (Exception ex) { _vm.StatusText = $"Freeze failed: {ex.Message}"; }
        finally { _vm.SuspendEnginePolling = false; }

        AfterFreezeChange();
    }

    private void UnfreezeTrack(int trackId)
    {
        Engine.UnfreezeTrack(trackId);
        if (_vm is not null) _vm.StatusText = "Track unfrozen.";
        AfterFreezeChange();
    }

    // Refresh the header buttons + chain dim for the *shown* track (not necessarily the one
    // acted on), then repaint the arrangement header snowflake / ice tint.
    private void AfterFreezeChange()
    {
        UpdateFreezeButton(_freezeTrackId);
        ApplyFrozenChainDim(_freezeTrackId);
        Timeline.Refresh();
    }

    // --- arrangement track context menu -----------------------------------

    // Freeze entries for a track header's right-click menu, mirroring the header button
    // cluster (spec 034). "Live freeze" is a switch, on for either side of a live-freeze link:
    // switching it on live-freezes the track, switching it off unfreezes the link (wakes the
    // source). While linked, Edit (or Done / Discard mid-session) and Flatten follow; a track
    // frozen in place offers Unfreeze / Flatten instead. Each command first selects its track
    // so the device panel and header buttons follow.
    private IReadOnlyList<Control> BuildTrackFreezeMenu(int trackId)
    {
        var items = new List<Control>();
        if ((LinkForSource(trackId) ?? LinkForFrozen(trackId)) is { } link)
        {
            int src = link.SourceId;
            items.Add(MenuKit.SwitchItem("Live freeze", GlyphKind.ChainLink, true, () => UnfreezeLink(src)));
            if (link.State == LinkState.Editing)
            {
                items.Add(MenuKit.Item("Done — re-freeze", GlyphKind.Check,
                    () => { Timeline.Select(trackId, -1); _ = CommitEditSessionAsync(src); }));
                items.Add(MenuKit.Danger("Discard edits", GlyphKind.Cross,
                    () => { Timeline.Select(trackId, -1); DiscardEditSession(src); }));
            }
            else
                // Editing changes the source's notes/devices, so land on the source track.
                items.Add(MenuKit.Item("Edit source", GlyphKind.Edit,
                    () => { Timeline.Select(src, -1); BeginEditSession(src); }));
            items.Add(MenuKit.Danger("Flatten (remove source)", GlyphKind.Flatten, () => FlattenLinkAsync(src)));
            return items;
        }

        if (!IsFreezable(trackId)) return items;
        if (Engine.IsTrackFrozen(trackId))
        {
            items.Add(MenuKit.Item("Unfreeze track", GlyphKind.Freeze,
                () => { Timeline.Select(trackId, -1); UnfreezeTrack(trackId); }));
            items.Add(MenuKit.Danger("Flatten to audio track", GlyphKind.Flatten,
                () => { Timeline.Select(trackId, -1); _ = FlattenTrackAsync(trackId); }));
        }
        else
        {
            items.Add(MenuKit.Item("Freeze track", GlyphKind.Freeze,
                () => { Timeline.Select(trackId, -1); _ = FreezeTrackAsync(trackId); }));
            items.Add(MenuKit.SwitchItem("Live freeze", GlyphKind.ChainLink, false,
                () => { Timeline.Select(trackId, -1); _ = LiveFreezeAsync(trackId); }));
        }
        return items;
    }

    // Freeze entries for the multi-track menu: freeze the live tracks / unfreeze the frozen
    // ones among the selection. Live-freeze pairs are per-track, so they're left to each
    // track's own menu, as are returns and groups (never freezable).
    private IReadOnlyList<Control> BuildTracksFreezeMenu(IReadOnlyList<int> trackIds)
    {
        var items = new List<Control>();
        var plain = trackIds.Where(id => IsFreezable(id) && LinkForSource(id) is null && LinkForFrozen(id) is null).ToList();
        var live = plain.Where(id => !Engine.IsTrackFrozen(id)).ToList();
        var frozen = plain.Where(id => Engine.IsTrackFrozen(id)).ToList();
        if (live.Count > 0)
            items.Add(MenuKit.Item(live.Count == 1 ? "Freeze track" : $"Freeze {live.Count} tracks", GlyphKind.Freeze,
                () => _ = FreezeTracksAsync(live)));
        if (frozen.Count > 0)
            items.Add(MenuKit.Item(frozen.Count == 1 ? "Unfreeze track" : $"Unfreeze {frozen.Count} tracks", GlyphKind.Freeze,
                () => UnfreezeTracks(frozen)));
        return items;
    }

    // Freeze several tracks behind one progress dialog (each bounce takes over the engine).
    private async Task FreezeTracksAsync(IReadOnlyList<int> trackIds)
    {
        if (_vm is null || trackIds.Count == 0) return;
        double endBeats = ProjectEndBeats();
        if (endBeats <= 0.0) { _vm.StatusText = "Nothing to freeze — the arrangement is empty."; return; }

        _vm.SuspendEnginePolling = true;
        int done = 0;
        try
        {
            await RunBlockingAsync("Freeze", "Freezing tracks…", async prog =>
            {
                for (int i = 0; i < trackIds.Count; i++)
                {
                    int k = i;
                    var frac = new Progress<double>(f =>
                    {
                        double all = (k + f) / trackIds.Count;
                        prog.Report(ProgressReport.At(all, $"Freezing track {k + 1} of {trackIds.Count}… {all * 100:0}\u2009%"));
                    });
                    if (await Task.Run(() => _freezer.Freeze(Engine, trackIds[k], endBeats,
                            _vm.Transport.LoopOn, _vm.Transport.MetronomeOn, frac)))
                        done++;
                }
            });
            _vm.StatusText = done == trackIds.Count
                ? $"{done} tracks frozen — their devices are idle until you unfreeze."
                : $"Froze {done} of {trackIds.Count} tracks.";
        }
        catch (Exception ex) { _vm.StatusText = $"Freeze failed: {ex.Message}"; }
        finally { _vm.SuspendEnginePolling = false; }

        AfterFreezeChange();
    }

    private void UnfreezeTracks(IReadOnlyList<int> trackIds)
    {
        foreach (var id in trackIds) Engine.UnfreezeTrack(id);
        if (_vm is not null) _vm.StatusText = trackIds.Count == 1 ? "Track unfrozen." : $"{trackIds.Count} tracks unfrozen.";
        AfterFreezeChange();
    }

    // The frozen state's ice tint on the header Freeze button (not used in menus: menu icons
    // stay neutral — spec 034).
    private static readonly IBrush IceBrush = NotaPalette.Frozen;

    // --- Flatten -----------------------------------------------------------

    private async Task FlattenTrackAsync(int trackId)
    {
        if (_vm is null || !IsFreezable(trackId)) return;
        if (!Engine.IsTrackFrozen(trackId)) { _vm.StatusText = "Freeze the track first, then Flatten."; return; }

        bool ok = await new ConfirmWindow("Flatten track",
            "Flatten replaces this track's instrument, MIDI and devices with a single audio clip of the frozen sound. " +
            "This can't be undone. Continue?", "Flatten", "Cancel").ShowDialog<bool>(this);
        if (!ok) return;

        try
        {
            int newId = FlattenFrozenTrack(trackId);
            Timeline.Refresh();
            if (newId > 0) Timeline.Select(newId, -1);   // selecting rebuilds the device panel
            _vm.StatusText = "Track flattened to audio.";
        }
        catch (Exception ex) { _vm.StatusText = $"Flatten failed: {ex.Message}"; }
    }

    // Bounce the frozen buffer to a WAV, drop it onto a fresh audio track that inherits the
    // old track's mixer strip + name/colour and its list position, then remove the original.
    private int FlattenFrozenTrack(int trackId)
    {
        string tmp = WriteFreezeBlobToWav(Engine.GetFreezeState(trackId));

        // Capture the old track's identity + mixer strip + list index before it's removed.
        int oldIndex = -1; float vol = 1f, pan = 0f;
        for (int i = 0; i < Engine.TrackCount; i++)
            if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) { oldIndex = i; vol = ti.Volume; pan = ti.Pan; break; }
        string oldName = Engine.GetTrackName(trackId);
        int color = Engine.GetTrackColorIndex(trackId);
        var sends = new float[4];
        for (int b = 0; b < 4; b++) sends[b] = Engine.GetTrackSend(trackId, b);

        try
        {
            int newId = Engine.AddAudioTrack();
            Engine.SetTrackName(newId, oldName is { Length: > 0 } ? oldName : $"Audio {trackId}");
            if (color >= 0) Engine.SetTrackColorIndex(newId, color);
            Engine.SetTrackVolume(newId, vol);
            Engine.SetTrackPan(newId, pan);
            for (int b = 0; b < 4; b++) Engine.SetTrackSend(newId, b, sends[b]);
            Engine.AddAudioClip(newId, tmp, 0);

            Engine.RemoveTrack(trackId);
            if (oldIndex >= 0) Engine.MoveTrack(newId, oldIndex);   // slot it where the original sat
            return newId;
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* temp WAV: best-effort cleanup */ }
        }
    }

    // Decode a freeze blob ([spb][frameCount] header + interleaved float PCM) to a temp WAV
    // at the device rate, returning the path. Caller deletes it after the engine loads it.
    private string WriteFreezeBlobToWav(byte[] blob)
    {
        const int header = sizeof(double) + sizeof(long);
        if (blob.Length < header) throw new InvalidOperationException("no frozen audio");
        long frames = BitConverter.ToInt64(blob, sizeof(double));
        if (frames <= 0) throw new InvalidOperationException("empty freeze buffer");
        long pcmBytes = frames * 2 * sizeof(float);
        if (blob.Length < header + pcmBytes) throw new InvalidOperationException("truncated freeze buffer");

        var pcm = new float[frames * 2];
        Buffer.BlockCopy(blob, header, pcm, 0, (int)pcmBytes);
        int sr = Engine.SampleRate > 0 ? (int)Math.Round(Engine.SampleRate) : 44100;
        string tmp = Path.Combine(Path.GetTempPath(), $"nota-freeze-{Guid.NewGuid():N}.wav");
        using (var w = new WavWriter(tmp, sr, 2, WavBitDepth.Float32)) w.WriteFrames(pcm, (int)frames);
        return tmp;
    }
}
