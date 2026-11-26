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
        foreach (var r in SeventeenthPortNeverShares()) yield return r;
        foreach (var r in TwoPortsTheirOwnChannels()) yield return r;
        foreach (var r in ChannelSetOnOnePort()) yield return r;
        foreach (var r in DisabledPortReleasesOnlyItsNotes()) yield return r;
        foreach (var r in RemovedChannelReleasesNoteAndPedal()) yield return r;
        foreach (var r in SameKeyFromTwoPorts()) yield return r;
        foreach (var r in LegacyPicksAndProjects()) yield return r;
        foreach (var r in InputChangeUndoes()) yield return r;
    }

    // FR-022: an input change is one undo step, and redo puts it back.
    private static IEnumerable<(bool, string)> InputChangeUndoes()
    {
        using var e = Engine();
        e.MidiTestPortAdd("A");
        int t = Instrument(e);
        e.ClearUndoHistory();
        e.SetTrackMidiInputs(t, Ports(("A", Ch(3, 7))));
        bool set = !e.GetTrackMidiInputs(t).All;
        e.Undo();
        bool undone = e.GetTrackMidiInputs(t).All;
        e.Redo();
        var redone = e.GetTrackMidiInputs(t);
        yield return (set && undone && !redone.All && redone.Routes[0].Channels == Ch(3, 7), "a MIDI input change undoes and redoes as one step");
    }

    // ---- specs/115 US2: ports and channel sets --------------------------------------------------

    private static List<int> InstrumentIds(NotaEngine e)
    {
        var ids = new List<int>();
        for (int i = 0; i < e.TrackCount; i++)
            if (e.TryGetTrackInfo(i, out var ti) && ti.IsInstrument) ids.Add(ti.Id);
        return ids;
    }

    private static ushort Ch(params int[] channels) => MidiInputRoute.Mask(channels);

    private static MidiInputList Ports(params (string Port, ushort Channels)[] routes) =>
        new(false, MidiInputRoute.AllChannels, routes.Select(r => new MidiInputRoute("test:" + r.Port, r.Channels, r.Port)).ToList());

    // A note on test port `source`, channel 1-16.
    private static void On(NotaEngine e, int source, int channel, int pitch) => e.MidiTestInject(source, new byte[] { (byte)(0x90 | (channel - 1)), (byte)pitch, 100 });
    private static void Off(NotaEngine e, int source, int channel, int pitch) => e.MidiTestInject(source, new byte[] { (byte)(0x80 | (channel - 1)), (byte)pitch, 0 });
    private static void Tap(NotaEngine e, int source, int channel, int pitch)
    {
        On(e, source, channel, pitch);
        Beats(e, 0.25);
        Off(e, source, channel, pitch);
        Beats(e, 0.1);
    }

    // An External MIDI track whose instrument-level events land on test output `outPort`: what
    // the track plays, Note Off by Note Off.
    private static int Listener(NotaEngine e, string outPort)
    {
        e.MidiTestPortAdd(outPort);
        int t = e.AddExternalMidiTrack();
        e.SetExternalMidi(t, "test:" + outPort, 1);
        return t;
    }

    private static List<byte[]> Sent(NotaEngine e, string outPort) => e.MidiTestOutputRead(outPort).Select(m => m.Bytes).ToList();
    private static bool HasOff(List<byte[]> m, int pitch) => m.Any(b => (b[0] & 0xF0) == 0x80 && b.Length > 1 && b[1] == pitch);
    private static bool HasOn(List<byte[]> m, int pitch) => m.Any(b => (b[0] & 0xF0) == 0x90 && b.Length > 2 && b[1] == pitch && b[2] > 0);

    // Story 2 #1: Port A channel 3 and Port B channel 7 on one track, in one take; A on 7 and port
    // C are not its.
    private static IEnumerable<(bool, string)> TwoPortsTheirOwnChannels()
    {
        using var e = Engine();
        int a = e.MidiTestPortAdd("A"), b = e.MidiTestPortAdd("B"), c = e.MidiTestPortAdd("C");
        int t = Instrument(e);
        e.SetTrackMidiInputs(t, Ports(("A", Ch(3)), ("B", Ch(7))));
        e.SetTrackArmed(t, true);
        Record(e, () =>
        {
            Tap(e, a, 3, 60);
            Tap(e, b, 7, 62);
            Tap(e, a, 7, 64);
            Tap(e, c, 3, 65);
        });
        var got = Pitches(e, t);
        yield return (got.SequenceEqual(new[] { 60, 62 }), $"ports A ch 3 + B ch 7: both record into one take, A ch 7 and port C don't ({string.Join(",", got)})");
        var back = e.GetTrackMidiInputs(t);
        yield return (!back.All && back.Routes.Count == 2 && back.Routes[0].Channels == Ch(3) && back.Routes[1].Channels == Ch(7),
            "…and the list reads back as set");
    }

    // Story 2 #2: one port on channels 3, 7 and 11 records exactly what it monitors.
    private static IEnumerable<(bool, string)> ChannelSetOnOnePort()
    {
        using var e = Engine();
        int a = e.MidiTestPortAdd("A");
        int t = Instrument(e);
        e.SetTrackMidiInputs(t, Ports(("A", Ch(3, 7, 11))));
        e.SetTrackArmed(t, true);
        int mon = Listener(e, "Out");
        e.SetTrackMidiInputs(mon, Ports(("A", Ch(3, 7, 11))));
        e.SetTrackArmed(mon, true);
        Record(e, () =>
        {
            Tap(e, a, 3, 60);
            Tap(e, a, 4, 61);
            Tap(e, a, 7, 62);
            Tap(e, a, 11, 63);
        });
        var got = Pitches(e, t);
        var heard = Sent(e, "Out").Where(m => (m[0] & 0xF0) == 0x90 && m[2] > 0).Select(m => (int)m[1]).OrderBy(p => p).ToList();
        yield return (got.SequenceEqual(new[] { 60, 62, 63 }) && heard.SequenceEqual(got),
            $"channels 3, 7, 11 on one port: monitored and recorded alike, channel 4 left out ({string.Join(",", got)} / {string.Join(",", heard)})");
    }

    // Story 2 #3: A and B held, A taken off the list: A's note ends, B's keeps sounding.
    private static IEnumerable<(bool, string)> DisabledPortReleasesOnlyItsNotes()
    {
        using var e = Engine();
        int a = e.MidiTestPortAdd("A"), b = e.MidiTestPortAdd("B");
        int t = Listener(e, "Out");
        e.SetTrackMidiInputs(t, Ports(("A", Ch(3)), ("B", Ch(7))));
        e.SetTrackArmed(t, true);
        Beats(e, 0.1);
        On(e, a, 3, 60);
        On(e, b, 7, 64);
        Beats(e, 0.5);
        Sent(e, "Out");
        e.SetTrackMidiInputs(t, Ports(("B", Ch(7))));
        Beats(e, 0.5);
        var m = Sent(e, "Out");
        yield return (HasOff(m, 60) && !HasOff(m, 64), $"Port A taken off while A and B hold notes: A's ends, B's keeps sounding ({string.Join(" ", m.Select(Convert.ToHexString))})");
        Off(e, b, 7, 64);
        Off(e, a, 3, 60);
        Beats(e, 0.25);
        m = Sent(e, "Out");
        yield return (HasOff(m, 64) && !HasOff(m, 60), "…B's ends with its key; A's late key-up sends nothing twice");
    }

    // Story 2 #4: channels 7 and 11 held with the pedal down on 7; channel 7 removed: its note and
    // the pedal let go, 11 keeps sounding, another track's note on channel 7 is untouched.
    private static IEnumerable<(bool, string)> RemovedChannelReleasesNoteAndPedal()
    {
        using var e = Engine();
        int a = e.MidiTestPortAdd("A");
        int t = Listener(e, "Out");
        int other = Listener(e, "Other");
        e.SetTrackMidiInputs(t, Ports(("A", Ch(7, 11))));
        e.SetTrackMidiInputs(other, Ports(("A", Ch(7))));
        e.SetTrackArmed(t, true);
        e.SetTrackArmed(other, true);
        Beats(e, 0.1);
        e.MidiTestInject(a, new byte[] { 0xB6, 64, 127 }); // sustain down on channel 7
        On(e, a, 7, 60);
        On(e, a, 11, 64);
        Beats(e, 0.5);
        Sent(e, "Out");
        Sent(e, "Other");
        e.SetTrackMidiInputs(t, Ports(("A", Ch(11))));
        Beats(e, 0.5);
        var m = Sent(e, "Out");
        var o = Sent(e, "Other");
        bool pedalUp = m.Any(b => b.Length == 3 && (b[0] & 0xF0) == 0xB0 && b[1] == 64 && b[2] == 0);
        yield return (HasOff(m, 60) && pedalUp && !HasOff(m, 64), $"channel 7 removed while 7 and 11 hold: 7's note and pedal let go, 11 keeps sounding ({string.Join(" ", m.Select(Convert.ToHexString))})");
        yield return (!HasOff(o, 60), "…another track's note on channel 7 is untouched");
        e.MidiTestInject(a, new byte[] { 0xB6, 64, 0 });
        Off(e, a, 7, 60);
        Off(e, a, 11, 64);
        Beats(e, 0.25);
    }

    // Edge case: the same key from two listed ports sounds once and ends with the last to let go.
    private static IEnumerable<(bool, string)> SameKeyFromTwoPorts()
    {
        using var e = Engine();
        int a = e.MidiTestPortAdd("A"), b = e.MidiTestPortAdd("B");
        int t = Listener(e, "Out");
        e.SetTrackMidiInputs(t, Ports(("A", Ch(3)), ("B", Ch(7))));
        e.SetTrackArmed(t, true);
        Beats(e, 0.1);
        On(e, a, 3, 60);
        Beats(e, 0.25);
        On(e, b, 7, 60);
        Beats(e, 0.25);
        Off(e, a, 3, 60);
        Beats(e, 0.25);
        var first = Sent(e, "Out");
        Off(e, b, 7, 60);
        Beats(e, 0.25);
        var last = Sent(e, "Out");
        int ons = first.Count(x => (x[0] & 0xF0) == 0x90 && x[2] > 0);
        yield return (ons == 1 && !HasOff(first, 60) && HasOff(last, 60),
            $"the same key from two ports sounds once and ends with the last to let go ({ons} on, then {string.Join(" ", last.Select(Convert.ToHexString))})");
    }

    // FR-001, SC-008: the specs/086 picks behave as before, and a v35 project loads unchanged; a
    // list survives save and load.
    private static IEnumerable<(bool, string)> LegacyPicksAndProjects()
    {
        using var e = Engine();
        e.MidiTestPortAdd("A");
        int t = Instrument(e), all = Instrument(e), multi = Instrument(e);
        e.SetTrackMidiInput(t, "test:A", 3);
        var l = e.GetTrackMidiInputs(t);
        yield return (!l.All && l.Routes.Count == 1 && l.Routes[0].Uid == "test:A" && l.Routes[0].Channels == Ch(3) && e.GetTrackMidiInput(t).Channel == 3,
            "a device and a channel (specs/086) is a one-port list on that channel");
        e.SetTrackMidiInput(all, null, 5);
        var la = e.GetTrackMidiInputs(all);
        yield return (la.All && la.AllChannels == Ch(5) && e.GetTrackMidiInput(all) is { Uid: null, Channel: 5 }, "All inputs on one channel stays All inputs");
        e.SetTrackMidiInputs(multi, Ports(("A", Ch(3, 7)), ("Gone", Ch(9))));
        var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>());
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-midi-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ProjectService.Save(doc, dir, e);
            using (var back = Engine())
            {
                ProjectService.Apply(ProjectService.Load(dir), back, dir);
                var ids = InstrumentIds(back);
                var m = back.GetTrackMidiInputs(ids[2]);
                yield return (!m.All && m.Routes.Count == 2 && m.Routes[0].Channels == Ch(3, 7) && m.Routes[1].Uid == "test:Gone"
                              && m.Routes[1].Name == "Gone",
                    "a port list survives save and load, a missing port kept by its saved name");
            }
            // The same project as v35 wrote it: no list, only the specs/086 fields.
            string json = Path.Combine(dir, "project.json");
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(json))!.AsObject();
            root["formatVersion"] = 35;
            foreach (var tr in root["tracks"]!.AsArray()) tr!.AsObject().Remove("midiInputs");
            File.WriteAllText(json, root.ToJsonString());
            using (var old = Engine())
            {
                ProjectService.Apply(ProjectService.Load(dir), old, dir);
                var ids = InstrumentIds(old);
                var a0 = old.GetTrackMidiInput(ids[0]);
                var a1 = old.GetTrackMidiInput(ids[1]);
                yield return (a0.Uid == "test:A" && a0.Channel == 3 && a1.Uid is null && a1.Channel == 5,
                    $"a v35 project's MIDI inputs load unchanged ({a0.Uid} ch {a0.Channel}; {a1.Uid ?? "All"} ch {a1.Channel})");
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // #1011 (specs/115 R7): past 16 enabled inputs a port gets no source index instead of sharing
    // the last one, so a track listening to input 16 can never hear input 17.
    private static IEnumerable<(bool, string)> SeventeenthPortNeverShares()
    {
        int st = NotaEngine.MidiIndexSelfTest(17);
        yield return (st == 0, $"#1011: 17 enabled inputs → 16 distinct indices, the 17th over the limit (self-test {st})");
        yield return (NotaEngine.MidiIndexSelfTest(40) == 0, "#1011: …and every later one too");
        using var e = Engine();
        var sources = new List<int>();
        for (int i = 0; i < 17; i++) sources.Add(e.MidiTestPortAdd($"limit-{i}"));
        bool distinct = sources.Take(16).Distinct().Count() == 16 && sources.Take(16).All(s => s >= 0);
        yield return (distinct && sources[16] == -1,
            $"#1011: the 17th port gets no source rather than another port's ({string.Join(",", sources)})");
        int t = Instrument(e);
        e.SetTrackMidiInputSourceForTest(t, sources[15], 0);
        e.SetTrackArmed(t, true);
        Record(e, () =>
        {
            e.MidiTestInject(sources[16], new byte[] { 0x90, 70, 100 }); // nothing to inject into
            Beats(e, 0.25);
            e.MidiTestInject(sources[16], new byte[] { 0x80, 70, 0 });
            HwTap(e, sources[15], 0, 72);
        });
        var got = Pitches(e, t);
        yield return (got.SequenceEqual(new[] { 72 }), $"#1011: a track on input 16 records only input 16 ({string.Join(",", got)})");
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
