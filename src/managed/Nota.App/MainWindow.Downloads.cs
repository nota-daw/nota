// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The status bar's Downloads indicator: a plug-in / sample pack / AI model install started in
// Settings → Downloads keeps running after Settings closes (DownloadJobs), and shows here — its
// message, a thin bar, the percentage and a Cancel cross; then its outcome until it fades or is
// closed. Clicking the message opens Settings on the job's Downloads source.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    private TextBlock _dlText = null!, _dlDetail = null!;
    private ProgressBar _dlBar = null!;
    private Button _dlCancel = null!;

    private void BuildDownloadIndicator()
    {
        _dlText = new TextBlock { Classes = { "Caption" }, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis };
        var open = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = _dlText };
        ToolTip.SetTip(open, "Open Settings → Downloads");
        open.PointerEntered += (_, _) => _dlText.TextDecorations = TextDecorations.Underline;
        open.PointerExited += (_, _) => _dlText.TextDecorations = null;
        open.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(open).Properties.IsLeftButtonPressed) ShowPreferences(downloads: DownloadJobs.Shared.Source);
        };

        _dlBar = new ProgressBar { Width = 90, Height = 4, MinWidth = 0, Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
        _dlBar.BindResource(ProgressBar.ForegroundProperty, "Brush.Accent");
        _dlBar.BindResource(ProgressBar.BackgroundProperty, "Brush.BorderDefault");
        _dlDetail = new TextBlock { Classes = { "Caption", "Mono" }, VerticalAlignment = VerticalAlignment.Center };

        // One cross: Cancel while the download runs, close once there's only the outcome.
        _dlCancel = new Button
        {
            Classes = { "ghost" }, Width = 18, Height = 18, Padding = new Thickness(0), MinHeight = 0, MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center, Content = new Glyph(GlyphKind.Close, 8),
        };
        _dlCancel.BindResource(Button.ForegroundProperty, "Brush.TextTertiary");
        _dlCancel.Click += (_, _) =>
        {
            var jobs = DownloadJobs.Shared;
            if (jobs.Busy) jobs.Cancel(); else jobs.Dismiss();
        };

        DownloadHost.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 14, 0),
            Children = { open, _dlBar, _dlDetail, _dlCancel },
        });

        DownloadJobs.Shared.Changed += UpdateDownloadIndicator;
        DownloadJobs.Shared.ProgressChanged += UpdateDownloadIndicator;
        Closed += (_, _) =>
        {
            DownloadJobs.Shared.Changed -= UpdateDownloadIndicator;
            DownloadJobs.Shared.ProgressChanged -= UpdateDownloadIndicator;
        };
        UpdateDownloadIndicator();
    }

    private void UpdateDownloadIndicator()
    {
        var jobs = DownloadJobs.Shared;
        bool busy = jobs.Busy;
        DownloadHost.IsVisible = busy || jobs.Result is not null;
        if (!DownloadHost.IsVisible) return;

        var pr = jobs.Progress;
        _dlText.Text = busy ? pr.Message : jobs.Result;
        _dlBar.IsVisible = busy;
        _dlBar.IsIndeterminate = busy && pr.Fraction < 0;
        if (busy && pr.Fraction >= 0) _dlBar.Value = pr.Fraction;
        _dlDetail.Text = busy && pr.Fraction >= 0 ? NotaNum.Unit(pr.Fraction * 100, "0", "%") : "";
        _dlDetail.IsVisible = _dlDetail.Text.Length > 0;
        // Past the download stage (unpacking, scanning, removing) there's nothing to cancel.
        _dlCancel.IsVisible = !busy || jobs.Cancellable;
        _dlCancel.IsEnabled = !jobs.Cancelling;
        ToolTip.SetTip(_dlCancel, busy ? "Cancel the download" : "Dismiss");
    }

    /// <summary>Settings → Downloads, on the given source; reuses an open Settings window.</summary>
    private void ShowPreferences(int downloads)
    {
        foreach (var w in OwnedWindows)
            if (w is PreferencesWindow open)
            {
                open.ShowDownloads(downloads);
                open.Activate();
                return;
            }
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var win = new PreferencesWindow(new SettingsViewModel(settings), _vm);
        win.ShowDownloads(downloads);
        win.Show(this);
    }
}
