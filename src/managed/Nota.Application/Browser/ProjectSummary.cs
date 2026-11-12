// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What the browser's Projects tab (spec 092, #582) says about a project without opening
// it: when it was last saved, its tempo, key, length and track count. The date rule lives
// here so the Projects tab and the Welcome window show the same date for a project.

using System.Globalization;
using System.IO;

namespace Nota.Application.Browser;

/// <summary>A project's details as read from its manifest. <see cref="LengthBeats"/> is the
/// end of its last arrangement clip.</summary>
public sealed record ProjectSummary(double Bpm, string? Key, int TrackCount, double LengthBeats)
{
    /// <summary>The length as m:ss at the project's tempo ("3:42").</summary>
    public string LengthText
    {
        get
        {
            double sec = Bpm > 0 ? LengthBeats * 60.0 / Bpm : 0;
            int s = (int)Math.Round(sec);
            return $"{s / 60}:{s % 60:00}";
        }
    }
}

/// <summary>Reads project summaries (cached; may touch the disk — call off the UI thread).</summary>
public interface IProjectSummaries
{
    /// <summary>The summary of the bundle at <paramref name="bundlePath"/>, or null when its
    /// manifest can't be read.</summary>
    ProjectSummary? Get(string bundlePath);
}

public static class ProjectDates
{
    /// <summary>The manifest's name inside a <c>.nota</c> bundle.</summary>
    public const string ManifestName = "project.json";

    /// <summary>When the project was last saved: its manifest's write time (every save
    /// rewrites it), else the bundle folder's; null when neither can be read.</summary>
    public static DateTime? Modified(string bundlePath)
    {
        try
        {
            var manifest = Path.Combine(bundlePath, ManifestName);
            if (File.Exists(manifest)) return File.GetLastWriteTime(manifest);
            if (Directory.Exists(bundlePath)) return Directory.GetLastWriteTime(bundlePath);
        }
        catch { /* unreadable */ }
        return null;
    }

    /// <summary>"11/12/2026 14:20" — the Welcome window's recent-projects format.</summary>
    public static string Format(DateTime when) => when.ToString("MM/dd/yyyy HH:mm", CultureInfo.InvariantCulture);
}
