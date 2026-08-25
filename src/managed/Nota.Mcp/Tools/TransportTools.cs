// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — transport & tempo. Thin adapters over IAudioEngine, marshalled to the UI thread.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class TransportTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record TransportState(bool Playing, double PositionBeats, double Bpm, bool LoopEnabled, double LoopStart, double LoopEnd);

    [McpServerTool(Name = "get_transport"), Description("Current transport state: playing, playhead position (beats), tempo (BPM), and loop region.")]
    public Task<TransportState> GetTransport()
        => Read(() => new TransportState(E.IsPlaying, E.PositionBeats, E.Bpm, E.LoopEnabled, E.LoopStart, E.LoopEnd));

    [McpServerTool(Name = "play"), Description("Start playback from the current playhead position.")]
    public Task Play() => Mutate(() => E.Play());

    [McpServerTool(Name = "stop"), Description("Stop playback (a second stop returns the playhead to the start).")]
    public Task StopTransport() => Mutate(() => E.StopTransport());

    [McpServerTool(Name = "seek"), Description("Move the playhead to a beat position (0 = bar 1).")]
    public Task Seek([Description("Target position in beats")] double beat) => Mutate(() => E.Seek(Math.Max(0, beat)));

    [McpServerTool(Name = "set_tempo"), Description("Set the project tempo in beats per minute.")]
    public Task SetTempo([Description("Tempo in BPM (e.g. 120)")] double bpm) => Mutate(() => E.SetBpm(Math.Clamp(bpm, 20, 300)));

    [McpServerTool(Name = "set_time_signature"), Description("Set the time signature, e.g. numerator=4 denominator=4.")]
    public Task SetTimeSignature([Description("Beats per bar")] int numerator, [Description("Beat unit (1,2,4,8,16)")] int denominator)
        => Mutate(() => E.SetTimeSignature(Math.Clamp(numerator, 1, 32), Math.Clamp(denominator, 1, 32)));

    [McpServerTool(Name = "set_loop"), Description("Enable/disable the loop region and set its start/end in beats.")]
    public Task SetLoop([Description("Loop on/off")] bool enabled, [Description("Loop start beat")] double startBeat, [Description("Loop end beat")] double endBeat)
        => Mutate(() => E.SetLoop(enabled, Math.Max(0, startBeat), Math.Max(startBeat, endBeat)));

    [McpServerTool(Name = "set_metronome"), Description("Turn the metronome click on or off.")]
    public Task SetMetronome([Description("Metronome on/off")] bool enabled) => Mutate(() => E.SetMetronome(enabled));

    [McpServerTool(Name = "set_master_volume"), Description("Set the master output volume (0..1 linear).")]
    public Task SetMasterVolume([Description("Linear gain 0..1")] float volume) => Mutate(() => E.SetMasterVolume(Math.Clamp(volume, 0f, 1f)));
}
