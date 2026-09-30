// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Get Plug-ins (IPluginStore): registry index parsing + platform choice, identifier
// matching for missing plugins, and install/uninstall end to end against a local index
// with generated zip / tar.gz / .deb assets — checksum and path-escape refusals included.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --store`.
// Opt-in live check against real releases: `-- --store-live <index.json> <id> [<id> …]`
// (downloads from GitHub; with a scan worker beside the test, loads each bundle too).

using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class PluginStoreTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        // ---- platform keys --------------------------------------------------------------
        yield return (RegistryIndex.PlatformKeys(OSPlatform.OSX, Architecture.Arm64).SequenceEqual(["macos-arm64", "macos-universal"]),
            "Apple silicon installs arm64 builds, else universal");
        yield return (RegistryIndex.PlatformKeys(OSPlatform.OSX, Architecture.X64).SequenceEqual(["macos-x64", "macos-universal"]),
            "Intel Macs install x64 builds, else universal");
        yield return (RegistryIndex.PlatformKeys(OSPlatform.Windows, Architecture.X64).SequenceEqual(["windows-x64"]),
            "Windows x64 installs only windows-x64 builds");
        yield return (RegistryIndex.PlatformKeys(OSPlatform.Linux, Architecture.Arm64).SequenceEqual(["linux-arm64"]),
            "Linux arm64 never takes an x64 build");

        // ---- identifiers ------------------------------------------------------------------
        yield return (RegistryIndex.ParseIdentifier("VST3-Surge XT-1a2b3c-4d5e6f") == ("VST3", "Surge XT"),
            "a catalog id splits into format and name");
        yield return (RegistryIndex.ParseIdentifier("VST3-GATE-12-ff00-1234") == ("VST3", "GATE-12"),
            "…keeping dashes inside the plugin name");
        yield return (RegistryIndex.ParseIdentifier("nonsense") is null, "…and rejects anything else");

        // ---- index parsing ----------------------------------------------------------------
        var sample = IndexJson(Plugin("dual", "Dual", "effect", "1.0",
            ("macos-universal", "https://github.com/a/b/releases/download/v1/dual-mac.dmg", new string('a', 64), 10, null, ["Dual.vst3"], "Dual.pkg"),
            ("linux-x64", "https://github.com/a/b/releases/download/v1/dual-linux.tar.xz", new string('b', 64), 20, null, ["x/Dual.vst3"], null)));
        var linux = RegistryIndex.Parse(sample, ["linux-x64"]).Single();
        yield return (linux.Asset is { Platform: "linux-x64", Archive: "tar", Size: 20, Inner: null } a && a.Bundles.SequenceEqual(["x/Dual.vst3"]),
            "the index yields the running platform's asset, archive type from the URL");
        var mac = RegistryIndex.Parse(sample, ["macos-arm64", "macos-universal"]).Single();
        yield return (mac.Asset is { Platform: "macos-universal", Archive: "dmg", Inner: "Dual.pkg" },
            "…falling back along the platform keys (arm64 → universal), nested archive kept");
        yield return (RegistryIndex.Parse(sample, ["windows-x64"]).Single().Asset is null,
            "a plugin without a build for this platform stays listed, with no asset");
        var broken = sample.Replace("\"plugins\": [", "\"plugins\": [{\"id\": \"half\"},");
        yield return (RegistryIndex.Parse(broken, ["linux-x64"]).Count == 1, "a malformed entry is skipped, not fatal");
        bool refused = false;
        try { RegistryIndex.Parse(sample.Replace("\"schema\": 1", "\"schema\": 99"), ["linux-x64"]); }
        catch (PluginStoreException) { refused = true; }
        yield return (refused, "a newer index schema asks for a newer Nota");

        // ---- install / uninstall end to end ------------------------------------------------
        var tmp = Directory.CreateTempSubdirectory("nota-store-test-").FullName;
        try
        {
            foreach (var r in InstallFlow(tmp)) yield return r;
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    private static IEnumerable<(bool, string)> InstallFlow(string tmp)
    {
        const string plat = "test-platform";
        var assets = Path.Combine(tmp, "assets");
        Directory.CreateDirectory(assets);

        // zip: a bundle two folders deep, plus a Windows-style entry with backslashes.
        var zip = Path.Combine(assets, "synth.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Entry(z, "synth-1.0/VST3/Fake Synth.vst3/Contents/Resources/moduleinfo.json", "{}");
            Entry(z, "synth-1.0/VST3/Fake Synth.vst3/Contents/x86_64-linux/Fake Synth.so", "binary");
            Entry(z, "synth-1.0\\VST3\\Fake FX.vst3\\Contents\\x86_64-win\\Fake FX.vst3", "dll");
            Entry(z, "synth-1.0/README.txt", "not a bundle");
        }
        // tar.gz and .deb (ar + data.tar.gz) with the bundle under usr/lib/vst3.
        var tgz = Path.Combine(assets, "verb.tar.gz");
        WriteTarGz(tgz, ("verb/Fake Verb.vst3/Contents/Resources/info.txt", "verb"));
        var deb = Path.Combine(assets, "delay.deb");
        var data = Path.Combine(tmp, "data.tar.gz");
        WriteTarGz(data, ("usr/lib/vst3/Fake Delay.vst3/Contents/info.txt", "delay"));
        WriteDeb(deb, File.ReadAllBytes(data));
        // zip-slip: an entry that climbs out of the extraction folder.
        var evil = Path.Combine(assets, "evil.zip");
        using (var z = ZipFile.Open(evil, ZipArchiveMode.Create)) { Entry(z, "../../escaped.txt", "x"); Entry(z, "Evil.vst3/x", "x"); }

        var index = Path.Combine(tmp, "index.json");
        void WriteIndex(string synthSha, string synthVersion) => File.WriteAllText(index, IndexJson(
            Plugin("fake-synth", "Fake Synth", "instrument", synthVersion,
                (plat, zip, synthSha, new FileInfo(zip).Length, null,
                    ["synth-1.0/VST3/Fake Synth.vst3", "synth-1.0/VST3/Fake FX.vst3"], null),
                provides: ["Fake Synth", "Fake FX"]),
            Plugin("fake-verb", "Fake Verb", "effect", "2.0", (plat, tgz, Sha(tgz), new FileInfo(tgz).Length, null, ["verb/Fake Verb.vst3"], null)),
            Plugin("fake-delay", "Fake Delay", "effect", "3.0", (plat, deb, Sha(deb), new FileInfo(deb).Length, null, ["usr/lib/vst3/Fake Delay.vst3"], null)),
            Plugin("fake-evil", "Fake Evil", "effect", "1.0", (plat, evil, Sha(evil), new FileInfo(evil).Length, null, ["Evil.vst3"], null)),
            Plugin("fake-escape", "Fake Escape", "effect", "1.0", (plat, zip, Sha(zip), new FileInfo(zip).Length, null, ["../../../etc.vst3"], null))));
        WriteIndex(Sha(zip), "1.0");

        var root = Path.Combine(tmp, "store");
        var store = new PluginStore(root, index, [plat]);
        var plugins = store.FetchAsync().GetAwaiter().GetResult();
        yield return (plugins.Count == 5 && plugins.All(p => p.Asset is not null), "a local index lists its plugins");
        StorePlugin P(string id) => store.FetchAsync().GetAwaiter().GetResult().Single(p => p.Id == id);

        var reports = new List<StoreProgress>();
        store.InstallAsync(P("fake-synth"), new SyncProgress(reports.Add)).GetAwaiter().GetResult();
        var dir = Path.Combine(store.PluginsDir, "fake-synth");
        yield return (File.Exists(Path.Combine(dir, "Fake Synth.vst3", "Contents", "x86_64-linux", "Fake Synth.so")),
            "a zip installs its bundle into the plugin's own folder");
        yield return (File.Exists(Path.Combine(dir, "Fake FX.vst3", "Contents", "x86_64-win", "Fake FX.vst3")),
            "…backslash-separated zip entries unpack as folders");
        yield return (!File.Exists(Path.Combine(dir, "README.txt")) && Directory.GetFileSystemEntries(dir).Length == 2,
            "…and only the listed bundles are copied");
        yield return (reports.Any(r => r.Fraction >= 0.99) && reports.Any(r => r.Message.StartsWith("Unpacking")),
            "install reports download progress, then the unpack stage");
        yield return (store.Installed.SingleOrDefault(i => i.Id == "fake-synth") is { Version: "1.0" } inst
                      && inst.Bundles.SequenceEqual(["Fake Synth.vst3", "Fake FX.vst3"]),
            "the install is recorded with its version and bundles");
        yield return (new PluginStore(root, index, [plat]).Installed.Any(i => i.Id == "fake-synth"),
            "…and survives a restart (installed.json)");

        store.InstallAsync(P("fake-verb")).GetAwaiter().GetResult();
        yield return (File.Exists(Path.Combine(store.PluginsDir, "fake-verb", "Fake Verb.vst3", "Contents", "Resources", "info.txt")),
            "a tar.gz installs through the system tar");
        store.InstallAsync(P("fake-delay")).GetAwaiter().GetResult();
        yield return (File.Exists(Path.Combine(store.PluginsDir, "fake-delay", "Fake Delay.vst3", "Contents", "info.txt")),
            "a .deb's data.tar installs");

        yield return (store.FindProvider("VST3-Fake FX-9c1d-77aa")?.Id == "fake-synth", "a missing plugin's id finds the registry entry that provides it");
        yield return (store.FindProvider("AudioUnit-Fake FX-9c1d-77aa") is null && store.FindProvider("VST3-Unknown-1-2") is null,
            "…only for VST3 names the registry lists");

        yield return (Throws(() => store.InstallAsync(P("fake-evil")).GetAwaiter().GetResult())
                      && !File.Exists(Path.Combine(Path.GetTempPath(), "escaped.txt")) && !Directory.Exists(Path.Combine(store.PluginsDir, "fake-evil")),
            "an archive entry escaping the unpack folder is refused, nothing installed");
        yield return (Throws(() => store.InstallAsync(P("fake-escape")).GetAwaiter().GetResult())
                      && !Directory.Exists(Path.Combine(store.PluginsDir, "fake-escape")),
            "a bundle path escaping the archive is refused");

        // Update with a checksum that doesn't match: refused, the installed version stays intact.
        WriteIndex(new string('0', 64), "1.1");
        yield return (Throws(() => store.InstallAsync(P("fake-synth")).GetAwaiter().GetResult())
                      && store.Installed.Single(i => i.Id == "fake-synth").Version == "1.0"
                      && File.Exists(Path.Combine(dir, "Fake Synth.vst3", "Contents", "x86_64-linux", "Fake Synth.so")),
            "a checksum mismatch aborts the update and keeps the installed version");
        WriteIndex(Sha(zip), "1.1");
        store.InstallAsync(P("fake-synth")).GetAwaiter().GetResult();
        yield return (store.Installed.Single(i => i.Id == "fake-synth").Version == "1.1" && Directory.GetFileSystemEntries(dir).Length == 2
                      && !Directory.EnumerateDirectories(store.PluginsDir, ".staging-*").Any(),
            "an update replaces the plugin's folder atomically");

        store.Uninstall("fake-synth");
        yield return (!Directory.Exists(dir) && store.Installed.All(i => i.Id != "fake-synth"), "uninstall removes the files and the record");
        yield return (Throws(() => store.Uninstall("../x")), "uninstall refuses ids that aren't registry ids");

        // Offline: an unreachable registry falls back to the last cached index.
        var cache = Path.Combine(tmp, "cache-root");
        Directory.CreateDirectory(cache);
        File.Copy(index, Path.Combine(cache, "registry-index.json"));
        File.SetLastWriteTimeUtc(Path.Combine(cache, "registry-index.json"), DateTime.UtcNow.AddDays(-3));
        var offline = new PluginStore(cache, "http://127.0.0.1:9/index.json", [plat]);
        yield return (offline.FetchAsync().GetAwaiter().GetResult().Count == 5, "offline, the stale cached index is used");
        var empty = new PluginStore(Path.Combine(tmp, "no-cache"), "http://127.0.0.1:9/index.json", [plat]);
        yield return (Throws(() => empty.FetchAsync().GetAwaiter().GetResult()), "offline with no cache is a clear error");
    }

    /// <summary>Installs real registry plugins into a temp store; with a scan worker, checks each bundle loads.</summary>
    public static IEnumerable<(bool Ok, string Label)> RunLive(string index, IReadOnlyList<string> ids)
    {
        var root = Directory.CreateTempSubdirectory("nota-store-live-").FullName;
        var store = new PluginStore(root, index);
        var plugins = store.FetchAsync().GetAwaiter().GetResult();
        var worker = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "nota-scanworker.exe" : "nota-scanworker");
        if (!File.Exists(worker))
            worker = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/native/nota.engine/build/nota-scanworker"));
        foreach (var id in ids)
        {
            var p = plugins.SingleOrDefault(x => x.Id == id);
            if (p?.Asset is null) { yield return (false, $"{id}: in the index with an asset for this platform"); continue; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string? err = null;
            try { store.InstallAsync(p).GetAwaiter().GetResult(); }
            catch (PluginStoreException e) { err = e.Message; }
            yield return (err is null, $"{id} {p.Version} ({p.Asset.Platform}, {p.Asset.Archive}{(p.Asset.Inner is null ? "" : " → pkg")}) installs in {sw.Elapsed.TotalSeconds:0.0} s {err}");
            if (err is not null || !File.Exists(worker)) continue;
            foreach (var bundle in Directory.GetFileSystemEntries(Path.Combine(store.PluginsDir, id)))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(worker) { RedirectStandardOutput = true };
                psi.ArgumentList.Add("VST3");
                psi.ArgumentList.Add(bundle);
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                var names = System.Text.RegularExpressions.Regex.Matches(output, "<PLUGIN name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
                yield return (names.Count > 0 && names.All(n => p.Provides.Contains(n)),
                    $"  {Path.GetFileName(bundle)} loads as [{string.Join(", ", names)}], listed in provides");
            }
        }
        Console.WriteLine($"  (installed under {root})");
    }

    /// <summary>Catalog behaviour the store relies on, against a real VST3 bundle: a plugin found at a
    /// new path still resolves by its old identifier (path hash ignored), and a bundle deleted from
    /// disk drops out of the catalog on the next scan. Rescans the catalog, so it refuses to run
    /// unless NOTA_DATA_DIR points at a scratch folder (where the catalog + scan paths then live).</summary>
    public static IEnumerable<(bool Ok, string Label)> RunCatalog(string bundle, string worker)
    {
        // The catalog and its scan paths must live in a scratch data dir (NOTA_DATA_DIR), never the user's.
        var home = Environment.GetEnvironmentVariable("NOTA_DATA_DIR") ?? "";
        if (home.Length == 0 || !Path.GetFullPath(home).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal)
                                && !home.StartsWith("/tmp/", StringComparison.Ordinal))
        {
            yield return (false, "run with NOTA_DATA_DIR=<scratch dir under /tmp> — this rescans the plugin catalog");
            yield break;
        }
        var a = Path.Combine(home, "plugins-a");
        var b = Path.Combine(home, "plugins-b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        var name = Path.GetFileName(bundle);
        ArchiveUnpacker.CopyBundle(bundle, Path.Combine(a, name));

        List<string> Ids() => Enumerable.Range(0, NotaEngine.PluginCount).Select(i => NotaEngine.PluginId(i)!).ToList();
        NotaEngine.ScanPlugins(worker);          // baseline: whatever the system folders hold
        var baseline = Ids();
        NotaEngine.AddScanPath(a);
        NotaEngine.ScanPlugins(worker);
        var idsA = Ids();
        var added = idsA.Except(baseline).ToList();
        yield return (added.Count > 0, $"the bundle scans in ({string.Join(", ", added)})");
        if (added.Count == 0) yield break;
        var oldId = added[0];

        // "Another machine": same bundle, different folder → different path hash.
        Directory.Move(Path.Combine(a, name), Path.Combine(b, name));
        NotaEngine.RemoveScanPath(0);
        NotaEngine.AddScanPath(b);
        NotaEngine.ScanPlugins(worker);
        var idsB = Ids();
        yield return (!idsB.Contains(oldId) && idsB.Count == idsA.Count,
            "a bundle gone from disk drops out of the catalog on rescan");
        int idx = NotaEngine.PluginIndexOfId(oldId);
        yield return (idx >= 0 && idsB[idx] != oldId,
            $"the old identifier still resolves to the moved plugin ({(idx >= 0 ? idsB[idx] : "none")})");
        yield return (NotaEngine.PluginIndexOfId(oldId[..oldId.LastIndexOf('-')] + "-deadbeef") < 0,
            "…but not with a different plugin uid");
    }

    // ---- helpers ----------------------------------------------------------------------------

    private sealed class SyncProgress(Action<StoreProgress> report) : IProgress<StoreProgress>
    {
        public void Report(StoreProgress value) => report(value);
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (PluginStoreException) { return true; }
    }

    private static string Sha(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Entry(ZipArchive z, string name, string text)
    {
        using var s = z.CreateEntry(name).Open();
        s.Write(Encoding.UTF8.GetBytes(text));
    }

    private static void WriteTarGz(string path, params (string Name, string Text)[] files)
    {
        using var fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        using var tar = new TarWriter(gz, TarEntryFormat.Pax);
        foreach (var (name, text) in files)
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) });
    }

    private static void WriteDeb(string path, byte[] dataTarGz)
    {
        using var f = File.Create(path);
        f.Write("!<arch>\n"u8);
        void Member(string name, byte[] body)
        {
            var header = $"{name,-16}{"0",-12}{"0",-6}{"0",-6}{"100644",-8}{body.Length,-10}`\n";
            f.Write(Encoding.ASCII.GetBytes(header));
            f.Write(body);
            if (body.Length % 2 == 1) f.WriteByte((byte)'\n');
        }
        Member("debian-binary", "2.0\n"u8.ToArray());
        Member("control.tar.gz", [0x1f]);   // odd length exercises the ar padding
        Member("data.tar.gz", dataTarGz);
    }

    private static string IndexJson(params string[] plugins)
        => $"{{\n\"schema\": 1,\n\"generated\": \"2026-01-01T00:00:00Z\",\n\"plugins\": [{string.Join(",", plugins)}]\n}}";

    private static string Plugin(string id, string name, string kind, string version,
        (string Platform, string Url, string Sha, long Size, string? Archive, string[] Bundles, string? Inner) asset,
        string[]? provides = null)
        => Plugin(id, name, kind, version, [asset], provides);

    private static string Plugin(string id, string name, string kind, string version,
        (string Platform, string Url, string Sha, long Size, string? Archive, string[] Bundles, string? Inner) a1,
        (string Platform, string Url, string Sha, long Size, string? Archive, string[] Bundles, string? Inner) a2)
        => Plugin(id, name, kind, version, [a1, a2], null);

    private static string Plugin(string id, string name, string kind, string version,
        (string Platform, string Url, string Sha, long Size, string? Archive, string[] Bundles, string? Inner)[] assets,
        string[]? provides)
    {
        var assetObj = assets.ToDictionary(a => a.Platform, a => new Dictionary<string, object?>
        {
            ["url"] = a.Url, ["sha256"] = a.Sha, ["size"] = a.Size, ["bundles"] = a.Bundles,
            ["archive"] = a.Archive, ["inner"] = a.Inner,
        }.Where(kv => kv.Value is not null).ToDictionary());
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = id, ["name"] = name, ["developer"] = "Test", ["description"] = $"{name} for tests.",
            ["repo"] = "https://github.com/nota-daw/test", ["license"] = "MIT", ["kind"] = kind,
            ["provides"] = (provides ?? [name]).Select(n => new { format = "VST3", name = n }),
            ["versions"] = new[] { new { version, assets = assetObj } },
        });
    }
}
