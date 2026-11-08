// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — mixer read/routing detail: return-bus sends, live level meters (per track + master),
// and a track's record-input source and hardware input (any input of the device, specs/086). Adding return buses and setting send levels live in TrackTools
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

    [McpServerTool(Name = "set_input_channels"), Description("Set an audio track's hardware input among the first two inputs: 0 = both as a stereo pair (1/2), 1 = input 1 alone, 2 = input 2 alone (a mono source, placed in the middle). For any input of a multi-input device use set_track_input.")]
    public Task SetInputChannels(int trackId, int channels) => Mutate(() => E.SetTrackInputChannels(trackId, (InputChannels)channels));

    [McpServerTool(Name = "get_input_channels"), Description("Read an audio track's hardware input as 0 stereo pair (1/2), 1 input 1, 2 input 2 — or -1 for any other input (read it with get_track_input).")]
    public Task<int> GetInputChannels(int trackId) => Read(() => E.GetTrackInput(trackId).AsChannels is { } c ? (int)c : -1);

    public sealed record AudioInputInfo(int Index, string Name);
    public sealed record TrackInputInfo(int First, int Width, bool Available);

    [McpServerTool(Name = "list_audio_inputs"), Description("List the capture device's inputs (1-based index and the name the device gives it, \"\" if none). Opens the selected input device briefly if it isn't open yet.")]
    public Task<AudioInputInfo[]> ListAudioInputs() => Read(() =>
    {
        int n = E.AudioInputChannelCount();
        if (n <= 0) n = E.ProbeAudioInput();
        var list = new AudioInputInfo[Math.Max(n, 0)];
        for (int i = 0; i < list.Length; i++) list[i] = new(i + 1, E.AudioInputChannelName(i));
        return list;
    });

    [McpServerTool(Name = "set_track_input"), Description("Set an audio track's hardware input: input `first` (1-based) alone with width 1 — a mono source, recorded and heard in the middle — or the stereo pair first/first+1 with width 2 (first must be odd: 1, 3, 5…). The track's record input must be the hardware input (set_record_input 0) for it to take effect.")]
    public Task SetTrackInput(int trackId, int first, int width) => Mutate(() =>
    {
        if (!E.SetTrackInput(trackId, new InputPick(first - 1, width)))
            throw new ArgumentException($"input {first} width {width} is not a valid pick for track {trackId}: width 1 or 2, a stereo pair starts on an odd input, inputs 1..{InputPick.MaxChannels}, audio tracks only");
    });

    [McpServerTool(Name = "get_track_input"), Description("Read an audio track's hardware input: first input (1-based), width (1 mono, 2 stereo pair) and whether the current input device has it.")]
    public Task<TrackInputInfo> GetTrackInput(int trackId) => Read(() =>
    {
        var p = E.GetTrackInput(trackId);
        return new TrackInputInfo(p.First + 1, p.Width, E.TrackInputAvailable(trackId));
    });

    [McpServerTool(Name = "set_track_monitor"), Description("Turn live input monitoring on/off for an audio track: it plays its record-input source (hardware input, or another track's / return's output) through its devices and fader in real time, in place of its clips.")]
    public Task SetTrackMonitor(int trackId, bool on) => Mutate(() => E.SetTrackMonitor(trackId, on));

    [McpServerTool(Name = "get_track_monitor"), Description("Read whether an audio track is monitoring its record input live.")]
    public Task<bool> GetTrackMonitor(int trackId) => Read(() => E.GetTrackMonitor(trackId));
}
