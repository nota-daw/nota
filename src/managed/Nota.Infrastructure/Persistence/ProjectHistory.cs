// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Project version history, kept inside the bundle:
//
//   MySong.nota/
//     project.json, *.json            working copy: the manifest + sidecars
//     samples/<hash>.wav              shared by every version (content-addressed, BundleContent)
//     plugin-states/<hash>.bin        shared by every version
//     .history/
//       versions.json                 the tree: versions, parents, head
//       objects/ab/cdef….br           manifests + sidecars of every version, Brotli
//
// A version is the set of top-level files (by content hash, stored in objects/) plus the
// list of binaries its manifest references (left where they are — a save already names them
// by content, so a sample shared by twenty versions is one file). Checkout writes a
// version's top-level files back; the binaries are already there. A save prunes binaries no
// version needs (BundleContent.PruneUnreferenced asks PinnedBinaries).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Nota.Infrastructure;

public sealed partial class ProjectHistory : IProjectHistory
{
    public const string HistoryDir = ".history";
    private const string VersionsFile = "versions.json";
    private const string ObjectsDir = "objects";
    private const int HistoryFormat = 1;

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _appVersion;

    /// <param name="appVersion">Recorded on each version ("saved with Nota X").</param>
    public ProjectHistory(string appVersion) => _appVersion = appVersion;

    // --- on-disk model -----------------------------------------------------

    private sealed class HistoryDoc
    {
        public int HistoryFormat { get; set; } = ProjectHistory.HistoryFormat;
        public string? Head { get; set; }
        public List<Node> Versions { get; set; } = new();
    }

    private sealed class Node
    {
        public string Id { get; set; } = "";
        public string? Parent { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? Label { get; set; }
        public string? Note { get; set; }
        public bool Starred { get; set; }
        public string AppVersion { get; set; } = "";
        public int ProjectFormat { get; set; }
        public long AddedBytes { get; set; }
        /// <summary>Top-level file name -> object hash.</summary>
        public SortedDictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);
        /// <summary>Bundle-relative binaries the manifest references (samples/…, plugin-states/…).</summary>
        public List<string> Binaries { get; set; } = new();
    }

    // --- IProjectHistory ---------------------------------------------------

    public ProjectHistoryState Read(string bundleDir)
    {
        lock (Gate)
        {
            var doc = Load(bundleDir);
            if (doc is null) return ProjectHistoryState.Empty;
            return new ProjectHistoryState(doc.Versions.Select(ToVersion).ToList(), doc.Head);
        }
    }

    public ProjectVersion? Commit(string bundleDir, string? note = null)
    {
        lock (Gate)
        {
            var doc = Load(bundleDir) ?? new HistoryDoc();
            var snap = TakeSnapshot(bundleDir);
            var head = Find(doc, doc.Head);
            if (head is not null && SameContent(head, snap)) return null;

            // What this version adds to the disk: objects not stored yet + binaries no
            // earlier version referenced (they'd have been pruned without this version).
            long added = 0;
            foreach (var (hash, bytes) in snap.Contents)
                if (WriteObject(bundleDir, hash, bytes) is long written) added += written;
            var known = new HashSet<string>(doc.Versions.SelectMany(v => v.Binaries), StringComparer.Ordinal);
            foreach (var rel in snap.Binaries)
                if (!known.Contains(rel)) added += FileSize(Path.Combine(bundleDir, rel));

            var node = new Node
            {
                Id = NewId(doc),
                Parent = head?.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                AppVersion = _appVersion,
                ProjectFormat = snap.ProjectFormat,
                AddedBytes = added,
                Files = snap.Files,
                Binaries = snap.Binaries,
            };
            doc.Versions.Add(node);
            doc.Head = node.Id;
            Store(bundleDir, doc);
            return ToVersion(node);
        }
    }

    public void Checkout(string bundleDir, string versionId)
    {
        lock (Gate)
        {
            var doc = Load(bundleDir) ?? throw new ProjectHistoryException("This project has no version history.");
            var node = Find(doc, versionId) ?? throw new ProjectHistoryException("That version no longer exists.");
            if (node.ProjectFormat > ProjectService.CurrentFormatVersion)
                throw new ProjectHistoryException(
                    $"This version was saved by Nota {node.AppVersion}, which is newer than this one. Update Nota to open it.");

            // Read and check everything before touching the working copy.
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            int missing = 0;
            foreach (var (name, hash) in node.Files)
            {
                var bytes = ReadObject(bundleDir, hash);
                if (bytes is null) missing++;
                else contents[name] = bytes;
            }
            missing += node.Binaries.Count(rel => !File.Exists(Path.Combine(bundleDir, rel)));
            if (missing > 0)
                throw new ProjectHistoryException($"This version can't be restored: {missing} of its files are missing.");

            foreach (var name in SnapshotFileNames(bundleDir))
                if (!contents.ContainsKey(name))
                    File.Delete(Path.Combine(bundleDir, name));   // a sidecar this version didn't have
            foreach (var (name, bytes) in contents.Where(kv => kv.Key != ProjectService.ManifestName))
                BundleContent.WriteAtomic(Path.Combine(bundleDir, name), tmp => File.WriteAllBytes(tmp, bytes));
            // The manifest last: until it lands, the bundle still opens as the previous state.
            if (contents.TryGetValue(ProjectService.ManifestName, out var manifest))
                BundleContent.WriteAtomic(Path.Combine(bundleDir, ProjectService.ManifestName), tmp => File.WriteAllBytes(tmp, manifest));

            doc.Head = node.Id;
            Store(bundleDir, doc);
        }
    }

    public void SetLabel(string bundleDir, string versionId, string? label)
        => Edit(bundleDir, versionId, n => n.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim());

    public void SetNote(string bundleDir, string versionId, string? note)
        => Edit(bundleDir, versionId, n => n.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim());

    public void SetStarred(string bundleDir, string versionId, bool starred)
        => Edit(bundleDir, versionId, n => n.Starred = starred);

    public long Delete(string bundleDir, string versionId)
    {
        lock (Gate)
        {
            var doc = Load(bundleDir) ?? throw new ProjectHistoryException("This project has no version history.");
            var node = Find(doc, versionId) ?? throw new ProjectHistoryException("That version no longer exists.");
            if (node.Id == doc.Head)
                throw new ProjectHistoryException("The current version can't be deleted — switch to another one first.");
            foreach (var child in doc.Versions.Where(v => v.Parent == node.Id)) child.Parent = node.Parent;
            doc.Versions.Remove(node);
            Store(bundleDir, doc);
            return Collect(bundleDir, doc);
        }
    }

    public long Erase(string bundleDir)
    {
        lock (Gate)
        {
            string dir = Path.Combine(bundleDir, HistoryDir);
            long freed = Directory.Exists(dir) ? DirSize(dir) : 0;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            return freed + Collect(bundleDir, new HistoryDoc());
        }
    }

    public void CopyTo(string fromBundle, string toBundle)
    {
        lock (Gate)
        {
            var doc = Load(fromBundle);
            if (doc is null) return;
            string src = Path.Combine(fromBundle, HistoryDir), dst = Path.Combine(toBundle, HistoryDir);
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(dst, Path.GetRelativePath(src, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (!File.Exists(to)) File.Copy(file, to);
            }
            File.Copy(Path.Combine(src, VersionsFile), Path.Combine(dst, VersionsFile), overwrite: true);
            foreach (var rel in doc.Versions.SelectMany(v => v.Binaries).Distinct())
            {
                string from = Path.Combine(fromBundle, rel), to = Path.Combine(toBundle, rel);
                if (File.Exists(to) || !File.Exists(from)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                BundleContent.WriteAtomic(to, tmp => File.Copy(from, tmp));
            }
        }
    }

    public ProjectHistorySize Size(string bundleDir)
    {
        lock (Gate)
        {
            string hist = Path.Combine(bundleDir, HistoryDir);
            long historyDir = Directory.Exists(hist) ? DirSize(hist) : 0;
            var working = WorkingBinaries(bundleDir);
            long binaries = 0, historyOnly = historyDir;
            foreach (var (rel, size) in BinaryFiles(bundleDir))
            {
                binaries += size;
                if (!working.Contains(rel)) historyOnly += size;
            }
            return new ProjectHistorySize(binaries + historyDir, historyOnly);
        }
    }

    /// <summary>Binaries any version references, for the save's prune to keep. Empty when
    /// there is no history; null when it can't be read — then nothing may be pruned.</summary>
    internal static IReadOnlySet<string>? PinnedBinaries(string bundleDir)
    {
        lock (Gate)
        {
            try
            {
                var doc = Load(bundleDir);
                return doc is null
                    ? new HashSet<string>()
                    : new HashSet<string>(doc.Versions.SelectMany(v => v.Binaries), StringComparer.Ordinal);
            }
            catch (ProjectHistoryException) { return null; }
        }
    }

    // --- snapshot ----------------------------------------------------------

    private sealed record Snapshot(
        SortedDictionary<string, string> Files, List<string> Binaries, int ProjectFormat,
        Dictionary<string, byte[]> Contents);

    private static Snapshot TakeSnapshot(string bundleDir)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in SnapshotFileNames(bundleDir))
        {
            var bytes = File.ReadAllBytes(Path.Combine(bundleDir, name));
            string hash = BundleContent.BlobHash(bytes);
            files[name] = hash;
            contents[hash] = bytes;
        }
        if (!files.ContainsKey(ProjectService.ManifestName))
            throw new ProjectHistoryException("The project hasn't been saved yet.");

        var manifest = ParseManifest(bundleDir);
        var binaries = new List<string>();
        foreach (var rel in ManifestBinaries(manifest).Distinct().Order(StringComparer.Ordinal))
        {
            // History relies on a name meaning one content forever (stage-1 bundles).
            if (!ContentNamedRx().IsMatch(rel))
                throw new ProjectHistoryException("Save the project once more before recording a version.");
            if (File.Exists(Path.Combine(bundleDir, rel))) binaries.Add(rel);   // a sample that failed to write stays missing
        }
        int format = manifest?["formatVersion"]?.GetValue<int>() ?? 1;
        return new Snapshot(files, binaries, format, contents);
    }

    // Top-level files that make up a version: the manifest and its sidecars. Hidden files
    // (.DS_Store), temp files and folders (samples/, analysis/, backups/, .history/) aren't.
    private static IEnumerable<string> SnapshotFileNames(string bundleDir)
        => Directory.EnumerateFiles(bundleDir)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(n => !n.StartsWith('.') && !n.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

    private static bool SameContent(Node head, Snapshot snap)
        => head.Files.Count == snap.Files.Count
           && head.Files.All(kv => snap.Files.TryGetValue(kv.Key, out var h) && h == kv.Value)
           && head.Binaries.SequenceEqual(snap.Binaries, StringComparer.Ordinal);

    private static JsonNode? ParseManifest(string bundleDir)
    {
        string path = Path.Combine(bundleDir, ProjectService.ManifestName);
        try { return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null; }
        catch (JsonException) { return null; }
    }

    // Every string in the manifest that points into samples/ or plugin-states/.
    private static IEnumerable<string> ManifestBinaries(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (_, v) in o)
                    foreach (var s in ManifestBinaries(v)) yield return s;
                break;
            case JsonArray a:
                foreach (var v in a)
                    foreach (var s in ManifestBinaries(v)) yield return s;
                break;
            case JsonValue v when v.TryGetValue(out string? s)
                                  && (s.StartsWith(BundleContent.SamplesDir + "/", StringComparison.Ordinal)
                                      || s.StartsWith(BundleContent.StatesDir + "/", StringComparison.Ordinal)):
                yield return s;
                break;
        }
    }

    private static HashSet<string> WorkingBinaries(string bundleDir)
        => new(ManifestBinaries(ParseManifest(bundleDir)), StringComparer.Ordinal);

    // --- garbage collection ------------------------------------------------

    // Deletes objects no version uses and binaries neither a version nor the working copy
    // references. Returns the bytes freed.
    private static long Collect(string bundleDir, HistoryDoc doc)
    {
        long freed = 0;
        var liveObjects = new HashSet<string>(doc.Versions.SelectMany(v => v.Files.Values), StringComparer.Ordinal);
        string objects = Path.Combine(bundleDir, HistoryDir, ObjectsDir);
        if (Directory.Exists(objects))
            foreach (var file in Directory.EnumerateFiles(objects, "*", SearchOption.AllDirectories))
            {
                string hash = Path.GetFileName(Path.GetDirectoryName(file)) + Path.GetFileNameWithoutExtension(file);
                if (liveObjects.Contains(hash)) continue;
                freed += FileSize(file);
                TryDelete(file);
            }

        var liveBinaries = WorkingBinaries(bundleDir);
        liveBinaries.UnionWith(doc.Versions.SelectMany(v => v.Binaries));
        foreach (var (rel, size) in BinaryFiles(bundleDir))
        {
            if (liveBinaries.Contains(rel)) continue;
            freed += size;
            TryDelete(Path.Combine(bundleDir, rel));
        }
        return freed;
    }

    private static IEnumerable<(string Rel, long Size)> BinaryFiles(string bundleDir)
    {
        foreach (var dir in new[] { BundleContent.SamplesDir, BundleContent.StatesDir })
        {
            string full = Path.Combine(bundleDir, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var file in Directory.EnumerateFiles(full))
            {
                string name = Path.GetFileName(file);
                if (BundleContent.IsSaveOutput(name, dir)) yield return ($"{dir}/{name}", FileSize(file));
            }
        }
    }

    // --- objects -----------------------------------------------------------

    private static string ObjectPath(string bundleDir, string hash)
        => Path.Combine(bundleDir, HistoryDir, ObjectsDir, hash[..2], hash[2..] + ".br");

    // Stores bytes under their hash unless already there. Returns the bytes written (null if present).
    private static long? WriteObject(string bundleDir, string hash, byte[] bytes)
    {
        string path = ObjectPath(bundleDir, hash);
        if (File.Exists(path)) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        BundleContent.WriteAtomic(path, tmp =>
        {
            using var fs = File.Create(tmp);
            using var br = new BrotliStream(fs, CompressionLevel.Optimal);
            br.Write(bytes);
        });
        return FileSize(path);
    }

    // The object's bytes, or null if missing or not matching its hash.
    private static byte[]? ReadObject(string bundleDir, string hash)
    {
        string path = ObjectPath(bundleDir, hash);
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BrotliStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            br.CopyTo(ms);
            var bytes = ms.ToArray();
            return BundleContent.BlobHash(bytes) == hash ? bytes : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException) { return null; }
    }

    // --- versions.json -----------------------------------------------------

    private static HistoryDoc? Load(string bundleDir)
    {
        string path = Path.Combine(bundleDir, HistoryDir, VersionsFile);
        if (!File.Exists(path)) return null;
        HistoryDoc? doc;
        try { doc = JsonSerializer.Deserialize<HistoryDoc>(File.ReadAllText(path), JsonOpts); }
        catch (Exception e) when (e is JsonException or IOException)
        {
            throw new ProjectHistoryException("The version history of this project is damaged.", e);
        }
        if (doc is null) throw new ProjectHistoryException("The version history of this project is damaged.");
        if (doc.HistoryFormat > HistoryFormat)
            throw new ProjectHistoryException("This project's version history was written by a newer Nota. Update Nota to use it.");
        return doc;
    }

    private static void Store(string bundleDir, HistoryDoc doc)
    {
        string dir = Path.Combine(bundleDir, HistoryDir);
        Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(doc, JsonOpts);
        BundleContent.WriteAtomic(Path.Combine(dir, VersionsFile), tmp => File.WriteAllText(tmp, json));
    }

    private void Edit(string bundleDir, string versionId, Action<Node> change)
    {
        lock (Gate)
        {
            var doc = Load(bundleDir) ?? throw new ProjectHistoryException("This project has no version history.");
            var node = Find(doc, versionId) ?? throw new ProjectHistoryException("That version no longer exists.");
            change(node);
            Store(bundleDir, doc);
        }
    }

    // --- helpers -----------------------------------------------------------

    private static Node? Find(HistoryDoc doc, string? id)
        => id is null ? null : doc.Versions.FirstOrDefault(v => v.Id == id);

    private static ProjectVersion ToVersion(Node n)
        => new(n.Id, n.Parent, n.CreatedAt, n.Label, n.Note, n.Starred, n.AppVersion, n.ProjectFormat, n.AddedBytes,
               CanOpen: n.ProjectFormat <= ProjectService.CurrentFormatVersion);

    private static string NewId(HistoryDoc doc)
    {
        string id;
        do id = $"v-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant()}";
        while (doc.Versions.Any(v => v.Id == id));
        return id;
    }

    private static long FileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return 0; }
    }

    private static long DirSize(string dir)
        => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(FileSize);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* in use or read-only: the next collect retries */ }
    }

    [GeneratedRegex(@"^(samples/[0-9a-f]{32}\.wav|plugin-states/[0-9a-f]{32}\.bin)$")]
    private static partial Regex ContentNamedRx();
}
