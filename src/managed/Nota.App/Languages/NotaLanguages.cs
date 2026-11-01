// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The interface languages (spec 068). Languages/<code>.json is the only place a translation
// lives; each file is embedded as Nota.Languages.<code>.json and read here at startup. The
// picker lists whatever loads, so adding a language is adding a file. Like the colour-theme
// loader, this validates rather than trusts: a file that fails is left out and noted in
// LoadErrors, never thrown at the UI, and Nota still starts in English.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nota.Application.Localization;

namespace Nota.App;

/// <summary>One offered language: its code ("fr") and its own name ("Français").</summary>
public sealed record LanguageInfo(string Code, string Name);

public static class NotaLanguages
{
    private const string Prefix = "Nota.Languages.";
    private const string Suffix = ".json";

    private static IReadOnlyDictionary<string, TranslationTable>? _tables;
    private static readonly List<string> _loadErrors = new();

    /// <summary>Why a language file was left out, if any was. Logged at startup.</summary>
    public static IReadOnlyList<string> LoadErrors { get { _ = Tables; return _loadErrors; } }

    /// <summary>The languages the picker offers: English first, then the rest by code.</summary>
    public static IReadOnlyList<LanguageInfo> Available
        => Tables.Values
            .OrderBy(t => t.Code == UiLanguage.Default ? 0 : 1).ThenBy(t => t.Code, StringComparer.Ordinal)
            .Select(t => new LanguageInfo(t.Code, t.Name)).ToList();

    /// <summary>The codes of <see cref="Available"/>, for <see cref="UiLanguage.Normalize"/>.</summary>
    public static IReadOnlyCollection<string> Codes => Tables.Keys.ToList();

    /// <summary>The loaded table for a code, or null if there is none.</summary>
    public static TranslationTable? Find(string code) => Tables.TryGetValue(code, out var t) ? t : null;

    private static IReadOnlyDictionary<string, TranslationTable> Tables => _tables ??= Load();

    private static IReadOnlyDictionary<string, TranslationTable> Load()
    {
        var tables = new Dictionary<string, TranslationTable>(StringComparer.Ordinal);
        var asm = typeof(NotaLanguages).Assembly;
        foreach (var resource in asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix) && n.EndsWith(Suffix)).Order(StringComparer.Ordinal))
        {
            var fileCode = resource[Prefix.Length..^Suffix.Length];
            string json;
            using (var s = asm.GetManifestResourceStream(resource))
            {
                if (s is null) { _loadErrors.Add($"{fileCode}: language file not readable"); continue; }
                using var r = new StreamReader(s);
                json = r.ReadToEnd();
            }
            var table = TranslationTable.Parse(json, out var error);
            if (table is null) { _loadErrors.Add($"{fileCode}: {error}"); continue; }
            if (table.Code != fileCode) { _loadErrors.Add($"{fileCode}: file declares code '{table.Code}'"); continue; }
            tables[table.Code] = table;
        }
        // English is the fallback of every text; it is offered even if its file were lost.
        if (!tables.ContainsKey(UiLanguage.Default))
            tables[UiLanguage.Default] = TranslationTable.Parse("{\"formatVersion\":1,\"code\":\"en\",\"name\":\"English\"}", out _)!;
        return tables;
    }
}
