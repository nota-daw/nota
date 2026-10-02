// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Content-addressed names for the binary files of a `.nota` bundle: a sample is
// stored as `samples/<hash>.wav` and a plugin state blob as `plugin-states/<hash>.bin`,
// where <hash> is the first 128 bits of the SHA-256 of the content. The same content
// always gets the same name, so a save skips files already on disk, identical buffers
// dedup to one file, and names stay stable across sessions (the ground for project
// version history).

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Nota.Infrastructure;

internal static partial class BundleContent
{
    public const string SamplesDir = "samples";
    public const string StatesDir = "plugin-states";

    private const int HashBytes = 16;   // 32 hex chars

    // Engine sample id -> content hash. Sample ids are process-unique (never reused, see
    // SampleBuffer::nextId) and a buffer's audio is fixed once it is in the graph, so an
    // entry stays valid for the whole process; frames/channels guard against surprises.
    private static readonly ConcurrentDictionary<long, (long Frames, int Channels, string Hash)> SampleHashes = new();

    public static string SampleRel(string hash) => $"{SamplesDir}/{hash}.wav";
    public static string StateRel(string hash) => $"{StatesDir}/{hash}.bin";

    /// <summary>Content hash of a live engine sample, or null if it is missing or empty.
    /// Hashing reads the whole buffer, so the result is cached per sample id.</summary>
    public static string? SampleHash(IAudioEngine engine, long sampleId, in NotaSampleInfo info)
    {
        if (SampleHashes.TryGetValue(sampleId, out var c) && c.Frames == info.Frames && c.Channels == info.Channels)
            return c.Hash;
        var data = engine.ReadSample(sampleId);
        if (data.Length == 0) return null;

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData("nota-sample-v1"u8);
        sha.AppendData(BitConverter.GetBytes(info.Channels));
        sha.AppendData(BitConverter.GetBytes(WavSampleRate(info)));
        sha.AppendData(BitConverter.GetBytes(info.Frames));
        sha.AppendData(MemoryMarshal.AsBytes(data.AsSpan()));
        string hash = Hex(sha.GetHashAndReset());
        SampleHashes[sampleId] = (info.Frames, info.Channels, hash);
        return hash;
    }

    /// <summary>After a load, record that <paramref name="sampleId"/> came from the
    /// content-named file <paramref name="relative"/>, so the next save reuses the name
    /// instead of re-hashing the audio. No-op for legacy names (<c>sample-N.wav</c>).</summary>
    public static void SeedSample(IAudioEngine engine, long sampleId, string relative)
    {
        if (sampleId <= 0 || !engine.TryGetSampleInfo(sampleId, out var info)) return;
        var m = SampleNameRx().Match(relative);
        if (m.Success) SampleHashes[sampleId] = (info.Frames, info.Channels, m.Groups[1].Value);
    }

    public static string BlobHash(byte[] blob) => Hex(SHA256.HashData(blob));

    /// <summary>The sample rate a sample is written to WAV with (and hashed with).</summary>
    public static int WavSampleRate(in NotaSampleInfo info)
        => info.SampleRate > 0 ? (int)Math.Round(info.SampleRate) : 44100;

    /// <summary>Writes <paramref name="path"/> through a temp file and a rename, so a crash
    /// mid-write never leaves a truncated file under a content name (which a later save
    /// would trust and skip).</summary>
    public static void WriteAtomic(string path, Action<string> write)
    {
        string tmp = path + ".tmp";
        try
        {
            write(tmp);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
        }
    }

    /// <summary>Deletes files in <c>samples/</c> and <c>plugin-states/</c> that neither the
    /// saved document nor any version in the project's history references: superseded
    /// content, legacy <c>sample-N.wav</c> / <c>state-N.bin</c> names, and temp files left by
    /// an interrupted save. Prunes nothing when the history can't be read.</summary>
    public static void PruneUnreferenced(string bundleDir, ProjectDocument doc)
    {
        if (ProjectHistory.PinnedBinaries(bundleDir) is not { } pinned) return;
        bool Keep(string rel) => doc.SampleRefs.ContainsKey(rel) || doc.StateBlobs.ContainsKey(rel) || pinned.Contains(rel);
        foreach (var relDir in new[] { SamplesDir, StatesDir })
        {
            string dir = Path.Combine(bundleDir, relDir);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                string name = Path.GetFileName(file);
                if (!IsSaveOutput(name, relDir) || Keep($"{relDir}/{name}")) continue;
                try { File.Delete(file); } catch { /* in use or read-only: leave it for the next save */ }
            }
        }
    }

    /// <summary>Is <paramref name="name"/> (in <paramref name="relDir"/>) a file a save writes —
    /// a sample / state or its temp? Anything else in those folders isn't ours to delete.</summary>
    public static bool IsSaveOutput(string name, string relDir)
    {
        string ext = relDir == SamplesDir ? ".wav" : ".bin";
        return name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(ext + ".tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static string Hex(ReadOnlySpan<byte> hash) => Convert.ToHexString(hash[..HashBytes]).ToLowerInvariant();

    [GeneratedRegex(@"^samples/([0-9a-f]{32})\.wav$")]
    private static partial Regex SampleNameRx();
}
