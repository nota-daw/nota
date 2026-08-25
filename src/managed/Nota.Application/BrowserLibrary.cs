// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Browser library metadata: user favorites + tags for instruments / effects /
// MIDI effects. Library-wide (not per-project), so the JSON-on-disk impl
// (BrowserLibraryService in Infrastructure) persists next to settings.json.
// Devices are keyed by a stable identity string (see BrowserItem.LibraryKey),
// never by the volatile plugin catalog index.

namespace Nota.Application;

/// <summary>A user-defined browser tag: a title and a display colour (hex "#RRGGBB").</summary>
public sealed class BrowserTag
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Color { get; set; }
}

public interface IBrowserLibrary
{
    /// <summary>All tags in creation order.</summary>
    IReadOnlyList<BrowserTag> Tags { get; }

    bool IsFavorite(string deviceKey);
    void SetFavorite(string deviceKey, bool on);

    /// <summary>Tag ids assigned to a device (empty if none).</summary>
    IReadOnlyList<string> TagIdsFor(string deviceKey);
    void AssignTag(string deviceKey, string tagId, bool on);

    BrowserTag CreateTag(string title, string color);
    void UpdateTag(string id, string title, string color);
    /// <summary>Deletes a tag and scrubs it from every device assignment.</summary>
    void DeleteTag(string id);

    void Save();

    /// <summary>Fired after any mutation persists, so views can refresh markers/chips.</summary>
    event Action? Changed;
}
