// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI routing (specs/115, #1103): an External MIDI track sends its post-MIDI-effect stream to a
// MIDI output, releases what it owns on every route change, survives unplug and reconnect, and
// can follow a project output role; the MIDI routes panel's query reads it all back. Driven
// without hardware: test ports stand in for devices and record what Nota sends them, with when
// each message was due on the render clock.

using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class MidiRoutingTests
{
    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0; // frames per beat at 120 BPM
    private const int Chunk = 256;
    private const int Arp = 0;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in ArpSessionClipToChannel5()) yield return r;
        foreach (var r in LiveInputThroughArp()) yield return r;
        foreach (var r in EveryRouteChangeReleases()) yield return r;
        foreach (var r in UnplugAndReturn()) yield return r;
        foreach (var r in TwoRoutesOneChannel()) yield return r;
        foreach (var r in MissingOutputSendsNothing()) yield return r;
        foreach (var r in KeepChannelsIsMpe()) yield return r;
        foreach (var r in EightArpTracksDropNothing()) yield return r;
        foreach (var r in RoutesOverview()) yield return r;
        foreach (var r in OutputRoleRebind()) yield return r;
    }

    // ---- US3: the MIDI routes overview --------------------------------------------------------

    // Story 3: A on a controller channel, B taking MIDI from A, C on another port, D sending out —
    // one row each; filters by source and destination; a missing port flagged; names follow
    // renames; reading changes nothing (FR-021).
    private static IEnumerable<(bool, string)> RoutesOverview()
    {
        using var e = Engine();
        e.MidiTestPortAdd("Keys");
        e.MidiTestPortAdd("Pads");
        int a = e.AddInstrumentTrack(), b = e.AddInstrumentTrack(), c = e.AddInstrumentTrack();
        e.SetTrackMidiInputs(a, new MidiInputList(false, MidiInputRoute.AllChannels, new[] { new MidiInputRoute("test:Keys", MidiInputRoute.Mask(new[] { 2 }), "Keys") }));
        e.SetTrackMidiSource(b, a);
        e.SetTrackMidiInputs(c, new MidiInputList(false, MidiInputRoute.AllChannels, new[] { new MidiInputRoute("test:Pads", MidiInputRoute.AllChannels, "Pads") }));
        int d = External(e, "Synth", 5);
        e.SetTrackName(a, "Lead");
        double pos = e.PositionBeats;
        var rows = Nota.Application.Midi.MidiRoutesQuery.Rows(e);
        var ra = rows.FirstOrDefault(r => r.TrackId == a);
        var rb = rows.FirstOrDefault(r => r.TrackId == b);
        var rd = rows.FirstOrDefault(r => r.TrackId == d);
        yield return (rows.Count == 4 && ra?.Inputs is [{ Name: "Keys", Detail: "2" }] && rb?.MidiFrom?.Name == "Lead"
                      && rd?.Destination is { Name: "Synth", Detail: "5", Availability: Nota.Application.Midi.RouteAvailability.Available },
            $"MIDI routes: one row per instrument track — inputs with channels, MIDI In by the source's current name, External MIDI's port and channel ({rows.Count} rows)");
        var fromA = Nota.Application.Midi.MidiRoutesQuery.Rows(e, source: "track:" + a);
        var toSynth = Nota.Application.Midi.MidiRoutesQuery.Rows(e, destination: "test:Synth");
        var fromKeys = Nota.Application.Midi.MidiRoutesQuery.Rows(e, source: "test:Keys");
        yield return (fromA.Select(r => r.TrackId).SequenceEqual(new[] { b }) && toSynth.Select(r => r.TrackId).SequenceEqual(new[] { d })
                      && fromKeys.Select(r => r.TrackId).SequenceEqual(new[] { a }),
            "…filtered by a source track, a source port or a destination");
        e.MidiTestPortRemove("Keys");
        var gone = Nota.Application.Midi.MidiRoutesQuery.Rows(e).First(r => r.TrackId == a);
        yield return (gone.Inputs is [{ Availability: Nota.Application.Midi.RouteAvailability.Missing, Name: "Keys" }] && gone.HasMissing,
            "…a disconnected controller's route stays listed, marked missing, by its saved name");
        yield return (e.PositionBeats == pos && e.GetTrackMidiSource(b) == a && e.GetExternalMidi(d)?.OutputUid == "test:Synth",
            "…and reading it changes neither playback nor routing");
    }

    // ---- US4: output roles ---------------------------------------------------------------------

    // Story 4: two tracks follow Stage Synth on channels 2 and 5; the port goes, a similarly named
    // spare gets nothing; rebinding is refused while playing, moves both when stopped, keeps their
    // channels, is one undo step, and survives save and load.
    private static IEnumerable<(bool, string)> OutputRoleRebind()
    {
        using var e = Engine();
        e.MidiTestPortAdd("X");
        e.MidiTestPortAdd("X2");
        e.MidiTestPortAdd("Y");
        string role = Nota.Application.Midi.MidiOutputRoles.Create(e, "Stage Synth", "test:X", "X");
        int t2 = e.AddExternalMidiTrack(), t5 = e.AddExternalMidiTrack();
        e.SetExternalMidi(t2, "", 2);
        e.SetExternalMidi(t5, "", 5);
        Nota.Application.Midi.MidiOutputRoles.Assign(e, t2, role);
        Nota.Application.Midi.MidiOutputRoles.Assign(e, t5, role);
        yield return (e.GetExternalMidi(t2) is { OutputUid: "test:X", Channel: 2 } dd && dd.RoleId == role && e.GetExternalMidi(t5)?.Channel == 5,
            "roles: two tracks follow Stage Synth on their own channels");
        e.MidiTestPortRemove("X");
        TrackArm.Set(e, t2, true, additive: false);
        Beats(e, 0.1);
        e.TrackNoteOn(t2, 60, 0.8f);
        Beats(e, 0.25);
        e.TrackNoteOff(t2, 60);
        Beats(e, 0.25);
        int spare = e.MidiTestOutputRead("X2").Count;   // reading lets the MIDI out thread look at the ports
        yield return (e.ExternalMidiStatus(t2) == 2 && spare == 0,
            "…its port gone, the role reads unavailable and a similarly named spare gets nothing");
        var preview = Nota.Application.Midi.MidiOutputRoles.UsedBy(e, role);
        yield return (preview.OrderBy(x => x).SequenceEqual(new[] { t2, t5 }.OrderBy(x => x)), "…the rebind preview lists exactly the two tracks");
        e.Play();
        Beats(e, 0.1); // the transport is rolling once a block has run
        bool refused = false;
        try { Nota.Application.Midi.MidiOutputRoles.Rebind(e, role, "test:Y", "Y"); } catch (InvalidOperationException) { refused = true; }
        e.StopTransport();
        Beats(e, 0.1);
        yield return (refused && e.GetExternalMidi(t2)?.OutputUid == "test:X", "…refused while the transport plays");
        e.ClearUndoHistory();
        Nota.Application.Midi.MidiOutputRoles.Rebind(e, role, "test:Y", "Y");
        e.TrackNoteOn(t2, 62, 0.8f);
        e.TrackNoteOn(t5, 64, 0.8f);
        Beats(e, 0.25);
        e.TrackNoteOff(t2, 62);
        e.TrackNoteOff(t5, 64);
        Beats(e, 0.25);
        var y = e.MidiTestOutputRead("Y");
        bool on2 = y.Any(m => IsOn(m.Bytes) && m.Bytes[1] == 62 && (m.Bytes[0] & 0x0F) == 1);
        bool on5 = y.Any(m => IsOn(m.Bytes) && m.Bytes[1] == 64 && (m.Bytes[0] & 0x0F) == 4);
        yield return (on2 && on5, $"…rebound with the transport stopped: both play the new port, on channels 2 and 5 ({Hex(y)})");
        var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>());
        e.Undo();
        yield return (e.GetExternalMidi(t2)?.OutputUid == "test:X" && e.GetExternalMidi(t5)?.OutputUid == "test:X"
                      && e.MidiRoles().First().OutputUid == "test:X", "…one undo puts the role and both tracks back");
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-midi-roles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            ProjectService.Save(doc, dir, e);
            using var back = Engine();
            ProjectService.Apply(ProjectService.Load(dir), back, dir);
            var ext = new List<ExternalMidiDestination>();
            for (int i = 0; i < back.TrackCount; i++)
                if (back.TryGetTrackInfo(i, out var ti) && back.GetExternalMidi(ti.Id) is { } x) ext.Add(x);
            yield return (back.MidiRoles() is [{ Name: "Stage Synth", OutputUid: "test:Y" }] && ext.Count == 2
                          && ext.All(x => x.RoleId == role && x.OutputUid == "test:Y") && ext.Select(x => x.Channel).OrderBy(c => c).SequenceEqual(new[] { 2, 5 }),
                "…saved and reopened, both tracks still follow the role, no per-track edit");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        bool deleteRefused = false;
        try { Nota.Application.Midi.MidiOutputRoles.Delete(e, role); } catch (ArgumentException) { deleteRefused = true; }
        yield return (deleteRefused && e.MidiRoles().Count == 1, "…a role tracks still follow can't be deleted");
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static NotaEngine Engine()
    {
        var e = new NotaEngine();
        e.SetBpm(120);
        e.SetMetronome(false);
        e.SetCountIn(0);
        return e;
    }

    private static void Beats(NotaEngine e, double beats)
    {
        var b = new float[Chunk * 2];
        int frames = (int)Math.Round(beats * Spb);
        for (int done = 0; done < frames; done += Chunk) { e.RenderOffline(b, Chunk, Sr); e.Poll(); }
    }

    // An External MIDI track sending to test port `port` on `channel`.
    private static int External(NotaEngine e, string port, int channel)
    {
        e.MidiTestPortAdd(port);
        int t = e.AddExternalMidiTrack();
        e.SetExternalMidi(t, "test:" + port, channel);
        return t;
    }

    private static bool IsOn(byte[] m) => m.Length == 3 && (m[0] & 0xF0) == 0x90 && m[2] > 0;
    private static bool IsOff(byte[] m) => m.Length == 3 && ((m[0] & 0xF0) == 0x80 || ((m[0] & 0xF0) == 0x90 && m[2] == 0));

    // Note Ons that no later Note Off (same channel and pitch) or All Notes Off ends.
    private static int Unpaired(IReadOnlyList<MidiSentMessage> msgs)
    {
        var open = new Dictionary<(int, int), int>();
        foreach (var m in msgs)
        {
            var b = m.Bytes;
            int ch = b[0] & 0x0F;
            if (IsOn(b)) open[(ch, b[1])] = open.GetValueOrDefault((ch, b[1])) + 1;
            else if (IsOff(b)) { if (open.TryGetValue((ch, b[1]), out int n) && n > 0) open[(ch, b[1])] = n - 1; }
            else if ((b[0] & 0xF0) == 0xB0 && b.Length == 3 && (b[1] == 123 || b[1] == 120))
                foreach (var k in open.Keys.Where(k => k.Item1 == ch).ToList()) open[k] = 0;
        }
        return open.Values.Sum();
    }

    private static string Hex(IEnumerable<MidiSentMessage> msgs, int max = 12) =>
        string.Join(" ", msgs.Take(max).Select(m => Convert.ToHexString(m.Bytes)));

    // ---- US1: External MIDI -------------------------------------------------------------------

    // Story 1 #1: a Session clip through Nota Arp reaches the port on channel 5, every Note On
    // paired, in order, at the arp's steady step on the render clock.
    private static IEnumerable<(bool, string)> ArpSessionClipToChannel5()
    {
        using var e = Engine();
        int t = External(e, "X", 5);
        e.AddMidiEffect(t, Arp);
        e.AddSessionMidiClip(t, 0, 4.0);
        e.SetSessionNotes(t, 0, new[] { new NotaNote(60, 0.0, 4.0, 0.9f), new NotaNote(64, 0.0, 4.0, 0.9f) });
        e.Play();
        e.LaunchSlot(t, 0);
        Beats(e, 3.0);
        e.StopSlot(t);
        Beats(e, 1.0);
        e.StopTransport();
        Beats(e, 0.25);
        var msgs = e.MidiTestOutputRead("X");
        var ons = msgs.Where(m => IsOn(m.Bytes)).ToList();
        bool ch5 = msgs.All(m => (m.Bytes[0] & 0x0F) == 4);
        bool ordered = msgs.Zip(msgs.Skip(1)).All(p => p.First.DueNs <= p.Second.DueNs);
        var steps = ons.Zip(ons.Skip(1)).Select(p => (double)(p.Second.DueNs - p.First.DueNs)).Where(d => d > 0).ToList();
        bool steady = steps.Count >= 2 && steps.Max() - steps.Min() < 1e9 / Sr * 2; // within 2 samples
        yield return (ons.Count >= 4 && ch5, $"External MIDI: a Session clip through Nota Arp reaches the port on channel 5 ({ons.Count} notes; {Hex(msgs)})");
        yield return (Unpaired(msgs) == 0, $"…every Note On has its Note Off ({Unpaired(msgs)} left sounding)");
        yield return (ordered && steady, $"…in order, at the arp's steady step on the render clock (steps {(steps.Count > 0 ? steps.Min() / 1e6 : 0):F2}–{(steps.Count > 0 ? steps.Max() / 1e6 : 0):F2} ms)");
    }

    // Story 1 #2: the same stream from live input.
    private static IEnumerable<(bool, string)> LiveInputThroughArp()
    {
        using var e = Engine();
        int t = External(e, "X", 5);
        e.AddMidiEffect(t, Arp);
        TrackArm.Set(e, t, true, additive: false);
        e.Play();
        Beats(e, 0.1);
        e.TrackNoteOn(t, 60, 0.8f);
        Beats(e, 2.0);
        e.TrackNoteOff(t, 60);
        Beats(e, 1.0);
        var msgs = e.MidiTestOutputRead("X");
        int ons = msgs.Count(m => IsOn(m.Bytes));
        yield return (ons >= 3 && msgs.All(m => (m.Bytes[0] & 0x0F) == 4) && Unpaired(msgs) == 0,
            $"External MIDI: a key held through Nota Arp plays the port on channel 5 and lets go with the key ({ons} notes, {Unpaired(msgs)} left)");
    }

    // Story 1 #3, FR-010: stop, remove the track, re-route, mute, undo — each while a clip note
    // sounds — leave nothing sounding at the output it went to.
    private static IEnumerable<(bool, string)> EveryRouteChangeReleases()
    {
        foreach (var change in new[] { "stop", "remove", "re-route", "mute", "undo" })
        {
            using var e = Engine();
            int t = External(e, "X", 5);
            e.MidiTestPortAdd("Y");
            e.AddMidiClip(t, 0.0, 8.0);
            e.SetClipNotes(t, 0, new[] { new NotaNote(60, 0.0, 8.0, 0.9f) });
            if (change == "undo") e.SetExternalMidi(t, "test:Y", 5); // undo goes back to X
            string at = change == "undo" ? "Y" : "X";
            e.Play();
            Beats(e, 1.0);
            bool sent = e.MidiTestOutputRead(at).Any(m => IsOn(m.Bytes));
            switch (change)
            {
                case "stop": e.StopTransport(); break;
                case "remove": e.RemoveTrack(t); break;
                case "re-route": e.SetExternalMidi(t, "test:Y", 5); break;
                case "mute": e.SetTrackMute(t, true); break;
                case "undo": e.Undo(); break;
            }
            Beats(e, 0.5);
            var after = e.MidiTestOutputRead(at);
            bool released = after.Any(m => IsOff(m.Bytes) && m.Bytes[1] == 60 && (m.Bytes[0] & 0x0F) == 4);
            yield return (sent && released, $"External MIDI: {change} while a clip note sounds sends its Note Off to test:{at} ({Hex(after)})");
            if (change == "re-route")
            {
                var y = e.MidiTestOutputRead("Y");
                yield return (!y.Any(m => IsOff(m.Bytes)), $"…and nothing of it to the new output ({Hex(y)})");
            }
            if (change == "undo")
                yield return (e.GetExternalMidi(t)?.OutputUid == "test:X", "…and undo restores the old destination");
        }
    }

    // Story 1 #4, FR-011: unplugged while a key is held, nothing reaches the port; when it comes
    // back, the held note's Note Off, Sustain off and All Notes Off go out before anything new.
    private static IEnumerable<(bool, string)> UnplugAndReturn()
    {
        using var e = Engine();
        int t = External(e, "X", 5);
        TrackArm.Set(e, t, true, additive: false);
        Beats(e, 0.1);
        e.TrackNoteOn(t, 60, 0.8f);
        Beats(e, 0.5);
        e.MidiTestOutputRead("X");
        e.MidiTestPortRemove("X");
        e.MidiTestOutputRead("X");                 // the service notices it gone
        int gone = e.ExternalMidiStatus(t);
        e.TrackNoteOn(t, 64, 0.8f);                // nothing reaches a missing device
        Beats(e, 0.5);
        e.TrackNoteOff(t, 64);
        Beats(e, 0.25);
        e.MidiTestPortAdd("X");                    // plugged back in
        var back = e.MidiTestOutputRead("X");
        e.TrackNoteOn(t, 67, 0.8f);
        Beats(e, 0.25);
        var next = e.MidiTestOutputRead("X");
        bool order = back.Count >= 3 && IsOff(back[0].Bytes) && back[0].Bytes[1] == 60
                     && back.Any(m => m.Bytes.SequenceEqual(new byte[] { 0xB4, 64, 0 }))
                     && back.Any(m => m.Bytes.SequenceEqual(new byte[] { 0xB4, 123, 0 }))
                     && !back.Any(m => IsOn(m.Bytes) || (IsOff(m.Bytes) && m.Bytes[1] == 64));
        yield return (gone == 2, $"External MIDI: an unplugged output reads as unavailable (status {gone})");
        yield return (order, $"…on its return: the held note's Note Off, Sustain off and All Notes Off, nothing else ({Hex(back)})");
        yield return (next.Any(m => IsOn(m.Bytes) && m.Bytes[1] == 67) && e.ExternalMidiStatus(t) == 1,
            $"…then it plays again ({Hex(next)})");
        e.TrackNoteOff(t, 60);
        e.TrackNoteOff(t, 67);
    }

    // Two routes on one output and channel: each releases its own note, one reset per channel.
    private static IEnumerable<(bool, string)> TwoRoutesOneChannel()
    {
        using var e = Engine();
        int a = External(e, "X", 2), b = External(e, "X", 2);
        TrackArm.Set(e, a, true, additive: false);
        TrackArm.Set(e, b, true, additive: true);
        Beats(e, 0.1);
        e.TrackNoteOn(a, 60, 0.8f);
        e.TrackNoteOn(b, 64, 0.8f);
        Beats(e, 0.5);
        e.MidiTestOutputRead("X");
        e.MidiTestPortRemove("X");
        e.MidiTestOutputRead("X");
        e.MidiTestPortAdd("X");
        var back = e.MidiTestOutputRead("X");
        int resets = back.Count(m => m.Bytes.SequenceEqual(new byte[] { 0xB1, 123, 0 }));
        bool both = back.Any(m => IsOff(m.Bytes) && m.Bytes[1] == 60) && back.Any(m => IsOff(m.Bytes) && m.Bytes[1] == 64);
        yield return (both && resets == 1, $"two routes on one output and channel: each sends its Note Off, one All Notes Off ({Hex(back)})");
        e.TrackNoteOff(a, 60);
        e.TrackNoteOff(b, 64);
    }

    // Story 1 #5, SC-003: a destination that isn't there sends nothing — not to a similarly
    // named port either.
    private static IEnumerable<(bool, string)> MissingOutputSendsNothing()
    {
        using var e = Engine();
        e.MidiTestPortAdd("Synth 2");
        int t = e.AddExternalMidiTrack();
        e.SetExternalMidi(t, "test:Synth", 1);
        TrackArm.Set(e, t, true, additive: false);
        Beats(e, 0.1);
        e.TrackNoteOn(t, 60, 0.8f);
        Beats(e, 0.5);
        e.TrackNoteOff(t, 60);
        Beats(e, 0.25);
        var near = e.MidiTestOutputRead("Synth 2");
        yield return (e.ExternalMidiStatus(t) == 2 && near.Count == 0,
            $"External MIDI: a missing output reads as unavailable and a similarly named port gets nothing (status {e.ExternalMidiStatus(t)}, {near.Count} messages)");
    }

    // Story 1 #7, research R5: Keep channels sends the MPE configuration, then each note on its own
    // member channel with its pitch bend there; switching to channel 5 releases those and plays on 5.
    private static IEnumerable<(bool, string)> KeepChannelsIsMpe()
    {
        using var e = Engine();
        int src = e.MidiTestPortAdd("Seaboard");
        int t = External(e, "X", 0);
        TrackArm.Set(e, t, true, additive: false);
        Beats(e, 0.1);
        e.MidiTestInject(src, new byte[] { 0x91, 60, 100 });   // MPE: member channel 2
        e.MidiTestInject(src, new byte[] { 0x92, 64, 100 });   // member channel 3
        e.MidiTestInject(src, new byte[] { 0xE1, 0x00, 0x50 }); // a bend on the first note
        Beats(e, 0.5);
        var msgs = e.MidiTestOutputRead("X");
        bool config = msgs.Count >= 3 && msgs[0].Bytes.SequenceEqual(new byte[] { 0xB0, 101, 0 })
                      && msgs[1].Bytes.SequenceEqual(new byte[] { 0xB0, 100, 6 }) && msgs[2].Bytes.SequenceEqual(new byte[] { 0xB0, 6, 15 });
        var on60 = msgs.FirstOrDefault(m => IsOn(m.Bytes) && m.Bytes[1] == 60);
        var on64 = msgs.FirstOrDefault(m => IsOn(m.Bytes) && m.Bytes[1] == 64);
        int c60 = on60.Bytes is null ? -1 : on60.Bytes[0] & 0x0F, c64 = on64.Bytes is null ? -1 : on64.Bytes[0] & 0x0F;
        bool bendThere = msgs.Any(m => (m.Bytes[0] & 0xF0) == 0xE0 && (m.Bytes[0] & 0x0F) == c60);
        yield return (config, $"Keep channels: the MPE configuration message goes first ({Hex(msgs, 5)})");
        yield return (c60 > 0 && c64 > 0 && c60 != c64 && bendThere,
            $"…each note on its own member channel, its bend on that channel (channels {c60 + 1}, {c64 + 1}; {Hex(msgs)})");
        e.SetExternalMidi(t, "test:X", 5);
        Beats(e, 0.25);
        e.MidiTestInject(src, new byte[] { 0x91, 67, 100 });
        Beats(e, 0.25);
        var after = e.MidiTestOutputRead("X");
        bool released = after.Any(m => IsOff(m.Bytes) && m.Bytes[1] == 60 && (m.Bytes[0] & 0x0F) == c60);
        bool on5 = after.Any(m => IsOn(m.Bytes) && m.Bytes[1] == 67 && (m.Bytes[0] & 0x0F) == 4);
        yield return (released && on5, $"…switched to channel 5: the member channels' notes end, the next note plays on 5 ({Hex(after)})");
        foreach (var b in new byte[][] { [0x81, 60, 0], [0x82, 64, 0], [0x81, 67, 0] }) e.MidiTestInject(src, b);
    }

    // SC-005: eight External MIDI tracks with Nota Arp for a minute lose no message.
    private static IEnumerable<(bool, string)> EightArpTracksDropNothing()
    {
        using var e = Engine();
        var tracks = new List<int>();
        for (int i = 0; i < 8; i++)
        {
            int t = External(e, "P" + i, i + 1);
            e.AddMidiEffect(t, Arp);
            e.AddMidiClip(t, 0.0, 120.0);
            e.SetClipNotes(t, 0, new[] { new NotaNote(48 + i, 0.0, 120.0, 0.8f), new NotaNote(55 + i, 0.0, 120.0, 0.8f) });
            tracks.Add(t);
        }
        e.Play();
        Beats(e, 120.0); // a minute at 120 BPM
        uint dropped = 0;
        int notes = 0;
        foreach (var (t, i) in tracks.Select((t, i) => (t, i)))
        {
            dropped += e.ExternalMidiDropped(t);
            notes += e.MidiTestOutputRead("P" + i).Count(m => IsOn(m.Bytes));
        }
        yield return (dropped == 0 && notes > 8 * 100, $"eight External MIDI tracks through Nota Arp for a minute drop nothing ({notes} notes, {dropped} dropped)");
    }
}
