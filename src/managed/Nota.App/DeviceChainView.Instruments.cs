// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the instrument-card dispatcher (routes the track's instrument to a
// RackCardView or an InstrumentCardFactory strategy) and the shared live-follow tick
// (RefreshSynthLive) that drives every card's registered graphs + faders.

using System;
using Avalonia.Controls;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

public sealed partial class DeviceChainView
{
    private Control InstrumentCard()
    {
        int kind = _engine.TrackInstrumentKind(_trackId);
        if (kind == 3) return new RackCardView(NewCardContext()).BuildInstrumentRackCard();   // Instrument Rack
        if (kind == 4) return new RackCardView(NewCardContext()).BuildDrumRackCard();          // Drum Rack
        // Built-in Sampler + synths (Synth/Physical/Aurora/Volt) → their strategy; anything
        // else (or a synth reporting no params) → the generic Open-GUI card.
        bool hasParams = _engine.PluginParamCount(_trackId, -1) > 0;
        var strategy = _instrumentFactory.Resolve(kind, hasParams);
        var body = strategy.Build(NewCardContext());
        if (!strategy.BodyOnly) return body;   // card owns its whole frame (not yet migrated)
        var spec = new ShellSpec(
            Name: _engine.DeviceName(_trackId, -1), Subtitle: strategy.Subtitle, DeviceIndex: -1, Count: 1,
            Bypassed: false, Bypassable: false, CanMove: false, CanDelete: false,
            PresetKind: kind, IsInstrument: true, Width: strategy.CardWidth, Kind: ChainKind.Instrument,
            VoiceLabel: strategy.VoiceLabel);
        return BuildCardShell(spec, body);
    }

    /// <summary>Re-reads the built-in instrument params and updates faders + graphs
    /// (so automation moves show live). Drives both the Synth and Physical editors.</summary>
    public void RefreshSynthLive()
    {
        // Device-param knobs + compressor GR/curve, then rack chain/macro knobs — all
        // read the engine each tick so automation (and macro) moves show live.
        for (int i = 0; i < _deviceLiveRefreshers.Count; i++) _deviceLiveRefreshers[i]();
        for (int i = 0; i < _rackParamRefreshers.Count; i++) _rackParamRefreshers[i]();
        _instLiveViz?.Invoke();   // synth/physical/aurora graphs (null for device-only cards)
        foreach (var (i, fader, val, fmt) in _instFaders)   // instrument / rack-macro knobs follow automation
        {
            if (fader.Dragging) continue;
            float v = _engine.PluginParamGet(_trackId, -1, i);
            if (Math.Abs(v - fader.Value) > 1e-3) { fader.Value = v; val.Text = (fmt ?? Pct)(v); }
        }
    }

}
