// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Empty plugin catalog / preset library, so a BrowserViewModel can be built over the
// factory catalog alone (no scanned plug-ins, no presets folder).

using Nota.Application;

namespace Nota.SmokeTest;

internal sealed class EmptyPluginCatalog : IPluginCatalog
{
    public int Scan(string workerPath) => 0;
    public int Count => 0;
    public string? Description(int index) => null;
    public string? Id(int index) => null;
    public int IndexOfId(string identifier) => -1;
    public void AddScanPath(string dir) { }
    public void RemoveScanPath(int index) { }
    public int ScanPathCount => 0;
    public string? ScanPath(int index) => null;
}

internal sealed class EmptyPresetLibrary : IPresetLibrary
{
    public IReadOnlyList<PresetInfo> List(string folder) => Array.Empty<PresetInfo>();
}
