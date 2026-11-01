// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The interface language (spec 068): which codes exist, what a stored value means, when a
// change is still waiting for a restart, and how a language counts ("1 track", "2 tracks").
// Pure — no UI, no I/O — so the smoke test can walk it, like InterfaceScale. A hand-edited or
// future value never throws: it reads as English.

using System;
using System.Collections.Generic;

namespace Nota.Application.Localization;

public enum PluralForm { One, Other }

public static class UiLanguage
{
    /// <summary>English: the default, and what any missing text falls back to.</summary>
    public const string Default = "en";

    /// <summary>The stored code if it names an available language (case ignored), else
    /// <see cref="Default"/>. The settings file is not rewritten by this.</summary>
    public static string Normalize(string? code, IReadOnlyCollection<string> available)
    {
        if (string.IsNullOrWhiteSpace(code)) return Default;
        var lower = code.Trim().ToLowerInvariant();
        foreach (var a in available)
            if (string.Equals(a, lower, StringComparison.Ordinal)) return lower;
        return Default;
    }

    /// <summary>True while the stored language is not the one running: a restart is pending.
    /// Choosing the running language again clears it.</summary>
    public static bool IsPending(string? stored, string running, IReadOnlyCollection<string> available)
        => !string.Equals(Normalize(stored, available), running, StringComparison.Ordinal);

    /// <summary>Which plural form a whole-number count takes in a language. English: one when
    /// n is 1. French: one when n is 0 or 1. A language with no rule here counts like English.</summary>
    public static PluralForm Plural(string code, long n) => code switch
    {
        "fr" => n is 0 or 1 ? PluralForm.One : PluralForm.Other,
        _ => n == 1 ? PluralForm.One : PluralForm.Other,
    };
}
