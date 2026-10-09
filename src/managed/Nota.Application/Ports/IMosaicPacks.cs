// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Nota.Application.Mosaic;

namespace Nota.Application;

/// <summary>A Nota Mosaic preset made from a sample pack (its SFZ or its file names) or by
/// "Create multisample": a .notapreset under the Mosaic packs folder, grouped by
/// <see cref="Folder"/> in the browser (Nota Mosaic → Packs → Folder).</summary>
public sealed record MosaicPackPreset(string Path, string Name, string Folder, string Source);

/// <summary>Nota Mosaic's preset library beyond the factory set: presets generated for the
/// installed sample packs and the multisamples the user creates.</summary>
public interface IMosaicPacks
{
    /// <summary>Where the generated presets live (one folder per pack).</summary>
    string Root { get; }

    /// <summary>Every generated / created preset on disk now.</summary>
    IReadOnlyList<MosaicPackPreset> Presets();

    /// <summary>Fired (on a worker thread) after a scan wrote new presets.</summary>
    event Action? Changed;

    /// <summary>Makes presets for every installed pack that has none yet (SFZ first, else the
    /// file names). Returns how many presets were written.</summary>
    Task<int> ScanAsync(CancellationToken ct = default);

    /// <summary>Saves a program as a preset in <paramref name="folder"/>; returns the file path.</summary>
    string Save(MosaicProgram program, string name, string folder, IReadOnlyDictionary<string, float>? param = null);

    /// <summary>Loads a Mosaic preset file into a track's Mosaic in place (its sound and its program).
    /// Returns "" or a warning.</summary>
    string ApplyInPlace(IAudioEngine engine, string path, int trackId);

    /// <summary>The program a preset file holds (null when it isn't a Mosaic preset).</summary>
    MosaicProgram? Read(string path);

    /// <summary>A portable reference for a sample path ("samples:…" inside the Samples folder).</summary>
    string ToRef(string path);

    /// <summary>The absolute path a program's file reference names, or null.</summary>
    string? Resolve(string reference);

    /// <summary>A file's fundamental as a MIDI note (fractional), or null — pitch detection for
    /// the auto-mapper.</summary>
    double? DetectNote(string path);
}
