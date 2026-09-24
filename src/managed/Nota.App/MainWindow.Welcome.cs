// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Welcome screen plumbing: build the launcher, feed it the recent-projects list, and
// wire its actions back to the main window. Recent projects are recorded here on every
// successful open/save (see MainWindow.Files.OpenProject / DoSaveAsync).

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private const int MaxRecentProjects = 10;

    /// <summary>Show the welcome launcher as a modal over the (empty) main window.</summary>
    private async Task ShowWelcomeAsync()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var recent = LoadRecentItems(settings);

        // Fold the crash-recovery prompt into the launcher as an inline banner. When there's
        // no surviving snapshot the arguments stay null and no banner is drawn.
        string? recoveryMessage = PendingRecovery is { } info ? RecoveryPromptText(info) : null;

        WelcomeWindow? win = null;
        win = new WelcomeWindow(
            recent,
            onNew: () => OnMenuNew(this, EventArgs.Empty),
            onOpen: () => _ = DoOpenAsync(),
            onOpenRecent: OpenProject,
            onSettings: () => new PreferencesWindow(new SettingsViewModel(settings), _vm).ShowDialog(win!),
            onWhatsNew: () => new WhatsNewWindow(AppInfo.UnseenSince("")).ShowDialog(win!),
            showOnStartup: settings.Current.ShowWelcomeOnStartup,
            onShowOnStartupChanged: v => { settings.Current.ShowWelcomeOnStartup = v; settings.Save(); },
            recoveryMessage: recoveryMessage,
            onRecover: recoveryMessage is null ? null
                : () => { if (PendingRecovery is { } ri) ApplyPendingRecovery(ri); win!.Close(); },
            onDismissRecovery: () => { try { _recovery.ClearRecovery(); } catch { } });

        // Whatever choice closes the launcher (New / Open / recent), the crash snapshot has
        // been offered — resolve it exactly once so it isn't re-offered next launch.
        if (PendingRecovery is not null)
            win.Closed += (_, _) => { try { _recovery.ClearRecovery(); } catch { } };

        // Look for a newer release in the background; the banner appears if one turns up
        // while the launcher is still open.
        _ = UpdateChecker.CheckAsync().ContinueWith(t =>
        {
            if (t.Result is { } update)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (win.IsVisible) win.ShowUpdateAvailable(update); });
        }, TaskScheduler.Default);

        await win.ShowDialog(this);
    }

    // Turn the persisted path list into displayable rows, dropping folders that no longer
    // exist (moved/deleted since last run). Newest first — the stored order is maintained.
    private static IReadOnlyList<RecentProjectItem> LoadRecentItems(ISettingsService settings)
    {
        var items = new List<RecentProjectItem>();
        foreach (var path in settings.Current.RecentProjects)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;
            var name = Path.GetFileNameWithoutExtension(path.TrimEnd('/', '\\'));
            string modified;
            try { modified = Directory.GetLastWriteTime(path).ToString("g"); }
            catch { modified = ""; }
            items.Add(new RecentProjectItem(name, path, modified));
        }
        return items;
    }

    /// <summary>Move a project bundle to the top of the recent list and persist. Called on
    /// every successful open/save so the welcome screen always reflects real usage.</summary>
    private void RecordRecentProject(string dir)
    {
        try
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            var list = settings.Current.RecentProjects;
            list.RemoveAll(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, dir);
            if (list.Count > MaxRecentProjects) list.RemoveRange(MaxRecentProjects, list.Count - MaxRecentProjects);
            settings.Save();
        }
        catch { /* recent list is best-effort — never disrupt the open/save */ }
    }
}
