// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Single source of truth for Nota's per-user data directory, shared by the
// settings, recovery and logging services (and mirrored by the native engine's
// audio.json / midi.json writers). Platform conventions:
//   macOS   -> ~/Library/Application Support/Nota   (unchanged from the mac-only era)
//   Windows -> %APPDATA%\Nota                       (Roaming AppData)
//   Linux   -> $XDG_CONFIG_HOME/nota or ~/.config/nota
// The native side resolves the same locations (SHGetKnownFolderPath / NSHomeDirectory)
// so config written by C++ and C# lands in one place.

namespace Nota.Infrastructure;

public static class NotaPaths
{
    /// <summary>The per-user Nota data root, created if missing.</summary>
    public static string DataDir { get; } = ResolveDataDir();

    /// <summary>User home directory.</summary>
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Ensures and returns a subfolder of <see cref="DataDir"/> (e.g. "logs", "presets").</summary>
    public static string SubDir(string name)
    {
        var dir = Path.Combine(DataDir, name);
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
        return dir;
    }

    private static string ResolveDataDir()
    {
        string root;
        if (OperatingSystem.IsWindows())
        {
            // %APPDATA% == Roaming AppData; on Windows SpecialFolder.ApplicationData maps here.
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nota");
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Preserve the exact mac-only path so existing installs keep their data.
            root = Path.Combine(Home, "Library", "Application Support", "Nota");
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDir = string.IsNullOrEmpty(xdg) ? Path.Combine(Home, ".config") : xdg;
            root = Path.Combine(baseDir, "nota");
        }

        try { Directory.CreateDirectory(root); } catch { /* best-effort */ }
        return root;
    }
}
