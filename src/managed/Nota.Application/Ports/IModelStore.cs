// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>Ids of the AI models Nota can download.</summary>
public static class AiModels
{
    /// <summary>htdemucs: splits audio into drums, bass, other and vocals (Separate Stems).</summary>
    public const string Stems = "htdemucs";
    /// <summary>basic-pitch: polyphonic audio → notes (Convert Melody / Harmony).</summary>
    public const string Transcription = "basic-pitch";
    /// <summary>ONNX Runtime, the library every model runs on; installed with the first one.</summary>
    public const string Runtime = "onnxruntime";
}

/// <summary>A downloadable AI model (or the runtime they share).</summary>
public sealed record StoreModel(
    string Id,
    string Name,
    string Feature,                       // what it does in Nota, e.g. "Separate Stems"
    string Description,
    string Author,
    string License,                       // SPDX id
    string Source,                        // where the model comes from
    long Size,                            // download bytes
    long UnpackedSize);                   // bytes on disk once installed

/// <summary>Installs the AI models behind Separate Stems and Convert to MIDI, with the ONNX
/// Runtime library they run on. Every download is pinned to a size + sha256; nothing is
/// executed. Models live in Nota's data folder, not the Samples folder.</summary>
public interface IModelStore
{
    /// <summary>The models the user can install (the runtime isn't listed).</summary>
    IReadOnlyList<StoreModel> Models { get; }

    /// <summary>The runtime for this computer, or null when there is no build for it.</summary>
    StoreModel? Runtime { get; }

    bool IsInstalled(string id);

    /// <summary>Bytes installing <paramref name="id"/> downloads now: the model, plus the
    /// runtime when it isn't installed yet.</summary>
    long DownloadSize(string id);

    /// <summary>Downloads, verifies and installs the model, and the runtime first if needed.
    /// Throws <see cref="StoreException"/> with a user-facing message.</summary>
    Task InstallAsync(string id, IProgress<StoreProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Removes the model; removing the last one also removes the runtime.</summary>
    void Uninstall(string id);

    /// <summary>The installed runtime's library file, or null.</summary>
    string? RuntimePath { get; }

    /// <summary>The installed model's file, or null.</summary>
    string? ModelPath(string id);

    /// <summary>Raised (on any thread) after an install or uninstall.</summary>
    event Action? Changed;
}
