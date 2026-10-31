// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The status bar's dropout counter (#271, specs/068). The engine counts device dropouts
// (xruns) since launch and never resets; the counter shows those since the device last
// started or since the user reset it, so a setting change can be judged by whether the
// count stays at zero.

namespace Nota.Presentation;

public sealed class DropoutCounter
{
    private int _baseline;   // the engine's count when the counter last read zero

    /// <summary>Dropouts since the last reset.</summary>
    public int Count { get; private set; }

    /// <summary>Follow the engine's running count. True when <see cref="Count"/> changed.</summary>
    public bool Observe(int engineCount)
    {
        int count = Math.Max(0, engineCount - _baseline);
        if (count == Count) return false;
        Count = count;
        return true;
    }

    /// <summary>Start counting again from zero at the engine's current count.</summary>
    public void Reset(int engineCount)
    {
        _baseline = engineCount;
        Count = 0;
    }

    /// <summary>"0 dropouts", "1 dropout", "3 dropouts".</summary>
    public static string Text(int count) => count == 1 ? "1 dropout" : $"{count} dropouts";
}
