// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M4.1-C: left browser. Aggregates built-in instruments/effects, the scanned
// plugin catalog (AU/VST3), and (M7-4) real samples, projects and presets from
// configurable folders. Double-clicking an item in the view adds/opens/applies
// it (handled by MainWindow).

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Nota.Application;

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

public sealed class BrowserItem
{
    public required string Name { get; init; }
    public required BrowserItemKind Kind { get; init; }
    public int CatalogIndex { get; init; } = -1; // plugins
    public int BuiltinKind { get; init; } = -1;   // effects: 0=EQ,1=Compressor,2=Reverb,3=Delay,4=Utility · instruments: 0=Synth,2=Physical,5=Aurora
    public string Sub { get; init; } = "";        // type / manufacturer / folder
    public string Format { get; init; } = "";     // plug-ins: "VST3" / "AU" (Sub carries the vendor)
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
    private readonly IFactoryPresets _factory;
    private readonly IDrumKits? _kits;
    private readonly ISettingsService? _settings;
    private readonly IBrowserLibrary? _library;

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
                            IDrumKits? kits = null)
    {
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
        
        var midis = new List<BrowserItem>
        {
            new() { Name = "Nota Arp", Kind = BrowserItemKind.BuiltinMidiEffect, BuiltinKind = 0, Sub = "step arpeggiator" },
            new() { Name = "Nota Chord", Kind = BrowserItemKind.BuiltinMidiEffect, BuiltinKind = 1, Sub = "stacked intervals" },
            new() { Name = "Nota Scale", Kind = BrowserItemKind.BuiltinMidiEffect, BuiltinKind = 2, Sub = "pitch quantize" },
            new() { Name = "Nota Length", Kind = BrowserItemKind.BuiltinMidiEffect, BuiltinKind = 3, Sub = "force note length" },
            new() { Name = "Nota Velocity", Kind = BrowserItemKind.BuiltinMidiEffect, BuiltinKind = 4, Sub = "velocity shaping" },
            new() { Name = "Nota Random", Kind = BrowserItemKind.BuiltinMidiEffect, BuiltinKind = 5, Sub = "random transpose" }
        };
        _midiTree.AddRange(midis.OrderBy(x => x.Name).ToList());
        
        var instruments = new List<BrowserItem>
        {
            new() { Name = "Nota Synth", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 0, Sub = "subtractive synth" },
            new() { Name = "Nota Physical", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 2, Sub = "physical synth" },
            new() { Name = "Nota Aurora", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 5, Sub = "wavetable synth" },
            new() { Name = "Nota Volt", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 6, Sub = "analog synth" },
            new() { Name = "Nota Bass", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 7, Sub = "bass synth" },
            new() { Name = "Nota Pendulum", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 8, Sub = "arp synth" },
            new() { Name = "Nota Operator", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 9, Sub = "FM synth" },
            new() { Name = "Nota Grain", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 10, Sub = "granular synth" },
            new() { Name = "Nota Flux", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 11, Sub = "vector-morph synth" },
            new() { Name = "Nota Rhythm", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 12, Sub = "drum machine" },
            new() { Name = "Nota Monolith", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 13, Sub = "mono synth" },
            new() { Name = "Nota Pentad", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 14, Sub = "poly synth" },
            new() { Name = "Nota Consort", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 15, Sub = "paraphonic synth" },
            new() { Name = "Nota Sampler", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 1, Sub = "built-in" },
            new() { Name = "Nota Instrument Rack", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 3, Sub = "built-in" },
            new() { Name = "Nota Drum Rack", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 4, Sub = "built-in" }
        };
        _instrTree.AddRange(instruments.OrderBy(x => x.Name).ToList());

        var fxs = new List<BrowserItem>
        {
            new() { Name = "Nota EQ-3", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 16, Sub = "3-band EQ" },
            new() { Name = "Nota EQ-8", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 0, Sub = "8-band EQ" },
            new() { Name = "Nota Compressor", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 1, Sub = "built-in" },
            new() { Name = "Nota Prism", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 21, Sub = "multiband dynamics" },
            new() { Name = "Nota Lens", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 22, Sub = "analyzer / scope" },
            new() { Name = "Nota Reverb", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 2, Sub = "built-in" },
            new() { Name = "Nota Chamber", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 20, Sub = "hybrid reverb" },
            new() { Name = "Nota Delay", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 3, Sub = "built-in" },
            new() { Name = "Nota Utility", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 4, Sub = "built-in" },
            new() { Name = "Nota Level", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 18, Sub = "gain" },
            new() { Name = "Nota Shutter", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 19, Sub = "gate" },
            new() { Name = "Nota Valve", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 6, Sub = "amplifier" },
            new() { Name = "Nota Auto Filter", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 7, Sub = "envelope / LFO" },
            new() { Name = "Nota Vintage", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 8, Sub = "vintage saturator" },
            new() { Name = "Nota Forge", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 17, Sub = "saturator" },
            new() { Name = "Nota Orbit", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 9, Sub = "auto-pan" },
            new() { Name = "Nota Flanger", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 23, Sub = "flanger" },
            new() { Name = "Nota Phaser", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 24, Sub = "phaser" },
            new() { Name = "Nota Chorus", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 25, Sub = "chorus" },
            new() { Name = "Nota Auto Shift", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 10, Sub = "pitch correction" },
            new() { Name = "Nota Beat Repeat", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 11, Sub = "glitch & repeats" },
            new() { Name = "Nota Crush", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 12, Sub = "bit crusher" },
            new() { Name = "Nota Dynamic EQ-8", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 13, Sub = "dynamic EQ" },
            new() { Name = "Nota Ceiling", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 14, Sub = "limiter" },
            new() { Name = "Nota Strata", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 15, Sub = "looper" },
            new() { Name = "Nota Audio Effect Rack", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 5, Sub = "built-in" }
        };
        _fxTree.AddRange(fxs.OrderBy(x => x.Name).ToList());

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

        // Preset groups start collapsed; the user expands a device to reveal its folders.
        foreach (var tree in new[] { _instrTree, _fxTree, _midiTree })
            foreach (var d in tree)
            {
                d.IsExpanded = false;
                foreach (var c in d.Children) c.IsExpanded = false;
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
                Kind = isInstrument ? BrowserItemKind.PluginInstrument : BrowserItemKind.PluginEffect,
                CatalogIndex = i,
            };
            (isInstrument ? _instrTree : _fxTree).Add(item);
        }

        RebuildDeviceTabs();
        RebuildSamples();
        RebuildProjects();
        RebuildPresets();
    }

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
            if (q.Length == 0)
            {
                dst.Add(c);
                if (c.IsExpanded) foreach (var p in c.Children) dst.Add(p);
            }
            else if (Matches(c, q))
            {
                dst.Add(c);
                foreach (var p in c.Children) dst.Add(p);
            }
            else if (c.Children.Any(p => Matches(p, q)))
            {
                dst.Add(c);
                foreach (var p in c.Children) if (Matches(p, q)) dst.Add(p);
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
        Samples.Clear();
        if (_settings is null) return;
        var root = _settings.ResolvedSamplesFolder();

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
            parentChildren.Add(new BrowserItem
            {
                Name = Path.GetFileName(path),
                Kind = Nota.Application.Midi.MidiFileReader.IsMidiFile(path) ? BrowserItemKind.MidiFile : BrowserItemKind.Sample,
                Sub = "",
                Path = path,
                Depth = depth,
            });
        }
        AppendKitFolders();
        SortSampleTree(_sampleTree);
        RebuildSampleVisible();
    }

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
    private void RebuildSampleVisible() => RebuildTreeVisible(_sampleTree, Samples, _treeQuery[3].Trim());

    /// <summary>Flattens the preset tree (category → device → preset) into <see cref="Presets"/>.</summary>
    private void RebuildPresetVisible() => RebuildTreeVisible(_presetTree, Presets, _treeQuery[4].Trim());

    // Shared folder-tree flatten (Files + Presets): both are Folder parents with non-folder
    // leaves, so the same expand/query logic applies.
    private void RebuildTreeVisible(List<BrowserItem> roots, BulkObservableCollection<BrowserItem> dst, string q)
    {
        var rows = new List<BrowserItem>();
        foreach (var node in roots) FlattenTree(node, q, ancestorMatched: false, rows);
        dst.ReplaceAll(rows);
    }

    // Adds `node` (and, for folders, its visible descendants) to `dst`. Returns whether the
    // node ended up visible. With a query, folders auto-expand and survive only if the folder
    // name or some descendant matches; a folder-name hit reveals its whole subtree.
    private bool FlattenTree(BrowserItem node, string q, bool ancestorMatched, List<BrowserItem> dst)
    {
        if (node.Kind != BrowserItemKind.Folder)
        {
            bool show = q.Length == 0 || ancestorMatched || Matches(node, q);
            if (show) dst.Add(node);
            return show;
        }
        bool folderMatched = ancestorMatched || (q.Length > 0 && Matches(node, q));
        if (q.Length == 0)
        {
            dst.Add(node);
            if (node.IsExpanded)
                foreach (var c in node.Children) FlattenTree(c, q, ancestorMatched: false, dst);
            return true;
        }
        int mark = dst.Count;
        dst.Add(node);                  // tentative — removed below if nothing under it matches
        bool any = folderMatched;
        foreach (var c in node.Children)
            any |= FlattenTree(c, q, folderMatched, dst);
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
