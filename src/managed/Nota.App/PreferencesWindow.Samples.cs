// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preferences → Downloads → Sample Packs: browse the Nota sample registry and install /
// update / remove free sample packs (ISampleStore) into the Samples folder. Built like Get Plug-ins
// (PreferencesWindow.Store.cs) and sharing its job state: one download at a time across both
// stores, shown in the same dock. A pack's busy key is "pack:<id>" so it can't collide with a
// plugin id. After every change the browser's Files tab is rescanned.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private static readonly string[] PackFilters = { "All", "One-shots", "Instruments", "Installed" };

    private readonly ISampleStore _samples = App.Services.GetRequiredService<ISampleStore>();
    private IReadOnlyList<StorePack>? _packs;
    private int _packFilter;
    private string _packSearch = "";
    private string? _packMessage;                    // registry loading / error, shown under the toolbar
    private Task? _packLoad;
    private bool _packLoading;
    private ContentControl? _packList;               // null while another pane is showing
    private ItemsControl? _packItems;
    private TextBlock[]? _packCounts;
    private TextBlock? _packStatus;
    private Button? _packRefresh;
    private ProgressBar? _packLoadBar;

    private sealed record PackEntry(StorePack Pack, InstalledStorePack? Installed, bool First);

    private static string PackKey(string id) => "pack:" + id;

    private const string PacksIntro = "Free sample packs from the Nota sample registry, licensed for any music, commercial releases included. Each pack downloads from where its author publishes it, is checked against the registry's checksum and unpacked into your Samples folder.";

    // The Sample Packs half of Downloads (PreferencesWindow.Downloads.cs).
    private (Control Toolbar, Control Body) PackParts()
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var segs = new List<ToggleButton>();
        _packCounts = new TextBlock[PackFilters.Length];
        for (int i = 0; i < PackFilters.Length; i++)
        {
            int idx = i;
            var count = new TextBlock { FontSize = 9, FontFamily = NotaFonts.MonoFamily, VerticalAlignment = VerticalAlignment.Center };
            _packCounts[i] = count;
            var b = new ToggleButton
            {
                Classes = { "seg" }, FontSize = 12, IsChecked = i == _packFilter,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { new TextBlock { Text = PackFilters[i], VerticalAlignment = VerticalAlignment.Center }, count },
                },
            };
            b.Click += (_, _) =>
            {
                for (int k = 0; k < segs.Count; k++) segs[k].IsChecked = k == idx;
                _packFilter = idx;
                RenderPackList();
            };
            segs.Add(b);
            strip.Children.Add(b);
        }
        var search = new TextBox { PlaceholderText = "Search", Text = _packSearch, Classes = { "search" }, FontSize = 12 };
        search.TextChanged += (_, _) => { _packSearch = search.Text ?? ""; RenderPackList(); };
        var refresh = new Button { Content = "Refresh", Classes = { "ghost" }, Height = 30, FontSize = 12, Padding = new Thickness(12, 0), Foreground = TextSecondary };
        ToolTip.SetTip(refresh, "Reload the registry");
        refresh.Click += (_, _) => { if (!_packLoading) _packLoad = LoadPacksAsync(refresh: true); };
        _packRefresh = refresh;

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        toolbar.Children.Add(new Border { Classes = { "segmented" }, Child = strip });
        var searchField = SearchField(search, double.PositiveInfinity);
        searchField.MinWidth = 140;
        Grid.SetColumn(searchField, 1); toolbar.Children.Add(searchField);
        Grid.SetColumn(refresh, 2); toolbar.Children.Add(refresh);

        var list = _packList = new ContentControl();
        _packStatus = Caption("");
        _packLoadBar = new ProgressBar { IsIndeterminate = true, Height = 3, MinWidth = 0, IsVisible = false };
        _packItems = PackItems();

        var body = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                _packStatus,
                new StackPanel { Spacing = 6, Children = { _packLoadBar, _packList } },
                Caption($"Installed to {_samples.InstallDir}"),
            },
        };
        body.DetachedFromVisualTree += (_, _) =>
        {
            if (_packList != list) return;
            _packList = null; _packItems = null; _packStatus = null; _packCounts = null; _packRefresh = null; _packLoadBar = null;
        };
        if (_packs is null && _packLoad is not { IsCompleted: false }) _packLoad = LoadPacksAsync(refresh: false);
        RenderPackList();
        return (toolbar, body);
    }

    private async Task LoadPacksAsync(bool refresh)
    {
        _packLoading = true;
        _packMessage = null;
        RenderPackList();
        try
        {
            _packs = await _samples.FetchAsync(refresh);
        }
        catch (StoreException e)
        {
            _packMessage = e.Message;
        }
        finally { _packLoading = false; }
        RenderPackList();
    }

    private ItemsControl PackItems() => new()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
        Margin = new Thickness(0, 0, 0, -1),
        DataTemplates =
        {
            new FuncDataTemplate<PackEntry>((e, _) => e is null ? new Panel() : new Border
            {
                Background = RowBg, BorderBrush = Hairline, BorderThickness = new Thickness(0, e.First ? 0 : 1, 0, 0),
                Child = PackRow(e.Pack, e.Installed),
            }),
        },
    };

    private void RenderPackList()
    {
        if (_packList is null || _packItems is null) return;
        if (_packStatus is not null)
        {
            _packStatus.Text = _packMessage ?? "";
            _packStatus.IsVisible = _packStatus.Text.Length > 0;
        }
        if (_packRefresh is not null)
        {
            _packRefresh.IsEnabled = !_packLoading;
            _packRefresh.Content = _packLoading && _packs is not null ? "Refreshing…" : "Refresh";
        }
        if (_packLoadBar is not null) _packLoadBar.IsVisible = _packLoading && _packs is not null;
        if (_packs is null)
        {
            _packList.Content = _packLoading ? PackLoadingBlock() : null;
            UpdatePackCounts(null);
            return;
        }

        var installed = _samples.Installed.ToDictionary(i => i.Id);
        UpdatePackCounts(installed);
        var q = _packSearch.Trim();
        var shown = _packs.Where(p => MatchesPackFilter(p, _packFilter, installed))
            .Where(p => q.Length == 0
                        || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Author.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (shown.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = _packFilter == 3 && q.Length == 0 ? "No sample packs installed from the registry yet." : "Nothing matches.",
                FontSize = 12, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(28),
            };
            _packList.Content = Table(new List<Control> { empty });
            return;
        }

        _packItems.ItemsSource = shown.Select((p, i) => new PackEntry(p, installed.GetValueOrDefault(p.Id), First: i == 0)).ToList();
        if (_packList.Content is not Border { Tag: "pack-table" })
            _packList.Content = new Border
            {
                Tag = "pack-table", BorderBrush = Divider, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel,
                ClipToBounds = true, Child = _packItems,
            };
    }

    private Control PackLoadingBlock() => Table(new List<Control>
    {
        new StackPanel
        {
            Spacing = 10, Margin = new Thickness(28, 36), HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new ProgressBar { IsIndeterminate = true, Width = 180, Height = 4, MinWidth = 0 },
                new TextBlock { Text = "Loading the sample registry…", FontSize = 12, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center },
            },
        },
    });

    private static bool MatchesPackFilter(StorePack p, int filter, IReadOnlyDictionary<string, InstalledStorePack> installed) => filter switch
    {
        1 => p.Kind is "one-shots" or "loops" or "mixed",
        2 => p.Kind is "multisample",
        3 => installed.ContainsKey(p.Id),
        _ => true,
    };

    private void UpdatePackCounts(IReadOnlyDictionary<string, InstalledStorePack>? installed)
    {
        if (_packCounts is null) return;
        for (int i = 0; i < _packCounts.Length; i++)
        {
            _packCounts[i].Text = _packs is null || installed is null ? "" : _packs.Count(p => MatchesPackFilter(p, i, installed)).ToString();
            _packCounts[i].Foreground = i == _packFilter ? NotaPalette.AccentEdge : TextDisabled;
        }
    }

    private static string PackKindLabel(string kind) => kind switch
    {
        "one-shots" => "ONE-SHOTS",
        "loops" => "LOOPS",
        "multisample" => "INSTRUMENT",
        "mixed" => "MIXED",
        _ => kind.ToUpperInvariant(),
    };

    private static string Megabytes(long bytes)
        => bytes >= 1L << 30 ? NotaNum.Unit(bytes / (double)(1L << 30), "0.0", "GB") : NotaNum.Unit(bytes / 1048576.0, "0.0", "MB");

    private Control PackRow(StorePack p, InstalledStorePack? inst)
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
                        new TextBlock { Text = PackKindLabel(p.Kind), FontSize = 9, LetterSpacing = 0.7, FontFamily = NotaFonts.MonoFamily, Foreground = TextMuted, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
                new TextBlock { Text = $"{p.Author} · {p.Version} · {p.License}", FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary },
                new TextBlock { Text = p.Description, FontSize = 12, LineHeight = 18, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap },
            },
        };
        if (p.Attribution is { Length: > 0 } credit)
            text.Children.Add(new TextBlock { Text = $"Credit: {credit}", FontSize = 11, LineHeight = 16, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap });
        if (p.Notes is { Length: > 0 } notes)
            text.Children.Add(new TextBlock { Text = notes, FontSize = 11, LineHeight = 16, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap });

        var actions = new StackPanel { Spacing = 8, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        bool busy = _storeBusyId is not null;
        if (_storeBusyId == PackKey(p.Id))
        {
            actions.Children.Add(StoreButton(inst is null ? "Installing…" : "Working…", enabled: false, () => { }));
        }
        else if (inst is null)
        {
            actions.Children.Add(StoreButton("Install", enabled: !busy, () => InstallPack(p)));
        }
        else
        {
            if (inst.Version != p.Version)
                actions.Children.Add(StoreButton($"Update to {p.Version}", enabled: !busy, () => InstallPack(p)));
            else
                actions.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6, Height = 26, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { Dot(Brass), new TextBlock { Text = "Installed", FontSize = 11, FontWeight = FontWeight.Medium, Foreground = Brass, VerticalAlignment = VerticalAlignment.Center } },
                });
            var show = StoreLink("Show", null, () => { try { MainWindow.RevealInFileManager(inst.Path); } catch { /* no file manager */ } });
            ToolTip.SetTip(show, "Show the pack's folder");
            actions.Children.Add(show);
            actions.Children.Add(StoreLink("Remove", null, busy ? null : () => RemovePack(p, inst)));
        }
        actions.Children.Add(new TextBlock
        {
            Text = inst is null ? $"{Megabytes(p.Asset.Size)} · {p.Asset.Files} samples" : $"{inst.Files} samples",
            FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Right,
        });
        if (inst is null)
            ToolTip.SetTip(actions.Children[^1], $"{Megabytes(p.Asset.Size)} download · {p.Asset.Files} samples · {Megabytes(p.Asset.UnpackedSize)} on disk · {string.Join(", ", p.Asset.Formats)}");
        var source = StoreLink("Source", GlyphKind.PopOut, () => _ = Launcher.LaunchUriAsync(new Uri(p.Source)));
        ToolTip.SetTip(source, "Open the pack's page");
        actions.Children.Add(source);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16, Margin = new Thickness(16, 14, 14, 14) };
        grid.Children.Add(text);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private async void InstallPack(StorePack p)
    {
        if (_storeBusyId is not null) return;
        _storeBusyId = PackKey(p.Id);
        _storeBusySize = p.Asset.Size;
        _storeProgress = new StoreProgress(0, $"Downloading {p.Name}…");
        SetStoreResult(null, sticky: true);
        _storeCts ??= new CancellationTokenSource();
        var job = _storeJobCts = CancellationTokenSource.CreateLinkedTokenSource(_storeCts.Token);
        RenderStoreLists();
        var progress = new Progress<StoreProgress>(r =>
        {
            if (job.IsCancellationRequested) return;
            // Only the download has a known byte count; the copy stage's fraction is of the unpacked size.
            if (!r.Message.StartsWith("Downloading", StringComparison.Ordinal)) _storeBusySize = 0;
            _storeProgress = r;
            UpdateDock();
        });
        try
        {
            await _samples.InstallAsync(p, progress, job.Token);
            _main?.Browser.RebuildSamples();
            SetStoreResult($"{p.Name} is installed — find it under Downloaded in the browser's Files tab.", sticky: false);
        }
        catch (OperationCanceledException)
        {
            if (_storeCts is { IsCancellationRequested: false }) SetStoreResult($"Installing {p.Name} was cancelled.", sticky: false);
        }
        catch (StoreException e) { SetStoreResult(e.Message, sticky: true); }
        catch (Exception e)
        {
            App.Services.GetRequiredService<ILogSink>().Error($"Installing pack {p.Id} failed", e);
            SetStoreResult($"Installing {p.Name} failed: {e.Message}", sticky: true);
        }
        finally
        {
            _storeBusyId = null;
            _storeJobCts = null;
            job.Dispose();
        }
        RenderStoreLists();
    }

    private async void RemovePack(StorePack p, InstalledStorePack inst)
    {
        if (_storeBusyId is not null) return;
        var ok = await new ConfirmWindow("Remove sample pack",
            $"Remove {p.Name}? Its folder ({inst.Path}) is deleted.",
            "Remove", "Cancel").ShowDialog<bool>(this);
        if (!ok) return;
        _storeBusyId = PackKey(p.Id);
        _storeBusySize = 0;
        _storeProgress = new StoreProgress(-1, $"Removing {p.Name}…");
        SetStoreResult(null, sticky: true);
        RenderStoreLists();
        try
        {
            await Task.Run(() => _samples.Uninstall(p.Id));
            _main?.Browser.RebuildSamples();
            SetStoreResult($"{p.Name} was removed.", sticky: false);
        }
        catch (StoreException e) { SetStoreResult(e.Message, sticky: true); }
        finally { _storeBusyId = null; }
        RenderStoreLists();
    }

    // Every list, since a job in one disables the Install buttons of the others.
    private void RenderStoreLists()
    {
        RenderStoreList();
        RenderPackList();
        RenderModelList();
        UpdateDock();   // RenderStoreList only reaches the dock while the Downloads pane is showing
    }
}
