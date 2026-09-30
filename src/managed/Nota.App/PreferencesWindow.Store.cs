// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preferences → Get Plug-ins: browse the Nota plugin registry and install / update /
// remove open-source VST3 plugins (IPluginStore). One install at a time; it keeps
// running when the user switches panes and is cancelled when the window closes. After
// every change the catalog is rescanned so the browser lists the new plugins.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
    private string? _storeMessage;                    // last result or error, shown under the toolbar
    private CancellationTokenSource? _storeCts;
    private StackPanel? _storeList;                   // null while another pane is showing
    private TextBlock? _storeStatus;
    private ProgressBar? _storeBar;

    private Control StorePane()
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(SectionLabel("GET PLUG-INS"));
        body.Children.Add(Caption("Open-source VST3 plugins from the Nota plugin registry. Each one downloads from the project's own GitHub release, is checked against the registry's checksum and unpacked — installers never run."));

        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var segs = new List<Avalonia.Controls.Primitives.ToggleButton>();
        for (int i = 0; i < StoreFilters.Length; i++)
        {
            int idx = i;
            var b = new Avalonia.Controls.Primitives.ToggleButton { Content = StoreFilters[i], Classes = { "seg" }, IsChecked = i == _storeFilter };
            b.Click += (_, _) =>
            {
                for (int k = 0; k < segs.Count; k++) segs[k].IsChecked = k == idx;
                _storeFilter = idx;
                RenderStoreList();
            };
            segs.Add(b);
            strip.Children.Add(b);
        }
        var search = new TextBox { PlaceholderText = "Search", Text = _storeSearch, Width = 160, Classes = { "search" }, VerticalAlignment = VerticalAlignment.Center };
        search.TextChanged += (_, _) => { _storeSearch = search.Text ?? ""; RenderStoreList(); };
        var refresh = new Button { Content = "Refresh", Classes = { "ghost" } };
        ToolTip.SetTip(refresh, "Reload the registry");
        refresh.Click += (_, _) => _ = LoadStoreAsync(refresh: true);

        var toolbar = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(strip, Dock.Left);
        toolbar.Children.Add(new Border { Classes = { "segmented" }, Child = strip });
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { search, refresh } };
        DockPanel.SetDock(right, Dock.Right);
        toolbar.Children.Add(right);
        body.Children.Add(toolbar);

        _storeStatus = Caption("");
        _storeBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 4, IsVisible = false };
        body.Children.Add(_storeStatus);
        body.Children.Add(_storeBar);

        _storeList = new StackPanel { Spacing = 6 };
        body.Children.Add(_storeList);
        body.Children.Add(Caption($"Installed to {_store.PluginsDir}"));

        body.DetachedFromVisualTree += (_, _) => { _storeList = null; _storeStatus = null; _storeBar = null; };
        if (_storePlugins is null) _ = LoadStoreAsync(refresh: false);
        else RenderStoreList();
        return body;
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
        if (_storeList is null) return;
        UpdateStoreStatus();
        _storeList.Children.Clear();
        if (_storePlugins is null) return;

        var installed = _store.Installed.ToDictionary(i => i.Id);
        var q = _storeSearch.Trim();
        var shown = _storePlugins.Where(p => _storeFilter switch
            {
                1 => p.Kind is "instrument",
                2 => p.Kind is "effect" or "midi" or "bundle",
                3 => installed.ContainsKey(p.Id),
                _ => true,
            })
            .Where(p => q.Length == 0
                        || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Developer.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || p.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (shown.Count == 0)
        {
            _storeList.Children.Add(Caption(_storeFilter == 3 && q.Length == 0 ? "Nothing installed from the registry yet." : "No plugins match."));
            return;
        }
        foreach (var p in shown)
            _storeList.Children.Add(StoreRow(p, installed.GetValueOrDefault(p.Id)));
    }

    private void UpdateStoreStatus()
    {
        if (_storeStatus is null || _storeBar is null) return;
        bool busy = _storeBusyId is not null;
        _storeStatus.Text = busy ? _storeProgress.Message : _storeMessage ?? "";
        _storeStatus.IsVisible = _storeStatus.Text.Length > 0;
        _storeBar.IsVisible = busy;
        _storeBar.IsIndeterminate = busy && _storeProgress.Fraction < 0;
        if (busy && _storeProgress.Fraction >= 0) _storeBar.Value = _storeProgress.Fraction;
    }

    private Control StoreRow(StorePlugin p, InstalledStorePlugin? inst)
    {
        var name = new TextBlock { Text = p.Name, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary };
        var kind = new TextBlock
        {
            Text = KindLabel(p.Kind), FontSize = 9, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var meta = new TextBlock
        {
            Text = $"{p.Developer} · {p.Version} · {p.License}",
            FontSize = 10, Foreground = TextTertiary,
        };
        var text = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { name, kind } },
                meta,
                new TextBlock { Text = p.Description, FontSize = 11, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap },
            },
        };
        if (p.Notes is { Length: > 0 } notes)
            text.Children.Add(new TextBlock { Text = notes, FontSize = 10, Foreground = NotaPalette.TextMuted, TextWrapping = TextWrapping.Wrap });

        var actions = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        bool busy = _storeBusyId is not null;
        if (_storeBusyId == p.Id)
        {
            actions.Children.Add(Caption("Working…"));
        }
        else if (p.Asset is null)
        {
            actions.Children.Add(new TextBlock { Text = "Not available for this computer", FontSize = 10, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, Width = 110, TextAlignment = TextAlignment.Right });
        }
        else if (inst is null)
        {
            actions.Children.Add(StoreButton("Install", ghost: false, enabled: !busy, () => InstallFromStore(p)));
        }
        else
        {
            if (inst.Version != p.Version)
                actions.Children.Add(StoreButton($"Update to {p.Version}", ghost: false, enabled: !busy, () => InstallFromStore(p)));
            else
                actions.Children.Add(new TextBlock { Text = "Installed", FontSize = 10, Foreground = AccentBright, HorizontalAlignment = HorizontalAlignment.Right });
            actions.Children.Add(StoreButton("Remove", ghost: true, enabled: !busy, () => RemoveFromStore(p)));
        }
        var source = StoreButton("Source", ghost: true, enabled: true, () => _ = Launcher.LaunchUriAsync(new Uri(p.Repo)));
        ToolTip.SetTip(source, "Open the source repository");
        actions.Children.Add(source);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(text);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return new Border
        {
            Background = Raised, BorderBrush = Divider, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Tile, Padding = new Thickness(12, 10), Child = grid,
        };
    }

    // Rows use raised buttons for actions and ghost for secondary links: a list of solid brass
    // Install buttons would break "one primary action per context".
    private static Button StoreButton(string label, bool ghost, bool enabled, Action onClick)
    {
        var b = new Button { Content = label, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Right };
        if (ghost) b.Classes.Add("ghost");
        b.Click += (_, _) => onClick();
        return b;
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
        _storeProgress = new StoreProgress(0, $"Downloading {p.Name}…");
        _storeCts ??= new CancellationTokenSource();
        RenderStoreList();
        var progress = new Progress<StoreProgress>(r => { _storeProgress = r; UpdateStoreStatus(); });
        try
        {
            await _store.InstallAsync(p, progress, _storeCts.Token);
            _storeProgress = new StoreProgress(-1, $"Scanning {p.Name}…");
            UpdateStoreStatus();
            _storeMessage = await RescanAfterStoreChange() ? $"{p.Name} {p.Version} is installed — find it in the browser." : $"{p.Name} is installed. Rescan plugins to use it.";
        }
        catch (PluginStoreException e) { _storeMessage = e.Message; }
        catch (OperationCanceledException) { _storeMessage = null; }
        catch (Exception e)
        {
            App.Services.GetRequiredService<ILogSink>().Error($"Installing {p.Id} failed", e);
            _storeMessage = $"Installing {p.Name} failed: {e.Message}";
        }
        finally { _storeBusyId = null; }
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
        _storeProgress = new StoreProgress(-1, $"Removing {p.Name}…");
        RenderStoreList();
        try
        {
            _store.Uninstall(p.Id);
            await RescanAfterStoreChange();
            _storeMessage = $"{p.Name} was removed.";
        }
        catch (PluginStoreException e) { _storeMessage = e.Message; }
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
