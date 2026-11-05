// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Nota.App;
using Nota.Application;
using Nota.Presentation;

// Runs the actual Nota window and engine using a scratch data directory. "legacy"
// disables only its new frame loop, leaving the original service-tick cursor path.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void Run(string[] args)
    {
        string mode = args[0], results = Path.GetFullPath(args[1]);
        Environment.SetEnvironmentVariable("NOTA_DATA_DIR", Path.Combine(Path.GetDirectoryName(results)!, "probe-data", Path.GetFileNameWithoutExtension(results)));
        AppBuilder.Configure<Nota.App.App>().UseWin32().UseSkia().UseHarfBuzz().WithNotaFonts().SetupWithoutStarting();
        var settings = (ISettingsService)App.Services.GetService(typeof(ISettingsService))!;
        settings.Current.ShowWelcomeOnStartup = false;
        settings.Current.CheckForUpdates = false;
        settings.Current.LastSeenVersion = AppInfo.Version;
        var vm = (MainWindowViewModel)App.Services.GetService(typeof(MainWindowViewModel))!;
        int requestedBuffer = args.Length > 2 ? int.Parse(args[2]) : 0;
        string requestedAsioDriver = args.Length > 3 ? args[3] : "";
        if (requestedBuffer > 0) vm.Engine.SetAudioBufferFrames(requestedBuffer);
        if (requestedAsioDriver.Length > 0)
        {
            var driver = vm.Engine.AsioDrivers().FirstOrDefault(d =>
                string.Equals(d.Name, requestedAsioDriver, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Uid, requestedAsioDriver, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(driver.Uid)) throw new InvalidOperationException($"ASIO driver not found: {requestedAsioDriver}");
            vm.Engine.SetAudioSystem(AudioSystem.Asio);
            vm.Engine.SetAsioDriver(driver.Uid);
        }
        if (requestedBuffer > 0 || requestedAsioDriver.Length > 0) vm.ApplyAudioSettings();
        void RequireBackend()
        {
            if (requestedAsioDriver.Length == 0
                || (vm.Engine.ActiveAudioSystem == AudioSystem.Asio && vm.Engine.IsRunning)) return;
            var failure = new
            {
                status = "asio-unavailable", requestedAsioDriver,
                backend = vm.Engine.ActiveAudioSystem.ToString(),
                running = vm.Engine.IsRunning, fallbackReason = vm.Engine.AudioFallbackReason,
                driverId = vm.Engine.AsioDriver
            };
            File.WriteAllText(results, JsonSerializer.Serialize(failure, new JsonSerializerOptions { WriteIndented = true }));
            throw new InvalidOperationException($"Requested ASIO driver did not remain active: {vm.Engine.AudioFallbackReason}");
        }
        RequireBackend();
        var main = new MainWindow { DataContext = vm, WindowState = WindowState.Normal, Width = 1280, Height = 720 };
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        if (mode == "legacy") typeof(MainWindow).GetField("_playheadFramesClosed", Private)!.SetValue(main, true);
        var timeline = main.FindControl<ArrangementView>("Timeline")!;
        var display = typeof(ArrangementView).GetField("_displayPlayheadBeats", Private)!;
        using var stop = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var changedAt = new List<double>();
        var frameAt = new List<double>();
        double last = double.NaN;
        bool started = false;
        bool completed = false;
        main.Show();
        if (mode == "lifecycle") { RunLifecycle(main, vm, timeline, stop, results, RequireBackend, requestedAsioDriver); return; }
        Action<TimeSpan>? sample = null;
        sample = _ =>
        {
            RequireBackend();
            double now = clock.Elapsed.TotalMilliseconds;
            if (!started && now > 500)
            {
                // Opened applied the default template. Remove its tracks to test an empty project.
                while (vm.Engine.TrackCount > 0)
                {
                    vm.Engine.TryGetTrackInfo(0, out var info);
                    vm.Engine.RemoveTrack(info.Id);
                }
                timeline.Refresh();
                vm.Engine.SetCountIn(0); vm.Engine.SetMetronome(false); vm.Engine.Seek(0); vm.Engine.Play();
                started = true;
            }
            if (now >= 1500 && now < 6500)
            {
                frameAt.Add(now);
                double beat = (double)display.GetValue(timeline)!;
                if (beat != last) { changedAt.Add(now); last = beat; }
            }
            if (now >= 6500)
            {
                var gaps = changedAt.Zip(changedAt.Skip(1), (a, b) => b - a).Order().ToArray();
                var result = new
                {
                    mode, platform = Environment.OSVersion.ToString(), backend = vm.Engine.ActiveAudioSystem.ToString(),
                    requestedBuffer, requestedAsioDriver, driverId = vm.Engine.AsioDriver,
                    fallbackReason = vm.Engine.AudioFallbackReason,
                    sampleRate = vm.Engine.SampleRate, bufferFrames = vm.Engine.NegotiatedBufferFrames,
                    observedFramesPerSecond = (frameAt.Count - 1) * 1000 / (frameAt[^1] - frameAt[0]),
                    cursorUpdatesPerSecond = 1000 / gaps.Average(),
                    medianCursorGapMs = gaps[gaps.Length / 2], p95CursorGapMs = gaps[(int)(gaps.Length * .95)],
                    observedFrames = frameAt.Count, cursorUpdates = changedAt.Count,
                    dropouts = vm.Engine.XrunCount, cursorChangeTimesMs = changedAt
                };
                File.WriteAllText(results, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                completed = true;
                typeof(MainWindow).GetField("_closeConfirmed", Private)!.SetValue(main, true);
                main.Close();
                stop.Cancel();
                return;
            }
            main.RequestAnimationFrame(sample!);
        };
        main.RequestAnimationFrame(sample);
        // A separate watchdog prevents an occluded window from hanging the diagnostic.
        using var watchdog = new Timer(_ => Dispatcher.UIThread.Post(() => stop.Cancel()), null, 20000, Timeout.Infinite);
        Dispatcher.UIThread.MainLoop(stop.Token);
        if (!completed) throw new InvalidOperationException("No visible frame sample was collected before the watchdog expired.");
    }

    private static void RunLifecycle(MainWindow main, MainWindowViewModel vm, ArrangementView timeline,
        CancellationTokenSource stop, string results, Action requireBackend, string requestedAsioDriver)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        bool Active() => typeof(MainWindow).GetField("_playheadFrameOwner", Private)!.GetValue(main) is not null;
        double Display() => (double)typeof(ArrangementView).GetField("_displayPlayheadBeats", Private)!.GetValue(timeline)!;
        var checks = new List<string>();
        void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks.Add(label);
        }
        int step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            requireBackend();
            switch (step++)
            {
                case 0: vm.Engine.SetCountIn(0); vm.Engine.Play(); break;
                case 1:
                    Require(Active() && Display() > 0, "play starts frame callbacks");
                    vm.SuspendEnginePolling = true;
                    break;
                case 2:
                    Require(!Active(), "export suspension stops frame callbacks");
                    vm.Engine.Stop();
                    vm.Engine.RenderOffline(new float[1024], 512);
                    vm.Engine.Start();
                    vm.SuspendEnginePolling = false;
                    vm.Engine.Play();
                    break;
                case 3:
                    Require(Active(), "device restart and export resume restart frame callbacks");
                    main.WindowState = WindowState.Minimized;
                    break;
                case 4:
                    Require(!Active(), "minimization stops frame callbacks");
                    main.WindowState = WindowState.Normal;
                    break;
                case 5:
                    Require(Active(), "restoring the window restarts frame callbacks");
                    vm.Engine.Seek(8);
                    break;
                case 6:
                    Require(Display() >= 8 && Display() < 10, "seek immediately replaces the display block");
                    vm.Engine.SetLoop(true, 8, 8.25);
                    break;
                case 7:
                    Require(Display() >= 8 && Display() <= 8.25, "repeated short loops keep the display inside the loop");
                    vm.Engine.SetLoop(false, 0, 0); vm.Engine.SetBpm(90); vm.Engine.Seek(12);
                    break;
                case 8:
                    Require(vm.Engine.TryGetTransportSnapshot(out var s) && s.Bpm == 90 && Display() >= 12 && Display() < 14,
                        "tempo change and forward seek reconcile together");
                    vm.Engine.StopTransport();
                    break;
                case 9:
                    Require(!Active() && Math.Abs(Display() - 12) < 1e-8, "stop returns to the launch anchor and ends animation");
                    vm.Engine.Play();
                    break;
                default:
                    Require(Active(), "play restarts after stop");
                    var device = new { requestedAsioDriver, driverId = vm.Engine.AsioDriver,
                        backend = vm.Engine.ActiveAudioSystem.ToString(), sampleRate = vm.Engine.SampleRate,
                        bufferFrames = vm.Engine.NegotiatedBufferFrames, fallbackReason = vm.Engine.AudioFallbackReason,
                        dropouts = vm.Engine.XrunCount };
                    int queuedGeneration = (int)typeof(MainWindow).GetField("_playheadFrameGeneration", Private)!.GetValue(main)!;
                    typeof(MainWindow).GetField("_closeConfirmed", Private)!.SetValue(main, true);
                    main.Close();
                    Require(!Active(), "closing detaches the frame owner before disposing the engine");
                    typeof(MainWindow).GetMethod("OnPlayheadFrame", Private)!.Invoke(main, new object[] { queuedGeneration });
                    checks.Add("a late frame callback after close does not access the disposed engine");
                    File.WriteAllText(results, JsonSerializer.Serialize(new { device, checks }, new JsonSerializerOptions { WriteIndented = true }));
                    timer.Stop(); stop.Cancel();
                    break;
            }
        };
        timer.Start();
        Dispatcher.UIThread.MainLoop(stop.Token);
    }
}
