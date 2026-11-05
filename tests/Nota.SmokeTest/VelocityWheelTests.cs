// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shift + wheel velocity steps in the piano roll (specs/063): direction, the 0–2× multiplier,
// trackpad fractions carried over, the per-event cap, and the 1–127 clamp the steps land in.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --velocity-wheel`.

using Nota.Application.Editing;

namespace Nota.SmokeTest;

internal static class VelocityWheelTests
{
    private static int Sum(VelocityWheel w, double notches, int events, bool reversed, double mult)
    {
        int s = 0;
        for (int i = 0; i < events; i++) s += w.Steps(notches, reversed, mult);
        return s;
    }

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var w = new VelocityWheel();
        yield return (w.Steps(1, false, 1) == 1, "one notch up at 1× raises by one step");
        yield return (w.Steps(-1, false, 1) == -1, "one notch down at 1× lowers by one step");
        yield return (w.Steps(1, true, 1) == -1 && w.Steps(-1, true, 1) == 1, "reversed flips both directions");
        yield return (w.Steps(1, false, 2) == 2, "2× moves two steps per notch");
        yield return (w.Steps(1, false, 5) == 2, "the multiplier is capped at 2×");
        yield return (w.Steps(1, false, 0) == 0 && !VelocityWheel.Enabled(0) && VelocityWheel.Enabled(0.25), "0× is off");

        w = new VelocityWheel();
        yield return (Sum(w, 1, 4, false, 0.5) == 2, "0.5× steps once every two notches");

        w = new VelocityWheel();
        int first = w.Steps(0.3, false, 1);
        yield return (first == 0 && Sum(w, 0.3, 3, false, 1) == 1, "trackpad fractions carry until a whole step");

        w = new VelocityWheel();
        w.Steps(0.6, false, 1);
        w.Reset();
        yield return (w.Steps(0.6, false, 1) == 0, "a reset drops the carried fraction");

        w = new VelocityWheel();
        yield return (w.Steps(40, false, 2) == 4, "one event moves at most two notches");

        yield return (NoteOverwrite.VelocityBy(127 / 127f, 3) == 1f, "steps above 127 clamp at 127");
        yield return (NoteOverwrite.VelocityBy(2 / 127f, -5) == 1 / 127f, "steps below 1 clamp at 1");
        yield return (NoteOverwrite.VelocityBy(64 / 127f, 2) == 66 / 127f, "a step is one MIDI velocity value");
    }
}
