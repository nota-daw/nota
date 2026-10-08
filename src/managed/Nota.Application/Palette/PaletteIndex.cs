// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-23): the index every keystroke searches. It is filled per source —
// actions, tracks, devices, presets, Modular nodes (one provider each, CP-24) — so a plug-in
// rescan, a saved preset or a renamed track replaces only its own slice. Items are prepared
// (normalised, word starts, term masks) where they are built, which may be a worker thread;
// swapping a slice in is cheap and happens on the UI thread. A palette opened before the
// index is complete searches what is there and shows the progress.

namespace Nota.Application.Palette;

/// <summary>One kind of palette entry and where it comes from (CP-24). A new kind of item
/// is a new provider.</summary>
public interface IPaletteProvider
{
    /// <summary>The slice this provider fills ("actions", "tracks", "devices", "presets", "modular").</summary>
    string Source { get; }
    /// <summary>Builds the slice's items. May run on a worker thread; must not touch the UI.</summary>
    IReadOnlyList<PaletteItem> Build();
}

public sealed class PaletteIndex
{
    private readonly Dictionary<string, PaletteItem[]> _slices = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();
    private PaletteItem[] _all = [];
    private Dictionary<string, PaletteItem> _byId = new(StringComparer.Ordinal);
    private string _progressLabel = "";
    private float _progress = 1f;

    /// <summary>Every item, slices in the order they were first set.</summary>
    public IReadOnlyList<PaletteItem> Items => _all;
    public int Count => _all.Length;
    /// <summary>Bumped on every change, so a view can tell its results went stale.</summary>
    public int Version { get; private set; }

    public event Action? Changed;

    /// <summary>Prepares items for search — call where they are built (any thread).</summary>
    public static PaletteItem[] Prepare(IEnumerable<PaletteItem> items)
    {
        var arr = items as PaletteItem[] ?? items.ToArray();
        foreach (var it in arr) if (!it.Prepared) it.Prepare();
        return arr;
    }

    /// <summary>Replaces one source's slice. Call on the thread that searches (the UI thread).</summary>
    public void Set(string source, IReadOnlyList<PaletteItem> items)
    {
        var arr = Prepare(items);
        if (!_slices.ContainsKey(source)) _order.Add(source);
        _slices[source] = arr;
        int n = 0;
        foreach (var s in _order) n += _slices[s].Length;
        var all = new PaletteItem[n];
        var byId = new Dictionary<string, PaletteItem>(n, StringComparer.Ordinal);
        int k = 0;
        foreach (var s in _order)
            foreach (var it in _slices[s]) { all[k++] = it; byId[it.Id] = it; }
        _all = all;
        _byId = byId;
        Version++;
        Changed?.Invoke();
    }

    public IReadOnlyList<PaletteItem> Slice(string source) => _slices.GetValueOrDefault(source) ?? [];

    public PaletteItem? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Background indexing state for the footer ("indexing presets… 62 %").</summary>
    public void ReportProgress(string label, float fraction)
    {
        _progressLabel = label;
        _progress = Math.Clamp(fraction, 0, 1);
        Changed?.Invoke();
    }

    public bool IsIndexing => _progress < 1f;

    /// <summary>The footer status: the progress while indexing, else the item count.</summary>
    public string Status => IsIndexing
        ? $"indexing {_progressLabel}… {(int)Math.Round(_progress * 100)} %"
        : Count.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", "\u2009") + (Count == 1 ? " item" : " items");
}
