// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Several instrument tracks recording at once, each through its own MIDI input (specs/086,
// #528): a track on All inputs takes every device and the computer keyboard, a track on one
// device and channel takes only that; linked group tracks record the same notes in one undo
// step; the pick survives save and load, and a device that isn't connected sends nothing.
// Driven without hardware: messages pushed as if a source sent them.

using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class MidiInputFilterTests
{
    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0; // frames per beat at 120 BPM
    private const int Chunk = 256;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in FilterPerTrack()) yield return r;
        foreach (var r in InputChangedWhileHeld()) yield return r;
        foreach (var r in OtherInputKeepsHeld()) yield return r;
        foreach (var r in LinkedGroupRecordsBoth()) yield return r;
        foreach (var r in OverdubPerTrack()) yield return r;
        foreach (var r in SavedAndDisconnected()) yield return r;
        foreach (var r in SessionSlotsTogether()) yield return r;
        foreach (var r in SessionSlotsRetry()) yield return r;
        foreach (var r in SessionFirstSlotStops()) yield return r;
        foreach (var r in PedalSeedsItsOwnTrack()) yield return r;
        foreach (var r in SessionSceneInsertFollows()) yield return r;
    }

    private static NotaEngine Engine()
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        return e;
    }

    private static int Instrument(NotaEngine e)
    {
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, 0);
        return t;
    }

    private static void Beats(NotaEngine e, double beats)
    {
        var b = new float[Chunk * 2];
        int frames = (int)Math.Round(beats * Spb);
        for (int done = 0; done < frames; done += Chunk) { e.RenderOffline(b, Chunk, Sr); e.Poll(); }
    }

    // A hardware note: on, held, off (channel 0-15).
    private static void HwTap(NotaEngine e, int source, int channel, int pitch)
    {
        e.PushMidiForTest(source, 0x90, channel, pitch, 100);
        Beats(e, 0.25);
        e.PushMidiForTest(source, 0x80, channel, pitch, 0);
        Beats(e, 0.1);
    }

    private static void KeyTap(NotaEngine e, int pitch)
    {
        e.NoteOn(pitch, 0.8f);
        Beats(e, 0.25);
        e.NoteOff(pitch);
        Beats(e, 0.1);
    }

    private static List<int> Pitches(NotaEngine e, int track)
    {
        var all = new List<int>();
        for (int c = 0; c < 64 && e.TryGetClipInfo(track, c, out _); c++)
            all.AddRange(e.GetClipNotes(track, c).Select(n => n.Pitch));
        all.Sort();
        return all;
    }

    private static int ClipCount(NotaEngine e, int track)
    {
        int n = 0;
        while (n < 64 && e.TryGetClipInfo(track, n, out _)) n++;
        return n;
    }

    private static void Record(NotaEngine e, Action play)
    {
        e.StopTransport();
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.1);
        play();
        e.SetRecording(false);
        e.Poll();
        e.StopTransport();
    }

    // T058: P on All inputs, Q on source 1 channel 1.
    private static IEnumerable<(bool, string)> FilterPerTrack()
    {
        using var e = Engine();
        int p = Instrument(e), q = Instrument(e);
        e.SetTrackMidiInputSourceForTest(q, 1, 1);
        e.SetTrackArmed(p, true);
        e.SetTrackArmed(q, true);
        Record(e, () =>
        {
            HwTap(e, 0, 0, 60); // source 0, channel 1
            HwTap(e, 1, 0, 62); // source 1, channel 1: Q's
            HwTap(e, 1, 1, 64); // source 1, channel 2
            KeyTap(e, 67);      // the computer keyboard
        });
        var pp = Pitches(e, p);
        var qp = Pitches(e, q);
        yield return (pp.SequenceEqual(new[] { 60, 62, 64, 67 }), $"a track on All inputs records every device and the keyboard ({string.Join(",", pp)})");
        yield return (qp.SequenceEqual(new[] { 62 }), $"a track on one device and channel records only that ({string.Join(",", qp)})");
        var mi = e.GetTrackMidiInput(q);
        yield return (mi.Channel == 1, $"…and reads its channel back ({mi.Channel})");
    }

    // The input switched while a key is held: its note-off, from the old input, still ends the
    // note, both the sound and the recorded note.
    private static IEnumerable<(bool, string)> InputChangedWhileHeld()
    {
        using var e = Engine();
        int t = Instrument(e);
        e.SetTrackMidiInputSourceForTest(t, 0, 0);
        e.SetTrackArmed(t, true);
        e.StopTransport();
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.1);
        e.PushMidiForTest(0, 0x90, 0, 60, 100);
        Beats(e, 0.25);
        e.SetTrackMidiInputSourceForTest(t, 1, 0);   // now device 1
        Beats(e, 0.1);
        e.PushMidiForTest(0, 0x80, 0, 60, 0);        // the key comes up on device 0
        Beats(e, 2.0);
        var tail = new float[Chunk * 2];
        e.RenderOffline(tail, Chunk, Sr);
        float peak = tail.Max(MathF.Abs);
        e.SetRecording(false);
        e.Poll();
        e.StopTransport();
        var notes = e.TryGetClipInfo(t, 0, out _) ? e.GetClipNotes(t, 0) : Array.Empty<NotaNote>();
        double len = notes.Length == 1 ? notes[0].LengthBeats : -1;
        yield return (peak < 1e-4f && notes.Length == 1 && len > 0.25 && len < 0.5,
            $"a key held while the track's MIDI input changes still ends: sound and recorded note (peak {peak:E1}, length {len:F2})");
    }

    // …but only the input that played it: the same key released on a keyboard the track
    // doesn't listen to leaves the note sounding and recording.
    private static IEnumerable<(bool, string)> OtherInputKeepsHeld()
    {
        using var e = Engine();
        int t = Instrument(e);
        e.SetTrackMidiInputSourceForTest(t, 0, 0);
        e.SetTrackArmed(t, true);
        e.StopTransport();
        e.Seek(0.0);
        Beats(e, 0.01);
        e.SetRecording(true);
        Beats(e, 0.1);
        e.PushMidiForTest(0, 0x90, 0, 60, 100);
        Beats(e, 0.25);
        e.PushMidiForTest(1, 0x80, 0, 60, 0);        // the same key up on device 1
        Beats(e, 1.0);
        var tail = new float[Chunk * 2];
        e.RenderOffline(tail, Chunk, Sr);
        float peak = tail.Max(MathF.Abs);
        e.PushMidiForTest(0, 0x80, 0, 60, 0);        // and on device 0, which played it
        Beats(e, 0.25);
        e.SetRecording(false);
        e.Poll();
        e.StopTransport();
        var notes = e.TryGetClipInfo(t, 0, out _) ? e.GetClipNotes(t, 0) : Array.Empty<NotaNote>();
        double len = notes.Length == 1 ? notes[0].LengthBeats : -1;
        yield return (peak > 1e-3f && notes.Length == 1 && len > 1.2,
            $"…releasing the same key on another device doesn't end it (peak {peak:E1}, length {len:F2})");
    }

    // T059: #528 for instrument groups — arming one linked track arms both, both record.
    private static IEnumerable<(bool, string)> LinkedGroupRecordsBoth()
    {
        using var e = Engine();
        int a = Instrument(e), b = Instrument(e);
        int g = e.CreateGroup(new[] { a, b });
        e.SetTrackEditLink(g, true);
        TrackArm.Set(e, a, true, additive: false);
        e.ClearUndoHistory();
        Record(e, () => { KeyTap(e, 60); KeyTap(e, 64); });
        var pa = Pitches(e, a);
        var pb = Pitches(e, b);
        yield return (ClipCount(e, a) == 1 && ClipCount(e, b) == 1 && pa.SequenceEqual(new[] { 60, 64 }) && pb.SequenceEqual(pa),
            $"both linked instrument tracks record the same notes (#528; {string.Join(",", pa)} / {string.Join(",", pb)})");
        bool undone = e.Undo();
        yield return (undone && ClipCount(e, a) == 0 && ClipCount(e, b) == 0,
            $"one Undo takes both clips back ({ClipCount(e, a)}, {ClipCount(e, b)})");
        e.Redo();
        yield return (ClipCount(e, a) == 1 && ClipCount(e, b) == 1, "one Redo brings both back");
    }

    // Each track overdubs into its own clip under the playhead.
    private static IEnumerable<(bool, string)> OverdubPerTrack()
    {
        using var e = Engine();
        int a = Instrument(e), b = Instrument(e);
        int ca = e.AddMidiClip(a, 0, 8);
        e.SetClipNotes(a, ca, new[] { new NotaNote(48, 4.0, 1.0, 0.8f) });
        e.SetTrackArmed(a, true);
        e.SetTrackArmed(b, true);
        Record(e, () => KeyTap(e, 72));
        var pa = Pitches(e, a);
        yield return (ClipCount(e, a) == 1 && pa.SequenceEqual(new[] { 48, 72 }) && ClipCount(e, b) == 1 && Pitches(e, b).SequenceEqual(new[] { 72 }),
            $"one track overdubs its clip, the other opens one ({string.Join(",", pa)})");
    }

    // T060: the pick survives save and load; a device that isn't connected sends nothing.
    private static IEnumerable<(bool, string)> SavedAndDisconnected()
    {
        using var e = Engine();
        int t = Instrument(e), all = Instrument(e);
        e.SetTrackMidiInput(t, "nota-test-not-connected", 3);
        var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>());
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-midi-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ProjectService.Save(doc, dir, e);
            using var back = Engine();
            ProjectService.Apply(ProjectService.Load(dir), back, dir);
            var ids = new List<int>();
            for (int i = 0; i < back.TrackCount; i++)
                if (back.TryGetTrackInfo(i, out var ti) && ti.IsInstrument) ids.Add(ti.Id);
            var mi = ids.Count == 2 ? back.GetTrackMidiInput(ids[0]) : default;
            var ma = ids.Count == 2 ? back.GetTrackMidiInput(ids[1]) : default;
            yield return (mi.Uid == "nota-test-not-connected" && mi.Channel == 3 && !mi.Connected,
                $"the MIDI input survives save and load, marked not connected ({mi.Uid}, ch {mi.Channel}, connected {mi.Connected})");
            yield return (ma.Uid is null && ma.Channel == 0 && ma.Connected, "a track on All inputs stays on All inputs");
            if (ids.Count != 2) yield break;
            back.SetTrackArmed(ids[0], true);
            Record(back, () => { HwTap(back, 0, 2, 60); KeyTap(back, 62); });
            var got = Pitches(back, ids[0]);
            yield return (got.Count == 0, $"a track on a device that isn't connected records nothing ({got.Count} notes)");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static int[] SlotPitches(NotaEngine e, int track, int scene)
        => e.GetSessionNotes(track, scene).Select(n => n.Pitch).OrderBy(p => p).ToArray();

    // T065: two MIDI slots in one Session take, each through its own input; one stops early.
    private static IEnumerable<(bool, string)> SessionSlotsTogether()
    {
        using var e = Engine();
        e.SetLaunchQuant(0.0);
        int a = Instrument(e), b = Instrument(e);
        e.SetTrackMidiInputSourceForTest(b, 1, 0);   // b: source 1 only
        e.SetTrackArmed(a, true);
        e.SetTrackArmed(b, true);
        e.RecordSessionSlot(a, 0);
        Beats(e, 0.1);
        e.RecordSessionSlot(b, 0);
        Beats(e, 0.1);
        yield return (e.SessionSlotState(a, 0) == 4 && e.SessionSlotState(b, 0) == 4,
            $"two MIDI slots record at once (states {e.SessionSlotState(a, 0)}, {e.SessionSlotState(b, 0)})");
        KeyTap(e, 60);           // All inputs: a only (b listens to source 1)
        HwTap(e, 1, 0, 62);      // source 1: both
        e.StopSessionRecordSlot(b);
        Beats(e, 0.1);
        yield return (e.SessionSlotState(a, 0) == 4 && e.SessionSlotState(b, 0) == 3,
            $"stopping one slot leaves the other recording, the stopped one plays (states {e.SessionSlotState(a, 0)}, {e.SessionSlotState(b, 0)})");
        HwTap(e, 1, 0, 64);
        e.StopSessionRecord();
        e.Poll();
        var pa = SlotPitches(e, a, 0);
        var pb = SlotPitches(e, b, 0);
        yield return (pa.SequenceEqual(new[] { 60, 62, 64 }) && pb.SequenceEqual(new[] { 62 }),
            $"each slot holds what its input played while it recorded ({string.Join(",", pa)} / {string.Join(",", pb)})");
        yield return (e.SessionSlotLength(a, 0) == 4.0 && e.SessionSlotLength(b, 0) == 4.0,
            $"open-ended takes are cut to whole bars ({e.SessionSlotLength(a, 0)}, {e.SessionSlotLength(b, 0)})");
        e.StopAllSession();
        e.StopTransport();
    }

    // Discard and Retry drops and restarts every MIDI slot still recording.
    private static IEnumerable<(bool, string)> SessionSlotsRetry()
    {
        using var e = Engine();
        e.SetLaunchQuant(0.0);
        int a = Instrument(e), b = Instrument(e);
        e.SetTrackArmed(a, true);
        e.SetTrackArmed(b, true);
        e.RecordSessionSlot(a, 0);
        Beats(e, 0.1);
        e.RecordSessionSlot(b, 0);
        Beats(e, 0.1);
        KeyTap(e, 60);
        var r = e.RetryTake();
        Beats(e, 0.1);
        yield return (r == RetryTakeResult.Session && e.SessionSlotState(a, 0) == 4 && e.SessionSlotState(b, 0) == 4
                      && e.GetSessionNotes(a, 0).Length == 0 && e.GetSessionNotes(b, 0).Length == 0,
            $"retry empties and restarts both MIDI slots ({r}, states {e.SessionSlotState(a, 0)}, {e.SessionSlotState(b, 0)})");
        KeyTap(e, 67);
        e.StopSessionRecord();
        e.Poll();
        var pa = SlotPitches(e, a, 0);
        var pb = SlotPitches(e, b, 0);
        yield return (pa.SequenceEqual(new[] { 67 }) && pb.SequenceEqual(new[] { 67 }),
            $"…and both keep only what followed ({string.Join(",", pa)} / {string.Join(",", pb)})");
        e.StopAllSession();
        e.StopTransport();
    }

    // The take's first slot stops: the second carries the take on (its clock, its stop).
    private static IEnumerable<(bool, string)> SessionFirstSlotStops()
    {
        using var e = Engine();
        e.SetLaunchQuant(0.0);
        int a = Instrument(e), b = Instrument(e);
        e.SetTrackArmed(a, true);
        e.SetTrackArmed(b, true);
        e.RecordSessionSlot(a, 0);
        Beats(e, 0.1);
        e.RecordSessionSlot(b, 0);
        Beats(e, 0.1);
        KeyTap(e, 60);
        e.StopSessionRecordSlot(a);
        Beats(e, 0.1);
        yield return (e.SessionSlotState(a, 0) == 3 && e.SessionSlotState(b, 0) == 4 && e.IsRecording,
            $"stopping the first slot leaves the second recording (states {e.SessionSlotState(a, 0)}, {e.SessionSlotState(b, 0)})");
        KeyTap(e, 64);
        e.StopSessionRecordSlot(b);
        e.Poll();
        var pa = SlotPitches(e, a, 0);
        var pb = SlotPitches(e, b, 0);
        yield return (!e.IsRecording && pa.SequenceEqual(new[] { 60 }) && pb.SequenceEqual(new[] { 60, 64 }),
            $"…which then ends the take, each slot with its notes ({string.Join(",", pa)} / {string.Join(",", pb)})");
        e.StopAllSession();
        e.StopTransport();
    }
    // A pedal already down when Record is pressed starts the take only on the tracks that
    // listen to the keyboard it came from (#536 review).
    private static IEnumerable<(bool, string)> PedalSeedsItsOwnTrack()
    {
        using var e = Engine();
        int p = Instrument(e), q = Instrument(e);
        e.SetTrackMidiInputSourceForTest(q, 1, 0);   // q: source 1 only
        e.SetTrackArmed(p, true);
        e.SetTrackArmed(q, true);
        e.PushMidiForTest(0, 0xB0, 0, MidiController.Sustain, 127);   // the pedal on source 0
        Beats(e, 0.05);
        Record(e, () => { HwTap(e, 0, 0, 60); HwTap(e, 1, 0, 62); });
        bool pHas = e.GetMidiClipControllers(p, 0).Contains(MidiController.Sustain);
        bool qHas = ClipCount(e, q) > 0 && e.GetMidiClipControllers(q, 0).Contains(MidiController.Sustain);
        yield return (pHas && !qHas, $"a pedal held on one keyboard seeds only the take of the track that listens to it (p {pHas}, q {qHas})");
    }

    // A scene row inserted above the slots while they record: their notes follow them to the
    // row they moved to (#536 review).
    private static IEnumerable<(bool, string)> SessionSceneInsertFollows()
    {
        using var e = Engine();
        e.SetLaunchQuant(0.0);
        int a = Instrument(e), b = Instrument(e);
        e.SetTrackArmed(a, true);
        e.SetTrackArmed(b, true);
        e.RecordSessionSlot(a, 0);
        Beats(e, 0.1);
        e.RecordSessionSlot(b, 0);
        Beats(e, 0.1);
        KeyTap(e, 60);
        e.InsertScene(0);
        Beats(e, 0.1);
        KeyTap(e, 62);
        e.StopSessionRecord();
        e.Poll();
        var a1 = SlotPitches(e, a, 1); var b1 = SlotPitches(e, b, 1);
        bool rowEmpty = e.GetSessionNotes(a, 0).Length == 0 && e.GetSessionNotes(b, 0).Length == 0;
        yield return (rowEmpty && a1.SequenceEqual(new[] { 60, 62 }) && b1.SequenceEqual(new[] { 60, 62 }),
            $"a scene inserted above recording slots: the notes follow the slots to their new row ({string.Join(",", a1)} / {string.Join(",", b1)}, new row empty {rowEmpty})");
        e.StopAllSession();
        e.StopTransport();
    }
}
