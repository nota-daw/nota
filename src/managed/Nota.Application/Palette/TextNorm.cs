// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-10): text normalisation shared by the index and the query parser —
// case- and diacritic-insensitive, ё folded to е — plus the RU ↔ EN keyboard-layout map, so
// "кумуки" typed with the Russian layout still finds "reverb", and the bounded
// Damerau–Levenshtein test behind "one typo per word".

using System.Globalization;
using System.Text;

namespace Nota.Application.Palette;

public static class TextNorm
{
    // The same physical keys on the ЙЦУКЕН and QWERTY layouts.
    private const string Ru = "йцукенгшщзхъфывапролджэячсмитьбюё";
    private const string En = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";

    /// <summary>Lower case, ё → е, Latin diacritics dropped (é → e). Cyrillic keeps its
    /// letters — й is not и with a mark.</summary>
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        bool plain = true;
        foreach (char c in s)
            if (c > 0x7F || char.IsUpper(c)) { plain = false; break; }
        if (plain) return s;

        var sb = new StringBuilder(s.Length);
        foreach (char c0 in s)
        {
            char c = char.ToLowerInvariant(c0);
            if (c == 'ё') { sb.Append('е'); continue; }
            if (c < 0x80 || c is >= 'а' and <= 'я') { sb.Append(c); continue; }
            if (c < 0x250)   // Latin-1 / Latin Extended: strip the accent
            {
                var d = c.ToString().Normalize(NormalizationForm.FormD);
                foreach (char p in d)
                    if (CharUnicodeInfo.GetUnicodeCategory(p) != UnicodeCategory.NonSpacingMark) sb.Append(p);
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static bool IsCyrillic(char c) => c is >= 'а' and <= 'я' or 'ё' or >= 'А' and <= 'Я';

    public static bool HasCyrillic(string s)
    {
        foreach (char c in s) if (IsCyrillic(c)) return true;
        return false;
    }

    /// <summary>What a Cyrillic string would have been with the QWERTY layout on ("кумуки" →
    /// "reverb"). Characters off the letter keys pass through.</summary>
    public static string RuToEnLayout(string s)
    {
        var a = s.ToCharArray();
        for (int i = 0; i < a.Length; i++)
        {
            int k = Ru.IndexOf(char.ToLowerInvariant(a[i]));
            if (k >= 0) a[i] = En[k];
        }
        return new string(a);
    }

    /// <summary>The reverse: Latin keys read on the ЙЦУКЕН layout ("ntgksq" → "теплый").</summary>
    public static string EnToRuLayout(string s)
    {
        var a = s.ToCharArray();
        for (int i = 0; i < a.Length; i++)
        {
            int k = En.IndexOf(char.ToLowerInvariant(a[i]));
            if (k >= 0) a[i] = Ru[k] == 'ё' ? 'е' : Ru[k];
        }
        return new string(a);
    }

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are at most one edit
    /// apart — an insertion, a deletion, a substitution or a swap of two neighbours
    /// (Damerau–Levenshtein ≤ 1). Allocation-free.</summary>
    public static bool WithinOneEdit(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        int m = a.Length, n = b.Length;
        if (Math.Abs(m - n) > 1) return false;
        int i = 0;
        while (i < m && i < n && a[i] == b[i]) i++;
        if (i == m && i == n) return true;          // equal
        if (m == n)
        {
            // substitution at i, or a transposition of i and i+1
            if (a[(i + 1)..].SequenceEqual(b[(i + 1)..])) return true;
            return i + 1 < m && a[i] == b[i + 1] && a[i + 1] == b[i] && a[(i + 2)..].SequenceEqual(b[(i + 2)..]);
        }
        // one insertion / deletion at i
        return m > n ? a[(i + 1)..].SequenceEqual(b[i..]) : a[i..].SequenceEqual(b[(i + 1)..]);
    }

    // Common Russian inflections, longest first, so "барабанов" / "барабаны" / "барабан" and
    // "тёплые" / "тёплый" meet on one stem.
    private static readonly string[] RuEndings =
    {
        "ами", "ями", "ого", "его", "ому", "ему", "ыми", "ими", "ая", "яя", "ое", "ее", "ые", "ие",
        "ый", "ий", "ой", "ов", "ев", "ам", "ям", "ах", "ях", "ом", "ем", "ую", "юю",
        "ы", "и", "а", "я", "о", "е", "у", "ю", "ь", "й",
    };

    /// <summary>A crude Russian stem: one inflectional ending off a word of 5+ letters.</summary>
    public static string RuStem(string w)
    {
        if (w.Length < 5) return w;
        foreach (var e in RuEndings)
            if (w.Length - e.Length >= 3 && w.EndsWith(e, StringComparison.Ordinal)) return w[..^e.Length];
        return w;
    }
}
