// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — engine info + audition. (Audio/MIDI device enumeration lives in separate services
// and is a Phase-2 addition.)

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class SettingsTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh,
    IProcessCpu? processCpu = null)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record EngineInfo(double SampleRate, int Xruns, int SceneCount);
    public sealed record CpuLoadInfo(float DspLoad, double? ProcessCpu, int LogicalCpus);

    [McpServerTool(Name = "get_engine_info"), Description("Read engine status: sample rate, audio dropout (xrun) count, session scene count.")]
    public Task<EngineInfo> GetEngineInfo() => Read(() => new EngineInfo(E.SampleRate, E.XrunCount, E.SceneCount));

    [McpServerTool(Name = "get_cpu_load"), Description("Read CPU load. DspLoad: audio-callback time / buffer length (0..1; dropouts near 1). " +
        "ProcessCpu: the whole Nota process's CPU over the last few seconds, in percent of ONE core on every OS " +
        "(can exceed 100; divide by LogicalCpus for percent of the whole machine, as Windows Task Manager shows); null while still measuring.")]
    public Task<CpuLoadInfo> GetCpuLoad() => Read(() => new CpuLoadInfo(
        Math.Clamp(E.CpuLoad, 0f, 1f), processCpu?.CorePercent(), processCpu?.LogicalCpus ?? Environment.ProcessorCount));

    [McpServerTool(Name = "preview_file"), Description("Audition an audio file (WAV/FLAC/MP3) through the master, without adding it to the project.")]
    public Task PreviewFile(string path) => Mutate(() => E.PreviewFile(path));

    [McpServerTool(Name = "stop_preview"), Description("Stop the file audition started by preview_file.")]
    public Task StopPreview() => Mutate(() => E.StopPreview());
}
