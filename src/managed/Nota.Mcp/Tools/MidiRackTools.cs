// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — the MIDI Effect Rack (MIDI effect kind 6, specs/040): chains of built-in MIDI
// effects that take notes by key, velocity and (Select mode) select zone, each with transpose
// and a velocity-scaling volume, merged into the track's notes before the instrument.
// Every tool targets the rack at `midiIndex` among the track's MIDI effects (list_midi_fx).
// Chains, devices and macros are 0-based. Writes are one undo step each.

using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class MidiRackTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record Device(int Index, int Kind, string Name, bool Bypassed);
    public sealed record Chain(int Index, string Name, bool Mute, bool Solo, int KeyLo, int KeyHi, int VelLo, int VelHi,
        int SelLo, int SelHi, int Transpose, float VelScale, Device[] Devices);
    public sealed record Rack(string Mode, int Selector, bool Merge, bool Thru, Chain[] Chains);
    public sealed record Param(int Index, string Name, float Min, float Max, float Value);
    public sealed record Macro(int Index, string Name, float Value, string Curve, int Targets);
    public sealed record Mapping(int Index, int Macro, int Chain, int Device, int Param, float Min, float Max, string Curve);

    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    // A key as a MIDI note number or a name ("C3", "F#4", "Bb2"; C4 = 60).
    private static int Note(string s)
    {
        s = s.Trim();
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return Math.Clamp(n, 0, 127);
        int i = 1;
        int pc = Array.IndexOf(Names, char.ToUpperInvariant(s[0]).ToString());
        if (pc < 0) throw new ArgumentException($"Not a note: {s}");
        if (s.Length > 1 && s[1] == '#') { pc++; i = 2; }
        else if (s.Length > 1 && (s[1] == 'b' || s[1] == '♭')) { pc--; i = 2; }
        int oct = int.Parse(s[i..].Replace('−', '-'), NumberStyles.Integer, CultureInfo.InvariantCulture);
        return Math.Clamp((oct + 1) * 12 + pc, 0, 127);
    }

    [McpServerTool(Name = "get_midi_rack"), Description(
        "Snapshot of a MIDI Effect Rack: mode (parallel | select), selector 0..127, merge duplicate notes, pass through "
        + "notes outside every zone, and every chain (name, mute, solo, key zone, velocity zone, select range, transpose, "
        + "velocity scale 0..2 and its MIDI effects).")]
    public Task<Rack> GetMidiRack(int trackId, int midiIndex) => Read(() =>
    {
        int n = E.MidiRackChainCount(trackId, midiIndex);
        var chains = new Chain[n];
        for (int c = 0; c < n; c++)
        {
            E.MidiRackChainZone(trackId, midiIndex, c, out int kl, out int kh, out int vl, out int vh);
            E.MidiRackChainSelectRange(trackId, midiIndex, c, out int sl, out int sh);
            int dn = E.MidiRackChainDeviceCount(trackId, midiIndex, c);
            var devs = new Device[dn];
            for (int d = 0; d < dn; d++)
                devs[d] = new Device(d, E.MidiRackChainDeviceKind(trackId, midiIndex, c, d), E.MidiRackChainDeviceName(trackId, midiIndex, c, d),
                    E.MidiRackChainDeviceBypassed(trackId, midiIndex, c, d));
            chains[c] = new Chain(c, E.MidiRackChainName(trackId, midiIndex, c), E.MidiRackChainMute(trackId, midiIndex, c),
                E.MidiRackChainSolo(trackId, midiIndex, c), kl, kh, vl, vh, sl, sh, E.MidiRackChainTranspose(trackId, midiIndex, c),
                E.MidiRackChainVelScale(trackId, midiIndex, c), devs);
        }
        return new Rack(E.MidiRackMode(trackId, midiIndex) == 2 ? "select" : "parallel", (int)Math.Round(E.MidiRackSelector(trackId, midiIndex) * 127),
            E.MidiRackMerge(trackId, midiIndex), E.MidiRackThru(trackId, midiIndex), chains);
    });

    [McpServerTool(Name = "add_midi_rack_chain"), Description("Add an empty chain (up to 16). Returns its index, or -1 at the limit.")]
    public Task<int> AddChain(int trackId, int midiIndex, string? name = null) => Mutate(() =>
    {
        int c = E.MidiRackAddChain(trackId, midiIndex);
        if (c >= 0 && !string.IsNullOrEmpty(name)) E.MidiRackSetChainName(trackId, midiIndex, c, name);
        return c;
    });

    [McpServerTool(Name = "remove_midi_rack_chain"), Description("Remove a chain (a rack keeps at least one); its macro mappings go with it.")]
    public Task<bool> RemoveChain(int trackId, int midiIndex, int chain) => Mutate(() => E.MidiRackRemoveChain(trackId, midiIndex, chain));

    [McpServerTool(Name = "duplicate_midi_rack_chain"), Description("Copy a chain right after itself, with its MIDI effects, zones and mappings. Returns the new index.")]
    public Task<int> DuplicateChain(int trackId, int midiIndex, int chain) => Mutate(() => E.MidiRackDuplicateChain(trackId, midiIndex, chain));

    [McpServerTool(Name = "set_midi_rack_chain_name"), Description("Rename a chain.")]
    public Task SetChainName(int trackId, int midiIndex, int chain, string name) => Mutate(() => E.MidiRackSetChainName(trackId, midiIndex, chain, name));

    [McpServerTool(Name = "add_midi_rack_chain_device"), Description("Append a built-in MIDI effect (kind 0..5, see list_midi_fx_kinds) to a chain. Returns its index.")]
    public Task<int> AddChainDevice(int trackId, int midiIndex, int chain, int kind) => Mutate(() => E.MidiRackChainDeviceAdd(trackId, midiIndex, chain, kind));

    [McpServerTool(Name = "remove_midi_rack_chain_device"), Description("Remove a MIDI effect from a chain.")]
    public Task<bool> RemoveChainDevice(int trackId, int midiIndex, int chain, int device) => Mutate(() => E.MidiRackChainDeviceRemove(trackId, midiIndex, chain, device));

    [McpServerTool(Name = "move_midi_rack_chain_device"), Description("Move a MIDI effect within its chain.")]
    public Task<bool> MoveChainDevice(int trackId, int midiIndex, int chain, int from, int to) => Mutate(() => E.MidiRackChainDeviceMove(trackId, midiIndex, chain, from, to));

    [McpServerTool(Name = "get_midi_rack_chain_params"), Description("List a chain MIDI effect's parameters (index, name, min, max, value).")]
    public Task<Param[]> GetChainParams(int trackId, int midiIndex, int chain, int device) => Read(() =>
    {
        int pc = E.MidiRackChainDeviceParamCount(trackId, midiIndex, chain, device);
        var ps = new Param[pc];
        for (int p = 0; p < pc; p++)
            ps[p] = new Param(p, E.MidiRackChainDeviceParamName(trackId, midiIndex, chain, device, p), E.MidiRackChainDeviceParamMin(trackId, midiIndex, chain, device, p),
                E.MidiRackChainDeviceParamMax(trackId, midiIndex, chain, device, p), E.MidiRackChainDeviceParamGet(trackId, midiIndex, chain, device, p));
        return ps;
    });

    [McpServerTool(Name = "set_midi_rack_chain_param"), Description("Set a chain MIDI effect's parameter (in its own min..max).")]
    public Task SetChainParam(int trackId, int midiIndex, int chain, int device, int param, float value)
        => Mutate(() => E.MidiRackChainDeviceParamSet(trackId, midiIndex, chain, device, param, value));

    [McpServerTool(Name = "set_midi_rack_chain_bypass"), Description("Bypass or enable a chain MIDI effect.")]
    public Task SetChainBypass(int trackId, int midiIndex, int chain, int device, bool bypassed)
        => Mutate(() => E.MidiRackChainDeviceSetBypassed(trackId, midiIndex, chain, device, bypassed));

    [McpServerTool(Name = "set_midi_rack_chain_mix"), Description("Set a chain's mute, solo, transpose (−48..+48 semitones) and velocity scale (\"Volume\", 0..2: 1 = unchanged, 0 = silent). Omitted fields stay.")]
    public Task SetChainMix(int trackId, int midiIndex, int chain, bool? mute = null, bool? solo = null, int? transpose = null, float? velScale = null) => Mutate(() =>
    {
        if (mute is { } m) E.MidiRackSetChainMute(trackId, midiIndex, chain, m);
        if (solo is { } s) E.MidiRackSetChainSolo(trackId, midiIndex, chain, s);
        if (transpose is { } tr) E.MidiRackSetChainTranspose(trackId, midiIndex, chain, tr);
        if (velScale is { } v) E.MidiRackSetChainVelScale(trackId, midiIndex, chain, v);
    });

    [McpServerTool(Name = "set_midi_rack_chain_zone"), Description(
        "Set a chain's zones; omitted ends stay. Keys take note numbers or names (\"C3\", \"F#4\"; C4 = 60); velocity and "
        + "select ranges are 0..127. In Select mode a chain plays while the selector is inside its select range.")]
    public Task SetChainZone(int trackId, int midiIndex, int chain, string? keyLo = null, string? keyHi = null,
        int? velLo = null, int? velHi = null, int? selLo = null, int? selHi = null) => Mutate(() =>
    {
        E.MidiRackChainZone(trackId, midiIndex, chain, out int kl, out int kh, out int vl, out int vh);
        if (keyLo is not null) kl = Note(keyLo);
        if (keyHi is not null) kh = Note(keyHi);
        E.MidiRackSetChainZone(trackId, midiIndex, chain, kl, kh, velLo ?? vl, velHi ?? vh);
        E.MidiRackChainSelectRange(trackId, midiIndex, chain, out int sl, out int sh);
        if (selLo is not null || selHi is not null) E.MidiRackSetChainSelect(trackId, midiIndex, chain, selLo ?? sl, selHi ?? sh);
    });

    [McpServerTool(Name = "set_midi_rack_mode"), Description("How chains run: parallel (every chain whose zone a note hits) or select (only chains whose select range holds the selector).")]
    public Task SetMode(int trackId, int midiIndex, [Description("parallel | select")] string mode)
        => Mutate(() => E.MidiRackSetMode(trackId, midiIndex, mode.Trim().Equals("select", StringComparison.OrdinalIgnoreCase) ? 2 : 0));

    [McpServerTool(Name = "set_midi_rack_selector"), Description("Set the Select-mode selector, 0..127.")]
    public Task SetSelector(int trackId, int midiIndex, int value) => Mutate(() => E.MidiRackSetSelector(trackId, midiIndex, Math.Clamp(value, 0, 127) / 127f));

    [McpServerTool(Name = "set_midi_rack_output"), Description("Merge duplicate notes from different chains into one; pass notes no chain takes through unchanged. Omitted fields stay.")]
    public Task SetOutput(int trackId, int midiIndex, bool? merge = null, bool? thru = null) => Mutate(() =>
    {
        if (merge is { } m) E.MidiRackSetMerge(trackId, midiIndex, m);
        if (thru is { } t) E.MidiRackSetThru(trackId, midiIndex, t);
    });

    [McpServerTool(Name = "get_midi_rack_macros"), Description("The rack's 8 macros: name, value 0..1, curve (lin/exp/log/s, \"mixed\" when targets differ, \"none\" without targets) and target count.")]
    public Task<Macro[]> GetMacros(int trackId, int midiIndex) => Read(() =>
    {
        int n = E.MidiRackMappingCount(trackId, midiIndex);
        (bool, int, int) Map(int i) => E.MidiRackMappingInfo(trackId, midiIndex, i, out int m, out _, out _, out _, out _, out _, out int cv) != 0 ? (true, m, cv) : (false, 0, 0);
        var ms = new Macro[8];
        for (int i = 0; i < 8; i++)
        {
            var (curve, targets) = RackTools.MacroCurve(n, Map, i);
            ms[i] = new Macro(i, E.MidiRackMacroName(trackId, midiIndex, i), E.MidiRackMacroGet(trackId, midiIndex, i), curve, targets);
        }
        return ms;
    });

    [McpServerTool(Name = "set_midi_rack_macro"), Description("Set a macro's value (0..1) and/or name.")]
    public Task SetMacro(int trackId, int midiIndex, int macro, float? value = null, string? name = null) => Mutate(() =>
    {
        if (value is { } v) E.MidiRackMacroSet(trackId, midiIndex, macro, Math.Clamp(v, 0f, 1f));
        if (name is not null) E.MidiRackSetMacroName(trackId, midiIndex, macro, name);
    });

    [McpServerTool(Name = "list_midi_rack_mappings"), Description("The rack's macro mappings (index, macro, chain, device — or -3 for the selector — param, range, curve).")]
    public Task<Mapping[]> ListMappings(int trackId, int midiIndex) => Read(() =>
    {
        int n = E.MidiRackMappingCount(trackId, midiIndex);
        var list = new List<Mapping>(n);
        for (int i = 0; i < n; i++)
            if (E.MidiRackMappingInfo(trackId, midiIndex, i, out int m, out int c, out int d, out int p, out float lo, out float hi, out int cv) != 0)
                list.Add(new Mapping(i, m, c, d, p, lo, hi, RackTools.CurveName(cv)));
        return list.ToArray();
    });

    [McpServerTool(Name = "add_midi_rack_mapping"), Description(
        "Map a macro (0..7) onto a chain MIDI effect's parameter (device = its index in the chain) or onto the selector "
        + "(device -3, param 0, range 0..1). Range is in the parameter's units; min above max inverts. A parameter belongs "
        + "to one macro: mapping it again moves its mapping. Returns the mapping index.")]
    public Task<int> AddMapping(int trackId, int midiIndex, int macro, int chain, int device, int param, float min = 0f, float max = 1f)
        => Mutate(() => E.MidiRackAddMacroMapping(trackId, midiIndex, macro, chain, device, param, min, max));

    [McpServerTool(Name = "remove_midi_rack_mapping"), Description("Remove a macro mapping by index.")]
    public Task<bool> RemoveMapping(int trackId, int midiIndex, int index) => Mutate(() => E.MidiRackRemoveMapping(trackId, midiIndex, index));

    [McpServerTool(Name = "set_midi_rack_mapping"), Description("Change a mapping's range (in the parameter's units) and/or curve (lin | exp | log | s).")]
    public Task SetMapping(int trackId, int midiIndex, int index, float? min = null, float? max = null, string? curve = null) => Mutate(() =>
    {
        if (min is not null || max is not null)
        {
            E.MidiRackMappingInfo(trackId, midiIndex, index, out _, out _, out _, out _, out float lo, out float hi, out _);
            E.MidiRackSetMappingRange(trackId, midiIndex, index, min ?? lo, max ?? hi);
        }
        if (curve is not null) E.MidiRackSetMappingCurve(trackId, midiIndex, index, RackTools.CurveId(curve));
    });
}
