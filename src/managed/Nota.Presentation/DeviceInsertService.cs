// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-25): the one place a browser item — an instrument, an effect, a MIDI
// effect, a plug-in, a preset or a kit — is put on a track. Drag & drop (arrangement, session,
// Devices panel, Modular) and the command palette both call it, so a device lands the same
// way whichever route it took. Each call is one undo step. It touches only the engine; what
// the views must do afterwards (refresh, reveal the chain, remember a preset label, keep card
// state glued to a moved device) comes back in the InsertResult.

using Nota.Application;

namespace Nota.Presentation;

/// <summary>Where in a chain an effect goes.</summary>
public enum InsertPlacement
{
    /// <summary>The usual routing: effects append to their section; instruments replace the
    /// track's instrument (a rack gets a new chain / pad).</summary>
    Default,
    /// <summary>The new effect goes in at <see cref="InsertTarget.Index"/> (a gap, 0..count).</summary>
    Insert,
    /// <summary>The effect at <see cref="InsertTarget.Index"/> is swapped for the new one.</summary>
    Replace,
}

/// <summary>Where an item goes. <paramref name="Midi"/>: the index refers to the MIDI-effect chain.
/// <paramref name="InPlace"/>: a factory instrument preset loads into the track's instrument
/// (swapping it if needed) instead of starting a track — the palette's rule (CP-3).</summary>
public readonly record struct InsertTarget(int TrackId, InsertPlacement Placement = InsertPlacement.Default, int Index = -1,
    bool Midi = false, bool NewTrack = false, double Beat = 0, bool InPlace = false);

/// <summary>Which chain a just-applied preset sits in, for the card's preset label.</summary>
public enum PresetChain { Instrument, Effect, Midi }

public sealed record InsertResult
{
    public bool Ok { get; init; }
    /// <summary>The status-line message.</summary>
    public string Status { get; init; } = "";
    /// <summary>The track that changed (or was created), or -1.</summary>
    public int TrackId { get; init; } = -1;
    public bool CreatedTrack { get; init; }
    /// <summary>The track's instrument was swapped in place — its card state is stale.</summary>
    public bool InstrumentReplaced { get; init; }
    /// <summary>The views should reveal the track's device chain.</summary>
    public bool ShowDevices { get; init; }
    /// <summary>An effect was appended at <c>Added</c> and then moved to <c>Final</c>
    /// (<c>Replaced</c>: the device that was at Final+1 removed). For the Devices card state.</summary>
    public (bool Midi, int Added, int Final, bool Replaced)? Placed { get; init; }
    /// <summary>A preset that landed: chain, device index, its name and factory id ("" for a user preset).</summary>
    public (int TrackId, PresetChain Chain, int Index, string Name, string FactoryId)? PresetLanded { get; init; }

    public static InsertResult Fail(string status) => new() { Ok = false, Status = status };
}

public sealed class DeviceInsertService
{
    private readonly IAudioEngine _engine;
    private readonly IFactoryPresets _factory;
    private readonly IPresetStore? _presets;
    private readonly IDrumKits? _kits;

    public DeviceInsertService(IAudioEngine engine, IFactoryPresets factory, IPresetStore? presets, IDrumKits? kits)
    {
        _engine = engine;
        _factory = factory;
        _presets = presets;
        _kits = kits;
    }

    private const string FactoryPrefix = "factory:", KitPrefix = "kit:", RhythmKitPrefix = "rhythmkit:";

    // ---- what a track is --------------------------------------------------------------------

    public bool TryTrackInfo(int trackId, out NotaTrackInfo info)
    {
        info = default;
        if (trackId <= 0) return false;
        for (int i = 0; i < _engine.TrackCount; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId) { info = ti; return true; }
        return false;
    }

    public bool IsMaster(int trackId) => trackId > 0 && trackId == _engine.MasterTrackId;

    public bool TrackIsAudio(int trackId) => TryTrackInfo(trackId, out var ti) && !ti.IsInstrument && !ti.IsReturn && !ti.IsGroup;

    /// <summary>A plain instrument track (not a rack, group, return or audio track) — an
    /// instrument dropped on it replaces its instrument in place.</summary>
    public bool CanReplaceInstrument(int trackId)
    {
        if (!TryTrackInfo(trackId, out var ti) || !ti.IsInstrument || ti.IsReturn || ti.IsGroup) return false;
        int kind = _engine.TrackInstrumentKind(trackId);
        return kind != 3 && kind != 4;   // never swap an Instrument/Drum Rack in place
    }

    public static bool IsKit(BrowserItem item)
        => item.Kind == BrowserItemKind.Preset
           && (item.Path.StartsWith(KitPrefix, StringComparison.Ordinal) || item.Path.StartsWith(RhythmKitPrefix, StringComparison.Ordinal));

    /// <summary>The factory preset behind a row, or null for a user preset / kit / non-preset.</summary>
    public FactoryPresetInfo? FactoryInfo(BrowserItem item)
    {
        if (item.Kind != BrowserItemKind.Preset || !item.Path.StartsWith(FactoryPrefix, StringComparison.Ordinal)) return null;
        string id = item.Path[FactoryPrefix.Length..];
        foreach (var fp in _factory.All()) if (fp.Id == id) return fp;
        return null;
    }

    // ---- instruments ------------------------------------------------------------------------

    /// <summary>A new track playing the instrument, with a 4-beat MIDI clip at <paramref name="beat"/>.</summary>
    public int CreateInstrumentTrack(BrowserItem item, double beat)
    {
        int t;
        if (item.Kind == BrowserItemKind.PluginInstrument) t = _engine.AddPluginInstrumentTrack(item.CatalogIndex);
        else
        {
            t = item.BuiltinKind switch
            {
                4 => _engine.AddDrumRackTrack(),
                3 => _engine.AddInstrumentRackTrack(),
                2 => _engine.AddPhysicalSynthTrack(),
                5 => _engine.AddWavetableSynthTrack(),
                6 => _engine.AddVoltSynthTrack(),
                7 => _engine.AddBassSynthTrack(),
                8 => _engine.AddPendulumSynthTrack(),
                9 => _engine.AddOperatorSynthTrack(),
                10 => _engine.AddGrainSynthTrack(),
                11 => _engine.AddFluxSynthTrack(),
                12 => _engine.AddRhythmTrack(),
                13 => _engine.AddMonolithTrack(),
                14 => _engine.AddPentadTrack(),
                15 => _engine.AddConsortTrack(),
                1 => _engine.AddSamplerInstrumentTrack(),
                _ => _engine.AddInstrumentTrack(),
            };
            if (item.BuiltinKind == RhythmModel.Kind && _kits is not null) _kits.LoadInto(_engine, t, _kits.DefaultRhythmKit, out _);   // a Rhythm starts on a factory kit
        }
        if (t > 0) _engine.AddMidiClip(t, beat, 4.0);
        return t;
    }

    /// <summary>Swaps a plain instrument track's instrument. False — nothing changed — when it can't.</summary>
    public bool TryReplaceInstrument(BrowserItem item, int trackId)
    {
        if (!CanReplaceInstrument(trackId)) return false;
        if (item.Kind == BrowserItemKind.BuiltinInstrument)
        {
            if (!_engine.SetTrackBuiltinInstrument(trackId, item.BuiltinKind)) return false;
            if (item.BuiltinKind == RhythmModel.Kind && _kits is not null) _kits.LoadInto(_engine, trackId, _kits.DefaultRhythmKit, out _);
        }
        else if (item.Kind == BrowserItemKind.PluginInstrument) _engine.SetTrackInstrumentPlugin(trackId, item.CatalogIndex);
        else return false;
        return true;
    }

    // First of the 16 drum pad notes (36..51) with no chain assigned, or -1.
    public int NextFreeDrumPad(int trackId)
    {
        int n = _engine.RackChainCount(trackId);
        var used = new HashSet<int>();
        for (int c = 0; c < n; c++) used.Add(_engine.RackChainTriggerNote(trackId, c));
        for (int note = 36; note < 52; note++) if (!used.Contains(note)) return note;
        return -1;
    }

    // ---- the entry points ---------------------------------------------------------------------

    /// <summary>Drag & drop onto an arrangement lane: an instrument replaces a plain track's
    /// instrument or starts its own track at the drop beat; effects and presets go to the
    /// track. Samples and MIDI files are imported by the view (they decode in the background).</summary>
    public InsertResult DropOnArrangement(BrowserItem item, int trackId, double beat)
    {
        if (item.Kind is BrowserItemKind.BuiltinInstrument or BrowserItemKind.PluginInstrument)
            return Group(() =>
            {
                if (TryReplaceInstrument(item, trackId))
                    return new InsertResult { Ok = true, TrackId = trackId, InstrumentReplaced = true, Status = $"Changed instrument to {item.Name} (track {trackId})" };
                int t = CreateInstrumentTrack(item, beat);
                return t > 0 ? new InsertResult { Ok = true, TrackId = t, CreatedTrack = true, Status = $"Added {item.Name} (track {t})" }
                             : InsertResult.Fail($"Couldn't load {item.Name}.");
            });
        return Group(() => RouteToTrack(item, trackId));
    }

    /// <summary>Drag & drop onto the Devices panel, the Modular canvas, or the palette's
    /// default: the item goes onto <paramref name="target"/>'s track — an effect into the
    /// gap or in place of the card the target names, an instrument in place of the track's
    /// (a rack gets a chain / pad), presets and samples as usual. One undo step.</summary>
    public InsertResult Insert(BrowserItem item, InsertTarget target)
        => Group(() => InsertCore(item, target));

    private InsertResult InsertCore(BrowserItem item, InsertTarget target)
    {
        int t = target.TrackId;
        bool isInstrument = item.Kind is BrowserItemKind.BuiltinInstrument or BrowserItemKind.PluginInstrument;

        // An instrument on a new track: asked for (⌘Enter), or there's no track that can host it.
        if (isInstrument && (target.NewTrack || t <= 0 || !TryTrackInfo(t, out var ti0) || !ti0.IsInstrument || ti0.IsGroup || ti0.IsReturn))
        {
            int nt = CreateInstrumentTrack(item, target.Beat);
            return nt > 0 ? new InsertResult { Ok = true, TrackId = nt, CreatedTrack = true, ShowDevices = item.BuiltinKind is 1 or 3 or 4, Status = $"Added {item.Name} (track {nt})" }
                          : InsertResult.Fail($"Couldn't load {item.Name}.");
        }
        if (item.Kind == BrowserItemKind.Preset) return ApplyPreset(item, target);
        if (t <= 0) return InsertResult.Fail("Select a track first.");

        if (target.Placement != InsertPlacement.Default)
            switch (item.Kind)
            {
                case BrowserItemKind.BuiltinInstrument:
                case BrowserItemKind.PluginInstrument:
                    if (TryReplaceInstrument(item, t))
                        return new InsertResult { Ok = true, TrackId = t, InstrumentReplaced = true, Status = $"Changed instrument to {item.Name} (track {t})" };
                    break;   // a rack: the default routing adds a chain / pad
                case BrowserItemKind.BuiltinEffect:
                case BrowserItemKind.PluginEffect:
                case BrowserItemKind.BuiltinMidiEffect:
                {
                    bool midi = item.Kind == BrowserItemKind.BuiltinMidiEffect;
                    if (midi && !(TryTrackInfo(t, out var mt) && mt.IsInstrument)) return InsertResult.Fail("MIDI effects need an instrument track.");
                    int idx = AddEffect(item, t);
                    if (idx < 0) return InsertResult.Fail($"Couldn't load {item.Name}.");
                    var placed = Place(t, midi, idx, target);
                    return new InsertResult
                    {
                        Ok = true, TrackId = t, ShowDevices = true, Placed = placed,
                        Status = placed.Replaced ? $"Replaced with {item.Name}" : $"Added {item.Name} to track {t}",
                    };
                }
            }
        return AddToTrack(item, t);
    }

    // The Devices-panel / Modular default routing (was MainWindow.DropBrowserItem).
    private InsertResult AddToTrack(BrowserItem item, int t)
    {
        if (t <= 0) return InsertResult.Fail("Select a track first.");
        int kind = _engine.TrackInstrumentKind(t);
        switch (item.Kind)
        {
            case BrowserItemKind.BuiltinInstrument when kind == 3:      // Instrument Rack → new chain
                _engine.RackAddChain(t, item.BuiltinKind);
                return Done(t, $"Added {item.Name} chain");
            case BrowserItemKind.BuiltinInstrument when kind == 4:      // Drum Rack → next free pad
            {
                int note = NextFreeDrumPad(t);
                if (note < 0) return InsertResult.Fail("All 16 pads are used.");
                int c = _engine.RackAddChain(t, item.BuiltinKind);
                if (c >= 0) _engine.RackSetChainTriggerNote(t, c, note);
                return Done(t, $"Added {item.Name} pad");
            }
            case BrowserItemKind.PluginInstrument when kind == 3:      // Instrument Rack → plugin chain
                return _engine.RackAddPluginInstrumentChain(t, item.CatalogIndex) < 0
                    ? InsertResult.Fail($"Couldn't load {item.Name}.") : Done(t, $"Added {item.Name} chain");
            case BrowserItemKind.PluginInstrument when kind == 4:      // Drum Rack → plugin pad
            {
                int note = NextFreeDrumPad(t);
                if (note < 0) return InsertResult.Fail("All 16 pads are used.");
                int c = _engine.RackAddPluginInstrumentChain(t, item.CatalogIndex);
                if (c < 0) return InsertResult.Fail($"Couldn't load {item.Name}.");
                _engine.RackSetChainTriggerNote(t, c, note);
                return Done(t, $"Added {item.Name} pad");
            }
            case BrowserItemKind.BuiltinInstrument:
            case BrowserItemKind.PluginInstrument:
                if (TryReplaceInstrument(item, t))
                    return new InsertResult { Ok = true, TrackId = t, InstrumentReplaced = true, Status = $"Changed instrument to {item.Name} (track {t})" };
                return InsertResult.Fail("Drop instruments onto the arrangement.");
            case BrowserItemKind.Sample when kind == 4:                 // sample → drum pad (Sampler)
            {
                int note = NextFreeDrumPad(t);
                if (note < 0) return InsertResult.Fail("All 16 pads are used.");
                int c = _engine.RackAddSamplerChain(t, item.Path, note, false);   // root = pad note → natural pitch
                if (c < 0) return InsertResult.Fail($"Couldn't load {item.Name}.");
                _engine.RackSetChainTriggerNote(t, c, note);
                return Done(t, $"Added {item.Name} pad");
            }
            case BrowserItemKind.Sample when kind == 3:                 // sample → instrument-rack Sampler chain
                return _engine.RackAddSamplerChain(t, item.Path, 60, false) < 0
                    ? InsertResult.Fail($"Couldn't load {item.Name}.") : Done(t, $"Added {item.Name} chain");
            case BrowserItemKind.Sample when kind == 1:                 // sample → load into the Sampler
                return !_engine.SetTrackSamplerSample(t, item.Path, 60)
                    ? InsertResult.Fail($"Couldn't load {item.Name}.") : Done(t, $"Loaded {item.Name}") with { ShowDevices = true };
            case BrowserItemKind.Sample when kind == 10:                // sample → load into Nota Grain
            {
                // The root from a note in the file name ("Pad_F#3.wav"), else C4.
                int root = SamplerModel.DetectRoot(item.Path) is var d and >= 0 ? d : 60;
                return !_engine.SetTrackGrainSample(t, item.Path, root)
                    ? InsertResult.Fail($"Couldn't load {item.Name}.")
                    : Done(t, $"Loaded {item.Name} · root {SamplerModel.NoteName(root)}") with { ShowDevices = true };
            }
            case BrowserItemKind.Sample:
                return InsertResult.Fail("Drop samples onto the arrangement.");
            case BrowserItemKind.MidiFile:
                return InsertResult.Fail("Drop MIDI files onto the arrangement or a session slot.");
            default:                                                    // effects + presets
                return RouteToTrack(item, t);
        }
    }

    /// <summary>An effect appended to a track, or a preset applied to it (was MainWindow.RouteToTrack).</summary>
    public InsertResult RouteToTrack(BrowserItem item, int trackId)
    {
        // Presets first: an instrument preset spawns its own track, so it doesn't need a
        // target; an effect preset reports "Select a track first." itself.
        if (item.Kind == BrowserItemKind.Preset) return ApplyPreset(item, new InsertTarget(trackId));
        if (trackId <= 0) return InsertResult.Fail("Drop onto a track.");
        switch (item.Kind)
        {
            case BrowserItemKind.BuiltinEffect:
            case BrowserItemKind.BuiltinMidiEffect:
            case BrowserItemKind.PluginEffect:
                if (item.Kind == BrowserItemKind.BuiltinMidiEffect && !(TryTrackInfo(trackId, out var ti) && ti.IsInstrument))
                    return InsertResult.Fail("MIDI effects need an instrument track.");
                if (AddEffect(item, trackId) < 0) return InsertResult.Fail(item.Kind == BrowserItemKind.PluginEffect ? "Failed to load effect." : $"Couldn't load {item.Name}.");
                return Done(trackId, $"Added {item.Name} to track {trackId}") with { ShowDevices = true };
        }
        return InsertResult.Fail($"{item.Name} can't go on a track.");
    }

    private int AddEffect(BrowserItem item, int t) => item.Kind switch
    {
        BrowserItemKind.BuiltinEffect => _engine.AddBuiltinDevice(t, item.BuiltinKind),
        BrowserItemKind.PluginEffect => _engine.AddTrackEffectPlugin(t, item.CatalogIndex),
        _ => _engine.AddMidiEffect(t, item.BuiltinKind),
    };

    // Moves an effect just appended at newIdx to where the target aims, removing the replaced
    // one (was DeviceChainView.PlaceDroppedDevice's engine half).
    private (bool Midi, int Added, int Final, bool Replaced) Place(int t, bool midi, int newIdx, InsertTarget target)
    {
        if (target.Placement == InsertPlacement.Default || target.Midi != midi) return (midi, newIdx, newIdx, false);
        int to = Math.Clamp(target.Index, 0, newIdx);
        if (to != newIdx) { if (midi) _engine.MoveMidiEffect(t, newIdx, to); else _engine.MoveDevice(t, newIdx, to); }
        bool replaced = false;
        if (target.Placement == InsertPlacement.Replace && to < newIdx)   // the replaced card now sits right after
        {
            if (midi) _engine.RemoveMidiEffect(t, to + 1); else _engine.RemoveDevice(t, to + 1);
            replaced = true;
        }
        return (midi, newIdx, to, replaced);
    }

    // ---- presets ----------------------------------------------------------------------------

    /// <summary>Applies a preset row. A kit loads into a Drum Rack / Rhythm target or makes its
    /// own track; an instrument preset makes a new track — or, for a factory preset on a plain
    /// instrument track when the target asks for it in place (the palette), swaps the
    /// instrument and loads the sound there; an effect preset lands on the target and, with a
    /// placement, moves to the aimed slot.</summary>
    public InsertResult ApplyPreset(BrowserItem item, InsertTarget target) => Group(() => ApplyPresetCore(item, target));

    private InsertResult ApplyPresetCore(BrowserItem item, InsertTarget target)
    {
        int t = target.TrackId;
        if (IsKit(item)) return ApplyKit(item, target);
        var fp = FactoryInfo(item);
        string factoryId = fp?.Id ?? "";

        // Palette: a factory instrument preset in place on a plain instrument track (CP-3).
        if (fp is { IsInstrument: true } f && !target.NewTrack && target.InPlace && CanReplaceInstrument(t))
        {
            bool swapped = false;
            if (_engine.TrackInstrumentKind(t) != f.BuiltinKind)
            {
                if (!_engine.SetTrackBuiltinInstrument(t, f.BuiltinKind)) return InsertResult.Fail($"Couldn't load {item.Name}.");
                swapped = true;
            }
            string w = _factory.ApplyInPlace(_engine, factoryId, t, -1);
            return new InsertResult
            {
                Ok = w.Length == 0, TrackId = t, InstrumentReplaced = swapped, ShowDevices = true,
                Status = w.Length > 0 ? w : $"Applied preset {item.Name}",
                PresetLanded = (t, PresetChain.Instrument, -1, item.Name, factoryId),
            };
        }

        // Apply doesn't say where the preset landed, so diff the chain around it: a new track
        // (instrument preset) or one more device / MIDI effect on the target (appended last).
        var before = TrackIds();
        int fxBefore = t > 0 ? _engine.TrackDeviceCount(t) : 0;
        int midiBefore = t > 0 ? _engine.TrackMidiEffectCount(t) : 0;
        string warn = fp is not null ? _factory.Apply(_engine, factoryId, t)
            : _presets?.ApplyFromFile(_engine, item.Path, t) ?? "Presets are unavailable.";
        if (warn.Length > 0) return InsertResult.Fail(warn);

        int newTrack = TrackIds().FirstOrDefault(id => !before.Contains(id));
        if (newTrack > 0)
            return new InsertResult
            {
                Ok = true, TrackId = newTrack, CreatedTrack = true, ShowDevices = true, Status = $"Applied preset {item.Name}",
                PresetLanded = (newTrack, PresetChain.Instrument, -1, item.Name, factoryId),
            };
        if (t > 0 && _engine.TrackDeviceCount(t) is var fx && fx > fxBefore)
        {
            var placed = Place(t, false, fx - 1, target);
            return new InsertResult
            {
                Ok = true, TrackId = t, ShowDevices = true, Placed = placed, Status = $"Applied preset {item.Name}",
                PresetLanded = (t, PresetChain.Effect, placed.Final, item.Name, factoryId),
            };
        }
        if (t > 0 && _engine.TrackMidiEffectCount(t) is var midi && midi > midiBefore)
        {
            var placed = Place(t, true, midi - 1, target);
            return new InsertResult
            {
                Ok = true, TrackId = t, ShowDevices = true, Placed = placed, Status = $"Applied preset {item.Name}",
                PresetLanded = (t, PresetChain.Midi, placed.Final, item.Name, factoryId),
            };
        }
        return new InsertResult { Ok = true, TrackId = t, ShowDevices = t > 0, Status = $"Applied preset {item.Name}" };
    }

    // A factory kit. On a Drum Rack or Nota Rhythm it replaces the pads / voices; anywhere else
    // it spawns its own track — a Drum Rack, or a Rhythm for a row under Rhythm.
    private InsertResult ApplyKit(BrowserItem item, InsertTarget target)
    {
        if (_kits is null) return InsertResult.Fail("Kits are unavailable.");
        bool rhythm = item.Path.StartsWith(RhythmKitPrefix, StringComparison.Ordinal);
        string id = item.Path[(rhythm ? RhythmKitPrefix.Length : KitPrefix.Length)..];
        int t = target.TrackId;
        if (!target.NewTrack && t > 0 && _engine.TrackInstrumentKind(t) is 4 or RhythmModel.Kind)
        {
            _kits.LoadInto(_engine, t, id, out string replaceWarn);
            return new InsertResult { Ok = true, TrackId = t, ShowDevices = true, Status = replaceWarn.Length > 0 ? replaceWarn : $"Loaded {item.Name} kit" };
        }
        int track = rhythm ? _kits.CreateRhythmTrack(_engine, id, out string warn) : _kits.CreateTrack(_engine, id, out warn);
        if (track <= 0) return InsertResult.Fail(warn.Length > 0 ? warn : $"Couldn't load {item.Name}.");
        _engine.AddMidiClip(track, 0.0, 4.0);
        return new InsertResult { Ok = true, TrackId = track, CreatedTrack = true, ShowDevices = true, Status = warn.Length > 0 ? warn : $"Loaded {item.Name} kit" };
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static InsertResult Done(int t, string status) => new() { Ok = true, TrackId = t, Status = status };

    private HashSet<int> TrackIds()
    {
        var ids = new HashSet<int>();
        for (int i = 0; i < _engine.TrackCount; i++)
            if (_engine.TryGetTrackInfo(i, out var ti)) ids.Add(ti.Id);
        return ids;
    }

    // One undo step per insertion (CP-25). Nests, so an outer group (a multi-item drop) holds.
    private InsertResult Group(Func<InsertResult> body)
    {
        _engine.BeginUndoGroup();
        try { return body(); }
        catch (Exception ex) { return InsertResult.Fail($"Drop failed: {ex.Message}"); }
        finally { _engine.EndUndoGroup(); }
    }
}
