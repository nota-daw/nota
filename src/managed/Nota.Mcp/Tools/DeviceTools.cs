// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — audio effects (devices) in a track's insert chain.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class DeviceTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    // Built-in audio-effect kinds (matches the engine's builtinKind() ids).
    private static readonly (int Kind, string Name)[] Kinds =
    {
        (0, "EQ-8"), (1, "Compressor"), (2, "Reverb"), (3, "Delay"), (4, "Utility"), (5, "Effect Rack"),
        (6, "Nota Valve"), (7, "Auto Filter"), (8, "Nota Vintage"), (9, "Nota Orbit"), (10, "Auto Shift"),
        (11, "Beat Repeat"), (12, "Crush"), (13, "Dynamic EQ-8"), (14, "Ceiling"), (15, "Strata"),
        (16, "EQ-3"), (17, "Forge"), (18, "Nota Level"), (19, "Nota Shutter"), (20, "Nota Chamber"), (21, "Nota Prism"),
        (22, "Nota Lens"),
    };

    public sealed record DeviceKind(int Kind, string Name);
    public sealed record Device(int Index, int BuiltinKind, string Name, bool Bypassed);
    public sealed record Param(int Index, string Name, float Min, float Max, float Value);

    [McpServerTool(Name = "list_device_kinds"), Description("List the built-in audio-effect kinds (kind id + name) you can add to a track.")]
    public DeviceKind[] ListDeviceKinds() => Array.ConvertAll(Kinds, k => new DeviceKind(k.Kind, k.Name));

    [McpServerTool(Name = "list_devices"), Description("List a track's insert effects (device index, built-in kind, name, bypass state).")]
    public Task<Device[]> ListDevices(int trackId) => Read(() =>
    {
        int dc = E.TrackDeviceCount(trackId);
        var ds = new Device[dc];
        for (int i = 0; i < dc; i++) ds[i] = new Device(i, E.TrackDeviceBuiltinKind(trackId, i), E.DeviceName(trackId, i), E.DeviceBypassed(trackId, i));
        return ds;
    });

    [McpServerTool(Name = "add_device"), Description("Add a built-in audio effect (see list_device_kinds) to a track's chain. Returns the device index.")]
    public Task<int> AddDevice(int trackId, [Description("Built-in effect kind id")] int kind) => Mutate(() => E.AddBuiltinDevice(trackId, kind));

    [McpServerTool(Name = "remove_device"), Description("Remove a device from a track's chain by index.")]
    public Task RemoveDevice(int trackId, int deviceIndex) => Mutate(() => E.RemoveDevice(trackId, deviceIndex));

    [McpServerTool(Name = "move_device"), Description("Reorder a device within a track's chain.")]
    public Task MoveDevice(int trackId, int fromIndex, int toIndex) => Mutate(() => E.MoveDevice(trackId, fromIndex, toIndex));

    [McpServerTool(Name = "set_device_bypass"), Description("Bypass or enable a device.")]
    public Task SetDeviceBypass(int trackId, int deviceIndex, bool bypassed) => Mutate(() => E.SetDeviceBypassed(trackId, deviceIndex, bypassed));

    [McpServerTool(Name = "get_device_params"), Description("List a device's parameters (index, name, min, max, current value).")]
    public Task<Param[]> GetDeviceParams(int trackId, int deviceIndex) => Read(() =>
    {
        int pc = E.DeviceParamCount(trackId, deviceIndex);
        var ps = new Param[pc];
        for (int i = 0; i < pc; i++)
            ps[i] = new Param(i, E.DeviceParamName(trackId, deviceIndex, i), E.DeviceParamMin(trackId, deviceIndex, i), E.DeviceParamMax(trackId, deviceIndex, i), E.DeviceGetParam(trackId, deviceIndex, i));
        return ps;
    });

    [McpServerTool(Name = "set_device_param"), Description("Set a device parameter by index (value is in the param's own min..max range from get_device_params).")]
    public Task SetDeviceParam(int trackId, int deviceIndex, int paramIndex, float value) => Mutate(() => E.DeviceSetParam(trackId, deviceIndex, paramIndex, value));

    [McpServerTool(Name = "load_device_file"), Description("Load an audio file into a device that takes one — Nota Chamber: a user impulse response (WAV / FLAC / MP3; mono, stereo or 4-channel true stereo), which also selects it. Returns true on success.")]
    public Task<bool> LoadDeviceFile(int trackId, int deviceIndex, [Description("Absolute path to the audio file")] string path) => Mutate(() => E.DeviceLoadFile(trackId, deviceIndex, path));

    [McpServerTool(Name = "get_device_text"), Description("Read a device's resource text. Nota Chamber: id 0 = current IR name, 1 = its category, 2 = the loaded user IR's name, 10 = the built-in IR list (name, category, seconds per line; the IR param selects entry round(v × 16), 1.0 = the user IR). Nota Lens: id 0 = the analysis summary, 1 = the third-octave band table, 2 = the scope measurements, 3 = the strongest spectral peaks, 4 = the A/B cursor measurements — or use read_analyzer, which returns all of it parsed. Delay (kind 3): id 0 = a one-line summary of what it is doing now (sync division or free times, ping-pong, feedback, mix, or that it is frozen). Reverb (kind 2): id 0 = a one-line summary (algorithm, RT60, pre-delay, diffusion, mix, early reflections / vintage, or that the tail is frozen). Compressor (kind 1): id 0 = a one-line summary (character, threshold, ratio, knee, attack / release, detector, range, mix, the reduction right now, external key / unlinked / Listen) — or use read_dynamics for the numbers. Auto Filter (kind 7): id 0 = a one-line summary (type, slope, cutoff, Q, what the envelope and LFO drive and by how much, drive, mix, sidechain key), 1 = the live reading (modulated cutoff with its note, resonance, envelope level, LFO value and phase, onsets counted), 2 = a guide to what each 0..1 parameter value means — or use read_filter_motion for the numbers. Nota Vintage (kind 8): id 0 = a one-line summary (character, drive, tone model and shelves, wow / flutter with their rates, noise, crackle, wear, stage, mix, output, oversampling, auto-comp), 1 = the live reading (in / out peaks, THD with the 2nd and 3rd harmonics, asymmetry, wow and flutter in cents, band-limit, hiss level, auto-comp gain), 2 = a guide to what each 0..1 parameter value means — or use read_vintage for the numbers. Nota Valve (kind 6): id 0 = a one-line summary (model, gain, the tone stack with the middle's frequency, bright / deep, even-only, cabinet with mic, distance, axis and position, low / high cut, gate, mix, output, oversampling, auto-comp), 1 = the live reading (in / out peaks, THD with the 2nd and 3rd harmonics, estimated aliasing, drive, gate open / closed, auto-comp gain, the cab's loss at 4 kHz vs on-axis), 2 = a guide to the parameter values and units — or use read_valve for the numbers. Nota Utility (kind 4): id 0 = a one-line summary (channel mode, width and its L/R or M/S law, mono below with its slope, balance, phase, mute, gain, auto match and its reference, true-peak limit), 1 = the live reading (in / out LUFS-S, peak and RMS, true peak, the delta in the meter's unit, correlation, width, energy balance, auto-match gain, limiter reduction), 2 = a guide to the parameter values and units — or use read_utility for the numbers. Nota Shutter (kind 19): id 0 = a one-line summary (Gate / Duck, threshold, return, shape with attack / hold / release, floor, lookahead, retrigger, internal / external key, the key filter band, peak hold, Listen), 1 = the live reading (state, gain, reduction now and peak, in / out / detector levels, the share of the window it was open, openings in the last bar and since reset, key level, latency), 2 = a guide to what each 0..1 parameter value means — or use read_shutter for the numbers. Nota Auto Shift (kind 10): id 0 = a one-line summary (key and scale or the MIDI target settings, key source, amount, speed, range, human, shift and fine, formant preserve and shift, mix, detection range and sensitivity, skip sibilants, follow), 1 = the live reading (detected note and cents, frequency, clarity, target and correction, ratio, input level, MIDI note held, share sung in scale, the best and runner-up key, Learn running, latency), 2 = a guide to what each 0..1 parameter value means — or use read_auto_shift for the numbers. Nota Beat Repeat (kind 11): id 0 = a one-line summary (mode, interval, grid, offset, gate, chance, variation, pitch and pitch decay, decay, volume, the repeat filter with its type, band and narrowing, mix, Repeat held, latch), 1 = the live reading (idle / capturing / repeating, the pass of how many, the slice in ms and beats, the repeat's gain, pitch and filter, in / repeat / out levels, bar and beat, bursts and skipped intervals since reset), 2 = a guide to what each 0..1 parameter value means — or use read_beat_repeat for the numbers.")]
    public Task<string> GetDeviceText(int trackId, int deviceIndex, int id) => Read(() => E.DeviceText(trackId, deviceIndex, id));

    [McpServerTool(Name = "device_action"), Description("Run a device's own command — the few things that are actions rather than parameters. Delay (kind 3): id 0 clears the loop (empties the delay buffer, so whatever is still circulating stops; useful after Freeze). Reverb (kind 2): id 0 kills the tail (empties the reverb's buffers, so whatever is still ringing — or frozen — stops). Auto Filter (kind 7): id 0 restarts the LFO (from its start phase; in Sync the cycle is re-anchored to the current beat), id 1 resets the envelope follower (drops a held peak). Nota Vintage (kind 8): id 0 resets the wear (restarts the wow and flutter at their zero phase and silences a ringing crackle). Nota Valve (kind 6): id 0 resets the amp (clears the filters, the gate and the auto-comp). Nota Utility (kind 4): id 0 gain match — moves Gain once so the output meets the reference (the input's level, or Target when Match To = 1) over the last 3 s in the Meter's unit; id 1 resets the meters (level history, holds and the auto-match ride). Nota Shutter (kind 19): id 0 resets the meters (peak reduction, the opening count, the history); id 1 sets the history window read_shutter and the card show (intArg 0 = 250 ms, 1 = 1 s, 2 = 4 s). Nota Auto Shift (kind 10): id 0 resets the analysis (the sung-note histogram and the pitch history); id 1 is Learn — intArg 1 starts listening (clears the histogram), 0 stops and sets Key + Scale (Major / Minor) to the best match and Key Source to Manual, 2 cancels. Nota Beat Repeat (kind 11): id 0 resets (stops the repeat, clears the timeline, the meters and the burst count); id 1 fires a repeat now, as if the interval hit (ignores Chance; while the transport plays — to hold one, set the Repeat param to 1). Devices without a command ignore the call.")]
    public Task DeviceAction(int trackId, int deviceIndex, [Description("Command id — see the device's list in this description")] int id,
        int intArg = 0, float floatArg = 0) => Mutate(() => E.DeviceAction(trackId, deviceIndex, id, intArg, floatArg));

    public sealed record Sidechain(bool Accepts, int SourceTrackId, bool TapPre, float GainDb, float Mix);

    [McpServerTool(Name = "get_device_sidechain"), Description("Read a device's sidechain routing: whether it can take a key at all (the Compressor, Ceiling, Nota Shutter, Nota Auto Shift — its MIDI source — and plugins with a sidechain bus), the source track id (-1 = the device keys off its own track), whether the source is tapped pre-FX (true) or post-fader (false), the detector gain in dB and the effect's sidechain dry/wet mix (0..1).")]
    public Task<Sidechain> GetDeviceSidechain(int trackId, int deviceIndex) => Read(() => new Sidechain(
        E.DeviceAcceptsSidechain(trackId, deviceIndex), E.DeviceSidechainSource(trackId, deviceIndex),
        E.DeviceSidechainTapPre(trackId, deviceIndex), E.DeviceSidechainGain(trackId, deviceIndex), E.DeviceSidechainMix(trackId, deviceIndex)));

    [McpServerTool(Name = "set_device_sidechain"), Description("Route a sidechain key into a device — e.g. duck a bass or pad under the kick with a Compressor (kind 1): pass the kick track as sourceTrackId, then shape the key with the Compressor's params (SC HP / SC LP / SC Q / SC Gain / Hold; External Key must be on, which is its default). Nota Shutter (kind 19) gates or ducks against a key the same way (Flip = 1 for Duck; Det HP / Det LP shape the key; External Key on, its default). Nota Auto Shift (kind 10) takes an instrument track here as its MIDI source: with Key Source = 1 (MIDI) the voice is pulled to that track's notes (its clips after its MIDI effects, even while muted). sourceTrackId -1 clears the source (the device keys off its own track). Optional: tapPre (true = the source before its effects and fader, false = after), gainDb (extra detector gain, ±24 dB). Leave an optional argument out to keep its current value. No effect on devices that take no sidechain.")]
    public Task SetDeviceSidechain(int trackId, int deviceIndex, [Description("Key source track id, or -1 for none")] int sourceTrackId,
        bool? tapPre = null, float? gainDb = null) => Mutate(() =>
    {
        E.SetDeviceSidechainSource(trackId, deviceIndex, sourceTrackId);
        if (tapPre is { } pre) E.SetDeviceSidechainTapPre(trackId, deviceIndex, pre);
        if (gainDb is { } g) E.SetDeviceSidechainGain(trackId, deviceIndex, Math.Clamp(g, -24f, 24f));
    });

    public sealed record DynamicsReading(string Summary, double GainReductionDb, double InputPeakDb, double InputRmsDb,
        double OutputPeakDb, double OutputRmsDb, double KeyPeakDb, bool ExternalKey, double AttackMs, double ReleaseMs,
        int LatencySamples, double SampleRate, int HitsLast2s, double DeepestReductionLast2sDb);

    [McpServerTool(Name = "read_dynamics"), Description(
        "Read what a Nota Compressor (built-in effect kind 1) is doing right now: the gain reduction (dB, positive = "
        + "reduced), input / output peak and 300 ms RMS levels (dBFS), the detector key's peak level (after the key "
        + "filters and SC Gain — compare it with the threshold), whether an external sidechain key is driving it, the "
        + "effective attack / release in ms (after the Character voicing and auto-release), the look-ahead latency, "
        + "how many times the reduction kicked in (rose through 1 dB) and the deepest reduction over the last 2 s, and "
        + "the one-line summary. Levels are only live while audio plays through the track.")]
    public Task<DynamicsReading> ReadDynamics(int trackId, int deviceIndex) => Read(() =>
    {
        const int kScope = 14, envN = 2048;
        var sc = new float[kScope + 2 * envN];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        static double Db(float lin) => lin > 1e-6f ? Math.Round(20 * Math.Log10(lin), 1) : -120;
        int hits = 0; bool armed = true; double deepest = 0;
        if (n >= sc.Length)
            for (int i = kScope + envN; i < kScope + 2 * envN; i++)
            {
                deepest = Math.Max(deepest, sc[i]);
                if (armed && sc[i] > 1) { hits++; armed = false; }
                else if (!armed && sc[i] < 0.5f) armed = true;
            }
        float V(int i) => n > i ? sc[i] : 0f;
        return new DynamicsReading(E.DeviceText(trackId, deviceIndex, 0), Math.Round(V(4), 2), Db(V(0)), Db(V(2)), Db(V(1)), Db(V(3)),
            Db(V(5)), V(12) > 0.5f, Math.Round(V(6), 2), Math.Round(V(7), 1), (int)V(10), V(8), hits, Math.Round(deepest, 1));
    });

    public sealed record FilterMotion(string Summary, string Live, double CutoffHz, double CutoffRightHz, string CutoffNote,
        double BaseCutoffHz, double Resonance, double EnvelopeDb, bool EnvelopeHeld, double Lfo, double LfoPhaseDeg,
        double InputPeakDb, double OutputPeakDb, int Onsets, int OnsetsLast2s, double CutoffMinLast2sHz, double CutoffMaxLast2sHz,
        double SampleRate);

    [McpServerTool(Name = "read_filter_motion"), Description(
        "Read what a Nota Auto Filter (built-in effect kind 7) is doing right now: the modulated cutoff in Hz (left and "
        + "right — they differ with LFO Stereo) with its note and cents, the base cutoff the modulation moves from, the "
        + "live resonance (0..1), the envelope follower level in dBFS and whether Hold has it frozen, the LFO value (−1..1) "
        + "and phase, the input / output peaks, the onsets counted (input rising through −24 dBFS — what LFO Retrig "
        + "restarts on) in total and over the last 2 s, the cutoff's range over the last 2 s, and the one-line summary. "
        + "Values move only while audio plays through the track. Use get_device_text id 2 for what each parameter value means.")]
    public Task<FilterMotion> ReadFilterMotion(int trackId, int deviceIndex) => Read(() =>
    {
        const int tele = 16, hist = 2048;
        var sc = new float[tele + 2 * hist];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        static double HzOf(double norm) => Math.Round(30 * Math.Pow(600, Math.Clamp(norm, 0, 1)), 1);
        static double Db(float lin) => lin > 1e-6f ? Math.Round(20 * Math.Log10(lin), 1) : -120;
        int recent = 0; bool armed = true; double lo = 1, hi = 0;
        if (n >= sc.Length)
            for (int i = 0; i < hist; i++)
            {
                float env = sc[tele + i], cut = sc[tele + hist + i];
                if (armed && env >= 0.063f) { recent++; armed = false; } else if (!armed && env < 0.0316f) armed = true;
                lo = Math.Min(lo, cut); hi = Math.Max(hi, cut);
            }
        if (hi < lo) { lo = hi = V(0); }
        double hz = HzOf(V(0));
        double midi = 69 + 12 * Math.Log2(hz / 440.0);
        int note = (int)Math.Round(midi);
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        string noteName = $"{names[((note % 12) + 12) % 12]}{note / 12 - 1} {(int)Math.Round((midi - note) * 100):+0;-0;0} ct";
        return new FilterMotion(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1), hz, HzOf(V(1)), noteName,
            HzOf(E.DeviceGetParam(trackId, deviceIndex, 0)), Math.Round(V(2), 3), Db(V(3)), V(12) > 0.5f, Math.Round(V(4), 3),
            Math.Round(V(5) * 360, 1), Db(V(6)), Db(V(7)), (int)V(11), recent, HzOf(lo), HzOf(hi), V(9));
    });

    public sealed record VintageReading(string Summary, string Live, string Character, double InputPeakDb, double OutputPeakDb,
        double ThdPercent, double TestLevelDb, double[] HarmonicsDb, double Asymmetry, double WowCents, double WowHz,
        double FlutterCents, double FlutterHz, double PitchMinCentsLast4s, double PitchMaxCentsLast4s, double BandLimitHz,
        double HissDbfs, double AutoCompDb, double TapeDelayMs, double SampleRate);

    [McpServerTool(Name = "read_vintage"), Description(
        "Read what a Nota Vintage (built-in effect kind 8) is doing right now: the character (Vinyl / Cassette / Reel / VHS / "
        + "Tube / Analog), the input / output peaks, the THD of its saturation at the input's level (or −6 dBFS when silent) with "
        + "the 2nd…7th harmonics in dB relative to the fundamental, the curve's asymmetry (0..1 — even harmonics), the wow and "
        + "flutter depth in ± cents with their rates, the pitch drift's range over the last ~4 s, the band-limit corner (0 = open), "
        + "the hiss level in dBFS, the auto-comp gain and the tape delay the wow adds. Peaks and the pitch history move only "
        + "while audio plays through the track. Use get_device_text id 2 for what each parameter value means.")]
    public Task<VintageReading> ReadVintage(int trackId, int deviceIndex) => Read(() =>
    {
        const int tele = 32, curve = 48, hist = 1024;
        var sc = new float[tele + curve + 2 * hist];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        static double Db(float lin) => lin > 1e-6f ? Math.Round(20 * Math.Log10(lin), 1) : -120;
        string[] modes = { "Vinyl", "Cassette", "Reel", "VHS", "Tube", "Analog" };
        double lo = 0, hi = 0;
        if (n >= sc.Length)
            for (int i = 0; i < hist; i++)
            {
                double c = sc[tele + curve + i] + sc[tele + curve + hist + i];
                lo = Math.Min(lo, c); hi = Math.Max(hi, c);
            }
        var h = new double[6];
        for (int k = 0; k < 6; k++) h[k] = Math.Round(V(14 + k), 1);
        return new VintageReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1),
            modes[Math.Clamp((int)V(23), 0, 5)], Db(V(0)), Db(V(1)), Math.Round(V(12) * 100, 2), Db(V(20)), h, Math.Round(V(21), 3),
            Math.Round(V(8), 1), Math.Round(V(6), 3), Math.Round(V(9), 1), Math.Round(V(7), 2), Math.Round(lo, 1), Math.Round(hi, 1),
            Math.Round(V(10)), Math.Round(V(11), 1), Math.Round(V(22), 1), V(3) > 0 ? Math.Round(V(5) / V(3) * 1000, 2) : 0, V(3));
    });

    public sealed record ValveResponsePoint(double Hz, double Db);
    public sealed record ValveReading(string Summary, string Live, string Model, double InputPeakDb, double OutputPeakDb,
        double ThdPercent, double TestLevelDb, double[] HarmonicsDb, double AliasingDb, int Oversampling, double Drive,
        bool GateOpen, double GateThresholdDb, double AutoCompDb, double MidHz, double CabLossAt4kDb, double MicDistanceCm,
        ValveResponsePoint[] ToneStack, ValveResponsePoint[] Cabinet, double SampleRate);

    [McpServerTool(Name = "read_valve"), Description(
        "Read what a Nota Valve (built-in effect kind 6, the guitar amp) is doing right now: the model (Clean / Boost / Blues / Rock / "
        + "Lead / Heavy / Bass), the input / output peaks, the preamp's THD at the input's level (or −6 dBFS when silent) with the "
        + "2nd…7th harmonics in dB relative to the fundamental, the estimated aliasing of a 2.5 kHz tone at the current oversampling, "
        + "the effective drive, whether the gate is open and its threshold, the auto-comp gain, the middle's frequency, how much the "
        + "mic placement costs at 4 kHz against on-axis, the mic distance, and the tone stack's and the cabinet's magnitude responses "
        + "(dB at 60, 120, 250, 500, 1k, 2k, 4k, 8k Hz — the cabinet including the low / high cuts). Peaks and the gate move only "
        + "while audio plays through the track. Use get_device_text id 2 for what each parameter value means.")]
    public Task<ValveReading> ReadValve(int trackId, int deviceIndex) => Read(() =>
    {
        const int tele = 32, resp = 96;
        var sc = new float[tele + 4 * resp];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        static double Db(float lin) => lin > 1e-6f ? Math.Round(20 * Math.Log10(lin), 1) : -120;
        string[] models = { "Clean", "Boost", "Blues", "Rock", "Lead", "Heavy", "Bass" };
        double lo = V(24) > 0 ? V(24) : 30, hi = V(25) > 0 ? V(25) : 16000;
        ValveResponsePoint[] Curve(int at)
        {
            double[] hz = { 60, 120, 250, 500, 1000, 2000, 4000, 8000 };
            var pts = new ValveResponsePoint[hz.Length];
            for (int i = 0; i < hz.Length; i++)
            {
                double t = Math.Clamp(Math.Log(hz[i] / lo) / Math.Log(hi / lo) * (resp - 1), 0, resp - 1);
                int a = (int)Math.Floor(t), b = Math.Min(resp - 1, a + 1);
                double db = n >= sc.Length ? sc[at + a] + (sc[at + b] - sc[at + a]) * (t - a) : 0;
                pts[i] = new ValveResponsePoint(hz[i], Math.Round(db, 1));
            }
            return pts;
        }
        var h = new double[6];
        for (int k = 0; k < 6; k++) h[k] = Math.Round(V(6 + k), 1);
        return new ValveReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1),
            models[Math.Clamp((int)V(17), 0, 6)], Db(V(0)), Db(V(1)), Math.Round(V(4) * 100, 2), Db(V(12)), h, Math.Round(V(15), 1),
            Math.Max(1, (int)V(18)), Math.Round(V(16), 2), V(14) > 0.5f, Math.Round(V(23), 1), Math.Round(V(13), 1), Math.Round(V(20)),
            Math.Round(V(19), 1), Math.Round(V(27), 1), Curve(tele), Curve(tele + 2 * resp), V(3));
    });

    public sealed record UtilityBand(double Hz, double Pan, double Width, double Correlation, double LevelDb, double SetWidthPercent);
    public sealed record UtilityReading(string Summary, string Live, string Meter, double InLevel, double OutLevel, double DeltaDb,
        double InLufsShort, double OutLufsShort, double InPeakDb, double OutPeakDb, double InRmsDb, double OutRmsDb, double TruePeakDb,
        double Correlation, double WidthPercent, double ConfiguredWidthPercent, double EnergyBalanceDb, double AutoMatchDb, double LimiterDb,
        double MonoHz, UtilityBand[] Bands, double SampleRate);

    [McpServerTool(Name = "read_utility"), Description(
        "Read what a Nota Utility (built-in effect kind 4 — routing, stereo field and levels) is measuring right now: the input and "
        + "output level in the Meter's unit (LUFS-S / Peak / RMS) and their delta, short-term loudness (LUFS, 3 s), sample peak "
        + "(dBFS, 1 s hold) and RMS (dBFS, 300 ms) of both, the output true peak (dBTP, 3 s hold), the output correlation (−1..+1), "
        + "its width (side against mid, %; 100 = uncorrelated), the configured width above the mono cutoff, the L/R energy balance "
        + "(dB, + = left louder), the auto-match gain, the true-peak limiter's reduction, the mono cutoff (0 = off) and eight bands "
        + "of the output's stereo field over the last second (pan −1 L … +1 R, width share 0 mono … 0.71 uncorrelated … 1 "
        + "anti-phase, correlation, level dB, and the width the settings give that band in %). Levels move only while audio plays "
        + "through the track (−120 = silence). Use get_device_text id 2 for what each parameter value means, device_action 0 to "
        + "gain match.")]
    public Task<UtilityReading> ReadUtility(int trackId, int deviceIndex) => Read(() =>
    {
        const int tele = 48, hist = 80, bands = 32, resp = 96;
        const int bandAt = tele + 6 * hist;
        var sc = new float[bandAt + 5 * bands + resp];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        float L(int i) => n > i ? sc[i] : -120f;
        static double R1(double v) => Math.Round(v, 1);
        string[] meters = { "LUFS-S", "Peak", "RMS" };
        double inL = L(16), outL = L(17);
        // Eight bands: every fourth analysis band, their centres ~ 45 Hz … 11 kHz.
        var outBands = new List<UtilityBand>();
        if (n >= bandAt + 5 * bands)
        {
            double lo = V(28) > 0 ? V(28) : 30, hi = V(29) > lo ? V(29) : 16000;
            for (int b = 1; b < bands; b += 4)
            {
                double hz = lo * Math.Pow(hi / lo, (b + 0.5) / bands);
                outBands.Add(new UtilityBand(Math.Round(hz), Math.Round(sc[bandAt + b], 2), Math.Round(sc[bandAt + bands + b], 2),
                    Math.Round(sc[bandAt + 2 * bands + b], 2), R1(sc[bandAt + 3 * bands + b]), Math.Round(sc[bandAt + 4 * bands + b])));
            }
        }
        return new UtilityReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1),
            meters[Math.Clamp((int)V(24), 0, 2)], R1(inL), R1(outL), inL > -119 && outL > -119 ? R1(outL - inL) : 0,
            R1(L(5)), R1(L(6)), R1(L(9)), R1(L(10)), R1(L(7)), R1(L(8)), R1(L(11)),
            Math.Round(V(4), 2), Math.Round(V(21)), Math.Round(V(22)), R1(V(20)), R1(V(14)), R1(V(15)), Math.Round(V(23)),
            outBands.ToArray(), V(3));
    });

    public sealed record ShutterPoint(double Ms, double InputDb, double Gain);
    public sealed record ShutterReading(string Summary, string Live, string Mode, string State, double Gain, double ReductionDb,
        double PeakReductionDb, double InputDb, double OutputDb, double DetectorDb, double KeyDb, bool ExternalKey, int KeySourceTrackId,
        double ThresholdDb, double CloseDb, double FloorDb, double OpenShare, double WindowSeconds, int OpeningsLastBar, int OpeningsSinceReset,
        int LatencySamples, ShutterPoint[] History, double SampleRate);

    [McpServerTool(Name = "read_shutter"), Description(
        "Read what a Nota Shutter (built-in effect kind 19 — noise gate / ducker) is doing right now: Gate or Duck, its state "
        + "(closed / attack / open / hold / release), the gain it applies (0..1) and the reduction now and at its deepest since the "
        + "last reset (dB, positive), the input, output, detector (filtered key) and key levels (dBFS, −120 = silence), whether an "
        + "external sidechain drives it and the source track, the threshold, the level it closes at (threshold − return) and the "
        + "floor (−120 = −∞), the share of the window it was open (ducking, for Duck), the openings in the last bar and since the "
        + "reset, the look-ahead latency, and the window's history in 16 steps (ms before now, input peak dB, lowest gain). Levels "
        + "move only while audio plays through the track. Use get_device_text id 2 for what each parameter value means, "
        + "set_device_sidechain to route a key (External Key must be on, its default), device_action 0 to reset the meters.")]
    public Task<ShutterReading> ReadShutter(int trackId, int deviceIndex) => Read(() =>
    {
        const int tele = 24, hist = 128;
        var sc = new float[tele + 3 * hist];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        static double R1(double v) => Math.Round(v, 1);
        string[] states = { "closed", "attack", "open", "hold", "release" };
        bool duck = E.DeviceGetParam(trackId, deviceIndex, 7) >= 0.5f;
        double win = V(20) > 0 ? V(20) : 1;
        var pts = new List<ShutterPoint>();
        if (n >= sc.Length)
            for (int k = 0; k < 16; k++)
            {
                int a = k * hist / 16, b = (k + 1) * hist / 16;
                double pk = -120, g = 1;
                for (int i = a; i < b; i++) { pk = Math.Max(pk, sc[tele + i]); g = Math.Min(g, sc[tele + hist + i]); }
                pts.Add(new ShutterPoint(Math.Round((1 - (double)b / hist) * win * 1000), R1(pk), Math.Round(g, 3)));
            }
        return new ShutterReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1),
            duck ? "Duck" : "Gate", states[Math.Clamp((int)Math.Round(V(5)), 0, 4)], Math.Round(V(1), 3), R1(V(2)), R1(V(18)),
            R1(V(0)), R1(V(6)), R1(V(3)), R1(V(14)), V(13) > 0.5f, E.DeviceSidechainSource(trackId, deviceIndex),
            R1(V(15)), R1(V(16)), R1(V(17)), Math.Round(V(7), 2), win, (int)V(8), (int)V(9), (int)V(12), pts.ToArray(), V(10));
    });

    public sealed record PitchPoint(double Ms, double DetectedMidi, double OutputMidi, double MidiTarget);
    public sealed record KeyGuess(string Key, string Scale, double Match);
    public sealed record AutoShiftReading(string Summary, string Live, string KeySource, bool Voiced, bool Sibilant,
        double DetectedMidi, string DetectedNote, double DetectedCents, double Hz, double Clarity, double TargetMidi, string TargetNote,
        double CorrectionCents, double Ratio, double InputDb, string Scale, string[] ScaleNotes, double InScale, double[] SungPitchClasses,
        double AnalysedSeconds, KeyGuess? Best, KeyGuess? RunnerUp, bool Learning, int MidiSourceTrackId, bool MidiLive, int MidiNote,
        int MidiVelocity, int MidiHeld, int LatencySamples, double WindowSeconds, PitchPoint[] History, double SampleRate);

    [McpServerTool(Name = "read_auto_shift"), Description(
        "Read what a Nota Auto Shift (built-in effect kind 10 — vocal pitch correction) is doing right now: the key source "
        + "(Auto / Manual / MIDI), whether the input is voiced or a sibilant, the detected pitch (fractional MIDI, note name, cents, Hz, "
        + "clarity 0..1), the target note, the correction being applied (cents) and the pitch ratio incl. Shift / Fine, the input level, "
        + "the active scale and its notes (absolute), the share of what was sung that lies in it, the sung pitch-class histogram (C..B, "
        + "max 1, the last ~16 bars) with the seconds it holds, the best and runner-up key (Krumhansl profile match 0..1), whether Learn "
        + "is running, the MIDI source track and the note it holds (Key Source MIDI), the latency, and the last ~2 s of pitch in 16 steps "
        + "(ms before now, detected, output and MIDI-target MIDI note numbers; 0 = none). Pitch moves only while audio plays through the "
        + "track. Use get_device_text id 2 for what each parameter value means, device_action 1 to Learn the key, set_device_sidechain "
        + "to pick the MIDI source.")]
    public Task<AutoShiftReading> ReadAutoShift(int trackId, int deviceIndex) => Read(() =>
    {
        const int tele = 32, hist = 384;
        string[] keys = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        string[] scales = { "Chromatic", "Major", "Minor", "Penta Maj", "Penta Min" };
        var sc = new float[tele + 12 + 3 * hist];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        static double R1(double v) => Math.Round(v, 1);
        string NoteName(double m) => m > 0.5 ? keys[((int)Math.Round(m) % 12 + 12) % 12] + ((int)Math.Round(m) / 12 - 1) : "";
        float P(int i) => E.DeviceGetParam(trackId, deviceIndex, i);
        float ks = P(8);
        string src = ks < 0.25f ? "Auto" : ks < 0.75f ? "Manual" : "MIDI";
        int mask = (int)V(26);
        var notes = new List<string>();
        for (int i = 0; i < 12; i++) if ((mask >> i & 1) != 0) notes.Add(keys[i]);
        string scaleName = P(17) >= 0.5f ? "Custom" : scales[Math.Clamp((int)Math.Round(P(1) * 4), 0, 4)];
        string scale = src == "MIDI" ? "MIDI" : keys[Math.Clamp((int)Math.Round(P(0) * 11), 0, 11)] + " " + scaleName;
        var pcs = new double[12];
        for (int i = 0; i < 12; i++) pcs[i] = Math.Round(V(tele + i), 3);
        KeyGuess? Guess(int k, int s, int m) => V(k) >= 0 && n > k ? new KeyGuess(keys[Math.Clamp((int)V(k), 0, 11)], V(s) > 1.5f ? "Minor" : "Major", Math.Round(V(m), 2)) : null;
        double win = V(24) > 0 ? V(24) : 2;
        var pts = new List<PitchPoint>();
        if (n >= sc.Length)
            for (int k = 0; k < 16; k++)
            {
                int a = k * hist / 16, b = (k + 1) * hist / 16;
                double Avg(int at) { double s = 0; int c = 0; for (int i = a; i < b; i++) if (sc[at + i] > 0) { s += sc[at + i]; c++; } return c > 0 ? R1(s / c) : 0; }
                pts.Add(new PitchPoint(Math.Round((1 - (double)b / hist) * win * 1000), Avg(tele + 12), Avg(tele + 12 + hist), Avg(tele + 12 + 2 * hist)));
            }
        double det = V(0);
        return new AutoShiftReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1), src,
            V(6) > 0.5f, V(7) > 0.5f, Math.Round(det, 2), NoteName(det), det > 0.5 ? Math.Round((det - Math.Round(det)) * 100) : 0, R1(V(5)), Math.Round(V(4), 2),
            Math.Round(V(1), 2), NoteName(V(1)), Math.Round(V(3)), Math.Round(V(28), 4), R1(V(29)), scale, notes.ToArray(), Math.Round(V(22), 2), pcs,
            R1(V(23)), Guess(16, 17, 18), Guess(19, 20, 21), V(15) > 0.5f, E.DeviceSidechainSource(trackId, deviceIndex), V(14) > 0.5f, (int)V(11),
            (int)V(12), (int)V(13), (int)V(10), R1(win), pts.ToArray(), V(8));
    });

    public sealed record BeatRepeatStep(double Beat, double InputDb, string Kind, double RepeatGain);
    public sealed record BeatRepeatReading(string Summary, string Live, string Mode, string State, int Pass, int Passes, bool RepeatHeld,
        double IntervalBeats, double SliceBeats, double SliceMs, double RepeatGain, double RepeatPitchSt, double FilterHz, double FilterOctaves,
        double InputDb, double RepeatDb, double OutputDb, double Bpm, int Bar, double BeatInBar, double IntervalPhase, int Bursts, int Skipped,
        bool Playing, double WindowBeats, BeatRepeatStep[] Timeline, double SampleRate);

    [McpServerTool(Name = "read_beat_repeat"), Description(
        "Read what a Nota Beat Repeat (built-in effect kind 11 — tempo-synced repeater / stutter) is doing right now: the mode "
        + "(Mix / Insert / Gate), its state (idle / capturing / repeating), which pass of the burst it is on (1 = the capture, 2.. = "
        + "repeats) out of how many (0 = held by Repeat until released), the interval and the slice (beats, slice also in ms; the "
        + "slice includes Variation's random step), the current repeat's gain, pitch (semitones) and filter (Hz, octaves), the input, "
        + "repeat and output levels (dBFS, −120 = silence), tempo, bar and beat, the interval phase (0..1), bursts and skipped "
        + "intervals since the last reset, and the timeline — the last two intervals in 32 steps (beat, input peak dB, dry / capture "
        + "/ repeat, repeat gain). Levels and the timeline move only while the transport plays through the track. Use get_device_text "
        + "id 2 for what each parameter value means, device_action 0 to reset, 1 to fire a repeat now; set the Repeat param to 1 to "
        + "hold one.")]
    public Task<BeatRepeatReading> ReadBeatRepeat(int trackId, int deviceIndex) => Read(() =>
    {
        const int tele = 32, cells = 64, wave = 128;
        var sc = new float[tele + 3 * cells + wave];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        static double R1(double v) => Math.Round(v, 1);
        string[] modes = { "Mix", "Insert", "Gate" };
        string[] states = { "idle", "capturing", "repeating" };
        string[] kinds = { "dry", "capture", "repeat" };
        int mode = Math.Clamp((int)Math.Round(E.DeviceGetParam(trackId, deviceIndex, 13) * 2), 0, 2);
        double win = V(8) > 0 ? V(8) : 8;
        var steps = new List<BeatRepeatStep>();
        if (n >= tele + 3 * cells)
            for (int k = 0; k < 32; k++)
            {
                int a = k * cells / 32, b = (k + 1) * cells / 32;
                double pk = -120, g = 0; int kind = 0;
                for (int i = a; i < b; i++)
                {
                    pk = Math.Max(pk, sc[tele + i]);
                    int kk = Math.Clamp((int)Math.Round(sc[tele + cells + i]), 0, 2);
                    if (kk > kind) kind = kk;
                    g = Math.Max(g, sc[tele + 2 * cells + i]);
                }
                steps.Add(new BeatRepeatStep(Math.Round((double)a / cells * win, 3), R1(pk), kinds[kind], Math.Round(g, 3)));
            }
        return new BeatRepeatReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1),
            modes[mode], states[Math.Clamp((int)Math.Round(V(3)), 0, 2)], (int)V(4), (int)V(5), V(18) > 0.5f,
            Math.Round(V(9), 3), Math.Round(V(10), 4), R1(V(11)), Math.Round(V(19), 3), R1(V(20)), Math.Round(V(21)), Math.Round(V(22), 2),
            R1(V(0)), R1(V(1)), R1(V(2)), Math.Round(V(12), 2), (int)V(13), Math.Round(V(14), 2), Math.Round(V(6), 3), (int)V(23), (int)V(29),
            V(24) > 0.5f, R1(win), steps.ToArray(), V(15));
    });

    public sealed record AnalyzerBand(double Hz, double Db);
    public sealed record AnalyzerPeak(double Hz, double Db, string Note);
    public sealed record AnalyzerReading(string Summary, string Scope, string Cursors, AnalyzerBand[] Bands, AnalyzerPeak[] Peaks);

    [McpServerTool(Name = "read_analyzer"), Description(
        "Read what a Nota Lens (built-in effect kind 22) is measuring on a track right now: a summary line "
        + "(loudest spectral peak, RMS, crest factor, momentary LUFS, L/R correlation), the scope measurements "
        + "(trigger state, window, period and frequency, Vpp, Vrms), the A/B cursor measurements, the 31 "
        + "third-octave band levels in dB, and the strongest spectral peaks with their note names. The reading "
        + "reflects the Lens's own parameters — FFT size, window, averaging, tilt, source (L+R / L / R, or "
        + "Mid/Side) and Freeze — so set those with set_device_param first. Add a Lens with add_device (kind 22) "
        + "on the track you want to measure; the analyzer passes audio through untouched.")]
    public Task<AnalyzerReading> ReadAnalyzer(int trackId, int deviceIndex) => Read(() =>
    {
        var bands = new List<AnalyzerBand>();
        foreach (var line in E.DeviceText(trackId, deviceIndex, 1).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t');
            if (f.Length >= 2 && Num(f[0]) is { } hz && Num(f[1]) is { } db) bands.Add(new AnalyzerBand(hz, db));
        }
        var peaks = new List<AnalyzerPeak>();
        foreach (var line in E.DeviceText(trackId, deviceIndex, 3).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t');
            if (f.Length >= 3 && Num(f[0]) is { } hz && Num(f[1]) is { } db) peaks.Add(new AnalyzerPeak(hz, db, f[2].Trim()));
        }
        return new AnalyzerReading(
            E.DeviceText(trackId, deviceIndex, 0),
            E.DeviceText(trackId, deviceIndex, 2),
            E.DeviceText(trackId, deviceIndex, 4),
            bands.ToArray(), peaks.ToArray());
    });

    // "20 Hz" / "−18.4 dB" → the number, or null when the field is not one.
    private static double? Num(string s)
    {
        var t = s.Trim().Replace('−', '-');
        int end = 0;
        while (end < t.Length && (char.IsDigit(t[end]) || t[end] is '-' or '+' or '.')) end++;
        return double.TryParse(t[..end], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
