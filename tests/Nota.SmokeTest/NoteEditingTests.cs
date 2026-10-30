// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Piano-roll note editing (specs/007-midi-note-editing): the overwrite rule, length and
// velocity steps. Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --notes`.

using Nota.Application;
using Nota.Application.Editing;

namespace Nota.SmokeTest;

internal static class NoteEditingTests
{
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;

    private static NotaNote N(int pitch, double start, double end, float vel = 0.8f) => new(pitch, start, end - start, vel);

    private static (List<NotaNote> Notes, List<int> Placed) Settle(IReadOnlyList<NotaNote> notes, params int[] placed)
    {
        var outN = new List<NotaNote>();
        var outP = new List<int>();
        NoteOverwrite.Settle(notes, placed, outN, outP);
        return (outN, outP);
    }

    private static bool Is(NotaNote n, int pitch, double start, double end)
        => n.Pitch == pitch && Near(n.StartBeat, start) && Near(n.StartBeat + n.LengthBeats, end);

    // Fate must predict Settle for every unplaced note (the mark shows what settling does).
    private static bool FateAgrees(IReadOnlyList<NotaNote> notes, params int[] placed)
    {
        var (after, _) = Settle(notes, placed);
        int k = 0;
        for (int i = 0; i < notes.Count; i++)
        {
            var f = NoteOverwrite.Fate(i, notes, placed);
            if (Array.IndexOf(placed, i) >= 0) { if (f.Kind != NoteFateKind.Kept) return false; k++; continue; }
            switch (f.Kind)
            {
                case NoteFateKind.Removed: break;
                case NoteFateKind.Kept: if (!Is(after[k++], notes[i].Pitch, notes[i].StartBeat, notes[i].StartBeat + notes[i].LengthBeats)) return false; break;
                case NoteFateKind.Cut: if (!Is(after[k++], notes[i].Pitch, notes[i].StartBeat, notes[i].StartBeat + f.CutLength)) return false; break;
            }
        }
        return k == after.Count;
    }

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        const int C4 = 60, D4 = 62;

        // ---- overwrite (Story 1) ----------------------------------------------------------
        {   // 1: B moved to start at 1 cuts A (0–2) to 0–1
            var (r, p) = Settle(new[] { N(C4, 0, 2), N(C4, 1, 2) }, 1);
            yield return (r.Count == 2 && Is(r[0], C4, 0, 1) && Is(r[p[0]], C4, 1, 2), "overwrite: a note running into a placed note is cut at its start");
        }
        {   // 2: B (0.5–2.5) covers A's start (1–2) → A removed
            var (r, p) = Settle(new[] { N(C4, 1, 2), N(C4, 0.5, 2.5) }, 1);
            yield return (r.Count == 1 && p[0] == 0 && Is(r[0], C4, 0.5, 2.5), "overwrite: a note whose start is covered is removed");
        }
        {   // 3: B lengthened to 0–2.5 covers A (2–3) start → A removed
            var (r, _) = Settle(new[] { N(C4, 2, 3), N(C4, 0, 2.5) }, 1);
            yield return (r.Count == 1 && Is(r[0], C4, 0, 2.5), "overwrite: a lengthened note removes the note it now covers");
        }
        {   // 4: different pitches never interact
            var (r, _) = Settle(new[] { N(C4, 0, 2), N(D4, 1, 3) }, 1);
            yield return (r.Count == 2 && Is(r[0], C4, 0, 2) && Is(r[1], D4, 1, 3), "overwrite: different pitches are untouched");
        }
        {   // placed notes never cut each other
            var (r, _) = Settle(new[] { N(C4, 0, 2), N(C4, 1, 3) }, 0, 1);
            yield return (r.Count == 2 && Is(r[0], C4, 0, 2) && Is(r[1], C4, 1, 3), "overwrite: notes of the same selection keep overlapping");
        }
        {   // fully inside a placed note → removed
            var (r, _) = Settle(new[] { N(C4, 1, 1.5), N(C4, 0, 4) }, 1);
            yield return (r.Count == 1 && Is(r[0], C4, 0, 4), "overwrite: a note inside a placed note is removed");
        }
        {   // run into by two placed notes → cut at the earliest placed start
            var (r, _) = Settle(new[] { N(C4, 0, 8), N(C4, 4, 5), N(C4, 2, 3) }, 1, 2);
            yield return (r.Count == 3 && Is(r[0], C4, 0, 2), "overwrite: cut at the earliest placed note it runs into");
        }
        {   // placed indices remap after removals
            var notes = new[] { N(C4, 1, 2), N(D4, 0, 1), N(C4, 0, 4), N(D4, 5, 6) };
            var (r, p) = Settle(notes, 2, 3);
            yield return (r.Count == 3 && p.Count == 2 && Is(r[p[0]], C4, 0, 4) && Is(r[p[1]], D4, 5, 6) && Is(r[0], D4, 0, 1),
                "overwrite: placed indices point at the same notes after removals");
        }
        {   // a copy exactly on its original removes the original
            var (r, p) = Settle(new[] { N(C4, 0, 1), N(C4, 0, 1) }, 1);
            yield return (r.Count == 1 && p[0] == 0, "overwrite: a copy settled on its original replaces it");
        }
        {   // ε: ending exactly at a placed start is kept
            var (r, _) = Settle(new[] { N(C4, 0, 1 + 1e-12), N(C4, 1, 2) }, 1);
            yield return (r.Count == 2 && Near(r[0].LengthBeats, 1 + 1e-12), "overwrite: a note ending where a placed note starts is kept");
        }
        {   // a note starting where a placed note ends is kept
            var (r, _) = Settle(new[] { N(C4, 2, 3), N(C4, 1, 2) }, 1);
            yield return (r.Count == 2 && Is(r[0], C4, 2, 3), "overwrite: a note starting where a placed note ends is kept");
        }
        yield return (FateAgrees(new[] { N(C4, 0, 2), N(C4, 1, 2), N(D4, 0, 4), N(C4, 1.5, 1.75), N(C4, 3, 5), N(C4, 2.5, 3.5) }, 1, 5),
            "overwrite: Fate predicts Settle for every note");

        // ---- length step (Story 3) --------------------------------------------------------
        {
            var notes = new List<NotaNote> { N(C4, 0, 0.25), N(D4, 1, 2) };
            bool ch = NoteOverwrite.ResizeBy(notes, new[] { 0, 1 }, 0.25, 0.25, 8);
            yield return (ch && Is(notes[0], C4, 0, 0.5) && Is(notes[1], D4, 1, 2.25), "length: +step lengthens every note, starts kept");
        }
        {
            var notes = new List<NotaNote> { N(C4, 0, 0.25) };
            bool ch = NoteOverwrite.ResizeBy(notes, new[] { 0 }, -0.25, 0.25, 8);
            yield return (!ch && Is(notes[0], C4, 0, 0.25), "length: never shorter than the minimum");
        }
        {
            var notes = new List<NotaNote> { N(C4, 6, 8), N(D4, 0, 1) };
            bool ch = NoteOverwrite.ResizeBy(notes, new[] { 0, 1 }, 0.25, 0.25, 8);
            yield return (ch && Is(notes[0], C4, 6, 8) && Is(notes[1], D4, 0, 1.25), "length: a note at the clip end does not grow, others do");
        }
        {
            var notes = new List<NotaNote> { N(C4, 6, 8) };
            yield return (!NoteOverwrite.ResizeBy(notes, new[] { 0 }, 0.25, 0.25, 8), "length: no change reports false");
        }

        // ---- velocity step (Story 4) ------------------------------------------------------
        static int Midi(float v) => (int)Math.Round(v * 127);
        static bool OnStep(float v) => Math.Abs(v * 127 - Math.Round(v * 127)) < 1e-3;
        yield return (Midi(NoteOverwrite.VelocityBy(40 / 127f, 10)) == 50, "velocity: 40 + 10 = 50");
        yield return (Midi(NoteOverwrite.VelocityBy(125 / 127f, 10)) == 127, "velocity: clamps at 127");
        yield return (Midi(NoteOverwrite.VelocityBy(3 / 127f, -10)) == 1, "velocity: clamps at 1, never 0");
        var fine = NoteOverwrite.VelocityBy(0.8f, 1);
        yield return (Midi(fine) == (int)Math.Round(0.8 * 127) + 1 && OnStep(fine), "velocity: fine step lands on an integer MIDI value");

        // ---- shaping a note while placing it (specs/055, #45) -----------------------------
        yield return (NotePlacementDrag.KindFor(false, false) == PlaceDragKind.Move, "place drag: plain drag moves the new note");
        yield return (NotePlacementDrag.KindFor(true, false) == PlaceDragKind.Length, "place drag: ⇧ drag sets its length");
        yield return (NotePlacementDrag.KindFor(false, true) == PlaceDragKind.Velocity
                      && NotePlacementDrag.KindFor(true, true) == PlaceDragKind.Velocity, "place drag: ⌘/Ctrl drag sets its velocity, over ⇧");
        double step = NotePlacementDrag.PxPerVelocityStep;
        yield return (Midi(NotePlacementDrag.Velocity(64 / 127f, -10 * step)) == 74, "place drag: dragging up 10 steps is 10 louder");
        yield return (Midi(NotePlacementDrag.Velocity(64 / 127f, 10 * step)) == 54, "place drag: dragging down is quieter");
        yield return (NotePlacementDrag.Velocity(64 / 127f, 0) == 64 / 127f, "place drag: no vertical movement keeps the velocity");
        yield return (Midi(NotePlacementDrag.Velocity(0.8f, -1000)) == 127 && Midi(NotePlacementDrag.Velocity(0.8f, 1000)) == 1,
                      "place drag: velocity clamps to 1..127");
    }
}
