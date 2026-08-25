// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Plugin hosting: scan worker, catalog, scan paths (M3-0/M3-1).</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_pluginhost_open_test_window")]
    internal static partial NotaResult PluginHostOpenTestWindow();

    [LibraryImport(Lib, EntryPoint = "nota_pluginhost_scan", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int PluginHostScan(string workerPath);

    [LibraryImport(Lib, EntryPoint = "nota_pluginhost_plugin_count")]
    internal static partial int PluginHostPluginCount();

    [LibraryImport(Lib, EntryPoint = "nota_pluginhost_plugin_desc")]
    internal static partial IntPtr PluginHostPluginDesc(int index);

    [LibraryImport(Lib, EntryPoint = "nota_pluginhost_plugin_id")]
    internal static partial IntPtr PluginHostPluginId(int index);

    [LibraryImport(Lib, EntryPoint = "nota_pluginhost_index_of_id", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int PluginHostIndexOfId(string identifier);

    [LibraryImport(Lib, EntryPoint = "nota_pluginhost_add_scan_path", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial NotaResult PluginHostAddScanPath(string dir);

    [LibraryImport(Lib, EntryPoint = "nota_pluginhost_remove_scan_path")]
    internal static partial NotaResult PluginHostRemoveScanPath(int index);

    [LibraryImport(Lib, EntryPoint = "nota_pluginhost_scan_path_count")]
    internal static partial int PluginHostScanPathCount();

    [LibraryImport(Lib, EntryPoint = "nota_pluginhost_scan_path")]
    internal static partial IntPtr PluginHostScanPath(int index);
}
