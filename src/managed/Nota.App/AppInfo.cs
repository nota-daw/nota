// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// App-level version + changelog access. The version's single source of truth is
// the repo-root VERSION file, baked into the assembly at build time by
// Directory.Build.props (InformationalVersion). Here we read it back at runtime
// and parse the embedded CHANGELOG.md so the About / What's New windows can show
// the human-readable notes. Distinct from the native engine version
// (NotaEngine.Version) — see the About window, which shows both.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Avalonia.Platform;

namespace Nota.App;

/// <summary>One version's changelog: its number, date and grouped notes.</summary>
public sealed record ChangelogEntry(string Version, string Date,
                                    IReadOnlyList<ChangelogSection> Sections);

/// <summary>A category within a changelog entry (Added / Changed / Fixed / Removed).</summary>
public sealed record ChangelogSection(string Title, IReadOnlyList<string> Items);

public static class AppInfo
{
    /// <summary>Running app version as semver "MAJOR.MINOR.PATCH" (from the VERSION file
    /// baked in as InformationalVersion). SourceLink may append "+commit"; we strip it.</summary>
    public static string Version
    {
        get
        {
            var info = typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info))
                info = typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
    }

    /// <summary>Parsed changelog entries, newest first. Empty if the resource is missing.</summary>
    public static IReadOnlyList<ChangelogEntry> Changelog()
    {
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://Nota.App/Assets/CHANGELOG.md"));
            using var r = new StreamReader(s);
            return ChangelogParser.Parse(r.ReadToEnd());
        }
        catch
        {
            return Array.Empty<ChangelogEntry>();
        }
    }

    /// <summary>Changelog entries strictly newer than <paramref name="sinceVersion"/>
    /// (the last one the user saw). If it's blank/unparsable, returns only the current
    /// version's entry — a fresh install shouldn't replay the whole history.</summary>
    public static IReadOnlyList<ChangelogEntry> UnseenSince(string sinceVersion)
    {
        var all = Changelog();
        if (!SemVer.TryParse(sinceVersion, out var seen))
        {
            // Find the entry for the running version — not just all[0], which is now
            // the unreleased "## [Unreleased]" section (skipped: no parseable version).
            ChangelogEntry? cur = null;
            foreach (var e in all)
                if (e.Version == Version) { cur = e; break; }
            return cur is null ? Array.Empty<ChangelogEntry>() : new[] { cur };
        }

        var unseen = new List<ChangelogEntry>();
        foreach (var e in all)
            if (SemVer.TryParse(e.Version, out var v) && v.CompareTo(seen) > 0)
                unseen.Add(e);
        return unseen;
    }
}
