// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>A plugin listed in the Nota plugin registry (github.com/nota-daw/nota-plugins-registry),
/// reduced to what the running platform can install.</summary>
public sealed record StorePlugin(
    string Id,
    string Name,
    string Developer,
    string Description,
    string Kind,                          // instrument | effect | midi | bundle
    string License,                       // SPDX id
    string Repo,                          // source repository URL
    string? Homepage,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Provides,       // VST3 display names, as the plugin scan reports them
    string Version,                       // newest registry version
    StoreAsset? Asset)                    // null: no build for this platform
{
    /// <summary>Optional caveat from the registry (e.g. "factory content is a separate download").</summary>
    public string? Notes { get; init; }

    /// <summary>Every asset key the newest version ships (e.g. macos-universal, windows-x64),
    /// including platforms other than this one.</summary>
    public IReadOnlyList<string> Platforms { get; init; } = [];
}

/// <summary>One downloadable release asset and the VST3 bundles inside it.</summary>
public sealed record StoreAsset(
    string Platform,                      // e.g. macos-universal, windows-x64, linux-x64
    string Url,
    string Sha256,
    long Size,
    string Archive,                       // zip | tar | dmg | pkg | deb
    string? Inner,                        // nested archive to expand as well (a .pkg inside a .dmg)
    IReadOnlyList<string> Bundles);       // relative .vst3 paths inside the (inner) archive

/// <summary>A registry plugin installed into Nota's managed plugin folder.</summary>
public sealed record InstalledStorePlugin(string Id, string Version, string Platform, string Sha256,
                                          IReadOnlyList<string> Bundles, DateTimeOffset InstalledAt);

/// <summary>Install progress: <paramref name="Fraction"/> in 0..1, or negative when unknown.</summary>
public readonly record struct StoreProgress(double Fraction, string Message);

/// <summary>Browse and install open-source plugins from the Nota plugin registry. Installs only
/// unpack archives (never run installers) into <see cref="PluginsDir"/>, which the plugin scan
/// searches; callers rescan the catalog after <see cref="InstallAsync"/> / <see cref="Uninstall"/>.</summary>
public interface IPluginStore
{
    /// <summary>Folder that holds installed VST3 bundles, one subfolder per plugin id.</summary>
    string PluginsDir { get; }

    /// <summary>The registry's plugins (newest version each). Served from the on-disk cache unless
    /// <paramref name="refresh"/> is set or the cache is older than a day; falls back to the cache
    /// when offline. Throws only when there is neither network nor cache.</summary>
    Task<IReadOnlyList<StorePlugin>> FetchAsync(bool refresh = false, CancellationToken ct = default);

    /// <summary>Plugins installed from the registry.</summary>
    IReadOnlyList<InstalledStorePlugin> Installed { get; }

    /// <summary>Downloads, verifies (size + sha256) and unpacks the plugin's asset, replacing any
    /// installed version. Throws <see cref="PluginStoreException"/> with a user-facing message.</summary>
    Task InstallAsync(StorePlugin plugin, IProgress<StoreProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Removes an installed plugin's files. No-op when it isn't installed.</summary>
    void Uninstall(string id);

    /// <summary>The registry plugin providing a hosted-plugin catalog identifier (as saved in
    /// projects: "VST3-Name-hash-uid"), from the last fetched list; null if none does.</summary>
    StorePlugin? FindProvider(string pluginIdentifier);
}

public sealed class PluginStoreException(string message, Exception? inner = null) : Exception(message, inner);
