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
    /// of known plugins, or null when the scan worker is missing. <paramref name="onlyDir"/>
    /// limits the search to one folder (a store install); <paramref name="retryFailed"/>
    /// probes plugins that failed before again (an explicit Rescan). While the scan runs,
    /// <paramref name="progress"/> gets its state a few times a second on the calling thread.</summary>
    public static async Task<int?> RescanAsync(MainWindowViewModel main, IProgress<PluginScanProgress>? progress = null,
        string? onlyDir = null, bool retryFailed = false)
    {
        if (WorkerPath is not { } worker) return null;
        var catalog = App.Services.GetRequiredService<IPluginCatalog>();
        var scan = Task.Run(() => catalog.Scan(worker, onlyDir, retryFailed));
        if (progress is not null)
        {
            // A first scan of a large plugin folder takes minutes: keep showing that it moves.
            while (!scan.IsCompleted)
            {
                await Task.WhenAny(scan, Task.Delay(250));
                if (catalog.ScanProgress is { } p) progress.Report(p);
            }
        }
        int n = await scan;
        main.Browser.Rebuild();
        return n;
    }

    /// <summary>Status line for a running scan, e.g. "Scanning plugins · 312 of 1,000 · Analytiks".</summary>
    public static string Describe(PluginScanProgress p)
    {
        if (p.Total == 0) return "Looking for plugins…";
        var line = $"Scanning plugins · {p.Done:N0} of {p.Total:N0}";
        return p.Current.Length > 0 ? $"{line} · {p.Current}" : line;
    }

    /// <summary>Share of the scan done, or -1 while the total isn't known yet.</summary>
    public static double Fraction(PluginScanProgress p) => p.Total > 0 ? (double)p.Done / p.Total : -1;
}
