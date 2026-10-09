// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — Nota Mosaic (instrument kind 17), the multisample instrument: read it, shape its
// shared sound in musical units, edit zones and groups, import an SFZ, map a folder of samples
// by their file names, and list / load its presets (factory multisamples and pack presets).

using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using Nota.Application;
using Nota.Application.Mosaic;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class MosaicTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, IMosaicPacks packs, IFactoryPresets factory)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record GroupRow(int Index, string Name, int Zones, double GainDb, double TuneCents, int RoundRobin, string RoundRobinMode, bool Release);
    public sealed record ZoneRow(int Index, string File, string Root, string Keys, string Velocity, int Group, int Step, bool Release,
        double TuneCents, double GainDb, double Pan, string Loop);
    public sealed record MosaicReading(string Summary, string Guide, string Name, string Source, string Pack, string Loading,
        int Files, int MissingFiles, double RamMb, int Zones, int Roots, int VelocityLayers, int ReleaseZones,
        int ActiveVoices, int VoiceLimit, int PerKey, bool PedalDown, GroupRow[] Groups, ZoneRow[]? ZoneList);
    public sealed record SfzImport(int TrackId, string Summary, int Regions, int Imported, int Groups, int Includes, int Missing,
        string[] Ignored, string[] Controllers);
    public sealed record MappedRow(string File, string Note, string From, string Layer, int Rr, bool Release, int Confidence, bool Duplicate);
    public sealed record MappedInstrumentRow(string Name, int Files, int Roots, string[] Layers, int RoundRobin, int ReleaseSamples,
        int Confidence, string[] Gaps, MappedRow[] Rows);
    public sealed record MultisampleResult(string OctaveVerdict, int OctaveShift, MappedInstrumentRow[] Instruments, int TrackId, string[] Presets);
    public sealed record PresetRow(string Name, string Kind, string Folder);

    private float Get(int t, string id)
    {
        int pc = E.PluginParamCount(t, -1);
        for (int i = 0; i < pc; i++) if (E.PluginParamId(t, -1, i) == id) return E.PluginParamGet(t, -1, i);
        return 0f;
    }
    private void Set(int t, string id, double v)
    {
        int pc = E.PluginParamCount(t, -1);
        for (int i = 0; i < pc; i++) if (E.PluginParamId(t, -1, i) == id) { E.PluginParamSet(t, -1, i, (float)Math.Clamp(v, 0, 1)); return; }
    }
    private bool IsMosaic(int t) => E.TrackInstrumentKind(t) == MosaicModel.Kind;

    [McpServerTool(Name = "read_mosaic"), Description(
        "Read a Nota Mosaic track (built-in instrument kind 17, the multisample instrument). Returns a summary, a guide, the "
        + "program (name, source: sfz / folder / factory / sample, the registry pack it uses), loading state (loading / ready, "
        + "files, missing files, RAM in MB), zones / roots / velocity layers / release zones, the groups (gain, tune, round-robin "
        + "length and mode) and the voices (sounding / limit, strikes per key, pedal down). includeZones lists every zone "
        + "(index for set_mosaic_zone, file, root, key and velocity ranges, group, round-robin step, release, tune, gain, pan, loop).")]
    public Task<MosaicReading?> ReadMosaic(int trackId, bool includeZones = false) => Read<MosaicReading?>(() =>
    {
        if (!IsMosaic(trackId)) return null;
        var p = MosaicProgram.Parse(E.MosaicProgram(trackId));
        E.TryGetMosaicStatus(trackId, out var st);
        var sc = new float[MosaicModel.ScopeLength];
        int n = E.InstrumentScope(trackId, sc);
        var snap = new MosaicModel.Snapshot();
        MosaicModel.Parse(sc.AsSpan(0, Math.Max(0, n)), snap);
        string loading = st.FilesTotal == 0 ? "empty" : st.State == 1 ? $"loading {st.FilesDone}/{st.FilesTotal}" : "ready";
        var groups = p.Groups.Select((g, i) => new GroupRow(i, g.Name, p.Zones.Count(z => z.Group == i), g.GainDb, g.Tune, g.SeqLen,
            MosaicProgram.RrNames[Math.Clamp(g.RrMode, 0, 2)], p.Zones.Any(z => z.Group == i) && p.Zones.Where(z => z.Group == i).All(z => z.Release))).ToArray();
        ZoneRow[]? zones = includeZones
            ? p.Zones.Select((z, i) => new ZoneRow(i, p.FileName(z.File), MosaicNames.NoteName(z.Root),
                $"{MosaicNames.NoteName(z.KeyLo)}-{MosaicNames.NoteName(z.KeyHi)}", $"{z.VelLo}-{z.VelHi}", z.Group, z.Seq, z.Release,
                z.Tune, z.GainDb, z.Pan, MosaicModel.LoopNames[Math.Clamp(z.LoopMode, 0, 2)])).ToArray()
            : null;
        return new MosaicReading(MosaicModel.Summary(p, id => Get(trackId, id)), MosaicModel.Guide, p.Name, p.SourceKind, p.PackId, loading,
            st.FilesTotal, st.Missing, Math.Round(st.RamBytes / 1048576.0, 1), p.Zones.Count, p.Roots.Count(), p.VelocityLayers, p.ReleaseZones,
            snap.Voices, MosaicModel.Poly(Get(trackId, "polyphony")), MosaicModel.PerKey(Get(trackId, "perkey")), snap.Pedal, groups, zones);
    });

    [McpServerTool(Name = "set_mosaic"), Description(
        "Shape a Nota Mosaic's shared sound (kind 17) in musical units; every argument is optional and only the given ones change. "
        + "voiceMode Poly | Mono | Choke; polyphony 16 | 32 | 64 | 128; perKey 1..4 (strikes of one key ringing together); "
        + "oldFadeMs 1..200 (how fast a stolen voice fades); releaseTriggers on/off; releaseVolumeDb -36..6; releaseDropDb 0..-24 "
        + "(how much quieter a release sample is after a 2 s hold); velocityPercent 0..100 (Vel → Vol); velocityCurve -100 (soft) "
        + "… 100 (hard); pedal (listen to CC64); bendSemitones 0..24; attackMs / decayMs / releaseMs; sustainPercent; "
        + "filter Off | LP | HP | BP with cutoffHz and resonancePercent; transposeSemitones -24..24; glideMs (0 = off); volumeDb.")]
    public Task<string> SetMosaic(int trackId, string? voiceMode = null, int? polyphony = null, int? perKey = null, double? oldFadeMs = null,
        bool? releaseTriggers = null, double? releaseVolumeDb = null, double? releaseDropDb = null, double? velocityPercent = null,
        double? velocityCurve = null, bool? pedal = null, int? bendSemitones = null, double? attackMs = null, double? decayMs = null,
        double? sustainPercent = null, double? releaseMs = null, string? filter = null, double? cutoffHz = null, double? resonancePercent = null,
        double? transposeSemitones = null, double? glideMs = null, double? volumeDb = null) => Mutate(() =>
    {
        if (!IsMosaic(trackId)) return $"error: track {trackId} is not a Nota Mosaic";
        var errors = new List<string>();
        static int Find(string[] names, string v) => Array.FindIndex(names, n => string.Equals(n, v.Trim(), StringComparison.OrdinalIgnoreCase));
        if (voiceMode is not null) { int m = Find(MosaicModel.VoiceNames, voiceMode); if (m < 0) errors.Add($"voiceMode '{voiceMode}'"); else Set(trackId, "voicemode", m / 2.0); }
        if (polyphony is { } pv) { int i = Array.IndexOf(MosaicModel.PolyCounts, pv); if (i < 0) errors.Add($"polyphony {pv}"); else Set(trackId, "polyphony", i / 3.0); }
        if (perKey is { } pk) Set(trackId, "perkey", (Math.Clamp(pk, 1, 4) - 1) / 3.0);
        if (oldFadeMs is { } of) Set(trackId, "oldfade", MosaicModel.OldFadeNorm(of));
        if (releaseTriggers is { } rt) Set(trackId, "relon", rt ? 1 : 0);
        if (releaseVolumeDb is { } rv) Set(trackId, "relvol", MosaicModel.RelVolNorm(rv));
        if (releaseDropDb is { } rd) Set(trackId, "rellen", MosaicModel.RelLenNorm(-Math.Abs(rd)));
        if (velocityPercent is { } vp) Set(trackId, "velamount", Math.Clamp(vp, 0, 100) / 100.0);
        if (velocityCurve is { } vc) Set(trackId, "velcurve", 0.5 + Math.Clamp(vc, -100, 100) / 200.0);
        if (pedal is { } pd) Set(trackId, "pedal", pd ? 1 : 0);
        if (bendSemitones is { } bs) Set(trackId, "bendrange", Math.Clamp(bs, 0, 24) / 24.0);
        if (attackMs is { } a) Set(trackId, "attack", MosaicModel.AttackNorm(a / 1000.0));
        if (decayMs is { } d) Set(trackId, "decay", MosaicModel.DecayNorm(d / 1000.0));
        if (releaseMs is { } r) Set(trackId, "release", MosaicModel.DecayNorm(r / 1000.0));
        if (sustainPercent is { } s) Set(trackId, "sustain", Math.Clamp(s, 0, 100) / 100.0);
        if (filter is not null) { int f = Find(MosaicModel.FilterNames, filter); if (f < 0) errors.Add($"filter '{filter}'"); else Set(trackId, "filtertype", f / 3.0); }
        if (cutoffHz is { } c) Set(trackId, "cutoff", Math.Log(Math.Clamp(c, 20, 20000) / 20.0) / Math.Log(1000.0));
        if (resonancePercent is { } rs) Set(trackId, "resonance", Math.Clamp(rs, 0, 100) / 100.0);
        if (transposeSemitones is { } ts) Set(trackId, "transpose", 0.5 + Math.Clamp(Math.Round(ts), -24, 24) / 48.0);
        if (glideMs is { } g) Set(trackId, "glide", g <= 0 ? 0 : Math.Log(Math.Clamp(g, 1, 2000)) / Math.Log(2000.0));
        if (volumeDb is { } vd) Set(trackId, "volume", Math.Pow(10, Math.Clamp(vd, -60, 0) / 20.0));
        string summary = MosaicModel.Summary(MosaicProgram.Parse(E.MosaicProgram(trackId)), id => Get(trackId, id));
        return errors.Count == 0 ? summary : $"error: could not use {string.Join(", ", errors)} · {summary}";
    });

    [McpServerTool(Name = "set_mosaic_zone"), Description(
        "Edit one zone of a Nota Mosaic (indices from read_mosaic includeZones); only the given fields change. zone -1 adds a zone "
        + "playing sampleFile (an absolute path). root / keyLo / keyHi as note names (C4 = 60) or MIDI numbers; velLo / velHi 0..127; "
        + "tuneCents; gainDb; pan -1..1; loop Off | Fwd | Ping; group (index); step (round-robin position); release (a release "
        + "trigger); remove deletes the zone. One undo step. Returns the program summary.")]
    public Task<string> SetMosaicZone(int trackId, int zone, string? sampleFile = null, string? root = null, string? keyLo = null, string? keyHi = null,
        int? velLo = null, int? velHi = null, double? tuneCents = null, double? gainDb = null, double? pan = null, string? loop = null,
        int? group = null, int? step = null, bool? release = null, bool remove = false) => Mutate(() =>
    {
        if (!IsMosaic(trackId)) return $"error: track {trackId} is not a Nota Mosaic";
        var p = MosaicProgram.Parse(E.MosaicProgram(trackId));
        MosaicZone z;
        if (zone < 0)
        {
            if (sampleFile is null || !File.Exists(sampleFile)) return "error: a new zone needs an existing sampleFile";
            if (p.Groups.Count == 0) p.Groups.Add(new MosaicGroup { Name = "Group 1" });
            p.Files.Add(packs.ToRef(sampleFile));
            int r = SamplerModel.DetectRoot(sampleFile) is var dr and >= 0 ? dr : 60;
            z = new MosaicZone { File = p.Files.Count - 1, Root = r, KeyLo = r, KeyHi = r };
            p.Zones.Add(z);
        }
        else if (zone >= p.Zones.Count) return $"error: zone {zone} doesn't exist ({p.Zones.Count} zones)";
        else z = p.Zones[zone];
        if (remove && zone >= 0) p.Zones.RemoveAt(zone);
        else
        {
            static int? Note(string? s) => s is null ? null : SfzImporter.Note(s.Replace("♯", "#"));
            if (Note(root) is { } rn) z.Root = rn;
            if (Note(keyLo) is { } kl) z.KeyLo = kl;
            if (Note(keyHi) is { } kh) z.KeyHi = kh;
            if (velLo is { } vl) z.VelLo = Math.Clamp(vl, 0, 127);
            if (velHi is { } vh) z.VelHi = Math.Clamp(vh, 0, 127);
            if (z.KeyLo > z.KeyHi) (z.KeyLo, z.KeyHi) = (z.KeyHi, z.KeyLo);
            if (z.VelLo > z.VelHi) (z.VelLo, z.VelHi) = (z.VelHi, z.VelLo);
            if (tuneCents is { } tc) z.Tune = Math.Clamp(tc, -1200, 1200);
            if (gainDb is { } gd) z.GainDb = Math.Clamp(gd, -60, 24);
            if (pan is { } pn) z.Pan = Math.Clamp(pn, -1, 1);
            if (loop is not null) { int l = Array.FindIndex(MosaicModel.LoopNames, x => string.Equals(x, loop.Trim(), StringComparison.OrdinalIgnoreCase)); if (l >= 0) z.LoopMode = l; }
            if (group is { } g && g >= 0 && g < p.Groups.Count) z.Group = g;
            if (step is { } sp) z.Seq = Math.Max(1, sp);
            if (release is { } rl) z.Release = rl;
        }
        E.MosaicSetProgram(trackId, p.Serialize());
        return p.Summary();
    });

    [McpServerTool(Name = "set_mosaic_group"), Description(
        "Edit a group (layer) of a Nota Mosaic: name, gainDb, tuneCents, roundRobin (steps 1..16) and roundRobinMode Sequential | "
        + "Random | No repeat. group -1 adds a group. One undo step.")]
    public Task<string> SetMosaicGroup(int trackId, int group, string? name = null, double? gainDb = null, double? tuneCents = null,
        int? roundRobin = null, string? roundRobinMode = null) => Mutate(() =>
    {
        if (!IsMosaic(trackId)) return $"error: track {trackId} is not a Nota Mosaic";
        var p = MosaicProgram.Parse(E.MosaicProgram(trackId));
        MosaicGroup g;
        if (group < 0) { g = new MosaicGroup { Name = $"Group {p.Groups.Count + 1}" }; p.Groups.Add(g); }
        else if (group >= p.Groups.Count) return $"error: group {group} doesn't exist ({p.Groups.Count} groups)";
        else g = p.Groups[group];
        if (name is not null) g.Name = name;
        if (gainDb is { } gd) g.GainDb = Math.Clamp(gd, -60, 24);
        if (tuneCents is { } tc) g.Tune = Math.Clamp(tc, -1200, 1200);
        if (roundRobin is { } rr) g.SeqLen = Math.Clamp(rr, 1, 16);
        if (roundRobinMode is not null)
        {
            int m = Array.FindIndex(MosaicProgram.RrNames, x => string.Equals(x, roundRobinMode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (m < 0) return $"error: roundRobinMode '{roundRobinMode}'";
            g.RrMode = m;
        }
        E.MosaicSetProgram(trackId, p.Serialize());
        return p.Summary();
    });

    [McpServerTool(Name = "import_sfz"), Description(
        "Load an SFZ instrument into a Nota Mosaic (trackId <= 0 adds a new Mosaic track). controllers selects an articulation "
        + "the SFZ switches by CC, e.g. \"110=64\" (comma-separated). Samples load in the background. Returns what was imported "
        + "and what Mosaic ignored (modulation it doesn't play), and the articulation controllers with their values.")]
    public Task<SfzImport> ImportSfz(string path, int trackId = -1, string? controllers = null) => Mutate(() =>
    {
        if (!File.Exists(path)) return new SfzImport(-1, $"error: no such file {path}", 0, 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>());
        var ccs = new Dictionary<int, int>();
        foreach (var part in (controllers ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && int.TryParse(kv[0].Trim().TrimStart('c', 'C'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c)
                && int.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) ccs[c] = v;
        }
        var res = SfzImporter.Import(path, ccs, packs.ToRef);
        res.Program.SourceRef = packs.ToRef(path);
        int t = trackId > 0 && IsMosaic(trackId) ? trackId : E.AddMosaicTrack();
        E.MosaicSetProgram(t, res.Program.Serialize());
        var r = res.Report;
        return new SfzImport(t, r.Summary(), r.Regions, r.Imported, r.Groups, r.Includes, r.Missing,
            r.Ignored.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} ({kv.Value})").ToArray(),
            r.CcState.Select(kv => $"cc{kv.Key}={kv.Value}" + (r.CcLabels.TryGetValue(kv.Key, out var l) ? $" {l}" : "")).ToArray());
    });

    [McpServerTool(Name = "create_multisample"), Description(
        "Map a folder of samples into Nota Mosaic instruments by their file names (\"trombone_gb4.wav\", \"piano_c4_f_rr2.wav\", "
        + "\"x_a2_rel.wav\"): each name stem is an instrument; the note, velocity layer (pp…ff, v1…), round-robin step (rr2) and "
        + "release mark come from the name, a file without a note gets one from pitch detection, and the octave numbering "
        + "(C3 or C4 = 60) is checked by ear. dryRun (default) returns the proposal: per instrument its files, roots, layers, "
        + "gaps and every file's reading. With dryRun false, instrument (a name stem, default the first) loads into trackId "
        + "(<= 0: a new Mosaic track) and savePresets writes a preset per instrument under Nota Mosaic → Packs → the folder's name. "
        + "octave forces the convention: \"C4\" (names as written) or \"C3\" (shift up an octave).")]
    public async Task<MultisampleResult> CreateMultisample(string folder, bool dryRun = true, string? instrument = null, int trackId = -1,
        bool savePresets = false, string? octave = null, int edgeStretch = 12)
    {
        var opt = new MultisampleOptions { EdgeStretch = Math.Clamp(edgeStretch, 0, 48) };
        if (octave is not null) opt.OctaveShift = octave.Trim().ToUpperInvariant() == "C3" ? 12 : 0;
        var prop = await Task.Run(() => Directory.Exists(folder) ? MultisampleMapper.MapFolder(folder, opt, packs.DetectNote) : new MultisampleProposal());
        opt.OctaveShift ??= prop.OctaveShift;
        var rows = prop.Instruments.Select(i => new MappedInstrumentRow(i.Stem, i.Files.Count, i.Roots, i.Layers.ToArray(), i.MaxRr,
            i.Used.Count(f => f.Release), i.Confidence, i.Gaps.Select(MosaicNames.NoteName).ToArray(),
            i.Files.Select(f => new MappedRow(f.Name, f.NoteText, f.NoteFrom, f.Layer, f.Rr, f.Release, f.Confidence, f.Duplicate)).ToArray())).ToArray();
        if (dryRun || prop.Instruments.Count == 0)
            return new MultisampleResult(prop.OctaveWhy, prop.OctaveShift, rows, -1, Array.Empty<string>());
        var chosen = instrument is null ? prop.Instruments[0] : prop.Instruments.FirstOrDefault(i => i.Stem == instrument) ?? prop.Instruments[0];
        string folderName = Path.GetFileName(folder.TrimEnd('/', '\\'));
        var saved = new List<string>();
        if (savePresets)
            foreach (var inst in prop.Instruments.Where(i => i.Used.Any()))
            {
                var p = MultisampleMapper.Build(inst, opt, packs.ToRef);
                saved.Add(packs.Save(p, p.Name, folderName));
            }
        var prog = MultisampleMapper.Build(chosen, opt, packs.ToRef);
        prog.SourceRef = packs.ToRef(folder);
        int t = await Mutate(() =>
        {
            int tt = trackId > 0 && IsMosaic(trackId) ? trackId : E.AddMosaicTrack();
            E.MosaicSetProgram(tt, prog.Serialize());
            return tt;
        });
        return new MultisampleResult(prop.OctaveWhy, prop.OctaveShift, rows, t, saved.ToArray());
    }

    [McpServerTool(Name = "list_mosaic_presets"), Description(
        "List Nota Mosaic's presets: the factory multisamples (rendered on first use) and the pack presets — made for each "
        + "installed sample pack (its SFZ, else its file names) and by create_multisample. Load one with load_mosaic_preset.")]
    public Task<PresetRow[]> ListMosaicPresets() => Read(() =>
        factory.All().Where(p => p.IsInstrument && p.BuiltinKind == MosaicModel.Kind).Select(p => new PresetRow(p.DisplayName, "factory", p.Category))
            .Concat(packs.Presets().Select(p => new PresetRow(p.Name, p.Source == "sfz" ? "sfz" : "pack", p.Folder))).ToArray());

    [McpServerTool(Name = "load_mosaic_preset"), Description(
        "Load a Nota Mosaic preset by name (a factory name, or a pack preset's name; folder narrows it) into trackId "
        + "(<= 0: a new Mosaic track). Samples load in the background. Returns the track id, or -1 when no preset matches.")]
    public Task<int> LoadMosaicPreset(string name, int trackId = -1, string? folder = null) => Mutate(() =>
    {
        var fp = factory.All().FirstOrDefault(p => p.IsInstrument && p.BuiltinKind == MosaicModel.Kind && string.Equals(p.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        var pp = fp.Id is { Length: > 0 } ? null : packs.Presets().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
            && (folder is null || string.Equals(p.Folder, folder, StringComparison.OrdinalIgnoreCase)));
        if (fp.Id is not { Length: > 0 } && pp is null) return -1;
        int t = trackId > 0 && IsMosaic(trackId) ? trackId : E.AddMosaicTrack();
        if (pp is null) factory.ApplyInPlace(E, fp.Id!, t, -1);
        else packs.ApplyInPlace(E, pp.Path, t);
        return t;
    });

    [McpServerTool(Name = "scan_sample_packs"), Description(
        "Make Nota Mosaic presets for every installed sample pack that has none yet (normally automatic at start and after an "
        + "install). Returns how many presets were written.")]
    public Task<int> ScanSamplePacks() => packs.ScanAsync();
}
