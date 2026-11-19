// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A Session slot or scene launch button pressed from outside the grid (#601: a pad or a button
// mapped with MIDI Learn). It does what a click on that button does, through the engine alone, so
// a mapping works whether or not the Session view is showing:
//   a clip          launches — with the pad's velocity, scaled by the clip's velocity amount —
//                   and its release stops a Gate / Repeat clip, as letting go of the button does;
//   a take          recording (or waiting for its boundary) in the slot stops recording;
//   an empty slot   records on an armed track, else stops the track when the slot has its stop
//                   button, else does nothing;
//   a scene         launches its row.
// A slot or scene past the last row does nothing: the engine would read a direct launch of a row
// that doesn't exist as "stop the track".

namespace Nota.Application.Controllers;

public static class SessionLaunch
{
    /// <summary>The slot's launch button went down, with <paramref name="velocity"/> 0..1.</summary>
    public static void Press(IAudioEngine engine, int trackId, int scene, float velocity = 1f)
    {
        if (!HasRow(engine, scene) || !TrackExists(engine, trackId, out bool armed)) return;
        int state = engine.SessionSlotState(trackId, scene);
        if (state is 4 or 5 || engine.TryGetSessionRecordTarget(out int rt, out int rs, out _) && rt == trackId && rs == scene)
        {
            engine.StopSessionRecordSlot(trackId);
            return;
        }
        if (state != 0) { engine.LaunchSlotVelocity(trackId, scene, Math.Clamp(velocity, 0f, 1f)); return; }
        if (armed) engine.RecordSessionSlot(trackId, scene);
        else if (engine.GetSessionSlotStopButton(trackId, scene)) engine.StopSlot(trackId);
    }

    /// <summary>The slot's launch button came up: a Gate / Repeat clip stops (the engine ignores the
    /// rest).</summary>
    public static void Release(IAudioEngine engine, int trackId, int scene)
    {
        if (HasRow(engine, scene)) engine.ReleaseSlot(trackId, scene);
    }

    /// <summary>The scene's launch button: every slot of the row launches (empty ones stop their track
    /// unless their stop button was removed).</summary>
    public static void Scene(IAudioEngine engine, int scene)
    {
        if (HasRow(engine, scene)) engine.LaunchScene(scene);
    }

    private static bool HasRow(IAudioEngine engine, int scene) => scene >= 0 && scene < engine.SceneCount;

    private static bool TrackExists(IAudioEngine engine, int trackId, out bool armed)
    {
        armed = false;
        int n = engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (engine.TryGetTrackInfo(i, out var ti) && ti.Id == trackId)
            {
                armed = ti.Armed != 0;
                return true;
            }
        return false;
    }
}
