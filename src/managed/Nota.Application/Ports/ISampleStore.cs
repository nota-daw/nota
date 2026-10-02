// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>A sample pack listed in the Nota sample registry (github.com/nota-daw/nota-samples-registry).</summary>
public sealed record StorePack(
    string Id,
    string Name,
    string Author,
    string Description,
    string Kind,                          // one-shots | loops | multisample | mixed
    string License,                       // SPDX id (CC0-1.0, CC-BY-4.0, …)
    string Source,                        // the page where the author publishes the pack
    IReadOnlyList<string> Tags,
    string Version,                       // newest registry version
    StorePackAsset Asset)
{
    /// <summary>Credit line the license asks for (CC-BY); null for CC0.</summary>
    public string? Attribution { get; init; }

    /// <summary>Optional caveat from the registry.</summary>
    public string? Notes { get; init; }

    public string? Homepage { get; init; }
}

/// <summary>The pack's archive and what installing it puts on disk.</summary>
public sealed record StorePackAsset(
    string Url,
    string Sha256,
    long Size,                            // download bytes
    string Archive,                       // zip | tar
    string? Root,                         // folder inside the archive to install; null = all of it
    int Files,                            // audio files installed
    long UnpackedSize,                    // bytes installed (audio + docs)
    IReadOnlyList<string> Formats);       // audio extensions, e.g. wav, flac

/// <summary>A registry pack installed into the Samples folder.</summary>
public sealed record InstalledStorePack(string Id, string Version, string Sha256, string Path, int Files, DateTimeOffset InstalledAt);

/// <summary>Browse and install free sample packs from the Nota sample registry. A pack installs
/// into its own folder under <see cref="InstallDir"/> (inside the Samples folder, so the browser's
/// Files tab lists it); only audio and docs are copied out of the archive.</summary>
public interface ISampleStore
{
    /// <summary>Where new packs go: "Downloaded" inside the current Samples folder.</summary>
    string InstallDir { get; }

    /// <summary>The registry's packs (newest version each). Served from the on-disk cache unless
    /// <paramref name="refresh"/> is set or the cache is older than a day; falls back to the cache
    /// when offline. Throws only when there is neither network nor cache.</summary>
    Task<IReadOnlyList<StorePack>> FetchAsync(bool refresh = false, CancellationToken ct = default);

    /// <summary>Packs installed from the registry whose folders still exist.</summary>
    IReadOnlyList<InstalledStorePack> Installed { get; }

    /// <summary>Checks free space, downloads, verifies (size + sha256) and unpacks the pack, replacing
    /// any installed version. Throws <see cref="StoreException"/> with a user-facing message.</summary>
    Task InstallAsync(StorePack pack, IProgress<StoreProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Removes an installed pack's folder. No-op when it isn't installed.</summary>
    void Uninstall(string id);
}
