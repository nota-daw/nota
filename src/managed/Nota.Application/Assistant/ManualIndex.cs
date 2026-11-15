// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The user manual cut into sections (specs/098, contracts/manual-index.md): what the Assistant
// searches and quotes, and the Show me steps its how-tos carry. Built from nota-docs by
// scripts/build-manual-index.sh, shipped inside each release (never downloaded), read here.
// A file this reader doesn't understand makes the index unavailable — the panel says so and
// the rest of Nota is untouched.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nota.Application.Assistant;

/// <summary>One step of a how-to: the anchor Show me points at (<c>ui:…</c> or <c>cmd:…</c>)
/// and the step's words, as Markdown.</summary>
public sealed record AnchoredStep(string Anchor, string Text);

/// <summary>A page intro or one <c>##</c> heading with everything under it, in one language.</summary>
public sealed record ManualSection(
    string Id,                 // "<lang>/<page>#<slug>"
    string Lang,
    string Page,
    string PageTitle,
    string Heading,            // "" for the intro
    string Slug,               // "" for the intro
    string Markdown,
    string? Twin,              // the English section a translation pairs with
    bool HowTo,
    bool Explanatory,
    IReadOnlyList<AnchoredStep> Steps);

public sealed class ManualIndex
{
    /// <summary>The format this build reads and the builder writes.</summary>
    public const int CurrentFormat = 1;

    public int Format { get; init; }
    public string DocsCommit { get; init; } = "";
    public string BuiltFor { get; init; } = "";
    public IReadOnlyList<string> Languages { get; init; } = [];
    public IReadOnlyList<ManualSection> Sections { get; init; } = [];

    /// <summary>sha256 of the decompressed file — keys the meaning-search vector cache.</summary>
    public string Hash { get; init; } = "";

    /// <summary>Why the index can't be used, or null when it can.</summary>
    public string? Unavailable { get; init; }

    public bool IsAvailable => Unavailable is null;

    private Dictionary<string, ManualSection>? _byId;

    public ManualSection? Find(string id)
    {
        _byId ??= Sections.ToDictionary(s => s.Id, StringComparer.Ordinal);
        return _byId.GetValueOrDefault(id);
    }

    public static ManualIndex Failed(string reason) => new() { Unavailable = reason };

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>Reads a gzip-compressed index. Never throws: a bad file comes back unavailable.</summary>
    public static ManualIndex Read(Stream gz)
    {
        byte[] raw;
        try
        {
            using var unzip = new GZipStream(gz, CompressionMode.Decompress);
            using var mem = new MemoryStream();
            unzip.CopyTo(mem);
            raw = mem.ToArray();
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return Failed(e.Message);
        }
        return Parse(raw);
    }

    /// <summary>Reads the decompressed JSON.</summary>
    public static ManualIndex Parse(byte[] json)
    {
        try { return ParseOrThrow(json); }
        catch (Exception e) when (e is JsonException or InvalidDataException) { return Failed(e.Message); }
    }

    // Diagnostics only (logged, never shown): the panel says "the manual isn't available".
    private static ManualIndex ParseOrThrow(byte[] json)
    {
        var file = JsonSerializer.Deserialize<FileModel>(json, Json) ?? throw new InvalidDataException("empty index file");
        if (file.Format != CurrentFormat) throw new InvalidDataException($"unknown index format {file.Format}");

        var sections = (file.Sections ?? []).Select(s => new ManualSection(
            s.Id ?? "", s.Lang ?? "", s.Page ?? "", s.PageTitle ?? "", s.Heading ?? "", s.Slug ?? "",
            s.Markdown ?? "", s.Twin, s.HowTo, s.Explanatory,
            (s.Steps ?? []).Select(t => new AnchoredStep(t.Anchor ?? "", t.Text ?? "")).ToList())).ToList();

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in sections)
            if (s.Id.Length == 0 || !ids.Add(s.Id)) throw new InvalidDataException($"duplicate or empty section id '{s.Id}'");
        foreach (var s in sections)
        {
            if (s.Lang == "en") continue;
            if (s.Twin is null || !ids.Contains(s.Twin) || !s.Twin.StartsWith("en/", StringComparison.Ordinal))
                throw new InvalidDataException($"section '{s.Id}' has no English twin");
        }

        return new ManualIndex
        {
            Format = file.Format,
            DocsCommit = file.DocsCommit ?? "",
            BuiltFor = file.BuiltFor ?? "",
            Languages = file.Languages ?? [],
            Sections = sections,
            Hash = Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant(),
        };
    }

    /// <summary>Serialises an index the way the builder writes it (sorted by id).</summary>
    public static byte[] Write(int format, string docsCommit, string builtFor, IEnumerable<string> languages,
                               IEnumerable<ManualSection> sections)
    {
        var file = new FileModel
        {
            Format = format,
            DocsCommit = docsCommit,
            BuiltFor = builtFor,
            Languages = languages.ToList(),
            Sections = sections.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => new SectionModel
            {
                Id = s.Id, Lang = s.Lang, Page = s.Page, PageTitle = s.PageTitle, Heading = s.Heading,
                Slug = s.Slug, Markdown = s.Markdown, Twin = s.Twin, HowTo = s.HowTo, Explanatory = s.Explanatory,
                Steps = s.Steps.Select(t => new StepModel { Anchor = t.Anchor, Text = t.Text }).ToList(),
            }).ToList(),
        };
        return JsonSerializer.SerializeToUtf8Bytes(file, Json);
    }

    private sealed class FileModel
    {
        public int Format { get; set; }
        public string? DocsCommit { get; set; }
        public string? BuiltFor { get; set; }
        public List<string>? Languages { get; set; }
        public List<SectionModel>? Sections { get; set; }
    }

    private sealed class SectionModel
    {
        public string? Id { get; set; }
        public string? Lang { get; set; }
        public string? Page { get; set; }
        public string? PageTitle { get; set; }
        public string? Heading { get; set; }
        public string? Slug { get; set; }
        public string? Markdown { get; set; }
        public string? Twin { get; set; }
        public bool HowTo { get; set; }
        public bool Explanatory { get; set; }
        public List<StepModel>? Steps { get; set; }
    }

    private sealed class StepModel
    {
        public string? Anchor { get; set; }
        public string? Text { get; set; }
    }
}
