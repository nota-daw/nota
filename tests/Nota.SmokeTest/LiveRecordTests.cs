// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Live MIDI recording (#56, specs/017): notes held into a take are readable while held (the
// growing-note preview) and match what is stored on release; notes played on the count-in and
// keys already down when Record is pressed are recorded where they started — the take's clip
// moves its left edge back, and before 1.1 a note keeps its head and sounds from the clip start.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class LiveRecordTests
{
    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0;   // frames per beat at 120 BPM
    private const int Chunk = 256;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in Snapshot()) yield return r;
        foreach (var r in Release()) yield return r;
        foreach (var r in CountIn()) yield return r;
        foreach (var r in HeldAcrossRecord()) yield return r;
        foreach (var r in Routed()) yield return r;
        foreach (var r in SessionSlot()) yield return r;
        foreach (var r in RoutedHeldAcrossRecord()) yield return r;
        foreach (var r in SessionAudioTake()) yield return r;
        foreach (var r in SessionCountIn()) yield return r;
    }

    // Render about `beats` beats in small chunks (events are timed per block).
    private static float Beats(NotaEngine e, double beats)
    {
        var b = new float[Chunk * 2];
        int frames = (int)Math.Round(beats * Spb);
        double sq = 0; int n = 0;
        for (int done = 0; done < frames; done += Chunk)
        {
            e.RenderOffline(b, Chunk, Sr);
            foreach (var v in b) { sq += v * (double)v; n++; }
        }
        return n > 0 ? (float)Math.Sqrt(sq / n) : 0f;
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

    private static int Held(NotaEngine e, NotaRecordHeldNote[] buf, out int track, out int scene, out double now)
        => e.RecordHeldNotes(buf, out track, out scene, out now);

    private static List<(NotaClipInfo Info, NotaNote[] Notes)> Clips(NotaEngine e, int track)
    {
        var all = new List<(NotaClipInfo, NotaNote[])>();
        for (int c = 0; c < 64 && e.TryGetClipInfo(track, c, out var ci); c++) all.Add((ci, e.GetClipNotes(track, c)));
        return all;
    }

    private static bool Near(double a, double b, double tol = 0.05) => Math.Abs(a - b) <= tol;

    // A held note is readable while held: the take's track, its pitch and start, a clock that runs.
    private static IEnumerable<(bool, string)> Snapshot()
    {
        using var e = Armed(out int t);
        var buf = new NotaRecordHeldNote[256];
        yield return (Held(e, buf, out int t0, out int s0, out _) == 0 && t0 == 0 && s0 == -1,
            "held notes: nothing to read while not recording");
        e.SetCountIn(0);
        e.SetRecording(true);
        Beats(e, 1.0);
        e.NoteOn(60, 0.8f);
        double pressed = e.PositionBeats;
        Beats(e, 0.5);
        int n = Held(e, buf, out int tr, out int sc, out double now1);
        yield return (n == 1 && buf[0].Pitch == 60 && buf[0].Source == 0 && tr == t && sc == -1,
            $"held notes: a held key reads back on the take's track (n={n}, pitch={(n > 0 ? buf[0].Pitch : -1)}, track={tr}/{t})");
        yield return (n == 1 && Near(buf[0].StartBeat, pressed) && Math.Abs(buf[0].Velocity - 0.8f) < 1e-3,
            $"held notes: the start is the note-on beat ({(n > 0 ? buf[0].StartBeat : -1):F3} vs {pressed:F3})");
        Beats(e, 1.0);
        Held(e, buf, out _, out _, out double now2);
        yield return (now2 > now1 + 0.9 && now1 > pressed, $"held notes: the clock runs with the playhead ({now1:F2} → {now2:F2})");
        yield return (e.RecordHeldNotes(Array.Empty<NotaRecordHeldNote>(), out _, out _, out _) == 1,
            "held notes: an empty buffer still returns the count");
        e.NoteOff(60);
        e.SetRecording(false);
    }

    // On release the note leaves the held set and is stored as it was drawn; stopping with a key
    // down stores nothing for it.
    private static IEnumerable<(bool, string)> Release()
    {
        using var e = Armed(out int t);
        var buf = new NotaRecordHeldNote[256];
        e.SetCountIn(0);
        e.SetRecording(true);
        Beats(e, 0.5);
        e.NoteOn(64, 0.7f);
        Beats(e, 1.5);
        Held(e, buf, out _, out _, out _);
        double heldStart = buf[0].StartBeat;
        e.NoteOff(64);
        double released = e.PositionBeats;
        Beats(e, 0.1);
        int n = Held(e, buf, out _, out _, out _);
        e.Poll();
        var stored = Clips(e, t).SelectMany(c => c.Notes.Select(x => (c.Info.StartBeat + x.StartBeat, x))).Where(p => p.x.Pitch == 64).ToList();
        yield return (n == 0, "release: the note leaves the held set");
        yield return (stored.Count == 1 && Near(stored[0].Item1, heldStart, 1e-6) && Near(stored[0].Item1 + stored[0].x.LengthBeats, released),
            $"release: stored once, from the held start to the release (n={stored.Count})");

        e.NoteOn(67, 0.7f);
        Beats(e, 0.5);
        e.SetRecording(false);
        Beats(e, 0.1);
        e.Poll();
        int after = Held(e, buf, out int tr, out _, out _);
        bool none67 = !Clips(e, t).Any(c => c.Notes.Any(x => x.Pitch == 67));
        yield return (after == 0 && tr == 0 && none67, "release: stopping with a key down leaves no held note and stores none");
        e.NoteOff(67);
    }

    // Notes on the count-in: a pickup before bar 5 is stored before it and the clip moves back
    // to it; a take at 1.1 keeps the head before the clip and sounds the rest from 1.1.
    private static IEnumerable<(bool, string)> CountIn()
    {
        var buf = new NotaRecordHeldNote[256];
        using (var e = Armed(out int t))
        {
            e.Seek(16.0);
            Beats(e, 0.01);
            e.SetCountIn(1);
            e.SetRecording(true);           // from stop: 1 bar of count-in, then the take at beat 16
            Beats(e, 3.25);
            e.NoteOn(60, 0.8f);
            Beats(e, 0.1);
            int n = Held(e, buf, out _, out _, out double now);
            yield return (n == 1 && Near(buf[0].StartBeat, 15.25) && now < 16.0,
                $"count-in: a pickup reads back before the take start ({(n > 0 ? buf[0].StartBeat : -1):F2}, clock {now:F2})");
            Beats(e, 0.4);
            e.NoteOff(60);                  // released before bar 5
            Beats(e, 1.0);
            e.Poll();
            e.SetRecording(false);
            e.Poll();
            var clips = Clips(e, t);
            var take = clips.FirstOrDefault(c => c.Notes.Any(x => x.Pitch == 60));
            double abs = take.Notes is null ? -1 : take.Info.StartBeat + take.Notes.First(x => x.Pitch == 60).StartBeat;
            yield return (take.Notes is not null && Near(abs, 15.25) && Near(take.Info.StartBeat, 15.25),
                $"count-in: the pickup is stored where it was played and the clip starts there (note {abs:F2}, clip {(take.Notes is null ? -1 : take.Info.StartBeat):F2})");
            e.StopTransport();
            e.Seek(15.0);
            Beats(e, 0.01);
            e.Play();
            float silent = Beats(e, 0.2), pickup = Beats(e, 0.5);
            yield return (pickup > 1e-3 && pickup > silent * 3, $"count-in: playback sounds the pickup (rms {silent:F4} → {pickup:F4})");
            e.StopTransport();
        }

        using (var e = Armed(out int t))
        {
            e.Seek(0.0);
            Beats(e, 0.01);
            e.SetCountIn(1);
            e.SetRecording(true);
            Beats(e, 3.0);
            e.NoteOn(62, 0.8f);             // one beat before 1.1
            Beats(e, 3.0);                  // held into the take, to beat 2
            e.NoteOff(62);
            Beats(e, 0.2);
            e.SetRecording(false);
            e.Poll();
            var take = Clips(e, t).FirstOrDefault(c => c.Notes.Any(x => x.Pitch == 62));
            var note = take.Notes?.First(x => x.Pitch == 62) ?? default;
            yield return (take.Notes is not null && take.Info.StartBeat == 0.0 && Near(note.StartBeat, -1.0) && Near(note.LengthBeats, 3.0),
                $"count-in at 1.1: the clip starts at 1.1, the note keeps its head (start {note.StartBeat:F2}, length {note.LengthBeats:F2})");
            e.StopTransport();
            e.Seek(0.0);
            Beats(e, 0.01);
            e.Play();
            float head = Beats(e, 0.5);
            yield return (head > 1e-3, $"count-in at 1.1: the note sounds from the clip start (rms {head:F4})");
            e.StopTransport();
        }
    }

    // Keys already down when Record is pressed join the take: from their press beat while the
    // transport rolls, from the count-in's first beat when pressed while stopped.
    private static IEnumerable<(bool, string)> HeldAcrossRecord()
    {
        using (var e = Armed(out int t))
        {
            e.SetCountIn(0);
            e.Seek(0.0);
            Beats(e, 0.01);
            e.Play();
            Beats(e, 3.0);
            e.NoteOn(65, 0.8f);
            double pressed = e.PositionBeats;
            Beats(e, 1.0);
            e.SetRecording(true);           // rolling: no count-in, the take opens at ~beat 4
            Beats(e, 2.0);
            e.NoteOff(65);
            Beats(e, 0.2);
            e.Poll();
            e.SetRecording(false);
            e.Poll();
            var take = Clips(e, t).FirstOrDefault(c => c.Notes.Any(x => x.Pitch == 65));
            double abs = take.Notes is null ? -1 : take.Info.StartBeat + take.Notes.First(x => x.Pitch == 65).StartBeat;
            yield return (take.Notes is not null && Near(abs, pressed) && Near(take.Info.StartBeat, pressed),
                $"held across Record: stored from the press beat ({abs:F2} vs {pressed:F2}), the clip moved back to it");
            e.StopTransport();
        }

        using (var e = Armed(out int t))
        {
            var buf = new NotaRecordHeldNote[256];
            e.Seek(8.0);
            Beats(e, 0.01);
            e.NoteOn(69, 0.8f);             // pressed while stopped
            Beats(e, 0.2);
            e.SetCountIn(1);
            e.SetRecording(true);
            Beats(e, 0.2);
            int n = Held(e, buf, out _, out _, out _);
            yield return (n == 1 && buf[0].Pitch == 69 && Near(buf[0].StartBeat, 4.0),
                $"held across Record: a key pressed while stopped starts at the count-in's first beat ({(n > 0 ? buf[0].StartBeat : -1):F2})");
            e.NoteOff(69);
            Beats(e, 0.1);
            e.SetRecording(false);
        }
    }

    // MIDI routed into the armed track ("MIDI In") is held and previewed as source 1.
    private static IEnumerable<(bool, string)> Routed()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        int dst = e.AddInstrumentTrack();
        int src = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(dst, 0);
        e.SetTrackBuiltinInstrument(src, 0);
        e.SetTrackMidiSource(dst, src);
        e.SetTrackArmed(dst, true);
        e.StopTransport();
        e.SetCountIn(0);
        e.SetRecording(true);
        Beats(e, 0.5);
        e.TrackNoteOn(src, 71, 0.8f);
        Beats(e, 0.5);
        var buf = new NotaRecordHeldNote[256];
        int n = Held(e, buf, out int tr, out _, out _);
        bool seen = Enumerable.Range(0, Math.Min(n, buf.Length)).Any(i => buf[i].Pitch == 71 && buf[i].Source == 1);
        yield return (seen && tr == dst, $"routed: a note held on the source reads back as routed MIDI (n={n})");
        e.TrackNoteOff(src, 71);
        Beats(e, 0.2);
        e.Poll();
        e.SetRecording(false);
        e.Poll();
        int stored = Clips(e, dst).Sum(c => c.Notes.Count(x => x.Pitch == 71));
        yield return (stored == 1, $"routed: on release it is stored once (n={stored})");
    }

    // A note already sounding on the routed source when Record is pressed joins the take from
    // where it started (convergence T036).
    private static IEnumerable<(bool, string)> RoutedHeldAcrossRecord()
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        int dst = e.AddInstrumentTrack();
        int src = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(dst, 0);
        e.SetTrackBuiltinInstrument(src, 0);
        e.SetTrackMidiSource(dst, src);
        e.SetTrackArmed(dst, true);
        e.SetCountIn(0);
        e.StopTransport();
        e.Seek(0.0);
        Beats(e, 0.01);
        e.Play();
        Beats(e, 2.0);
        e.TrackNoteOn(src, 74, 0.8f);
        double pressed = e.PositionBeats;
        Beats(e, 1.0);
        e.SetRecording(true);           // rolling: the take opens at ~beat 3, the note is down since ~2
        Beats(e, 1.0);
        e.TrackNoteOff(src, 74);
        Beats(e, 0.2);
        e.Poll();
        e.SetRecording(false);
        e.Poll();
        var take = Clips(e, dst).FirstOrDefault(c => c.Notes.Any(x => x.Pitch == 74));
        double abs = take.Notes is null ? -1 : take.Info.StartBeat + take.Notes.First(x => x.Pitch == 74).StartBeat;
        int count = Clips(e, dst).Sum(c => c.Notes.Count(x => x.Pitch == 74));
        yield return (take.Notes is not null && count == 1 && Near(abs, pressed),
            $"routed held across Record: stored once from the source's press beat ({abs:F2} vs {pressed:F2}, n={count})");
        e.StopTransport();
    }

    // An audio take into a Session slot shows no MIDI held notes, even right after a MIDI take
    // (convergence T035). Needs a capture device; without one the slot can't record audio.
    private static IEnumerable<(bool, string)> SessionAudioTake()
    {
        using var e = Armed(out int t);
        e.SetCountIn(0);
        e.SetRecording(true);           // a MIDI take first, so the engine remembers its track
        Beats(e, 0.5);
        e.SetRecording(false);
        e.SetTrackArmed(t, false);
        int a = e.AddAudioTrack();
        e.SetLaunchQuant(0.0);
        e.RecordSessionSlot(a, 0);
        if (!e.IsRecording)
        {
            yield return (true, "session audio take: no MIDI preview (skipped — no capture device here)");
            yield break;
        }
        e.NoteOn(60, 0.8f);
        Beats(e, 0.5);
        int n = e.RecordHeldNotes(new NotaRecordHeldNote[256], out int tr, out _, out _);
        yield return (n == 0 && tr == 0, $"session audio take: no MIDI preview (n={n}, track={tr})");
        e.NoteOff(60);
        Beats(e, 0.1);
        e.StopSessionRecord();
        e.StopAllSession();
        e.StopTransport();
    }

    // A Session slot take reports its scene and slot-local starts.
    private static IEnumerable<(bool, string)> SessionSlot()
    {
        using var e = Armed(out int t);
        e.SetLaunchQuant(0.0);
        e.Seek(0);
        Beats(e, 0.01);
        e.RecordSessionSlot(t, 0);
        Beats(e, 1.5);
        e.NoteOn(72, 0.9f);
        Beats(e, 0.5);
        var buf = new NotaRecordHeldNote[256];
        int n = Held(e, buf, out int tr, out int sc, out double now);
        double len = e.SessionSlotLength(t, 0);
        yield return (n == 1 && tr == t && sc == 0 && buf[0].StartBeat >= 0 && buf[0].StartBeat < Math.Max(len, 1e9) && now >= 0,
            $"session: a held key reads back on the recording slot (n={n}, scene={sc}, start={(n > 0 ? buf[0].StartBeat : -1):F2})");
        e.NoteOff(72);
        Beats(e, 0.2);
        e.Poll();
        e.StopSessionRecord();
        yield return (e.GetSessionNotes(t, 0).Any(x => x.Pitch == 72), "session: the note is stored into the slot on release");
        e.StopAllSession();
        e.StopTransport();
    }
    // Recording into a Session slot from stop counts in first (#158): the clicks run with the
    // transport and the slot still, the slot starts on the sample the count-in runs out (not a
    // launch quantum later), a note held into the take starts the slot and one released in
    // the count-in is dropped. Stop during the count-in leaves the slot as it was; a take
    // started while the transport rolls doesn't count in.
    private static IEnumerable<(bool, string)> SessionCountIn()
    {
        using (var e = Armed(out int t))
        {
            e.SetLaunchQuant(4.0);
            e.Seek(16.0);
            Beats(e, 0.01);
            e.SetCountIn(1);
            e.RecordSessionSlot(t, 0);
            yield return (e.CountInBeats > 0 && !e.IsPlaying, $"session count-in: armed from stop ({e.CountInBeats:F2} beats)");
            Beats(e, 1.0);
            e.NoteOn(64, 0.8f);             // played and released inside the count-in
            Beats(e, 0.5);
            e.NoteOff(64);
            Beats(e, 2.0);                  // 3.5 beats into the bar
            e.TryGetSessionRecordTarget(out _, out _, out double waiting);
            yield return (!e.IsPlaying && e.SessionPlayingSlot(t) < 0 && waiting == 0.0,
                $"session count-in: slot and transport wait (playing={e.IsPlaying}, slot={e.SessionPlayingSlot(t)}, elapsed={waiting:F2})");
            e.NoteOn(60, 0.8f);             // a pickup held into the take
            Beats(e, 1.0);                  // the count-in ran out half a beat ago
            e.NoteOff(60);
            e.TryGetSessionRecordTarget(out _, out _, out double el);
            yield return (e.IsPlaying && e.SessionPlayingSlot(t) == 0 && Near(el, 0.5),
                $"session count-in: the slot starts as it runs out, not a quantum later (elapsed {el:F2})");
            Beats(e, 0.25);
            e.NoteOn(67, 0.8f);
            Beats(e, 0.25);
            e.NoteOff(67);
            Beats(e, 0.1);
            e.Poll();
            e.StopSessionRecord();
            e.Poll();
            var notes = e.GetSessionNotes(t, 0);
            var pickup = notes.FirstOrDefault(x => x.Pitch == 60);
            yield return (notes.Any(x => x.Pitch == 60) && Near(pickup.StartBeat, 0.0) && Near(pickup.LengthBeats, 0.5),
                $"session count-in: a note held into the take starts the slot (start {pickup.StartBeat:F2}, length {pickup.LengthBeats:F2})");
            yield return (!notes.Any(x => x.Pitch == 64) && notes.Any(x => x.Pitch == 67),
                $"session count-in: a note over before the take is dropped, the take records ({string.Join(",", notes.Select(x => x.Pitch))})");
            e.StopAllSession();
            e.StopTransport();
        }

        using (var e = Armed(out int t))
        {
            e.Seek(0.0);
            Beats(e, 0.01);
            e.SetCountIn(1);
            bool couldUndo = e.CanUndo;
            e.RecordSessionSlot(t, 0);
            Beats(e, 1.0);
            e.StopTransport();              // Stop during the count-in cancels the take
            Beats(e, 0.1);
            yield return (!e.IsPlaying && e.CountInBeats == 0 && !e.IsRecording && e.SessionSlotLength(t, 0) == 0.0,
                $"session count-in: Stop cancels it and the slot is empty again (len {e.SessionSlotLength(t, 0):F2})");
            yield return (e.CanUndo == couldUndo, "session count-in: a cancelled take leaves no undo step");
            Beats(e, 4.0);
            yield return (!e.IsPlaying && e.SessionPlayingSlot(t) < 0, "session count-in: a cancelled count-in never starts the slot");

            e.RecordSessionSlot(t, 0);
            Beats(e, 1.0);
            e.StopSessionRecord();          // the slot's own stop, too
            Beats(e, 0.1);
            yield return (!e.IsPlaying && e.CountInBeats == 0 && e.SessionSlotLength(t, 0) == 0.0,
                "session count-in: stopping the take from the slot cancels it too");

            e.SetLaunchQuant(0.0);
            e.Play();
            Beats(e, 0.5);
            e.RecordSessionSlot(t, 0);      // transport rolling: no count-in
            Beats(e, 0.1);
            yield return (e.CountInBeats == 0 && e.SessionPlayingSlot(t) == 0, "session count-in: none while the transport rolls");
            e.StopSessionRecord();
            e.StopAllSession();
            e.StopTransport();
            e.SetCountIn(0);
        }
    }
}
