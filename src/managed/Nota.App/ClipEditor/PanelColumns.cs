// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// How the clip editor's side panels (the clip inspector, the MIDI tools) lay out their
// sections (specs/055): one column while everything fits the height, two when it doesn't and
// the editor has the width, and scrolling only past that. Sections are never split, keep
// their order (the first column takes a leading run, the second the rest), and the split is
// the one that leaves the taller column shortest. No Avalonia here, so the smoke test links it.

using System;

namespace Nota.App;

public static class PanelColumns
{
    /// <summary>The chosen layout: 1 or 2 columns, how many sections the first column holds
    /// (all of them for one column), and the taller column's height.</summary>
    public readonly record struct Plan(int Columns, int Split, double Height);

    /// <summary>Height of sections <paramref name="from"/>..<paramref name="to"/> (exclusive)
    /// stacked with <paramref name="gap"/> between neighbours.</summary>
    public static double Stack(ReadOnlySpan<double> heights, int from, int to, double gap)
    {
        double h = 0;
        for (int i = from; i < to; i++) h += heights[i] + (i > from ? gap : 0);
        return h;
    }

    /// <summary>The first column's section count (1..n-1) that leaves the taller column shortest;
    /// on a tie the first column takes more, so it reads top-left first.</summary>
    public static int BestSplit(ReadOnlySpan<double> heights, double gap)
    {
        int n = heights.Length, best = n;
        double bestH = double.PositiveInfinity;
        for (int k = 1; k < n; k++)
        {
            double h = Math.Max(Stack(heights, 0, k, gap), Stack(heights, k, n, gap));
            if (h <= bestH) { bestH = h; best = k; }
        }
        return best;
    }

    /// <summary>Whether a panel can take its second column: of the <paramref name="available"/>
    /// width, <paramref name="two"/> goes to the panel and what is left must still hold the
    /// larger of <paramref name="reserve"/> (the canvas's minimum) and <paramref name="beside"/>
    /// (what the header needs next to the clip cell, which widens with the panel).</summary>
    public static bool RoomForTwo(double available, double two, double reserve, double beside)
        => available - Math.Max(reserve, beside) >= two;

    /// <summary>Picks the layout. <paramref name="room"/> is the height the sections get in one
    /// column; <paramref name="widthForTwo"/> says the editor can spare a second column.</summary>
    public static Plan Choose(ReadOnlySpan<double> heights, double gap, double room, bool widthForTwo)
    {
        int n = heights.Length;
        double one = Stack(heights, 0, n, gap);
        if (n < 2 || one <= room || !widthForTwo) return new Plan(1, n, one);
        int k = BestSplit(heights, gap);
        return new Plan(2, k, Math.Max(Stack(heights, 0, k, gap), Stack(heights, k, n, gap)));
    }
}
