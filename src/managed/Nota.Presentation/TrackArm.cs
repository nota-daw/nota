// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Exclusive record-arm (#332): arming a track with a plain click arms that track alone, so
// picking which instrument the keyboard plays is one click; the additive form (⌘-click, or
// Ctrl-click on Windows / Linux) arms or disarms one track and leaves the others as they are.
// With arm-on-select on (Transport ▸ Auto-arm selected track, off by default), selecting an
// instrument track arms it the same way, while the arms are still the one the selection picked.
// A linked group (specs/076) arms as one track: arming or disarming a member does the same to
// every member of its link set, unless the click bypasses the link (⌥-click).

using System.Collections.Generic;
using Nota.Application;
using Nota.Application.Editing;

namespace Nota.Presentation;

public static class TrackArm
{
    /// <summary>Arms or disarms <paramref name="trackId"/> — and, when <paramref name="linked"/>,
    /// the other members of its link set. Arming without <paramref name="additive"/> disarms every
    /// track outside the set first — except while recording, where disarming would end another
    /// track's take. Returns true when tracks other than <paramref name="trackId"/> changed.</summary>
    public static bool Set(IAudioEngine engine, int trackId, bool on, bool additive, bool linked = true)
    {
        var set = new HashSet<int> { trackId };
        if (linked) foreach (var m in Links(engine).Members(trackId)) set.Add(m);
        bool others = false;
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var t) || t.Id == trackId) continue;
            if (set.Contains(t.Id))
            {
                if ((t.Armed != 0) != on) { engine.SetTrackArmed(t.Id, on); others = true; }
            }
            else if (on && !additive && !engine.IsRecording && t.Armed != 0)
            {
                engine.SetTrackArmed(t.Id, false);
                others = true;
            }
        }
        engine.SetTrackArmed(trackId, on);
        return others;
    }

    /// <summary>Arm-on-select: selecting an instrument track arms it alone, so the keyboard
    /// plays the track you're on. The arms are left as they are while recording, when the
    /// track is already armed, and when they're a set built by hand — several tracks, or an
    /// audio track. A linked set counts as one track. Returns true when any arm changed.</summary>
    public static bool FollowSelection(IAudioEngine engine, int trackId)
    {
        if (engine.IsRecording) return false;
        var links = Links(engine);
        var own = new HashSet<int>(links.Members(trackId)) { trackId };
        bool isInstrument = false;
        var armedOthers = new HashSet<int>();   // link roots (or track ids) of armed tracks outside the set
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var t)) continue;
            bool inst = t.IsInstrument && !t.IsReturn && !t.IsGroup;
            if (t.Id == trackId)
            {
                if (t.Armed != 0) return false;
                isInstrument = inst;
            }
            else if (own.Contains(t.Id)) continue;
            else if (t.Armed != 0)
            {
                if (!inst) return false;
                int root = links.RootOf(t.Id);
                armedOthers.Add(root >= 0 ? -root : t.Id);   // a linked set is one track
            }
        }
        if (!isInstrument || armedOthers.Count > 1) return false;
        Set(engine, trackId, true, additive: false);
        return true;
    }

    /// <summary>The link sets as the engine has them now.</summary>
    public static GroupLink Links(IAudioEngine engine)
    {
        var tracks = new List<LinkTrack>(engine.TrackCount);
        bool anyGroup = false;
        for (int i = 0; i < engine.TrackCount; i++)
            if (engine.TryGetTrackInfo(i, out var t))
            {
                anyGroup |= t.IsGroup;
                tracks.Add(new LinkTrack(t.Id, t.GroupId, t.IsGroup, t.IsReturn, t.IsGroup && engine.GetTrackEditLink(t.Id)));
            }
        return anyGroup ? GroupLink.Build(tracks) : GroupLink.None;
    }
}
