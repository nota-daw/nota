// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class TempoMapRenderingTests
{
    private const int Sr = 48000;
    private static float[] Render(NotaEngine e, int frames)
    {
        var pcm = new float[frames * 2]; e.RenderOffline(pcm, frames, Sr); return pcm;
    }
    private static void Setup(NotaEngine e)
    {
        e.SetBpm(120); Render(e, 1);
        e.SetTempoPoints(new[] { new TempoPoint(0, 120), new TempoPoint(4, 60) });
    }
    private static double Energy(float[] pcm, double from, double to)
    {
        double sum = 0;
        for (int i = (int)(from * Sr) * 2; i < Math.Min(pcm.Length, (int)(to * Sr) * 2); ++i)
            sum += pcm[i] * pcm[i];
        return sum;
    }
    private static void Build(NotaEngine e, int method)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (e.WarpBuildRemaining > 0 && timer.Elapsed.TotalSeconds < 30)
        {
            if (method == 1) e.WarpBuildStep(16384);
            else { e.WarpBuildPoll(); Thread.Sleep(1); }
        }
        // Progress can reach zero just before the worker publishes its finished flag.
        if (method == 2) for (int i = 0; i < 4; ++i) { Thread.Sleep(2); e.WarpBuildPoll(); }
    }
    public static IEnumerable<(bool, string)> Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-tempo-rendering-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string pulse = Path.Combine(dir, "pulse.wav"), tone = Path.Combine(dir, "tone.wav");
            WavWriter.WriteStereo(pulse, 4, Sr, i =>
            {
                double v = Math.Abs(i - Sr) < 500 ? .7 * Math.Sin(2 * Math.PI * 440 * i / Sr) : 0;
                return (v, v);
            });
            WavWriter.WriteStereo(tone, 6, Sr, i =>
            {
                double v = .25 * Math.Sin(2 * Math.PI * 440 * i / Sr); return (v, v);
            });
            foreach (int mode in new[] { 5, 3 }) // exact RePitch and pitch-preserving Complex
            foreach (int build in new[] { 0, 1, 2 })
            {
                using var e = new NotaEngine(); Setup(e);
                e.SetDeferWarpBuild(build != 0);
                int t = e.AddAudioTrack(); e.AddAudioClip(t, pulse, 0);
                e.SetClipWarp(t, 0, true, mode); e.SetClipWarpLength(t, 0, 8);
                if (build == 2) e.WarpBuildPoll(); // start forward work, then invalidate its direction
                e.SetClipReverse(t, 0, true); Build(e, build);
                e.Seek(0); e.Play(); var pcm = Render(e, 6 * Sr);
                yield return (Energy(pcm, 3.85, 4.15) > .01 && Energy(pcm, 4.85, 5.15) < .001,
                    $"tempo rendering: reversed mode {mode}, build {build} sounds at beat 6 (second 4)");
                e.SetClipReverse(t, 0, false); Build(e, build);
                e.Seek(0); pcm = Render(e, 6 * Sr);
                yield return (Energy(pcm, .85, 1.15) > .01,
                    $"tempo rendering: direction toggles invalidate mapped cache, mode {mode}, build {build}");
                e.ResizeAudioClip(t, 0, 1, 6); e.SetClipReverse(t, 0, true); Build(e, build);
                e.Seek(0); pcm = Render(e, 6 * Sr);
                yield return (Energy(pcm, 3.85, 4.15) > .01,
                    $"tempo rendering: trimmed reversed window preserves beat 6, mode {mode}, build {build}");
            }
            foreach (bool reverse in new[] { false, true })
            foreach (double start in new[] { 0.0, 2.0 })
            {
                using var e = new NotaEngine(); Setup(e);
                int t = e.AddAudioTrack(); e.AddAudioClip(t, tone, start); e.SetClipReverse(t, 0, reverse);
                e.SetClipAdsr(t, 0, new ClipAdsr { Sustain = 1, ReleaseBeats = 1 });
                double end = e.BeatToSeconds(start) + 6;
                e.Seek(0); e.Play(); var pcm = Render(e, 8 * Sr);
                yield return (Energy(pcm, end - 1.8, end - 1.2) > .01 &&
                    Energy(pcm, end - .9, end - .7) > Energy(pcm, end - .3, end - .1) * 3,
                    $"tempo rendering: natural-rate release follows the mapped end, reverse {reverse}, start {start}");
            }
            foreach (bool warped in new[] { false, true })
            foreach (bool deferred in new[] { false, true })
            foreach (double start in new[] { 0.0, 4.0 })
            {
                using var e = new NotaEngine(); Setup(e);
                e.SetDeferWarpBuild(deferred);
                int t = e.AddAudioTrack(); e.AddAudioClip(t, tone, 0);
                if (warped) { e.SetClipWarp(t, 0, true, 5); e.SetClipWarpLength(t, 0, 8); }
                e.Seek(2); e.Play(); Render(e, 1);
                double position = e.PositionBeats; bool running = e.IsPlaying;
                bool consolidated = e.ConsolidateRange(new[] { t }, start, 8);
                e.TryGetAudioClipInfo(t, 0, out var info);
                // A nonzero range leaves the original clip's earlier portion before the result.
                if (start != 0) e.TryGetAudioClipInfo(t, 1, out info);
                long expected = (long)((e.BeatToSeconds(8) - e.BeatToSeconds(start)) * Sr);
                yield return (consolidated && info.LengthFrames == expected &&
                    Math.Abs(e.PositionBeats - position) < 1e-8 && e.IsPlaying == running,
                    $"tempo rendering: consolidation retains mapped duration and transport, warped {warped}, deferred {deferred}, start {start}");
                Build(e, 1); e.Seek(0); var pcm = Render(e, 6 * Sr);
                yield return (Energy(pcm, 4.8, 5.7) > .01,
                    $"tempo rendering: consolidation keeps the ending audible, warped {warped}, deferred {deferred}, start {start}");
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
