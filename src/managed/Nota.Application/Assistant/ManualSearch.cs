// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Finds the manual's sections for a question (specs/098, research R3/R4). Words-only search
// always works: BM25 over the page title (×3), the heading (×2) and the text, with the
// palette's normalisation (ё → е, accents, Russian stems), a light English stemmer, one-typo
// tolerance, the wrong keyboard layout and the palette vocabulary's EN ↔ RU synonyms. When the
// Manual search model is installed, its cosine ranking is fused in by reciprocal rank, so an
// exact control name still wins while a question in other words finds the section too.
// Answers come from one language: the question's (Cyrillic → Russian), else the interface
// language when the manual has it and the script matches, else English (FR-010).

using Nota.Application.Palette;

namespace Nota.Application.Assistant;

/// <summary>Section vectors from the meaning model, in index order (row i = Sections[i]).</summary>
public sealed record SectionVectorSet(int Count, int Dim, float[] Data)
{
    public ReadOnlySpan<float> Row(int i) => Data.AsSpan(i * Dim, Dim);
}

/// <summary>A section found for a question. <see cref="Coverage"/> is the share of the
/// question's words the section has (0–1); <see cref="Cosine"/> the meaning similarity, or
/// null without the model.</summary>
public sealed record SearchHit(ManualSection Section, int Ordinal, double Score, double Coverage, double? Cosine);

public sealed record SearchResult(string Lang, IReadOnlyList<SearchHit> Hits);

public sealed class ManualSearch
{
    private const double K1 = 1.2, B = 0.75;
    private const int TitleWeight = 3, HeadingWeight = 2, BodyWeight = 1;
    private const double TypoWeight = 0.7, SynonymWeight = 0.5, FormWeight = 0.9;
    private const int Rrf = 60;
    private const double HeadBonus = 1.5;

    /// <summary>A hit is an answer when it has at least this share of the question's words…</summary>
    public const double MinCoverage = 0.5;
    /// <summary>…or the meaning model rates it at least this similar (e5 cosine).</summary>
    public const double MinCosine = 0.83;

    private static readonly HashSet<string> QuestionWords = new(StringComparer.Ordinal)
    {
        "how", "what", "where", "why", "when", "which", "do", "does", "can", "could", "i", "is", "are",
        "it", "this", "that", "me", "you", "your", "there", "way", "make", "get", "use", "want", "need", "nota",
        "как", "что", "где", "почему", "когда", "какой", "какая", "какие", "можно", "ли", "я", "это", "мне",
        "сделать", "нужно", "надо", "хочу", "есть", "чтобы", "чем", "нота", "nota",
    };

    private readonly ManualIndex _index;
    private readonly Dictionary<string, LangIndex> _langs = new(StringComparer.Ordinal);

    public ManualIndex Index => _index;

    public ManualSearch(ManualIndex index)
    {
        _index = index;
        for (int i = 0; i < index.Sections.Count; i++)
        {
            var s = index.Sections[i];
            if (!_langs.TryGetValue(s.Lang, out var li)) _langs[s.Lang] = li = new LangIndex();
            li.Add(i, s);
        }
        foreach (var li in _langs.Values) li.Seal();
    }

    /// <summary>The language the answer comes from (FR-010).</summary>
    public string AnswerLanguage(string question, string uiLanguage)
    {
        bool cyrillic = TextNorm.HasCyrillic(question);
        if (cyrillic && _langs.ContainsKey("ru")) return "ru";
        string ui = (uiLanguage ?? "").Split('-', '_')[0].ToLowerInvariant();
        bool uiCyrillic = ui is "ru" or "uk" or "be" or "bg" or "sr";
        if (ui.Length > 0 && ui != "en" && _langs.ContainsKey(ui) && cyrillic == uiCyrillic) return ui;
        return _langs.ContainsKey("en") ? "en" : _langs.Keys.FirstOrDefault() ?? "en";
    }

    /// <summary>The best sections for <paramref name="question"/>, best first.</summary>
    public SearchResult Search(string question, string uiLanguage, int take = 10,
                               float[]? queryVector = null, SectionVectorSet? vectors = null)
    {
        string lang = AnswerLanguage(question, uiLanguage);
        if (!_langs.TryGetValue(lang, out var li)) return new SearchResult(lang, []);

        var words = li.Score(QueryTerms(question, li));
        // Nothing for the words as typed: maybe the keyboard layout was wrong ("ысфкп").
        if (words.Count == 0)
        {
            string flipped = TextNorm.HasCyrillic(question) ? TextNorm.RuToEnLayout(TextNorm.Normalize(question))
                                                            : TextNorm.EnToRuLayout(TextNorm.Normalize(question));
            string lang2 = AnswerLanguage(flipped, uiLanguage);
            if (_langs.TryGetValue(lang2, out var li2))
            {
                var w2 = li2.Score(QueryTerms(flipped, li2));
                if (w2.Count > 0) { lang = lang2; li = li2; words = w2; }
            }
        }

        // Meaning ranking over the same language's sections.
        Dictionary<int, double>? cos = null;
        if (queryVector is not null && vectors is not null && vectors.Count == _index.Sections.Count)
        {
            cos = new Dictionary<int, double>();
            foreach (int ord in li.Ordinals) cos[ord] = Dot(queryVector, vectors.Row(ord));
        }

        var fused = new Dictionary<int, double>();
        int r = 0;
        // A section with more of the question's words ranks above one that repeats a single word.
        foreach (var (ord, _, _) in words.OrderByDescending(w => w.Score * (0.5 + w.Coverage))) fused[ord] = 1.0 / (Rrf + ++r);
        if (cos is not null)
        {
            r = 0;
            foreach (var (ord, c) in cos.OrderByDescending(kv => kv.Value).Take(50).Select(kv => (kv.Key, kv.Value)))
            {
                ++r;
                if (c < MinCosine - 0.08) break;   // far below an answer: don't let it in on rank alone
                fused[ord] = fused.GetValueOrDefault(ord) + 1.0 / (Rrf + r);
            }
        }

        var coverage = words.ToDictionary(w => w.Ordinal, w => w.Coverage);
        var hits = fused.OrderByDescending(kv => kv.Value).Take(take).Select(kv => new SearchHit(
            _index.Sections[kv.Key], kv.Key, kv.Value, coverage.GetValueOrDefault(kv.Key),
            cos is not null ? cos.GetValueOrDefault(kv.Key) : null)).ToList();
        return new SearchResult(lang, hits);
    }

    /// <summary>Whether a hit is good enough to quote (FR-011).</summary>
    public static bool IsAnswer(SearchHit hit) =>
        hit.Coverage >= MinCoverage || hit.Cosine is { } c && c >= MinCosine;

    private static double Dot(float[] a, ReadOnlySpan<float> b)
    {
        double s = 0;
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) s += a[i] * b[i];
        return s;
    }

    // ---- terms ---------------------------------------------------------------------------

    /// <summary>The searchable terms of a text: normalised, stop words out, stemmed.</summary>
    public static IEnumerable<string> Terms(string text)
    {
        foreach (var raw in Words(text))
        {
            string w = TextNorm.Normalize(raw);
            if (w.Length < 2 && !char.IsDigit(w[0])) continue;
            if (SemanticVocabulary.IsStopWord(w)) continue;
            yield return Stem(w);
        }
    }

    private static string Stem(string w) => TextNorm.HasCyrillic(w) ? TextNorm.RuStem(w) : EnglishStem.Of(w);

    private static IEnumerable<string> Words(string text)
    {
        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool letter = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '-' && start >= 0);
            if (letter) { if (start < 0) start = i; continue; }
            if (start >= 0)
            {
                var w = text[start..i].Trim('-');
                if (w.Length > 0)
                {
                    yield return w;
                    if (w.Contains('-')) foreach (var part in w.Split('-', StringSplitOptions.RemoveEmptyEntries)) yield return part;
                }
                start = -1;
            }
        }
    }

    // A question's weighted terms: its own words (question words dropped), each typo'd word's
    // one-edit neighbours in this language's vocabulary, and vocabulary synonyms.
    private static Dictionary<string, double> QueryTerms(string question, LangIndex li)
    {
        var terms = new Dictionary<string, double>(StringComparer.Ordinal);
        void Put(string t, double w) { if (w > terms.GetValueOrDefault(t)) terms[t] = w; }

        foreach (var raw in Words(question))
        {
            string w = TextNorm.Normalize(raw);
            if (QuestionWords.Contains(w) || SemanticVocabulary.IsStopWord(w)) continue;
            if (w.Length < 2 && !char.IsDigit(w[0])) continue;
            string t = Stem(w);
            Put(t, 1);
            // Other forms of the word the crude stems didn't fold: "записать" ~ "запис", "дублей" ~
            // "дубл" — vocabulary terms sharing a long common start.
            if (t.Length >= 4)
                foreach (var form in li.SameStart(t)) Put(form, FormWeight);
            if (!li.Has(t) && t.Length >= 4)
                foreach (var near in li.WithinOneEdit(t)) Put(near, TypoWeight);
            if (SemanticVocabulary.Lookup(w) is { } term)
                foreach (var syn in term.Words)
                    foreach (var st in Terms(syn))
                        if (st != t) Put(st, SynonymWeight);
        }
        return terms;
    }

    // ---- one language's BM25 tables ------------------------------------------------------

    private sealed class LangIndex
    {
        private readonly List<int> _ordinals = new();
        private readonly List<double> _lengths = new();
        private readonly List<HashSet<string>> _heads = new();   // title + heading terms per section
        private readonly Dictionary<string, List<(int Doc, double Tf)>> _postings = new(StringComparer.Ordinal);
        private string[] _vocab = [];
        private double _avg;

        public IReadOnlyList<int> Ordinals => _ordinals;

        public void Add(int ordinal, ManualSection s)
        {
            int doc = _ordinals.Count;
            _ordinals.Add(ordinal);
            var tf = new Dictionary<string, double>(StringComparer.Ordinal);
            double len = 0;
            void Count(string text, int weight)
            {
                foreach (var t in Terms(text)) { tf[t] = tf.GetValueOrDefault(t) + weight; len += weight; }
            }
            Count(s.PageTitle, TitleWeight);
            Count(s.Heading, HeadingWeight);
            _heads.Add(new HashSet<string>(Terms(s.PageTitle).Concat(Terms(s.Heading)), StringComparer.Ordinal));
            Count(s.Markdown, BodyWeight);
            _lengths.Add(len);
            foreach (var (t, n) in tf)
            {
                if (!_postings.TryGetValue(t, out var list)) _postings[t] = list = new();
                list.Add((doc, n));
            }
        }

        public void Seal()
        {
            _avg = _lengths.Count > 0 ? Math.Max(1, _lengths.Average()) : 1;
            _vocab = _postings.Keys.Where(k => k.Length >= 3).ToArray();
        }

        public bool Has(string term) => _postings.ContainsKey(term);

        // The question word a matched form stands for (its full-weight term with the same start).
        private static string Root(string term, Dictionary<string, double> query)
        {
            foreach (var (q, w) in query)
                if (w >= 1 && (q == term || q.Length >= 4 && term.StartsWith(q[..Math.Max(4, q.Length - 2)], StringComparison.Ordinal)))
                    return q;
            return term;
        }

        /// <summary>Terms that share a long start with <paramref name="term"/>: word forms the
        /// stemmers left apart.</summary>
        public IEnumerable<string> SameStart(string term)
        {
            // Russian endings run two letters and more; English ones are folded by EnglishStem.
            int need = TextNorm.HasCyrillic(term) ? Math.Max(4, term.Length - 2) : Math.Max(5, term.Length - 1);
            if (need > term.Length) yield break;
            string head = term[..need];
            foreach (var v in _vocab)
                if (v.Length >= need && v.Length <= term.Length + 3 && v.StartsWith(head, StringComparison.Ordinal) && v != term)
                    yield return v;
        }

        public IEnumerable<string> WithinOneEdit(string term)
        {
            foreach (var v in _vocab)
                if (Math.Abs(v.Length - term.Length) <= 1 && TextNorm.WithinOneEdit(v, term)) yield return v;
        }

        /// <summary>Sections with any of the terms: (ordinal, BM25 score, coverage).</summary>
        public List<(int Ordinal, double Score, double Coverage)> Score(Dictionary<string, double> query)
        {
            var result = new List<(int, double, double)>();
            if (query.Count == 0) return result;
            int n = _ordinals.Count;
            var score = new Dictionary<int, double>();
            var covered = new Dictionary<int, HashSet<string>>();
            // Coverage counts a question word once, through itself or its typo/synonym stand-in.
            double full = query.Count(kv => kv.Value >= 1);
            if (full == 0) full = 1;
            foreach (var (term, weight) in query)
            {
                if (!_postings.TryGetValue(term, out var list)) continue;
                double idf = Math.Log(1 + (n - list.Count + 0.5) / (list.Count + 0.5));
                foreach (var (doc, tf) in list)
                {
                    double norm = tf * (K1 + 1) / (tf + K1 * (1 - B + B * _lengths[doc] / _avg));
                    // A word the section is titled by counts again: "sidechain" belongs to the
                    // Sidechain heading more than to a preset list that names it ten times.
                    double head = _heads[doc].Contains(term) ? HeadBonus : 0;
                    score[doc] = score.GetValueOrDefault(doc) + weight * idf * (norm + head);
                    if (!covered.TryGetValue(doc, out var set)) covered[doc] = set = new(StringComparer.Ordinal);
                    set.Add(weight >= FormWeight ? Root(term, query) : "~" + term);
                }
            }
            foreach (var (doc, s) in score)
            {
                // A word found in several forms still counts once.
                double c = covered[doc].Count(t => t[0] != '~') + 0.6 * covered[doc].Count(t => t[0] == '~');
                result.Add((_ordinals[doc], s, Math.Min(1, c / full)));
            }
            return result;
        }
    }
}
