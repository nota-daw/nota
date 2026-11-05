// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Reset velocity in the piano roll's velocity lane (specs/069): the default, what counts as
// already at it, and which notes a reset on a stem changes.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --velocity-reset`.

using Nota.Application.Editing;

namespace Nota.SmokeTest;

internal static class VelocityResetTests
{
    private static string Show(IEnumerable<int> t) => string.Join(",", t);

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        yield return (VelocityReset.Default == 0.8f, "the default velocity is 0.8");
        yield return (VelocityReset.IsDefault(0.8f), "0.8 is the default");
        yield return (VelocityReset.IsDefault(0.8f + 0.003f) && VelocityReset.IsDefault(0.8f - 0.003f),
            "within half a MIDI step of 0.8 counts as the default");
        yield return (!VelocityReset.IsDefault(0.8f + 1f / 127f) && !VelocityReset.IsDefault(0.8f - 1f / 127f),
            "one MIDI step away from 0.8 is not the default");

        var vel = new List<float> { 0.2f, 0.5f, 0.8f, 1f, 0.05f };
        static bool None(int _) => false;
        var sel = new HashSet<int> { 0, 1, 2 };

        yield return (Show(VelocityReset.Targets(new HashSet<int>(), 1, vel, None)) == "1",
            "a stem with nothing selected resets that note only");
        yield return (Show(VelocityReset.Targets(sel, 1, vel, None)) == "0,1",
            "a stem in the selection resets every selected note not already at the default");
        yield return (Show(VelocityReset.Targets(sel, 3, vel, None)) == "3",
            "a stem outside the selection resets that note only");
        yield return (VelocityReset.Targets(sel, 2, vel, None).Count == 2
                      && !VelocityReset.Targets(sel, 2, vel, None).Contains(2),
            "a selected note already at the default is left out");
        yield return (VelocityReset.Targets(new HashSet<int>(), 2, vel, None).Count == 0,
            "a note already at the default is not a target: nothing to do, no undo step");
        yield return (Show(VelocityReset.Targets(sel, 0, vel, i => i == 1)) == "0",
            "a locked note is never a target, even inside the selection");
        yield return (VelocityReset.Targets(new HashSet<int>(), 0, vel, i => i == 0).Count == 0,
            "a locked stem resets nothing");
        yield return (VelocityReset.Targets(sel, -1, vel, None).Count == 0
                      && VelocityReset.Targets(sel, 99, vel, None).Count == 0,
            "a stem index out of range resets nothing");
        yield return (Show(VelocityReset.Targets(new HashSet<int> { 0, 9, -3 }, 0, vel, None)) == "0",
            "selection indices out of range are dropped");
        yield return (VelocityReset.Targets(new HashSet<int> { 4 }, 4, vel, None).Count == 1,
            "a very quiet note is a target");
    }
}
