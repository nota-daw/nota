// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Get Samples (ISampleStore): sample registry index parsing, which files of a pack get
// installed, and install / update / uninstall end to end against a local index with generated
// zip / tar.gz packs — checksum, path-escape, free-space and folder-name collisions included.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --samples`.

using System.IO.Compression;
using System.Text.Json;
using Nota.Application;
using Nota.Infrastructure;
using static Nota.SmokeTest.PluginStoreTests;

namespace Nota.SmokeTest;

internal static class SampleStoreTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        // ---- which files install ----------------------------------------------------------
        yield return (SampleIndex.IsInstalled("Kick 01.WAV") && SampleIndex.IsInstalled("pad.flac") && SampleIndex.IsInstalled("Piano.sfz")
                      && SampleIndex.IsInstalled("LICENSE") && SampleIndex.IsInstalled("readme.txt"),
            "samples, mappings and docs install");
        yield return (!SampleIndex.IsInstalled("patch.xiz") && !SampleIndex.IsInstalled("Kit.nki") && !SampleIndex.IsInstalled("setup.exe")
                      && !SampleIndex.IsInstalled(".DS_Store") && !SampleIndex.IsInstalled("._kick.wav"),
            "…presets, instruments for other samplers, programs and archive cruft don't");
        yield return (!SampleIndex.IsInstalled("pad.aiff") && !SampleIndex.IsInstalled("loop.ogg"),
            "…nor audio the engine can't decode yet");

        // ---- index parsing ----------------------------------------------------------------
        var json = IndexJson(Pack("drums", "Drums", "https://github.com/a/b/releases/download/v1/drums.tar.gz", new string('A', 64), 10,
                                  root: "Drums-1.0", files: 3, unpacked: 30, license: "CC-BY-4.0", attribution: "Drums by A"));
        var parsed = SampleIndex.Parse(json).Single();
        yield return (parsed.Asset is { Archive: "tar", Root: "Drums-1.0", Files: 3, UnpackedSize: 30, Size: 10 } a
                      && a.Sha256 == new string('a', 64) && a.Formats.SequenceEqual(["wav"]),
            "the index yields the pack's archive (type from the URL), root and contents");
        yield return (parsed is { License: "CC-BY-4.0", Attribution: "Drums by A", Kind: "one-shots", Version: "1.0" },
            "…with its license and attribution");
        yield return (SampleIndex.Parse(json.Replace("\"packs\": [", "\"packs\": [{\"id\": \"half\"},")).Count == 1,
            "a malformed entry is skipped, not fatal");
        yield return (SampleIndex.Parse(json.Replace("drums.tar.gz", "drums.dmg")).Count == 0,
            "a pack in an archive type Nota doesn't unpack for samples is skipped");
        yield return (Throws(() => SampleIndex.Parse(json.Replace("\"schema\": 1", "\"schema\": 99"))),
            "a newer index schema asks for a newer Nota");

        // ---- install / update / uninstall end to end ----------------------------------------
        var tmp = Directory.CreateTempSubdirectory("nota-samples-test-").FullName;
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
        var assets = Path.Combine(tmp, "assets");
        Directory.CreateDirectory(assets);

        // A zip wrapped in one folder, with presets to skip and macOS cruft.
        var zip = Path.Combine(assets, "kit.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Entry(z, "Kit-1.0/Drums/kick.wav", "kick");
            Entry(z, "Kit-1.0/Drums/snare.flac", "snare");
            Entry(z, "Kit-1.0/Kit.sfz", "<region>");
            Entry(z, "Kit-1.0/README.txt", "hi");
            Entry(z, "Kit-1.0/Presets/Kit.nki", "kontakt");
            Entry(z, "Kit-1.0/.DS_Store", "x");
            Entry(z, "__MACOSX/Kit-1.0/._kick.wav", "x");
        }
        var zip2 = Path.Combine(assets, "kit2.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zip2, ZipArchiveMode.Create))
            Entry(z, "Kit-2.0/Drums/clap.wav", "clap");
        var tgz = Path.Combine(assets, "loops.tar.gz");
        WriteTarGz(tgz, ("loop 120.mp3", "loop"), ("loop 120.ogg", "not loadable"), ("LICENSE", "CC0"));
        var noAudio = Path.Combine(assets, "presets.zip");
        using (var z = System.IO.Compression.ZipFile.Open(noAudio, ZipArchiveMode.Create))
            Entry(z, "patches/lead.xiz", "x");

        var index = Path.Combine(tmp, "index.json");
        string Packs(string kitZip, string kitVersion, string kitRoot) => IndexJson(
            Pack("kit", "Test Kit", kitZip, Sha(kitZip), new FileInfo(kitZip).Length, root: kitRoot, files: 2, unpacked: 20, version: kitVersion),
            Pack("loops", "Other Pack", tgz, Sha(tgz), new FileInfo(tgz).Length, root: null, files: 1, unpacked: 7, kind: "loops"),
            Pack("bad-sum", "Bad Sum", zip, new string('0', 64), new FileInfo(zip).Length, root: "Kit-1.0", files: 2, unpacked: 20),
            Pack("escape", "Escape", zip, Sha(zip), new FileInfo(zip).Length, root: "../..", files: 2, unpacked: 20),
            Pack("huge", "Huge", zip, Sha(zip), new FileInfo(zip).Length, root: "Kit-1.0", files: 2, unpacked: 1L << 60),
            Pack("no-audio", "No Audio", noAudio, Sha(noAudio), new FileInfo(noAudio).Length, root: null, files: 1, unpacked: 1));
        File.WriteAllText(index, Packs(zip, "1.0", "Kit-1.0"));

        var samples = Path.Combine(tmp, "My Samples");
        var root = Path.Combine(tmp, "store");
        var store = new SampleStore(root, () => samples, index);
        StorePack P(string id) => store.FetchAsync().GetAwaiter().GetResult().Single(p => p.Id == id);
        yield return (store.FetchAsync().GetAwaiter().GetResult().Count == 6, "a local index lists its packs");
        yield return (store.InstallDir == Path.Combine(samples, "Downloaded"), "packs install under Downloaded in the Samples folder");

        var reports = new List<StoreProgress>();
        store.InstallAsync(P("kit"), new SyncProgress(reports.Add)).GetAwaiter().GetResult();
        var kit = Path.Combine(samples, "Downloaded", "Test Kit");
        yield return (File.Exists(Path.Combine(kit, "Drums", "kick.wav")) && File.Exists(Path.Combine(kit, "Drums", "snare.flac")),
            "a zip installs into a folder named after the pack, its wrapper folder dropped");
        yield return (File.Exists(Path.Combine(kit, "Kit.sfz")) && File.Exists(Path.Combine(kit, "README.txt")),
            "…mappings and docs come along");
        yield return (!Directory.Exists(Path.Combine(kit, "Presets")) && !File.Exists(Path.Combine(kit, ".DS_Store")) && !Directory.Exists(Path.Combine(samples, "Downloaded", "__MACOSX")),
            "…presets and archive cruft don't, and a folder left empty isn't created");
        yield return (reports.Any(r => r.Message.StartsWith("Downloading") && r.Fraction >= 0.99)
                      && reports.Any(r => r.Message.StartsWith("Unpacking")) && reports.Any(r => r.Message.StartsWith("Installing")),
            "install reports the download, unpack and copy stages");
        yield return (store.Installed.SingleOrDefault(i => i.Id == "kit") is { Version: "1.0", Files: 2 } inst && inst.Path == kit,
            "the install is recorded with its version, folder and sample count");
        yield return (new SampleStore(root, () => samples, index).Installed.Any(i => i.Id == "kit"),
            "…and survives a restart (installed.json)");
        yield return (!Directory.EnumerateDirectories(store.InstallDir, ".staging-*").Any(), "no staging folder is left behind");

        // The user already has a folder named like the next pack: it gets a numbered sibling.
        var mine = Path.Combine(samples, "Downloaded", "Other Pack");
        Directory.CreateDirectory(mine);
        File.WriteAllText(Path.Combine(mine, "mine.wav"), "mine");
        store.InstallAsync(P("loops")).GetAwaiter().GetResult();
        var other = Path.Combine(samples, "Downloaded", "Other Pack (2)");
        yield return (File.Exists(Path.Combine(other, "loop 120.mp3")) && File.Exists(Path.Combine(mine, "mine.wav")),
            "a tar.gz installs beside a same-named user folder without touching it");
        yield return (!File.Exists(Path.Combine(other, "loop 120.ogg")) && File.Exists(Path.Combine(other, "LICENSE")),
            "…skipping audio the engine can't load, keeping the license");

        // Update: a new version replaces the pack's files in the same folder.
        File.WriteAllText(index, Packs(zip2, "2.0", "Kit-2.0"));
        store.InstallAsync(P("kit")).GetAwaiter().GetResult();
        yield return (File.Exists(Path.Combine(kit, "Drums", "clap.wav")) && !File.Exists(Path.Combine(kit, "Drums", "kick.wav"))
                      && store.Installed.Single(i => i.Id == "kit").Version == "2.0",
            "an update replaces the pack's files in place");

        yield return (Throws(() => store.InstallAsync(P("bad-sum")).GetAwaiter().GetResult()) && !Directory.Exists(Path.Combine(samples, "Downloaded", "Bad Sum")),
            "a checksum mismatch is refused, nothing installed");
        yield return (Throws(() => store.InstallAsync(P("escape")).GetAwaiter().GetResult()) && !Directory.Exists(Path.Combine(samples, "Downloaded", "Escape")),
            "a root that climbs out of the archive is refused");
        yield return (Throws(() => store.InstallAsync(P("huge")).GetAwaiter().GetResult()), "a pack bigger than the free space is refused up front");
        yield return (Throws(() => store.InstallAsync(P("no-audio")).GetAwaiter().GetResult()) && !Directory.Exists(Path.Combine(samples, "Downloaded", "No Audio")),
            "an archive with no usable samples installs nothing");
        yield return (!Directory.EnumerateDirectories(store.InstallDir, ".staging-*").Any(), "…and failed installs leave no staging folder");

        store.Uninstall("kit");
        yield return (!Directory.Exists(kit) && store.Installed.All(i => i.Id != "kit"), "uninstall removes the pack's folder");
        yield return (Throws(() => store.Uninstall("../x")), "uninstall refuses ids that aren't registry ids");

        // A pack folder whose marker is gone (the user replaced it) is forgotten, never deleted.
        var loops = store.Installed.Single(i => i.Id == "loops").Path;
        File.Delete(Path.Combine(loops, SampleStore.MarkerFile));
        store.Uninstall("loops");
        yield return (Directory.Exists(loops) && store.Installed.All(i => i.Id != "loops"),
            "a folder without its pack marker is dropped from the list but not deleted");

        // The browser's Files tab: Downloaded gets its flag (icon) and heads the list.
        Directory.CreateDirectory(Path.Combine(samples, "Acoustic"));
        File.WriteAllText(Path.Combine(samples, "Acoustic", "a.wav"), "a");
        File.WriteAllText(Path.Combine(samples, "Amen.wav"), "a");
        var browser = new Nota.Presentation.BrowserViewModel(new EmptyPluginCatalog(), new EmptyPresetLibrary(), new FactoryPresetCatalog(), new FolderSettings(samples));
        var rows = browser.Samples.Select(r => r.Name).ToList();
        yield return (browser.Samples.FirstOrDefault() is { Name: SamplePacks.FolderName, IsDownloads: true }
                      && rows.IndexOf("Acoustic") > 0 && rows.IndexOf("Amen.wav") > rows.IndexOf("Acoustic")
                      && browser.Samples.Count(r => r.IsDownloads) == 1,
            "the Files tab lists Downloaded first, flagged, ahead of other folders and files");

        var offline = new SampleStore(Path.Combine(tmp, "store2"), () => samples, "https://127.0.0.1:1/index.json");
        yield return (Throws(() => offline.FetchAsync().GetAwaiter().GetResult()), "offline with no cache is a clear error");
    }

    private sealed class FolderSettings(string samples) : ISettingsService
    {
        public Settings Current { get; } = new();
        public void Save() { }
        public event Action? Changed { add { } remove { } }
        public string ResolvedSamplesFolder() => samples;
        public string ResolvedProjectsFolder() => samples;
        public string PresetsFolder() => samples;
    }

    private static string IndexJson(params string[] packs)
        => $"{{\n\"schema\": 1,\n\"generated\": \"2026-01-01T00:00:00Z\",\n\"packs\": [{string.Join(",", packs)}]\n}}";

    private static string Pack(string id, string name, string url, string sha, long size, string? root, int files, long unpacked,
                               string version = "1.0", string kind = "one-shots", string license = "CC0-1.0", string? attribution = null)
    {
        var asset = new Dictionary<string, object?>
        {
            ["url"] = url, ["sha256"] = sha, ["size"] = size, ["files"] = files, ["unpackedSize"] = unpacked, ["formats"] = new[] { "wav" },
        };
        if (root is not null) asset["root"] = root;
        var pack = new Dictionary<string, object?>
        {
            ["id"] = id, ["name"] = name, ["author"] = "Test", ["description"] = "A test pack.", ["source"] = "https://example.com",
            ["license"] = license, ["kind"] = kind, ["tags"] = new[] { "test" },
            ["versions"] = new[] { new Dictionary<string, object?> { ["version"] = version, ["asset"] = asset } },
        };
        if (attribution is not null) pack["attribution"] = attribution;
        return JsonSerializer.Serialize(pack);
    }
}
