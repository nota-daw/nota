// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preferences → Get Plug-ins: browse the Nota plugin registry and install / update /
// remove open-source VST3 plugins (IPluginStore). One install at a time; it keeps
// running when the user switches panes and is cancelled when the window closes. Its
// progress and outcome show in a dock at the bottom of the window (visible from every
// pane), which also cancels it. After every change the catalog is rescanned so the
// browser lists the new plugins.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private static readonly string[] StoreFilters = { "All", "Instruments", "Effects", "Installed" };

    private readonly IPluginStore _store = App.Services.GetRequiredService<IPluginStore>();
    private IReadOnlyList<StorePlugin>? _storePlugins;
    private int _storeFilter;
    private string _storeSearch = "";
    private string? _storeBusyId;                    // plugin being installed / removed
    private StoreProgress _storeProgress;
    private long _storeBusySize;                      // download size in bytes, 0 = unknown
    private string? _storeMessage;                    // registry loading / error, shown under the toolbar
    private string? _storeResult;                     // last install / remove outcome, shown in the dock
    private int _storeResultSeq;                      // guards the auto-hide of an older result
    private CancellationTokenSource? _storeCts;       // window lifetime
    private CancellationTokenSource? _storeJobCts;    // the running install; null when it can't be cancelled
    private Task? _storeLoad;
    private ContentControl? _storeList;               // null while another pane is showing
    private TextBlock[]? _storeCounts;                // mono counts on the filter segments
    private TextBlock? _storeStatus;

    // The download dock (built once with the window).
    private Border _dock = null!;
    private TextBlock _dockTitle = null!, _dockDetail = null!;
    private ProgressBar _dockBar = null!;
    private Button _dockCancel = null!;
    private Control _dockClose = null!;

    private Control StorePane()
    {
        var intro = Caption("Open-source VST3 plugins from the Nota plugin registry. Each one downloads from the project's own GitHub release, is checked against the registry's checksum and unpacked — installers never run.", muted: true);
        intro.MaxWidth = 600;
        intro.HorizontalAlignment = HorizontalAlignment.Left;

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
        refresh.Click += (_, _) => _ = LoadStoreAsync(refresh: true);

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        toolbar.Children.Add(new Border { Classes = { "segmented" }, Child = strip });
        var searchField = SearchField(search, double.PositiveInfinity);
        searchField.MinWidth = 140;
        Grid.SetColumn(searchField, 1); toolbar.Children.Add(searchField);
        Grid.SetColumn(refresh, 2); toolbar.Children.Add(refresh);

        _storeStatus = Caption("");
        _storeList = new ContentControl();

        var body = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                intro, toolbar,
                _storeStatus,
                _storeList,
                Caption($"Installed to {_store.PluginsDir}"),
            },
        };
        body.DetachedFromVisualTree += (_, _) => { _storeList = null; _storeStatus = null; _storeCounts = null; };
        if (_storePlugins is null) EnsureStoreLoading();
        else RenderStoreList();
        return body;
    }

    // The registry index is cached on disk, so the window starts this on open: the sidebar's
    // Downloads count needs it before the pane is ever shown.
    private void EnsureStoreLoading()
    {
        if (_storePlugins is null && _storeLoad is not { IsCompleted: false }) _storeLoad = LoadStoreAsync(refresh: false);
    }

    private async Task LoadStoreAsync(bool refresh)
    {
        _storeMessage = "Loading the plugin registry…";
        RenderStoreList();
        try
        {
            _storePlugins = await _store.FetchAsync(refresh);
            _storeMessage = null;
        }
        catch (PluginStoreException e)
        {
            _storeMessage = e.Message;
        }
        RenderStoreList();
    }

    private void RenderStoreList()
    {
        UpdateStoreBadge();
        if (_storeList is null) return;
        UpdateStoreStatus();
        if (_storePlugins is null) { _storeList.Content = null; UpdateStoreCounts(null); return; }

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
        _storeList.Content = Table(shown.Select(p => StoreRow(p, installed.GetValueOrDefault(p.Id))).ToList());
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
        _dockCancel = StoreButton("Cancel", enabled: true, CancelStoreInstall);
        _dockCancel.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(_dockCancel, "Stop the download — nothing is changed");
        _dockClose = new Button
        {
            Content = new Glyph(GlyphKind.Close, 10), Classes = { "ghost" }, Width = 26, Height = 26, Padding = new Thickness(0),
            Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        };
        ((Button)_dockClose).Click += (_, _) => { _storeResult = null; UpdateDock(); };

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
        bool busy = _storeBusyId is not null;
        _dock.IsVisible = busy || _storeResult is not null;
        if (!_dock.IsVisible) return;

        var pr = _storeProgress;
        _dockTitle.Text = busy ? pr.Message : _storeResult;
        _dockTitle.Foreground = busy ? TextPrimary : TextSecondary;
        _dockBar.IsVisible = busy;
        _dockBar.IsIndeterminate = busy && pr.Fraction < 0;
        if (busy && pr.Fraction >= 0) _dockBar.Value = pr.Fraction;
        _dockDetail.Text = !busy || pr.Fraction < 0 ? ""
            : _storeBusySize > 0
                ? $"{(pr.Fraction * _storeBusySize / 1048576.0).ToString("0.0", NotaNum.Culture)} / {NotaNum.Unit(_storeBusySize / 1048576.0, "0.0", "MB")}  ·  {NotaNum.Unit(pr.Fraction * 100, "0", "%")}"
                : NotaNum.Unit(pr.Fraction * 100, "0", "%");
        _dockDetail.IsVisible = _dockDetail.Text.Length > 0;
        _dockCancel.IsVisible = busy && _storeJobCts is not null;
        _dockCancel.IsEnabled = _storeJobCts is { IsCancellationRequested: false };
        _dockClose.IsVisible = !busy;
    }

    private void CancelStoreInstall()
    {
        if (_storeJobCts is not { IsCancellationRequested: false } cts) return;
        cts.Cancel();
        _storeProgress = _storeProgress with { Message = "Cancelling…" };
        UpdateDock();
    }

    // Shows an install / remove outcome in the dock; a success fades out on its own, an error
    // stays until it's closed.
    private void SetStoreResult(string? text, bool sticky)
    {
        _storeResult = text;
        int seq = ++_storeResultSeq;
        if (text is not null && !sticky)
            DispatcherTimer.RunOnce(() => { if (seq == _storeResultSeq && _storeBusyId is null) { _storeResult = null; UpdateDock(); } }, TimeSpan.FromSeconds(8));
    }

    private Control StoreRow(StorePlugin p, InstalledStorePlugin? inst)
    {
        var text = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new WrapPanel
                {
                    Children =
                    {
                        new TextBlock { Text = p.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = KindLabel(p.Kind), FontSize = 9, LetterSpacing = 0.7, FontFamily = NotaFonts.MonoFamily, Foreground = TextMuted, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                new TextBlock { Text = $"{p.Developer} · {p.Version} · {p.License}", FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary },
                new TextBlock { Text = p.Description, FontSize = 12, LineHeight = 18, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap },
            },
        };
        if (p.Notes is { Length: > 0 } notes)
            text.Children.Add(new TextBlock { Text = notes, FontSize = 11, LineHeight = 16, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap });

        var actions = new StackPanel { Spacing = 8, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        bool busy = _storeBusyId is not null;
        if (_storeBusyId == p.Id)
        {
            actions.Children.Add(StoreButton(inst is null ? "Installing…" : "Working…", enabled: false, () => { }));
        }
        else if (p.Asset is null)
        {
            actions.Children.Add(new TextBlock { Text = "Not available for this computer", FontSize = 11, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, Width = 110, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right });
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

    private static string KindLabel(string kind) => kind switch
    {
        "instrument" => "INSTRUMENT",
        "effect" => "EFFECT",
        "midi" => "MIDI",
        "bundle" => "COLLECTION",
        _ => kind.ToUpperInvariant(),
    };

    private async void InstallFromStore(StorePlugin p)
    {
        if (_storeBusyId is not null) return;
        _storeBusyId = p.Id;
        _storeBusySize = p.Asset?.Size ?? 0;
        _storeProgress = new StoreProgress(0, $"Downloading {p.Name}…");
        SetStoreResult(null, sticky: true);
        _storeCts ??= new CancellationTokenSource();
        var job = _storeJobCts = CancellationTokenSource.CreateLinkedTokenSource(_storeCts.Token);
        RenderStoreList();
        // A report queued before Cancel must not overwrite "Cancelling…".
        var progress = new Progress<StoreProgress>(r => { if (job.IsCancellationRequested) return; _storeProgress = r; UpdateDock(); });
        try
        {
            await _store.InstallAsync(p, progress, job.Token);
            _storeJobCts = null;   // the files are in place — too late to cancel
            _storeProgress = new StoreProgress(-1, $"Scanning {p.Name}…");
            UpdateDock();
            SetStoreResult(await RescanAfterStoreChange() ? $"{p.Name} {p.Version} is installed — find it in the browser." : $"{p.Name} is installed. Rescan plugins to use it.", sticky: false);
        }
        catch (OperationCanceledException)
        {
            // Window closing: nothing to show. Otherwise the user pressed Cancel.
            if (_storeCts is { IsCancellationRequested: false }) SetStoreResult($"Installing {p.Name} was cancelled.", sticky: false);
        }
        catch (PluginStoreException e) { SetStoreResult(e.Message, sticky: true); }
        catch (Exception e)
        {
            App.Services.GetRequiredService<ILogSink>().Error($"Installing {p.Id} failed", e);
            SetStoreResult($"Installing {p.Name} failed: {e.Message}", sticky: true);
        }
        finally
        {
            _storeBusyId = null;
            _storeJobCts = null;
            job.Dispose();
        }
        RenderStoreList();
    }

    private async void RemoveFromStore(StorePlugin p)
    {
        if (_storeBusyId is not null) return;
        var ok = await new ConfirmWindow("Remove plugin",
            $"Remove {p.Name}? Projects that use it will load without it until it's installed again.",
            "Remove", "Cancel").ShowDialog<bool>(this);
        if (!ok) return;
        _storeBusyId = p.Id;
        _storeBusySize = 0;
        _storeProgress = new StoreProgress(-1, $"Removing {p.Name}…");
        SetStoreResult(null, sticky: true);
        RenderStoreList();
        try
        {
            _store.Uninstall(p.Id);
            await RescanAfterStoreChange();
            SetStoreResult($"{p.Name} was removed.", sticky: false);
        }
        catch (PluginStoreException e) { SetStoreResult(e.Message, sticky: true); }
        finally { _storeBusyId = null; }
        RenderStoreList();
    }

    private async Task<bool> RescanAfterStoreChange()
    {
        if (_main is null) return false;
        return await PluginScan.RescanAsync(_main) is not null;
    }

    // Called from the constructor's Closed handler chain: stop a running download.
    private void CancelStoreWork()
    {
        _storeCts?.Cancel();
        _storeCts?.Dispose();
        _storeCts = null;
    }
}
