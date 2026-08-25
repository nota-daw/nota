// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Infrastructure;

/// <summary>IPluginCatalog over the engine's process-wide plugin host statics.</summary>
public sealed class PluginCatalog : IPluginCatalog
{
    public int Scan(string workerPath) => NotaEngine.ScanPlugins(workerPath);
    public int Count => NotaEngine.PluginCount;
    public string? Description(int index) => NotaEngine.PluginDescription(index);
    public string? Id(int index) => NotaEngine.PluginId(index);
    public int IndexOfId(string identifier) => NotaEngine.PluginIndexOfId(identifier);
    public void AddScanPath(string dir) => NotaEngine.AddScanPath(dir);
    public void RemoveScanPath(int index) => NotaEngine.RemoveScanPath(index);
    public int ScanPathCount => NotaEngine.ScanPathCount;
    public string? ScanPath(int index) => NotaEngine.ScanPath(index);
}
