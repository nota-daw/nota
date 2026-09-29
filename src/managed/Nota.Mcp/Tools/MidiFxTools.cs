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

    // ---- Nota Scale (kind 2) — named access ---------------------------------------------------
    // Param layout mirrors MidiScale.h: Root 0, Scale 1, Transpose 2, Mask 0..11 = 3..14, Fold 15,
    // Follow Key 16, Range Low 17, Range High 18, Learn 19, View 20.
    private const int SRoot = 0, SScale = 1, STrans = 2, SMask = 3, SFold = 15, SFollow = 16, SLo = 17, SHi = 18, SLearn = 19, SView = 20, SCustom = 10;
    private static readonly string[] ScaleIds = { "major", "minor", "harmonic-minor", "dorian", "phrygian", "lydian", "mixolydian", "penta", "penta-minor", "chromatic", "custom" };
    private static readonly int[] ScaleMasks = { 2741, 1453, 2477, 1709, 1451, 2773, 1717, 661, 1193, 4095 };
    private static readonly string[] ScaleFolds = { "nearest", "down", "up" };
    private static readonly string[] PcNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public sealed record ScaleState(
        string Root, string Scale, int[] Degrees, string[] Notes, string Fold, int Transpose, int RangeLow, int RangeHigh,
        bool FollowKey, bool Learning, string View, int LastIn, int LastOut, string[] FoldMap, int[] HeldPitchClasses);

    private void RequireScale(int trackId, int index)
    {
        if (E.MidiEffectKind(trackId, index) != 2 || E.MidiEffectParamCount(trackId, index) <= SView)
            throw new ArgumentException($"MIDI effect {index} on track {trackId} is not a Nota Scale.");
    }
    private static int ParsePc(string s)
    {
        string t = s.Trim().Replace("♯", "#").Replace("♭", "b");
        int i = Array.FindIndex(PcNames, x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) return i;
        string[] flats = { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };
        i = Array.FindIndex(flats, x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) return i;
        throw new ArgumentException($"Unknown note '{s}'. Use C, C#/Db … B.");
    }
    private int ScaleMask(int trackId, int index)
    {
        int sc = Math.Clamp((int)MathF.Round(E.MidiEffectGetParam(trackId, index, SScale)), 0, SCustom);
        if (sc < SCustom) return ScaleMasks[sc];
        int m = 0; for (int i = 0; i < 12; i++) if (E.MidiEffectGetParam(trackId, index, SMask + i) >= 0.5f) m |= 1 << i;
        return m;
    }
    // Put the scale in effect into the Mask params and switch to Custom, so single notes can be edited.
    private void ScaleMakeCustom(int trackId, int index)
    {
        if ((int)MathF.Round(E.MidiEffectGetParam(trackId, index, SScale)) >= SCustom) return;
        int m = ScaleMask(trackId, index);
        for (int i = 0; i < 12; i++) E.MidiEffectSetParam(trackId, index, SMask + i, (m >> i) & 1);
        E.MidiEffectSetParam(trackId, index, SScale, SCustom);
    }

    [McpServerTool(Name = "get_scale"), Description(
        "Read a Nota Scale (MIDI effect kind 2): root (C..B), scale (major minor harmonic-minor dorian phrygian lydian "
        + "mixolydian penta penta-minor chromatic custom), its degrees (semitones above the root) and note names, fold "
        + "(nearest | down | up: where an out-of-scale note goes), transpose (st, after snapping), the range (MIDI notes; "
        + "outside it notes pass through), followKey (the root follows what is played), learning (Learn mode on), card view, "
        + "and live state: the last note in / out (MIDI, -1 none), foldMap (for C..B what each pitch class plays as) and "
        + "the pitch classes held now.")]
    public Task<ScaleState> GetScale(int trackId, int index) => Read(() =>
    {
        RequireScale(trackId, index);
        float G(int p) => E.MidiEffectGetParam(trackId, index, p);
        int GI(int p) => (int)MathF.Round(G(p));
        int root = ((GI(SRoot)) % 12 + 12) % 12, mask = ScaleMask(trackId, index), fold = Math.Clamp(GI(SFold), 0, 2);
        var deg = Enumerable.Range(0, 12).Where(d => (mask >> d & 1) != 0).ToArray();
        var map = new string[12];
        for (int pc = 0; pc < 12; pc++)
        {
            int rel = ((pc - root) % 12 + 12) % 12, d = 0;
            bool Has(int x) => (mask >> ((x % 12 + 12) % 12) & 1) != 0;
            if (mask != 0 && !Has(rel))
            {
                if (fold == 1) { for (int k = 1; k < 12; k++) if (Has(rel - k)) { d = -k; break; } }
                else if (fold == 2) { for (int k = 1; k < 12; k++) if (Has(rel + k)) { d = k; break; } }
                else { for (int k = 1; k < 7; k++) { if (Has(rel + k)) { d = k; break; } if (Has(rel - k)) { d = -k; break; } } }
            }
            map[pc] = $"{PcNames[pc]}→{PcNames[((pc + d) % 12 + 12) % 12]}";
        }
        var scope = new float[32];
        int n = E.MidiEffectScope(trackId, index, scope);
        int held = n >= 6 ? (int)scope[4] : 0;
        return new ScaleState(PcNames[root], ScaleIds[Math.Clamp(GI(SScale), 0, SCustom)], deg, deg.Select(d => PcNames[(root + d) % 12]).ToArray(),
            ScaleFolds[fold], GI(STrans), GI(SLo), GI(SHi), G(SFollow) >= 0.5f, G(SLearn) >= 0.5f, G(SView) >= 0.5f ? "S" : "L",
            E.MidiEffectLastIn(trackId, index), E.MidiEffectLastOut(trackId, index), map,
            Enumerable.Range(0, 12).Where(pc => (held >> pc & 1) != 0).ToArray());
    });

    [McpServerTool(Name = "set_scale"), Description(
        "Set a Nota Scale; omit what you don't change. root C..B (or Db-style flats). scale: major minor harmonic-minor dorian "
        + "phrygian lydian mixolydian penta penta-minor chromatic custom. degrees: semitones above the root (0..11) — sets a "
        + "Custom scale (overrides scale). notes: note names (e.g. [\"D\",\"F\",\"A\"]) — a Custom scale of those pitch classes, "
        + "relative to the root. fold nearest|down|up. transpose −24..+24 st. rangeLow / rangeHigh MIDI notes 0..127 (only "
        + "notes inside are snapped). followKey: the root follows the key of what's played. learn: true starts Learn (played "
        + "notes pass through and become a fresh Custom scale), false ends it. view S|L (card size).")]
    public Task SetScale(int trackId, int index, string? root = null, string? scale = null, int[]? degrees = null, string[]? notes = null,
        string? fold = null, int? transpose = null, int? rangeLow = null, int? rangeHigh = null, bool? followKey = null, bool? learn = null,
        string? view = null) => Mutate(() =>
    {
        RequireScale(trackId, index);
        void S(int p, float v) => E.MidiEffectSetParam(trackId, index, p, v);
        if (root is not null) S(SRoot, ParsePc(root));
        if (scale is not null)
        {
            int si = Array.FindIndex(ScaleIds, x => string.Equals(x, scale.Trim().Replace(' ', '-'), StringComparison.OrdinalIgnoreCase));
            if (si < 0) throw new ArgumentException($"Unknown scale '{scale}'. Use one of: {string.Join(", ", ScaleIds)}.");
            if (si == SCustom) ScaleMakeCustom(trackId, index); else S(SScale, si);
        }
        int[]? rel = degrees;
        if (rel is null && notes is not null)
        {
            int r = ((int)MathF.Round(E.MidiEffectGetParam(trackId, index, SRoot)) % 12 + 12) % 12;
            rel = notes.Select(nm => ((ParsePc(nm) - r) % 12 + 12) % 12).ToArray();
        }
        if (rel is not null)
        {
            for (int d = 0; d < 12; d++) S(SMask + d, rel.Any(x => ((x % 12) + 12) % 12 == d) ? 1 : 0);
            S(SScale, SCustom);
        }
        if (fold is not null)
        {
            int fi = Array.FindIndex(ScaleFolds, x => string.Equals(x, fold.Trim(), StringComparison.OrdinalIgnoreCase));
            if (fi < 0) throw new ArgumentException("fold must be nearest, down or up.");
            S(SFold, fi);
        }
        if (transpose is { } t) S(STrans, Math.Clamp(t, -24, 24));
        if (rangeLow is { } lo) S(SLo, Math.Clamp(lo, 0, 127));
        if (rangeHigh is { } hi) S(SHi, Math.Clamp(hi, 0, 127));
        if (followKey is { } fk) S(SFollow, fk ? 1 : 0);
        if (learn is { } ln) S(SLearn, ln ? 1 : 0);
        if (view is not null) S(SView, view.Trim().Equals("S", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
    });

    [McpServerTool(Name = "set_scale_note"), Description(
        "Add a note to or take it out of a Nota Scale (like clicking the note map): note is a name C..B (absolute pitch "
        + "class); on true adds it, false removes it. The scale in effect is copied and turns Custom.")]
    public Task SetScaleNote(int trackId, int index, string note, bool on) => Mutate(() =>
    {
        RequireScale(trackId, index);
        ScaleMakeCustom(trackId, index);
        int r = ((int)MathF.Round(E.MidiEffectGetParam(trackId, index, SRoot)) % 12 + 12) % 12;
        E.MidiEffectSetParam(trackId, index, SMask + ((ParsePc(note) - r) % 12 + 12) % 12, on ? 1 : 0);
    });

    // ---- Nota Length (kind 3) — named access in musical units --------------------------------
    // Param layout mirrors MidiNoteLength.h: Rate 0 / Gate 1 (legacy), Mode 2, Ms 3, Percent 4, Trigger 5,
    // Vel to Len 6, Key to Len 7, Random 8, Legato 9, Clip Limit 10, Division 11, View 12.
    private const int LGate = 1, LMode = 2, LMs = 3, LPct = 4, LTrig = 5, LVel = 6, LKey = 7, LRnd = 8, LLegato = 9, LClip = 10, LDiv = 11, LView = 12;
    private static readonly string[] LenModes = { "sync", "ms", "gate" };
    private static readonly string[] LenDivs = { "1/32", "1/16", "1/8", "1/4", "1/2", "1/1", "1/8.", "1/4T" };
    private static readonly double[] LenDivBeats = { 0.125, 0.25, 0.5, 1, 2, 4, 0.75, 2.0 / 3.0 };

    public sealed record LengthLastNote(int InPitch, float InVelocity, float HeldMs, int OutPitch, float OutMs);
    public sealed record LengthState(
        string Mode, string Division, float Ms, float GatePercent, float BaseLengthMs, string StartFrom,
        float VelToLenPercent, float KeyToLenPercent, float RandomPercent, bool Legato, bool ClipLimit, string View,
        LengthLastNote? LastNote, int Sounding, int Finished);

    private void RequireLength(int trackId, int index)
    {
        if (E.MidiEffectKind(trackId, index) != 3 || E.MidiEffectParamCount(trackId, index) <= LView)
            throw new ArgumentException($"MIDI effect {index} on track {trackId} is not a Nota Length.");
    }

    [McpServerTool(Name = "get_length"), Description(
        "Read a Nota Length (MIDI effect kind 3) in musical units: mode (sync | ms | gate), the sync division, the fixed ms, "
        + "the gate % (of how long each note was held), the resulting base length in ms at the current tempo (gate mode: of a "
        + "one-beat note), startFrom (note-on | note-off), the bipolar modifiers velToLenPercent / keyToLenPercent (−100..+100: "
        + "+ makes loud / low notes longer), randomPercent (± spread per note), legato, clipLimit (never cross the bar line), "
        + "card view, and live telemetry: the last note in (pitch, velocity 0..1, held ms or -1 while held) and out (pitch, "
        + "actual forced length ms), notes sounding now and notes finished so far.")]
    public Task<LengthState> GetLength(int trackId, int index) => Read(() =>
    {
        RequireLength(trackId, index);
        float G(int p) => E.MidiEffectGetParam(trackId, index, p);
        int mode = Math.Clamp((int)MathF.Round(G(LMode)), 0, 2), div = Math.Clamp((int)MathF.Round(G(LDiv)), 0, LenDivs.Length - 1);
        double msPerBeat = 60000.0 / (E.Bpm > 0 ? E.Bpm : 120);
        double baseMs = mode switch
        {
            1 => G(LMs),
            2 => msPerBeat * G(LPct) / 100.0,
            _ => LenDivBeats[div] * Math.Clamp(G(LGate), 0.05f, 2f) * msPerBeat,
        };
        var sc = new float[8];
        int sn = E.MidiEffectScope(trackId, index, sc);
        LengthLastNote? last = sn >= 5 && sc[0] >= 0
            ? new LengthLastNote((int)sc[0], sc[1], sc[2] < 0 ? -1 : (float)Math.Round(sc[2] * msPerBeat), (int)sc[3], (float)Math.Round(sc[4] * msPerBeat))
            : null;
        return new LengthState(LenModes[mode], LenDivs[div], G(LMs), G(LPct), (float)Math.Round(baseMs, 1), G(LTrig) >= 0.5f ? "note-off" : "note-on",
            MathF.Round(G(LVel) * 100), MathF.Round(G(LKey) * 100), MathF.Round(G(LRnd) * 100), G(LLegato) >= 0.5f, G(LClip) >= 0.5f,
            G(LView) >= 0.5f ? "S" : "L", last, sn >= 6 ? (int)sc[5] : 0, sn >= 7 ? (int)sc[6] : 0);
    });

    [McpServerTool(Name = "set_length"), Description(
        "Set a Nota Length's params by name; omit what you don't change. mode: sync | ms | gate. division: 1/32 1/16 1/8 1/4 1/2 "
        + "1/1 1/8. 1/4T (turns mode to sync unless mode is given); ms 10..2000 (turns mode to ms); gatePercent 10..200 of the "
        + "held length (turns mode to gate). startFrom: note-on | note-off (the forced note fires when the key is released). "
        + "velToLenPercent / keyToLenPercent −100..+100 (+ = loud / low notes longer, − = the opposite), randomPercent 0..100, "
        + "legato (each note lasts until the next starts), clipLimit (no note crosses the bar line it started in), view S|L.")]
    public Task SetLength(int trackId, int index, string? mode = null, string? division = null, float? ms = null, float? gatePercent = null,
        string? startFrom = null, float? velToLenPercent = null, float? keyToLenPercent = null, float? randomPercent = null,
        bool? legato = null, bool? clipLimit = null, string? view = null) => Mutate(() =>
    {
        RequireLength(trackId, index);
        void S(int p, float v) => E.MidiEffectSetParam(trackId, index, p, v);
        if (division is not null)
        {
            string d = division.Trim().Replace("D", ".", StringComparison.OrdinalIgnoreCase).Replace("t", "T");
            int di = Array.FindIndex(LenDivs, x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase));
            if (di < 0) throw new ArgumentException($"Unknown division '{division}'. Use one of: {string.Join(" ", LenDivs)}.");
            S(LDiv, di); S(LGate, 1); S(LMode, 0);
        }
        if (ms is { } m) { S(LMs, Math.Clamp(m, 10, 2000)); S(LMode, 1); }
        if (gatePercent is { } g) { S(LPct, Math.Clamp(g, 10, 200)); S(LMode, 2); }
        if (mode is not null)
        {
            string mm = mode.Trim().ToLowerInvariant();
            int mi = mm.StartsWith("gate", StringComparison.Ordinal) || mm == "%" ? 2 : Array.IndexOf(LenModes, mm);
            if (mi < 0) throw new ArgumentException($"Unknown mode '{mode}'. Use one of: sync, ms, gate.");
            S(LMode, mi);
        }
        if (startFrom is not null)
        {
            string sf = startFrom.Trim().ToLowerInvariant().Replace(" ", "-").Replace("_", "-");
            if (sf is not ("note-on" or "on" or "note-off" or "off")) throw new ArgumentException("startFrom must be note-on or note-off.");
            S(LTrig, sf.EndsWith("off", StringComparison.Ordinal) ? 1 : 0);
        }
        if (velToLenPercent is { } v) S(LVel, Math.Clamp(v, -100, 100) / 100f);
        if (keyToLenPercent is { } k) S(LKey, Math.Clamp(k, -100, 100) / 100f);
        if (randomPercent is { } r) S(LRnd, Math.Clamp(r, 0, 100) / 100f);
        if (legato is { } l) S(LLegato, l ? 1 : 0);
        if (clipLimit is { } c) S(LClip, c ? 1 : 0);
        if (view is not null) S(LView, view.Trim().Equals("S", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
    });

    // ---- Nota Random (kind 5) — named access in musical units --------------------------------
    // Param layout mirrors MidiRandom.h: Chance 0, Note Range 1, Vel Amt 2, Time Amt 3, Skip 4, Oct Amt 5,
    // Dist 6, Rate 7, Stay In Scale 8, Locked 9, Seed 10, View 11, Lock Bar 12.
    private const int RChance = 0, RNote = 1, RVel = 2, RTime = 3, RSkip = 4, ROct = 5, RDist = 6, RRate = 7, RScale = 8,
                      RLocked = 9, RSeed = 10, RView = 11, RLockBar = 12;
    private static readonly string[] RandDists = { "gauss", "even", "walk" };

    public sealed record RandomLastNote(int InPitch, float InVelocity, int OutPitch, float OutVelocity, float DelayMs, bool Skipped);
    public sealed record RandomState(
        float ChancePercent, int NoteRangeSemitones, int VelocityRange, float TimingMaxMs, float SkipPercent, int OctaveRange,
        string Distribution, string Rate, bool StayInScale, int Seed, bool Locked, int LockBar, string View,
        int Bar, int NotesIn, int Changed, int Skipped, int Queued, RandomLastNote? LastNote);

    private void RequireRandom(int trackId, int index)
    {
        if (E.MidiEffectKind(trackId, index) != 5 || E.MidiEffectParamCount(trackId, index) <= RLockBar)
            throw new ArgumentException($"MIDI effect {index} on track {trackId} is not a Nota Random.");
    }

    [McpServerTool(Name = "get_random"), Description(
        "Read a Nota Random (MIDI effect kind 5) in musical units: chancePercent (how many notes get varied), the five "
        + "amounts — noteRangeSemitones (±0..12), velocityRange (±0..64 MIDI), timingMaxMs (a 0..100 ms late shift), "
        + "skipPercent (varied notes dropped), octaveRange (±0..2) — distribution (gauss | even | walk), rate (per-note | "
        + "per-bar), stayInScale (snap into C major), seed 0..999, locked (hold one roll: lockBar's roll repeats every bar), "
        + "card view, and live telemetry: the bar being rolled, notes in / changed / skipped so far, delayed events queued, "
        + "and the last note (in pitch + velocity 0..1 → out pitch + velocity, delay ms, skipped).")]
    public Task<RandomState> GetRandom(int trackId, int index) => Read(() =>
    {
        RequireRandom(trackId, index);
        float G(int p) => E.MidiEffectGetParam(trackId, index, p);
        int R(float v) => (int)MathF.Round(v, MidpointRounding.AwayFromZero);
        var sc = new float[10];
        int sn = E.MidiEffectScope(trackId, index, sc);
        RandomLastNote? last = sn >= 9 && sc[4] >= 0
            ? new RandomLastNote((int)sc[4], sc[6], (int)sc[5], sc[7], MathF.Round(sc[8], 1), sc[5] < 0)
            : null;
        return new RandomState(MathF.Round(G(RChance) * 100), R(G(RNote)), R(G(RVel) * 64), MathF.Round(G(RTime) * 100), MathF.Round(G(RSkip) * 100),
            R(G(ROct) * 2), RandDists[Math.Clamp(R(G(RDist)), 0, 2)], G(RRate) >= 0.5f ? "per-bar" : "per-note", G(RScale) >= 0.5f,
            R(G(RSeed)), G(RLocked) >= 0.5f, R(G(RLockBar)), G(RView) >= 0.5f ? "S" : "L",
            sn >= 1 ? (int)sc[0] : 0, sn >= 2 ? (int)sc[1] : 0, sn >= 3 ? (int)sc[2] : 0, sn >= 4 ? (int)sc[3] : 0, sn >= 10 ? (int)sc[9] : 0, last);
    });

    [McpServerTool(Name = "set_random"), Description(
        "Set a Nota Random's params by name; omit what you don't change. chancePercent 0..100; noteRangeSemitones 0..12 (±); "
        + "velocityRange 0..64 (±); timingMaxMs 0..100 (notes play up to this late); skipPercent 0..100; octaveRange 0..2 (±); "
        + "distribution gauss | even | walk; rate per-note | per-bar; stayInScale (C major); seed 0..999 or reroll=true (the next "
        + "seed); locked=true holds the roll of lockBar (default: the bar the transport is in) so it repeats every bar, "
        + "locked=false rolls a new set every bar and pass; view S|L.")]
    public Task SetRandom(int trackId, int index, float? chancePercent = null, int? noteRangeSemitones = null, float? velocityRange = null,
        float? timingMaxMs = null, float? skipPercent = null, float? octaveRange = null, string? distribution = null, string? rate = null,
        bool? stayInScale = null, int? seed = null, bool? reroll = null, bool? locked = null, int? lockBar = null, string? view = null) => Mutate(() =>
    {
        RequireRandom(trackId, index);
        void S(int p, float v) => E.MidiEffectSetParam(trackId, index, p, v);
        if (chancePercent is { } c) S(RChance, Math.Clamp(c, 0, 100) / 100f);
        if (noteRangeSemitones is { } n) S(RNote, Math.Clamp(n, 0, 12));
        if (velocityRange is { } v) S(RVel, Math.Clamp(v, 0, 64) / 64f);
        if (timingMaxMs is { } t) S(RTime, Math.Clamp(t, 0, 100) / 100f);
        if (skipPercent is { } k) S(RSkip, Math.Clamp(k, 0, 100) / 100f);
        if (octaveRange is { } o) S(ROct, Math.Clamp(o, 0, 2) / 2f);
        if (distribution is not null)
        {
            int di = Array.IndexOf(RandDists, distribution.Trim().ToLowerInvariant());
            if (di < 0) throw new ArgumentException($"Unknown distribution '{distribution}'. Use one of: gauss, even, walk.");
            S(RDist, di);
        }
        if (rate is not null)
        {
            string r = rate.Trim().ToLowerInvariant().Replace(" ", "-").Replace("_", "-");
            if (r is not ("per-note" or "note" or "per-bar" or "bar")) throw new ArgumentException("rate must be per-note or per-bar.");
            S(RRate, r.EndsWith("bar", StringComparison.Ordinal) ? 1 : 0);
        }
        if (stayInScale is { } sc) S(RScale, sc ? 1 : 0);
        if (seed is { } sd) S(RSeed, Math.Clamp(sd, 0, 999));
        if (reroll == true) S(RSeed, (int)MathF.Round(E.MidiEffectGetParam(trackId, index, RSeed)) % 999 + 1);
        if (lockBar is { } lb) S(RLockBar, Math.Clamp(lb, 0, 9999));
        if (locked is { } l)
        {
            if (l && lockBar is null) S(RLockBar, Math.Max(0, (int)Math.Floor(E.PositionBeats / 4.0)));
            S(RLocked, l ? 1 : 0);
        }
        if (view is not null) S(RView, view.Trim().Equals("S", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
    });
}
