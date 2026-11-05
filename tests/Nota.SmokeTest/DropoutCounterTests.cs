// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Status bar dropout counter (#271, specs/068): it counts the engine's dropouts since the
// device started or the user reset it, reports only real changes (the view updates its
// text then, not every tick), and reads "1 dropout" / "N dropouts".

using Nota.Presentation;

namespace Nota.SmokeTest;

internal static class DropoutCounterTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        yield return (DropoutCounter.Text(0) == "0 dropouts", "no dropouts reads \"0 dropouts\"");
        yield return (DropoutCounter.Text(1) == "1 dropout", "one dropout is singular");
        yield return (DropoutCounter.Text(3) == "3 dropouts", "several dropouts are plural");

        var c = new DropoutCounter();
        yield return (c.Count == 0 && !c.Observe(0), "a fresh counter reads zero and an idle engine changes nothing");
        yield return (c.Observe(2) && c.Count == 2, "new engine dropouts are counted");
        yield return (!c.Observe(2) && c.Count == 2, "an unchanged engine count reports no change");

        c.Reset(2);
        yield return (c.Count == 0, "a reset counts from zero again");
        yield return (!c.Observe(2), "the dropouts before the reset stay forgotten");
        yield return (c.Observe(3) && c.Count == 1, "a dropout after the reset counts as the first");

        c.Reset(10);
        yield return (!c.Observe(4) && c.Count == 0, "an engine count below the reset point never goes negative");
    }
}
