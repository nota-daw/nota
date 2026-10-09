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
    // --- browser drag & drop (M7-5) ----------------------------------------

    // Every route a browser item takes onto a track goes through one service (CP-25), so a
    // drop and the command palette put a device in the same place. The service does the
    // engine work as one undo step; ApplyInsertResult does what the views need afterwards.

    private void OnArrangementDrop(BrowserItem item, int trackId, double beat)
    {
        if (_vm is null) return;
        try
        {
            switch (item.Kind)
            {
                case BrowserItemKind.Sample:
                    // Decodes in the background; the track shows a filling placeholder meanwhile.
                    _ = ImportAudioInBackgroundAsync(item.Path, Insertion.TrackIsAudio(trackId) ? trackId : -1, beat);
                    break;
                case BrowserItemKind.MidiFile:
                    ImportMidiFileToArrangement(item, trackId, beat);
                    break;
                default:   // instruments replace / start a track; effects + presets target the track
                    ApplyInsertResult(Insertion.DropOnArrangement(item, trackId, beat), revealDevices: item.Kind is not (BrowserItemKind.BuiltinInstrument or BrowserItemKind.PluginInstrument));
                    break;
            }
            Timeline.Refresh();
            _session?.Refresh();
        }
        catch (Exception ex) { _vm.StatusText = $"Drop failed: {ex.Message}"; }
    }

    private void OnSessionDrop(BrowserItem item, int trackId, int scene, bool instrumentTrack)
    {
        if (_vm is null) return;
        try
        {
            switch (item.Kind)
            {
                case BrowserItemKind.Sample:
                    if (instrumentTrack) { _vm.StatusText = "Drop samples onto audio slots."; break; }
                    if (Engine.AddSessionAudioFile(trackId, scene, item.Path))
                        _vm.StatusText = $"Added {item.Name} to slot";
                    break;
                case BrowserItemKind.MidiFile:
                    ImportMidiFileToSlot(item, trackId, scene, instrumentTrack);
                    break;
                case BrowserItemKind.BuiltinEffect:
                case BrowserItemKind.PluginEffect:
                case BrowserItemKind.BuiltinMidiEffect:
                case BrowserItemKind.Preset:
                    ApplyInsertResult(Insertion.RouteToTrack(item, trackId)); // a session column is a track
                    break;
                default: // instruments
                    _vm.StatusText = "Drop instruments onto the arrangement.";
                    break;
            }
            _session?.Refresh();
            Timeline.Refresh();
        }
        catch (Exception ex) { _vm.StatusText = $"Drop failed: {ex.Message}"; }
    }

    // A browser item dropped on the Devices panel of the shown track. An effect lands in
    // the gap it was dropped on, or replaces the card it was dropped on; an instrument
    // replaces the track's instrument (a rack gets a new chain / pad instead). The rest —
    // presets, samples — routes as usual.
    private void OnDevicePanelDrop(BrowserItem item, DeviceDropTarget target)
    {
        int t = _deviceChain?.TrackId ?? -1;
        if (_vm is null) return;
        var placement = target.Mode switch
        {
            DeviceDropMode.Insert => InsertPlacement.Insert,
            DeviceDropMode.Replace => InsertPlacement.Replace,
            _ => InsertPlacement.Default,
        };
        ApplyInsertResult(Insertion.Insert(item, new InsertTarget(t, placement, target.Index, target.Midi)));
    }

    private void OnModularDrop(BrowserItem item) => DropBrowserItem(item, _modular?.TrackId ?? -1);

    // Adds an instrument/effect/MIDI-FX/sample/preset from the browser to a track;
    // shared by the Devices-panel and Modular-view drop targets.
    private void DropBrowserItem(BrowserItem item, int t)
    {
        if (_vm is null) return;
        if (t <= 0) { _vm.StatusText = "Select a track first."; return; }
        ApplyInsertResult(Insertion.Insert(item, new InsertTarget(t)));
    }

    private DeviceInsertService? _insertion;
    private DeviceInsertService Insertion => _insertion ??= new DeviceInsertService(Engine, _factory, _presets, _kits);

    /// <summary>What the views do after the insert service changed a track: card state, the
    /// last-instrument fallback, refreshes, revealing the chain, the status line.</summary>
    private void ApplyInsertResult(InsertResult r, bool revealDevices = true)
    {
        if (_vm is null) return;
        _vm.StatusText = r.Status;
        if (!r.Ok) return;
        int t = r.TrackId;
        if (t > 0 && (r.InstrumentReplaced || r.CreatedTrack) && Insertion.TryTrackInfo(t, out var ti) && ti.IsInstrument)
            _lastInstrumentTrackId = t;
        if (r.InstrumentReplaced) RefreshDeviceChainIfShowing(t);
        if (r.Placed is { } p) _deviceChain?.AdoptPlacement(t, p.Midi, p.Added, p.Final, p.Replaced);
        if (r.PresetLanded is { } pl)
            _deviceChain?.RememberPreset(pl.TrackId, pl.Chain switch
            {
                PresetChain.Instrument => DeviceChainView.ChainKind.Instrument,
                PresetChain.Midi => DeviceChainView.ChainKind.Midi,
                _ => DeviceChainView.ChainKind.Effect,
            }, pl.Index, pl.Name, pl.FactoryId);
        Timeline.Refresh();
        _session?.Refresh();
        if (_modular?.IsVisible == true) _modular.Refresh();
        if (revealDevices && r.ShowDevices && t > 0) ShowDevices(t);
        else _deviceChain?.Refresh();
    }

    private bool TrackIsAudio(int trackId) => Insertion.TrackIsAudio(trackId);

    // Refresh the device panel after an in-place instrument swap, but only when it's already
    // open on that track (never force it open). The old instrument's preset label is dropped
    // either way so it doesn't reappear on the new one.
    private void RefreshDeviceChainIfShowing(int trackId)
    {
        _deviceChain?.ForgetInstrumentState(trackId);
        if (_deviceChain?.IsVisible == true && _deviceChain.TrackId == trackId) _deviceChain.Show(trackId);
    }
}
