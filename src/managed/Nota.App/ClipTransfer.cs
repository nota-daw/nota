// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The clip clipboard shared by the Arrangement and the Session view. Each view keeps its own
// kind of copy — the arrangement's is the engine's block clipboard (a snapshot, so a cut
// survives), the session's a reference to a slot (a cut moves it on paste) — and the last
// copy or cut decides which one ⌘V takes from in either view. So clips copied on the
// timeline paste into session slots, and a slot copied in the grid pastes onto the timeline.

using Nota.Application;

namespace Nota.App;

internal static class ClipTransfer
{
    internal enum Source { None, Arrangement, Session }

    /// <summary>The view the last clip copy / cut came from.</summary>
    public static Source Last { get; private set; }

    /// <summary>The session clipboard: a slot; a cut moves it on paste.</summary>
    public static (int TrackId, int Scene, bool Cut)? Slot { get; private set; }

    public static void TookFromArrangement() => Last = Source.Arrangement;

    public static void TookSlot(int trackId, int scene, bool cut)
    {
        Slot = (trackId, scene, cut);
        Last = Source.Session;
    }

    /// <summary>After a cut slot was pasted: it now lives at its new place (or nowhere).</summary>
    public static void SlotMoved((int TrackId, int Scene)? to)
        => Slot = to is { } t ? (t.TrackId, t.Scene, false) : null;

    /// <summary>⌘V should paste the session slot.</summary>
    public static bool FromSession => Last == Source.Session && Slot is not null;

    /// <summary>⌘V should paste the arrangement's block clipboard.</summary>
    public static bool FromArrangement(IAudioEngine engine)
        => Last == Source.Arrangement && engine.ClipboardBlockCount() > 0;
}
