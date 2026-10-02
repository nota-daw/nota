// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Self-update (IAppUpdater): version compare, picking this platform's asset from a GitHub
// release, what counts as an updatable install, download + checksum against a local release
// JSON, and the install helpers themselves — the Linux AppImage swap and the macOS bundle swap
// run for real on fake installs (plus, on macOS, staging Nota.app out of a real .dmg).
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --updater`.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class AppUpdaterTests
{
    private static readonly string[] AssetNames =
    [
        "Nota-1.3.0-arm64.dmg", "Nota-1.3.0-x86_64.dmg",
        "Nota-Setup-1.3.0-x64.exe", "Nota-Setup-1.3.0-arm64.exe",
        "Nota-1.3.0-x86_64.AppImage", "Nota-1.3.0-aarch64.AppImage",
    ];

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        // ---- versions -------------------------------------------------------------------
        yield return (AppUpdater.IsNewer("v1.3.0", "1.2.9") && AppUpdater.IsNewer("1.10.0", "1.9.9"),
            "a higher tag (with or without the v) is newer; components compare numerically");
        yield return (!AppUpdater.IsNewer("1.2.0", "1.2.0") && !AppUpdater.IsNewer("1.1.9", "1.2.0"),
            "the same or an older release is not an update");
        yield return (!AppUpdater.IsNewer("nightly", "1.2.0") && !AppUpdater.IsNewer("1.3", "1.2.0"),
            "unparsable tags are ignored");

        // ---- assets ---------------------------------------------------------------------
        var assets = AssetNames.Select(n => new AppReleaseAsset(n, "https://x/" + n, 1, null)).ToList();
        string? Pick(OSPlatform os, Architecture a) => AppUpdater.PickAsset(assets, os, a)?.Name;
        yield return (Pick(OSPlatform.OSX, Architecture.Arm64) == "Nota-1.3.0-arm64.dmg"
                      && Pick(OSPlatform.OSX, Architecture.X64) == "Nota-1.3.0-x86_64.dmg",
            "macOS takes the .dmg for its architecture");
        yield return (Pick(OSPlatform.Windows, Architecture.X64) == "Nota-Setup-1.3.0-x64.exe"
                      && Pick(OSPlatform.Windows, Architecture.Arm64) == "Nota-Setup-1.3.0-arm64.exe",
            "Windows takes the installer for its architecture");
        yield return (Pick(OSPlatform.Linux, Architecture.X64) == "Nota-1.3.0-x86_64.AppImage"
                      && Pick(OSPlatform.Linux, Architecture.Arm64) == "Nota-1.3.0-aarch64.AppImage",
            "Linux takes the AppImage for its architecture");

        var json = ReleaseJson("v1.3.0", [("Nota-1.3.0-arm64.dmg", "https://x/a.dmg", 42, "sha256:ABCDEF")]);
        var rel = AppUpdater.ParseRelease(json, "1.2.0", OSPlatform.OSX, Architecture.Arm64);
        yield return (rel is { Version: "1.3.0", PageUrl: "https://github.com/nota-daw/nota/releases/tag/v1.3.0",
                               Asset: { Size: 42, Sha256: "abcdef", Url: "https://x/a.dmg" } },
            "a release parses to its version, page, and this platform's asset with its digest");
        yield return (AppUpdater.ParseRelease(json, "1.3.0", OSPlatform.OSX, Architecture.Arm64) is null,
            "…and is no update for a client already on it");
        yield return (AppUpdater.ParseRelease(json, "1.2.0", OSPlatform.Linux, Architecture.X64) is { Asset: null },
            "a release without this platform's build still reports the version (manual download)");

        var tmp = Directory.CreateTempSubdirectory("nota-updater-test-").FullName;
        try
        {
            foreach (var r in Targets(tmp)) yield return r;
            foreach (var r in LinuxFlow(tmp)) yield return r;
            if (!OperatingSystem.IsWindows())
                foreach (var r in MacSwap(tmp)) yield return r;
            if (OperatingSystem.IsMacOS())
                foreach (var r in MacFlow(tmp)) yield return r;
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    // ---- what is updatable ---------------------------------------------------------------
    private static IEnumerable<(bool, string)> Targets(string tmp)
    {
        var bundle = Path.Combine(tmp, "Apps", "Nota.app");
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
        var exe = Path.Combine(bundle, "Contents", "MacOS", "Nota.App");
        yield return (AppUpdater.InstallTarget(OSPlatform.OSX, exe, null) == bundle,
            "macOS: the .app bundle the process runs from is the target");
        yield return (AppUpdater.InstallTarget(OSPlatform.OSX, "/private/var/folders/x/AppTranslocation/ABC/d/Nota.app/Contents/MacOS/Nota.App", null) is null
                      && AppUpdater.InstallTarget(OSPlatform.OSX, "/Volumes/Nota/Nota.app/Contents/MacOS/Nota.App", null) is null,
            "…but not a translocated copy or one running off the disk image");
        yield return (AppUpdater.InstallTarget(OSPlatform.OSX, Path.Combine(tmp, "bin", "Nota.App"), null) is null,
            "…nor a dev build outside a bundle");

        var win = Path.Combine(tmp, "Program Files", "Nota");
        Directory.CreateDirectory(win);
        var winExe = Path.Combine(win, "Nota.App.exe");
        yield return (AppUpdater.InstallTarget(OSPlatform.Windows, winExe, null) is null,
            "Windows: a folder without the Inno uninstaller isn't an installed copy");
        File.WriteAllText(Path.Combine(win, "unins000.exe"), "");
        yield return (AppUpdater.InstallTarget(OSPlatform.Windows, winExe, null) == win,
            "…with it, the install folder is the target");

        var appImage = Path.Combine(tmp, "Nota.AppImage");
        File.WriteAllText(appImage, "");
        yield return (AppUpdater.InstallTarget(OSPlatform.Linux, "/tmp/.mount_x/usr/bin/Nota.App", appImage) == appImage
                      && AppUpdater.InstallTarget(OSPlatform.Linux, "/usr/bin/Nota.App", null) is null,
            "Linux: only an AppImage ($APPIMAGE) updates itself");
    }

    // ---- download + verify, then the AppImage swap ------------------------------------------
    private static IEnumerable<(bool, string)> LinuxFlow(string tmp)
    {
        var dir = Path.Combine(tmp, "linux");
        Directory.CreateDirectory(dir);
        var installed = Path.Combine(dir, "Nota.AppImage");
        File.WriteAllText(installed, "old build");
        var newBuild = Path.Combine(dir, "Nota-1.3.0-x86_64.AppImage");
        File.WriteAllText(newBuild, "new build 1.3.0");
        var bytes = File.ReadAllBytes(newBuild);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var api = Path.Combine(dir, "latest.json");
        File.WriteAllText(api, ReleaseJson("v1.3.0", [(Path.GetFileName(newBuild), newBuild, bytes.Length, "sha256:" + sha)]));
        var updates = Path.Combine(dir, "updates");
        var updater = new AppUpdater("1.2.0", api, updates, OSPlatform.Linux, Architecture.X64, "/tmp/.mount/usr/bin/Nota.App", installed);

        yield return (updater.CanInstallInPlace, "an AppImage in a writable folder can update in place");
        var release = updater.CheckAsync().GetAwaiter().GetResult();
        yield return (release is { Version: "1.3.0", Asset: not null }, "the check finds 1.3.0 and its AppImage");
        if (release is null) yield break;

        var fractions = new List<double>();
        updater.DownloadAsync(release, new SyncProgress(p => fractions.Add(p.Fraction))).GetAwaiter().GetResult();
        yield return (updater.PendingVersion == "1.3.0" && fractions.Count > 0 && fractions[^1] == 1.0,
            "the download reports progress to 100% and leaves 1.3.0 pending");
        yield return (File.ReadAllText(installed) == "old build", "…without touching the running install");

        var bad = release with { Asset = release.Asset! with { Sha256 = new string('0', 64) } };
        var err = Throws(() => updater.DownloadAsync(bad));
        yield return (err is StoreException && updater.PendingVersion is null
                      && !Directory.EnumerateFiles(updates, "*.AppImage").Any(),
            "a checksum mismatch is refused, deleted, and nothing stays pending");
        var tooBig = release with { Asset = release.Asset! with { Size = bytes.Length - 1, Sha256 = null } };
        yield return (Throws(() => updater.DownloadAsync(tooBig)) is StoreException,
            "…as is a download larger than the release says");

        if (OperatingSystem.IsWindows()) yield break;
        updater.DownloadAsync(release).GetAwaiter().GetResult();
        var staged = Directory.EnumerateFiles(updates, "*.AppImage").Single();
        var code = RunScript(AppUpdater.LinuxScript, updates, staged, installed);
        yield return (code == 0 && File.ReadAllText(installed) == "new build 1.3.0" && !File.Exists(staged),
            "the install helper swaps the new AppImage in once the app has exited");
        yield return ((File.GetUnixFileMode(installed) & UnixFileMode.UserExecute) != 0,
            "…and leaves it executable");
    }

    // ---- the macOS bundle swap (plain shell, runs on any Unix) ------------------------------
    private static IEnumerable<(bool, string)> MacSwap(string tmp)
    {
        var dir = Path.Combine(tmp, "macswap");
        var target = Path.Combine(dir, "Applications", "Nota.app");
        var staged = Path.Combine(dir, "updates", "Nota-1.3.0.app");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(staged);
        File.WriteAllText(Path.Combine(target, "version"), "old");
        File.WriteAllText(Path.Combine(staged, "version"), "new");
        var code = RunScript(AppUpdater.MacScript, Path.GetDirectoryName(staged)!, staged, target);
        yield return (code == 0 && File.ReadAllText(Path.Combine(target, "version")) == "new"
                      && !Directory.Exists(staged) && !Directory.Exists(target + ".updating-old"),
            "the macOS helper replaces the bundle and removes the old copy");

        var missing = Path.Combine(dir, "updates", "gone.app");
        code = RunScript(AppUpdater.MacScript, Path.GetDirectoryName(staged)!, missing, target);
        yield return (code != 0 && File.ReadAllText(Path.Combine(target, "version")) == "new",
            "…and rolls back, keeping the installed app, when the new bundle can't be moved in");
    }

    // ---- macOS: stage Nota.app out of a real disk image ---------------------------------------
    private static IEnumerable<(bool, string)> MacFlow(string tmp)
    {
        var dir = Path.Combine(tmp, "mac");
        var installed = Path.Combine(dir, "Applications", "Nota.app");
        Directory.CreateDirectory(Path.Combine(installed, "Contents", "MacOS"));
        var src = Path.Combine(dir, "dmgsrc");
        Directory.CreateDirectory(Path.Combine(src, "Nota.app", "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(src, "Nota.app", "Contents", "Info.plist"), """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>CFBundleShortVersionString</key> <string>1.3.0</string>
            </dict></plist>
            """);
        File.WriteAllText(Path.Combine(src, "Nota.app", "Contents", "MacOS", "Nota.App"), "new binary");
        var dmg = Path.Combine(dir, "Nota-1.3.0-arm64.dmg");
        var made = Process.Start(new ProcessStartInfo("hdiutil", ["create", "-quiet", "-volname", "Nota", "-srcfolder", src, "-format", "UDZO", dmg])
                                 { RedirectStandardOutput = true, RedirectStandardError = true })!;
        made.WaitForExit();
        yield return (made.ExitCode == 0 && File.Exists(dmg), "(setup) built a test disk image");
        if (made.ExitCode != 0) yield break;

        var bytes = File.ReadAllBytes(dmg);
        var api = Path.Combine(dir, "latest.json");
        File.WriteAllText(api, ReleaseJson("v1.3.0", [
            ("Nota-1.3.0-arm64.dmg", dmg, bytes.Length, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes))),
            ("Nota-1.3.0-x86_64.dmg", dmg, bytes.Length, null)]));
        var updates = Path.Combine(dir, "updates");
        var updater = new AppUpdater("1.2.0", api, updates, OSPlatform.OSX, Architecture.Arm64, Path.Combine(installed, "Contents", "MacOS", "Nota.App"), null);
        var release = updater.CheckAsync().GetAwaiter().GetResult();
        yield return (updater.CanInstallInPlace && release is { Asset: not null }, "macOS: a bundle in a writable folder can update in place");
        if (release is null) yield break;

        updater.DownloadAsync(release).GetAwaiter().GetResult();
        var staged = Path.Combine(updates, "Nota-1.3.0.app");
        yield return (updater.PendingVersion == "1.3.0"
                      && File.ReadAllText(Path.Combine(staged, "Contents", "MacOS", "Nota.App")) == "new binary"
                      && !Directory.EnumerateFiles(updates, "*.dmg").Any(),
            "the .dmg is mounted, its Nota.app copied out and the image deleted");

        var wrong = new AppUpdater("1.2.0", api, updates, OSPlatform.OSX, Architecture.Arm64, Path.Combine(installed, "Contents", "MacOS", "Nota.App"), null);
        yield return (Throws(() => wrong.DownloadAsync(release with { Version = "1.4.0" })) is StoreException,
            "a bundle whose version isn't the release's is refused");
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static string ReleaseJson(string tag, IEnumerable<(string Name, string Url, long Size, string? Digest)> assets)
        => JsonSerializer.Serialize(new
        {
            tag_name = tag,
            html_url = "https://github.com/nota-daw/nota/releases/tag/" + tag,
            assets = assets.Select(a => new { name = a.Name, browser_download_url = a.Url, size = a.Size, digest = a.Digest }),
        });

    // Run an install helper against an already-dead pid, as if the app had just quit.
    private static int RunScript(string script, string dir, string staged, string target)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test-install.sh");
        File.WriteAllText(path, script);
        using var gone = Process.Start(new ProcessStartInfo("/bin/sh", ["-c", "exit 0"]))!;
        gone.WaitForExit();
        using var p = Process.Start(new ProcessStartInfo("/bin/sh", [path, gone.Id.ToString(), staged, target, "0"])
                                    { RedirectStandardError = true })!;
        p.WaitForExit(30_000);
        return p.ExitCode;
    }

    private static Exception? Throws(Func<Task> f)
    {
        try { f().GetAwaiter().GetResult(); return null; }
        catch (Exception e) { return e; }
    }

    private sealed class SyncProgress(Action<StoreProgress> on) : IProgress<StoreProgress>
    {
        public void Report(StoreProgress value) => on(value);
    }
}
