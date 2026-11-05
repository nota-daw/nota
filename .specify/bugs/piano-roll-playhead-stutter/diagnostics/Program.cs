// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Rendering.Composition;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nota.App;
using Nota.Application;
using Nota.Presentation;

// Runs the actual App/clip editor with scratch settings and generated MIDI content.
// Draw-list replacements count re-recordings of static content, not presented frames.
internal static class Program
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object? Field(object o, string n) => o.GetType().GetField(n, Hidden)!.GetValue(o);
    static object? Call(object o, string n, params object[] a) => o.GetType().GetMethods(Hidden)
        .Single(m => m.Name == n && m.GetParameters().Length == a.Length).Invoke(o, a);
    static object? DrawList(Control c)
    {
        var v = ElementComposition.GetElementVisual(c);
        return v?.GetType().GetProperty("DrawList", Hidden)?.GetValue(v);
    }
    [STAThread]
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Run(string[] args)
    {
        string mode = args[0], output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Environment.SetEnvironmentVariable("NOTA_DATA_DIR", Path.Combine(Path.GetDirectoryName(output)!, "probe-data", Path.GetFileNameWithoutExtension(output)));
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppBuilder.Configure<Nota.App.App>().UseWin32().UseSkia().UseHarfBuzz().WithNotaFonts().SetupWithLifetime(lifetime);
        var settings = (ISettingsService)App.Services.GetService(typeof(ISettingsService))!;
        settings.Current.ShowWelcomeOnStartup = false; settings.Current.CheckForUpdates = false;
        settings.Current.LastSeenVersion = AppInfo.Version;
        var vm = (MainWindowViewModel)App.Services.GetService(typeof(MainWindowViewModel))!;
        string requestedAsio = args.Length > 2 ? args[2] : "";
        if (requestedAsio.Length > 0)
        {
            var driver = vm.Engine.AsioDrivers().Single(d => d.Name == requestedAsio);
            vm.Engine.SetAudioSystem(AudioSystem.Asio);
            vm.Engine.SetAsioDriver(driver.Uid);
            vm.ApplyAudioSettings();
            if (!vm.Engine.IsRunning || vm.Engine.ActiveAudioSystem != AudioSystem.Asio)
                throw new InvalidOperationException("Requested ASIO device is unavailable: " + vm.Engine.AudioFallbackReason);
        }
        using var stop = new CancellationTokenSource();
        Exception? error = null; bool completed = false;
        var checks = new List<string>();
        void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); checks.Add(text); }
        void Fail(Exception e) { error = e; stop.Cancel(); }
        DispatcherTimer.RunOnce(() =>
        {
            try
            {
                var main = (MainWindow)lifetime.MainWindow!;
                main.WindowState = WindowState.Normal; main.Width = 1280; main.Height = 900;
                while (vm.Engine.TrackCount > 0) { vm.Engine.TryGetTrackInfo(0, out var info); vm.Engine.RemoveTrack(info.Id); }
                int id = vm.Engine.AddInstrumentTrack(); int ci = vm.Engine.AddMidiClip(id, 4, 64);
                var notes = Enumerable.Range(0, 128).Select(i => new NotaNote(48 + i % 24, i * .5, .25, .6f)).ToArray();
                vm.Engine.SetClipNotes(id, ci, notes);
                var timeline = main.FindControl<ArrangementView>("Timeline")!;
                timeline.Refresh(); timeline.SelectTrackFromOutside(id);
                Call(main, "BuildClipEditor", id, ci); Call(main, "ShowClipPanel");
                var roll = (PianoRollView)Field(main, "_editorRoll")!;
                var grid = (Control)Field(roll, "_gridControl")!;
                var ruler = (Control)Field(roll, "_ruler")!;
                // The visible probe must not accept real wheel/hover/edit input during the
                // cursor-only recording: those interactions legitimately repaint the grid.
                main.IsEnabled = false;
                vm.Engine.SetCountIn(0); vm.Engine.SetMetronome(false); vm.Engine.Seek(4); vm.Engine.Play();
                var clock = Stopwatch.StartNew();
                var cursorTimes = new List<double>(); var arrangementTimes = new List<double>();
                int frameCount = 0, gridDraws = 0, rulerDraws = 0;
                object? oldGrid = null, oldRuler = null;
                double previous = double.NaN, previousArrangement = double.NaN;
                int stage = 0; double stageAt = 0; bool finishedFrames = false;
                var xrunStart = vm.Engine.XrunCount;
                Window frameOwner = main;
                Action<TimeSpan>? sample = null;
                sample = _ =>
                {
                    try
                    {
                        double now = clock.Elapsed.TotalMilliseconds;
                        double beat = (double)Field(roll, "_playheadBeat")!;
                        if (now >= 1000 && now < 6000)
                        {
                            frameCount++;
                            if (beat != previous) { cursorTimes.Add(now); previous = beat; }
                            double ab = (double)Field(timeline, "_displayPlayheadBeats")!;
                            if (ab != previousArrangement) { arrangementTimes.Add(now); previousArrangement = ab; }
                            var gd = DrawList(grid); var rd = DrawList(ruler);
                            if (gd is null || rd is null) throw new InvalidOperationException("Cannot inspect composition draw lists");
                            if (oldGrid is not null && !ReferenceEquals(oldGrid, gd))
                            {
                                gridDraws++;
                                Console.Error.WriteLine($"grid redraw at {now:F1}: bounds={grid.Bounds}, ppb={Field(roll, "_pixelsPerBeat")}, scroll={Field(roll, "_scrollBeats")}, hover={Field(grid, "_hoverNote")}");
                            }
                            if (oldRuler is not null && !ReferenceEquals(oldRuler, rd)) rulerDraws++;
                            oldGrid = gd; oldRuler = rd;
                        }
                        if (now >= 6000 && !finishedFrames)
                        {
                            finishedFrames = true;
                            main.IsEnabled = true;
                            Console.Error.WriteLine(JsonSerializer.Serialize(new { frameCount, cursorUpdates = cursorTimes.Count, gridDraws, rulerDraws }));
                            if (mode != "before")
                            {
                                Require(cursorTimes.Count > frameCount * .9, "piano roll changes on at least 90% of animation callbacks");
                                Require(gridDraws == 0 && rulerDraws == 0, $"cursor animation preserves note-grid and ruler draw lists (grid {gridDraws}, ruler {rulerDraws})");
                            }
                            vm.Engine.StopTransport(); vm.Engine.Seek(9); stageAt = now;
                        }
                        if (finishedFrames && now - stageAt >= 300)
                        {
                            switch (stage++)
                            {
                                case 0:
                                    Require(Math.Abs(beat - 5) < .001, "paused seek maps global beat through clip origin");
                                    if (mode != "before")
                                    {
                                        var gl = (Control)Field(roll, "_gridPlayhead")!;
                                        var rl = (Control)Field(roll, "_rulerPlayhead")!;
                                        Require(gl.Bounds == grid.Bounds && rl.Bounds == ruler.Bounds,
                                            "cursor layers match content geometry");
                                        Require(!gl.IsHitTestVisible && !rl.IsHitTestVisible && gl.ClipToBounds && rl.ClipToBounds,
                                            "cursor layers clip without intercepting editing gestures");
                                        Call(roll, "ZoomAt", 2.0, 5.0);
                                        Call(roll, "SetScrollBeats", 2.0);
                                        Require(Math.Abs((double)Field(roll, "_scrollBeats")! - 2) < .001,
                                            "horizontal zoom and scroll retain cursor transform");
                                        Require((double)Call(roll, "BeatToX", 5.0)! > 0,
                                            "cursor remains in the scrolled viewport");
                                        using var image = new RenderTargetBitmap(new PixelSize((int)main.Bounds.Width, (int)main.Bounds.Height));
                                        image.Render(main); image.Save(Path.ChangeExtension(output, ".png"));
                                    }
                                    roll.SetPlayhead(65, true); Require((double)Field(roll, "_playheadBeat")! < 0, "outside clip hides cursor");
                                    roll.SetPlayhead(3, false); Require((double)Field(roll, "_playheadBeat")! < 0, "playing=false hides cursor");
                                    Call(roll, "SetScrollBeats", 2.0);
                                    vm.Engine.Seek(10); vm.Engine.Play(); break;
                                case 1:
                                    Require(beat > 6, "resuming advances cursor after seek");
                                    vm.Engine.SetLoop(true, 10, 10.25); break;
                                case 2:
                                    Require(beat >= 6 && beat <= 6.25, "song loop stays within clip-local bounds");
                                    Call(main, "OnToggleDetailWindow", null!, new Avalonia.Interactivity.RoutedEventArgs());
                                    main.WindowState = WindowState.Minimized;
                                    frameOwner = (Window)Field(main, "_detailWindow")!; break;
                                case 3:
                                    Require(Field(main, "_playheadFrameOwner") == frameOwner && roll.IsEffectivelyVisible,
                                        "floating editor owns frames when arrangement is minimized");
                                    vm.SuspendEnginePolling = true; break;
                                case 4:
                                    Require(Field(main, "_playheadFrameOwner") is null, "suspension stops frame delivery");
                                    vm.SuspendEnginePolling = false; break;
                                case 5:
                                    Require(Field(main, "_playheadFrameOwner") == frameOwner, "resuming polling restarts floating frames");
                                    vm.Engine.StopTransport(); break;
                                case 6:
                                    Require(Field(main, "_playheadFrameOwner") is null, "pause detaches animation loop");
                                    vm.Engine.SetLoop(false, 0, 4); vm.Engine.Seek(11); break;
                                case 7:
                                    Require(Math.Abs(beat - 7) < .001, "stopped seek updates floating piano roll");
                                    var gaps = cursorTimes.Zip(cursorTimes.Skip(1), (a,b) => b-a).Order().ToArray();
                                    var result = new {
                                        mode, backend = vm.Engine.ActiveAudioSystem.ToString(), vm.Engine.NegotiatedBufferFrames,
                                        requestedAsio,
                                        frameCount, cursorUpdates = cursorTimes.Count, arrangementUpdates = arrangementTimes.Count,
                                        cursorUpdatesPerSecond = 1000 / gaps.Average(), medianGapMs = gaps[gaps.Length/2],
                                        p95GapMs = gaps[(int)(gaps.Length*.95)], gridDraws, rulerDraws,
                                        dropouts = vm.Engine.XrunCount - xrunStart, checks
                                    };
                                    typeof(MainWindow).GetField("_closeConfirmed", Hidden)!.SetValue(main, true);
                                    main.Close();
                                    Require(Field(main, "_playheadFrameOwner") is null, "closing detaches animation loop");
                                    File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                                    completed = true; stop.Cancel(); return;
                            }
                            stageAt = now;
                        }
                        frameOwner.RequestAnimationFrame(sample!);
                    }
                    catch (Exception e) { Fail(e); }
                };
                main.RequestAnimationFrame(sample);
            }
            catch (Exception e) { Fail(e); }
        }, TimeSpan.FromMilliseconds(800));
        using var watchdog = new Timer(_ => Dispatcher.UIThread.Post(() => stop.Cancel()), null, 25000, Timeout.Infinite);
        Dispatcher.UIThread.MainLoop(stop.Token);
        if (error is not null) throw error;
        if (!completed) throw new InvalidOperationException("Timed out before completing probe");
        Console.WriteLine(File.ReadAllText(output));
    }
}
