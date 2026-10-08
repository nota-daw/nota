// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-9): VS Code-style fuzzy matching. The query must appear in the text as
// a subsequence; each matched character scores, more at the start of a word or camelCase
// hump and when it follows the previous match. "afl" → Auto FiLter, "nv" → Nota Vintage.
// Matched positions come back as a 64-bit mask, so highlighting allocates nothing.

namespace Nota.Application.Palette;

public static class FuzzyMatcher
{
    /// <summary>Scores <paramref name="q"/> (normalised, no spaces) against
    /// <paramref name="text"/> (normalised) from <paramref name="from"/>. <paramref name="starts"/>
    /// marks word starts by position. Returns 0..1 (0 = no match) and the matched positions.</summary>
    public static float Score(ReadOnlySpan<char> q, string text, int from, ulong starts, int letters, out ulong mask)
    {
        mask = 0;
        if (q.Length == 0 || text.Length - from < q.Length) return 0;
        int pos = from, prev = -2;
        float score = 0;
        foreach (char c in q)
        {
            int f = -1;
            // Keep a run going when the next text character continues it…
            if (prev >= 0 && pos < text.Length && text[pos] == c && prev == pos - 1) f = pos;
            else
            {
                // …else prefer the next word start carrying the character, else its next occurrence.
                for (int k = pos; k < text.Length; k++)
                    if (text[k] == c && IsStart(starts, k)) { f = k; break; }
                if (f < 0) f = text.IndexOf(c, pos);
            }
            if (f < 0) { mask = 0; return 0; }
            score += 1 + (IsStart(starts, f) ? 2 : 0) + (f == prev + 1 ? 1.5f : 0);
            if (f < 64) mask |= 1UL << f;
            prev = f;
            pos = f + 1;
        }
        float coverage = Math.Min(1f, q.Length / (float)Math.Max(1, letters));
        return Math.Min(1f, score / (q.Length * 3f)) * (0.85f + 0.15f * coverage);
    }

    private static bool IsStart(ulong starts, int k) => k < 64 && (starts & (1UL << k)) != 0;

    /// <summary>Non-space characters from <paramref name="from"/> — the coverage denominator.</summary>
    public static int Letters(string text, int from = 0)
    {
        int n = 0;
        for (int i = from; i < text.Length; i++) if (text[i] != ' ') n++;
        return n;
    }

    /// <summary>Word starts of a plain (already normalised) string, for fields without a precomputed mask.</summary>
    public static ulong StartsOf(string text)
    {
        ulong m = 0;
        for (int i = 0; i < text.Length && i < 64; i++)
            if (PaletteItem.IsWordStart(text, i)) m |= 1UL << i;
        return m;
    }
}
