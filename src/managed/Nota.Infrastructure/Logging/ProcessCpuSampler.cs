// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Process CPU over a recent window (issue #54). Uses Environment.CpuUsage: GetProcessTimes on
// Windows, getrusage(RUSAGE_SELF) on Linux and macOS — no Process object, no allocation per
// read, and correct units on Apple Silicon. The figure is Δ(process CPU time) ÷ Δ(wall time),
// i.e. percent of one core, smoothed with a time-based moving average.

using System.Diagnostics;
using Nota.Application;

namespace Nota.Infrastructure;

public sealed class ProcessCpuSampler : IProcessCpu
{
    /// <summary>Reads closer together than this return the cached value.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(500);
    /// <summary>Smoothing time constant: the readout settles over a few seconds, like a monitor.</summary>
    public static readonly TimeSpan Smoothing = TimeSpan.FromSeconds(2);

    private readonly Func<TimeSpan> _cpuTime;
    private readonly Func<TimeSpan> _wallTime;
    private TimeSpan _lastCpu, _lastWall;
    private double? _value;
    private bool _failed;

    public ProcessCpuSampler() : this(() => Environment.CpuUsage.TotalTime, StopwatchClock()) { }

    /// <summary>Clocks injected for tests: process CPU time and a monotonic wall clock.</summary>
    public ProcessCpuSampler(Func<TimeSpan> cpuTime, Func<TimeSpan> wallTime)
    {
        _cpuTime = cpuTime;
        _wallTime = wallTime;
        try { _lastCpu = _cpuTime(); }
        catch { _failed = true; }
        _lastWall = _wallTime();
    }

    public int LogicalCpus => Environment.ProcessorCount;

    public double? CorePercent()
    {
        if (_failed) return null;
        var wall = _wallTime();
        var dWall = wall - _lastWall;
        if (dWall < Step) return _value;
        TimeSpan cpu;
        try { cpu = _cpuTime(); }
        catch { _failed = true; return _value = null; }

        // CPU time never runs backwards; clamp in case an OS counter hiccups.
        double instant = Math.Max(0, (cpu - _lastCpu).TotalSeconds) / dWall.TotalSeconds * 100.0;
        double alpha = 1.0 - Math.Exp(-dWall.TotalSeconds / Smoothing.TotalSeconds);
        _value = _value is { } prev ? prev + (instant - prev) * alpha : instant;
        _lastCpu = cpu;
        _lastWall = wall;
        return _value;
    }

    private static Func<TimeSpan> StopwatchClock()
    {
        var sw = Stopwatch.StartNew();
        return () => sw.Elapsed;
    }
}
