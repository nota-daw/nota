// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>A published release newer than the running app. <paramref name="Asset"/> is the build
/// for this OS + architecture, or null when the release ships none.</summary>
public sealed record AppRelease(string Version, string PageUrl, AppReleaseAsset? Asset);

/// <summary>One downloadable release file. <paramref name="Sha256"/> is null when the release
/// host didn't publish a digest — the size is still checked.</summary>
public sealed record AppReleaseAsset(string Name, string Url, long Size, string? Sha256);

/// <summary>Self-update: find a newer release, download and verify its build for this platform,
/// and swap it in when the app quits. The swap runs in a small detached helper that waits for
/// this process to exit, replaces the installed app (the .app bundle on macOS, the AppImage on
/// Linux; the Inno Setup installer runs silently on Windows) and optionally relaunches it.</summary>
public interface IAppUpdater
{
    /// <summary>The latest published release if it's newer than the running version, else null.
    /// Best-effort: network / parse failures return null.</summary>
    Task<AppRelease?> CheckAsync(CancellationToken ct = default);

    /// <summary>True when this install can be replaced in place (a packaged app in a writable
    /// location). Otherwise the user downloads the release by hand.</summary>
    bool CanInstallInPlace { get; }

    /// <summary>Downloads <paramref name="release"/>'s asset, verifies it and prepares it for
    /// install; afterwards <see cref="PendingVersion"/> is set and the update is applied on exit.
    /// Throws <see cref="StoreException"/> with a user-facing message on failure.</summary>
    Task DownloadAsync(AppRelease release, IProgress<StoreProgress>? progress = null, CancellationToken ct = default);

    /// <summary>The version a downloaded update will install on exit, or null.</summary>
    string? PendingVersion { get; }

    /// <summary>Start the new version once the update is installed (Restart now), rather than
    /// just installing it on quit.</summary>
    bool RelaunchAfterInstall { get; set; }

    /// <summary>Hands a pending update to the install helper. Called once, as the app exits;
    /// a no-op when nothing is pending.</summary>
    void RunPendingInstall();
}
