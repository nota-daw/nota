// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Loop record mode (#29, specs/018-loop-record-replace): with the transport loop on, Overdub adds
// every pass to the MIDI take; Replace lets the first fresh note of a pass remove what earlier
// passes of the take recorded — a silent pass removes nothing, notes the clip held before the
// take stay, and a mode switch applies from the next pass.

using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class LoopRecordModeTests
{
    private const double Sr = 48000.0;
    private const double Spb = Sr * 60.0 / 120.0;   // frames per beat at 120 BPM
    private const int Chunk = 256;
    private static readonly double[] PhraseAt = { 0.0, 1.0, 2.0, 3.0 };
    private static readonly int[] PhrasePitch = { 60, 62, 64, 65 };

    public static IEnumerable<(bool, string)> Run()
    {
        foreach (var r in Defaults()) yield return r;
        foreach (var r in Overdub()) yield return r;
        foreach (var r in ReplaceLatestPass()) yield return r;
        foreach (var r in SilentPass()) yield return r;
        foreach (var r in ExistingNotes()) yield return r;
        foreach (var r in HeldAcrossSeam()) yield return r;
        foreach (var r in Routed()) yield return r;
        foreach (var r in SwitchMidTake()) yield return r;
        foreach (var r in ReplacedNoteReleases()) yield return r;
    }

    // A take on a 1-bar loop (beats 0–4), timed on an absolute clock: pass k spans [4k, 4k+4).
    private sealed class Rig : IDisposable
    {
        public readonly NotaEngine E = new();
        public readonly int Track, Source = -1;
        private long _frames;

        public Rig(bool replace, bool routed = false)
        {
            E.SetBpm(120);
            E.SetMetronome(false);
            E.SetCountIn(0);
            Track = E.AddInstrumentTrack();
            E.SetTrackBuiltinInstrument(Track, 0);
            if (routed)
            {
                Source = E.AddInstrumentTrack();
                E.SetTrackBuiltinInstrument(Source, 0);
                E.SetTrackMidiSource(Track, Source);
            }
            E.SetTrackArmed(Track, true);
            E.SetLoop(true, 0.0, 4.0);
            E.SetLoopRecordMode(replace);
            E.StopTransport();
            E.Seek(0.0);
            E.RenderOffline(new float[Chunk * 2], Chunk, Sr);
        }

        public void Record() { E.SetRecording(true); _frames = 0; }

        // Render up to an absolute beat of the take (beat 0 = Record pressed).
        public float To(double beat)
        {
            var b = new float[Chunk * 2];
            long target = (long)Math.Round(beat * Spb);
            double sq = 0; long n = 0;
            while (_frames < target)
            {
                E.RenderOffline(b, Chunk, Sr); _frames += Chunk;
                foreach (var v in b) { sq += v * (double)v; n++; }
            }
            return n > 0 ? (float)Math.Sqrt(sq / n) : 0f;
        }

        public void On(int pitch) { if (Source >= 0) E.TrackNoteOn(Source, pitch, 0.8f); else E.NoteOn(pitch, 0.8f); }
        public void Off(int pitch) { if (Source >= 0) E.TrackNoteOff(Source, pitch); else E.NoteOff(pitch); }

        public void Note(int pitch, double at, double len = 0.25) { To(at); On(pitch); To(at + len); Off(pitch); }

        // The 4-note phrase on pass k, shifted by `jitter` beats.
        public void Phrase(int pass, double jitter = 0.0)
        {
            for (int i = 0; i < PhraseAt.Length; i++) Note(PhrasePitch[i], 4.0 * pass + PhraseAt[i] + 0.1 + jitter);
        }

        public void Stop() { E.Poll(); E.SetRecording(false); E.Poll(); }

        // (absolute start in the loop, pitch) of every note on the record track, sorted by start.
        public List<(double At, int Pitch)> Notes()
        {
            E.Poll();
            var all = new List<(double, int)>();
            for (int c = 0; c < 64 && E.TryGetClipInfo(Track, c, out var ci); c++)
                foreach (var n in E.GetClipNotes(Track, c)) all.Add((ci.StartBeat + n.StartBeat, n.Pitch));
            return all.OrderBy(x => x.Item1).ThenBy(x => x.Item2).ToList();
        }

        public void Dispose() => E.Dispose();
    }

    private static string Show(List<(double At, int Pitch)> n) => string.Join(", ", n.Select(x => $"{x.Pitch}@{x.At:F2}"));
    private static bool Near(double a, double b, double tol = 0.05) => Math.Abs(a - b) <= tol;

    private static IEnumerable<(bool, string)> Defaults()
    {
        using var e = new NotaEngine();
        bool def = e.LoopRecordReplace;
        e.SetLoopRecordMode(true);
        bool on = e.LoopRecordReplace;
        e.SetLoopRecordMode(false);
        yield return (!def && on && !e.LoopRecordReplace, $"loop record mode: overdub by default, set/get round-trips ({def}/{on}/{e.LoopRecordReplace})");
    }

    // Overdub: a different pitch on each of three passes — all three stay (today's behaviour).
    private static IEnumerable<(bool, string)> Overdub()
    {
        using var r = new Rig(replace: false);
        r.Record();
        r.Note(60, 0.5); r.Note(62, 4.5); r.Note(64, 8.5);
        r.To(12.2);
        r.Stop();
        var n = r.Notes();
        yield return (n.Count == 3 && n.Select(x => x.Pitch).OrderBy(p => p).SequenceEqual(new[] { 60, 62, 64 }),
            $"loop record overdub: every pass adds to the take ({Show(n)})");
    }

    // Replace: the same phrase on 5 passes with jittered timing leaves exactly one phrase, the last.
    private static IEnumerable<(bool, string)> ReplaceLatestPass()
    {
        using var r = new Rig(replace: true);
        r.Record();
        double[] jit = { 0.03, -0.03, 0.02, -0.02, 0.01 };
        for (int p = 0; p < 5; p++) r.Phrase(p, jit[p]);
        r.To(20.2);
        r.Stop();
        var n = r.Notes();
        bool last = n.Count == 4 && n.Select((x, i) => Near(x.At, PhraseAt[i] + 0.1 + jit[4], 0.02) && x.Pitch == PhrasePitch[i]).All(b => b);
        yield return (last, $"loop record replace: 5 passes of a phrase leave only the last one ({Show(n)})");
    }

    // Replace: a pass with no notes removes nothing.
    private static IEnumerable<(bool, string)> SilentPass()
    {
        using var r = new Rig(replace: true);
        r.Record();
        r.Phrase(0);
        r.To(8.2);                       // pass 1: silent
        r.Stop();
        var n = r.Notes();
        yield return (n.Count == 4, $"loop record replace: a silent pass keeps the take ({Show(n)})");
    }

    // Replace: notes the clip held before the take stay.
    private static IEnumerable<(bool, string)> ExistingNotes()
    {
        using var r = new Rig(replace: true);
        r.E.AddMidiClip(r.Track, 0.0, 4.0);
        r.E.SetClipNotes(r.Track, 0, new[] { new NotaNote(48, 2.5, 0.5, 0.8f) });
        r.Record();
        r.Phrase(0); r.Phrase(1); r.Phrase(2);
        r.To(12.2);
        r.Stop();
        var n = r.Notes();
        bool kept = n.Count(x => x.Pitch == 48 && Near(x.At, 2.5, 1e-6)) == 1;
        yield return (kept && n.Count == 5, $"loop record replace: notes in the clip before the take stay ({Show(n)})");
    }

    // Replace with a note held across the seam: its continuation belongs to the new pass and
    // doesn't trigger a replace; the next fresh note removes the old pass (with the head).
    private static IEnumerable<(bool, string)> HeldAcrossSeam()
    {
        using var r = new Rig(replace: true);
        r.Record();
        r.To(3.5); r.On(67); r.To(4.5); r.Off(67);   // 67: 3.5 → seam → 0.5
        r.To(4.8);
        var mid = r.Notes();
        bool bothHalves = mid.Count(x => x.Pitch == 67) == 2;
        r.Note(69, 5.0);                              // fresh note on pass 1
        r.To(6.0);
        var after = r.Notes();
        bool headGone = after.Count == 2 && after.Any(x => x.Pitch == 67 && Near(x.At, 0.0)) && after.Any(x => x.Pitch == 69 && Near(x.At, 1.0));
        r.Note(71, 9.0);                              // fresh note on pass 2
        r.To(10.0);
        r.Stop();
        var end = r.Notes();
        yield return (bothHalves, $"loop record replace: a note held across the seam doesn't trigger a replace ({Show(mid)})");
        yield return (headGone, $"loop record replace: the next fresh note removes the old pass, the continuation stays ({Show(after)})");
        yield return (end.Count == 1 && end[0].Pitch == 71, $"loop record replace: a further pass replaces that one too ({Show(end)})");
    }

    // Routed MIDI ("MIDI In" from another track) follows the mode like the keyboard.
    private static IEnumerable<(bool, string)> Routed()
    {
        using var r = new Rig(replace: true, routed: true);
        r.Record();
        r.Phrase(0, 0.02); r.Phrase(1, -0.02); r.Phrase(2);
        r.To(12.2);
        r.Stop();
        var n = r.Notes();
        bool last = n.Count == 4 && n.Select((x, i) => Near(x.At, PhraseAt[i] + 0.1, 0.02)).All(b => b);
        yield return (last, $"loop record replace: routed MIDI keeps only the last pass ({Show(n)})");
    }

    // A switch mid-take applies from the next pass: the pass it was made on keeps overdubbing.
    private static IEnumerable<(bool, string)> SwitchMidTake()
    {
        using var r = new Rig(replace: false);
        r.Record();
        r.Note(60, 0.5);
        r.Note(62, 4.5);
        r.To(5.0);
        r.E.SetLoopRecordMode(true);
        r.Note(64, 6.0);                 // same pass as the switch: still overdub
        r.To(7.9);
        var mid = r.Notes();
        r.Note(65, 8.5);                 // next pass: the replace fires
        r.To(9.5);
        r.Stop();
        var end = r.Notes();
        yield return (mid.Count == 3, $"loop record mode: a switch mid-pass doesn't replace on that pass ({Show(mid)})");
        yield return (end.Count == 1 && end[0].Pitch == 65, $"loop record mode: the switch applies from the next pass ({Show(end)})");
    }

    // A replaced note that is sounding (played back from an earlier pass) is released, not left
    // hanging: Overdub as the control keeps sounding; Replace goes quiet once the new note ends.
    private static IEnumerable<(bool, string)> ReplacedNoteReleases()
    {
        float Run(bool replace)
        {
            using var r = new Rig(replace);
            r.Record();
            r.To(0.1); r.On(60); r.To(3.6); r.Off(60);
            r.To(3.7); r.E.Poll();                      // in the take before pass 1 plays it back
            r.To(4.5); r.On(62); r.To(4.7); r.Off(62);  // fresh note while 60 sounds
            r.To(4.8); r.E.Poll();
            r.To(5.2);
            float rest = r.To(7.5);                     // 62 is done; only 60 could still sound
            r.Stop();
            return rest;
        }
        float overdub = Run(false), replace = Run(true);
        yield return (overdub > 1e-3 && replace < 1e-4,
            $"loop record replace: a replaced note that is sounding is released, not left hanging (overdub {overdub:F4}, replace {replace:F6})");
    }
}
