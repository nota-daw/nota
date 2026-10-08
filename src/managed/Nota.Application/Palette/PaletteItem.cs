// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-6a): one searchable entry — an action, a track, a device or plug-in,
// a preset, or a Modular node — with everything the search needs precomputed when the index
// is built, so a keystroke only reads.

namespace Nota.Application.Palette;

public enum PaletteKind { Action, Track, Device, Preset, Modular }

/// <summary>Where a device or preset goes in a chain — decides insertion and the target line.</summary>
public enum PaletteDeviceRole { None, Instrument, AudioEffect, MidiEffect, Modular }

/// <summary>Where a preset comes from — decides whether it can load in place (CP-3).</summary>
public enum PresetOrigin { None, Factory, Kit, User }

public sealed class PaletteItem
{
    public required PaletteKind Kind { get; init; }
    /// <summary>Stable id: recents and frecency are keyed by it ("a:transport.play",
    /// "d:be:2", "p:factory:reverb/Big Hall", "t:7").</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>The secondary text after the name: device · folder, vendor · format, track type.</summary>
    public string Sub { get; init; } = "";
    /// <summary>The quiet tag on the right edge ("Audio FX", "Plug-in", "Preset", "Track").</summary>
    public string Tag { get; init; } = "";
    /// <summary>Actions: the shortcut in menu notation ("⌘⇧P"), or "".</summary>
    public string Gesture { get; init; } = "";
    public PaletteDeviceRole Role { get; init; }
    public bool IsPlugin { get; init; }
    public PresetOrigin PresetOrigin { get; init; }
    public bool IsFavorite { get; set; }
    /// <summary>Actions: the main view they belong to (boosted there, CP-8), or "".</summary>
    public string View { get; init; } = "";
    /// <summary>A device or preset that can be auditioned (CP-21, P1).</summary>
    public bool CanPreview { get; init; }

    public SemanticDescriptor? Descriptor { get; init; }
    /// <summary>For presets: what the preset's own folder and name say (no inheritance).</summary>
    public SemanticDescriptor? OwnDescriptor { get; init; }
    public string[] Aliases { get; init; } = [];
    /// <summary>Device / vendor text, searched after the name and aliases.</summary>
    public string Device { get; init; } = "";
    /// <summary>Folder / category text, searched after the device.</summary>
    public string Folder { get; init; } = "";
    /// <summary>The user's tag titles, searched last.</summary>
    public string[] UserTags { get; init; } = [];

    /// <summary>What applying needs: a command id, a track id, a browser row … opaque here.</summary>
    public object? Payload { get; init; }

    // ---- precomputed for search ---------------------------------------------------------
    internal string NameNorm = "";
    internal int CoreOffset;            // 5 when the name starts "Nota " — scored on the distinctive word
    internal string CoreCompact = "";   // the core, lower case, without spaces (exact-match bonus)
    internal ulong WordStarts;          // bit i: name[i] starts a word (first 64 chars)
    internal (int Start, int Length)[] Words = [];
    internal string[] AliasNorm = [];
    internal string DeviceNorm = "", FolderNorm = "", TagsNorm = "";
    internal ulong DeviceStarts, FolderStarts, TagsStarts;
    internal int CoreLetters, NameLetters, DeviceLetters, FolderLetters, TagsLetters;
    internal bool Prepared;
    internal TermSet Terms, OwnTerms;

    /// <summary>Fills the search fields. Called by <see cref="PaletteIndex"/> on insert.</summary>
    internal void Prepare()
    {
        NameNorm = TextNorm.Normalize(Name);
        CoreOffset = Name.StartsWith("Nota ", StringComparison.Ordinal) && Name.Length > 5 ? 5 : 0;
        CoreCompact = NameNorm[CoreOffset..].Replace(" ", "");
        WordStarts = 0;
        var words = new List<(int, int)>();
        for (int i = 0; i < Name.Length; i++)
        {
            bool start = IsWordStart(Name, i);
            if (start && i < 64) WordStarts |= 1UL << i;
            if (start && char.IsLetterOrDigit(Name[i]))
            {
                int j = i;
                while (j < Name.Length && char.IsLetterOrDigit(Name[j])) j++;
                words.Add((i, j - i));
            }
        }
        Words = words.ToArray();
        var aliases = new List<string>();
        foreach (var a in Aliases) aliases.Add(TextNorm.Normalize(a).Replace(" ", ""));
        if (Descriptor is { } d) foreach (var a in d.Aliases) aliases.Add(TextNorm.Normalize(a).Replace(" ", ""));
        AliasNorm = aliases.Distinct().ToArray();
        DeviceNorm = TextNorm.Normalize(Device);
        FolderNorm = TextNorm.Normalize(Folder);
        TagsNorm = TextNorm.Normalize(string.Join(' ', UserTags));
        DeviceStarts = FuzzyMatcher.StartsOf(DeviceNorm);
        FolderStarts = FuzzyMatcher.StartsOf(FolderNorm);
        TagsStarts = FuzzyMatcher.StartsOf(TagsNorm);
        CoreLetters = FuzzyMatcher.Letters(NameNorm, CoreOffset);
        NameLetters = FuzzyMatcher.Letters(NameNorm);
        DeviceLetters = FuzzyMatcher.Letters(DeviceNorm);
        FolderLetters = FuzzyMatcher.Letters(FolderNorm);
        TagsLetters = FuzzyMatcher.Letters(TagsNorm);
        Terms = Descriptor?.Terms() ?? default;
        OwnTerms = OwnDescriptor?.Terms() ?? default;
        // The user's tags may name vocabulary terms too ("warm").
        foreach (var t in UserTags) if (SemanticVocabulary.Exact(t) is { } term) Terms.Add(term);
        Prepared = true;
    }

    // A word starts after a separator, at a lower→upper case change (camelCase), and where
    // digits begin ("EQ-8": E and 8).
    internal static bool IsWordStart(string text, int k)
    {
        if (k == 0) return true;
        char p = text[k - 1], c = text[k];
        if (p is ' ' or '-' or '·' or '.' or '&' or '_' or '/' or '(' or '|') return char.IsLetterOrDigit(c);
        if (char.IsUpper(c) && char.IsLower(p)) return true;
        return char.IsDigit(c) && !char.IsDigit(p);
    }

    public override string ToString() => $"{Kind}:{Name}";
}
