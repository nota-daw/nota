// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Updates (#58): whether Nota looks for new versions on its own (on launch and once
// a day), the state of the last check with Check now, and the update itself when one is out —
// the same shared state (AppUpdates) as the start screen banner and the status bar notice.

using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private Control UpdatesPane()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var updates = AppUpdates.Shared;

        var auto = SwitchRow(Loc.T("preferences.updates.check_for_updates_automatically"), settings.Current.CheckForUpdates, on =>
        {
            settings.Current.CheckForUpdates = on;
            settings.Save();
            if (on && UpdateSchedule.IsDue(true, updates.LastChecked, DateTime.Now)) _ = updates.CheckAsync();
        });

        var status = new TextBlock { FontSize = 11, Foreground = TextPrimary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var detail = Caption("");
        var check = Btn(Loc.T("preferences.updates.check_now"));
        var act = Btn(Loc.T("common.update"));
        act.Classes.Add("primary");
        check.Click += async (_, _) => await updates.CheckAsync();
        act.Click += (_, _) =>
        {
            if (updates.State == AppUpdates.Stage.Downloading) updates.CancelDownload();
            else updates.Download(GetTopLevel(this));
        };

        void Refresh()
        {
            var r = updates.Release;
            check.IsEnabled = !updates.Checking && updates.State is not (AppUpdates.Stage.Downloading or AppUpdates.Stage.Ready);
            check.Content = updates.Checking ? Loc.T("preferences.updates.checking") : Loc.T("preferences.updates.check_now");
            act.IsVisible = r is not null && updates.State != AppUpdates.Stage.Ready;
            (status.Text, detail.Text) = updates.State switch
            {
                AppUpdates.Stage.Available => (Loc.F("preferences.updates.nota_is_available_you_have", r!.Version, AppInfo.Version),
                    updates.CanInstall ? Loc.T("preferences.updates.update_downloads_it_here_it") : Loc.T("preferences.updates.download_opens_the_release_page")),
                AppUpdates.Stage.Downloading => (Loc.F("common.downloading_nota", r!.Version), Percent()),
                AppUpdates.Stage.Ready => (Loc.F("preferences.updates.nota_is_ready_to_install", r!.Version), Loc.T("preferences.updates.it_installs_the_next_time")),
                AppUpdates.Stage.Failed => (Loc.T("preferences.updates.the_update_didnt_download"), updates.Error ?? ""),
                _ when updates.LastCheckFailed => (Loc.F("preferences.updates.you_have_nota", AppInfo.Version), Loc.T("preferences.updates.couldnt_reach_github_to_check")),
                _ when updates.LastChecked is { } at => (Loc.F("preferences.updates.nota_is_the_latest_version", AppInfo.Version), Loc.F("preferences.updates.last_checked", When(at))),
                _ => (Loc.F("preferences.updates.you_have_nota", AppInfo.Version), Loc.T("preferences.updates.not_checked_yet")),
            };
            act.Content = updates.State switch
            {
                AppUpdates.Stage.Downloading => Loc.T("common.cancel"),
                AppUpdates.Stage.Failed => Loc.T("common.try_again"),
                _ => updates.CanInstall ? Loc.T("common.update") : Loc.T("common.download"),
            };
            act.Classes.Set("primary", updates.State != AppUpdates.Stage.Downloading);
            detail.IsVisible = detail.Text!.Length > 0;
        }
        string Percent() => updates.Progress.Fraction >= 0 ? NotaNum.Unit(updates.Progress.Fraction * 100, "0", "%") : updates.Progress.Message;
        void Progress() => detail.Text = Percent();

        updates.Changed += Refresh;
        updates.ProgressChanged += Progress;
        status.DetachedFromVisualTree += (_, _) => { updates.Changed -= Refresh; updates.ProgressChanged -= Progress; };
        Refresh();

        var rows = new System.Collections.Generic.List<Control>
        {
            new StackPanel { Spacing = 3, Children = { status, detail } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { check, act } },
        };
        if (!updates.CanInstallInPlace)
            rows.Add(Caption(Loc.T("preferences.updates.this_copy_of_nota_cant"), muted: true));

        return Sections(
            Section(Loc.T("preferences.updates.automatic_updates"), 10, auto,
                Caption(Loc.T("preferences.updates.on_launch_and_once_a"))),
            Section(Loc.T("preferences.updates.this_version"), 10, rows.ToArray()));
    }

    // "today at 14:02", "yesterday at 09:15", or the date.
    private static string When(DateTime at)
    {
        var day = at.Date == DateTime.Today ? "today" : at.Date == DateTime.Today.AddDays(-1) ? "yesterday" : at.ToString("d MMM");
        return $"{day} at {at:HH:mm}";
    }
}
