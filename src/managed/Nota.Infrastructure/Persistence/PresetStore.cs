// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Infrastructure;

/// <summary>IPresetStore over PresetService.</summary>
public sealed class PresetStore : IPresetStore
{
    public string ApplyFromFile(IAudioEngine engine, string path, int targetTrackId)
    {
        var doc = PresetService.Load(path);
        return PresetService.Apply(doc, engine, targetTrackId);
    }

    public string? ApplyInstrumentInPlace(IAudioEngine engine, string path, int trackId, out bool swapped)
    {
        swapped = false;
        PresetDocument doc;
        try { doc = PresetService.Load(path); }
        catch { return null; }   // ApplyFromFile reports it
        switch (doc.Type)
        {
            case "builtin-instrument":
                if (engine.TrackInstrumentKind(trackId) != doc.BuiltinKind || engine.TrackInstrumentPluginId(trackId).Length > 0)
                {
                    if (!engine.SetTrackBuiltinInstrument(trackId, doc.BuiltinKind)) return $"Couldn't load {doc.DisplayName}.";
                    swapped = true;
                }
                break;
            case "plugin-instrument":
                if (engine.TrackInstrumentPluginId(trackId) != doc.PluginId)
                {
                    int idx = NotaEngine.PluginIndexOfId(doc.PluginId);
                    if (idx < 0) return $"Plugin not installed: {doc.PluginId}";
                    engine.SetTrackInstrumentPlugin(trackId, idx);
                    swapped = true;
                }
                break;
            default:
                return null;
        }
        return PresetService.ApplyInPlace(doc, engine, trackId, -1);
    }

    public (string Type, int BuiltinKind)? Describe(string path)
    {
        try
        {
            var doc = PresetService.Load(path);
            return (doc.Type, doc.BuiltinKind);
        }
        catch { return null; }
    }

    public IReadOnlyDictionary<string, float>? InstrumentParams(string path)
    {
        try
        {
            var doc = PresetService.Load(path);
            return doc.Type == "builtin-instrument" && doc.NamedParams is { Count: > 0 } named ? named : null;
        }
        catch { return null; }
    }

    public string ApplyFileInPlace(IAudioEngine engine, string path, int trackId, int deviceIndex)
    {
        try { return PresetService.ApplyInPlace(PresetService.Load(path), engine, trackId, deviceIndex); }
        catch (Exception e) { return $"Couldn't load the preset: {e.Message}"; }
    }

    public bool Save(IAudioEngine engine, int trackId, int deviceIndex, string displayName, string folder)
    {
        var doc = PresetService.Capture(engine, trackId, deviceIndex, displayName);
        if (doc is null) return false;
        PresetService.Save(doc, folder);
        return true;
    }

    public bool SaveRackChainInstrument(IAudioEngine engine, int trackId, int chain, string displayName, string folder)
    {
        var doc = PresetService.CaptureRackChainInstrument(engine, trackId, chain, displayName);
        if (doc is null) return false;
        PresetService.Save(doc, folder);
        return true;
    }
}
