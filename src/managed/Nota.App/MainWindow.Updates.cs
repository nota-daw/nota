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

        UpdateHost.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 14, 0),
            Children = { open, _upBar, _upDetail, _upClose },
        });

        updates.Changed += UpdateUpdateIndicator;
        updates.ProgressChanged += UpdateUpdateIndicator;
        Closed += (_, _) =>
        {
            updates.Changed -= UpdateUpdateIndicator;
            updates.ProgressChanged -= UpdateUpdateIndicator;
        };
        UpdateUpdateIndicator();
    }

    private void UpdateUpdateIndicator()
    {
        var u = AppUpdates.Shared;
        UpdateHost.IsVisible = u.Release is { } && u.State != AppUpdates.Stage.None && !u.Dismissed;
        if (!UpdateHost.IsVisible) return;
        var version = u.Release!.Version;
        bool busy = u.State == AppUpdates.Stage.Downloading;
        var p = u.Progress;
        _upText.Text = u.State switch
        {
            AppUpdates.Stage.Downloading => Loc.F("Downloading Nota {0}…", version),
            AppUpdates.Stage.Ready => Loc.F("Restart to update to Nota {0}", version),
            AppUpdates.Stage.Failed => Loc.T("The update didn't download"),
            _ => Loc.F("Nota {0} is available", version),
        };
        ToolTip.SetTip(_upOpen, u.State switch
        {
            AppUpdates.Stage.Downloading => null,
            AppUpdates.Stage.Ready => Loc.F("Nota {0} is downloaded. Click to restart now — or it installs the next time you quit.", version),
            AppUpdates.Stage.Failed => u.Error,
            _ => Loc.F("You have {0}. Click to update or read the release notes.", AppInfo.Version),
        });
        _upDot.IsVisible = !busy;
        _upBar.IsVisible = busy;
        _upBar.IsIndeterminate = busy && p.Fraction < 0;
        if (busy && p.Fraction >= 0) _upBar.Value = p.Fraction;
        _upDetail.Text = busy && p.Fraction >= 0 ? NotaNum.Unit(p.Fraction * 100, "0", "%") : "";
        _upDetail.IsVisible = _upDetail.Text.Length > 0;
        ToolTip.SetTip(_upClose, busy ? Loc.T("Cancel the download")
            : u.State == AppUpdates.Stage.Ready ? Loc.T("Hide — it still installs when you quit") : Loc.T("Not now"));
    }

    // Available / failed: what to do about it.
    private ContextMenu UpdateMenu()
    {
        var u = AppUpdates.Shared;
        var version = u.Release?.Version ?? "";
        var menu = new ContextMenu();
        menu.Items.Add(MenuKit.Item(
            u.State == AppUpdates.Stage.Failed ? Loc.T("Try again")
                : u.CanInstall ? Loc.F("Update to Nota {0}", version) : Loc.F("Download Nota {0}…", version),
            GlyphKind.Update, () => u.Download(this)));
        menu.Items.Add(MenuKit.Item(Loc.T("Release notes"), GlyphKind.Doc, () => u.OpenReleasePage(this)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuKit.Item(Loc.T("Not now"), click: u.Dismiss));
        return menu;
    }

    /// <summary>Help → Check for updates… (and the macOS app menu): ask GitHub now and say what
    /// came back, including "you're up to date" and "couldn't check".</summary>
    public async Task CheckForUpdatesAsync()
    {
        var u = AppUpdates.Shared;
        var checking = Loc.T("Checking for updates…");
        if (_vm is not null) _vm.StatusText = checking;
        bool? found = await u.CheckAsync();
        if (_vm is not null && _vm.StatusText == checking) _vm.StatusText = "";
        var title = Loc.T("Check for updates");

        if (found is null)
        {
            await new ConfirmWindow(title, Loc.T("Couldn't reach GitHub to check for a newer version of Nota. Check your internet connection and try again."),
                "OK", null).ShowDialog<bool>(this);
            return;
        }
        if (found == false || u.Release is not { } release)
        {
            await new ConfirmWindow(title, Loc.F("You're up to date: Nota {0} is the latest version.", AppInfo.Version), "OK", null).ShowDialog<bool>(this);
            return;
        }

        u.Undismiss();
        switch (u.State)
        {
            case AppUpdates.Stage.Downloading:
                return;   // its progress is in the status bar already
            case AppUpdates.Stage.Ready:
                if (await new ConfirmWindow(title, Loc.F("Nota {0} is ready to install. Restart Nota to finish updating — or it installs the next time you quit.", release.Version),
                        Loc.T("Restart now"), Loc.T("Later")).ShowDialog<bool>(this))
                    u.Restart(Close);
                return;
            default:
                var message = Loc.F("Nota {0} is out — you have {1}.", release.Version, AppInfo.Version)
                    + (u.CanInstall ? Loc.T(" Nota downloads it here and installs it when you restart or quit.") : Loc.T(" Download it from the release page."));
                if (await new ConfirmWindow(Loc.T("Update available"), message, u.CanInstall ? Loc.T("Update") : Loc.T("Download"), Loc.T("Later")).ShowDialog<bool>(this))
                    u.Download(this);
                return;
        }
    }
}
