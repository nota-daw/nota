// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — the Audio Effect Rack: a rack device sitting in a track's insert chain (add it with
// add_device kind 5). It runs parallel effect chains; mode = 0 Parallel (summed) | 1 Series |
// 2 Select, with a dry/wet and 8 macros. Every op targets the rack at (trackId, deviceIndex);
// chains and macros are 0-based. Chain instruments/params are normalized 0..1 like built-ins.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class EffectRackTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record ChainInfo(int Index, int InstrumentKind, string InstrumentName, int TriggerNote,
        float Gain, float Pan, bool Mute, bool Solo, int DeviceCount);
    public sealed record RackSnapshot(int Mode, string ModeName, float DryWet, float Volume, float ChainSelect, ChainInfo[] Chains);
    public sealed record Param(int Index, string Name, float Value);
    public sealed record MacroInfo(int Index, string Name, float Value);
    public sealed record Mapping(int Index, int Macro, int Chain, int DeviceIndex, int ParamIndex, float RangeMin, float RangeMax);

    private static string ModeName(int m) => m switch { 1 => "series", 2 => "select", _ => "parallel" };

    [McpServerTool(Name = "get_effect_rack"), Description(
        "Snapshot of an Audio Effect Rack device: mode, dry/wet, volume, chain-select, and every chain "
        + "(index, instrument kind+name, trigger note, gain/pan/mute/solo, device count).")]
    public Task<RackSnapshot> GetEffectRack(int trackId, int deviceIndex) => Read(() =>
    {
        int n = E.RackDevChainCount(trackId, deviceIndex);
        var chains = new ChainInfo[n];
        for (int c = 0; c < n; c++)
            chains[c] = new ChainInfo(c, E.RackDevChainInstrumentKind(trackId, deviceIndex, c), E.RackDevChainInstrumentName(trackId, deviceIndex, c),
                E.RackDevChainTriggerNote(trackId, deviceIndex, c), E.RackDevChainGain(trackId, deviceIndex, c), E.RackDevChainPan(trackId, deviceIndex, c),
                E.RackDevChainMute(trackId, deviceIndex, c), E.RackDevChainSolo(trackId, deviceIndex, c), E.RackDevChainDeviceCount(trackId, deviceIndex, c));
        int mode = E.RackDevMode(trackId, deviceIndex);
        return new RackSnapshot(mode, ModeName(mode), E.RackDevDryWet(trackId, deviceIndex), E.RackDevVolume(trackId, deviceIndex),
            E.RackDevChainSelect(trackId, deviceIndex), chains);
    });

    [McpServerTool(Name = "set_effect_rack_mode"), Description("Set the rack mode: 0 = Parallel (summed), 1 = Series, 2 = Select.")]
    public Task SetEffectRackMode(int trackId, int deviceIndex, int mode) => Mutate(() => E.RackDevSetMode(trackId, deviceIndex, Math.Clamp(mode, 0, 2)));

    [McpServerTool(Name = "set_effect_rack_drywet"), Description("Set the rack dry/wet mix (0 = dry .. 1 = wet).")]
    public Task SetEffectRackDryWet(int trackId, int deviceIndex, float value) => Mutate(() => E.RackDevSetDryWet(trackId, deviceIndex, Math.Clamp(value, 0f, 1f)));

    [McpServerTool(Name = "set_effect_rack_volume"), Description("Set the rack output volume (0..1 linear).")]
    public Task SetEffectRackVolume(int trackId, int deviceIndex, float value) => Mutate(() => E.RackDevSetVolume(trackId, deviceIndex, Math.Clamp(value, 0f, 1f)));

    [McpServerTool(Name = "set_effect_rack_chain_select"), Description("Select-mode: set the chain-select position (0..1) that picks/crossfades the active chain.")]
    public Task SetEffectRackChainSelect(int trackId, int deviceIndex, float value) => Mutate(() => E.RackDevSetChainSelect(trackId, deviceIndex, Math.Clamp(value, 0f, 1f)));

    [McpServerTool(Name = "add_effect_rack_chain"), Description("Add a parallel effect chain to the rack (instrumentKind seeds the chain's source; typically 4 = Utility). Returns the chain index.")]
    public Task<int> AddEffectRackChain(int trackId, int deviceIndex, int instrumentKind = 4) => Mutate(() => E.RackDevAddChain(trackId, deviceIndex, instrumentKind));

    [McpServerTool(Name = "remove_effect_rack_chain"), Description("Remove a chain from the rack by index.")]
    public Task<bool> RemoveEffectRackChain(int trackId, int deviceIndex, int chain) => Mutate(() => E.RackDevRemoveChain(trackId, deviceIndex, chain));

    [McpServerTool(Name = "set_effect_rack_chain_instrument"), Description("Replace a chain's source instrument with another built-in kind.")]
    public Task<bool> SetEffectRackChainInstrument(int trackId, int deviceIndex, int chain, int instrumentKind)
        => Mutate(() => E.RackDevSetChainInstrument(trackId, deviceIndex, chain, instrumentKind));

    [McpServerTool(Name = "get_effect_rack_chain_params"), Description("List a chain instrument's parameters (index, name, normalized value 0..1).")]
    public Task<Param[]> GetEffectRackChainParams(int trackId, int deviceIndex, int chain) => Read(() =>
    {
        int pc = E.RackDevChainInstrumentParamCount(trackId, deviceIndex, chain);
        var ps = new Param[pc];
        for (int i = 0; i < pc; i++) ps[i] = new Param(i, E.RackDevChainInstrumentParamName(trackId, deviceIndex, chain, i), E.RackDevChainInstrumentParamGet(trackId, deviceIndex, chain, i));
        return ps;
    });

    [McpServerTool(Name = "set_effect_rack_chain_param"), Description("Set a chain instrument parameter by index to a normalized value 0..1.")]
    public Task SetEffectRackChainParam(int trackId, int deviceIndex, int chain, int paramIndex, [Description("Normalized 0..1")] float value)
        => Mutate(() => E.RackDevChainInstrumentParamSet(trackId, deviceIndex, chain, paramIndex, Math.Clamp(value, 0f, 1f)));

    [McpServerTool(Name = "set_effect_rack_chain_mix"), Description("Set a chain mixer field. field = gain (0..1) | pan (-1..1) | mute (0/1) | solo (0/1).")]
    public Task SetEffectRackChainMix(int trackId, int deviceIndex, int chain, [Description("gain | pan | mute | solo")] string field, float value) => Mutate(() =>
    {
        switch (field.ToLowerInvariant())
        {
            case "gain": E.RackDevSetChainGain(trackId, deviceIndex, chain, value); break;
            case "pan": E.RackDevSetChainPan(trackId, deviceIndex, chain, Math.Clamp(value, -1f, 1f)); break;
            case "mute": E.RackDevSetChainMute(trackId, deviceIndex, chain, value != 0); break;
            case "solo": E.RackDevSetChainSolo(trackId, deviceIndex, chain, value != 0); break;
        }
    });

    [McpServerTool(Name = "get_effect_rack_macros"), Description("List the rack's 8 macro knobs (index, name, value 0..1).")]
    public Task<MacroInfo[]> GetEffectRackMacros(int trackId, int deviceIndex) => Read(() =>
    {
        var ms = new MacroInfo[8];
        for (int i = 0; i < 8; i++) ms[i] = new MacroInfo(i, E.RackDevMacroName(trackId, deviceIndex, i), E.RackDevMacroGet(trackId, deviceIndex, i));
        return ms;
    });

    [McpServerTool(Name = "set_effect_rack_macro"), Description("Set a macro knob (0..7) to a normalized value 0..1.")]
    public Task SetEffectRackMacro(int trackId, int deviceIndex, int macro, [Description("Normalized 0..1")] float value)
        => Mutate(() => E.RackDevMacroSet(trackId, deviceIndex, macro, Math.Clamp(value, 0f, 1f)));

    [McpServerTool(Name = "list_effect_rack_mappings"), Description("List the rack's macro→parameter mappings (index, macro, chain, device, param, range).")]
    public Task<Mapping[]> ListEffectRackMappings(int trackId, int deviceIndex) => Read(() =>
    {
        int n = E.RackDevMappingCount(trackId, deviceIndex);
        var list = new List<Mapping>(n);
        for (int i = 0; i < n; i++)
            if (E.RackDevTryGetMapping(trackId, deviceIndex, i, out var m))
                list.Add(new Mapping(i, m.Macro, m.Chain, m.DeviceIndex, m.ParamIndex, m.RangeMin, m.RangeMax));
        return list.ToArray();
    });

    [McpServerTool(Name = "add_effect_rack_mapping"), Description(
        "Map a macro (0..7) onto a chain parameter. targetDevice = the chain's device index (or -1 for its source instrument); "
        + "range is the param's normalized 0..1 span the macro sweeps. Returns the mapping index.")]
    public Task<int> AddEffectRackMapping(int trackId, int deviceIndex, int macro, int chain, int targetDevice, int paramIndex, float rangeMin = 0f, float rangeMax = 1f)
        => Mutate(() => E.RackDevAddMacroMapping(trackId, deviceIndex, macro, chain, targetDevice, paramIndex, rangeMin, rangeMax));

    [McpServerTool(Name = "remove_effect_rack_mapping"), Description("Remove a macro mapping by index.")]
    public Task<bool> RemoveEffectRackMapping(int trackId, int deviceIndex, int index) => Mutate(() => E.RackDevRemoveMapping(trackId, deviceIndex, index));
}
