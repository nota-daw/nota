// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// MCP tools — instruments: add an instrument track by kind, read/write its parameters.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class InstrumentTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    // Built-in instrument kinds (matches the engine's kind() ids + browser mapping).
    private static readonly (int Kind, string Name)[] Kinds =
    {
        (0, "Nota Synth"), (1, "Nota Sampler"), (2, "Nota Physical"), (3, "Instrument Rack"),
        (4, "Drum Rack"), (5, "Nota Aurora"), (6, "Nota Volt"), (7, "Nota Bass"),
        (8, "Nota Pendulum"), (9, "Nota Operator"), (10, "Nota Grain"), (11, "Nota Flux"),
        (12, "Nota Rhythm"), (13, "Nota Monolith"),
    };

    public sealed record InstrumentKind(int Kind, string Name);
    public sealed record Param(int Index, string Id, string Name, float Value);

    [McpServerTool(Name = "list_instrument_kinds"), Description("List the built-in instrument kinds (kind id + name) you can add.")]
    public InstrumentKind[] ListInstrumentKinds() => Array.ConvertAll(Kinds, k => new InstrumentKind(k.Kind, k.Name));

    [McpServerTool(Name = "add_instrument_track"), Description("Add an instrument track with the given built-in kind (see list_instrument_kinds). Returns the track id.")]
    public Task<int> AddInstrumentTrack([Description("Instrument kind id (0..13)")] int kind) => Mutate(() => kind switch
    {
        1 => E.AddSamplerInstrumentTrack(),
        2 => E.AddPhysicalSynthTrack(),
        3 => E.AddInstrumentRackTrack(),
        4 => E.AddDrumRackTrack(),
        5 => E.AddWavetableSynthTrack(),
        6 => E.AddVoltSynthTrack(),
        7 => E.AddBassSynthTrack(),
        8 => E.AddPendulumSynthTrack(),
        9 => E.AddOperatorSynthTrack(),
        10 => E.AddGrainSynthTrack(),
        11 => E.AddFluxSynthTrack(),
        12 => E.AddRhythmTrack(),
        13 => E.AddMonolithTrack(),
        _ => E.AddInstrumentTrack(),   // 0 = Nota Synth
    });

    [McpServerTool(Name = "get_instrument_params"), Description("List the track instrument's parameters (index, stable id, name, normalized value 0..1).")]
    public Task<Param[]> GetInstrumentParams(int trackId) => Read(() =>
    {
        int pc = E.PluginParamCount(trackId, -1);
        var ps = new Param[pc];
        for (int i = 0; i < pc; i++) ps[i] = new Param(i, E.PluginParamId(trackId, -1, i), E.PluginParamName(trackId, -1, i), E.PluginParamGet(trackId, -1, i));
        return ps;
    });

    [McpServerTool(Name = "set_instrument_param"), Description("Set a track instrument parameter by index to a normalized value (0..1). Use get_instrument_params for indices.")]
    public Task SetInstrumentParam(int trackId, int paramIndex, [Description("Normalized 0..1")] float value)
        => Mutate(() => E.PluginParamSet(trackId, -1, paramIndex, Math.Clamp(value, 0f, 1f)));
}
