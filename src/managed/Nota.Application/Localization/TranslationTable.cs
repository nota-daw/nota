// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// One language's texts (spec 068, contracts/translation-file.md): a map from the English
// source text to its translation, read from a JSON language file. The loader validates
// rather than trusts, like the colour-theme loader: a file that fails yields no table and
// a reason, never an exception thrown at the UI. Immutable once built, so lookups are
// lock-free from any thread.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nota.Application.Localization;

/// <summary>A translated text: a plain string (<see cref="One"/> is null), or the singular
/// and other forms of a counted one.</summary>
public readonly record struct Translation(string? One, string Other);

public sealed class TranslationTable
{
    private static readonly Regex CodePattern = new("^[a-z]{2,3}(-[a-z0-9]+)*$", RegexOptions.Compiled);

    private readonly Dictionary<string, Translation> _strings;

    private TranslationTable(string code, string name, string notes, Dictionary<string, Translation> strings)
    {
        Code = code;
        Name = name;
        Notes = notes;
        _strings = strings;
    }

    /// <summary>Lower-case language code, equal to the file name ("fr").</summary>
    public string Code { get; }

    /// <summary>The language's own name, shown in the picker ("Français").</summary>
    public string Name { get; }

    /// <summary>Free text for translators; not used at run time.</summary>
    public string Notes { get; }

    public int Count => _strings.Count;

    public IEnumerable<string> Keys => _strings.Keys;

    /// <summary>The lookup key of a text: the English text itself, or "text|context" when the
    /// same English needs two translations.</summary>
    public static string KeyOf(string text, string? context)
        => string.IsNullOrEmpty(context) ? text : text + "|" + context;

    public bool TryGet(string key, out Translation translation) => _strings.TryGetValue(key, out translation);

    /// <summary>Parse and validate one language file. Returns null with a reason on the first
    /// problem; a file with a newer formatVersion, a duplicate key or a malformed entry is
    /// left out (the language is not offered) and Nota carries on in English.</summary>
    public static TranslationTable? Parse(string json, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail("the file must hold one JSON object", out error);
            if (!root.TryGetProperty("formatVersion", out var fv) || fv.ValueKind != JsonValueKind.Number
                || !fv.TryGetInt32(out var version) || version != 1)
                return Fail("formatVersion must be 1", out error);

            var code = Str(root, "code");
            if (!CodePattern.IsMatch(code)) return Fail($"invalid code '{code}'", out error);
            var name = Str(root, "name");
            if (name.Length is < 1 or > 40) return Fail("name must be 1–40 characters", out error);
            var notes = Str(root, "notes");

            var strings = new Dictionary<string, Translation>(StringComparer.Ordinal);
            if (root.TryGetProperty("strings", out var table))
            {
                if (table.ValueKind != JsonValueKind.Object) return Fail("strings must be an object", out error);
                foreach (var p in table.EnumerateObject())
                {
                    if (strings.ContainsKey(p.Name)) return Fail($"duplicate key \"{p.Name}\"", out error);
                    if (!TryEntry(p.Value, out var entry)) return Fail($"\"{p.Name}\": a text, or an object with an \"other\" text", out error);
                    strings[p.Name] = entry;
                }
            }
            return new TranslationTable(code, name, notes, strings);
        }
        catch (JsonException e)
        {
            return Fail("not valid JSON: " + e.Message, out error);
        }
    }

    private static bool TryEntry(JsonElement v, out Translation entry)
    {
        entry = default;
        if (v.ValueKind == JsonValueKind.String) { entry = new Translation(null, v.GetString() ?? ""); return true; }
        if (v.ValueKind != JsonValueKind.Object) return false;
        if (!v.TryGetProperty("other", out var other) || other.ValueKind != JsonValueKind.String) return false;
        string? one = v.TryGetProperty("one", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
        entry = new Translation(one, other.GetString() ?? "");
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
