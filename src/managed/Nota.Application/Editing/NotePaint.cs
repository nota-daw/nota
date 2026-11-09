// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Piano-roll Draw Mode (#554): a plain press places a note and the held drag paints a run of
// them. Two things the run must get right, and neither belongs in the view: a fast drag is
// sampled about once per frame, so the cells under the pointer between two samples have to be
// filled in; and a note longer than the grid step must not stack same-pitch notes on top of
// itself, so painted cells sit on the step or the note's own length, whichever is wider.
// Pure and UI-free: the smoke test drives it directly.

using System;
using System.Collections.Generic;

namespace Nota.Application.Editing;

public static class NotePaint
{
    /// <summary>How far apart painted cells sit on one pitch: the grid step, unless the note
    /// being painted is longer than it — then the note's own length, so a run tiles instead of
    /// overlapping itself.</summary>
    public static double Step(double snapStep, double noteLen)
        => Math.Max(snapStep > 0 ? snapStep : 0, noteLen > 0 ? noteLen : 0);

    /// <summary>The cell column a beat falls in, counted from the cell the gesture started in —
    /// negative to the left of it. A non-positive step puts everything in one column.</summary>
    public static int Column(double beat, double anchorBeat, double step)
        => step <= 0 ? 0 : (int)Math.Round((beat - anchorBeat) / step);

    /// <summary>The cells a drag passes through, from one to the other, in a straight line in
    /// cell space (Bresenham) — the cells the pointer crossed, not only the ones it happened to
    /// be sampled in. Both ends are included, and equal ends give exactly one cell, so a press
    /// and a drag paint the same way.</summary>
    public static IEnumerable<(int Column, int Row)> Line((int Column, int Row) from, (int Column, int Row) to)
    {
        int x = from.Column, y = from.Row;
        int dx = Math.Abs(to.Column - x), dy = Math.Abs(to.Row - y);
        int sx = x < to.Column ? 1 : -1, sy = y < to.Row ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            yield return (x, y);
            if (x == to.Column && y == to.Row) yield break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }
}
