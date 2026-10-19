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

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private const int UpdatesIndex = 9;

    private Control UpdatesPane()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var updates = AppUpdates.Shared;

        var auto = SwitchRow("Check for updates automatically", settings.Current.CheckForUpdates, on =>
        {
            settings.Current.CheckForUpdates = on;
            settings.Save();
            if (on && UpdateSchedule.IsDue(true, updates.LastChecked, DateTime.Now)) _ = updates.CheckAsync();
        });

        var status = new TextBlock { FontSize = 12, Foreground = TextPrimary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var detail = Caption("");
        var check = Btn("Check now");
        var act = Btn("Update");
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
            check.Content = updates.Checking ? "Checking…" : "Check now";
            act.IsVisible = r is not null && updates.State != AppUpdates.Stage.Ready;
            (status.Text, detail.Text) = updates.State switch
            {
                AppUpdates.Stage.Available => ($"Nota {r!.Version} is available — you have {AppInfo.Version}.",
                    updates.CanInstall ? "Update downloads it here; it installs when you restart or quit Nota." : "Download opens the release page."),
                AppUpdates.Stage.Downloading => ($"Downloading Nota {r!.Version}…", Percent()),
                AppUpdates.Stage.Ready => ($"Nota {r!.Version} is ready to install.", "It installs the next time you quit Nota, or click Restart to update in the status bar."),
                AppUpdates.Stage.Failed => ("The update didn't download.", updates.Error ?? ""),
                _ when updates.LastCheckFailed => ($"You have Nota {AppInfo.Version}.", "Couldn't reach GitHub to check for a newer version. Check your internet connection and try again."),
                _ when updates.LastChecked is { } at => ($"Nota {AppInfo.Version} is the latest version.", $"Last checked {When(at)}."),
                _ => ($"You have Nota {AppInfo.Version}.", "Not checked yet."),
            };
            act.Content = updates.State switch
            {
                AppUpdates.Stage.Downloading => "Cancel",
                AppUpdates.Stage.Failed => "Try again",
                _ => updates.CanInstall ? "Update" : "Download",
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
            rows.Add(Caption("This copy of Nota can't replace itself (a development build, or one run straight from the disk image), so a new version opens its release page to download.", muted: true));

        return Sections(
            Section("Automatic updates", 10, auto,
                Caption("On launch and once a day, Nota asks GitHub whether a newer version is out and tells you in the status bar. Nothing is downloaded until you click Update.")),
            Section("This version", 10, rows.ToArray()));
    }

    // "today at 14:02", "yesterday at 09:15", or the date.
    private static string When(DateTime at)
    {
        var day = at.Date == DateTime.Today ? "today" : at.Date == DateTime.Today.AddDays(-1) ? "yesterday" : at.ToString("d MMM");
        return $"{day} at {at:HH:mm}";
    }
}
