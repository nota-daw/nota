// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Parses the sample registry's index.json (github.com/nota-daw/nota-samples-registry — its
// README documents the manifest), and holds the rules for which files of a pack get installed.
// Those mirror AUDIO / EXTRAS / JUNK_* in the registry's scripts/registry.py, so the
// manifest's files / unpackedSize match what Nota puts on disk.

using System.Text.Json;

namespace Nota.Infrastructure;

public static class SampleIndex
{
    /// <summary>Highest index schema this build understands.</summary>
    public const int SupportedSchema = 1;

    /// <summary>Audio the engine decodes (AudioFile.cpp picks the decoder by extension); these
    /// count as the pack's samples. Other audio (aiff, ogg) is skipped like any other file.</summary>
    public static readonly IReadOnlySet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "wav", "flac", "mp3" };

    /// <summary>Docs and mappings installed alongside the samples ("" = no extension: LICENSE,
    /// README). Anything else in an archive (synth presets, Kontakt files, DAW projects) is skipped.</summary>
    public static readonly IReadOnlySet<string> ExtraExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "txt", "md", "pdf", "rtf", "html", "htm", "sfz", "mid", "midi", "png", "jpg", "jpeg", "json", "xml", "csv", "nfo", "" };

    private static readonly HashSet<string> JunkFiles = new(StringComparer.OrdinalIgnoreCase) { ".DS_Store", "Thumbs.db", "desktop.ini" };

    public static string ExtensionOf(string name)
    {
        var trimmed = name.TrimStart('.');
        int dot = trimmed.LastIndexOf('.');
        return dot < 0 ? "" : trimmed[(dot + 1)..].ToLowerInvariant();
    }

    public static bool IsJunkDir(string name) => name == "__MACOSX";

    public static bool IsJunkFile(string name) => JunkFiles.Contains(name) || name.StartsWith("._", StringComparison.Ordinal);

    /// <summary>Whether a file of a pack gets installed.</summary>
    public static bool IsInstalled(string name)
    {
        if (IsJunkFile(name)) return false;
        var ext = ExtensionOf(name);
        return AudioExtensions.Contains(ext) || ExtraExtensions.Contains(ext);
    }

    public static IReadOnlyList<StorePack> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int schema = root.TryGetProperty("schema", out var s) && s.TryGetInt32(out var n) ? n : 0;
        if (schema is < 1 or > SupportedSchema)
            throw new StoreException("The sample registry needs a newer version of Nota.");

        var list = new List<StorePack>();
        foreach (var p in root.GetProperty("packs").EnumerateArray())
        {
            try { list.Add(ParsePack(p)); }
            catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // One malformed entry must not hide the rest of the registry.
            }
        }
        return list;
    }

    private static StorePack ParsePack(JsonElement p)
    {
        var latest = p.GetProperty("versions")[0];
        var a = latest.GetProperty("asset");
        string url = a.GetProperty("url").GetString()!;
        var archive = Str(a, "archive") ?? ArchiveUnpacker.KindOf(url);
        if (archive is not ("zip" or "tar")) throw new FormatException("archive type");
        var asset = new StorePackAsset(
            url,
            a.GetProperty("sha256").GetString()!.ToLowerInvariant(),
            a.GetProperty("size").GetInt64(),
            archive,
            Str(a, "root"),
            a.GetProperty("files").GetInt32(),
            a.GetProperty("unpackedSize").GetInt64(),
            a.TryGetProperty("formats", out var f) ? f.EnumerateArray().Select(x => x.GetString()!).ToList() : []);

        return new StorePack(
            Id: p.GetProperty("id").GetString()!,
            Name: p.GetProperty("name").GetString()!,
            Author: p.GetProperty("author").GetString()!,
            Description: p.GetProperty("description").GetString()!,
            Kind: p.GetProperty("kind").GetString()!,
            License: p.GetProperty("license").GetString()!,
            Source: p.GetProperty("source").GetString()!,
            Tags: p.TryGetProperty("tags", out var t) ? t.EnumerateArray().Select(x => x.GetString()!).ToList() : [],
            Version: latest.GetProperty("version").GetString()!,
            Asset: asset)
        {
            Attribution = Str(p, "attribution"),
            Notes = Str(p, "notes"),
            Homepage = Str(p, "homepage"),
        };
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
