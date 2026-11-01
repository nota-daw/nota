// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using Nota.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Loading the native DLL also runs JUCE's Windows timer-period initializer.
        if (args.Length > 0) NativeLibrary.Load(Path.GetFullPath(args[0]));
        // Initialize the Windows dispatcher without creating an application window.
        AppBuilder.Configure<Avalonia.Application>().UseWin32().UseSkia().UseHarfBuzz().SetupWithoutStarting();
        using var stop = new CancellationTokenSource();
        var clock = Stopwatch.StartNew();
        var ticks = new List<double>();
        using var timer = new AvaloniaPeriodicTimerFactory().Create(TimeSpan.FromMilliseconds(33), () =>
        {
            ticks.Add(clock.Elapsed.TotalMilliseconds);
            if (ticks.Count == 121) stop.Cancel();
        });
        timer.Start();
        Dispatcher.UIThread.MainLoop(stop.Token);
        var intervals = ticks.Zip(ticks.Skip(1), (a, b) => b - a).ToArray();
        var sorted = intervals.Order().ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            platform = Environment.OSVersion.ToString(),
            engineLoaded = args.Length > 0,
            requestedMs = 33,
            samples = intervals.Length,
            meanMs = intervals.Average(),
            minMs = sorted[0],
            p50Ms = sorted[sorted.Length / 2],
            p95Ms = sorted[(int)(sorted.Length * .95)],
            maxMs = sorted[^1],
            updatesPerSecond = 1000 / intervals.Average(),
            intervalsMs = intervals
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
