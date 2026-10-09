// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Auto-multisample: a folder of "trombone_gb4.wav", "trombone_f_c3_rr2.wav",
// "trombone_a2_rel.wav" … becomes Nota Mosaic programs, one per instrument.
//
//   1. Each file name splits into tokens: the note (gb4, c#3, db2, cs4, a MIDI number
//      "_060" / "n60"), a velocity layer (pp … fff, v1 … vN, vel3, soft / med / hard), a
//      round-robin step (rr2, seq2), a release mark (rel, release). The rest is the
//      instrument's stem — "trombone" and "trombone_mute" are two instruments. Words that
//      only mark a take ("take3", "copy", "final") don't make a stem of their own.
//   2. A file whose name names no note gets one from pitch detection (lower confidence).
//   3. Octave numbering differs between makers (C3 or C4 = 60). The pitch detector checks a
//      few named files; a steady ±12 shifts the whole set.
//   4. Layers split velocity 1 … 127 evenly in dynamic order; each root covers the keys up
//      to half-way to its neighbours, the outermost stretch ±N semitones (12 by default).
//   5. Problems are listed, not hidden: duplicates (same note, layer and step), gaps (where
//      neighbours had to stretch far), notes that only came from analysis.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nota.Application.Mosaic;

/// <summary>What the mapper made of one file.</summary>
public sealed class MappedFile
{
    public string Path = "", Name = "", Stem = "";
    public int Note = -1;               // after the octave shift
    public int NamedNote = -1;          // as the name spelled it (before the shift)
    public string NoteFrom = "";        // "name" · "midi" · "analysis" · ""
    public string Layer = "";           // the velocity token, "" = one layer
    public bool LayerGuessed;           // no layer in the name; joined the softest one
    public int Rr = 0;                  // 0 = none
    public bool Release;
    public int Confidence;              // 0 … 100
    public bool Duplicate;
    public int KeyLo = -1, KeyHi = -1, VelLo = 1, VelHi = 127;
    public bool Stretched, NearGap;

    public string NoteText => Note < 0 ? "—" : MosaicNames.NoteName(Note);
}

/// <summary>One instrument found in a folder.</summary>
public sealed class MappedInstrument
{
    public string Stem = "";
    public List<MappedFile> Files = new();
    public bool Selected = true;
    public IEnumerable<MappedFile> Used => Files.Where(f => !f.Duplicate && f.Note >= 0);
    public int Roots => Used.Where(f => !f.Release).Select(f => f.Note).Distinct().Count();
    public List<string> Layers => Used.Where(f => !f.Release).Select(f => f.Layer).Distinct().OrderBy(MultisampleMapper.LayerOrder).ToList();
    public int MaxRr => Used.Select(f => f.Rr).DefaultIfEmpty(0).Max();
    public int Confidence => Used.Any() ? (int)Math.Round(Used.Average(f => f.Confidence)) : 0;
    public (int Lo, int Hi) Span => Used.Any() ? (Used.Min(f => f.KeyLo), Used.Max(f => f.KeyHi)) : (0, 0);
    public List<int> Gaps = new();      // root-less keys where neighbours stretched more than 2 st
}

public sealed class MultisampleOptions
{
    /// <summary>How far the outermost roots stretch past themselves, semitones.</summary>
    public int EdgeStretch = 12;
    /// <summary>Semitones added to every named note (+12 when C3 = 60). Null = detect.</summary>
    public int? OctaveShift;
    public bool ReleaseGroup = true;
    public int RrMode = 2;              // random without repeat
}

public sealed class MultisampleProposal
{
    public string Folder = "";
    public List<MappedInstrument> Instruments = new();
    public int OctaveShift;             // what was applied
    public string OctaveWhy = "";       // "Pitch detector on 3 files (gb4, c3, f2): offset is a stable +12."
    public int Unmapped;                // files with no note at all
    public long Bytes;
}

public static class MultisampleMapper
{
    private static readonly string[] Dynamics = { "pppp", "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "ffff" };
    private static readonly string[] Softness = { "soft", "med", "medium", "mid", "hard", "loud" };
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
        { "take", "copy", "final", "edit", "new", "old", "mono", "stereo", "st", "norm", "normalized", "trimmed", "sample", "samp", "smp" };
    public static readonly string[] AudioExtensions = { ".wav", ".flac", ".mp3", ".aif", ".aiff" };

    /// <summary>Velocity-layer sort key: dynamics in musical order, then numbers.</summary>
    public static int LayerOrder(string layer)
    {
        if (layer.Length == 0) return 0;
        int d = Array.IndexOf(Dynamics, layer);
        if (d >= 0) return 100 + d;
        int s = Array.IndexOf(Softness, layer);
        if (s >= 0) return 200 + s;
        var m = Regex.Match(layer, @"\d+");
        return m.Success && int.TryParse(m.Value, out int n) ? 300 + n : 999;
    }

    // ---- tokens ------------------------------------------------------------------------------
    private static readonly Regex NoteTok = new(@"^([a-g])(#|s|b|♯|♭)?(-?\d)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MidiTok = new(@"^n?(\d{2,3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VelTok = new(@"^(v|vel|vl|velocity|layer|dyn)(\d{1,3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RrTok = new(@"^(rr|seq|robin)(\d{1,2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TakeTok = new(@"^(take|t)(\d{1,2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Parses one file name. Exposed for the tests and the MCP.</summary>
    public static MappedFile ParseName(string path)
    {
        var f = new MappedFile { Path = path, Name = System.IO.Path.GetFileName(path) };
        string baseName = System.IO.Path.GetFileNameWithoutExtension(path);
        // Split on separators and on letter→digit-group borders a name glues ("PianoC4" stays one: too risky).
        var tokens = Regex.Split(baseName, @"[\s_\-\.\(\)\[\]]+").Where(t => t.Length > 0).ToList();
        var stem = new List<string>();
        int noteAt = -1;
        // The note: the last note-like token wins ("bass_16_1_c2", "piano_c4_rel").
        for (int i = tokens.Count - 1; i >= 0 && f.NamedNote < 0; i--)
            if (NoteOf(tokens[i]) is { } n) { f.NamedNote = n; f.NoteFrom = "name"; noteAt = i; }
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i == noteAt) continue;
            string t = tokens[i], tl = t.ToLowerInvariant();
            if (RrTok.Match(tl) is { Success: true } rm) { f.Rr = int.Parse(rm.Groups[2].Value); continue; }
            if (tl is "rel" or "release" or "rls" or "relse") { f.Release = true; continue; }
            if (Array.IndexOf(Dynamics, tl) >= 0 && i > 0) { f.Layer = tl; continue; }   // "f" first is a name, not forte
            if (Array.IndexOf(Softness, tl) >= 0) { f.Layer = tl; continue; }
            if (VelTok.Match(tl) is { Success: true } vm) { f.Layer = "v" + int.Parse(vm.Groups[2].Value); continue; }
            if (Noise.Contains(tl) || TakeTok.IsMatch(tl)) continue;
            stem.Add(tl);
        }
        // No note name: a MIDI number among the leftovers (the last one in 0 … 127).
        if (f.NamedNote < 0)
            for (int i = stem.Count - 1; i >= 0; i--)
                if (MidiTok.Match(stem[i]) is { Success: true } mm && int.Parse(mm.Groups[1].Value) is var mn && mn is >= 12 and <= 120)
                { f.NamedNote = mn; f.NoteFrom = "midi"; stem.RemoveAt(i); break; }
        f.Stem = stem.Count > 0 ? string.Join("_", stem) : "sample";
        f.Note = f.NamedNote;
        f.Confidence = f.NoteFrom switch { "name" => 98, "midi" => 95, _ => 0 };
        if (f.NoteFrom == "name" && f.Layer.Length == 0 && f.Rr == 0 && !f.Release && stem.Count == 0) f.Confidence = 90;
        return f;
    }

    /// <summary>A note token: "c4", "c#3", "db2", "cs4", "gb6", "a-1" (C4 = 60). Null otherwise.</summary>
    public static int? NoteOf(string token)
    {
        var m = NoteTok.Match(token);
        if (!m.Success) return null;
        int pc = char.ToLowerInvariant(m.Groups[1].Value[0]) switch { 'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5, 'g' => 7, 'a' => 9, _ => 11 };
        string acc = m.Groups[2].Value.ToLowerInvariant();
        if (acc is "#" or "s" or "♯") pc++;
        else if (acc is "b" or "♭") pc--;
        int note = (int.Parse(m.Groups[3].Value) + 1) * 12 + pc;
        return note is >= 0 and <= 127 ? note : null;
    }

    // ---- the folder ------------------------------------------------------------------------------
    /// <summary>Maps the audio files under <paramref name="folder"/> (recursively). <paramref name="detect"/>
    /// returns a file's fundamental as a MIDI note (fractional) or null — pitch detection for files
    /// without a note in their name and for the octave check; null skips both.</summary>
    public static MultisampleProposal MapFolder(string folder, MultisampleOptions? opt = null, Func<string, double?>? detect = null)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(IsAudio).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch { files = Array.Empty<string>(); }
        var prop = MapFiles(files, opt, detect);
        prop.Folder = folder;
        return prop;
    }

    public static bool IsAudio(string path) => AudioExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    public static MultisampleProposal MapFiles(IEnumerable<string> paths, MultisampleOptions? opt = null, Func<string, double?>? detect = null)
    {
        opt ??= new MultisampleOptions();
        var prop = new MultisampleProposal();
        var parsed = paths.Select(ParseName).ToList();
        foreach (var f in parsed) { try { prop.Bytes += new FileInfo(f.Path).Length; } catch { } }

        // Octave check: up to three named files across the range.
        int shift = opt.OctaveShift ?? 0;
        if (opt.OctaveShift is null && detect is not null)
        {
            var named = parsed.Where(f => f.NoteFrom == "name" && !f.Release).OrderBy(f => f.NamedNote).ToList();
            var probe = named.Count <= 3 ? named : new List<MappedFile> { named[named.Count / 6], named[named.Count / 2], named[named.Count * 5 / 6] };
            var offs = new List<int>();
            foreach (var f in probe)
                if (detect(f.Path) is { } heard)
                {
                    int off = (int)Math.Round((heard - f.NamedNote) / 12.0) * 12;
                    offs.Add(off);
                }
            if (offs.Count > 0 && offs.All(o => o == offs[0]) && Math.Abs(offs[0]) == 12)
            {
                shift = offs[0];
                prop.OctaveWhy = $"Pitch detector on {offs.Count} files ({string.Join(", ", probe.Take(3).Select(f => ShortNote(f.NamedNote)))}): offset is a stable {(shift > 0 ? "+" : "−")}12. Whole set shifted one octave.";
            }
            else if (offs.Count > 0)
                prop.OctaveWhy = $"Pitch detector on {offs.Count} files agrees with the names.";
        }
        prop.OctaveShift = shift;
        foreach (var f in parsed.Where(f => f.NamedNote >= 0)) f.Note = Math.Clamp(f.NamedNote + shift, 0, 127);

        // Files with no note: pitch detection.
        foreach (var f in parsed.Where(f => f.Note < 0))
        {
            if (detect?.Invoke(f.Path) is { } m)
            {
                f.Note = Math.Clamp((int)Math.Round(m), 0, 127);
                f.NoteFrom = "analysis";
                double cents = Math.Abs(m - Math.Round(m)) * 100;
                f.Confidence = (int)Math.Clamp(70 - cents / 2, 40, 70);
            }
            else prop.Unmapped++;
        }

        // Instruments by stem; files with no note at all join their stem but play nothing.
        foreach (var g in parsed.GroupBy(f => f.Stem).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var inst = new MappedInstrument { Stem = g.Key, Files = g.ToList() };
            prop.Instruments.Add(inst);
        }
        MergeStragglers(prop);
        foreach (var inst in prop.Instruments) Layout(inst, opt);
        // Biggest, most confident first.
        prop.Instruments = prop.Instruments.OrderByDescending(i => i.Used.Count()).ThenBy(i => i.Stem, StringComparer.Ordinal).ToList();
        return prop;
    }

    // A one-file "instrument" whose stem starts with a real instrument's stem is a take of it.
    private static void MergeStragglers(MultisampleProposal prop)
    {
        foreach (var small in prop.Instruments.Where(i => i.Files.Count == 1).ToList())
        {
            var host = prop.Instruments.Where(i => i != small && i.Files.Count > 1 && small.Stem.StartsWith(i.Stem + "_", StringComparison.Ordinal))
                                       .OrderByDescending(i => i.Stem.Length).FirstOrDefault();
            if (host is null) continue;
            foreach (var f in small.Files) f.Stem = host.Stem;
            host.Files.AddRange(small.Files);
            prop.Instruments.Remove(small);
        }
    }

    // Duplicates, velocity splits, key ranges, gaps.
    private static void Layout(MappedInstrument inst, MultisampleOptions opt)
    {
        var seen = new HashSet<(int, string, int, bool)>();
        foreach (var f in inst.Files.OrderBy(f => f.Name.Contains("copy", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ThenBy(f => f.Name, StringComparer.Ordinal))
        {
            if (f.Note < 0) continue;
            f.Duplicate = !seen.Add((f.Note, f.Layer, f.Rr, f.Release));
        }
        // A file without a layer among layered ones (a stray take) joins the softest layer.
        var named = inst.Used.Where(f => !f.Release && f.Layer.Length > 0).Select(f => f.Layer).Distinct().OrderBy(LayerOrder).ToList();
        if (named.Count > 0)
            foreach (var f in inst.Used.Where(f => !f.Release && f.Layer.Length == 0)) { f.Layer = named[0]; f.LayerGuessed = true; }
        var layers = inst.Layers;
        for (int li = 0; li < layers.Count; li++)
        {
            // Two layers: 1 – 63 · 64 – 127.
            int lo = li == 0 ? 1 : (int)Math.Round(126.0 * li / layers.Count) + 1;
            int hi = li == layers.Count - 1 ? 127 : (int)Math.Round(126.0 * (li + 1) / layers.Count);
            foreach (var f in inst.Used.Where(f => !f.Release && f.Layer == layers[li])) { f.VelLo = lo; f.VelHi = hi; }
        }
        // Key ranges per (layer, rr) — and for the release set.
        inst.Gaps.Clear();
        foreach (var set in inst.Used.GroupBy(f => (f.Release, f.Release ? "" : f.Layer, f.Rr)))
        {
            var roots = set.Select(f => f.Note).Distinct().OrderBy(n => n).ToList();
            for (int i = 0; i < roots.Count; i++)
            {
                int r = roots[i];
                int lo = i == 0 ? Math.Max(0, r - opt.EdgeStretch) : (roots[i - 1] + r) / 2 + 1;
                int hi = i == roots.Count - 1 ? Math.Min(127, r + opt.EdgeStretch) : (r + roots[i + 1]) / 2;
                foreach (var f in set.Where(f => f.Note == r))
                {
                    f.KeyLo = lo; f.KeyHi = hi;
                    f.Stretched = r - lo > 2 || hi - r > 2;
                    if (set.Key.Release) { f.VelLo = 1; f.VelHi = 127; }
                }
                if (i > 0 && r - roots[i - 1] > 5 && !set.Key.Release)
                {
                    int gapKey = (roots[i - 1] + r) / 2;
                    if (!inst.Gaps.Contains(gapKey)) inst.Gaps.Add(gapKey);
                    foreach (var f in set.Where(f => f.Note == r || f.Note == roots[i - 1])) f.NearGap = true;
                }
            }
        }
    }

    /// <summary>The program for one instrument: a group per velocity layer (with its round-robin)
    /// and a release group. <paramref name="toRef"/> maps a path to the stored reference.</summary>
    public static MosaicProgram Build(MappedInstrument inst, MultisampleOptions? opt = null, Func<string, string>? toRef = null, string? name = null)
    {
        opt ??= new MultisampleOptions();
        var p = new MosaicProgram { Name = name ?? Pretty(inst.Stem), SourceKind = "folder" };
        var fileIdx = new Dictionary<string, int>(StringComparer.Ordinal);
        int FileOf(MappedFile f)
        {
            if (!fileIdx.TryGetValue(f.Path, out int i)) { i = p.Files.Count; fileIdx[f.Path] = i; p.Files.Add(toRef is null ? f.Path : toRef(f.Path)); }
            return i;
        }
        var layers = inst.Layers;
        foreach (var layer in layers)
        {
            var g = new MosaicGroup { Name = layers.Count > 1 ? "Sustain · " + (layer.Length > 0 ? layer : "—") : "Sustain", SeqLen = Math.Max(1, inst.Used.Where(f => !f.Release && f.Layer == layer).Select(f => f.Rr).DefaultIfEmpty(0).Max()), RrMode = opt.RrMode };
            p.Groups.Add(g);
            int gi = p.Groups.Count - 1;
            foreach (var f in inst.Used.Where(f => !f.Release && f.Layer == layer).OrderBy(f => f.Note).ThenBy(f => f.Rr))
                p.Zones.Add(new MosaicZone { File = FileOf(f), Group = gi, Root = f.Note, KeyLo = f.KeyLo, KeyHi = f.KeyHi, VelLo = f.VelLo, VelHi = f.VelHi, Seq = Math.Max(1, f.Rr) });
        }
        var rel = inst.Used.Where(f => f.Release).ToList();
        if (rel.Count > 0 && opt.ReleaseGroup)
        {
            p.Groups.Add(new MosaicGroup { Name = "Release", SeqLen = Math.Max(1, rel.Max(f => f.Rr)), RrMode = opt.RrMode });
            int gi = p.Groups.Count - 1;
            foreach (var f in rel.OrderBy(f => f.Note))
                p.Zones.Add(new MosaicZone { File = FileOf(f), Group = gi, Root = f.Note, KeyLo = f.KeyLo, KeyHi = f.KeyHi, VelLo = 0, VelHi = 127, Seq = Math.Max(1, f.Rr), Release = true });
        }
        if (p.Groups.Count == 0) p.Groups.Add(new MosaicGroup { Name = "Sustain" });
        // The lowest layer starts at velocity 0 so a note at 0 isn't silent.
        foreach (var z in p.Zones) if (z.VelLo == 1) z.VelLo = 0;
        return p;
    }

    public static string Pretty(string stem)
    {
        var words = stem.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private static string ShortNote(int n) => n < 0 ? "?" : MosaicNames.NoteName(n).Replace("♯", "#").ToLowerInvariant();
}

/// <summary>Note names as Nota writes them (C4 = 60, sharps).</summary>
public static class MosaicNames
{
    private static readonly string[] Pc = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    public static string NoteName(int n) => n is < 0 or > 127 ? "—" : Pc[n % 12] + (n / 12 - 1);
}
