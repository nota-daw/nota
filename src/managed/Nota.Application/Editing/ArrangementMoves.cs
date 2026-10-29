// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Keyboard moves in the Arrangement (specs/039-arrangement-keyboard-moves): how far ←/→ nudge
// a clip selection (#302), which rows ⌥↑/⌥↓ move it to, and the track range a Shift-click on
// a header selects (#249). Pure and UI-free: the smoke test drives it directly.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nota.Application.Editing;

/// <summary>One committed block move: where each clip of the touched tracks went (<see
/// cref="ArrangementMoves.Reindex"/>), and the touched tracks' clip starts before and after —
/// which tell an undo or redo of exactly this move apart from any other step.</summary>
public sealed record ClipBlockMove(
    IReadOnlyDictionary<(int Track, int Clip), (int Track, int Clip)?> Map,
    IReadOnlyDictionary<int, IReadOnlyList<double>> StartsBefore,
    IReadOnlyDictionary<int, IReadOnlyList<double>> StartsAfter);

public static class ArrangementMoves
{
    /// <summary>The fine nudge (⌥←/⌥→): a 64th note, whatever the grid.</summary>
    public const double FineNudgeBeats = 1.0 / 16;

    /// <summary>The beat shift for nudging a selection whose earliest clip starts at
    /// <paramref name="firstStart"/> by <paramref name="step"/> in direction <paramref name="dir"/>
    /// (-1 left, +1 right). The block moves as one and stops at the song start, so a nudge left
    /// that would cross it moves only as far as it goes; 0 when nothing can move.</summary>
    public static double NudgeDelta(double firstStart, double step, int dir)
    {
        if (step <= 0 || dir == 0) return 0;
        double d = Math.Sign(dir) * step;
        if (firstStart + d < 0) d = -Math.Max(0, firstStart);
        return Math.Abs(d) < 1e-9 ? 0 : d;
    }

    /// <summary>The row shift that moves clips on <paramref name="rows"/> to the nearest rows in
    /// direction <paramref name="dir"/> where every one of them lands on a row that takes it
    /// (<paramref name="accepts"/>(from, to)) — so an audio clip skips over instrument tracks.
    /// 0 when no such rows exist before the edge.</summary>
    public static int RowShift(IReadOnlyCollection<int> rows, int rowCount, int dir, Func<int, int, bool> accepts)
    {
        if (rows.Count == 0 || dir == 0) return 0;
        int step = Math.Sign(dir);
        for (int d = step; ; d += step)
        {
            if (rows.Any(r => r + d < 0 || r + d >= rowCount)) return 0;
            if (rows.All(r => accepts(r, r + d))) return d;
        }
    }

    /// <summary>The tracks a Shift-click on <paramref name="targetId"/> selects: every id from the
    /// anchor (the last plain- or ⌘-clicked track) to the target in <paramref name="order"/>
    /// (the rows as shown), in either direction. Just the target when the anchor isn't shown.</summary>
    public static IReadOnlyList<int> Range(IReadOnlyList<int> order, int anchorId, int targetId)
    {
        int a = IndexOf(order, anchorId), t = IndexOf(order, targetId);
        if (t < 0) return Array.Empty<int>();
        if (a < 0) return new[] { targetId };
        int lo = Math.Min(a, t), hi = Math.Max(a, t);
        var ids = new int[hi - lo + 1];
        for (int i = lo; i <= hi; i++) ids[i - lo] = order[i];
        return ids;
    }

    /// <summary>Where each clip of the tracks a block move touched is afterwards. Clips are
    /// addressed by index, and a move re-indexes them: a landing carves away or splits clips in
    /// front of others, and a clip arriving from another track is appended. A moved clip is where
    /// the engine placed it (<paramref name="moved"/>[i] → <paramref name="placed"/>[i]); a clip
    /// that stayed is found by its start, which a carve of its tail keeps; null when it was carved
    /// away or lost its head. <paramref name="startsBefore"/> / <paramref name="startsAfter"/>:
    /// each touched track's clip starts, by index.</summary>
    public static Dictionary<(int Track, int Clip), (int Track, int Clip)?> Reindex(
        IReadOnlyDictionary<int, IReadOnlyList<double>> startsBefore,
        IReadOnlyList<(int Track, int Clip)> moved, IReadOnlyList<(int Track, int Clip)> placed,
        IReadOnlyDictionary<int, IReadOnlyList<double>> startsAfter)
    {
        var map = new Dictionary<(int, int), (int, int)?>();
        for (int i = 0; i < moved.Count && i < placed.Count; i++) map[moved[i]] = placed[i];
        var taken = new HashSet<(int, int)>(placed);
        foreach (var (track, starts) in startsBefore)
        {
            var after = startsAfter.TryGetValue(track, out var a) ? a : Array.Empty<double>();
            for (int c = 0; c < starts.Count; c++)
            {
                if (map.ContainsKey((track, c))) continue;
                (int, int)? to = null;
                for (int j = 0; j < after.Count; j++)
                    if (!taken.Contains((track, j)) && Math.Abs(after[j] - starts[c]) < 1e-9) { to = (track, j); break; }
                map[(track, c)] = to;
            }
        }
        return map;
    }

    /// <summary>The way back: where each clip is after undoing a move that re-indexed with
    /// <paramref name="map"/>. A clip the move didn't produce on a touched track (a carve's right
    /// piece) has no place before it: null. Clips of other tracks are not in either map.</summary>
    public static Dictionary<(int Track, int Clip), (int Track, int Clip)?> Invert(
        IReadOnlyDictionary<(int Track, int Clip), (int Track, int Clip)?> map,
        IReadOnlyDictionary<int, IReadOnlyList<double>> startsAfter)
    {
        var back = new Dictionary<(int, int), (int, int)?>();
        foreach (var (track, starts) in startsAfter)
            for (int c = 0; c < starts.Count; c++) back[(track, c)] = null;
        foreach (var (from, to) in map)
            if (to is { } t) back[t] = from;
        return back;
    }

    /// <summary>Whether two snapshots of track clip starts are the same.</summary>
    public static bool SameStarts(IReadOnlyDictionary<int, IReadOnlyList<double>> a, IReadOnlyDictionary<int, IReadOnlyList<double>> b)
        => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var o) && o.Count == kv.Value.Count
                                             && o.Zip(kv.Value).All(p => Math.Abs(p.First - p.Second) < 1e-9));

    private static int IndexOf(IReadOnlyList<int> order, int id)
    {
        for (int i = 0; i < order.Count; i++) if (order[i] == id) return i;
        return -1;
    }
}
