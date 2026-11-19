// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A Session slot or scene launched by a mapped pad or button (#601, specs/108): SessionLaunch does
// what a click on that launch button does, through the engine alone. A clip launches and its
// release stops a Gate clip (a Trigger clip plays on); an empty slot stops its track only with its
// stop button; an armed empty slot records and a second press stops the take; a scene launches its
// row; a row past the last one, or a track that is gone, does nothing.

using Nota.Application;
using Nota.Application.Controllers;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SessionLaunchTests
{
    private const double Sr = 48000.0;
    private const int Chunk = 256;
    private const double Bar = 4.0; // launch quantum, beats

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in ClipLaunchAndRelease()) yield return r;
        foreach (var r in EmptySlots()) yield return r;
        foreach (var r in RecordAndStop()) yield return r;
        foreach (var r in Scenes()) yield return r;
        foreach (var r in MissingRowsAndTracks()) yield return r;
    }

    // Two tracks: drum has clips in rows 0 and 2; bass has clips in rows 1 and 2. Row 0 of bass and
    // row 1 of drum are empty.
    private sealed class Rig : IDisposable
    {
        public readonly NotaEngine E = new();
        public readonly int Drum, Bass;
        private readonly float[] _buf = new float[Chunk * 2];
        public Rig()
        {
            E.SetBpm(120);
            E.SetLaunchQuant(Bar);
            Drum = E.AddInstrumentTrack();
            Bass = E.AddInstrumentTrack();
            while (E.SceneCount < 4) E.AddScene();
            E.AddSessionMidiClip(Drum, 0, 4.0);
            E.AddSessionMidiClip(Drum, 2, 4.0);
            E.AddSessionMidiClip(Bass, 1, 4.0);
            E.AddSessionMidiClip(Bass, 2, 4.0);
            E.SetSessionNotes(Drum, 0, new[] { new NotaNote(36, 0.0, 0.5, 0.9f) });
            E.SetSessionNotes(Bass, 1, new[] { new NotaNote(40, 0.0, 1.0, 0.9f) });
            E.StopTransport();
            E.Seek(0);
            E.RenderOffline(_buf, 64, Sr);
        }
        public void Block() { E.RenderOffline(_buf, Chunk, Sr); E.Poll(); }
        // Render until the transport reaches `beat`.
        public void To(double beat)
        {
            for (int g = 0; g < 200000 && E.PositionBeats < beat; g++) Block();
        }
        public void Dispose() => E.Dispose();
    }

    private static void Mode(NotaEngine e, int track, int scene, SessionLaunchMode mode)
    {
        e.TryGetSessionClipProps(track, scene, out var p);
        p.LaunchMode = (int)mode;
        e.SetSessionClipProps(track, scene, p);
    }

    private static IEnumerable<(bool, string)> ClipLaunchAndRelease()
    {
        using var r = new Rig();
        var e = r.E;
        SessionLaunch.Press(e, r.Drum, 0, 100 / 127f);
        yield return (e.SessionSlotState(r.Drum, 0) == 2, $"session launch: a pad queues its clip (state {e.SessionSlotState(r.Drum, 0)})");
        r.To(0.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 0, $"session launch: the clip plays (slot {e.SessionPlayingSlot(r.Drum)})");
        SessionLaunch.Release(e, r.Drum, 0);
        r.To(1.0);
        yield return (e.SessionPlayingSlot(r.Drum) == 0, "session launch: a Trigger clip plays on after the pad is let go");

        Mode(e, r.Bass, 1, SessionLaunchMode.Gate);
        SessionLaunch.Press(e, r.Bass, 1);
        r.To(Bar + 0.5);
        yield return (e.SessionPlayingSlot(r.Bass) == 1, $"session launch: a Gate clip plays while its pad is held (slot {e.SessionPlayingSlot(r.Bass)})");
        SessionLaunch.Release(e, r.Bass, 1);
        r.To(2 * Bar + 0.5);   // the stop waits for the launch quantum, as the on-screen button's does
        yield return (e.SessionPlayingSlot(r.Bass) == -1, $"session launch: letting go of the pad stops the Gate clip (slot {e.SessionPlayingSlot(r.Bass)})");
        yield return (e.SessionPlayingSlot(r.Drum) == 0, "session launch: the other track plays on");
    }

    private static IEnumerable<(bool, string)> EmptySlots()
    {
        using var r = new Rig();
        var e = r.E;
        e.LaunchSlot(r.Drum, 0);
        r.To(0.5);
        e.SetSessionSlotStopButton(r.Drum, 1, false);
        SessionLaunch.Press(e, r.Drum, 1);
        r.To(Bar + 0.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 0,
            $"session launch: an empty slot without its stop button leaves the track playing (slot {e.SessionPlayingSlot(r.Drum)})");
        e.SetSessionSlotStopButton(r.Drum, 1, true);
        SessionLaunch.Press(e, r.Drum, 1);
        r.To(2 * Bar + 0.5);
        yield return (e.SessionPlayingSlot(r.Drum) == -1,
            $"session launch: an empty slot with its stop button stops the track (slot {e.SessionPlayingSlot(r.Drum)})");
        yield return (e.SessionSlotState(r.Drum, 1) == 0, "session launch: the empty slot stays empty");
    }

    private static IEnumerable<(bool, string)> RecordAndStop()
    {
        using var r = new Rig();
        var e = r.E;
        e.SetLaunchQuant(0.0);
        e.LaunchSlot(r.Drum, 0);   // the transport rolls: the take starts without a count-in
        r.To(0.5);
        e.SetTrackArmed(r.Bass, true);
        SessionLaunch.Press(e, r.Bass, 0);
        r.To(1.0);
        bool rec = e.TryGetSessionRecordTarget(out int rt, out int rs, out _);
        yield return (rec && rt == r.Bass && rs == 0,
            $"session launch: an armed track's empty slot records (target {(rec ? $"{rt}/{rs}" : "none")})");
        e.NoteOn(60, 0.8f);
        r.To(1.5);
        e.NoteOff(60);
        r.To(2.0);
        SessionLaunch.Press(e, r.Bass, 0);
        r.To(2.5);
        yield return (!e.TryGetSessionRecordTarget(out _, out _, out _), "session launch: a second press stops the take");
        yield return (e.SessionSlotState(r.Bass, 0) != 0, $"session launch: the take is kept in the slot (state {e.SessionSlotState(r.Bass, 0)})");
        yield return (e.SessionPlayingSlot(r.Drum) == 0, "session launch: recording leaves the other track playing");
    }

    private static IEnumerable<(bool, string)> Scenes()
    {
        using var r = new Rig();
        var e = r.E;
        e.LaunchSlot(r.Drum, 0);
        r.To(0.5);
        SessionLaunch.Scene(e, 1);   // bass row 1, drum's empty slot stops it
        yield return (e.SessionSceneQueued(1), "session launch: a scene button queues its row");
        r.To(Bar + 0.5);
        yield return (e.SessionPlayingSlot(r.Bass) == 1 && e.SessionPlayingSlot(r.Drum) == -1,
            $"session launch: the row starts (bass {e.SessionPlayingSlot(r.Bass)}, drum {e.SessionPlayingSlot(r.Drum)})");
        SessionLaunch.Scene(e, 2);
        r.To(2 * Bar + 0.5);
        yield return (e.SessionPlayingSlot(r.Bass) == 2 && e.SessionPlayingSlot(r.Drum) == 2,
            $"session launch: the next scene button starts its whole row (bass {e.SessionPlayingSlot(r.Bass)}, drum {e.SessionPlayingSlot(r.Drum)})");
    }

    private static IEnumerable<(bool, string)> MissingRowsAndTracks()
    {
        using var r = new Rig();
        var e = r.E;
        e.LaunchSlot(r.Drum, 0);
        e.LaunchSlot(r.Bass, 1);
        r.To(0.5);
        int past = e.SceneCount;
        SessionLaunch.Press(e, r.Drum, past);
        SessionLaunch.Press(e, r.Drum, -1);
        SessionLaunch.Scene(e, past);
        SessionLaunch.Release(e, r.Drum, past);
        r.To(Bar + 0.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 0 && e.SessionPlayingSlot(r.Bass) == 1 && !e.SessionSceneQueued(past - 1),
            $"session launch: a row past the last one does nothing (drum {e.SessionPlayingSlot(r.Drum)}, bass {e.SessionPlayingSlot(r.Bass)})");
        int gone = Math.Max(r.Drum, r.Bass) + 1000;
        SessionLaunch.Press(e, gone, 0);
        SessionLaunch.Release(e, gone, 0);
        r.To(2 * Bar + 0.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 0 && e.SessionPlayingSlot(r.Bass) == 1,
            "session launch: a track that is gone does nothing");
    }
}
