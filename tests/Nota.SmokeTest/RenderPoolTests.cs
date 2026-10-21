// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Parallel render pool: performance regression guards (issue #223, the #212 review). The
// render threads must cost nothing while nothing renders, must stay parked while playing
// when the audio thread keeps up alone, must join when it can't (an overloaded small
// buffer), must not make a bounce slower than rendering on one thread, must render
// bit-identically to one thread, and must survive tracks being added and removed while
// blocks render. Real-time checks render through the device callback's own path
// (RenderDeviceBlock), not the offline one.
//
// Hardware-independent by construction: every check compares the parallel engine with the
// same session rendered on the audio thread alone (render threads = 0) in this process,
// or measures the render threads' own CPU clocks (nota_engine_render_threads_cpu_seconds)
// rather than the whole process, so the .NET runtime and earlier tests add no noise.
// Render CPU = time spent inside the render call (the calling thread renders or waits there)
// + the render threads' CPU. Only the wall-clock and late-block comparisons are timing
// budgets.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --perf`.

using System.Diagnostics;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class RenderPoolTests
{
    private const int Block = 512, Rate = 48000;
    private static readonly double BlockSeconds = (double)Block / Rate;

    private static string NoiseClip()
    {
        string path = Path.Combine(Path.GetTempPath(), "nota-perf-noise.wav");
        if (!File.Exists(path))
        {
            var rng = new Random(1);
            WavWriter.WriteStereo(path, 8, Rate, _ => (rng.NextDouble() * 0.5 - 0.25, rng.NextDouble() * 0.5 - 0.25));
        }
        return path;
    }

    // `tracks` audio tracks, each a noise clip through a Reverb — real DSP on every track.
    private static NotaEngine AudioSession(int tracks, int renderThreads)
    {
        var e = new NotaEngine();
        if (renderThreads >= 0) e.RenderThreads = renderThreads;
        string clip = NoiseClip();
        for (int i = 0; i < tracks; i++)
        {
            int t = e.AddAudioTrack();
            e.AddAudioClip(t, clip, 0.0);
            e.AddBuiltinDevice(t, 2); // Reverb
        }
        var b = new float[Block * 2];
        e.RenderOffline(b, Block, Rate); // prepare every node at the rate
        e.Seek(0); e.Play();
        return e;
    }

    // Parallel engines get at least two render threads, so the comparison means something
    // on a two-core runner too (where the default is none).
    private static int ParallelThreads()
    {
        using var probe = new NotaEngine();
        return Math.Max(probe.RenderThreads, 2);
    }

    // Renders `seconds` of audio in `block`-frame blocks; paced = one block per buffer period
    // through the device callback's path (RenderDeviceBlock), else offline like a bounce, with
    // `gapMs` of other work between blocks (an exporter encodes each chunk before the next).
    // Returns render CPU s, wall s, the render threads' share of the CPU, and blocks that took
    // longer than their buffer period (their indices into `lateAt`, if given).
    private static (double Cpu, double Wall, double Threads, int Late) Measure(
        NotaEngine e, double seconds, bool paced, int block = Block, double gapMs = 0, List<int>? lateAt = null)
    {
        var b = new float[block * 2];
        double period = (double)block / Rate;
        int blocks = (int)(seconds / period), late = 0;
        double inside = 0, workers0 = e.RenderThreadsCpuSeconds;
        var wall = Stopwatch.StartNew();
        var call = new Stopwatch();
        for (int i = 0; i < blocks; i++)
        {
            call.Restart();
            if (paced) e.RenderDeviceBlock(b, block); else e.RenderOffline(b, block);
            double took = call.Elapsed.TotalSeconds;
            inside += took;
            if (took > period) { late++; lateAt?.Add(i); }
            if (gapMs > 0)
            {
                var gap = Stopwatch.StartNew();
                while (gap.Elapsed.TotalMilliseconds < gapMs) Thread.SpinWait(50);
            }
            if (paced)
            {
                double ahead = (i + 1) * period - wall.Elapsed.TotalSeconds;
                if (ahead > 0.0005) Thread.Sleep(TimeSpan.FromSeconds(ahead));
            }
        }
        double threads = e.RenderThreadsCpuSeconds - workers0;
        return (inside + threads, wall.Elapsed.TotalSeconds, threads, late);
    }

    private static float[] RenderSynths(int renderThreads)
    {
        using var e = new NotaEngine();
        e.RenderThreads = renderThreads;
        e.SetBpm(120);
        for (int i = 0; i < 8; i++)
        {
            int t = e.AddInstrumentTrack();
            int clip = e.AddMidiClip(t, 0.0, 4.0);
            e.SetClipNotes(t, clip, new[] { new NotaNote(48 + i * 3, 0.0, 2.0, 0.8f), new NotaNote(60 + i, 1.0, 3.0, 0.7f) });
            e.AddBuiltinDevice(t, 3); // Delay: state carried across blocks
        }
        e.Seek(0); e.Play();
        var all = new float[24 * Block * 2];
        var b = new float[Block * 2];
        for (int i = 0; i < 24; i++) { e.RenderOffline(b, Block); Array.Copy(b, 0, all, i * Block * 2, Block * 2); }
        return all;
    }

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        int par = ParallelThreads();

        // 1. Idle: render threads park. A spinning pool costs ~1 s of CPU per thread per second.
        using (var e = new NotaEngine())
        {
            e.RenderThreads = par;
            double c0 = e.RenderThreadsCpuSeconds;
            Thread.Sleep(1000);
            double used = e.RenderThreadsCpuSeconds - c0;
            yield return (used < 0.02,
                $"idle engine: {par} render threads use {used * 1000:0.0} ms CPU over 1 s (parked, < 20 ms)");
        }

        // 2. After playback: once blocks stop coming, the threads park again.
        using (var e = AudioSession(8, par))
        {
            Measure(e, 0.5, paced: true);
            e.StopTransport();
            Thread.Sleep(100); // let the last job's linger run out
            double c0 = e.RenderThreadsCpuSeconds;
            Thread.Sleep(1000);
            double used = e.RenderThreadsCpuSeconds - c0;
            yield return (used < 0.02,
                $"after playback stops, render threads use {used * 1000:0.0} ms CPU over 1 s (parked, < 20 ms)");
        }

        // 3. Playing in real time when one thread keeps up: the render threads stay out of it
        //    (spreading a block that fits the buffer over parked threads costs 2-3x the CPU and
        //    misses more deadlines; the #212 pool spun between blocks: ~40x).
        (double Cpu, double Wall, double Threads, int Late) ser, prl;
        using (var e = AudioSession(16, 0)) ser = Measure(e, 2.0, paced: true);
        using (var e = AudioSession(16, par)) prl = Measure(e, 2.0, paced: true);
        {
            double s = ser.Cpu / ser.Wall * 100, p = prl.Cpu / prl.Wall * 100, t = prl.Threads / prl.Wall * 100;
            yield return (t < 2,
                $"playing 16 tracks in real time, one thread keeps up: render threads stay parked ({t:0.0} % of a core, < 2)");
            yield return (p <= 1.5 * s + 3,
                $"playing 16 tracks in real time: render CPU {p:0.0} % of a core vs {s:0.0} % on one thread (<= 1.5x + 3)");
        }

        // 3b. Overloaded (one thread can't make a 64-frame deadline): the render threads join,
        //     and stay joined while the overload lasts. The load is sized for this machine:
        //     one thread needs ~1.3 buffer periods, which the pool covers with room to spare.
        //     A mild overload is the hard case for leaving the pool: dropping to one thread to
        //     re-measure (the #232 pool, every 2 s) made a run of late blocks each time.
        //     6 s spans several of those hold expiries.
        const int Small = 64;
        double smallPeriod = (double)Small / Rate;
        int overloadTracks;
        using (var e = AudioSession(32, 0))
        {
            var cal = Measure(e, 0.5, paced: false, block: Small);
            double perBlock = cal.Cpu / (int)(0.5 / smallPeriod);
            overloadTracks = Math.Clamp((int)Math.Round(32 * 1.3 * smallPeriod / perBlock), 4, 2048);
        }
        using (var e = AudioSession(overloadTracks, 0)) ser = Measure(e, 1.0, paced: true, block: Small);
        int serBlocks = (int)(1.0 / smallPeriod);
        if (ser.Late * 2 < serBlocks)
            yield return (true, $"overload check skipped: one thread renders {overloadTracks} tracks at a 64-frame buffer in time ({ser.Late} of {serBlocks} blocks late)");
        else
        {
            var lateAt = new List<int>();
            using (var e = AudioSession(overloadTracks, par)) prl = Measure(e, 6.0, paced: true, block: Small, lateAt: lateAt);
            int warm = (int)(0.5 / smallPeriod), blocks = (int)(6.0 / smallPeriod) - warm;
            int lateWarm = lateAt.Count(i => i >= warm);
            // A burst: late blocks crowded into 50 ms. Scheduler noise makes isolated ones.
            int window = (int)(0.05 / smallPeriod), burst = 0;
            for (int k = 0, j = 0; k < lateAt.Count; k++)
            {
                while (lateAt[k] - lateAt[j] >= window) j++;
                if (lateAt[k] >= warm) burst = Math.Max(burst, k - j + 1);
            }
            yield return (prl.Threads > 0.05,
                $"overloaded ({overloadTracks} tracks, 64-frame buffer, {ser.Late} of {serBlocks} blocks late on one thread): render threads join ({prl.Threads * 1000:0} ms CPU)");
            yield return TimingBudget.Check(true, lateWarm <= blocks / 100,
                $"overloaded: {lateWarm} of {blocks} blocks late with render threads after warm-up (<= 1 %)");
            yield return TimingBudget.Check(true, burst <= 4,
                $"overloaded: at most {burst} late blocks within 50 ms (<= 4: no bursts when the pool re-checks the load)");
        }

        // 4. Bounce (blocks back-to-back): CPU work bounded, and never slower than one thread.
        using (var e = AudioSession(16, 0)) ser = Measure(e, 3.0, paced: false);
        using (var e = AudioSession(16, par)) prl = Measure(e, 3.0, paced: false);
        yield return (prl.Cpu <= 3 * ser.Cpu + 0.05,
            $"bouncing 16 tracks: parallel CPU work {prl.Cpu * 1000:0} ms vs {ser.Cpu * 1000:0} ms on one thread (<= 3x)");
        yield return TimingBudget.Check(true, prl.Wall <= 1.3 * ser.Wall + 0.01,
            $"bouncing 16 tracks: parallel {prl.Wall * 1000:0} ms vs {ser.Wall * 1000:0} ms on one thread (not slower, <= 1.3x)");

        // 4b. Export and freeze: 8192-frame chunks with the encoder's work between them. The
        //     render threads must take a real share (the #232 pool took the gap for a device
        //     callback and rendered them on one thread: 3-4x slower than develop).
        const int Chunk = 8192;
        using (var e = AudioSession(16, 0)) ser = Measure(e, 6.0, paced: false, block: Chunk, gapMs: 2);
        using (var e = AudioSession(16, par)) prl = Measure(e, 6.0, paced: false, block: Chunk, gapMs: 2);
        yield return (prl.Threads >= 0.2 * prl.Cpu,
            $"exporting 16 tracks in 8192-frame chunks: render threads do {prl.Threads / prl.Cpu * 100:0} % of the work (>= 20 %)");
        yield return TimingBudget.Check(true, prl.Wall <= 1.3 * ser.Wall + 0.01,
            $"exporting 16 tracks in 8192-frame chunks: parallel {prl.Wall * 1000:0} ms vs {ser.Wall * 1000:0} ms on one thread (not slower, <= 1.3x)");

        // 5. Parallel rendering is transparent: same samples as one thread.
        var one = RenderSynths(0);
        var many = RenderSynths(par);
        double diff = 0, rms = 0;
        for (int i = 0; i < one.Length; i++) { diff = Math.Max(diff, Math.Abs(one[i] - many[i])); rms += one[i] * (double)one[i]; }
        rms = Math.Sqrt(rms / one.Length);
        yield return (rms > 1e-3 && diff == 0.0,
            $"8 synth tracks render bit-identically on {par} render threads and on one (rms {rms:0.0000}, maxdiff {diff:E1})");

        // 6. Tracks added and removed while blocks render (the render scratch used to be
        //    reallocated under a block in flight: use-after-free, crashes).
        using (var e = AudioSession(1, par))
        {
            long rendered = 0;
            var stop = false;
            var render = new Thread(() =>
            {
                var b = new float[256 * 2];
                while (!Volatile.Read(ref stop)) { e.RenderOffline(b, 256); Interlocked.Increment(ref rendered); }
            });
            render.Start();
            string clip = NoiseClip();
            for (int round = 0; round < 2; round++)
            {
                var ids = new List<int>();
                for (int i = 0; i < 24; i++)
                {
                    int t = i % 2 == 0 ? e.AddAudioTrack() : e.AddInstrumentTrack();
                    if (i % 2 == 0) { e.AddAudioClip(t, clip, 0.0); e.AddBuiltinDevice(t, 2); }
                    ids.Add(t);
                    Thread.Sleep(1);
                }
                foreach (int t in ids) e.RemoveTrack(t);
            }
            Volatile.Write(ref stop, true);
            render.Join();
            yield return (Interlocked.Read(ref rendered) > 100,
                $"adding and removing 48 tracks while blocks render: no crash ({rendered} blocks rendered)");
        }
    }
}
