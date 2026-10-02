// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Self-update from GitHub releases. The check asks the API for the latest published release
// (drafts and pre-releases are excluded by that endpoint) and picks the asset the release
// workflow builds for this OS + arch. The download is verified against the asset's size and
// the sha256 digest GitHub reports for it, then prepared under <data>/updates:
//   macOS   — the .dmg is mounted and its Nota.app copied out (ditto), version-checked;
//   Windows — the Inno Setup installer is kept as is;
//   Linux   — the AppImage is kept as is.
// Nothing touches the installed app while Nota runs. On exit, RunPendingInstall writes a tiny
// helper script and starts it detached: it waits for this process to end, swaps the new
// build in (or runs the installer silently) and relaunches when asked.
//
// NOTA_UPDATE_API overrides the release endpoint (a URL or a local JSON file, for testing);
// asset URLs may be local paths as well.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nota.Application;

namespace Nota.Infrastructure;

public sealed partial class AppUpdater : IAppUpdater
{
    public const string ReleasesPage = "https://github.com/nota-daw/nota/releases";
    private const string LatestApi = "https://api.github.com/repos/nota-daw/nota/releases/latest";

    /// <summary>How the prepared build gets installed.</summary>
    public enum InstallKind { MacBundle, WindowsInstaller, AppImage }

    private sealed record Pending(InstallKind Kind, string Staged, string Target, string Version);

    private readonly string _running;
    private readonly string _api;
    private readonly string _updatesDir;
    private readonly HttpClient _http;
    private readonly OSPlatform _os;
    private readonly Architecture _arch;
    private readonly string? _target;
    private Pending? _pending;

    public AppUpdater(string runningVersion) : this(runningVersion,
        Environment.GetEnvironmentVariable("NOTA_UPDATE_API") is { Length: > 0 } api ? api : LatestApi,
        NotaPaths.SubDir("updates"), CurrentOs(), RuntimeInformation.OSArchitecture,
        Environment.ProcessPath, Environment.GetEnvironmentVariable("APPIMAGE"))
    { }

    /// <summary>Test seam: every platform input explicit.</summary>
    public AppUpdater(string runningVersion, string api, string updatesDir, OSPlatform os, Architecture arch,
                      string? processPath, string? appImage, HttpMessageHandler? handler = null)
    {
        _running = runningVersion;
        _api = api;
        _updatesDir = updatesDir;
        _os = os;
        _arch = arch;
        _target = InstallTarget(os, processPath, appImage);
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Timeout.InfiniteTimeSpan;   // big download; each request has its own token
        // GitHub's API rejects requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Nota/{runningVersion}");
    }

    /// <summary>The installed app this process would replace, or null when it can't.</summary>
    public string? Target => _target;

    public bool CanInstallInPlace => _target is not null && (_os == OSPlatform.Windows || IsWritable(Path.GetDirectoryName(_target)!));

    public string? PendingVersion => _pending?.Version;

    public bool RelaunchAfterInstall { get; set; }

    // ---- check ------------------------------------------------------------------------------

    public async Task<AppRelease?> CheckAsync(CancellationToken ct = default)
    {
        if (_pending is null) CleanUpdatesDir();
        try
        {
            string json;
            if (RegistryClient.IsLocal(_api, out var local))
                json = await File.ReadAllTextAsync(local, ct).ConfigureAwait(false);
            else
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                using var req = new HttpRequestMessage(HttpMethod.Get, _api);
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            }
            return ParseRelease(json, _running, _os, _arch);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A GitHub "release" JSON → the release if it's newer than <paramref name="running"/>,
    /// with the asset for <paramref name="os"/> + <paramref name="arch"/> (if it ships one).</summary>
    public static AppRelease? ParseRelease(string json, string running, OSPlatform os, Architecture arch)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var latest = tag?.Trim().TrimStart('v', 'V');
        if (!IsNewer(latest, running)) return null;
        var page = root.TryGetProperty("html_url", out var u) && u.GetString() is { Length: > 0 } url ? url : ReleasesPage;

        var assets = new List<AppReleaseAsset>();
        if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var a in arr.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                var dl = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                string? sha = null;
                if (a.TryGetProperty("digest", out var dg) && dg.GetString() is { } digest
                    && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = digest[7..].ToLowerInvariant();
                if (name is { Length: > 0 } && dl is { Length: > 0 } && size > 0)
                    assets.Add(new AppReleaseAsset(name, dl, size, sha));
            }
        return new AppRelease(latest!, page, PickAsset(assets, os, arch));
    }

    /// <summary>True when <paramref name="latest"/> is a higher MAJOR.MINOR.PATCH than <paramref name="running"/>.</summary>
    public static bool IsNewer(string? latest, string? running)
        => TryParseVersion(latest, out var l) && TryParseVersion(running, out var r) && l > r;

    private static bool TryParseVersion(string? s, out Version v)
    {
        v = new Version();
        if (string.IsNullOrWhiteSpace(s)) return false;
        var core = s.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        var parts = core.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var a) || !int.TryParse(parts[1], out var b) || !int.TryParse(parts[2], out var c)) return false;
        v = new Version(a, b, c);
        return true;
    }

    /// <summary>The asset the release workflow names for this platform (see release.yml):
    /// Nota-X-arm64.dmg / Nota-X-x86_64.dmg, Nota-Setup-X-x64.exe / -arm64.exe,
    /// Nota-X-x86_64.AppImage / Nota-X-aarch64.AppImage.</summary>
    public static AppReleaseAsset? PickAsset(IEnumerable<AppReleaseAsset> assets, OSPlatform os, Architecture arch)
    {
        bool arm = arch == Architecture.Arm64;
        Func<string, bool>? match = null;
        if (os == OSPlatform.OSX)
            match = n => n.EndsWith(arm ? "-arm64.dmg" : "-x86_64.dmg", StringComparison.OrdinalIgnoreCase);
        else if (os == OSPlatform.Windows)
            match = n => n.StartsWith("Nota-Setup-", StringComparison.OrdinalIgnoreCase)
                         && n.EndsWith(arm ? "-arm64.exe" : "-x64.exe", StringComparison.OrdinalIgnoreCase);
        else if (os == OSPlatform.Linux)
            match = n => n.EndsWith(arm ? "-aarch64.AppImage" : "-x86_64.AppImage", StringComparison.OrdinalIgnoreCase);
        return match is null ? null : assets.FirstOrDefault(a => match(a.Name));
    }

    /// <summary>What an in-place update replaces: the .app bundle the process runs from (not a
    /// translocated or disk-image copy), the folder of an Inno Setup install, or the AppImage.
    /// Null for dev builds and anything else.</summary>
    public static string? InstallTarget(OSPlatform os, string? processPath, string? appImage)
    {
        if (os == OSPlatform.Linux)
            return appImage is { Length: > 0 } && File.Exists(appImage) ? appImage : null;
        if (string.IsNullOrEmpty(processPath)) return null;
        if (os == OSPlatform.OSX)
        {
            const string marker = ".app/Contents/MacOS/";
            int i = processPath.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return null;
            var bundle = processPath[..(i + 4)];
            if (bundle.Contains("/AppTranslocation/", StringComparison.Ordinal)
                || bundle.StartsWith("/Volumes/", StringComparison.Ordinal)) return null;
            return Directory.Exists(bundle) ? bundle : null;
        }
        if (os == OSPlatform.Windows)
        {
            var dir = Path.GetDirectoryName(processPath);
            return dir is not null && File.Exists(Path.Combine(dir, "unins000.exe")) ? dir : null;
        }
        return null;
    }

    // ---- download + prepare -----------------------------------------------------------------

    public async Task DownloadAsync(AppRelease release, IProgress<StoreProgress>? progress = null, CancellationToken ct = default)
    {
        var asset = release.Asset ?? throw new StoreException($"Nota {release.Version} has no download for this computer.");
        if (!CanInstallInPlace) throw new StoreException("This copy of Nota can't update itself — download the new version from the release page.");
        _pending = null;
        CleanUpdatesDir();
        Directory.CreateDirectory(_updatesDir);

        var file = Path.Combine(_updatesDir, Path.GetFileName(asset.Name));
        await DownloadVerifiedAsync(asset, file, $"Downloading Nota {release.Version}…", progress, ct).ConfigureAwait(false);

        switch (_os == OSPlatform.OSX ? InstallKind.MacBundle : _os == OSPlatform.Windows ? InstallKind.WindowsInstaller : InstallKind.AppImage)
        {
            case InstallKind.MacBundle:
                progress?.Report(new StoreProgress(-1, "Preparing the update…"));
                var staged = Path.Combine(_updatesDir, $"Nota-{release.Version}.app");
                await Task.Run(() => StageMacBundle(file, staged, release.Version), ct).ConfigureAwait(false);
                TryDelete(file);
                _pending = new Pending(InstallKind.MacBundle, staged, _target!, release.Version);
                break;
            case InstallKind.WindowsInstaller:
                _pending = new Pending(InstallKind.WindowsInstaller, file, _target!, release.Version);
                break;
            default:
                _pending = new Pending(InstallKind.AppImage, file, _target!, release.Version);
                break;
        }
    }

    private async Task DownloadVerifiedAsync(AppReleaseAsset asset, string file, string label,
                                             IProgress<StoreProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new StoreProgress(0, label));
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        try
        {
            Stream src;
            HttpResponseMessage? resp = null;
            if (RegistryClient.IsLocal(asset.Url, out var localPath)) src = File.OpenRead(localPath);
            else
            {
                resp = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            }
            using (resp)
            await using (src)
            await using (var dst = File.Create(file))
            {
                var buf = new byte[1 << 16];
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > asset.Size) throw new StoreException("The update download is larger than expected — not installing it.");
                    sha.AppendData(buf, 0, n);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    progress?.Report(new StoreProgress((double)total / asset.Size, label));
                }
            }
        }
        catch (HttpRequestException e)
        {
            TryDelete(file);
            throw new StoreException($"Couldn't download the update: {e.Message}", e);
        }
        catch
        {
            TryDelete(file);
            throw;
        }

        var digest = Convert.ToHexStringLower(sha.GetHashAndReset());
        if (total != asset.Size || asset.Sha256 is not null && digest != asset.Sha256)
        {
            TryDelete(file);
            throw new StoreException("The update download is damaged (checksum mismatch) — not installing it.");
        }
    }

    // Mount the disk image, copy its .app out and check it's the version we asked for.
    private void StageMacBundle(string dmg, string staged, string version)
    {
        // A fresh mount point each time, so a mount left behind by a crash can't get in the way.
        var mount = Directory.CreateTempSubdirectory("nota-update-").FullName;
        RunTool("hdiutil", "attach", "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mount, dmg);
        try
        {
            var app = Directory.EnumerateDirectories(mount, "*.app").FirstOrDefault()
                      ?? throw new StoreException("The update disk image doesn't contain Nota.app.");
            RunTool("ditto", app, staged);
        }
        finally
        {
            try { RunTool("hdiutil", "detach", mount, "-force"); Directory.Delete(mount); } catch { /* best-effort */ }
        }
        var plist = Path.Combine(staged, "Contents", "Info.plist");
        var m = File.Exists(plist) ? BundleVersion().Match(File.ReadAllText(plist)) : Match.Empty;
        if (!m.Success || m.Groups[1].Value.Trim() != version)
            throw new StoreException("The downloaded Nota.app isn't the expected version — not installing it.");
    }

    [GeneratedRegex(@"<key>CFBundleShortVersionString</key>\s*<string>([^<]*)</string>")]
    private static partial Regex BundleVersion();

    private static void RunTool(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new StoreException($"Couldn't start {exe}.");
        var err = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromMinutes(2))) { try { p.Kill(); } catch { } throw new StoreException($"{exe} timed out."); }
        if (p.ExitCode != 0) throw new StoreException($"Couldn't prepare the update ({exe}: {err.Result.Trim()}).");
    }

    // ---- install on exit --------------------------------------------------------------------

    public void RunPendingInstall()
    {
        if (_pending is not { } p) return;
        _pending = null;
        try
        {
            var pid = Environment.ProcessId.ToString();
            var relaunch = RelaunchAfterInstall ? "1" : "0";
            ProcessStartInfo psi;
            if (p.Kind == InstallKind.WindowsInstaller)
            {
                var script = Path.Combine(_updatesDir, "install.ps1");
                File.WriteAllText(script, WindowsScript);
                psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script, pid, p.Staged, relaunch })
                    psi.ArgumentList.Add(a);
            }
            else
            {
                var script = Path.Combine(_updatesDir, "install.sh");
                File.WriteAllText(script, p.Kind == InstallKind.MacBundle ? MacScript : LinuxScript);
                psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
                foreach (var a in new[] { script, pid, p.Staged, p.Target, relaunch })
                    psi.ArgumentList.Add(a);
            }
            Process.Start(psi)?.Dispose();
        }
        catch { /* the app is quitting either way; the next launch offers the update again */ }
    }

    // Shared prologue: wait (up to two minutes) for the app to exit; give up if it never does.
    private const string WaitForExit = """
        pid="$1"; staged="$2"; target="$3"; relaunch="$4"
        n=0
        while kill -0 "$pid" 2>/dev/null; do
          n=$((n+1)); [ "$n" -gt 600 ] && exit 1
          sleep 0.2
        done

        """;

    // Move the old bundle aside, move the new one in, roll back if that fails.
    public const string MacScript = "#!/bin/sh\n" + WaitForExit + """
        old="$target.updating-old"
        rm -rf "$old"
        if mv "$target" "$old"; then
          if mv "$staged" "$target"; then
            rm -rf "$old"
          else
            mv "$old" "$target"
            exit 1
          fi
        else
          exit 1
        fi
        xattr -dr com.apple.quarantine "$target" 2>/dev/null
        [ "$relaunch" = 1 ] && open "$target"
        exit 0

        """;

    // Copy next to the AppImage (same filesystem), then rename over it atomically.
    public const string LinuxScript = "#!/bin/sh\n" + WaitForExit + """
        tmp="$target.updating"
        if cp "$staged" "$tmp" && chmod 755 "$tmp" && mv -f "$tmp" "$target"; then
          rm -f "$staged"
        else
          rm -f "$tmp"
          exit 1
        fi
        [ "$relaunch" = 1 ] && nohup "$target" >/dev/null 2>&1 &
        exit 0

        """;

    // Run the installer silently (it asks for elevation itself); /relaunch=1 makes it start
    // Nota again as the original user (see scripts/nota.iss).
    public const string WindowsScript = """
        $p = [int]$args[0]; $installer = $args[1]; $relaunch = $args[2]
        try { Wait-Process -Id $p -Timeout 120 -ErrorAction SilentlyContinue } catch {}
        if (Get-Process -Id $p -ErrorAction SilentlyContinue) { exit 1 }
        $a = @('/SILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')
        if ($relaunch -eq '1') { $a += '/relaunch=1' }
        Start-Process -FilePath $installer -ArgumentList $a -Wait
        Remove-Item -LiteralPath $installer -ErrorAction SilentlyContinue

        """;

    // ---- helpers ----------------------------------------------------------------------------

    private static OSPlatform CurrentOs()
        => OperatingSystem.IsMacOS() ? OSPlatform.OSX : OperatingSystem.IsWindows() ? OSPlatform.Windows : OSPlatform.Linux;

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".nota-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    // Leftovers from an earlier update (an installed .app copy, an interrupted download).
    private void CleanUpdatesDir()
    {
        try
        {
            if (!Directory.Exists(_updatesDir)) return;
            foreach (var d in Directory.EnumerateDirectories(_updatesDir))
                try { Directory.Delete(d, recursive: true); } catch { }
            foreach (var f in Directory.EnumerateFiles(_updatesDir)) TryDelete(f);
        }
        catch { /* best-effort */ }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { }
    }
}
