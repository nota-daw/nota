// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — Nota Rhythm (drum machine, instrument kind 12). Eight fixed voices (Kick, Snare,
// Clap, Rim, Closed Hat, Open Hat, Tom, Perc), each with an internal 16-step sequencer across four
// pattern banks (A–D). Step edits apply to the CURRENTLY-selected bank (select_rhythm_bank first).
// Per-voice knobs (Tune/Decay/Punch/Tone/Drive/Level/Pan) + Swing/Humanize/Accent are plugin
// parameters — read/write them with get_instrument_params / set_instrument_param. A voice can play
// its built-in synth engine or a loaded one-shot sample.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class RhythmTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    // action ids — must match RhythmMachine::Act.
    private const int A_ToggleStep = 0, A_SetVel = 1, A_ToggleAccent = 2, A_SelectBank = 3, A_ClearBank = 4, A_SetSource = 6;
    private const int Voices = 8, Steps = 16, Banks = 4;
    private static readonly string[] VoiceNames = { "Kick", "Snare", "Clap", "Rim", "Closed Hat", "Open Hat", "Tom", "Perc" };

    public sealed record Step(int Index, float Velocity, bool Accent);
    public sealed record VoiceInfo(int Index, string Name, string Source, bool HasSample, Step[] Steps);
    public sealed record RhythmSnapshot(int CurrentBank, int SelectedVoice, int Bank, VoiceInfo[] Voices);

    // Reads the instrument state blob and exposes a per-step accessor (mirrors RhythmInstrumentCard).
    private readonly record struct Pattern(byte[] St, int PatBase, int CurrentBank, int SelectedVoice)
    {
        private int Ix(int bank, int v, int s) => PatBase + ((bank * Voices + v) * Steps + s) * 3;
        public bool On(int bank, int v, int s) { int i = Ix(bank, v, s); return i < St.Length && St[i] != 0; }
        public float Vel(int bank, int v, int s) { int i = Ix(bank, v, s) + 1; return i < St.Length ? St[i] / 255f : 0.7f; }
        public bool Accent(int bank, int v, int s) { int i = Ix(bank, v, s) + 2; return i < St.Length && St[i] != 0; }
    }

    private Pattern ReadPattern(int trackId)
    {
        byte[] st = E.GetPluginState(trackId, -1);
        int pc = E.PluginParamCount(trackId, -1);
        int bankByte = 4 + pc * 4, patBase = bankByte + 4;
        int bank = st.Length > bankByte ? Math.Clamp(st[bankByte], (byte)0, (byte)(Banks - 1)) : 0;
        int sel = st.Length > bankByte + 1 ? Math.Clamp(st[bankByte + 1], (byte)0, (byte)(Voices - 1)) : 0;
        return new Pattern(st, patBase, bank, sel);
    }

    [McpServerTool(Name = "get_rhythm"), Description(
        "Snapshot of a Nota Rhythm track: current bank + selected voice, and for a bank (default = current) "
        + "each voice's name, source (synth|sample) and its ON steps (index, velocity 0..1, accent).")]
    public Task<RhythmSnapshot> GetRhythm(int trackId, [Description("Bank 0..3 (A–D), or -1 for the current bank")] int bank = -1) => Read(() =>
    {
        var p = ReadPattern(trackId);
        int b = bank >= 0 && bank < Banks ? bank : p.CurrentBank;
        var voices = new VoiceInfo[Voices];
        for (int v = 0; v < Voices; v++)
        {
            var steps = new List<Step>();
            for (int s = 0; s < Steps; s++) if (p.On(b, v, s)) steps.Add(new Step(s, p.Vel(b, v, s), p.Accent(b, v, s)));
            string src = E.RhythmVoiceSource(trackId, v) == 1 ? "sample" : "synth";
            bool hasSample = E.TryGetRhythmVoiceInfo(trackId, v, out var vi) && vi.SampleId != 0;
            voices[v] = new VoiceInfo(v, VoiceNames[v], src, hasSample, steps.ToArray());
        }
        return new RhythmSnapshot(p.CurrentBank, p.SelectedVoice, b, voices);
    });

    [McpServerTool(Name = "select_rhythm_bank"), Description("Select the active Rhythm pattern bank (0..3 = A–D). Step edits and playback use this bank.")]
    public Task SelectRhythmBank(int trackId, int bank) => Mutate(() => E.InstrumentAction(trackId, A_SelectBank, Math.Clamp(bank, 0, Banks - 1), 0f));

    [McpServerTool(Name = "clear_rhythm_bank"), Description("Clear all steps + accents in a Rhythm bank (0..3). Velocities are left as-is.")]
    public Task ClearRhythmBank(int trackId, int bank) => Mutate(() => E.InstrumentAction(trackId, A_ClearBank, Math.Clamp(bank, 0, Banks - 1), 0f));

    [McpServerTool(Name = "set_rhythm_step"), Description(
        "Set one step of a voice in the current bank (idempotent): on/off, velocity 0..1, accent. "
        + "voice 0..7 (0 Kick .. 7 Perc), step 0..15.")]
    public Task SetRhythmStep(int trackId, int voice, int step, bool on, float velocity = 0.7f, bool accent = false) => Mutate(() =>
    {
        SetStep(trackId, ReadPattern(trackId), voice, step, on, velocity, accent);
    });

    [McpServerTool(Name = "set_rhythm_row"), Description(
        "Program a voice's whole 16-step row in the current bank (idempotent — clears steps not listed). "
        + "voice 0..7; onSteps lists the active step indices (0..15) with velocity 0..1 and accent.")]
    public Task SetRhythmRow(int trackId, int voice, Step[] onSteps) => Mutate(() =>
    {
        var p = ReadPattern(trackId);
        var want = new Dictionary<int, Step>();
        foreach (var s in onSteps) if (s.Index >= 0 && s.Index < Steps) want[s.Index] = s;
        for (int s = 0; s < Steps; s++)
        {
            if (want.TryGetValue(s, out var w)) SetStep(trackId, p, voice, s, true, w.Velocity, w.Accent);
            else SetStep(trackId, p, voice, s, false, 0f, false);
        }
    });

    [McpServerTool(Name = "set_rhythm_voice_source"), Description("Switch a Rhythm voice between its built-in synth engine and a loaded sample. source = synth | sample (sample requires a loaded one-shot).")]
    public Task SetRhythmVoiceSource(int trackId, int voice, [Description("synth | sample")] string source)
        => Mutate(() => E.InstrumentAction(trackId, A_SetSource, Math.Clamp(voice, 0, Voices - 1), source.Equals("sample", StringComparison.OrdinalIgnoreCase) ? 1f : 0f));

    [McpServerTool(Name = "load_rhythm_voice_sample"), Description("Load a one-shot audio file into a Rhythm voice (0..7) and switch that voice to Sample mode.")]
    public Task<bool> LoadRhythmVoiceSample(int trackId, int voice, string path) => Mutate(() => E.SetRhythmVoiceSample(trackId, Math.Clamp(voice, 0, Voices - 1), path));

    // Idempotent single-step write against a freshly-read pattern (current bank).
    private void SetStep(int trackId, in Pattern p, int voice, int step, bool on, float velocity, bool accent)
    {
        int v = Math.Clamp(voice, 0, Voices - 1), s = Math.Clamp(step, 0, Steps - 1), b = p.CurrentBank;
        int arg = v * Steps + s;
        bool curOn = p.On(b, v, s);
        if (on)
        {
            if (!curOn) E.InstrumentAction(trackId, A_ToggleStep, arg, 0f);           // turn on
            E.InstrumentAction(trackId, A_SetVel, arg, Math.Clamp(velocity, 0f, 1f)); // set velocity
            if (p.Accent(b, v, s) != accent) E.InstrumentAction(trackId, A_ToggleAccent, arg, 0f);
        }
        else if (curOn)
        {
            E.InstrumentAction(trackId, A_ToggleStep, arg, 0f);                        // turn off
        }
    }
}
