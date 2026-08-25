// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — MIDI effects (transform the note stream before the instrument).

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class MidiFxTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    private static readonly (int Kind, string Name)[] Kinds =
    {
        (0, "Nota Arp"), (1, "Nota Chord"), (2, "Nota Scale"), (3, "Nota Length"), (4, "Nota Velocity"), (5, "Nota Random"),
    };

    public sealed record MidiFxKind(int Kind, string Name);
    public sealed record MidiFx(int Index, int Kind, string Name, bool Bypassed);
    public sealed record Param(int Index, string Name, float Min, float Max, float Value);

    [McpServerTool(Name = "list_midi_fx_kinds"), Description("List the built-in MIDI-effect kinds (kind id + name).")]
    public MidiFxKind[] ListMidiFxKinds() => Array.ConvertAll(Kinds, k => new MidiFxKind(k.Kind, k.Name));

    [McpServerTool(Name = "list_midi_fx"), Description("List a track's MIDI effects (index, kind, name, bypass).")]
    public Task<MidiFx[]> ListMidiFx(int trackId) => Read(() =>
    {
        int c = E.TrackMidiEffectCount(trackId);
        var arr = new MidiFx[c];
        for (int i = 0; i < c; i++) arr[i] = new MidiFx(i, E.MidiEffectKind(trackId, i), E.MidiEffectName(trackId, i), E.MidiEffectBypassed(trackId, i));
        return arr;
    });

    [McpServerTool(Name = "add_midi_fx"), Description("Add a built-in MIDI effect (see list_midi_fx_kinds) before the instrument. Returns the index (or -1).")]
    public Task<int> AddMidiFx(int trackId, int kind) => Mutate(() => E.AddMidiEffect(trackId, kind));

    [McpServerTool(Name = "remove_midi_fx"), Description("Remove a MIDI effect from a track by index.")]
    public Task RemoveMidiFx(int trackId, int index) => Mutate(() => E.RemoveMidiEffect(trackId, index));

    [McpServerTool(Name = "set_midi_fx_bypass"), Description("Bypass or enable a MIDI effect.")]
    public Task SetMidiFxBypass(int trackId, int index, bool bypassed) => Mutate(() => E.SetMidiEffectBypassed(trackId, index, bypassed));

    [McpServerTool(Name = "get_midi_fx_params"), Description("List a MIDI effect's parameters (index, name, min, max, value).")]
    public Task<Param[]> GetMidiFxParams(int trackId, int index) => Read(() =>
    {
        int pc = E.MidiEffectParamCount(trackId, index);
        var ps = new Param[pc];
        for (int i = 0; i < pc; i++)
            ps[i] = new Param(i, E.MidiEffectParamName(trackId, index, i), E.MidiEffectParamMin(trackId, index, i), E.MidiEffectParamMax(trackId, index, i), E.MidiEffectGetParam(trackId, index, i));
        return ps;
    });

    [McpServerTool(Name = "set_midi_fx_param"), Description("Set a MIDI effect parameter by index (value in the param's min..max range).")]
    public Task SetMidiFxParam(int trackId, int index, int paramIndex, float value) => Mutate(() => E.MidiEffectSetParam(trackId, index, paramIndex, value));
}
