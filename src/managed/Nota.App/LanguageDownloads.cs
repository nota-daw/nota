// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The app's one view of the languages it can download (spec 077): the language list from
// nota-translations, a download in progress, and a language that is installed and waiting for a
// restart. It lives for the app, so Settings → Appearance → Language and the main window's status
// bar notice show the same state and share one download. All members are used on the UI thread.
//
// Network policy (research R10): Nota asks for the list on its own only when Settings → Updates →
// "Check for updates" is on — when the Language row is shown, and at start when the stored
// language is not installed (an update from a Nota that had French built in: story 5). With
// checks off it asks only when the user clicks. Starting never waits for any of it.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Infrastructure;

namespace Nota.App;

internal sealed class LanguageDownloads
{
    public static LanguageDownloads Shared { get; } = new();

    private Task? _fetching;
    private CancellationTokenSource? _download;

    /// <summary>The list, once fetched (or read from the cache) this run.</summary>
    public LanguageIndex? Index { get; private set; }

    /// <summary>Why the list could not be had, if it could not.</summary>
    public string? IndexError { get; private set; }

    /// <summary>The code being downloaded, and how far (0–1, or -1 unknown).</summary>
    public string? Downloading { get; private set; }
    public double Progress { get; private set; } = -1;

    /// <summary>The last download that failed: its code and why.</summary>
    public (string Code, string Message)? Failed { get; private set; }

    /// <summary>A language installed this run that is the stored choice but not the running one:
    /// the restart the status bar offers (story 5).</summary>
    public LanguageInfo? Ready { get; private set; }

    /// <summary>The status bar notice was dismissed for this run.</summary>
    public bool Dismissed { get; private set; }

    public event Action? Changed;

    private static ISettingsService Settings => App.Services.GetRequiredService<ISettingsService>();
    private static LanguageCatalog Catalog => App.Services.GetRequiredService<LanguageCatalog>();
    private static InstalledLanguages Installed => App.Services.GetRequiredService<InstalledLanguages>();

    /// <summary>Whether Nota may ask GitHub on its own (Settings → Updates).</summary>
    public static bool MayCheck => Settings.Current.CheckForUpdates;

    /// <summary>This Nota's key set and version, for coverage (contracts/index.md).</summary>
    public static int? CoverageOf(LanguageIndex index, CatalogLanguage l)
        => LanguageCatalog.CoverageFor(index, l, Loc.English.KeySetHash(), AppInfo.Version);

    /// <summary>The list without the network: the cache, if any. Leaves <see cref="Index"/> alone
    /// when it already has a fresher one.</summary>
    public void LoadCached()
    {
        if (Index is null && Catalog.Cached() is { } cached) { Index = cached; Raise(); }
    }

    /// <summary>Fetch the list (once per run unless <paramref name="refresh"/>). The cache serves
    /// while it is a day fresh and when offline.</summary>
    public Task FetchAsync(bool refresh = false)
    {
        if (_fetching is { IsCompleted: false }) return _fetching;
        if (Index is not null && IndexError is null && !refresh && _fetchedThisRun) return Task.CompletedTask;
        return _fetching = Run();

        async Task Run()
        {
            try
            {
                var index = await Task.Run(() => Catalog.FetchAsync(refresh));
                Index = index;
                IndexError = null;
                _fetchedThisRun = true;
            }
            catch (Exception e) when (e is StoreException or System.Text.Json.JsonException or System.IO.IOException)
            {
                IndexError = e.Message;
            }
            Raise();
        }
    }
    private bool _fetchedThisRun;

    /// <summary>Download and install one language. True on success; on failure <see cref="Failed"/>
    /// says why and the installed copy, if any, is unchanged.</summary>
    public async Task<bool> DownloadAsync(CatalogLanguage language)
    {
        if (Downloading is not null) return false;
        Downloading = language.Code;
        Progress = -1;
        Failed = null;
        _download = new CancellationTokenSource();
        Raise();
        var progress = new Progress<StoreProgress>(p => { Progress = p.Fraction; Raise(); });
        try
        {
            await Task.Run(() => Catalog.DownloadAsync(language, progress, _download.Token));
            Downloading = null;
            NoteReady(language.Code, language.Name);
            Raise();
            return true;
        }
        catch (Exception e) when (e is StoreException or System.IO.IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            Downloading = null;
            Failed = (language.Code, e is OperationCanceledException ? Loc.T("language_downloads.cancelled") : e.Message);
            Raise();
            return false;
        }
    }

    public void CancelDownload() => _download?.Cancel();

    public void Dismiss() { Dismissed = true; Raise(); }

    /// <summary>After an install or a removal: whether a restart now changes the language.</summary>
    public void NoteReady(string code, string name)
    {
        Ready = Settings.Current.Language == code && Loc.Language != code ? new LanguageInfo(code, name) : null;
    }

    public void ClearReady() { Ready = null; Raise(); }

    /// <summary>Once the main window is up, when Nota may check on its own: if the stored language
    /// is not installed (Nota used to ship French, story 5) fetch it in the background; if a
    /// language from the repository is running, fetch the list so an update is known.</summary>
    public void StartupCheck(ILogSink log)
    {
        var stored = Settings.Current.Language?.Trim().ToLowerInvariant() ?? "";
        // A language from the repository is running: learn about a newer translation in the
        // background, so Settings offers Update straight away (research R10).
        if (Loc.Language != UiLanguage.Default && Loc.Language == stored && MayCheck
            && Installed.Read(stored) is { Table: not null, Meta.IsLocal: false })
        {
            _ = FetchAsync();
            return;
        }
        if (stored.Length == 0 || stored == UiLanguage.Default || Loc.Language == stored || !InstalledLanguages.IsSafeCode(stored)) return;
        if (Installed.Read(stored) is { Table: not null }) return;   // installed, only not running (e.g. NOTA_UI_LANGUAGE)
        if (!MayCheck) { LoadCached(); return; }
        _ = Run();

        async Task Run()
        {
            await FetchAsync();
            if (Index?.Languages.FirstOrDefault(l => l.Code == stored) is not { } language)
            {
                log.Warn($"Interface language {stored} is not installed and not offered: {IndexError ?? "not in the language list"}");
                return;
            }
            log.Info($"Interface language {stored} is not installed: downloading it");
            if (await DownloadAsync(language)) log.Info($"Interface language {stored} installed; applies after a restart");
            else log.Warn($"Interface language {stored} could not be downloaded: {Failed?.Message}");
        }
    }

    private void Raise()
    {
        if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke();
        else Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }
}
