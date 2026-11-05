// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Discard and retry a take (#185, specs/078): the take in progress goes with no undo or redo
// step and records again — the Arrangement from where the transport was started (counting in
// again when the take did), a Session slot from the next launch quantum while the other clips
// keep playing. Device-free: MIDI takes from live notes, audio takes from internal resampling.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class RetryTakeTests
{
    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0;   // frames per beat at 120 BPM
    private const int Chunk = 256;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in Catalog()) yield return r;
        foreach (var r in NothingRecording()) yield return r;
        foreach (var r in MidiFromStopWithCountIn()) yield return r;
        foreach (var r in OverdubIntoClip()) yield return r;
        foreach (var r in PunchIn()) yield return r;
        foreach (var r in HeldAcrossRetry()) yield return r;
        foreach (var r in Automation()) yield return r;
        foreach (var r in AudioTake()) yield return r;
        foreach (var r in LoopPasses()) yield return r;
        foreach (var r in SessionMidi()) yield return r;
        foreach (var r in SessionAudio()) yield return r;
        foreach (var r in SessionCountIn()) yield return r;
        foreach (var r in AutomationOnAnotherTrack()) yield return r;
        foreach (var r in EditsMadeMeanwhile()) yield return r;
        foreach (var r in RecordingTrackEditedMeanwhile()) yield return r;
        foreach (var r in DeviceMovedMeanwhile()) yield return r;
        foreach (var r in DeviceReplacedMeanwhile()) yield return r;
        foreach (var r in DisarmedTakeIsKept()) yield return r;
        foreach (var r in SessionHeldIntoRelaunch()) yield return r;
        foreach (var r in SessionToggleSlot()) yield return r;
        foreach (var r in SessionAudioTempoChange()) yield return r;
        foreach (var r in DisarmOneOfTwo()) yield return r;
        foreach (var r in StoppedWhilePending()) yield return r;
        foreach (var r in NoEmptyUndoAfterPartialKeep()) yield return r;
    }

    // Audio and MIDI into empty tracks, audio disarmed (kept), MIDI retried and kept: two undo
    // steps, each one undoing a recording — no step left that changes nothing.
    private static IEnumerable<(bool, string)> NoEmptyUndoAfterPartialKeep()
    {
        using var e = Resampling(out int rec);
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 0);
        e.SetTrackArmed(t, true);
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.ClearUndoHistory();
        e.SetRecording(true);
        Beats(e, 0.5);
        Tap(e, 60, 0.5);
        e.SetTrackArmed(rec, false);            // the audio take is kept
        e.Poll();
        e.RetryTake();
        Beats(e, 0.5);
        Tap(e, 64, 0.5);
        e.SetRecording(false);
        e.Poll();
        e.StopTransport();
        bool u1 = e.Undo();
        bool midiGone = Notes(e, t).Count == 0 && AudioClips(e, rec) == 1;
        bool u2 = e.Undo();
        bool audioGone = AudioClips(e, rec) == 0;
        yield return (u1 && midiGone && u2 && audioGone && !e.CanUndo,
            $"retry after a partial keep: two undo steps, none empty (midi undone {midiGone}, audio undone {audioGone}, more {e.CanUndo})");
    }

    // Recording audio and MIDI together, one track disarmed: its take is kept, the other can
    // still be retried.
    private static IEnumerable<(bool, string)> DisarmOneOfTwo()
    {
        using var e = Resampling(out int rec);
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 0);
        e.SetTrackArmed(t, true);
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.5);
        Tap(e, 60, 0.5);
        e.SetTrackArmed(rec, false);            // the audio take ends here and is kept
        e.Poll();
        int audio = AudioClips(e, rec);
        var r = e.RetryTake();
        Beats(e, 0.01);
        yield return (audio == 1 && r == RetryTakeResult.Arrangement && AudioClips(e, rec) == 1 && Notes(e, t).Count == 0,
            $"retry after disarming one of two: the kept audio stays, the MIDI is retried ({r}, audio clips {AudioClips(e, rec)}, notes {Notes(e, t).Count})");
        e.SetRecording(false);
        e.StopTransport();
    }

    // A Session MIDI retry stopped before its slot relaunched: the next ordinary take records.
    private static IEnumerable<(bool, string)> StoppedWhilePending()
    {
        using var e = Armed(out int t);
        e.SetLaunchQuant(4.0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.Play();
        Beats(e, 0.5);
        e.RecordSessionSlot(t, 0);
        Beats(e, 4.0);
        e.RetryTake();                           // waits for beat 8
        Beats(e, 0.5);
        e.StopSessionRecord();
        e.StopAllSession();
        e.StopTransport();
        e.BackToArrangement();
        Beats(e, 0.05);
        e.SetCountIn(0);
        e.Seek(16.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.5);
        Tap(e, 67, 0.5);
        e.SetRecording(false);
        e.Poll();
        yield return (Notes(e, t).Any(n => n.Note.Pitch == 67),
            $"retry session stopped while pending: the next Arrangement take still records ({string.Join(",", Notes(e, t).Select(n => n.Note.Pitch))})");
        e.StopTransport();
    }

    // Review of #486. A lane written on a track the take doesn't record goes with the take too.
    private static IEnumerable<(bool, string)> AutomationOnAnotherTrack()
    {
        using var e = Armed(out int t);
        int o = e.AddInstrumentTrack();
        int lanes = e.AutomationLaneCount(o);
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.5);
        e.BeginAutomationWrite(o, AutomationTarget.Volume, -1, -1, "");
        Beats(e, 0.5);
        e.EndAutomationWrite(o, AutomationTarget.Volume, -1, -1, "");
        bool wrote = e.AutomationLaneCount(o) > lanes;
        e.RetryTake();
        Beats(e, 0.01);
        yield return (wrote && e.AutomationLaneCount(o) == lanes,
            $"retry automation elsewhere: another track's lane written in the take goes too ({lanes} → {e.AutomationLaneCount(o)})");
        e.SetRecording(false);
        e.StopTransport();
    }

    // An edit made during the take keeps its undo step, and undoing it doesn't bring the take back.
    private static IEnumerable<(bool, string)> EditsMadeMeanwhile()
    {
        using var e = Armed(out int t);
        int o = e.AddInstrumentTrack();
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.5);
        Tap(e, 60, 0.5);
        e.AddMidiClip(o, 8, 4);                 // the user's own edit, mid-take
        e.RetryTake();
        Beats(e, 0.5);
        Tap(e, 62, 0.5);
        e.SetRecording(false);
        e.Poll();
        e.StopTransport();
        bool kept = Clips(e, o).Count == 1;
        e.Undo();                               // the kept take
        bool takeGone = Notes(e, t).Count == 0 && Clips(e, o).Count == 1;
        e.Undo();                               // the edit made during the discarded take
        bool editGone = Clips(e, o).Count == 0 && Notes(e, t).All(n => n.Note.Pitch != 60);
        yield return (kept && takeGone && editGone,
            $"retry: an edit made meanwhile keeps its undo step, without the discarded take in it (kept {kept}, take undone {takeGone}, edit undone {editGone})");
    }

    // Renaming the recording track mid-take: the retry drops the recording, not the rename,
    // and the rename keeps its own undo step.
    private static IEnumerable<(bool, string)> RecordingTrackEditedMeanwhile()
    {
        using var e = Armed(out int t);
        e.SetTrackName(t, "Before");
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.ClearUndoHistory();
        e.SetRecording(true);
        Beats(e, 0.5);
        Tap(e, 60, 0.5);
        e.SetTrackName(t, "Renamed");            // the user's own edit on the recording track
        e.RetryTake();
        Beats(e, 0.01);
        bool named = e.GetTrackName(t) == "Renamed";
        bool dropped = Notes(e, t).All(n => n.Note.Pitch != 60);
        e.SetRecording(false);
        e.Poll();
        e.StopTransport();
        e.Undo();                                // the kept take
        e.Undo();                                // the rename
        bool renameUndone = e.GetTrackName(t) == "Before" && Notes(e, t).All(n => n.Note.Pitch != 60);
        yield return (named && dropped && renameUndone,
            $"retry keeps a rename of the recording track (name kept {named}, take dropped {dropped}, rename undoes to the old name without the take {renameUndone})");
    }

    // Moving an automated effect mid-take: the pre-take lane comes back aimed at the effect
    // where it now sits; removing one takes its lane with it.
    private static IEnumerable<(bool, string)> DeviceMovedMeanwhile()
    {
        const int Utility = 4, Delay = 3;
        using var e = Armed(out int t);
        e.AddBuiltinDevice(t, Utility);
        e.AddBuiltinDevice(t, Delay);
        int lane = e.AddAutomationLane(t, AutomationTarget.DeviceParam, 0, 0);   // the Utility
        e.SetAutomationPoints(t, lane, new[] { new AutomationPoint(0, 0.2f), new AutomationPoint(4, 0.8f) });
        int lanes = e.AutomationLaneCount(t);
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.5);
        e.BeginAutomationWrite(t, AutomationTarget.Pan, -1, -1, "");
        Beats(e, 0.5);
        e.EndAutomationWrite(t, AutomationTarget.Pan, -1, -1, "");
        e.MoveDevice(t, 0, 1);                   // the Utility now sits second
        e.RetryTake();
        Beats(e, 0.01);
        var moved = Enumerable.Range(0, e.AutomationLaneCount(t)).Select(i => e.AutomationLaneInfo(t, i))
            .Where(i => i.Target == AutomationTarget.DeviceParam).ToArray();
        yield return (e.AutomationLaneCount(t) == lanes && moved.Length == 1 && moved[0].DeviceIndex == 1 && moved[0].PointCount == 2,
            $"retry after moving an automated effect: its lane follows it (lanes {lanes} → {e.AutomationLaneCount(t)}, device {(moved.Length == 1 ? moved[0].DeviceIndex : -9)})");
        e.RemoveDevice(t, 1);                    // the Utility goes, mid-take
        e.RetryTake();
        Beats(e, 0.01);
        bool gone = Enumerable.Range(0, e.AutomationLaneCount(t)).All(i => e.AutomationLaneInfo(t, i).Target != AutomationTarget.DeviceParam);
        yield return (gone, "retry after removing an automated effect: its lane goes with it");
        e.SetRecording(false);
        e.StopTransport();
    }

    // Edits that swap a device for a copy in its slot — renaming a MIDI Effect Rack's macro,
    // loading a MIDI effect's state — keep its automation through a retry.
    private static IEnumerable<(bool, string)> DeviceReplacedMeanwhile()
    {
        const int Arp = 0, MidiEffectRack = 6;
        using var e = Armed(out int t);
        int arp = e.AddMidiEffect(t, Arp);
        int rack = e.AddMidiEffect(t, MidiEffectRack);
        foreach (int d in new[] { arp, rack })
        {
            int l = e.AddAutomationLane(t, AutomationTarget.MidiDeviceParam, d, 0);
            e.SetAutomationPoints(t, l, new[] { new AutomationPoint(0, 0.2f), new AutomationPoint(4, 0.8f) });
        }
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.5);
        e.MidiRackSetMacroName(t, rack, 0, "Spread");      // forks the rack
        e.MidiEffectSetState(t, arp, e.MidiEffectGetState(t, arp), true);   // copies the Arp
        e.RetryTake();
        Beats(e, 0.01);
        var on = Enumerable.Range(0, e.AutomationLaneCount(t)).Select(i => e.AutomationLaneInfo(t, i))
            .Where(i => i.Target == AutomationTarget.MidiDeviceParam && i.PointCount == 2).Select(i => i.DeviceIndex).OrderBy(x => x).ToArray();
        yield return (on.SequenceEqual(new[] { arp, rack }) && e.MidiRackMacroName(t, rack, 0) == "Spread",
            $"retry after editing a MIDI rack and loading an effect's state: their automation stays (lanes on {string.Join(",", on)})");
        e.SetRecording(false);
        e.StopTransport();
    }

    // Disarming the track ends (and keeps) its take: a retry afterwards has nothing to discard.
    private static IEnumerable<(bool, string)> DisarmedTakeIsKept()
    {
        using var e = Armed(out int t);
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.5);
        Tap(e, 60, 0.5);
        e.SetTrackArmed(t, false);
        e.Poll();
        var r = e.RetryTake();
        yield return (r == RetryTakeResult.None && Notes(e, t).Any(n => n.Note.Pitch == 60),
            $"retry after disarm: the kept take stays ({r})");
        e.SetRecording(false);
        e.StopTransport();
    }

    // A key pressed while the retried slot waits and released after it relaunched: the new
    // take holds it from the relaunch — no note wrapped round the open-ended slot.
    private static IEnumerable<(bool, string)> SessionHeldIntoRelaunch()
    {
        using var e = Armed(out int t);
        e.SetLaunchQuant(4.0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.Play();
        Beats(e, 0.5);
        e.RecordSessionSlot(t, 0);
        Beats(e, 4.0);                          // recording from beat 4
        Tap(e, 60, 0.5);
        e.RetryTake();                          // waits for beat 8
        Beats(e, 1.0);
        e.SendController(1, 0.7f);              // played while pending: not part of the take
        e.NoteOn(64, 0.8f);
        Beats(e, 2.5);                          // past the relaunch at 8
        e.NoteOff(64);
        Beats(e, 0.1);
        e.StopSessionRecord();
        Beats(e, 0.05);
        var notes = e.GetSessionNotes(t, 0);
        var n64 = notes.Where(n => n.Pitch == 64).ToList();
        yield return (notes.All(n => n.Pitch != 60) && n64.Count == 1 && n64[0].StartBeat < 0.1 && n64[0].LengthBeats < 1.0,
            $"retry session held key: starts with the relaunched take ({string.Join(",", notes.Select(n => $"{n.Pitch}@{n.StartBeat:F2}+{n.LengthBeats:F2}"))})");
        e.StopAllSession();
        e.StopTransport();
    }

    // A Toggle slot relaunches for the retried take instead of stopping.
    private static IEnumerable<(bool, string)> SessionToggleSlot()
    {
        using var e = Armed(out int t);
        e.AddSessionMidiClip(t, 0, 4);
        e.TryGetSessionClipProps(t, 0, out var p);
        p.LaunchMode = 2;                        // Toggle
        e.SetSessionClipProps(t, 0, p);
        e.SetLaunchQuant(4.0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.Play();
        Beats(e, 0.5);
        e.RecordSessionSlot(t, 0);
        Beats(e, 4.0);
        e.RetryTake();
        Beats(e, 4.0);                           // past the relaunch at 8
        yield return (e.SessionPlayingSlot(t) == 0 && e.SessionSlotState(t, 0) == 4,
            $"retry toggle slot: relaunches and records (playing {e.SessionPlayingSlot(t)}, state {e.SessionSlotState(t, 0)})");
        e.StopSessionRecord();
        e.StopAllSession();
        e.StopTransport();
    }

    // A tempo change after the boundary doesn't move where the retried audio take begins.
    private static IEnumerable<(bool, string)> SessionAudioTempoChange()
    {
        using var e = Resampling(out int rec);
        e.SetCountIn(0);
        e.SetLaunchQuant(4.0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.Play();
        Beats(e, 0.5);
        e.RecordSessionSlot(rec, 0);
        Beats(e, 0.5);
        e.RetryTake();                           // boundary at 4
        Beats(e, 4.0);                           // ~1 beat past it at 120 BPM: 24000 frames
        e.SetBpm(60);
        Beats(e, 0.01);
        var b = new float[Chunk * 2];
        for (int done = 0; done < 48000; done += Chunk) { e.RenderOffline(b, Chunk, Sr); e.Poll(); }   // 1 s more
        e.StopSessionRecord();
        Beats(e, 0.01);
        int idx = e.SessionSlotToArrangement(rec, 0, 32.0);
        long frames = idx >= 0 && e.TryGetAudioClipInfo(rec, idx, out var ci) && e.TryGetSampleInfo(ci.SampleId, out var si) ? si.Frames : -1;
        yield return (Math.Abs(frames - 72000) < 2500,
            $"retry session audio + tempo change: keeps what followed the boundary ({frames} frames, ~72000 expected)");
        e.StopAllSession();
        e.StopTransport();
    }

    // A retry during a Session count-in starts the count-in over; nothing was recorded yet.
    private static IEnumerable<(bool, string)> SessionCountIn()
    {
        using var e = Armed(out int t);
        e.SetLaunchQuant(4.0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetCountIn(1);
        e.RecordSessionSlot(t, 0);
        Beats(e, 2.5);
        double left = e.CountInBeats;
        var r = e.RetryTake();
        Beats(e, 0.01);
        double again = e.CountInBeats;
        yield return (r == RetryTakeResult.Session && left < 2.0 && again > 3.5 && !e.IsPlaying && e.IsRecording,
            $"retry session count-in: the count-in starts over ({left:F2} → {again:F2} beats left)");
        Beats(e, 4.5);
        yield return (e.IsPlaying && e.SessionPlayingSlot(t) == 0, "retry session count-in: then the slot records as it runs out");
        e.StopSessionRecord();
        e.StopAllSession();
        e.StopTransport();
    }

    private static void Beats(NotaEngine e, double beats)
    {
        var b = new float[Chunk * 2];
        int frames = (int)Math.Round(beats * Spb);
        for (int done = 0; done < frames; done += Chunk) { e.RenderOffline(b, Chunk, Sr); e.Poll(); }
    }

    private static NotaEngine Armed(out int track)
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        track = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(track, 0);
        e.SetTrackArmed(track, true);
        e.StopTransport();
        e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        return e;
    }

    // An instrument track sounding a long note, recorded into an armed audio track.
    private static NotaEngine Resampling(out int rec)
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        int src = e.AddBassSynthTrack();
        int mc = e.AddMidiClip(src, 0, 64);
        e.SetClipNotes(src, mc, new[] { new NotaNote(36, 0.0, 64.0, 1.0f) });
        rec = e.AddAudioTrack();
        e.SetTrackRecordInput(rec, src);
        e.SetTrackArmed(rec, true);
        e.StopTransport();
        e.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        return e;
    }

    private static List<(NotaClipInfo Info, NotaNote[] Notes)> Clips(NotaEngine e, int track)
    {
        var all = new List<(NotaClipInfo, NotaNote[])>();
        for (int c = 0; c < 64 && e.TryGetClipInfo(track, c, out var ci); c++) all.Add((ci, e.GetClipNotes(track, c)));
        return all;
    }

    private static List<(double Abs, NotaNote Note)> Notes(NotaEngine e, int track)
        => Clips(e, track).SelectMany(c => c.Notes.Select(n => (c.Info.StartBeat + n.StartBeat, n))).ToList();

    private static int AudioClips(NotaEngine e, int track)
    {
        int n = 0;
        while (n < 64 && e.TryGetAudioClipInfo(track, n, out _)) n++;
        return n;
    }

    private static void Tap(NotaEngine e, int pitch, double holdBeats)
    {
        e.NoteOn(pitch, 0.8f);
        Beats(e, holdBeats);
        e.NoteOff(pitch);
        Beats(e, 0.05);
    }

    private static bool Near(double a, double b, double tol = 0.05) => Math.Abs(a - b) <= tol;

    // ⌘. (Ctrl+. elsewhere) runs it, from the Transport group; "retry" / "undo restart" find it.
    private static IEnumerable<(bool, string)> Catalog()
    {
        var c = Nota.Application.Palette.CommandCatalog.Create().FirstOrDefault(x => x.Id == "transport.retryTake");
        yield return (c is not null && c.Gesture == "⌘." && c.Aliases.Contains("retry") && c.Aliases.Contains("restart"),
            $"retry: the catalog holds transport.retryTake on ⌘. ({c?.Gesture ?? "missing"})");
    }

    private static IEnumerable<(bool, string)> NothingRecording()
    {
        using var e = Armed(out int t);
        bool undo = e.CanUndo, redo = e.CanRedo;
        var r = e.RetryTake();
        yield return (r == RetryTakeResult.None && !e.IsRecording && !e.IsPlaying && e.CanUndo == undo && e.CanRedo == redo && Clips(e, t).Count == 0,
            $"retry: nothing recording changes nothing ({r})");
    }

    // The issue's case: a take from stop with a count-in. Retry drops it (and the clip it made),
    // returns to the take start, counts in again and records; the kept take is one undo step.
    private static IEnumerable<(bool, string)> MidiFromStopWithCountIn()
    {
        using var e = Armed(out int t);
        e.Seek(8.0);
        Beats(e, 0.01);
        e.SetCountIn(1);
        e.SetRecording(true);
        Beats(e, 4.5);                       // the bar of count-in, then half a beat of take
        Tap(e, 60, 0.5);
        yield return (Notes(e, t).Any(n => n.Note.Pitch == 60), "retry count-in: the first take recorded a note");

        var r = e.RetryTake();
        Beats(e, 0.01);
        // The new take opens its clip at the start, as any take does: empty, the first take's note gone.
        yield return (r == RetryTakeResult.Arrangement && e.IsRecording && Clips(e, t).Count <= 1 && Notes(e, t).Count == 0,
            $"retry count-in: the take's notes are gone, recording again ({r}, clips {Clips(e, t).Count}, notes {Notes(e, t).Count})");
        yield return (e.CountInBeats > 0 && !e.IsPlaying && Near(e.PositionBeats, 8.0),
            $"retry count-in: counting in again at the take start (count-in {e.CountInBeats:F2}, at {e.PositionBeats:F2})");
        yield return (!e.CanRedo, "retry count-in: no redo step");

        Beats(e, 4.5);
        Tap(e, 64, 0.5);
        e.SetRecording(false);
        e.Poll();
        var notes = Notes(e, t);
        var n64 = notes.FirstOrDefault(n => n.Note.Pitch == 64);
        yield return (notes.All(n => n.Note.Pitch != 60) && notes.Any(n => n.Note.Pitch == 64) && Near(n64.Abs, 8.5, 0.06),
            $"retry count-in: only the second take is kept, where it was played ({string.Join(",", notes.Select(n => $"{n.Note.Pitch}@{n.Abs:F2}"))})");
        e.StopTransport();
        bool undone = e.Undo();
        yield return (undone && Clips(e, t).Count == 0, "retry count-in: one undo removes the kept take — the discarded one left no step");
        bool redone = e.Redo();
        yield return (redone && Notes(e, t).Any(n => n.Note.Pitch == 64) && !e.CanRedo,
            "retry count-in: redo brings back only the kept take");
    }

    // Overdubbing into a clip: the retry gives the clip its notes back, nothing more.
    private static IEnumerable<(bool, string)> OverdubIntoClip()
    {
        using var e = Armed(out int t);
        int c = e.AddMidiClip(t, 0, 16);
        e.SetClipNotes(t, c, new[] { new NotaNote(50, 2.0, 1.0, 0.7f) });
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 1.0);
        Tap(e, 60, 0.5);
        var r = e.RetryTake();
        Beats(e, 0.01);
        var notes = Notes(e, t);
        yield return (r == RetryTakeResult.Arrangement && Clips(e, t).Count == 1 && notes.Count == 1 && notes[0].Note.Pitch == 50,
            $"retry overdub: the clip holds what it held before the take ({string.Join(",", notes.Select(n => n.Note.Pitch))})");
        yield return (e.IsPlaying && e.PositionBeats < 0.5, $"retry overdub: rolling again from the take start (at {e.PositionBeats:F2})");
        e.SetRecording(false);
        e.StopTransport();
    }

    // Punched in during playback: the retry goes back to where Play was pressed and keeps rolling.
    private static IEnumerable<(bool, string)> PunchIn()
    {
        using var e = Armed(out int t);
        e.SetCountIn(1);                     // set, but a punch-in never counts in
        e.Seek(4.0);
        Beats(e, 0.01);
        e.Play();
        Beats(e, 4.0);                       // ~beat 8
        e.SetRecording(true);
        Beats(e, 0.5);
        Tap(e, 60, 0.5);
        var r = e.RetryTake();
        Beats(e, 0.05);
        yield return (r == RetryTakeResult.Arrangement && e.IsRecording && e.IsPlaying && e.CountInBeats <= 0 && Near(e.PositionBeats, 4.05, 0.05),
            $"retry punch-in: back to where Play was pressed, rolling, no count-in ({r}, at {e.PositionBeats:F2}, count-in {e.CountInBeats:F2})");
        Beats(e, 0.45);
        Tap(e, 62, 0.5);
        e.SetRecording(false);
        e.Poll();
        var notes = Notes(e, t);
        yield return (notes.All(n => n.Note.Pitch != 60) && notes.Any(n => n.Note.Pitch == 62 && Near(n.Abs, 4.5, 0.06)),
            $"retry punch-in: only the new take, recorded from the run-up ({string.Join(",", notes.Select(n => $"{n.Note.Pitch}@{n.Abs:F2}"))})");
        e.StopTransport();
    }

    // A key held through the retry joins the new take at its start, not at its old press beat.
    private static IEnumerable<(bool, string)> HeldAcrossRetry()
    {
        using var e = Armed(out int t);
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 1.0);
        e.NoteOn(60, 0.8f);
        Beats(e, 0.5);
        e.RetryTake();
        Beats(e, 1.0);
        e.NoteOff(60);
        Beats(e, 0.05);
        e.SetRecording(false);
        e.Poll();
        var n60 = Notes(e, t).Where(n => n.Note.Pitch == 60).ToList();
        yield return (n60.Count == 1 && n60[0].Abs < 0.05 && Near(n60[0].Note.LengthBeats, 1.0, 0.06),
            $"retry held key: starts with the new take ({(n60.Count > 0 ? $"{n60[0].Abs:F2}+{n60[0].Note.LengthBeats:F2}" : "none")}, n={n60.Count})");
        e.StopTransport();
    }

    // Automation written while the record button is down goes with the take.
    private static IEnumerable<(bool, string)> Automation()
    {
        using var e = Armed(out int t);
        int lanes = e.AutomationLaneCount(t);
        e.SetCountIn(0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.5);
        e.BeginAutomationWrite(t, AutomationTarget.Pan, -1, -1, "");
        Beats(e, 0.5);
        e.EndAutomationWrite(t, AutomationTarget.Pan, -1, -1, "");
        bool wrote = e.AutomationLaneCount(t) > lanes;
        e.RetryTake();
        Beats(e, 0.01);
        yield return (wrote && e.AutomationLaneCount(t) == lanes && e.AutomationRecording,
            $"retry automation: the lane the take wrote is gone, record stays on (lanes {lanes} → {e.AutomationLaneCount(t)})");
        e.SetRecording(false);
        e.StopTransport();
    }

    // An audio take: no clip survives the retry; the kept take sits where the first began.
    private static IEnumerable<(bool, string)> AudioTake()
    {
        using var e = Resampling(out int rec);
        e.SetCountIn(0);
        e.Seek(2.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 1.0);
        var r = e.RetryTake();
        Beats(e, 0.01);
        yield return (r == RetryTakeResult.Arrangement && AudioClips(e, rec) == 0 && e.IsRecording,
            $"retry audio: the capture is dropped unmaterialised ({r}, clips {AudioClips(e, rec)})");
        Beats(e, 1.0);
        e.SetRecording(false);
        e.Poll();
        bool one = AudioClips(e, rec) == 1 && e.TryGetAudioClipInfo(rec, 0, out var ci) && Near(ci.StartBeat, 2.0, 0.01);
        e.TryGetAudioClipInfo(rec, 0, out var info);
        yield return (one, $"retry audio: one clip, at the original start (clips {AudioClips(e, rec)}, start {info.StartBeat:F3})");
        e.StopTransport();
    }

    // A loop take of several passes: none of them stays as a take lane.
    private static IEnumerable<(bool, string)> LoopPasses()
    {
        using var e = Resampling(out int rec);
        e.SetCountIn(0);
        e.SetLoop(true, 0.0, 2.0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 5.0);                       // two full passes and a half
        var r = e.RetryTake();
        Beats(e, 0.01);
        yield return (r == RetryTakeResult.Arrangement && e.TakeCount(rec) == 0 && AudioClips(e, rec) == 0,
            $"retry loop: no pass is kept (takes {e.TakeCount(rec)}, clips {AudioClips(e, rec)})");
        e.SetRecording(false);
        e.StopTransport();
    }

    // Session MIDI: only the recording slot is dropped; the other track's clip plays on, the
    // slot waits for the next quantum (state 5), then records; stop keeps the new take only.
    private static IEnumerable<(bool, string)> SessionMidi()
    {
        using var e = Armed(out int t);
        int d = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(d, 0);
        e.AddSessionMidiClip(d, 0, 4);
        e.SetSessionNotes(d, 0, new[] { new NotaNote(40, 0.0, 1.0, 0.8f) });
        e.SetLaunchQuant(4.0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.LaunchSlot(d, 0);
        Beats(e, 1.0);
        e.RecordSessionSlot(t, 1);
        Beats(e, 3.5);                       // past the quantum at 4
        Tap(e, 60, 0.5);
        bool first = e.GetSessionNotes(t, 1).Any(n => n.Pitch == 60);

        var r = e.RetryTake();
        int pending = e.SessionSlotState(t, 1);
        yield return (first && r == RetryTakeResult.Session && pending == 5 && !e.GetSessionNotes(t, 1).Any(n => n.Pitch == 60),
            $"retry session: the slot's take is dropped and waits for the quantum ({r}, state {pending})");
        Beats(e, 0.5);
        yield return (e.SessionSlotState(d, 0) == 3 && e.IsPlaying, "retry session: the other track's clip keeps playing");
        Beats(e, 2.5);                       // past the quantum at 8
        yield return (e.SessionSlotState(t, 1) == 4, $"retry session: recording from the quantum (state {e.SessionSlotState(t, 1)})");
        Tap(e, 62, 0.5);
        e.StopSessionRecord();
        Beats(e, 0.1);
        var notes = e.GetSessionNotes(t, 1);
        yield return (notes.Any(n => n.Pitch == 62) && notes.All(n => n.Pitch != 60) && e.SessionSlotState(d, 0) == 3,
            $"retry session: stop keeps only the new take ({string.Join(",", notes.Select(n => n.Pitch))})");
        e.StopAllSession();
        e.StopTransport();
    }

    // Session audio: the retried take starts on the launch quantum — what came before is trimmed.
    private static IEnumerable<(bool, string)> SessionAudio()
    {
        using var e = Resampling(out int rec);
        e.SetCountIn(0);
        e.SetLaunchQuant(4.0);
        e.Seek(0.0);
        Beats(e, 0.01);
        e.Play();
        Beats(e, 0.5);
        e.RecordSessionSlot(rec, 0);
        Beats(e, 0.5);                       // ~beat 1
        var r = e.RetryTake();
        Beats(e, 0.01);
        int pending = e.SessionSlotState(rec, 0);
        yield return (r == RetryTakeResult.Session && pending == 5, $"retry session audio: waits for the quantum ({r}, state {pending})");
        Beats(e, 3.5);                       // ~beat 4.5
        int recording = e.SessionSlotState(rec, 0);
        Beats(e, 1.5);                       // ~beat 6
        e.StopSessionRecord();
        Beats(e, 0.05);
        int idx = e.SessionSlotToArrangement(rec, 0, 32.0);
        long frames = idx >= 0 && e.TryGetAudioClipInfo(rec, idx, out var ci) && e.TryGetSampleInfo(ci.SampleId, out var si) ? si.Frames : -1;
        double beats = frames / Spb;
        yield return (recording == 4 && Near(beats, 2.0, 0.1),
            $"retry session audio: the take starts on the quantum ({beats:F2} beats kept, state then {recording})");
        e.StopAllSession();
        e.StopTransport();
    }
}
