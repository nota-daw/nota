// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>The scanned AU/VST3 plugin catalog and its user scan paths. Wraps the
/// engine's process-wide plugin host so ViewModels stay Infrastructure-free.</summary>
public interface IPluginCatalog
{
    /// <summary>Rescans installed plugins out-of-process. Returns the known count.</summary>
    int Scan(string workerPath);
    /// <summary>Rescan with options: <paramref name="onlyDir"/> searches just that folder (a store
    /// install); <paramref name="retryFailed"/> probes plugins that failed before again.</summary>
    int Scan(string workerPath, string? onlyDir, bool retryFailed) => Scan(workerPath);
    /// <summary>Progress of the running scan, readable from any thread; null when none runs.</summary>
    PluginScanProgress? ScanProgress => null;
    /// <summary>Plugins that failed to load (crash, hang, nothing inside), skipped until they change.</summary>
    int FailedCount => 0;
    /// <summary>Number of plugins currently in the catalog.</summary>
    int Count { get; }
    /// <summary>Human-readable entry ("Name | Format | inst|fx | Manufacturer | Category"; the category
    /// is the VST3 sub-categories joined by "/" or the AU type, "" when unknown), or null.</summary>
    string? Description(int index);
    /// <summary>Stable identifier for a catalog entry, or null.</summary>
    string? Id(int index);
    /// <summary>Catalog index whose identifier matches, or -1 if not installed.</summary>
    int IndexOfId(string identifier);

    void AddScanPath(string dir);
    void RemoveScanPath(int index);
    int ScanPathCount { get; }
    string? ScanPath(int index);
}

/// <summary>A running plugin scan: probes finished of <see cref="Total"/> (0 while still
/// searching folders), and a plugin being probed now.</summary>
public readonly record struct PluginScanProgress(int Done, int Total, string Current);
