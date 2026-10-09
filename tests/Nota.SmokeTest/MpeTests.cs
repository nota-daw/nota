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
}
