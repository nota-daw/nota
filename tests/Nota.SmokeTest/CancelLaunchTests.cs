// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Cancelling a queued Session launch before its quantized start (#480, specs/104). A cancelled
// slot never starts and the clip playing on its track plays on, unbroken; a cancelled scene takes
// back everything its launch queued — the clips it would start and the stops of tracks whose slot
// in that row is empty — together with its follow action and tempo change, and the scene follow
// that ran before it carries on. A cancel and a boundary in the same block never split a scene. A
// second launch keeps a launch queued; Stop and Stop All still stop.

using System.Threading;
using System.Threading.Tasks;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class CancelLaunchTests
{
    private const double Sr = 48000.0;
    private const int Chunk = 256;
    private const double Bar = 4.0; // launch quantum, beats

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in SlotCancel()) yield return r;
        foreach (var r in SceneCancelKeepsDrum()) yield return r;
        foreach (var r in BoundaryRace()) yield return r;
        foreach (var r in NewerLaunchSurvives()) yield return r;
        foreach (var r in LastSlotCancelledDropsScene()) yield return r;
        foreach (var r in FollowComesBack()) yield return r;
        foreach (var r in FollowLaunchedSceneCancelled()) yield return r;
        foreach (var r in SecondPressAndStop()) yield return r;
        foreach (var r in StopAllDropsSceneTempo()) yield return r;
        foreach (var r in RepeatCancelled()) yield return r;
        // Review of nota-daw/nota#677.
        foreach (var r in BigGroupAndTwoRows()) yield return r;
        foreach (var r in PartlyStartedSceneIsStarted()) yield return r;
        foreach (var r in DueFollowComesBack()) yield return r;
        foreach (var r in StartedBeforePollKeepsItsFollow()) yield return r;
        foreach (var r in StopsOnlyRowAndRestart()) yield return r;
        foreach (var r in RelaunchAfterCancelSurvives()) yield return r;
        foreach (var r in ClipFollowKeepsSceneStarted()) yield return r;
        foreach (var r in CancelRacesTheBoundary()) yield return r;
        foreach (var r in WaitingClipOfStartedSceneCancels()) yield return r;
        foreach (var r in RowInsertKeepsTheCancel()) yield return r;
    }

    // Two tracks: drum plays 4-beat loops in rows 0 and 2; bass has clips in rows 1, 2 and 3.
    // Row 1 = bass + an empty drum slot (with its stop button): the issue's "bass scene".
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
            E.AddSessionMidiClip(Drum, 0, 4.0);
            E.AddSessionMidiClip(Drum, 2, 4.0);
            E.AddSessionMidiClip(Bass, 1, 4.0);
            E.AddSessionMidiClip(Bass, 2, 4.0);
            E.AddSessionMidiClip(Bass, 3, 4.0);
            E.SetSessionNotes(Drum, 0, new[] { new NotaNote(36, 0.0, 0.5, 0.9f) });
            E.SetSessionNotes(Bass, 1, new[] { new NotaNote(40, 0.0, 1.0, 0.9f) });
            E.StopTransport();
            E.Seek(0);
            E.RenderOffline(_buf, 64, Sr);
        }
        public double BlockBeats => Chunk / (Sr * 60.0 / E.Bpm);
        public void Block() { E.RenderOffline(_buf, Chunk, Sr); E.Poll(); }
        public void BlockNoPoll() => E.RenderOffline(_buf, Chunk, Sr);
        // Render until the transport reaches `beat`.
        public void To(double beat)
        {
            for (int g = 0; g < 200000 && E.PositionBeats < beat; g++) Block();
        }
        // Render up to the last block that ends before `beat` (the next block holds it).
        public void JustBefore(double beat)
        {
            for (int g = 0; g < 200000 && E.PositionBeats + BlockBeats < beat - 1e-9; g++) Block();
        }
        public void Dispose() => E.Dispose();
    }

    private static IEnumerable<(bool, string)> SlotCancel()
    {
        using var r = new Rig();
        var e = r.E;
        yield return (!e.CancelSlotLaunch(r.Drum, 0), "slot cancel: nothing queued yet → false");
        e.LaunchSlot(r.Drum, 0);
        r.To(5.0);
        yield return (e.SessionPlayingSlot(r.Drum) == 0, "slot cancel: drum plays");
        // A clip with its own one-beat quantum, queued on the same track: it would start at beat 6.
        var p = Props(e, r.Drum, 2); p.QuantBeats = 1.0; e.SetSessionClipProps(r.Drum, 2, p);
        e.LaunchSlot(r.Drum, 2);
        yield return (e.SessionSlotState(r.Drum, 2) == 2, "slot cancel: the other clip is queued");
        yield return (e.CancelSlotLaunch(r.Drum, 2), "slot cancel: cancel reports a queued launch");
        r.Block();
        yield return (e.SessionSlotState(r.Drum, 2) == 1, $"slot cancel: no longer queued (state {e.SessionSlotState(r.Drum, 2)})");
        r.To(6.5);
        double pos = e.SessionSlotPosition(r.Drum);
        yield return (e.SessionPlayingSlot(r.Drum) == 0 && Math.Abs(pos - 2.5) < 0.05,
            $"slot cancel: the drum plays on through the boundary, unbroken (slot {e.SessionPlayingSlot(r.Drum)}, at {pos:F2}, want 2.50)");
        yield return (!e.CancelSlotLaunch(r.Drum, 2), "slot cancel: a second cancel finds nothing");
        e.LaunchSlot(r.Drum, 2);
        r.To(7.2);
        yield return (e.SessionPlayingSlot(r.Drum) == 2, "slot cancel: launching it again later works as ever");
        yield return (!e.CancelSlotLaunch(r.Drum, 2), "slot cancel: a started launch cannot be cancelled");
    }

    // The issue's example: drum plays, the bass scene is queued for the next bar, then cancelled.
    private static IEnumerable<(bool, string)> SceneCancelKeepsDrum()
    {
        using var r = new Rig();
        var e = r.E;
        e.SetSceneProps(1, new NotaSceneProps { Color = -1, Tempo = 140, Follow = 1, FollowBeats = 4 });
        e.LaunchScene(0);
        r.To(5.0);
        e.LaunchScene(1);
        yield return (e.SessionSlotState(r.Bass, 1) == 2, "scene cancel: the bass scene is queued");
        yield return (e.CancelSceneLaunch(1), "scene cancel: cancel reports a queued scene");
        r.Block();
        yield return (e.SessionSlotState(r.Bass, 1) == 1, "scene cancel: the bass slot is no longer queued");
        r.To(8.5);
        double pos = e.SessionSlotPosition(r.Drum);
        yield return (e.SessionPlayingSlot(r.Drum) == 0 && e.SessionPlayingSlot(r.Bass) == -1,
            $"scene cancel: the drum keeps playing, the bass never starts (drum {e.SessionPlayingSlot(r.Drum)}, bass {e.SessionPlayingSlot(r.Bass)})");
        yield return (Math.Abs(pos - 0.5) < 0.05, $"scene cancel: the drum's loop runs on unbroken ({pos:F2})");
        r.To(8.5 + 16 * Bar);
        yield return (e.SessionPlayingSlot(r.Drum) == 0 && e.SessionPlayingSlot(r.Bass) == -1,
            $"scene cancel: its follow action never fires over 16 bars (drum {e.SessionPlayingSlot(r.Drum)}, bass {e.SessionPlayingSlot(r.Bass)})");
        yield return (!e.TakeSceneTempoChange(out _, out _, out _) && Math.Abs(e.Bpm - 120) < 1e-9,
            $"scene cancel: its tempo change never applies ({e.Bpm})");
        yield return (!e.CancelSceneLaunch(1), "scene cancel: nothing left to cancel");
    }

    // A cancel drained in the block that holds the boundary withdraws every track; one block later
    // every track has started. Never a mix.
    private static IEnumerable<(bool, string)> BoundaryRace()
    {
        foreach (bool late in new[] { false, true })
        {
            using var r = new Rig();
            var e = r.E;
            e.LaunchScene(0);
            r.To(5.0);
            e.LaunchScene(1);
            r.JustBefore(8.0);
            if (late) r.Block(); // the boundary block has rendered: the scene started
            bool queued = e.CancelSceneLaunch(1);
            r.Block();
            int drum = e.SessionPlayingSlot(r.Drum), bass = e.SessionPlayingSlot(r.Bass);
            if (!late)
                yield return (queued && drum == 0 && bass == -1,
                    $"boundary race: cancelled in the boundary block → all withdrawn (drum {drum}, bass {bass})");
            else
                yield return (!queued && drum == -1 && bass == 1,
                    $"boundary race: one block late → all started (drum {drum}, bass {bass}, cancel {queued})");
        }
    }

    // A newer launch on one of the scene's tracks is not the scene's to withdraw.
    private static IEnumerable<(bool, string)> NewerLaunchSurvives()
    {
        using var r = new Rig();
        var e = r.E;
        e.LaunchScene(0);
        r.To(5.0);
        e.LaunchScene(1);       // bass row 1, drum stop
        e.LaunchSlot(r.Drum, 2); // the drum's request is now a launch of row 2
        e.CancelSceneLaunch(1);
        r.To(8.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 2 && e.SessionPlayingSlot(r.Bass) == -1,
            $"newer launch: survives the scene cancel (drum {e.SessionPlayingSlot(r.Drum)}, bass {e.SessionPlayingSlot(r.Bass)})");
    }

    // A scene whose only request is cancelled by hand counts as cancelled: no tempo change.
    private static IEnumerable<(bool, string)> LastSlotCancelledDropsScene()
    {
        using var r = new Rig();
        var e = r.E;
        e.SetSessionSlotStopButton(r.Drum, 3, false); // row 3 queues only the bass
        e.SetSceneProps(3, new NotaSceneProps { Color = -1, Tempo = 150 });
        e.LaunchScene(0);
        r.To(5.0);
        e.LaunchScene(3);
        yield return (e.CancelSlotLaunch(r.Bass, 3), "last slot: cancelled by hand");
        r.To(8.5);
        yield return (e.SessionPlayingSlot(r.Bass) == -1 && e.SessionPlayingSlot(r.Drum) == 0
                      && !e.TakeSceneTempoChange(out _, out _, out _) && Math.Abs(e.Bpm - 120) < 1e-9,
            $"last slot: the scene's tempo change is dropped with it ({e.Bpm})");
    }

    // The scene follow that ran before a cancelled launch carries on.
    private static IEnumerable<(bool, string)> FollowComesBack()
    {
        using var r = new Rig();
        var e = r.E;
        e.SetSceneProps(0, new NotaSceneProps { Color = -1, Follow = 1, FollowBeats = 16 }); // → row 1
        e.LaunchScene(0);
        r.To(5.0);
        e.LaunchScene(2);
        e.CancelSceneLaunch(2);
        r.To(12.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 0 && e.SessionPlayingSlot(r.Bass) == -1,
            "follow comes back: row 0 still playing before its follow time");
        r.To(16.5);
        yield return (e.SessionPlayingSlot(r.Bass) == 1 && e.SessionPlayingSlot(r.Drum) == -1,
            $"follow comes back: row 0's follow still launches row 1 on time (drum {e.SessionPlayingSlot(r.Drum)}, bass {e.SessionPlayingSlot(r.Bass)})");
    }

    // A scene a follow action launched, cancelled: follow ends, the outgoing row plays on.
    private static IEnumerable<(bool, string)> FollowLaunchedSceneCancelled()
    {
        using var r = new Rig();
        var e = r.E;
        e.SetSceneProps(0, new NotaSceneProps { Color = -1, Follow = 1, FollowBeats = 8 });
        e.LaunchScene(0);
        r.To(5.0); // the follow (lead one quantum) has queued row 1 for beat 8
        yield return (e.SessionSlotState(r.Bass, 1) == 2, "follow-launched: row 1 queued by the follow action");
        yield return (e.CancelSceneLaunch(1), "follow-launched: cancelled");
        r.To(8.5 + 8 * Bar);
        yield return (e.SessionPlayingSlot(r.Drum) == 0 && e.SessionPlayingSlot(r.Bass) == -1,
            $"follow-launched: scene follow ends, row 0 plays on (drum {e.SessionPlayingSlot(r.Drum)}, bass {e.SessionPlayingSlot(r.Bass)})");
    }

    private static IEnumerable<(bool, string)> SecondPressAndStop()
    {
        using var r = new Rig();
        var e = r.E;
        e.LaunchSlot(r.Drum, 0);
        r.To(5.0);
        e.LaunchSlot(r.Drum, 2);
        e.LaunchSlot(r.Drum, 2); // a second press
        r.Block();
        yield return (e.SessionSlotState(r.Drum, 2) == 2, "second press: the launch stays queued");
        r.To(8.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 2, "second press: it starts at the boundary");
        e.LaunchSlot(r.Drum, 0);
        e.StopSlot(r.Drum);
        r.Block();
        yield return (e.SessionSlotState(r.Drum, 0) == 1 && e.SessionPlayingSlot(r.Drum) == 2,
            "stop: withdraws the queued launch; the playing clip plays to the boundary");
        r.To(12.5);
        yield return (e.SessionPlayingSlot(r.Drum) == -1, "stop: the track stops at the boundary");
    }

    private static IEnumerable<(bool, string)> StopAllDropsSceneTempo()
    {
        using var r = new Rig();
        var e = r.E;
        e.SetSceneProps(1, new NotaSceneProps { Color = -1, Tempo = 140, Follow = 1, FollowBeats = 4 });
        e.LaunchScene(0);
        r.To(5.0);
        e.LaunchScene(1);
        e.StopAllSession();
        r.To(8.5 + 4 * Bar);
        yield return (e.SessionPlayingSlot(r.Drum) == -1 && e.SessionPlayingSlot(r.Bass) == -1,
            "stop all: everything stops, the queued scene never starts");
        yield return (!e.TakeSceneTempoChange(out _, out _, out _) && Math.Abs(e.Bpm - 120) < 1e-9,
            $"stop all: the queued scene's tempo change is dropped ({e.Bpm})");

        // Back to Arrangement stops every Session clip the same way: a waiting scene's tempo goes too.
        e.LaunchScene(0);
        r.To(e.PositionBeats + 1.0);
        e.LaunchScene(1);
        e.BackToArrangement();
        r.To(e.PositionBeats + 2 * Bar);
        yield return (e.SessionPlayingSlot(r.Bass) == -1 && !e.TakeSceneTempoChange(out _, out _, out _) && Math.Abs(e.Bpm - 120) < 1e-9,
            $"back to arrangement: the queued scene never starts, its tempo change is dropped ({e.Bpm})");
    }

    // A held Repeat clip re-queues itself each quantum; cancelling ends the repeat, the clip plays on.
    private static IEnumerable<(bool, string)> RepeatCancelled()
    {
        using var r = new Rig();
        var e = r.E;
        var p = Props(e, r.Drum, 0); p.LaunchMode = (int)SessionLaunchMode.Repeat; p.QuantBeats = 1.0;
        e.SetSessionClipProps(r.Drum, 0, p);
        e.LaunchSlot(r.Drum, 0);
        r.To(2.5);
        double before = e.SessionSlotPosition(r.Drum);
        yield return (before < 0.75, $"repeat: retriggers every beat while held ({before:F2})");
        r.To(2.7); // past the block that queues the next retrigger
        yield return (e.CancelSlotLaunch(r.Drum, 0), "repeat: its queued retrigger is cancelled");
        r.To(5.5);
        double after = e.SessionSlotPosition(r.Drum);
        yield return (e.SessionPlayingSlot(r.Drum) == 0 && after > 2.5,
            $"repeat: no more retriggers, the clip plays on ({after:F2})");
    }

    // Nine tracks in a group cancel together (no per-block limit), and two rows cancelled in the
    // same block both go.
    private static IEnumerable<(bool, string)> BigGroupAndTwoRows()
    {
        using var r = new Rig();
        var e = r.E;
        var members = Enumerable.Range(0, 9).Select(_ => e.AddInstrumentTrack()).ToArray();
        foreach (int m in members) e.AddSessionMidiClip(m, 5, 4.0);
        int grp = e.CreateGroup(members);
        e.LaunchSlot(r.Drum, 0);
        r.To(5.0);
        foreach (int m in members) e.LaunchSlot(m, 5);
        yield return (e.SessionSlotLaunchQueued(grp, 5), "big group: its slot reads as queued");
        yield return (e.CancelSlotLaunch(grp, 5), "big group: one cancel for the group slot");
        r.Block();
        int left = members.Count(m => e.SessionSlotState(m, 5) == 2);
        yield return (left == 0, $"big group: all nine launches withdrawn ({left} left)");
        // Two rows queued on different tracks, cancelled before the audio thread drains either.
        e.LaunchSlot(r.Bass, 3);
        e.LaunchSlot(r.Drum, 2);
        yield return (e.CancelSceneLaunch(3) && e.CancelSceneLaunch(2), "two rows: both report a queued launch");
        r.Block();
        yield return (e.SessionSlotState(r.Bass, 3) == 1 && e.SessionSlotState(r.Drum, 2) == 1,
            $"two rows: both withdrawn in one block (bass {e.SessionSlotState(r.Bass, 3)}, drum {e.SessionSlotState(r.Drum, 2)})");
    }

    // Clips with their own quanta start a scene at different boundaries: once one has started, the
    // scene has started and is not cancelled, so it never ends up half launched.
    private static IEnumerable<(bool, string)> PartlyStartedSceneIsStarted()
    {
        using var r = new Rig();
        var e = r.E;
        var p = Props(e, r.Drum, 2); p.QuantBeats = 1.0; e.SetSessionClipProps(r.Drum, 2, p);
        e.LaunchSlot(r.Drum, 0);
        r.To(5.2);
        e.LaunchScene(2); // drum Fill at beat 6, bass Walk at beat 8
        r.To(6.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 2 && !e.SessionSceneQueued(2) && !e.CancelSceneLaunch(2),
            $"partly started: the scene counts as started, no cancel offered (drum {e.SessionPlayingSlot(r.Drum)}, queued {e.SessionSceneQueued(2)})");
        r.To(8.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 2 && e.SessionPlayingSlot(r.Bass) == 2,
            $"partly started: the rest of the scene starts on its boundary (drum {e.SessionPlayingSlot(r.Drum)}, bass {e.SessionPlayingSlot(r.Bass)})");
    }

    // A manual launch cancelled after the previous follow's lead time passed: that follow still runs.
    private static IEnumerable<(bool, string)> DueFollowComesBack()
    {
        using var r = new Rig();
        var e = r.E;
        e.SetSceneProps(0, new NotaSceneProps { Color = -1, Follow = 1, FollowBeats = 10 }); // → row 1
        e.LaunchScene(0);
        r.To(5.0);
        e.LaunchScene(2); // by hand, for beat 8
        r.To(6.5);        // row 0's follow (due at 10, lead 4) would have fired by now
        yield return (e.CancelSceneLaunch(2), "due follow: the manual launch is cancelled");
        r.To(12.5);
        yield return (e.SessionPlayingSlot(r.Bass) == 1,
            $"due follow: row 0's follow still launches row 1 (bass {e.SessionPlayingSlot(r.Bass)}, drum {e.SessionPlayingSlot(r.Drum)})");
    }

    // A scene that started before the UI polled, then another queued and cancelled: the follow that
    // comes back is the started scene's, not the one from before it.
    private static IEnumerable<(bool, string)> StartedBeforePollKeepsItsFollow()
    {
        using var r = new Rig();
        var e = r.E;
        e.SetSceneProps(1, new NotaSceneProps { Color = -1, Follow = 1, FollowBeats = 8 }); // → row 2
        e.LaunchScene(0);
        r.To(5.0);
        e.LaunchScene(1);
        for (int g = 0; g < 200000 && e.PositionBeats < 8.3; g++) r.BlockNoPoll(); // row 1 starts unpolled
        e.LaunchScene(3);
        yield return (e.CancelSceneLaunch(3), "started before poll: the next launch is cancelled");
        r.To(16.5);
        yield return (e.SessionPlayingSlot(r.Bass) == 2 && e.SessionPlayingSlot(r.Drum) == 2,
            $"started before poll: row 1's own follow launches row 2 (drum {e.SessionPlayingSlot(r.Drum)}, bass {e.SessionPlayingSlot(r.Bass)})");
    }

    // A row that queues only stops is queued (and cancellable); so is a restart of a playing slot.
    private static IEnumerable<(bool, string)> StopsOnlyRowAndRestart()
    {
        using var r = new Rig();
        var e = r.E;
        e.LaunchScene(0);
        r.To(5.0);
        e.LaunchScene(6); // an empty row: stops the drum at beat 8
        yield return (e.SessionSceneQueued(6) && e.SessionSlotState(r.Drum, 6) == 0, "stops-only row: reads as queued with no slot in state 2");
        yield return (e.CancelSceneLaunch(6), "stops-only row: cancelled");
        r.To(8.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 0, $"stops-only row: the drum plays on ({e.SessionPlayingSlot(r.Drum)})");
        e.LaunchSlot(r.Drum, 0); // a restart of the clip playing
        yield return (e.SessionSlotState(r.Drum, 0) == 3 && e.SessionSlotLaunchQueued(r.Drum, 0) && e.SessionSceneQueued(0),
            "restart: the playing slot's queued restart reads as queued");
        yield return (e.CancelSlotLaunch(r.Drum, 0), "restart: cancelled");
        r.To(12.5);
        double pos = e.SessionSlotPosition(r.Drum);
        yield return (e.SessionPlayingSlot(r.Drum) == 0 && Math.Abs(pos - 0.5) < 0.05, $"restart: the clip plays on, not restarted ({pos:F2})");
    }

    // A cancel withdraws the request it saw, not one made after it: cancel, then launch the same slot
    // (or the same scene) again before the audio thread drains the cancel — the new launch stays.
    private static IEnumerable<(bool, string)> RelaunchAfterCancelSurvives()
    {
        using var r = new Rig();
        var e = r.E;
        e.LaunchSlot(r.Drum, 0);
        r.To(5.0);
        e.LaunchSlot(r.Drum, 2);
        e.CancelSlotLaunch(r.Drum, 2);
        e.LaunchSlot(r.Drum, 2); // changed my mind back, same UI tick
        r.Block();
        yield return (e.SessionSlotLaunchQueued(r.Drum, 2), "relaunch after cancel: the newer slot launch is still queued");
        r.To(8.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 2, $"relaunch after cancel: it starts ({e.SessionPlayingSlot(r.Drum)})");
        e.LaunchScene(1);
        e.CancelSceneLaunch(1);
        e.LaunchScene(1);
        r.Block();
        yield return (e.SessionSlotLaunchQueued(r.Bass, 1) && e.SessionSlotLaunchQueued(r.Drum, 1) == false && e.SessionSceneQueued(1),
            "relaunch after cancel: the newer scene launch is whole (bass queued, drum stop queued)");
        r.To(12.5);
        yield return (e.SessionPlayingSlot(r.Bass) == 1 && e.SessionPlayingSlot(r.Drum) == -1,
            $"relaunch after cancel: the scene starts on every track (bass {e.SessionPlayingSlot(r.Bass)}, drum {e.SessionPlayingSlot(r.Drum)})");
    }

    // A scene whose clip follows on before the UI polls: the follow's request applying afterwards must
    // not erase the proof that the scene started — its tempo change still applies.
    private static IEnumerable<(bool, string)> ClipFollowKeepsSceneStarted()
    {
        using var r = new Rig();
        var e = r.E;
        var p = Props(e, r.Bass, 1);
        p.FollowA = (int)SessionFollowAction.Next; p.ChanceA = 100; p.ChanceB = 0; p.FollowBeats = 0.5;
        e.SetSessionClipProps(r.Bass, 1, p);
        e.SetSessionSlotStopButton(r.Drum, 1, false); // a one-track scene
        e.SetSceneProps(1, new NotaSceneProps { Color = -1, Tempo = 140 });
        e.LaunchSlot(r.Bass, 3);
        r.To(5.0);
        e.LaunchScene(1);
        for (int g = 0; g < 200000 && e.PositionBeats < 8.8; g++) r.BlockNoPoll(); // starts at 8, follows at 8.5
        int bass = e.SessionPlayingSlot(r.Bass);
        r.Block(); r.Block();
        yield return (bass == 2 && Math.Abs(e.Bpm - 140) < 1e-6,
            $"clip follow before poll: the scene counts as started, its tempo applies (bass {bass}, {e.Bpm} BPM)");
    }

    // A cancel made on another thread while the audio thread renders the boundary block (#677
    // review): whatever the timing, the scene starts on every track or is withdrawn on every track —
    // never a mix, and never a track that misses the boundary and starts a quantum late.
    private static IEnumerable<(bool, string)> CancelRacesTheBoundary()
    {
        var rng = new Random(480);
        int started = 0, withdrawn = 0, mixed = 0, late = 0;
        for (int k = 0; k < 150; k++)
        {
            using var r = new Rig();
            var e = r.E;
            e.LaunchScene(0);
            r.To(5.0);
            e.LaunchScene(1); // bass starts, drum stops at beat 8
            r.JustBefore(8.0);
            int spinCancel = rng.Next(0, 20000), spinRender = rng.Next(0, 20000);
            using var ready = new CountdownEvent(2);
            var canceller = new Thread(() => { ready.Signal(); ready.Wait(); Thread.SpinWait(spinCancel); e.CancelSceneLaunch(1); });
            canceller.Start();
            ready.Signal(); ready.Wait();
            Thread.SpinWait(spinRender);
            for (int b = 0; b < 3; b++) r.BlockNoPoll(); // the boundary block, then two more
            canceller.Join();
            r.BlockNoPoll();
            int drum = e.SessionPlayingSlot(r.Drum), bass = e.SessionPlayingSlot(r.Bass);
            if (drum == -1 && bass == 1) started++;
            else if (drum == 0 && bass == -1) withdrawn++;
            else mixed++;
            r.To(12.5); // a quantum later nothing else may change
            if (e.SessionPlayingSlot(r.Drum) != drum || e.SessionPlayingSlot(r.Bass) != bass) late++;
        }
        // Either order is valid; which one the scheduler produces is not up to the test (BoundaryRace
        // forces each one). What must never happen is a mix or a late start.
        yield return (mixed == 0 && late == 0,
            $"cancel racing the boundary: all or nothing every time ({started} started, {withdrawn} withdrawn, {mixed} mixed, {late} late)");
    }

    // A clip still waiting in a scene whose other clip started (its own longer quantum) can be
    // cancelled on its own slot; the scene as a whole can't be.
    private static IEnumerable<(bool, string)> WaitingClipOfStartedSceneCancels()
    {
        using var r = new Rig();
        var e = r.E;
        var p = Props(e, r.Drum, 2); p.QuantBeats = 1.0; e.SetSessionClipProps(r.Drum, 2, p);
        e.LaunchSlot(r.Drum, 0);
        r.To(5.2);
        e.LaunchScene(2); // drum Fill at beat 6, bass Walk at beat 8
        r.To(6.5);
        yield return (e.SessionSlotLaunchQueued(r.Bass, 2) && e.CancelSlotLaunch(r.Bass, 2),
            "started scene: its still-waiting clip is cancelled on its own slot");
        r.To(8.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 2 && e.SessionPlayingSlot(r.Bass) == -1,
            $"started scene: that clip never starts, the rest plays on (drum {e.SessionPlayingSlot(r.Drum)}, bass {e.SessionPlayingSlot(r.Bass)})");
    }

    // A row inserted between a scene cancel and the next audio block renumbers the queued requests;
    // they are the same requests, so the cancel still withdraws all of them.
    private static IEnumerable<(bool, string)> RowInsertKeepsTheCancel()
    {
        using var r = new Rig();
        var e = r.E;
        e.LaunchScene(0);
        r.To(5.0);
        e.LaunchScene(1); // bass row 1, drum stop
        yield return (e.CancelSceneLaunch(1), "row insert: the scene is cancelled");
        e.InsertScene(0); // before the audio thread drains the cancel: row 1 becomes row 2
        r.To(8.5);
        yield return (e.SessionPlayingSlot(r.Drum) == 1 && e.SessionPlayingSlot(r.Bass) == -1,
            $"row insert: the whole scene is still withdrawn (drum {e.SessionPlayingSlot(r.Drum)}, bass {e.SessionPlayingSlot(r.Bass)})");
    }

    private static NotaSessionClipProps Props(NotaEngine e, int track, int scene)
    {
        e.TryGetSessionClipProps(track, scene, out var p);
        return p;
    }
}
