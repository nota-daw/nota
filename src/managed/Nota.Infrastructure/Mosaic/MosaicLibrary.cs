// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic's samples and presets on disk:
//
//   • Path roots. Programs reference samples as "samples:<rel>" (the user's Samples folder,
//     where registry packs install) or "data:<rel>" (Nota's data folder, where the factory
//     multisamples render) — the engine resolves them against the roots set here, at start
//     and whenever Settings move the Samples folder.
//   • Factory multisamples (MosaicSources): rendered into <data>/mosaic-sources/<id>/ the
//     first time a preset needs one, then mapped by their file names into a program.
//   • Pack presets: for every installed sample pack, one preset per instrument it holds —
//     each of its SFZ programs (the ones no other SFZ #includes), or, without SFZ, every
//     instrument the file-name mapper finds — in <presets>/Mosaic Packs/<Pack>/, written
//     once per pack version. "Create multisample" saves there too.

using System.Text.Json;
using Nota.Application.Mosaic;
using Nota.Infrastructure.Kits;

namespace Nota.Infrastructure.Mosaic;

public static class MosaicRoots
{
    /// <summary>Points the engine's "samples" and "data" roots at the current folders, and keeps
    /// them there when Settings change.</summary>
    public static void Install(ISettingsService settings)
    {
        void Apply()
        {
            try { NotaEngine.SetPathRoot(MosaicPaths.SamplesRoot, settings.ResolvedSamplesFolder()); } catch { }
            NotaEngine.SetPathRoot(MosaicPaths.DataRoot, NotaPaths.DataDir);
        }
        Apply();
        settings.Changed += Apply;
    }
}

public static class MosaicSourceLibrary
{
    private const int RendererVersion = 1;
    private static readonly object Gate = new();

    public static string Root => NotaPaths.SubDir("mosaic-sources");
    public static string FolderOf(MosaicSource s) => Path.Combine(Root, s.Id);
    private static string StampOf(MosaicSource s) => Path.Combine(FolderOf(s), ".stamp");
    private static string StampFor(MosaicSource s) => $"v{RendererVersion}-{s.Id}-r{s.Rev}-{s.Files}";

    public static bool IsRendered(MosaicSource s)
    {
        try { return File.Exists(StampOf(s)) && File.ReadAllText(StampOf(s)).Trim() == StampFor(s); }
        catch { return false; }
    }

    /// <summary>The source's folder, rendering it first if missing or stale; null on failure.</summary>
    public static string? Ensure(string id, IProgress<double>? progress = null)
    {
        var s = MosaicSources.ById(id);
        if (s is null) return null;
        lock (Gate)
        {
            if (IsRendered(s)) return FolderOf(s);
            try
            {
                string dir = FolderOf(s);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                var rng = new Rng(Fnv(s.Id));
                int total = s.Files, done = 0;
                foreach (int root in s.Roots)
                {
                    for (int li = 0; li < s.Layers.Length; li++)
                        for (int rr = 1; rr <= Math.Max(1, s.RoundRobin); rr++)
                        {
                            string name = $"{s.Id}_{NoteToken(root)}_{s.Layers[li]}{(s.RoundRobin > 1 ? $"_rr{rr}" : "")}.wav";
                            Write(Path.Combine(dir, name), s.Render(root, li, rr, false, rng));
                            progress?.Report(++done / (double)total);
                        }
                    if (s.Releases)
                    {
                        Write(Path.Combine(dir, $"{s.Id}_{NoteToken(root)}_rel.wav"), s.Render(root, 0, 1, true, rng));
                        progress?.Report(++done / (double)total);
                    }
                }
                File.WriteAllText(StampOf(s), StampFor(s));
                return dir;
            }
            catch { return null; }
        }
    }

    /// <summary>The program for a factory source (rendering it first), or null.</summary>
    public static MosaicProgram? Program(string id, IProgress<double>? progress = null)
    {
        var s = MosaicSources.ById(id);
        string? dir = Ensure(id, progress);
        if (s is null || dir is null) return null;
        NotaEngine.SetPathRoot(MosaicPaths.DataRoot, NotaPaths.DataDir);   // its files are "data:" references
        var prop = MultisampleMapper.MapFolder(dir, new MultisampleOptions { OctaveShift = 0, EdgeStretch = 12, RrMode = 2 });
        var inst = prop.Instruments.FirstOrDefault();
        if (inst is null) return null;
        var p = MultisampleMapper.Build(inst, new MultisampleOptions { RrMode = 2 },
            path => MosaicPaths.ToRef(path, null, NotaPaths.DataDir), s.Name);
        p.SourceKind = "factory"; p.SourceRef = s.Id;
        if (s.Loop is { } loop)
            foreach (var z in p.Zones)
            {
                z.LoopMode = 1;
                z.LoopStart = loop.Start * MosaicSources.Rate; z.LoopEnd = loop.End * MosaicSources.Rate;
                z.Crossfade = 0.25;
            }
        return p;
    }

    private static string NoteToken(int n)
    {
        string[] pc = { "c", "c#", "d", "d#", "e", "f", "f#", "g", "g#", "a", "a#", "b" };
        return pc[n % 12] + (n / 12 - 1);
    }

    private static void Write(string path, (double[] L, double[] R) b)
    {
        int n = b.L.Length;
        var inter = new float[n * 2];
        for (int i = 0; i < n; i++) { inter[2 * i] = (float)Math.Clamp(b.L[i], -1, 1); inter[2 * i + 1] = (float)Math.Clamp(b.R[i], -1, 1); }
        string tmp = path + ".part";
        using (var w = new WavWriter(tmp, MosaicSources.Rate, 2, WavBitDepth.Pcm16)) w.WriteFrames(inter, n);
        File.Move(tmp, path, overwrite: true);
    }

    private static uint Fnv(string s) { uint h = 2166136261; foreach (char c in s) { h ^= c; h *= 16777619; } return h; }
}

public sealed class MosaicPackLibrary : IMosaicPacks
{
    private const string Stamp = ".nota-mosaic";
    private const int GeneratorVersion = 1;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly ISettingsService? _settings;
    private readonly ISampleStore? _store;
    private readonly string? _root;
    private readonly SemaphoreSlim _scan = new(1, 1);

    public MosaicPackLibrary(ISettingsService settings, ISampleStore store) { _settings = settings; _store = store; }

    /// <summary>For tests: a fixed root, no store.</summary>
    public MosaicPackLibrary(string root) { _root = root; }

    public event Action? Changed;

    public string Root
    {
        get
        {
            string r = _root ?? Path.Combine(_settings!.PresetsFolder(), "Mosaic Packs");
            try { Directory.CreateDirectory(r); } catch { }
            return r;
        }
    }

    private string? SamplesFolder { get { try { return _settings?.ResolvedSamplesFolder(); } catch { return null; } } }

    public string ToRef(string path) => MosaicPaths.ToRef(path, SamplesFolder, NotaPaths.DataDir);
    public string? Resolve(string reference) => MosaicPaths.Resolve(reference, SamplesFolder, NotaPaths.DataDir);

    public IReadOnlyList<MosaicPackPreset> Presets()
    {
        var list = new List<MosaicPackPreset>();
        string root = Root;
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch { return list; }
        foreach (var dir in dirs)
        {
            string folder = Path.GetFileName(dir);
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*" + PresetService.Extension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch { continue; }
            foreach (var f in files)
            {
                string source = "";
                try
                {
                    var doc = PresetService.Load(f);
                    if (doc.BuiltinKind != MosaicModel.Kind) continue;
                    source = MosaicProgram.Parse(doc.MosaicProgram).SourceKind;
                    list.Add(new MosaicPackPreset(f, doc.DisplayName.Length > 0 ? doc.DisplayName : Path.GetFileNameWithoutExtension(f), folder, source));
                }
                catch { /* not ours / corrupt */ }
            }
        }
        return list;
    }

    public string ApplyInPlace(IAudioEngine engine, string path, int trackId)
    {
        try { return PresetService.ApplyInPlace(PresetService.Load(path), engine, trackId, -1); }
        catch (Exception e) { return $"Couldn't load the preset: {e.Message}"; }
    }

    public MosaicProgram? Read(string path)
    {
        try
        {
            var doc = PresetService.Load(path);
            return doc.BuiltinKind == MosaicModel.Kind && doc.MosaicProgram is { Length: > 0 } t ? MosaicProgram.Parse(t) : null;
        }
        catch { return null; }
    }

    public string Save(MosaicProgram program, string name, string folder, IReadOnlyDictionary<string, float>? param = null)
    {
        var doc = new PresetDocument
        {
            DisplayName = name, Type = "builtin-instrument", DeviceName = "Nota Mosaic", BuiltinKind = MosaicModel.Kind,
            NamedParams = param is null ? new Dictionary<string, float>() : new Dictionary<string, float>(param),
            MosaicProgram = program.Serialize(),
        };
        return PresetService.Save(doc, Path.Combine(Root, Sanitize(folder)));
    }

    public double? DetectNote(string path)
    {
        var mono = NotaEngine.DecodeMono(path, 1.5, out double sr);
        if (mono is null || sr <= 0 || mono.Length < 8192) return null;
        // Past the attack: a quarter in, or 50 ms, whichever is later.
        int start = Math.Max((int)(0.05 * sr), mono.Length / 4);
        int win = 2048;
        if (start + win + (int)(sr / 30) >= mono.Length) start = Math.Max(0, mono.Length - win - (int)(sr / 30) - 1);
        double hz = AudioPitch.YinHz(mono, start, win, sr, 0.15, 30, 2000);
        return hz > 0 ? 69 + 12 * Math.Log2(hz / 440.0) : null;
    }

    // ---- packs → presets -------------------------------------------------------------------------
    public async Task<int> ScanAsync(CancellationToken ct = default)
    {
        if (_store is null) return 0;
        await _scan.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            int written = 0;
            foreach (var pack in _store.Installed)
            {
                ct.ThrowIfCancellationRequested();
                written += await Task.Run(() => ScanPack(pack.Path, pack.Id, pack.Version), ct).ConfigureAwait(false);
            }
            if (written > 0) Changed?.Invoke();
            return written;
        }
        finally { _scan.Release(); }
    }

    /// <summary>Writes the presets for one pack folder unless its stamp says they're current.</summary>
    public int ScanPack(string packPath, string packId, string version)
    {
        string packName = Path.GetFileName(packPath.TrimEnd('/', '\\'));
        string dir = Path.Combine(Root, Sanitize(packName));
        string stampPath = Path.Combine(dir, Stamp);
        string want = $"v{GeneratorVersion} {packId} {version}";
        try { if (File.Exists(stampPath) && File.ReadAllText(stampPath).Trim() == want) return 0; } catch { }
        var made = Generate(packPath, packId, packName);
        if (made.Count == 0) return 0;
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.EnumerateFiles(dir, "*" + PresetService.Extension)) { try { File.Delete(f); } catch { } }
        int n = 0;
        foreach (var (name, prog, param) in made) { Save(prog, name, packName, param); n++; }
        File.WriteAllText(stampPath, want);
        return n;
    }

    /// <summary>The instruments in a pack folder: its SFZ programs, else the mapped file names.</summary>
    public List<(string Name, MosaicProgram Program, Dictionary<string, float> Params)> Generate(string packPath, string packId, string packName)
    {
        var made = new List<(string, MosaicProgram, Dictionary<string, float>)>();
        var sfz = TopLevelSfz(packPath);
        foreach (var path in sfz)
        {
            SfzResult res;
            try { res = SfzImporter.Import(path, null, ToRef); } catch { continue; }
            if (res.Program.Zones.Count == 0) continue;
            string stem = Path.GetFileNameWithoutExtension(path);
            string name = sfz.Count == 1 || stem.Equals("main", StringComparison.OrdinalIgnoreCase) || stem.Equals(packName, StringComparison.OrdinalIgnoreCase)
                ? packName : MultisampleMapper.Pretty(stem.Replace(' ', '_'));
            res.Program.Name = name; res.Program.SourceRef = ToRef(path); res.Program.PackId = packId; res.Program.PackName = packName;
            made.Add((name, res.Program, ParamsFrom(res.Report)));
        }
        if (made.Count > 0) return made;

        var prop = MultisampleMapper.MapFolder(packPath, null, DetectNote);
        var instruments = prop.Instruments.Where(i => i.Roots >= 2).ToList();
        foreach (var inst in instruments)
        {
            string name = instruments.Count == 1 ? packName : MultisampleMapper.Pretty(inst.Stem);
            var p = MultisampleMapper.Build(inst, null, ToRef, name);
            p.SourceKind = "folder"; p.SourceRef = ToRef(packPath); p.PackId = packId; p.PackName = packName;
            made.Add((name, p, new Dictionary<string, float>()));
        }
        return made;
    }

    // The SFZ files no other SFZ in the pack #includes (an instrument, not a fragment of one).
    private static List<string> TopLevelSfz(string root)
    {
        List<string> all;
        try { all = Directory.EnumerateFiles(root, "*.sfz", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch { return new List<string>(); }
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rx = new System.Text.RegularExpressions.Regex("#include\\s+\"([^\"]+)\"");
        foreach (var f in all)
        {
            string text;
            try { text = File.ReadAllText(f); } catch { continue; }
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(text))
                included.Add(Path.GetFileName(m.Groups[1].Value.Replace('\\', '/')));
        }
        return all.Where(f => !included.Contains(Path.GetFileName(f))).ToList();
    }

    // The envelope / bend / filter the SFZ names, as Mosaic params.
    private static Dictionary<string, float> ParamsFrom(SfzImportReport r)
    {
        var d = new Dictionary<string, float>();
        if (r.Attack is { } a) d["attack"] = MosaicModel.AttackNorm(a);
        if (r.Decay is { } dc) d["decay"] = MosaicModel.DecayNorm(dc);
        if (r.Sustain is { } s) d["sustain"] = (float)s;
        if (r.Release is { } rel) d["release"] = MosaicModel.DecayNorm(rel);
        if (r.BendSemis is { } b) d["bendrange"] = (float)Math.Clamp(b / 24.0, 0, 1);
        if (r.CutoffHz is { } c && c < 18000)
        {
            d["filtertype"] = 1f / 3f;
            d["cutoff"] = (float)Math.Clamp(Math.Log(Math.Max(20, c) / 20.0) / Math.Log(1000.0), 0, 1);
        }
        return d;
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim().Length == 0 ? "Pack" : name.Trim();
    }
}
