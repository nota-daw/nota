// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// OS user folders for the library defaults (#522): Linux reads freedesktop user-dirs
// (translated/relocated Documents/Music), Windows/macOS use their Known Folders, so a
// non-English home does not grow an unwanted ~/Documents. Pure except for the file read
// and SpecialFolder lookup on the production path; the smoke test drives the pure parts.

using System.Text;

namespace Nota.Infrastructure;

public static class UserDirs
{
    /// <summary>Base folder user projects live in (without "Nota Projects").</summary>
    public static string GetDocumentsDir()
        => GetKnownDir("DOCUMENTS", "Documents", Environment.SpecialFolder.MyDocuments);

    /// <summary>Base folder the sample library lives in (without "Nota Samples").</summary>
    public static string GetMusicDir()
        => GetKnownDir("MUSIC", "Music", Environment.SpecialFolder.MyMusic);

    internal static string GetKnownDir(string xdgName, string fallbackName, Environment.SpecialFolder folder)
    {
        string home = NotaPaths.Home;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            try
            {
                string known = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(known)) return known;
            }
            catch { /* best-effort: fall through to ~/fallback */ }
            return Path.Combine(home, fallbackName);
        }

        string? content = TryReadXdgFile(home);
        return ResolveBaseDir(xdgName, fallbackName, home, content, specialFolderPath: null, useXdg: true);
    }

    internal static string ResolveBaseDir(string xdgName, string fallbackName, string home, string? fileContent, string? specialFolderPath, bool useXdg)
    {
        if (!useXdg)
            return !string.IsNullOrWhiteSpace(specialFolderPath) ? specialFolderPath : Path.Combine(home, fallbackName);
        string? parsed = ParseXdgDir(fileContent, xdgName, home);
        return !string.IsNullOrWhiteSpace(parsed) ? parsed : Path.Combine(home, fallbackName);
    }

    internal static string GetXdgFilePath(string home, string? xdgConfigHome)
        => Path.Combine(string.IsNullOrEmpty(xdgConfigHome) ? Path.Combine(home, ".config") : xdgConfigHome, "user-dirs.dirs");

    internal static string? TryReadXdgFile(string home)
    {
        try
        {
            string path = GetXdgFilePath(home, Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch { return null; }
    }

    internal static string? ParseXdgDir(string? fileContent, string key, string home)
    {
        if (string.IsNullOrEmpty(fileContent) || string.IsNullOrEmpty(home)) return null;
        string prefix = "XDG_" + key + "_DIR=";
        foreach (var rawLine in fileContent.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string value = line[prefix.Length..].Trim();
            if (value.Length == 0) return null;
            if (value[0] == '"')
                // Quoted: ends at the first *unescaped* quote; \" \\ \$ \` decode
                // to their literal char (shell double-quote rules, the escaping
                // xdg-user-dirs-update emits), so My\"Docs keeps its quote.
                value = Unquote(value[1..]);
            else
            {
                int hash = value.IndexOf('#');
                if (hash >= 0) value = value[..hash];
                value = value.Trim().Trim('"', '\'');
            }
            if (value.Length == 0) return null;
            // Disabled per the spec ($HOME alone): keep everything under $HOME so a
            // disabled Documents dir is not recreated.
            string trimmed = value.TrimEnd('/', '\\');
            if (trimmed is "$HOME" || trimmed.Equals(home.TrimEnd('/', '\\'), StringComparison.Ordinal))
                return home;
            if (value.StartsWith("$HOME/", StringComparison.Ordinal))
                return Path.Combine(home, value["$HOME/".Length..]);
            if (value.StartsWith("$HOME", StringComparison.Ordinal))
                return home + value["$HOME".Length..];
            if (Path.IsPathRooted(value)) return value;
            return Path.Combine(home, value);
        }
        return null;
    }

    internal static string Unquote(string rest)
    {
        var sb = new StringBuilder(rest.Length);
        for (int i = 0; i < rest.Length; i++)
        {
            char c = rest[i];
            if (c == '"') break;
            if (c == '\\' && i + 1 < rest.Length && rest[i + 1] is '$' or '`' or '"' or '\\')
            {
                sb.Append(rest[i + 1]);
                i++;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
