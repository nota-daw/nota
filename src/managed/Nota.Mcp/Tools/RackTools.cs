// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — the Instrument Rack and Drum Rack (an instrument track whose "instrument" is a set
// of parallel chains). Each chain has its own instrument + device chain + mix (gain/pan/mute/solo)
// and a MIDI trigger note (Drum Rack pads); 8 macros can map onto any chain parameter. The Drum
// Rack adds per-chain choke/tune/decay and kit-wide swing/humanize.
//
// All ops target the instrument-rack on `trackId`. Chains and macros are 0-based.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class RackTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record ChainInfo(int Index, int InstrumentKind, string InstrumentName, int TriggerNote,
        float Gain, float Pan, bool Mute, bool Solo, int DeviceCount);
    public sealed record RackSnapshot(float Volume, ChainInfo[] Chains);
    public sealed record Param(int Index, string Id, string Name, float Value);
    public sealed record MacroInfo(int Index, string Name, float Value);
    public sealed record Mapping(int Index, int Macro, int Chain, int DeviceIndex, int ParamIndex, float RangeMin, float RangeMax);

    // ---- snapshot ---------------------------------------------------------

    [McpServerTool(Name = "get_rack"), Description(
        "Snapshot of an Instrument/Drum Rack on a track: rack volume and every chain "
        + "(index, instrument kind+name, MIDI trigger note, gain/pan/mute/solo, device count).")]
    public Task<RackSnapshot> GetRack(int trackId) => Read(() =>
    {
        int n = E.RackChainCount(trackId);
        var chains = new ChainInfo[n];
        for (int c = 0; c < n; c++)
            chains[c] = new ChainInfo(c, E.RackChainInstrumentKind(trackId, c), E.RackChainInstrumentName(trackId, c),
                E.RackChainTriggerNote(trackId, c), E.RackChainGain(trackId, c), E.RackChainPan(trackId, c),
                E.RackChainMute(trackId, c), E.RackChainSolo(trackId, c), E.RackChainDeviceCount(trackId, c));
        return new RackSnapshot(E.RackVolume(trackId), chains);
    });

    // ---- chains -----------------------------------------------------------

    [McpServerTool(Name = "add_rack_chain"), Description("Add a parallel chain to the rack with the given built-in instrument kind. Returns the chain index.")]
    public Task<int> AddRackChain(int trackId, [Description("Instrument kind id, e.g. 0=Synth, 2=Physical, 6=Volt")] int instrumentKind)
        => Mutate(() => E.RackAddChain(trackId, instrumentKind));

    [McpServerTool(Name = "remove_rack_chain"), Description("Remove a chain from the rack by index.")]
    public Task<bool> RemoveRackChain(int trackId, int chain) => Mutate(() => E.RackRemoveChain(trackId, chain));

    [McpServerTool(Name = "set_chain_instrument"), Description("Replace a chain's instrument with another built-in kind.")]
    public Task<bool> SetChainInstrument(int trackId, int chain, int instrumentKind)
        => Mutate(() => E.RackSetChainInstrument(trackId, chain, instrumentKind));

    [McpServerTool(Name = "set_chain_trigger_note"), Description("Set the MIDI note that triggers a chain (Drum Rack pad mapping, e.g. 36 = C1 kick).")]
    public Task SetChainTriggerNote(int trackId, int chain, int note) => Mutate(() => E.RackSetChainTriggerNote(trackId, chain, note));

    // ---- chain instrument params -----------------------------------------

    [McpServerTool(Name = "get_chain_instrument_params"), Description("List a chain instrument's parameters (index, stable id, name, normalized value 0..1).")]
    public Task<Param[]> GetChainInstrumentParams(int trackId, int chain) => Read(() =>
    {
        int pc = E.RackChainInstrumentParamCount(trackId, chain);
        var ps = new Param[pc];
        for (int i = 0; i < pc; i++)
            ps[i] = new Param(i, E.RackChainInstrumentParamId(trackId, chain, i), E.RackChainInstrumentParamName(trackId, chain, i),
                E.RackChainInstrumentParamGet(trackId, chain, i));
        return ps;
    });

    [McpServerTool(Name = "set_chain_instrument_param"), Description("Set a chain instrument parameter by index to a normalized value (0..1).")]
    public Task SetChainInstrumentParam(int trackId, int chain, int paramIndex, [Description("Normalized 0..1")] float value)
        => Mutate(() => E.RackChainInstrumentParamSet(trackId, chain, paramIndex, Math.Clamp(value, 0f, 1f)));

    // ---- chain mix --------------------------------------------------------

    [McpServerTool(Name = "set_chain_mix"), Description(
        "Set a chain mixer field. field = gain (0..1) | pan (-1..1) | mute (0/1) | solo (0/1).")]
    public Task SetChainMix(int trackId, int chain,
        [Description("gain | pan | mute | solo")] string field, float value) => Mutate(() =>
    {
        switch (field.ToLowerInvariant())
        {
            case "gain": E.RackSetChainGain(trackId, chain, value); break;
            case "pan": E.RackSetChainPan(trackId, chain, Math.Clamp(value, -1f, 1f)); break;
            case "mute": E.RackSetChainMute(trackId, chain, value != 0); break;
            case "solo": E.RackSetChainSolo(trackId, chain, value != 0); break;
        }
    });

    [McpServerTool(Name = "set_chain_zone"), Description("Set a chain's key/velocity zone (which incoming notes reach it): key + velocity ranges 0..127.")]
    public Task SetChainZone(int trackId, int chain, int keyLo, int keyHi, int velLo, int velHi)
        => Mutate(() => E.RackSetChainZone(trackId, chain, keyLo, keyHi, velLo, velHi));

    // ---- macros -----------------------------------------------------------

    [McpServerTool(Name = "get_macros"), Description("List the rack's 8 macro knobs (index, name, value 0..1).")]
    public Task<MacroInfo[]> GetMacros(int trackId) => Read(() =>
    {
        var ms = new MacroInfo[8];
        for (int i = 0; i < 8; i++) ms[i] = new MacroInfo(i, E.RackMacroName(trackId, i), E.RackMacroGet(trackId, i));
        return ms;
    });

    [McpServerTool(Name = "set_macro"), Description("Set a macro knob (0..7) to a normalized value 0..1; drives all its mapped parameters.")]
    public Task SetMacro(int trackId, int macro, [Description("Normalized 0..1")] float value)
        => Mutate(() => E.RackMacroSet(trackId, macro, Math.Clamp(value, 0f, 1f)));

    [McpServerTool(Name = "set_macro_name"), Description("Rename a macro knob (0..7).")]
    public Task SetMacroName(int trackId, int macro, string name) => Mutate(() => E.RackSetMacroName(trackId, macro, name));

    [McpServerTool(Name = "list_macro_mappings"), Description("List the rack's macro→parameter mappings (index, macro, chain, device, param, range).")]
    public Task<Mapping[]> ListMacroMappings(int trackId) => Read(() =>
    {
        int n = E.RackMappingCount(trackId);
        var list = new List<Mapping>(n);
        for (int i = 0; i < n; i++)
            if (E.RackTryGetMapping(trackId, i, out var m))
                list.Add(new Mapping(i, m.Macro, m.Chain, m.DeviceIndex, m.ParamIndex, m.RangeMin, m.RangeMax));
        return list.ToArray();
    });

    [McpServerTool(Name = "add_macro_mapping"), Description(
        "Map a macro (0..7) onto a parameter. deviceIndex = -1 targets the chain instrument, else the "
        + "chain's device index. range is the param's normalized 0..1 span the macro sweeps. Returns mapping index.")]
    public Task<int> AddMacroMapping(int trackId, int macro, int chain, int deviceIndex, int paramIndex, float rangeMin = 0f, float rangeMax = 1f)
        => Mutate(() => E.RackAddMacroMapping(trackId, macro, chain, deviceIndex, paramIndex, rangeMin, rangeMax));

    [McpServerTool(Name = "remove_macro_mapping"), Description("Remove a macro mapping by index.")]
    public Task<bool> RemoveMacroMapping(int trackId, int index) => Mutate(() => E.RackRemoveMapping(trackId, index));

    // ---- rack level -------------------------------------------------------

    [McpServerTool(Name = "set_rack_volume"), Description("Set the rack's overall output volume (0..1 linear).")]
    public Task SetRackVolume(int trackId, float value) => Mutate(() => E.RackSetVolume(trackId, Math.Clamp(value, 0f, 1f)));

    [McpServerTool(Name = "set_rack_glide"), Description("Set the rack glide/portamento amount (0..1).")]
    public Task SetRackGlide(int trackId, float value) => Mutate(() => E.RackSetGlide(trackId, Math.Clamp(value, 0f, 1f)));

    // ---- Drum Rack extras -------------------------------------------------

    [McpServerTool(Name = "set_pad_choke"), Description("Drum Rack: set a pad/chain's choke group (0 = none). Pads in the same group cut each other off.")]
    public Task SetPadChoke(int trackId, int chain, int group) => Mutate(() => E.RackSetChainChoke(trackId, chain, group));

    [McpServerTool(Name = "set_pad_tune"), Description("Drum Rack: transpose a pad/chain by semitones.")]
    public Task SetPadTune(int trackId, int chain, int semitones) => Mutate(() => E.RackSetChainTune(trackId, chain, semitones));

    [McpServerTool(Name = "set_pad_decay"), Description("Drum Rack: set a pad/chain's decay amount (0..1).")]
    public Task SetPadDecay(int trackId, int chain, float value) => Mutate(() => E.RackSetChainDecay(trackId, chain, Math.Clamp(value, 0f, 1f)));

    [McpServerTool(Name = "set_kit_swing"), Description("Drum Rack: set kit-wide swing (0..1).")]
    public Task SetKitSwing(int trackId, float value) => Mutate(() => E.RackSetSwing(trackId, Math.Clamp(value, 0f, 1f)));

    [McpServerTool(Name = "set_kit_humanize"), Description("Drum Rack: set kit-wide humanize/timing randomisation (0..1).")]
    public Task SetKitHumanize(int trackId, float value) => Mutate(() => E.RackSetHumanize(trackId, Math.Clamp(value, 0f, 1f)));
}
