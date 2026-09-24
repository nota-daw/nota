// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — loading audio samples into sample-based instruments: the built-in Sampler (kind 1),
// the Grain granular synth (kind 10), and a Sampler inside a rack chain. rootNote is the MIDI note
// at which the sample plays back at its original pitch (60 = middle C). read_sampler / set_sampler
// read and shape the Sampler in musical units (SamplerModel holds the engine's value maps).

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class SampleTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record SamplerInfo(bool HasSample, long SampleId, int RootNote, bool Loop, int Channels, long Frames, double SampleRate, string Name = "");

    private static int RootFor(string path, int rootNote, bool detectRoot)
        => detectRoot && SamplerModel.DetectRoot(path) is var d and >= 0 ? d : rootNote;

    [McpServerTool(Name = "add_sampler_track"), Description(
        "Add a Sampler instrument track and load an audio file into it in one step. Returns the track id. detectRoot = take the root "
        + "from a note in the file name (\"Piano_C4.wav\" → C4), falling back to rootNote.")]
    public Task<int> AddSamplerTrack(string path, int rootNote = 60, bool detectRoot = false) => Mutate(() =>
    {
        int id = E.AddSamplerInstrumentTrack();
        if (id > 0) E.SetTrackSamplerSample(id, path, RootFor(path, rootNote, detectRoot));
        return id;
    });

    [McpServerTool(Name = "load_sampler_sample"), Description(
        "Load an audio file into an existing Sampler track (kind 1), keeping its settings. detectRoot = take the root from a note in the "
        + "file name, falling back to rootNote.")]
    public Task<bool> LoadSamplerSample(int trackId, string path, int rootNote = 60, bool detectRoot = false)
        => Mutate(() => E.SetTrackSamplerSample(trackId, path, RootFor(path, rootNote, detectRoot)));

    [McpServerTool(Name = "set_sampler_root"), Description("Set the Sampler's root note (the MIDI note that plays the sample at its original pitch).")]
    public Task<bool> SetSamplerRoot(int trackId, int rootNote) => Mutate(() => E.SetTrackSamplerRoot(trackId, rootNote));

    [McpServerTool(Name = "get_sampler_info"), Description("Read a Sampler track's loaded sample: root note, loop flag, channels, frame count, sample rate, file name.")]
    public Task<SamplerInfo> GetSamplerInfo(int trackId) => Read(() =>
    {
        if (!E.TryGetSamplerInfo(trackId, out var si) || si.SampleId == 0)
            return new SamplerInfo(false, 0, 0, false, 0, 0, 0);
        E.TryGetSampleInfo(si.SampleId, out var s);
        return new SamplerInfo(true, si.SampleId, si.RootNote, si.Loop != 0, s.Channels, s.Frames, s.SampleRate, E.SampleName(si.SampleId));
    });

    public sealed record SamplerValues(
        double StartSec, double EndSec, string Loop, double LoopStartSec, double LoopEndSec, double CrossfadeMs, double GainDb,
        string Root, int RootMidi, double TransposeSt, double DetuneCents, double PitchKeytrackPercent,
        double AttackMs, double DecayMs, double SustainDb, double ReleaseMs, double VelocityPercent,
        string Filter, double CutoffHz, double ResonancePercent, double FilterKeytrackPercent, bool EnvToCutoff, double EnvOctaves,
        string Voices, double GlideMs, double VolumeDb, double PanPercent, double OutputPercent);
    public sealed record SamplerLive(int ActiveVoices, int MaxVoices, double PlayPosition, string Stage, double Envelope, string Note, double CutoffHz);
    public sealed record SamplerReading(string Summary, string Guide, bool HasSample, string SampleName, double DurationSec, int Channels,
        double SampleRate, string DetectedRoot, double SoundStartSec, double SoundEndSec, SamplerValues Values, SamplerLive Live);

    [McpServerTool(Name = "read_sampler"), Description(
        "Read a Nota Sampler track (built-in instrument kind 1 — one sample pitched by the key relative to a root note). Returns a "
        + "one-line summary, a guide to what each 0..1 parameter value means (ids for set_instrument_param_by_id, or use set_sampler), "
        + "the sample (file name, length, channels, rate), the root a note in the file name suggests (DetectedRoot, \"—\" none), where "
        + "the sound starts and fades out (SoundStart/End, what trimSilence would set), every setting in real units (seconds, dB, "
        + "semitones, cents, Hz, %) and the live state: sounding voices, the loudest voice's play position 0..1, envelope stage and "
        + "level, note (fractional while gliding) and the cutoff it hears after key tracking and the envelope (-1 = filter off).")]
    public Task<SamplerReading?> ReadSampler(int trackId) => Read<SamplerReading?>(() =>
    {
        if (E.TrackInstrumentKind(trackId) != SamplerModel.Kind) return null;
        var g = Getter(trackId);
        E.TryGetSamplerInfo(trackId, out var si);
        bool has = si.SampleId != 0 && E.TryGetSampleInfo(si.SampleId, out _);
        E.TryGetSampleInfo(si.SampleId, out var s);
        double dur = has && s.SampleRate > 0 ? s.Frames / s.SampleRate : 0;
        string name = has ? E.SampleName(si.SampleId) : "";
        int det = SamplerModel.DetectRoot(name);
        double soundA = 0, soundB = dur;
        if (has && SamplerModel.TrimSilence(E.ReadSample(si.SampleId), s.Channels, s.Frames) is { } t) { soundA = t.Start * dur; soundB = t.End * dur; }
        int root = has ? si.RootNote : 60;
        var v = new SamplerValues(
            R(g("start") * dur, 3), R(g("end") * dur, 3), SamplerModel.LoopNames[SamplerModel.LoopIndex(g("loopmode"), g("reverse"))],
            R(g("loopstart") * dur, 3), R(g("loopend") * dur, 3), R(SamplerModel.CrossfadeSec(g("loopxfade")) * 1000, 1), R(SamplerModel.GainDb(g("gain")), 1),
            SamplerModel.NoteName(root), root, R(SamplerModel.TransposeSt(g("transpose")), 2), R(SamplerModel.DetuneCents(g("detune")), 1), R(g("pitchtrack") * 100, 1),
            R(SamplerModel.AttackSec(g("attack")) * 1000, 1), R(SamplerModel.DecaySec(g("decay")) * 1000, 1), R(Db(g("sustain")), 1),
            R(SamplerModel.ReleaseSec(g("release")) * 1000, 1), R(g("velamount") * 100, 1),
            SamplerModel.FilterNames[SamplerModel.Index(g("filtertype"), 4)], R(SamplerModel.CutoffHz(g("cutoff")), 0), R(g("resonance") * 100, 1),
            R(g("keytrack") * 100, 1), g("envcutoff") >= 0.5f, R(SamplerModel.EnvOctaves(g("envamount")), 2),
            SamplerModel.VoiceNames[SamplerModel.Index(g("voicemode"), 3)], R(SamplerModel.GlideSec(g("glide")) * 1000, 1),
            R(Db(g("volume")), 1), R(SamplerModel.PanSigned(g("pan")) * 100, 0), R(g("output") * 100, 1));
        var sc = new float[SamplerModel.ScopeLength];
        int n = E.InstrumentScope(trackId, sc);
        var snap = new SamplerModel.Snapshot();
        SamplerModel.Parse(sc.AsSpan(0, Math.Max(0, n)), snap);
        var live = new SamplerLive(snap.Voices, SamplerModel.Voices, R(snap.PlayPos, 3),
            snap.Stage is >= 0 and < 4 ? SamplerModel.StageNames[snap.Stage] : "idle", R(snap.Env, 3),
            snap.Note >= 0 ? SamplerModel.NoteName((int)Math.Round(snap.Note)) : "—", R(snap.CutoffHz, 0));
        return new SamplerReading(SamplerModel.Summary(g, root, dur, name), SamplerModel.Guide, has, name, R(dur, 3), s.Channels, s.SampleRate,
            det >= 0 ? SamplerModel.NoteName(det) : "—", R(soundA, 3), R(soundB, 3), v, live);
    });

    [McpServerTool(Name = "set_sampler"), Description(
        "Shape a Nota Sampler track (kind 1) in real units; every argument is optional and only the given ones change. "
        + "Sample: startSec / endSec (the played window), loop Off | Fwd | Ping | Rev, loopStartSec / loopEndSec, crossfadeMs 0..200 "
        + "(forward loops), gainDb ±24, reverse. Tools: trimSilence (Start/End to where the sound begins and fades, -48 dB), snapToZero "
        + "(Start/End/loop to the nearest zero crossings), detectRoot (root from a note in the file name). "
        + "Pitch: root (\"C4\", \"F#3\", \"Eb2\" or a MIDI number; C4 = 60), transposeSt ±24, detuneCents ±50, pitchKeytrackPercent 0..100 "
        + "(0 = every key plays the root). Env: attackMs 0.5..4000, decayMs 2..6000, sustainDb (0 = full, -inf allowed as -100), "
        + "releaseMs 2..6000, velocityPercent 0..100 (velocity → volume). Filter: filter Off | LP | HP | BP, cutoffHz 20..20000, "
        + "resonancePercent, filterKeytrackPercent, envToCutoff on/off, envOctaves ±6. Voices: voices Poly | Mono | Choke, glideMs 0..2000 "
        + "(Mono + glide = legato), volumeDb (≤ 0), panPercent -100..100, outputPercent 0..100. Returns the new summary, or an error line "
        + "naming what it could not use.")]
    public Task<string> SetSampler(int trackId,
        double? startSec = null, double? endSec = null, string? loop = null, double? loopStartSec = null, double? loopEndSec = null,
        double? crossfadeMs = null, double? gainDb = null, bool? reverse = null, bool? trimSilence = null, bool? snapToZero = null, bool? detectRoot = null,
        string? root = null, double? transposeSt = null, double? detuneCents = null, double? pitchKeytrackPercent = null,
        double? attackMs = null, double? decayMs = null, double? sustainDb = null, double? releaseMs = null, double? velocityPercent = null,
        string? filter = null, double? cutoffHz = null, double? resonancePercent = null, double? filterKeytrackPercent = null,
        bool? envToCutoff = null, double? envOctaves = null,
        string? voices = null, double? glideMs = null, double? volumeDb = null, double? panPercent = null, double? outputPercent = null) => Mutate(() =>
    {
        if (E.TrackInstrumentKind(trackId) != SamplerModel.Kind) return $"error: track {trackId} is not a Nota Sampler";
        int pc = E.PluginParamCount(trackId, -1);
        var ids = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) ids[E.PluginParamId(trackId, -1, i)] = i;
        float Get(string id) => ids.TryGetValue(id, out var i) ? E.PluginParamGet(trackId, -1, i) : 0f;
        void Set(string id, double v) { if (ids.TryGetValue(id, out var i)) E.PluginParamSet(trackId, -1, i, (float)Math.Clamp(v, 0, 1)); }
        static int Find(string[] names, string v) => Array.FindIndex(names, n => string.Equals(n, v.Trim(), StringComparison.OrdinalIgnoreCase)
            || n.StartsWith(v.Trim(), StringComparison.OrdinalIgnoreCase));
        var errors = new List<string>();

        E.TryGetSamplerInfo(trackId, out var si);
        float[] raw = Array.Empty<float>(); int ch = 1; long frames = 0; double dur = 0;
        if (si.SampleId != 0 && E.TryGetSampleInfo(si.SampleId, out var s) && s.SampleRate > 0)
        { ch = s.Channels; frames = s.Frames; dur = s.Frames / s.SampleRate; }
        bool needDur = startSec is not null || endSec is not null || loopStartSec is not null || loopEndSec is not null;
        if (needDur && dur <= 0) errors.Add("positions (no sample loaded)");
        else
        {
            if (startSec is { } a) Set("start", a / dur);
            if (endSec is { } b) Set("end", b / dur);
            if (loopStartSec is { } la) Set("loopstart", la / dur);
            if (loopEndSec is { } lb) Set("loopend", lb / dur);
        }
        if (loop is not null)
        {
            string l = loop.Trim().ToLowerInvariant() switch { "forward" => "fwd", "pingpong" or "ping-pong" => "ping", "reverse" => "rev", "none" or "one-shot" => "off", var x => x };
            int li = Find(SamplerModel.LoopNames, l);
            if (li < 0) errors.Add($"loop '{loop}'");
            else { var (lm, rv) = SamplerModel.LoopValues(li); Set("loopmode", lm); Set("reverse", rv); }
        }
        if (reverse is { } rev) Set("reverse", rev ? 1 : 0);
        if (crossfadeMs is { } xf) Set("loopxfade", xf / 200.0);
        if (gainDb is { } gd) Set("gain", SamplerModel.GainNorm(gd));
        if (trimSilence == true || snapToZero == true)
        {
            if (frames < 2) errors.Add("trim / snap (no sample loaded)");
            else
            {
                raw = E.ReadSample(si.SampleId);
                if (trimSilence == true && SamplerModel.TrimSilence(raw, ch, frames) is { } t) { Set("start", t.Start); Set("end", t.End); }
                if (snapToZero == true)
                    foreach (var id in new[] { "start", "end", "loopstart", "loopend" }) Set(id, SamplerModel.SnapZero(raw, ch, frames, Get(id)));
            }
        }
        if (detectRoot == true)
        {
            int d = SamplerModel.DetectRoot(si.SampleId != 0 ? E.SampleName(si.SampleId) : "");
            if (d < 0) errors.Add("detectRoot (the file name names no note)"); else E.SetTrackSamplerRoot(trackId, d);
        }
        if (root is not null)
        {
            int r = SamplerModel.ParseNote(root);
            if (r < 0) errors.Add($"root '{root}'"); else E.SetTrackSamplerRoot(trackId, r);
        }
        if (transposeSt is { } st) Set("transpose", SamplerModel.TransposeNorm(st));
        if (detuneCents is { } dc) Set("detune", SamplerModel.DetuneNorm(dc));
        if (pitchKeytrackPercent is { } pk) Set("pitchtrack", pk / 100.0);
        if (attackMs is { } at) Set("attack", SamplerModel.ExpInv(at / 1000, 0.0005, 4.0));
        if (decayMs is { } dm) Set("decay", SamplerModel.ExpInv(dm / 1000, 0.002, 6.0));
        if (sustainDb is { } sd) Set("sustain", sd <= -99 ? 0 : Math.Pow(10, Math.Min(0, sd) / 20));
        if (releaseMs is { } rm) Set("release", SamplerModel.ExpInv(rm / 1000, 0.002, 6.0));
        if (velocityPercent is { } vp) Set("velamount", vp / 100.0);
        if (filter is not null)
        {
            int f = Find(SamplerModel.FilterNames, filter.Trim().ToLowerInvariant() switch { "lowpass" or "low-pass" => "LP", "highpass" or "high-pass" => "HP", "bandpass" or "band-pass" => "BP", var x => x });
            if (f < 0) errors.Add($"filter '{filter}'"); else Set("filtertype", f / 3.0);
        }
        if (cutoffHz is { } hz) Set("cutoff", SamplerModel.ExpInv(hz, 20, 20000));
        if (resonancePercent is { } rp) Set("resonance", rp / 100.0);
        if (filterKeytrackPercent is { } fk) Set("keytrack", fk / 100.0);
        if (envToCutoff is { } ec) Set("envcutoff", ec ? 1 : 0);
        if (envOctaves is { } eo) { Set("envamount", SamplerModel.EnvOctavesNorm(eo)); if (envToCutoff is null) Set("envcutoff", 1); }
        if (voices is not null)
        {
            int vi = Find(SamplerModel.VoiceNames, voices);
            if (vi < 0) errors.Add($"voices '{voices}'"); else Set("voicemode", vi / 2.0);
        }
        if (glideMs is { } gm) Set("glide", SamplerModel.GlideNorm(gm / 1000));
        if (volumeDb is { } vd) Set("volume", vd <= -99 ? 0 : Math.Pow(10, Math.Min(0, vd) / 20));
        if (panPercent is { } pp) Set("pan", 0.5 + Math.Clamp(pp, -100, 100) / 200.0);
        if (outputPercent is { } op) Set("output", op / 100.0);

        E.TryGetSamplerInfo(trackId, out var si2);
        string summary = SamplerModel.Summary(Getter(trackId), si2.RootNote, dur, si2.SampleId != 0 ? E.SampleName(si2.SampleId) : "");
        return errors.Count == 0 ? summary : $"error: could not use {string.Join(", ", errors)} · {summary}";
    });

    private Func<string, float> Getter(int trackId)
    {
        int pc = E.PluginParamCount(trackId, -1);
        var vals = new Dictionary<string, float>();
        for (int i = 0; i < pc; i++) vals[E.PluginParamId(trackId, -1, i)] = E.PluginParamGet(trackId, -1, i);
        return id => vals.TryGetValue(id, out var v) ? v : 0f;
    }

    private static double R(double v, int digits) => double.IsFinite(v) ? Math.Round(v, digits) : -100;
    private static double Db(float lin) => lin <= 1e-5f ? -100 : 20 * Math.Log10(lin);

    [McpServerTool(Name = "add_grain_track"), Description(
        "Add a Grain (granular) instrument track and load an audio file to granulate. Returns the track id.")]
    public Task<int> AddGrainTrack(string path, int rootNote = 60) => Mutate(() =>
    {
        int id = E.AddGrainSynthTrack();
        if (id > 0) E.SetTrackGrainSample(id, path, rootNote);
        return id;
    });

    [McpServerTool(Name = "load_grain_sample"), Description("Load an audio file into an existing Grain granular track (kind 10).")]
    public Task<bool> LoadGrainSample(int trackId, string path, int rootNote = 60)
        => Mutate(() => E.SetTrackGrainSample(trackId, path, rootNote));

    [McpServerTool(Name = "get_grain_info"), Description("Read a Grain track's loaded sample: root note, channels, frame count, sample rate.")]
    public Task<SamplerInfo> GetGrainInfo(int trackId) => Read(() =>
    {
        if (!E.TryGetGrainInfo(trackId, out var si) || si.SampleId == 0)
            return new SamplerInfo(false, 0, 0, false, 0, 0, 0);
        E.TryGetSampleInfo(si.SampleId, out var s);
        return new SamplerInfo(true, si.SampleId, si.RootNote, si.Loop != 0, s.Channels, s.Frames, s.SampleRate);
    });

    [McpServerTool(Name = "load_chain_sample"), Description(
        "Load an audio file into a rack chain's Sampler (Instrument/Drum Rack). Keeps the chain's params + devices.")]
    public Task<bool> LoadChainSample(int trackId, int chain, string path, int rootNote = 60)
        => Mutate(() => E.RackSetChainSamplerSample(trackId, chain, path, rootNote));
}
