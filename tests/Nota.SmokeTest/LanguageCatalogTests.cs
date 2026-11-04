// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Downloadable interface languages (spec 077 stories 3–5): the language list from
// nota-translations, a download pinned to size + sha256, update / remove, a translator's local
// file, and the offline cases — against a fake nota-translations served from memory (an
// HttpMessageHandler), so the real network path and its on-disk cache are exercised with no
// network. The fixture language is Fixtures/Languages/xx.json, checked against the real English.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class LanguageCatalogTests
{
    private const string Base = "https://translations.test";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>Serves a dictionary of URL → body; any other URL is 404, and Offline makes every
    /// request fail as an unreachable network does. Counts requests.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
        public bool Offline;
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (Offline) throw new HttpRequestException("offline (test)");
            var url = request.RequestUri!.ToString();
            return Task.FromResult(Files.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static string Index(string hash, int version, byte[] file, int formatVersion = 1, string commit = Commit)
        => $$"""
           { "formatVersion": {{formatVersion}}, "generated": "2026-11-04T00:00:00Z",
             "references": { "0.58.0": "0000000000000000", "develop": "{{hash}}" },
             "languages": [ { "code": "xx", "name": "Xxxx", "version": {{version}}, "commit": "{{commit}}",
               "path": "languages/xx.json", "size": {{file.Length}},
               "sha256": "{{Convert.ToHexStringLower(SHA256.HashData(file))}}", "coverage": { "{{hash}}": 1, "0000000000000000": 2 } } ] }
           """;

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var root = RepoRoot();
        if (root is null) { yield return (false, "repo root located"); yield break; }
        var xx = File.ReadAllBytes(Path.Combine(root, "tests/Nota.SmokeTest/Fixtures/Languages/xx.json"));
        var hash = Loc.English.KeySetHash();
        var dir = Path.Combine(Path.GetTempPath(), "nota-smoke-languages-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var r in Scenario(dir, xx, hash)) yield return r;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static IEnumerable<(bool Ok, string Label)> Scenario(string dir, byte[] xx, string hash)
    {
        var server = new FakeServer();
        server.Files[$"{Base}/published/index.json"] = Encoding.UTF8.GetBytes(Index(hash, 1, xx));
        server.Files[$"{Base}/{Commit}/languages/xx.json"] = xx;
        var installed = new InstalledLanguages(dir);
        var catalog = new LanguageCatalog(installed, Base, server);

        // Nothing installed, nothing cached: a fresh Nota ships English only.
        yield return (installed.List().Count == 0 && catalog.Cached() is null, "a fresh data folder has no language and no list");

        // The list, and coverage for this Nota's key set (contracts/index.md § App behaviour).
        var index = catalog.FetchAsync(refresh: false).GetAwaiter().GetResult();
        var offered = index.Languages.SingleOrDefault(l => l.Code == "xx");
        yield return (offered is { Name: "Xxxx", Version: 1 } && index.References.Count == 2, "the list offers the language with its name and version");
        yield return (offered is not null && LanguageCatalog.CoverageFor(index, offered, hash, "0.60.0") == 1
                      && LanguageCatalog.CoverageFor(index, offered, "ffffffffffffffff", "0.60.0") == 2
                      && LanguageCatalog.CoverageFor(index, offered, "ffffffffffffffff", "0.57.0") is null,
            "coverage is the running key set's, else the newest older release's, else unknown");
        if (offered is null) yield break;

        // Install: pinned commit, size and sha256; recorded with its version.
        var got = catalog.DownloadAsync(offered).GetAwaiter().GetResult();
        yield return (got.Table is { Code: "xx" } && got.Meta is { Source: InstalledLanguageMeta.Repository, Version: 1, Commit: Commit },
            "a download installs the language and records where it came from");
        yield return (installed.List().Select(l => l.Code).SequenceEqual(new[] { "xx" }) && !Directory.EnumerateFiles(dir, ".staging-*").Any(),
            "the installed language is listed and no staging file is left");
        yield return (!LanguageCatalog.UpdateAvailable(got, offered), "the same version offers no update");

        // A newer version is offered as an update; a download that does not match its sha256 is
        // refused and the installed copy stays.
        // Same size, one byte changed: only the sha256 can tell.
        var tampered = (byte[])xx.Clone();
        tampered[Array.IndexOf(tampered, (byte)'X')] = (byte)'Y';
        server.Files[$"{Base}/published/index.json"] = Encoding.UTF8.GetBytes(Index(hash, 2, xx));
        server.Files[$"{Base}/{Commit}/languages/xx.json"] = tampered;
        var newer = catalog.FetchAsync(refresh: true).GetAwaiter().GetResult().Languages.Single();
        yield return (LanguageCatalog.UpdateAvailable(installed.Read("xx")!, newer), "a newer version in the list is offered as an update");
        string? refused = null;
        try { catalog.DownloadAsync(newer).GetAwaiter().GetResult(); }
        catch (StoreException e) { refused = e.Message; }
        var kept = installed.Read("xx");
        yield return (refused is not null && kept is { Table: not null, Meta.Version: 1 } && !Directory.EnumerateFiles(dir, ".staging-*").Any(),
            $"a download that does not match its checksum is refused and the installed copy stays ({refused})");
        server.Files[$"{Base}/{Commit}/languages/xx.json"] = xx;
        var updated = catalog.DownloadAsync(newer).GetAwaiter().GetResult();
        yield return (updated.Meta.Version == 2, "the update installs the newer version");

        // An unsafe entry is never fetched.
        var evil = newer with { Path = "../../etc/passwd" };
        string? unsafeError = null;
        try { catalog.DownloadAsync(evil).GetAwaiter().GetResult(); }
        catch (StoreException e) { unsafeError = e.Message; }
        yield return (unsafeError is not null, "a list entry with a path outside the repository is refused");

        // Offline: the list falls back to the cache; an installed language needs no network.
        server.Offline = true;
        int before = server.Requests;
        var offline = catalog.FetchAsync(refresh: true).GetAwaiter().GetResult();
        yield return (offline.Languages.Count == 1 && server.Requests == before + 1, "offline, the list is the cached one");
        yield return (installed.Read("xx") is { Table: not null } && server.Requests == before + 1, "an installed language is read with no network");
        var noCache = new LanguageCatalog(new InstalledLanguages(Path.Combine(dir, "other")), Base, server);
        bool threw = false;
        try { noCache.FetchAsync(refresh: false).GetAwaiter().GetResult(); } catch (StoreException) { threw = true; }
        yield return (threw && noCache.Cached() is null, "offline with no cached list: a StoreException to show in place, nothing thrown at the UI");
        server.Offline = false;

        // A list from a newer Nota format is not read.
        bool newerFormat = false;
        try { LanguageCatalog.Parse(Index(hash, 3, xx, formatVersion: 2)); } catch (StoreException) { newerFormat = true; }
        yield return (newerFormat, "a list of a newer format asks to update Nota instead of guessing");

        // Review of #485: a malformed entry is skipped, not thrown.
        var withNull = Index(hash, 3, xx).Replace("\"languages\": [", "\"languages\": [ null, { \"code\": \"zz\" },");
        var parsedNull = LanguageCatalog.Parse(withNull);
        yield return (parsedNull.Languages.Count == 1 && parsedNull.Languages[0].Code == "xx", "null and incomplete entries in the list are skipped");

        // A failed install puts the language back as it was (file and record).
        var fileBefore = File.ReadAllText(Path.Combine(dir, "xx.json"));
        var metaPath = Path.Combine(dir, "xx.meta.json");
        var metaBefore = File.ReadAllText(metaPath);
        var staging = installed.StagingPath("xx");
        File.WriteAllText(staging, "{\"formatVersion\":2,\"code\":\"xx\",\"name\":\"New\",\"plural\":\"one_is_1\"}");
        File.Delete(metaPath);
        Directory.CreateDirectory(metaPath);   // the record cannot be written
        bool installFailed = false;
        try { installed.InstallFrom(staging, "xx", new InstalledLanguageMeta(InstalledLanguageMeta.Repository, Version: 9)); }
        catch (Exception) { installFailed = true; }
        Directory.Delete(metaPath, true);
        File.WriteAllText(metaPath, metaBefore);
        yield return (installFailed && File.ReadAllText(Path.Combine(dir, "xx.json")) == fileBefore,
            "a failed install leaves the installed file as it was");

        // Loading the installed file itself through Load file… keeps it.
        installed.InstallLocal(Path.Combine(dir, "xx.json"), "xx");
        yield return (installed.Read("xx") is { Meta.IsLocal: true, Table: not null }, "loading the installed file as a local file keeps it readable");

        // A file that cannot be read (permissions) is reported, not thrown.
        if (!OperatingSystem.IsWindows())
        {
            var locked = Path.Combine(dir, "locked.json");
            File.WriteAllBytes(locked, xx);
            installed.InstallLocal(locked, "xx");
            File.SetUnixFileMode(locked, UnixFileMode.None);
            InstalledLanguage? lockedRead = null;
            bool threwLocked = false;
            try { lockedRead = installed.Read("xx"); } catch (Exception) { threwLocked = true; }
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            yield return (!threwLocked && lockedRead is { Table: null, Error: not null } || !threwLocked && lockedRead is { Table: not null },
                "a language file without read permission is reported, not thrown");
        }

        // Remove: the files go; the code is no longer installed.
        installed.Remove("xx");
        yield return (installed.Read("xx") is null && installed.List().Count == 0, "removing a language deletes it");

        // Story 5: a stored language that is not installed is fetched and installed.
        var settings = new Settings { Language = "xx" };
        yield return (installed.Read(settings.Language) is null, "a stored language that is not installed reads as not installed");
        var again = catalog.FetchAsync(refresh: true).GetAwaiter().GetResult().Languages.Single(l => l.Code == settings.Language);
        yield return (catalog.DownloadAsync(again).GetAwaiter().GetResult().Table is not null, "…and the start-up download installs it");

        // A translator's local file: read in place, problems listed, never replaced by a download.
        var root = RepoRoot()!;
        var brokenPath = Path.Combine(root, "tests/Nota.SmokeTest/Fixtures/Languages/xx-broken.json");
        installed.InstallLocal(brokenPath, "xx");
        var local = installed.Read("xx");
        var (problems, _) = local?.Table?.ValidateAgainst(Loc.English) ?? (Array.Empty<TranslationProblem>(), 0);
        yield return (local is { Meta.IsLocal: true, Table: not null } && local.Meta.Path == Path.GetFullPath(brokenPath) && problems.Count > 0,
            $"a local file is read in place with its problems listed ({problems.Count})");
        yield return (local is not null && !LanguageCatalog.UpdateAvailable(local, again with { Version = 99 }), "a local file is never offered an update");
        Loc.ResetForTests();
        Loc.Install(local?.Table, "xx");
        yield return (Loc.English.TryGet("common.cancel", out var cancel) && Loc.T("common.cancel") == cancel.Other,
            "entries a local file lacks or breaks show English");
        Loc.ResetForTests();
        Loc.Install(null, "en");

        var missingLocal = Path.Combine(dir, "gone.json");
        File.WriteAllBytes(missingLocal, xx);
        installed.InstallLocal(missingLocal, "xx");
        File.Delete(missingLocal);
        yield return (installed.Read("xx") is { Table: null, Error: not null }, "a local file that is gone at start is reported, not thrown");
        yield return (!InstalledLanguages.IsSafeCode("../x") && !InstalledLanguages.IsSafeCode("") && InstalledLanguages.IsSafeCode("pt-br"),
            "a language code is checked before it becomes a file name");
    }

    private static string? RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src/managed")) && Directory.Exists(Path.Combine(d.FullName, "tests"))) return d.FullName;
        return null;
    }
}
