// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One language's texts by key (spec 077, contracts/language-file.md, format version 2): the
// English reference (en.json, embedded beside Loc) and every translation share this shape. The
// loader validates rather than trusts, like the colour-theme loader: a file that cannot be read
// as a language yields no table and a reason, never an exception thrown at the UI; an entry that
// is merely wrong is left out and listed in Problems, so the rest of the language still shows.
// Immutable once built, so lookups are lock-free from any thread.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nota.Application.Localization;

/// <summary>A translated text: a plain string (<see cref="One"/> is null and
/// <see cref="IsPlural"/> false), or the singular and other forms of a counted one.</summary>
public readonly record struct Translation(string? One, string Other, bool IsPlural = false);

/// <summary>Something wrong with one entry of a language file; the entry is not used.</summary>
public readonly record struct TranslationProblem(string Key, string Problem);

public sealed class TranslationTable
{
    /// <summary>The language-file format this build reads (contract § Rules).</summary>
    public const int FormatVersion = 2;

    /// <summary>A key: lower-case dot-separated segments, at least two (research R2).</summary>
    public static readonly Regex KeyPattern = new(@"^[a-z0-9_]+(\.[a-z0-9_]+)+$", RegexOptions.Compiled);
    public const int MaxKeyLength = 80;

    private static readonly Regex CodePattern = new("^[a-z]{2,3}(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex PlaceholderPattern = new(@"\{(\d+)(?:[,:][^{}]*)?\}", RegexOptions.Compiled);

    private readonly Dictionary<string, Translation> _strings;

    private TranslationTable(string code, string name, string pluralRule, string notes,
        Dictionary<string, Translation> strings, List<TranslationProblem> problems)
    {
        Code = code;
        Name = name;
        PluralRule = pluralRule;
        Notes = notes;
        _strings = strings;
        Problems = problems;
    }

    /// <summary>Lower-case language code, equal to the file name ("fr").</summary>
    public string Code { get; }

    /// <summary>The language's own name, shown in the picker ("Français").</summary>
    public string Name { get; }

    /// <summary>Which count takes the singular form (<see cref="UiLanguage.Plural"/>).</summary>
    public string PluralRule { get; }

    /// <summary>Free text for translators; not used at run time.</summary>
    public string Notes { get; }

    /// <summary>Entries left out while reading, with why. Empty for a clean file.</summary>
    public IReadOnlyList<TranslationProblem> Problems { get; }

    public int Count => _strings.Count;

    public IEnumerable<string> Keys => _strings.Keys;

    public bool TryGet(string key, out Translation translation) => _strings.TryGetValue(key, out translation);

    /// <summary>Parse one language file. Returns null with a reason when the file as a whole
    /// cannot be used (not JSON, another format version, a bad code or plural rule, a duplicate
    /// key) — the language is then not offered and Nota carries on in English. A bad key or a
    /// malformed or empty value only drops that entry (listed in <see cref="Problems"/>).</summary>
    public static TranslationTable? Parse(string json, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("the file must hold one JSON object", out error);
            if (!root.TryGetProperty("formatVersion", out var fv) || fv.ValueKind != JsonValueKind.Number
                || !fv.TryGetInt32(out var version))
                return Fail("formatVersion is missing", out error);
            if (version != FormatVersion)
                return Fail($"language file format {version} is not supported (this Nota reads format {FormatVersion})", out error);

            var code = Str(root, "code");
            if (!CodePattern.IsMatch(code)) return Fail($"invalid code '{code}'", out error);
            var name = Str(root, "name");
            if (name.Length is < 1 or > 40) return Fail("name must be 1–40 characters", out error);
            var rule = Str(root, "plural");
            if (rule.Length == 0 && code == UiLanguage.Default) rule = UiLanguage.RuleOneIs1;
            if (!UiLanguage.IsKnownRule(rule)) return Fail(rule.Length == 0 ? "plural rule is missing" : $"unknown plural rule '{rule}'", out error);
            var notes = Str(root, "notes");

            var strings = new Dictionary<string, Translation>(StringComparer.Ordinal);
            var problems = new List<TranslationProblem>();
            if (root.TryGetProperty("strings", out var table))
            {
                if (table.ValueKind != JsonValueKind.Object) return Fail("strings must be an object", out error);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in table.EnumerateObject())
                {
                    if (!seen.Add(p.Name)) return Fail($"duplicate key \"{p.Name}\"", out error);
                    if (!IsKey(p.Name)) { problems.Add(new(p.Name, "not a valid key (lower-case dot-separated segments, up to 80 characters)")); continue; }
                    if (!TryEntry(p.Value, out var entry)) { problems.Add(new(p.Name, "must be a text, or an object with an \"other\" text")); continue; }
                    if (entry.Other.Length == 0) { problems.Add(new(p.Name, "empty text (English is shown)")); continue; }
                    strings[p.Name] = entry;
                }
            }
            return new TranslationTable(code, name, rule, notes, strings, problems);
        }
        catch (JsonException e)
        {
            return Fail("not valid JSON: " + e.Message, out error);
        }
    }

    public static bool IsKey(string key) => key.Length <= MaxKeyLength && KeyPattern.IsMatch(key);

    /// <summary>The placeholder indices a text uses ({0}, {1:0.0} …); {{ }} are literal braces.</summary>
    public static HashSet<int> Placeholders(string text)
        => PlaceholderPattern.Matches(text.Replace("{{", "").Replace("}}", "")).Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();

    /// <summary>This language checked against the English reference: the entries that cannot be
    /// used (unknown key, text where the reference counts or the other way round, placeholders
    /// that differ) and the share of reference keys it translates validly, in whole percent
    /// rounded down (contract § Coverage). The table itself is not changed.</summary>
    public (IReadOnlyList<TranslationProblem> Problems, int Coverage) ValidateAgainst(TranslationTable reference)
    {
        var problems = new List<TranslationProblem>(Problems);
        int valid = 0;
        foreach (var (key, t) in _strings.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!reference.TryGet(key, out var r)) { problems.Add(new(key, "not in the English reference")); continue; }
            if (EntryProblem(t, r) is { } p) { problems.Add(new(key, p)); continue; }
            valid++;
        }
        int coverage = reference.Count == 0 ? 100 : (int)(100L * valid / reference.Count);
        return (problems, coverage);
    }

    /// <summary>Why a translation cannot stand for its reference entry, or null if it can.</summary>
    public static string? EntryProblem(Translation t, Translation reference)
    {
        if (t.IsPlural != reference.IsPlural)
            return reference.IsPlural ? "the English text is counted: give \"one\" and \"other\"" : "the English text is not counted: give a plain text";
        var want = Placeholders(reference.Other);
        if (!Placeholders(t.Other).SetEquals(want))
            return $"placeholders {{{string.Join(",", Placeholders(t.Other).Order())}}} differ from the English {{{string.Join(",", want.Order())}}}";
        if (t.One is not null && !Placeholders(t.One).IsSubsetOf(want))
            return "the singular form has a placeholder the English lacks";
        return null;
    }

    /// <summary>The identity of this table's key set (contract § Key-set hash): SHA-256 over
    /// the sorted lines "key \t text|plural \t placeholders", first 16 hex characters. Rewording
    /// a text keeps it; adding or removing a key, or changing a text's shape or placeholders,
    /// changes it.</summary>
    public string KeySetHash()
    {
        var lines = _strings
            .Select(kv => $"{kv.Key}\t{(kv.Value.IsPlural ? "plural" : "text")}\t{string.Join(",", Placeholders(kv.Value.Other).Order())}")
            .OrderBy(l => l, StringComparer.Ordinal);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    private static bool TryEntry(JsonElement v, out Translation entry)
    {
        entry = default;
        if (v.ValueKind == JsonValueKind.String) { entry = new Translation(null, v.GetString() ?? ""); return true; }
        if (v.ValueKind != JsonValueKind.Object) return false;
        if (!v.TryGetProperty("other", out var other) || other.ValueKind != JsonValueKind.String) return false;
        string? one = v.TryGetProperty("one", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
        if (one is { Length: 0 }) one = null;
        entry = new Translation(one, other.GetString() ?? "", IsPlural: true);
        return true;   // other plural categories are ignored (contract)
    }

    // Metadata is read only when it is a string: a number or object here fails the file with
    // a reason instead of throwing.
    private static string Str(JsonElement root, string key)
        => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static TranslationTable? Fail(string reason, out string? error)
    {
        error = reason;
        return null;
    }
}
