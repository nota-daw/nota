// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Turns what the user typed or pasted into the Files tab's path bar (spec 092, #580) into
// a folder to open and, for a file path, the file to select. Pasted paths come quoted
// (Finder's "Copy as Pathname" in a shell, Explorer's "Copy as path"), as file:// URLs, or
// with "~" for home — all accepted.

using System.IO;

namespace Nota.Application.Browser;

public static class PathInput
{
    /// <summary>The folder to open and the file to select (null for a folder path), or
    /// null when the text names nothing that exists.</summary>
    public static (string Folder, string? SelectFile)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim();
        if (t.Length >= 2 && (t[0] == '"' && t[^1] == '"' || t[0] == '\'' && t[^1] == '\'')) t = t[1..^1].Trim();
        if (t.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try { t = new Uri(t).LocalPath; } catch { return null; }
        }
        if (t == "~" || t.StartsWith("~/", StringComparison.Ordinal) || t.StartsWith("~\\", StringComparison.Ordinal))
            t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), t.Length > 2 ? t[2..] : "");
        if (!Path.IsPathRooted(t)) return null;

        string p = SampleFolderRules.Normalize(t);
        try
        {
            if (Directory.Exists(p)) return (p, null);
            if (File.Exists(p)) return (Path.GetDirectoryName(p) ?? p, p);
        }
        catch { /* unreadable: treated as missing */ }
        return null;
    }
}
