// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Minimal Keep a Changelog parser + semver comparison. Only the subset the app
// authors is supported: "## [x.y.z] — date"
// version headers, "### Category" section headers, and "- item" bullets.
// The "## [Unreleased]" header (where changes accumulate before a release) parses
// into an entry too, but its version isn't a SemVer, so version filtering in
// AppInfo.UnseenSince skips it — unreleased notes never reach the What's New window.

using System;
using System.Collections.Generic;

namespace Nota.App;

/// <summary>A comparable semantic version (MAJOR.MINOR.PATCH). Pre-release/build
/// metadata is ignored — the app only ships plain X.Y.Z versions.</summary>
public readonly record struct SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
{
    public int CompareTo(SemVer o)
    {
        if (Major != o.Major) return Major.CompareTo(o.Major);
        if (Minor != o.Minor) return Minor.CompareTo(o.Minor);
        return Patch.CompareTo(o.Patch);
    }

    public static bool TryParse(string? s, out SemVer v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var core = s.Trim();
        // Drop any -prerelease / +build suffix before splitting the numeric core.
        foreach (var sep in new[] { '-', '+' })
        {
            var i = core.IndexOf(sep);
            if (i >= 0) core = core[..i];
        }
        var parts = core.Split('.');
        if (parts.Length < 3) return false;
        if (int.TryParse(parts[0], out var maj) &&
            int.TryParse(parts[1], out var min) &&
            int.TryParse(parts[2], out var pat))
        {
            v = new SemVer(maj, min, pat);
            return true;
        }
        return false;
    }
}

public static class ChangelogParser
{
    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        string? version = null, date = "";
        List<ChangelogSection>? sections = null;
        string? sectionTitle = null;
        List<string>? items = null;

        void FlushSection()
        {
            if (sectionTitle != null && items != null && items.Count > 0)
                sections!.Add(new ChangelogSection(sectionTitle, items));
            sectionTitle = null;
            items = null;
        }
        void FlushEntry()
        {
            FlushSection();
            if (version != null)
                entries.Add(new ChangelogEntry(version, date, sections ?? new List<ChangelogSection>()));
            version = null; date = ""; sections = null;
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var t = line.Trim();

            if (t.StartsWith("## ")) // version header: "## [0.2.0] — 2026-07-30"
            {
                FlushEntry();
                var (ver, dt) = ParseVersionHeader(t[3..]);
                version = ver; date = dt;
                sections = new List<ChangelogSection>();
            }
            else if (t.StartsWith("### ") && version != null) // category header
            {
                FlushSection();
                sectionTitle = t[4..].Trim();
                items = new List<string>();
            }
            else if ((t.StartsWith("- ") || t.StartsWith("* ")) && items != null)
            {
                items.Add(t[2..].Trim());
            }
            else if (items != null && t.Length > 0 && !t.StartsWith("#"))
            {
                // Continuation of the previous wrapped bullet.
                if (items.Count > 0) items[^1] += " " + t;
            }
        }
        FlushEntry();
        return entries;
    }

    // "[0.2.0] — 2026-07-30" → ("0.2.0", "2026-07-30"). Tolerant of missing date /
    // different dashes; the version is whatever sits inside the [brackets].
    private static (string version, string date) ParseVersionHeader(string s)
    {
        s = s.Trim();
        string version = s, date = "";
        var open = s.IndexOf('[');
        var close = s.IndexOf(']');
        if (open >= 0 && close > open)
        {
            version = s[(open + 1)..close].Trim();
            var rest = s[(close + 1)..].Trim();
            date = rest.TrimStart('—', '-', '–', ' ').Trim();
        }
        return (version, date);
    }
}
