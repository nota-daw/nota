// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Linux JUCE pump's pacing (.specify/bugs/linux-idle-cpu): it starts fast, backs off
// only after a run of empty pumps, and one handled message brings it straight back.

using Nota.Application;

namespace Nota.SmokeTest;

internal static class PumpPacerTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var p = new PumpPacer();
        yield return (p.Interval == PumpPacer.Fast, "pump pacer starts at the fast rate");

        TimeSpan last = p.Interval;
        for (int i = 0; i < PumpPacer.IdlePumpsBeforeSlow - 1; i++) last = p.Next(0);
        yield return (last == PumpPacer.Fast, "a short run of empty pumps stays fast");

        last = p.Next(0);
        yield return (last == PumpPacer.Slow, "a long run of empty pumps backs off to the slow rate");

        for (int i = 0; i < 1000; i++) last = p.Next(0);
        yield return (last == PumpPacer.Slow, "an idle pump stays slow");

        yield return (p.Next(3) == PumpPacer.Fast, "one handled message snaps back to fast");
        yield return (p.Next(0) == PumpPacer.Fast, "…and the back-off starts over");
    }
}
