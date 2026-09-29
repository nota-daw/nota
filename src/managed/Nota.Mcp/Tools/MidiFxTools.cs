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

    // ---- Nota Arp (kind 0) — named access to its globals and 16-step Groove lanes ----------
    // Param layout mirrors Arpeggiator.h: 13 globals, 7 lanes × 16 steps, then VelAmt / View.
    private const int GRate = 0, GSync = 1, GFreeRate = 2, GGate = 3, GOctaves = 4, GOctMode = 5, GOrder = 6, GSwing = 7,
                      GHold = 8, GRetrig = 9, GTranspose = 10, GLoop = 11, GLoopMode = 12, PVelAmt = 125, PView = 126, ArpSteps = 16;
    private static readonly string[] ArpRates = { "1/1", "1/2", "1/4", "1/8", "1/8T", "1/16", "1/16T", "1/32" };
    // Persisted Order values: 0 up, 1 down, 2 up-down, 3 converge, 4 as-played, 5 chord, 6 random, 7 down-up, 8 diverge.
    private static readonly string[] ArpOrders = { "up", "down", "up-down", "converge", "as-played", "chord", "random", "down-up", "diverge" };
    private static readonly string[] ArpOctModes = { "up", "down", "up-down", "random" };
    private static readonly string[] ArpRetrigs = { "off", "note", "beat" };
    private static readonly string[] ArpLoopModes = { "forward", "backward", "ping-pong", "random" };
    // lane → (first param, to display units, from display units)
    private static readonly Dictionary<string, (int Base, Func<float, float> To, Func<float, float> From)> ArpLanes = new()
    {
        ["velocity"]  = (13, v => MathF.Round(v * 127), d => Math.Clamp(d, 0, 127) / 127f),
        ["length"]    = (29, v => MathF.Round(v * 100), d => Math.Clamp(d, 0, 200) / 100f),
        ["chance"]    = (45, v => MathF.Round(v * 100), d => Math.Clamp(d, 0, 100) / 100f),
        ["ratchet"]   = (61, v => MathF.Round(v), d => Math.Clamp(MathF.Round(d), 1, 8)),
        ["transpose"] = (77, v => MathF.Round(v), d => Math.Clamp(MathF.Round(d), -24, 24)),
        ["on"]        = (93, v => v >= 0.5f ? 1 : 0, d => d >= 0.5f ? 1 : 0),
        ["cc"]        = (109, v => MathF.Round(v), d => Math.Clamp(MathF.Round(d), 0, 127)),
    };

    public sealed record ArpLanesDto(float[] Velocity, float[] Length, float[] Chance, float[] Ratchet, float[] Transpose, bool[] On, float[] Cc);
    public sealed record ArpState(
        string Order, bool Sync, string Rate, float FreeMs, float GatePercent, float SwingPercent, float VelAmtPercent,
        int Octaves, string OctaveMode, string Retrig, bool Hold, int Transpose, int Steps, string LoopMode, string View,
        ArpLanesDto Lanes, int CurrentStep, int[] Chord, bool Running);

    private void RequireArp(int trackId, int index)
    {
        if (E.MidiEffectKind(trackId, index) != 0 || E.MidiEffectParamCount(trackId, index) <= PView)
            throw new ArgumentException($"MIDI effect {index} on track {trackId} is not a Nota Arp.");
    }
    private static int Pick(string[] names, string value, string what)
    {
        int i = Array.FindIndex(names, n => string.Equals(n, value.Trim().Replace(' ', '-').Replace('_', '-'), StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : throw new ArgumentException($"Unknown {what} '{value}'. Use one of: {string.Join(", ", names)}.");
    }

    [McpServerTool(Name = "get_arp"), Description(
        "Read a Nota Arp (MIDI effect kind 0) in musical units: order, sync/rate or free ms, gate/swing/vel-amt %, octaves, "
        + "retrig, hold, transpose, steps, loop mode, card view, the 16-step Groove lanes (velocity 1..127, length %, chance %, "
        + "ratchet ×, transpose st, on) and live state (current step 0-based or -1, the chord it plays, running).")]
    public Task<ArpState> GetArp(int trackId, int index) => Read(() =>
    {
        RequireArp(trackId, index);
        float G(int p) => E.MidiEffectGetParam(trackId, index, p);
        int GI(int p) => (int)MathF.Round(G(p));
        float[] Lane(string n) { var l = ArpLanes[n]; var a = new float[ArpSteps]; for (int s = 0; s < ArpSteps; s++) a[s] = l.To(G(l.Base + s)); return a; }
        var scope = new float[40];
        int sn = E.MidiEffectScope(trackId, index, scope);
        int cn = sn >= 4 ? Math.Min((int)scope[3], sn - 4) : 0;
        var chord = new int[cn]; for (int i = 0; i < cn; i++) chord[i] = (int)scope[4 + i];
        return new ArpState(
            ArpOrders[Math.Clamp(GI(GOrder), 0, ArpOrders.Length - 1)], G(GSync) > 0.5f, ArpRates[Math.Clamp(GI(GRate), 0, 7)],
            MathF.Round(1000f / MathF.Max(0.1f, G(GFreeRate))), MathF.Round(G(GGate) * 100), MathF.Round(G(GSwing) * 50), MathF.Round(G(PVelAmt) * 100),
            GI(GOctaves), ArpOctModes[Math.Clamp(GI(GOctMode), 0, 3)], ArpRetrigs[Math.Clamp(GI(GRetrig), 0, 2)], G(GHold) > 0.5f,
            GI(GTranspose), GI(GLoop), ArpLoopModes[Math.Clamp(GI(GLoopMode), 0, 3)], G(PView) >= 0.5f ? "S" : "L",
            new ArpLanesDto(Lane("velocity"), Lane("length"), Lane("chance"), Lane("ratchet"), Lane("transpose"),
                            Array.ConvertAll(Lane("on"), v => v > 0.5f), Lane("cc")),
            sn >= 4 ? (int)scope[0] : -1, chord, sn >= 4 && scope[2] > 0);
    });

    [McpServerTool(Name = "set_arp"), Description(
        "Set a Nota Arp's globals by name; omit what you don't change. order: up | down | up-down | down-up | converge | diverge | "
        + "random | chord | as-played. rate: 1/1 1/2 1/4 1/8 1/8T 1/16 1/16T 1/32 (turns sync on); freeMs 20..1000 (turns sync off). "
        + "gatePercent 0..200, swingPercent 0..50, velAmtPercent 0..100 (how much the Groove velocity lane shapes velocity), "
        + "octaves 1..8, octaveMode up|down|up-down|random, retrig off|note|beat (beat = restart every bar), hold, "
        + "transpose −24..24 st, steps 1..16, loopMode forward|backward|ping-pong|random, view S|L (card size).")]
    public Task SetArp(int trackId, int index, string? order = null, string? rate = null, float? freeMs = null,
        float? gatePercent = null, float? swingPercent = null, float? velAmtPercent = null, int? octaves = null,
        string? octaveMode = null, string? retrig = null, bool? hold = null, int? transpose = null, int? steps = null,
        string? loopMode = null, string? view = null) => Mutate(() =>
    {
        RequireArp(trackId, index);
        void S(int p, float v) => E.MidiEffectSetParam(trackId, index, p, v);
        if (order is not null) S(GOrder, Pick(ArpOrders, order, "order"));
        if (rate is not null) { S(GRate, Pick(ArpRates, rate, "rate")); S(GSync, 1); }
        if (freeMs is { } ms) { S(GFreeRate, 1000f / Math.Clamp(ms, 20, 1000)); S(GSync, 0); }
        if (gatePercent is { } g) S(GGate, Math.Clamp(g, 0, 200) / 100f);
        if (swingPercent is { } sw) S(GSwing, Math.Clamp(sw, 0, 50) / 50f);
        if (velAmtPercent is { } va) S(PVelAmt, Math.Clamp(va, 0, 100) / 100f);
        if (octaves is { } o) S(GOctaves, Math.Clamp(o, 1, 8));
        if (octaveMode is not null) S(GOctMode, Pick(ArpOctModes, octaveMode, "octave mode"));
        if (retrig is not null) S(GRetrig, Pick(ArpRetrigs, retrig, "retrig"));
        if (hold is { } h) S(GHold, h ? 1 : 0);
        if (transpose is { } tr) S(GTranspose, Math.Clamp(tr, -24, 24));
        if (steps is { } st) S(GLoop, Math.Clamp(st, 1, 16));
        if (loopMode is not null) S(GLoopMode, Pick(ArpLoopModes, loopMode, "loop mode"));
        if (view is not null) S(PView, view.Trim().Equals("S", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
    });

    [McpServerTool(Name = "set_arp_lane"), Description(
        "Write a Nota Arp Groove lane from step 1 (up to 16 values, extra ignored). lane: velocity (1..127), length (% of the step, "
        + "5..200), chance (%), ratchet (1..8 repeats), transpose (semitones ±24), on (1 = play, 0 = muted), cc (0..127, Map/CC).")]
    public Task SetArpLane(int trackId, int index, string lane, float[] values) => Mutate(() =>
    {
        RequireArp(trackId, index);
        if (!ArpLanes.TryGetValue(lane.Trim().ToLowerInvariant(), out var l))
            throw new ArgumentException($"Unknown lane '{lane}'. Use one of: {string.Join(", ", ArpLanes.Keys)}.");
        for (int s = 0; s < values.Length && s < ArpSteps; s++) E.MidiEffectSetParam(trackId, index, l.Base + s, l.From(values[s]));
    });

    [McpServerTool(Name = "set_arp_step"), Description(
        "Edit one Nota Arp step (1..16) across lanes; omit what you don't change. Units as in set_arp_lane; on = false mutes the step.")]
    public Task SetArpStep(int trackId, int index, int step, float? velocity = null, float? length = null, float? chance = null,
        int? ratchet = null, int? transpose = null, bool? on = null) => Mutate(() =>
    {
        RequireArp(trackId, index);
        int s = Math.Clamp(step, 1, ArpSteps) - 1;
        void L(string n, float? v) { if (v is { } x) { var l = ArpLanes[n]; E.MidiEffectSetParam(trackId, index, l.Base + s, l.From(x)); } }
        L("velocity", velocity); L("length", length); L("chance", chance);
        L("ratchet", ratchet); L("transpose", transpose); L("on", on is { } b ? (b ? 1 : 0) : null);
    });

    [McpServerTool(Name = "restart_arp"), Description("Restart a Nota Arp's pattern from step 1 (like its Restart button).")]
    public Task RestartArp(int trackId, int index) => Mutate(() => { RequireArp(trackId, index); E.MidiEffectCommand(trackId, index, 1); });

    // ---- Nota Chord (kind 1) — named access to its six shifts and globals ------------------
    // Param layout mirrors MidiChord.h: Voice 1..6 (0..5), Strum 6, Keep Root 7, Spread 8, Fold 9,
    // Vel 1..6 (10..15), On 1..6 (16..21), Fold Key 22, Fold Mode 23, View 24.
    private const int CVoice = 0, CStrum = 6, CKeep = 7, CSpread = 8, CFold = 9, CVel = 10, COn = 16, CKey = 22, CMode = 23, CView = 24, CSlots = 6;
    private static readonly (string Name, int[] Shifts)[] ChordTypes =
    {
        ("maj7", new[] { 4, 7, 11 }), ("min7", new[] { 3, 7, 10 }), ("sus4", new[] { 5, 7 }), ("5th", new[] { 7 }),
        ("major", new[] { 4, 7 }), ("minor", new[] { 3, 7 }), ("dom7", new[] { 4, 7, 10 }), ("sus2", new[] { 2, 7 }),
        ("dim7", new[] { 3, 6, 9 }), ("m7b5", new[] { 3, 6, 10 }), ("aug", new[] { 4, 8 }), ("6", new[] { 4, 7, 9 }),
        ("m6", new[] { 3, 7, 9 }), ("power", new[] { 7, 12 }), ("octave", new[] { 12 }),
    };
    private static readonly string[] ChordKeys = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public sealed record ChordShift(int Slot, bool On, int Semitones, int Velocity);
    public sealed record ChordState(
        string Type, ChordShift[] Shifts, float StrumMs, float SpreadPercent, bool KeepRoot, bool Fold, string FoldKey, string FoldMode,
        string View, int LastRoot, int HeldKeys, int[] LastChord, int SoundedNotes);

    private void RequireChord(int trackId, int index)
    {
        if (E.MidiEffectKind(trackId, index) != 1 || E.MidiEffectParamCount(trackId, index) <= CView)
            throw new ArgumentException($"MIDI effect {index} on track {trackId} is not a Nota Chord.");
    }

    [McpServerTool(Name = "get_chord"), Description(
        "Read a Nota Chord (MIDI effect kind 1): the matching type (maj7 / min7 / sus4 / 5th … or custom), the six shifts "
        + "(slot 1..6, on, semitones −12..+12, velocity offset −64..+63 MIDI units), strum ms, spread %, keep root, fold into "
        + "a scale (key + major/minor), card view, and live state: the last root played (MIDI note, -1 none), keys held now, "
        + "the last chord it produced (MIDI notes low → high) and how many of them have sounded so far (the strum's progress).")]
    public Task<ChordState> GetChord(int trackId, int index) => Read(() =>
    {
        RequireChord(trackId, index);
        float G(int p) => E.MidiEffectGetParam(trackId, index, p);
        int GI(int p) => (int)MathF.Round(G(p));
        var shifts = new ChordShift[CSlots];
        for (int s = 0; s < CSlots; s++) shifts[s] = new ChordShift(s + 1, G(COn + s) >= 0.5f, GI(CVoice + s), GI(CVel + s));
        var active = shifts.Where(x => x.On).Select(x => x.Semitones).OrderBy(x => x).ToArray();
        string type = ChordTypes.FirstOrDefault(t => t.Shifts.OrderBy(x => x).SequenceEqual(active)).Name ?? "custom";
        var scope = new float[18];
        int sn = E.MidiEffectScope(trackId, index, scope);
        int n = sn >= 4 ? Math.Min((int)scope[3], (sn - 4) / 2) : 0;
        var last = new int[n]; for (int i = 0; i < n; i++) last[i] = (int)scope[4 + i];
        return new ChordState(type, shifts, G(CStrum), G(CSpread), G(CKeep) >= 0.5f, G(CFold) >= 0.5f,
            ChordKeys[((GI(CKey)) % 12 + 12) % 12], G(CMode) >= 0.5f ? "minor" : "major", G(CView) >= 0.5f ? "S" : "L",
            sn >= 4 ? (int)scope[0] : -1, sn >= 4 ? (int)scope[1] : 0, last, sn >= 4 ? (int)scope[2] : 0);
    });

    [McpServerTool(Name = "set_chord"), Description(
        "Set a Nota Chord's globals by name; omit what you don't change. type sets the shifts from a chord type (and clears "
        + "their velocity offsets): maj7 min7 sus4 5th major minor dom7 sus2 dim7 m7b5 aug 6 m6 power octave. shifts: up to six "
        + "semitone offsets (−12..+12) — listed ones are switched on in order, the rest off (overrides type). strumMs 0..100 "
        + "(chord note-ons low → high this far apart), spreadPercent 0..100 (≥34 lifts every other shift an octave, ≥67 all), "
        + "keepRoot (play the pressed note too), fold + foldKey (C..B) + foldMode (major|minor): added notes outside the "
        + "scale drop a semitone into it. view S|L (card size).")]
    public Task SetChord(int trackId, int index, string? type = null, int[]? shifts = null, float? strumMs = null, float? spreadPercent = null,
        bool? keepRoot = null, bool? fold = null, string? foldKey = null, string? foldMode = null, string? view = null) => Mutate(() =>
    {
        RequireChord(trackId, index);
        void S(int p, float v) => E.MidiEffectSetParam(trackId, index, p, v);
        int[]? iv = shifts;
        if (iv is null && type is not null)
        {
            var t = ChordTypes.FirstOrDefault(c => string.Equals(c.Name, type.Trim(), StringComparison.OrdinalIgnoreCase));
            iv = t.Shifts ?? throw new ArgumentException($"Unknown chord type '{type}'. Use one of: {string.Join(", ", ChordTypes.Select(c => c.Name))}.");
            for (int s = 0; s < CSlots; s++) S(CVel + s, 0);
        }
        if (iv is not null)
        {
            if (iv.Length > CSlots) throw new ArgumentException("At most six shifts.");
            for (int s = 0; s < CSlots; s++) { bool on = s < iv.Length; S(CVoice + s, on ? Math.Clamp(iv[s], -12, 12) : 0); S(COn + s, on ? 1 : 0); }
        }
        if (strumMs is { } st) S(CStrum, Math.Clamp(st, 0, 100));
        if (spreadPercent is { } sp) S(CSpread, Math.Clamp(sp, 0, 100));
        if (keepRoot is { } k) S(CKeep, k ? 1 : 0);
        if (fold is { } f) S(CFold, f ? 1 : 0);
        if (foldKey is not null)
        {
            int ki = Array.FindIndex(ChordKeys, x => string.Equals(x, foldKey.Trim(), StringComparison.OrdinalIgnoreCase));
            if (ki < 0) throw new ArgumentException($"Unknown key '{foldKey}'. Use one of: {string.Join(" ", ChordKeys)}.");
            S(CKey, ki);
        }
        if (foldMode is not null) S(CMode, foldMode.Trim().StartsWith("min", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        if (view is not null) S(CView, view.Trim().Equals("S", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
    });

    [McpServerTool(Name = "set_chord_shift"), Description(
        "Edit one Nota Chord shift (slot 1..6); omit what you don't change. semitones −12..+12 (setting it switches the slot on "
        + "unless on is given), on, velocity offset −64..+63 (MIDI units added to the played velocity).")]
    public Task SetChordShift(int trackId, int index, int slot, int? semitones = null, bool? on = null, int? velocity = null) => Mutate(() =>
    {
        RequireChord(trackId, index);
        if (slot is < 1 or > CSlots) throw new ArgumentException("slot must be 1..6.");
        int s = slot - 1;
        void S(int p, float v) => E.MidiEffectSetParam(trackId, index, p, v);
        if (semitones is { } st) { S(CVoice + s, Math.Clamp(st, -12, 12)); if (on is null) S(COn + s, 1); }
        if (on is { } o) S(COn + s, o ? 1 : 0);
        if (velocity is { } v) S(CVel + s, Math.Clamp(v, -64, 63));
    });
}
