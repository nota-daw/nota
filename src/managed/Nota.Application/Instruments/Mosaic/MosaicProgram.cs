// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Mosaic's program: files, groups (layers) and zones — the map the instrument plays.
// It travels as text (the engine parses the same grammar, see native Mosaic.h): one record
// per line, "key=value" tokens, names and paths %-escaped. The text is what a project and a
// preset store; samples are referenced, never copied: "samples:<rel>" resolves against the
// user's Samples folder, "data:<rel>" against Nota's data folder, anything else is a path.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Nota.Application.Mosaic;

/// <summary>A sample on a key × velocity rectangle. Frames are the sample's own (−1 = its edge).</summary>
public sealed class MosaicZone
{
    public int File = -1, Group, Root = 60;
    public int KeyLo, KeyHi = 127, VelLo, VelHi = 127;
    public double Tune, GainDb, Pan;                    // cents · dB · −1..+1
    public double Start, End = -1, LoopStart = -1, LoopEnd = -1;
    public int LoopMode;                                // 0 off · 1 forward · 2 ping-pong
    public double Crossfade;                            // loop crossfade, seconds
    public int Seq = 1;                                 // round-robin step (1-based)
    public double RandLo, RandHi = 1;                   // SFZ lorand / hirand
    public int XKeyInLo = -1, XKeyInHi = -1, XKeyOutLo = -1, XKeyOutHi = -1;
    public int XVelInLo = -1, XVelInHi = -1, XVelOutLo = -1, XVelOutHi = -1;
    public bool Release;                                // a release trigger
    public int Excl, OffBy;                             // exclusive group · off by
    public bool OffNormal;                              // off_mode normal (release) vs fast

    public MosaicZone Clone() => (MosaicZone)MemberwiseClone();
    public bool Covers(int key, int vel) => key >= KeyLo && key <= KeyHi && vel >= VelLo && vel <= VelHi;
}

/// <summary>A layer: shared gain / tune and a round-robin.</summary>
public sealed class MosaicGroup
{
    public string Name = "";
    public double GainDb, Tune;
    public int RrMode;      // 0 sequential · 1 random · 2 random without repeat
    public int SeqLen = 1;
    public MosaicGroup Clone() => (MosaicGroup)MemberwiseClone();
}

public sealed class MosaicProgram
{
    public const int Version = 1;
    public static readonly string[] RrNames = { "Sequential", "Random", "No repeat" };

    public string Name = "";
    /// <summary>Where it came from: "sfz" · "folder" · "files" · "factory" · "sample" · "" (informational).</summary>
    public string SourceKind = "";
    public string SourceRef = "";
    /// <summary>The registry pack the samples come from ("" = none) and its display name.</summary>
    public string PackId = "", PackName = "";
    /// <summary>SFZ articulation state (CC number → value) the zones were imported with.</summary>
    public SortedDictionary<int, int> Ccs = new();
    public List<string> Files = new();
    public List<MosaicGroup> Groups = new();
    public List<MosaicZone> Zones = new();

    public bool IsEmpty => Zones.Count == 0;

    public MosaicProgram Clone()
    {
        var p = (MosaicProgram)MemberwiseClone();
        p.Ccs = new SortedDictionary<int, int>(Ccs);
        p.Files = new List<string>(Files);
        p.Groups = Groups.Select(g => g.Clone()).ToList();
        p.Zones = Zones.Select(z => z.Clone()).ToList();
        return p;
    }

    /// <summary>A program of one sample spanning the whole keyboard (a dropped file, a Sampler sound).</summary>
    public static MosaicProgram Single(string fileRef, int root, string name, bool loop = false)
    {
        var p = new MosaicProgram { Name = name, SourceKind = "sample" };
        p.Files.Add(fileRef);
        p.Groups.Add(new MosaicGroup { Name = "Sample" });
        p.Zones.Add(new MosaicZone { File = 0, Root = Math.Clamp(root, 0, 127), LoopMode = loop ? 1 : 0 });
        return p;
    }

    // ---- reading ---------------------------------------------------------------------------
    public IEnumerable<int> Roots => Zones.Where(z => !z.Release).Select(z => z.Root).Distinct().OrderBy(r => r);
    public int VelocityLayers => Zones.Where(z => !z.Release).Select(z => (z.VelLo, z.VelHi)).Distinct().Count();
    public int ReleaseZones => Zones.Count(z => z.Release);
    public int MaxRoundRobin => Groups.Count == 0 ? 1 : Groups.Max(g => g.SeqLen);
    public (int Lo, int Hi) KeySpan => Zones.Count == 0 ? (0, 127) : (Zones.Min(z => z.KeyLo), Zones.Max(z => z.KeyHi));
    public string FileName(int file) => file >= 0 && file < Files.Count ? MosaicPaths.FileName(Files[file]) : "";

    /// <summary>"30 roots × 4 velocity layers · release 30".</summary>
    public string Summary()
    {
        if (IsEmpty) return "no zones";
        int roots = Roots.Count(), layers = VelocityLayers, rel = ReleaseZones, rr = MaxRoundRobin;
        var sb = new StringBuilder();
        sb.Append(roots).Append(roots == 1 ? " root" : " roots");
        if (layers > 1) sb.Append(" × ").Append(layers).Append(" velocity layers");
        if (rr > 1) sb.Append(" · rr ").Append(rr);
        if (rel > 0) sb.Append(" · release ").Append(rel);
        return sb.ToString();
    }

    // ---- text --------------------------------------------------------------------------------
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string N(double v) => Math.Round(v, 6).ToString("0.######", Inv);

    public string Serialize()
    {
        var sb = new StringBuilder();
        sb.Append("mosaic ").Append(Version).Append('\n');
        if (Name.Length > 0) sb.Append("name ").Append(Escape(Name)).Append('\n');
        if (SourceKind.Length > 0) sb.Append("source ").Append(Escape(SourceKind)).Append(' ').Append(Escape(SourceRef)).Append('\n');
        if (PackId.Length > 0) sb.Append("pack ").Append(Escape(PackId)).Append(' ').Append(Escape(PackName)).Append('\n');
        if (Ccs.Count > 0) sb.Append("ccs ").Append(string.Join(' ', Ccs.Select(kv => $"{kv.Key}={kv.Value}"))).Append('\n');
        foreach (var f in Files) sb.Append("file ").Append(Escape(f)).Append('\n');
        foreach (var g in Groups)
            sb.Append("group gain=").Append(N(g.GainDb)).Append(" tune=").Append(N(g.Tune))
              .Append(" rr=").Append(g.RrMode).Append(" seq=").Append(g.SeqLen)
              .Append(" name=").Append(Escape(g.Name)).Append('\n');
        foreach (var z in Zones)
        {
            sb.Append("zone f=").Append(z.File).Append(" g=").Append(z.Group).Append(" root=").Append(z.Root)
              .Append(" klo=").Append(z.KeyLo).Append(" khi=").Append(z.KeyHi)
              .Append(" vlo=").Append(z.VelLo).Append(" vhi=").Append(z.VelHi);
            if (z.Tune != 0) sb.Append(" tune=").Append(N(z.Tune));
            if (z.GainDb != 0) sb.Append(" gain=").Append(N(z.GainDb));
            if (z.Pan != 0) sb.Append(" pan=").Append(N(z.Pan));
            if (z.Start > 0) sb.Append(" s=").Append(N(z.Start));
            if (z.End >= 0) sb.Append(" e=").Append(N(z.End));
            if (z.LoopMode != 0) sb.Append(" lm=").Append(z.LoopMode);
            if (z.LoopStart >= 0) sb.Append(" ls=").Append(N(z.LoopStart));
            if (z.LoopEnd >= 0) sb.Append(" le=").Append(N(z.LoopEnd));
            if (z.Crossfade > 0) sb.Append(" xf=").Append(N(z.Crossfade));
            if (z.Seq != 1) sb.Append(" seq=").Append(z.Seq);
            if (z.RandLo > 0) sb.Append(" rlo=").Append(N(z.RandLo));
            if (z.RandHi < 1) sb.Append(" rhi=").Append(N(z.RandHi));
            void X(string k, int v) { if (v >= 0) sb.Append(' ').Append(k).Append('=').Append(v); }
            X("xkil", z.XKeyInLo); X("xkih", z.XKeyInHi); X("xkol", z.XKeyOutLo); X("xkoh", z.XKeyOutHi);
            X("xvil", z.XVelInLo); X("xvih", z.XVelInHi); X("xvol", z.XVelOutLo); X("xvoh", z.XVelOutHi);
            if (z.Release) sb.Append(" trig=1");
            if (z.Excl != 0) sb.Append(" excl=").Append(z.Excl);
            if (z.OffBy != 0) sb.Append(" off=").Append(z.OffBy);
            if (z.OffNormal) sb.Append(" om=1");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static MosaicProgram Parse(string? text)
    {
        var p = new MosaicProgram();
        if (string.IsNullOrEmpty(text)) return p;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var tok = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) continue;
            string? Kv(string key)
            {
                for (int t = 1; t < tok.Length; t++)
                    if (tok[t].Length > key.Length && tok[t].StartsWith(key, StringComparison.Ordinal) && tok[t][key.Length] == '=')
                        return tok[t][(key.Length + 1)..];
                return null;
            }
            double D(string key, double def) => Kv(key) is { } s && double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : def;
            int I(string key, int def) => (int)Math.Round(D(key, def));
            switch (tok[0])
            {
                case "name" when tok.Length >= 2: p.Name = Unescape(tok[1]); break;
                case "source" when tok.Length >= 2: p.SourceKind = Unescape(tok[1]); p.SourceRef = tok.Length >= 3 ? Unescape(tok[2]) : ""; break;
                case "pack" when tok.Length >= 2: p.PackId = Unescape(tok[1]); p.PackName = tok.Length >= 3 ? Unescape(tok[2]) : ""; break;
                case "ccs":
                    for (int t = 1; t < tok.Length; t++)
                    {
                        var kv = tok[t].Split('=');
                        if (kv.Length == 2 && int.TryParse(kv[0], NumberStyles.Integer, Inv, out int cc) && int.TryParse(kv[1], NumberStyles.Integer, Inv, out int val))
                            p.Ccs[cc] = val;
                    }
                    break;
                case "file" when tok.Length >= 2: p.Files.Add(Unescape(tok[1])); break;
                case "group":
                    p.Groups.Add(new MosaicGroup
                    {
                        Name = Kv("name") is { } n ? Unescape(n) : "",
                        GainDb = D("gain", 0), Tune = D("tune", 0),
                        RrMode = Math.Clamp(I("rr", 0), 0, 2), SeqLen = Math.Clamp(I("seq", 1), 1, 64),
                    });
                    break;
                case "zone":
                    p.Zones.Add(new MosaicZone
                    {
                        File = I("f", -1), Group = I("g", 0), Root = Math.Clamp(I("root", 60), 0, 127),
                        KeyLo = Math.Clamp(I("klo", 0), 0, 127), KeyHi = Math.Clamp(I("khi", 127), 0, 127),
                        VelLo = Math.Clamp(I("vlo", 0), 0, 127), VelHi = Math.Clamp(I("vhi", 127), 0, 127),
                        Tune = D("tune", 0), GainDb = D("gain", 0), Pan = Math.Clamp(D("pan", 0), -1, 1),
                        Start = Math.Max(0, D("s", 0)), End = D("e", -1),
                        LoopMode = Math.Clamp(I("lm", 0), 0, 2), LoopStart = D("ls", -1), LoopEnd = D("le", -1),
                        Crossfade = Math.Clamp(D("xf", 0), 0, 2), Seq = Math.Max(1, I("seq", 1)),
                        RandLo = D("rlo", 0), RandHi = D("rhi", 1),
                        XKeyInLo = I("xkil", -1), XKeyInHi = I("xkih", -1), XKeyOutLo = I("xkol", -1), XKeyOutHi = I("xkoh", -1),
                        XVelInLo = I("xvil", -1), XVelInHi = I("xvih", -1), XVelOutLo = I("xvol", -1), XVelOutHi = I("xvoh", -1),
                        Release = D("trig", 0) >= 0.5, Excl = I("excl", 0), OffBy = I("off", 0), OffNormal = D("om", 0) >= 0.5,
                    });
                    break;
            }
        }
        if (p.Groups.Count == 0) p.Groups.Add(new MosaicGroup { Name = "Group 1" });
        p.Zones.RemoveAll(z => z.File < 0 || z.File >= p.Files.Count);
        foreach (var z in p.Zones) z.Group = Math.Clamp(z.Group, 0, p.Groups.Count - 1);
        return p;
    }

    /// <summary>%-escapes bytes at or below space, '%' and '=' (UTF-8 stays as it is).</summary>
    public static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            if (c <= ' ' || c == '%' || c == '=') sb.Append('%').Append(((int)c).ToString("X2", Inv));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Unescape(string s)
    {
        if (s.IndexOf('%') < 0) return s;
        var bytes = new List<byte>(s.Length);
        var src = Encoding.UTF8.GetBytes(s);
        for (int i = 0; i < src.Length; i++)
        {
            if (src[i] == '%' && i + 2 < src.Length && Hex(src[i + 1]) >= 0 && Hex(src[i + 2]) >= 0)
            {
                bytes.Add((byte)(Hex(src[i + 1]) * 16 + Hex(src[i + 2]))); i += 2;
            }
            else bytes.Add(src[i]);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
        static int Hex(byte b) => b is >= (byte)'0' and <= (byte)'9' ? b - '0' : b is >= (byte)'a' and <= (byte)'f' ? b - 'a' + 10 : b is >= (byte)'A' and <= (byte)'F' ? b - 'A' + 10 : -1;
    }
}

/// <summary>Sample references: "samples:<rel>" / "data:<rel>" against the host's roots, else a path.</summary>
public static class MosaicPaths
{
    public const string SamplesRoot = "samples", DataRoot = "data";

    /// <summary>A portable reference for an absolute path: relative to the Samples folder or
    /// the data folder when inside one of them (forward slashes), else the path itself.</summary>
    public static string ToRef(string path, string? samplesFolder, string? dataFolder)
    {
        string full;
        try { full = System.IO.Path.GetFullPath(path); } catch { return path; }
        foreach (var (name, root) in new[] { (SamplesRoot, samplesFolder), (DataRoot, dataFolder) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            string r;
            try { r = System.IO.Path.GetFullPath(root).TrimEnd('/', '\\'); } catch { continue; }
            if (full.Length > r.Length + 1 && full.StartsWith(r, StringComparison.OrdinalIgnoreCase) && (full[r.Length] == '/' || full[r.Length] == '\\'))
                return name + ":" + full[(r.Length + 1)..].Replace('\\', '/');
        }
        return full;
    }

    /// <summary>The absolute path a reference names, given the roots (null when a root is unknown).</summary>
    public static string? Resolve(string reference, string? samplesFolder, string? dataFolder)
    {
        int colon = reference.IndexOf(':');
        if (colon >= 2 && !reference.StartsWith('/'))
        {
            string name = reference[..colon];
            string? root = name == SamplesRoot ? samplesFolder : name == DataRoot ? dataFolder : null;
            if (name is SamplesRoot or DataRoot)
                return string.IsNullOrEmpty(root) ? null : System.IO.Path.Combine(root, reference[(colon + 1)..].Replace('/', System.IO.Path.DirectorySeparatorChar));
        }
        return reference;
    }

    public static string FileName(string reference)
    {
        int cut = Math.Max(reference.LastIndexOf('/'), Math.Max(reference.LastIndexOf('\\'), reference.IndexOf(':') >= 2 ? reference.IndexOf(':') : -1));
        return cut >= 0 ? reference[(cut + 1)..] : reference;
    }
}
