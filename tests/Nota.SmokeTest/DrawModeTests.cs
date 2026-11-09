// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Piano-roll Draw Mode (#554): the cells a paint drag fills, and how far apart they sit.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --draw`.

using Nota.Application.Editing;

namespace Nota.SmokeTest;

internal static class DrawModeTests
{
    private static string Show((int Column, int Row) c) => $"({c.Column},{c.Row})";

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        // ---- spacing: a note longer than the grid step tiles instead of stacking --------------
        yield return (Math.Abs(NotePaint.Step(0.25, 0.25) - 0.25) < 1e-9,
            "draw: cells sit a grid step apart when the note is one step long");
        yield return (Math.Abs(NotePaint.Step(0.25, 1.0) - 1.0) < 1e-9,
            "draw: a longer note spaces the run by its own length, so same-pitch notes never overlap");
        yield return (Math.Abs(NotePaint.Step(0.5, 0.125) - 0.5) < 1e-9,
            "draw: the grid step wins when the note is shorter than it");
        yield return (NotePaint.Step(0, 0) == 0, "draw: no step at all does not divide by zero");

        // ---- the cell a beat falls in --------------------------------------------------------
        yield return (NotePaint.Column(1.0, 0, 0.25) == 4, "draw: a beat four cells on is column 4");
        yield return (NotePaint.Column(0.9, 0, 0.25) == 4, "draw: a beat rounds to the nearest cell");
        yield return (NotePaint.Column(-0.5, 0, 0.5) == -1, "draw: dragging left of where it started is negative");
        yield return (NotePaint.Column(3, 3, 0) == 0, "draw: a zero step keeps everything in one column");

        // ---- the cells a drag crosses --------------------------------------------------------
        var right = NotePaint.Line((0, 0), (3, 0)).ToList();
        yield return (string.Join(" ", right.Select(Show)) == "(0,0) (1,0) (2,0) (3,0)",
            "draw: a horizontal drag fills every cell it crossed, both ends included");

        var left = NotePaint.Line((0, 0), (-2, 0)).ToList();
        yield return (string.Join(" ", left.Select(Show)) == "(0,0) (-1,0) (-2,0)",
            "draw: a drag to the left fills the cells back to the start");

        var up = NotePaint.Line((0, 0), (0, 3)).ToList();
        yield return (string.Join(" ", up.Select(Show)) == "(0,0) (0,1) (0,2) (0,3)",
            "draw: a vertical drag fills every row crossed");

        var diagonal = NotePaint.Line((0, 0), (4, 4)).ToList();
        yield return (diagonal.Count == 5 && diagonal.All(c => c.Column == c.Row) && diagonal[^1] == (4, 4),
            "draw: a 45° drag is a clean staircase, one note per cell, no gaps");

        var shallow = NotePaint.Line((0, 0), (4, 1)).ToList();
        yield return (shallow.Count == 5 && shallow[0] == (0, 0) && shallow[^1] == (4, 1)
                      && shallow.Select(c => c.Column).Distinct().Count() == 5,
            "draw: a shallow drag still covers every column it crossed");

        yield return (NotePaint.Line((7, 7), (7, 7)).SequenceEqual(new[] { (7, 7) }),
            "draw: a press without movement paints exactly one cell");

        // A drag that comes back on itself must not paint the same cell twice: the view asks for
        // every segment, and each segment's own ends are included.
        var there = NotePaint.Line((0, 0), (2, 0)).ToList();
        var back = NotePaint.Line((2, 0), (0, 0)).ToList();
        yield return (there.Count == 3 && back.Count == 3 && back[0] == (2, 0) && back[^1] == (0, 0),
            "draw: a drag that reverses covers the same cells again (the view skips ones already painted)");

        // A fast flick: the pointer jumped 16 cells, and the run has no hole in it.
        var flick = NotePaint.Line((0, 0), (16, 0)).ToList();
        yield return (flick.Count == 17 && flick.Select((c, i) => c.Column == i).All(x => x),
            "draw: a 16-cell flick leaves no gap between samples");
    }
}
