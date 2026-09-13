// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M4.1-C / M7-4 + Phase 3 + the "compact index" refresh (mockup 1a): the left
// browser, an island panel with a 40px icon rail (Instr / FX / MIDI / Files /
// Preset / Proj / Map) down its left edge. Each tab shows one list of single-line
// 24px rows, sectioned into BUILT-IN and PLUG-INS with counts; the "Nota" prefix
// drops to tertiary ink so names scan on their distinctive word, and the device
// type sits at the right edge as a quiet tag. A search box filters the active list
// live, a ⋮ button holds the view options, chips filter by favorite/tag, a status
// line counts what is on screen, and a pinned preview footer auditions the selected
// sample (Files tab). Double-clicking an item raises ItemActivated; the footer ▶ (or
// Auto-audition on selection) raises PreviewRequested. Brushes bind via
// GetResourceObservable (the control is built during MainWindow's XAML load, before
// it is attached). Rows are drag sources (M7-5).

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed class BrowserView : UserControl
{
    // The browser drag payload. macOS's native drag session requires exactly one
    // pasteboard item per drag image, and Avalonia makes one drag image per data
    // FORMAT while only *native* formats become pasteboard items — an in-process
    // format is never native, so it always mismatches and crashes the NSDragging
    // session. So the drag carries a single native text item (pasteboard filler)
    // and the actual BrowserItem is handed off in-process via this static (the drag
    // is in-process anyway; DoDragDropAsync blocks the UI thread until drop).
    private const string DragSentinel = "nota-browser-item";
    internal static BrowserItem? CurrentDrag { get; private set; }

    /// <summary>True while a browser row is being dragged (used by drop targets).</summary>
    public static bool IsBrowserDrag => CurrentDrag is not null;

    // --- external file drops (from Finder / other programs) -----------------
    // Audio extensions Nota can import as a clip / sample. Anything else in an
    // external drag is ignored.
    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
    { ".wav", ".wave", ".aif", ".aiff", ".flac", ".mp3", ".ogg", ".m4a", ".aac" };

    /// <summary>A drag a sample target should light up for: an internal browser row,
    /// or an external drag that carries file(s). Lenient by design — used in DragOver,
    /// where some backends don't expose the file list yet; the drop re-checks.</summary>
    public static bool IsAcceptableDrag(DragEventArgs e)
        => IsBrowserDrag || e.DataTransfer.Contains(DataFormat.File);

    /// <summary>Local paths of the audio files carried by an external drag (empty if
    /// none / not a file drag).</summary>
    private static List<string> ExternalAudioFiles(DragEventArgs e)
    {
        var paths = new List<string>();
        if (!e.DataTransfer.Contains(DataFormat.File)) return paths;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return paths;
        foreach (var f in files)
        {
            var p = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p) && AudioExts.Contains(System.IO.Path.GetExtension(p))) paths.Add(p!);
        }
        return paths;
    }

    /// <summary>The items a drop should load: the internal browser item (any kind), or
    /// one synthesized Sample item per external audio file. Empty when there's nothing
    /// we can use (e.g. a non-audio external file). Callers that take one use [0].</summary>
    public static IReadOnlyList<BrowserItem> DroppedItems(DragEventArgs e)
    {
        if (CurrentDrag is { } item) return new[] { item };
        var list = new List<BrowserItem>();
        foreach (var p in ExternalAudioFiles(e))
            list.Add(new BrowserItem
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(p),
                Kind = BrowserItemKind.Sample,
                Path = p,
            });
        return list;
    }

    private BrowserItem? _pressedItem;
    private Point _pressPoint;
    private PointerPressedEventArgs? _pressArgs;

    // Rail icons — stroke glyphs on the design's 24-unit grid, 1.5 stroke, round caps and
    // joins. Each sits in its OWN optical box inside that grid (roughly 18 wide by 14–18
    // tall), which is how they end up looking the same weight next to each other — so they
    // must all be scaled by one factor, not each stretched to fill the icon box. GridIcon
    // does that; see the note there.
    // Instruments — a piano keyboard: case with key dividers and three short black keys.
    private const string IconKeyboard = "M3.5 6 h17 v12 h-17 z M9.2 6 v12 M14.8 6 v12 M6.3 6 v5 M12 6 v5 M17.7 6 v5";
    // Audio effects — vertical faders with offset handles (a mixer/EQ strip).
    private const string IconSliders = "M7 4.5 v15 M12 4.5 v15 M17 4.5 v15 M5 9 h4 M10 14.5 h4 M15 7.5 h4";
    // Files — a folder with a raised tab on the left.
    private const string IconFolder = "M3.5 7.5 h5.6 l2.1 2.6 h9.3 v9.4 h-17 z";
    private const string IconStar = "M12 3 L14.5 9 L21 9.3 L16 13.5 L17.7 20 L12 16.2 L6.3 20 L8 13.5 L3 9.3 L9.5 9 Z";
    // Presets — a bookmark (a saved snapshot). Distinct from the fader glyphs above.
    private const string IconBookmark = "M6.5 4 h11 v16 l-5.5 -4.4 l-5.5 4.4 z";
    // Projects — a document with a dog-ear and two content lines.
    private const string IconDoc = "M6 3.5 h7.6 l4.8 4.8 v12.2 h-12.4 z M13.6 3.5 v4.8 h4.8 M9.2 13.4 h6 M9.2 16.8 h6";
    private const string IconPlay = "M7 4 L19 12 L7 20 Z";
    // MIDI effects — an eighth note: head bottom-left, tall stem, flag curling to the right.
    private const string IconMidi = "M6 17.2 a2.6 2.4 0 1 0 5.2 0 a2.6 2.4 0 1 0 -5.2 0 M11.2 17.2 V5 c3 .9 5.6 2.4 5.6 5.6";
    // MIDI-learn map — two nodes joined by a link (a control mapped to a parameter).
    private const string IconMap = "M3.4 17.6 a2.6 2.6 0 1 0 5.2 0 a2.6 2.6 0 1 0 -5.2 0 M15.4 6.4 a2.6 2.6 0 1 0 5.2 0 a2.6 2.6 0 1 0 -5.2 0 M7.9 15.7 L16.1 8.3";
    // View options — three centred rules, shortening downward (a sort/·filter glyph).
    private const string IconOptions = "M4 7 H20 M7 12 H17 M10 17 H14";

    /// <summary>One glyph off the design's 24-unit grid, rendered at <paramref name="size"/>.
    /// The Path keeps the full 24×24 box and draws at its native coordinates; a Viewbox then
    /// scales that square as a whole. Stretch.Uniform on the Path would instead normalise
    /// every glyph to its own bounding box — a wide one and a tall one would come out the
    /// same size, which is exactly the optical balance the grid exists to set. Pass
    /// <paramref name="strokeToken"/> only where no style already paints the stroke: a local
    /// value outranks a style setter, and the rail's active/inactive colours are styles.</summary>
    private static Viewbox GridIcon(string data, double size, string? strokeToken = null, double strokeWidth = 1.5)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Width = 24, Height = 24,
            Stretch = Stretch.None,
            StrokeThickness = strokeWidth,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
        };
        if (strokeToken is not null) path.BindResource(Shape.StrokeProperty, strokeToken);
        return new Viewbox
        {
            Width = size, Height = size,
            Stretch = Stretch.Uniform,
            Child = path,
        };
    }

    private const int TabCount = 7;   // Instr / FX / MIDI / Files / Preset / Proj / Map
    private const int FilesTab = 3;   // the only tab with a sample auditioner
    private const int MapTab = 6;     // MIDI-learn mappings — hosts a MidiMapView, not a list
    private const double RowH = 24;   // single-line index row
    private const double GroupRowH = 22;

    private readonly ListBox[] _pages;
    private readonly Border[] _tabs;
    private readonly ObservableCollection<BrowserItem>[] _filtered;
    private readonly IReadOnlyList<BrowserItem>?[] _sources = new IReadOnlyList<BrowserItem>?[TabCount];
    private readonly ContentControl _content = new();
    private int _active = -1;
    private BrowserViewModel? _vm;
    private ISettingsService? _settings;
    private MidiMapView? _midiMap;

    // Disclosure triangles for tree parent rows.
    private const string IconCollapsed = "M1 0 L6 4.5 L1 9 Z";  // ▸
    private const string IconExpanded = "M0 1 L9 1 L4.5 7 Z";   // ▾

    private readonly TextBox _search;
    private readonly TextBlock _matchCount;
    private readonly Border _searchWrap;
    private readonly Border _optionsBtn;

    // Centered empty-state message shown over a tab whose list is empty.
    private readonly TextBlock _emptyText = new();
    private readonly Border _emptyWrap;

    // Pinned preview footer + the status line under it.
    private readonly Button _previewBtn;
    private readonly Path _previewIcon;
    private readonly TextBlock _previewName;
    private readonly TextBlock _previewSub;
    private readonly ToggleButton _autoAudition;
    private readonly PreviewWaveform _previewWave;
    private readonly Border _previewFooter;   // sample auditioner — Files tab only
    private readonly Border _statusBar;
    private readonly TextBlock _statusText;
    private BrowserItem? _footerItem;

    public event Action<BrowserItem>? ItemActivated;
    public event Action<BrowserItem>? PreviewRequested;
    /// <summary>Reveal a sample / folder / project in the system file manager.</summary>
    public event Action<BrowserItem>? RevealRequested;
    /// <summary>Delete a project bundle (moves to Trash after confirmation).</summary>
    public event Action<BrowserItem>? DeleteProjectRequested;
    /// <summary>Open the tag editor. Item null = manage all tags; non-null = create a tag
    /// and assign it to that device.</summary>
    public event Action<BrowserItem?>? EditTagsRequested;

    // Header filter chips (★ Favorites + one per tag); rebuilt from the VM's tags.
    private readonly Border _chipsHost;
    private readonly ChipStrip _chipsPanel;
    private readonly Border _overflowChip;
    private readonly TextBlock _overflowText;

    public BrowserView()
    {
        MinWidth = 160; // stretches to fill its (resizable) grid column

        var template = BuildItemTemplate();
        _pages = new ListBox[TabCount];
        _filtered = new ObservableCollection<BrowserItem>[TabCount];
        for (int i = 0; i < _pages.Length; i++)
        {
            _filtered[i] = new ObservableCollection<BrowserItem>();
            _pages[i] = NewList(template);
            _pages[i].ItemsSource = _filtered[i];
            HookActivation(_pages[i]);
        }

        _tabs = new[]
        {
            RailTab(0, "Instruments", IconKeyboard),
            RailTab(1, "Audio effects", IconSliders),
            RailTab(2, "MIDI effects", IconMidi),
            RailTab(3, "Files", IconFolder),
            RailTab(4, "Presets", IconBookmark),
            RailTab(5, "Projects", IconDoc),
            RailTab(6, "MIDI map", IconMap),
        };
        // A hairline above Files and above Map: devices · library · mappings.
        foreach (int i in new[] { 3, 6 })
        {
            _tabs[i].BorderThickness = new Thickness(0, 1, 0, 0);
            _tabs[i].BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        }
        var rail = new StackPanel { Width = 40 };
        foreach (var t in _tabs) rail.Children.Add(t);

        // --- Search (26h): accent border on focus, match count on the right ---
        _search = new TextBox
        {
            Watermark = "Search",
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 11,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "search" },
        };
        _search.TextChanged += (_, _) => { ApplyFilter(_active); };
        _search.GotFocus += (_, _) => _searchWrap!.BindResource(Border.BorderBrushProperty, "Brush.Accent");
        _search.LostFocus += (_, _) => _searchWrap!.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        _matchCount = new TextBlock
        {
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        _matchCount.Classes.Add("Mono");
        _matchCount.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        var searchGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        searchGrid.Children.Add(_search);
        Grid.SetColumn(_matchCount, 1);
        searchGrid.Children.Add(_matchCount);
        _searchWrap = new Border
        {
            Height = 26,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 0),
            Child = searchGrid,
        };
        _searchWrap.BindResource(Border.BackgroundProperty, "Brush.BgSunken");
        _searchWrap.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");

        // Sort & view options — the row's shape, sectioning and tag management.
        _optionsBtn = BuildOptionsButton();

        var headerRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(8, 8, 8, 6),
        };
        headerRow.Children.Add(_searchWrap);
        Grid.SetColumn(_optionsBtn, 1);
        headerRow.Children.Add(_optionsBtn);

        // Filter chips (★ Favorites + tags). Populated from the VM in SetViewModel and
        // shown only on the device tabs (Instr / FX / MIDI). One line: the chips that
        // don't fit collapse into a "+N" that opens the rest.
        _overflowText = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        _overflowText.Classes.Add("Mono");
        _overflowText.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        _overflowChip = new Border
        {
            Height = 20,
            Padding = new Thickness(4, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = _overflowText,
        };
        _overflowChip.PointerPressed += (_, e) => { ShowOverflowChips(); e.Handled = true; };
        _chipsPanel = new ChipStrip(_overflowChip, _overflowText);
        _chipsHost = new Border { Margin = new Thickness(8, 0, 8, 7), Child = _chipsPanel };

        _previewFooter = BuildPreviewFooter(out _previewBtn, out _previewIcon, out _previewName,
                                            out _previewSub, out _autoAudition, out _previewWave);

        // Status line: what the active tab is showing, counted.
        _statusText = new TextBlock { FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
        _statusText.Classes.Add("Mono");
        _statusText.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        _statusBar = new Border
        {
            Height = 26,
            Padding = new Thickness(10, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _statusText,
        };
        _statusBar.BindResource(Border.BackgroundProperty, "Brush.BgApp");
        _statusBar.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");

        // Empty-state message, centered over the list when a tab has nothing to show.
        _emptyText.FontSize = 11;
        _emptyText.TextWrapping = TextWrapping.Wrap;
        _emptyText.TextAlignment = TextAlignment.Center;
        _emptyText.LineHeight = 16;
        _emptyText.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        _emptyWrap = new Border
        {
            IsHitTestVisible = false,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 240,
            Padding = new Thickness(20, 0),
            Child = _emptyText,
        };
        var contentHost = new Grid();
        contentHost.Children.Add(_content);
        contentHost.Children.Add(_emptyWrap);

        var right = new DockPanel();
        var topStack = new StackPanel { Children = { headerRow, _chipsHost } };
        DockPanel.SetDock(topStack, Dock.Top);
        DockPanel.SetDock(_statusBar, Dock.Bottom);
        DockPanel.SetDock(_previewFooter, Dock.Bottom);
        right.Children.Add(topStack);
        right.Children.Add(_statusBar);
        right.Children.Add(_previewFooter);
        right.Children.Add(contentHost);

        var railDivider = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Child = rail };
        railDivider.BindResource(Border.BackgroundProperty, "Brush.BgApp");
        railDivider.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(railDivider, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(railDivider);
        grid.Children.Add(right);

        // The island: a rounded card floating on the app ground. ClipToBounds keeps the
        // rail, the full-bleed rows and the status line inside the rounded corners.
        var outer = new Border
        {
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = grid,
        };
        outer.BindResource(Border.CornerRadiusProperty, "Radius.Md");
        outer.BindResource(Border.BackgroundProperty, "Brush.SurfaceCard");
        outer.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        Content = outer;

        SelectTab(0);
    }

    /// <summary>Give the browser the MIDI-learn service so the Map tab can show/edit mappings.</summary>
    public void SetMidiLearn(MidiLearnService learn)
    {
        _midiMap = new MidiMapView(learn);
        if (_active == MapTab) _content.Content = _midiMap;
    }

    /// <summary>Reveal the MIDI-mappings tab (called when learn mode is armed).</summary>
    public void ShowMidiMap() => SelectTab(MapTab);

    /// <summary>Persist the view options (the ⋮ menu) across sessions. Optional: without it
    /// the browser still works, it just forgets the toggles on quit.</summary>
    public void SetSettings(ISettingsService settings)
    {
        _settings = settings;
        if (_vm is null) return;
        _vm.GroupBySource = settings.Current.BrowserGroupBySource;
        _vm.FavoritesFirst = settings.Current.BrowserFavoritesFirst;
        RefreshRows();
    }

    public void SetViewModel(BrowserViewModel vm)
    {
        _vm = vm;
        if (_settings is not null)
        {
            vm.GroupBySource = _settings.Current.BrowserGroupBySource;
            vm.FavoritesFirst = _settings.Current.BrowserFavoritesFirst;
        }
        // Tree tabs (Instruments / FX / MIDI / Files / Presets) bind straight to the VM's
        // flattened visible collections; the VM owns their filtering + expand/collapse.
        _pages[0].ItemsSource = vm.Instruments;
        _pages[1].ItemsSource = vm.Effects;
        _pages[2].ItemsSource = vm.MidiEffects;
        _pages[3].ItemsSource = vm.Samples;
        _pages[4].ItemsSource = vm.Presets;
        for (int i = 0; i <= 4; i++)
        {
            int idx = i;
            VisibleOf(idx)!.CollectionChanged += (_, _) => { if (_active == idx) RefreshCounts(); };
        }

        // Flat tabs (Projects) keep the view-side substring filter.
        var cols = new ObservableCollection<BrowserItem>?[] { null, null, null, null, null, vm.Projects };
        for (int i = 5; i < cols.Length; i++)
        {
            int idx = i;
            _sources[idx] = cols[idx];
            cols[idx]!.CollectionChanged += (_, _) => ApplyFilter(idx);
            ApplyFilter(idx);
        }
        for (int i = 0; i <= 4; i++) ApplyFilter(i);

        // Rebuild the header chips whenever tags/favorites change (also refreshes active state).
        vm.LibraryChanged += RebuildChips;
        RebuildChips();
        RefreshCounts();
    }

    private ObservableCollection<BrowserItem>? VisibleOf(int tab) => tab switch
    {
        0 => _vm?.Instruments,
        1 => _vm?.Effects,
        2 => _vm?.MidiEffects,
        3 => _vm?.Samples,
        4 => _vm?.Presets,
        _ => null,
    };

    private void ApplyFilter(int index)
    {
        if (index < 0 || index >= _filtered.Length) return;
        // Tree tabs (devices + Files + Presets): the VM re-filters + flattens into its collections.
        if (index is >= 0 and <= 4)
        {
            _vm?.FilterTree(index, _search.Text?.Trim() ?? "");
            if (index == _active) RefreshCounts();
            return;
        }
        var src = _sources[index];
        var dst = _filtered[index];
        dst.Clear();
        if (src is not null)
        {
            string q = _search.Text?.Trim() ?? "";
            foreach (var it in src)
            {
                if (q.Length == 0
                    || it.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || it.Sub.Contains(q, StringComparison.OrdinalIgnoreCase))
                    dst.Add(it);
            }
        }
        if (index == _active) RefreshCounts();
    }

    // Re-flatten the visible lists (after a view option changed) without touching the query.
    private void RefreshRows()
    {
        if (_vm is null) return;
        for (int i = 0; i <= 4; i++) _vm.FilterTree(i, _search.Text?.Trim() ?? "");
        RefreshCounts();
    }

    // The search-box match count, the status line and the empty state all describe the
    // same thing — what the active tab is showing — so they refresh together.
    private void RefreshCounts()
    {
        if (_active < 0 || _active == MapTab) return;
        _matchCount.Text = VisibleCount(_active).ToString();
        _statusText.Text = StatusFor(_active);
        UpdateEmptyState();
    }

    // Show the centered empty-state message when the active tab has nothing to display.
    private void UpdateEmptyState()
    {
        if (_active < 0) return;
        bool empty = VisibleCount(_active) == 0;
        _emptyWrap.IsVisible = empty;
        if (empty) _emptyText.Text = EmptyMessage(_active);
    }

    private string EmptyMessage(int tab)
    {
        bool searching = !string.IsNullOrEmpty(_search.Text?.Trim());
        bool filtering = tab is 0 or 1 or 2 && _vm is not null && _vm.FilterMode != BrowserFilter.None;
        if (searching || filtering) return "No matches — try a different search or clear the filters.";
        return tab switch
        {
            0 => "Instruments live here — Nota's built-in synths plus your VST/AU plug-ins.\n\nScan your plug-ins in Settings ▸ Plug-ins to add more.",
            1 => "Audio effects live here — Nota's built-in effects plus your VST/AU plug-ins.\n\nScan your plug-ins in Settings ▸ Plug-ins to add more.",
            2 => "MIDI effects live here — arpeggiator, chord, scale and more.\n\nThey process notes before an instrument; drop one to the left of an instrument in a track.",
            3 => "No samples yet.\n\nAdd audio files to your Samples folder (set it in Settings ▸ Folders), then browse them here as a folder tree. You can also drag files in from Finder.",
            4 => "No presets yet.\n\nSave a preset from any instrument or effect (the Save button on the device), and it appears here grouped by category and device.",
            _ => "No projects yet.\n\nSave a project (⌘S) into your Projects folder (set it in Settings ▸ Folders) and it shows up here.",
        };
    }

    // Rows the user can actually act on — section headers are chrome, not matches.
    private int VisibleCount(int index)
    {
        var src = VisibleOf(index) ?? (IReadOnlyList<BrowserItem>)_filtered[index];
        int n = 0;
        foreach (var it in src) if (!it.IsGroup) n++;
        return n;
    }

    // "13 instruments · 10 built-in · 3 plug-ins" — the device tabs split by source, the
    // library tabs just counted. Everything here counts what is on screen, filters included.
    private string StatusFor(int tab)
    {
        if (_vm is null) return "";
        if (tab is 0 or 1 or 2)
        {
            var (bi, pl) = _vm.VisibleDeviceCounts(tab);
            // The total is already in the search box, so spend the line on the split — it is
            // the thing the sections exist to show, and it fits a narrow panel.
            return $"{bi} built-in · {Plural(pl, "plug-in")}";
        }
        int n = VisibleCount(tab);
        return tab switch
        {
            3 => Plural(n, "sample"),
            4 => Plural(n, "preset"),
            _ => Plural(n, "project"),
        };
    }

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    private void SelectTab(int index)
    {
        if (index == _active) return;
        _active = index;
        for (int i = 0; i < _tabs.Length; i++)
            _tabs[i].Classes.Set("active", i == index);
        // The MIDI-map tab hosts a bespoke editor, not a filtered item list.
        bool isMap = index == MapTab;
        _content.Content = isMap ? (Control?)_midiMap : _pages[index];
        _searchWrap.IsVisible = !isMap;
        _optionsBtn.IsVisible = !isMap;
        // The sample auditioner only makes sense for the Files tab.
        _previewFooter.IsVisible = index == FilesTab;
        // Favorite/tag filter chips apply only to the device tabs.
        _chipsHost.IsVisible = index is 0 or 1 or 2;
        _statusBar.IsVisible = !isMap;
        if (isMap) { _matchCount.Text = ""; _emptyWrap.IsVisible = false; return; }
        RefreshCounts();
    }

    private Border RailTab(int index, string caption, string iconData)
    {
        // Icon-only rail; the caption survives as a tooltip. 16px icon in a 36px tab, with
        // a 2px brass edge on the left when active.
        var icon = GridIcon(iconData, 16);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var bar = new Border { Classes = { "RailBar" }, VerticalAlignment = VerticalAlignment.Stretch };
        var inner = new Grid();
        inner.Children.Add(icon);
        inner.Children.Add(bar);
        var tab = new Border { Classes = { "RailTab" }, Child = inner };
        ToolTip.SetTip(tab, caption);
        tab.PointerPressed += (_, _) => SelectTab(index);
        return tab;
    }

    // --- rows ----------------------------------------------------------------

    private IDataTemplate BuildItemTemplate() => new FuncDataTemplate<BrowserItem>((item, _) =>
    {
        if (item is null) return new Control();
        return item.IsGroup ? GroupRow(item) : IndexRow(item);
    }, supportsRecycling: false);

    // A section header: caption, a hairline filling the gap, and the section's count.
    private Control GroupRow(BrowserItem item)
    {
        var label = new TextBlock
        {
            Text = item.Name,
            Classes = { "GroupLabel" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 0) };
        rule.BindResource(Border.BackgroundProperty, "Brush.BorderDefault");
        var count = new TextBlock
        {
            Text = item.GroupCount.ToString(),
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        count.Classes.Add("Mono");
        count.BindResource(TextBlock.ForegroundProperty, "Brush.TextDisabled");

        var grid = new Grid { Height = GroupRowH, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(label);
        Grid.SetColumn(rule, 1);
        grid.Children.Add(rule);
        Grid.SetColumn(count, 2);
        grid.Children.Add(count);
        return grid;
    }

    // One index row: [brass edge][caret][Nota][name] … [★][tag dots][type tag].
    private Control IndexRow(BrowserItem item)
    {
        bool child = item.Depth > 0;

        var edge = new Border { Classes = { "RowEdge" } };

        // Disclosure triangle for parents with children; a spacer otherwise, so leaf rows
        // and child rows keep their names aligned.
        Control disclosure;
        if (item.HasChildren)
        {
            var tri = new Path
            {
                Data = Geometry.Parse(item.IsExpanded ? IconExpanded : IconCollapsed),
                Stretch = Stretch.Uniform,
                Width = 8, Height = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            tri.BindResource(Shape.FillProperty, "Brush.TextTertiary");
            var hit = new Border
            {
                Width = 14, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                Child = tri,
            };
            hit.PointerPressed += (_, e) => { _vm?.ToggleExpand(item); e.Handled = true; };
            disclosure = hit;
        }
        else
        {
            disclosure = new Border { Width = 14 };
        }

        // "Nota" drops to tertiary ink so the eye lands on the distinctive word.
        var prefix = item.Prefix.Length > 0
            ? new TextBlock { Text = item.Prefix, Classes = { "RowPrefix" } }
            : null;
        var name = new TextBlock
        {
            Text = item.Prefix.Length > 0 ? item.ShortName : item.Name,
            Classes = { "RowName" },
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (child) name.Classes.Add("child");

        // The type, as a quiet tag on the right edge (toggleable in the ⋮ menu).
        string tag = RowTag(item);
        TextBlock? tagText = null;
        if (tag.Length > 0 && (_settings?.Current.BrowserShowTypeTags ?? true))
            tagText = new TextBlock { Text = tag, Classes = { "RowTag" }, TextTrimming = TextTrimming.CharacterEllipsis };

        var row = new IndexRowPanel
        {
            Height = RowH,
            Indent = 6 + item.Depth * 12,
            Edge = edge,
            Disclosure = disclosure,
            Prefix = prefix,
            Name2 = name,
            Markers = BuildRowMarkers(item),   // brass ★ + tag colour dots
            Tag2 = tagText,
        };

        // The row's full identity lives in the tooltip, where the tag has no room for it
        // (a plug-in's format, a sample's folder).
        ToolTip.SetTip(row, item.Sub.Length > 0 ? $"{item.Name} · {item.Sub}" : item.Name);
        return row;
    }

    /// <summary>Lays out one index row. A Grid cannot express the priority this row needs:
    /// the name is the thing being read, so it takes the space it wants first and the type
    /// tag lives on whatever is left — an Auto tag column would instead trim every long
    /// device name to keep "paraphonic synth" whole.</summary>
    private sealed class IndexRowPanel : Panel
    {
        private const double Gutter = 9;   // right margin, clear of the scrollbar
        private const double Gap = 5;

        public double Indent { get; init; }
        public Border Edge { get; init; } = null!;
        public Control Disclosure { get; init; } = null!;
        public TextBlock? Prefix { get; init; }
        public TextBlock Name2 { get; init; } = null!;
        public Control? Markers { get; init; }
        public TextBlock? Tag2 { get; init; }

        protected override Size MeasureOverride(Size availableSize)
        {
            Build();
            foreach (var c in Children) c.Measure(Size.Infinity);
            return new Size(0, Height);
        }

        private bool _built;
        private void Build()
        {
            if (_built) return;
            _built = true;
            Children.Add(Edge);
            Children.Add(Disclosure);
            if (Prefix is not null) Children.Add(Prefix);
            Children.Add(Name2);
            if (Markers is not null) Children.Add(Markers);
            if (Tag2 is not null) Children.Add(Tag2);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double h = finalSize.Height;
            void Place(Control c, double x, double w)
                => c.Arrange(new Rect(x, (h - c.DesiredSize.Height) / 2, w, c.DesiredSize.Height));

            Edge.Arrange(new Rect(0, 0, Edge.Width, h));
            Disclosure.Arrange(new Rect(Indent, 0, Disclosure.DesiredSize.Width, h));

            double left = Indent + Disclosure.DesiredSize.Width + 3;
            if (Prefix is not null)
            {
                Place(Prefix, left, Prefix.DesiredSize.Width);
                left += Prefix.DesiredSize.Width + Gap;
            }

            double right = Math.Max(left, finalSize.Width - Gutter);
            double markersW = Markers?.DesiredSize.Width ?? 0;
            double avail = Math.Max(0, right - left - (markersW > 0 ? markersW + 6 : 0));

            // The name first, then the tag on what survives — so a long name trims at the
            // row's edge instead of being squeezed by a secondary label.
            double nameW = Math.Min(Name2.DesiredSize.Width, avail);
            double tagW = 0;
            if (Tag2 is not null)
                tagW = Math.Min(Tag2.DesiredSize.Width, Math.Max(0, avail - nameW - 6));
            Place(Name2, left, nameW);

            double x = right;
            if (Tag2 is not null) { x -= tagW; Place(Tag2, x, tagW); if (tagW > 0) x -= 6; }
            if (Markers is not null) { x -= markersW; Place(Markers, x, markersW); }
            return finalSize;
        }
    }

    // The right-edge tag: what kind of thing the row is, in as few words as possible.
    // Nothing for rows whose indent already says it (presets under a device, folders).
    private static string RowTag(BrowserItem it) => it.Kind switch
    {
        BrowserItemKind.Folder => "",
        BrowserItemKind.Preset => it.Depth > 0 ? "" : it.Sub,
        BrowserItemKind.Sample => System.IO.Path.GetExtension(it.Path).TrimStart('.').ToLowerInvariant(),
        // A column of "project" down a tab that is only projects says nothing.
        BrowserItemKind.Project => "",
        _ => it.Sub,
    };

    // A small trailing strip of markers for a device row: a brass ★ when favorited, then up
    // to three tag-colour dots. Null for non-device rows (presets / samples / folders / projects).
    private Control? BuildRowMarkers(BrowserItem item)
    {
        if (_vm is null || item.LibraryKey.Length == 0) return null;
        bool fav = _vm.IsFavorite(item);
        var tags = _vm.TagsFor(item);
        if (!fav && tags.Count == 0) return null;

        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        if (fav)
        {
            var star = new Path
            {
                Data = Geometry.Parse(IconStar), Stretch = Stretch.Uniform, Width = 9, Height = 9,
                VerticalAlignment = VerticalAlignment.Center,
            };
            star.BindResource(Shape.FillProperty, "Brush.Accent");
            strip.Children.Add(star);
        }
        int shown = 0;
        foreach (var t in tags)
        {
            if (shown++ >= 3) break;
            strip.Children.Add(new Ellipse
            {
                Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center,
                Fill = BrushFromHex(t.Color),
            });
        }
        return strip;
    }

    private static IBrush BrushFromHex(string hex)
    {
        // A tag colour comes from the library, not the palette — Ink() re-tints it for the
        // active theme instead of painting a graphite-tuned hue onto paper.
        try { return NotaPalette.Ink(hex); }
        catch { return Brushes.Gray; }
    }

    // --- view options ---------------------------------------------------------

    private Border BuildOptionsButton()
    {
        var icon = GridIcon(IconOptions, 14, "Brush.TextSecondary", strokeWidth: 1.6);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var btn = new Border
        {
            Width = 26, Height = 26,
            Margin = new Thickness(6, 0, 0, 0),
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = icon,
        };
        btn.BindResource(Border.BackgroundProperty, "Brush.BgSunken");
        btn.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        ToolTip.SetTip(btn, "Sort & view options");
        btn.PointerPressed += (_, e) => { ShowOptionsMenu(btn); e.Handled = true; };
        return btn;
    }

    private void ShowOptionsMenu(Control anchor)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(CheckItem("Show type tags", _settings?.Current.BrowserShowTypeTags ?? true, v =>
        {
            if (_settings is null) return;
            _settings.Current.BrowserShowTypeTags = v;
            _settings.Save();
            RefreshRows();
        }));
        flyout.Items.Add(CheckItem("Group by source", _vm?.GroupBySource ?? true, v =>
        {
            if (_vm is not null) _vm.GroupBySource = v;
            if (_settings is not null) { _settings.Current.BrowserGroupBySource = v; _settings.Save(); }
            RefreshCounts();
        }));
        flyout.Items.Add(CheckItem("Favorites first", _vm?.FavoritesFirst ?? true, v =>
        {
            if (_vm is not null) _vm.FavoritesFirst = v;
            if (_settings is not null) { _settings.Current.BrowserFavoritesFirst = v; _settings.Save(); }
            RefreshCounts();
        }));
        flyout.Items.Add(new Separator());
        var editTags = new MenuItem { Header = "Edit tags…" };
        editTags.Click += (_, _) => EditTagsRequested?.Invoke(null);
        flyout.Items.Add(editTags);
        flyout.ShowAt(anchor);
    }

    // A checkable row, drawn the same way the tag menu marks assignment (a leading ✓ or
    // its width in blanks) so the two menus read alike.
    private static MenuItem CheckItem(string label, bool on, Action<bool> set)
    {
        var mi = new MenuItem { Header = (on ? "✓  " : "     ") + label };
        mi.Click += (_, _) => set(!on);
        return mi;
    }

    // --- preview footer --------------------------------------------------------

    private Border BuildPreviewFooter(out Button playBtn, out Path playIcon, out TextBlock name,
                                      out TextBlock sub, out ToggleButton auto, out PreviewWaveform wave)
    {
        // Row 1: filename + format info (mono).
        name = new TextBlock { Text = "—", FontSize = 10, VerticalAlignment = VerticalAlignment.Bottom };
        name.BindResource(TextBlock.ForegroundProperty, "Brush.TextPrimary");
        sub = new TextBlock { Text = "", FontSize = 9, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 0) };
        sub.Classes.Add("Mono");
        sub.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        var infoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Children = { name, sub } };

        // Row 2: solid brass play button + waveform strip.
        playIcon = new Path { Data = Geometry.Parse(IconPlay), Stretch = Stretch.Uniform, Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        playIcon.BindResource(Shape.FillProperty, "Brush.TextOnAccent");
        playBtn = new Button { Content = playIcon, Classes = { "primary" }, Width = 26, Height = 26, Padding = new Thickness(0), IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
        var pb = playBtn;
        pb.Click += (_, _) => { if (_footerItem is { } it && it.Kind == BrowserItemKind.Sample) PreviewRequested?.Invoke(it); };
        wave = new PreviewWaveform { Height = 30, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var playRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(pb, 0);
        Grid.SetColumn(wave, 1);
        playRow.Children.Add(pb);
        playRow.Children.Add(wave);

        // Row 3: Auto-audition chip + preview volume bar (decorative — not wired).
        auto = new ToggleButton { Content = "Auto-audition", FontSize = 9, Padding = new Thickness(6, 0), Height = 18, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(auto, "Auto-audition the selected sample on selection");
        var volFillOuter = new Border { Width = 50, Height = 3, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
        volFillOuter.BindResource(Border.BackgroundProperty, "Brush.BgSunken");
        var volFill = new Border { Width = 32, Height = 3, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        volFill.BindResource(Border.BackgroundProperty, "Brush.BorderStrong");
        volFillOuter.Child = volFill;
        ToolTip.SetTip(volFillOuter, "Preview volume — not wired yet");
        var row3 = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Children = { auto, Caption("vol"), volFillOuter },
        };

        var footer = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 8),
            Child = new StackPanel { Children = { infoRow, playRow, row3 } },
        };
        footer.BindResource(Border.BackgroundProperty, "Brush.BgApp");
        footer.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        return footer;
    }

    private void UpdateFooter(BrowserItem? item)
    {
        _footerItem = item;
        bool isSample = item is { Kind: BrowserItemKind.Sample };
        _previewName.Text = item?.Name ?? "—";
        _previewSub.Text = isSample && !string.IsNullOrEmpty(item!.Sub) ? "· " + item.Sub : "";
        _previewBtn.IsEnabled = isSample;
        _previewWave.SetSample(isSample ? item!.Name : null);
        if (isSample && (_autoAudition.IsChecked ?? false)) PreviewRequested?.Invoke(item!);
    }

    private TextBlock Caption(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
        t.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        return t;
    }

    private ListBox NewList(IDataTemplate itemTemplate) => new()
    {
        Classes = { "browser" },
        Padding = new Thickness(0, 0, 0, 6),
        ItemTemplate = itemTemplate,
    };

    private void HookActivation(ListBox list)
    {
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is BrowserItem { IsGroup: false } item) ItemActivated?.Invoke(item);
        };
        list.SelectionChanged += (_, _) =>
        {
            // Section headers are chrome: never let one become the selection.
            if (list.SelectedItem is BrowserItem { IsGroup: true }) { list.SelectedItem = null; return; }
            if (_active >= 0 && list == _pages[_active]) UpdateFooter(list.SelectedItem as BrowserItem);
        };
        // Drag source (M7-5): start a copy-drag when the pointer leaves the row.
        // Must use handledEventsToo — ListBoxItem marks PointerPressed as Handled for
        // selection, so a plain `+=` (handled=false) never fires and the drag never
        // starts (no drag cursor). AddHandler with handledEventsToo:true still runs.
        list.AddHandler(InputElement.PointerPressedEvent, OnRowPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerMovedEvent, OnRowMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerReleasedEvent,
            (object? _, PointerReleasedEventArgs _) => { _pressedItem = null; _pressArgs = null; },
            RoutingStrategies.Bubble, handledEventsToo: true);
        // Right-click / context menu: favorites + tags on device rows, reveal on files,
        // open / reveal / delete on projects.
        list.ContextRequested += (_, e) => ShowContextMenu(list, e);
    }

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedItem = null;
        _pressArgs = null;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (InButton(e.Source as StyledElement)) return;          // preview button etc.
        var item = (e.Source as StyledElement)?.DataContext as BrowserItem;
        // Projects, folders and section headers aren't draggable (folders are navigation,
        // not payload; headers are chrome).
        if (item is null || item.IsGroup || item.Kind is BrowserItemKind.Project or BrowserItemKind.Folder) return;
        _pressedItem = item;
        _pressPoint = e.GetPosition(this);
        _pressArgs = e;   // DoDragDropAsync needs the PointerPressed args
    }

    // Once the pointer moves past the threshold, start the drag with the stored
    // press args (DoDragDropAsync requires PointerPressedEventArgs).
    private async void OnRowMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedItem is null || _pressArgs is null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _pressedItem = null; _pressArgs = null; return; }
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _pressPoint.X) < 4 && Math.Abs(p.Y - _pressPoint.Y) < 4) return;

        var item = _pressedItem;
        var args = _pressArgs;
        _pressedItem = null; _pressArgs = null; // start the drag once

        // Single native pasteboard item (one format → one drag image → one
        // pasteboard item: macOS is satisfied). The payload rides CurrentDrag.
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(DataFormat.Text, DragSentinel));
        CurrentDrag = item;
        try { await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Copy); }
        finally { CurrentDrag = null; }
    }

    private static bool InButton(StyledElement? el)
    {
        for (var c = el; c is not null; c = c.Parent)
            if (c is Button) return true;
        return false;
    }

    // --- header filter chips -------------------------------------------------

    private void RebuildChips()
    {
        _chipsPanel.ClearChips();   // the "+N" is a permanent child, not one of the chips
        if (_vm is null) return;
        _chipsPanel.Children.Add(MakeChip("★ Favorites", null,
            _vm.FilterMode == BrowserFilter.Favorites, () => ToggleFilter(BrowserFilter.Favorites, "")));
        foreach (var t in _vm.Tags)
        {
            var tag = t;
            bool active = _vm.FilterMode == BrowserFilter.Tag && _vm.FilterTagId == tag.Id;
            _chipsPanel.Children.Add(MakeChip(tag.Title, tag.Color, active, () => ToggleFilter(BrowserFilter.Tag, tag.Id)));
        }
        _chipsPanel.InvalidateMeasure();
    }

    // The chips that didn't fit on the line, offered as a menu off the "+N".
    private void ShowOverflowChips()
    {
        if (_vm is null) return;
        var flyout = new MenuFlyout();
        foreach (var item in _chipsPanel.Hidden)
        {
            var chip = item;
            var mi = new MenuItem { Header = (chip.Active ? "✓  " : "     ") + chip.Label };
            if (chip.ColorHex is not null)
                mi.Icon = new Ellipse { Width = 10, Height = 10, Fill = BrushFromHex(chip.ColorHex) };
            mi.Click += (_, _) => chip.OnClick();
            flyout.Items.Add(mi);
        }
        if (flyout.Items.Count > 0) flyout.ShowAt(_overflowChip);
    }

    private void ToggleFilter(BrowserFilter mode, string tagId)
    {
        if (_vm is null) return;
        bool same = _vm.FilterMode == mode && (mode != BrowserFilter.Tag || _vm.FilterTagId == tagId);
        _vm.SetLibraryFilter(same ? BrowserFilter.None : mode, tagId);
        RebuildChips();
        RefreshCounts();
    }

    private ChipBorder MakeChip(string text, string? colorHex, bool active, Action onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        if (colorHex is not null)
            content.Children.Add(new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Fill = BrushFromHex(colorHex) });
        var tb = new TextBlock { Text = text, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        tb.BindResource(TextBlock.ForegroundProperty, active ? "Brush.TextOnAccent" : "Brush.TextSecondary");
        content.Children.Add(tb);

        var chip = new ChipBorder
        {
            Label = text, ColorHex = colorHex, Active = active, OnClick = onClick,
            Height = 20, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 0),
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand), Child = content,
        };
        chip.BindResource(Border.BackgroundProperty, active ? "Brush.Accent" : "Brush.BgSunken");
        chip.BindResource(Border.BorderBrushProperty, active ? "Brush.Accent" : "Brush.BorderDefault");
        chip.PointerPressed += (_, e) => { onClick(); e.Handled = true; };
        return chip;
    }

    /// <summary>A filter chip that remembers what it stands for, so the overflow menu can
    /// offer the same choice the chip would have.</summary>
    private sealed class ChipBorder : Border
    {
        public string Label { get; init; } = "";
        public string? ColorHex { get; init; }
        public bool Active { get; init; }
        public Action OnClick { get; init; } = () => { };
    }

    /// <summary>One line of chips: lays them out left to right and collapses whatever runs
    /// off the end into a trailing "+N" (which opens the rest as a menu). A WrapPanel would
    /// grow the header instead, pushing the list down as tags are added.</summary>
    private sealed class ChipStrip : Panel
    {
        private const double Gap = 5;
        private readonly Border _overflow;
        private readonly TextBlock _overflowText;
        private readonly List<ChipBorder> _hidden = new();

        public ChipStrip(Border overflow, TextBlock overflowText)
        {
            ClipToBounds = true;
            _overflow = overflow;
            _overflowText = overflowText;
            Children.Add(overflow);
        }

        /// <summary>The chips that didn't fit, in order.</summary>
        public IReadOnlyList<ChipBorder> Hidden => _hidden;

        /// <summary>Drop every chip, keeping the overflow affordance.</summary>
        public void ClearChips()
        {
            for (int i = Children.Count - 1; i >= 0; i--)
                if (Children[i] is ChipBorder) Children.RemoveAt(i);
            _hidden.Clear();
        }

        private IEnumerable<ChipBorder> Chips
        {
            get { foreach (var c in Children) if (c is ChipBorder cb) yield return cb; }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var c in Children) c.Measure(Size.Infinity);
            return new Size(0, 20);   // one chip row, whatever the width
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _hidden.Clear();
            var chips = new List<ChipBorder>(Chips);

            // Park anything that doesn't fit outside the panel; ClipToBounds swallows it.
            // Toggling IsVisible from inside Arrange would re-enter layout and leave a chip
            // painted where it was last arranged.
            void Park(Control c) => c.Arrange(new Rect(finalSize.Width, 0, 0, 0));

            double total = 0;
            foreach (var c in chips) total += c.DesiredSize.Width + Gap;
            if (total - Gap <= finalSize.Width)
            {
                double x0 = 0;
                foreach (var c in chips)
                {
                    c.Arrange(new Rect(x0, 0, c.DesiredSize.Width, 20));
                    x0 += c.DesiredSize.Width + Gap;
                }
                Park(_overflow);
                return finalSize;
            }

            // Reserve the "+N" width up front so the last chip never sits under it. Its
            // widest form is the one where everything is hidden, so measure that.
            _overflowText.Text = "+" + chips.Count;
            _overflow.Measure(Size.Infinity);
            double reserve = _overflow.DesiredSize.Width + Gap;
            double x = 0;
            foreach (var c in chips)
            {
                double w = c.DesiredSize.Width;
                if (_hidden.Count == 0 && x + w + reserve <= finalSize.Width)
                {
                    c.Arrange(new Rect(x, 0, w, 20));
                    x += w + Gap;
                }
                else
                {
                    Park(c);          // once one chip is out, the rest follow it — order stays
                    _hidden.Add(c);
                }
            }
            _overflowText.Text = "+" + _hidden.Count;
            _overflow.Measure(Size.Infinity);
            _overflow.Arrange(new Rect(x, 0, _overflow.DesiredSize.Width, 20));
            return finalSize;
        }
    }

    // --- context menus -------------------------------------------------------

    private void ShowContextMenu(Control owner, ContextRequestedEventArgs e)
    {
        if (_vm is null) return;
        var item = (e.Source as StyledElement)?.DataContext as BrowserItem
                   ?? (owner as ListBox)?.SelectedItem as BrowserItem;
        if (item is null || item.IsGroup) return;
        var flyout = BuildContextMenu(item);
        if (flyout is null) return;
        flyout.ShowAt(owner, showAtPointer: true);
        e.Handled = true;
    }

    private MenuFlyout? BuildContextMenu(BrowserItem item)
    {
        var flyout = new MenuFlyout();

        // Device rows (Instr / FX / MIDI): favorites + tag assignment.
        if (item.LibraryKey.Length > 0 && _vm is not null)
        {
            bool fav = _vm.IsFavorite(item);
            var favItem = new MenuItem { Header = fav ? "Remove from Favorites" : "Add to Favorites" };
            favItem.Click += (_, _) => _vm.ToggleFavorite(item);
            flyout.Items.Add(favItem);

            var tagsMenu = new MenuItem { Header = "Tags" };
            foreach (var t in _vm.Tags)
            {
                var tag = t;
                bool assigned = _vm.IsTagAssigned(item, tag.Id);
                var mi = new MenuItem
                {
                    Header = (assigned ? "✓  " : "     ") + tag.Title,
                    Icon = new Ellipse { Width = 10, Height = 10, Fill = BrushFromHex(tag.Color) },
                };
                mi.Click += (_, _) => _vm.AssignTag(item, tag.Id, !assigned);
                tagsMenu.Items.Add(mi);
            }
            if (_vm.Tags.Count > 0) tagsMenu.Items.Add(new Separator());
            var newTag = new MenuItem { Header = "New tag…" };
            newTag.Click += (_, _) => EditTagsRequested?.Invoke(item);   // create + assign to this device
            var editTags = new MenuItem { Header = "Edit tags…" };
            editTags.Click += (_, _) => EditTagsRequested?.Invoke(null); // manage all
            tagsMenu.Items.Add(newTag);
            tagsMenu.Items.Add(editTags);
            flyout.Items.Add(tagsMenu);
            return flyout;
        }

        switch (item.Kind)
        {
            case BrowserItemKind.Sample:
            case BrowserItemKind.Preset when item.Path.Length > 0 && !item.Path.StartsWith("factory:", StringComparison.Ordinal):   // saved preset file on disk
            {
                var reveal = new MenuItem { Header = "Reveal in Finder" };
                reveal.Click += (_, _) => RevealRequested?.Invoke(item);
                flyout.Items.Add(reveal);
                return flyout;
            }
            case BrowserItemKind.Folder when _active == FilesTab:   // real sample folders (preset folders are synthetic)
            {
                var reveal = new MenuItem { Header = "Reveal in Finder" };
                reveal.Click += (_, _) => RevealRequested?.Invoke(item);
                flyout.Items.Add(reveal);
                return flyout;
            }
            case BrowserItemKind.Project:
            {
                var open = new MenuItem { Header = "Open" };
                open.Click += (_, _) => ItemActivated?.Invoke(item);
                var reveal = new MenuItem { Header = "Reveal in Finder" };
                reveal.Click += (_, _) => RevealRequested?.Invoke(item);
                var del = new MenuItem { Header = "Delete…" };
                del.Click += (_, _) => DeleteProjectRequested?.Invoke(item);
                flyout.Items.Add(open);
                flyout.Items.Add(reveal);
                flyout.Items.Add(new Separator());
                flyout.Items.Add(del);
                return flyout;
            }
            default:
                return null;   // preset children etc. — no menu
        }
    }
}
