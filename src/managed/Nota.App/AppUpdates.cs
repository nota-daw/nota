// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The app's one view of self-update (#58): whether a newer release is out, its download and
// whether it's ready to install. It lives for the app, so the start screen's banner, the main
// window's status bar notice (MainWindow.Updates.cs), Help → Check for updates… and Settings →
// Updates all show the same state and share one download. Nota checks on launch and once a day
// while it runs (UpdateSchedule) unless Settings → Updates turned that off; a check the user
// asks for runs either way. Nothing downloads until the user clicks Update. All members are
// used on the UI thread.

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

internal sealed class AppUpdates
{
    public static AppUpdates Shared { get; } = new();

    public enum Stage
    {
        /// <summary>Up to date, or not checked yet.</summary>
        None,
        /// <summary>A newer release is out; nothing downloaded.</summary>
        Available,
        Downloading,
        /// <summary>Downloaded and verified: installs on quit, or now with Restart.</summary>
        Ready,
        /// <summary>The download failed; <see cref="Error"/> says why.</summary>
        Failed,
    }

    public Stage State { get; private set; }

    /// <summary>The newer release, or null while <see cref="State"/> is None.</summary>
    public AppRelease? Release { get; private set; }

    public StoreProgress Progress { get; private set; }

    public string? Error { get; private set; }

    /// <summary>The user said "Not now" to <see cref="Release"/>: the start screen and the status
    /// bar stop offering it until a newer one turns up or they ask again.</summary>
    public bool Dismissed { get; private set; }

    /// <summary>A check is running.</summary>
    public bool Checking => _check is not null;

    /// <summary>When the last check got an answer (local time), or null.</summary>
    public DateTime? LastChecked { get; private set; }

    /// <summary>The last check couldn't reach or read the release feed.</summary>
    public bool LastCheckFailed { get; private set; }

    /// <summary>This copy can replace itself (a packaged app in a writable place, not a dev
    /// build or one run from the disk image).</summary>
    public bool CanInstallInPlace => Updater.CanInstallInPlace;

    /// <summary>The release has a build for this computer and this copy can replace itself;
    /// otherwise its page is where to get it.</summary>
    public bool CanInstall => Release?.Asset is not null && Updater.CanInstallInPlace;

    /// <summary>State, release or dismissal changed.</summary>
    public event Action? Changed;

    /// <summary>The download reported progress.</summary>
    public event Action? ProgressChanged;

    private static IAppUpdater Updater => App.Services.GetRequiredService<IAppUpdater>();
    private static ISettingsService Settings => App.Services.GetRequiredService<ISettingsService>();

    private readonly CancellationTokenSource _app = new();   // app lifetime
    private CancellationTokenSource? _download;
    private Task<bool?>? _check;
    private DispatcherTimer? _timer;

    /// <summary>Launch: the first automatic check, then the daily one. Called once, when the
    /// main window opens.</summary>
    public void Start()
    {
        if (_timer is not null) return;
        if (Settings.Current.CheckForUpdates) _ = CheckAsync();
        _timer = new DispatcherTimer { Interval = UpdateSchedule.Poll };
        _timer.Tick += (_, _) =>
        {
            if (UpdateSchedule.IsDue(Settings.Current.CheckForUpdates, LastChecked, DateTime.Now)) _ = CheckAsync();
        };
        _timer.Start();
    }

    /// <summary>Ask GitHub for the latest release. True when a newer one is out, false when this
    /// is the latest, null when the check failed. A check already running is shared; with an
    /// update downloading or downloaded there's nothing to ask.</summary>
    public Task<bool?> CheckAsync()
    {
        if (State is Stage.Downloading or Stage.Ready) return Task.FromResult<bool?>(true);
        return _check ??= RunCheck();
    }

    private async Task<bool?> RunCheck()
    {
        Changed?.Invoke();   // Checking
        try
        {
            var release = await Updater.CheckAsync(_app.Token);
            LastCheckFailed = Updater.LastCheckFailed;
            if (LastCheckFailed) return null;
            LastChecked = DateTime.Now;
            if (State is Stage.Downloading or Stage.Ready) return true;   // started while we asked
            if (release is null)
            {
                Release = null;
                State = Stage.None;
                Error = null;
                return false;
            }
            if (Release?.Version != release.Version) Dismissed = false;   // a newer one than they waved off
            Release = release;
            if (State == Stage.None) State = Stage.Available;
            return true;
        }
        catch (Exception e)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Update check failed", e);
            LastCheckFailed = true;
            return null;
        }
        finally
        {
            _check = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Update: download and verify the new build, which then installs on quit. A copy
    /// that can't replace itself opens the release page instead.</summary>
    public async void Download(TopLevel? from)
    {
        if (Release is not { } release || State is Stage.Downloading or Stage.Ready) return;
        if (!CanInstall)
        {
            OpenReleasePage(from);
            return;
        }
        var job = _download = CancellationTokenSource.CreateLinkedTokenSource(_app.Token);
        State = Stage.Downloading;
        Dismissed = false;
        Error = null;
        Progress = new StoreProgress(0, $"Downloading Nota {release.Version}…");
        Changed?.Invoke();
        var progress = new Progress<StoreProgress>(p =>
        {
            if (job.IsCancellationRequested || State != Stage.Downloading) return;
            Progress = p;
            ProgressChanged?.Invoke();
        });
        try
        {
            await Updater.DownloadAsync(release, progress, job.Token);
            State = Stage.Ready;
        }
        catch (OperationCanceledException) { State = Stage.Available; }
        catch (StoreException e) { Fail(e.Message); }
        catch (Exception e)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Update download failed", e);
            Fail($"Couldn't download the update: {e.Message}");
        }
        finally
        {
            _download = null;
            job.Dispose();
        }
        if (!_app.IsCancellationRequested) Changed?.Invoke();

        void Fail(string message)
        {
            State = Stage.Failed;
            Error = message;
        }
    }

    public void CancelDownload() => _download?.Cancel();

    /// <summary>"Not now": stop offering this release (a download in progress is cancelled).
    /// A downloaded update still installs on quit.</summary>
    public void Dismiss()
    {
        CancelDownload();
        if (State == Stage.None || Dismissed) return;
        Dismissed = true;
        Changed?.Invoke();
    }

    /// <summary>Offer the release again after a "Not now" (the user asked: Check for updates…).</summary>
    public void Undismiss()
    {
        if (!Dismissed) return;
        Dismissed = false;
        Changed?.Invoke();
    }

    /// <summary>Restart now: quit through <paramref name="close"/> (the normal close path, which
    /// asks about unsaved changes) and start the new version once it's installed.</summary>
    public void Restart(Action close)
    {
        if (State != Stage.Ready) return;
        Updater.RelaunchAfterInstall = true;
        close();
    }

    public void OpenReleasePage(TopLevel? from)
    {
        var url = Release?.PageUrl ?? Nota.Infrastructure.AppUpdater.ReleasesPage;
        _ = from?.Launcher.LaunchUriAsync(new Uri(url));
    }

    /// <summary>App exit: stop a running check or download.</summary>
    public void CancelAll()
    {
        _timer?.Stop();
        _app.Cancel();
    }
}
