// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The one call every user-visible text goes through (spec 068, contracts/loc-api.md). The
// English source text is the key, so English needs no table and wrapping a literal can never
// change what an English user sees; any text the running language lacks falls back to its
// English original, never blank and never an identifier. The language is fixed for the whole
// run (a change applies after a restart), so static text tables built after Install stay
// right and no view ever has to rebuild.
//
// Numbers format with the thread's current culture, exactly as the interpolated strings they
// replace did, and the language never changes that culture: choosing French alters no number.
// In the app that culture is the display culture NotaNum installs at start-up (a point, a real
// minus sign), so a tempo, dB or frequency reads as it did before.

using System.Globalization;

namespace Nota.Application.Localization;

public static class Loc
{
    private sealed record State(string Code, TranslationTable? Table);

    private static volatile State _state = new(UiLanguage.Default, null);
    private static volatile bool _installed;
    private static volatile bool _usedBeforeInstall;

    /// <summary>The running language code. English until <see cref="Install"/>.</summary>
    public static string Language => _state.Code;

    /// <summary>True if a text was asked for before <see cref="Install"/>. Such a text was
    /// built in English and will not follow the language, so the app logs it and the smoke
    /// test watches for it (static tables must be built after the install).</summary>
    public static bool UsedBeforeInstall => _usedBeforeInstall;

    /// <summary>Called once at start-up by the app, before any window or static text table is
    /// built. English passes a null table.</summary>
    public static void Install(TranslationTable? table, string languageCode)
    {
        _state = new State(languageCode, languageCode == UiLanguage.Default ? null : table);
        _installed = true;
    }

    /// <summary>Back to the uninstalled English state. For tests only.</summary>
    public static void ResetForTests()
    {
        _state = new State(UiLanguage.Default, null);
        _installed = false;
        _usedBeforeInstall = false;
    }

    /// <summary>The text in the running language, else <paramref name="text"/> itself. A
    /// <paramref name="context"/> separates two meanings of the same English word.</summary>
    public static string T(string text, string? context = null)
    {
        var table = Enter();
        return table is not null && Find(table, text, context) is { } t ? t.Other : text;
    }

    /// <summary>A text with values: one whole pattern ("Delete {0} clips?") so the translator
    /// controls word order and agreement. Placeholders are {0}, {1}…; {{ }} are literal braces.</summary>
    public static string F(string text, params object?[] args) => Format(Pattern(text, null), text, args);

    /// <summary><see cref="F"/> with a context, for the few patterns that need one.</summary>
    public static string FC(string context, string text, params object?[] args) => Format(Pattern(text, context), text, args);

    /// <summary>A counted text. <paramref name="other"/> is the key; <paramref name="n"/> is
    /// {0} and picks the form by the running language's rule (French counts 0 and 1 as one).</summary>
    public static string Plural(long n, string one, string other, string? context = null)
    {
        var table = Enter();
        string english = UiLanguage.Plural(UiLanguage.Default, n) == PluralForm.One ? one : other;
        string pattern = table is not null && Find(table, other, context) is { } t
            ? UiLanguage.Plural(_state.Code, n) == PluralForm.One ? t.One ?? t.Other : t.Other
            : english;
        return Format(pattern, english, new object?[] { n });
    }

    private static TranslationTable? Enter()
    {
        if (!_installed) _usedBeforeInstall = true;
        return _state.Table;
    }

    private static string Pattern(string text, string? context)
    {
        var table = Enter();
        return table is not null && Find(table, text, context) is { } t ? t.Other : text;
    }

    // An empty translation counts as missing: a text is never shown blank.
    private static Translation? Find(TranslationTable table, string text, string? context)
        => table.TryGet(TranslationTable.KeyOf(text, context), out var t) && t.Other.Length > 0 ? t : null;

    // A bad pattern in a language file must not crash the UI: fall back to the English one.
    private static string Format(string pattern, string english, object?[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, pattern, args); }
        catch (FormatException)
        {
            try { return string.Format(CultureInfo.CurrentCulture, english, args); }
            catch (FormatException) { return english; }
        }
    }
}
