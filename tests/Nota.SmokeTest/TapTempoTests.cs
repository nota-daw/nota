// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Tap tempo: steady taps land on the tempo, jitter averages out, a pause or a tempo change
// starts a new series, a switch bounce is ignored, and the result stays in 20–300 BPM.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --tap`.

using Nota.Application;

namespace Nota.SmokeTest;

internal static class TapTempoTests
{
    private static double? Series(TapTempo t, double start, double gapMs, int taps)
    {
        double? bpm = null;
        for (int i = 0; i < taps; i++) bpm = t.Tap(start + i * gapMs);
        return bpm;
    }

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var t = new TapTempo();
        yield return (t.Tap(0) is null && t.Count == 1, "one tap gives no tempo yet");
        yield return (t.Tap(500) == 120, "a second tap 500 ms later reads 120 BPM");

        t = new TapTempo();
        yield return (Series(t, 0, 60000.0 / 128, 8) == 128, "eight steady taps at 128 BPM read 128");

        // ±15 ms of jitter around 500 ms: the mean over the series still lands on 120.
        t = new TapTempo();
        double[] jitter = { 0, 512, 988, 1510, 1995, 2508, 2991, 3500 };
        double? j = null;
        foreach (var ms in jitter) j = t.Tap(ms);
        yield return (j == 120, $"jittered taps average to 120 BPM (got {j})");

        // A pause longer than 3 s starts over: the next tap alone gives no tempo.
        t = new TapTempo();
        Series(t, 0, 500, 4);
        yield return (t.Tap(1500 + 3500) is null && t.Count == 1, "a pause over 3 s starts a new series");

        // Switching from 120 to 90 BPM mid-series: the old taps are dropped at once.
        t = new TapTempo();
        Series(t, 0, 500, 6);                         // last tap at 2500
        double? changed = t.Tap(2500 + 60000.0 / 90);
        yield return (changed == 90 && t.Count == 2, $"a tempo change restarts the series from the previous tap (got {changed})");

        // A bounce 20 ms after a tap is ignored and doesn't count as a beat.
        t = new TapTempo();
        Series(t, 0, 500, 4);
        double? bounced = t.Tap(1520);
        yield return (bounced == 120 && t.Count == 4, "a 20 ms bounce is ignored");

        // Only the last eight gaps count, so a long run doesn't anchor the tempo forever.
        t = new TapTempo();
        double now = 0;
        for (int i = 0; i < 12; i++) { t.Tap(now); now += 600; }
        yield return (t.Count == 9, "the series keeps the last nine taps (eight gaps)");

        // Clamp: very fast taps (80 ms ≈ 750 BPM) read the 300 BPM ceiling.
        t = new TapTempo();
        yield return (Series(t, 0, 80, 5) == TapTempo.MaxBpm, "taps faster than 300 BPM clamp to 300");

        t.Reset();
        yield return (t.Count == 0 && t.Tap(10000) is null, "Reset clears the series");
    }
}
