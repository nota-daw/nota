// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Live / Linked Freeze (v1.1). Unlike the in-place Freeze (MainWindow.Freeze.cs), this
// bounces a source track into a *separate linked audio track* placed right below it, then
// puts the source to sleep (implicit-mute → the engine skips its instrument/devices, so its
// CPU is freed). The link stays live: "Edit" wakes the source (unmute) so you can change
// notes/devices, "Done" re-renders the linked audio and puts the source back to sleep.
//
// v1 slice of the spec: core loop (Live Freeze · Edit session · Commit/Discard · Unfreeze ·
// Flatten) + link persistence. Deferred: plugin RAM unload (§7.3), Live/Manual auto-render
// (§5B), revision cache/GC (§5B.7-8), dependency-graph cascade / Commit All (§14), partial /
// segment scope (§4.1-3), session-as-one-undo-step (§5A.6), realtime render for external
// inputs (§8.3), preserving frozen-clip edits across re-commit (§6.2).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nota.App;

public partial class MainWindow
{
    private enum LinkState { Frozen, Editing }

    private sealed class FreezeLink
    {
        public int SourceId;
        public int FrozenId;
        public int Revision;
        public LinkState State = LinkState.Frozen;
    }

    // Keyed by source track id (ids are stable within a session; the sidecar maps by index).
    private readonly Dictionary<int, FreezeLink> _links = new();

    private FreezeLink? LinkForSource(int id) => _links.TryGetValue(id, out var l) ? l : null;
    private FreezeLink? LinkForFrozen(int id)
    { foreach (var l in _links.Values) if (l.FrozenId == id) return l; return null; }

    // 0 = unrelated, 1 = a live-freeze source (sleeping), 2 = a linked frozen track.
    private int FreezeRoleOf(int trackId)
        => _links.ContainsKey(trackId) ? 1 : LinkForFrozen(trackId) is not null ? 2 : 0;

    // --- button cluster (next to the in-place Freeze button) ---------------

    // Reflect the shown track's live-freeze state onto the header buttons.
    private void UpdateLiveFreezeButtons(int trackId)
    {
        if (LiveFreezeBtn is null) return;
        var srcLink = LinkForSource(trackId);
        var frzLink = LinkForFrozen(trackId);
        var link = srcLink ?? frzLink;

        bool plainFreezable = IsFreezable(trackId) && link is null && !Engine.IsTrackFrozen(trackId);
        // A frozen (in-place) track or a return/group can't be live-frozen either.
        LiveFreezeBtn.IsVisible = plainFreezable;

        bool editing = link is { State: LinkState.Editing };
        EditBtn.IsVisible = link is not null && !editing;
        DoneBtn.IsVisible = editing;
        DiscardBtn.IsVisible = editing;
    }

    // The source track a header action targets (following a frozen track back to its source).
    private int LinkSourceFor(int trackId)
        => LinkForSource(trackId)?.SourceId ?? LinkForFrozen(trackId)?.SourceId ?? -1;

    private async void OnLiveFreezeClick(object? sender, RoutedEventArgs e)
    { if (_freezeTrackId > 0) await LiveFreezeAsync(_freezeTrackId); }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    { int s = LinkSourceFor(_freezeTrackId); if (s > 0) BeginEditSession(s); }

    private async void OnDoneClick(object? sender, RoutedEventArgs e)
    { int s = LinkSourceFor(_freezeTrackId); if (s > 0) await CommitEditSessionAsync(s); }

    private void OnDiscardClick(object? sender, RoutedEventArgs e)
    { int s = LinkSourceFor(_freezeTrackId); if (s > 0) DiscardEditSession(s); }

    // --- commands ----------------------------------------------------------

    private async Task LiveFreezeAsync(int sourceId)
    {
        if (_vm is null || !IsFreezable(sourceId) || LinkForSource(sourceId) is not null) return;
        double endBeats = ProjectEndBeats();
        if (endBeats <= 0.0) { _vm.StatusText = "Nothing to freeze — the arrangement is empty."; return; }

        string? wav = await RenderSourceToWavAsync(sourceId, "Live Freeze");
        if (wav is null) { _vm.StatusText = "Couldn't freeze this track."; return; }

        try
        {
            // Frozen track inherits the source's name/colour + static mixer strip so the mix
            // sounds identical while the source sleeps. Placed directly under the source.
            int srcIndex = TrackIndexOf(sourceId);
            float vol = 1f, pan = 0f;
            for (int i = 0; i < Engine.TrackCount; i++)
                if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == sourceId) { vol = ti.Volume; pan = ti.Pan; break; }
            string srcName = Engine.GetTrackName(sourceId);
            int color = Engine.GetTrackColorIndex(sourceId);
            var sends = new float[4];
            for (int b = 0; b < 4; b++) sends[b] = Engine.GetTrackSend(sourceId, b);

            int frozenId = Engine.AddAudioTrack();
            Engine.SetTrackName(frozenId, (srcName is { Length: > 0 } ? srcName : $"Track {sourceId}") + " (frozen)");
            if (color >= 0) Engine.SetTrackColorIndex(frozenId, color);
            Engine.SetTrackVolume(frozenId, vol);
            Engine.SetTrackPan(frozenId, pan);
            for (int b = 0; b < 4; b++) Engine.SetTrackSend(frozenId, b, sends[b]);
            Engine.AddAudioClip(frozenId, wav, 0);
            if (srcIndex >= 0) Engine.MoveTrack(frozenId, srcIndex + 1);   // sit directly below the source

            Engine.SetTrackMute(sourceId, true);   // implicit mute: engine skips its instrument/devices
            _links[sourceId] = new FreezeLink { SourceId = sourceId, FrozenId = frozenId };
            _vm.StatusText = "Track live-frozen — press Edit to change it, then Done.";
        }
        catch (Exception ex) { _vm.StatusText = $"Live Freeze failed: {ex.Message}"; }
        finally { try { File.Delete(wav); } catch { } }

        Timeline.Refresh();
        if (Engine.TryGetTrackInfo(TrackIndexOf(sourceId), out _)) ShowDevices(sourceId);   // keep Edit reachable
    }

    private void BeginEditSession(int sourceId)
    {
        var link = LinkForSource(sourceId);
        if (link is null || _vm is null) return;
        Engine.SetTrackMute(sourceId, false);       // wake the source — it plays live now
        Engine.SetTrackMute(link.FrozenId, true);    // silence the stale bounce
        link.State = LinkState.Editing;
        _vm.StatusText = "Editing the frozen source — press Done to re-freeze, or Discard.";
        Timeline.Refresh();
        UpdateLiveFreezeButtons(_freezeTrackId);
        ApplyFrozenChainDim(_freezeTrackId);
    }

    private async Task CommitEditSessionAsync(int sourceId)
    {
        var link = LinkForSource(sourceId);
        if (link is null || _vm is null) return;

        string? wav = await RenderSourceToWavAsync(sourceId, "Re-freeze");
        if (wav is null) { _vm.StatusText = "Re-freeze failed — the source is still live."; return; }

        try
        {
            // Replace the frozen track's clips wholesale with the new bounce (v1: frozen-clip
            // edits aren't preserved across a re-commit — see the deferred list).
            if (Engine.TryGetTrackInfo(TrackIndexOf(link.FrozenId), out var fi))
                for (int c = fi.ClipCount - 1; c >= 0; c--) Engine.TryDeleteClip(link.FrozenId, c);
            Engine.AddAudioClip(link.FrozenId, wav, 0);

            Engine.SetTrackMute(sourceId, true);         // back to sleep
            Engine.SetTrackMute(link.FrozenId, false);   // the fresh bounce plays
            link.State = LinkState.Frozen;
            link.Revision++;
            _vm.StatusText = "Re-frozen.";
        }
        catch (Exception ex) { _vm.StatusText = $"Re-freeze failed: {ex.Message}"; }
        finally { try { File.Delete(wav); } catch { } }

        Timeline.Refresh();
        UpdateLiveFreezeButtons(_freezeTrackId);
        ApplyFrozenChainDim(_freezeTrackId);
    }

    private void DiscardEditSession(int sourceId)
    {
        var link = LinkForSource(sourceId);
        if (link is null || _vm is null) return;
        Engine.SetTrackMute(sourceId, true);
        Engine.SetTrackMute(link.FrozenId, false);
        link.State = LinkState.Frozen;
        _vm.StatusText = "Left edit mode (the frozen audio was not changed).";
        Timeline.Refresh();
        UpdateLiveFreezeButtons(_freezeTrackId);
        ApplyFrozenChainDim(_freezeTrackId);
    }

    // Remove the frozen track and wake the source (back to a normal live track).
    private void UnfreezeLink(int sourceId)
    {
        var link = LinkForSource(sourceId);
        if (link is null) return;
        Engine.RemoveTrack(link.FrozenId);
        Engine.SetTrackMute(sourceId, false);
        _links.Remove(sourceId);
        if (_vm is not null) _vm.StatusText = "Unfrozen — the source is live again.";
        Timeline.Refresh();
        ShowDevices(sourceId);
    }

    // Make permanent: keep the frozen audio track, drop the link, and remove the sleeping source.
    private async void FlattenLinkAsync(int sourceId)
    {
        var link = LinkForSource(sourceId);
        if (link is null || _vm is null) return;
        bool ok = await new ConfirmWindow("Flatten track",
            "Flatten keeps the frozen audio track and permanently removes its source (instrument, MIDI and devices). Continue?",
            "Flatten", "Cancel").ShowDialog<bool>(this);
        if (!ok) return;
        int frozenId = link.FrozenId;
        _links.Remove(sourceId);
        Engine.RemoveTrack(sourceId);
        _vm.StatusText = "Flattened — the frozen audio is now a plain track.";
        Timeline.Refresh();
        if (Engine.TryGetTrackInfo(TrackIndexOf(frozenId), out _)) Timeline.Select(frozenId, -1);
    }

    // Render the source's pre-fader chain to a temp WAV behind the progress dialog, reusing the
    // in-place freeze capture (we take its PCM, then clear the transient in-place frozen flag).
    private async Task<string?> RenderSourceToWavAsync(int sourceId, string title)
    {
        if (_vm is null) return null;
        double endBeats = ProjectEndBeats();
        if (endBeats <= 0.0) return null;

        bool ok = false;
        _vm.SuspendEnginePolling = true;
        try
        {
            await RunBlockingAsync(title, "Rendering…", async prog =>
            {
                var frac = new Progress<double>(f => prog.Report(ProgressReport.At(f, $"Rendering… {f * 100:0}%")));
                ok = await Task.Run(() => _freezer.Freeze(Engine, sourceId, endBeats,
                    _vm.Transport.LoopOn, _vm.Transport.MetronomeOn, frac));
            });
        }
        catch (Exception ex) { _vm.StatusText = $"{title} failed: {ex.Message}"; }
        finally { _vm.SuspendEnginePolling = false; }

        if (!ok) { Engine.UnfreezeTrack(sourceId); return null; }
        byte[] blob = Engine.GetFreezeState(sourceId);
        Engine.UnfreezeTrack(sourceId);   // we only wanted the PCM; the link owns the audio
        try { return WriteFreezeBlobToWav(blob); }
        catch (Exception ex) { _vm.StatusText = $"{title} failed: {ex.Message}"; return null; }
    }

    // --- context menu (Unfreeze / Flatten), shared by source + frozen tracks ---

    private bool _liveFreezeMenuBuilt;
    private void EnsureLiveFreezeMenu()
    {
        if (_liveFreezeMenuBuilt) return;
        _liveFreezeMenuBuilt = true;
        var unfreeze = new MenuItem { Header = "Unfreeze (wake source)" };
        unfreeze.Click += (_, _) => { int s = LinkSourceFor(_freezeTrackId); if (s > 0) UnfreezeLink(s); };
        var flatten = new MenuItem { Header = "Flatten (remove source)" };
        flatten.Click += (_, _) => { int s = LinkSourceFor(_freezeTrackId); if (s > 0) FlattenLinkAsync(s); };
        var menu = new ContextMenu();
        menu.Items.Add(unfreeze);
        menu.Items.Add(flatten);
        EditBtn.ContextMenu = menu;
        DoneBtn.ContextMenu = menu;
    }

    // --- helpers + persistence (bundle sidecar, mirrors MidiLearnService) ---

    private int TrackIndexOf(int id)
    {
        for (int i = 0; i < Engine.TrackCount; i++)
            if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == id) return i;
        return -1;
    }
    private int TrackIdAt(int index)
        => index >= 0 && Engine.TryGetTrackInfo(index, out var ti) ? ti.Id : -1;

    private const string FreezeLinkSidecar = "freeze-links.json";

    private sealed class LinkDto { public int Source { get; set; } = -1; public int Frozen { get; set; } = -1; public int Revision { get; set; } }

    private void SaveFreezeLinks(string bundleDir)
    {
        string path = Path.Combine(bundleDir, FreezeLinkSidecar);
        if (_links.Count == 0) { try { if (File.Exists(path)) File.Delete(path); } catch { } return; }
        var list = new List<LinkDto>(_links.Count);
        foreach (var l in _links.Values)
            list.Add(new LinkDto { Source = TrackIndexOf(l.SourceId), Frozen = TrackIndexOf(l.FrozenId), Revision = l.Revision });
        File.WriteAllText(path, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LoadFreezeLinks(string bundleDir)
    {
        _links.Clear();
        string path = Path.Combine(bundleDir, FreezeLinkSidecar);
        if (!File.Exists(path)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<LinkDto>>(File.ReadAllText(path)) ?? new();
            foreach (var d in list)
            {
                int src = TrackIdAt(d.Source), frz = TrackIdAt(d.Frozen);
                if (src <= 0 || frz <= 0) continue;   // a linked track was deleted meanwhile
                _links[src] = new FreezeLink { SourceId = src, FrozenId = frz, Revision = d.Revision, State = LinkState.Frozen };
                Engine.SetTrackMute(src, true);       // re-sleep the source (state re-opens as Frozen)
            }
        }
        catch { /* corrupt sidecar: start with no links */ }
    }
}
