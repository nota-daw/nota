// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// New-version check for the welcome screen. Asks the GitHub API for the latest
// published release (drafts and pre-releases are excluded by that endpoint) and
// compares its tag against the running version. Best-effort: any network / parse
// failure just means "no update known" — the launcher never waits on or errors
// because of it.

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nota.App;

/// <summary>A release newer than the running app: its version and the page to download it from.</summary>
public sealed record AvailableUpdate(string Version, string Url);

public static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/nota-daw/nota/releases";
    private const string LatestApi = "https://api.github.com/repos/nota-daw/nota/releases/latest";

    /// <summary>The latest GitHub release if it's newer than <see cref="AppInfo.Version"/>, else null.</summary>
    public static async Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            // GitHub's API rejects requests without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Nota/{AppInfo.Version}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await http.GetAsync(LatestApi, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);

            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            return NewerThanRunning(tag, url);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Compare a release tag ("v1.2.3" or "1.2.3") against the running version.</summary>
    public static AvailableUpdate? NewerThanRunning(string? tag, string? url, string? running = null)
    {
        var latest = tag?.Trim().TrimStart('v', 'V');
        if (!SemVer.TryParse(latest, out var lv)) return null;
        if (!SemVer.TryParse(running ?? AppInfo.Version, out var cv)) return null;
        if (lv.CompareTo(cv) <= 0) return null;
        return new AvailableUpdate(latest!, string.IsNullOrWhiteSpace(url) ? ReleasesPage : url!);
    }
}
