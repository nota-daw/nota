// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The interface languages this Nota can start in (specs 068 and 077). English is the reference
// built into Loc; every other language is one Nota downloaded from nota-translations or a file a
// translator loaded, both kept by InstalledLanguages under the data folder. Read once at start-up
// with no network: what is installed is what Nota can run in. Like the colour-theme loader, this
// validates rather than trusts — a file that cannot be read is left out and noted in LoadErrors,
// an entry that cannot be used shows English, and Nota always starts.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nota.Application.Localization;
using Nota.Infrastructure;

namespace Nota.App;

/// <summary>One language Nota can start in: its code ("fr") and its own name ("Français").</summary>
public sealed record LanguageInfo(string Code, string Name);

public static class NotaLanguages
{
    private static IReadOnlyDictionary<string, TranslationTable>? _tables;
    private static readonly List<string> _loadErrors = new();
    private static InstalledLanguages? _installed;

    /// <summary>The languages folder (created on first use).</summary>
    public static InstalledLanguages Installed => _installed ??= new InstalledLanguages();

    /// <summary>Why a language was left out or which of its entries show English. Logged at start.</summary>
    public static IReadOnlyList<string> LoadErrors { get { _ = Tables; return _loadErrors; } }

    /// <summary>English first, then the installed languages by code.</summary>
    public static IReadOnlyList<LanguageInfo> Available
        => Tables.Values
            .OrderBy(t => t.Code == UiLanguage.Default ? 0 : 1).ThenBy(t => t.Code, StringComparer.Ordinal)
            .Select(t => new LanguageInfo(t.Code, t.Name)).ToList();

    /// <summary>The codes of <see cref="Available"/>, for <see cref="UiLanguage.Normalize"/>.</summary>
    public static IReadOnlyCollection<string> Codes => Tables.Keys.ToList();

    /// <summary>The table for a code, or null if that language is not installed.</summary>
    public static TranslationTable? Find(string code) => Tables.TryGetValue(code, out var t) ? t : null;

    /// <summary>A language file read from a path (NOTA_UI_LANGUAGE=/path/to/de.json): this run
    /// only, never installed. Null with a reason when it cannot be used.</summary>
    public static TranslationTable? FromFile(string path, out string? error)
    {
        try
        {
            var table = TranslationTable.Parse(File.ReadAllText(path), out error);
            if (table is not null) Report(table.Code, table);
            return table;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return null;
        }
    }

    private static IReadOnlyDictionary<string, TranslationTable> Tables => _tables ??= Load();

    private static IReadOnlyDictionary<string, TranslationTable> Load()
    {
        var tables = new Dictionary<string, TranslationTable>(StringComparer.Ordinal);
        if (Loc.EnglishError is { } englishError) _loadErrors.Add($"en: {englishError}");
        tables[UiLanguage.Default] = Loc.English;
        IReadOnlyList<InstalledLanguage> installed;
        try { installed = Installed.List(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _loadErrors.Add("languages folder: " + e.Message); installed = Array.Empty<InstalledLanguage>(); }
        foreach (var l in installed)
        {
            if (l.Table is null) { _loadErrors.Add($"{l.Code}: {l.Error} — the language is not offered"); continue; }
            if (l.Code == UiLanguage.Default) continue;   // English is the built-in reference
            Report(l.Code, l.Table);
            tables[l.Code] = l.Table;
        }
        return tables;
    }

    private static void Report(string code, TranslationTable table)
    {
        var (problems, _) = table.ValidateAgainst(Loc.English);
        foreach (var p in problems.Take(50)) _loadErrors.Add($"{code}: {p.Key} — {p.Problem} (English is shown)");
        if (problems.Count > 50) _loadErrors.Add($"{code}: …and {problems.Count - 50} more entries that show English");
    }
}
