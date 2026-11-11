// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Monitoring another track through an audio track (#563). The monitor used to copy its source's
// tap from the previous segment, `frames` long — clean only while every segment has the same
// length. A device that converts the rate (a Bluetooth output) asks for 470, 471, 470… frames,
// and a loop seam cuts a segment short; each change read stale frames or dropped some, and the
// monitored signal crackled. Now it reads a continuous stream at a fixed lag.
// Also runnable alone: `dotnet Nota.SmokeTest.dll --track-monitor`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TrackMonitorTests
{
    private const int Rate = 44100;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        string wav = Wav();
        try
        {
            // A 330 Hz sine at 0.4 moves at most ~0.0009 per sample in its second difference; a
            // stale or dropped frame jumps by far more.
            var fixedX = Monitor(wav, [512]);
            var (fixedJump, fixedRms) = Smoothness(fixedX, Rate * 3 / 4, Rate * 7 / 4);
            yield return (fixedRms > 0.05 && fixedJump < 0.01,
                $"an audio track monitoring another track hears it whole with fixed blocks (rms {fixedRms:F3}, worst jump {fixedJump:G3})");
            double fixedLag = Match(fixedX, 512, Rate * 3 / 4, Rate * 7 / 4);
            yield return (fixedLag > 0.99, $"…one block behind its source, as before (correlation {fixedLag:F3})");

            var varX = Monitor(wav, [470, 471, 470, 333, 512, 128, 1024, 471]);
            var (varJump, varRms) = Smoothness(varX, Rate * 3 / 4, Rate * 7 / 4);
            yield return (varRms > 0.05 && varJump < 0.01,
                $"…and with blocks of varying length, as a rate-converting device asks (rms {varRms:F3}, worst jump {varJump:G3})");
            double varLag = Match(varX, 1024, Rate * 3 / 4, Rate * 7 / 4);
            yield return (varLag > 0.99, $"…at a fixed lag, the longest block (correlation {varLag:F3})");

            // An export's long chunks, then short live blocks: after a second of short ones the
            // lag comes back down to one short block (~2 s in), crossfading from the old read
            // position rather than skipping 3840 frames. Measured across the change itself.
            var downX = Monitor(wav, [256], warmup: [4096]);
            var (downJump, downRms) = Smoothness(downX, Rate * 3 / 4, Rate * 11 / 4);
            double downLag = Match(downX, 256, Rate * 9 / 4, Rate * 11 / 4);
            yield return (downRms > 0.05 && downJump < 0.01 && downLag > 0.99,
                $"…and the lag shrinks back smoothly once long chunks give way to short blocks (correlation {downLag:F3}, worst jump {downJump:G3})");

            // Short blocks, then longer ones (a larger buffer picked while monitoring, 1 s in):
            // the lag grows without repeating frames audibly.
            var upX = Monitor(wav, [1024], warmup: [256], warmupSeconds: 1.0);
            var (upJump, upRms) = Smoothness(upX, Rate * 3 / 4, Rate * 7 / 4);
            double upLag = Match(upX, 1024, Rate * 5 / 4, Rate * 7 / 4);
            yield return (upRms > 0.05 && upJump < 0.01 && upLag > 0.99,
                $"…and grows smoothly when longer blocks arrive (correlation {upLag:F3}, worst jump {upJump:G3})");
        }
        finally { try { File.Delete(wav); } catch { } }
    }

    // The monitoring track alone (soloed: its source still feeds it) over 3 s of the clip,
    // rendered in `chunks` (cycled), after `warmupSeconds` of `warmup` chunks if given.
    private static float[] Monitor(string wav, int[] chunks, int[]? warmup = null, double warmupSeconds = 0.5)
    {
        using var e = new NotaEngine();
        e.SetBpm(120);
        int src = e.AddAudioTrack();
        e.AddAudioClip(src, wav, 0.0);
        int mon = e.AddAudioTrack();
        e.SetTrackRecordInput(mon, src);
        e.SetTrackMonitor(mon, true);
        e.SetTrackSolo(mon, true);
        e.Seek(0.0);
        e.Play();
        var head = warmup != null ? Render(e, warmup, (int)(Rate * warmupSeconds)) : [];
        var tail = Render(e, chunks, 3 * Rate - head.Length / 2);
        return [.. head, .. tail];
    }

    // The worst second difference of the left channel and its RMS over [from, to) frames.
    private static (double Jump, double Rms) Smoothness(float[] x, int from, int to)
    {
        double jump = 0, sq = 0;
        for (int i = from; i < to; i++)
        {
            double l = x[i * 2];
            jump = Math.Max(jump, Math.Abs(l - 2 * x[(i - 1) * 2] + x[(i - 2) * 2]));
            sq += l * l;
        }
        return (jump, Math.Sqrt(sq / (to - from)));
    }

    // Normalized correlation of the left channel over [from, to) with the clip's sine `lag`
    // frames late (the clip starts on frame 0 of the render).
    private static double Match(float[] x, int lag, int from, int to)
    {
        double xy = 0, xx = 0, yy = 0;
        for (int i = from; i < to; i++)
        {
            double a = x[i * 2], b = Sine(i - lag);
            xy += a * b;
            xx += a * a;
            yy += b * b;
        }
        return xy / Math.Sqrt(Math.Max(1e-12, xx * yy));
    }

    private static float Sine(int i) => 0.4f * (float)Math.Sin(2 * Math.PI * 330 * i / Rate);

    private static float[] Render(NotaEngine e, int[] chunks, int frames)
    {
        var all = new float[frames * 2];
        var b = new float[chunks.Max() * 2];
        for (int done = 0, k = 0; done < frames; k++)
        {
            int n = Math.Min(chunks[k % chunks.Length], frames - done);
            e.RenderOffline(b, n);
            Array.Copy(b, 0, all, done * 2, n * 2);
            done += n;
        }
        return all;
    }

    private static string Wav()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nota-monitor-{Guid.NewGuid():N}.wav");
        var x = new float[Rate * 4 * 2];
        for (int i = 0; i < x.Length / 2; i++) x[2 * i] = x[2 * i + 1] = Sine(i);
        using (var w = new Nota.Infrastructure.WavWriter(path, Rate, 2, WavBitDepth.Float32)) w.WriteFrames(x, x.Length / 2);
        return path;
    }
}
