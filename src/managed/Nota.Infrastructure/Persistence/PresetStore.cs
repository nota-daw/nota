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
