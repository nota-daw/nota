// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>Save/apply of a single device or instrument preset. Listing presets for
/// the browser is <see cref="IPresetLibrary"/>.</summary>
public interface IPresetStore
{
    /// <summary>Loads a preset file and applies it to a track. Returns a warning
    /// string ("" = clean).</summary>
    string ApplyFromFile(IAudioEngine engine, string path, int targetTrackId);

    /// <summary>Loads an instrument preset file into a track's instrument in place, swapping
    /// the instrument first when the preset is for another one (<paramref name="swapped"/>).
    /// Null — nothing done — when the file isn't an instrument preset; else a warning ("" = clean).</summary>
    string? ApplyInstrumentInPlace(IAudioEngine engine, string path, int trackId, out bool swapped);

    /// <summary>What a preset file is for: its type ("builtin-instrument", "builtin-effect",
    /// "builtin-midi-effect", "plugin-instrument", "plugin-effect") and built-in kind (−1 for a
    /// plug-in). Null when the file can't be read.</summary>
    (string Type, int BuiltinKind)? Describe(string path);

    /// <summary>A built-in instrument preset file's params by id (null when the file can't be
    /// read, isn't a built-in instrument preset or names none) — for loading part of a preset,
    /// e.g. one Nota Twin part (specs/055).</summary>
    IReadOnlyDictionary<string, float>? InstrumentParams(string path) => null;

    /// <summary>Applies a preset file to a device that already matches it, in place
    /// (<paramref name="deviceIndex"/> −1 = the instrument, else the effect or MIDI effect at that
    /// index). Returns a warning ("" = clean).</summary>
    string ApplyFileInPlace(IAudioEngine engine, string path, int trackId, int deviceIndex);

    /// <summary>Captures a track's device/instrument as a preset and saves it to
    /// <paramref name="folder"/>. Returns false if there was nothing to capture
    /// (e.g. the built-in synth has no state).</summary>
    bool Save(IAudioEngine engine, int trackId, int deviceIndex, string displayName, string folder);

    /// <summary>Captures a rack chain's instrument as a preset (hosted plugins only).
    /// Returns false if there was nothing to capture (built-in chain instrument).</summary>
    bool SaveRackChainInstrument(IAudioEngine engine, int trackId, int chain, string displayName, string folder);
}
