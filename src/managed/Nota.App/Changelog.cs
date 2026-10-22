// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Minimal Keep a Changelog parser + semver comparison. Only the subset the app
// authors is supported: "## [x.y.z] — date"
// version headers, "### Category" section headers, and "- item" bullets.
// The "## [Unreleased]" header (where changes accumulate before a release) parses
// into an entry too, but its version isn't a SemVer, so version filtering in
// AppInfo.UnseenSince skips it — unreleased notes never reach the What's New window.
//
// ReleaseNotesParser reads the curated notes a release ships (release-notes/X.Y.Z.en.md,
// format in .claude/skills/release/SKILL.md § P4): a summary — a lede and "**N. Title**"
// sections with a paragraph each — then, after a "<!-- changes -->" marker, the full
// list as "**Added**" / "**Changed**" / "**Fixed**" groups of "•" bullets ending in commit
// links, and after "<!-- contributors -->" a "**Contributors**" line of profile links.
// Notes from before the markers (a summary and "**Also**" bullets) parse as all-Feature
// sections. What's New prefers the notes over the raw changelog section. No Avalonia
// here: the smoke test compiles this file directly.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nota.App;

/// <summary>One version's notes: its number, date and grouped notes. <see cref="Lede"/>
/// is set only for curated release notes (the changelog has no lede).</summary>
public sealed record ChangelogEntry(string Version, string Date,
                                    IReadOnlyList<ChangelogSection> Sections,
                                    string Lede = "")
{
    public bool IsReleaseNotes => Lede.Length > 0;
}

/// <summary>A category within a changelog entry (Added / Changed / Fixed / Removed), or a
/// part of curated release notes: its title, a paragraph (<see cref="Text"/>) and/or
/// bullets. <see cref="Kind"/> says which part of the notes it belongs to.</summary>
public sealed record ChangelogSection(string Title, IReadOnlyList<string> Items, string Text = "",
                                      SectionKind Kind = SectionKind.Feature);

/// <summary>Which part of curated release notes a section is in: the summary's features
/// (and every changelog category), a group of the full change list, or the
/// contributors.</summary>
public enum SectionKind { Feature, Changes, Contributors }

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

public static class ReleaseNotesParser
{
    // "**1. Studio, a new look**" or "**Also**" alone on a line. The number is the
    // order in a news post; the window keeps the order and drops the number.
    private static readonly Regex Heading = new(@"^\*\*(?:\d+\.\s*)?(?<title>(?:(?!\*\*).)+)\*\*$");

    // Where the full change list and the contributors start: "<!-- changes -->" and
    // "<!-- contributors -->" alone on a (trimmed) line, any case, any spacing inside.
    // Comments, so they vanish on GitHub and in the posts whatever the language;
    // release-notes.sh and the smoke test match them the same way.
    public static readonly Regex ChangesMarker = new(@"^<!--\s*changes\s*-->$", RegexOptions.IgnoreCase);
    public static readonly Regex ContributorsMarker = new(@"^<!--\s*contributors\s*-->$", RegexOptions.IgnoreCase);

    public static ChangelogEntry Parse(string version, string date, string markdown)
    {
        var lede = new List<string>();
        var sections = new List<ChangelogSection>();
        string? title = null;
        var text = new List<string>();
        var items = new List<string>();
        var kind = SectionKind.Feature;

        void FlushSection()
        {
            if (title != null && (text.Count > 0 || items.Count > 0))
                sections.Add(new ChangelogSection(title, items.ToArray(), string.Join("\n", text), kind));
            title = null;
            text.Clear();
            items.Clear();
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith("<!--", StringComparison.Ordinal))
            {
                // The part markers; contributors close the notes, so a later
                // "changes" marker can't reopen the list.
                if (ChangesMarker.IsMatch(t) && kind == SectionKind.Feature)
                {
                    FlushSection();
                    kind = SectionKind.Changes;
                }
                else if (ContributorsMarker.IsMatch(t))
                {
                    FlushSection();
                    kind = SectionKind.Contributors;
                }
                continue;
            }

            if (Heading.Match(t) is { Success: true } m)
            {
                FlushSection();
                title = m.Groups["title"].Value.Trim();
            }
            else if (t.StartsWith('•') || t.StartsWith("- ", StringComparison.Ordinal))
            {
                if (title != null) items.Add(t[1..].Trim());
            }
            else if (title == null) lede.Add(t);
            else text.Add(t);
        }
        FlushSection();
        return new ChangelogEntry(version, date, sections, string.Join("\n", lede));
    }
}
