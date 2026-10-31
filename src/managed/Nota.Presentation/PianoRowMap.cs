// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The piano roll's vertical layout (specs/045 Fold): which pitches have a row, top row first.
// Unfolded it is every key of the range; folded it is the scale's pitches (melodic) or the
// Drum Rack's pads, plus any pitch a note sits on, so folding never hides a note. The view
// maps every y through it, so folded and unfolded share one drawing and editing path.

namespace Nota.Presentation;

public enum PianoRowMark { InSet, OutOfScale, NoPad }

public sealed class PianoRowMap
{
    private readonly int[] _rows;            // pitches, highest first
    private readonly int[] _rowOf;           // pitch → row, −1 when hidden (indexed 0..127)
    private readonly PianoRowMark[] _marks;
    private readonly string?[] _labels;      // pad names; null = the note name

    /// <summary>True when every key of the range has a row: unfolded, or folding found nothing
    /// to fold to (no scale and no notes, or a rack without pads).</summary>
    public bool IsChromatic { get; }

    public int Count => _rows.Length;

    private PianoRowMap(List<int> pitchesDesc, Func<int, PianoRowMark> mark, Func<int, string?> label, bool chromatic)
    {
        _rows = pitchesDesc.ToArray();
        _rowOf = new int[128];
        Array.Fill(_rowOf, -1);
        _marks = new PianoRowMark[_rows.Length];
        _labels = new string?[_rows.Length];
        for (int r = 0; r < _rows.Length; r++)
        {
            _rowOf[_rows[r]] = r;
            _marks[r] = mark(_rows[r]);
            _labels[r] = label(_rows[r]);
        }
        IsChromatic = chromatic;
    }

    /// <summary>Every key from <paramref name="maxPitch"/> down to <paramref name="minPitch"/>.</summary>
    public static PianoRowMap Chromatic(int minPitch, int maxPitch)
    {
        var rows = new List<int>(maxPitch - minPitch + 1);
        for (int p = maxPitch; p >= minPitch; p--) rows.Add(p);
        return new PianoRowMap(rows, _ => PianoRowMark.InSet, _ => null, chromatic: true);
    }

    /// <summary>Builds the folded map. <paramref name="pads"/> (note → name) set means a Drum
    /// Rack track; otherwise <paramref name="scaleMask"/> (12 bits from <paramref name="root"/>)
    /// picks the rows, or with no scale the pitches in use. Used pitches always keep a row.</summary>
    public static PianoRowMap Folded(int minPitch, int maxPitch, IReadOnlyDictionary<int, string>? pads,
                                     ushort? scaleMask, int root, IEnumerable<int> usedPitches)
    {
        var used = new HashSet<int>();
        foreach (int p in usedPitches) if (p >= minPitch && p <= maxPitch) used.Add(p);
        bool InScale(int p) => scaleMask is { } m && (m & (1 << ((((p - root) % 12) + 12) % 12))) != 0;

        var rows = new List<int>();
        for (int p = maxPitch; p >= minPitch; p--)
        {
            bool inSet = pads is not null ? pads.ContainsKey(p) : scaleMask is not null && InScale(p);
            if (inSet || used.Contains(p)) rows.Add(p);
        }
        if (rows.Count == 0) return Chromatic(minPitch, maxPitch);
        return new PianoRowMap(rows,
            p => pads is not null ? (pads.ContainsKey(p) ? PianoRowMark.InSet : PianoRowMark.NoPad)
                 : scaleMask is null || InScale(p) ? PianoRowMark.InSet : PianoRowMark.OutOfScale,
            p => pads is not null && pads.TryGetValue(p, out var n) ? n : null,
            chromatic: false);
    }

    /// <summary>Row of <paramref name="pitch"/>, or −1 when it has none.</summary>
    public int RowOf(int pitch) => pitch is >= 0 and < 128 ? _rowOf[pitch] : -1;

    /// <summary>Pitch of row <paramref name="row"/>, clamped to the first and last row.</summary>
    public int PitchAt(int row) => _rows[Math.Clamp(row, 0, _rows.Length - 1)];

    public PianoRowMark Mark(int row) => _marks[Math.Clamp(row, 0, _rows.Length - 1)];

    /// <summary>The pad name on a folded Drum Rack row, else null (show the note name).</summary>
    public string? Label(int row) => _labels[Math.Clamp(row, 0, _rows.Length - 1)];

    /// <summary>The nearest row to <paramref name="pitch"/>: its own, else the closest by pitch
    /// (the higher one on a tie).</summary>
    public int NearestRow(int pitch)
    {
        int r = RowOf(pitch);
        if (r >= 0) return r;
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < _rows.Length; i++)
        {
            int d = Math.Abs(_rows[i] - pitch);
            if (d < bestD) { best = i; bestD = d; }   // rows run high → low, so a tie keeps the higher
        }
        return best;
    }

    /// <summary>The pitch <paramref name="rows"/> rows above (+) or below (−) the row of
    /// <paramref name="pitch"/> (or its nearest row), clamped to the map.</summary>
    public int Step(int pitch, int rows) => PitchAt(NearestRow(pitch) - rows);

    /// <summary>How many rows <paramref name="pitch"/> can move up (+) and down (−) and stay
    /// on the map — the clamp a group move applies so it keeps its shape.</summary>
    public (int down, int up) Reach(int pitch)
    {
        int r = NearestRow(pitch);
        return (-(Count - 1 - r), r);
    }
}
