// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette: one keystroke's search. The query is parsed (CP-7 prefixes, CP-14 facets,
// CP-10 wrong layout), every item is scored lexically (CP-9) and semantically, the scores
// are combined (CP-15) and the results grouped into the rows the palette shows (CP-18/19).
// Runs synchronously on the UI thread against the prepared index; the per-item loop
// allocates nothing (CP-22).

namespace Nota.Application.Palette;

/// <summary>The ranking weights (CP-15). Tuned by the golden tests.</summary>
public sealed record RankingWeights
{
    public float Lexical { get; init; } = 1.0f;
    public float Facets { get; init; } = 0.8f;
    public float Context { get; init; } = 0.3f;
    public float Frecency { get; init; } = 0.3f;
    public float Favorite { get; init; } = 0.2f;
    public static readonly RankingWeights Default = new();
}

/// <summary>A parsed query: the type filter from its prefix, the text, the facets found and
/// the words left for lexical matching.</summary>
public sealed class ParsedQuery
{
    public PaletteKind? Kind { get; init; }
    public string Prefix { get; init; } = "";
    /// <summary>The text after the prefix, as typed.</summary>
    public string Rest { get; init; } = "";
    public bool IsEmpty => Rest.Trim().Length == 0;
    public List<SemanticTerm> Facets { get; } = new();
    /// <summary>All query words joined without spaces — matched against names.</summary>
    public string LexFull { get; set; } = "";
    /// <summary>The same with Cyrillic typed in the wrong layout converted, or "".</summary>
    public string LexFullAlt { get; set; } = "";
    /// <summary>The words that named no facet, joined.</summary>
    public string LexLeft { get; set; } = "";
    public string LexLeftAlt { get; set; } = "";
    public int LeftCount { get; set; }
    /// <summary>The query as read in the other layout ("reverb" for "кумуки"), when that is
    /// what matched — the palette says so ("typed in RU layout").</summary>
    public string LayoutHint { get; set; } = "";
    public bool LayoutFacet { get; set; }

    public static readonly (char Prefix, PaletteKind Kind, string Label)[] Prefixes =
    {
        ('>', PaletteKind.Action, "Actions"), ('@', PaletteKind.Track, "Tracks"), ('+', PaletteKind.Device, "Devices"),
        ('#', PaletteKind.Preset, "Presets"), ('~', PaletteKind.Modular, "Modular"),
    };

    public static ParsedQuery Parse(string raw)
    {
        raw ??= "";
        PaletteKind? kind = null; string prefix = "", rest = raw;
        if (raw.Length > 0)
            foreach (var (p, k, _) in Prefixes)
                if (raw[0] == p) { kind = k; prefix = p.ToString(); rest = raw[1..]; break; }
        var q = new ParsedQuery { Kind = kind, Prefix = prefix, Rest = rest };
        var norm = TextNorm.Normalize(rest.Trim());
        if (norm.Length == 0) return q;

        var raw0 = norm.Split((char[])[' ', '\t', ',', '/'], StringSplitOptions.RemoveEmptyEntries);
        var tokens = raw0.Where(t => !SemanticVocabulary.IsStopWord(t)).ToList();
        if (tokens.Count == 0) tokens = raw0.ToList();   // a query of only "for" still means something
        // Two-word terms typed apart ("tape stop", "hip hop", "de ess").
        for (int i = 0; i + 1 < tokens.Count; i++)
            if (SemanticVocabulary.Exact(tokens[i] + tokens[i + 1]) is not null)
            { tokens[i] += tokens[i + 1]; tokens.RemoveAt(i + 1); }

        var left = new List<string>(); var leftAlt = new List<string>();
        var full = new List<string>(); var fullAlt = new List<string>();
        bool alt = false;
        foreach (var t in tokens)
        {
            var term = SemanticVocabulary.Lookup(t);
            string latin = t;
            if (TextNorm.HasCyrillic(t))
            {
                latin = TextNorm.RuToEnLayout(t);
                if (term is null && SemanticVocabulary.Lookup(latin) is { } viaLayout) { term = viaLayout; q.LayoutFacet = true; }
                alt = true;
            }
            else if (term is null && t.Length >= 3 && t.All(c => c < 0x80))
            {
                // QWERTY keys meant on the Russian layout ("ntgksq" → "теплый"): exact words only.
                var ru = TextNorm.EnToRuLayout(t);
                if (SemanticVocabulary.Exact(ru) is { } viaRu) { term = viaRu; q.LayoutFacet = true; }
            }
            full.Add(t); fullAlt.Add(latin);
            if (term is not null) { if (!q.Facets.Contains(term)) q.Facets.Add(term); }
            else { left.Add(t); leftAlt.Add(latin); }
        }
        q.LexFull = string.Concat(full);
        q.LexFullAlt = alt ? string.Concat(fullAlt) : "";
        q.LexLeft = string.Concat(left);
        q.LexLeftAlt = alt ? string.Concat(leftAlt) : "";
        q.LeftCount = left.Count;
        if (alt) q.LayoutHint = string.Join(' ', fullAlt);
        return q;
    }
}

/// <summary>One result: the item, its score, the matched name positions, and — when it
/// cannot be applied here — why (CP-4, CP-8).</summary>
public readonly record struct PaletteHit(PaletteItem Item, float Score, ulong Mask, float Lexical, float FacetScore, bool Disabled, string Why);

/// <summary>A row of the result list: a section header or an item.</summary>
public readonly record struct PaletteRow(string? Header, PaletteHit Hit)
{
    public bool IsHeader => Header is not null;
}

public sealed class PaletteResults
{
    public ParsedQuery Query { get; init; } = new();
    public List<PaletteRow> Rows { get; } = new();
    /// <summary>The selectable rows, in order.</summary>
    public List<PaletteHit> Items { get; } = new();
    /// <summary>"Showing results for reverb · typed in RU layout", or "".</summary>
    public string LayoutHint { get; set; } = "";

    /// <summary>Why a semantic hit was shown ("warm · pad"): the query facets it carries, when
    /// its name alone wouldn't have found it. "" for a name match.</summary>
    public string ReasonFor(in PaletteHit hit)
    {
        if (Query.Facets.Count == 0 || hit.FacetScore <= 0 || hit.Lexical >= 0.75f) return "";
        var parts = new List<string>(Query.Facets.Count);
        foreach (var f in Query.Facets) if (hit.Item.Terms.Contains(f.Id)) parts.Add(f.Label);
        return string.Join(" · ", parts);
    }
}

public sealed class PaletteSearch
{
    private const float Threshold = 0.45f, NameGood = 0.75f;
    private const int PerGroup = 5, FlatMax = 60;

    private readonly PaletteIndex _index;
    private readonly PaletteUsage? _usage;
    private RankingWeights _w;
    private Candidate[] _buf = new Candidate[256];
    private readonly CandidateComparer _cmp = new();

    private struct Candidate
    {
        public PaletteItem Item; public float Score, Lex, Facet; public ulong Mask;
        public bool Disabled; public string Why;
    }

    private sealed class CandidateComparer : IComparer<Candidate>
    {
        public int Compare(Candidate a, Candidate b)
        {
            int c = b.Score.CompareTo(a.Score);
            return c != 0 ? c : string.CompareOrdinal(a.Item.Name, b.Item.Name);
        }
    }

    public PaletteSearch(PaletteIndex index, PaletteUsage? usage = null, RankingWeights? weights = null)
    {
        _index = index;
        _usage = usage;
        _w = weights ?? RankingWeights.Default;
    }

    public RankingWeights Weights { get => _w; set => _w = value; }

    /// <param name="availability">Whether an item can be applied in this context (CP-4/CP-8).</param>
    /// <param name="suggested">Ids suggested for the context when the query is empty (CP-19).</param>
    /// <param name="flat">One ranked list without kind groups (tests, MCP).</param>
    public PaletteResults Run(string raw, PaletteContext ctx, Func<PaletteItem, Availability>? availability = null,
        IReadOnlyList<string>? suggested = null, bool flat = false)
    {
        var q = ParsedQuery.Parse(raw);
        var res = new PaletteResults { Query = q };
        bool modularOk = ctx.Origin == PaletteOrigin.Modular;
        if (q.IsEmpty) { EmptyQuery(res, q, ctx, availability, suggested, modularOk); return res; }

        var items = _index.Items;
        if (_buf.Length < items.Count) _buf = new Candidate[Math.Max(items.Count, _buf.Length * 2)];
        int n = 0;
        bool layoutWon = false;
        int facetCount = q.Facets.Count;

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (q.Kind is { } k && it.Kind != k) continue;
            if (it.Kind == PaletteKind.Modular && !modularOk) continue;

            float l1 = Lex(it, q.LexFull, out ulong m1);
            if (q.LexFullAlt.Length > 0)
            {
                float l1b = Lex(it, q.LexFullAlt, out ulong m1b);
                if (l1b > l1) { l1 = l1b; m1 = m1b; if (facetCount == 0 && l1b >= Threshold) layoutWon = true; }
            }
            float lex; ulong mask; float facet = 0, specific = 0;
            if (facetCount == 0) { lex = l1; mask = m1; }
            else
            {
                int hits = 0, own = 0;
                foreach (var f in q.Facets)
                {
                    if (it.Terms.Contains(f.Id)) hits++;
                    if (it.OwnTerms.Contains(f.Id)) own++;
                }
                facet = hits / (float)facetCount;
                specific = own / (float)facetCount;
                float l2 = 0; ulong m2 = 0;
                if (q.LexLeft.Length > 0)
                {
                    l2 = Lex(it, q.LexLeft, out m2);
                    if (q.LexLeftAlt.Length > 0) { float l2b = Lex(it, q.LexLeftAlt, out ulong m2b); if (l2b > l2) { l2 = l2b; m2 = m2b; } }
                }
                if (l1 >= NameGood) { lex = l1; mask = m1; } else { lex = l2; mask = m2; }
                if (q.LeftCount > 0 && l2 <= 0) facet *= 0.5f;
            }
            if (lex < Threshold && facet <= 0) continue;
            if (lex < Threshold) { lex = 0; mask = 0; }   // a faint name match doesn't light letters

            bool disabled = false; string why = "";
            if (availability is not null)
            {
                var a = availability(it);
                if (!a.Ok)
                {
                    // An action that can't run here stays hidden unless asked for by name (CP-8);
                    // a device that can't go here shows, inactive, with the reason (CP-4).
                    if (it.Kind == PaletteKind.Action && !NamedExactly(it, q)) continue;
                    disabled = true; why = a.Reason;
                }
            }

            float ctxScore = ContextScore(it, ctx);
            float frec = _usage?.Frecency(it.Id) ?? 0;
            float score = _w.Lexical * lex + _w.Facets * facet + _w.Context * ctxScore + _w.Frecency * frec
                        + (it.IsFavorite ? _w.Favorite : 0)
                        + 0.15f * specific                                  // the preset names the term itself
                        + (it.Kind == PaletteKind.Device ? 0.1f : 0);       // a device before its presets on a tie
            if (disabled) score -= 0.5f;   // inactive rows sink below the ones that apply
            ref var c = ref _buf[n++];
            c.Item = it; c.Score = score; c.Lex = lex; c.Facet = facet; c.Mask = mask; c.Disabled = disabled; c.Why = why;
        }
        Array.Sort(_buf, 0, n, _cmp);

        if (layoutWon || q.LayoutFacet) res.LayoutHint = q.LayoutHint;
        if (q.Kind is not null || flat)
        {
            for (int i = 0; i < n && i < FlatMax; i++) AddItem(res, Hit(_buf[i]));
        }
        else
        {
            // Groups by kind, each its best five, the group with the best item first; Modular
            // nodes lead when they are offered at all (CP-6a.5).
            var order = new List<(PaletteKind Kind, float Top)>(5);
            Span<bool> seenKind = stackalloc bool[5];
            for (int i = 0; i < n; i++)
            {
                var k = _buf[i].Item.Kind;
                if (seenKind[(int)k]) continue;
                seenKind[(int)k] = true;
                order.Add((k, _buf[i].Score));   // sorted already: the first of a kind is its best
            }
            order.Sort((a, b) => a.Kind == PaletteKind.Modular ? -1 : b.Kind == PaletteKind.Modular ? 1 : b.Top.CompareTo(a.Top));
            foreach (var (kind, _) in order)
            {
                res.Rows.Add(new PaletteRow(GroupLabel(kind), default));
                int shown = 0;
                for (int i = 0; i < n && shown < PerGroup; i++)
                    if (_buf[i].Item.Kind == kind) { AddItem(res, Hit(_buf[i])); shown++; }
            }
        }
        for (int i = 0; i < n; i++) { _buf[i].Item = null!; _buf[i].Why = null!; }   // don't pin old items
        return res;
    }

    private static PaletteHit Hit(in Candidate c) => new(c.Item, c.Score, c.Mask, c.Lex, c.Facet, c.Disabled, c.Why ?? "");

    private static void AddItem(PaletteResults r, PaletteHit h) { r.Rows.Add(new PaletteRow(null, h)); r.Items.Add(h); }

    public static string GroupLabel(PaletteKind k) => k switch
    {
        PaletteKind.Action => "ACTIONS", PaletteKind.Track => "TRACKS", PaletteKind.Device => "DEVICES",
        PaletteKind.Preset => "PRESETS", _ => "MODULAR",
    };

    // CP-8: an unavailable action is shown only when the query names it.
    private static bool NamedExactly(PaletteItem it, ParsedQuery q)
        => q.LexFull.Length >= 3 && it.NameNorm.Replace(" ", "").StartsWith(q.LexFull, StringComparison.Ordinal);

    private static float ContextScore(PaletteItem it, PaletteContext ctx)
    {
        if (ctx.Origin is PaletteOrigin.Devices or PaletteOrigin.Modular or PaletteOrigin.DeviceWindow
            && it.Kind is PaletteKind.Device or PaletteKind.Preset or PaletteKind.Modular) return 1;
        if (it.Kind == PaletteKind.Action)
        {
            if (it.View.Length > 0 && it.View == ctx.View) return 1;
            if (ctx.Origin == PaletteOrigin.PianoRoll && it.Id.StartsWith("a:roll.", StringComparison.Ordinal)) return 1;
            if (ctx.Origin == PaletteOrigin.Session && it.View == "Session") return 1;
        }
        return 0;
    }

    /// <summary>Lexical score of one item for one query string (CP-9): the name, scored on its
    /// distinctive word (the "Nota " prefix ignored), then aliases, device / vendor, folder,
    /// tags — each weaker than the one before. One typo per word of 4+ letters is forgiven.</summary>
    public static float Lex(PaletteItem it, string q, out ulong mask)
    {
        mask = 0;
        if (q.Length == 0) return 0;
        ReadOnlySpan<char> qs = q;
        float best = FuzzyMatcher.Score(qs, it.NameNorm, it.CoreOffset, it.WordStarts, it.CoreLetters, out ulong m);
        if (best <= 0 && it.CoreOffset > 0)
        {
            // The prefix's initial may lead an acronym ("nv" → Nota Vintage); coverage still
            // counts only the distinctive word.
            best = 0.9f * FuzzyMatcher.Score(qs, it.NameNorm, 0, it.WordStarts, it.CoreLetters, out m);
        }
        if (best > 0)
        {
            if (it.CoreCompact == q) best += 1.5f;
            else if (it.CoreCompact.StartsWith(q, StringComparison.Ordinal)) best += 0.3f;
            mask = m;
        }
        if (best < 0.85f)
            foreach (var a in it.AliasNorm)
                if (a == q || (q.Length >= 3 && a.StartsWith(q, StringComparison.Ordinal))) { best = 0.85f; mask = 0; break; }
        best = Field(best, ref mask, qs, it.DeviceNorm, it.DeviceStarts, it.DeviceLetters, 0.5f);
        best = Field(best, ref mask, qs, it.FolderNorm, it.FolderStarts, it.FolderLetters, 0.45f);
        best = Field(best, ref mask, qs, it.TagsNorm, it.TagsStarts, it.TagsLetters, 0.4f);
        if (best < Threshold && q.Length >= 4)
            foreach (var (start, len) in it.Words)
                if (len >= 3 && start + len <= it.NameNorm.Length && TextNorm.WithinOneEdit(qs, it.NameNorm.AsSpan(start, len)))
                {
                    best = 0.55f;
                    mask = 0;
                    for (int i = start; i < start + len && i < 64; i++) mask |= 1UL << i;
                    break;
                }
        return best;
    }

    private static float Field(float best, ref ulong mask, ReadOnlySpan<char> q, string text, ulong starts, int letters, float weight)
    {
        if (text.Length == 0) return best;
        float s = weight * FuzzyMatcher.Score(q, text, 0, starts, letters, out _);
        if (s > best) { mask = 0; return s; }
        return best;
    }

    // CP-19: an empty query shows Recent and what suits the context; with a type filter, that
    // type's items (the ones that apply here first).
    private void EmptyQuery(PaletteResults res, ParsedQuery q, PaletteContext ctx, Func<PaletteItem, Availability>? availability,
        IReadOnlyList<string>? suggested, bool modularOk)
    {
        bool Ok(PaletteItem? it) => it is not null && (q.Kind is null || it.Kind == q.Kind)
                                    && (it.Kind != PaletteKind.Modular || modularOk);
        PaletteHit Make(PaletteItem it)
        {
            var a = availability?.Invoke(it) ?? Availability.Yes;
            return new PaletteHit(it, 0, 0, 0, 0, !a.Ok, a.Reason);
        }
        if (q.Kind is null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var recent = (_usage?.Recent ?? []).Select(_index.Find).Where(Ok).Select(it => it!).ToList();
            if (recent.Count > 0)
            {
                res.Rows.Add(new PaletteRow("RECENT", default));
                foreach (var it in recent) { AddItem(res, Make(it)); seen.Add(it.Id); }
            }
            var sug = (suggested ?? []).Select(_index.Find).Where(it => Ok(it) && !seen.Contains(it!.Id)).Select(it => it!)
                .Where(it => it.Kind != PaletteKind.Action || availability is null || availability(it).Ok).ToList();
            if (sug.Count > 0)
            {
                res.Rows.Add(new PaletteRow("SUGGESTED · " + OriginLabel(ctx.Origin).ToUpperInvariant(), default));
                foreach (var it in sug) AddItem(res, Make(it));
            }
            return;
        }
        var list = _index.Items.Where(Ok)
            .Select(it => (it, a: availability?.Invoke(it) ?? Availability.Yes))
            .Where(x => x.a.Ok || x.it.Kind != PaletteKind.Action)
            .OrderBy(x => x.a.Ok ? 0 : 1)
            .ThenByDescending(x => _usage?.Frecency(x.it.Id) ?? 0)
            .Take(FlatMax);
        foreach (var (it, a) in list) AddItem(res, new PaletteHit(it, 0, 0, 0, 0, !a.Ok, a.Reason));
    }

    public static string OriginLabel(PaletteOrigin o) => o switch
    {
        PaletteOrigin.PianoRoll => "Piano roll",
        PaletteOrigin.DeviceWindow => "Device window",
        _ => o.ToString(),
    };
}
