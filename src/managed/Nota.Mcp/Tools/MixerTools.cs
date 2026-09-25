// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — mixer read/routing detail: return-bus sends, live level meters (per track + master),
// and a track's record-input source. Adding return buses and setting send levels live in TrackTools
// (add_return_track / set_send); this group adds the reads + the record-input source.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class MixerTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record MeterLevels(float PeakL, float PeakR, float RmsL, float RmsR);
    public sealed record ReturnInfo(int Id, string Name, int Bus);

    [McpServerTool(Name = "get_send"), Description("Read a track's post-fader send level (0..1) to a return bus (0-based).")]
    public Task<float> GetSend(int trackId, int bus) => Read(() => E.GetTrackSend(trackId, bus));

    [McpServerTool(Name = "list_returns"), Description("List the return (aux/FX) buses: track id, name, and bus index for use with set_send/get_send.")]
    public Task<ReturnInfo[]> ListReturns() => Read(() =>
    {
        int n = E.TrackCount;
        var list = new List<ReturnInfo>();
        for (int i = 0; i < n; i++)
        {
            if (!E.TryGetTrackInfo(i, out var ti) || ti.Type != 2) continue;
            list.Add(new ReturnInfo(ti.Id, E.GetTrackName(ti.Id), E.TrackReturnIndex(ti.Id)));
        }
        return list.ToArray();
    });

    [McpServerTool(Name = "get_track_meter"), Description("Read a track's live output meter (linear 0..1 peak + RMS, per channel). Snapshot at call time.")]
    public Task<MeterLevels> GetTrackMeter(int trackId) => Read(() =>
        E.TryGetTrackMeter(trackId, out var m) ? new MeterLevels(m.PeakL, m.PeakR, m.RmsL, m.RmsR) : new MeterLevels(0, 0, 0, 0));

    [McpServerTool(Name = "get_master_meter"), Description("Read the master output meter (linear 0..1 peak + RMS, per channel). Snapshot at call time.")]
    public Task<MeterLevels> GetMasterMeter() => Read(() =>
    {
        var m = E.MasterMeter();
        return new MeterLevels(m.PeakL, m.PeakR, m.RmsL, m.RmsR);
    });

    [McpServerTool(Name = "set_record_input"), Description("Set a track's record-input source (engine input-source index; 0 = none/default).")]
    public Task SetRecordInput(int trackId, int source) => Mutate(() => E.SetTrackRecordInput(trackId, source));

    [McpServerTool(Name = "get_record_input"), Description("Read a track's record-input source index.")]
    public Task<int> GetRecordInput(int trackId) => Read(() => E.GetTrackRecordInput(trackId));

    [McpServerTool(Name = "set_track_monitor"), Description("Turn live input monitoring on/off for an audio track: it plays its record-input source (hardware input, or another track's / return's output) through its devices and fader in real time, in place of its clips.")]
    public Task SetTrackMonitor(int trackId, bool on) => Mutate(() => E.SetTrackMonitor(trackId, on));

    [McpServerTool(Name = "get_track_monitor"), Description("Read whether an audio track is monitoring its record input live.")]
    public Task<bool> GetTrackMonitor(int trackId) => Read(() => E.GetTrackMonitor(trackId));
}
