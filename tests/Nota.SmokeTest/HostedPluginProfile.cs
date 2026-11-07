// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Opt-in profile of an installed, licensed VST3. Offline device-path calls produce
// no hardware audio and use an isolated catalog; never part of the normal smoke run.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class HostedPluginProfile
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    private static extern bool PeekMessage(out WindowMessage message, IntPtr window, uint first, uint last, uint flags);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(in WindowMessage message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(in WindowMessage message);

    private static void Pump()
    {
        if (OperatingSystem.IsWindows())
            while (PeekMessage(out var message, IntPtr.Zero, 0, 0, 1))
            { TranslateMessage(in message); DispatchMessage(in message); }
        else NotaEngine.PumpPluginHost();
    }

    public static void Run(string pluginPath, int frames = 128, int rate = 48000)
    {
        string scratch = Directory.CreateTempSubdirectory("nota-plugin-profile-").FullName;
        NotaEngine.SetDataDir(scratch);
        string native = Path.GetFullPath("src/native/nota.engine/build");
        string worker = Path.Combine(native, OperatingSystem.IsWindows() ? "nota-scanworker.exe" : "nota-scanworker");
        NotaEngine.ScanPlugins(worker, Path.GetDirectoryName(pluginPath)!, false);
        string name = Path.GetFileNameWithoutExtension(pluginPath);
        int catalog = Enumerable.Range(0, NotaEngine.PluginCount)
            .First(i => NotaEngine.PluginDescription(i)?.Contains(name, StringComparison.OrdinalIgnoreCase) == true);
        foreach (var (instances, helpers) in new[] { (1, 4), (4, 0), (4, 4) })
        {
            Console.WriteLine($"PROFILE loading {instances} {name}, helpers={helpers}; {rate} Hz / {frames} frames");
            using var engine = new NotaEngine { RenderThreads = helpers };
            var buffer = new float[frames * 2];
            engine.RenderOffline(buffer, frames, rate);
            for (int i = 0; i < instances; i++)
            {
                int track = engine.AddPluginInstrumentTrack(catalog);
                engine.SetTrackVolume(track, 0.1f);
                int clip = engine.AddMidiClip(track, 0, 128);
                engine.SetClipNotes(track, clip, [new NotaNote(60 + i * 3, 0, 120, 0.8f)]);
                engine.SetMidiClipController(track, clip, 11, [new AutomationPoint(0, 0.8f)]);
                engine.SetMidiClipController(track, clip, 1, [new AutomationPoint(0, 0.8f)]);
                // Probe-only per-instance render load, the same knob the HostedRenderPoolTests
                // guard uses (Work reads 0..4096 / 4096). 0.5 ≈ 1.4 ms per instance at a
                // 128-frame block: four instances ≈ 220 % of the period, the load of #532.
                int workParam = Enumerable.Range(0, engine.PluginParamCount(track, -1))
                    .FirstOrDefault(j => engine.PluginParamName(track, -1, j) == "Work");
                if (engine.PluginParamName(track, -1, workParam) == "Work")
                    engine.PluginParamSet(track, -1, workParam, 0.5f);
            }
            // Commercial instruments may defer model/license initialization to
            // their message thread. Give it a real pump before sending the notes.
            var initialize = Stopwatch.StartNew();
            while (initialize.ElapsedMilliseconds < 1000)
            { engine.RenderDeviceBlock(buffer, frames); Pump(); Thread.Sleep(2); }
            engine.Seek(0); engine.Play();
            const int warmup = 120, count = 200;
            double period = frames * 1000.0 / rate;
            var samples = new double[count]; double sum = 0;
            for (int i = 0; i < warmup; i++) { engine.RenderDeviceBlock(buffer, frames); Pump(); }
            double workerCpu = engine.RenderThreadsCpuSeconds;
            var wall = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                long start = Stopwatch.GetTimestamp();
                engine.RenderDeviceBlock(buffer, frames);
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                foreach (float value in buffer) sum += value * (double)value;
                Pump();
                double due = (i + 1) * period;
                double ahead = due - wall.Elapsed.TotalMilliseconds;
                if (ahead > 1) Thread.Sleep(TimeSpan.FromMilliseconds(ahead - 0.5));
                while (wall.Elapsed.TotalMilliseconds < due) Thread.SpinWait(25);
            }
            workerCpu = (engine.RenderThreadsCpuSeconds - workerCpu) * 1000;
            int late = samples.Count(t => t > period); double mean = samples.Average();
            Array.Sort(samples);
            Console.WriteLine("PLUGIN_PROFILE " + JsonSerializer.Serialize(new { name, instances, helpers, rate, frames,
                medianMs = samples[count / 2], p95Ms = samples[(int)(count * 0.95)], meanMs = mean,
                deadlinePercent = mean / period * 100, late, count, workerCpuMs = workerCpu,
                rms = Math.Sqrt(sum / (count * frames * 2)) }));
        }
    }

    public static void RunProject(string bundle, int frames = 128, int rate = 48000, string? onlyMode = null)
    {
        // Seed a scratch catalog from installed identifiers, never scan/save into
        // the user's catalog or write a project. Apply reads state blobs in place.
        string scratch = Directory.CreateTempSubdirectory("nota-project-profile-").FullName;
        File.Copy(Path.Combine(NotaPaths.DataDir, "plugins.xml"), Path.Combine(scratch, "plugins.xml"));
        NotaEngine.SetDataDir(scratch);
        foreach (string mode in new[] { "one-swam", "four-swam", "complete-project", "four-swam-all-workers" })
        {
            if (onlyMode is not null && mode != onlyMode) continue;
            bool allWorkers = mode == "four-swam-all-workers";
            var doc = ProjectService.Load(bundle);
            var swam = doc.Tracks.Where(t => t.Instrument?.PluginId?.Contains("SWAM") == true).ToArray();
            if (swam.Length == 0) throw new InvalidOperationException("Project contains no SWAM instrument");
            double startBeat = swam.SelectMany(t => t.MidiClips)
                .Where(c => c.Active && c.Notes.Length > 0)
                .Select(c => c.StartBeat + c.Notes.Min(n => n.Start)).DefaultIfEmpty(0).Min();
            if (mode != "complete-project")
                foreach (var track in doc.Tracks)
                    track.Mute = mode == "one-swam" ? track != swam[0] : !swam.Contains(track);
            Console.WriteLine($"PROJECT_PROFILE loading {mode}; {rate} Hz / {frames} frames; start beat {startBeat:0.###}");
            using var engine = new NotaEngine();
            var buffer = new float[frames * 2];
            engine.RenderOffline(buffer, frames, rate);
            var warnings = ProjectService.Apply(doc, engine, bundle);
            if (warnings.Count > 0) throw new InvalidOperationException("Project profile restore warnings: " + string.Join("; ", warnings));
            engine.SetMetronome(false); engine.SetLoop(false, 0, 4);
            var initialize = Stopwatch.StartNew();
            while (initialize.ElapsedMilliseconds < 1200)
            { engine.RenderDeviceBlock(buffer, frames); Pump(); Thread.Sleep(2); }
            engine.Seek(startBeat); engine.Play();
            for (int i = 0; i < 120; i++)
            { if (allWorkers) engine.RenderOffline(buffer, frames); else engine.RenderDeviceBlock(buffer, frames); Pump(); }
            const int count = 400;
            var samples = new double[count]; double squares = 0;
            double workerCpu = engine.RenderThreadsCpuSeconds;
            double period = frames * 1000.0 / rate;
            var wall = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                long before = Stopwatch.GetTimestamp();
                if (allWorkers) engine.RenderOffline(buffer, frames); else engine.RenderDeviceBlock(buffer, frames);
                samples[i] = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
                foreach (float value in buffer) squares += value * (double)value;
                Pump();
                double due = (i + 1) * period;
                double ahead = due - wall.Elapsed.TotalMilliseconds;
                if (ahead > 1) Thread.Sleep(TimeSpan.FromMilliseconds(ahead - 0.5));
                while (wall.Elapsed.TotalMilliseconds < due) Thread.SpinWait(25);
            }
            workerCpu = (engine.RenderThreadsCpuSeconds - workerCpu) * 1000;
            double mean = samples.Average(); int late = samples.Count(t => t > period);
            Array.Sort(samples);
            Console.WriteLine("SAVED_PROJECT_PROFILE " + JsonSerializer.Serialize(new { mode, rate, frames,
                helpers = engine.RenderThreads, startBeat, medianMs = samples[count / 2], p95Ms = samples[(int)(count * 0.95)],
                meanMs = mean, deadlinePercent = mean / period * 100, late, count, workerCpuMs = workerCpu,
                rms = Math.Sqrt(squares / (count * frames * 2)) }));
        }
    }
}
