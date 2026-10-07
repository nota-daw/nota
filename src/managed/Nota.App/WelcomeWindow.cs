// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Welcome screen — the launcher shown on startup (see MainWindow.ShowWelcomeAsync), as
// drawn in nota-design/Nota Start.html. Brand header, New / Open actions, a list of
// recent projects (click selects, double-click / Return opens, ↑ ↓ move), and shortcuts to
// Settings and What's New, plus banners for crash recovery and a newer release on
// GitHub (which downloads and installs it in place — see IAppUpdater). New / Open / a recent project close the launcher and hand
// off to the main window; Settings and What's New open as child dialogs so the user
// stays on the launcher.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        // --- Footer: show-on-startup + Documentation / Settings / What's New --------------
        var footer = new Grid
        {
            Margin = new Thickness(0, 20, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
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

        // The user manual, for a first launch that wants a guided start.
        var docsBtn = FooterLink("Documentation");
        docsBtn.Click += (_, _) => NotaDocs.Open(this, "start-here/first-track");
        Grid.SetColumn(docsBtn, 2);
        footer.Children.Add(docsBtn);

        var settingsBtn = FooterLink("Settings");
        settingsBtn.Margin = new Thickness(8, 0, 0, 0);
        settingsBtn.Click += (_, _) => onSettings();
        Grid.SetColumn(settingsBtn, 3);
        footer.Children.Add(settingsBtn);

        var whatsNewBtn = FooterLink("What's New");
        whatsNewBtn.Margin = new Thickness(8, 0, 0, 0);
        whatsNewBtn.Click += (_, _) => onWhatsNew();
        Grid.SetColumn(whatsNewBtn, 4);
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

    /// <summary>Show the "new version available" banner. When this install can update itself
    /// it downloads in place (progress in the banner), then offers Restart now / Later — Later
    /// installs on quit. Otherwise Download opens the release page. Called once the background
    /// update check resolves.</summary>
    public void ShowUpdateAvailable(AppRelease release, IAppUpdater updater, Action restart)
    {
        new UpdateBanner(release, updater, restart, _banners).Attach();
    }

    // The update banner's states: offer → downloading → ready (or failed → try again).
    private sealed class UpdateBanner
    {
        private readonly AppRelease _release;
        private readonly IAppUpdater _updater;
        private readonly Action _restart;
        private readonly StackPanel _host;
        private readonly Border _card;
        private CancellationTokenSource? _cts;

        public UpdateBanner(AppRelease release, IAppUpdater updater, Action restart, StackPanel host)
        {
            _release = release;
            _updater = updater;
            _restart = restart;
            _host = host;
            _card = BannerCard();
        }

        public void Attach()
        {
            if (_updater.PendingVersion == _release.Version) ShowReady();
            else ShowOffer();
            _host.Children.Add(_card);
        }

        private bool CanUpdate => _release.Asset is not null && _updater.CanInstallInPlace;

        private void ShowOffer()
        {
            var openPage = () => { _ = Launcher(_card)?.LaunchUriAsync(new Uri(_release.PageUrl)); };
            _card.Child = CanUpdate
                ? BannerContent("A new version of Nota is available",
                    $"Nota {_release.Version} is out — you have {AppInfo.Version}.",
                    progress: null,
                    ("Update", true, () => _ = DownloadAsync()),
                    ("Release notes", false, openPage),
                    ("Dismiss", false, Remove))
                : BannerContent("A new version of Nota is available",
                    $"Nota {_release.Version} is out — you have {AppInfo.Version}.",
                    progress: null,
                    ("Download", true, openPage),
                    ("Dismiss", false, Remove));
        }

        private async Task DownloadAsync()
        {
            _cts = new CancellationTokenSource();
            var bar = new ProgressBar
            {
                Minimum = 0, Maximum = 1, Height = 4, Margin = new Thickness(0, 6, 0, 0),
                Foreground = NotaPalette.Accent, Background = NotaPalette.BorderDefault,
            };
            var size = _release.Asset!.Size / 1048576.0;
            var content = BannerContent($"Downloading Nota {_release.Version}…",
                $"0.0 / {NotaNum.Unit(size, "0.0", "MB")}", bar,
                ("Cancel", false, () => _cts?.Cancel()));
            _card.Child = content;
            var message = (TextBlock)((StackPanel)content.Children[0]).Children[1];   // BannerContent's message line

            var progress = new Progress<StoreProgress>(p =>
            {
                if (p.Fraction < 0)
                {
                    bar.IsIndeterminate = true;
                    message.Text = p.Message;
                    return;
                }
                bar.Value = p.Fraction;
                message.Text = $"{(p.Fraction * size).ToString("0.0", NotaNum.Culture)} / {NotaNum.Unit(size, "0.0", "MB")}"
                               + $"  ·  {NotaNum.Unit(p.Fraction * 100, "0", "%")}";
            });
            try
            {
                await _updater.DownloadAsync(_release, progress, _cts.Token);
                ShowReady();
            }
            catch (OperationCanceledException) { ShowOffer(); }
            catch (Exception e) { ShowFailed(e is StoreException ? e.Message : $"Couldn't download the update: {e.Message}"); }
            finally { _cts.Dispose(); _cts = null; }
        }

        private void ShowReady()
        {
            _card.Child = BannerContent($"Nota {_release.Version} is ready to install",
                "Restart Nota to finish updating — or it installs the next time you quit.",
                progress: null,
                ("Restart now", true, () => { _updater.RelaunchAfterInstall = true; _restart(); }),
                ("Later", false, Remove));
        }

        private void ShowFailed(string error)
        {
            _card.Child = BannerContent("The update didn't download", error,
                progress: null,
                ("Try again", true, () => _ = DownloadAsync()),
                ("Dismiss", false, Remove));
        }

        private void Remove()
        {
            _cts?.Cancel();
            _host.Children.Remove(_card);
        }

        private static Avalonia.Platform.Storage.ILauncher? Launcher(Visual v) => TopLevel.GetTopLevel(v)?.Launcher;
    }

    // Offer to restore a crashed session.
    private static Border RecoveryBanner(string message, Action onRecover, Action onDismiss)
        => Banner("Unsaved work available", message, "Recover", onRecover, onDismiss);

    // A dismissible alert card with one action. Accent-tinted so it reads as an action
    // prompt, not an error.
    private static Border Banner(string title, string message, string actionLabel,
                                 Action onAction, Action onDismiss)
    {
        var card = BannerCard();
        card.Child = BannerContent(title, message, progress: null,
            (actionLabel, true, onAction), ("Dismiss", false, onDismiss));
        return card;
    }

    private static Border BannerCard() => new()
    {
        Background = NotaPalette.AccentSubtle,
        BorderBrush = NotaPalette.Accent,
        BorderThickness = new Thickness(1),
        CornerRadius = NotaRadius.Panel,
        Padding = new Thickness(14, 12),
        Margin = new Thickness(28, 16, 28, 0),
    };

    // Title + message (+ an optional progress bar) on the left, buttons on the right. The
    // first "main" button is a plain button (not solid brass: "New project" is this window's
    // primary action); the rest are ghosts.
    private static Grid BannerContent(string title, string message, Control? progress,
                                      params (string Label, bool Main, Action OnClick)[] actions)
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
        if (progress is not null) text.Children.Add(progress);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*" + string.Concat(Enumerable.Repeat(",Auto", actions.Length))) };
        grid.Children.Add(text);
        for (int i = 0; i < actions.Length; i++)
        {
            var (label, main, onClick) = actions[i];
            var button = new Button
            {
                Content = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(i == 0 ? 12 : 6, 0, 0, 0),
            };
            if (!main) button.Classes.Add("ghost");
            button.Click += (_, _) => onClick();
            Grid.SetColumn(button, i + 1);
            grid.Children.Add(button);
        }
        return grid;
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
