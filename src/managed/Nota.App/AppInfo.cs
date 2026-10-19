// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// App-level version + changelog access. The version's single source of truth is
// the repo-root VERSION file, baked into the assembly at build time by
// Directory.Build.props (InformationalVersion). Here we read it back at runtime
// and parse the embedded notes so the About / What's New windows can show them: a
// released version shows its curated release notes (release-notes/X.Y.Z.en.md, the
// same text as the GitHub release), and a version without them falls back to its
// CHANGELOG.md section. Distinct from the native engine version
// (NotaEngine.Version) — see the About window, which shows both.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Avalonia.Platform;

namespace Nota.App;

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

    /// <summary>Parsed changelog entries, newest first. Empty if the resource is missing.
    /// A version that shipped curated release notes gets those in place of its raw
    /// changelog section (the date still comes from the changelog).</summary>
    public static IReadOnlyList<ChangelogEntry> Changelog()
    {
        IReadOnlyList<ChangelogEntry> raw;
        try
        {
            raw = ChangelogParser.Parse(ReadAsset("avares://Nota.App/Assets/CHANGELOG.md"));
        }
        catch
        {
            return Array.Empty<ChangelogEntry>();
        }

        var entries = new List<ChangelogEntry>(raw.Count);
        foreach (var e in raw)
            entries.Add(ReleaseNotes(e) ?? e);
        return entries;
    }

    // The curated notes for a changelog entry's version, or null when that version
    // has none (unreleased work, versions from before release-notes/ existed).
    private static ChangelogEntry? ReleaseNotes(ChangelogEntry e)
    {
        if (!SemVer.TryParse(e.Version, out _)) return null;
        var uri = new Uri($"avares://Nota.App/Assets/ReleaseNotes/{e.Version}.en.md");
        try
        {
            if (!AssetLoader.Exists(uri)) return null;
            var notes = ReleaseNotesParser.Parse(e.Version, e.Date, ReadAsset(uri.ToString()));
            return notes.Sections.Count > 0 || notes.Lede.Length > 0 ? notes : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadAsset(string uri)
    {
        using var s = AssetLoader.Open(new Uri(uri));
        using var r = new StreamReader(s);
        return r.ReadToEnd();
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
