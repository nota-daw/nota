// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Exclusive record-arm (#332): arming a track with a plain click arms that track alone, so
// picking which instrument the keyboard plays is one click; the additive form (⌘-click, or
// Ctrl-click on Windows / Linux) arms or disarms one track and leaves the others as they are.

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
}
