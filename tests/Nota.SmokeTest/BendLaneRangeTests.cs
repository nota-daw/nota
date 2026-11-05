// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The piano roll Bend lane's range (specs/073): the steps, the range picked from the lane's
// label as the least it shows, and a bend already on the notes widening it past the pick.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --bend-range`.

using Nota.Application.Editing;

namespace Nota.SmokeTest;

internal static class BendLaneRangeTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        yield return (string.Join(",", BendLaneRange.Steps) == "2,12,24,48,96",
            "the steps are ±2, 12, 24, 48 and 96 semitones");
        yield return (BendLaneRange.Default == 2, "the lane opens at ±2 before a range is picked");

        // Nothing picked: the lane fits what is there, as before.
        yield return (BendLaneRange.Shown(BendLaneRange.Default, 0) == 2, "no bend shows ±2");
        yield return (BendLaneRange.Shown(BendLaneRange.Default, 2) == 2, "a 2 st bend still fits ±2");
        yield return (BendLaneRange.Shown(BendLaneRange.Default, 2.5) == 12, "a 2.5 st bend shows ±12");
        yield return (BendLaneRange.Shown(BendLaneRange.Default, 12.0004) == 12,
            "rounding slack: 12.0004 st fits ±12");
        yield return (BendLaneRange.Shown(BendLaneRange.Default, 30) == 48, "a 30 st bend shows ±48");

        // A pick: the least the lane shows, so a big bend can be drawn from scratch.
        foreach (double r in BendLaneRange.Steps)
            yield return (BendLaneRange.Shown(r, 0) == r, $"with no bend, picking ±{r} shows ±{r}");
        yield return (BendLaneRange.Shown(24, 5) == 24, "a pick wider than the bend wins");
        yield return (BendLaneRange.Shown(12, 30) == 48, "a bend wider than the pick widens the lane, never clipped");
        yield return (BendLaneRange.Shown(96, 200) == 96, "past every step the widest is shown");
        yield return (BendLaneRange.Shown(BendLaneRange.Default, -13) == 24, "a downward bend counts by its size");

        // Which picks the menu offers: a step that would clip a bend on the notes is disabled.
        yield return (BendLaneRange.Fits(12, 12) && BendLaneRange.Fits(12, -12), "±12 fits a 12 st bend either way");
        yield return (!BendLaneRange.Fits(2, 2.5) && !BendLaneRange.Fits(12, -13), "a step narrower than the bend does not fit");
        yield return (BendLaneRange.Fits(2, 0), "every step fits no bend");
    }
}
