// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Hosted plug-ins load after they are inserted (#979, specs/113). The render probe VST3 stands in
// for a copy-protection-wrapped plug-in: with NOTA_RENDER_PROBE_LOAD_DELAY_MS its binary blocks
// while it loads. Run in a child process (the delay only happens on the process's first load of
// the binary, and both environment variables must be there before the engine starts), with its
// own catalog and data dir, so nothing reaches the user's library.

using System.Diagnostics;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class PluginLoadTests
{
    private const int LoadDelayMs = 1500, TimeoutMs = 400;

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
            throw new DirectoryNotFoundException("Native build directory unavailable for plug-in load tests.");
        }
    }

    // Each child gets one blocking binary load (the probe blocks only on the process's first).
    public static IEnumerable<(bool Ok, string Label)> RunIsolated()
    {
        foreach (var r in Child("--plugin-load", LoadDelayMs, "isolated plug-in load checks (a binary that blocks while it loads)")) yield return r;
        foreach (var r in Child("--plugin-load-removal", RemovalDelayMs, "isolated plug-in load checks (removing a load that doesn't return)")) yield return r;
    }

    private const int RemovalDelayMs = 3000;

    private static IEnumerable<(bool Ok, string Label)> Child(string mode, int delayMs, string label)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(PluginLoadTests).Assembly.Location);
        start.ArgumentList.Add(mode);
        start.Environment["NOTA_RENDER_PROBE_LOAD_DELAY_MS"] = delayMs.ToString();
        start.Environment["NOTA_PLUGIN_LOAD_TIMEOUT_MS"] = TimeoutMs.ToString();
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            try { child.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            stdout.GetAwaiter().GetResult();
            stderr.GetAwaiter().GetResult();
            yield return (false, label + " (did not finish within 5 minutes, killed)");
            yield break;
        }
        Console.Write(stdout.GetAwaiter().GetResult());
        if (child.ExitCode != 0) Console.Write(stderr.GetAwaiter().GetResult());
        yield return (child.ExitCode == 0, label);
    }

    private static int ScanProbe(string scratch, out string id)
    {
        NotaEngine.SetDataDir(scratch);
        string fixture = Path.Combine(NativeDir, "nota-render-probe_artefacts", "Release", "VST3");
        string worker = Path.Combine(NativeDir, OperatingSystem.IsWindows() ? "nota-scanworker.exe" : "nota-scanworker");
        if (!Directory.Exists(fixture)) throw new DirectoryNotFoundException("Build nota-render-probe_VST3 before plug-in load tests.");
        NotaEngine.ScanPlugins(worker, fixture, true);
        int catalog = Enumerable.Range(0, NotaEngine.PluginCount)
            .First(i => NotaEngine.PluginDescription(i)?.StartsWith("Nota Render Probe |") == true);
        id = NotaEngine.PluginId(catalog) ?? "";
        return catalog;
    }

    // Removing a plug-in whose binary load doesn't come back: the loads behind it end, asking for
    // a restart, instead of waiting for a card that is gone; undo brings it back "couldn't load".
    public static IEnumerable<(bool Ok, string Label)> RunRemoval()
    {
        string scratch = Directory.CreateTempSubdirectory("nota-plugin-load-rm-").FullName;
        try
        {
            int catalog = ScanProbe(scratch, out _);
            using var e = new NotaEngine();
            e.RenderOffline(new float[256], 128, 48000);
            int t = e.AddInstrumentTrack();
            var clock = Stopwatch.StartNew();
            int stuck = e.AddTrackEffectPlugin(t, catalog);   // its binary blocks for RemovalDelayMs
            int behind = e.AddTrackEffectPlugin(t, catalog);
            e.ServicePluginLoads();
            yield return (e.DevicePluginLoadState(t, behind) == PluginLoadState.Waiting
                          && e.DevicePluginLoadReasonCode(t, behind) == PluginLoadReason.WaitingFor
                          && e.DevicePluginLoadReasonArg(t, behind) == "Nota Render Probe",
                "a second load waits for the first, naming it");
            e.RemoveDevice(t, stuck);
            behind = 0;   // the chain closed up
            while (clock.ElapsedMilliseconds < RemovalDelayMs - 500 && e.DevicePluginLoadState(t, behind) == PluginLoadState.Waiting)
            {
                e.ServicePluginLoads();
                Thread.Sleep(20);
            }
            yield return (e.DevicePluginLoadState(t, behind) == PluginLoadState.Failed
                          && e.DevicePluginLoadReasonCode(t, behind) == PluginLoadReason.RestartRequired
                          && clock.ElapsedMilliseconds < RemovalDelayMs,
                $"once the removed load is past its deadline the one behind it ends, asking for a restart ({clock.ElapsedMilliseconds} ms)");
            int later = e.AddTrackEffectPlugin(t, catalog);
            yield return (e.DevicePluginLoadState(t, later) == PluginLoadState.Failed
                          && e.DevicePluginLoadReasonCode(t, later) == PluginLoadReason.RestartRequired,
                "…as does a new insert while that load is still out");
            e.Undo();   // the later insert
            e.Undo();   // the removal
            yield return (e.TrackDeviceCount(t) == 2 && e.DevicePluginLoadState(t, 0) == PluginLoadState.Failed
                          && e.DevicePluginLoadReasonCode(t, 0) == PluginLoadReason.Removed,
                "undo brings the removed plug-in back as \"couldn't load\", saying it was removed while loading");
            while (clock.ElapsedMilliseconds < RemovalDelayMs + 500) { e.ServicePluginLoads(); Thread.Sleep(20); }
            e.DevicePluginLoadAction(t, 0, PluginLoadAction.TryAgain);
            e.DevicePluginLoadAction(t, 1, PluginLoadAction.TryAgain);
            int left = e.WaitPluginLoads(10000);
            yield return (left == 0 && e.DevicePluginLoadState(t, 0) == PluginLoadState.Loaded
                          && e.DevicePluginLoadState(t, 1) == PluginLoadState.Loaded,
                "once the load comes back, Try again loads both");
        }
        finally { NotaEngine.SetDataDir(null); }
    }

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        string scratch = Directory.CreateTempSubdirectory("nota-plugin-load-").FullName;
        try
        {
            int catalog = ScanProbe(scratch, out string id);
            yield return (id.Length > 0, "render probe scanned into an isolated catalog");

            // ---- the freeze guard (US3), before anything loads the binary -----------------
            // Nota "ended" while the probe's main-thread step ran: the record is still there.
            File.WriteAllText(Path.Combine(scratch, "plugin-loading.txt"), id + "\nNota Render Probe\n");
            string notice = NotaEngine.TakeFrozenPluginNotice();
            yield return (notice == "Nota Render Probe" && NotaEngine.TakeFrozenPluginNotice() == "",
                $"a leftover loading record names the plug-in once (got '{notice}')");
            string froze = Path.Combine(scratch, "plugins-froze-nota.txt");
            yield return (!File.Exists(Path.Combine(scratch, "plugin-loading.txt"))
                          && File.Exists(froze) && File.ReadAllText(froze).Contains(id),
                "…and marks it as having frozen Nota");

            using var e = new NotaEngine();
            e.RenderOffline(new float[256], 128, 48000);
            int t = e.AddInstrumentTrack();
            NotaEngine.PluginAutoloadScope(true);
            int frozen = e.AddTrackEffectPlugin(t, catalog);
            NotaEngine.PluginAutoloadScope(false);
            yield return (frozen >= 0 && e.DevicePluginLoadState(t, frozen) == PluginLoadState.Failed
                          && e.DevicePluginLoadReasonCode(t, frozen) == PluginLoadReason.FrozeNota,
                $"on project open it comes up \"couldn't load\" without loading ({e.DevicePluginLoadReasonCode(t, frozen)})");
            var blob = new byte[] { 7, 0, 255, 1, 42, 9 };
            e.SetPluginState(t, frozen, blob);
            yield return (e.GetPluginState(t, frozen).SequenceEqual(blob) && e.TrackDevicePluginId(t, frozen) == id,
                "a device that couldn't load keeps its identity and saved state byte for byte");

            // ---- a binary that blocks while it loads (US1) --------------------------------
            var sw = Stopwatch.StartNew();
            int fx = e.AddTrackEffectPlugin(t, catalog);
            long insertMs = sw.ElapsedMilliseconds;
            var first = e.DevicePluginLoadState(t, fx);
            yield return (fx >= 0 && insertMs < LoadDelayMs / 2 && first is PluginLoadState.Waiting or PluginLoadState.Loading,
                $"inserting returns at once, the device still loading ({insertMs} ms, {first})");
            yield return (e.PluginParamCount(t, fx) == 0, "…with no parameters until it loads");

            var buf = new float[256];
            e.RenderOffline(buf, 128);   // the engine renders through the loading device
            bool sawNotResponding = false, kept = false;
            long slowestService = 0;
            sw.Restart();
            while (sw.ElapsedMilliseconds < 20000 && e.DevicePluginLoadState(t, fx) != PluginLoadState.Loaded)
            {
                var s = Stopwatch.StartNew();
                e.ServicePluginLoads();
                slowestService = Math.Max(slowestService, s.ElapsedMilliseconds);
                if (e.DevicePluginLoadState(t, fx) == PluginLoadState.NotResponding && !kept)
                {
                    sawNotResponding = true;
                    kept = e.DevicePluginLoadAction(t, fx, PluginLoadAction.KeepWaiting)
                           && e.DevicePluginLoadState(t, fx) == PluginLoadState.Loading;
                }
                Thread.Sleep(20);
            }
            yield return (sawNotResponding && kept, $"past its {TimeoutMs} ms deadline it reads not responding; Keep waiting resumes it");
            yield return (e.DevicePluginLoadState(t, fx) == PluginLoadState.Loaded && e.PluginParamCount(t, fx) > 0,
                $"the load lands after {sw.ElapsedMilliseconds} ms and the plug-in has its parameters");
            // The caller (the UI thread in the app) never waited on the binary.
            yield return (slowestService < LoadDelayMs / 3, $"servicing loads never blocks on the binary (slowest {slowestService} ms)");

            // ---- a lane made while its plug-in loads resolves when it lands (US2) ---------
            int gain = Enumerable.Range(0, e.PluginParamCount(t, fx)).First(i => e.PluginParamName(t, fx, i) == "Gain");
            string gainId = e.PluginParamId(t, fx, gain);
            int fx2 = e.AddTrackEffectPlugin(t, catalog);
            int lane = e.AddPluginAutomationLane(t, fx2, gainId);
            int before = e.AutomationLaneInfo(t, lane).ParamIndex;
            int left = e.WaitPluginLoads(10000);
            int after = e.AutomationLaneInfo(t, lane).ParamIndex;
            yield return (left == 0 && before == -1 && after == gain,
                $"a plug-in-param lane made while loading resolves once it loads ({before} → {after})");

            // ---- a CV link onto a hosted instrument that is still loading (project open) ---
            int inst = e.AddPluginInstrumentTrack(catalog);
            int mod = e.ModulatorAdd(inst, 0);
            int link = e.CvLinkAddToTarget(inst, mod, 1, -1, -1, gain);   // 1 = the instrument
            bool keptWhileLoading = link >= 0 && e.CvLinkCount(inst) == 1;
            e.WaitPluginLoads(10000);
            yield return (keptWhileLoading && e.CvLinkCount(inst) == 1 && e.DevicePluginLoadState(inst, -1) == PluginLoadState.Loaded,
                $"a CV link onto a loading instrument's parameter is kept until it loads (link {link})");

            // ---- give up, then try again --------------------------------------------------
            int fx3 = e.AddTrackEffectPlugin(t, catalog);
            bool gaveUp = e.DevicePluginLoadAction(t, fx3, PluginLoadAction.GiveUp);
            yield return (gaveUp && e.DevicePluginLoadState(t, fx3) == PluginLoadState.Failed
                          && e.DevicePluginLoadReasonCode(t, fx3) == PluginLoadReason.GaveUp,
                $"Give up leaves a \"couldn't load\" device ({e.DevicePluginLoadReasonCode(t, fx3)})");
            Thread.Sleep(200);   // the loader thread comes back from the given-up load
            e.ServicePluginLoads();
            yield return (e.DevicePluginLoadState(t, fx3) == PluginLoadState.Failed,
                "…and a late result of a given-up load is dropped");
            e.DevicePluginLoadAction(t, fx3, PluginLoadAction.TryAgain);
            e.WaitPluginLoads(10000);
            yield return (e.DevicePluginLoadState(t, fx3) == PluginLoadState.Loaded, "Try again loads it");

            // Try again on the device held back as having frozen Nota loads it, and unmarks it.
            e.DevicePluginLoadAction(t, frozen, PluginLoadAction.TryAgain);
            e.WaitPluginLoads(10000);
            yield return (e.DevicePluginLoadState(t, frozen) == PluginLoadState.Loaded && !File.ReadAllText(froze).Contains(id),
                "a plug-in that froze Nota loads when tried again, and is no longer held back");

            // ---- copies load the same way, with the source's state ------------------------
            e.PluginParamSet(t, fx, gain, 0.25f);
            bool copied = e.CopyDevice(t, fx);
            int at = copied ? e.PasteDevice(t, e.TrackDeviceCount(t)) : -1;
            e.WaitPluginLoads(10000);
            yield return (at >= 0 && Math.Abs(e.PluginParamGet(t, at, gain) - 0.25f) < 1e-3,
                "a copied plug-in loads with its source's state");
        }
        finally { NotaEngine.SetDataDir(null); }
    }
}
