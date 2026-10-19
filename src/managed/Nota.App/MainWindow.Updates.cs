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
            AppUpdates.Stage.Downloading => $"Downloading Nota {version}…",
            AppUpdates.Stage.Ready => $"Restart to update to Nota {version}",
            AppUpdates.Stage.Failed => "The update didn't download",
            _ => $"Nota {version} is available",
        };
        ToolTip.SetTip(_upOpen, u.State switch
        {
            AppUpdates.Stage.Downloading => null,
            AppUpdates.Stage.Ready => $"Nota {version} is downloaded. Click to restart now — or it installs the next time you quit.",
            AppUpdates.Stage.Failed => u.Error,
            _ => $"You have {AppInfo.Version}. Click to update or read the release notes.",
        });
        _upDot.IsVisible = !busy;
        _upBar.IsVisible = busy;
        _upBar.IsIndeterminate = busy && p.Fraction < 0;
        if (busy && p.Fraction >= 0) _upBar.Value = p.Fraction;
        _upDetail.Text = busy && p.Fraction >= 0 ? NotaNum.Unit(p.Fraction * 100, "0", "%") : "";
        _upDetail.IsVisible = _upDetail.Text.Length > 0;
        ToolTip.SetTip(_upClose, busy ? "Cancel the download"
            : u.State == AppUpdates.Stage.Ready ? "Hide — it still installs when you quit" : "Not now");
    }

    // Available / failed: what to do about it.
    private ContextMenu UpdateMenu()
    {
        var u = AppUpdates.Shared;
        var version = u.Release?.Version ?? "";
        var menu = new ContextMenu();
        menu.Items.Add(MenuKit.Item(
            u.State == AppUpdates.Stage.Failed ? "Try again"
                : u.CanInstall ? $"Update to Nota {version}" : $"Download Nota {version}…",
            GlyphKind.Update, () => u.Download(this)));
        menu.Items.Add(MenuKit.Item("Release notes", GlyphKind.Doc, () => u.OpenReleasePage(this)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuKit.Item("Not now", click: u.Dismiss));
        return menu;
    }

    /// <summary>Help → Check for updates… (and the macOS app menu): ask GitHub now and say what
    /// came back, including "you're up to date" and "couldn't check".</summary>
    public async Task CheckForUpdatesAsync()
    {
        var u = AppUpdates.Shared;
        const string checking = "Checking for updates…";
        if (_vm is not null) _vm.StatusText = checking;
        bool? found = await u.CheckAsync();
        if (_vm is not null && _vm.StatusText == checking) _vm.StatusText = "";
        var title = "Check for updates";

        if (found is null)
        {
            await new ConfirmWindow(title, "Couldn't reach GitHub to check for a newer version of Nota. Check your internet connection and try again.",
                "OK", null).ShowDialog<bool>(this);
            return;
        }
        if (found == false || u.Release is not { } release)
        {
            await new ConfirmWindow(title, $"You're up to date: Nota {AppInfo.Version} is the latest version.", "OK", null).ShowDialog<bool>(this);
            return;
        }

        u.Undismiss();
        switch (u.State)
        {
            case AppUpdates.Stage.Downloading:
                return;   // its progress is in the status bar already
            case AppUpdates.Stage.Ready:
                if (await new ConfirmWindow(title, $"Nota {release.Version} is ready to install. Restart Nota to finish updating — or it installs the next time you quit.",
                        "Restart now", "Later").ShowDialog<bool>(this))
                    u.Restart(Close);
                return;
            default:
                var message = $"Nota {release.Version} is out — you have {AppInfo.Version}."
                    + (u.CanInstall ? " Nota downloads it here and installs it when you restart or quit." : " Download it from the release page.");
                if (await new ConfirmWindow("Update available", message, u.CanInstall ? "Update" : "Download", "Later").ShowDialog<bool>(this))
                    u.Download(this);
                return;
        }
    }
}
