// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The project's bar length for every view that counts bars (#405): the arrangement grid and
// ruler, the clip editors, the props-rail and Session readouts, Export's range fields. A bar is
// the time signature's numerator in beats — as the engine's metronome and count-in count it.
// App-wide like EditorGrids: MainWindow sets it from the transport's time signature (typed,
// loaded with a project, or set by a launched scene) and views read it as they draw.

using System;

namespace Nota.App;

public static class Meter
{
    private static int s_beatsPerBar = 4;

    /// <summary>Beats in one bar (the time signature's numerator, at least 1).</summary>
    public static int BeatsPerBar => s_beatsPerBar;

    /// <summary>Raised on the UI thread after the bar length changes.</summary>
    public static event Action? Changed;

    public static void Set(int numerator)
    {
        int n = Math.Clamp(numerator, 1, 32);
        if (n == s_beatsPerBar) return;
        s_beatsPerBar = n;
        Changed?.Invoke();
    }
}
