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

    private void OnArrangementDrop(BrowserItem item, int trackId, double beat)
    {
        if (_vm is null) return;
        try
        {
            switch (item.Kind)
            {
                case BrowserItemKind.Sample:
                {
                    int t = TrackIsAudio(trackId) ? trackId : Engine.AddAudioTrack();
                    int c = Engine.AddAudioClip(t, item.Path, beat);
                    AutoWarpImported(t, c);
                    _vm.StatusText = $"Added {item.Name} (track {t})";
                    break;
                }
                case BrowserItemKind.BuiltinInstrument:
                {
                    // Dropped on an existing (non-rack) instrument track → swap its instrument;
                    // otherwise add a new track. Racks/Sampler fall through to their own adds.
                    if (CanReplaceInstrument(trackId) && Engine.SetTrackBuiltinInstrument(trackId, item.BuiltinKind))
                    {
                        if (item.BuiltinKind == RhythmModel.Kind) _kits.LoadInto(Engine, trackId, _kits.DefaultRhythmKit, out _);
                        _lastInstrumentTrackId = trackId;
                        RefreshDeviceChainIfShowing(trackId);
                        _vm.StatusText = $"Changed instrument to {item.Name} (track {trackId})";
                        break;
                    }
                    int t = item.BuiltinKind switch
                    {
                        4 => Engine.AddDrumRackTrack(),
                        3 => Engine.AddInstrumentRackTrack(),
                        2 => Engine.AddPhysicalSynthTrack(),
                        5 => Engine.AddWavetableSynthTrack(),
                        6 => Engine.AddVoltSynthTrack(),
                        7 => Engine.AddBassSynthTrack(),
                        8 => Engine.AddPendulumSynthTrack(),
                        9 => Engine.AddOperatorSynthTrack(),
                        10 => Engine.AddGrainSynthTrack(),
                        11 => Engine.AddFluxSynthTrack(),
                        12 => Engine.AddRhythmTrack(),
                        13 => Engine.AddMonolithTrack(),
                        14 => Engine.AddPentadTrack(),
                        15 => Engine.AddConsortTrack(),
                        1 => Engine.AddSamplerInstrumentTrack(),
                        _ => Engine.AddInstrumentTrack(),
                    };
                    if (item.BuiltinKind == RhythmModel.Kind) _kits.LoadInto(Engine, t, _kits.DefaultRhythmKit, out _);   // a Rhythm starts on a factory kit
                    Engine.AddMidiClip(t, beat, 4.0);
                    _lastInstrumentTrackId = t;
                    _vm.StatusText = $"Added {item.Name} (track {t})";
                    break;
                }
                case BrowserItemKind.PluginInstrument:
                {
                    // Dropped on an existing (non-rack) instrument track → swap its instrument.
                    if (CanReplaceInstrument(trackId))
                    {
                        Engine.SetTrackInstrumentPlugin(trackId, item.CatalogIndex);
                        _lastInstrumentTrackId = trackId;
                        RefreshDeviceChainIfShowing(trackId);
                        _vm.StatusText = $"Changed instrument to {item.Name} (track {trackId})";
                        break;
                    }
                    int t = Engine.AddPluginInstrumentTrack(item.CatalogIndex);
                    Engine.AddMidiClip(t, beat, 4.0);
                    _lastInstrumentTrackId = t;
                    _vm.StatusText = $"Added {item.Name} (track {t})";
                    break;
                }
                default: // effects + presets target the dropped-on track
                    RouteToTrack(item, trackId);
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
                case BrowserItemKind.BuiltinEffect:
                case BrowserItemKind.PluginEffect:
                case BrowserItemKind.Preset:
                    RouteToTrack(item, trackId); // a session column is a track
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

    // A browser item dropped on the Devices panel of the shown track. Effects/presets
    // go on the track; instruments dropped on an Instrument/Drum Rack become a new
    // chain / pad; otherwise they belong on the arrangement.
    private void OnDevicePanelDrop(BrowserItem item) => DropBrowserItem(item, _deviceChain?.TrackId ?? -1);
    private void OnModularDrop(BrowserItem item) => DropBrowserItem(item, _modular?.TrackId ?? -1);

    // Adds an instrument/effect/MIDI-FX/sample/preset from the browser to a track;
    // shared by the Devices-panel and Modular-view drop targets.
    private void DropBrowserItem(BrowserItem item, int t)
    {
        if (_vm is null) return;
        if (t <= 0) { _vm.StatusText = "Select a track first."; return; }
        try
        {
            int kind = Engine.TrackInstrumentKind(t);
            switch (item.Kind)
            {
                case BrowserItemKind.BuiltinInstrument when kind == 3:      // Instrument Rack → new chain
                    Engine.RackAddChain(t, item.BuiltinKind);
                    _vm.StatusText = $"Added {item.Name} chain";
                    break;
                case BrowserItemKind.BuiltinInstrument when kind == 4:      // Drum Rack → next free pad
                {
                    int note = NextFreeDrumPad(t);
                    if (note < 0) { _vm.StatusText = "All 16 pads are used."; break; }
                    int c = Engine.RackAddChain(t, item.BuiltinKind);
                    if (c >= 0) Engine.RackSetChainTriggerNote(t, c, note);
                    _vm.StatusText = $"Added {item.Name} pad";
                    break;
                }
                case BrowserItemKind.PluginInstrument when kind == 3:      // Instrument Rack → plugin chain
                    if (Engine.RackAddPluginInstrumentChain(t, item.CatalogIndex) < 0) _vm.StatusText = $"Couldn't load {item.Name}.";
                    else _vm.StatusText = $"Added {item.Name} chain";
                    break;
                case BrowserItemKind.PluginInstrument when kind == 4:      // Drum Rack → plugin pad
                {
                    int note = NextFreeDrumPad(t);
                    if (note < 0) { _vm.StatusText = "All 16 pads are used."; break; }
                    int c = Engine.RackAddPluginInstrumentChain(t, item.CatalogIndex);
                    if (c < 0) { _vm.StatusText = $"Couldn't load {item.Name}."; break; }
                    Engine.RackSetChainTriggerNote(t, c, note);
                    _vm.StatusText = $"Added {item.Name} pad";
                    break;
                }
                case BrowserItemKind.BuiltinInstrument:
                case BrowserItemKind.PluginInstrument:
                    _vm.StatusText = "Drop instruments onto the arrangement.";
                    break;
                case BrowserItemKind.Sample when kind == 4:                 // sample → drum pad (Sampler)
                {
                    int note = NextFreeDrumPad(t);
                    if (note < 0) { _vm.StatusText = "All 16 pads are used."; break; }
                    int c = Engine.RackAddSamplerChain(t, item.Path, note, false);   // root = pad note → natural pitch
                    if (c < 0) { _vm.StatusText = $"Couldn't load {item.Name}."; break; }
                    Engine.RackSetChainTriggerNote(t, c, note);
                    _vm.StatusText = $"Added {item.Name} pad";
                    break;
                }
                case BrowserItemKind.Sample when kind == 3:                 // sample → instrument-rack Sampler chain
                    if (Engine.RackAddSamplerChain(t, item.Path, 60, false) < 0) _vm.StatusText = $"Couldn't load {item.Name}.";
                    else _vm.StatusText = $"Added {item.Name} chain";
                    break;
                case BrowserItemKind.Sample when kind == 1:                 // sample → load into the Sampler
                    if (!Engine.SetTrackSamplerSample(t, item.Path, 60)) _vm.StatusText = $"Couldn't load {item.Name}.";
                    else { ShowDevices(t); _vm.StatusText = $"Loaded {item.Name}"; }
                    break;
                case BrowserItemKind.Sample when kind == 10:                // sample → load into Nota Grain
                    if (!Engine.SetTrackGrainSample(t, item.Path, 60)) _vm.StatusText = $"Couldn't load {item.Name}.";
                    else { ShowDevices(t); _vm.StatusText = $"Loaded {item.Name}"; }
                    break;
                case BrowserItemKind.Sample:
                    _vm.StatusText = "Drop samples onto the arrangement.";
                    break;
                default:                                                    // effects + presets
                    RouteToTrack(item, t);
                    break;
            }
            _deviceChain?.Refresh();
            if (_modular?.IsVisible == true) _modular.Refresh();
            Timeline.Refresh();
        }
        catch (Exception ex) { _vm.StatusText = $"Drop failed: {ex.Message}"; }
    }

    // First of the 16 drum pad notes (36..51) with no chain assigned, or -1.
    private int NextFreeDrumPad(int trackId)
    {
        int n = Engine.RackChainCount(trackId);
        var used = new HashSet<int>();
        for (int c = 0; c < n; c++) used.Add(Engine.RackChainTriggerNote(trackId, c));
        for (int note = 36; note < 52; note++) if (!used.Contains(note)) return note;
        return -1;
    }

    // Adds an effect / applies a preset to a specific track (shared by both drop paths).
    private void RouteToTrack(BrowserItem item, int trackId)
    {
        if (_vm is null) return;
        // Presets first: an instrument preset spawns its own track, so it doesn't need a
        // drop target; an effect preset reports "Select a track first." via Apply itself.
        if (item.Kind == BrowserItemKind.Preset)
        {
            bool kit = IsKitRow(item);
            string warn = ApplyPresetItem(item, trackId);
            if (!kit && trackId > 0) ShowDevices(trackId);
            _vm.StatusText = warn.Length > 0 ? warn
                : kit ? $"Loaded {item.Name} kit" : $"Applied preset {item.Name}";
            return;
        }
        if (trackId <= 0) { _vm.StatusText = "Drop onto a track."; return; }
        switch (item.Kind)
        {
            case BrowserItemKind.BuiltinEffect:
                Engine.AddBuiltinDevice(trackId, item.BuiltinKind);
                ShowDevices(trackId);
                _vm.StatusText = $"Added {item.Name} to track {trackId}";
                break;
            case BrowserItemKind.BuiltinMidiEffect:
                Engine.AddMidiEffect(trackId, item.BuiltinKind);
                ShowDevices(trackId);
                _vm.StatusText = $"Added {item.Name} to track {trackId}";
                break;
            case BrowserItemKind.PluginEffect:
                if (Engine.AddTrackEffectPlugin(trackId, item.CatalogIndex) < 0) { _vm.StatusText = "Failed to load effect."; break; }
                ShowDevices(trackId);
                _vm.StatusText = $"Added {item.Name} to track {trackId}";
                break;
        }
    }

    private bool TrackIsAudio(int trackId)
    {
        if (trackId <= 0) return false;
        int n = Engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId)
                return !ti.IsInstrument && !ti.IsReturn;
        return false;
    }

    // Refresh the device panel after an in-place instrument swap, but only when it's already
    // open on that track (never force it open). The old instrument's preset label is dropped
    // either way so it doesn't reappear on the new one.
    private void RefreshDeviceChainIfShowing(int trackId)
    {
        _deviceChain?.ForgetInstrumentState(trackId);
        if (_deviceChain?.IsVisible == true && _deviceChain.TrackId == trackId) _deviceChain.Show(trackId);
    }

    // True when an instrument dropped on this track should replace its instrument in place rather
    // than spawn a new track: a plain instrument track (not audio/return/group) that isn't a rack.
    private bool CanReplaceInstrument(int trackId)
    {
        if (trackId <= 0) return false;
        int n = Engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (Engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId)
            {
                if (!ti.IsInstrument || ti.IsReturn || ti.IsGroup) return false;
                int kind = Engine.TrackInstrumentKind(trackId);
                return kind != 3 && kind != 4;   // never swap an Instrument/Drum Rack in place
            }
        return false;
    }
}
