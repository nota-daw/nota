// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The sample library's analysis index (ISampleIndex). A background scan walks the Samples
// folder and has the engine measure every audio file it doesn't know yet — tempo, key,
// envelope, timbre — on a few below-normal-priority threads, a just-installed pack first.
// Results persist in <data>/sample-index/index.bin, keyed by path and invalidated by size or
// date, so a library of tens of thousands of files is analysed once, not on every launch.
// Only the raw analysis is stored: what it means (loop or one-shot, which tempo to trust)
// is worked out again from the name on load, so classifier fixes need no re-analysis.

using System.Collections.Concurrent;
using Nota.Application;
using Nota.Application.Samples;

namespace Nota.Infrastructure;

public sealed class SampleAnalyzer : ISampleAnalyzer
{
    public SampleAnalysis? Analyze(string path, double maxSeconds)
    {
        try
        {
            return NativeMethods.SampleAnalyzeFile(path, maxSeconds, out var a) == NativeMethods.NotaResult.Ok
                ? a.ToAnalysis() : null;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }
}

public sealed class SampleLibraryIndex : ISampleIndex, IDisposable
{
    /// <summary>Seconds of a file the analysis decodes — a loop's worth and then some; a long
    /// recording's tempo and key show well before the end.</summary>
    public const double AnalyzeSeconds = 60;

    private static readonly string[] AudioExts = { ".wav", ".flac", ".mp3" };
    private const uint Magic = 0x3158534E;   // "NSX1"
    private const int FileVersion = 1;
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NotifyEvery = TimeSpan.FromMilliseconds(750);

    // Timbre weights for "similar sounds": the band shape is the sound's colour; brightness,
    // noisiness and attack separate a clap from a rim; length keeps hits away from loops.
    private static readonly float[] TimbreWeights = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2.5f, 2f, 2f, 1.5f };

    private sealed class Entry
    {
        public long Size;
        public long Ticks;
        public SampleAnalysis? Analysis;   // null = couldn't be decoded
        public SampleInfo? Info;           // derived, cleared when the root changes
    }

    private readonly ISampleAnalyzer _analyzer;
    private readonly string _file;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(PathComparer);
    private readonly List<string> _priority = new();
    private bool _loaded;
    private string? _root;
    private CancellationTokenSource? _scanCts;
    private int _scanGen;
    private int _done, _total;
    private int _mutations;                 // bumps on every change; keys the stats cache
    private (int Mutations, float[] Mean, float[] Std)? _stats;
    private DateTime _lastNotify = DateTime.MinValue;
    private DateTime _lastSave = DateTime.UtcNow;
    private bool _dirty;

    public event Action? Changed;

    private static StringComparer PathComparer
        => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public SampleLibraryIndex(ISampleAnalyzer analyzer) : this(analyzer, Path.Combine(NotaPaths.SubDir("sample-index"), "index.bin")) { }

    /// <summary>Tests: an index persisted at <paramref name="file"/>.</summary>
    public SampleLibraryIndex(ISampleAnalyzer analyzer, string file)
    {
        _analyzer = analyzer;
        _file = file;
    }

    public (int Done, int Total) Progress { get { lock (_gate) return (_done, _total); } }

    public SampleInfo? Get(string path)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _entries.TryGetValue(path, out var e) ? InfoOf(path, e) : null;
        }
    }

    public SampleInfo? GetOrAnalyze(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) return null;
        lock (_gate)
        {
            EnsureLoaded();
            if (_entries.TryGetValue(path, out var e) && Fresh(e, fi)) return e.Analysis is null ? null : InfoOf(path, e);
        }
        var a = _analyzer.Analyze(path, AnalyzeSeconds);
        return a is null ? null : Store(path, a);
    }

    public SampleInfo Store(string path, SampleAnalysis analysis)
    {
        var fi = new FileInfo(path);
        var e = new Entry { Size = fi.Exists ? fi.Length : -1, Ticks = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0, Analysis = analysis };
        SampleInfo info;
        lock (_gate)
        {
            EnsureLoaded();
            _entries[path] = e;
            info = InfoOf(path, e);
            _mutations++;
            _dirty = true;
        }
        Notify(force: true);
        return info;
    }

    public void Watch(string root)
    {
        CancellationTokenSource cts;
        int gen;
        lock (_gate)
        {
            if (_root is null || !PathComparer.Equals(_root, root))
            {
                _root = root;
                foreach (var e in _entries.Values) e.Info = null;   // folder hints are relative to the root
            }
            _scanCts?.Cancel();
            _scanCts = cts = new CancellationTokenSource();
            gen = ++_scanGen;
        }
        var thread = new Thread(() => Scan(root, gen, cts.Token)) { IsBackground = true, Name = "Sample index", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    public void Prioritize(string folder)
    {
        string? root;
        lock (_gate)
        {
            _priority.Remove(folder);
            _priority.Insert(0, folder);
            root = _root;
        }
        if (root is not null) Watch(root);   // re-scan: the new files sort to the front
    }

    public IReadOnlyList<SimilarSample> Similar(string path, int count)
    {
        var anchor = GetOrAnalyze(path);
        if (anchor is null || anchor.Timbre.Length != SampleAnalysis.TimbreDims) return Array.Empty<SimilarSample>();
        lock (_gate)
        {
            var (mean, std) = Stats();
            var a = Normalize(anchor.Timbre, mean, std);
            var best = new List<SimilarSample>();
            foreach (var (p, e) in _entries)
            {
                if (e.Analysis is not { Timbre.Length: SampleAnalysis.TimbreDims } || PathComparer.Equals(p, path)) continue;
                var info = InfoOf(p, e);
                // A loop is never "like" a hit, however close their spectra.
                if (anchor.Kind != SampleKind.Unknown && info.Kind != SampleKind.Unknown && anchor.Kind != info.Kind) continue;
                double d = Distance(a, Normalize(info.Timbre, mean, std));
                best.Add(new SimilarSample(info, d));
            }
            best.Sort((x, y) => x.Distance.CompareTo(y.Distance));
            return best.Count > count ? best.GetRange(0, count) : best;
        }
    }

    public IReadOnlyList<SampleInfo> Search(SampleFilter filter, string? text, int limit)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hits = new List<SampleInfo>();
        lock (_gate)
        {
            EnsureLoaded();
            foreach (var (p, e) in _entries)
            {
                if (e.Analysis is null) continue;
                if (words.Any(w => !p.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                var info = InfoOf(p, e);
                if (filter.Matches(info)) hits.Add(info);
            }
        }
        hits.Sort((x, y) => string.Compare(x.Path, y.Path, StringComparison.OrdinalIgnoreCase));
        return hits.Count > limit ? hits.GetRange(0, limit) : hits;
    }

    public void Dispose()
    {
        lock (_gate) { _scanCts?.Cancel(); }
        Save();
    }

    // --- scan -----------------------------------------------------------------------------

    private void Scan(string root, int gen, CancellationToken ct)
    {
        try
        {
            List<string> pending;
            lock (_gate) EnsureLoaded();
            WarmInfos(gen, ct);
            var found = Enumerate(root, ct);
            var present = new HashSet<string>(found, PathComparer);
            var stamps = new Dictionary<string, (long Size, long Ticks)>(found.Count, PathComparer);
            foreach (var p in found)
            {
                ct.ThrowIfCancellationRequested();
                var fi = new FileInfo(p);
                if (fi.Exists) stamps[p] = (fi.Length, fi.LastWriteTimeUtc.Ticks);
            }
            lock (_gate)
            {
                if (gen != _scanGen) return;
                // Forget files that left the library (anything outside it, dragged in from
                // elsewhere, stays until its file is gone).
                var gone = _entries.Keys.Where(p => IsUnder(p, root) ? !present.Contains(p) : !File.Exists(p)).ToList();
                foreach (var p in gone) _entries.Remove(p);
                if (gone.Count > 0) { _mutations++; _dirty = true; }

                pending = new List<string>();
                foreach (var (p, st) in stamps)
                    if (!_entries.TryGetValue(p, out var e) || e.Size != st.Size || e.Ticks != st.Ticks) pending.Add(p);
                var priority = _priority.ToList();
                pending.Sort((a, b) =>
                {
                    int pa = PriorityOf(a, priority), pb = PriorityOf(b, priority);
                    return pa != pb ? pa.CompareTo(pb) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                });
                _done = 0;
                _total = pending.Count;
            }
            if (pending.Count > 0) Notify(force: true);

            int next = -1;
            int workers = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            var threads = new List<Thread>();
            for (int w = 0; w < workers; w++)
            {
                var t = new Thread(() =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= pending.Count) break;
                        Analyze(pending[i], gen);
                    }
                }) { IsBackground = true, Name = "Sample analysis", Priority = ThreadPriority.BelowNormal };
                threads.Add(t);
                t.Start();
            }
            foreach (var t in threads) t.Join();
            if (ct.IsCancellationRequested) return;
            lock (_gate)
            {
                if (gen != _scanGen) return;
                _done = _total = 0;
                _priority.Clear();
            }
            Save();
            Notify(force: true);
        }
        catch (Exception) when (ct.IsCancellationRequested) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Classify what loaded from disk here, a few hundred at a time, so the browser's first
    // pass over tens of thousands of rows is lookups, not name parsing on the UI thread.
    private void WarmInfos(int gen, CancellationToken ct)
    {
        string[] paths;
        lock (_gate) paths = _entries.Keys.ToArray();
        for (int i = 0; i < paths.Length; i += 500)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (gen != _scanGen) return;
                for (int k = i; k < Math.Min(paths.Length, i + 500); k++)
                    if (_entries.TryGetValue(paths[k], out var e)) InfoOf(paths[k], e);
            }
        }
        Notify(force: true);
    }

    private void Analyze(string path, int gen)
    {
        FileInfo fi;
        SampleAnalysis? a;
        try
        {
            fi = new FileInfo(path);
            if (!fi.Exists) return;
            a = _analyzer.Analyze(path, AnalyzeSeconds);
        }
        catch (IOException) { return; }
        lock (_gate)
        {
            _entries[path] = new Entry { Size = fi.Length, Ticks = fi.LastWriteTimeUtc.Ticks, Analysis = a };
            _mutations++;
            _dirty = true;
            if (gen == _scanGen) _done++;
        }
        if (DateTime.UtcNow - _lastSave > SaveEvery) Save();
        Notify(force: false);
    }

    private static int PriorityOf(string path, List<string> priority)
    {
        for (int i = 0; i < priority.Count; i++) if (IsUnder(path, priority[i])) return i;
        return priority.Count;
    }

    private static bool IsUnder(string path, string folder)
    {
        var rel = Path.GetRelativePath(folder, path);
        return rel != "." && !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
    }

    private static List<string> Enumerate(string root, CancellationToken ct)
    {
        var files = new List<string>();
        if (!Directory.Exists(root)) return files;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        foreach (var f in Directory.EnumerateFiles(root, "*", opts))
        {
            ct.ThrowIfCancellationRequested();
            if (AudioExts.Contains(Path.GetExtension(f).ToLowerInvariant())) files.Add(f);
        }
        return files;
    }

    private void Notify(bool force)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (!force && now - _lastNotify < NotifyEvery) return;
            _lastNotify = now;
        }
        Changed?.Invoke();
    }

    // --- derived ----------------------------------------------------------------------------

    private static bool Fresh(Entry e, FileInfo fi) => e.Size == fi.Length && e.Ticks == fi.LastWriteTimeUtc.Ticks;

    private SampleInfo InfoOf(string path, Entry e)
        => e.Info ??= SampleClassifier.Classify(path, SampleNameHints.Parse(path, _root), e.Analysis);

    // Per-dimension mean / spread over the library, so a dB band and a 0..1 flatness weigh alike.
    private (float[] Mean, float[] Std) Stats()
    {
        if (_stats is { } s && s.Mutations == _mutations) return (s.Mean, s.Std);
        int n = SampleAnalysis.TimbreDims;
        var mean = new double[n];
        var sq = new double[n];
        int count = 0;
        foreach (var e in _entries.Values)
        {
            if (e.Analysis is not { Timbre.Length: SampleAnalysis.TimbreDims } a) continue;
            for (int i = 0; i < n; i++) { mean[i] += a.Timbre[i]; sq[i] += a.Timbre[i] * (double)a.Timbre[i]; }
            count++;
        }
        var m = new float[n];
        var sd = new float[n];
        for (int i = 0; i < n; i++)
        {
            double mu = count > 0 ? mean[i] / count : 0;
            double v = count > 1 ? Math.Max(0, sq[i] / count - mu * mu) : 1;
            m[i] = (float)mu;
            sd[i] = (float)Math.Max(Math.Sqrt(v), 0.05);   // a dimension the library barely varies on can't dominate
        }
        _stats = (_mutations, m, sd);
        return (m, sd);
    }

    private static float[] Normalize(float[] t, float[] mean, float[] std)
    {
        var z = new float[t.Length];
        for (int i = 0; i < t.Length; i++) z[i] = (t[i] - mean[i]) / std[i];
        return z;
    }

    private static double Distance(float[] a, float[] b)
    {
        double sum = 0, wsum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            sum += TimbreWeights[i] * d * d;
            wsum += TimbreWeights[i];
        }
        return Math.Sqrt(sum / wsum);
    }

    // --- persistence --------------------------------------------------------------------------
    // u32 magic, i32 file version, i32 analysis version, i32 count, then per entry:
    // string path, i64 size, i64 ticks, bool analysed, [f64 dur, f64 sr, i32 ch, f64 bpm,
    // i32 tonic, i32 mode, f32 conf, f32 tail, f32 peak, f32 rms, i32 n, f32[n] timbre].

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(_file)) return;
            using var r = new BinaryReader(File.OpenRead(_file));
            if (r.ReadUInt32() != Magic || r.ReadInt32() != FileVersion || r.ReadInt32() != SampleAnalysis.Version) return;
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string path = r.ReadString();
                var e = new Entry { Size = r.ReadInt64(), Ticks = r.ReadInt64() };
                if (r.ReadBoolean())
                {
                    double dur = r.ReadDouble(), sr = r.ReadDouble();
                    int ch = r.ReadInt32();
                    double bpm = r.ReadDouble();
                    int tonic = r.ReadInt32(), mode = r.ReadInt32();
                    float conf = r.ReadSingle(), tail = r.ReadSingle(), peak = r.ReadSingle(), rms = r.ReadSingle();
                    int n = r.ReadInt32();
                    if (n is < 0 or > 64) return;   // corrupt: keep what loaded, rescan the rest
                    var t = new float[n];
                    for (int k = 0; k < n; k++) t[k] = r.ReadSingle();
                    e.Analysis = new SampleAnalysis(dur, sr, ch, bpm, tonic, mode, conf, tail, peak, rms, t);
                }
                _entries[path] = e;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Save()
    {
        KeyValuePair<string, Entry>[] snapshot;
        lock (_gate)
        {
            if (!_dirty || !_loaded) return;
            _dirty = false;
            _lastSave = DateTime.UtcNow;
            snapshot = _entries.ToArray();
        }
        string tmp = _file + "." + Environment.CurrentManagedThreadId + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write(Magic);
                w.Write(FileVersion);
                w.Write(SampleAnalysis.Version);
                w.Write(snapshot.Length);
                foreach (var (path, e) in snapshot)
                {
                    w.Write(path);
                    w.Write(e.Size);
                    w.Write(e.Ticks);
                    w.Write(e.Analysis is not null);
                    if (e.Analysis is not { } a) continue;
                    w.Write(a.DurationSec); w.Write(a.SampleRate); w.Write(a.Channels); w.Write(a.Bpm);
                    w.Write(a.KeyTonic); w.Write(a.KeyMode);
                    w.Write(a.KeyConfidence); w.Write(a.TailRatio); w.Write(a.PeakDb); w.Write(a.RmsDb);
                    w.Write(a.Timbre.Length);
                    foreach (var v in a.Timbre) w.Write(v);
                }
            }
            File.Move(tmp, _file, overwrite: true);
        }
        catch (IOException) { lock (_gate) _dirty = true; TryDelete(tmp); }
        catch (UnauthorizedAccessException) { lock (_gate) _dirty = true; TryDelete(tmp); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
