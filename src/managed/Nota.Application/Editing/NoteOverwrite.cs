// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Piano-roll note edits that must agree everywhere (specs/007-midi-note-editing): the
// "placed notes win" overwrite rule shared by Duplicate and by settling a moved, stretched,
// drawn or pasted selection, plus the keyboard length and velocity steps. Pure and UI-free:
// the smoke test drives it directly. With several clips open in one editor (specs/029) each
// note has an owner clip, and notes only ever overwrite notes of their own clip.

using System;
using System.Collections.Generic;

namespace Nota.Application.Editing;

public enum NoteFateKind { Kept, Cut, Removed }

/// <summary>What settling does to one unplaced note: nothing, cut to CutLength, or remove it.</summary>
public readonly record struct NoteFate(NoteFateKind Kind, double CutLength);

public static class NoteOverwrite
{
    private const double Eps = 1e-9;

    /// <summary>What settling <paramref name="placed"/> does to note <paramref name="index"/>: removed if
    /// its start lies under a same-pitch placed note, cut where the earliest placed note it runs
    /// into starts, else kept. Placed notes themselves are always kept. Allocation-free when
    /// <paramref name="placed"/> is a list or an array (the piano roll calls it per frame).</summary>
    public static NoteFate Fate(int index, IReadOnlyList<NotaNote> notes, IReadOnlyCollection<int> placed,
                                IReadOnlyList<int>? owners = null)
    {
        var m = notes[index];
        int own = owners is null ? 0 : owners[index];
        double cut = double.MaxValue;
        if (placed is IReadOnlyList<int> list)
        {
            for (int k = 0; k < list.Count; k++)
            {
                if (list[k] == index) return new(NoteFateKind.Kept, 0);
                if (!SameOwner(owners, list[k], own)) continue;
                if (Against(m, notes, list[k], ref cut)) return new(NoteFateKind.Removed, 0);
            }
        }
        else
        {
            foreach (int i in placed)
            {
                if (i == index) return new(NoteFateKind.Kept, 0);
                if (!SameOwner(owners, i, own)) continue;
                if (Against(m, notes, i, ref cut)) return new(NoteFateKind.Removed, 0);
            }
        }
        return cut < double.MaxValue ? new(NoteFateKind.Cut, cut - m.StartBeat) : new(NoteFateKind.Kept, 0);
    }

    private static bool SameOwner(IReadOnlyList<int>? owners, int i, int own)
        => owners is null || (i >= 0 && i < owners.Count && owners[i] == own);

    // True when placed note i removes m; otherwise tightens `cut` if m runs into it.
    private static bool Against(in NotaNote m, IReadOnlyList<NotaNote> notes, int i, ref double cut)
    {
        if (i < 0 || i >= notes.Count) return false;
        var c = notes[i];
        if (c.Pitch != m.Pitch) return false;
        double cs = c.StartBeat, ce = c.StartBeat + c.LengthBeats;
        if (m.StartBeat >= cs - Eps && m.StartBeat < ce - Eps) return true;
        if (m.StartBeat < cs && m.StartBeat + m.LengthBeats > cs + Eps && cs < cut) cut = cs;
        return false;
    }

    /// <summary>Applies the rule: placed notes unchanged, every other note kept, cut or removed
    /// (see <see cref="Fate"/>). Survivors keep their order; <paramref name="placedOut"/> gets
    /// the new indices of the placed notes, in the order of <paramref name="placed"/>. With
    /// <paramref name="owners"/>, only notes of the same owner affect each other, and
    /// <paramref name="ownersOut"/> receives the survivors' owners.</summary>
    public static void Settle(IReadOnlyList<NotaNote> notes, IReadOnlyCollection<int> placed, List<NotaNote> output, List<int> placedOut,
                              IReadOnlyList<int>? owners = null, List<int>? ownersOut = null)
    {
        output.Clear();
        placedOut.Clear();
        ownersOut?.Clear();
        var isPlaced = new bool[notes.Count];
        foreach (int i in placed) if (i >= 0 && i < notes.Count) isPlaced[i] = true;
        var newIndex = new int[notes.Count];
        for (int i = 0; i < notes.Count; i++)
        {
            newIndex[i] = -1;
            var m = notes[i];
            if (!isPlaced[i])
            {
                var f = Fate(i, notes, placed, owners);
                if (f.Kind == NoteFateKind.Removed) continue;
                if (f.Kind == NoteFateKind.Cut) m.LengthBeats = f.CutLength;
            }
            newIndex[i] = output.Count;
            output.Add(m);
            ownersOut?.Add(owners is null ? 0 : owners[i]);
        }
        foreach (int i in placed) if (i >= 0 && i < notes.Count) placedOut.Add(newIndex[i]);
    }

    /// <summary>Changes the length of the notes at <paramref name="indices"/> by
    /// <paramref name="delta"/> beats, keeping their starts: never below <paramref name="minLen"/>,
    /// never past <paramref name="clipLength"/>. Returns whether any note changed.</summary>
    public static bool ResizeBy(List<NotaNote> notes, IEnumerable<int> indices, double delta, double minLen, double clipLength)
        => ResizeBy(notes, indices, delta, minLen, _ => clipLength);

    /// <summary>As above, with each note's own limit — the end of the clip that holds it (specs/029).</summary>
    public static bool ResizeBy(List<NotaNote> notes, IEnumerable<int> indices, double delta, double minLen, Func<int, double> endOf)
    {
        bool changed = false;
        foreach (int i in indices)
        {
            if (i < 0 || i >= notes.Count) continue;
            var n = notes[i];
            double max = Math.Max(minLen, endOf(i) - n.StartBeat);
            double len = n.LengthBeats + delta;
            // Clamp toward the limits, but never pull a note that is already past one further.
            if (delta > 0) len = Math.Min(len, Math.Max(n.LengthBeats, max));
            else len = Math.Max(len, Math.Min(n.LengthBeats, minLen));
            if (Math.Abs(len - n.LengthBeats) < Eps) continue;
            n.LengthBeats = len;
            notes[i] = n;
            changed = true;
        }
        return changed;
    }

    /// <summary>A velocity (0..1) moved by <paramref name="steps127"/> MIDI steps, kept on an
    /// integer MIDI value in 1..127 — so a note can be made quieter but never silent.</summary>
    public static float VelocityBy(float v, int steps127)
        => Math.Clamp((int)Math.Round(v * 127) + steps127, 1, 127) / 127f;
}
