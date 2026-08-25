// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// JSON-on-disk implementation of the IBrowserLibrary port (contract in
// Application). Stores browser favorites + tags in NotaPaths.DataDir
// (browser-library.json), mirroring SettingsService.

using System.Text.Json;
using Nota.Application;

namespace Nota.Infrastructure;

public sealed class BrowserLibraryService : IBrowserLibrary
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;
    private Data _data = new();

    public event Action? Changed;

    // Serialised shape. Assignments: device key -> tag ids.
    private sealed class Data
    {
        public List<BrowserTag> Tags { get; set; } = new();
        public HashSet<string> Favorites { get; set; } = new();
        public Dictionary<string, List<string>> Assignments { get; set; } = new();
    }

    /// <param name="path">Override for tests; defaults to DataDir/browser-library.json.</param>
    public BrowserLibraryService(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(NotaPaths.DataDir, "browser-library.json");
        try
        {
            if (File.Exists(_path))
                _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new Data();
        }
        catch { _data = new Data(); } // corrupt/older file → empty library
    }

    public IReadOnlyList<BrowserTag> Tags => _data.Tags;

    public bool IsFavorite(string deviceKey)
        => !string.IsNullOrEmpty(deviceKey) && _data.Favorites.Contains(deviceKey);

    public void SetFavorite(string deviceKey, bool on)
    {
        if (string.IsNullOrEmpty(deviceKey)) return;
        if (on) _data.Favorites.Add(deviceKey);
        else _data.Favorites.Remove(deviceKey);
        Save();
    }

    public IReadOnlyList<string> TagIdsFor(string deviceKey)
        => !string.IsNullOrEmpty(deviceKey) && _data.Assignments.TryGetValue(deviceKey, out var ids)
            ? ids : System.Array.Empty<string>();

    public void AssignTag(string deviceKey, string tagId, bool on)
    {
        if (string.IsNullOrEmpty(deviceKey) || string.IsNullOrEmpty(tagId)) return;
        if (on)
        {
            if (!_data.Assignments.TryGetValue(deviceKey, out var ids))
                _data.Assignments[deviceKey] = ids = new List<string>();
            if (!ids.Contains(tagId)) ids.Add(tagId);
        }
        else if (_data.Assignments.TryGetValue(deviceKey, out var ids))
        {
            ids.Remove(tagId);
            if (ids.Count == 0) _data.Assignments.Remove(deviceKey);
        }
        Save();
    }

    public BrowserTag CreateTag(string title, string color)
    {
        var tag = new BrowserTag { Id = System.Guid.NewGuid().ToString("N"), Title = title, Color = color };
        _data.Tags.Add(tag);
        Save();
        return tag;
    }

    public void UpdateTag(string id, string title, string color)
    {
        var tag = _data.Tags.Find(t => t.Id == id);
        if (tag is null) return;
        tag.Title = title;
        tag.Color = color;
        Save();
    }

    public void DeleteTag(string id)
    {
        _data.Tags.RemoveAll(t => t.Id == id);
        // Scrub the id from every assignment; drop now-empty entries.
        foreach (var key in new List<string>(_data.Assignments.Keys))
        {
            _data.Assignments[key].Remove(id);
            if (_data.Assignments[key].Count == 0) _data.Assignments.Remove(key);
        }
        Save();
    }

    public void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_data, JsonOpts)); }
        catch { /* best-effort; not fatal */ }
        Changed?.Invoke();
    }
}
