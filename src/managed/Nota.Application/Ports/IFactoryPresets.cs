// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Application;

/// <summary>A shipped factory preset for a built-in instrument or effect, described
/// for the browser. Grouped under its parent device (BuiltinKind + IsInstrument).</summary>
public readonly record struct FactoryPresetInfo(
    string Id,            // stable synthetic id; the browser row's Path is "factory:" + Id
    string DisplayName,
    bool IsInstrument,    // groups under the Instruments tree
    int BuiltinKind,      // parent built-in device kind
    bool IsMidiEffect = false); // groups under the MIDI tree (else FX when not an instrument)

/// <summary>The built-in (read-only) preset library shipped with the app.</summary>
public interface IFactoryPresets
{
    /// <summary>All factory presets, in catalog order.</summary>
    IReadOnlyList<FactoryPresetInfo> All();

    /// <summary>Applies a factory preset by id. Instrument presets create a new track;
    /// effect presets add to <paramref name="targetTrackId"/>. Returns "" on success or a
    /// user-facing warning.</summary>
    string Apply(IAudioEngine engine, string id, int targetTrackId);

    /// <summary>Applies a factory preset by id to an EXISTING instrument/device in place
    /// (deviceIndex -1 = the track's instrument), for the in-header preset picker.</summary>
    string ApplyInPlace(IAudioEngine engine, string id, int trackId, int deviceIndex);
}
