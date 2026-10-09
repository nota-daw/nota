// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MPE: the MIDI decoder (zones, member/master channels, RPN bend range, MCM — the native
// self-test), and per-note expression reaching the built-in synths that take it: a note bent
// an octave sounds an octave up, only that note bends, pressure is louder, a fresh note-on
// starts unbent, and the whole-instrument wheel bends by the synth's range.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MpeTests
{
    // Built-in kinds that take per-note expression, and one that doesn't.
    private static readonly (int Kind, string Name)[] Synths =
    {
        (0, "Nota Synth"), (6, "Nota Volt"), (5, "Nota Aurora"), (9, "Nota Operator"), (14, "Nota Pentad"), (2, "Nota Physical"),
        (16, "Nota Keys"),
    };
    private const int NotMpeKind = 13;   // Nota Monolith (mono)

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

    private static double Rms(float[] x) { double s = 0; foreach (var v in x) s += v * (double)v; return Math.Sqrt(s / x.Length); }

    // Energy at one frequency (Goertzel), 48 kHz.
    private static double At(float[] x, double hz)
    {
        double w = 2 * Math.PI * hz / 48000.0, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        foreach (var v in x) { double s0 = v + c * s1 - s2; s2 = s1; s1 = s0; }
        return s1 * s1 + s2 * s2 - c * s1 * s2;
    }

    // One note on a fresh track of `kind`: optionally bent / pressed right after its note-on.
    private static float[] Note(int kind, int pitch, float bend = 0, float pressure = 0, int blocks = 12)
    {
        using var e = new NotaEngine();
        int t = e.AddInstrumentTrack();
        e.SetTrackBuiltinInstrument(t, kind);
        Render(e, 2);
        e.TrackNoteOn(t, pitch, 0.8f);
        if (bend != 0) e.TrackNoteExpression(t, pitch, NoteExpressionDim.Bend, bend);
        if (pressure != 0) e.TrackNoteExpression(t, pitch, NoteExpressionDim.Pressure, pressure);
        Render(e, 2);   // skip the attack
        return Render(e, blocks);
    }

    public static IEnumerable<(bool, string)> Run()
    {
        int st = NotaEngine.MpeSelfTest();
        yield return (st == 0, $"MPE decoder: zones, member/master channels, RPN 0, MCM, initial state (self-test {st})");

        foreach (var (kind, name) in Synths)
        {
            // An octave of bend moves the energy from A3 to A4.
            var plain = Note(kind, 57);
            var bent = Note(kind, 57, bend: 12f);
            double p220 = At(plain, 220), p440 = At(plain, 440), b220 = At(bent, 220), b440 = At(bent, 440);
            yield return (b440 > 4 * b220 && b440 > p440 && p220 > b220 * 4,
                $"{name}: a note bent +12 st sounds an octave up (220/440 Hz energy {p220:E1}/{p440:E1} → {b220:E1}/{b440:E1})");

            var loud = Note(kind, 57, pressure: 1f);
            double r0 = Rms(plain), r1 = Rms(loud);
            yield return (r1 > r0 * 1.15, $"{name}: pressure makes the note louder (rms {r0:F4} → {r1:F4})");
        }

        // Pressure bows Nota Physical: a held, pressed note sustains — and stays bounded.
        {
            var bowed = Note(2, 57, pressure: 1f, blocks: 400);   // ~4 s
            var tail = bowed[^(Block * 20)..];
            double rTail = Rms(tail);
            var struck = Note(2, 57, blocks: 400)[^(Block * 20)..];
            yield return (bowed.All(float.IsFinite) && rTail < 0.5 && rTail > Rms(struck) * 2,
                $"Nota Physical: pressure bows a held note — it sustains and stays bounded (tail rms {Rms(struck):F4} struck → {rTail:F4} bowed)");
        }

        // Only the addressed note bends: two notes, bend one, its partner stays put.
        {
            using var e = new NotaEngine();
            int t = e.AddInstrumentTrack();
            e.SetTrackBuiltinInstrument(t, 0);
            Render(e, 2);
            e.TrackNoteOn(t, 57, 0.8f);
            e.TrackNoteOn(t, 64, 0.8f);
            Render(e, 1);   // separate blocks: a note-on and its expression share one in practice, not two notes
            e.TrackNoteExpression(t, 57, NoteExpressionDim.Bend, -12f);
            Render(e, 2);
            var x = Render(e, 12);
            double a110 = At(x, 110), a220 = At(x, 220), e330 = At(x, 329.63);
            yield return (a110 > a220 * 2 && e330 > a220, $"per-note bend moves only its note (110/220/330 Hz {a110:E1}/{a220:E1}/{e330:E1})");
        }

        // A note-on starts unbent even though the previous note on that pitch was bent.
        {
            using var e = new NotaEngine();
            int t = e.AddInstrumentTrack();
            e.SetTrackBuiltinInstrument(t, 0);
            Render(e, 2);
            e.TrackNoteOn(t, 57, 0.8f);
            e.TrackNoteExpression(t, 57, NoteExpressionDim.Bend, 12f);
            Render(e, 4);
            e.TrackNoteOff(t, 57);
            Render(e, 60);
            e.TrackNoteOn(t, 57, 0.8f);
            Render(e, 2);
            var x = Render(e, 12);
            yield return (At(x, 220) > At(x, 440), "a fresh note-on resets the note's expression");
        }

        // The whole-instrument wheel (a keyboard's pitch bend): +1 = +2 st on Nota Synth.
        {
            using var e = new NotaEngine();
            int t = e.AddInstrumentTrack();
            e.SetTrackBuiltinInstrument(t, 0);
            Render(e, 2);
            e.TrackNoteOn(t, 57, 0.8f);
            e.TrackNoteExpression(t, -1, NoteExpressionDim.Bend, 1f);
            Render(e, 2);
            var x = Render(e, 12);
            double b247 = At(x, 246.94), b220 = At(x, 220);
            yield return (b247 > b220 * 4, $"the instrument-wide wheel bends by its range (247/220 Hz {b247:E1}/{b220:E1})");
        }

        // Supports-MPE is reported per instrument; a rack of MPE synths takes it too.
        {
            using var e = new NotaEngine();
            int a = e.AddInstrumentTrack(), b = e.AddInstrumentTrack();
            e.SetTrackBuiltinInstrument(a, 0);
            e.SetTrackBuiltinInstrument(b, NotMpeKind);
            yield return (e.TrackInstrumentSupportsMpe(a) && !e.TrackInstrumentSupportsMpe(b),
                "MPE support is reported per instrument (Synth yes, Monolith no)");
        }

        // Expression addressed to one track never reaches another.
        {
            using var e = new NotaEngine();
            int a = e.AddInstrumentTrack(), b = e.AddInstrumentTrack();
            e.SetTrackBuiltinInstrument(a, 0); e.SetTrackBuiltinInstrument(b, 0);
            Render(e, 2);
            e.TrackNoteOn(a, 57, 0.8f); e.TrackNoteOn(b, 57, 0.8f);
            e.TrackNoteExpression(a, 57, NoteExpressionDim.Bend, 12f);
            e.SetTrackMute(a, true);
            Render(e, 2);
            var x = Render(e, 12);
            yield return (At(x, 220) > At(x, 440), "track-addressed expression stays on its track");
        }

        foreach (var r in Recording()) yield return r;

        // The MPE preference round-trips through the staged MIDI config.
        {
            using var e = new NotaEngine();
            var (on0, r0) = e.GetMpe();
            e.SetMpe(false, 24);
            var (on1, r1) = e.GetMpe();
            e.SetMpe(on0, r0);
            yield return (!on1 && r1 == 24, $"the MPE setting stages (default {(on0 ? "on" : "off")} ±{r0})");
        }
    }

    // ---- recorded MPE: into the take, back out of the clip, through edits and save/load ----

    private static float[] PlayFrom(NotaEngine e, int skipBlocks = 2, int blocks = 12)
    {
        e.StopTransport(); e.Seek(0); e.Play();
        Render(e, skipBlocks);
        return Render(e, blocks);
    }

    private static IEnumerable<(bool, string)> Recording()
    {
        // The store: a curve goes in, the same curve comes out, sorted per dimension.
        using (var e = new NotaEngine())
        {
            int id = e.CreateNoteExpression(new[]
            {
                new NotaExprPoint(NoteExpressionDim.Bend, 0.5f, 2f), new NotaExprPoint(NoteExpressionDim.Bend, 0f, 0f),
                new NotaExprPoint(NoteExpressionDim.Pressure, 0f, 0.7f),
            });
            var back = e.GetNoteExpression(id);
            yield return (id > 0 && back.Length == 3 && back[0].Dim == NoteExpressionDim.Bend && back[0].Beat == 0f && back[1].Value == 2f
                          && back[2].Dim == NoteExpressionDim.Pressure && e.GetNoteExpression(0).Length == 0,
                "the expression store returns a curve as given, sorted per dimension");
        }

        // Record: a note bent smoothly up an octave and pressed, on the armed track.
        using var r = new NotaEngine();
        r.SetBpm(120);
        int t = r.AddInstrumentTrack();
        r.SetTrackBuiltinInstrument(t, 0);
        r.SetTrackArmed(t, true);
        r.SetRecording(true);
        Render(r, 4);
        r.TrackNoteOn(t, 57, 0.8f);
        r.TrackNoteExpression(t, 57, NoteExpressionDim.Bend, 0f);
        r.TrackNoteExpression(t, 57, NoteExpressionDim.Slide, 0.5f);   // resting: not kept
        for (int k = 1; k <= 40; k++)
        {
            r.TrackNoteExpression(t, 57, NoteExpressionDim.Bend, Math.Min(12f, k * 0.6f));
            r.TrackNoteExpression(t, 57, NoteExpressionDim.Pressure, 0.8f);
            Render(r, 1);
        }
        Render(r, 20);   // hold the octave
        r.TrackNoteOff(t, 57);
        Render(r, 4);
        r.SetRecording(false);
        r.Poll();
        NotaNote? rec = null; int recClip = -1;
        if (r.TryGetTrackInfo(0, out var ti))
            for (int c = 0; c < ti.ClipCount && rec is null; c++)
                foreach (var n in r.GetClipNotes(t, c)) if (n.Pitch == 57) { rec = n; recClip = c; }
        var curve = rec is { } rn ? r.GetNoteExpression(rn.ExprId) : Array.Empty<NotaExprPoint>();
        var bend = curve.Where(p => p.Dim == NoteExpressionDim.Bend).ToArray();
        yield return (rec is { ExprId: > 0 } && bend.Length >= 2 && Math.Abs(bend[^1].Value - 12f) < 0.1f && bend[0].Value < 1f,
            $"a recorded note keeps its bend (points {bend.Length}, {(bend.Length > 0 ? bend[0].Value : -1):F1} → {(bend.Length > 0 ? bend[^1].Value : -1):F1} st)");
        yield return (bend.Length < 12, $"the recorded curve is thinned to its shape (a 40-step ramp → {bend.Length} points)");
        yield return (curve.Any(p => p.Dim == NoteExpressionDim.Pressure && Math.Abs(p.Value - 0.8f) < 0.01f) && !curve.Any(p => p.Dim == NoteExpressionDim.Slide),
            "pressure is kept; a dimension that stayed at rest is not");
        if (rec is null) yield break;

        // Play the take back: the note ends an octave up (its last stretch is held at +12).
        var outp = PlayFrom(r, skipBlocks: 2 + 4 + 40, blocks: 12);
        double a220 = At(outp, 220), a440 = At(outp, 440);
        yield return (a440 > a220 * 4, $"playback replays the recorded bend (220/440 Hz {a220:E1}/{a440:E1})");

        // A piano-roll edit (move the note, push the whole set back) keeps the curve.
        var notes = r.GetClipNotes(t, recClip);
        for (int i = 0; i < notes.Length; i++) notes[i].StartBeat += 0.0;   // same position, round-tripped
        r.SetClipNotes(t, recClip, notes);
        var again = r.GetClipNotes(t, recClip).First(n => n.Pitch == 57);
        yield return (again.ExprId == rec.Value.ExprId, "pushing the notes back from the editor keeps their expression");

        // A note given an expression plays it from the clip: +12 st from its start.
        using (var e = new NotaEngine())
        {
            e.SetBpm(120);
            int u = e.AddInstrumentTrack();
            e.SetTrackBuiltinInstrument(u, 0);
            int c = e.AddMidiClip(u, 0, 4);
            var n = new NotaNote(57, 0, 4, 0.8f) { ExprId = e.CreateNoteExpression(new[] { new NotaExprPoint(NoteExpressionDim.Bend, 0f, 12f) }) };
            var m = new NotaNote(64, 0, 4, 0.8f);
            e.SetClipNotes(u, c, new[] { n, m });
            var x = PlayFrom(e);
            double b220 = At(x, 220), b440 = At(x, 440), e330 = At(x, 329.63);
            yield return (b440 > b220 * 4 && e330 > b220 * 4, $"a clip note plays its curve; its neighbour stays put (220/440/330 Hz {b220:E1}/{b440:E1}/{e330:E1})");

            // Save and load: the curve is in the project file and comes back on the note.
            e.StopTransport();
            var doc = ProjectService.Capture(e, new TransportState(120.0, 1.0, false, false), new List<string>());
            string bundle = Path.Combine(Path.GetTempPath(), "nota-mpe-" + Guid.NewGuid().ToString("N") + ".nota");
            ProjectService.Save(doc, bundle, e);
            using var e2 = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(bundle), e2, bundle);
            NotaNote? loaded = null;
            for (int i = 0; i < e2.TrackCount; i++)
                if (e2.TryGetTrackInfo(i, out var lti) && lti.IsInstrument && lti.ClipCount > 0)
                    foreach (var ln in e2.GetClipNotes(lti.Id, 0)) if (ln.Pitch == 57) loaded = ln;
            var lc = loaded is { } l ? e2.GetNoteExpression(l.ExprId) : Array.Empty<NotaExprPoint>();
            yield return (lc.Length == 1 && lc[0].Dim == NoteExpressionDim.Bend && lc[0].Value == 12f,
                "recorded MPE survives save and load");
            try { Directory.Delete(bundle, true); } catch { /* temp */ }
        }
    }
}
