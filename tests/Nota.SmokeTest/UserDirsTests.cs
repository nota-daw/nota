// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// OS user-folder defaults (#522): the XDG parser and the resolver fallbacks. Pure.
// Expected paths are built with Path.Combine so the checks hold on every OS.
// Runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --user-dirs`.

using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class UserDirsTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        const string home = "/home/u";

        var quoted = UserDirs.ParseXdgDir("XDG_DOCUMENTS_DIR=\"$HOME/Dokumenty\"\n", "DOCUMENTS", home);
        yield return (quoted == Path.Combine(home, "Dokumenty"), "userdirs: quoted $HOME/Documents parses with a translated name");

        var spaced = UserDirs.ParseXdgDir("XDG_MUSIC_DIR=\"/media/dane/Muzyka moja\"\n", "MUSIC", home);
        yield return (spaced == "/media/dane/Muzyka moja", "userdirs: an absolute path with spaces survives");

        var escapedQuote = UserDirs.ParseXdgDir("XDG_DOCUMENTS_DIR=\"$HOME/My\\\"Docs\"\n", "DOCUMENTS", home);
        yield return (escapedQuote == Path.Combine(home, "My\"Docs"), "userdirs: an escaped quote inside quotes survives");

        var escapedBackslash = UserDirs.ParseXdgDir("XDG_MUSIC_DIR=\"$HOME/a\\\\b\"\n", "MUSIC", home);
        yield return (escapedBackslash == Path.Combine(home, "a\\b"), "userdirs: an escaped backslash decodes to one");

        var missing = UserDirs.ParseXdgDir("# comment\n\nXDG_DOWNLOAD_DIR=\"$HOME/Pobrane\"\n", "DOCUMENTS", home);
        yield return (missing is null, "userdirs: a missing key parses to null (fallback applies)");

        var disabled = UserDirs.ParseXdgDir("XDG_DOCUMENTS_DIR=\"$HOME\"\n", "DOCUMENTS", home);
        yield return (disabled == home, "userdirs: a disabled dir ($HOME) resolves to home, not ~/Documents");

        var disabledSlash = UserDirs.ParseXdgDir("XDG_DOCUMENTS_DIR=\"$HOME/\"\n", "DOCUMENTS", home);
        yield return (disabledSlash == home, "userdirs: a disabled dir ($HOME/) resolves to home too");

        var unquoted = UserDirs.ParseXdgDir("XDG_DOCUMENTS_DIR=$HOME/Docs\n", "DOCUMENTS", home);
        yield return (unquoted == Path.Combine(home, "Docs"), "userdirs: an unquoted $HOME value still expands");

        var hit = UserDirs.ResolveBaseDir("DOCUMENTS", "Documents", home, "XDG_DOCUMENTS_DIR=\"$HOME/Dokumenty\"\n", null, useXdg: true);
        yield return (hit == Path.Combine(home, "Dokumenty"), "userdirs: linux hit uses the XDG dir");

        var fallback = UserDirs.ResolveBaseDir("DOCUMENTS", "Documents", home, null, null, useXdg: true);
        yield return (fallback == Path.Combine(home, "Documents"), "userdirs: missing user-dirs falls back to ~/Documents");

        var disabledBase = UserDirs.ResolveBaseDir("DOCUMENTS", "Documents", home, "XDG_DOCUMENTS_DIR=\"$HOME\"\n", null, useXdg: true);
        yield return (disabledBase == home, "userdirs: disabled XDG dir falls back to home, not ~/Documents");

        var known = UserDirs.ResolveBaseDir("DOCUMENTS", "Documents", home, null, @"D:\Docs", useXdg: false);
        yield return (known == @"D:\Docs", "userdirs: redirected Known Folder wins on Windows/macOS");

        var knownFallback = UserDirs.ResolveBaseDir("MUSIC", "Music", home, null, "  ", useXdg: false);
        yield return (knownFallback == Path.Combine(home, "Music"), "userdirs: empty Known Folder falls back to ~/Music");

        var cfgDefault = UserDirs.GetXdgFilePath(home, null);
        yield return (cfgDefault == Path.Combine(home, ".config", "user-dirs.dirs"), "userdirs: default user-dirs path is ~/.config/user-dirs.dirs");

        var cfgOverride = UserDirs.GetXdgFilePath(home, "/tmp/xdg");
        yield return (cfgOverride == Path.Combine("/tmp/xdg", "user-dirs.dirs"), "userdirs: XDG_CONFIG_HOME relocates user-dirs.dirs");
    }
}
