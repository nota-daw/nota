// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What launching the selected Session slot does (specs/112, #364): the same thing a click on
// the slot does. Return and a controller's launch button go through here, so the keyboard can
// record into an armed track's empty slot and end a take, as the mouse can. Kept pure so the
// smoke test checks the rule without a view.

namespace Nota.Application.Editing;

/// <summary>What launching a selected track slot does.</summary>
public enum SlotLaunchAction
{
    /// <summary>Today's launch: a clip starts; an empty slot stops its track (or nothing, without a stop button).</summary>
    Launch,
    /// <summary>An empty slot on an armed track: recording starts in it.</summary>
    Record,
    /// <summary>The slot is recording: the take ends.</summary>
    StopRecording,
}

public static class SessionLaunchKey
{
    /// <summary>The action for a track's slot. A recording slot wins over everything; an empty
    /// slot records only on an armed track.</summary>
    public static SlotLaunchAction Resolve(bool filled, bool armed, bool recording) =>
        recording ? SlotLaunchAction.StopRecording
        : !filled && armed ? SlotLaunchAction.Record
        : SlotLaunchAction.Launch;
}
