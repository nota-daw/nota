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
}

/// <summary>Active library filter for the device tabs (Instr / FX / MIDI).</summary>
public enum BrowserFilter { None, Favorites, Tag }

public sealed class BrowserItem
{
    public required string Name { get; init; }
    public required BrowserItemKind Kind { get; init; }
    public int CatalogIndex { get; init; } = -1; // plugins
    public int BuiltinKind { get; init; } = -1;   // effects: 0=EQ,1=Compressor,2=Reverb,3=Delay,4=Utility · instruments: 0=Synth,2=Physical,5=Aurora
    public string Sub { get; init; } = "";        // format / manufacturer / folder
    public string Path { get; init; } = "";       // file/bundle path for samples/projects/presets

    // --- tree (Instruments / FX): a built-in device parent with factory-preset children ---
    public int Depth { get; init; }                            // 0 = device/plugin, 1 = preset child
    public List<BrowserItem> Children { get; } = new();        // factory presets under this device
    public bool HasChildren => Children.Count > 0;
    public bool IsExpanded { get; set; }                       // toggled by the view-model

    public string Display => string.IsNullOrEmpty(Sub) ? Name : $"{Name}  ·  {Sub}";
    public override string ToString() => Display;

    /// <summary>Stable identity for favorites/tags — keyed by device kind, never the
    /// volatile plugin catalog index. Empty for non-device rows (presets/samples/etc).</summary>
    public string LibraryKey => Kind switch
    {
        BrowserItemKind.BuiltinInstrument => $"bi:{BuiltinKind}",
        BrowserItemKind.BuiltinEffect => $"be:{BuiltinKind}",
        BrowserItemKind.BuiltinMidiEffect => $"bm:{BuiltinKind}",
        BrowserItemKind.PluginInstrument or BrowserItemKind.PluginEffect => $"pl:{Name}|{Sub}",
        _ => "",
    };
}

public sealed partial class BrowserViewModel : ObservableObject
{
    private const int MaxSamples = 2000;
    private static readonly string[] SampleExts = { ".wav", ".flac", ".mp3" };

    private readonly IPluginCatalog _catalog;
    private readonly IPresetLibrary _presets;
    private readonly IFactoryPresets _factory;
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

    public ObservableCollection<BrowserItem> Instruments { get; } = new();
    public ObservableCollection<BrowserItem> Effects { get; } = new();
    public ObservableCollection<BrowserItem> MidiEffects { get; } = new();
    public ObservableCollection<BrowserItem> Samples { get; } = new();
    public ObservableCollection<BrowserItem> Projects { get; } = new();
    public ObservableCollection<BrowserItem> Presets { get; } = new();

    public BrowserViewModel(IPluginCatalog catalog, IPresetLibrary presets, IFactoryPresets factory,
                            ISettingsService? settings = null, IBrowserLibrary? library = null)
    {
        _catalog = catalog;
        _presets = presets;
        _factory = factory;
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
        RebuildVisible(0);
        RebuildVisible(1);
        RebuildVisible(2);
    }

    // Any favorite/tag mutation → refresh device rows (markers + active filter). If the
    // filtered-on tag was deleted, drop back to the unfiltered view.
    private void OnLibraryChanged()
    {
        if (_filterMode == BrowserFilter.Tag && !Tags.Any(t => t.Id == _filterTagId))
        { _filterMode = BrowserFilter.None; _filterTagId = ""; }
        RebuildVisible(0);
        RebuildVisible(1);
        RebuildVisible(2);
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
            new() { Name = "Nota Synth", Kind = BrowserItemKind.BuiltinInstrument, BuiltinKind = 0, Sub = "simple synth" },
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
            new() { Name = "Nota Auto Shift", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 10, Sub = "pitch correction" },
            new() { Name = "Nota Beat Repeat", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 11, Sub = "glitch & repeats" },
            new() { Name = "Nota Crush", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 12, Sub = "bit crusher" },
            new() { Name = "Nota Dynamic EQ-8", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 13, Sub = "dynamic EQ" },
            new() { Name = "Nota Ceiling", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 14, Sub = "limiter" },
            new() { Name = "Nota Strata", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 15, Sub = "looper" },
            new() { Name = "Nota Audio Effect Rack", Kind = BrowserItemKind.BuiltinEffect, BuiltinKind = 5, Sub = "built-in" }
        };
        _fxTree.AddRange(fxs.OrderBy(x => x.Name).ToList());

        // Attach factory presets as children of their parent built-in device.
        foreach (var fp in _factory.All())
        {
            var tree = fp.IsMidiEffect ? _midiTree : fp.IsInstrument ? _instrTree : _fxTree;
            var parentKind = fp.IsMidiEffect ? BrowserItemKind.BuiltinMidiEffect
                : fp.IsInstrument ? BrowserItemKind.BuiltinInstrument : BrowserItemKind.BuiltinEffect;
            var parent = tree.Find(d => d.BuiltinKind == fp.BuiltinKind && d.Kind == parentKind);
            parent?.Children.Add(new BrowserItem
            {
                Name = fp.DisplayName,
                Kind = BrowserItemKind.Preset,
                Sub = "preset",
                Path = "factory:" + fp.Id,
                Depth = 1,
            });
        }
        // Preset groups start collapsed; the user expands a device to reveal its presets.
        foreach (var tree in new[] { _instrTree, _fxTree, _midiTree })
            foreach (var d in tree) d.IsExpanded = false;

        int count = _catalog.Count;
        for (int i = 0; i < count; i++)
        {
            var desc = _catalog.Description(i) ?? "";
            // "Name | Format | inst|fx | Manufacturer"
            var parts = desc.Split('|');
            string name = parts.Length > 0 ? parts[0].Trim() : desc;
            string fmt = parts.Length > 1 ? parts[1].Trim() : "";
            bool isInstrument = desc.Contains("| inst |");
            var item = new BrowserItem
            {
                Name = name,
                Sub = fmt,
                Kind = isInstrument ? BrowserItemKind.PluginInstrument : BrowserItemKind.PluginEffect,
                CatalogIndex = i,
            };
            (isInstrument ? _instrTree : _fxTree).Add(item);
        }

        RebuildVisible(0);
        RebuildVisible(1);
        RebuildVisible(2);
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
    // shows all its presets, and a device with matching presets shows just those.
    private void RebuildVisible(int tab)
    {
        var tree = tab == 2 ? _midiTree : tab == 1 ? _fxTree : _instrTree;
        var dst = tab == 2 ? MidiEffects : tab == 1 ? Effects : Instruments;
        string q = _treeQuery[tab].Trim();
        dst.Clear();
        // Favorited devices float to the top (stable: keeps alphabetical order within each group).
        var ordered = _library is null ? (IEnumerable<BrowserItem>)tree
            : tree.OrderByDescending(n => _library.IsFavorite(n.LibraryKey));
        foreach (var node in ordered)
        {
            if (!PassesLibraryFilter(node)) continue;   // header favorite/tag chip
            if (q.Length == 0)
            {
                dst.Add(node);
                if (node.IsExpanded)
                    foreach (var c in node.Children) dst.Add(c);
                continue;
            }
            bool nodeHit = Matches(node, q);
            if (nodeHit)
            {
                dst.Add(node);
                foreach (var c in node.Children) dst.Add(c);
            }
            else
            {
                var hits = node.Children.Where(c => Matches(c, q)).ToList();
                if (hits.Count > 0)
                {
                    dst.Add(node);
                    foreach (var c in hits) dst.Add(c);
                }
            }
        }
    }

    private static bool Matches(BrowserItem it, string q)
        => it.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
           || it.Sub.Contains(q, StringComparison.OrdinalIgnoreCase);

    /// <summary>Scans the configured samples folder into a navigable folder tree (M7-4a).
    /// Folders become expandable parent nodes; audio files are leaves. Only folders on a
    /// path to an audio file appear.</summary>
    public void RebuildSamples()
    {
        _sampleTree.Clear();
        Samples.Clear();
        if (_settings is null) return;
        var root = _settings.ResolvedSamplesFolder();

        // Folder nodes keyed by absolute directory path (reused while placing files).
        var folders = new Dictionary<string, BrowserItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateFilesSafe(root)
                     .Where(p => SampleExts.Contains(Path.GetExtension(p).ToLowerInvariant()))
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                     .Take(MaxSamples))
        {
            var dir = Path.GetDirectoryName(path) ?? root;
            var parentChildren = GetOrCreateFolder(root, dir, folders, out int depth);
            parentChildren.Add(new BrowserItem
            {
                Name = Path.GetFileName(path),
                Kind = BrowserItemKind.Sample,
                Sub = "",
                Path = path,
                Depth = depth,
            });
        }
        SortSampleTree(_sampleTree);
        RebuildSampleVisible();
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
                node = new BrowserItem
                {
                    Name = segments[i],
                    Kind = BrowserItemKind.Folder,
                    Sub = "",
                    Path = abs,
                    Depth = i,
                    IsExpanded = _expandedTreeKeys.Contains(abs),
                };
                folders[abs] = node;
                children.Add(node);
            }
            children = node.Children;
        }
        fileDepth = segments.Length;
        return children;
    }

    // Folders before files, each group alphabetical; recurses into subfolders.
    private static void SortSampleTree(List<BrowserItem> nodes)
    {
        nodes.Sort((a, b) =>
        {
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
    private void RebuildTreeVisible(List<BrowserItem> roots, ObservableCollection<BrowserItem> dst, string q)
    {
        dst.Clear();
        foreach (var node in roots) FlattenTree(node, q, ancestorMatched: false, dst);
    }

    // Adds `node` (and, for folders, its visible descendants) to `dst`. Returns whether the
    // node ended up visible. With a query, folders auto-expand and survive only if the folder
    // name or some descendant matches; a folder-name hit reveals its whole subtree.
    private bool FlattenTree(BrowserItem node, string q, bool ancestorMatched, ObservableCollection<BrowserItem> dst)
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
