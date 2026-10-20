// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Several clips in one Clip editor (specs/029-multi-clip-editing): the ClipSet rules (editor
// beats, reach and owner of moved notes, split/merge, paste routing), the overwrite rule kept
// within a clip, and one undo step for an edit spanning clips.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --multiclip`.

using Nota.Application;
using Nota.Application.Editing;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MultiClipTests
{
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
    private static NotaNote N(int pitch, double start, double end, float vel = 0.8f) => new(pitch, start, end - start, vel);

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        foreach (var r in Model()) yield return r;
        foreach (var r in Overwrite()) yield return r;
        foreach (var r in Moves()) yield return r;
        foreach (var r in Paste()) yield return r;
        foreach (var r in GroupedUndo()) yield return r;
    }

    // Track 1: A (bars 1–4 = beats 0–16), B (16–32). Track 2: C (8–40).
    private static ClipSet ABC(int activeTrack = 1, int activeClip = 1) => new(new[]
    {
        new OpenClip(1, 1, 16, 16),
        new OpenClip(2, 0, 8, 32),
        new OpenClip(1, 0, 0, 16),
    }, activeTrack, activeClip);

    private static IEnumerable<(bool, string)> Model()
    {
        var s = ABC();
        yield return (s.Clips.Select(c => (c.TrackId, c.ClipIndex)).SequenceEqual(new[] { (1, 0), (2, 0), (1, 1) }),
            "multiclip: clips are ordered by start, then track");
        yield return (Near(s.Origin, 0) && Near(s.SpanBeats, 40) && s.IsMulti, "multiclip: origin is the earliest start, span reaches the latest end");
        yield return (s.ActiveClip is { TrackId: 1, ClipIndex: 1 }, "multiclip: the requested clip is active");
        yield return (Near(s.ToEditor(2, 3), 19) && Near(s.ToLocal(2, 19), 3), "multiclip: editor and clip-local beats convert both ways");

        var bars = new ClipSet(new[] { new OpenClip(1, 0, 6, 4), new OpenClip(1, 1, 10, 4) }, 1, 0, barBeats: 4);
        yield return (Near(bars.Origin, 4) && Near(bars.SpanBeats, 10) && Near(bars.EditorStart(0), 2),
            "multiclip: with several clips editor beat 0 sits on the bar line before the first");

        var one = ClipSet.Single(4, 2, 24, 8);
        yield return (Near(one.Origin, 24) && Near(one.ToEditor(0, 5), 5) && !one.IsMulti,
            "multiclip: a one-clip set's editor beats are its clip-local beats");

        var notes = new List<NotaNote> { N(60, 1, 2), N(64, 9, 10), N(62, 17, 18), N(60, 3, 4) };
        var owners = new List<int> { 0, 1, 2, 0 };
        var split = s.Split(notes, owners);
        yield return (split.Count == 3 && split[0].Notes.Count == 2 && split[1].Notes.Count == 1 && Near(split[1].Notes[0].StartBeat, 1)
                      && Near(split[2].Notes[0].StartBeat, 1), "multiclip: Split gives each clip its notes in clip-local beats");
        var back = new List<NotaNote>(); var backOwners = new List<int>();
        s.Merge(split, back, backOwners);
        bool round = back.Count == notes.Count;
        for (int k = 0; round && k < notes.Count; k++)
        {
            int j = back.FindIndex(n => n.Pitch == notes[k].Pitch && Near(n.StartBeat, notes[k].StartBeat));
            round = j >= 0 && backOwners[j] == owners[k];
        }
        yield return (round, "multiclip: Merge undoes Split (every note and owner)");

        yield return (!s.SetReference(s.Active, true), "multiclip: the active clip cannot be a reference");
        yield return (s.SetReference(1, true) && !s.IsEditable(1), "multiclip: another clip can be locked as reference");
        yield return (s.SetActive(1) && s.IsEditable(1) && s.Active == 1, "multiclip: making a reference clip active unlocks it");

        var r = ABC(1, 0);   // A active (slot 0)
        int removed = r.Remove(1, 0);
        yield return (removed == 0 && r.Count == 2 && r.ActiveClip is { TrackId: 2, ClipIndex: 0 } && Near(r.Origin, 8),
            "multiclip: removing the active clip passes active to the next clip and re-measures");
        yield return (r.UpdateGeometry(1, 1, 20, 16) && Near(r.SpanBeats, 32) && r.Clips[1].StartBeat == 20,
            "multiclip: a moved clip keeps its slot and the span follows");
    }

    private static IEnumerable<(bool, string)> Overwrite()
    {
        // Two C4s overlapping: note 1 placed over note 0.
        var notes = new[] { N(60, 0, 2), N(60, 1, 2) };
        var outN = new List<NotaNote>(); var outP = new List<int>(); var outO = new List<int>();
        NoteOverwrite.Settle(notes, new[] { 1 }, outN, outP, new[] { 0, 1 }, outO);
        yield return (outN.Count == 2 && Near(outN[0].LengthBeats, 2) && outO.SequenceEqual(new[] { 0, 1 }),
            "multiclip: a placed note never cuts a note of another clip");
        NoteOverwrite.Settle(notes, new[] { 1 }, outN, outP, new[] { 3, 3 }, outO);
        yield return (outN.Count == 2 && Near(outN[0].LengthBeats, 1) && outO.SequenceEqual(new[] { 3, 3 }),
            "multiclip: within one clip the spec 007 rule still cuts");
        var plain = new List<NotaNote>(); var plainP = new List<int>();
        NoteOverwrite.Settle(notes, new[] { 1 }, plain, plainP);
        yield return (plain.Count == outN.Count && Near(plain[0].LengthBeats, outN[0].LengthBeats),
            "multiclip: without owners Settle is unchanged");
        yield return (NoteOverwrite.Fate(0, notes, new[] { 1 }, new[] { 0, 1 }).Kind == NoteFateKind.Kept
                      && NoteOverwrite.Fate(0, notes, new[] { 1 }).Kind == NoteFateKind.Cut,
            "multiclip: the overwrite mark agrees with Settle");
    }

    private static IEnumerable<(bool, string)> Moves()
    {
        var s = ABC();   // slots: 0 = A (1/0, 0–16), 1 = C (2/0, 8–40), 2 = B (1/1, 16–32)
        var (lo, hi) = s.Reach(0);
        yield return (Near(lo, 0) && Near(hi, 32), "multiclip: a note of A can reach B across the seam");
        yield return (s.OwnerAfterMove(0, 17) == 2, "multiclip: a note moved to start in B belongs to B");
        yield return (s.OwnerAfterMove(0, 15) == 0, "multiclip: a note still starting in A stays in A");
        yield return (s.OwnerAfterMove(0, 30) == 2 && s.OwnerAfterMove(2, 9) == 0,
            "multiclip: notes never move to another track's clip");

        // Duplicate (bug06): a copy shifted past A's end lands in B; past B, in a gap or behind
        // a reference clip it is dropped (−1), never piled up at the edge.
        yield return (s.ShiftTarget(0, 15) == 0 && s.ShiftTarget(0, 17) == 2 && s.ShiftTarget(0, 33) == -1,
            "multiclip: a duplicated note lands in the clip its shift reaches, past the reach nowhere");
        var lone = ClipSet.Single(1, 0, 0, 4);
        yield return (lone.ShiftTarget(0, 3.25) == 0 && lone.ShiftTarget(0, 4.125) == -1,
            "multiclip: in one clip a copy past its end is dropped");

        var gap = new ClipSet(new[] { new OpenClip(1, 0, 0, 16), new OpenClip(1, 1, 20, 16) }, 1, 0);
        var g = gap.Reach(0);
        yield return (Near(g.Start, 0) && Near(g.End, 16), "multiclip: a gap between clips is a wall");

        var locked = ABC(1, 0);   // A active, so B can be locked
        bool lockedB = locked.SetReference(2, true);
        var l = locked.Reach(0);
        yield return (lockedB && Near(l.End, 16) && locked.OwnerAfterMove(0, 17) == 0, "multiclip: a reference clip is a wall");

        // Overlapping clips of one track: X (0–16), Y (8–24), Z (12–28); Y active.
        var ov = new ClipSet(new[] { new OpenClip(1, 0, 0, 16), new OpenClip(1, 1, 8, 16), new OpenClip(1, 2, 12, 16) }, 1, 1);
        yield return (ov.OwnerAfterMove(0, 13) == 0, "multiclip: overlap — a note stays in its clip while it holds the start");
        yield return (ov.OwnerAfterMove(2, 9) == 1, "multiclip: overlap — otherwise the active clip takes it");
        var ov2 = new ClipSet(ov.Clips, 1, 0);   // X active
        yield return (ov2.OwnerAfterMove(0, 20) == 2, "multiclip: overlap — otherwise the later-starting clip");
    }

    private static IEnumerable<(bool, string)> Paste()
    {
        var s = ABC();   // B (slot 2) active
        var (slot, at) = s.PasteTarget(1, 0, 3);
        yield return (slot == 0 && Near(at, 3), "multiclip: copies from an open clip paste back into it");
        (slot, at) = s.PasteTarget(9, 9, 3);
        yield return (slot == 2 && Near(at, 19), "multiclip: copies from a clip not open paste into the active clip");
        s.SetReference(0, true);
        (slot, _) = s.PasteTarget(1, 0, 3);
        yield return (slot == 2, "multiclip: copies never paste into a reference clip");
    }

    // FR-011's basis: the host commits each clip inside one undo group.
    private static IEnumerable<(bool, string)> GroupedUndo()
    {
        using var e = new NotaEngine();
        int t1 = e.AddInstrumentTrack();
        int t2 = e.AddInstrumentTrack();
        int a = e.AddMidiClip(t1, 0.0, 4.0);
        int b = e.AddMidiClip(t2, 4.0, 4.0);
        e.SetClipNotes(t1, a, new[] { N(60, 0, 1) });
        e.SetClipNotes(t2, b, new[] { N(48, 0, 1) });
        e.BeginUndoGroup();
        try
        {
            e.SetClipNotes(t1, a, new[] { N(61, 0, 1) });
            e.SetClipNotes(t2, b, new[] { N(49, 0, 1) });
        }
        finally { e.EndUndoGroup(); }
        bool edited = e.GetClipNotes(t1, a)[0].Pitch == 61 && e.GetClipNotes(t2, b)[0].Pitch == 49;
        e.Undo();
        var na = e.GetClipNotes(t1, a); var nb = e.GetClipNotes(t2, b);
        yield return (edited && na.Length == 1 && na[0].Pitch == 60 && nb.Length == 1 && nb[0].Pitch == 48,
            "multiclip: one undo restores every clip of a grouped edit");
    }
}
