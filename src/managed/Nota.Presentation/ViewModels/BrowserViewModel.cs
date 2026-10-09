// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M4.1-C: left browser. Aggregates built-in instruments/effects, the scanned
// plugin catalog (AU/VST3), and (M7-4) real samples, projects and presets from
// configurable folders. Double-clicking an item in the view adds/opens/applies
// it (handled by MainWindow).

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Nota.Application;
using Nota.Application.Samples;

namespace Nota.Presentation;

public enum BrowserItemKind
{
    BuiltinInstrument, BuiltinEffect, BuiltinMidiEffect, PluginInstrument, PluginEffect, Sample, Project, Preset, Folder,
    /// <summary>A section header row (BUILT-IN / PLUG-INS) inside a flattened list — never
    /// selectable, draggable or activatable.</summary>
    Group,
    /// <summary>A Standard MIDI File (.mid) — drops as MIDI clip(s).</summary>
    MidiFile,
}

/// <summary>Active library filter for the device tabs (Instr / FX / MIDI).</summary>
public enum BrowserFilter { None, Favorites, Tag }

public sealed class BrowserItem : INotifyPropertyChanged
{
    private SampleInfo? _sample;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What the sample index knows about a Files row (tempo, key, loop or one-shot);
    /// null until it has been analysed. Fills in live as the background scan runs.</summary>
    public SampleInfo? Sample
    {
        get => _sample;
        set
        {
            if (ReferenceEquals(_sample, value)) return;
            _sample = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sample)));
        }
    }

    public required string Name { get; init; }
    public required BrowserItemKind Kind { get; init; }
    public int CatalogIndex { get; init; } = -1; // plugins
    public int BuiltinKind { get; init; } = -1;   // effects: 0=EQ,1=Compressor,2=Reverb,3=Delay,4=Utility · instruments: 0=Synth,2=Physical,5=Aurora
    public string Sub { get; init; } = "";        // type / manufacturer / folder
    public string Format { get; init; } = "";     // plug-ins: "VST3" / "AU" (Sub carries the vendor)
    public string Vendor { get; init; } = "";     // plug-ins: the manufacturer
    public string Category { get; init; } = "";   // plug-ins: VST3 sub-categories ("Fx|Reverb") or the AU type
    public string Path { get; init; } = "";       // file/bundle path for samples/projects/presets
    /// <summary>Long-form description for the row's tooltip, where <see cref="Sub"/> (the
    /// narrow tag column) has no room for it. Falls back to Sub when empty.</summary>
    public string Tip { get; init; } = "";

    /// <summary>Section-header row (see <see cref="BrowserItemKind.Group"/>): Name is the
    /// caption and <see cref="GroupCount"/> how many rows it heads.</summary>
    public bool IsGroup => Kind == BrowserItemKind.Group;
    public int GroupCount { get; init; }

    /// <summary>The brand prefix a built-in device name carries ("Nota"), or "" — the browser
    /// prints it in tertiary ink so names scan on their distinctive word.</summary>
    public string Prefix => Name.StartsWith("Nota ", StringComparison.Ordinal) ? "Nota" : "";
    /// <summary>The name without its brand prefix.</summary>
    public string ShortName => Prefix.Length > 0 ? Name[5..] : Name;
    /// <summary>True for a scanned AU/VST3 row (as opposed to one of Nota's own devices).</summary>
    public bool IsPlugin => Kind is BrowserItemKind.PluginInstrument or BrowserItemKind.PluginEffect;

    // --- tree (Instruments / FX / MIDI): a built-in device parent with factory-preset children,
    // filed into category folders (Pads, Bass, Vocals …) with any unfiled presets loose on top ---
    public int Depth { get; init; }                            // 0 = device/plugin, 1 = folder or preset, 2 = preset in a folder
    public List<BrowserItem> Children { get; } = new();        // presets and category folders under this device
    public bool HasChildren => Children.Count > 0;
    public bool IsExpanded { get; set; }                       // toggled by the view-model
    /// <summary>The Files tab's Downloaded folder (installed sample packs): its own icon, listed first.</summary>
    public bool IsDownloads { get; init; }

    public string Display => string.IsNullOrEmpty(Sub) ? Name : $"{Name}  ·  {Sub}";
    public override string ToString() => Display;

    /// <summary>Stable identity for favorites/tags — keyed by device kind, never the
    /// volatile plugin catalog index. Empty for non-device rows (presets/samples/etc).</summary>
    public string LibraryKey => Kind switch
    {
        BrowserItemKind.BuiltinInstrument => $"bi:{BuiltinKind}",
        BrowserItemKind.BuiltinEffect => $"be:{BuiltinKind}",
        BrowserItemKind.BuiltinMidiEffect => $"bm:{BuiltinKind}",
        // Keyed on the format, not the (display-only) vendor, so favorites and tags
        // saved before the vendor moved into Sub still resolve.
        BrowserItemKind.PluginInstrument or BrowserItemKind.PluginEffect => $"pl:{Name}|{Format}",
        _ => "",
    };
}

public sealed partial class BrowserViewModel : ObservableObject
{
    // A safety cap on the Files scan, not a browsing limit: installed sample packs alone run to
    // tens of thousands of files (the tree builds in ~150 ms for 45k).
    private const int MaxSamples = 100_000;
    private static readonly string[] SampleExts = { ".wav", ".flac", ".mp3" };

    private readonly IPluginCatalog _catalog;
    private readonly IPresetLibrary _presets;
    private readonly IMosaicPacks? _mosaic;
    private readonly IFactoryPresets _factory;
    private readonly IDrumKits? _kits;
    private readonly ISettingsService? _settings;
    private readonly IBrowserLibrary? _library;
    private readonly ISampleIndex? _index;

    // Files tab: every audio leaf (for live tag updates), the sample filter, and "similar
    // sounds" mode — a flat list of the closest samples to an anchor, in place of the tree.
    private readonly List<BrowserItem> _sampleLeaves = new();
    private SampleFilter _sampleFilter = SampleFilter.Empty;
    private BrowserItem? _similarAnchor;
    private List<BrowserItem> _similarRows = new();

    // Full trees (top-level devices/plugins with factory-preset children); the public
    // Instruments/Effects collections are the flattened, filtered *visible* projection.
    private readonly List<BrowserItem> _instrTree = new();
    private readonly List<BrowserItem> _fxTree = new();
    private readonly List<BrowserItem> _midiTree = new();
    private readonly List<BrowserItem> _sampleTree = new();   // Files tab: folder hierarchy roots
    private readonly List<BrowserItem> _presetTree = new();   // Presets tab: category → device → preset
    private readonly HashSet<string> _expandedTreeKeys = new(StringComparer.OrdinalIgnoreCase); // folder expansion (Files+Presets), survives rescans
    private readonly string[] _treeQuery = { "", "", "", "", "" };   // per tree tab: 0 Instr, 1 FX, 2 MIDI, 3 Files, 4 Presets

    private BrowserFilter _filterMode = BrowserFilter.None;
    private string _filterTagId = "";
    private bool _groupBySource = true;
    private bool _favoritesFirst = true;

    /// <summary>Split the device tabs into BUILT-IN / PLUG-INS sections with counts.</summary>
    public bool GroupBySource
    {
        get => _groupBySource;
        set { if (_groupBySource == value) return; _groupBySource = value; RebuildDeviceTabs(); }
    }

    /// <summary>Float favorited devices to the top of their section.</summary>
    public bool FavoritesFirst
    {
        get => _favoritesFirst;
        set { if (_favoritesFirst == value) return; _favoritesFirst = value; RebuildDeviceTabs(); }
    }

    private void RebuildDeviceTabs() { RebuildVisible(0); RebuildVisible(1); RebuildVisible(2); }

    public ObservableCollection<BrowserItem> Instruments { get; } = new();
    public ObservableCollection<BrowserItem> Effects { get; } = new();
    public ObservableCollection<BrowserItem> MidiEffects { get; } = new();
    public BulkObservableCollection<BrowserItem> Samples { get; } = new();
    public ObservableCollection<BrowserItem> Projects { get; } = new();
    public BulkObservableCollection<BrowserItem> Presets { get; } = new();

    public BrowserViewModel(IPluginCatalog catalog, IPresetLibrary presets, IFactoryPresets factory,
                            ISettingsService? settings = null, IBrowserLibrary? library = null,
                            IDrumKits? kits = null, ISampleIndex? sampleIndex = null, IMosaicPacks? mosaic = null)
    {
        _mosaic = mosaic;
        _index = sampleIndex;
        _catalog = catalog;
        _presets = presets;
        _factory = factory;
        _kits = kits;
        _settings = settings;
        _library = library;
        if (_library is not null) _library.Changed += OnLibraryChanged;
        Rebuild();
    }

    // --- favorites / tags (library metadata) --------------------------------

    /// <summary>All user tags (empty if no library service).</summary>
    public IReadOnlyList<BrowserTag> Tags => _library?.Tags ?? Array.Empty<BrowserTag>();

    public BrowserFilter FilterMode => _filterMode;
    public string FilterTagId => _filterTagId;

    /// <summary>Raised after favorites/tags change so the view can rebuild its filter chips.</summary>
    public event Action? LibraryChanged;

    public bool IsFavorite(BrowserItem it) => _library?.IsFavorite(it.LibraryKey) ?? false;
    public bool IsTagAssigned(BrowserItem it, string tagId) => _library?.TagIdsFor(it.LibraryKey).Contains(tagId) ?? false;

    /// <summary>Resolved tags assigned to a device row (in library order), for row markers.</summary>
    public IReadOnlyList<BrowserTag> TagsFor(BrowserItem it)
    {
        if (_library is null) return Array.Empty<BrowserTag>();
        var ids = _library.TagIdsFor(it.LibraryKey);
        return ids.Count == 0 ? Array.Empty<BrowserTag>() : _library.Tags.Where(t => ids.Contains(t.Id)).ToList();
    }

    public void ToggleFavorite(BrowserItem it) { if (it.LibraryKey.Length > 0) _library?.SetFavorite(it.LibraryKey, !IsFavorite(it)); }
    public void AssignTag(BrowserItem it, string tagId, bool on) { if (it.LibraryKey.Length > 0) _library?.AssignTag(it.LibraryKey, tagId, on); }
    public BrowserTag? CreateTag(string title, string color) => _library?.CreateTag(title, color);
    public void UpdateTag(string id, string title, string color) => _library?.UpdateTag(id, title, color);
    public void DeleteTag(string id) => _library?.DeleteTag(id);

    /// <summary>Sets the active header filter for the device tabs and refreshes them.</summary>
    public void SetLibraryFilter(BrowserFilter mode, string tagId = "")
    {
        _filterMode = mode;
        _filterTagId = tagId ?? "";
        RebuildDeviceTabs();
    }

    // Any favorite/tag mutation → refresh device rows (markers + active filter). If the
    // filtered-on tag was deleted, drop back to the unfiltered view.
    private void OnLibraryChanged()
    {
        if (_filterMode == BrowserFilter.Tag && !Tags.Any(t => t.Id == _filterTagId))
        { _filterMode = BrowserFilter.None; _filterTagId = ""; }
        RebuildDeviceTabs();
        LibraryChanged?.Invoke();
    }

    private bool PassesLibraryFilter(BrowserItem node) => _filterMode switch
    {
        BrowserFilter.Favorites => _library?.IsFavorite(node.LibraryKey) ?? false,
        BrowserFilter.Tag => _library?.TagIdsFor(node.LibraryKey).Contains(_filterTagId) ?? false,
        _ => true,
    };

    /// <summary>Rescan plugins out-of-process, then rebuild the lists.</summary>
    public int Scan(string workerPath)
    {
        int n = _catalog.Scan(workerPath);
        Rebuild();
        return n;
    }

    public void Rebuild()
    {
        _instrTree.Clear();
        _fxTree.Clear();
        _midiTree.Clear();
        
        // The built-in devices come from one registry (BuiltinDeviceCatalog) — the command
        // palette reads the same list, with each device's semantic descriptor.
        var builtins = BuiltinDeviceCatalog.All.Select(d => new BrowserItem
        {
            Name = d.Name,
            Kind = d.Type switch
            {
                BuiltinDeviceType.Instrument => BrowserItemKind.BuiltinInstrument,
                BuiltinDeviceType.AudioEffect => BrowserItemKind.BuiltinEffect,
                _ => BrowserItemKind.BuiltinMidiEffect,
            },
            BuiltinKind = d.Kind,
            Sub = d.Sub,
        }).OrderBy(x => x.Name).ToList();
        _instrTree.AddRange(builtins.Where(b => b.Kind == BrowserItemKind.BuiltinInstrument));
        _fxTree.AddRange(builtins.Where(b => b.Kind == BrowserItemKind.BuiltinEffect));
        _midiTree.AddRange(builtins.Where(b => b.Kind == BrowserItemKind.BuiltinMidiEffect));

        // Attach factory presets under their parent built-in device: unfiled ones (Init) loose
        // at the top, the rest in a folder per category, folders in catalog order.
        var folders = new Dictionary<(BrowserItem, string), BrowserItem>();
        foreach (var fp in _factory.All())
        {
            var tree = fp.IsMidiEffect ? _midiTree : fp.IsInstrument ? _instrTree : _fxTree;
            var parentKind = fp.IsMidiEffect ? BrowserItemKind.BuiltinMidiEffect
                : fp.IsInstrument ? BrowserItemKind.BuiltinInstrument : BrowserItemKind.BuiltinEffect;
            var parent = tree.Find(d => d.BuiltinKind == fp.BuiltinKind && d.Kind == parentKind);
            if (parent is null) continue;
            bool filed = fp.Category.Length > 0;
            var preset = new BrowserItem
            {
                Name = fp.DisplayName,
                Kind = BrowserItemKind.Preset,
                Sub = filed ? fp.Category : "preset",
                Path = "factory:" + fp.Id,
                Depth = filed ? 2 : 1,
            };
            if (!filed) { parent.Children.Insert(parent.Children.Count(c => !c.HasChildren), preset); continue; }
            if (!folders.TryGetValue((parent, fp.Category), out var folder))
            {
                folder = new BrowserItem
                {
                    Name = fp.Category,
                    Kind = BrowserItemKind.Folder,
                    Path = $"category:{parent.LibraryKey}/{fp.Category}",
                    Depth = 1,
                };
                folders[(parent, fp.Category)] = folder;
                parent.Children.Add(folder);
            }
            folder.Children.Add(preset);
        }
        // Factory drum kits hang under the Drum Rack and Nota Rhythm alongside their presets —
        // a kit is what a "preset" means for those two, and both share the one set of kits.
        if (_kits is not null)
            foreach (var (kind, prefix, what) in new[] { (4, "kit:", "pads"), (12, "rhythmkit:", "pads, 8 on the voices") })
            {
                var dev = _instrTree.Find(d => d.Kind == BrowserItemKind.BuiltinInstrument && d.BuiltinKind == kind);
                if (dev is null) continue;
                foreach (var kit in _kits.All())
                    dev.Children.Add(new BrowserItem
                    {
                        Name = kit.Name,
                        Kind = BrowserItemKind.Preset,
                        Sub = "kit",
                        Tip = $"{kit.Blurb} · {kit.PadCount} {what}",
                        Path = prefix + kit.Id,
                        Depth = 1,
                    });
            }

        // Nota Mosaic's pack presets (installed sample packs, "Create multisample"): Packs → a pack.
        if (_mosaic is not null && _instrTree.Find(d => d.Kind == BrowserItemKind.BuiltinInstrument && d.BuiltinKind == MosaicModel.Kind) is { } mosaicDev)
        {
            var list = _mosaic.Presets();
            if (list.Count > 0)
            {
                var packs = new BrowserItem { Name = "Packs", Kind = BrowserItemKind.Folder, Path = $"category:{mosaicDev.LibraryKey}/Packs", Depth = 1 };
                foreach (var g in list.GroupBy(p => p.Folder))
                {
                    var folder = new BrowserItem { Name = g.Key, Kind = BrowserItemKind.Folder, Path = $"category:{mosaicDev.LibraryKey}/Packs/{g.Key}", Depth = 2 };
                    foreach (var p in g)
                        folder.Children.Add(new BrowserItem { Name = p.Name, Kind = BrowserItemKind.Preset, Sub = p.Source == "sfz" ? "sfz" : "multi", Path = p.Path, Depth = 3 });
                    packs.Children.Add(folder);
                }
                mosaicDev.Children.Add(packs);
            }
        }

        // Preset groups start collapsed; the user expands a device to reveal its folders.
        foreach (var tree in new[] { _instrTree, _fxTree, _midiTree })
            foreach (var d in tree)
            {
                d.IsExpanded = false;
                foreach (var c in d.Children) { c.IsExpanded = false; foreach (var cc in c.Children) cc.IsExpanded = false; }
            }

        int count = _catalog.Count;
        for (int i = 0; i < count; i++)
        {
            var desc = _catalog.Description(i) ?? "";
            // "Name | Format | inst|fx | Manufacturer"
            var parts = desc.Split('|');
            string name = parts.Length > 0 ? parts[0].Trim() : desc;
            string fmt = parts.Length > 1 ? parts[1].Trim() : "";
            string vendor = parts.Length > 3 ? parts[3].Trim() : "";
            if (vendor == "?") vendor = "";
            string category = parts.Length > 4 ? parts[4].Trim() : "";   // VST3 sub-categories / AU type
            bool isInstrument = desc.Contains("| inst |");
            var item = new BrowserItem
            {
                Name = name,
                // The row's right-edge tag. Format first: the same plug-in is often installed
                // in two formats, and those rows are otherwise identical. The vendor follows,
                // and is what gets trimmed when the panel is narrow.
                Sub = fmt.Length > 0 && vendor.Length > 0 ? $"{fmt} · {vendor}"
                    : vendor.Length > 0 ? vendor : fmt,
                Format = fmt,
                Vendor = vendor,
                Category = category,
                Kind = isInstrument ? BrowserItemKind.PluginInstrument : BrowserItemKind.PluginEffect,
                CatalogIndex = i,
            };
            (isInstrument ? _instrTree : _fxTree).Add(item);
        }

        RebuildDeviceTabs();
        RebuildSamples();
        RebuildProjects();
        RebuildPresets();
        DevicesRebuilt?.Invoke();
    }

    // --- the command palette's view of the library -------------------------------------

    /// <summary>Instruments / audio effects / MIDI effects: built-in devices (with their factory
    /// presets and kits as children, in category folders) and scanned plug-ins.</summary>
    public IReadOnlyList<BrowserItem> InstrumentTree => _instrTree;
    public IReadOnlyList<BrowserItem> EffectTree => _fxTree;
    public IReadOnlyList<BrowserItem> MidiTree => _midiTree;
    /// <summary>The user's saved presets: category folder → device folder → preset.</summary>
    public IReadOnlyList<BrowserItem> UserPresetTree => _presetTree;

    /// <summary>After <see cref="Rebuild"/> (plug-in rescan, folder change) — the palette re-indexes devices.</summary>
    public event Action? DevicesRebuilt;
    /// <summary>After the user presets were re-listed (saved / deleted / folder change).</summary>
    public event Action? PresetsRebuilt;

    /// <summary>The titles of the user's tags on a device row.</summary>
    public IReadOnlyList<string> TagTitlesFor(BrowserItem it) => TagsFor(it).Select(t => t.Title).ToList();

    /// <summary>Expand/collapse a tree node (device or Files folder), then refresh its
    /// tab's visible list.</summary>
    public void ToggleExpand(BrowserItem item)
    {
        if (!item.HasChildren) return;
        item.IsExpanded = !item.IsExpanded;
        int deviceTab = DeviceTabOf(item);
        if (deviceTab >= 0) { RebuildVisible(deviceTab); return; }
        if (item.Kind == BrowserItemKind.Folder)
        {
            // Remember folder expansion across full rescans (settings change / refresh). Folders
            // live in either the Files or Presets tree; refresh both (only the active tab shows).
            if (item.IsExpanded) _expandedTreeKeys.Add(item.Path);
            else _expandedTreeKeys.Remove(item.Path);
            RebuildSampleVisible();
            RebuildPresetVisible();
            return;
        }
        RebuildVisible(item.Kind switch
        {
            BrowserItemKind.BuiltinEffect or BrowserItemKind.PluginEffect => 1,
            BrowserItemKind.BuiltinMidiEffect => 2,
            _ => 0,
        });
    }

    // The device tab (0 Instr / 1 FX / 2 MIDI) a preset category folder belongs to, or -1.
    private int DeviceTabOf(BrowserItem item)
    {
        if (item.Kind != BrowserItemKind.Folder) return -1;
        if (_instrTree.Any(d => d.Children.Contains(item))) return 0;
        if (_fxTree.Any(d => d.Children.Contains(item))) return 1;
        if (_midiTree.Any(d => d.Children.Contains(item))) return 2;
        return -1;
    }

    /// <summary>Sets the live search query for a tree tab (0 = Instruments, 1 = FX,
    /// 2 = MIDI, 3 = Files, 4 = Presets) and refreshes its visible list.</summary>
    public void FilterTree(int tab, string query)
    {
        if (tab >= 0 && tab < _treeQuery.Length) _treeQuery[tab] = query ?? "";
        if (tab == 3) RebuildSampleVisible();
        else if (tab == 4) RebuildPresetVisible();
        else RebuildVisible(tab);
    }

    // Flatten a tree into its visible ObservableCollection, honouring expand state and
    // the current query. With a query, matching is name/sub substring; a matching device
    // shows its category folders, and a device with matching presets shows just those
    // (inside their folders, which a query opens). With
    // GroupBySource on, a BUILT-IN / PLUG-INS header carrying the section's device count
    // is emitted ahead of each run — so it is clear where Nota's own devices end.
    private void RebuildVisible(int tab)
    {
        var tree = tab == 2 ? _midiTree : tab == 1 ? _fxTree : _instrTree;
        var dst = tab == 2 ? MidiEffects : tab == 1 ? Effects : Instruments;
        string q = _treeQuery[tab].Trim();
        dst.Clear();

        // Favorited devices float to the top (stable sorts: alphabetical order survives
        // inside each run, and the source sort keeps the favorites-first order within a section).
        IEnumerable<BrowserItem> ordered = tree;
        if (_favoritesFirst && _library is not null)
            ordered = ordered.OrderByDescending(n => _library.IsFavorite(n.LibraryKey));
        if (_groupBySource) ordered = ordered.OrderBy(n => n.IsPlugin ? 1 : 0);

        // Visible devices first, so a section header can carry its real count.
        var devices = new List<BrowserItem>();
        foreach (var node in ordered)
        {
            if (!PassesLibraryFilter(node)) continue;   // header favorite/tag chip
            if (q.Length == 0 || Matches(node, q) || AnyMatch(node.Children, q))
                devices.Add(node);
        }

        string section = "";
        foreach (var node in devices)
        {
            if (_groupBySource)
            {
                string s = node.IsPlugin ? "PLUG-INS" : "BUILT-IN";
                if (s != section)
                {
                    section = s;
                    dst.Add(new BrowserItem
                    {
                        Name = s,
                        Kind = BrowserItemKind.Group,
                        GroupCount = devices.Count(d => (d.IsPlugin ? "PLUG-INS" : "BUILT-IN") == s),
                    });
                }
            }
            dst.Add(node);
            // No query: presets follow only while the device is expanded. With a query, a
            // device that matched shows its folders as if expanded; one that didn't shows the hits.
            if (q.Length == 0) { if (node.IsExpanded) AddPresetRows(node.Children, "", dst); }
            else AddPresetRows(node.Children, Matches(node, q) ? "" : q, dst);
        }
    }

    // A device's preset rows: loose presets, then category folders with their presets. With
    // no query, a folder shows its presets only while expanded; with one, a folder shows up
    // open when its name or any of its presets match (a name hit reveals all of them).
    private static void AddPresetRows(List<BrowserItem> children, string q, ObservableCollection<BrowserItem> dst)
    {
        foreach (var c in children)
        {
            if (!c.HasChildren)
            {
                if (q.Length == 0 || Matches(c, q)) dst.Add(c);
                continue;
            }
            // Folders nest (Nota Mosaic → Packs → a pack): the same rule one level down.
            if (q.Length == 0)
            {
                dst.Add(c);
                if (c.IsExpanded) AddPresetRows(c.Children, "", dst);
            }
            else if (Matches(c, q))
            {
                dst.Add(c);
                AddPresetRows(c.Children, "", dst);
            }
            else if (AnyMatch(c.Children, q))
            {
                dst.Add(c);
                AddPresetRows(c.Children, q, dst);
            }
        }
    }

    private static bool AnyMatch(List<BrowserItem> nodes, string q)
        => nodes.Any(n => Matches(n, q) || AnyMatch(n.Children, q));

    /// <summary>Device rows on a tab, split by source — for the browser's status line.
    /// Counts what is actually visible (search + favorite/tag filter applied).</summary>
    public (int builtin, int plugins) VisibleDeviceCounts(int tab)
    {
        var src = tab == 2 ? MidiEffects : tab == 1 ? Effects : Instruments;
        int bi = 0, pl = 0;
        foreach (var it in src)
        {
            if (it.IsGroup || it.Depth > 0) continue;
            if (it.IsPlugin) pl++; else bi++;
        }
        return (bi, pl);
    }

    private static bool Matches(BrowserItem it, string q)
        => it.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
           || it.Sub.Contains(q, StringComparison.OrdinalIgnoreCase);

    /// <summary>Scans the configured samples folder into a navigable folder tree (M7-4a).
    /// Folders become expandable parent nodes; audio and MIDI files are leaves. Only folders
    /// on a path to such a file appear.</summary>
    public void RebuildSamples()
    {
        _sampleTree.Clear();
        _sampleLeaves.Clear();
        Samples.Clear();
        if (_settings is null) return;
        var root = _settings.ResolvedSamplesFolder();
        _index?.Watch(root);   // analyses whatever it doesn't know yet, in the background

        // Folder nodes keyed by absolute directory path (reused while placing files).
        var folders = new Dictionary<string, BrowserItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateFilesSafe(root)
                     .Where(p => SampleExts.Contains(Path.GetExtension(p).ToLowerInvariant())
                                 || Nota.Application.Midi.MidiFileReader.IsMidiFile(p))
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxSamples))
        {
            var dir = Path.GetDirectoryName(path) ?? root;
            var parentChildren = GetOrCreateFolder(root, dir, folders, out int depth);
            var leaf = new BrowserItem
            {
                Name = Path.GetFileName(path),
                Kind = Nota.Application.Midi.MidiFileReader.IsMidiFile(path) ? BrowserItemKind.MidiFile : BrowserItemKind.Sample,
                Sub = "",
                Path = path,
                Depth = depth,
            };
            if (leaf.Kind == BrowserItemKind.Sample) { leaf.Sample = _index?.Get(path); _sampleLeaves.Add(leaf); }
            parentChildren.Add(leaf);
        }
        AppendKitFolders();
        SortSampleTree(_sampleTree);
        _similarAnchor = null;   // the anchor row is gone; its list would point at stale rows
        _similarRows.Clear();
        RebuildSampleVisible();
    }

    // --- smart samples: tags, filter, similar ------------------------------------------

    /// <summary>The Files tab's sample filter ("loops · 120–128 · A minor").</summary>
    public SampleFilter SampleFilter
    {
        get => _sampleFilter;
        set { _sampleFilter = value ?? SampleFilter.Empty; RebuildSampleVisible(); }
    }

    /// <summary>The sample "similar sounds" is showing neighbours of, or null for the tree.</summary>
    public BrowserItem? SimilarAnchor => _similarAnchor;

    /// <summary>Files analysed / found by the running library scan; Total 0 when idle.</summary>
    public (int Done, int Total) IndexProgress => _index?.Progress ?? (0, 0);

    public bool HasSampleIndex => _index is not null;

    /// <summary>Raised after <see cref="OnSampleIndexChanged"/> so the view can refresh its status line.</summary>
    public event Action? SampleIndexChanged;

    /// <summary>Call on the UI thread when the index reports progress: Files rows pick up their
    /// tags in place (no list reset — the selection and scroll stay), and a filtered or
    /// "similar" list is recomputed only if its rows actually changed.</summary>
    public void OnSampleIndexChanged()
    {
        if (_index is null) return;
        foreach (var leaf in _sampleLeaves) leaf.Sample = _index.Get(leaf.Path);
        if (!_sampleFilter.IsEmpty) RebuildSampleVisible(onlyIfChanged: true);
        SampleIndexChanged?.Invoke();
    }

    /// <summary>Replaces the Files tree with the samples that sound most like
    /// <paramref name="item"/>, closest first. Analyses the anchor first if it must (off the
    /// UI thread). Returns how many were found.</summary>
    public async Task<int> ShowSimilarAsync(BrowserItem item, int count = 50)
    {
        if (_index is null || item.Kind != BrowserItemKind.Sample) return 0;
        var index = _index;
        string path = item.Path;
        var hits = await Task.Run(() => index.Similar(path, count));
        var root = _settings?.ResolvedSamplesFolder();
        _similarRows = hits.Select(h => new BrowserItem
        {
            Name = Path.GetFileName(h.Info.Path),
            Kind = BrowserItemKind.Sample,
            Sub = "",
            Tip = FolderOf(h.Info.Path, root),
            Path = h.Info.Path,
            Depth = 0,
        }).ToList();
        foreach (var r in _similarRows) r.Sample = index.Get(r.Path);
        _similarAnchor = item;
        item.Sample ??= index.Get(path);
        RebuildSampleVisible();
        return _similarRows.Count;
    }

    /// <summary>Back from "similar sounds" to the folder tree.</summary>
    public void ClearSimilar()
    {
        if (_similarAnchor is null) return;
        _similarAnchor = null;
        _similarRows.Clear();
        RebuildSampleVisible();
    }

    private static string FolderOf(string path, string? root)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        if (root is null) return dir;
        var rel = Path.GetRelativePath(root, dir);
        return rel == "." ? "" : rel.StartsWith("..", StringComparison.Ordinal) ? dir : rel;
    }

    // Files rows the sample filter lets through: audio the index has analysed and that
    // matches; a MIDI file has no tempo or key, so any filter hides it.
    private bool PassesSampleFilter(BrowserItem leaf)
        => leaf.Kind == BrowserItemKind.Sample ? _sampleFilter.Matches(leaf.Sample) : _sampleFilter.IsEmpty;

    // The rendered factory kits, as a browsable folder alongside the user's own samples.
    // They live in Nota's data folder rather than the user's samples folder (they are
    // regenerable, and they should not appear in a folder the user curates), so the tree
    // gets them from the kit service instead of the folder scan above.
    private void AppendKitFolders()
    {
        if (_kits is null) return;
        var root = _kits.Root;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        var rootNode = new BrowserItem
        {
            Name = "Nota Kits",
            Kind = BrowserItemKind.Folder,
            Sub = "factory",
            Path = root,
            Depth = 0,
            IsExpanded = _expandedTreeKeys.Contains(root),
        };
        foreach (var kit in _kits.All())
        {
            var dir = _kits.FolderOf(kit.Id);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            var kitNode = new BrowserItem
            {
                Name = kit.Name,
                Kind = BrowserItemKind.Folder,
                Sub = "",
                Tip = kit.Blurb,
                Path = dir,
                Depth = 1,
                IsExpanded = _expandedTreeKeys.Contains(dir),
            };
            foreach (var f in Directory.EnumerateFiles(dir, "*.wav").OrderBy(f => f, StringComparer.Ordinal))
                kitNode.Children.Add(new BrowserItem
                {
                    Name = Path.GetFileName(f),
                    Kind = BrowserItemKind.Sample,
                    Sub = "",
                    Path = f,
                    Depth = 2,
                });
            if (kitNode.Children.Count > 0) rootNode.Children.Add(kitNode);
        }
        if (rootNode.Children.Count > 0) _sampleTree.Add(rootNode);
    }

    // Ensures the folder chain from root down to `dir` exists, returning the child list the
    // file should join and the file's tree depth. Root-level files land directly in the roots.
    private List<BrowserItem> GetOrCreateFolder(string root, string dir,
                                                Dictionary<string, BrowserItem> folders, out int fileDepth)
    {
        var rel = Path.GetRelativePath(root, dir);
        if (rel == "." || rel.StartsWith("..", StringComparison.Ordinal)) { fileDepth = 0; return _sampleTree; }

        var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        List<BrowserItem> children = _sampleTree;
        string abs = root;
        for (int i = 0; i < segments.Length; i++)
        {
            abs = Path.Combine(abs, segments[i]);
            if (!folders.TryGetValue(abs, out var node))
            {
                bool downloads = i == 0 && string.Equals(segments[0], SamplePacks.FolderName, StringComparison.OrdinalIgnoreCase);
                node = new BrowserItem
                {
                    Name = segments[i],
                    Kind = BrowserItemKind.Folder,
                    Sub = "",
                    Tip = downloads ? "sample packs installed from Settings → Downloads" : "",
                    Path = abs,
                    Depth = i,
                    IsExpanded = _expandedTreeKeys.Contains(abs),
                    IsDownloads = downloads,
                };
                folders[abs] = node;
                children.Add(node);
            }
            children = node.Children;
        }
        fileDepth = segments.Length;
        return children;
    }

    // Downloaded first, then folders before files, each group alphabetical; recurses into subfolders.
    private static void SortSampleTree(List<BrowserItem> nodes)
    {
        nodes.Sort((a, b) =>
        {
            if (a.IsDownloads != b.IsDownloads) return a.IsDownloads ? -1 : 1;
            bool af = a.Kind == BrowserItemKind.Folder, bf = b.Kind == BrowserItemKind.Folder;
            if (af != bf) return af ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var n in nodes) if (n.Kind == BrowserItemKind.Folder) SortSampleTree(n.Children);
    }

    /// <summary>Flattens the sample folder tree into the visible <see cref="Samples"/>
    /// collection, honouring expand state and the Files search query.</summary>
    private void RebuildSampleVisible(bool onlyIfChanged = false)
    {
        string q = _treeQuery[3].Trim();
        List<BrowserItem> rows;
        if (_similarAnchor is not null)
            rows = _similarRows.Where(r => (q.Length == 0 || Matches(r, q)) && PassesSampleFilter(r)).ToList();
        else
        {
            rows = new List<BrowserItem>();
            Func<BrowserItem, bool>? leafOk = _sampleFilter.IsEmpty ? null : PassesSampleFilter;
            foreach (var node in _sampleTree) FlattenTree(node, q, ancestorMatched: false, rows, leafOk);
        }
        if (onlyIfChanged && rows.SequenceEqual(Samples)) return;
        Samples.ReplaceAll(rows);
    }

    /// <summary>Flattens the preset tree (category → device → preset) into <see cref="Presets"/>.</summary>
    private void RebuildPresetVisible() => RebuildTreeVisible(_presetTree, Presets, _treeQuery[4].Trim());

    // Shared folder-tree flatten (Files + Presets): both are Folder parents with non-folder
    // leaves, so the same expand/query logic applies.
    private void RebuildTreeVisible(List<BrowserItem> roots, BulkObservableCollection<BrowserItem> dst, string q)
    {
        var rows = new List<BrowserItem>();
        foreach (var node in roots) FlattenTree(node, q, ancestorMatched: false, rows, leafOk: null);
        dst.ReplaceAll(rows);
    }

    // Adds `node` (and, for folders, its visible descendants) to `dst`. Returns whether the
    // node ended up visible. With a query, folders auto-expand and survive only if the folder
    // name or some descendant matches; a folder-name hit reveals its whole subtree. A leaf
    // filter (the Files tab's sample filter) works like a query: folders open to show what
    // passes and drop out when nothing under them does — a folder's name alone never does.
    private bool FlattenTree(BrowserItem node, string q, bool ancestorMatched, List<BrowserItem> dst,
                             Func<BrowserItem, bool>? leafOk)
    {
        if (node.Kind != BrowserItemKind.Folder)
        {
            bool show = (q.Length == 0 || ancestorMatched || Matches(node, q)) && (leafOk?.Invoke(node) ?? true);
            if (show) dst.Add(node);
            return show;
        }
        bool folderMatched = ancestorMatched || (q.Length > 0 && Matches(node, q));
        if (q.Length == 0 && leafOk is null)
        {
            dst.Add(node);
            if (node.IsExpanded)
                foreach (var c in node.Children) FlattenTree(c, q, ancestorMatched: false, dst, leafOk);
            return true;
        }
        int mark = dst.Count;
        dst.Add(node);                  // tentative — removed below if nothing under it matches
        bool any = folderMatched && leafOk is null;
        foreach (var c in node.Children)
            any |= FlattenTree(c, q, folderMatched, dst, leafOk);
        if (!any) { while (dst.Count > mark) dst.RemoveAt(dst.Count - 1); return false; }
        return true;
    }

    /// <summary>Scans the configured projects folder for .nota bundles (M7-4b).</summary>
    public void RebuildProjects()
    {
        Projects.Clear();
        if (_settings is null) return;
        var root = _settings.ResolvedProjectsFolder();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root)
                         .Where(d => d.EndsWith(".nota", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                Projects.Add(new BrowserItem
                {
                    Name = Path.GetFileNameWithoutExtension(dir),
                    Kind = BrowserItemKind.Project,
                    Sub = "project",
                    Path = dir,
                });
            }
        }
        catch { /* folder missing/unreadable — empty list */ }
    }

    /// <summary>Lists saved presets from the app presets folder, grouped into a tree:
    /// category (Instruments / Audio Effects / MIDI Effects) → device → preset (M7-4c).</summary>
    public void RebuildPresets()
    {
        _presetTree.Clear();
        Presets.Clear();
        if (_settings is null) return;
        var root = _settings.PresetsFolder();

        var cats = new Dictionary<string, BrowserItem>(StringComparer.OrdinalIgnoreCase);
        var devs = new Dictionary<string, BrowserItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _presets.List(root))
        {
            string cat = CategoryOf(p.Type);
            string dev = string.IsNullOrWhiteSpace(p.DeviceName) ? "Other" : p.DeviceName;
            var catNode = GetOrAddPresetFolder(cats, cat, cat, $"presetcat:{cat}", depth: 0, parent: _presetTree);
            var devNode = GetOrAddPresetFolder(devs, $"{cat}/{dev}", dev, $"presetdev:{cat}/{dev}", depth: 1, parent: catNode.Children);
            devNode.Children.Add(new BrowserItem
            {
                Name = p.DisplayName,
                Kind = BrowserItemKind.Preset,
                Sub = "",
                Path = p.Path,
                Depth = 2,
            });
        }
        SortSampleTree(_presetTree);   // folders-first alphabetical, recursive
        RebuildPresetVisible();
        PresetsRebuilt?.Invoke();
    }

    private static string CategoryOf(string type)
        => type.Contains("midi", StringComparison.OrdinalIgnoreCase) ? "MIDI Effects"
         : type.Contains("instrument", StringComparison.OrdinalIgnoreCase) ? "Instruments"
         : "Audio Effects";

    // Fetches or creates a preset-tree folder node (category or device), keyed for expansion
    // persistence and linked under `parent`.
    private BrowserItem GetOrAddPresetFolder(Dictionary<string, BrowserItem> index, string key,
                                             string name, string expandKey, int depth, List<BrowserItem> parent)
    {
        if (index.TryGetValue(key, out var node)) return node;
        node = new BrowserItem
        {
            Name = name,
            Kind = BrowserItemKind.Folder,
            Sub = "",
            Path = expandKey,
            Depth = depth,
            IsExpanded = _expandedTreeKeys.Contains(expandKey),
        };
        index[key] = node;
        parent.Add(node);
        return node;
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        try { return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories); }
        catch { return Enumerable.Empty<string>(); }
    }
}
