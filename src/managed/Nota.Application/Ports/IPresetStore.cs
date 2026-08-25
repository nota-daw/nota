// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Application;

/// <summary>Save/apply of a single device or instrument preset. Listing presets for
/// the browser is <see cref="IPresetLibrary"/>.</summary>
public interface IPresetStore
{
    /// <summary>Loads a preset file and applies it to a track. Returns a warning
    /// string ("" = clean).</summary>
    string ApplyFromFile(IAudioEngine engine, string path, int targetTrackId);

    /// <summary>Captures a track's device/instrument as a preset and saves it to
    /// <paramref name="folder"/>. Returns false if there was nothing to capture
    /// (e.g. the built-in synth has no state).</summary>
    bool Save(IAudioEngine engine, int trackId, int deviceIndex, string displayName, string folder);

    /// <summary>Captures a rack chain's instrument as a preset (hosted plugins only).
    /// Returns false if there was nothing to capture (built-in chain instrument).</summary>
    bool SaveRackChainInstrument(IAudioEngine engine, int trackId, int chain, string displayName, string folder);
}
