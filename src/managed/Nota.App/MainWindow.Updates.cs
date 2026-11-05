// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A newer Nota in the main window (#58), so it isn't only offered on the start screen: the
// status bar notice (an accent dot and one line beside the version — available, downloading
// with its bar, ready to install, or failed) and Help → Check for updates…, which answers
// even when there's nothing new. Both follow AppUpdates, as the start screen banner does.

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    private TextBlock _upText = null!, _upDetail = null!;
    private Ellipse _upDot = null!;
    private ProgressBar _upBar = null!;
    private Button _upClose = null!;
    private Border _upOpen = null!;
    private StackPanel _upPanel = null!, _langPanel = null!;
    private TextBlock _langText = null!;

    private void BuildUpdateIndicator()
    {
        var updates = AppUpdates.Shared;
        _upDot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        _upDot.BindResource(Shape.FillProperty, "Brush.Accent");
        _upText = new TextBlock { Classes = { "Caption" }, VerticalAlignment = VerticalAlignment.Center };
        var open = _upOpen = new Border
        {
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _upDot, _upText } },
        };
        open.PointerEntered += (_, _) => { if (updates.State != AppUpdates.Stage.Downloading) _upText.TextDecorations = TextDecorations.Underline; };
        open.PointerExited += (_, _) => _upText.TextDecorations = null;
        open.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(open).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            switch (updates.State)
            {
                case AppUpdates.Stage.Ready: updates.Restart(Close); break;
                case AppUpdates.Stage.Available or AppUpdates.Stage.Failed: UpdateMenu().Open(open); break;
            }
        };

        _upBar = new ProgressBar { Width = 90, Height = 4, MinWidth = 0, Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
        _upBar.BindResource(ProgressBar.ForegroundProperty, "Brush.Accent");
        _upBar.BindResource(ProgressBar.BackgroundProperty, "Brush.BorderDefault");
        _upDetail = new TextBlock { Classes = { "Caption", "Mono" }, VerticalAlignment = VerticalAlignment.Center };

        // One cross: Cancel while it downloads, "Not now" otherwise.
        _upClose = new Button
        {
            Classes = { "ghost" }, Width = 18, Height = 18, Padding = new Thickness(0), MinHeight = 0, MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center, Content = new Glyph(GlyphKind.Close, 8),
        };
        _upClose.BindResource(Button.ForegroundProperty, "Brush.TextTertiary");
        _upClose.Click += (_, _) =>
        {
            if (updates.State == AppUpdates.Stage.Downloading) updates.CancelDownload();
            else updates.Dismiss();
        };

        _upPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 14, 0),
            Children = { open, _upBar, _upDetail, _upClose },
        };
        BuildLanguageNotice();
        UpdateHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _langPanel, _upPanel } });

        updates.Changed += UpdateUpdateIndicator;
        updates.ProgressChanged += UpdateUpdateIndicator;
        LanguageDownloads.Shared.Changed += UpdateUpdateIndicator;
        Closed += (_, _) =>
        {
            updates.Changed -= UpdateUpdateIndicator;
            updates.ProgressChanged -= UpdateUpdateIndicator;
            LanguageDownloads.Shared.Changed -= UpdateUpdateIndicator;
        };
        UpdateUpdateIndicator();
    }

    private void UpdateUpdateIndicator()
    {
        var u = AppUpdates.Shared;
        var lang = LanguageDownloads.Shared;
        _langPanel.IsVisible = lang.Ready is { } && !lang.Dismissed;
        if (lang.Ready is { } ready)
            _langText.Text = AppRestart.CanRelaunch ? Loc.F("main.updates.language_is_ready_restart", ready.Name)
                : Loc.F("main.updates.language_is_ready_next_start", ready.Name);
        _upPanel.IsVisible = u.Release is { } && u.State != AppUpdates.Stage.None && !u.Dismissed;
        UpdateHost.IsVisible = _upPanel.IsVisible || _langPanel.IsVisible;
        if (!_upPanel.IsVisible) return;
        var version = u.Release!.Version;
        bool busy = u.State == AppUpdates.Stage.Downloading;
        var p = u.Progress;
        _upText.Text = u.State switch
        {
            AppUpdates.Stage.Downloading => Loc.F("common.downloading_nota", version),
            AppUpdates.Stage.Ready => Loc.F("main.updates.restart_to_update_to_nota", version),
            AppUpdates.Stage.Failed => Loc.T("main.updates.the_update_didnt_download"),
            _ => Loc.F("main.updates.nota_is_available", version),
        };
        ToolTip.SetTip(_upOpen, u.State switch
        {
            AppUpdates.Stage.Downloading => null,
            AppUpdates.Stage.Ready => Loc.F("main.updates.nota_is_downloaded_click_to", version),
            AppUpdates.Stage.Failed => u.Error,
            _ => Loc.F("main.updates.you_have_click_to_update", AppInfo.Version),
        });
        _upDot.IsVisible = !busy;
        _upBar.IsVisible = busy;
        _upBar.IsIndeterminate = busy && p.Fraction < 0;
        if (busy && p.Fraction >= 0) _upBar.Value = p.Fraction;
        _upDetail.Text = busy && p.Fraction >= 0 ? NotaNum.Unit(p.Fraction * 100, "0", "%") : "";
        _upDetail.IsVisible = _upDetail.Text.Length > 0;
        ToolTip.SetTip(_upClose, busy ? Loc.T("main.cancel_the_download")
            : u.State == AppUpdates.Stage.Ready ? Loc.T("main.updates.hide_it_still_installs_when") : Loc.T("main.not_now"));
    }

    // A language Nota downloaded on its own at start (the stored one was not installed, spec 077
    // story 5) is ready: the same quiet notice, with a restart on click.
    private void BuildLanguageNotice()
    {
        var dot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        dot.BindResource(Shape.FillProperty, "Brush.Accent");
        _langText = new TextBlock { Classes = { "Caption" }, VerticalAlignment = VerticalAlignment.Center };
        var open = new Border
        {
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, _langText } },
        };
        open.PointerEntered += (_, _) => _langText.TextDecorations = AppRestart.CanRelaunch ? TextDecorations.Underline : null;
        open.PointerExited += (_, _) => _langText.TextDecorations = null;
        open.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(open).Properties.IsLeftButtonPressed || !AppRestart.CanRelaunch) return;
            e.Handled = true;
            AppRestart.Request();   // the usual close: it asks about unsaved changes
            Close();
        };
        var close = new Button
        {
            Classes = { "ghost" }, Width = 18, Height = 18, Padding = new Thickness(0), MinHeight = 0, MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center, Content = new Glyph(GlyphKind.Close, 8),
        };
        close.BindResource(Button.ForegroundProperty, "Brush.TextTertiary");
        ToolTip.SetTip(close, Loc.T("main.not_now"));
        close.Click += (_, _) => LanguageDownloads.Shared.Dismiss();
        _langPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 14, 0), IsVisible = false,
            Children = { open, close },
        };
    }

    // Available / failed: what to do about it.
    private ContextMenu UpdateMenu()
    {
        var u = AppUpdates.Shared;
        var version = u.Release?.Version ?? "";
        var menu = new ContextMenu();
        menu.Items.Add(MenuKit.Item(
            u.State == AppUpdates.Stage.Failed ? Loc.T("common.try_again")
                : u.CanInstall ? Loc.F("main.updates.update_to_nota", version) : Loc.F("main.updates.download_nota", version),
            GlyphKind.Update, () => u.Download(this)));
        menu.Items.Add(MenuKit.Item(Loc.T("main.updates.release_notes"), GlyphKind.Doc, () => u.OpenReleasePage(this)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuKit.Item(Loc.T("main.not_now"), click: u.Dismiss));
        return menu;
    }

    /// <summary>Help → Check for updates… (and the macOS app menu): ask GitHub now and say what
    /// came back, including "you're up to date" and "couldn't check".</summary>
    public async Task CheckForUpdatesAsync()
    {
        var u = AppUpdates.Shared;
        var checking = Loc.T("main.updates.checking_for_updates");
        if (_vm is not null) _vm.StatusText = checking;
        bool? found = await u.CheckAsync();
        if (_vm is not null && _vm.StatusText == checking) _vm.StatusText = "";
        var title = Loc.T("main.updates.check_for_updates");

        if (found is null)
        {
            await new ConfirmWindow(title, Loc.T("main.updates.couldnt_reach_github_to_check"),
                "OK", null).ShowDialog<bool>(this);
            return;
        }
        if (found == false || u.Release is not { } release)
        {
            await new ConfirmWindow(title, Loc.F("main.updates.youre_up_to_date_nota", AppInfo.Version), "OK", null).ShowDialog<bool>(this);
            return;
        }

        u.Undismiss();
        switch (u.State)
        {
            case AppUpdates.Stage.Downloading:
                return;   // its progress is in the status bar already
            case AppUpdates.Stage.Ready:
                if (await new ConfirmWindow(title, Loc.F("main.updates.nota_is_ready_to_install", release.Version),
                        Loc.T("main.updates.restart_now"), Loc.T("main.updates.later")).ShowDialog<bool>(this))
                    u.Restart(Close);
                return;
            default:
                var message = Loc.F("main.updates.nota_is_out_you_have", release.Version, AppInfo.Version)
                    + (u.CanInstall ? Loc.T("main.updates.nota_downloads_it_here_and") : Loc.T("main.updates.download_it_from_the_release"));
                if (await new ConfirmWindow(Loc.T("main.updates.update_available"), message, u.CanInstall ? Loc.T("common.update") : Loc.T("common.download"), Loc.T("main.updates.later")).ShowDialog<bool>(this))
                    u.Download(this);
                return;
        }
    }
}
