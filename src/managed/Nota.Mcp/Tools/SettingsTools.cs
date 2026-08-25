// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// MCP tools — engine info + audition. (Audio/MIDI device enumeration lives in separate services
// and is a Phase-2 addition.)

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class SettingsTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record EngineInfo(double SampleRate, int Xruns, int SceneCount);
    public sealed record CpuLoadInfo(float DspLoad, double ProcessCpu);

    [McpServerTool(Name = "get_engine_info"), Description("Read engine status: sample rate, audio dropout (xrun) count, session scene count.")]
    public Task<EngineInfo> GetEngineInfo() => Read(() => new EngineInfo(E.SampleRate, E.XrunCount, E.SceneCount));

    [McpServerTool(Name = "get_cpu_load"), Description("Read CPU load: DSP load (0..1, engine-side audio thread) and this process's total CPU usage (percent, from the OS).")]
    public Task<CpuLoadInfo> GetCpuLoad() => Read(() =>
    {
        float dsp = Math.Clamp(E.CpuLoad, 0f, 1f);
        double proc = 0;
        try
        {
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            p.Refresh();
            proc = p.TotalProcessorTime.TotalSeconds / (DateTime.UtcNow - p.StartTime.ToUniversalTime()).TotalSeconds * 100.0;
        }
        catch { }
        return new CpuLoadInfo(dsp, proc);
    });

    [McpServerTool(Name = "preview_file"), Description("Audition an audio file (WAV/FLAC/MP3) through the master, without adding it to the project.")]
    public Task PreviewFile(string path) => Mutate(() => E.PreviewFile(path));

    [McpServerTool(Name = "stop_preview"), Description("Stop the file audition started by preview_file.")]
    public Task StopPreview() => Mutate(() => E.StopPreview());
}
