// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI controllers in clips (specs/020): the sustain pedal, pitch wheel, channel pressure and
// any CC recorded with the notes, played back (with chase and an end-of-clip release), carried
// through clip edits, saved with the project. The instrument side is observed through the
// engine's last-delivered controller value, plus an audible check on Nota Keys' pedal.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MidiControllerTests
{
    private const int Block = 512;

    private static float[] Render(NotaEngine e, int blocks)
    {
        var all = new float[blocks * Block];
        var b = new float[Block * 2];
        for (int k = 0; k < blocks; k++)
        {
            e.RenderOffline(b, Block, 48000);
            for (int i = 0; i < Block; i++) all[k * Block + i] = (b[i * 2] + b[i * 2 + 1]) * 0.5f;
        }
        return all;
    }

    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0;   // frames per beat at 120 BPM
    private const int Chunk = 256;
    private const int Sustain = MidiController.Sustain, Mod = MidiController.ModWheel, Wheel = MidiController.PitchWheel;

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in SelfTest()) yield return r;
        foreach (var r in RoundTrip()) yield return r;
        foreach (var r in Recording()) yield return r;
        foreach (var r in Playback()) yield return r;
        foreach (var r in Chase()) yield return r;
        foreach (var r in EndRelease()) yield return r;
        foreach (var r in SplitCarry()) yield return r;
        foreach (var r in OverdubFold()) yield return r;
        foreach (var r in PedalOnlyReplace()) yield return r;
        foreach (var r in Persistence()) yield return r;
        foreach (var r in LongTake()) yield return r;
        foreach (var r in SessionSlot()) yield return r;
        foreach (var r in Routed()) yield return r;
        foreach (var r in CountInPickup()) yield return r;
        foreach (var r in OldProject()) yield return r;
    }

    // Render `beats` of the engine's own clock (whatever it is doing: playing, counting in).
    private static void Beats(NotaEngine e, double beats)
    {
        var b = new float[Chunk * 2];
        for (long f = 0, n = (long)Math.Round(beats * Spb); f < n; f += Chunk) e.RenderOffline(b, Chunk, Sr);
    }

    // An instrument track at 120 BPM, timed on an absolute clock from the last Play / Record.
    private sealed class Rig : IDisposable
    {
        public readonly NotaEngine E = new();
        public readonly int Track;
        private long _frames;

        public Rig(int kind = 0, bool? loopReplace = null)
        {
            E.SetBpm(120);
            E.SetMetronome(false);
            E.SetCountIn(0);
            Track = E.AddInstrumentTrack();
            E.SetTrackBuiltinInstrument(Track, kind);
            if (loopReplace is { } rep) { E.SetLoop(true, 0.0, 4.0); E.SetLoopRecordMode(rep); }
            E.StopTransport();
            E.Seek(0.0);
            E.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        }

        public void Record() { E.SetTrackArmed(Track, true); E.SetRecording(true); _frames = 0; }
        public void Play(double from = 0.0) { E.Seek(from); E.Play(); _frames = (long)Math.Round(from * Spb); }
        public void Stop() { E.Poll(); E.SetRecording(false); E.Poll(); }

        // Render up to an absolute beat.
        public void To(double beat)
        {
            var b = new float[Chunk * 2];
            long target = (long)Math.Round(beat * Spb);
            while (_frames < target) { E.RenderOffline(b, Chunk, Sr); _frames += Chunk; }
        }

        public void Ctl(int ctl, float v, double at) { To(at); E.SendTrackController(Track, ctl, v); }
        public void Note(int pitch, double at, double len = 0.25) { To(at); E.TrackNoteOn(Track, pitch, 0.8f); To(at + len); E.TrackNoteOff(Track, pitch); }
        public float Got(int ctl) => E.DeliveredController(Track, ctl);

        // The take: (clip start, clip index) of the first MIDI clip on the track.
        public (double Start, int Index) Take()
        {
            E.Poll();
            return E.TryGetClipInfo(Track, 0, out var ci) ? (ci.StartBeat, 0) : (0, -1);
        }
        // The value of `ctl` at an absolute beat in the take, read back through the ABI.
        public float At(int ctl, double beat)
        {
            var (start, c) = Take();
            return c < 0 ? float.NaN : ValueAt(E.GetMidiClipController(Track, c, ctl), ctl, beat - start);
        }

        public void Dispose() => E.Dispose();
    }

    // Mirrors the engine's curve read (switch: last point at or before; else linear, no shaping).
    private static float ValueAt(AutomationPoint[] p, int ctl, double beat)
    {
        if (p.Length == 0) return 0f;
        if (beat <= p[0].Beat) return p[0].Value;
        if (beat >= p[^1].Beat) return p[^1].Value;
        int i = 1; while (p[i].Beat < beat) i++;
        if (MidiController.IsSwitch(ctl)) return p[i - 1].Value;
        double t = (beat - p[i - 1].Beat) / (p[i].Beat - p[i - 1].Beat);
        return (float)(p[i - 1].Value + (p[i].Value - p[i - 1].Value) * t);
    }

    private static bool Near(float a, float b, float tol = 0.02f) => Math.Abs(a - b) <= tol;
    private static string Pts(AutomationPoint[] p) => string.Join(" ", p.Select(x => $"{x.Beat:F2}:{x.Value:F2}"));

    private static IEnumerable<(bool, string)> SelfTest()
    {
        using var e = new NotaEngine();
        int st = e.MidiCtlSelfTest();
        yield return (st == 0, $"controller curves: value at a beat, slices, playback wire, recording fold (self-test {st})");
    }

    // Set / get / list / remove, reserved controllers refused, undo restores.
    private static IEnumerable<(bool, string)> RoundTrip()
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        int c = e.AddMidiClip(t, 0, 4);
        e.SetMidiClipController(t, c, Sustain, new[] { new AutomationPoint(3, 0f), new AutomationPoint(1, 1f) });
        e.SetMidiClipController(t, c, Mod, new[] { new AutomationPoint(0, 0f), new AutomationPoint(2, 0.5f), new AutomationPoint(2, 1f) });
        var list = e.GetMidiClipControllers(t, c);
        var sus = e.GetMidiClipController(t, c, Sustain);
        var mod = e.GetMidiClipController(t, c, Mod);
        yield return (list.SequenceEqual(new[] { Mod, Sustain }) && sus.Length == 2 && sus[0].Beat == 1 && sus[0].Value == 1f
                      && mod.Length == 2 && mod[1].Value == 1f,
            $"clip controllers round-trip: sorted by beat, the last point per beat kept, listed ascending ({Pts(sus)} | {Pts(mod)})");

        bool refused = true;
        foreach (int r in new[] { 6, 38, 98, 121, 130 })
            try { e.SetMidiClipController(t, c, r, new[] { new AutomationPoint(0, 1f) }); refused = false; } catch { }
        yield return (refused && e.GetMidiClipControllers(t, c).Length == 2, "data entry, NRPN, channel mode and unknown controllers are refused");

        e.SetMidiClipController(t, c, Mod, Array.Empty<AutomationPoint>());
        bool removed = e.GetMidiClipControllers(t, c).SequenceEqual(new[] { Sustain });
        e.Undo();
        yield return (removed && e.GetMidiClipController(t, c, Mod).Length == 2, "an empty set removes the curve; undo brings it back");
    }

    // Recording: the pedal, a wheel ramp and the mod wheel land in the take, thinned.
    private static IEnumerable<(bool, string)> Recording()
    {
        using (var r = new Rig())
        {
            r.Record();
            r.Ctl(Sustain, 1f, 0.5);
            r.Note(60, 0.6, 0.3);
            for (int k = 0; k <= 40; k++)   // the wheel −1 → +1 over beats 1–3; the mod wheel up at 1.5, the pedal up at 2
            {
                r.Ctl(Wheel, -1f + k / 20f, 1.0 + k * 0.05);
                if (k == 10) r.E.SendTrackController(r.Track, Mod, 0.6f);
                if (k == 20) r.E.SendTrackController(r.Track, Sustain, 0f);
            }
            r.To(4.0);
            r.Stop();
            var (start, c) = r.Take();
            var sus = c >= 0 ? r.E.GetMidiClipController(r.Track, c, Sustain) : Array.Empty<AutomationPoint>();
            var wh = c >= 0 ? r.E.GetMidiClipController(r.Track, c, Wheel) : Array.Empty<AutomationPoint>();
            yield return (sus.Length == 2 && Near(r.At(Sustain, 1.0), 1f) && Near(r.At(Sustain, 3.0), 0f)
                          && Math.Abs(start + sus[0].Beat - 0.5) < 0.02 && Math.Abs(start + sus[1].Beat - 2.0) < 0.02,
                $"the sustain pedal is recorded where it was pressed and released ({Pts(sus)}, clip at {start:F2})");
            yield return (Near(r.At(Wheel, 1.0), -1f, 0.05f) && Near(r.At(Wheel, 2.0), 0f, 0.06f) && Near(r.At(Wheel, 3.2), 1f, 0.05f) && wh.Length <= 20,
                $"a pitch-wheel ramp is recorded and thinned to its shape (41 moves → {wh.Length} points, {r.At(Wheel, 1.0):F2}/{r.At(Wheel, 2.0):F2}/{r.At(Wheel, 3.2):F2}: {Pts(wh)})");
            yield return (float.IsNaN(r.At(Mod, 1.2)) is false && r.E.GetMidiClipController(r.Track, c, Mod).Length == 1 && Near(r.At(Mod, 2.0), 0.6f)
                          && Math.Abs(start + r.E.GetMidiClipController(r.Track, c, Mod)[0].Beat - 1.5) < 0.02,
                $"the mod wheel is recorded where it moved ({Pts(r.E.GetMidiClipController(r.Track, c, Mod))})");
        }

        // A pedal already down when Record is pressed is in the take from its start.
        using (var r = new Rig())
        {
            r.E.SendTrackController(r.Track, Sustain, 1f);
            r.E.RenderOffline(new float[Chunk * 2], Chunk, Sr);
            r.Record();
            r.Note(60, 0.5);
            r.Ctl(Sustain, 0f, 1.5);
            r.To(2.0);
            r.Stop();
            yield return (Near(r.At(Sustain, 0.6), 1f) && Near(r.At(Sustain, 1.8), 0f),
                $"a pedal held when recording starts is recorded from the take's start ({r.At(Sustain, 0.6):F2}/{r.At(Sustain, 1.8):F2})");
        }

            // Disarming the track ends the take: the mod wheel's moving last stretch is still in it.
        using (var r = new Rig())
        {
            r.Record();
            r.Note(60, 0.2);
            for (int k = 0; k <= 20; k++) r.Ctl(Mod, k / 20f, 1.0 + k * 0.1);   // 0 → 1 over beats 1–3
            r.To(3.2);
            r.E.SetTrackArmed(r.Track, false);
            r.E.Poll();
            var mod = r.E.GetMidiClipController(r.Track, 0, Mod);
            yield return (mod.Length >= 2 && Near(mod[^1].Value, 1f, 0.03f),
                $"disarming the record track keeps the controllers up to its last move ({Pts(mod)})");
        }

        // Polyphonic aftertouch (an ordinary keyboard) lands in the note's own pressure.
        using (var r = new Rig())
        {
            r.Record();
            r.To(0.5);
            r.E.TrackNoteOn(r.Track, 57, 0.8f);
            for (int k = 1; k <= 10; k++) { r.To(0.5 + k * 0.05); r.E.TrackNoteExpression(r.Track, 57, NoteExpressionDim.Pressure, 0.9f); }
            r.To(1.5);
            r.E.TrackNoteOff(r.Track, 57);
            r.To(2.0);
            r.Stop();
            var n = r.E.GetClipNotes(r.Track, 0).FirstOrDefault(x => x.Pitch == 57);
            var curve = n.ExprId > 0 ? r.E.GetNoteExpression(n.ExprId) : Array.Empty<NotaExprPoint>();
            yield return (curve.Any(p => p.Dim == NoteExpressionDim.Pressure && Math.Abs(p.Value - 0.9f) < 0.01f),
                "polyphonic aftertouch is recorded as the note's pressure");
        }
    }

    // A clip's curves reach the instrument: a step for the pedal, a ramp for the mod wheel.
    private static Rig ClipRig()
    {
        var r = new Rig();
        int c = r.E.AddMidiClip(r.Track, 0, 8);
        r.E.SetClipNotes(r.Track, c, new[] { new NotaNote(60, 0, 8, 0.8f) });
        r.E.SetMidiClipController(r.Track, c, Sustain, new[] { new AutomationPoint(1, 1f), new AutomationPoint(3, 0f) });
        r.E.SetMidiClipController(r.Track, c, Mod, new[] { new AutomationPoint(0, 0f), new AutomationPoint(2, 1f) });
        return r;
    }

    private static IEnumerable<(bool, string)> Playback()
    {
        using var r = ClipRig();
        r.Play();
        r.To(0.5);
        float s0 = r.Got(Sustain), m0 = r.Got(Mod);
        r.To(1.5);
        float s1 = r.Got(Sustain), m1 = r.Got(Mod);
        r.To(3.5);
        float s2 = r.Got(Sustain), m2 = r.Got(Mod);
        yield return (Near(s0, 1f) is false && Near(s1, 1f) && Near(s2, 0f),
            $"clip playback presses and releases the pedal at its beats ({s0:F2} → {s1:F2} → {s2:F2})");
        yield return (Near(m0, 0.25f, 0.04f) && Near(m1, 0.75f, 0.04f) && Near(m2, 1f),
            $"clip playback moves the mod wheel along its curve ({m0:F2} → {m1:F2} → {m2:F2})");
    }

    private static IEnumerable<(bool, string)> Chase()
    {
        using var r = ClipRig();
        r.Play(2.5);
        r.To(2.5 + 0.02);
        yield return (Near(r.Got(Sustain), 1f) && Near(r.Got(Mod), 1f),
            $"starting playback mid-clip first sets each controller to its value there (pedal {r.Got(Sustain):F2}, mod {r.Got(Mod):F2})");
    }

    private static IEnumerable<(bool, string)> EndRelease()
    {
        using (var r = new Rig())
        {
            int c = r.E.AddMidiClip(r.Track, 0, 2);
            r.E.SetClipNotes(r.Track, c, new[] { new NotaNote(60, 0, 2, 0.8f) });
            r.E.SetMidiClipController(r.Track, c, Sustain, new[] { new AutomationPoint(0, 1f) });
            r.E.SetMidiClipController(r.Track, c, Wheel, new[] { new AutomationPoint(0, 0.5f) });
            r.Play();
            r.To(1.0);
            float inside = r.Got(Sustain), wIn = r.Got(Wheel);
            r.To(2.5);
            yield return (Near(inside, 1f) && Near(wIn, 0.5f) && Near(r.Got(Sustain), 0f) && Near(r.Got(Wheel), 0f),
                $"at a clip's end its pedal is released and the wheel centred ({inside:F2}/{wIn:F2} → {r.Got(Sustain):F2}/{r.Got(Wheel):F2})");
        }
        using (var r = ClipRig())
        {
            r.Play();
            r.To(2.0);
            float held = r.Got(Sustain);
            r.E.StopTransport();
            r.E.RenderOffline(new float[Chunk * 2], Chunk, Sr);
            r.E.RenderOffline(new float[Chunk * 2], Chunk, Sr);
            yield return (Near(held, 1f) && Near(r.Got(Sustain), 0f),
                $"stopping the transport releases a pedal the clip held ({held:F2} → {r.Got(Sustain):F2})");
        }
    }

    // Splitting a clip keeps each half sounding the same: the right half starts with the pedal down.
    private static IEnumerable<(bool, string)> SplitCarry()
    {
        using var r = ClipRig();
        int right = r.E.SplitClip(r.Track, 0, 2.0);
        var left = r.E.GetMidiClipController(r.Track, 0, Sustain);
        var rgt = right >= 0 ? r.E.GetMidiClipController(r.Track, right, Sustain) : Array.Empty<AutomationPoint>();
        var rmod = right >= 0 ? r.E.GetMidiClipController(r.Track, right, Mod) : Array.Empty<AutomationPoint>();
        yield return (right >= 0 && Near(ValueAt(left, Sustain, 1.5), 1f) && Near(ValueAt(rgt, Sustain, 0.0), 1f)
                      && Near(ValueAt(rgt, Sustain, 1.5), 0f) && Near(ValueAt(rmod, Mod, 0.0), 1f),
            $"a split carries the controllers: each half keeps its part ({Pts(left)} | {Pts(rgt)} | mod {Pts(rmod)})");
    }

    // Overdub: a later pass replaces the mod wheel only where it moved it.
    private static IEnumerable<(bool, string)> OverdubFold()
    {
        using var r = new Rig(loopReplace: false);
        r.Record();
        r.Note(60, 0.1);
        r.Ctl(Mod, 0.3f, 0.2);
        r.Ctl(Mod, 0.3f, 3.6);
        r.Ctl(Mod, 0.9f, 4.0 + 2.0);     // pass 1: from beat 2 of the loop on
        r.Ctl(Mod, 0.9f, 4.0 + 3.0);
        r.To(8.2);
        r.Stop();
        var (_, c) = r.Take();
        var mod = c >= 0 ? r.E.GetMidiClipController(r.Track, c, Mod) : Array.Empty<AutomationPoint>();
        yield return (Near(r.At(Mod, 1.0), 0.3f) && Near(r.At(Mod, 3.0), 0.9f) && r.E.GetClipNotes(r.Track, 0).Length == 1,
            $"loop overdub: a pass replaces a controller only over the range it moved it ({Pts(mod)})");
    }

    // Replace: a pass with only the pedal re-records the pedal there and keeps the notes.
    private static IEnumerable<(bool, string)> PedalOnlyReplace()
    {
        using var r = new Rig(loopReplace: true);
        r.Record();
        foreach (var at in new[] { 0.1, 1.1, 2.1, 3.1 }) r.Note(60, at);
        r.Ctl(Sustain, 1f, 0.5);
        r.Ctl(Sustain, 0f, 3.5);
        r.Ctl(Sustain, 1f, 4.0 + 2.0);   // pass 1: no notes, a shorter pedal
        r.Ctl(Sustain, 0f, 4.0 + 3.0);
        r.To(8.2);
        r.Stop();
        int notes = r.E.GetClipNotes(r.Track, 0).Length;
        yield return (notes == 4 && Near(r.At(Sustain, 1.0), 1f) && Near(r.At(Sustain, 2.5), 1f) && Near(r.At(Sustain, 3.2), 0f),
            $"loop replace: a pedal-only pass keeps the notes and re-records the pedal where it was pressed (notes {notes}, " +
            $"{r.At(Sustain, 1.0):F0}/{r.At(Sustain, 2.5):F0}/{r.At(Sustain, 3.2):F0})");
    }

    // SC-005: two minutes of continuous wheel and mod-wheel movement over a played part stay a
    // compact take (the project grows by well under 100 KB) and cost the render no more than a
    // little over the same take without them. The UI's ~30 Hz poll folds the take as it grows.
    private static IEnumerable<(bool, string)> LongTake()
    {
        (double meanUs, double maxUs, long projBytes, int points) Take(bool moveControllers)
        {
            using var r = new Rig();
            r.Record();
            var b = new float[Chunk * 2];
            var sw = new System.Diagnostics.Stopwatch();
            double total = 0, worst = 0; long blocks = 0;
            const double Beats = 240.0;   // 2 minutes at 120 BPM
            long frames = 0, end = (long)(Beats * Spb);
            int k = 0;
            while (frames < end)
            {
                double beat = frames / Spb;
                if (k % 24 == 0) { int p = 48 + (k / 24) % 24; r.E.TrackNoteOn(r.Track, p, 0.8f); }
                if (k % 24 == 12) { int p = 48 + (k / 24) % 24; r.E.TrackNoteOff(r.Track, p); }
                if (moveControllers)
                {
                    r.E.SendTrackController(r.Track, Wheel, (float)Math.Sin(beat * 1.3));
                    r.E.SendTrackController(r.Track, Mod, (float)(0.5 + 0.5 * Math.Sin(beat * 0.37)));
                }
                sw.Restart();
                r.E.RenderOffline(b, Chunk, Sr);
                double us = sw.Elapsed.TotalMilliseconds * 1000.0;
                total += us; worst = Math.Max(worst, us); blocks++;
                frames += Chunk; k++;
                if (k % 6 == 0) r.E.Poll();   // ~30 Hz, as the UI polls
            }
            r.Stop();
            int pts = 0;
            foreach (int ctl in r.E.GetMidiClipControllers(r.Track, 0)) pts += r.E.GetMidiClipController(r.Track, 0, ctl).Length;
            string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-ctl-long-" + Guid.NewGuid().ToString("N") + ".nota");
            try
            {
                ProjectService.Save(ProjectService.Capture(r.E, new TransportState(120.0, 1.0, false, false), new List<string>()), dir, r.E);
                long bytes = new FileInfo(Path.Combine(dir, ProjectService.ManifestName)).Length;
                return (total / blocks, worst, bytes, pts);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        var plain = Take(false);
        var moved = Take(true);
        long grow = moved.projBytes - plain.projBytes;
        yield return (grow < 100 * 1024 && moved.points > 10,
            $"a 2-minute take with the wheels moving all through stays compact ({moved.points} points, project +{grow / 1024.0:F1} KB)");
        double blockUs = Chunk / Sr * 1e6;
        yield return TimingBudget.Check(true, moved.meanUs < plain.meanUs * 1.25 + 5 && moved.maxUs < blockUs,
            $"…and costs the render about the same (mean {plain.meanUs:F1} → {moved.meanUs:F1} µs, worst {moved.maxUs:F0} µs of a {blockUs:F0} µs block)");
    }

    // A Session slot take: the controllers land loop-local — the moving mod wheel's last stretch
    // included (T062: a slot take ends without a record stop) — and the slot plays them back on
    // every pass.
    private static IEnumerable<(bool, string)> SessionSlot()
    {
        using var r = new Rig();
        var e = r.E;
        e.SetLaunchQuant(0.0);
        e.SetTrackArmed(r.Track, true);
        e.Seek(0);
        Beats(e, 0.01);
        e.RecordSessionSlot(r.Track, 0);
        Beats(e, 0.25);
        // Ordinary live input, as a slot take records it (track-addressed input plays only its track).
        e.SendController(Sustain, 1f);
        for (int k = 0; k <= 30; k++) { e.SendController(Mod, k / 30f); Beats(e, 0.1); }   // 0 → 1 over 3 beats
        e.SendController(Sustain, 0f);
        Beats(e, 0.5);
        e.Poll();
        e.StopSessionRecord();
        e.Poll();
        var sus = e.GetSessionController(r.Track, 0, Sustain);
        var mod = e.GetSessionController(r.Track, 0, Mod);
        double len = e.SessionSlotLength(r.Track, 0);
        yield return (sus.Length == 2 && sus[0].Beat >= 0 && sus[^1].Beat < len && Near(ValueAt(sus, Sustain, 1.0), 1f) && Near(ValueAt(sus, Sustain, 3.8), 0f),
            $"a Session slot take records the pedal loop-local ({Pts(sus)}, slot {len:F0} beats)");
        yield return (mod.Length >= 2 && Near(mod[^1].Value, 1f, 0.04f) && mod[^1].Beat > 2.9,
            $"…and the moving mod wheel to its last move, when the slot take ends ({Pts(mod)})");

        // Play the slot: the pedal is down at beat 1 of each pass, up by 3.8.
        e.StopAllSession();
        Beats(e, 0.1);
        e.LaunchSlot(r.Track, 0);
        Beats(e, 1.0);
        float p1 = r.Got(Sustain);
        Beats(e, 2.8);
        float p1up = r.Got(Sustain);
        Beats(e, 0.2 + 1.0);              // into the second pass, beat 1
        float p2 = r.Got(Sustain);
        yield return (Near(p1, 1f) && Near(p1up, 0f) && Near(p2, 1f),
            $"the slot plays its pedal back on every pass ({p1:F0} → {p1up:F0} → {p2:F0})");
        e.StopAllSession();
    }

    // Recording a track fed through "MIDI In" prints the source clip's controllers into the take.
    private static IEnumerable<(bool, string)> Routed()
    {
        using var r = new Rig();
        var e = r.E;
        int src = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(src, 0);
        int c = e.AddMidiClip(src, 0, 4);
        e.SetClipNotes(src, c, new[] { new NotaNote(60, 0, 4, 0.8f) });
        e.SetMidiClipController(src, c, Mod, new[] { new AutomationPoint(0, 0f), new AutomationPoint(2, 1f) });
        e.SetTrackMidiSource(r.Track, src);
        r.Record();
        r.To(3.0);
        r.Stop();
        yield return (Near(r.At(Mod, 1.0), 0.5f, 0.05f) && Near(r.At(Mod, 2.5), 1f, 0.03f),
            $"recording a routed track prints its source's controllers into the take ({r.At(Mod, 1.0):F2} / {r.At(Mod, 2.5):F2})");
    }

    // A pedal pressed on the count-in lands before the record start, and the take's clip opens there.
    private static IEnumerable<(bool, string)> CountInPickup()
    {
        using var r = new Rig();
        var e = r.E;
        e.Seek(16.0);
        Beats(e, 0.01);
        e.SetCountIn(1);
        e.SetTrackArmed(r.Track, true);
        e.SetRecording(true);             // 1 bar of count-in, then the take from beat 16
        Beats(e, 3.25);
        e.SendTrackController(r.Track, Sustain, 1f);
        Beats(e, 1.0);
        e.TrackNoteOn(r.Track, 60, 0.8f);
        Beats(e, 0.5);
        e.TrackNoteOff(r.Track, 60);
        e.SendTrackController(r.Track, Sustain, 0f);
        Beats(e, 0.5);
        e.Poll();
        e.SetRecording(false);
        e.Poll();
        e.TryGetClipInfo(r.Track, 0, out var ci);
        var sus = e.GetMidiClipController(r.Track, 0, Sustain);
        double at = sus.Length > 0 ? ci.StartBeat + sus[0].Beat : -1;
        yield return (sus.Length == 2 && Math.Abs(at - 15.25) < 0.03 && Math.Abs(ci.StartBeat - 15.25) < 0.03,
            $"a pedal pressed on the count-in is recorded there and the clip starts with it (pedal {at:F2}, clip {ci.StartBeat:F2})");
        e.StopTransport();
    }

    // A project saved before controllers existed (format v21) loads as it was: nothing added.
    private static IEnumerable<(bool, string)> OldProject()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-ctl-v21-" + Guid.NewGuid().ToString("N") + ".nota");
        try
        {
            using (var src = new NotaEngine())
            {
                int t = src.AddInstrumentTrack();
                int c = src.AddMidiClip(t, 0, 4);
                src.SetClipNotes(t, c, new[] { new NotaNote(60, 0, 1, 0.8f) });
                ProjectService.Save(ProjectService.Capture(src, new TransportState(120.0, 1.0, false, false), new List<string>()), dir, src);
            }
            string manifest = Path.Combine(dir, ProjectService.ManifestName);
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            root["formatVersion"] = 21;
            File.WriteAllText(manifest, root.ToJsonString());
            using var dst = new NotaEngine();
            var warn = ProjectService.Apply(ProjectService.Load(dir), dst, dir);
            int dt = dst.TryGetTrackInfo(0, out var ti) ? ti.Id : -1;
            yield return (dt > 0 && warn.Count == 0 && dst.GetClipNotes(dt, 0).Length == 1 && dst.GetMidiClipControllers(dt, 0).Length == 0,
                $"a format-v21 project loads unchanged, with no controllers (warnings {warn.Count})");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // Saved with the project; a project without them loads with none.
    private static IEnumerable<(bool, string)> Persistence()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-smoke-ctl-" + Guid.NewGuid().ToString("N") + ".nota");
        var transport = new TransportState(120.0, 1.0, false, false);
        try
        {
            using var src = new NotaEngine();
            int t = src.AddInstrumentTrack();
            int c = src.AddMidiClip(t, 0, 4);
            src.SetMidiClipController(t, c, Sustain, new[] { new AutomationPoint(1, 1f), new AutomationPoint(3, 0f) });
            src.SetMidiClipController(t, c, Wheel, new[] { new AutomationPoint(0, -0.5f), new AutomationPoint(2, 0.5f, 0.3f) });
            int plain = src.AddMidiClip(t, 8, 4);
            ProjectService.Save(ProjectService.Capture(src, transport, new List<string>()), dir, src);

            using var dst = new NotaEngine();
            var warn = ProjectService.Apply(ProjectService.Load(dir), dst, dir);
            int dt = dst.TryGetTrackInfo(0, out var ti) ? ti.Id : -1;
            var wh = dt > 0 ? dst.GetMidiClipController(dt, 0, Wheel) : Array.Empty<AutomationPoint>();
            yield return (dt > 0 && dst.GetMidiClipControllers(dt, 0).SequenceEqual(new[] { Sustain, Wheel })
                          && dst.GetMidiClipController(dt, 0, Sustain).Length == 2 && wh.Length == 2 && wh[0].Value == -0.5f && wh[1].Curve == 0.3f
                          && dst.GetMidiClipControllers(dt, 1).Length == 0,
                $"clip controllers are saved with the project and load back; a clip without them stays without ({Pts(wh)}, warnings {warn.Count})");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
