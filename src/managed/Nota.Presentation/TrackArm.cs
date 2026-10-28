// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Exclusive record-arm (#332): arming a track with a plain click arms that track alone, so
// picking which instrument the keyboard plays is one click; the additive form (⌘-click, or
// Ctrl-click on Windows / Linux) arms or disarms one track and leaves the others as they are.
// With arm-on-select on (Transport ▸ Auto-arm selected track, off by default), selecting an
// instrument track arms it the same way, while the arms are still the one the selection picked.

using Nota.Application;

namespace Nota.Presentation;

public static class TrackArm
{
    /// <summary>Arms or disarms <paramref name="trackId"/>. Arming without
    /// <paramref name="additive"/> disarms every other track first — except while recording,
    /// where disarming would end another track's take. Returns true when other tracks changed.</summary>
    public static bool Set(IAudioEngine engine, int trackId, bool on, bool additive)
    {
        bool others = false;
        if (on && !additive && !engine.IsRecording)
            for (int i = 0; i < engine.TrackCount; i++)
                if (engine.TryGetTrackInfo(i, out var t) && t.Id != trackId && t.Armed != 0)
                {
                    engine.SetTrackArmed(t.Id, false);
                    others = true;
                }
        engine.SetTrackArmed(trackId, on);
        return others;
    }

    /// <summary>Arm-on-select: selecting an instrument track arms it alone, so the keyboard
    /// plays the track you're on. The arms are left as they are while recording, when the
    /// track is already armed, and when they're a set built by hand — several tracks, or an
    /// audio track. Returns true when any arm changed.</summary>
    public static bool FollowSelection(IAudioEngine engine, int trackId)
    {
        if (engine.IsRecording) return false;
        bool isInstrument = false;
        int armedOthers = 0;
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var t)) continue;
            bool inst = t.IsInstrument && !t.IsReturn && !t.IsGroup;
            if (t.Id == trackId)
            {
                if (t.Armed != 0) return false;
                isInstrument = inst;
            }
            else if (t.Armed != 0)
            {
                if (!inst) return false;
                armedOthers++;
            }
        }
        if (!isInstrument || armedOthers > 1) return false;
        Set(engine, trackId, true, additive: false);
        return true;
    }
}
