// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The one call every user-visible text goes through (spec 077, contracts/loc-api.md). A text is
// asked for by a stable key ("preferences.language.restart"), never by its English wording, so
// rewording English never detaches a translation. English lives in en.json, embedded in this
// assembly beside Loc: it is there before Install, in tests that never install a language and in
// projects that do not reference the app. Any text the running language lacks, or has in a form
// that cannot be used, falls back to English — never blank. The language is fixed for the whole
// run (a change applies after a restart), so static text tables built after Install stay right
// and no view ever has to rebuild.
//
// Numbers format with the thread's current culture, exactly as the interpolated strings they
// replace did, and the language never changes that culture: choosing French alters no number.
// In the app that culture is the display culture NotaNum installs at start-up (a point, a real
// minus sign), so a tempo, dB or frequency reads as it did before.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;

namespace Nota.Application.Localization;

public static class Loc
{
    /// <summary>The manifest name of the English reference in this assembly.</summary>
    public const string EnglishResource = "Nota.Localization.en.json";

    private sealed record State(string Code, TranslationTable? Table);

    private static readonly Lazy<TranslationTable> _english = new(LoadEnglish);
    private static volatile TranslationTable? _englishForTests;
    private static volatile State _state = new(UiLanguage.Default, null);
    private static volatile bool _installed;
    private static volatile bool _usedBeforeInstall;
    private static readonly ConcurrentDictionary<string, byte> _missing = new(StringComparer.Ordinal);

    /// <summary>The running language code. English until <see cref="Install"/>.</summary>
    public static string Language => _state.Code;

    /// <summary>The English reference: every key with its English text.</summary>
    public static TranslationTable English => _englishForTests ?? _english.Value;

    /// <summary>Why the English reference could not be read, if it could not (then every text
    /// shows its key). Null in a healthy build; the smoke test checks it.</summary>
    public static string? EnglishError { get; private set; }

    /// <summary>Raised once per key that is in no table, not even English (the key is shown).
    /// The smoke test makes this unreachable; the app logs it.</summary>
    public static event Action<string>? MissingKey;

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

    /// <summary>Back to the uninstalled English state. For tests only: <paramref name="english"/>
    /// stands in for the embedded reference until the next reset (null = the real one).</summary>
    public static void ResetForTests(TranslationTable? english = null)
    {
        _englishForTests = english;
        _state = new State(UiLanguage.Default, null);
        _installed = false;
        _usedBeforeInstall = false;
    }

    /// <summary>The text of <paramref name="key"/> in the running language, else in English.</summary>
    public static string T(string key) => Lookup(key, Enter()) is { } t ? t.Other : Missing(key);

    /// <summary>A text with values: one whole pattern ("Delete {0} clips?") so the translator
    /// controls word order and agreement. Placeholders are {0}, {1}…; {{ }} are literal braces.</summary>
    public static string F(string key, params object?[] args)
    {
        var table = Enter();
        var english = English.TryGet(key, out var e) ? e.Other : Missing(key);
        var pattern = Lookup(key, table) is { } t ? t.Other : english;
        return Format(pattern, english, args);
    }

    /// <summary>A counted text: <paramref name="n"/> is {0} and picks the form by the running
    /// language's rule (French counts 0 and 1 as one).</summary>
    public static string Plural(string key, long n)
    {
        var table = Enter();
        if (!English.TryGet(key, out var e)) return Missing(key);
        string english = UiLanguage.Plural(UiLanguage.RuleOneIs1, n) == PluralForm.One ? e.One ?? e.Other : e.Other;
        string pattern = english;
        if (table is not null && Valid(table, key, e) is { } t)
            pattern = UiLanguage.Plural(table.PluralRule, n) == PluralForm.One ? t.One ?? t.Other : t.Other;
        return Format(pattern, english, new object?[] { n });
    }

    private static TranslationTable? Enter()
    {
        if (!_installed) _usedBeforeInstall = true;
        return _state.Table;
    }

    // The running language's entry when it can stand for the English one, else the English entry.
    private static Translation? Lookup(string key, TranslationTable? table)
    {
        if (!English.TryGet(key, out var e)) return null;
        return table is not null && Valid(table, key, e) is { } t ? t : e;
    }

    // A translation is used only if it has the English entry's shape and placeholders: a broken
    // entry in a language file shows English, it never crashes or garbles the UI.
    private static Translation? Valid(TranslationTable table, string key, Translation english)
        => table.TryGet(key, out var t) && TranslationTable.EntryProblem(t, english) is null ? t : null;

    private static string Missing(string key)
    {
        if (_missing.TryAdd(key, 0)) MissingKey?.Invoke(key);
        return key;
    }

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

    private static TranslationTable LoadEnglish()
    {
        const string empty = "{\"formatVersion\":2,\"code\":\"en\",\"name\":\"English\"}";
        string? error;
        using (var s = typeof(Loc).Assembly.GetManifestResourceStream(EnglishResource))
        {
            if (s is null) error = $"{EnglishResource} is not embedded";
            else
            {
                using var r = new StreamReader(s);
                if (TranslationTable.Parse(r.ReadToEnd(), out error) is { } table) return table;
            }
        }
        EnglishError = error;
        return TranslationTable.Parse(empty, out _)!;
    }
}
