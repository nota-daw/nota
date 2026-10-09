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
        if (kind == 4) return DrumRackCard();
        if (kind == -1) return PluginCardFor(ChainKind.Instrument, -1, 1);   // hosted VST3 / AU
        // Built-in Sampler + synths (Synth/Physical/Aurora/Volt) → their strategy; anything
        // else (or a synth reporting no params) → the generic Open-GUI card.
        bool hasParams = _engine.PluginParamCount(_trackId, -1) > 0;
        var strategy = _instrumentFactory.Resolve(kind, hasParams);
        var ctx = NewCardContext();
        var body = strategy.Build(ctx);
        if (!strategy.BodyOnly) return body;   // card owns its whole frame (not yet migrated)
        double width = strategy.WidthFor(_engine, _trackId);
        var spec = new ShellSpec(
            Name: _engine.DeviceName(_trackId, -1), Subtitle: strategy.Subtitle, DeviceIndex: -1, Count: 1,
            Bypassed: false, Bypassable: false, CanMove: false, CanDelete: false,
            PresetKind: kind, IsInstrument: true, Width: width, Kind: ChainKind.Instrument,
            VoiceLabel: strategy.VoiceLabel,
            Presets: kind == Nota.Application.RhythmModel.Kind ? KitPresets() : kind == Nota.Application.MosaicModel.Kind ? MosaicPresets() : null,
            HeaderExtra: strategy.HeaderAccessory(ctx), Compact: width < 300);
        return BuildCardShell(spec, body);
    }

    // The factory drum kits as a card's presets — shared by the Drum Rack (they replace its
    // pads) and Nota Rhythm (they replace its voices' samples and FX, keeping the steps).
    private CardPresets? KitPresets()
    {
        if (_kits is not { } k) return null;
        int t = _trackId;
        var items = new System.Collections.Generic.List<(string, string)>();
        foreach (var kit in k.All()) items.Add((kit.Id, kit.Name));
        return new CardPresets(items, () => k.Identify(_engine, t), id =>
        {
            k.LoadInto(_engine, t, id, out string warn);
            _rackSelChain = 0;
            return warn;
        });
    }

    // Nota Mosaic's presets: the factory multisamples, then every pack preset ("Pack / Name").
    // The one the instrument holds is named by its program (a pack preset keeps its name).
    private CardPresets MosaicPresets()
    {
        int t = _trackId;
        var items = new System.Collections.Generic.List<(string, string)>();
        foreach (var p in _factory.All())
            if (p.IsInstrument && p.BuiltinKind == Nota.Application.MosaicModel.Kind) items.Add(("factory:" + p.Id, p.DisplayName));
        if (_mosaic is not null)
            foreach (var p in _mosaic.Presets()) items.Add((p.Path, $"{p.Folder} / {p.Name}"));
        // A program that is no preset (an SFZ, dropped files) still names itself in the picker.
        string held = Nota.Application.Mosaic.MosaicProgram.Parse(_engine.MosaicProgram(t)).Name;
        if (held.Length > 0 && !items.Exists(i => i.Item2 == held || i.Item2.EndsWith(" / " + held, StringComparison.Ordinal)))
            items.Insert(0, ("program:", held));
        string Current()
        {
            string name = Nota.Application.Mosaic.MosaicProgram.Parse(_engine.MosaicProgram(t)).Name;
            if (name.Length == 0) return "";
            foreach (var (id, n) in items) if (n == name || n.EndsWith(" / " + name, StringComparison.Ordinal)) return id;
            return "";
        }
        return new CardPresets(items, Current, id =>
        {
            if (id == "program:") return "";
            if (id.StartsWith("factory:", StringComparison.Ordinal)) return _factory.ApplyInPlace(_engine, id["factory:".Length..], t, -1);
            return _mosaic?.ApplyInPlace(_engine, id, t) ?? "Presets are unavailable.";
        });
    }

    // The Drum Rack in the shared shell: its presets are the factory kits, which replace the
    // rack's pads, and the badge counts the loaded pads.
    private Control DrumRackCard()
    {
        var body = new RackCardView(NewCardContext()).BuildDrumRackBody(out int pads);
        var kits = KitPresets();
        var spec = new ShellSpec(
            Name: _engine.DeviceName(_trackId, -1), Subtitle: $"DRUMS · {pads} PAD{(pads == 1 ? "" : "S")}", DeviceIndex: -1, Count: 1,
            Bypassed: false, Bypassable: false, CanMove: false, CanDelete: false,
            PresetKind: 4, IsInstrument: true, Width: 700, Kind: ChainKind.Instrument, Presets: kits);
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
