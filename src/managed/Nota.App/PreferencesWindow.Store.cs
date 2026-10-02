// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preferences → Downloads → Plug-ins: browse the Nota plugin registry and install / update /
// remove open-source VST3 plugins (IPluginStore). One install at a time across all of Downloads
// (DownloadJobs); it keeps running when the user switches panes or closes the window, and then
// shows in the main window's status bar. Here its progress and outcome show in a dock at the
// bottom of the window (visible from every pane), which also cancels it; Sample Packs and AI
// Models share that dock. After every change the catalog is rescanned so the browser lists the
// new plugins.
//
// The list is virtualized (only the rows in view exist), so filtering and searching the
// whole registry stays instant. Plugins without a build for this computer sink to the end,
// under their own heading.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private static readonly string[] StoreFilters = { "All", "Instruments", "Effects", "Installed" };

    private readonly IPluginStore _store = App.Services.GetRequiredService<IPluginStore>();
    private IReadOnlyList<StorePlugin>? _storePlugins;
    private int _storeFilter;
    private string _storeSearch = "";
    private static DownloadJobs Jobs => DownloadJobs.Shared;   // the running install / removal, app-wide
    private string? _storeMessage;                    // registry loading / error, shown under the toolbar
    private Task? _storeLoad;
    private bool _storeLoading;                       // fetching the registry index
    private ContentControl? _storeList;               // null while another pane is showing
    private ItemsControl? _storeItems;                // the virtualized rows inside _storeList
    private TextBlock[]? _storeCounts;                // mono counts on the filter segments
    private TextBlock? _storeStatus;
    private Button? _storeRefresh;
    private ProgressBar? _storeLoadBar;               // over the list while a refresh runs

    // List items: a plugin row, or the heading above the plugins this computer can't install.
    private sealed record StoreEntry(StorePlugin Plugin, InstalledStorePlugin? Installed, bool First);
    private sealed record StoreSection(string Title, int Count);

    // The download dock (built once with the window).
    private Border _dock = null!;
    private TextBlock _dockTitle = null!, _dockDetail = null!;
    private ProgressBar _dockBar = null!;
    private Button _dockCancel = null!;
    private Control _dockClose = null!;

    private const string StoreIntro = "Open-source VST3 plugins from the Nota plugin registry. Each one downloads from the project's own GitHub release, is checked against the registry's checksum and unpacked — installers never run.";

    // The Plug-ins half of Downloads (PreferencesWindow.Downloads.cs): its toolbar pins to the
    // top while the body scrolls.
    private (Control Toolbar, Control Body) StoreParts()
    {
        // Filter segments carry a mono count; RenderStoreList keeps the counts current.
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var segs = new List<ToggleButton>();
        _storeCounts = new TextBlock[StoreFilters.Length];
        for (int i = 0; i < StoreFilters.Length; i++)
        {
            int idx = i;
            var count = new TextBlock { FontSize = 9, FontFamily = NotaFonts.MonoFamily, VerticalAlignment = VerticalAlignment.Center };
            _storeCounts[i] = count;
            var b = new ToggleButton
            {
                Classes = { "seg" }, FontSize = 12, IsChecked = i == _storeFilter,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { new TextBlock { Text = StoreFilters[i], VerticalAlignment = VerticalAlignment.Center }, count },
                },
            };
            b.Click += (_, _) =>
            {
                for (int k = 0; k < segs.Count; k++) segs[k].IsChecked = k == idx;
                _storeFilter = idx;
                RenderStoreList();
            };
            segs.Add(b);
            strip.Children.Add(b);
        }
        var search = new TextBox { PlaceholderText = "Search", Text = _storeSearch, Classes = { "search" }, FontSize = 12 };
        search.TextChanged += (_, _) => { _storeSearch = search.Text ?? ""; RenderStoreList(); };
        var refresh = new Button { Content = "Refresh", Classes = { "ghost" }, Height = 30, FontSize = 12, Padding = new Thickness(12, 0), Foreground = TextSecondary };
        ToolTip.SetTip(refresh, "Reload the registry");
        refresh.Click += (_, _) => { if (!_storeLoading) _storeLoad = LoadStoreAsync(refresh: true); };
        _storeRefresh = refresh;

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        toolbar.Children.Add(new Border { Classes = { "segmented" }, Child = strip });
        var searchField = SearchField(search, double.PositiveInfinity);
        searchField.MinWidth = 140;
        Grid.SetColumn(searchField, 1); toolbar.Children.Add(searchField);
        Grid.SetColumn(refresh, 2); toolbar.Children.Add(refresh);

        var list = _storeList = new ContentControl();
        _storeStatus = Caption("");
        _storeLoadBar = new ProgressBar { IsIndeterminate = true, Height = 3, MinWidth = 0, IsVisible = false };
        _storeItems = StoreItems();

        var body = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                _storeStatus,
                new StackPanel { Spacing = 6, Children = { _storeLoadBar, _storeList } },
                Caption($"Installed to {_store.PluginsDir}"),
            },
        };
        // Only forget the controls if they are still these: switching to Sample Packs and back
        // builds the new ones before the old body leaves the tree.
        body.DetachedFromVisualTree += (_, _) =>
        {
            if (_storeList != list) return;
            _storeList = null; _storeItems = null; _storeStatus = null; _storeCounts = null; _storeRefresh = null; _storeLoadBar = null;
        };
        EnsureStoreLoading();
        RenderStoreList();
        return (toolbar, body);
    }

    // The registry index is cached on disk, so the window starts this on open: the sidebar's
    // Downloads count needs it before the pane is ever shown.
    private void EnsureStoreLoading()
    {
        if (_storePlugins is null && _storeLoad is not { IsCompleted: false }) _storeLoad = LoadStoreAsync(refresh: false);
    }

    private async Task LoadStoreAsync(bool refresh)
    {
        _storeLoading = true;
        _storeMessage = null;
        RenderStoreList();
        try
        {
            _storePlugins = await _store.FetchAsync(refresh);
        }
        catch (StoreException e)
        {
            _storeMessage = e.Message;
        }
        finally { _storeLoading = false; }
        RenderStoreList();
    }

    // The rows: an ItemsControl on a VirtualizingStackPanel. The panel follows its effective
    // viewport, so it only realizes the rows in view even inside the pane's ScrollViewer.
    private ItemsControl StoreItems() => new()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
        // -1 bottom: the last row's hairline slides under the table's own border (clipped).
        Margin = new Thickness(0, 0, 0, -1),
        DataTemplates =
        {
            new FuncDataTemplate<StoreEntry>((e, _) => e is null ? new Panel() : new Border
            {
                Background = RowBg, BorderBrush = Hairline, BorderThickness = new Thickness(0, e.First ? 0 : 1, 0, 0),
                Child = StoreRow(e.Plugin, e.Installed),
            }),
            new FuncDataTemplate<StoreSection>((h, _) => h is null ? new Panel() : new Border
            {
                Background = Sunken, BorderBrush = Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 9),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = h.Title, FontSize = 9, FontWeight = FontWeight.Bold, LetterSpacing = 1.1, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = h.Count.ToString(), FontSize = 9, FontFamily = NotaFonts.MonoFamily, Foreground = TextDisabled, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            }),
        },
    };

    // The first registry load has nothing to show yet: a bar and a line in an empty table.
    private Control StoreLoadingBlock() => Table(new List<Control>
    {
        new StackPanel
        {
            Spacing = 10, Margin = new Thickness(28, 36), HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new ProgressBar { IsIndeterminate = true, Width = 180, Height = 4, MinWidth = 0 },
                new TextBlock { Text = "Loading the plugin registry…", FontSize = 12, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center },
            },
        },
    });

    private static string CurrentOsName =>
        OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "this computer";

    private void RenderStoreList()
    {
        UpdateStoreBadge();
        if (_storeList is null || _storeItems is null) return;
        UpdateStoreStatus();
        if (_storeRefresh is not null)
        {
            _storeRefresh.IsEnabled = !_storeLoading;
            _storeRefresh.Content = _storeLoading && _storePlugins is not null ? "Refreshing…" : "Refresh";
        }
        if (_storeLoadBar is not null) _storeLoadBar.IsVisible = _storeLoading && _storePlugins is not null;
        if (_storePlugins is null)
        {
            _storeList.Content = _storeLoading ? StoreLoadingBlock() : null;
            UpdateStoreCounts(null);
            return;
        }

        var installed = _store.Installed.ToDictionary(i => i.Id);
        UpdateStoreCounts(installed);
        var q = _storeSearch.Trim();
        var shown = _storePlugins.Where(p => MatchesFilter(p, _storeFilter, installed))
            .Where(p => q.Length == 0
                        || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Developer.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (shown.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = _storeFilter == 3 && q.Length == 0 ? "Nothing installed from the registry yet." : "Nothing matches.",
                FontSize = 12, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(28),
            };
            _storeList.Content = Table(new List<Control> { empty });
            return;
        }

        // What this computer can install first; the rest after a heading, registry order kept.
        var items = new List<object>(shown.Count + 1);
        var available = shown.Where(p => p.Asset is not null).ToList();
        var unavailable = shown.Where(p => p.Asset is null).ToList();
        items.AddRange(available.Select((p, i) => new StoreEntry(p, installed.GetValueOrDefault(p.Id), First: i == 0)));
        if (unavailable.Count > 0)
        {
            // The heading sits at the very top when nothing else matched — no rule above it then.
            if (available.Count > 0) items.Add(new StoreSection("NOT AVAILABLE FOR THIS COMPUTER", unavailable.Count));
            items.AddRange(unavailable.Select(p => new StoreEntry(p, installed.GetValueOrDefault(p.Id), First: false)));
        }
        if (available.Count == 0 && items[0] is StoreEntry first) items[0] = first with { First = true };
        _storeItems.ItemsSource = items;
        if (_storeList.Content is not Border { Tag: "store-table" })
            _storeList.Content = new Border
            {
                Tag = "store-table", BorderBrush = Divider, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel,
                ClipToBounds = true, Child = _storeItems,
            };
    }

    private static bool MatchesFilter(StorePlugin p, int filter, IReadOnlyDictionary<string, InstalledStorePlugin> installed) => filter switch
    {
        1 => p.Kind is "instrument",
        2 => p.Kind is "effect" or "midi" or "bundle",
        3 => installed.ContainsKey(p.Id),
        _ => true,
    };

    private void UpdateStoreCounts(IReadOnlyDictionary<string, InstalledStorePlugin>? installed)
    {
        if (_storeCounts is null) return;
        for (int i = 0; i < _storeCounts.Length; i++)
        {
            _storeCounts[i].Text = _storePlugins is null || installed is null ? "" : _storePlugins.Count(p => MatchesFilter(p, i, installed)).ToString();
            // On the brass segment the count drops to the brass edge; off it, Ink 6.
            _storeCounts[i].Foreground = i == _storeFilter ? NotaPalette.AccentEdge : TextDisabled;
        }
    }

    // Sidebar: how many registry plugins this computer could still install.
    private void UpdateStoreBadge()
    {
        if (_storePlugins is null) return;
        var installed = _store.Installed.Select(i => i.Id).ToHashSet();
        UpdateDownloadsBadge(_storePlugins.Count(p => p.Asset is not null && !installed.Contains(p.Id)));
    }

    private void UpdateStoreStatus()
    {
        UpdateDock();
        if (_storeStatus is null) return;
        _storeStatus.Text = _storeMessage ?? "";
        _storeStatus.IsVisible = _storeStatus.Text.Length > 0;
    }

    // Bottom dock: the running install / removal (message, bytes, bar, Cancel), or the last
    // outcome with a close button. Hidden when there's neither.
    private Control StoreDock()
    {
        _dockTitle = new TextBlock { FontSize = 12, FontWeight = FontWeight.Medium, Foreground = TextPrimary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _dockDetail = new TextBlock { FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        _dockBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 4, MinWidth = 0 };
        _dockCancel = StoreButton("Cancel", enabled: true, Jobs.Cancel);
        _dockCancel.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(_dockCancel, "Stop the download — nothing is changed");
        _dockClose = new Button
        {
            Content = new Glyph(GlyphKind.Close, 10), Classes = { "ghost" }, Width = 26, Height = 26, Padding = new Thickness(0),
            Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        };
        ((Button)_dockClose).Click += (_, _) => Jobs.Dismiss();

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        head.Children.Add(_dockTitle);
        Grid.SetColumn(_dockDetail, 1); head.Children.Add(_dockDetail);
        var left = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Children = { head, _dockBar } };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 16 };
        grid.Children.Add(left);
        Grid.SetColumn(_dockCancel, 1); grid.Children.Add(_dockCancel);
        Grid.SetColumn(_dockClose, 2); grid.Children.Add(_dockClose);

        _dock = new Border
        {
            Background = Sidebar, BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(32, 12, 24, 12), MinHeight = 56, Child = grid, IsVisible = false,
        };
        return _dock;
    }

    private void UpdateDock()
    {
        bool busy = Jobs.Busy;
        _dock.IsVisible = busy || Jobs.Result is not null;
        if (!_dock.IsVisible) return;

        var pr = Jobs.Progress;
        _dockTitle.Text = busy ? pr.Message : Jobs.Result;
        _dockTitle.Foreground = busy ? TextPrimary : TextSecondary;
        _dockBar.IsVisible = busy;
        _dockBar.IsIndeterminate = busy && pr.Fraction < 0;
        if (busy && pr.Fraction >= 0) _dockBar.Value = pr.Fraction;
        _dockDetail.Text = busy ? ProgressDetail(pr, Jobs.Size) : "";
        _dockDetail.IsVisible = _dockDetail.Text.Length > 0;
        _dockCancel.IsVisible = busy && Jobs.Cancellable;
        _dockCancel.IsEnabled = !Jobs.Cancelling;
        _dockClose.IsVisible = !busy;
    }

    /// <summary>"12.3 / 40.0 MB · 31 %" while the size is known, else just the percentage; empty
    /// for an indeterminate stage.</summary>
    internal static string ProgressDetail(StoreProgress pr, long size)
        => pr.Fraction < 0 ? ""
            : size > 0
                ? $"{(pr.Fraction * size / 1048576.0).ToString("0.0", NotaNum.Culture)} / {NotaNum.Unit(size / 1048576.0, "0.0", "MB")}  ·  {NotaNum.Unit(pr.Fraction * 100, "0", "%")}"
                : NotaNum.Unit(pr.Fraction * 100, "0", "%");

    private Control StoreRow(StorePlugin p, InstalledStorePlugin? inst)
    {
        // A plugin this computer can't install reads one Ink step down.
        bool here = p.Asset is not null;
        var text = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new WrapPanel
                {
                    Children =
                    {
                        new TextBlock { Text = p.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = here ? TextPrimary : TextSecondary, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = KindLabel(p.Kind), FontSize = 9, LetterSpacing = 0.7, FontFamily = NotaFonts.MonoFamily, Foreground = TextMuted, VerticalAlignment = VerticalAlignment.Center },
                        PlatformIcons(p.Platforms),
                    },
                },
                new TextBlock { Text = $"{p.Developer} · {p.Version} · {p.License}", FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = here ? TextTertiary : TextDisabled },
                new TextBlock { Text = p.Description, FontSize = 12, LineHeight = 18, Foreground = here ? TextSecondary : TextTertiary, TextWrapping = TextWrapping.Wrap },
            },
        };
        if (p.Notes is { Length: > 0 } notes)
            text.Children.Add(new TextBlock { Text = notes, FontSize = 11, LineHeight = 16, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap });

        var actions = new StackPanel { Spacing = 8, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        bool busy = Jobs.BusyId is not null;
        if (Jobs.BusyId == p.Id)
        {
            actions.Children.Add(StoreButton(inst is null ? "Installing…" : "Working…", enabled: false, () => { }));
        }
        else if (p.Asset is null)
        {
            actions.Children.Add(new TextBlock { Text = UnavailableReason(p.Platforms), FontSize = 11, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, Width = 110, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right });
        }
        else if (inst is null)
        {
            actions.Children.Add(StoreButton("Install", enabled: !busy, () => InstallFromStore(p)));
        }
        else
        {
            if (inst.Version != p.Version)
                actions.Children.Add(StoreButton($"Update to {p.Version}", enabled: !busy, () => InstallFromStore(p)));
            else
                actions.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6, Height = 26, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { Dot(Brass), new TextBlock { Text = "Installed", FontSize = 11, FontWeight = FontWeight.Medium, Foreground = Brass, VerticalAlignment = VerticalAlignment.Center } },
                });
            actions.Children.Add(StoreLink("Remove", null, busy ? null : () => RemoveFromStore(p)));
        }
        if (inst is null && p.Asset is { Size: > 0 } asset)
            actions.Children.Add(new TextBlock
            {
                Text = NotaNum.Unit(asset.Size / 1048576.0, "0.0", "MB"), FontSize = 10, FontFamily = NotaFonts.MonoFamily,
                Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Right,
            });
        var source = StoreLink("Source", GlyphKind.PopOut, () => _ = Launcher.LaunchUriAsync(new Uri(p.Repo)));
        ToolTip.SetTip(source, "Open the source repository");
        actions.Children.Add(source);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, Margin = new Thickness(16, 14, 14, 14) };
        grid.Children.Add(text);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    // Rows use raised buttons for actions: a list of solid brass Install buttons would break
    // "one primary action per context".
    private static Button StoreButton(string label, bool enabled, Action onClick)
    {
        var b = new Button
        {
            Content = label, IsEnabled = enabled, Height = 26, MinWidth = 88, FontSize = 12, Padding = new Thickness(12, 0),
            CornerRadius = NotaRadius.Tile, HorizontalAlignment = HorizontalAlignment.Right,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    // A text link: Ink 4, Ink 1 on hover; null action = inert (Ink 6).
    private static Control StoreLink(string label, GlyphKind? glyph, Action? onClick)
    {
        var text = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent };
        row.Children.Add(text);
        if (glyph is { } g) row.Children.Add(new Glyph(g, 9));
        void Ink(IBrush b) { text.Foreground = b; foreach (var gl in row.Children.OfType<Glyph>()) gl.Foreground = b; }
        Ink(onClick is null ? TextDisabled : TextMuted);
        if (onClick is null) return row;
        row.Cursor = new Cursor(StandardCursorType.Hand);
        row.PointerEntered += (_, _) => Ink(TextPrimary);
        row.PointerExited += (_, _) => Ink(TextMuted);
        row.PointerPressed += (_, e) => { if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) onClick(); };
        return row;
    }

    // 24×24 filled marks for the three systems registry assets target.
    private const string AppleIcon = "M12 7.2 C9.6 5.6 4.5 5.8 4.5 11.8 C4.5 16.6 7.4 21 9.3 21 C10.6 21 11 20.3 12 20.3 C13 20.3 13.4 21 14.7 21 C16.2 21 18.4 18.2 19.3 15.4 C17.6 14.7 16.6 13.2 16.6 11.5 C16.6 9.9 17.5 8.6 18.8 7.9 C17.2 6 14 5.9 12 7.2 Z M12.2 6.3 C12.2 4.3 13.6 2.8 15.6 2.7 C15.6 4.7 14.2 6.2 12.2 6.3 Z";
    private const string WindowsIcon = "M3.5 3.5 H11 V11 H3.5 Z M13 3.5 H20.5 V11 H13 Z M3.5 13 H11 V20.5 H3.5 Z M13 13 H20.5 V20.5 H13 Z";
    private const string LinuxIcon = "M12 2 C14 2 15.2 3.6 15.2 5.8 C15.2 7.4 14.5 8.2 15 9.4 C16.6 11.6 19 14.4 18.4 17.8 L19.8 21 H4.2 L5.6 17.8 C5 14.4 7.4 11.6 9 9.4 C9.5 8.2 8.8 7.4 8.8 5.8 C8.8 3.6 10 2 12 2 Z M12 10.2 C10.1 10.2 8.8 13 8.8 15.8 C8.8 18.3 10.2 19.8 12 19.8 C13.8 19.8 15.2 18.3 15.2 15.8 C15.2 13 13.9 10.2 12 10.2 Z";

    // The systems a plugin ships for, as small marks with the architectures in the tooltip.
    // This computer's system reads a step brighter than the others.
    private static Control PlatformIcons(IReadOnlyList<string> keys)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var (prefix, name, icon, current) in new[]
        {
            ("macos-", "macOS", AppleIcon, OperatingSystem.IsMacOS()),
            ("windows-", "Windows", WindowsIcon, OperatingSystem.IsWindows()),
            ("linux-", "Linux", LinuxIcon, OperatingSystem.IsLinux()),
        })
        {
            var archs = keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => ArchLabel(k[prefix.Length..])).Distinct().ToList();
            if (archs.Count == 0) continue;
            // "F1": even-odd fill, so the penguin's belly is a hole.
            var mark = IconBox(new Path { Data = Geometry.Parse("F1 " + icon), Fill = current ? TextSecondary : TextTertiary }, 12);
            var box = new Border { Background = Brushes.Transparent, Child = mark };   // hit-testable for the tooltip
            ToolTip.SetTip(box, $"{name} · {string.Join(", ", archs)}");
            row.Children.Add(box);
        }
        return row;
    }

    // Why a plugin has no asset here: no build for this system at all, or not for this CPU.
    private static string UnavailableReason(IReadOnlyList<string> keys)
    {
        var os = CurrentOsName;
        var prefix = os is "macOS" ? "macos-" : os.ToLowerInvariant() + "-";
        if (!keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal))) return $"No {os} version";
        var cpu = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        return $"No {os} version for {ArchLabel(cpu)}";
    }

    private static string ArchLabel(string arch) => arch switch
    {
        "universal" => "Apple silicon + Intel",
        "arm64" => OperatingSystem.IsMacOS() ? "Apple silicon" : "ARM64",
        "x64" => "x64",
        _ => arch,
    };

    private static string KindLabel(string kind) => kind switch
    {
        "instrument" => "INSTRUMENT",
        "effect" => "EFFECT",
        "midi" => "MIDI",
        "bundle" => "COLLECTION",
        _ => kind.ToUpperInvariant(),
    };

    private void InstallFromStore(StorePlugin p)
    {
        var main = _main;
        Jobs.Install(p.Id, source: 0, p.Name, p.Asset?.Size ?? 0, new StoreProgress(0, $"Downloading {p.Name}…"),
            (progress, ct) => _store.InstallAsync(p, progress, ct),
            async () =>
            {
                Jobs.Report(new StoreProgress(-1, $"Scanning {p.Name}…"));
                return await RescanAfterStoreChange(main)
                    ? $"{p.Name} {p.Version} is installed — find it in the browser."
                    : $"{p.Name} is installed. Rescan plugins to use it.";
            });
    }

    private async void RemoveFromStore(StorePlugin p)
    {
        if (Jobs.Busy) return;
        var ok = await new ConfirmWindow("Remove plugin",
            $"Remove {p.Name}? Projects that use it will load without it until it's installed again.",
            "Remove", "Cancel").ShowDialog<bool>(this);
        if (!ok) return;
        var main = _main;
        Jobs.Remove(p.Id, source: 0, $"Removing {p.Name}…", async () =>
        {
            _store.Uninstall(p.Id);
            await RescanAfterStoreChange(main);
            return $"{p.Name} was removed.";
        });
    }

    // Static: the job can outlive this window.
    private static async Task<bool> RescanAfterStoreChange(MainWindowViewModel? main)
    {
        if (main is null) return false;
        return await PluginScan.RescanAsync(main) is not null;
    }
}
