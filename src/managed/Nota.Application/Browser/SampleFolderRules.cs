// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The rules of the sample-folder list (spec 092, #346 / #268), kept free of the settings
// file so the smoke test can pin them: one folder may not be added twice, nor sit inside or
// around another (every file would be listed and indexed twice), exactly one folder is the
// default, and a settings file from before the list migrates to a one-entry list.

using System.Collections.Generic;
using System.IO;

namespace Nota.Application.Browser;

public static class SampleFolderRules
{
    /// <summary>Path comparison as the file system does it: case-insensitive on macOS and
    /// Windows, sensitive on Linux.</summary>
    public static StringComparer PathComparer
        => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>An absolute path without a trailing separator (a volume root keeps its own).
    /// "" stays "".</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string full;
        try { full = Path.GetFullPath(path.Trim()); } catch { full = path.Trim(); }
        string root = Path.GetPathRoot(full) ?? "";
        while (full.Length > root.Length && (full.EndsWith('/') || full.EndsWith('\\')))
            full = full[..^1];
        return full;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or lies inside
    /// it. Separator-aware: "/A/Drum" is not inside "/A/Drums".</summary>
    public static bool IsSameOrInside(string path, string folder)
    {
        path = Normalize(path); folder = Normalize(folder);
        if (path.Length == 0 || folder.Length == 0) return false;
        var cmp = PathComparer;
        if (cmp.Equals(path, folder)) return true;
        if (path.Length <= folder.Length) return false;
        if (!path.StartsWith(folder, cmp == StringComparer.Ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            return false;
        // A root like "/" or "C:\" already ends with a separator.
        char last = folder[^1];
        if (last == '/' || last == '\\') return true;
        char next = path[folder.Length];
        return next == '/' || next == '\\';
    }

    /// <summary>Whether <paramref name="candidate"/> may join <paramref name="existing"/>
    /// (paths, list order). Does not look at the disk.</summary>
    public static SampleFolderResult Check(IReadOnlyList<string> existing, string candidate)
    {
        candidate = Normalize(candidate);
        if (candidate.Length == 0) return new(SampleFolderOutcome.NotADirectory);
        var cmp = PathComparer;
        for (int i = 0; i < existing.Count; i++)
            if (cmp.Equals(Normalize(existing[i]), candidate)) return new(SampleFolderOutcome.AlreadyPresent, i);
        for (int i = 0; i < existing.Count; i++)
        {
            if (IsSameOrInside(candidate, existing[i])) return new(SampleFolderOutcome.Inside, i);
            if (IsSameOrInside(existing[i], candidate)) return new(SampleFolderOutcome.Contains, i);
        }
        return new(SampleFolderOutcome.Added);
    }

    /// <summary>Brings a loaded list into shape: a missing/empty list becomes one default
    /// entry at <paramref name="legacyFolder"/> (the pre-092 Samples folder, already resolved
    /// to a real path); paths are normalised; an entry equal to, inside or around an earlier
    /// one is dropped (returned, for the log); exactly one default remains (the first marked
    /// one, else the first entry).</summary>
    public static (List<SampleFolderEntry> List, List<string> Dropped) Repair(List<SampleFolderEntry>? list, string legacyFolder)
    {
        var dropped = new List<string>();
        var result = new List<SampleFolderEntry>();
        if (list is { Count: > 0 })
        {
            foreach (var e in list)
            {
                if (e is null) continue;
                var path = Normalize(e.Path);
                if (path.Length == 0) { dropped.Add(e.Path ?? ""); continue; }
                if (!Check(result.ConvertAll(r => r.Path), path).Ok) { dropped.Add(path); continue; }
                result.Add(new SampleFolderEntry
                {
                    Path = path,
                    Name = string.IsNullOrWhiteSpace(e.Name) ? null : e.Name.Trim(),
                    IsDefault = e.IsDefault,
                });
            }
        }
        if (result.Count == 0)
            result.Add(new SampleFolderEntry { Path = Normalize(legacyFolder), IsDefault = true });

        int def = result.FindIndex(e => e.IsDefault);
        if (def < 0) def = 0;
        for (int i = 0; i < result.Count; i++) result[i].IsDefault = i == def;
        return (result, dropped);
    }

    /// <summary>The label a folder shows: its name, else the folder's own name (a volume root
    /// shows itself).</summary>
    public static string Label(SampleFolderEntry e)
    {
        if (!string.IsNullOrWhiteSpace(e.Name)) return e.Name!;
        var name = Path.GetFileName(Normalize(e.Path));
        return name.Length > 0 ? name : e.Path;
    }
}
