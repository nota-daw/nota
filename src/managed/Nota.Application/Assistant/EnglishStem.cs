// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A light English stemmer for the manual search (specs/098, research R3): enough to make
// "takes" find "take", "recording" find "record", "loops" find "loop". Not Porter — the manual
// is small and a missed ending only costs a little rank; an over-eager rule costs wrong hits.

namespace Nota.Application.Assistant;

public static class EnglishStem
{
    /// <summary>The stem of a lower-case ASCII word; anything else is returned as it is.</summary>
    public static string Of(string w) => DropE(Strip(w));

    // "make" / "making" / "makes" all end as "mak": the silent e goes after the suffix rules.
    private static string DropE(string w) => w.Length >= 4 && w[^1] == 'e' && w[^2] != 'e' ? w[..^1] : w;

    private static string Strip(string w)
    {
        if (w.Length < 4) return w;
        foreach (char c in w) if (c is < 'a' or > 'z') return w;
        if (w.EndsWith("ies", StringComparison.Ordinal) && w.Length > 4) return w[..^3] + "y";
        if (w.EndsWith("ing", StringComparison.Ordinal) && w.Length > 5) return Undouble(w[..^3]);
        if (w.EndsWith("eed", StringComparison.Ordinal)) return w;            // speed, need, feed
        if (w.EndsWith("ed", StringComparison.Ordinal) && w.Length > 4) return Undouble(w[..^2]);
        if (w.EndsWith("ly", StringComparison.Ordinal) && w.Length > 5) return w[..^2];
        if (w.EndsWith("sses", StringComparison.Ordinal)) return w[..^2];
        if (w.EndsWith("ches", StringComparison.Ordinal) || w.EndsWith("shes", StringComparison.Ordinal)
            || w.EndsWith("xes", StringComparison.Ordinal)) return w[..^2];
        if (w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) && !w.EndsWith("us", StringComparison.Ordinal)
            && !w.EndsWith("is", StringComparison.Ordinal)) return w[..^1];
        return w;
    }

    // "stopped" → "stopp" → "stop"; "dropping" → "drop". Keeps "ll", "ss" ("fill", "pass").
    private static string Undouble(string w)
    {
        if (w.Length >= 3 && w[^1] == w[^2] && w[^1] is not ('l' or 's' or 'z') && !"aeiou".Contains(w[^1]))
            return w[..^1];
        return w;
    }
}
