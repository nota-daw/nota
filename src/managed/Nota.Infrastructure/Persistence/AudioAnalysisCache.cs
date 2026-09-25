// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// On-disk cache of an imported file's analysis — its waveform overview (min/max per
// 512-frame block) and detected tempo — so re-importing the same audio shows the whole
// waveform at once and skips tempo detection. Unlike Ableton's .asd files these are not
// written next to the user's audio: they live in the project bundle's `analysis/` folder
// (or, for a project that was never saved, a per-user staging folder, adopted into the
// bundle on its first save). Entries are keyed by a content fingerprint, so a moved or
// renamed file still hits and an edited one misses.

using System.Security.Cryptography;

namespace Nota.Infrastructure;

/// <summary>A cached analysis: the peak overview and the detected tempo (0 = none).</summary>
public sealed record AudioAnalysis(float[] PeakTable, double Bpm);

/// <summary>Reads/writes <see cref="AudioAnalysis"/> entries (thread-safe; used from the
/// import worker). <c>projectDir</c> is the open `.nota` bundle, or null while unsaved.</summary>
public sealed class AudioAnalysisCache
{
    public const string FolderName = "analysis";
    private const string Ext = ".npk";
    private const uint Magic = 0x314B504E;   // "NPK1"
    private const int FormatVersion = 1;
    private const int SampleBytes = 64 * 1024;   // fingerprint window (head / middle / tail)
    private static readonly TimeSpan StagingMaxAge = TimeSpan.FromDays(30);

    private readonly object _gate = new();
    private readonly HashSet<string> _stagedKeys = new();   // used from staging this session

    private static string StagingDir => NotaPaths.SubDir("analysis-cache");

    /// <summary>Content fingerprint of an audio file: its length plus three 64 KB windows.
    /// Cheap on a multi-GB file, stable across copies/renames. Null if unreadable.</summary>
    public static string? Fingerprint(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long len = fs.Length;
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            sha.AppendData(BitConverter.GetBytes(len));
            var buf = new byte[SampleBytes];
            foreach (long at in new[] { 0L, Math.Max(0, len / 2 - SampleBytes / 2), Math.Max(0, len - SampleBytes) })
            {
                fs.Position = at;
                int n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
                sha.AppendData(buf, 0, n);
            }
            return Convert.ToHexString(sha.GetHashAndReset(), 0, 16).ToLowerInvariant();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>The cached analysis for <paramref name="key"/>: the project's copy first,
    /// then the staging folder. Null on a miss or an unreadable/foreign file.</summary>
    public AudioAnalysis? TryLoad(string key, string? project)
    {
        if (project is not null && Read(Path.Combine(project, FolderName, key + Ext)) is { } own) return own;
        var staged = Read(Path.Combine(StagingDir, key + Ext));
        // A staged hit now belongs to this project too: copy it in (or remember it for the
        // first save) so the bundle stays self-contained.
        if (staged is not null)
        {
            if (project is not null) Store(key, staged, project);
            else lock (_gate) _stagedKeys.Add(key);
        }
        return staged;
    }

    /// <summary>Stores an analysis in the project (or staging while unsaved). Best-effort.</summary>
    public void Store(string key, AudioAnalysis a, string? project)
    {
        string dir = project is null ? StagingDir : Path.Combine(project, FolderName);
        try
        {
            Directory.CreateDirectory(dir);
            Write(Path.Combine(dir, key + Ext), a);
            if (project is null)
            {
                lock (_gate) _stagedKeys.Add(key);
                PruneStaging();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Copies this session's staged entries into a bundle — called when an
    /// unsaved project is first saved, so its analysis moves in with it.</summary>
    public void AdoptInto(string bundleDir)
    {
        string[] keys;
        lock (_gate) { keys = _stagedKeys.ToArray(); _stagedKeys.Clear(); }
        if (keys.Length == 0) return;
        string dest = Path.Combine(bundleDir, FolderName);
        try
        {
            Directory.CreateDirectory(dest);
            foreach (var k in keys)
            {
                string src = Path.Combine(StagingDir, k + Ext), dst = Path.Combine(dest, k + Ext);
                if (File.Exists(src) && !File.Exists(dst)) File.Copy(src, dst);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // --- file format --------------------------------------------------------
    // u32 magic "NPK1", i32 version, i32 block frames, f64 bpm, i64 float count, f32[count].

    private static AudioAnalysis? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var r = new BinaryReader(File.OpenRead(path));
            if (r.ReadUInt32() != Magic || r.ReadInt32() != FormatVersion) return null;
            if (r.ReadInt32() != 512) return null;   // must match SampleBuffer::kPeakBlock
            double bpm = r.ReadDouble();
            long count = r.ReadInt64();
            if (count <= 0 || count > int.MaxValue || count * sizeof(float) > r.BaseStream.Length) return null;
            var table = new float[count];
            var bytes = r.ReadBytes((int)count * sizeof(float));
            if (bytes.Length != count * sizeof(float)) return null;
            Buffer.BlockCopy(bytes, 0, table, 0, bytes.Length);
            return new AudioAnalysis(table, bpm);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void Write(string path, AudioAnalysis a)
    {
        string tmp = path + ".tmp";
        using (var w = new BinaryWriter(File.Create(tmp)))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(512);
            w.Write(a.Bpm);
            w.Write((long)a.PeakTable.Length);
            var bytes = new byte[a.PeakTable.Length * sizeof(float)];
            Buffer.BlockCopy(a.PeakTable, 0, bytes, 0, bytes.Length);
            w.Write(bytes);
        }
        File.Move(tmp, path, overwrite: true);
    }

    // The staging folder is shared by every unsaved session, so it would only grow: drop
    // entries nobody has written in a month.
    private static void PruneStaging()
    {
        try
        {
            var cutoff = DateTime.UtcNow - StagingMaxAge;
            foreach (var f in Directory.EnumerateFiles(StagingDir, "*" + Ext))
                if (File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
