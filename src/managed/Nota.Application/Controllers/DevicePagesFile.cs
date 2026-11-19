// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The device pages file (specs/051, contracts/device-pages.md): `format: 1` and `types`, a map
// from a device type key to its pages, each with an optional name and up to eight parameter
// references (a name, `id:<PluginParamId>`, `macro:<n>`, or null for an empty knob). One format
// for the pages Nota ships, the user's global `pages.json` and a project's `devicepages.json`.

using System.Text;
using System.Text.Json;
using Nota.Application.Localization;

namespace Nota.Application.Controllers;

public static class DevicePagesFile
{
    public const int Format = 1;

    /// <summary>Read a pages file; null with <paramref name="error"/> when it can't be used
    /// (bad JSON, a newer format). Malformed entries are skipped.</summary>
    public static Dictionary<string, IReadOnlyList<DevicePage>>? Parse(string json, out string? error)
    {
        error = null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { error = Loc.F("controllers.profile.bad_json", ex.Message); return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = Loc.T("controllers.profile.not_object"); return null; }
            if (!root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.Number || !f.TryGetInt32(out int format) || format < 1)
            { error = Loc.T("controllers.profile.no_format"); return null; }
            if (format > Format) { error = Loc.F("controllers.profile.newer_format", format); return null; }

            var types = new Dictionary<string, IReadOnlyList<DevicePage>>(StringComparer.Ordinal);
            if (!root.TryGetProperty("types", out var t) || t.ValueKind != JsonValueKind.Object) return types;
            foreach (var type in t.EnumerateObject())
            {
                if (type.Value.ValueKind != JsonValueKind.Array) continue;
                var pages = new List<DevicePage>();
                foreach (var p in type.Value.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    string? name = p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    var refs = new List<string?>();
                    if (p.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Array)
                        foreach (var r in ps.EnumerateArray())
                        {
                            if (refs.Count == DevicePage.Knobs) break;
                            refs.Add(r.ValueKind == JsonValueKind.String ? r.GetString() : null);
                        }
                    pages.Add(new DevicePage(string.IsNullOrWhiteSpace(name) ? null : name, refs));
                }
                if (pages.Count > 0) types[type.Name] = pages;
            }
            return types;
        }
    }

    /// <summary>A pages file for <paramref name="types"/> (sorted by type key, so saves diff cleanly).</summary>
    public static string Write(IReadOnlyDictionary<string, IReadOnlyList<DevicePage>> types)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("format", Format);
            w.WriteStartObject("types");
            foreach (var (key, pages) in types.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                w.WriteStartArray(key);
                foreach (var p in pages)
                {
                    w.WriteStartObject();
                    if (p.Name is { Length: > 0 } name) w.WriteString("name", name);
                    w.WriteStartArray("params");
                    foreach (var r in p.Params)
                        if (r is null) w.WriteNullValue(); else w.WriteStringValue(r);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray()) + "\n";
    }
}
