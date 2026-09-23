// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
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
        (12, "Nota Rhythm"), (13, "Nota Monolith"), (14, "Nota Pentad"), (15, "Nota Consort"),
    };

    public sealed record InstrumentKind(int Kind, string Name);
    public sealed record Param(int Index, string Id, string Name, float Value);

    [McpServerTool(Name = "list_instrument_kinds"), Description("List the built-in instrument kinds (kind id + name) you can add.")]
    public InstrumentKind[] ListInstrumentKinds() => Array.ConvertAll(Kinds, k => new InstrumentKind(k.Kind, k.Name));

    [McpServerTool(Name = "add_instrument_track"), Description("Add an instrument track with the given built-in kind (see list_instrument_kinds). Returns the track id.")]
    public Task<int> AddInstrumentTrack([Description("Instrument kind id (0..15)")] int kind) => Mutate(() => kind switch
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
        14 => E.AddPentadTrack(),
        15 => E.AddConsortTrack(),
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

    public sealed record PhysicalPartial(int Index, double Ratio, double Hz, double Amp, double RingSeconds, bool Audible);
    public sealed record PhysicalReading(string Summary, string Guide, int ActiveVoices, int MaxVoices, bool Mono,
        double LastNoteHz, double OutputPeakDb, string Res1Type, string Res2Type, bool Res2On, string Structure,
        int Res1Audible, int Res2Audible, PhysicalPartial[] Res1, PhysicalPartial[] Res2);

    [McpServerTool(Name = "read_physical"), Description(
        "Read a Nota Physical track (built-in instrument kind 2 — modal percussion: a mallet and/or filtered noise burst "
        + "strikes one or two tuned resonator banks). Returns a one-line summary of the patch, a guide to what each 0..1 "
        + "parameter value means (ids for set_instrument_param_by_id), the sounding voices (8 in Poly, 1 in Mono), the last "
        + "struck fundamental (Hz, 0 = none yet), the output peak (dBFS, ~300 ms hold; -120 = silence) and both resonators' "
        + "16 partials exactly as the engine tunes them: ratio to the note, Hz at the last note (or middle C before the first), "
        + "struck amplitude, ring time in seconds, and whether it sounds (below Nyquist, >= 2 % of the loudest). Res 2 only "
        + "sounds when Res2On; Structure 1>2 feeds res 1 into res 2, 1+2 strikes both; resmix balances res 1 against res 2 in either.")]
    public Task<PhysicalReading?> ReadPhysical(int trackId) => Read<PhysicalReading?>(() =>
    {
        if (E.TrackInstrumentKind(trackId) != PhysicalModel.Kind) return null;
        int pc = E.PluginParamCount(trackId, -1);
        var vals = new Dictionary<string, float>();
        for (int i = 0; i < pc; i++) vals[E.PluginParamId(trackId, -1, i)] = E.PluginParamGet(trackId, -1, i);
        float G(string id) => vals.TryGetValue(id, out var v) ? v : 0f;
        var sc = new float[PhysicalModel.ScopeLength];
        int n = E.InstrumentScope(trackId, sc);
        var scope = sc[..Math.Max(0, n)];
        double sr = E.SampleRate > 0 ? E.SampleRate : 48000;
        double f0 = n > 2 && sc[2] > 0 ? sc[2] : 261.63;
        PhysicalPartial[] Bank(int b, out int audible)
        {
            var modes = PhysicalModel.Bank(scope, b);
            double max = 1e-9;
            foreach (var m in modes) max = Math.Max(max, m.Amp);
            var list = new PhysicalPartial[modes.Length];
            audible = 0;
            for (int i = 0; i < modes.Length; i++)
            {
                var (r, a, tau) = modes[i];
                bool on = f0 * r < sr * 0.49 && a >= max * 0.02;
                if (on) audible++;
                list[i] = new PhysicalPartial(i, Math.Round(r, 4), Math.Round(f0 * r, 1), Math.Round(a, 3), Math.Round(tau, 3), on);
            }
            return list;
        }
        var r1 = Bank(0, out int a1);
        var r2 = Bank(1, out int a2);
        double peak = n > 3 ? sc[3] : 0;
        return new PhysicalReading(PhysicalModel.Summary(G), PhysicalModel.Guide,
            n > 0 ? (int)sc[0] : 0, PhysicalModel.Voices, G("mono") >= 0.5f,
            n > 2 ? Math.Round(sc[2], 2) : 0, peak > 1e-6 ? Math.Round(20 * Math.Log10(peak), 1) : -120,
            PhysicalModel.TypeNames[PhysicalModel.TypeIndex(G("r1type"))], PhysicalModel.TypeNames[PhysicalModel.TypeIndex(G("r2type"))],
            G("r2on") >= 0.5f, G("structure") < 0.5f ? "1>2 serial" : "1+2 parallel", a1, a2, r1, r2);
    });

    [McpServerTool(Name = "set_instrument_param_by_id"), Description(
        "Set a track instrument parameter by its stable id (e.g. \"cutoff\", \"pmoscb\") to a normalized value (0..1). "
        + "Ids never change between versions, unlike indices. Returns false if the instrument has no such parameter.")]
    public Task<bool> SetInstrumentParamById(int trackId, string paramId, [Description("Normalized 0..1")] float value) => Mutate(() =>
    {
        int pc = E.PluginParamCount(trackId, -1);
        for (int i = 0; i < pc; i++)
        {
            if (E.PluginParamId(trackId, -1, i) != paramId) continue;
            E.PluginParamSet(trackId, -1, i, Math.Clamp(value, 0f, 1f));
            return true;
        }
        return false;
    });
}
