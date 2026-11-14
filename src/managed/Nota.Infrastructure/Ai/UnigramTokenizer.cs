// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SentencePiece Unigram tokenization as XLM-RoBERTa (and so multilingual-e5) does it, read from
// the model's Hugging Face tokenizer.json (specs/098, research R5): NFKC in place of the
// precompiled character map (they differ only on rare characters), runs of spaces folded, each
// word prefixed with "▁" (Metaspace), the most likely segmentation by Viterbi over the vocabulary's
// log-probabilities, unknown characters fused into one <unk>, and <s> … </s> around the whole.
// The smoke test checks the ids against the reference library.

using System.Text;
using System.Text.Json;

namespace Nota.Infrastructure;

internal sealed class UnigramTokenizer
{
    private const char Meta = '▁';

    private readonly Dictionary<string, (int Id, float Score)> _pieces;
    private readonly Dictionary<string, (int Id, float Score)>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly int _maxPiece;
    private readonly float _unkScore;

    public int Bos { get; }
    public int Eos { get; }
    public int Unk { get; }
    public int Pad { get; }

    private UnigramTokenizer(Dictionary<string, (int, float)> pieces, int bos, int eos, int unk, int pad)
    {
        _pieces = pieces;
        _lookup = pieces.GetAlternateLookup<ReadOnlySpan<char>>();
        _maxPiece = pieces.Keys.Max(k => k.Length);
        _unkScore = pieces.Values.Min(v => v.Item2) - 10f;
        Bos = bos; Eos = eos; Unk = unk; Pad = pad;
    }

    /// <summary>Reads a Hugging Face tokenizer.json with a Unigram model.</summary>
    public static UnigramTokenizer Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var model = doc.RootElement.GetProperty("model");
        if (model.GetProperty("type").GetString() != "Unigram")
            throw new InvalidDataException("tokenizer.json: not a Unigram model");
        var pieces = new Dictionary<string, (int, float)>(StringComparer.Ordinal);
        int id = 0;
        foreach (var entry in model.GetProperty("vocab").EnumerateArray())
        {
            string piece = entry[0].GetString() ?? "";
            pieces.TryAdd(piece, (id, (float)entry[1].GetDouble()));
            id++;
        }
        int Special(string s, int fallback) => pieces.TryGetValue(s, out var v) ? v.Item1 : fallback;
        int unk = model.TryGetProperty("unk_id", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt32() : Special("<unk>", 3);
        int bos = Special("<s>", 0), eos = Special("</s>", 2), pad = Special("<pad>", 1);
        // Specials are matched only as specials, never inside text.
        foreach (var s in new[] { "<s>", "</s>", "<pad>", "<unk>", "<mask>" }) pieces.Remove(s);
        return new UnigramTokenizer(pieces, bos, eos, unk, pad);
    }

    /// <summary>Token ids for <paramref name="text"/>, with &lt;s&gt; and &lt;/s&gt;, at most
    /// <paramref name="maxTokens"/> long (the text is cut, the end token kept).</summary>
    public int[] Encode(string text, int maxTokens = 512)
    {
        var ids = new List<int>(64) { Bos };
        string norm = Normalize(text);
        foreach (var word in norm.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            Segment(Meta + word, ids);
            if (ids.Count >= maxTokens - 1) break;
        }
        // A trailing space survives normalisation as a lone "▁", as in the reference library.
        if (norm.EndsWith(' ') && ids.Count < maxTokens - 1 && _lookup.TryGetValue(Meta.ToString(), out var lone)) ids.Add(lone.Id);
        if (ids.Count > maxTokens - 1) ids.RemoveRange(maxTokens - 1, ids.Count - (maxTokens - 1));
        ids.Add(Eos);
        return ids.ToArray();
    }

    private static string Normalize(string text)
    {
        var s = text.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        if (space && sb.Length > 0) sb.Append(' ');
        return sb.ToString();
    }

    // Viterbi: best[i] = the most likely segmentation of word[..i].
    private void Segment(string word, List<int> ids)
    {
        int n = word.Length;
        var best = new float[n + 1];
        var from = new int[n + 1];
        var piece = new int[n + 1];
        Array.Fill(best, float.NegativeInfinity);
        best[0] = 0;
        var span = word.AsSpan();
        for (int i = 0; i < n; i++)
        {
            if (float.IsNegativeInfinity(best[i])) continue;
            bool any = false;
            int maxLen = Math.Min(_maxPiece, n - i);
            for (int len = 1; len <= maxLen; len++)
            {
                if (!_lookup.TryGetValue(span.Slice(i, len), out var p)) continue;
                any |= len == 1;
                float s = best[i] + p.Score;
                if (s > best[i + len]) { best[i + len] = s; from[i + len] = i; piece[i + len] = p.Id; }
            }
            // A character no piece starts with: one unknown token for it (surrogate pairs whole).
            if (!any)
            {
                int len = char.IsHighSurrogate(word[i]) && i + 1 < n ? 2 : 1;
                float s = best[i] + _unkScore;
                if (s > best[i + len]) { best[i + len] = s; from[i + len] = i; piece[i + len] = Unk; }
            }
        }
        var rev = new List<int>();
        for (int at = n; at > 0; at = from[at]) rev.Add(piece[at]);
        rev.Reverse();
        // Unknowns side by side are one token (fuse_unk).
        int last = -1;
        foreach (var id in rev)
        {
            if (id == Unk && last == Unk) continue;
            ids.Add(id);
            last = id;
        }
    }
}
