// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Welcome screen — the launcher shown on startup (see MainWindow.ShowWelcomeAsync), as
// drawn in nota-design/Nota Start.html. Brand header, New / Open actions, a list of
// recent projects (click selects, double-click / Return opens, ↑ ↓ move), and shortcuts to
// Settings and What's New, plus banners for crash recovery and a newer release on
// GitHub. New / Open / a recent project close the launcher and hand
// off to the main window; Settings and What's New open as child dialogs so the user
// stays on the launcher.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

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
        Height = 560;   // including the title-bar band
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = NotaPalette.Panel;
        BlendTitleBar();

        _recent = recent;
        _onOpenRecent = onOpenRecent;

        var root = new Grid
        {
            Margin = new Thickness(28, 12, 28, 20),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
        };

        // --- Brand header --------------------------------------------------
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        header.Children.Add(new BrandMark { VerticalAlignment = VerticalAlignment.Center });
        var brand = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new TextBlock
        {
            Text = "Nota",
            FontSize = NotaType.Title,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = -0.4,
            LineHeight = NotaType.Title,
            Foreground = NotaPalette.TextHeading,
        });
        brand.Children.Add(new TextBlock
        {
            Text = $"Version {AppInfo.Version} · Engine {App.Services.GetRequiredService<EngineBuildInfo>().Version} · Desktop DAW",
            FontFamily = NotaFonts.MonoFamily,
            FontSize = NotaType.Value,
            Foreground = NotaPalette.TextMuted,
        });
        header.Children.Add(brand);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // --- Primary actions ----------------------------------------------
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 20, 0, 20),
        };
        var newBtn = new Button { Content = "New project", Classes = { "primary" }, FontWeight = FontWeight.SemiBold };
        newBtn.Click += (_, _) => { Close(); onNew(); };
        var openBtn = new Button { Content = "Open project…" };
        openBtn.Click += (_, _) => { Close(); onOpen(); };
        actions.Children.Add(newBtn);
        actions.Children.Add(openBtn);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        // --- Recent projects ----------------------------------------------
        var recentHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        recentHead.Children.Add(new TextBlock { Classes = { "SectionLabel" }, Text = "RECENT PROJECTS", VerticalAlignment = VerticalAlignment.Bottom });
        recentHead.Children.Add(new TextBlock
        {
            Text = recent.Count.ToString(NotaNum.Culture),
            FontFamily = NotaFonts.MonoFamily,
            FontSize = NotaType.Eyebrow,
            Foreground = NotaPalette.TextDisabled,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 1),
        });

        Control wellBody;
        if (recent.Count == 0)
        {
            wellBody = new TextBlock
            {
                Text = "No recent projects",
                FontSize = NotaType.Body,
                Foreground = NotaPalette.TextTertiary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            var list = new StackPanel();
            for (int i = 0; i < recent.Count; i++)
            {
                var row = new RecentRow(recent[i]);
                int index = i;
                row.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                    Select(index);
                    if (e.ClickCount == 2) OpenRecent(index);
                    e.Handled = true;
                };
                _rows.Add(row);
                list.Children.Add(row);
            }
            wellBody = _scroll = new ScrollViewer
            {
                Content = list,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            };
            Select(0);
        }

        var well = new Border
        {
            Background = NotaPalette.BgSunken,
            BorderBrush = NotaPalette.GraphBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Panel,
            Padding = new Thickness(4),
            ClipToBounds = true,
            Child = wellBody,
        };
        well[!Border.BoxShadowProperty] = well.GetResourceObservable("Shadow.Sunken").ToBinding();

        var recentPanel = new DockPanel();
        DockPanel.SetDock(recentHead, Dock.Top);
        recentPanel.Children.Add(recentHead);
        recentPanel.Children.Add(well);
        Grid.SetRow(recentPanel, 2);
        root.Children.Add(recentPanel);

        // --- Footer: show-on-startup + Settings / What's New --------------
        var footer = new Grid
        {
            Margin = new Thickness(0, 20, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
        };

        var startupTrack = new SwitchTrack { IsOn = showOnStartup };
        var startup = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    startupTrack,
                    new TextBlock
                    {
                        Text = "Show on startup",
                        FontSize = NotaType.Body,
                        Foreground = NotaPalette.TextStrong,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        startup.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(startup).Properties.IsLeftButtonPressed) return;
            startupTrack.IsOn = !startupTrack.IsOn;
            onShowOnStartupChanged(startupTrack.IsOn);
            e.Handled = true;
        };
        Grid.SetColumn(startup, 0);
        footer.Children.Add(startup);

        var settingsBtn = FooterLink("Settings");
        settingsBtn.Click += (_, _) => onSettings();
        Grid.SetColumn(settingsBtn, 2);
        footer.Children.Add(settingsBtn);

        var whatsNewBtn = FooterLink("What's New");
        whatsNewBtn.Margin = new Thickness(8, 0, 0, 0);
        whatsNewBtn.Click += (_, _) => onWhatsNew();
        Grid.SetColumn(whatsNewBtn, 3);
        footer.Children.Add(whatsNewBtn);

        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        // Banners (crash recovery, available update) stack above the content. The update
        // banner arrives asynchronously — see ShowUpdateAvailable.
        _banners = new StackPanel();
        var outer = new DockPanel();
        DockPanel.SetDock(_banners, Dock.Top);
        outer.Children.Add(_banners);
        outer.Children.Add(root);

        // Crash-recovery prompt (same wording as the standalone recovery dialog). Absent
        // when there's no surviving snapshot.
        if (recoveryMessage is not null && onRecover is not null)
        {
            Border banner = null!;
            banner = RecoveryBanner(
                recoveryMessage,
                onRecover,
                onDismiss: () =>
                {
                    onDismissRecovery?.Invoke();
                    _banners.Children.Remove(banner);
                });
            _banners.Children.Add(banner);
        }

        SetBody(outer);
    }

    private readonly IReadOnlyList<RecentProjectItem> _recent;
    private readonly Action<string> _onOpenRecent;
    private readonly List<RecentRow> _rows = new();
    private ScrollViewer? _scroll;
    private int _selected = -1;
    private bool _opening;

    // ↑ / ↓ move the selection through the recent list, Return opens it.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_rows.Count > 0 && !_opening)
        {
            switch (e.Key)
            {
                case Key.Down: Select(Math.Min(_rows.Count - 1, _selected + 1)); e.Handled = true; break;
                case Key.Up: Select(Math.Max(0, _selected - 1)); e.Handled = true; break;
                case Key.Enter: OpenRecent(_selected); e.Handled = true; break;
            }
        }
        if (!e.Handled) base.OnKeyDown(e);
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        if (_selected >= 0) _rows[_selected].IsSelected = false;
        _selected = index;
        _rows[index].IsSelected = true;
        _rows[index].BringIntoView();
    }

    // Mark the row "Opening…", let that frame land, then close the launcher and hand the
    // project to the main window (loading blocks the UI thread).
    private void OpenRecent(int index)
    {
        if (_opening || index < 0 || index >= _rows.Count) return;
        _opening = true;
        _rows[index].ShowOpening();
        var path = _recent[index].Path;
        Dispatcher.UIThread.Post(() => { Close(); _onOpenRecent(path); }, DispatcherPriority.Background);
    }

    // A quiet text button in the footer: Ink 3, a hover wash, no face.
    private static Button FooterLink(string text) => new()
    {
        Content = text,
        Classes = { "ghost" },
        Padding = new Thickness(10, 0),
        Foreground = NotaPalette.TextSecondary,
    };

    private readonly StackPanel _banners;

    /// <summary>Show a "new version available" banner with a Download button that opens
    /// the release page in the browser. Called once the background update check resolves.</summary>
    public void ShowUpdateAvailable(AvailableUpdate update)
    {
        Border banner = null!;
        banner = Banner(
            "A new version of Nota is available",
            $"Nota {update.Version} is out — you have {AppInfo.Version}.",
            actionLabel: "Download",
            onAction: () => _ = Launcher.LaunchUriAsync(new Uri(update.Url)),
            onDismiss: () => _banners.Children.Remove(banner));
        _banners.Children.Add(banner);
    }

    // Offer to restore a crashed session.
    private static Border RecoveryBanner(string message, Action onRecover, Action onDismiss)
        => Banner("Unsaved work available", message, "Recover", onRecover, onDismiss);

    // A dismissible alert card with one action. Accent-tinted so it reads as an action
    // prompt, not an error.
    private static Border Banner(string title, string message, string actionLabel,
                                 Action onAction, Action onDismiss)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = title,
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
            Content = actionLabel,   // not solid brass: "New project" is this window's primary action
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        recover.Click += (_, _) => onAction();
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
            CornerRadius = NotaRadius.Panel,
            Padding = new Thickness(14, 12),
            Margin = new Thickness(28, 16, 28, 0),
            Child = grid,
        };
    }

    // One line of the recent list: name left, modified date right in mono. The selected
    // row takes the brass wash and a 2px brass edge; hover washes the others.
    private sealed class RecentRow : Border
    {
        private readonly Border _edge;
        private readonly TextBlock _name, _meta;
        private bool _selected, _hover;

        public RecentRow(RecentProjectItem item)
        {
            Height = 28;
            CornerRadius = NotaRadius.Badge;
            Padding = new Thickness(12, 0, 10, 0);
            Transitions = new Transitions
            {
                new BrushTransition { Property = BackgroundProperty, Duration = TimeSpan.FromMilliseconds(120), Easing = new CubicEaseOut() },
            };
            ToolTip.SetTip(this, item.Path);

            _edge = new Border
            {
                Width = 2,
                Margin = new Thickness(-12, 7, 0, 7),
                CornerRadius = NotaRadius.Bar,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            _name = new TextBlock
            {
                Text = item.Name,
                FontSize = NotaType.Name,
                FontWeight = FontWeight.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _meta = new TextBlock
            {
                Text = item.Modified,
                FontFamily = NotaFonts.MonoFamily,
                FontSize = NotaType.Value,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(_meta, 1);
            grid.Children.Add(_name);
            grid.Children.Add(_meta);

            var layer = new Panel();
            layer.Children.Add(grid);
            layer.Children.Add(_edge);
            Child = layer;

            PointerEntered += (_, _) => { _hover = true; Paint(); };
            PointerExited += (_, _) => { _hover = false; Paint(); };
            Paint();
        }

        public bool IsSelected { get => _selected; set { _selected = value; Paint(); } }

        public void ShowOpening()
        {
            _meta.Text = "Opening…";
            _meta.Foreground = NotaPalette.AccentHover;
        }

        private void Paint()
        {
            Background = _selected ? NotaPalette.AccentSubtle : _hover ? NotaPalette.SurfaceRaised : NotaPalette.Wash(NotaPalette.SurfaceRaised, 0);
            _edge.Background = _selected ? NotaPalette.Accent : null;
            _name.Foreground = _selected ? NotaPalette.TextPrimary : NotaPalette.TextStrong;
            _meta.Foreground = _selected ? NotaPalette.TextSecondary : NotaPalette.TextMuted;
        }
    }
}
