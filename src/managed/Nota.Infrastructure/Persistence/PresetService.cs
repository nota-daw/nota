// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M7-4c: capture/save/list/load/apply device & instrument presets. Built on the
// existing device param API (built-in devices) and plugin-state + stable
// plugin-id API (AU/VST3), mirroring ProjectService's ownership model. Presets
// are *.notapreset JSON files in an app-managed folder.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Nota.Infrastructure;

public static class PresetService
{
    public const string Extension = ".notapreset";
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Snapshot a track's device (deviceIndex &lt; 0 = its instrument) into a preset.
    /// Returns null if the target has nothing capturable (e.g. a built-in synth).</summary>
    public static PresetDocument? Capture(IAudioEngine engine, int trackId, int deviceIndex, string displayName)
    {
        var doc = new PresetDocument { DisplayName = displayName, DeviceName = engine.DeviceName(trackId, deviceIndex) };
        if (deviceIndex < 0)
        {
            int ik = engine.TrackInstrumentKind(trackId);
            if (ik == -1)   // hosted plugin: opaque state blob
            {
                doc.Type = "plugin-instrument";
                doc.PluginId = engine.TrackInstrumentPluginId(trackId);
                doc.StateBase64 = Convert.ToBase64String(engine.GetPluginState(trackId, -1));
                return string.IsNullOrEmpty(doc.PluginId) ? null : doc;
            }
            // Built-in instrument: capture the normalized plugin-params by id. Skip kinds whose
            // state isn't fully recallable from params alone — Sampler (1) & Grain (10) need the
            // sample, Rhythm (12) its patterns/samples, and the Instrument/Drum racks (3/4) their
            // chains. Everything else (Synth, Physical, Aurora, Volt, Bass, Pendulum, Operator,
            // Flux, …) is a param-only synth and saves cleanly.
            if (ik is 1 or 3 or 4 or 10 or 12) return null;
            {
                int pc = engine.PluginParamCount(trackId, -1);
                if (pc <= 0) return null;
                doc.Type = "builtin-instrument";
                doc.BuiltinKind = ik;
                doc.NamedParams = new Dictionary<string, float>(pc);
                for (int i = 0; i < pc; i++)
                    doc.NamedParams[engine.PluginParamId(trackId, -1, i)] = engine.PluginParamGet(trackId, -1, i);
                return doc;
            }
        }

        int bk = engine.TrackDeviceBuiltinKind(trackId, deviceIndex);
        if (bk >= 0)
        {
            doc.Type = "builtin-effect";
            doc.BuiltinKind = bk;
            int pc = engine.DeviceParamCount(trackId, deviceIndex);
            doc.NamedParams = new Dictionary<string, float>(pc);
            for (int i = 0; i < pc; i++)
                doc.NamedParams[engine.DeviceParamName(trackId, deviceIndex, i)] = engine.DeviceGetParam(trackId, deviceIndex, i);
            return doc;
        }

        doc.Type = "plugin-effect";
        doc.PluginId = engine.TrackDevicePluginId(trackId, deviceIndex);
        doc.StateBase64 = Convert.ToBase64String(engine.GetPluginState(trackId, deviceIndex));
        return string.IsNullOrEmpty(doc.PluginId) ? null : doc;
    }

    /// <summary>Snapshot a rack chain's instrument (only hosted plugins carry recallable
    /// state) into a preset. Returns null for built-in chain instruments.</summary>
    public static PresetDocument? CaptureRackChainInstrument(IAudioEngine engine, int trackId, int chain, string displayName)
    {
        if (engine.RackChainInstrumentKind(trackId, chain) != -1) return null; // -1 = plugin
        var doc = new PresetDocument
        {
            DisplayName = displayName,
            Type = "plugin-instrument",
            DeviceName = engine.RackChainInstrumentName(trackId, chain),
            PluginId = engine.RackChainInstrumentPluginId(trackId, chain),
            StateBase64 = Convert.ToBase64String(engine.RackGetChainInstrumentState(trackId, chain)),
        };
        return string.IsNullOrEmpty(doc.PluginId) ? null : doc;
    }

    /// <summary>Writes the preset atomically; returns the file path.</summary>
    public static string Save(PresetDocument doc, string presetsDir)
    {
        Directory.CreateDirectory(presetsDir);
        string name = Sanitize(string.IsNullOrWhiteSpace(doc.DisplayName) ? "Preset" : doc.DisplayName);
        string path = Path.Combine(presetsDir, name + Extension);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts));
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    /// <summary>All presets in the folder (path + parsed doc), name-sorted.</summary>
    public static IEnumerable<(string Path, PresetDocument Doc)> List(string presetsDir)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(presetsDir, "*" + Extension); }
        catch { yield break; }
        foreach (var path in files)
        {
            PresetDocument? doc = null;
            try { doc = Load(path); } catch { /* skip corrupt */ }
            if (doc is not null)
            {
                if (string.IsNullOrEmpty(doc.DisplayName))
                    doc.DisplayName = Path.GetFileNameWithoutExtension(path);
                yield return (path, doc);
            }
        }
    }

    public static PresetDocument Load(string path)
        => JsonSerializer.Deserialize<PresetDocument>(File.ReadAllText(path))
           ?? throw new InvalidDataException("empty preset");

    /// <summary>Applies a preset. Effect presets add to <paramref name="targetTrackId"/> (must be &gt; 0);
    /// instrument presets always create a new track. Returns "" on success or a user-facing warning.</summary>
    public static string Apply(PresetDocument doc, IAudioEngine engine, int targetTrackId)
    {
        switch (doc.Type)
        {
            case "builtin-effect":
            {
                if (targetTrackId <= 0) return "Select a track first.";
                int di = engine.AddBuiltinDevice(targetTrackId, doc.BuiltinKind);
                if (di < 0) return "Failed to add device.";
                if (doc.NamedParams is { Count: > 0 })
                {
                    int pc = engine.DeviceParamCount(targetTrackId, di);
                    for (int i = 0; i < pc; i++)
                        if (doc.NamedParams.TryGetValue(engine.DeviceParamName(targetTrackId, di, i), out var v))
                            engine.DeviceSetParam(targetTrackId, di, i, v);
                }
                else
                    for (int i = 0; i < doc.Params.Length; i++)
                        engine.DeviceSetParam(targetTrackId, di, i, doc.Params[i]);
                return "";
            }
            case "builtin-midi-effect":
            {
                if (targetTrackId <= 0) return "Select a track first.";
                int mi = engine.AddMidiEffect(targetTrackId, doc.BuiltinKind);
                if (mi < 0) return "Failed to add MIDI effect.";
                if (doc.NamedParams is { Count: > 0 })
                {
                    int pc = engine.MidiEffectParamCount(targetTrackId, mi);
                    for (int i = 0; i < pc; i++)
                        if (doc.NamedParams.TryGetValue(engine.MidiEffectParamName(targetTrackId, mi, i), out var v))
                            engine.MidiEffectSetParam(targetTrackId, mi, i, v);
                }
                return "";
            }
            case "builtin-instrument":
            {
                int t = doc.BuiltinKind switch
                {
                    2 => engine.AddPhysicalSynthTrack(),
                    5 => engine.AddWavetableSynthTrack(),
                    6 => engine.AddVoltSynthTrack(),
                    7 => engine.AddBassSynthTrack(),
                    8 => engine.AddPendulumSynthTrack(),
                    9 => engine.AddOperatorSynthTrack(),
                    10 => engine.AddGrainSynthTrack(),
                    11 => engine.AddFluxSynthTrack(),
                    12 => engine.AddRhythmTrack(),
                    13 => engine.AddMonolithTrack(),
                    14 => engine.AddPentadTrack(),
                    _ => engine.AddInstrumentTrack(),
                };
                if (t <= 0) return "Failed to add instrument.";
                if (doc.NamedParams is { Count: > 0 })
                {
                    int pc = engine.PluginParamCount(t, -1);
                    for (int i = 0; i < pc; i++)
                        if (doc.NamedParams.TryGetValue(engine.PluginParamId(t, -1, i), out var v))
                            engine.PluginParamSet(t, -1, i, v);
                }
                return "";
            }
            case "plugin-effect":
            {
                if (targetTrackId <= 0) return "Select a track first.";
                int idx = NotaEngine.PluginIndexOfId(doc.PluginId);
                if (idx < 0) return $"Plugin not installed: {doc.PluginId}";
                int di = engine.AddTrackEffectPlugin(targetTrackId, idx);
                if (di < 0) return "Failed to load plugin.";
                ApplyState(engine, targetTrackId, di, doc.StateBase64);
                return "";
            }
            case "plugin-instrument":
            {
                int idx = NotaEngine.PluginIndexOfId(doc.PluginId);
                if (idx < 0) return $"Plugin not installed: {doc.PluginId}";
                int t = engine.AddPluginInstrumentTrack(idx);
                if (t <= 0) return "Failed to load instrument.";
                ApplyState(engine, t, -1, doc.StateBase64);
                return "";
            }
            default:
                return "Unknown preset type.";
        }
    }

    /// <summary>Applies a preset to an EXISTING instrument/device in place (no new track/
    /// device is created) — for the in-header preset picker. Built-in instruments reset all
    /// params to default first so the patch is clean; effects set the preset's named params.</summary>
    public static string ApplyInPlace(PresetDocument doc, IAudioEngine engine, int trackId, int deviceIndex)
    {
        if (trackId <= 0) return "Select a track first.";
        switch (doc.Type)
        {
            case "builtin-instrument":
            {
                int pc = engine.PluginParamCount(trackId, -1);
                for (int i = 0; i < pc; i++)
                {
                    string id = engine.PluginParamId(trackId, -1, i);
                    float v = doc.NamedParams is { Count: > 0 } && doc.NamedParams.TryGetValue(id, out var pv)
                        ? pv : engine.InstrumentParamDefault(trackId, i);
                    engine.PluginParamSet(trackId, -1, i, v);
                }
                return "";
            }
            case "builtin-effect":
            {
                if (deviceIndex < 0) return "Not an effect.";
                int pc = engine.DeviceParamCount(trackId, deviceIndex);
                if (doc.NamedParams is { Count: > 0 })
                {
                    // A param the preset doesn't name goes back to its default (like instruments),
                    // so switching presets never inherits leftovers from the previous one.
                    for (int i = 0; i < pc; i++)
                        engine.DeviceSetParam(trackId, deviceIndex, i,
                            doc.NamedParams.TryGetValue(engine.DeviceParamName(trackId, deviceIndex, i), out var v)
                                ? v : engine.DeviceParamDefault(trackId, deviceIndex, i));
                }
                else
                {
                    for (int i = 0; i < doc.Params.Length && i < pc; i++)
                        engine.DeviceSetParam(trackId, deviceIndex, i, doc.Params[i]);
                }
                return "";
            }
            case "builtin-midi-effect":
            {
                if (deviceIndex < 0) return "Not a MIDI effect.";
                if (doc.NamedParams is { Count: > 0 })
                {
                    int pc = engine.MidiEffectParamCount(trackId, deviceIndex);
                    for (int i = 0; i < pc; i++)
                        if (doc.NamedParams.TryGetValue(engine.MidiEffectParamName(trackId, deviceIndex, i), out var v))
                            engine.MidiEffectSetParam(trackId, deviceIndex, i, v);
                }
                return "";
            }
            case "plugin-effect":
                if (deviceIndex < 0) return "Not an effect.";
                ApplyState(engine, trackId, deviceIndex, doc.StateBase64);
                return "";
            case "plugin-instrument":
                ApplyState(engine, trackId, -1, doc.StateBase64);
                return "";
            default:
                return "Preset can't be applied in place.";
        }
    }

    private static void ApplyState(IAudioEngine engine, int trackId, int deviceIndex, string base64)
    {
        if (string.IsNullOrEmpty(base64)) return;
        try { engine.SetPluginState(trackId, deviceIndex, Convert.FromBase64String(base64)); }
        catch { /* mismatched state — leave plugin at defaults */ }
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }
}
