// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The clips one Clip editor shows (specs/029-multi-clip-editing). The piano roll keeps a single
// note list in *editor beats* — beats from the earliest open clip's start — with each note's
// owner clip (its slot here) alongside. This class owns the rules that must agree everywhere:
// coordinates, which clips a moved note can reach, which clip it belongs to after a move,
// and splitting the roll's list back into each clip's own notes. A single-clip editor is a
// set of one, so editor beats equal clip-local beats there. Pure and UI-free: the smoke test
// drives it directly.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nota.Application.Editing;

/// <summary>One MIDI clip open in a Clip editor. Start and length are arrangement beats.</summary>
public sealed record OpenClip(int TrackId, int ClipIndex, double StartBeat, double LengthBeats, bool IsReference = false)
{
    public double EndBeat => StartBeat + LengthBeats;
}

/// <summary>One clip's notes in its own (clip-local) beats — what the engine stores.</summary>
public readonly record struct ClipNotes(int TrackId, int ClipIndex, IReadOnlyList<NotaNote> Notes);

public sealed class ClipSet
{
    private const double Eps = 1e-9;
    private readonly List<OpenClip> _clips;

    private readonly double _barBeats;

    /// <summary>A set of the given clips (at least one), ordered by start then track; the clip
    /// (activeTrack, activeClip) is active, or the first in time when it is not among them.
    /// With <paramref name="barBeats"/> &gt; 0 a multi-clip set puts editor beat 0 on the bar
    /// line at or before the earliest clip, so the editor's grid is the arrangement's; a single
    /// clip always starts at editor beat 0.</summary>
    public ClipSet(IEnumerable<OpenClip> clips, int activeTrack, int activeClip, double barBeats = 0)
    {
        _barBeats = barBeats;
        _clips = clips.Select(c => c with { IsReference = false })
                      .GroupBy(c => (c.TrackId, c.ClipIndex)).Select(g => g.First())
                      .OrderBy(c => c.StartBeat).ThenBy(c => c.TrackId).ToList();
        if (_clips.Count == 0) throw new ArgumentException("A clip set needs at least one clip.", nameof(clips));
        Active = Math.Max(0, IndexOf(activeTrack, activeClip));
        Measure();
    }

    public static ClipSet Single(int trackId, int clipIndex, double startBeat, double lengthBeats)
        => new(new[] { new OpenClip(trackId, clipIndex, startBeat, lengthBeats) }, trackId, clipIndex);

    public IReadOnlyList<OpenClip> Clips => _clips;
    public int Count => _clips.Count;
    public bool IsEmpty => _clips.Count == 0;
    public bool IsMulti => _clips.Count > 1;
    /// <summary>Slot of the active clip: it receives new notes and drives header, inspector and envelopes.</summary>
    public int Active { get; private set; }
    public OpenClip ActiveClip => _clips[Active];

    /// <summary>Arrangement beat of editor beat 0 (cached: the roll reads it per note per frame).</summary>
    public double Origin { get; private set; }
    public double SpanBeats { get; private set; }

    private void Measure()
    {
        if (_clips.Count == 0) { Origin = 0; SpanBeats = 0; return; }
        Origin = _clips.Min(c => c.StartBeat);
        if (_clips.Count > 1 && _barBeats > 0) Origin = Math.Floor(Origin / _barBeats + 1e-9) * _barBeats;
        SpanBeats = _clips.Max(c => c.EndBeat) - Origin;
    }

    public int IndexOf(int trackId, int clipIndex) => _clips.FindIndex(c => c.TrackId == trackId && c.ClipIndex == clipIndex);
    public bool IsEditable(int slot) => slot >= 0 && slot < _clips.Count && !_clips[slot].IsReference;

    public double EditorStart(int slot) => _clips[slot].StartBeat - Origin;
    public double EditorEnd(int slot) => _clips[slot].EndBeat - Origin;
    public double ToEditor(int slot, double localBeat) => localBeat + EditorStart(slot);
    public double ToLocal(int slot, double editorBeat) => editorBeat - EditorStart(slot);

    /// <summary>Whether editor beat <paramref name="b"/> lies inside the clip (start inclusive, end exclusive).</summary>
    public bool Contains(int slot, double b) => b >= EditorStart(slot) - Eps && b < EditorEnd(slot) - Eps;

    /// <summary>Makes <paramref name="slot"/> active; a reference clip becomes editable first.
    /// Returns whether anything changed.</summary>
    public bool SetActive(int slot)
    {
        if (slot < 0 || slot >= _clips.Count) return false;
        bool changed = slot != Active || _clips[slot].IsReference;
        if (_clips[slot].IsReference) _clips[slot] = _clips[slot] with { IsReference = false };
        Active = slot;
        return changed;
    }

    /// <summary>Locks or unlocks a clip as read-only reference. Refused for the active clip.</summary>
    public bool SetReference(int slot, bool on)
    {
        if (slot < 0 || slot >= _clips.Count || slot == Active || _clips[slot].IsReference == on) return false;
        _clips[slot] = _clips[slot] with { IsReference = on };
        return true;
    }

    /// <summary>Drops a clip that no longer exists. Returns its old slot (or −1); notes owned by
    /// later slots must shift down by one. The active clip falls to the next one.</summary>
    public int Remove(int trackId, int clipIndex)
    {
        int slot = IndexOf(trackId, clipIndex);
        if (slot < 0) return -1;
        _clips.RemoveAt(slot);
        Measure();
        if (_clips.Count == 0) { Active = 0; return slot; }
        if (Active > slot) Active--;
        else if (Active == slot) Active = Math.Min(slot, _clips.Count - 1);
        if (_clips[Active].IsReference) _clips[Active] = _clips[Active] with { IsReference = false };
        return slot;
    }

    /// <summary>A clip moved or was trimmed in the arrangement. Slots stay put.</summary>
    public bool UpdateGeometry(int trackId, int clipIndex, double startBeat, double lengthBeats)
    {
        int slot = IndexOf(trackId, clipIndex);
        if (slot < 0) return false;
        var c = _clips[slot];
        if (Math.Abs(c.StartBeat - startBeat) < Eps && Math.Abs(c.LengthBeats - lengthBeats) < Eps) return false;
        _clips[slot] = c with { StartBeat = startBeat, LengthBeats = lengthBeats };
        Measure();
        return true;
    }

    /// <summary>The editor-beat range a note of <paramref name="slot"/> can start in when moved:
    /// its own clip joined with every open, editable clip of the same track that touches or
    /// overlaps it, transitively. A gap, a reference clip or a clip not open is a wall.</summary>
    public (double Start, double End) Reach(int slot)
    {
        double lo = EditorStart(slot), hi = EditorEnd(slot);
        int track = _clips[slot].TrackId;
        bool grew = true;
        while (grew)
        {
            grew = false;
            for (int k = 0; k < _clips.Count; k++)
            {
                if (k == slot || _clips[k].TrackId != track || _clips[k].IsReference) continue;
                double s = EditorStart(k), e = EditorEnd(k);
                if (s > hi + Eps || e < lo - Eps) continue;
                if (s < lo - Eps) { lo = s; grew = true; }
                if (e > hi + Eps) { hi = e; grew = true; }
            }
        }
        return (lo, hi);
    }

    /// <summary>The clip a note of <paramref name="slot"/> belongs to once its start is at
    /// <paramref name="editorStart"/>: its own clip while that holds the start; otherwise an open,
    /// editable clip of the same track holding it — the active clip first, else the one that
    /// starts later; otherwise unchanged.</summary>
    public int OwnerAfterMove(int slot, double editorStart)
    {
        if (Contains(slot, editorStart)) return slot;
        int track = _clips[slot].TrackId, best = -1;
        for (int k = 0; k < _clips.Count; k++)
        {
            if (k == slot || _clips[k].TrackId != track || _clips[k].IsReference || !Contains(k, editorStart)) continue;
            if (k == Active) return k;
            if (best < 0 || _clips[k].StartBeat > _clips[best].StartBeat) best = k;
        }
        return best >= 0 ? best : slot;
    }

    /// <summary>Where a copy of a note of <paramref name="slot"/> lands when shifted to
    /// <paramref name="editorStart"/> (Duplicate): the clip that holds it within the note's
    /// reach — its own clip or the next open one of its track — or −1 past the reach, where
    /// the copy is dropped rather than piled up at the edge.</summary>
    public int ShiftTarget(int slot, double editorStart)
    {
        var (lo, hi) = Reach(slot);
        if (editorStart < lo - Eps || editorStart >= hi - Eps) return -1;
        return OwnerAfterMove(slot, editorStart);
    }

    /// <summary>Where a pasted note goes (specs/029 FR-012): back into the clip it was copied
    /// from when that clip is open, else into the active clip — at its clip-relative beat
    /// <paramref name="localStart"/> either way. Returns the slot and the editor-beat start.</summary>
    public (int Slot, double EditorStart) PasteTarget(int sourceTrack, int sourceClip, double localStart)
    {
        int slot = IndexOf(sourceTrack, sourceClip);
        if (slot < 0 || _clips[slot].IsReference) slot = Active;
        return (slot, ToEditor(slot, localStart));
    }

    /// <summary>Each clip's notes in clip-local beats, in list order. Every clip gets an entry.</summary>
    public List<ClipNotes> Split(IReadOnlyList<NotaNote> notes, IReadOnlyList<int> owners)
    {
        var lists = new List<NotaNote>[_clips.Count];
        for (int k = 0; k < lists.Length; k++) lists[k] = new List<NotaNote>();
        for (int i = 0; i < notes.Count; i++)
        {
            int o = owners[i];
            if (o < 0 || o >= _clips.Count) continue;
            var n = notes[i];
            n.StartBeat = ToLocal(o, n.StartBeat);
            lists[o].Add(n);
        }
        var result = new List<ClipNotes>(_clips.Count);
        for (int k = 0; k < _clips.Count; k++) result.Add(new ClipNotes(_clips[k].TrackId, _clips[k].ClipIndex, lists[k]));
        return result;
    }

    /// <summary>Inverse of <see cref="Split"/>: every clip's notes in editor beats, clip by
    /// clip, with their owners. Entries for clips not in the set are ignored.</summary>
    public void Merge(IEnumerable<ClipNotes> perClip, List<NotaNote> notesOut, List<int> ownersOut)
    {
        notesOut.Clear();
        ownersOut.Clear();
        foreach (var cn in perClip)
        {
            int slot = IndexOf(cn.TrackId, cn.ClipIndex);
            if (slot < 0) continue;
            foreach (var src in cn.Notes)
            {
                var n = src;
                n.StartBeat = ToEditor(slot, n.StartBeat);
                notesOut.Add(n);
                ownersOut.Add(slot);
            }
        }
    }
}
