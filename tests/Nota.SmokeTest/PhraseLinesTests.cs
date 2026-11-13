// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Phrase lines in the arrangement (specs/091): the choices, a stored value read back, and
// which bars carry the heavier line — counted from bar 1, by bars in any meter, and always on
// a line the snap grid draws anyway. Also runnable alone:
// `dotnet run --project tests/Nota.SmokeTest -- --phrase-lines`.

using Nota.Application;
using Nota.Application.Editing;

namespace Nota.SmokeTest;

internal static class PhraseLinesTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        yield return (string.Join(",", PhraseLines.Choices) == "0,4,8,16,32",
            "the choices are Off, 4, 8, 16 and 32 bars");
        yield return (new Settings().ArrangementPhraseBars == 0, "phrase lines are off by default");

        // A stored value: one of the choices is kept, anything else reads as Off.
        foreach (int c in PhraseLines.Choices)
            yield return (PhraseLines.Normalize(c) == c, $"a stored {c} is kept");
        yield return (PhraseLines.Normalize(3) == 0 && PhraseLines.Normalize(-4) == 0 && PhraseLines.Normalize(64) == 0,
            "a hand-edited 3, -4 or 64 reads as Off");

        // Which bars start a phrase: bar 1, then every N bars.
        yield return (PhraseLines.StartsPhrase(0, 16) && PhraseLines.StartsPhrase(16, 16) && PhraseLines.StartsPhrase(32, 16),
            "every 16 bars: bars 1, 17 and 33 start a phrase");
        yield return (!PhraseLines.StartsPhrase(4, 16) && !PhraseLines.StartsPhrase(15, 16) && !PhraseLines.StartsPhrase(17, 16),
            "every 16 bars: bars 5, 16 and 18 do not");
        yield return (PhraseLines.StartsPhrase(4, 4) && !PhraseLines.StartsPhrase(6, 4), "every 4 bars: bar 5 does, bar 7 does not");
        yield return (!PhraseLines.StartsPhrase(0, 0) && !PhraseLines.StartsPhrase(8, 0), "Off: no bar starts a phrase");
        yield return (!PhraseLines.StartsPhrase(-8, 8), "nothing before bar 1");
        yield return (!PhraseLines.StartsPhrase(6, 3), "a length that is not a choice draws nothing");

        // A grid line by its beat: on a bar line, and on a phrase bar.
        yield return (PhraseLines.IsPhraseLine(0, 8, 4) && PhraseLines.IsPhraseLine(32, 8, 4) && PhraseLines.IsPhraseLine(64, 8, 4),
            "4/4, every 8 bars: beats 0, 32 and 64");
        yield return (!PhraseLines.IsPhraseLine(16, 8, 4) && !PhraseLines.IsPhraseLine(33, 8, 4) && !PhraseLines.IsPhraseLine(32.5, 8, 4),
            "4/4, every 8 bars: not beat 16 (bar 5), 33 or 32.5");
        yield return (PhraseLines.IsPhraseLine(24, 8, 3) && !PhraseLines.IsPhraseLine(32, 8, 3),
            "3/4 counts bars, not beats: beat 24 is bar 9, beat 32 is inside bar 11");
        yield return (PhraseLines.IsPhraseLine(64, 16, 0) && !PhraseLines.IsPhraseLine(48, 16, 0),
            "an unset meter reads as 4/4: beat 64 is bar 17, beat 48 is bar 13");
        yield return (PhraseLines.IsPhraseLine(64.0000001, 16, 4), "rounding slack: 64.0000001 is still bar 17");
        yield return (!PhraseLines.IsPhraseLine(0, 0, 4), "Off: no line at all");

        // Every phrase line is a line the snap grid already draws, whatever its step and meter,
        // so the grid only has to weight it — never add a line between its own.
        bool onGrid = true;
        foreach (int meter in new[] { 3, 4, 5, 7 })
            foreach (int n in new[] { 4, 8, 16, 32 })
                for (int i = 0; i < GridStep.Count; i++)
                {
                    double step = GridStep.Of(i, false).Beats(meter);
                    var lines = new HashSet<double>(SnapGrid.GridLines(0, meter * 70, step, meter));
                    for (int bar = 0; bar < 70; bar += n)
                        if (!lines.Contains(bar * (double)meter)) onGrid = false;
                }
        yield return (onGrid, "every phrase line falls on a drawn grid line at every step and meter");
    }
}
