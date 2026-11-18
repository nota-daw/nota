// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Default plugin scan paths (nota-daw/nota#622), against the real engine:
// - Contract: VST3 OS defaults are absolute and unique, unknown formats report none.
// - Fresh profile: an empty data dir is seeded with the OS defaults on first load.
// - Roundtrip: removing a default and restoring it keeps user folders, no dupes.
// - Upgrade: a pre-feature list gains the defaults once, order kept; removals stay
//   removed until restored (FR-011).
// - Scan identity: listing changes never alter what the scan finds (FR-008) — each
//   scan runs from scratch in a fresh child process.
// Opt-in, since it mutates the catalog: NOTA_DATA_DIR must point at scratch space
// under /tmp (rerun-stable), like RunScanScale/RunCatalog:
//   dotnet Nota.SmokeTest.dll --default-scan-paths
//   dotnet Nota.SmokeTest.dll --default-scan-paths-upgrade
//   dotnet Nota.SmokeTest.dll --default-scan-paths-identity <nota-scanworker>
// (--default-scan-paths-scan <worker> <idsFile> is the child side of the identity
// check, not for direct use.)

using System.Diagnostics;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class DefaultScanPathsTests
{
    // ---- T010 contract (mutates nothing) -------------------------------------------

    public static IEnumerable<(bool Ok, string Label)> RunContract()
    {
        int n = NotaEngine.DefaultScanPathCount("VST3");
        yield return (n >= 1, $"VST3 reports {n} OS default scan folder(s)");
        var paths = Enumerable.Range(0, n).Select(i => NotaEngine.DefaultScanPath("VST3", i)).ToList();
        yield return (paths.All(p => p is { Length: > 0 } && Path.IsPathFullyQualified(p)),
            "…all absolute paths");
        yield return (paths.Distinct(StringComparer.Ordinal).Count() == paths.Count,
            "…with no duplicates");
        yield return (NotaEngine.DefaultScanPath("VST3", -1) is null && NotaEngine.DefaultScanPath("VST3", n) is null,
            "…and nothing out of range");
        yield return (NotaEngine.DefaultScanPathCount("CLAP") == 0
                      && NotaEngine.DefaultScanPath("CLAP", 0) is null
                      && NotaEngine.RestoreDefaultScanPaths("CLAP") == 0
                      && NotaEngine.DefaultScanPathCount(null!) == 0,
            "an unknown format (CLAP today) reports no defaults");
    }

    // ---- T011 + T014: seed state, list-level roundtrip (no scans) -----------------------

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var home = RequireScratch();
        if (home is null)
        {
            yield return (false, "run with NOTA_DATA_DIR=<scratch dir under /tmp> — this reseeds the plugin catalog");
            yield break;
        }
        foreach (var r in RunContract()) yield return r;

        // Normalize to exactly the OS defaults (a fresh dir is seeded by the first
        // touch below; a reused scratch dir is reset) — rerun-stable either way.
        var osDefaults = OsDefaults();
        foreach (var at in AllScanPaths()
                     .Select((p, i) => (p, i))
                     .Where(t => !osDefaults.Contains(t.p))
                     .Select(t => t.i)
                     .OrderByDescending(i => i))
            NotaEngine.RemoveScanPath(at);
        var seeded = AllScanPaths();
        yield return (osDefaults.Count > 0 && seeded.Count == osDefaults.Count && osDefaults.All(seeded.Contains),
            $"the list starts from exactly the OS defaults ({seeded.Count} folder(s))");

        // T014: remove one default, add a custom folder, restore.
        var victim = osDefaults[0];
        NotaEngine.RemoveScanPath(seeded.IndexOf(victim));
        var custom = Path.Combine(home, "my-vst3");
        Directory.CreateDirectory(custom);
        if (AllScanPaths().Contains(custom))
            NotaEngine.RemoveScanPath(AllScanPaths().IndexOf(custom));
        NotaEngine.AddScanPath(custom);
        var afterRemove = AllScanPaths();
        yield return (!afterRemove.Contains(victim) && afterRemove.Contains(custom),
            "a default can be removed while a custom folder stays");
        int added = NotaEngine.RestoreDefaultScanPaths("VST3");
        var afterRestore = AllScanPaths();
        yield return (added == 1 && afterRestore.Contains(victim) && afterRestore.Contains(custom)
                      && afterRestore.Distinct(StringComparer.Ordinal).Count() == afterRestore.Count,
            $"restore re-adds just the missing default ({added}) without touching user folders");
        yield return (NotaEngine.RestoreDefaultScanPaths("VST3") == 0,
            "a second restore is a no-op");
    }

    // ---- T017/T030: upgrade a pre-feature list; removals stay removed ------------------

    public static IEnumerable<(bool Ok, string Label)> RunUpgrade()
    {
        var home = RequireScratch();
        if (home is null)
        {
            yield return (false, "run with NOTA_DATA_DIR=<scratch dir under /tmp> — this reseeds the plugin catalog");
            yield break;
        }
        var customA = Path.Combine(home, "plugins-a");
        var customB = Path.Combine(home, "plugins-b");
        Directory.CreateDirectory(customA);
        Directory.CreateDirectory(customB);
        // A pre-feature list: custom folders, no defaults, store folder last.
        // Rewritten on every run, so reruns stay deterministic.
        var store = Path.Combine(home, "store-VST3");
        Directory.CreateDirectory(store);
        File.WriteAllLines(Path.Combine(home, "scanpaths.txt"), [customA, customB, store]);
        // The user removed one OS default before upgrading: it must stay gone.
        var osDefaults = OsDefaults();
        var blocked = osDefaults[0];
        File.WriteAllLines(Path.Combine(home, "scanpaths-removed.txt"), [blocked]);

        // First touch backfills the missing OS defaults once, order kept —
        // except the remembered removal.
        var list = AllScanPaths();
        var restorable = osDefaults.Where(d => d != blocked).ToList();
        yield return (list.Count >= 3 && list[0] == customA && list[1] == customB && list[2] == store
                      && !list.Contains(blocked) && restorable.All(list.Contains)
                      && list.Distinct(StringComparer.Ordinal).Count() == list.Count,
            $"upgrade appends the missing defaults once, customs first, removals stay removed ({list.Count} folders)");
        var fileLines = File.ReadAllLines(Path.Combine(home, "scanpaths.txt"));
        yield return (fileLines.Distinct(StringComparer.Ordinal).Count() == fileLines.Length,
            "…persisted without duplicates, so a relaunch is stable");

        // Restore brings the removed default back and clears the block.
        int restored = NotaEngine.RestoreDefaultScanPaths("VST3");
        var afterRestore = AllScanPaths();
        var removedFile = Path.Combine(home, "scanpaths-removed.txt");
        var stillBlocked = File.Exists(removedFile) ? File.ReadAllLines(removedFile) : [];
        yield return (restored == 1 && afterRestore.Contains(blocked) && !stillBlocked.Contains(blocked),
            "restore re-adds the removed default and clears its block");
    }

    // ---- T031: scan identity across list changes, each scan from scratch ---------------
    // In-memory catalog state is per-process, so every scan runs in a FRESH child
    // process with plugins.xml deleted first: a scan that stopped discovering a
    // folder could otherwise still return its saved ids and pass vacuously.

    public static IEnumerable<(bool Ok, string Label)> RunIdentity(string worker)
    {
        var home = RequireScratch();
        if (home is null)
        {
            yield return (false, "run with NOTA_DATA_DIR=<scratch dir under /tmp> — this reseeds the plugin catalog");
            yield break;
        }
        var customA = Path.Combine(home, "plugins-a");
        var customB = Path.Combine(home, "plugins-b");
        Directory.CreateDirectory(customA);
        Directory.CreateDirectory(customB);
        var store = Path.Combine(home, "store-VST3");
        Directory.CreateDirectory(store);
        foreach (var f in new[] { "scanpaths.txt", "scanpaths-removed.txt", "plugins.xml", "scan-failed.txt" })
            try { File.Delete(Path.Combine(home, f)); } catch { }
        File.WriteAllLines(Path.Combine(home, "scanpaths.txt"), [customA, customB, store]);

        // First touch backfills the defaults; the children below see this list.
        var osDefaults = OsDefaults();
        var list = AllScanPaths();
        yield return (osDefaults.Count > 0 && osDefaults.All(list.Contains),
            $"identity starts from the full list ({list.Count} folders)");

        var ids1 = ChildScan(home, worker, "seeded");
        yield return (ids1 is not null, $"from-scratch scan finds the set ({ids1?.Count ?? -1} plugins)");
        if (ids1 is null) yield break;

        foreach (var d in osDefaults)
        {
            int at = AllScanPaths().IndexOf(d);
            if (at >= 0) NotaEngine.RemoveScanPath(at);
        }
        var ids2 = ChildScan(home, worker, "nodefaults");
        yield return (ids2 is not null && new HashSet<string>(ids2).SetEquals(ids1),
            $"removing every default changes nothing found ({ids1.Count} → {ids2?.Count ?? -1} plugins)");

        int restored = NotaEngine.RestoreDefaultScanPaths("VST3");
        var ids3 = ChildScan(home, worker, "restored");
        yield return (restored == osDefaults.Count && ids3 is not null && new HashSet<string>(ids3).SetEquals(ids1),
            $"…and restoring them ({restored}) still finds the same set");
    }

    /// <summary>Child side of the identity check: forget everything, scan from scratch,
    /// write the catalog ids. Exit code 0 means the ids file is written.</summary>
    public static int RunChildScan(string worker, string idsFile)
    {
        var home = Environment.GetEnvironmentVariable("NOTA_DATA_DIR") ?? "";
        foreach (var f in new[] { "plugins.xml", "scan-failed.txt" })
            try { File.Delete(Path.Combine(home, f)); } catch { return 2; }
        int n;
        try { n = NotaEngine.ScanPlugins(worker); }
        catch { return 3; }
        try { File.WriteAllLines(idsFile, Enumerable.Range(0, n).Select(i => NotaEngine.PluginId(i)!)); }
        catch { return 4; }
        return 0;
    }

    private static List<string>? ChildScan(string home, string worker, string tag)
    {
        var idsFile = Path.Combine(home, $"identity-{tag}.txt");
        try { if (File.Exists(idsFile)) File.Delete(idsFile); } catch { return null; }
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        start.ArgumentList.Add(typeof(DefaultScanPathsTests).Assembly.Location);
        start.ArgumentList.Add("--default-scan-paths-scan");
        start.ArgumentList.Add(worker);
        start.ArgumentList.Add(idsFile);
        // A hung native scan in the child must fail this check, not stall the suite.
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(TimeSpan.FromMinutes(10)))
        {
            try { child.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { } // already exited between the timeout and the kill
            stdout.GetAwaiter().GetResult();
            stderr.GetAwaiter().GetResult();
            return null;
        }
        Console.Write(stdout.GetAwaiter().GetResult());
        if (child.ExitCode != 0) Console.Write(stderr.GetAwaiter().GetResult());
        return child.ExitCode == 0 && File.Exists(idsFile) ? File.ReadAllLines(idsFile).ToList() : null;
    }

    // ---- helpers ---------------------------------------------------------------------

    private static string? RequireScratch()
    {
        var home = Environment.GetEnvironmentVariable("NOTA_DATA_DIR") ?? "";
        if (home.Length == 0 || !Path.GetFullPath(home).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal)
                                && !home.StartsWith("/tmp/", StringComparison.Ordinal))
            return null;
        return home;
    }

    private static List<string> AllScanPaths()
        => Enumerable.Range(0, NotaEngine.ScanPathCount).Select(i => NotaEngine.ScanPath(i)!).ToList();

    private static List<string> OsDefaults()
        => Enumerable.Range(0, NotaEngine.DefaultScanPathCount("VST3"))
            .Select(i => NotaEngine.DefaultScanPath("VST3", i)!).ToList();
}
