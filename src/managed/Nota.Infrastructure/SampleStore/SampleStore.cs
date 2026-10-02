// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Get Samples: installs free sample packs listed in the Nota sample registry
// (github.com/nota-daw/nota-samples-registry) into the user's Samples folder, so the browser's
// Files tab lists them. Packs download straight from where their authors publish them and must
// match the size + sha256 pinned in the index before anything is unpacked (RegistryClient).
//
// Layout:
//   <Samples>/Downloaded/<Pack Name>/   one folder per pack: its audio + docs, nothing else
//     .nota-pack                        marker with the pack id; Uninstall only deletes a
//                                       folder that carries the matching marker
//   <data>/sample-store/
//     registry-index.json               last fetched index (offline fallback, 24 h freshness)
//     installed.json                    what was installed where
// An install checks free space first, unpacks into a temp dir, copies the pack's audio and
// docs into Downloaded/.staging-<id> (same volume as the final folder), then swaps that in —
// a failed or cancelled install never leaves a half-copied pack behind.
// NOTA_SAMPLE_REGISTRY overrides the index URL (or points at a local index.json).

using System.Text.Json;

namespace Nota.Infrastructure;

public sealed class SampleStore : ISampleStore
{
    public const string DefaultIndexUrl = "https://nota-daw.github.io/nota-samples-registry/index.json";
    public const string MarkerFile = ".nota-pack";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root;
    private readonly Func<string> _samplesFolder;
    private readonly RegistryClient _registry;
    private readonly object _gate = new();
    private List<InstalledStorePack> _installed;

    public SampleStore(ISettingsService settings) : this(NotaPaths.SubDir("sample-store"), settings.ResolvedSamplesFolder) { }

    /// <param name="root">Store bookkeeping folder (tests pass a temp dir).</param>
    /// <param name="samplesFolder">The current Samples folder; read on every install, since the user can change it.</param>
    /// <param name="indexSource">Index URL or local path; defaults to NOTA_SAMPLE_REGISTRY, then the public registry.</param>
    public SampleStore(string root, Func<string> samplesFolder, string? indexSource = null, HttpMessageHandler? handler = null)
    {
        _root = root;
        _samplesFolder = samplesFolder;
        Directory.CreateDirectory(root);
        var env = Environment.GetEnvironmentVariable("NOTA_SAMPLE_REGISTRY");
        _registry = new RegistryClient(indexSource ?? (string.IsNullOrEmpty(env) ? DefaultIndexUrl : env),
                                       Path.Combine(root, "registry-index.json"), "sample registry", "Nota-SampleStore", handler);
        _installed = LoadInstalled();
    }

    public string InstallDir => Path.Combine(_samplesFolder(), "Downloaded");

    private string InstalledPath => Path.Combine(_root, "installed.json");

    public IReadOnlyList<InstalledStorePack> Installed
    {
        get
        {
            lock (_gate)
            {
                // A pack folder the user deleted or moved by hand is no longer installed.
                if (_installed.RemoveAll(i => !IsOurFolder(i.Path, i.Id)) > 0) SaveInstalled();
                return _installed.ToList();
            }
        }
    }

    public async Task<IReadOnlyList<StorePack>> FetchAsync(bool refresh = false, CancellationToken ct = default)
    {
        var json = await _registry.FetchIndexAsync(refresh, j => SampleIndex.Parse(j), ct).ConfigureAwait(false);
        try { return SampleIndex.Parse(json); }
        catch (JsonException e) { throw new StoreException("The sample registry index is damaged.", e); }
    }

    // ---- install / uninstall ----------------------------------------------------------

    public async Task InstallAsync(StorePack pack, IProgress<StoreProgress>? progress = null, CancellationToken ct = default)
    {
        if (!RegistryClient.IsValidId(pack.Id)) throw new StoreException($"Invalid pack id \"{pack.Id}\".");
        var asset = pack.Asset;
        var installDir = InstallDir;
        Directory.CreateDirectory(installDir);
        var work = Directory.CreateTempSubdirectory("nota-pack-").FullName;
        try
        {
            CheckFreeSpace(pack, installDir, work);

            var file = Path.Combine(work, "pack" + RegistryClient.ExtensionOf(asset.Url, asset.Archive));
            await _registry.DownloadAsync(asset.Url, asset.Size, asset.Sha256, file, pack.Name, progress, ct).ConfigureAwait(false);

            progress?.Report(new StoreProgress(-1, $"Unpacking {pack.Name}…"));
            var unpacked = Path.Combine(work, "x");
            await Task.Run(() => ArchiveUnpacker.Unpack(file, asset.Archive, unpacked, ct), ct).ConfigureAwait(false);
            try { File.Delete(file); } catch { /* temp; frees space for big packs */ }
            var packRoot = asset.Root is { } r ? RegistryClient.Resolve(unpacked, r) : unpacked;
            if (!Directory.Exists(packRoot)) throw new StoreException($"The download of {pack.Name} doesn't contain {asset.Root}.");

            var path = await Task.Run(() => Place(pack, packRoot, installDir, progress, ct), ct).ConfigureAwait(false);
            lock (_gate)
            {
                _installed.RemoveAll(i => i.Id == pack.Id);
                _installed.Add(new InstalledStorePack(pack.Id, pack.Version, asset.Sha256, path.Path, path.Files, DateTimeOffset.UtcNow));
                SaveInstalled();
            }
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* temp; best-effort */ }
        }
    }

    public void Uninstall(string id)
    {
        if (!RegistryClient.IsValidId(id)) throw new StoreException($"Invalid pack id \"{id}\".");
        InstalledStorePack? inst;
        lock (_gate) inst = _installed.FirstOrDefault(i => i.Id == id);
        if (inst is null) return;
        try
        {
            if (IsOurFolder(inst.Path, id)) Directory.Delete(inst.Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new StoreException("Couldn't remove the pack's files — close anything using them and try again.", e);
        }
        lock (_gate)
        {
            if (_installed.RemoveAll(i => i.Id == id) > 0) SaveInstalled();
        }
    }

    // Unpacking needs the archive plus its contents in temp; the pack's own folder needs its
    // unpacked size. Both checked before the download, with 5% headroom.
    private static void CheckFreeSpace(StorePack pack, string installDir, string work)
    {
        long need = pack.Asset.UnpackedSize + pack.Asset.UnpackedSize / 20;
        long needTemp = pack.Asset.Size + need;
        if (FreeBytes(installDir) is { } free && free < need)
            throw new StoreException($"{pack.Name} needs {Mb(need)} of free space in your Samples folder, which has {Mb(free)}.");
        if (FreeBytes(work) is { } freeTemp && freeTemp < needTemp)
            throw new StoreException($"Unpacking {pack.Name} needs {Mb(needTemp)} of free space on the system drive, which has {Mb(freeTemp)}.");
    }

    // Free space on the volume holding path: the drive whose root is the longest prefix of it
    // (on macOS/Linux every mount point is a "drive"). Null when it can't be told.
    private static long? FreeBytes(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var drive = DriveInfo.GetDrives()
                .Where(d => d.IsReady && full.StartsWith(d.RootDirectory.FullName, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                .MaxBy(d => d.RootDirectory.FullName.Length);
            return drive?.AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string Mb(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes / (double)(1 << 20):0} MB";

    // Copies the pack's audio + docs into Downloaded/.staging-<id>, then swaps it in for the
    // installed folder (same name on an update) or a new one named after the pack.
    private (string Path, int Files) Place(StorePack pack, string packRoot, string installDir, IProgress<StoreProgress>? progress, CancellationToken ct)
    {
        var staging = Path.Combine(installDir, ".staging-" + pack.Id);
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        try
        {
            string label = $"Installing {pack.Name}…";
            long copied = 0, total = Math.Max(1, pack.Asset.UnpackedSize);
            int files = 0;
            progress?.Report(new StoreProgress(0, label));
            CopyPack(new DirectoryInfo(packRoot), staging, ref files, ref copied, () => progress?.Report(new StoreProgress(Math.Min(1, (double)copied / total), label)), ct);
            if (files == 0) throw new StoreException($"The download of {pack.Name} contains no samples Nota can use.");
            File.WriteAllText(Path.Combine(staging, MarkerFile), pack.Id);

            InstalledStorePack? previous;
            lock (_gate) previous = _installed.FirstOrDefault(i => i.Id == pack.Id);
            string final = previous is not null && IsOurFolder(previous.Path, pack.Id) ? previous.Path : FreeFolder(installDir, pack);
            if (Directory.Exists(final)) Directory.Delete(final, recursive: true);
            Directory.Move(staging, final);
            return (final, files);
        }
        catch
        {
            try { Directory.Delete(staging, recursive: true); } catch { /* best-effort */ }
            throw;
        }
    }

    private static void CopyPack(DirectoryInfo src, string dest, ref int files, ref long copied, Action report, CancellationToken ct)
    {
        foreach (var entry in src.EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if (entry.LinkTarget is not null) continue;   // a link could point anywhere on disk
            if (entry is DirectoryInfo d)
            {
                if (SampleIndex.IsJunkDir(d.Name)) continue;
                var sub = Path.Combine(dest, d.Name);
                Directory.CreateDirectory(sub);
                CopyPack(d, sub, ref files, ref copied, report, ct);
                if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub);   // e.g. a presets-only folder
            }
            else if (entry is FileInfo f && SampleIndex.IsInstalled(f.Name))
            {
                f.CopyTo(Path.Combine(dest, f.Name), overwrite: true);
                copied += f.Length;
                if (SampleIndex.AudioExtensions.Contains(SampleIndex.ExtensionOf(f.Name))) files++;
                report();
            }
        }
    }

    // "<Pack Name>", or "<Pack Name> (2)" … when the user already has a folder by that name.
    private static string FreeFolder(string installDir, StorePack pack)
    {
        var name = string.Concat(pack.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' ? '-' : c)).Trim().TrimEnd('.');
        if (name.Length == 0 || name.StartsWith('.')) name = pack.Id;
        var path = Path.Combine(installDir, name);
        for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Path.Combine(installDir, $"{name} ({n})");
        return path;
    }

    private static bool IsOurFolder(string path, string id)
    {
        try
        {
            var marker = Path.Combine(path, MarkerFile);
            return File.Exists(marker) && File.ReadAllText(marker).Trim() == id;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---- installed.json ---------------------------------------------------------------

    private sealed record InstalledFile(List<InstalledStorePack> Packs);

    private List<InstalledStorePack> LoadInstalled()
    {
        try
        {
            if (!File.Exists(InstalledPath)) return [];
            return JsonSerializer.Deserialize<InstalledFile>(File.ReadAllText(InstalledPath), Json)?.Packs ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return [];
        }
    }

    private void SaveInstalled() => RegistryClient.WriteAtomic(InstalledPath, JsonSerializer.Serialize(new InstalledFile(_installed), Json));
}
