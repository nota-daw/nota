// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SFZ → a Nota Mosaic program. The subset a sample library needs to play: headers
// <control> <global> <master> <group> <region> with opcode inheritance down that chain,
// #define / #include, default_path, Windows paths ("..\Samples\x.wav") and case-insensitive
// file names; key / velocity ranges, roots, tune / transpose / volume / pan / amplitude,
// offset / end, loops, release triggers, round-robin (seq_length / seq_position and lorand /
// hirand), exclusive groups (group / off_by / off_mode), key and velocity crossfades.
//
// What the instrument can't do is reported, never silently lost: modulation (CC-driven
// opcodes, flex EGs, LFOs, curves) is counted per family ("eg06*", "pitcheg*", "tune_cc")
// for the import summary. Articulations switched by CC (locc / hicc) or keyswitches
// (sw_last) are resolved at import time against the controllers' starting values
// (set_ccN, sw_default); importing again with other values picks another articulation.
// The amp envelope of the first played region is offered as the patch's envelope.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nota.Application.Mosaic;

public sealed class SfzImportReport
{
    public string File = "";
    public int Regions, Imported, Groups, Includes, PathsFixed, Missing, Generators, Inactive;
    /// <summary>CC numbers whose value selects regions (locc / hicc), with the value used.</summary>
    public SortedDictionary<int, int> CcState = new();
    /// <summary>"label_cc" names, for the articulation picker.</summary>
    public SortedDictionary<int, string> CcLabels = new();
    /// <summary>Ignored opcode families → how many regions carried them.</summary>
    public SortedDictionary<string, int> Ignored = new(StringComparer.Ordinal);
    public double Seconds;
    /// <summary>The amp envelope the library asks for (seconds / 0..1), when it names one.</summary>
    public double? Attack, Decay, Sustain, Release;
    /// <summary>The pitch-bend range (semitones, from bend_up) and a low-pass cutoff (Hz), when named.</summary>
    public double? BendSemis, CutoffHz;

    public string Summary()
    {
        string head = $"Imported {Imported} zones";
        if (Ignored.Count == 0) return head;
        return head + "; ignored: " + string.Join(", ", Ignored.OrderByDescending(kv => kv.Value).Take(4).Select(kv => kv.Key));
    }
}

public sealed record SfzResult(MosaicProgram Program, SfzImportReport Report);

public static class SfzImporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Imports <paramref name="sfzPath"/>. <paramref name="ccOverrides"/> sets controller values
    /// (an articulation); <paramref name="toRef"/> turns an absolute sample path into the reference the
    /// program stores (the default keeps the path).</summary>
    public static SfzResult Import(string sfzPath, IReadOnlyDictionary<int, int>? ccOverrides = null, Func<string, string>? toRef = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = new SfzImportReport { File = Path.GetFileName(sfzPath) };
        string rootDir = Path.GetDirectoryName(Path.GetFullPath(sfzPath)) ?? ".";
        var defines = new Dictionary<string, string>(StringComparer.Ordinal);
        string text = Load(sfzPath, rootDir, defines, report, 0);

        // ---- tokenize into (header | opcode) ----------------------------------------------
        var control = new Dictionary<string, string>(StringComparer.Ordinal);
        var global = new Dictionary<string, string>(StringComparer.Ordinal);
        var master = new Dictionary<string, string>(StringComparer.Ordinal);
        var group = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string>? region = null;
        string header = "";
        int groupSerial = 0, masterSerial = 0;
        var regions = new List<(Dictionary<string, string> Ops, int Group, int Master)>();

        void CloseRegion()
        {
            if (region is null) return;
            var merged = new Dictionary<string, string>(global, StringComparer.Ordinal);
            foreach (var kv in master) merged[kv.Key] = kv.Value;
            foreach (var kv in group) merged[kv.Key] = kv.Value;
            foreach (var kv in region) merged[kv.Key] = kv.Value;
            regions.Add((merged, groupSerial, masterSerial));
            region = null;
        }

        foreach (var (kind, key, value) in Tokens(text))
        {
            if (kind == 'h')
            {
                CloseRegion();
                header = key;
                switch (header)
                {
                    case "control": control.Clear(); break;
                    case "global": global.Clear(); master.Clear(); group.Clear(); break;
                    case "master": master.Clear(); group.Clear(); masterSerial++; groupSerial++; break;
                    case "group": group.Clear(); groupSerial++; break;
                    case "region": region = new Dictionary<string, string>(StringComparer.Ordinal); break;
                }
                continue;
            }
            switch (header)
            {
                case "control": control[key] = value; break;
                case "global": global[key] = value; break;
                case "master": master[key] = value; break;
                case "group": group[key] = value; break;
                case "region": region![key] = value; break;
                default: break;   // <curve>, <effect>, <midi>: not ours
            }
        }
        CloseRegion();
        report.Regions = regions.Count;

        // ---- controllers ------------------------------------------------------------------
        var cc = new Dictionary<int, int>();
        foreach (var kv in control)
        {
            if (kv.Key.StartsWith("set_cc", StringComparison.Ordinal) && int.TryParse(kv.Key[6..], NumberStyles.Integer, Inv, out int n))
                cc[n] = (int)Math.Round(Num(kv.Value, 0));
            else if (kv.Key.StartsWith("label_cc", StringComparison.Ordinal) && int.TryParse(kv.Key[8..], NumberStyles.Integer, Inv, out int l))
                report.CcLabels[l] = kv.Value;
        }
        if (ccOverrides is not null) foreach (var kv in ccOverrides) cc[kv.Key] = kv.Value;
        int CcVal(int n) => cc.TryGetValue(n, out var v) ? v : 0;
        string defaultPath = control.TryGetValue("default_path", out var dp) ? dp : "";

        // Keyswitch state: the first sw_default met.
        int? keySwitch = null;
        foreach (var (ops, _, _) in regions)
            if (ops.TryGetValue("sw_default", out var swd) && Note(swd) is { } kn) { keySwitch = kn; break; }

        // ---- regions → zones ----------------------------------------------------------------
        var p = new MosaicProgram { Name = Path.GetFileNameWithoutExtension(sfzPath), SourceKind = "sfz" };
        var fileIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var groupIndex = new Dictionary<(int Group, int Master, bool Release, int SeqLen), int>();
        var dirCache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        bool envTaken = false;

        foreach (var (ops, gSerial, mSerial) in regions)
        {
            // Ignored opcode families (counted once per region).
            var fams = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in ops.Keys) if (Family(k) is { } fam) fams.Add(fam);
            foreach (var f in fams) report.Ignored[f] = report.Ignored.TryGetValue(f, out var c) ? c + 1 : 1;

            if (!ops.TryGetValue("sample", out var sample) || sample.Length == 0) { report.Inactive++; continue; }
            if (sample.StartsWith('*')) { report.Generators++; continue; }   // *sine, *noise …

            // CC / keyswitch conditions.
            bool active = true;
            foreach (var kv in ops)
            {
                if (kv.Key.StartsWith("locc", StringComparison.Ordinal) && int.TryParse(kv.Key[4..], NumberStyles.Integer, Inv, out int n))
                { report.CcState[n] = CcVal(n); if (CcVal(n) < Num(kv.Value, 0)) active = false; }
                else if (kv.Key.StartsWith("hicc", StringComparison.Ordinal) && int.TryParse(kv.Key[4..], NumberStyles.Integer, Inv, out int h))
                { report.CcState[h] = CcVal(h); if (CcVal(h) > Num(kv.Value, 127)) active = false; }
                else if (kv.Key.StartsWith("on_locc", StringComparison.Ordinal) || kv.Key.StartsWith("on_hicc", StringComparison.Ordinal))
                    active = false;   // fires on a controller move, not a key
            }
            if (ops.TryGetValue("sw_last", out var swl) && Note(swl) is { } swn && keySwitch is { } ks && swn != ks) active = false;
            if (!active) { report.Inactive++; continue; }

            // The file.
            string rel = (defaultPath + sample).Trim();
            if (rel.Contains('\\')) { report.PathsFixed++; rel = rel.Replace('\\', '/'); }
            string? abs = FindFile(rootDir, rel, dirCache);
            if (abs is null) { report.Missing++; continue; }
            if (!fileIndex.TryGetValue(abs, out int fi))
            {
                fi = p.Files.Count; fileIndex[abs] = fi;
                p.Files.Add(toRef is null ? abs : toRef(abs));
            }

            // Ranges and root.
            int lo = 0, hi = 127, root = 60;
            if (ops.TryGetValue("key", out var key) && Note(key) is { } k1) { lo = hi = root = k1; }
            if (ops.TryGetValue("lokey", out var lk) && Note(lk) is { } k2) lo = k2;
            if (ops.TryGetValue("hikey", out var hk) && Note(hk) is { } k3) hi = k3;
            if (ops.TryGetValue("pitch_keycenter", out var pkc) && Note(pkc) is { } k4) root = k4;
            else if (!ops.ContainsKey("key")) root = ops.ContainsKey("lokey") || ops.ContainsKey("hikey") ? (lo + hi) / 2 : 60;
            // pitch_keytrack=0 (drums): one key, played at that key — the root sits on it.
            if (ops.TryGetValue("pitch_keytrack", out var pkt) && Math.Abs(Num(pkt, 100)) < 1 && lo == hi) root = lo;
            int vlo = Clamp7(Num(Get(ops, "lovel"), 0)), vhi = Clamp7(Num(Get(ops, "hivel"), 127));
            bool release = ops.TryGetValue("trigger", out var tr) && tr is "release" or "release_key";
            int seqLen = Math.Clamp((int)Num(Get(ops, "seq_length"), 1), 1, 64);

            var gk = (gSerial, mSerial, release, seqLen);
            if (!groupIndex.TryGetValue(gk, out int gi))
            {
                gi = p.Groups.Count; groupIndex[gk] = gi;
                string label = Get(ops, "group_label") ?? Get(ops, "master_label") ?? (release ? "Release" : $"Group {gi + 1}");
                p.Groups.Add(new MosaicGroup { Name = label, SeqLen = seqLen, RrMode = 0 });
            }

            double amp = Num(Get(ops, "amplitude"), 100);
            var z = new MosaicZone
            {
                File = fi, Group = gi, Root = Math.Clamp(root, 0, 127),
                KeyLo = Math.Clamp(Math.Min(lo, hi), 0, 127), KeyHi = Math.Clamp(Math.Max(lo, hi), 0, 127),
                VelLo = Math.Min(vlo, vhi), VelHi = Math.Max(vlo, vhi),
                Tune = Num(Get(ops, "tune"), 0) + 100 * Num(Get(ops, "transpose"), 0),
                GainDb = Num(Get(ops, "volume"), 0) + (amp > 0 ? 20 * Math.Log10(amp / 100.0) : -96),
                Pan = Math.Clamp(Num(Get(ops, "pan"), 0) / 100.0, -1, 1),
                Start = Math.Max(0, Num(Get(ops, "offset"), 0)),
                End = Num(Get(ops, "end"), -1) is var e && e > 0 ? e : -1,
                Seq = Math.Clamp((int)Num(Get(ops, "seq_position"), 1), 1, 64),
                RandLo = Math.Clamp(Num(Get(ops, "lorand"), 0), 0, 1), RandHi = Math.Clamp(Num(Get(ops, "hirand"), 1), 0, 1),
                Release = release,
                Excl = (int)Num(Get(ops, "group"), 0), OffBy = (int)Num(Get(ops, "off_by"), 0),
                OffNormal = Get(ops, "off_mode") == "normal",
                XKeyInLo = NoteOr(ops, "xfin_lokey"), XKeyInHi = NoteOr(ops, "xfin_hikey"),
                XKeyOutLo = NoteOr(ops, "xfout_lokey"), XKeyOutHi = NoteOr(ops, "xfout_hikey"),
                XVelInLo = IntOr(ops, "xfin_lovel"), XVelInHi = IntOr(ops, "xfin_hivel"),
                XVelOutLo = IntOr(ops, "xfout_lovel"), XVelOutHi = IntOr(ops, "xfout_hivel"),
            };
            string lm = Get(ops, "loop_mode") ?? Get(ops, "loopmode") ?? "";
            z.LoopMode = lm is "loop_continuous" or "loop_sustain" ? 1 : 0;
            if (z.LoopMode != 0)
            {
                z.LoopStart = Num(Get(ops, "loop_start") ?? Get(ops, "loopstart"), -1);
                z.LoopEnd = Num(Get(ops, "loop_end") ?? Get(ops, "loopend"), -1);
                z.Crossfade = Math.Clamp(Num(Get(ops, "loop_crossfade"), 0), 0, 2);
            }
            p.Zones.Add(z);

            if (!envTaken && !release)
            {
                envTaken = true;
                if (Get(ops, "ampeg_attack") is { } a) report.Attack = Num(a, 0);
                if (Get(ops, "ampeg_decay") is { } d) report.Decay = Num(d, 0);
                if (Get(ops, "ampeg_sustain") is { } s) report.Sustain = Math.Clamp(Num(s, 100) / 100.0, 0, 1);
                if (Get(ops, "ampeg_release") is { } r) report.Release = Num(r, 0);
                if (Get(ops, "bend_up") is { } bu) report.BendSemis = Math.Round(Math.Abs(Num(bu, 200)) / 100.0);
                if (Get(ops, "cutoff") is { } cu && (Get(ops, "fil_type") ?? "lpf_2p").StartsWith("lpf", StringComparison.Ordinal)) report.CutoffHz = Num(cu, 20000);
            }
        }

        // Random-range groups play their layers by lorand / hirand; a seq group sequentially.
        foreach (var g in p.Groups) if (g.SeqLen <= 1) g.RrMode = 0;
        report.Imported = p.Zones.Count;
        report.Groups = p.Groups.Count;
        foreach (var kv in report.CcState) p.Ccs[kv.Key] = kv.Value;
        report.Seconds = sw.Elapsed.TotalSeconds;
        return new SfzResult(p, report);
    }

    /// <summary>The articulations an SFZ selects by controller: per CC, the distinct locc / hicc
    /// ranges its regions use, with the label_cc name — for an articulation picker.</summary>
    public static SortedDictionary<int, List<(int Lo, int Hi, string Label)>> CcRanges(string sfzPath)
    {
        var result = new SortedDictionary<int, List<(int, int, string)>>();
        var report = new SfzImportReport();
        string rootDir = Path.GetDirectoryName(Path.GetFullPath(sfzPath)) ?? ".";
        string text = Load(sfzPath, rootDir, new Dictionary<string, string>(StringComparer.Ordinal), report, 0);
        var labels = new Dictionary<int, string>();
        var cur = new Dictionary<int, (int Lo, int Hi)>();
        var seen = new HashSet<(int, int, int)>();
        foreach (var (kind, key, value) in Tokens(text))
        {
            if (kind == 'h') { if (key is "region" or "group" or "master" or "global") { } continue; }
            if (key.StartsWith("label_cc", StringComparison.Ordinal) && int.TryParse(key[8..], NumberStyles.Integer, Inv, out int l)) { labels[l] = value; continue; }
            bool lo = key.StartsWith("locc", StringComparison.Ordinal), hi = key.StartsWith("hicc", StringComparison.Ordinal);
            if (!lo && !hi) continue;
            if (!int.TryParse(key[4..], NumberStyles.Integer, Inv, out int cc)) continue;
            int v = (int)Math.Round(Num(value, lo ? 0 : 127));
            var r = cur.TryGetValue(cc, out var c) ? c : (0, 127);
            r = lo ? (v, r.Item2) : (r.Item1, v);
            cur[cc] = r;
            if (hi && seen.Add((cc, r.Item1, r.Item2)))
            {
                if (!result.TryGetValue(cc, out var list)) result[cc] = list = new List<(int, int, string)>();
                list.Add((r.Item1, r.Item2, ""));
            }
        }
        foreach (var (cc, list) in result.ToList())
        {
            if (list.Count < 2) { result.Remove(cc); continue; }
            string name = labels.TryGetValue(cc, out var n) ? n : $"cc{cc}";
            result[cc] = list.OrderBy(x => x.Item1).Select(x => (x.Item1, x.Item2, $"{name} {x.Item1}–{x.Item2}")).ToList();
        }
        return result;
    }

    // ---- loading (#include, #define, comments) ---------------------------------------------
    private static readonly Regex DefineRx = new(@"#define\s+(\$\w+)\s+(\S+)", RegexOptions.Compiled);
    private static readonly Regex IncludeRx = new("#include\\s+\"([^\"]+)\"", RegexOptions.Compiled);

    private static string Load(string path, string rootDir, Dictionary<string, string> defines, SfzImportReport report, int depth)
    {
        if (depth > 16) return "";
        string raw;
        try { raw = File.ReadAllText(path); } catch { return ""; }
        raw = StripComments(raw);
        var sb = new StringBuilder(raw.Length);
        foreach (var lineRaw in raw.Split('\n'))
        {
            string line = lineRaw;
            var dm = DefineRx.Match(line);
            if (dm.Success) { defines[dm.Groups[1].Value] = dm.Groups[2].Value; line = line.Remove(dm.Index, dm.Length); }
            line = Substitute(line, defines);
            var im = IncludeRx.Match(line);
            while (im.Success)
            {
                report.Includes++;
                string inc = im.Groups[1].Value.Replace('\\', '/');
                string? found = FindIncluded(inc, rootDir, Path.GetDirectoryName(path) ?? rootDir);
                string body = found is null ? "" : Load(found, rootDir, defines, report, depth + 1);
                line = line[..im.Index] + "\n" + body + "\n" + line[(im.Index + im.Length)..];
                im = IncludeRx.Match(line, im.Index + body.Length + 2);
            }
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    private static string? FindIncluded(string inc, string rootDir, string here)
    {
        foreach (var dir in new[] { rootDir, here })
        {
            var cand = Path.GetFullPath(Path.Combine(dir, inc));
            if (File.Exists(cand)) return cand;
        }
        return null;
    }

    private static string Substitute(string line, Dictionary<string, string> defines)
    {
        if (defines.Count == 0 || line.IndexOf('$') < 0) return line;
        // Longest names first so $FOO doesn't eat $FOOBAR.
        foreach (var kv in defines.OrderByDescending(kv => kv.Key.Length)) line = line.Replace(kv.Key, kv.Value);
        return line;
    }

    private static string StripComments(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; if (i < s.Length) sb.Append('\n'); continue; }
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? s.Length : e + 1; continue; }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    // ---- tokens: headers and opcodes (a sample= value may hold spaces) ----------------------
    private static readonly Regex TokRx = new(@"<(\w+)>|([A-Za-z0-9_]+)=", RegexOptions.Compiled);

    private static IEnumerable<(char Kind, string Key, string Value)> Tokens(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var ms = TokRx.Matches(line);
            for (int i = 0; i < ms.Count; i++)
            {
                var m = ms[i];
                if (m.Groups[1].Success) { yield return ('h', m.Groups[1].Value, ""); continue; }
                int vStart = m.Index + m.Length;
                int vEnd = i + 1 < ms.Count ? ms[i + 1].Index : line.Length;
                string key = m.Groups[2].Value;
                string value = line[vStart..vEnd].Trim();
                // Only sample (and labels) may contain spaces; any other value is its first word.
                if (key != "sample" && !key.EndsWith("label", StringComparison.Ordinal) && !key.StartsWith("label_cc", StringComparison.Ordinal))
                {
                    int sp = value.IndexOfAny(new[] { ' ', '\t' });
                    if (sp > 0) value = value[..sp];
                }
                yield return ('o', key, value);
            }
        }
    }

    // ---- files -------------------------------------------------------------------------------
    // Resolves rel against the SFZ's folder, case-insensitively per path segment (libraries
    // made on Windows name "Samples/C4.WAV" what sits on disk as "samples/c4.wav").
    private static string? FindFile(string rootDir, string rel, Dictionary<string, Dictionary<string, string>> dirCache)
    {
        string direct;
        try { direct = Path.GetFullPath(Path.Combine(rootDir, rel)); } catch { return null; }
        if (File.Exists(direct)) return direct;
        string cur = rootDir;
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part == ".") continue;
            if (part == "..") { cur = Path.GetDirectoryName(cur) ?? cur; continue; }
            if (!dirCache.TryGetValue(cur, out var entries))
            {
                entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try { foreach (var e in Directory.EnumerateFileSystemEntries(cur)) entries[Path.GetFileName(e)] = e; } catch { }
                dirCache[cur] = entries;
            }
            if (!entries.TryGetValue(part, out var next)) return null;
            cur = next;
        }
        return File.Exists(cur) ? cur : null;
    }

    // ---- values ------------------------------------------------------------------------------
    private static string? Get(Dictionary<string, string> ops, string key) => ops.TryGetValue(key, out var v) ? v : null;
    private static int Clamp7(double v) => Math.Clamp((int)Math.Round(v), 0, 127);
    private static int NoteOr(Dictionary<string, string> ops, string key) => ops.TryGetValue(key, out var v) && Note(v) is { } n ? n : -1;
    private static int IntOr(Dictionary<string, string> ops, string key) => ops.TryGetValue(key, out var v) ? Clamp7(Num(v, 0)) : -1;

    private static double Num(string? s, double def)
        => s is not null && double.TryParse(s, NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v : def;

    /// <summary>A key: a MIDI number or a note name with SFZ's octave numbering (c4 = 60).</summary>
    public static int? Note(string s)
    {
        s = s.Trim();
        if (int.TryParse(s, NumberStyles.Integer, Inv, out int n)) return n is >= 0 and <= 127 ? n : null;
        var m = Regex.Match(s, @"^([A-Ga-g])([#b♯♭]?)(-?\d+)$");
        if (!m.Success) return null;
        int pc = char.ToLowerInvariant(m.Groups[1].Value[0]) switch { 'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5, 'g' => 7, 'a' => 9, _ => 11 };
        if (m.Groups[2].Value is "#" or "♯") pc++;
        else if (m.Groups[2].Value is "b" or "♭") pc--;
        int note = (int.Parse(m.Groups[3].Value, Inv) + 1) * 12 + pc;
        return note is >= 0 and <= 127 ? note : null;
    }

    // Opcodes Mosaic plays (or uses to choose regions); everything else is a family to report.
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "sample", "key", "lokey", "hikey", "pitch_keycenter", "pitch_keytrack", "lovel", "hivel", "tune", "transpose", "volume",
        "pan", "amplitude", "offset", "end", "loop_mode", "loopmode", "loop_start", "loopstart", "loop_end", "loopend",
        "loop_crossfade", "trigger", "seq_length", "seq_position", "lorand", "hirand", "group", "off_by", "off_mode",
        "xfin_lokey", "xfin_hikey", "xfout_lokey", "xfout_hikey", "xfin_lovel", "xfin_hivel", "xfout_lovel", "xfout_hivel",
        "ampeg_attack", "ampeg_decay", "ampeg_sustain", "ampeg_release", "sw_last", "sw_default", "sw_lokey", "sw_hikey",
        "group_label", "master_label", "region_label", "default_path", "note_offset", "octave_offset",
        "bend_up", "bend_down", "cutoff", "fil_type",
    };

    private static string? Family(string op)
    {
        if (Known.Contains(op)) return null;
        if (op.StartsWith("locc", StringComparison.Ordinal) || op.StartsWith("hicc", StringComparison.Ordinal)
            || op.StartsWith("set_cc", StringComparison.Ordinal) || op.StartsWith("label_cc", StringComparison.Ordinal)) return null;
        // eg06_time1_oncc → eg06* · lfo02_freq → lfo02* · pitcheg_decay_oncc → pitcheg* · ampeg_release_oncc → ampeg*
        var m = Regex.Match(op, @"^(eg\d+|lfo\d+|var\d+|pitcheg|fileg|ampeg|pitchlfo|amplfo|fillfo)_");
        if (m.Success) return m.Groups[1].Value + "*";
        if (op.StartsWith("amp_velcurve_", StringComparison.Ordinal)) return "amp_velcurve*";
        // amplitude_cc15 → amplitude_cc* · cutoff_oncc124 → cutoff_cc*
        var cc = Regex.Match(op, @"^(.+?)_(?:on)?cc\d+$");
        if (cc.Success) return cc.Groups[1].Value + "_cc*";
        return op;
    }
}
