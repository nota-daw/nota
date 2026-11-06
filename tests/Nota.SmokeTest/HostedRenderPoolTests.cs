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
        var stderr = child.StandardError.ReadToEndAsync();
        string stdout = child.StandardOutput.ReadToEnd();
        child.WaitForExit();
        Console.Write(stdout);
        if (child.ExitCode != 0) Console.Write(stderr.GetAwaiter().GetResult());
        yield return (child.ExitCode == 0, "isolated real VST3 render-pool regressions");
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
                return Render(e, 80, i =>
                {
                    if (i == 8) e.SetDeviceBypassed(ids[0], effect, true);
                    if (i == 16) e.SetDeviceBypassed(ids[0], effect, false);
                    if (i == 24) e.RemoveDevice(ids[0], effect);
                    if (i == 32) e.SetTrackInstrumentPlugin(ids[1], catalog);
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
                yield return TimingBudget.Check(true, many.Late <= 1,
                    $"four indivisible VST3 jobs: {many.Late}/{many.Blocks} device deadlines missed after warm-up (<= 1)");
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
                int clip = rack.AddMidiClip(t, 0, 16);
                rack.SetClipNotes(t, clip, [new NotaNote(60, 0, 8, 0.8f)]); rack.Play();
                double cpu = rack.RenderThreadsCpuSeconds;
                var samples = Render(rack, 64);
                yield return (samples.Any(v => Math.Abs(v) > 0.001) && rack.RenderThreadsCpuSeconds - cpu < 0.01,
                    "hosted instrument rack retains serial fallback and audible output");
            }

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

    private static IEnumerable<(bool Ok, string Label)> BufferChecks(int catalog)
    {
        foreach (bool inserts in new[] { false, true })
        {
            using var smallEngine = Session(catalog, 0, 0, inserts);
            using var largeEngine = Session(catalog, 0, 0, inserts);
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
}
