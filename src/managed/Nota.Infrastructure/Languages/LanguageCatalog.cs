// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The languages nota-translations offers (spec 077, contracts/index.md) and their download. The
// index is the published branch's index.json, cached a day under <data>/languages and used as
// the offline fallback, like the plugin and sample registries (RegistryClient). A language file
// is fetched from <base>/<commit>/<path> — the commit the index pins — so it always matches the
// size + sha256 the index states; it is parsed and checked before it replaces anything.
// NOTA_LANGUAGES_URL relocates <base> (a URL or a local folder laid out the same way: tests,
// staging). Nothing fetched is executed: a language file is data.

using System.Text.Json;
using System.Text.Json.Serialization;
using Nota.Application.Localization;

namespace Nota.Infrastructure;

/// <summary>One language the index offers.</summary>
public sealed record CatalogLanguage(
    string Code, string Name, int Version, string Commit, string Path, long Size, string Sha256,
    IReadOnlyDictionary<string, int> Coverage);

/// <summary>The parsed index: Nota version → key-set hash, and the languages.</summary>
public sealed record LanguageIndex(IReadOnlyDictionary<string, string> References, IReadOnlyList<CatalogLanguage> Languages);

public sealed class LanguageCatalog
{
    /// <summary>The index shape this build reads; a higher one is ignored ("update Nota").</summary>
    public const int IndexFormat = 1;
    public const string DefaultBase = "https://raw.githubusercontent.com/nota-daw/nota-translations";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly InstalledLanguages _installed;
    private readonly RegistryClient _http;
    private readonly string _base;
    private readonly string _cachePath;

    public LanguageCatalog(InstalledLanguages installed)
        : this(installed, Environment.GetEnvironmentVariable("NOTA_LANGUAGES_URL") is { Length: > 0 } b ? b : DefaultBase) { }

    /// <param name="baseUrl">nota-translations raw root (or a local folder with the same layout).</param>
    public LanguageCatalog(InstalledLanguages installed, string baseUrl, HttpMessageHandler? handler = null)
    {
        _installed = installed;
        _base = baseUrl.TrimEnd('/');
        _cachePath = System.IO.Path.Combine(installed.Root, "index.json");
        _http = new RegistryClient(_base + "/published/index.json", _cachePath, "language list", "Nota-Languages", handler);
    }

    /// <summary>The last index fetched, without any network, or null if there is none yet (or it
    /// is unreadable).</summary>
    public LanguageIndex? Cached()
    {
        try { return File.Exists(_cachePath) ? Parse(File.ReadAllText(_cachePath)) : null; }
        catch (Exception e) when (e is JsonException or IOException or StoreException) { return null; }
    }

    /// <summary>The index: the cache while it is a day fresh (unless <paramref name="refresh"/>),
    /// else the network, falling back to the cache offline. Throws <see cref="StoreException"/>
    /// when there is neither, or the index is newer than this Nota reads.</summary>
    public async Task<LanguageIndex> FetchAsync(bool refresh, CancellationToken ct = default)
        => Parse(await _http.FetchIndexAsync(refresh, json => Parse(json), ct).ConfigureAwait(false));

    /// <summary>How complete <paramref name="language"/> is for this Nota: its coverage for the
    /// running key set, else for the newest reference of a version at or below
    /// <paramref name="appVersion"/>; null when the index has neither.</summary>
    public static int? CoverageFor(LanguageIndex index, CatalogLanguage language, string keySetHash, string appVersion)
    {
        if (language.Coverage.TryGetValue(keySetHash, out var exact)) return exact;
        var app = VersionOf(appVersion);
        var older = index.References
            .Where(r => r.Key != "develop" && VersionOf(r.Key) is { } v && app is not null && v <= app)
            .OrderByDescending(r => VersionOf(r.Key))
            .Select(r => language.Coverage.TryGetValue(r.Value, out var c) ? c : (int?)null)
            .FirstOrDefault(c => c is not null);
        return older;
    }

    /// <summary>Whether the index has a newer version of an installed repository language.</summary>
    public static bool UpdateAvailable(InstalledLanguage installed, CatalogLanguage offered)
        => !installed.Meta.IsLocal && (installed.Meta.Version ?? 0) < offered.Version;

    /// <summary>Downloads a language, checks its size, sha256 and that it reads as a language
    /// file for its code, and installs it. On any failure the installed copy (if any) stays.</summary>
    public async Task<InstalledLanguage> DownloadAsync(CatalogLanguage language, IProgress<StoreProgress>? progress = null, CancellationToken ct = default)
    {
        if (!InstalledLanguages.IsSafeCode(language.Code) || !IsSafePath(language.Path) || !IsCommit(language.Commit))
            throw new StoreException(Loc.F("language_catalog.the_language_list_entry_for_is_invalid", language.Code));
        var staging = _installed.StagingPath(language.Code);
        try
        {
            await _http.DownloadAsync($"{_base}/{language.Commit}/{language.Path}", language.Size, language.Sha256,
                staging, language.Name, progress, ct).ConfigureAwait(false);
            var table = TranslationTable.Parse(await File.ReadAllTextAsync(staging, ct).ConfigureAwait(false), out var error);
            if (table is null) throw new StoreException(Loc.F("language_catalog.the_downloaded_file_is_not_a_language", language.Name, error ?? ""));
            if (table.Code != language.Code) throw new StoreException(Loc.F("language_catalog.the_downloaded_file_is_not_a_language", language.Name, table.Code));
            _installed.InstallFrom(staging, language.Code,
                new InstalledLanguageMeta(InstalledLanguageMeta.Repository, Version: language.Version, Commit: language.Commit, Sha256: language.Sha256));
            return _installed.Read(language.Code)!;
        }
        finally
        {
            try { File.Delete(staging); } catch { /* moved, or never written */ }
        }
    }

    /// <summary>Parses an index; throws on a malformed one or one of a newer format.</summary>
    public static LanguageIndex Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<RawIndex>(json, Json) ?? throw new JsonException("empty language list");
        if (raw.FormatVersion > IndexFormat)
            throw new StoreException(Loc.T("language_catalog.update_nota_to_see_more_languages"));
        var langs = (raw.Languages ?? new())
            .Where(l => l.Code is { } c && InstalledLanguages.IsSafeCode(c) && l.Name is { Length: > 0 } && l.Commit is { } && l.Path is { } && l.Sha256 is { })
            .Select(l => new CatalogLanguage(l.Code!, l.Name!, l.Version, l.Commit!, l.Path!, l.Size, l.Sha256!,
                l.Coverage ?? new Dictionary<string, int>()))
            .OrderBy(l => l.Code, StringComparer.Ordinal).ToList();
        return new LanguageIndex(raw.References ?? new Dictionary<string, string>(), langs);
    }

    private static Version? VersionOf(string s)
    {
        var core = s.Split('-', '+')[0];
        return System.Version.TryParse(core, out var v) ? v : null;
    }

    private static bool IsSafePath(string p)
        => p.Length > 0 && !p.StartsWith('/') && !p.Contains("..") && !p.Contains('\\') && !p.Contains("://");

    private static bool IsCommit(string c) => c.Length is >= 7 and <= 64 && c.All(char.IsAsciiHexDigitLower);

    private sealed class RawIndex
    {
        public int FormatVersion { get; set; }
        public Dictionary<string, string>? References { get; set; }
        public List<RawLanguage>? Languages { get; set; }
    }

    private sealed class RawLanguage
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public int Version { get; set; }
        public string? Commit { get; set; }
        public string? Path { get; set; }
        public long Size { get; set; }
        public string? Sha256 { get; set; }
        public Dictionary<string, int>? Coverage { get; set; }
    }
}
