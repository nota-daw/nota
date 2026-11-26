// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Real VST3 instances through the shipped JUCE adapters and engine C ABI. Run in a
// child process with its own catalog, so the fixture never enters the user's library.

using System.Diagnostics;
using System.Text.Json;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class HostedRenderPoolTests
{
    private const int Rate = 48000, Block = 128;
    private static string NativeDir
    {
        get
        {
            if (Environment.GetEnvironmentVariable("NOTA_RENDER_NATIVE_DIR") is { Length: > 0 } path) return path;
            for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
            {
                path = Path.Combine(dir.FullName, "src", "native", "nota.engine", "build");
                if (Directory.Exists(path)) return path;
            }
            throw new DirectoryNotFoundException("Native build directory unavailable for hosted render tests.");
        }
    }

    public static IEnumerable<(bool Ok, string Label)> RunIsolated()
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(HostedRenderPoolTests).Assembly.Location);
        start.ArgumentList.Add("--hosted-render-pool");
        using var child = Process.Start(start)!;
        // A native hang in the child must fail this module, not stall the whole
        // smoke suite: drain both streams while a timeout runs (ReadToEnd blocks,
        // so a timeout after it would never fire), then kill the child's tree.
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(TimeSpan.FromMinutes(10)))
        {
            try { child.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { } // already exited between the timeout and the kill
            stdout.GetAwaiter().GetResult();
            stderr.GetAwaiter().GetResult();
            yield return (false, "isolated real VST3 render-pool regressions (did not finish within 10 minutes, killed)");
            yield break;
        }
        Console.Write(stdout.GetAwaiter().GetResult());
        if (child.ExitCode != 0) Console.Write(stderr.GetAwaiter().GetResult());
        yield return (child.ExitCode == 0, "isolated real VST3 render-pool regressions");

        if (OperatingSystem.IsMacOS())
        {
            var auStart = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            auStart.ArgumentList.Add(typeof(HostedRenderPoolTests).Assembly.Location);
            auStart.ArgumentList.Add("--hosted-au-pool");
            using var auChild = Process.Start(auStart)!;
            var auOut = auChild.StandardOutput.ReadToEndAsync();
            var auErr = auChild.StandardError.ReadToEndAsync();
            if (!auChild.WaitForExit(TimeSpan.FromMinutes(3)))
            {
                try { auChild.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                auOut.GetAwaiter().GetResult();
                auErr.GetAwaiter().GetResult();
                yield return (false, "isolated macOS AU render-pool regressions (timed out, killed)");
                yield break;
            }
            Console.Write(auOut.GetAwaiter().GetResult());
            if (auChild.ExitCode != 0) Console.Write(auErr.GetAwaiter().GetResult());
            yield return (auChild.ExitCode == 0, "isolated macOS real AudioUnit planar + parallel regressions");
        }
    }

    private static int Param(NotaEngine e, int track, int device, string name)
    {
        for (int i = 0; i < e.PluginParamCount(track, device); i++)
            if (e.PluginParamName(track, device, i) == name) return i;
        throw new InvalidOperationException($"Render probe parameter missing: {name}");
    }

    private static NotaEngine Session(int catalog, int helpers, int work, bool inserts = false, bool routing = false, int tracks = 4)
    {
        var e = new NotaEngine { RenderThreads = helpers };
        e.RenderOffline(new float[Block * 2], Block, Rate);
        e.SetBpm(120);
        int group = routing ? e.AddGroupTrack() : -1;
        if (routing) e.AddReturnTrack();
        int previousTrack = -1;
        for (int i = 0; i < tracks; i++)
        {
            int t = inserts ? e.AddInstrumentTrack() : e.AddPluginInstrumentTrack(catalog);
            int device = inserts ? e.AddTrackEffectPlugin(t, catalog) : -1;
            e.WaitPluginLoads(60000); // plug-ins load after they are inserted (specs/113)
            e.SetTrackVolume(t, 0.2f);
            e.SetTrackPan(t, (i - 1.5f) / 4);
            int clip = e.AddMidiClip(t, 0, 16);
            e.SetClipNotes(t, clip, [new NotaNote(48 + i * 3, 0, 0.15, 0.8f),
                new NotaNote(60 + i, 0.2, 8, 0.7f)]);
            e.PluginParamSet(t, device, Param(e, t, device, "Work"), work / 4096f);
            if (routing)
            {
                e.SetTrackGroup(t, group); e.SetTrackSend(t, 0, 0.1f);
                int lane = e.AddPluginAutomationLane(t, device, e.PluginParamId(t, device, Param(e, t, device, "Gain")));
                e.SetAutomationPoints(t, lane, [new AutomationPoint(0, 0.3f), new AutomationPoint(1, 0.8f)]);
                if (inserts && previousTrack >= 0) e.SetDeviceSidechainSource(t, device, previousTrack);
            }
            previousTrack = t;
        }
        e.Seek(0); e.Play(); return e;
    }

    private static float[] Render(NotaEngine e, int blocks = 128, Action<int>? edit = null)
    {
        var all = new float[blocks * Block * 2]; var block = new float[Block * 2];
        for (int i = 0; i < blocks; i++)
        {
            edit?.Invoke(i);
            e.RenderOffline(block, Block);
            block.CopyTo(all, i * Block * 2);
        }
        return all;
    }

    private sealed record Measurement(double MedianMs, double P95Ms, double CallerMs,
        double WorkerMs, double DeadlinePercent, int Late, int Blocks);

    private static Measurement Measure(NotaEngine e, bool device, int blocks = 100, int block = Block, int warmup = 80)
    {
        var buffer = new float[block * 2];
        // Adaptive device scheduling must settle before measuring participation/latency.
        for (int i = 0; i < warmup; i++)
            if (device) e.RenderDeviceBlock(buffer, block); else e.RenderOffline(buffer, block);
        var samples = new double[blocks];
        double workerStart = e.RenderThreadsCpuSeconds;
        var wall = Stopwatch.StartNew();
        for (int i = 0; i < blocks; i++)
        {
            long before = Stopwatch.GetTimestamp();
            if (device) e.RenderDeviceBlock(buffer, block); else e.RenderOffline(buffer, block);
            samples[i] = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
            if (buffer.Any(v => !float.IsFinite(v))) throw new InvalidOperationException("Reentrant/non-finite plugin render");
            if (device)
            {
                double due = (i + 1) * block * 1000.0 / Rate;
                double ahead = due - wall.Elapsed.TotalMilliseconds;
                if (ahead > 1) Thread.Sleep(TimeSpan.FromMilliseconds(ahead - 0.5));
                while (wall.Elapsed.TotalMilliseconds < due) Thread.SpinWait(25);
            }
        }
        double workerMs = (e.RenderThreadsCpuSeconds - workerStart) * 1000;
        double callerMs = samples.Sum(); int late = samples.Count(t => t > block * 1000.0 / Rate);
        Array.Sort(samples);
        return new(samples[blocks / 2], samples[(int)(blocks * 0.95)], callerMs, workerMs,
            callerMs / blocks / (block * 1000.0 / Rate) * 100, late, blocks);
    }

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        string scratch = Directory.CreateTempSubdirectory("nota-hosted-render-").FullName;
        NotaEngine.SetDataDir(scratch);
        try
        {
            string fixture = Path.Combine(NativeDir, "nota-render-probe_artefacts", "Release", "VST3");
            string worker = Path.Combine(NativeDir, OperatingSystem.IsWindows() ? "nota-scanworker.exe" : "nota-scanworker");
            if (!Directory.Exists(fixture)) throw new DirectoryNotFoundException("Build nota-render-probe_VST3 before hosted render tests.");
            NotaEngine.ScanPlugins(worker, fixture, true);
            int catalog = Enumerable.Range(0, NotaEngine.PluginCount)
                .First(i => NotaEngine.PluginDescription(i)?.StartsWith("Nota Render Probe |") == true);
            yield return (catalog >= 0, "real VST3 fixture scanned into isolated temporary catalog");

            foreach (var result in BufferChecks(catalog)) yield return result;

            foreach (bool inserts in new[] { false, true })
            {
                using var one = Session(catalog, 0, 32, inserts, routing: true);
                using var many = Session(catalog, 4, 32, inserts, routing: true);
                var a = Render(one); var b = Render(many);
                yield return (a.Any(v => Math.Abs(v) > 0.001) && a.All(float.IsFinite) && a.SequenceEqual(b),
                    $"VST3 {(inserts ? "inserts/sidechains" : "instruments")}: MIDI, automation, group/return output is sample-identical with helpers");
            }

            float[] Edited(int helpers)
            {
                using var e = Session(catalog, helpers, 32);
                var ids = Enumerable.Range(0, e.TrackCount).Select(i =>
                    e.TryGetTrackInfo(i, out var track) ? track.Id : -1).ToArray();
                int effect = e.AddTrackEffectPlugin(ids[0], catalog);
                e.WaitPluginLoads(60000);
                return Render(e, 80, i =>
                {
                    if (i == 8) e.SetDeviceBypassed(ids[0], effect, true);
                    if (i == 16) e.SetDeviceBypassed(ids[0], effect, false);
                    if (i == 24) e.RemoveDevice(ids[0], effect);
                    if (i == 32) { e.SetTrackInstrumentPlugin(ids[1], catalog); e.WaitPluginLoads(60000); }
                    if (i == 48) e.RemoveTrack(ids[2]);
                    if (i == 56 && !e.Undo()) throw new InvalidOperationException("Hosted remove undo failed");
                });
            }
            yield return (Edited(0).SequenceEqual(Edited(4)),
                "VST3 bypass, insert removal, instrument replacement and track undo preserve serial output");

            using (var effects = Session(catalog, 4, 1024, inserts: true))
            {
                var measured = Measure(effects, false, 40);
                yield return (measured.WorkerMs > 1, $"real VST3 insert adapters use helpers ({measured.WorkerMs:0.0} ms CPU)");
            }

            using (var inline = Session(catalog, 0, 1024))
            using (var parallel = Session(catalog, 4, 1024))
            {
                var serial = Measure(inline, false); var many = Measure(parallel, false);
                yield return (many.WorkerMs > 1, $"VST3 offline helpers participate ({many.WorkerMs:0.0} ms worker CPU)");
                yield return TimingBudget.Check(true, many.MedianMs < serial.MedianMs * 0.9,
                    $"four VST3 tracks: offline median {many.MedianMs:0.000} ms vs serial {serial.MedianMs:0.000} ms");
                yield return TimingBudget.Check(true, many.CallerMs + many.WorkerMs < serial.CallerMs * 3,
                    "VST3 parallel processing keeps total render CPU work bounded");
                Console.WriteLine("HOSTED_OFFLINE " + JsonSerializer.Serialize(new { serial, parallel = many }));
            }

            using (var inline = Session(catalog, 0, 2048))
            using (var parallel = Session(catalog, 4, 2048))
            {
                var serial = Measure(inline, true); var many = Measure(parallel, true);
                yield return (many.WorkerMs > 1, $"VST3 overloaded device helpers participate ({many.WorkerMs:0.0} ms worker CPU)");
                yield return TimingBudget.Check(true, many.MedianMs < serial.MedianMs * 0.9,
                    $"four VST3 tracks: device median {many.MedianMs:0.000} ms vs serial {serial.MedianMs:0.000} ms");
                // The Work=2048 probe is fixed, not machine-scaled: a single instance
                // may cost more than the callback period on a slower machine, where no
                // helper count can save the deadline. Calibrate like RenderPoolTests'
                // overload check and enforce the late-block gate only when one instance
                // could meet the period at all.
                double devicePeriodMs = Block * 1000.0 / Rate;
                bool oneInstanceFits;
                using (var cal = Session(catalog, 0, 2048, tracks: 1))
                    oneInstanceFits = Measure(cal, true, 40).MedianMs <= devicePeriodMs;
                yield return TimingBudget.Check(true, !oneInstanceFits || many.Late <= 1,
                    !oneInstanceFits
                        ? $"one VST3 probe render does not fit a {devicePeriodMs:0.00} ms period on this machine; the late-block gate is skipped ({many.Late}/{many.Blocks} late with helpers)"
                        : $"four indivisible VST3 jobs: {many.Late}/{many.Blocks} device deadlines missed after warm-up (<= 1)");
                Console.WriteLine("HOSTED_DEVICE " + JsonSerializer.Serialize(new { serial, parallel = many }));
                if (Environment.GetEnvironmentVariable("NOTA_RENDER_REPORT") is { Length: > 0 } report)
                    File.WriteAllText(report, JsonSerializer.Serialize(new { serial, parallel = many }, new JsonSerializerOptions { WriteIndented = true }));
            }

            using (var light = Session(catalog, 4, 0))
            {
                var measured = Measure(light, true, 150);
                yield return (measured.WorkerMs < 15, $"light VST3 device workload leaves helpers parked ({measured.WorkerMs:0.0} ms CPU)");
            }

            using (var oneHeavy = Session(catalog, 0, 1024, tracks: 1))
            using (var fourHeavy = Session(catalog, 4, 1024))
            {
                var one = Measure(oneHeavy, true, block: 512);
                var four = Measure(fourHeavy, true, block: 512);
                yield return TimingBudget.Check(true, four.MedianMs <= one.MedianMs * 1.6,
                    $"512-frame large VST3 jobs reserve headroom: four {four.MedianMs:0.000} ms vs one {one.MedianMs:0.000} ms (<= 1.6x)");
                yield return TimingBudget.Check(true, four.CallerMs + four.WorkerMs <= one.CallerMs * 4 * 2,
                    "512-frame VST3 headroom keeps caller/worker work proxy bounded (<= 2x four serial tracks)");
                Console.WriteLine("HOSTED_HEADROOM " + JsonSerializer.Serialize(new { frames = 512, one, four }));
            }

            using (var rack = new NotaEngine { RenderThreads = 4 })
            {
                rack.RenderOffline(new float[Block * 2], Block, Rate);
                int t = rack.AddInstrumentRackTrack();
                rack.RackAddPluginInstrumentChain(t, catalog);
                rack.WaitPluginLoads(60000);
                int clip = rack.AddMidiClip(t, 0, 16);
                rack.SetClipNotes(t, clip, [new NotaNote(60, 0, 8, 0.8f)]); rack.Play();
                double cpu = rack.RenderThreadsCpuSeconds;
                var samples = Render(rack, 64);
                yield return (samples.Any(v => Math.Abs(v) > 0.001) && rack.RenderThreadsCpuSeconds - cpu < 0.01,
                    "hosted instrument rack retains serial fallback and audible output");
            }

            foreach (var result in SleepGateChecks(catalog)) yield return result;

            foreach (var result in PlanarPluginChainChecks(catalog)) yield return result;

            using (var live = Session(catalog, 4, 32))
            {
                bool stop = false, finite = true;
                int rendered = 0;
                Exception? renderFailure = null;
                var render = new Thread(() =>
                {
                    try
                    {
                        var block = new float[Block * 2];
                        while (!Volatile.Read(ref stop))
                        {
                            live.RenderOffline(block, Block);
                            finite &= block.All(float.IsFinite);
                            Interlocked.Increment(ref rendered);
                        }
                    }
                    catch (Exception ex) { renderFailure = ex; }
                });
                render.Start();
                try
                {
                    for (int i = 0; i < 8; i++)
                    {
                        int added = live.AddPluginInstrumentTrack(catalog);
                        int clip = live.AddMidiClip(added, 0, 16);
                        live.SetClipNotes(added, clip, [new NotaNote(60, 0, 8, 0.8f)]);
                        Thread.Sleep(2);
                        live.RemoveTrack(added);
                    }
                }
                finally { Volatile.Write(ref stop, true); render.Join(); }
                yield return (rendered > 0 && finite && renderFailure is null,
                    $"VST3 graph additions/removals during worker renders stay finite and complete ({rendered} blocks)");
            }
        }
        finally { NotaEngine.SetDataDir(null); }
    }

    private static IEnumerable<(bool Ok, string Label)> SleepGateChecks(int catalog)
    {
        NotaEngine MakeIdleInstEngine(PluginSleepMode mode, int helpers = 0)
        {
            var e = new NotaEngine { RenderThreads = helpers };
            e.RenderOffline(new float[Block * 2], Block, Rate);
            e.SetBpm(120);
            int t = e.AddPluginInstrumentTrack(catalog);
            e.WaitPluginLoads(60000);
            e.SetTrackVolume(t, 0.5f);
            e.PluginParamSet(t, -1, Param(e, t, -1, "Work"), 1.0f);
            e.PluginParamSet(t, -1, Param(e, t, -1, "Gain"), 1.0f);
            e.SetPluginSleepMode(t, -1, mode);
            int c1 = e.AddMidiClip(t, 0.0, 0.15);
            e.SetClipNotes(t, c1, [new NotaNote(60, 0.0, 0.05, 0.9f)]);
            int c2 = e.AddMidiClip(t, 2.0, 0.5);
            e.SetClipNotes(t, c2, [new NotaNote(67, 0.0, 0.4, 0.9f)]);
            e.Seek(0);
            e.Play();
            return e;
        }

        // Measure Auto vs Never while silent past the 250 ms hangover (beat 0.2..1.9 = ~640 blocks @ 128 frames).
        static double MeasureQuietRegionMs(int cat, PluginSleepMode mode)
        {
            using var e = new NotaEngine { RenderThreads = 0 };
            e.RenderOffline(new float[Block * 2], Block, Rate);
            e.SetBpm(120);
            int t = e.AddPluginInstrumentTrack(cat);
            e.WaitPluginLoads(60000);
            e.PluginParamSet(t, -1, Param(e, t, -1, "Work"), 0.5f); // 2048 trig ops/sample
            e.SetPluginSleepMode(t, -1, mode);
            var buf = new float[Block * 2];
            for (int i = 0; i < 120; i++) e.RenderOffline(buf, Block); // settle past 250 ms
            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < 120; i++) e.RenderOffline(buf, Block);
            return Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }

        double autoMs = MeasureQuietRegionMs(catalog, PluginSleepMode.Auto);
        double neverMs = MeasureQuietRegionMs(catalog, PluginSleepMode.Never);
        yield return (autoMs < neverMs * 0.2,
            $"VST3 PluginInstrument sleeps on silence vs Never ({autoMs:0.00} ms vs {neverMs:0.00} ms)");

        using (var a0 = MakeIdleInstEngine(PluginSleepMode.Auto, 0))
        using (var a4 = MakeIdleInstEngine(PluginSleepMode.Auto, 4))
        {
            const int blocks = 550; // > 1.46 s @ 128 frames: beat 0 hit -> >0.9s silence -> beat 2..2.5 wake (blocks 375..469)
            var r0 = Render(a0, blocks);
            var r4 = Render(a4, blocks);
            float quietPeak = r0.Skip(200 * Block * 2).Take(160 * Block * 2).Select(Math.Abs).Max();
            float wakePeak = r0.Skip(375 * Block * 2).Take(94 * Block * 2).Select(Math.Abs).Max();
            yield return (quietPeak == 0f && wakePeak > 0.005f && r0.SequenceEqual(r4) && r0.All(float.IsFinite),
                $"VST3 PluginInstrument sleeps on quiet gap (quiet={quietPeak:0.0000}) and wakes cleanly (wakePeak={wakePeak:0.000})");
        }

        // VST3 PluginEffect: settles on silence, wakes cleanly on sidechain and main audio input.
        using (var fxAuto = new NotaEngine { RenderThreads = 0 })
        using (var fxAutoMt = new NotaEngine { RenderThreads = 4 })
        {
            static float[] RenderFx(NotaEngine e, int cat)
            {
                e.RenderOffline(new float[Block * 2], Block, Rate);
                e.SetBpm(120);
                int src = e.AddBassSynthTrack();
                int main = e.AddInstrumentTrack();
                int eff = e.AddTrackEffectPlugin(main, cat);
                e.WaitPluginLoads(60000);
                e.PluginParamSet(main, eff, Param(e, main, eff, "Work"), 0.0f);
                e.PluginParamSet(main, eff, Param(e, main, eff, "Gain"), 1.0f);
                e.SetDeviceSidechainSource(main, eff, src);
                e.SetDeviceSidechainMix(main, eff, 1.0f);
                int cSrc = e.AddMidiClip(src, 1.5, 0.5); // sidechain wakes sleeping PluginEffect at beat 1.5 (blocks 281..375)
                e.SetClipNotes(src, cSrc, [new NotaNote(48, 0.0, 0.4, 0.9f)]);
                e.Seek(0);
                e.Play();
                return Render(e, 450);
            }
            var f0 = RenderFx(fxAuto, catalog);
            var f4 = RenderFx(fxAutoMt, catalog);
            float quietPeak = f0.Skip(150 * Block * 2).Take(120 * Block * 2).Select(Math.Abs).Max();
            float scWakePeak = f0.Skip(281 * Block * 2).Take(94 * Block * 2).Select(Math.Abs).Max();
            yield return (quietPeak == 0f && scWakePeak > 0.001f && f0.SequenceEqual(f4) && f0.All(float.IsFinite),
                $"VST3 PluginEffect sleeps past hangover (quiet={quietPeak:0.0000}) and wakes on sidechain audio (peak={scWakePeak:0.000})");
        }

        int edCode = NotaEngine.PluginEditorSleepSelfTest(catalog);
        yield return (edCode == 0,
            $"VST3 open editor keeps PluginInstrument, Audio/Muted/Group PluginEffect, Hybrid Auto, and Racks awake (code={edCode})");
    }

    private static IEnumerable<(bool Ok, string Label)> BufferChecks(int catalog)
    {
        foreach (bool inserts in new[] { false, true })
        {
            using var smallEngine = Session(catalog, 0, 0, inserts);
            using var largeEngine = Session(catalog, 0, 0, inserts);
            foreach (var eng in new[] { smallEngine, largeEngine })
            {
                for (int i = 0; i < eng.TrackCount; i++)
                {
                    if (!eng.TryGetTrackInfo(i, out var tr)) continue;
                    eng.SetPluginSleepMode(tr.Id, -1, PluginSleepMode.Never);
                    if (inserts) eng.SetPluginSleepMode(tr.Id, 0, PluginSleepMode.Never);
                }
            }
            smallEngine.StopTransport(); largeEngine.StopTransport();
            bool clean = true;
            foreach (int frames in new[] { 128, 16, 8192, 64, 1024, 16384, 128 })
            {
                var samples = new float[frames * 2];
                smallEngine.RenderOffline(samples, frames);
                clean &= samples.All(v => float.IsFinite(v) && Math.Abs(v) < 1e-7);
            }
            string adapter = inserts ? "insert" : "instrument";
            yield return (clean, $"VST3 {adapter}: changing callback sizes leave no stale main/sidechain input");
            var small = Measure(smallEngine, false, 400);
            var large = Measure(largeEngine, false, 40, block: 8192, warmup: 8);
            yield return TimingBudget.Check(true, small.MedianMs * 32 < large.MedianMs,
                $"56-channel VST3 {adapter}: 128-frame cost {small.MedianMs:0.0000} ms scales below 1/32 of 8192-frame cost {large.MedianMs:0.0000} ms");
            Console.WriteLine("HOSTED_BUFFER " + JsonSerializer.Serialize(new { adapter, channels = 56, small, large }));
        }
    }

    private static IEnumerable<(bool Ok, string Label)> PlanarPluginChainChecks(int catalog)
    {
        static (float[] Pcm, double WorkerMs) RenderPlanarChain(int cat, int threads)
        {
            using var e = new NotaEngine { RenderThreads = threads };
            e.RenderOffline(new float[Block * 2], Block, Rate);
            e.SetBpm(120);
            for (int t = 0; t < 4; t++)
            {
                int tr = e.AddVoltSynthTrack();
                e.SetTrackVolume(tr, 0.2f);
                for (int k = 0; k < 4; k++)
                {
                    int fx = e.AddTrackEffectPlugin(tr, cat);
                    e.WaitPluginLoads(60000);
                    e.PluginParamSet(tr, fx, Param(e, tr, fx, "Work"), 0.02f);
                    e.PluginParamSet(tr, fx, Param(e, tr, fx, "Gain"), 1.0f);
                }
                int clip = e.AddMidiClip(tr, 0, 8);
                e.SetClipNotes(tr, clip, [new NotaNote(48 + t * 2, 0, 2, 0.8f), new NotaNote(60 + t, 0.5, 2, 0.7f)]);
            }
            e.Seek(0);
            e.Play();
            double cpu0 = e.RenderThreadsCpuSeconds;
            var pcm = Render(e, 48);
            return (pcm, (e.RenderThreadsCpuSeconds - cpu0) * 1000.0);
        }

        var c0 = RenderPlanarChain(catalog, 0);
        var c4 = RenderPlanarChain(catalog, 4);
        bool ok = c0.Pcm.Any(v => Math.Abs(v) > 0.001f) && c0.Pcm.All(float.IsFinite) && c0.Pcm.SequenceEqual(c4.Pcm) && c4.WorkerMs > 0.0;
        yield return (ok, $"VST3 contiguous 4-plugin planar chain: 0 vs 4 threads sample-identical (worker={c4.WorkerMs:0.0} ms)");
    }

    public static IEnumerable<(bool Ok, string Label)> RunAuMacOs()
    {
        if (!OperatingSystem.IsMacOS()) yield break;

        string scratch = Directory.CreateTempSubdirectory("nota-hosted-au-").FullName;
        try
        {
            string auBundle = Path.Combine(NativeDir, "nota-render-probe_artefacts", "Release", "AU", "Nota Render Probe.component");
            yield return (Directory.Exists(auBundle), $"macOS AU fixture bundle built at {auBundle}");

            string worker = Path.Combine(NativeDir, "nota-scanworker");
            string ProbeAuXml(string identifier)
            {
                var psi = new ProcessStartInfo(worker)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("AudioUnit");
                psi.ArgumentList.Add(identifier);
                using var p = Process.Start(psi)!;
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                int s = stdout.IndexOf("<PLUGIN", StringComparison.Ordinal);
                int e = stdout.LastIndexOf("</plugins>", StringComparison.Ordinal);
                if (s < 0 || e < s) throw new InvalidOperationException($"Failed to probe macOS AU '{identifier}'");
                return stdout[s..e].Trim();
            }

            string delyXml = ProbeAuXml("AudioUnit:Effects/aufx,dely,appl");
            string bpasXml = ProbeAuXml("AudioUnit:Effects/aufx,bpas,appl");
            File.WriteAllText(Path.Combine(scratch, "plugins.xml"),
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<KNOWNPLUGINS>\n{delyXml}\n{bpasXml}\n</KNOWNPLUGINS>\n");
            NotaEngine.SetDataDir(scratch);

            int delyIdx = -1, bpasIdx = -1;
            for (int i = 0; i < NotaEngine.PluginCount; i++)
            {
                string desc = NotaEngine.PluginDescription(i) ?? "";
                if (desc.Contains("AUDelay | AudioUnit", StringComparison.Ordinal)) delyIdx = i;
                else if (desc.Contains("AUBandpass | AudioUnit", StringComparison.Ordinal)) bpasIdx = i;
            }
            yield return (delyIdx >= 0 && bpasIdx >= 0, $"Apple system AudioUnits registered into isolated catalog (dely={delyIdx}, bpas={bpasIdx})");

            static (float[] Pcm, double WorkerMs) RenderAuSession(int fxA, int fxB, int threads)
            {
                using var e = new NotaEngine { RenderThreads = threads };
                e.RenderOffline(new float[512 * 2], 512, Rate);
                e.SetBpm(120);
                for (int t = 0; t < 4; t++)
                {
                    int tr = e.AddVoltSynthTrack();
                    e.SetTrackVolume(tr, 0.25f);
                    e.AddTrackEffectPlugin(tr, fxA);
                    e.AddTrackEffectPlugin(tr, fxB);
                    int clip = e.AddMidiClip(tr, 0, 8);
                    e.SetClipNotes(tr, clip, [new NotaNote(48 + t * 3, 0, 2, 0.8f), new NotaNote(60 + t, 0.25, 2, 0.7f)]);
                }
                e.WaitPluginLoads(60000);
                e.Seek(0);
                e.Play();
                double cpu0 = e.RenderThreadsCpuSeconds;
                var all = new float[32 * 512 * 2];
                var buf = new float[512 * 2];
                for (int b = 0; b < 32; b++)
                {
                    e.RenderOffline(buf, 512, Rate);
                    buf.CopyTo(all, b * 512 * 2);
                }
                return (all, (e.RenderThreadsCpuSeconds - cpu0) * 1000.0);
            }

            var au0 = RenderAuSession(delyIdx, bpasIdx, 0);
            var au4 = RenderAuSession(delyIdx, bpasIdx, 4);
            double maxDiff = 0.0;
            for (int i = 0; i < au0.Pcm.Length; i++) maxDiff = Math.Max(maxDiff, Math.Abs(au0.Pcm[i] - au4.Pcm[i]));
            bool ok = au0.Pcm.Any(v => Math.Abs(v) > 0.001f) && au0.Pcm.All(float.IsFinite) && maxDiff < 1e-5 && au4.WorkerMs > 0.0;
            yield return (ok, $"macOS real AUDelay+AUBandpass planar+parallel 4-track parity (maxDiff={maxDiff:0.0e0}, worker={au4.WorkerMs:0.0} ms)");

            int auEdCode = NotaEngine.PluginEditorSleepSelfTest(bpasIdx);
            yield return (auEdCode == 0, $"macOS real AudioUnit open editor keeps Audio/Muted/Group/Rack chains active past hangover (code={auEdCode})");
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
        }
    }
}
