// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The range the piano roll's Bend lane shows (specs/073): ± semitones from one of a few fixed
// steps. The lane fits itself to the bends already on the notes, so a clip without any bend
// opens at ±2; the range the user picks in the lane is the least it shows, so a bigger bend can
// be drawn from scratch. A bend never gets clipped: a curve that needs more widens the lane past
// the pick. This is the one place that says which steps there are and which one is shown; the
// lane only draws it and opens the menu.

using System;
using System.Collections.Generic;

namespace Nota.Application.Editing;

public static class BendLaneRange
{
    /// <summary>The ranges the lane can show, in ± semitones, smallest first.</summary>
    public static IReadOnlyList<double> Steps { get; } = new[] { 2.0, 12, 24, 48, 96 };

    /// <summary>The range before the user picks one: the smallest step.</summary>
    public const double Default = 2;

    // Rounding slack: a bend stored at 12.0004 st still fits ±12.
    private const double Slack = 1e-3;

    /// <summary>Whether ±<paramref name="range"/> shows a bend reaching
    /// <paramref name="largestBend"/> semitones (either way) without clipping it.</summary>
    public static bool Fits(double range, double largestBend) => Math.Abs(largestBend) <= range + Slack;

    /// <summary>The range the lane shows: the smallest step at least as wide as the user's
    /// <paramref name="picked"/> range that fits <paramref name="largestBend"/>; the widest
    /// step when nothing fits.</summary>
    public static double Shown(double picked, double largestBend)
    {
        foreach (double r in Steps)
            if (r >= picked - Slack && Fits(r, largestBend)) return r;
        return Steps[^1];
    }
}
