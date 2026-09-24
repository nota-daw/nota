// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — Nota Rhythm (drum machine, instrument kind 12). Eight fixed voices (Kick, Snare,
// Clap, Rim, Closed Hat, Open Hat, Tom, Perc), each with an internal 16-step sequencer across four
// pattern banks (A–D). Step edits apply to the CURRENTLY-selected bank (select_rhythm_bank first).
// A step has a velocity (below 0.45 = a quiet step) and an accent flag. Per-voice sound (Tune /
// Decay / Punch / Tone / Drive / Level / Pan, and for a sample voice Start / Length / Reverse)
// and the groove (Swing / Humanize / Accent / Glue / Volume) are plugin parameters — set them with
// set_rhythm_voice / set_rhythm_perform, or get_instrument_params / set_instrument_param_by_id.
// A voice plays its built-in synth engine or a loaded one-shot sample.

using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Nota.Application;
using RM = Nota.Application.RhythmModel;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class RhythmTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    private const int Voices = RM.Voices, Steps = RM.Steps, Banks = RM.Banks;

    public sealed record Step(int Index, float Velocity, bool Accent);
    public sealed record VoiceSound(float Tune, float Decay, float Punch, float Tone, float Drive, float Level, float Pan,
        float Start, float Length, bool Reverse, string TuneText, string DecayText);
    public sealed record VoiceInfo(int Index, string Name, string Source, bool HasSample, Step[] Steps,
        string Engine = "", int MidiNote = 0, string SampleName = "", string Pattern = "", VoiceSound? Sound = null);
    public sealed record RhythmSnapshot(int CurrentBank, int SelectedVoice, int Bank, VoiceInfo[] Voices,
        string Summary = "", string Guide = "", float Swing = 0, float Humanize = 0, float Accent = 0, float Glue = 0, float Volume = 0,
        int PlayingStep = -1, int SoundingVoices = 0);

    private RM.Pattern ReadPattern(int trackId) => RM.Parse(E.GetPluginState(trackId, -1), E.PluginParamCount(trackId, -1));

    private Func<string, float> Getter(int trackId)
    {
        int pc = E.PluginParamCount(trackId, -1);
        var vals = new Dictionary<string, float>();
        for (int i = 0; i < pc; i++) vals[E.PluginParamId(trackId, -1, i)] = E.PluginParamGet(trackId, -1, i);
        return id => vals.TryGetValue(id, out var v) ? v : 0f;
    }

    private Dictionary<string, int> Ids(int trackId)
    {
        int pc = E.PluginParamCount(trackId, -1);
        var ids = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) ids[E.PluginParamId(trackId, -1, i)] = i;
        return ids;
    }

    /// <summary>A row as text: x on, X accent, o quiet, . off.</summary>
    private static string RowText(RM.Pattern p, int b, int v)
    {
        var sb = new StringBuilder(Steps);
        for (int s = 0; s < Steps; s++)
            sb.Append(!p.On[b, v, s] ? '.' : p.Acc[b, v, s] ? 'X' : p.Quiet(b, v, s) ? 'o' : 'x');
        return sb.ToString();
    }

    [McpServerTool(Name = "get_rhythm"), Description(
        "Snapshot of a Nota Rhythm track: current bank + selected voice; for a bank (default = current) each voice's name, "
        + "engine, MIDI note, source (synth|sample), loaded sample name, its ON steps (index, velocity 0..1, accent), the row "
        + "as text (x on · X accent · o quiet · . off, 16 chars) and its sound (normalized params + tune/decay in units); "
        + "the groove (swing, humanize, accent, glue, volume, all 0..1), the step playing now (-1 stopped), a one-line "
        + "summary of the selected voice and a guide to every parameter.")]
    public Task<RhythmSnapshot> GetRhythm(int trackId, [Description("Bank 0..3 (A–D), or -1 for the current bank")] int bank = -1) => Read(() =>
    {
        var p = ReadPattern(trackId);
        var g = Getter(trackId);
        int b = bank >= 0 && bank < Banks ? bank : p.CurrentBank;
        var voices = new VoiceInfo[Voices];
        for (int v = 0; v < Voices; v++)
        {
            var steps = new List<Step>();
            for (int s = 0; s < Steps; s++) if (p.On[b, v, s]) steps.Add(new Step(s, (float)Math.Round(p.Vel[b, v, s], 3), p.Acc[b, v, s]));
            bool smp = E.RhythmVoiceSource(trackId, v) == 1;
            long sid = E.TryGetRhythmVoiceInfo(trackId, v, out var vi) ? vi.SampleId : 0;
            float F(string id) => g(RM.Id(v, id));
            string tune = smp ? RM.Semis(RM.SampleSemis(F("tune"))) : $"{RM.TuneHz(v, F("tune")):0} Hz";
            string decay = RM.Ms(smp ? RM.SampleDecaySeconds(F("decay")) : RM.DecaySeconds(v, F("decay"), F("punch")));
            var sound = new VoiceSound(F("tune"), F("decay"), F("punch"), F("tone"), F("drive"), F("level"), F("pan"),
                F("start"), F("length"), F("reverse") >= 0.5f, tune, decay);
            voices[v] = new VoiceInfo(v, RM.VoiceNames[v], smp ? "sample" : "synth", sid != 0, steps.ToArray(),
                RM.Engines[v], RM.MidiNotes[v], sid != 0 ? E.SampleName(sid) : "", RowText(p, b, v), sound);
        }
        var sc = new float[RM.ScopeLength];
        int n = E.InstrumentScope(trackId, sc);
        int sel = p.SelectedVoice;
        bool selSample = E.RhythmVoiceSource(trackId, sel) == 1 && voices[sel].HasSample;
        return new RhythmSnapshot(p.CurrentBank, sel, b, voices,
            RM.Summary(g, p, sel, selSample, voices[sel].SampleName), RM.Guide,
            g("swing"), g("humanize"), g("accent"), g("glue"), g("volume"),
            n > RM.S_Step ? (int)Math.Round(sc[RM.S_Step]) : -1, Math.Max(0, E.InstrumentVoiceCount(trackId)));
    });

    [McpServerTool(Name = "select_rhythm_bank"), Description("Select the active Rhythm pattern bank (0..3 = A–D). Step edits and playback use this bank.")]
    public Task SelectRhythmBank(int trackId, int bank) => Mutate(() => E.InstrumentAction(trackId, RM.A_SelectBank, Math.Clamp(bank, 0, Banks - 1), 0f));

    [McpServerTool(Name = "select_rhythm_voice"), Description("Select the voice (0..7) the Rhythm editor shows and edits.")]
    public Task SelectRhythmVoice(int trackId, int voice) => Mutate(() => E.InstrumentAction(trackId, RM.A_SelectVoice, Math.Clamp(voice, 0, Voices - 1), 0f));

    [McpServerTool(Name = "clear_rhythm_bank"), Description("Clear all steps + accents in a Rhythm bank (0..3). Velocities are left as-is.")]
    public Task ClearRhythmBank(int trackId, int bank) => Mutate(() => E.InstrumentAction(trackId, RM.A_ClearBank, Math.Clamp(bank, 0, Banks - 1), 0f));

    [McpServerTool(Name = "copy_rhythm_bank"), Description("Copy a whole Rhythm bank (every voice's steps, velocities and accents) onto another: from / to 0..3 (A–D).")]
    public Task<string> CopyRhythmBank(int trackId, int from, int to) => Mutate(() =>
    {
        if (from < 0 || from >= Banks || to < 0 || to >= Banks || from == to) return "error: from and to must be different banks 0..3";
        E.InstrumentAction(trackId, RM.A_CopyBank, from * Banks + to, 0f);
        return $"copied bank {RM.BankNames[from]} to {RM.BankNames[to]}";
    });

    [McpServerTool(Name = "clear_rhythm_voice"), Description("Clear one voice's steps (0..7) in the current bank.")]
    public Task ClearRhythmVoice(int trackId, int voice) => Mutate(() => E.InstrumentAction(trackId, RM.A_ClearVoice, Math.Clamp(voice, 0, Voices - 1), 0f));

    [McpServerTool(Name = "set_rhythm_step"), Description(
        "Set one step of a voice in the current bank (idempotent): on/off, velocity 0..1 (below 0.45 = quiet), accent. "
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

    [McpServerTool(Name = "set_rhythm_pattern"), Description(
        "Program voices from text rows in the current bank — the quickest way to write a beat. rows maps a voice (index "
        + "0..7 or name: kick, snare, clap, rim, ch/closed hat, oh/open hat, tom, perc) to 16 characters, one per 1/16 step: "
        + "x = on (velocity 0.7), X = accent, o = quiet (0.3), . or - = off; spaces and | are ignored (\"x...|x...|x...|x...\"). "
        + "Voices not named keep their steps. Returns each programmed row, or an error naming what it could not read.")]
    public Task<string> SetRhythmPattern(int trackId, Dictionary<string, string> rows) => Mutate(() =>
    {
        var errors = new List<string>();
        var done = new List<string>();
        var p = ReadPattern(trackId);
        foreach (var (key, row) in rows)
        {
            int v = VoiceIndex(key);
            if (v < 0) { errors.Add($"voice '{key}'"); continue; }
            var cells = row.Where(c => c is not ' ' and not '|').ToArray();
            if (cells.Length != Steps || cells.Any(c => "xXo.-".IndexOf(c) < 0)) { errors.Add($"row '{row}' (16 of x X o . -)"); continue; }
            for (int s = 0; s < Steps; s++)
            {
                char c = cells[s];
                if (c is '.' or '-') SetStep(trackId, p, v, s, false, 0f, false);
                else SetStep(trackId, p, v, s, true, c == 'o' ? RM.QuietVel : c == 'X' ? 0.9f : RM.NormalVel, c == 'X');
            }
            done.Add($"{RM.VoiceNames[v]} {new string(cells)}");
        }
        string res = string.Join(" · ", done);
        return errors.Count == 0 ? res : $"error: could not use {string.Join(", ", errors)}" + (res.Length > 0 ? " · " + res : "");
    });

    [McpServerTool(Name = "set_rhythm_voice"), Description(
        "Shape one Rhythm voice (0..7 or its name); every argument is optional and only the given ones change. All 0..1 "
        + "(see get_rhythm's guide): tune, decay, punch, tone, drive, level, pan (.5 centre); for a sample voice semitones "
        + "(-12..12, overrides tune), start and length (the region, fractions of the file) and reverse. Returns the voice's "
        + "summary line.")]
    public Task<string> SetRhythmVoice(int trackId, string voice, float? tune = null, float? decay = null, float? punch = null,
        float? tone = null, float? drive = null, float? level = null, float? pan = null, double? semitones = null,
        float? start = null, float? length = null, bool? reverse = null) => Mutate(() =>
    {
        if (E.TrackInstrumentKind(trackId) != RM.Kind) return $"error: track {trackId} is not a Nota Rhythm";
        int v = VoiceIndex(voice);
        if (v < 0) return $"error: voice '{voice}' (0..7 or kick, snare, clap, rim, ch, oh, tom, perc)";
        var ids = Ids(trackId);
        void Set(string p, float? val) { if (val is { } x && ids.TryGetValue(RM.Id(v, p), out var i)) E.PluginParamSet(trackId, -1, i, Math.Clamp(x, 0f, 1f)); }
        Set("tune", tune); Set("decay", decay); Set("punch", punch); Set("tone", tone); Set("drive", drive);
        Set("level", level); Set("pan", pan); Set("start", start); Set("length", length);
        if (semitones is { } st) Set("tune", RM.SampleSemisNorm(st));
        if (reverse is { } r) Set("reverse", r ? 1f : 0f);
        return VoiceLine(trackId, v);
    });

    [McpServerTool(Name = "set_rhythm_perform"), Description(
        "Set the Rhythm groove and bus; every argument optional, all 0..1: swing (delays every second 16th, 1 = half a step), "
        + "humanize (velocity jitter), accent (how much louder accented steps play), glue (bus compressor, 0 = off), "
        + "volume (master). Returns the new values.")]
    public Task<string> SetRhythmPerform(int trackId, float? swing = null, float? humanize = null, float? accent = null,
        float? glue = null, float? volume = null) => Mutate(() =>
    {
        if (E.TrackInstrumentKind(trackId) != RM.Kind) return $"error: track {trackId} is not a Nota Rhythm";
        var ids = Ids(trackId);
        void Set(string id, float? val) { if (val is { } x && ids.TryGetValue(id, out var i)) E.PluginParamSet(trackId, -1, i, Math.Clamp(x, 0f, 1f)); }
        Set("swing", swing); Set("humanize", humanize); Set("accent", accent); Set("glue", glue); Set("volume", volume);
        var g = Getter(trackId);
        return $"swing {RM.Pct(g("swing"))} · humanize {RM.Pct(g("humanize"))} · accent {RM.Pct(g("accent"))} · glue {RM.Pct(g("glue"))} · volume {RM.Pct(g("volume"))}";
    });

    [McpServerTool(Name = "audition_rhythm_voice"), Description("Play one Rhythm voice (0..7 or its name) once now, at velocity 0..1 — to hear a sound while shaping it.")]
    public Task<string> AuditionRhythmVoice(int trackId, string voice, float velocity = 0.8f) => Mutate(() =>
    {
        int v = VoiceIndex(voice);
        if (v < 0) return $"error: voice '{voice}'";
        E.InstrumentAction(trackId, RM.A_Audition, v, Math.Clamp(velocity, 0f, 1f));
        return $"played {RM.VoiceNames[v]}";
    });

    [McpServerTool(Name = "set_rhythm_voice_source"), Description("Switch a Rhythm voice between its built-in synth engine and a loaded sample. source = synth | sample (sample requires a loaded one-shot).")]
    public Task SetRhythmVoiceSource(int trackId, int voice, [Description("synth | sample")] string source)
        => Mutate(() => E.InstrumentAction(trackId, RM.A_SetSource, Math.Clamp(voice, 0, Voices - 1), source.Equals("sample", StringComparison.OrdinalIgnoreCase) ? 1f : 0f));

    [McpServerTool(Name = "load_rhythm_voice_sample"), Description(
        "Load a one-shot audio file into a Rhythm voice (0..7) and switch that voice to Sample mode; the voice's tune goes to "
        + "the sample's own pitch (0 st) and its region to the whole file.")]
    public Task<bool> LoadRhythmVoiceSample(int trackId, int voice, string path) => Mutate(() =>
    {
        int v = Math.Clamp(voice, 0, Voices - 1);
        if (!E.SetRhythmVoiceSample(trackId, v, path)) return false;
        var ids = Ids(trackId);
        foreach (var (p, val) in new[] { ("tune", 0.5f), ("tone", 1f), ("start", 0f), ("length", 1f) })
            if (ids.TryGetValue(RM.Id(v, p), out var i)) E.PluginParamSet(trackId, -1, i, val);
        return true;
    });

    // ---- voice FX: each voice's own insert chain of built-in effects ------------------------

    public sealed record FxParam(string Name, float Value, float Min, float Max);
    public sealed record FxDeviceInfo(int Index, string Name, int Kind, bool Bypassed, FxParam[] Params);

    [McpServerTool(Name = "get_rhythm_voice_fx"), Description(
        "The insert effects on one Rhythm voice (0..7 or its name), in signal order: each device's index, name, "
        + "builtin kind, bypass and params (value with its min..max). A factory kit puts saturation on the kick, a "
        + "reverb on the snare and clap and a tempo-synced delay on hats and percussion.")]
    public Task<FxDeviceInfo[]> GetRhythmVoiceFx(int trackId, string voice) => Read(() =>
    {
        int v = VoiceIndex(voice);
        if (v < 0 || E.TrackInstrumentKind(trackId) != RM.Kind) return Array.Empty<FxDeviceInfo>();
        int n = E.RhythmVoiceDeviceCount(trackId, v);
        var list = new FxDeviceInfo[n];
        for (int d = 0; d < n; d++)
        {
            int pc = E.RhythmVoiceDeviceParamCount(trackId, v, d);
            var ps = new FxParam[pc];
            for (int p = 0; p < pc; p++)
                ps[p] = new FxParam(E.RhythmVoiceDeviceParamName(trackId, v, d, p), E.RhythmVoiceDeviceParamGet(trackId, v, d, p),
                    E.RhythmVoiceDeviceParamMin(trackId, v, d, p), E.RhythmVoiceDeviceParamMax(trackId, v, d, p));
            list[d] = new FxDeviceInfo(d, E.RhythmVoiceDeviceName(trackId, v, d), E.RhythmVoiceDeviceBuiltinKind(trackId, v, d),
                E.RhythmVoiceDeviceBypassed(trackId, v, d), ps);
        }
        return list;
    });

    [McpServerTool(Name = "add_rhythm_voice_fx"), Description(
        "Append a built-in effect to a Rhythm voice's insert chain. kind: 0 EQ-8, 1 Compressor, 2 Reverb, 3 Delay, "
        + "4 Utility, 6 Valve, 7 Auto Filter, 8 Vintage, 9 Orbit, 10 Auto Shift, 11 Beat Repeat, 12 Crush, "
        + "13 Dynamic EQ, 14 Ceiling, 15 Strata, 16 EQ-3, 17 Forge, 18 Level, 19 Shutter, 20 Chamber, 21 Prism. "
        + "Returns the new device index, or -1.")]
    public Task<int> AddRhythmVoiceFx(int trackId, string voice, int kind) => Mutate(() =>
    {
        int v = VoiceIndex(voice);
        return v < 0 || E.TrackInstrumentKind(trackId) != RM.Kind ? -1 : E.RhythmAddVoiceDevice(trackId, v, kind);
    });

    [McpServerTool(Name = "remove_rhythm_voice_fx"), Description("Remove effect `index` from a Rhythm voice's insert chain.")]
    public Task<bool> RemoveRhythmVoiceFx(int trackId, string voice, int index) => Mutate(() =>
    {
        int v = VoiceIndex(voice);
        return v >= 0 && E.RhythmRemoveVoiceDevice(trackId, v, index);
    });

    [McpServerTool(Name = "set_rhythm_voice_fx"), Description(
        "Set params of effect `index` on a Rhythm voice by name (as get_rhythm_voice_fx lists them, in the device's "
        + "own units — Reverb / Delay / Forge are 0..1), and/or bypass it. Returns what changed.")]
    public Task<string> SetRhythmVoiceFx(int trackId, string voice, int index, Dictionary<string, float>? values = null, bool? bypassed = null) => Mutate(() =>
    {
        int v = VoiceIndex(voice);
        if (v < 0 || index < 0 || index >= E.RhythmVoiceDeviceCount(trackId, v)) return $"error: no effect {index} on voice '{voice}'";
        var done = new List<string>();
        if (values is { Count: > 0 })
        {
            int pc = E.RhythmVoiceDeviceParamCount(trackId, v, index);
            foreach (var (name, val) in values)
            {
                int p = Enumerable.Range(0, pc).FirstOrDefault(i => string.Equals(E.RhythmVoiceDeviceParamName(trackId, v, index, i), name, StringComparison.OrdinalIgnoreCase), -1);
                if (p < 0) { done.Add($"no param '{name}'"); continue; }
                float x = Math.Clamp(val, E.RhythmVoiceDeviceParamMin(trackId, v, index, p), E.RhythmVoiceDeviceParamMax(trackId, v, index, p));
                E.RhythmVoiceDeviceParamSet(trackId, v, index, p, x);
                done.Add($"{name} = {x:0.###}");
            }
        }
        if (bypassed is { } b) { E.RhythmSetVoiceDeviceBypassed(trackId, v, index, b); done.Add(b ? "bypassed" : "active"); }
        return $"{E.RhythmVoiceDeviceName(trackId, v, index)}: {string.Join(" · ", done)}";
    });

    private string VoiceLine(int trackId, int v)
    {
        var p = ReadPattern(trackId);
        bool smp = E.RhythmVoiceSource(trackId, v) == 1 && E.TryGetRhythmVoiceInfo(trackId, v, out var vi) && vi.SampleId != 0;
        string name = smp && E.TryGetRhythmVoiceInfo(trackId, v, out var vi2) ? E.SampleName(vi2.SampleId) : "";
        return RM.Summary(Getter(trackId), p, v, smp, name);
    }

    private static int VoiceIndex(string key)
    {
        var k = key.Trim().ToLowerInvariant();
        if (int.TryParse(k, out int n)) return n is >= 0 and < Voices ? n : -1;
        return k switch
        {
            "kick" or "bd" => 0,
            "snare" or "sd" => 1,
            "clap" or "cp" => 2,
            "rim" or "rimshot" or "rs" => 3,
            "ch" or "closed hat" or "closedhat" or "closed" or "hh" => 4,
            "oh" or "open hat" or "openhat" or "open" => 5,
            "tom" => 6,
            "perc" or "percussion" => 7,
            _ => -1,
        };
    }

    // Idempotent single-step write against a freshly-read pattern (current bank); keeps it in sync.
    private void SetStep(int trackId, RM.Pattern p, int voice, int step, bool on, float velocity, bool accent)
    {
        int v = Math.Clamp(voice, 0, Voices - 1), s = Math.Clamp(step, 0, Steps - 1), b = p.CurrentBank;
        int arg = v * Steps + s;
        if (on)
        {
            if (!p.On[b, v, s]) { E.InstrumentAction(trackId, RM.A_ToggleStep, arg, 0f); p.On[b, v, s] = true; }
            E.InstrumentAction(trackId, RM.A_SetVel, arg, Math.Clamp(velocity, 0f, 1f));
            p.Vel[b, v, s] = velocity;
            if (p.Acc[b, v, s] != accent) { E.InstrumentAction(trackId, RM.A_ToggleAccent, arg, 0f); p.Acc[b, v, s] = accent; }
        }
        else
        {
            if (p.On[b, v, s]) { E.InstrumentAction(trackId, RM.A_ToggleStep, arg, 0f); p.On[b, v, s] = false; }
            if (p.Acc[b, v, s]) { E.InstrumentAction(trackId, RM.A_ToggleAccent, arg, 0f); p.Acc[b, v, s] = false; }
        }
    }
}
