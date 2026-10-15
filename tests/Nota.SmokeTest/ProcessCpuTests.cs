// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Process CPU readout (.specify/bugs/cpu-meter-dsp-only, issue #54): the sampler reports a
// recent window, not a lifetime average; it never goes negative or throws; the display scale
// follows the platform monitor (Windows: of all CPUs, macOS/Linux: of one core); and a real
// one-core busy loop reads about 100 % of one core on whichever OS the smoke test runs on.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --cpu`.

using System.Diagnostics;
using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ProcessCpuTests
{
    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        // --- deterministic: injected clocks ---
        var cpu = TimeSpan.Zero; var wall = TimeSpan.Zero;
        var s = new ProcessCpuSampler(() => cpu, () => wall);
        wall = TimeSpan.FromMilliseconds(200);
        yield return (s.CorePercent() is null, "no reading before the first window has elapsed");

        cpu = TimeSpan.FromSeconds(0.5); wall = TimeSpan.FromSeconds(1);
        double? first = s.CorePercent();
        yield return (first is { } f && Math.Abs(f - 50) < 1e-9, $"0.5 s CPU over 1 s wall reads 50 % of one core (got {first})");

        cpu = TimeSpan.FromSeconds(0.9); wall = TimeSpan.FromSeconds(1.2);
        yield return (s.CorePercent() == first, "reads closer than the step return the cached value");

        // 2 s of CPU over the next 1 s (two busy cores): the readout moves toward 200 but is smoothed.
        cpu = TimeSpan.FromSeconds(2.5); wall = TimeSpan.FromSeconds(2);
        double? second = s.CorePercent();
        yield return (second is > 50 and < 200, $"a jump to 200 % is smoothed, not instant (got {second})");

        // Recent window, not lifetime: after a long idle stretch the reading falls toward 0.
        for (int i = 3; i <= 20; i++) { wall = TimeSpan.FromSeconds(i); s.CorePercent(); }
        double? idle = s.CorePercent();
        yield return (idle is < 1, $"after 18 s idle the reading is near 0, not a lifetime average (got {idle})");

        // An OS counter that steps backwards clamps to 0 instead of going negative.
        cpu = TimeSpan.FromSeconds(1); wall = TimeSpan.FromSeconds(21);
        double? back = s.CorePercent();
        yield return (back is >= 0, $"a backwards CPU counter never reads negative (got {back})");

        // An unavailable OS reading yields null, never an exception.
        var broken = new ProcessCpuSampler(() => throw new PlatformNotSupportedException(), () => wall);
        wall += TimeSpan.FromSeconds(1);
        yield return (broken.CorePercent() is null, "an unavailable OS reading gives null, no exception");

        // --- display scale ---
        yield return (ProcessCpuScale.ToDisplay(400, 8, wholeMachine: true) == 50, "Windows scale: 400 % of one core on 8 CPUs shows 50 %");
        yield return (ProcessCpuScale.ToDisplay(400, 8, wholeMachine: false) == 400, "macOS/Linux scale: 400 % of one core shows 400 %");
        yield return (ProcessCpuScale.ToDisplay(80, 0, wholeMachine: true) == 80, "a zero CPU count can't divide by zero");
        yield return (ProcessCpuScale.WholeMachine == OperatingSystem.IsWindows(), "whole-machine scale only on Windows");

        // --- real clocks: one thread busy for ~1.5 s ≈ 100 % of one core on this OS ---
        // Catches wrong units from the OS counter (e.g. Mach ticks on Apple Silicon would read
        // tens of times too high or low). Loose bounds: runtime threads (GC, JIT) add a little.
        var real = new ProcessCpuSampler();
        var sw = Stopwatch.StartNew();
        double spin = 0;
        while (sw.Elapsed < TimeSpan.FromSeconds(1.5)) spin += Math.Sqrt(spin + 1);
        double? busy = real.CorePercent();
        double ceiling = 100.0 * Environment.ProcessorCount + 50;
        yield return (busy is >= 60 && busy <= ceiling && spin > 0,
            $"a one-core busy loop reads about 100 % of one core on {RuntimeInformationOs()} (got {busy:0.0} %, {Environment.ProcessorCount} CPUs)");
    }

    private static string RuntimeInformationOs() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription.Split(' ')[0]} {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";
}
