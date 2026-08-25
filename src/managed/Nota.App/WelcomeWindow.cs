// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Welcome screen — the launcher shown on startup (see MainWindow.ShowWelcomeAsync).
// Brand header, New / Open actions, a list of recent projects, and shortcuts to
// Settings and What's New. New / Open / a recent project close the launcher and hand
// off to the main window; Settings and What's New open as child dialogs so the user
// stays on the launcher.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Nota.App;

/// <summary>One row on the welcome screen's recent-projects list.</summary>
public sealed record RecentProjectItem(string Name, string Path, string Modified);

public sealed class WelcomeWindow : NotaWindow
{
    public WelcomeWindow(
        IReadOnlyList<RecentProjectItem> recent,
        Action onNew,
        Action onOpen,
        Action<string> onOpenRecent,
        Action onSettings,
        Action onWhatsNew,
        bool showOnStartup,
        Action<bool> onShowOnStartupChanged,
        string? recoveryMessage = null,
        Action? onRecover = null,
        Action? onDismissRecovery = null)
    {
        Title = "Welcome to Nota";
        Width = 640;
        Height = 560;   // + title-bar band
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = NotaPalette.BgApp;

        var root = new Grid
        {
            Margin = new Thickness(28, 20, 28, 24),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
        };

        // --- Brand header --------------------------------------------------
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var logo = new Image { Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Center };
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://Nota.App/Assets/logo.png"));
            logo.Source = new Bitmap(s);
        }
        catch { /* decorative */ }
        Grid.SetColumn(logo, 0);
        header.Children.Add(logo);

        var brand = new StackPanel
        {
            Margin = new Thickness(16, 0, 0, 0),
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        brand.Children.Add(new TextBlock
        {
            Text = "Nota",
            FontSize = 26,
            FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextPrimary,
        });
        brand.Children.Add(new TextBlock
        {
            Classes = { "Caption" },
            Text = $"Version {AppInfo.Version} · Desktop DAW",
        });
        Grid.SetColumn(brand, 1);
        header.Children.Add(brand);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // --- Primary actions ----------------------------------------------
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 24, 0, 20),
        };
        var newBtn = new Button { Content = "New project", Classes = { "primary" } };
        newBtn.Click += (_, _) => { Close(); onNew(); };
        var openBtn = new Button { Content = "Open project…", Classes = { "secondary" } };
        openBtn.Click += (_, _) => { Close(); onOpen(); };
        actions.Children.Add(newBtn);
        actions.Children.Add(openBtn);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        // --- Recent projects ----------------------------------------------
        var recentLabel = new TextBlock
        {
            Classes = { "SectionLabel" },
            Text = "Recent projects",
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(recentLabel, 2);
        root.Children.Add(recentLabel);

        Control recentBody;
        if (recent.Count == 0)
        {
            recentBody = new TextBlock
            {
                Classes = { "Caption" },
                Text = "No recent projects yet — create one to get started.",
                VerticalAlignment = VerticalAlignment.Top,
            };
        }
        else
        {
            var list = new StackPanel { Spacing = 4 };
            foreach (var item in recent)
            {
                // Match New / Open: close the launcher, then hand off to the main window.
                list.Children.Add(RecentRow(item, p => { Close(); onOpenRecent(p); }));
            }
            recentBody = new ScrollViewer
            {
                Content = list,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            };
        }
        Grid.SetRow(recentBody, 3);
        root.Children.Add(recentBody);

        // --- Footer: show-on-startup + Settings / What's New --------------
        var footer = new Grid
        {
            Margin = new Thickness(0, 16, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
        };

        var startupCheck = new CheckBox
        {
            Content = "Show on startup",
            IsChecked = showOnStartup,
            VerticalAlignment = VerticalAlignment.Center,
        };
        startupCheck.IsCheckedChanged += (_, _) => onShowOnStartupChanged(startupCheck.IsChecked == true);
        Grid.SetColumn(startupCheck, 0);
        footer.Children.Add(startupCheck);

        var settingsBtn = new Button { Content = "Settings", Classes = { "ghost" } };
        settingsBtn.Click += (_, _) => onSettings();
        Grid.SetColumn(settingsBtn, 1);
        footer.Children.Add(settingsBtn);

        var whatsNewBtn = new Button
        {
            Content = "What's New",
            Classes = { "ghost" },
            Margin = new Thickness(8, 0, 0, 0),
        };
        whatsNewBtn.Click += (_, _) => onWhatsNew();
        Grid.SetColumn(whatsNewBtn, 2);
        footer.Children.Add(whatsNewBtn);

        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        // Crash-recovery prompt rides on top as a dismissible banner (same wording as the
        // standalone recovery dialog). Absent when there's no surviving snapshot.
        Control body = root;
        if (recoveryMessage is not null && onRecover is not null)
        {
            var outer = new DockPanel();
            Border banner = null!;
            banner = RecoveryBanner(
                recoveryMessage,
                onRecover,
                onDismiss: () =>
                {
                    onDismissRecovery?.Invoke();
                    (banner.Parent as Panel)?.Children.Remove(banner);
                });
            DockPanel.SetDock(banner, Dock.Top);
            outer.Children.Add(banner);
            outer.Children.Add(root);
            body = outer;
        }

        SetBody(body);
    }

    // A dismissible alert card offering to restore a crashed session. Accent-tinted so it
    // reads as an action prompt, not an error.
    private static Border RecoveryBanner(string message, Action onRecover, Action onDismiss)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = "Unsaved work available",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextPrimary,
        });
        text.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            Foreground = NotaPalette.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(text, 0);

        var recover = new Button
        {
            Content = "Recover",
            Classes = { "primary" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        recover.Click += (_, _) => onRecover();
        Grid.SetColumn(recover, 1);

        var dismiss = new Button
        {
            Content = "Dismiss",
            Classes = { "ghost" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        dismiss.Click += (_, _) => onDismiss();
        Grid.SetColumn(dismiss, 2);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        grid.Children.Add(text);
        grid.Children.Add(recover);
        grid.Children.Add(dismiss);

        return new Border
        {
            Background = NotaPalette.AccentSubtle,
            BorderBrush = NotaPalette.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14, 12),
            Margin = new Thickness(28, 16, 28, 0),
            Child = grid,
        };
    }

    // A clickable card for one recent project: name over its path, modified date on the
    // right. A ghost Button gives the standard hover wash for free.
    private static Button RecentRow(RecentProjectItem item, Action<string> onOpenRecent)
    {
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontSize = 13,
            Foreground = NotaPalette.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = item.Path,
            FontSize = 11,
            Foreground = NotaPalette.TextTertiary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var date = new TextBlock
        {
            Text = item.Modified,
            FontSize = 11,
            Foreground = NotaPalette.TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(text, 0);
        Grid.SetColumn(date, 1);
        grid.Children.Add(text);
        grid.Children.Add(date);

        var btn = new Button
        {
            Classes = { "ghost" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8),
            Content = grid,
        };
        ToolTip.SetTip(btn, item.Path);
        btn.Click += (_, _) => onOpenRecent(item.Path);
        return btn;
    }
}
