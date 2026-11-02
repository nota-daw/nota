// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Restart Nota after a language change (spec 068, research R5). The language is fixed for a
// run, so a change applies at the next start: Preferences asks for a restart, the main window
// closes by its usual path (which asks about unsaved changes), and when the app exits a small
// detached helper waits for this process to be gone and starts Nota again. Nothing is started
// while the old process still holds the engine, the MCP port or the data folder.
//
// Cancelling the unsaved-changes prompt cancels the restart (MainWindow.Recovery). Where there
// is nothing to start again — a `dotnet run` session — CanRelaunch is false and the caller
// tells the user to reopen Nota by hand. Relaunching is best-effort: if the helper can't be
// started, the app has still quit cleanly and the user reopens it.

using System;
using System.Diagnostics;
using System.IO;

namespace Nota.App;

internal static class AppRestart
{
    private static volatile bool _pending;

    /// <summary>A restart was asked for and the app has not exited yet.</summary>
    public static bool Pending => _pending;

    /// <summary>Whether there is something to start again: the .app bundle on macOS, the
    /// AppImage or the executable elsewhere.</summary>
    public static bool CanRelaunch => Target() is not null;

    /// <summary>Start Nota again once the app has exited.</summary>
    public static void Request() => _pending = true;

    /// <summary>The restart is off (the unsaved-changes prompt was cancelled).</summary>
    public static void Cancel() => _pending = false;

    /// <summary>What to start again, or null if it can't be told. A development run goes
    /// through the dotnet host, whose command line this can't safely repeat.</summary>
    internal static string? Target()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return null;
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return null;
        if (OperatingSystem.IsMacOS())
        {
            for (var d = new DirectoryInfo(Path.GetDirectoryName(exe)!); d is not null; d = d.Parent)
                if (d.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return d.FullName;
            return null;
        }
        // The AppImage runs from a mounted image; the file the user started is what to run again.
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image && File.Exists(image))
            return image;
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>Called when the app exits: if a restart is pending, hand over to the helper.</summary>
    public static void RunPending()
    {
        if (!_pending) return;
        _pending = false;
        if (Target() is not { } target) return;
        try
        {
            int pid = Environment.ProcessId;
            var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
            if (OperatingSystem.IsWindows())
            {
                start.FileName = "powershell.exe";
                foreach (var a in new[] { "-NoProfile", "-WindowStyle", "Hidden", "-Command",
                    $"Wait-Process -Id {pid} -ErrorAction SilentlyContinue; Start-Process -FilePath '{target.Replace("'", "''")}'" })
                    start.ArgumentList.Add(a);
            }
            else
            {
                // The pid and the target travel as arguments ($1, $2), never inside the script text.
                start.FileName = "/bin/sh";
                start.ArgumentList.Add("-c");
                start.ArgumentList.Add("while ps -p \"$1\" >/dev/null 2>&1; do sleep 0.2; done; "
                    + (OperatingSystem.IsMacOS() ? "exec open -n \"$2\"" : "exec \"$2\""));
                start.ArgumentList.Add("nota-restart");
                start.ArgumentList.Add(pid.ToString());
                start.ArgumentList.Add(target);
            }
            Process.Start(start);
        }
        catch { /* best-effort: the app has quit; the user can open it again */ }
    }
}
