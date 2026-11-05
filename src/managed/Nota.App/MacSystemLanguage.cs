// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// macOS draws some text itself — the standard items of the application menu (Hide, Show All,
// Quit, Services) and the open / save panels — in the language it runs the app in, which it
// takes from the OS unless the app says otherwise. Nota says otherwise: when the user has chosen
// an interface language, the app's own language list (AppleLanguages in its preferences domain)
// is set to it, so those texts follow the choice. macOS reads the list when the app starts, which
// is when the language change applies anyway. The Info.plist lists the languages Nota is
// translated into (CFBundleLocalizations); without that macOS keeps English for them.
// Only the .app bundle is touched; a development run (`dotnet run`) leaves the OS alone.

using System;
using System.Diagnostics;
using System.Linq;

namespace Nota.App;

internal static class MacSystemLanguage
{
    private const string Domain = "com.nota.daw";   // CFBundleIdentifier, scripts/bundle-mac.sh

    /// <summary>Make the system's own text follow <paramref name="code"/> from the next start.
    /// Best-effort: a failure leaves the system text in the OS language.</summary>
    public static void Set(string code)
    {
        if (!OperatingSystem.IsMacOS() || !AppRestart.CanRelaunch) return;
        try
        {
            if (Read().SequenceEqual(new[] { code })) return;
            Run("write", Domain, "AppleLanguages", "-array", code);
        }
        catch { /* best-effort */ }
    }

    private static string[] Read()
    {
        var text = Run("read", Domain, "AppleLanguages");
        // "(\n    fr\n)" — one language per line between the parentheses.
        return text.Split('\n').Select(l => l.Trim().Trim(',', '"')).Where(l => l.Length > 0 && l is not ("(" or ")")).ToArray();
    }

    private static string Run(params string[] args)
    {
        var start = new ProcessStartInfo("/usr/bin/defaults") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var p = Process.Start(start)!;
        string output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } }
        return output;
    }
}
