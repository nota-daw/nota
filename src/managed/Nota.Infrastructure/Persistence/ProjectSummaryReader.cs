// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The browser's Projects tab details (spec 092, #582) — tempo, key, length, track count —
// read straight from a bundle's project.json without binding the whole document or
// touching the engine, and cached by the manifest's write time and size so a folder of
// projects is read once. The cache is disposable: missing or corrupt, it is rebuilt.

using System.Text.Json;
using Nota.Application.Browser;

namespace Nota.Infrastructure;

public sealed class ProjectSummaryReader : IProjectSummaries
{
    private sealed class Entry
    {
        public string Stamp { get; set; } = "";
        public double Bpm { get; set; }
        public string? Key { get; set; }
        public int Tracks { get; set; }
        public double LengthBeats { get; set; }
    }

    private readonly string _cacheFile;
    private readonly object _gate = new();
    private Dictionary<string, Entry>? _cache;

    public ProjectSummaryReader() : this(Path.Combine(NotaPaths.SubDir("cache"), "project-summaries.json")) { }

    /// <summary>Tests: a cache kept in <paramref name="cacheFile"/>.</summary>
    public ProjectSummaryReader(string cacheFile) { _cacheFile = cacheFile; }

    public ProjectSummary? Get(string bundlePath)
    {
        var manifest = Path.Combine(bundlePath, ProjectDates.ManifestName);
        FileInfo fi;
        try { fi = new FileInfo(manifest); if (!fi.Exists) return null; }
        catch { return null; }
        string stamp = $"{fi.LastWriteTimeUtc.Ticks}:{fi.Length}";

        lock (_gate)
        {
            var cache = Load();
            if (cache.TryGetValue(bundlePath, out var hit) && hit.Stamp == stamp)
                return new ProjectSummary(hit.Bpm, hit.Key, hit.Tracks, hit.LengthBeats);
        }

        var sum = Read(bundlePath);
        if (sum is null) return null;
        lock (_gate)
        {
            var cache = Load();
            cache[bundlePath] = new Entry { Stamp = stamp, Bpm = sum.Bpm, Key = sum.Key, Tracks = sum.TrackCount, LengthBeats = sum.LengthBeats };
            Save(cache);
        }
        return sum;
    }

    /// <summary>Reads the summary of one bundle (no cache); null when its manifest is missing
    /// or not a project.</summary>
    public static ProjectSummary? Read(string bundlePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(bundlePath, ProjectDates.ManifestName)));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            double bpm = 120;
            string? key = null;
            if (Prop(root, "transport") is { ValueKind: JsonValueKind.Object } tr)
            {
                if (Prop(tr, "bpm") is { ValueKind: JsonValueKind.Number } b) bpm = b.GetDouble();
                if (Prop(tr, "key") is { ValueKind: JsonValueKind.String } k && k.GetString() is { Length: > 0 } ks) key = ks;
            }
            int tracks = 0;
            double end = 0;
            if (Prop(root, "tracks") is { ValueKind: JsonValueKind.Array } arr)
            {
                foreach (var t in arr.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    int type = Prop(t, "type") is { ValueKind: JsonValueKind.Number } ty ? ty.GetInt32() : 0;
                    if (type != 2) tracks++;   // return tracks are buses, not tracks you make music on
                    if (Prop(t, "midiClips") is { ValueKind: JsonValueKind.Array } mc)
                        foreach (var c in mc.EnumerateArray())
                            end = Math.Max(end, Num(c, "startBeat") + Num(c, "lengthBeats"));
                    if (Prop(t, "audioClips") is { ValueKind: JsonValueKind.Array } ac)
                        foreach (var c in ac.EnumerateArray())
                            end = Math.Max(end, Num(c, "startBeat") + AudioBeats(bundlePath, c, bpm));
                }
            }
            return new ProjectSummary(bpm, key, tracks, end);
        }
        catch { return null; }
    }

    // How many beats an audio clip covers: a warped clip says so in beats; an unwarped one
    // plays its frames at the file's rate (read from the WAV header when the clip runs to
    // the end of its sample).
    private static double AudioBeats(string bundle, JsonElement c, double bpm)
    {
        if (Num(c, "warpEnabled") != 0)
        {
            double ps = Num(c, "warpPlayStart"), pe = Num(c, "warpPlayEnd");
            return pe > 0 ? Math.Max(0, pe - ps) : Num(c, "warpBeats");
        }
        long frames = (long)Num(c, "lengthFrames");
        string? sample = Prop(c, "sample") is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
        var (rate, total) = sample is { Length: > 0 } ? WavInfo(Path.Combine(bundle, sample)) : (0, 0L);
        if (frames <= 0) frames = Math.Max(0, total - (long)Num(c, "sourceOffsetFrames"));
        if (rate <= 0) rate = 48000;
        return frames / (double)rate * bpm / 60.0;
    }

    // Sample rate and frame count from a WAV header; (0, 0) when it isn't one.
    internal static (int Rate, long Frames) WavInfo(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            using var r = new BinaryReader(f);
            if (new string(r.ReadChars(4)) != "RIFF") return (0, 0);
            r.ReadInt32();
            if (new string(r.ReadChars(4)) != "WAVE") return (0, 0);
            int rate = 0, blockAlign = 0;
            while (f.Position + 8 <= f.Length)
            {
                string id = new(r.ReadChars(4));
                uint size = r.ReadUInt32();
                if (id == "fmt ")
                {
                    long start = f.Position;
                    r.ReadInt16(); r.ReadInt16();
                    rate = r.ReadInt32();
                    r.ReadInt32();
                    blockAlign = r.ReadInt16();
                    f.Position = start + size;
                }
                else if (id == "data")
                    return blockAlign > 0 ? (rate, size / blockAlign) : (rate, 0);
                else f.Position += size + (size & 1);
            }
        }
        catch { }
        return (0, 0);
    }

    private static JsonElement? Prop(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    private static double Num(JsonElement e, string name)
        => Prop(e, name) switch
        {
            { ValueKind: JsonValueKind.Number } n => n.GetDouble(),
            { ValueKind: JsonValueKind.True } => 1,
            _ => 0,
        };

    // --- cache --------------------------------------------------------------------------------

    private Dictionary<string, Entry> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (File.Exists(_cacheFile))
                _cache = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_cacheFile));
        }
        catch { _cache = null; }
        return _cache ??= new Dictionary<string, Entry>();
    }

    // Written after each new read; bundles that are gone are dropped on the way.
    private void Save(Dictionary<string, Entry> cache)
    {
        try
        {
            foreach (var gone in cache.Keys.Where(k => !Directory.Exists(k)).ToList()) cache.Remove(gone);
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            File.WriteAllText(_cacheFile, JsonSerializer.Serialize(cache));
        }
        catch { /* a cache — best effort */ }
    }
}
