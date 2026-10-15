// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>CPU used by the whole Nota process (all threads: audio, UI, rendering, plugins) over
/// a recent window — what the OS system monitor shows, as opposed to the engine's DSP load
/// (audio-callback time ÷ buffer length). Implemented in Infrastructure; read on the UI thread.</summary>
public interface IProcessCpu
{
    /// <summary>Recent process CPU in percent of ONE core (can exceed 100 on multi-core machines),
    /// smoothed over a few seconds. Null until the first window has elapsed, or if the OS
    /// reading is unavailable. Cheap: calls closer together than the sampling step return the
    /// cached value, so several readers (transport, MCP) can share one instance.</summary>
    double? CorePercent();

    /// <summary>Logical CPUs available to the process (honours affinity / cgroup limits).</summary>
    int LogicalCpus { get; }
}

/// <summary>Converts a per-core CPU figure into the scale the platform's own monitor uses, so
/// Nota's readout lines up with it: Windows Task Manager shows percent of the whole machine;
/// macOS Activity Monitor, <c>top</c>/<c>htop</c> and GNOME System Monitor show percent of one core.</summary>
public static class ProcessCpuScale
{
    /// <summary>True where the platform monitor shows percent of all CPUs (Windows).</summary>
    public static bool WholeMachine => OperatingSystem.IsWindows();

    public static double ToDisplay(double corePercent, int logicalCpus, bool wholeMachine) =>
        wholeMachine ? corePercent / Math.Max(1, logicalCpus) : corePercent;

    /// <summary>What a displayed figure is a percentage of, for tooltips.</summary>
    public static string Unit(bool wholeMachine) => wholeMachine ? "of all CPUs" : "of one core";
}
