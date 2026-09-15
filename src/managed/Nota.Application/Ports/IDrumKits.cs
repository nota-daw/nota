// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>A shipped Drum Rack kit, described for the browser.</summary>
public readonly record struct DrumKitInfo(
    string Id,           // stable; the browser row's Path is "kit:" + Id
    string Name,
    string Blurb,        // one-line description for the row
    int PadCount);

/// <summary>The factory drum kits. Their samples are synthesized on the user's machine
/// rather than shipped as audio, so a kit may need rendering before it can be loaded —
/// <see cref="EnsureRendered"/> does that and is safe to call repeatedly.</summary>
public interface IDrumKits
{
    /// <summary>All shipped kits, in catalog order.</summary>
    IReadOnlyList<DrumKitInfo> All();

    /// <summary>Folder holding the rendered one-shots of every kit.</summary>
    string Root { get; }

    /// <summary>Folder of one kit's rendered one-shots ("" for an unknown id).</summary>
    string FolderOf(string id);

    /// <summary>True when the kit's samples are on disk and current.</summary>
    bool IsRendered(string id);

    /// <summary>Renders the kit (or every kit when <paramref name="id"/> is empty) if it
    /// is missing or out of date. Returns the number of samples written.</summary>
    int EnsureRendered(string id = "");

    /// <summary>Creates a Drum Rack track loaded with the kit. Returns the new track id,
    /// or -1; <paramref name="warning"/> is "" on success.</summary>
    int CreateTrack(IAudioEngine engine, string id, out string warning);

    /// <summary>Replaces the pads of an existing Drum Rack track with the kit.</summary>
    bool LoadInto(IAudioEngine engine, int trackId, string id, out string warning);
}
