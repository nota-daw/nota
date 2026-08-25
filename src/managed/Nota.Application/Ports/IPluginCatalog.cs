// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Application;

/// <summary>The scanned AU/VST3 plugin catalog and its user scan paths. Wraps the
/// engine's process-wide plugin host so ViewModels stay Infrastructure-free.</summary>
public interface IPluginCatalog
{
    /// <summary>Rescans installed plugins out-of-process. Returns the known count.</summary>
    int Scan(string workerPath);
    /// <summary>Number of plugins currently in the catalog.</summary>
    int Count { get; }
    /// <summary>Human-readable entry ("Name | Format | inst|fx | Manufacturer"), or null.</summary>
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
