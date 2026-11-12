// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The browser's Files tab beyond the plain sample tree (spec 092): several sample folders
// with their own menu (#268 / #346), folders dropped in from the OS, and the browse view
// (#580) — a Tree / Browse switch, Back / Forward / Up, a path bar whose segments jump and
// which turns into a text field for a typed or pasted path, and Copy path. Kept apart from
// BrowserView.cs, which is the list machinery every tab shares.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.Application;
using Nota.Application.Browser;
using Nota.Application.Localization;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class BrowserView
{
    private Border _filesBar = null!;     // Tree | Browse · Add folder…
    private Border _navBar = null!;       // ‹ › ↑ · path · copy
    private Border _modeSeg = null!;
    private Border _addFolderBtn = null!;
    private Border _backBtn = null!, _fwdBtn = null!, _upBtn = null!, _homeBtn = null!, _copyPathBtn = null!;
    private StackPanel _segments = null!;
    private ScrollViewer _segmentsScroll = null!;
    private TextBox _pathBox = null!;
    private Border _pathWrap = null!;

    /// <summary>A one-line note for the app's status line ("Path copied", why a folder wasn't added).</summary>
    public event Action<string>? StatusMessage;

    private bool BrowseOn => _vm?.BrowseMode == true;

    // Built once from the constructor; shown only on the Files tab.
    private Control BuildFilesBars()
    {
        _modeSeg = SessionView.Segmented(new[] { Loc.T("browser.tree"), Loc.T("browser.browse") }, 0, i => SetBrowseMode(i == 1));
        _modeSeg.HorizontalAlignment = HorizontalAlignment.Left;
        _addFolderBtn = IconButton(GlyphKind.Plus, Loc.T("browser.add_folder"), () => _ = AddFolderAsync());

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(_modeSeg);
        Grid.SetColumn(_addFolderBtn, 1);
        top.Children.Add(_addFolderBtn);
        _filesBar = new Border { Margin = new Thickness(8, 0, 8, 6), Child = top, IsVisible = false };

        _backBtn = IconButton(GlyphKind.ChevronLeft, MenuKit.Keys(Loc.T("browser.back_tip")), () => _vm?.Browse.GoBack());
        _fwdBtn = IconButton(GlyphKind.ChevronRight, MenuKit.Keys(Loc.T("browser.forward_tip")), () => _vm?.Browse.GoForward());
        _upBtn = IconButton(GlyphKind.ChevronUp, MenuKit.Keys(Loc.T("browser.up_tip")), () => _vm?.Browse.Up());
        _homeBtn = IconButton(GlyphKind.Home, Loc.T("browser.home_tip"), () => _vm?.BrowseHome());
        _copyPathBtn = IconButton(GlyphKind.Copy, Loc.T("browser.copy_path"), () => _ = CopyPathAsync(_vm?.Browse.Location));

        // The path: segments as buttons, scrolled to the end so the folder you are in is what
        // stays visible; a click on the bar's empty part (or ⌘L) swaps in a text field.
        _segments = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _segmentsScroll = new ScrollViewer
        {
            Content = _segments,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        _pathBox = new TextBox
        {
            IsVisible = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = NotaType.Caption,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Classes = { "search" },
        };
        _pathBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitPath(); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndPathEdit(); e.Handled = true; }
        };
        _pathBox.LostFocus += (_, _) => EndPathEdit();
        var pathGrid = new Grid();
        pathGrid.Children.Add(_segmentsScroll);
        pathGrid.Children.Add(_pathBox);
        _pathWrap = new Border
        {
            Height = 24, CornerRadius = NotaRadius.Field, Padding = new Thickness(6, 0), Margin = new Thickness(4, 0),
            BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Child = pathGrid,
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Ibeam),
        };
        _pathWrap.BindResource(Border.BackgroundProperty, "Brush.Well");
        _pathWrap.PointerPressed += (_, e) =>
        {
            // Segment buttons handle their own clicks; the empty part edits.
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
            BeginPathEdit();
            e.Handled = true;
        };

        var nav = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto") };
        nav.Children.Add(_backBtn);
        Grid.SetColumn(_fwdBtn, 1); nav.Children.Add(_fwdBtn);
        Grid.SetColumn(_upBtn, 2); nav.Children.Add(_upBtn);
        Grid.SetColumn(_homeBtn, 3); nav.Children.Add(_homeBtn);
        Grid.SetColumn(_pathWrap, 4); nav.Children.Add(_pathWrap);
        Grid.SetColumn(_copyPathBtn, 5); nav.Children.Add(_copyPathBtn);
        _navBar = new Border { Margin = new Thickness(8, 0, 8, 6), Child = nav, IsVisible = false };

        AddHandler(KeyDownEvent, OnFilesKey, RoutingStrategies.Tunnel);

        // Folders dropped on the Files list join the sample folders.
        var files = _pages[FilesTab];
        DragDrop.SetAllowDrop(files, true);
        files.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            if (IsBrowserDrag || DeviceDrag.IsActive) return;
            if (ExternalPaths(e).Any(System.IO.Directory.Exists)) { e.DragEffects = DragDropEffects.Copy; e.Handled = true; }
        });
        files.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (IsBrowserDrag || _settings is null) return;
            var dirs = ExternalPaths(e).Where(System.IO.Directory.Exists).ToList();
            if (dirs.Count == 0) return;
            Say(SampleFolderUi.AddAll(_settings, dirs));
            e.Handled = true;
        });

        return new StackPanel { Children = { _filesBar, _navBar } };
    }

    // A 24px icon button in the browser's style: no fill at rest, the well on hover.
    private static Border IconButton(GlyphKind kind, string tip, Action click)
    {
        var icon = RailIcon(kind, 14, "Brush.TextSecondary");
        // Disabled reads in the disabled ink, not through transparency (DESIGN.md).
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var btn = new Border
        {
            Width = 24, Height = 24, CornerRadius = NotaRadius.Field, Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand), Child = icon,
        };
        btn.PointerEntered += (_, _) => { if (btn.IsEnabled) btn.BindResource(Border.BackgroundProperty, "Brush.Well"); };
        btn.PointerExited += (_, _) => btn.Background = Brushes.Transparent;
        btn.PointerPressed += (_, e) => { if (btn.IsEnabled && e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed) { click(); e.Handled = true; } };
        btn.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsEnabledProperty) icon.BindResource(Glyph.ForegroundProperty, btn.IsEnabled ? "Brush.TextSecondary" : "Brush.TextDisabled");
        };
        ToolTip.SetTip(btn, tip);
        return btn;
    }

    private void Say(string? note)
    {
        if (!string.IsNullOrEmpty(note)) StatusMessage?.Invoke(note);
    }

    // Wire the VM's browse events (from SetViewModel).
    private void HookBrowse(BrowserViewModel vm)
    {
        vm.BrowseItems.CollectionChanged += (_, _) => { if (_active == FilesTab && BrowseOn) RefreshCounts(); };
        vm.BrowseRefreshed += () =>
        {
            RenderPath();
            if (vm.BrowseSelect is { } sel && vm.BrowseItems.FirstOrDefault(r => SampleFolderRules.PathComparer.Equals(r.Path, sel)) is { } row)
            {
                // Selected for the eye, not the ear: Locate doesn't audition (#581).
                _suppressPreview = true;
                _pages[FilesTab].SelectedItem = row;
                _pages[FilesTab].ScrollIntoView(row);
                _suppressPreview = false;
            }
            RefreshCounts();
        };
        if (_settings is not null) _settings.SampleFoldersChanged += () => Dispatcher.UIThread.Post(() => vm.RebuildSamples());
        ApplyBrowseMode();
        if (vm.BrowseMode) _ = vm.RefreshBrowseAsync();
    }

    private void SetBrowseMode(bool on)
    {
        if (_vm is null || _vm.BrowseMode == on) return;
        // Switching to Browse from a folder row in the tree opens that folder.
        if (on && _pages[FilesTab].SelectedItem is BrowserItem { Kind: BrowserItemKind.Folder } f && !f.Unavailable)
            _vm.BrowseTo(f.Path);
        _vm.BrowseMode = on;
        ApplyBrowseMode();
    }

    // The Files list shows the tree or the browse list; the bars and counts follow.
    private void ApplyBrowseMode()
    {
        if (_vm is null) return;
        bool on = _vm.BrowseMode;
        _pages[FilesTab].ItemsSource = on ? _vm.BrowseItems : _vm.Samples;
        if (_modeSeg.Child is Avalonia.Controls.Primitives.UniformGrid g)
            for (int i = 0; i < g.Children.Count; i++)
                if (g.Children[i] is Avalonia.Controls.Primitives.ToggleButton tb) tb.IsChecked = (i == 1) == on;
        UpdateFilesBars();
        RenderPath();
        if (_active == FilesTab) RefreshCounts();
    }

    private void UpdateFilesBars()
    {
        bool files = _active == FilesTab && _vm is not null;
        _filesBar.IsVisible = files;
        _addFolderBtn.IsVisible = files && !BrowseOn;
        _navBar.IsVisible = files && BrowseOn;
        // The sample filter chips belong to the indexed tree, not to an arbitrary folder.
        _sampleChipsHost.IsVisible = files && !BrowseOn && _vm?.HasSampleIndex == true;
    }

    private void RenderPath()
    {
        if (_vm is null || !BrowseOn) return;
        var b = _vm.Browse;
        _backBtn.IsEnabled = b.CanGoBack;
        _fwdBtn.IsEnabled = b.CanGoForward;
        _upBtn.IsEnabled = b.CanGoUp;
        _homeBtn.IsEnabled = !_vm.BrowseAtHome;
        _copyPathBtn.IsEnabled = !b.IsComputer;
        _segments.Children.Clear();
        var computer = SegmentButton(Loc.T("browser.computer"), FileBrowseState.Computer, b.IsComputer);
        _segments.Children.Add(computer);
        var segs = b.Segments();
        for (int i = 0; i < segs.Count; i++)
        {
            _segments.Children.Add(SegmentSeparator());
            _segments.Children.Add(SegmentButton(segs[i].Label, segs[i].Path, i == segs.Count - 1));
        }
        ToolTip.SetTip(_pathWrap, b.IsComputer ? Loc.T("browser.computer") : b.Location);
        Dispatcher.UIThread.Post(() => _segmentsScroll.Offset = new Vector(double.MaxValue, 0), DispatcherPriority.Loaded);
    }

    private Control SegmentButton(string label, string path, bool current)
    {
        var text = new TextBlock { Text = label, FontSize = NotaType.Caption, VerticalAlignment = VerticalAlignment.Center };
        text.BindResource(TextBlock.ForegroundProperty, current ? "Brush.TextPrimary" : "Brush.TextSecondary");
        var btn = new Button
        {
            Content = text, Padding = new Thickness(3, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Classes = { "ghost" },
        };
        btn.Click += (_, _) => { if (!current) _vm?.BrowseTo(path); };
        return btn;
    }

    private static Control SegmentSeparator()
    {
        var g = RailIcon(GlyphKind.CaretRight, 9, "Brush.TextTertiary");
        g.VerticalAlignment = VerticalAlignment.Center;
        return g;
    }

    private void BeginPathEdit()
    {
        if (_vm is null || !BrowseOn) return;
        _segmentsScroll.IsVisible = false;
        _pathBox.IsVisible = true;
        _pathBox.Text = _vm.Browse.IsComputer ? "" : _vm.Browse.Location;
        _pathWrap.BindResource(Border.BorderBrushProperty, "Brush.Accent");
        Dispatcher.UIThread.Post(() => { _pathBox.Focus(); _pathBox.SelectAll(); }, DispatcherPriority.Input);
    }

    private void EndPathEdit()
    {
        if (!_pathBox.IsVisible) return;
        _pathBox.IsVisible = false;
        _segmentsScroll.IsVisible = true;
        _pathWrap.BorderBrush = Brushes.Transparent;
    }

    private void CommitPath()
    {
        if (_vm is null) return;
        string text = _pathBox.Text ?? "";
        if (PathInput.Parse(text) is not { } target)
        {
            _pathWrap.BindResource(Border.BorderBrushProperty, "Brush.Danger");
            Say(Loc.F("browser.cant_open", text.Trim()));
            return;
        }
        EndPathEdit();
        _vm.BrowseTo(target.Folder, target.SelectFile);
    }

    private async System.Threading.Tasks.Task CopyPathAsync(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
        {
            await cb.SetTextAsync(path);
            Say(Loc.T("browser.path_copied"));
        }
    }

    private async System.Threading.Tasks.Task AddFolderAsync()
    {
        if (_settings is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var picked = await top.PickFoldersAsync(new FolderPickerOpenOptions
        {
            Title = Loc.T("preferences.add_sample_folder"),
            AllowMultiple = true,
        });
        var paths = picked.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) Say(SampleFolderUi.AddAll(_settings, paths));
    }

    // ⌘[ ⌘] ⌘↑ (Ctrl elsewhere, where Alt+Left / Right / Up work too) walk the browse view;
    // ⌘L / Ctrl+L edits the path.
    private void OnFilesKey(object? sender, KeyEventArgs e)
    {
        if (_vm is null || _active != FilesTab || _collapsed || _search.IsFocused || _pathBox.IsFocused) return;
        bool mac = OperatingSystem.IsMacOS();
        var cmd = mac ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers == cmd && e.Key == Key.L)
        {
            if (!BrowseOn) { SetBrowseMode(true); }
            BeginPathEdit();
            e.Handled = true;
            return;
        }
        if (!BrowseOn) return;
        // Enter opens the selected folder (or drive) of the browse view.
        if (e.Key is Key.Enter or Key.Return && e.KeyModifiers == KeyModifiers.None
            && _pages[FilesTab].SelectedItem is BrowserItem { Kind: BrowserItemKind.Folder } folder)
        {
            _vm.BrowseTo(folder.Path);
            e.Handled = true;
            return;
        }
        bool alt = !mac && e.KeyModifiers == KeyModifiers.Alt;
        bool back = e.KeyModifiers == cmd && e.Key == Key.OemOpenBrackets || alt && e.Key == Key.Left;
        bool fwd = e.KeyModifiers == cmd && e.Key == Key.OemCloseBrackets || alt && e.Key == Key.Right;
        bool up = e.KeyModifiers == cmd && e.Key == Key.Up || alt && e.Key == Key.Up;
        if (back) _vm.Browse.GoBack();
        else if (fwd) _vm.Browse.GoForward();
        else if (up) _vm.Browse.Up();
        else return;
        e.Handled = true;
    }

    // --- context menu additions -------------------------------------------------------------

    // A top-level sample folder: Rename · Set as default · Reveal · Remove from browser.
    private void AddPlaceItems(MenuFlyout flyout, BrowserItem item)
    {
        if (_settings is null || item.PlaceIndex < 0) return;
        var folders = _settings.ResolvedSampleFolders();
        if (item.PlaceIndex >= folders.Count) return;
        var f = folders[item.PlaceIndex];
        var owner = TopLevel.GetTopLevel(this) as Window;
        flyout.Items.Add(MenuKit.Item(Loc.T("common.rename"), GlyphKind.Edit, () => { if (owner is not null) _ = SampleFolderUi.RenameAsync(owner, _settings, f); }));
        flyout.Items.Add(MenuKit.Item(Loc.T("browser.set_as_default"), null, () => _settings.SetDefaultSampleFolder(f.Index), enabled: !f.IsDefault && f.Available));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(MenuKit.Danger(Loc.T("browser.remove_from_browser"), GlyphKind.Minus,
            () => { if (owner is not null) _ = SampleFolderUi.RemoveAsync(owner, _settings, f); }, enabled: folders.Count > 1));
    }

    private MenuItem CopyPathItem(BrowserItem item)
        => MenuKit.Item(Loc.T("browser.copy_path"), GlyphKind.Copy, () => _ = CopyPathAsync(item.Path));

    // Browse view folder: open it, or keep it as a sample folder.
    private void AddBrowseFolderItems(MenuFlyout flyout, BrowserItem item)
    {
        flyout.Items.Add(MenuKit.Item(Loc.T("common.open"), null, () => _vm?.BrowseTo(item.Path)));
        if (_settings is not null && !item.IsVolume)
            flyout.Items.Add(MenuKit.Item(Loc.T("browser.add_this_folder"), GlyphKind.Plus,
                () => Say(SampleFolderUi.AddAll(_settings, new[] { item.Path }) ?? Loc.F("browser.folder_added", item.Name))));
    }

    // --- Locate (spec 092, #581) ------------------------------------------------------------

    /// <summary>Shows <paramref name="path"/> in the Files tab: in the tree when it lies in a
    /// sample folder (its folders opened, scrolled to, selected — not auditioned), otherwise in
    /// the browse view at its folder.</summary>
    public void RevealFile(string path)
    {
        if (_vm is null) return;
        SetCollapsed(false, persist: true);
        SelectTab(FilesTab);
        var inFolder = _settings?.ResolvedSampleFolders().Any(f => f.Available && SampleFolderRules.IsSameOrInside(path, f.Path)) == true;
        if (inFolder)
        {
            if (BrowseOn) { _vm.BrowseMode = false; ApplyBrowseMode(); }
            _search.Text = "";
            var row = _vm.RevealSample(path);
            if (row is null)
            {
                // The tree is still being walked (startup, a big folder just added): try
                // again once it is in.
                if (_vm.SamplesBuilding && !_revealPending)
                {
                    _revealPending = true;
                    void Retry() { _vm.SamplesRebuilt -= Retry; _revealPending = false; RevealFile(path); }
                    _vm.SamplesRebuilt += Retry;
                }
                return;
            }
            _suppressPreview = true;
            _pages[FilesTab].SelectedItem = row;
            _pages[FilesTab].ScrollIntoView(row);
            _suppressPreview = false;
        }
        else
        {
            _search.Text = "";   // a query that misses the file would filter it out of view
            if (!BrowseOn) { _vm.BrowseMode = true; ApplyBrowseMode(); }
            _vm.BrowseTo(System.IO.Path.GetDirectoryName(path) ?? path, path);
        }
    }

    private bool _suppressPreview;
    private bool _revealPending;
}
