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

    public sealed record PendulumBall(int Index, double Phase, double Position, string Direction, string Note,
        double SecondsToWall, double RatePercent, bool JustFired);
    public sealed record PendulumStep(int Step, string Note, string Bar);
    public sealed record PendulumReading(string Summary, string Guide, int ActiveVoices, int MaxVoices, string[] HeldChord,
        string LastNote, double BarPosition, int StepsPerBar, double SwingSeconds, bool TransportRolling, bool Live,
        PendulumBall[] Balls, PendulumStep[] BarNotes);

    [McpServerTool(Name = "read_pendulum"), Description(
        "Read a Nota Pendulum track (built-in instrument kind 8 — generative keys: hold a chord and 2-6 balls swing across it; "
        + "x = pitch, so each chord degree a ball crosses plays that note, weaving an arpeggio). Returns a one-line summary of the "
        + "patch, a guide to what each 0..1 parameter value means (ids for set_instrument_param_by_id, or use set_pendulum), the "
        + "sounding voices, the held chord (low to high), the last generated note, where the bar is (0..1) and the live balls exactly "
        + "as the audio thread swings them: phase, position 0 (low wall) .. 1 (high wall), direction, the note it sits on, seconds "
        + "to the wall ahead (-1 = stopped), its own rate (% of full speed, negative = reverse) and whether it just fired. BarNotes "
        + "lists the generated notes on the bar's 1/16 grid (Bar = this or last).")]
    public Task<PendulumReading?> ReadPendulum(int trackId) => Read<PendulumReading?>(() =>
    {
        if (E.TrackInstrumentKind(trackId) != PendulumModel.Kind) return null;
        var g = PendulumGetter(trackId);
        var sc = new float[PendulumModel.ScopeLength];
        int n = E.InstrumentScope(trackId, sc);
        var s = new PendulumModel.Snapshot();
        PendulumModel.Parse(sc.AsSpan(0, Math.Max(0, n)), s);
        var heldBuf = new int[16];
        int hn = E.InstrumentHeldNotes(trackId, heldBuf);
        var held = new string[hn];
        for (int i = 0; i < hn; i++) held[i] = PendulumModel.NoteName(heldBuf[i]);
        int count = PendulumModel.BallCount(g("balls"));
        var balls = new PendulumBall[count];
        for (int b = 0; b < count; b++)
        {
            var x = s.BallState[b];
            balls[b] = new PendulumBall(b, Math.Round(x.Phase, 3), Math.Round(x.Pos, 3), x.Dir > 0 ? "up" : x.Dir < 0 ? "down" : "still",
                PendulumModel.NoteName(x.Pitch), x.ToWall < 0 ? -1 : Math.Round(x.ToWall, 3), Math.Round(x.Rate * 100, 1), x.Pitch >= 0 && x.Since < 0.16);
        }
        var steps = new List<PendulumStep>();
        for (int i = 0; i < s.StepCount; i++)
            if (s.Steps[i].Pitch >= 0) steps.Add(new PendulumStep(i, PendulumModel.NoteName(s.Steps[i].Pitch), s.Steps[i].Age == 0 ? "this" : "last"));
        return new PendulumReading(PendulumModel.Summary(g), PendulumModel.Guide, s.Voices, PendulumModel.Voices, held,
            PendulumModel.NoteName(s.LastPitch), Math.Round(s.BarPos, 3), s.StepCount, Math.Round(s.SwingSeconds, 3), s.Playing, s.Live,
            balls, steps.ToArray());
    });

    [McpServerTool(Name = "set_pendulum"), Description(
        "Shape a Nota Pendulum track (kind 8) in musical terms; every argument is optional and only the given ones change. "
        + "balls 2..6; ratePercent -100..100 (0 stops, negative reverses); sync true = a swing takes a note division (division "
        + "\"1/1\", \"1/2\", \"1/4\", \"1/8\", \"1/16\"), false = freeSeconds 0.1..4 per swing; motion Linear | Pendulum | Ease | Bounce; "
        + "quantize Off | 1/16 | 1/8; sort up | down; spreadPercent 0..100 (the balls drift apart); hold / firstNote on/off; "
        + "wave Keys | Glass | Saw | Sqr | Bell; scale \"Off\" or \"<root> <mode>\" with mode major | minor | dorian | mixolydian | "
        + "minor penta (e.g. \"A minor penta\"); restart = restart every ball now. Returns the new summary, or an error line naming "
        + "the value it could not use. Voice / envelope / spread details: set_instrument_param_by_id with the read_pendulum guide.")]
    public Task<string> SetPendulum(int trackId, int? balls = null, double? ratePercent = null, bool? sync = null, string? division = null,
        double? freeSeconds = null, string? motion = null, string? quantize = null, string? sort = null, double? spreadPercent = null,
        bool? hold = null, bool? firstNote = null, string? wave = null, string? scale = null, bool? restart = null) => Mutate(() =>
    {
        if (E.TrackInstrumentKind(trackId) != PendulumModel.Kind) return $"error: track {trackId} is not a Nota Pendulum";
        int pc = E.PluginParamCount(trackId, -1);
        var ids = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) ids[E.PluginParamId(trackId, -1, i)] = i;
        void Set(string id, double v) { if (ids.TryGetValue(id, out var i)) E.PluginParamSet(trackId, -1, i, (float)Math.Clamp(v, 0, 1)); }
        static int Find(string[] names, string? v) => v is null ? -1 : Array.FindIndex(names, n => string.Equals(n, v.Trim(), StringComparison.OrdinalIgnoreCase));
        var errors = new List<string>();

        if (balls is { } bc) Set("balls", PendulumModel.BallCountNorm(bc));
        if (ratePercent is { } rp) Set("rate", 0.5 + Math.Clamp(rp, -100, 100) / 200.0);
        if (sync is { } sy) Set("sync", sy ? 1 : 0);
        if (division is not null) { int d = Find(PendulumModel.DivNames, division); if (d < 0) errors.Add($"division '{division}'"); else { Set("division", d / 4.0); Set("sync", 1); } }
        if (freeSeconds is { } fs) { Set("freerate", Math.Log(Math.Clamp(fs, 0.1, 4.0) / 0.1) / Math.Log(40.0)); if (sync is null) Set("sync", 0); }
        if (motion is not null) { int m = Find(PendulumModel.MotionNames, motion); if (m < 0) errors.Add($"motion '{motion}'"); else Set("motion", m / 3.0); }
        if (quantize is not null) { int q = Find(PendulumModel.QuantNames, quantize); if (q < 0) errors.Add($"quantize '{quantize}'"); else Set("quantize", q / 2.0); }
        if (sort is not null)
        {
            if (string.Equals(sort, "up", StringComparison.OrdinalIgnoreCase)) Set("chordsort", 0);
            else if (string.Equals(sort, "down", StringComparison.OrdinalIgnoreCase)) Set("chordsort", 1);
            else errors.Add($"sort '{sort}'");
        }
        if (spreadPercent is { } sp) Set("spread", sp / 100.0);
        if (hold is { } h) Set("hold", h ? 1 : 0);
        if (firstNote is { } fn) Set("firstnote", fn ? 1 : 0);
        if (wave is not null) { int w = Find(PendulumModel.WaveNames, wave); if (w < 0) errors.Add($"wave '{wave}'"); else Set("wave", w / 4.0); }
        if (scale is not null)
        {
            var t = scale.Trim();
            if (string.Equals(t, "off", StringComparison.OrdinalIgnoreCase)) Set("scalemode", 0);
            else
            {
                int sp2 = t.IndexOf(' ');
                int root = sp2 > 0 ? Find(PendulumModel.RootNames, t[..sp2]) : -1;
                int mode = sp2 > 0 ? Find(PendulumModel.ModeNames, t[(sp2 + 1)..]) : -1;
                if (root < 0 || mode <= 0) errors.Add($"scale '{scale}'");
                else { Set("root", root / 11.0); Set("scalemode", mode / (double)(PendulumModel.ModeNames.Length - 1)); }
            }
        }
        if (restart == true) Set("reset", 1);
        string summary = PendulumModel.Summary(PendulumGetter(trackId));
        return errors.Count == 0 ? summary : $"error: could not use {string.Join(", ", errors)} · {summary}";
    });

    private Func<string, float> PendulumGetter(int trackId)
    {
        int pc = E.PluginParamCount(trackId, -1);
        var vals = new Dictionary<string, float>();
        for (int i = 0; i < pc; i++) vals[E.PluginParamId(trackId, -1, i)] = E.PluginParamGet(trackId, -1, i);
        return id => vals.TryGetValue(id, out var v) ? v : 0f;
    }

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
