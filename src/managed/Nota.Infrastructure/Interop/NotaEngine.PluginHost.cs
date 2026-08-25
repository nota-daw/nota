// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- Plugin hosting spike (M3-0) ---------------------------------------

    /// <summary>
    /// Opens a JUCE test window to verify the JUCE message loop coexists with
    /// the Avalonia run loop (M3-0 spike). Must be called from the UI thread.
    /// </summary>
    public static void OpenPluginHostTestWindow() => Check(NativeMethods.PluginHostOpenTestWindow());

    // --- Plugin scanning & catalog (M3-1) ----------------------------------

    /// <summary>
    /// Scans installed AU/VST3 plugins out-of-process using the given worker
    /// executable. Blocking. Returns the number of known plugins.
    /// </summary>
    public static int ScanPlugins(string workerPath)
    {
        int n = NativeMethods.PluginHostScan(workerPath);
        if (n < 0) throw new NotaEngineException($"Plugin scan failed (code {n}).");
        return n;
    }

    /// <summary>Number of plugins currently in the catalog.</summary>
    public static int PluginCount => NativeMethods.PluginHostPluginCount();

    /// <summary>Human-readable catalog entry ("Name | Format | inst|fx | Manufacturer"), or null.</summary>
    public static string? PluginDescription(int index)
        => Marshal.PtrToStringUTF8(NativeMethods.PluginHostPluginDesc(index));

    /// <summary>Stable identifier for a catalog entry (for project save), or null (M7-6c).</summary>
    public static string? PluginId(int index)
        => Marshal.PtrToStringUTF8(NativeMethods.PluginHostPluginId(index));

    /// <summary>Catalog index whose identifier matches, or -1 if the plugin isn't installed.</summary>
    public static int PluginIndexOfId(string identifier)
        => NativeMethods.PluginHostIndexOfId(identifier);

    /// <summary>Adds an extra plugin search directory (persisted; used on the next scan).</summary>
    public static void AddScanPath(string dir) => Check(NativeMethods.PluginHostAddScanPath(dir));

    /// <summary>Removes the extra scan directory at <paramref name="index"/>.</summary>
    public static void RemoveScanPath(int index) => Check(NativeMethods.PluginHostRemoveScanPath(index));

    /// <summary>Number of user-added scan directories.</summary>
    public static int ScanPathCount => NativeMethods.PluginHostScanPathCount();

    /// <summary>The extra scan directory at <paramref name="index"/>, or null.</summary>
    public static string? ScanPath(int index)
        => Marshal.PtrToStringUTF8(NativeMethods.PluginHostScanPath(index));
}
