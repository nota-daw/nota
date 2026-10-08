// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-12): what a device, plug-in or preset is, in the closed vocabulary —
// role, slang aliases, character, the part it plays, what it goes on, the job it does, the
// genre. A query is matched against this, not only against the name, so "warm pad" finds
// "Velvet Choir". Stored beside the device / preset metadata (BuiltinDeviceCatalog,
// PresetDescriptors, PluginDescriptors).

namespace Nota.Application.Palette;

/// <summary>A set of vocabulary terms as a 256-bit mask — matching a query against 20 000
/// items is then a few ANDs per item, with nothing allocated.</summary>
public struct TermSet : IEquatable<TermSet>
{
    private ulong _a, _b, _c, _d;

    public readonly bool IsEmpty => (_a | _b | _c | _d) == 0;

    public void Add(int id)
    {
        if ((uint)id >= 256) return;
        ulong bit = 1UL << (id & 63);
        switch (id >> 6) { case 0: _a |= bit; break; case 1: _b |= bit; break; case 2: _c |= bit; break; default: _d |= bit; break; }
    }

    public void Add(SemanticTerm t)
    {
        Add(t.Id);
        if (t.Parent is { } p && SemanticVocabulary.ByKey(p) is { } parent) Add(parent.Id);
    }

    public readonly bool Contains(int id)
    {
        if ((uint)id >= 256) return false;
        ulong bit = 1UL << (id & 63);
        return ((id >> 6) switch { 0 => _a, 1 => _b, 2 => _c, _ => _d } & bit) != 0;
    }

    public void UnionWith(TermSet o) { _a |= o._a; _b |= o._b; _c |= o._c; _d |= o._d; }

    public readonly bool Equals(TermSet o) => _a == o._a && _b == o._b && _c == o._c && _d == o._d;
    public override readonly bool Equals(object? obj) => obj is TermSet o && Equals(o);
    public override readonly int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);
}

/// <summary>The semantic descriptor of a device, plug-in or preset (CP-12). Every list holds
/// vocabulary keys (<see cref="SemanticVocabulary"/>) except <see cref="Aliases"/>, which are
/// free slang the lexical search also reads ("ott", "verb").</summary>
public sealed record SemanticDescriptor
{
    public string Role { get; init; } = "";
    public string[] Aliases { get; init; } = [];
    public string[] Character { get; init; } = [];
    public string[] Sound { get; init; } = [];
    public string[] Source { get; init; } = [];
    public string[] Task { get; init; } = [];
    public string[] Genre { get; init; } = [];
    /// <summary>One or two English sentences — the tooltip, and the text a future embedding
    /// model indexes (CP-16).</summary>
    public string Description { get; init; } = "";

    public static readonly SemanticDescriptor Empty = new();

    /// <summary>Every vocabulary term this descriptor names, parents included. Unknown keys
    /// are skipped (the coverage test reports them).</summary>
    public TermSet Terms()
    {
        var set = new TermSet();
        void AddKey(string k) { if (SemanticVocabulary.ByKey(k) is { } t) set.Add(t); }
        if (Role.Length > 0) AddKey(Role);
        foreach (var k in Character) AddKey(k);
        foreach (var k in Sound) AddKey(k);
        foreach (var k in Source) AddKey(k);
        foreach (var k in Task) AddKey(k);
        foreach (var k in Genre) AddKey(k);
        // An alias that happens to be a vocabulary word counts too ("ott" isn't; "glue" is).
        foreach (var a in Aliases) if (SemanticVocabulary.Exact(a) is { } t) set.Add(t);
        return set;
    }

    /// <summary>Keys this descriptor uses that the vocabulary doesn't have, or that sit in a
    /// facet the term doesn't belong to — for the coverage test.</summary>
    public IEnumerable<string> UnknownKeys()
    {
        IEnumerable<string> Check(IEnumerable<string> keys, Facet f)
        {
            foreach (var k in keys)
                if (SemanticVocabulary.ByKey(k) is not { } t || (t.Facets & f) == 0) yield return $"{f}:{k}";
        }
        var all = Check(Role.Length > 0 ? [Role] : [], Facet.Role)
            .Concat(Check(Character, Facet.Character)).Concat(Check(Sound, Facet.Sound))
            .Concat(Check(Source, Facet.Source)).Concat(Check(Task, Facet.Task)).Concat(Check(Genre, Facet.Genre));
        return all;
    }

    /// <summary>This descriptor with another's fields appended (duplicates dropped) — a preset
    /// inherits its device's descriptor and adds its own.</summary>
    public SemanticDescriptor Merge(SemanticDescriptor o) => new()
    {
        Role = Role.Length > 0 ? Role : o.Role,
        Aliases = Aliases.Union(o.Aliases).ToArray(),
        Character = Character.Union(o.Character).ToArray(),
        Sound = Sound.Union(o.Sound).ToArray(),
        Source = Source.Union(o.Source).ToArray(),
        Task = Task.Union(o.Task).ToArray(),
        Genre = Genre.Union(o.Genre).ToArray(),
        Description = Description.Length > 0 ? Description : o.Description,
    };

    /// <summary>Puts each vocabulary word found in free text (a preset name, a folder) into
    /// the facet it belongs to — how user presets and plug-in presets get a descriptor from
    /// their names (CP-13.3). Role words are not taken from names: "Hall" in a preset name
    /// doesn't make a synth a reverb.</summary>
    public SemanticDescriptor WithTokensFrom(params string[] texts)
    {
        var ch = new List<string>(Character); var so = new List<string>(Sound); var src = new List<string>(Source);
        var ta = new List<string>(Task); var ge = new List<string>(Genre);
        foreach (var text in texts)
            foreach (var tok in Tokenize(text))
            {
                if (SemanticVocabulary.Exact(tok) is not { } t) continue;
                if ((t.Facets & Facet.Character) != 0) AddOnce(ch, t.Key);
                else if ((t.Facets & Facet.Sound) != 0) AddOnce(so, t.Key);
                else if ((t.Facets & Facet.Genre) != 0) AddOnce(ge, t.Key);
                else if ((t.Facets & Facet.Task) != 0) AddOnce(ta, t.Key);
                else if ((t.Facets & Facet.Source) != 0) AddOnce(src, t.Key);
            }
        return this with { Character = ch.ToArray(), Sound = so.ToArray(), Source = src.ToArray(), Task = ta.ToArray(), Genre = ge.ToArray() };

        static void AddOnce(List<string> l, string k) { if (!l.Contains(k)) l.Add(k); }
    }

    internal static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var n = TextNorm.Normalize(text);
        int i = 0;
        while (i < n.Length)
        {
            while (i < n.Length && !char.IsLetterOrDigit(n[i])) i++;
            int s = i;
            while (i < n.Length && (char.IsLetterOrDigit(n[i]) || n[i] == '-')) i++;
            if (i > s) yield return n[s..i].Trim('-');
        }
    }
}
