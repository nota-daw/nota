// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Plugin rescans shared by Preferences (Rescan, Get Plug-ins) and the open-project
// "install missing plugins" offer. The scan runs out-of-process in nota-scanworker,
// shipped beside the app; the native catalog tolerates a scan off the UI thread.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

internal static class PluginScan
{
    /// <summary>The bundled scan worker, or null when it isn't beside the app.</summary>
    public static string? WorkerPath
    {
        get
        {
            var name = OperatingSystem.IsWindows() ? "nota-scanworker.exe" : "nota-scanworker";
            var path = Path.Combine(AppContext.BaseDirectory, name);
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>Makes the Get Plug-ins folder a scan path (once; persisted by the catalog).</summary>
    public static void EnsureStoreScanPath(IPluginCatalog catalog, IPluginStore store)
    {
        for (int i = 0; i < catalog.ScanPathCount; i++)
            if (string.Equals(catalog.ScanPath(i), store.PluginsDir, StringComparison.Ordinal)) return;
        catalog.AddScanPath(store.PluginsDir);
    }

    /// <summary>Rescans off the UI thread, then rebuilds the browser lists. Returns the number
    /// of known plugins, or null when the scan worker is missing.</summary>
    public static async Task<int?> RescanAsync(MainWindowViewModel main)
    {
        if (WorkerPath is not { } worker) return null;
        var catalog = App.Services.GetRequiredService<IPluginCatalog>();
        int n = await Task.Run(() => catalog.Scan(worker));
        main.Browser.Rebuild();
        return n;
    }
}
