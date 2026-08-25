// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// M4.1-C / M7-4 + Phase 3 (HANDOFF 1g): left browser panel. A 46px icon rail
// (Instruments / FX / Samples / Presets / Projects) swaps templated lists; a
// search box filters the active list live; a pinned preview footer auditions the
// selected sample. Double-clicking an item raises ItemActivated; the footer ▶ (or
// Auto-audition on selection) raises PreviewRequested. Brushes bind via
// GetResourceObservable (the control is built during MainWindow's XAML load,
// before it is attached). Rows are drag sources (M7-5).

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
    private const string DragSentinel = "nota-browser-item";
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
    // Rail + row icons — Lucide-style stroke glyphs. Each maps to a browser context; no two
    // share a glyph. Reused by RailTab (tabs) and IconFor (list rows). Every glyph's bounding
    // box is a square [4,20]²: Path.Stretch=Uniform fills the box exactly, so the icon reads
    // centred (a non-square box would be top-left aligned by the stretch and drift up/left).
    // Instruments — a piano keyboard: case with key dividers hanging from the top.
    private const string IconKeyboard = "M4 4 H20 V20 H4 Z M8 4 V13 M12 4 V13 M16 4 V13";
    // Audio effects — vertical faders with offset handles (a mixer/EQ strip).
    private const string IconSliders = "M6 4 V20 M12 4 V20 M18 4 V20 M4 9 H8 M10 14 H14 M16 8 H20";
    // Files — a folder with a raised tab on the left.
    private const string IconFolder = "M4 4 H9 L11 6 H20 V20 H4 Z";
    private const string IconStar = "M12 3 L14.5 9 L21 9.3 L16 13.5 L17.7 20 L12 16.2 L6.3 20 L8 13.5 L3 9.3 L9.5 9 Z";
    // Presets — a bookmark (a saved snapshot). Distinct from the fader glyphs above.
    private const string IconBookmark = "M4 4 H20 V20 L12 13 L4 20 Z";
    // Projects — a document with a dog-ear and two content lines.
    private const string IconDoc = "M4 4 H14 L20 10 V20 H4 Z M14 4 V10 H20 M8 13 H16 M8 16 H16";
    private const string IconPlay = "M7 4 L19 12 L7 20 Z";
    // MIDI effects — a clean eighth note (head bottom-left, tall stem, flag to the top-right).
    private const string IconMidi = "M4 17 A3 3 0 0 1 10 17 A3 3 0 0 1 4 17 M10 17 V4 L20 7";
    // MIDI-learn map — two nodes joined by a link (a control mapped to a parameter).
    private const string IconMap = "M4 17.5 A2.5 2.5 0 0 1 9 17.5 A2.5 2.5 0 0 1 4 17.5 M15 6.5 A2.5 2.5 0 0 1 20 6.5 A2.5 2.5 0 0 1 15 6.5 M8.7 15.3 L15.3 8.7";

    private const int TabCount = 7;   // Instr / FX / MIDI / Files / Preset / Proj / Map
    private const int MapTab = 6;     // MIDI-learn mappings — hosts a MidiMapView, not a list
    private readonly ListBox[] _pages;
    private readonly Border[] _tabs;
    private readonly ObservableCollection<BrowserItem>[] _filtered;
    private readonly IReadOnlyList<BrowserItem>?[] _sources = new IReadOnlyList<BrowserItem>?[TabCount];
    private readonly ContentControl _content = new();
    private int _active = -1;
    private BrowserViewModel? _vm;
    private MidiMapView? _midiMap;

    // Disclosure triangles for tree parent rows.
    private const string IconCollapsed = "M1 0 L6 4.5 L1 9 Z";  // ▸
    private const string IconExpanded = "M0 1 L9 1 L4.5 7 Z";   // ▾

    private readonly TextBox _search;
    private readonly TextBlock _matchCount;
    private readonly Border _searchWrap;

    // Centered empty-state message shown over a tab whose list is empty.
    private readonly TextBlock _emptyText = new();
    private readonly Border _emptyWrap;

    // Pinned preview footer.
    private readonly Button _previewBtn;
    private readonly Path _previewIcon;
    private readonly TextBlock _previewName;
    private readonly TextBlock _previewSub;
    private readonly ToggleButton _autoAudition;
    private readonly PreviewWaveform _previewWave;
    private readonly Border _previewFooter;   // sample auditioner — Files tab only
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
    private StackPanel _chipsHost = null!;
    private WrapPanel _chipsPanel = null!;

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
            RailTab(0, "Instr", IconKeyboard),
            RailTab(1, "FX", IconSliders),
            RailTab(2, "MIDI", IconMidi),
            RailTab(3, "Files", IconFolder),
            RailTab(4, "Preset", IconBookmark),
            RailTab(5, "Proj", IconDoc),
            RailTab(6, "Map", IconMap),
        };
        var rail = new StackPanel { Width = 46 };
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
            Margin = new Thickness(8, 8, 8, 4),
            Child = searchGrid,
        };
        _searchWrap.BindResource(Border.BackgroundProperty, "Brush.BgSunken");
        _searchWrap.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");

        // Filter chips (★ Favorites + tags). Populated from the VM in SetViewModel and
        // shown only on the device tabs (Instr / FX / MIDI). Wraps so many tags still fit.
        _chipsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        _chipsHost = new StackPanel { Margin = new Thickness(10, 0, 8, 6), Children = { _chipsPanel } };

        // Plugin rescan lives in Settings ▸ Plug-ins now (not the browser).
        _previewFooter = BuildPreviewFooter(out _previewBtn, out _previewIcon, out _previewName,
                                            out _previewSub, out _autoAudition, out _previewWave);

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
        var topStack = new StackPanel { Children = { _searchWrap, _chipsHost } };
        DockPanel.SetDock(topStack, Dock.Top);
        DockPanel.SetDock(_previewFooter, Dock.Bottom);
        right.Children.Add(topStack);
        right.Children.Add(_previewFooter);
        right.Children.Add(contentHost);

        var railDivider = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
        railDivider.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        railDivider.Child = rail;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(railDivider, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(railDivider);
        grid.Children.Add(right);

        var outer = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Child = grid };
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

    public void SetViewModel(BrowserViewModel vm)
    {
        _vm = vm;
        // Tree tabs (Instruments / FX / MIDI / Files) bind straight to the VM's flattened
        // visible collections; the VM owns their filtering + expand/collapse.
        _pages[0].ItemsSource = vm.Instruments;
        _pages[1].ItemsSource = vm.Effects;
        _pages[2].ItemsSource = vm.MidiEffects;
        _pages[3].ItemsSource = vm.Samples;
        _pages[4].ItemsSource = vm.Presets;
        vm.Instruments.CollectionChanged += (_, _) => { if (_active == 0) { _matchCount.Text = vm.Instruments.Count.ToString(); UpdateEmptyState(); } };
        vm.Effects.CollectionChanged += (_, _) => { if (_active == 1) { _matchCount.Text = vm.Effects.Count.ToString(); UpdateEmptyState(); } };
        vm.MidiEffects.CollectionChanged += (_, _) => { if (_active == 2) { _matchCount.Text = vm.MidiEffects.Count.ToString(); UpdateEmptyState(); } };
        vm.Samples.CollectionChanged += (_, _) => { if (_active == 3) { _matchCount.Text = vm.Samples.Count.ToString(); UpdateEmptyState(); } };
        vm.Presets.CollectionChanged += (_, _) => { if (_active == 4) { _matchCount.Text = vm.Presets.Count.ToString(); UpdateEmptyState(); } };

        // Flat tabs (Projects) keep the view-side substring filter.
        var cols = new ObservableCollection<BrowserItem>?[] { null, null, null, null, null, vm.Projects };
        for (int i = 5; i < cols.Length; i++)
        {
            int idx = i;
            _sources[idx] = cols[idx];
            cols[idx]!.CollectionChanged += (_, _) => ApplyFilter(idx);
            ApplyFilter(idx);
        }
        ApplyFilter(0);
        ApplyFilter(1);
        ApplyFilter(2);
        ApplyFilter(3);
        ApplyFilter(4);

        // Rebuild the header chips whenever tags/favorites change (also refreshes active state).
        vm.LibraryChanged += RebuildChips;
        RebuildChips();
    }

    private void ApplyFilter(int index)
    {
        if (index < 0 || index >= _filtered.Length) return;
        // Tree tabs (devices + Files + Presets): the VM re-filters + flattens into its collections.
        if (index is 0 or 1 or 2 or 3 or 4)
        {
            _vm?.FilterTree(index, _search.Text?.Trim() ?? "");
            if (index == _active) { _matchCount.Text = VisibleCount(index).ToString(); UpdateEmptyState(); }
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
        if (index == _active) { _matchCount.Text = dst.Count.ToString(); UpdateEmptyState(); }
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

    private int VisibleCount(int index) => index switch
    {
        0 => _vm?.Instruments.Count ?? 0,
        1 => _vm?.Effects.Count ?? 0,
        2 => _vm?.MidiEffects.Count ?? 0,
        3 => _vm?.Samples.Count ?? 0,
        4 => _vm?.Presets.Count ?? 0,
        _ => _filtered[index].Count,
    };

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
        // The sample auditioner only makes sense for the Files tab (index 3);
        // hide it for Instruments / FX / MIDI / Presets / Projects / Map.
        _previewFooter.IsVisible = index == 3;
        // Favorite/tag filter chips apply only to the device tabs.
        _chipsHost.IsVisible = index is 0 or 1 or 2;
        if (isMap) { _matchCount.Text = ""; _emptyWrap.IsVisible = false; return; }
        _matchCount.Text = VisibleCount(index).ToString();
        UpdateEmptyState();
    }

    private Border RailTab(int index, string caption, string iconData)
    {
        // Icon-only rail (HANDOFF 1g); the caption survives as a tooltip.
        // Mockup: 16px icons in a 36×36 tab, thin 1.8/24 stroke.
        var icon = new Path
        {
            Data = Geometry.Parse(iconData),
            StrokeThickness = 1.4,
            Stretch = Stretch.Uniform,
            Width = 16, Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var tab = new Border { Classes = { "RailTab" }, Child = icon };
        ToolTip.SetTip(tab, caption);
        tab.PointerPressed += (_, _) => SelectTab(index);
        return tab;
    }

    private IDataTemplate BuildItemTemplate() => new FuncDataTemplate<BrowserItem>((item, _) =>
    {
        if (item is null) return new Control();
        bool child = item.Depth > 0;

        // Column 0: disclosure triangle for parents with presets; a spacer otherwise
        // (so leaf rows and preset rows keep their icons aligned).
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
                VerticalAlignment = VerticalAlignment.Stretch, Child = tri,
            };
            hit.PointerPressed += (_, e) => { _vm?.ToggleExpand(item); e.Handled = true; };
            disclosure = hit;
        }
        else
        {
            disclosure = new Border { Width = 14 };
        }

        var icon = new Path
        {
            Data = Geometry.Parse(IconFor(item.Kind)),
            StrokeThickness = 1.5,
            Stretch = Stretch.Uniform,
            Width = child ? 13 : 15, Height = child ? 13 : 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
        };
        // Nota's own devices carry the brass brand accent so they stand out from external
        // plugins (which stay neutral grey); factory-preset children inherit the accent too.
        bool builtin = item.Kind is BrowserItemKind.BuiltinInstrument
            or BrowserItemKind.BuiltinEffect or BrowserItemKind.BuiltinMidiEffect;
        icon.BindResource(Shape.StrokeProperty, child || builtin ? "Brush.Accent" : "Brush.TextSecondary");

        var name = new TextBlock { Text = item.Name, FontSize = 11, FontWeight = child ? FontWeight.Normal : FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        name.BindResource(TextBlock.ForegroundProperty, child ? "Brush.TextSecondary" : "Brush.TextPrimary");
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Children = { name } };
        if (!child && !string.IsNullOrEmpty(item.Sub))
        {
            var sub = new TextBlock { Text = item.Sub, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
            sub.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
            texts.Children.Add(sub);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(item.Depth * 16, 0, 0, 0) };
        Grid.SetColumn(disclosure, 0);
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(texts, 2);
        grid.Children.Add(disclosure);
        grid.Children.Add(icon);
        grid.Children.Add(texts);

        // Trailing favorite ★ + tag colour dots for device rows (Instr / FX / MIDI).
        var markers = BuildRowMarkers(item);
        if (markers is not null)
        {
            Grid.SetColumn(markers, 3);
            grid.Children.Add(markers);
        }
        return grid;
    }, supportsRecycling: false);

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
            Margin = new Thickness(6, 0, 4, 0),
        };
        if (fav)
        {
            var star = new Path
            {
                Data = Geometry.Parse(IconStar), Stretch = Stretch.Uniform, Width = 10, Height = 10,
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
                Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center,
                Fill = BrushFromHex(t.Color),
            });
        }
        return strip;
    }

    private static IBrush BrushFromHex(string hex)
    {
        try { return new SolidColorBrush(Color.Parse(hex)); }
        catch { return Brushes.Gray; }
    }

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
            Background = new SolidColorBrush(Color.Parse("#1B1916")),
            Child = new StackPanel { Children = { infoRow, playRow, row3 } },
        };
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

    private static string IconFor(BrowserItemKind kind) => kind switch
    {
        BrowserItemKind.BuiltinInstrument or BrowserItemKind.PluginInstrument => IconKeyboard,
        BrowserItemKind.BuiltinEffect or BrowserItemKind.PluginEffect => IconSliders,
        BrowserItemKind.BuiltinMidiEffect => IconMidi,
        BrowserItemKind.Preset => IconBookmark,
        BrowserItemKind.Project => IconDoc,
        _ => IconFolder,
    };

    private TextBlock Caption(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
        t.BindResource(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        return t;
    }

    private ListBox NewList(IDataTemplate itemTemplate) => new()
    {
        Background = null,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(4),
        ItemTemplate = itemTemplate,
    };

    private void HookActivation(ListBox list)
    {
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is BrowserItem item) ItemActivated?.Invoke(item);
        };
        list.SelectionChanged += (_, _) =>
        {
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
        // Projects and folders aren't draggable (folders are navigation, not payload).
        if (item is null || item.Kind is BrowserItemKind.Project or BrowserItemKind.Folder) return;
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
        _chipsPanel.Children.Clear();
        if (_vm is null) return;
        _chipsPanel.Children.Add(MakeChip("★ Favorites", null,
            _vm.FilterMode == BrowserFilter.Favorites, () => ToggleFilter(BrowserFilter.Favorites, "")));
        foreach (var t in _vm.Tags)
        {
            var tag = t;
            bool active = _vm.FilterMode == BrowserFilter.Tag && _vm.FilterTagId == tag.Id;
            _chipsPanel.Children.Add(MakeChip(tag.Title, tag.Color, active, () => ToggleFilter(BrowserFilter.Tag, tag.Id)));
        }
        // Trailing affordance to manage the tag list.
        _chipsPanel.Children.Add(MakeChip("Tags…", null, false, () => EditTagsRequested?.Invoke(null)));
    }

    private void ToggleFilter(BrowserFilter mode, string tagId)
    {
        if (_vm is null) return;
        bool same = _vm.FilterMode == mode && (mode != BrowserFilter.Tag || _vm.FilterTagId == tagId);
        _vm.SetLibraryFilter(same ? BrowserFilter.None : mode, tagId);
        RebuildChips();
        if (_active is 0 or 1 or 2) _matchCount.Text = VisibleCount(_active).ToString();
    }

    private Border MakeChip(string text, string? colorHex, bool active, Action onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        if (colorHex is not null)
            content.Children.Add(new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Fill = BrushFromHex(colorHex) });
        var tb = new TextBlock { Text = text, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        tb.BindResource(TextBlock.ForegroundProperty, active ? "Brush.TextOnAccent" : "Brush.TextSecondary");
        content.Children.Add(tb);

        var chip = new Border
        {
            Height = 20, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 0),
            Margin = new Thickness(0, 0, 5, 5), BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand), Child = content,
        };
        chip.BindResource(Border.BackgroundProperty, active ? "Brush.Accent" : "Brush.BgSunken");
        chip.BindResource(Border.BorderBrushProperty, active ? "Brush.Accent" : "Brush.BorderDefault");
        chip.PointerPressed += (_, e) => { onClick(); e.Handled = true; };
        return chip;
    }

    // --- context menus -------------------------------------------------------

    private void ShowContextMenu(Control owner, ContextRequestedEventArgs e)
    {
        if (_vm is null) return;
        var item = (e.Source as StyledElement)?.DataContext as BrowserItem
                   ?? (owner as ListBox)?.SelectedItem as BrowserItem;
        if (item is null) return;
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
            case BrowserItemKind.Folder when _active == 3:   // real sample folders (preset folders are synthetic)
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
