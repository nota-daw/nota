// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The user themes folder (spec 087): every *.json file in it, read in a fixed order and
// validated, with a plain reason for each one that cannot be worn. Never throws — a bad
// file is a line in the problems list, not an error dialog. Pure, so the smoke test checks
// the same rules the app runs.

using Nota.Application.Localization;

namespace Nota.Application.Themes;

public sealed record ThemeFolderEntry(string FileName, string Path, ThemeValidation Validation);

public sealed record ThemeFolderScan(IReadOnlyList<ThemeFolderEntry> Loaded, IReadOnlyList<(string FileName, string Reason)> Problems);

public static class ThemeFolder
{
    /// <summary>Read <paramref name="dir"/>. A file loads when it validates and its id is neither
    /// <paramref name="reservedId"/> (a built-in) nor the id of an earlier file (ordinal file-name
    /// order). A missing folder is an empty one.</summary>
    public static ThemeFolderScan Scan(string dir, IReadOnlyList<string> catalogue, IReadOnlySet<string> translucent,
        Func<string, bool> reservedId, Func<string, bool>? fontInstalled = null)
    {
        var loaded = new List<ThemeFolderEntry>();
        var problems = new List<(string, string)>();
        string[] files;
        try { files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : Array.Empty<string>(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add((dir, Loc.F("themes.problem.folder_unreadable", e.Message)));
            return new(loaded, problems);
        }
        Array.Sort(files, StringComparer.Ordinal);

        var ids = new HashSet<string>();
        foreach (var path in files)
        {
            var file = System.IO.Path.GetFileName(path);
            string json;
            try
            {
                var len = new FileInfo(path).Length;
                if (len > ThemeFile.MaxFileBytes)
                {
                    problems.Add((file, Loc.F("themes.problem.too_large", ThemeFile.MaxFileBytes / 1024)));
                    continue;
                }
                json = File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add((file, Loc.F("themes.problem.file_unreadable", e.Message)));
                continue;
            }

            var v = ThemeFile.Validate(json, catalogue, translucent, fontInstalled);
            if (!v.IsValid)
            {
                problems.Add((file, Describe(v.Problems)));
                continue;
            }
            var id = v.Theme!.Id;
            if (reservedId(id))
            {
                problems.Add((file, Loc.F("themes.problem.builtin_id", id)));
                continue;
            }
            if (!ids.Add(id))
            {
                problems.Add((file, Loc.F("themes.problem.duplicate_id", id)));
                continue;
            }
            loaded.Add(new(file, path, v));
        }
        return new(loaded, problems);
    }

    /// <summary>The first three problems in one line, then how many more.</summary>
    public static string Describe(IReadOnlyList<ThemeProblem> problems)
    {
        var head = string.Join("; ", problems.Take(3).Select(p => p.Message));
        return problems.Count > 3 ? Loc.F("themes.problem.and_more", head, problems.Count - 3) : head;
    }
}
