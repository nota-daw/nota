// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which language of the user manual (nota-daw/nota-docs) the app opens. The manual is English
// at its root, with a translation under /<code>/ for each language listed here. The choice
// follows the language the interface runs in (Loc.Language), never the OS language
// (spec 068 FR-006). Pure, no Avalonia, so the smoke test can walk it.

using System;

namespace Nota.App;

internal static class ManualLanguage
{
    // The translations that exist in nota-docs (src/content/docs/<code>/). Add a code here when
    // a translation lands there. A page a translation lacks is served by the manual itself, in
    // English with a notice, so the app never has to check pages.
    private static readonly string[] Translated = { "fr", "ru" };

    /// <summary>The manual's folder for an interface language code: "fr/", "ru/", or "" (the
    /// English root) for English and for any language the manual does not have. A regional code
    /// ("fr-CA") reads as its language.</summary>
    public static string Folder(string? uiLanguage)
    {
        if (string.IsNullOrWhiteSpace(uiLanguage)) return "";
        int cut = uiLanguage.AsSpan().IndexOfAny('-', '_');
        string code = (cut < 0 ? uiLanguage : uiLanguage[..cut]).Trim().ToLowerInvariant();
        return Array.IndexOf(Translated, code) >= 0 ? code + "/" : "";
    }
}
