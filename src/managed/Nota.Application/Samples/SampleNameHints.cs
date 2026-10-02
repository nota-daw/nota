// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Globalization;
using System.Text.RegularExpressions;

namespace Nota.Application.Samples;

/// <summary>What a sample's file name and folders say about it. Pack authors usually write
/// the tempo and key into the name ("DH_Loop_124_Am.wav", "Pad C#m 90bpm") and sort loops
/// from one-shots by folder — facts the analysis can only estimate.</summary>
public sealed record SampleNameHints(
    double Bpm,                // 0 = none
    bool BpmExplicit,          // written with "bpm"; a bare number is only a candidate
    MusicalKey? Key,
    SampleKind NameKind,       // from the file name
    SampleKind FolderKind,     // from the folders between the library root and the file
    bool DrumWord)             // kick / snare / hat … — a weak one-shot hint
{
    public static readonly SampleNameHints None = new(0, false, null, SampleKind.Unknown, SampleKind.Unknown, false);

    private static readonly Regex BpmBefore = new(@"(?<![\d.])(\d{2,3}(?:\.\d{1,2})?)\s*-?\s*bpm(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BpmAfter = new(@"(?<![a-z])bpm\s*[-_ ]?\s*(\d{2,3}(?:\.\d{1,2})?)(?![\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Separators = new(@"[\s_\-.,()\[\]{}+&=~]+", RegexOptions.CultureInvariant);

    // A key token. Group 1 letter, 2 accidental, 3 mode word, 4 trailing chord extension / octave.
    private static readonly Regex KeyToken = new(
        @"^([A-Ga-g])(#|♯|♭|b|sharp|flat)?(major|maj|minor|min|m)?(\d{0,2})$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> LoopWords = new(StringComparer.OrdinalIgnoreCase) { "loop", "loops", "looped", "groove", "grooves" };
    private static readonly HashSet<string> ShotWords = new(StringComparer.OrdinalIgnoreCase) { "oneshot", "oneshots", "shot", "shots", "hit", "hits", "stab", "stabs" };
    private static readonly HashSet<string> DrumWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "kick", "kicks", "bd", "snare", "snares", "sd", "clap", "claps", "hat", "hats", "hihat", "hihats", "hh",
        "perc", "percs", "rim", "rimshot", "tom", "toms", "cymbal", "cymbals", "crash", "ride", "shaker", "snap", "snaps",
    };

    /// <summary>Reads the hints for <paramref name="path"/>. <paramref name="root"/> (the
    /// library folder) bounds which folders count; null = only the file's own folder.</summary>
    public static SampleNameHints Parse(string path, string? root = null)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var tokens = Tokens(name);

        double bpm = 0; bool bpmExplicit = false;
        var bm = BpmBefore.Match(name);
        if (!bm.Success) bm = BpmAfter.Match(name);
        if (bm.Success && double.TryParse(bm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b is >= 40 and <= 250)
        { bpm = b; bpmExplicit = true; }
        int bpmToken = -1;
        if (bpm == 0)
        {
            // A bare number in tempo range is a candidate; the classifier checks it against the audio.
            for (int i = tokens.Count - 1; i >= 0; i--)
                if (tokens[i].Length is 2 or 3 && int.TryParse(tokens[i], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 60 and <= 200)
                { bpm = n; bpmToken = i; break; }
        }
        else
        {
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i].Contains("bpm", StringComparison.OrdinalIgnoreCase) || (i + 1 < tokens.Count && tokens[i + 1].Equals("bpm", StringComparison.OrdinalIgnoreCase)))
                { bpmToken = i; break; }
        }

        var key = KeyOf(tokens, bpmToken, bpm > 0);
        var nameKind = KindOf(tokens, out bool drum);

        var folderKind = SampleKind.Unknown;
        foreach (var seg in Folders(path, root))
        {
            var k = KindOf(Tokens(seg), out bool d);
            drum |= d;
            if (k != SampleKind.Unknown) folderKind = k;   // the innermost folder that says wins
        }
        return new SampleNameHints(bpm, bpmExplicit, key, nameKind, folderKind, drum);
    }

    private static List<string> Tokens(string s)
        => Separators.Split(s).Where(t => t.Length > 0).ToList();

    private static IEnumerable<string> Folders(string path, string? root)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) yield break;
        if (root is null) { yield return Path.GetFileName(dir); yield break; }
        var rel = Path.GetRelativePath(root, dir);
        if (rel == "." || rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
        { yield return Path.GetFileName(dir); yield break; }
        foreach (var seg in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            yield return seg;
    }

    private static SampleKind KindOf(List<string> tokens, out bool drum)
    {
        drum = false;
        var kind = SampleKind.Unknown;
        for (int i = 0; i < tokens.Count; i++)
        {
            string t = tokens[i];
            if (LoopWords.Contains(t)) return SampleKind.Loop;   // "Kick Loop" is a loop
            if (ShotWords.Contains(t) || (t.Equals("one", StringComparison.OrdinalIgnoreCase)
                                          && i + 1 < tokens.Count && tokens[i + 1].StartsWith("shot", StringComparison.OrdinalIgnoreCase)))
                kind = SampleKind.OneShot;
            if (DrumWords.Contains(t)) drum = true;
        }
        return kind;
    }

    private static MusicalKey? KeyOf(List<string> tokens, int bpmToken, bool hasBpm)
    {
        MusicalKey? strong = null, weak = null;
        for (int i = 0; i < tokens.Count; i++)
        {
            var m = KeyToken.Match(tokens[i]);
            if (!m.Success) continue;
            string letter = m.Groups[1].Value, acc = m.Groups[2].Value, mode = m.Groups[3].Value, tail = m.Groups[4].Value;
            bool upper = char.IsUpper(letter[0]);
            // A lowercase 'b' after a letter is a flat; "Bb" — but "bb" / "db" are words, not keys.
            if (acc == "b" && !upper) continue;
            int tonic = MusicalKey.TonicOf(letter, acc);
            string next = i + 1 < tokens.Count ? tokens[i + 1] : "";

            if (mode.Length == 0 && tail.Length == 0
                && (next.StartsWith("min", StringComparison.OrdinalIgnoreCase) || next.StartsWith("maj", StringComparison.OrdinalIgnoreCase))
                && next.Length is 3 or 5)
            {   // "A minor", "Eb maj"
                strong = new MusicalKey(tonic, next.StartsWith("min", StringComparison.OrdinalIgnoreCase) ? KeyMode.Minor : KeyMode.Major);
                continue;
            }
            if (mode.Length > 1 || (mode == "m" && upper))
            {   // "Amin", "Cmaj7", "C#m", "Dm9" — "am" / "em" alone are words
                bool minor = mode == "m" || mode.StartsWith("min", StringComparison.OrdinalIgnoreCase);
                strong = new MusicalKey(tonic, minor ? KeyMode.Minor : KeyMode.Major);
                continue;
            }
            if (mode.Length > 0) continue;
            if (tail.Length == 1)
            {   // "C4", "F#2": a one-shot's pitch
                strong = new MusicalKey(tonic, KeyMode.Note);
                continue;
            }
            if (tail.Length > 0 || !upper) continue;
            if (acc.Length > 0) { strong = new MusicalKey(tonic, KeyMode.Major); continue; }   // "F#", "Eb"
            // A lone capital letter is as often a take ("Kick A") as a key: only next to the
            // tempo, after the word "key", or closing a name that carries a tempo.
            bool byBpm = bpmToken >= 0 && Math.Abs(i - bpmToken) == 1;
            bool afterKey = i > 0 && tokens[i - 1].Equals("key", StringComparison.OrdinalIgnoreCase);
            if (byBpm || afterKey || (hasBpm && i == tokens.Count - 1)) weak = new MusicalKey(tonic, KeyMode.Major);
        }
        return strong ?? weak;
    }
}
