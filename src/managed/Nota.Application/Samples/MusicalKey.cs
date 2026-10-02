// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Text.RegularExpressions;

namespace Nota.Application.Samples;

/// <summary>Major, minor, or a pitch whose mode can't be told (a single note, an 808).</summary>
public enum KeyMode : byte { Major, Minor, Note }

/// <summary>A musical key (tonic 0..11 = C..B plus a mode) — what the Files tab filters on
/// and what the project key is. <see cref="KeyMode.Note"/> is a tonal one-shot: it has a
/// pitch but no scale.</summary>
public readonly record struct MusicalKey(int Tonic, KeyMode Mode)
{
    // Spelled the way sample packs and chord charts usually spell them.
    private static readonly string[] MajorNames = { "C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };
    private static readonly string[] MinorNames = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "G#", "A", "Bb", "B" };

    /// <summary>The 24 keys in picker order: C major, C minor, Db major, C# minor, …</summary>
    public static IReadOnlyList<MusicalKey> All { get; } =
        Enumerable.Range(0, 12).SelectMany(t => new[] { new MusicalKey(t, KeyMode.Major), new MusicalKey(t, KeyMode.Minor) }).ToArray();

    public string TonicName => (Mode == KeyMode.Minor ? MinorNames : MajorNames)[Mod12(Tonic)];

    /// <summary>"Am", "F#", "Eb" (a note reads like its major key: the letter alone).</summary>
    public string Short => Mode == KeyMode.Minor ? TonicName + "m" : TonicName;

    /// <summary>"A minor", "Eb major", "F#".</summary>
    public string Long => Mode switch
    {
        KeyMode.Major => TonicName + " major",
        KeyMode.Minor => TonicName + " minor",
        _ => TonicName,
    };

    public override string ToString() => Short;

    /// <summary>The relative key — same notes, other tonic (A minor ↔ C major). A note has none.</summary>
    public MusicalKey? Relative => Mode switch
    {
        KeyMode.Major => new MusicalKey(Mod12(Tonic + 9), KeyMode.Minor),
        KeyMode.Minor => new MusicalKey(Mod12(Tonic + 3), KeyMode.Major),
        _ => null,
    };

    /// <summary>Whether a sample in this key sits in <paramref name="target"/> as is: the same
    /// key, its relative, or a note that is the target's tonic.</summary>
    public bool FitsIn(MusicalKey target)
    {
        if (Mode == KeyMode.Note || target.Mode == KeyMode.Note) return Mod12(Tonic) == Mod12(target.Tonic);
        return this == target || Relative == target;
    }

    /// <summary>Semitones (−6..+5, the shorter way) that move this key onto <paramref name="target"/>.
    /// Across modes the relative is used, so A minor → C major is 0 (same notes, nothing to move)
    /// and A minor → D major is +2 (A minor's notes become B minor's, D major's relative).</summary>
    public int SemitonesTo(MusicalKey target)
    {
        int from = Mod12(Tonic), to = Mod12(target.Tonic);
        if (Mode != KeyMode.Note && target.Mode != KeyMode.Note && Mode != target.Mode)
            to = Mod12(target.Relative!.Value.Tonic);
        int d = Mod12(to - from);
        return d > 5 ? d - 12 : d;
    }

    /// <summary>Compact persistence code: 0..11 major, 12..23 minor, 24..35 note.</summary>
    public int Code => (int)Mode * 12 + Mod12(Tonic);

    public static MusicalKey? FromCode(int code)
        => code is >= 0 and < 36 ? new MusicalKey(code % 12, (KeyMode)(code / 12)) : null;

    private static readonly Regex KeyText = new(
        @"^\s*([A-Ga-g])\s*([#♯b♭]|sharp|flat)?\s*(major|maj|minor|min|m)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Parses "Am", "A minor", "F#", "Eb maj", "Bbmin", "c#m". A bare letter is a
    /// major key. Null for anything else.</summary>
    public static MusicalKey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = KeyText.Match(text);
        if (!m.Success) return null;
        int tonic = TonicOf(m.Groups[1].Value, m.Groups[2].Value);
        // "M" alone is ambiguous in the wild ("CM" = C major to some, a typo to most); only the
        // lowercase m and the spelled-out words say minor.
        string mode = m.Groups[3].Value;
        bool minor = mode == "m" || mode.StartsWith("min", StringComparison.OrdinalIgnoreCase);
        return new MusicalKey(tonic, minor ? KeyMode.Minor : KeyMode.Major);
    }

    internal static int TonicOf(string letter, string accidental)
    {
        int t = char.ToUpperInvariant(letter[0]) switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, _ => 11,
        };
        if (accidental is "#" or "♯" || accidental.Equals("sharp", StringComparison.OrdinalIgnoreCase)) t++;
        else if (accidental is "b" or "♭" || accidental.Equals("flat", StringComparison.OrdinalIgnoreCase)) t--;
        return Mod12(t);
    }

    private static int Mod12(int v) => ((v % 12) + 12) % 12;
}
