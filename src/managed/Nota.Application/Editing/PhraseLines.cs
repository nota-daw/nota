// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Phrase lines in the arrangement (specs/092, #517): a heavier grid line every 4, 8, 16 or
// 32 bars, counted from bar 1, so a song's phrase structure reads at a glance. A user
// preference (Settings → Appearance), never saved in a project. This is the one place that
// says which lengths there are and which bars carry a line; the lanes and the ruler only
// draw it. Bars, not beats: a line lands on the same bar in any meter.

using System;
using System.Collections.Generic;

namespace Nota.Application.Editing;

public static class PhraseLines
{
    /// <summary>The phrase lengths Settings offers, in bars; 0 is Off (the default).</summary>
    public static IReadOnlyList<int> Choices { get; } = new[] { 0, 4, 8, 16, 32 };

    /// <summary><paramref name="bars"/> when it is one of <see cref="Choices"/>, else Off — so
    /// a hand-edited settings file can never draw a line every 3 bars.</summary>
    public static int Normalize(int bars)
    {
        foreach (int c in Choices)
            if (c == bars) return bars;
        return 0;
    }

    /// <summary>Whether the bar with zero-based index <paramref name="bar"/> (bar 1 is 0)
    /// starts a phrase of <paramref name="phraseBars"/> bars.</summary>
    public static bool StartsPhrase(int bar, int phraseBars)
    {
        int n = Normalize(phraseBars);
        return n > 0 && bar >= 0 && bar % n == 0;
    }

    /// <summary>Whether a grid line at <paramref name="beat"/> is a phrase line: it sits on a
    /// bar line and that bar starts a phrase of <paramref name="phraseBars"/> bars.</summary>
    public static bool IsPhraseLine(double beat, int phraseBars, int beatsPerBar)
    {
        if (Normalize(phraseBars) == 0) return false;
        double bars = beat / SnapGrid.BarBeats(beatsPerBar);
        double whole = Math.Round(bars);
        return Math.Abs(bars - whole) < 1e-6 && StartsPhrase((int)whole, phraseBars);
    }
}
