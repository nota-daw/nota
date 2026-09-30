// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>Transport values the App owns, passed across the persistence boundary
/// so the on-disk model stays UI-framework-free.</summary>
public readonly record struct TransportState(double Bpm, double MasterVolume, bool MetronomeOn, bool LoopOn,
    int TimeSigNumerator = 4, int TimeSigDenominator = 4);

/// <summary>Outcome of loading a project: the transport to restore + any items that
/// were downgraded/skipped (referential-integrity warnings).</summary>
public readonly record struct ProjectLoadResult(TransportState Transport, IReadOnlyList<string> Warnings)
{
    /// <summary>Hosted-plugin identifiers the project uses that aren't installed (distinct).</summary>
    public IReadOnlyList<string> MissingPlugins { get; init; } = [];
}

/// <summary>Save/load of the <c>.nota</c> project bundle. Hides the on-disk document
/// model and the engine-capture/apply mechanics behind a use-case surface.</summary>
public interface IProjectStore
{
    /// <summary>Captures the engine + transport into <paramref name="dir"/>. Returns
    /// warnings for anything unsupported/skipped.</summary>
    IReadOnlyList<string> Save(IAudioEngine engine, TransportState transport, string dir);

    /// <summary>Loads a bundle and applies it to a reset engine. Returns the transport
    /// to restore + downgrade warnings.</summary>
    ProjectLoadResult Load(IAudioEngine engine, string dir);

    /// <summary>A comparable snapshot of the project's current state (the serialized
    /// manifest, nothing written to disk). Two equal fingerprints mean no change —
    /// used to detect unsaved edits.</summary>
    string Fingerprint(IAudioEngine engine, TransportState transport);
}
