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
        (16, "Nota EQ-3"), (17, "Forge"), (18, "Nota Level"), (19, "Nota Shutter"), (20, "Nota Chamber"), (21, "Nota Prism"),
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

    [McpServerTool(Name = "get_device_text"), Description("Read a device's resource text. Nota Chamber: id 0 = current IR name, 1 = its category, 2 = the loaded user IR's name, 10 = the built-in IR list (name, category, seconds per line; the IR param selects entry round(v × 16), 1.0 = the user IR). Nota Lens: id 0 = the analysis summary, 1 = the third-octave band table, 2 = the scope measurements, 3 = the strongest spectral peaks, 4 = the A/B cursor measurements — or use read_analyzer, which returns all of it parsed. Delay (kind 3): id 0 = a one-line summary of what it is doing now (sync division or free times, ping-pong, feedback, mix, or that it is frozen). Reverb (kind 2): id 0 = a one-line summary (algorithm, RT60, pre-delay, diffusion, mix, early reflections / vintage, or that the tail is frozen). Compressor (kind 1): id 0 = a one-line summary (character, threshold, ratio, knee, attack / release, detector, range, mix, the reduction right now, external key / unlinked / Listen) — or use read_dynamics for the numbers. Auto Filter (kind 7): id 0 = a one-line summary (type, slope, cutoff, Q, what the envelope and LFO drive and by how much, drive, mix, sidechain key), 1 = the live reading (modulated cutoff with its note, resonance, envelope level, LFO value and phase, onsets counted), 2 = a guide to what each 0..1 parameter value means — or use read_filter_motion for the numbers. Nota Vintage (kind 8): id 0 = a one-line summary (character, drive, tone model and shelves, wow / flutter with their rates, noise, crackle, wear, stage, mix, output, oversampling, auto-comp), 1 = the live reading (in / out peaks, THD with the 2nd and 3rd harmonics, asymmetry, wow and flutter in cents, band-limit, hiss level, auto-comp gain), 2 = a guide to what each 0..1 parameter value means — or use read_vintage for the numbers. Nota Valve (kind 6): id 0 = a one-line summary (model, gain, the tone stack with the middle's frequency, bright / deep, even-only, cabinet with mic, distance, axis and position, low / high cut, gate, mix, output, oversampling, auto-comp), 1 = the live reading (in / out peaks, THD with the 2nd and 3rd harmonics, estimated aliasing, drive, gate open / closed, auto-comp gain, the cab's loss at 4 kHz vs on-axis), 2 = a guide to the parameter values and units — or use read_valve for the numbers. Nota Utility (kind 4): id 0 = a one-line summary (channel mode, width and its L/R or M/S law, mono below with its slope, balance, phase, mute, gain, auto match and its reference, true-peak limit), 1 = the live reading (in / out LUFS-S, peak and RMS, true peak, the delta in the meter's unit, correlation, width, energy balance, auto-match gain, limiter reduction), 2 = a guide to the parameter values and units — or use read_utility for the numbers. Nota Shutter (kind 19): id 0 = a one-line summary (Gate / Duck, threshold, return, shape with attack / hold / release, floor, lookahead, retrigger, internal / external key, the key filter band, peak hold, Listen), 1 = the live reading (state, gain, reduction now and peak, in / out / detector levels, the share of the window it was open, openings in the last bar and since reset, key level, latency), 2 = a guide to what each 0..1 parameter value means — or use read_shutter for the numbers. Nota Auto Shift (kind 10): id 0 = a one-line summary (key and scale or the MIDI target settings, key source, amount, speed, range, human, shift and fine, formant preserve and shift, mix, detection range and sensitivity, skip sibilants, follow), 1 = the live reading (detected note and cents, frequency, clarity, target and correction, ratio, input level, MIDI note held, share sung in scale, the best and runner-up key, Learn running, latency), 2 = a guide to what each 0..1 parameter value means — or use read_auto_shift for the numbers. Nota Beat Repeat (kind 11): id 0 = a one-line summary (mode, interval, grid, offset, gate, chance, variation, pitch and pitch decay, decay, volume, the repeat filter with its type, band and narrowing, mix, Repeat held, latch), 1 = the live reading (idle / capturing / repeating, the pass of how many, the slice in ms and beats, the repeat's gain, pitch and filter, in / repeat / out levels, bar and beat, bursts and skipped intervals since reset), 2 = a guide to what each 0..1 parameter value means — or use read_beat_repeat for the numbers. Nota Dynamic EQ-8 (kind 13): id 0 = a one-line summary (bands on and dynamic, the Dynamic switch, each band on with its type, frequency, gain, Q and — for a dynamic band — mode, threshold, range, attack / release and key, solo, output, whether a key track is routed), 1 = the live reading (in / out peaks, whether a key arrives, each band's detector level and — for a dynamic band — its threshold and the gain it has now of its range, CPU), 2 = a guide to the parameter layout and units — or use read_dynamic_eq for the numbers and set_dynamic_eq_band to edit a band. Nota Ceiling (kind 14): id 0 = a one-line summary (character, gain, ceiling and whether it is true-peak, release or auto, look-ahead, stereo link, key high-pass, key track, the loudness target, Delta), 1 = the live reading (gain reduction now, its mean and deepest over the last 4 s and since reset, in / out peaks and their holds, true peak, the share of time over the ceiling, the release in use and its range, transients that reached the clip, LUFS M / S / I and the distance to the target, LRA, PLR, latency), 2 = a guide to the parameter values and units — or use read_ceiling for the numbers. Nota Crush (kind 12): id 0 = a one-line summary (mode, bits, rate with the hold, drive, wet, dither / jitter / noise, post filter, output, anti-alias, auto gain, DC filter), 1 = the live reading (in / out peak and RMS, crest, peak holds, bits with the level count and quantisation noise, the reduced rate, hold and Nyquist, THD+N, images above the Nyquist, the driven peak and folds, auto gain, CPU), 2 = a guide to what each 0..1 parameter value means — or use read_crush for the numbers. Nota Orbit (kind 9): id 0 = a one-line summary (waveform, free rate in Hz or the Sync division, shape / glide, phase with the mode it makes — tremolo, auto-pan or offset pan — amount, mix), 1 = the live reading (the LFO rate and period, locked to the song or free-running, its place in the two-cycle window, the L / R gain now, the pan and its swing, the floor, in / out levels, tempo and bar, CPU), 2 = a guide to what each 0..1 parameter value means incl. the Division table — or use read_orbit for the numbers. Nota EQ-3 (kind 16): id 0 = a one-line summary (slope, the two crossovers, each band's gain or kill, the fader range, output), 1 = the live reading (in / out peaks, each band's level after its gain and the gain in effect now, the crossovers in use, CPU), 2 = a guide to what each 0..1 parameter value means — or use read_eq3 for the numbers and set_eq3 to play it in dB and Hz. Nota Forge (kind 17): id 0 = a one-line summary (routing, amount, wet, output, each stage on with its type, role, drive, out, feedback, bias, tone and width, the LFO → drive with its rate, env → tone, the master tone / bias / width offsets when set, oversampling and an aliasing warning), 1 = the live reading (in / out peak and RMS, THD of a −6 dB sine through the device and through each stage with its flavour, the LFO value, drive offset and rate, the envelope, the master tilt now, oversampling, latency, CPU), 2 = a guide to what each 0..1 parameter value means — or use read_forge for the numbers and the curve, set_forge and set_forge_stage to play it in units. Nota EQ-8 (kind 0): id 0 = a one-line summary (bands on, scale, each band on with its type, frequency, gain, Q, slope for the cuts and channel when not stereo, output, auto gain, analyzer), 1 = the live reading (in / out peaks, the auto gain in effect, CPU), 2 = a guide to the parameter layout and units — or use read_eq8 for the numbers, set_eq8_band to edit a band and set_eq8 for the globals.")]
    public Task<string> GetDeviceText(int trackId, int deviceIndex, int id) => Read(() => E.DeviceText(trackId, deviceIndex, id));

    [McpServerTool(Name = "device_action"), Description("Run a device's own command — the few things that are actions rather than parameters. Delay (kind 3): id 0 clears the loop (empties the delay buffer, so whatever is still circulating stops; useful after Freeze). Reverb (kind 2): id 0 kills the tail (empties the reverb's buffers, so whatever is still ringing — or frozen — stops). Auto Filter (kind 7): id 0 restarts the LFO (from its start phase; in Sync the cycle is re-anchored to the current beat), id 1 resets the envelope follower (drops a held peak). Nota Vintage (kind 8): id 0 resets the wear (restarts the wow and flutter at their zero phase and silences a ringing crackle). Nota Valve (kind 6): id 0 resets the amp (clears the filters, the gate and the auto-comp). Nota Utility (kind 4): id 0 gain match — moves Gain once so the output meets the reference (the input's level, or Target when Match To = 1) over the last 3 s in the Meter's unit; id 1 resets the meters (level history, holds and the auto-match ride). Nota Shutter (kind 19): id 0 resets the meters (peak reduction, the opening count, the history); id 1 sets the history window read_shutter and the card show (intArg 0 = 250 ms, 1 = 1 s, 2 = 4 s). Nota Auto Shift (kind 10): id 0 resets the analysis (the sung-note histogram and the pitch history); id 1 is Learn — intArg 1 starts listening (clears the histogram), 0 stops and sets Key + Scale (Major / Minor) to the best match and Key Source to Manual, 2 cancels. Nota Beat Repeat (kind 11): id 0 resets (stops the repeat, clears the timeline, the meters and the burst count); id 1 fires a repeat now, as if the interval hit (ignores Chance; while the transport plays — to hold one, set the Repeat param to 1). Nota Ceiling (kind 14): id 0 resets the peaks (the peak and true-peak holds, the max reduction, the clip count and the 4 s level window); id 1 resets the loudness (integrated, LRA, the 60 s window) and the peaks. Nota Crush (kind 12): id 0 resets the meters (the peak holds and the spectrum average). Nota Orbit (kind 9): id 0 restarts the LFO (free run: from phase 0; with Sync on and the transport playing it stays locked to the bar), id 1 resets the meters. Nota EQ-3 (kind 16): id 0 resets the meters (the peaks and the spectrum average). Nota Forge (kind 17): id 0 restarts the LFO (free run: from phase 0) and resets the meters. Nota EQ-8 (kind 0): id 0 resets the meters (the peaks and the spectrum averages). Devices without a command ignore the call.")]
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

    public sealed record CeilingCell(double Seconds, double InputDb, double OutputDb, double ReductionDb, bool Clipped);
    public sealed record CeilingLoudness(double Seconds, double MomentaryLufs, double ShortTermLufs, double IntegratedLufs);
    public sealed record CeilingReading(string Summary, string Live, string Character, double CeilingDb, bool TruePeakMode, bool Delta,
        double ReductionDb, double AvgReductionDb, double MaxReductionDb, double MaxReductionHoldDb, double InputDb, double OutputDb,
        double PeakInHoldDb, double PeakOutHoldDb, double TruePeakHoldDb, double OverShare, double ReleaseMs, double ReleaseMinMs,
        double ReleaseMaxMs, int Clips, double MomentaryLufs, double ShortTermLufs, double IntegratedLufs, double TargetLufs,
        double FromTargetLu, double LraLu, double PlrDb, double MeasuredSeconds, int LatencySamples, double SampleRate,
        CeilingCell[] Level, CeilingLoudness[] Loudness);

    [McpServerTool(Name = "read_ceiling"), Description(
        "Read what a Nota Ceiling (built-in effect kind 14 — look-ahead brick-wall limiter with a loudness meter) is doing right "
        + "now: the character, the ceiling (dBTP when TruePeakMode), Delta (playing what it removes), the gain reduction now, its "
        + "mean (while there is signal) and deepest over the last 4 s and since the peak reset, the input / output peaks now and "
        + "held since the reset, the output true-peak hold, the share of the last 4 s the input was over the ceiling (0..1), the "
        + "release in use and its range, transients that reached the clip stage, LUFS momentary / short-term / integrated, the "
        + "target and the distance to it (LU, + = louder), loudness range (LRA, LU), PLR (true-peak hold − integrated), seconds "
        + "measured, latency; plus the level window (the last 4 s in 16 steps: input, output, reduction, clipped) and the loudness "
        + "window (the last 60 s in 20 steps). Levels are dBFS / LUFS, −120 = silence or not measured yet. The meters move only "
        + "while audio runs through the track. Use device_action 0 to reset the peaks, 1 the loudness; get_device_text id 2 for "
        + "what each parameter value means.")]
    public Task<CeilingReading> ReadCeiling(int trackId, int deviceIndex) => Read(() =>
    {
        const int tele = 40, lvl = 64, loud = 120, lvlAt = tele, loudAt = tele + 7 * lvl;
        var sc = new float[loudAt + 3 * loud];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        float Db(int i) => n > i ? sc[i] : -120f;
        static double R1(double v) => Math.Round(v, 1);
        string[] chars = { "Clean", "Punch", "Glue" };
        float Pm(int p) => E.DeviceGetParam(trackId, deviceIndex, p);
        var cells = new List<CeilingCell>();
        var louds = new List<CeilingLoudness>();
        if (n >= sc.Length)
        {
            for (int k = 0; k < 16; k++)
            {
                double pin = -120, pout = -120, gr = 0; bool clip = false;
                for (int i = k * lvl / 16; i < (k + 1) * lvl / 16; i++)
                {
                    pin = Math.Max(pin, sc[lvlAt + i]); pout = Math.Max(pout, sc[lvlAt + lvl + i]);
                    gr = Math.Max(gr, sc[lvlAt + 2 * lvl + i]); clip |= sc[lvlAt + 3 * lvl + i] > 0.5f;
                }
                cells.Add(new CeilingCell(Math.Round(-4.0 + (k + 1) * 0.25, 2), R1(pin), R1(pout), R1(gr), clip));
            }
            for (int k = 0; k < 20; k++)
            {
                int i = (k + 1) * loud / 20 - 1;
                louds.Add(new CeilingLoudness(-60 + (k + 1) * 3, R1(sc[loudAt + i]), R1(sc[loudAt + loud + i]), R1(sc[loudAt + 2 * loud + i])));
            }
        }
        double target = Pm(10), lufsI = Db(5);
        return new CeilingReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1),
            chars[Math.Clamp((int)Math.Round(Pm(4)), 0, 2)], R1(Pm(0)), Pm(7) >= 0.5f, Pm(8) >= 0.5f,
            R1(V(2)), R1(V(14)), R1(V(15)), R1(V(9)), R1(Db(0)), R1(Db(1)), R1(Db(7)), R1(Db(8)), R1(Db(10)), Math.Round(V(13), 3),
            Math.Round(V(16)), Math.Round(V(17)), Math.Round(V(18)), (int)V(19), R1(Db(3)), R1(Db(4)), R1(lufsI), R1(target),
            lufsI > -119 ? R1(lufsI - target) : 0, R1(V(11)), R1(V(12)), R1(V(30)), (int)V(22), V(20), cells.ToArray(), louds.ToArray());
    });

    public sealed record CrushBand(double Hz, double InputDb, double OutputDb);
    public sealed record CrushReading(string Summary, string Live, string Mode, double Bits, double Levels, double QuantNoiseDb,
        double RateHz, double HoldSamples, double NyquistHz, double DriveDb, double Wet, double PostFilterHz, bool AntiAlias,
        bool AutoGain, bool DcFilter, double InputPeakDb, double OutputPeakDb, double InputRmsDb, double OutputRmsDb, double CrestDb,
        double InputPeakHoldDb, double OutputPeakHoldDb, double ThdNPercent, double ImagesDb, double DrivenPeak, int Folds,
        double AutoGainDb, bool Signal, bool SpectrumValid, double SampleRate, CrushBand[] Spectrum);

    [McpServerTool(Name = "read_crush"), Description(
        "Read what a Nota Crush (built-in effect kind 12 — bit crusher) is doing right now: the mode (Digital / Analog / Fold), "
        + "the bit depth with its level count and the theoretical quantisation noise, the reduced sample rate with the samples "
        + "each value is held and its Nyquist, drive, wet, the post filter, anti-alias / auto gain / DC filter; the meters — "
        + "input and output peak and RMS (dBFS), the output crest factor, the peak holds since the reset; what the crush does "
        + "to the sound, from an FFT of the driven input against the crushed output — THD+N (the content it added, % of the "
        + "level-matched input), the images above the reduced Nyquist (dB against the whole output, −120 = none), the driven "
        + "peak (linear) and how many times Fold folds it, the auto-gain correction; plus the spectrum in 20 log bands "
        + "(input level-matched to the output, dB). Levels read −120 while silent; the meters move only while audio runs "
        + "through the track. device_action 0 resets the meters; get_device_text id 2 explains each parameter value.")]
    public Task<CrushReading> ReadCrush(int trackId, int deviceIndex) => Read(() =>
    {
        const int tele = 32, bands = 40;
        var sc = new float[tele + 2 * bands];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        float Db(int i) => n > i ? sc[i] : -120f;
        static double R1(double v) => Math.Round(v, 1);
        float Pm(int p) => E.DeviceGetParam(trackId, deviceIndex, p);
        string[] modes = { "Digital", "Analog", "Fold" };
        var spec = new List<CrushBand>();
        if (n >= sc.Length)
            for (int k = 0; k < 20; k++)
            {
                double pi = 0, po = 0;
                for (int b = 2 * k; b < 2 * k + 2; b++) { pi += Math.Pow(10, sc[tele + b] / 10); po += Math.Pow(10, sc[tele + bands + b] / 10); }
                double hz = 20 * Math.Pow(1000, (k + 0.5) / 20);
                spec.Add(new CrushBand(Math.Round(hz), R1(Math.Max(-120, 10 * Math.Log10(Math.Max(1e-12, pi)))), R1(Math.Max(-120, 10 * Math.Log10(Math.Max(1e-12, po))))));
            }
        return new CrushReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1),
            modes[Math.Clamp((int)Math.Round(Pm(2) * 2), 0, 2)], R1(V(8)), Math.Round(V(9)), R1(V(10)),
            Math.Round(V(6)), V(7), Math.Round(V(20)), R1(-12 + Pm(10) * 36), Math.Round(Pm(7), 3), Math.Round(V(21)),
            Pm(8) >= 0.5f, Pm(11) >= 0.5f, Pm(12) >= 0.5f, R1(Db(0)), R1(Db(1)), R1(Db(2)), R1(Db(3)), R1(V(4)), R1(Db(25)), R1(Db(24)),
            R1(V(11) * 100), R1(Db(12)), Math.Round(V(16), 3), (int)V(17), R1(V(13)), V(23) > 0.5f, V(22) > 0.5f, V(5), spec.ToArray());
    });

    public sealed record ForgeStage(int Stage, bool On, string Type, string Role, double DrivePercent, double OutDb, double FeedbackPercent,
        double BiasPercent, double ToneDb, double WidthPercent, double ThdPercent, string Flavor, double[] HarmonicsDb);
    public sealed record ForgeReading(string Summary, string Live, string Routing, double AmountDb, double Wet, double OutputDb,
        double LfoDrivePercent, double EnvTonePercent, bool LfoSync, string LfoRate, double LfoHz, string Oversampling,
        double MasterTonePercent, double MasterBiasPercent, double MasterWidthPercent, ForgeStage[] Stages, double ThdPercent,
        string Flavor, double[] HarmonicsDb, double[] TransferCurve, double InputPeakDb, double OutputPeakDb, double InputRmsDb,
        double OutputRmsDb, double Lfo, double DriveModulation, double Envelope, bool AliasingWarning, int LatencySamples,
        double CpuPercent, bool Signal, double SampleRate);

    private static readonly string[] ForgeAlgos = { "Tube", "Diode", "Tape", "Fuzz", "Digital", "Fold" };
    private static readonly string[] ForgeRoutes = { "Serial", "Parallel", "Mid/Side", "Multiband" };
    private static readonly string[] ForgeDivs = { "2/1", "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" };
    private static readonly string[] ForgeFlavors = { "clean", "odd-heavy", "even-heavy", "mixed" };

    [McpServerTool(Name = "read_forge"), Description(
        "Read a Nota Forge (built-in effect kind 17 — multi-stage saturator): the routing (Serial 1→2→3 / Parallel averaged / "
        + "Mid/Side — stage 1 the Mid, 2 the Side, 3 the recombined M+S / Multiband — stage 1 < 180 Hz, 2 the mids, 3 > 2.4 kHz), "
        + "Amount (dB into the stages), wet, output; each stage — on, type (Tube / Diode / Tape / Fuzz / Digital / Fold), its role, "
        + "drive %, out dB, feedback %, bias %, tone dB, width % and the THD and harmonics 2..9 (dB against the fundamental) of a "
        + "−6 dB sine through it alone; the same for the whole device (Flavor: clean / odd-heavy / even-heavy / mixed) with its "
        + "transfer curve (output at 17 inputs from −1 to +1); the modulation (LFO → drive with its rate, env → tone) and its live "
        + "values; the master Tone / Bias / Width offsets older projects carry; oversampling with the aliasing warning (Digital / "
        + "Fold without it); in / out peak and RMS (dBFS, −120 while silent), latency and CPU. set_forge / set_forge_stage change "
        + "it in units; get_device_text id 2 explains each parameter value.")]
    public Task<ForgeReading> ReadForge(int trackId, int deviceIndex) => Read(() => ForgeRead(trackId, deviceIndex));

    private ForgeReading ForgeRead(int trackId, int deviceIndex)
    {
        if (E.TrackDeviceBuiltinKind(trackId, deviceIndex) != 17) throw new ArgumentException("not a Nota Forge (kind 17)");
        const int tele = 32, harm = 8, pts = 129, curveOff = tele + harm * 4;
        var sc = new float[curveOff + pts * 8];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        float Db(int i) => n > i ? sc[i] : -120f;
        static double R1(double v) => Math.Round(v, 1);
        int pc = E.DeviceParamCount(trackId, deviceIndex);
        float Pm(int p) => p < pc ? E.DeviceGetParam(trackId, deviceIndex, p) : 0.5f;
        int routing = Math.Clamp((int)Math.Round(Pm(6) * 3), 0, 3);
        double[] Harm(int set) { var h = new double[harm]; for (int k = 0; k < harm; k++) h[k] = R1(n >= curveOff ? sc[tele + set * harm + k] : -120); return h; }
        var stages = new ForgeStage[3];
        for (int s = 0; s < 3; s++)
        {
            int b = 11 + s * 5, sh = 27 + s * 3;
            string role = routing switch { 2 => new[] { "Mid", "Side", "M+S" }[s], 3 => new[] { "Low", "Mid", "High" }[s], 1 => "Parallel", _ => "Serial" };
            stages[s] = new ForgeStage(s + 1, Pm(b + 4) >= 0.5f, ForgeAlgos[Math.Clamp((int)Math.Round(Pm(b) * 5), 0, 5)], role,
                R1(Pm(b + 1) * 100), R1((Pm(b + 2) - 0.5) * 24), R1(Pm(b + 3) * 100), R1((Pm(sh) - 0.5) * 200), R1((Pm(sh + 1) - 0.5) * 24),
                R1(Pm(sh + 2) * 200), R1(V(14 + s)), ForgeFlavors[Math.Clamp((int)V(18 + s), 0, 3)], Harm(s + 1));
        }
        var curve = new double[17];
        for (int i = 0; i < 17; i++) curve[i] = Math.Round(n >= sc.Length ? sc[curveOff + i * 8] : 0, 3);
        bool sync = Pm(10) >= 0.5f;
        string rate = sync ? ForgeDivs[Math.Clamp((int)Math.Round(Pm(9) * 7), 0, 7)] : NotaNumInv($"{0.05 * Math.Pow(400, Pm(9)):0.00} Hz");
        int os = Math.Clamp((int)Math.Round(Pm(26) * 3), 0, 3);
        return new ForgeReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1), ForgeRoutes[routing],
            R1(Pm(0) * 30), Math.Round(Pm(2), 3), R1((Pm(3) - 0.5) * 48), R1(Pm(7) * 100), R1(Pm(8) * 100), sync, rate, Math.Round(V(25), 3),
            os == 0 ? "Off" : $"{1 << os}x", R1((Pm(1) - 0.5) * 200), R1((Pm(4) - 0.5) * 200), R1(Pm(5) * 200), stages,
            R1(V(13)), ForgeFlavors[Math.Clamp((int)V(17), 0, 3)], Harm(0), curve, R1(Db(0)), R1(Db(1)), R1(Db(2)), R1(Db(3)),
            Math.Round(V(8), 3), Math.Round(V(9), 3), Math.Round(V(10), 3), V(22) > 0.5f, (int)V(6), Math.Round(V(5) * 100, 2), V(21) > 0.5f, V(4));
    }

    private static string NotaNumInv(FormattableString f) => f.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void ForgeSet(int trackId, int deviceIndex, int p, double v)
    {
        E.BeginAutomationWrite(trackId, AutomationTarget.DeviceParam, deviceIndex, p, "");
        E.DeviceSetParam(trackId, deviceIndex, p, (float)Math.Clamp(v, 0, 1));
        E.EndAutomationWrite(trackId, AutomationTarget.DeviceParam, deviceIndex, p, "");
    }

    [McpServerTool(Name = "set_forge"), Description(
        "Play a Nota Forge's globals (built-in effect kind 17) in units — only the fields you pass change; each change is recorded "
        + "like a hand edit (so it lands in automation while recording). routing \"Serial\" | \"Parallel\" | \"MidSide\" | "
        + "\"Multiband\"; amountDb 0..30 into the stages; wetPercent 0..100; outputDb ±24; lfoDrivePercent 0..100 (the LFO sweeps "
        + "every stage's drive by up to ±30 %); envTonePercent 0..100 (loud parts push the tone brighter); lfoSync with lfoDivision "
        + "\"2/1\" \"1/1\" \"1/2\" \"1/4\" \"1/8\" \"1/16\" \"1/32\" \"1/64\", or lfoHz 0.05..20 (sets free run); oversampling 1, 2, 4 "
        + "or 8; masterTonePercent / masterBiasPercent ±100 and masterWidthPercent 0..200 are the master offsets (0 / 0 / 100 = "
        + "neutral; the per-stage ones are in set_forge_stage). Returns the device as read back.")]
    public Task<ForgeReading> SetForge(int trackId, int deviceIndex, string? routing = null, double? amountDb = null, double? wetPercent = null,
        double? outputDb = null, double? lfoDrivePercent = null, double? envTonePercent = null, bool? lfoSync = null, string? lfoDivision = null,
        double? lfoHz = null, int? oversampling = null, double? masterTonePercent = null, double? masterBiasPercent = null,
        double? masterWidthPercent = null) => Mutate(() =>
        {
            if (E.TrackDeviceBuiltinKind(trackId, deviceIndex) != 17) throw new ArgumentException("not a Nota Forge (kind 17)");
            void S(int p, double v) => ForgeSet(trackId, deviceIndex, p, v);
            if (routing is not null)
            {
                int r = routing.Trim().ToLowerInvariant().Replace("/", "").Replace("-", "").Replace(" ", "") switch
                {
                    "serial" => 0, "parallel" => 1, "midside" or "ms" => 2, "multiband" or "multi" => 3,
                    _ => throw new ArgumentException("routing must be Serial, Parallel, MidSide or Multiband"),
                };
                S(6, r / 3.0);
            }
            if (amountDb is { } a) S(0, a / 30);
            if (wetPercent is { } w) S(2, w / 100);
            if (outputDb is { } o) S(3, 0.5 + o / 48);
            if (lfoDrivePercent is { } l) S(7, l / 100);
            if (envTonePercent is { } e) S(8, e / 100);
            if (lfoHz is { } hz) { S(10, 0); S(9, Math.Log(Math.Clamp(hz, 0.05, 20) / 0.05) / Math.Log(400)); }
            if (lfoSync is { } sy) S(10, sy ? 1 : 0);
            if (lfoDivision is not null)
            {
                int d = Array.IndexOf(ForgeDivs, lfoDivision.Trim());
                if (d < 0) throw new ArgumentException("lfoDivision must be one of " + string.Join(", ", ForgeDivs));
                S(10, 1); S(9, d / 7.0);
            }
            if (oversampling is { } os)
            {
                int i = os switch { 1 => 0, 2 => 1, 4 => 2, 8 => 3, _ => throw new ArgumentException("oversampling must be 1, 2, 4 or 8") };
                S(26, i / 3.0);
            }
            if (masterTonePercent is { } mt) S(1, 0.5 + mt / 200);
            if (masterBiasPercent is { } mb) S(4, 0.5 + mb / 200);
            if (masterWidthPercent is { } mw) S(5, mw / 200);
            return ForgeRead(trackId, deviceIndex);
        });

    [McpServerTool(Name = "set_forge_stage"), Description(
        "Edit one Nota Forge stage (built-in effect kind 17) in units — only the fields you pass change; each change is recorded like "
        + "a hand edit. stage 1..3; on; type \"Tube\" | \"Diode\" | \"Tape\" | \"Fuzz\" | \"Digital\" | \"Fold\"; drivePercent 0..100 "
        + "(gain 1 … ×21, level made up); outDb ±12; feedbackPercent 0..100 (0..85 % of the output back in); biasPercent ±100 "
        + "(asymmetry → even harmonics); toneDb ±12 (a tilt after the stage around 800 Hz); widthPercent 0..200 (the stage's "
        + "stereo image; in Mid/Side stage 2's width scales the side). Returns the device as read back.")]
    public Task<ForgeReading> SetForgeStage(int trackId, int deviceIndex, [Description("Stage 1..3")] int stage, bool? on = null,
        string? type = null, double? drivePercent = null, double? outDb = null, double? feedbackPercent = null, double? biasPercent = null,
        double? toneDb = null, double? widthPercent = null) => Mutate(() =>
        {
            if (E.TrackDeviceBuiltinKind(trackId, deviceIndex) != 17) throw new ArgumentException("not a Nota Forge (kind 17)");
            if (stage is < 1 or > 3) throw new ArgumentException("stage must be 1, 2 or 3");
            int b = 11 + (stage - 1) * 5, sh = 27 + (stage - 1) * 3;
            void S(int p, double v) => ForgeSet(trackId, deviceIndex, p, v);
            if (type is not null)
            {
                int t = Array.FindIndex(ForgeAlgos, x => string.Equals(x, type.Trim(), StringComparison.OrdinalIgnoreCase));
                if (t < 0) throw new ArgumentException("type must be one of " + string.Join(", ", ForgeAlgos));
                S(b, t / 5.0);
            }
            if (drivePercent is { } d) S(b + 1, d / 100);
            if (outDb is { } o) S(b + 2, 0.5 + o / 24);
            if (feedbackPercent is { } f) S(b + 3, f / 100);
            if (on is { } en) S(b + 4, en ? 1 : 0);
            if (biasPercent is { } bi) S(sh, 0.5 + bi / 200);
            if (toneDb is { } tn) S(sh + 1, 0.5 + tn / 24);
            if (widthPercent is { } w) S(sh + 2, w / 200);
            return ForgeRead(trackId, deviceIndex);
        });

    public sealed record OrbitReading(string Summary, string Live, string Waveform, bool Sync, string Division, double RateHz, double PeriodMs,
        double ShapeOrGlide, double PhaseDeg, string Mode, double Amount, double Mix, double FloorDb, double GainLDb, double GainRDb,
        double Pan, double PanMin, double PanMax, double WindowPhase, bool Locked, bool Playing, double Bpm, double BarBeats,
        double InputPeakDb, double OutputPeakLDb, double OutputPeakRDb, bool Signal, double SampleRate);

    private static readonly string[] OrbitWaves = { "Sine", "Triangle", "Saw", "Square", "S&H" };
    private static readonly string[] OrbitDivs = { "4/1", "2/1", "1/1", "1/2D", "1/2", "1/2T", "1/4D", "1/4", "1/4T", "1/8D", "1/8", "1/8T", "1/16D", "1/16", "1/16T", "1/32" };

    [McpServerTool(Name = "read_orbit"), Description(
        "Read what a Nota Orbit (built-in effect kind 9 — auto-pan / tremolo) is doing right now: the waveform, whether the rate "
        + "is free (Hz) or synced (the division), the LFO rate and period, shape (glide on S&H), the phase between the channels "
        + "(degrees) and the mode it makes (tremolo near 0°, auto-pan near 180°, offset pan between), amount, mix and the floor "
        + "the gain dips to (dB); live — each channel's gain now (dB), the stereo position (−1 left … +1 right) and the swing it "
        + "covers over two cycles, the LFO's place in its two-cycle window (0..2), whether it is locked to the song position "
        + "(Sync + transport playing), tempo and beats per bar, input and L / R output peaks (dBFS, −120 = silence). The gains "
        + "and levels move only while audio runs through the track. Params: 0 Rate, 1 Amount, 2 Waveform, 3 Shape, 4 Phase, "
        + "5 Mix, 6 Sync, 7 Division — get_device_text id 2 explains each value; device_action 0 restarts the LFO, 1 resets "
        + "the meters.")]
    public Task<OrbitReading> ReadOrbit(int trackId, int deviceIndex) => Read(() =>
    {
        var sc = new float[32];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        float Db(int i) => n > i ? sc[i] : -120f;
        static double R1(double v) => Math.Round(v, 1);
        static double GDb(double g) => g < 1e-3 ? -120 : Math.Round(20 * Math.Log10(g), 1);
        float Pm(int p) => E.DeviceGetParam(trackId, deviceIndex, p);
        int wave = Math.Clamp((int)Math.Round(Pm(2) * 4), 0, 4);
        int div = Math.Clamp((int)Math.Round(Pm(7) * 15), 0, 15);
        double deg = Math.Round(Pm(4) * 360);
        string mode = deg <= 15 || deg >= 345 ? "tremolo" : Math.Abs(deg - 180) <= 15 ? "auto-pan" : "offset pan";
        double rate = V(7);
        return new OrbitReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1),
            OrbitWaves[wave], Pm(6) >= 0.5f, OrbitDivs[div], Math.Round(rate, 3), rate > 0 ? Math.Round(1000 / rate) : 0,
            Math.Round(Pm(3), 3), deg, mode, Math.Round(Pm(1), 3), Math.Round(Pm(5), 3), GDb(1 - Pm(1) * Pm(5)),
            GDb(n > 3 ? V(3) : 1), GDb(n > 4 ? V(4) : 1), Math.Round(V(5), 3), Math.Round(V(16), 3), Math.Round(V(17), 3),
            Math.Round(V(6), 3), V(10) > 0.5f, V(9) > 0.5f, Math.Round(V(8), 2), V(19), R1(Db(0)), R1(Db(1)), R1(Db(2)),
            V(21) > 0.5f, V(11));
    });

    public sealed record DynEqBand(int Band, bool On, string Type, double FreqHz, double GainDb, double Q, string Mode, double ThresholdDb,
        double RangeDb, double AttackMs, double ReleaseMs, string Key, bool Dynamic, double LevelDb, double GainNowDb, double ResponseDb);
    public sealed record DynEqSpectrumBand(double Hz, double Db);
    public sealed record DynEqReading(string Summary, string Live, bool Dynamics, int SoloBand, double OutputDb, int KeyTrackId, bool KeyLive,
        double InputPeakDb, double OutputPeakDb, double SampleRate, DynEqBand[] Bands, bool SpectrumValid, DynEqSpectrumBand[] Spectrum);

    private static readonly string[] DynEqTypes = { "Low cut", "Low shelf", "Bell", "Notch", "High shelf", "High cut" };
    private static readonly string[] DynEqModes = { "Static", "Duck", "Lift" };

    [McpServerTool(Name = "read_dynamic_eq"), Description(
        "Read what a Nota Dynamic EQ-8 (built-in effect kind 13 — eight-band EQ where each band can react to level) is doing right "
        + "now: the Dynamic master switch, the soloed band (0 = none), output, the key track routed (−1 none) and whether its signal "
        + "arrives, in / out peaks; per band: on, type, frequency, static gain, Q, mode (Static / Duck — acts above the threshold / "
        + "Lift — acts below it), threshold, signed range (negative cuts, positive boosts), attack / release, key (Self / Ext), "
        + "whether it is dynamic now, its detector level (dBFS), the gain it adds now (dB) and the band's static response at its "
        + "own frequency; plus the output spectrum in 24 log bands (dB). Levels read −120 while silent; they move only while audio "
        + "runs through the track. get_device_text id 2 explains the parameter layout.")]
    public Task<DynEqReading> ReadDynamicEq(int trackId, int deviceIndex) => Read(() =>
    {
        const int tele = 32, spec = 96;
        var sc = new float[tele + spec];
        int n = E.DeviceScope(trackId, deviceIndex, sc, sc.Length);
        float V(int i) => n > i ? sc[i] : 0f;
        float Db(int i) => n > i ? sc[i] : -120f;
        static double R1(double v) => Math.Round(v, 1);
        int pc = E.DeviceParamCount(trackId, deviceIndex);
        float Pm(int p) => p < pc ? E.DeviceGetParam(trackId, deviceIndex, p) : 0f;
        bool dyn = pc <= 83 || Pm(83) >= 0.5f;
        var bands = new DynEqBand[8];
        for (int b = 0; b < 8; b++)
        {
            int o = b * 10, ty = Math.Clamp((int)Math.Round(Pm(o + 1)), 0, 5), md = Math.Clamp((int)Math.Round(Pm(o + 5)), 0, 2);
            bool on = Pm(o) > 0.5f, gain = ty is 1 or 2 or 4, ext = Pm(81) >= 0.5f || Pm(84 + b) >= 0.5f;
            bool isDyn = dyn && on && gain && md != 0;
            bands[b] = new DynEqBand(b + 1, on, DynEqTypes[ty], Math.Round(Pm(o + 2)), gain ? R1(Pm(o + 3)) : 0, Math.Round(Pm(o + 4), 2),
                gain ? DynEqModes[md] : "Static", R1(Pm(o + 6)), R1(Pm(o + 7)), Math.Round(Pm(o + 8), 1), Math.Round(Pm(o + 9)), ext ? "Ext" : "Self",
                isDyn, R1(Db(8 + b)), isDyn ? R1(V(b)) : 0, gain ? R1(Pm(o + 3) + (isDyn ? V(b) : 0)) : 0);
        }
        var sp = new List<DynEqSpectrumBand>();
        bool valid = n >= sc.Length && V(18) > 0.5f;
        if (valid)
            for (int k = 0; k < 24; k++)
            {
                double pw = 0;
                for (int b = 4 * k; b < 4 * k + 4; b++) pw += Math.Pow(10, sc[tele + b] / 10);
                sp.Add(new DynEqSpectrumBand(Math.Round(20 * Math.Pow(1000, (k + 0.5) / 24)), R1(Math.Max(-120, 10 * Math.Log10(Math.Max(1e-12, pw))))));
            }
        return new DynEqReading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1), dyn,
            Math.Clamp((int)Math.Round(Pm(82)), 0, 8), R1(Pm(80)), E.DeviceSidechainSource(trackId, deviceIndex), V(21) > 0.5f,
            R1(Db(19)), R1(Db(20)), V(16), bands, valid, sp.ToArray());
    });

    [McpServerTool(Name = "set_dynamic_eq_band"), Description(
        "Edit one band of a Nota Dynamic EQ-8 (built-in effect kind 13) in one call — only the fields you pass change. type: "
        + "\"Low cut\" | \"Low shelf\" | \"Bell\" | \"Notch\" | \"High shelf\" | \"High cut\" (only shelves and bells take gain and "
        + "dynamics). mode: \"Static\" | \"Duck\" (acts as the band's level rises above the threshold) | \"Lift\" (acts as it falls "
        + "below it). rangeDb is signed: negative cuts, positive boosts (a Duck normally cuts, a Lift boosts; the other sign is "
        + "upward / downward expansion); full engagement is 6 dB past the threshold. key: \"Self\" or \"Ext\" (the key track — "
        + "route one with set_device_sidechain). Each change is recorded like a hand edit. Returns the band as read back.")]
    public Task<DynEqBand> SetDynamicEqBand(int trackId, int deviceIndex, [Description("Band 1..8")] int band,
        bool? on = null, string? type = null, [Description("20..20000 Hz")] double? freqHz = null, [Description("−18..+18 dB")] double? gainDb = null,
        [Description("0.1..18")] double? q = null, string? mode = null, [Description("−60..+6 dBFS")] double? thresholdDb = null,
        [Description("−18..+18 dB")] double? rangeDb = null, [Description("0.1..300 ms")] double? attackMs = null,
        [Description("5..2000 ms")] double? releaseMs = null, string? key = null) => Mutate(() =>
    {
        if (band < 1 || band > 8) throw new ArgumentException("band is 1..8");
        int o = (band - 1) * 10;
        void S(int p, double v)
        {
            E.BeginAutomationWrite(trackId, AutomationTarget.DeviceParam, deviceIndex, p, "");
            E.DeviceSetParam(trackId, deviceIndex, p, (float)v);
            E.EndAutomationWrite(trackId, AutomationTarget.DeviceParam, deviceIndex, p, "");
        }
        int Idx(string[] names, string v, string what)
        {
            int i = Array.FindIndex(names, s => string.Equals(s, v.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0) throw new ArgumentException($"{what} must be one of: {string.Join(", ", names)}");
            return i;
        }
        if (type is not null) S(o + 1, Idx(DynEqTypes, type, "type"));
        if (freqHz is { } f) S(o + 2, f);
        if (gainDb is { } g) S(o + 3, g);
        if (q is { } qq) S(o + 4, qq);
        if (thresholdDb is { } t) S(o + 6, t);
        if (rangeDb is { } r) S(o + 7, r);
        if (attackMs is { } a) S(o + 8, a);
        if (releaseMs is { } rl) S(o + 9, rl);
        if (mode is not null) S(o + 5, Idx(DynEqModes, mode, "mode"));
        if (key is not null)
        {
            bool ext = Idx(new[] { "Self", "Ext" }, key, "key") == 1;
            if (!ext && E.DeviceGetParam(trackId, deviceIndex, 81) >= 0.5f)
            {
                for (int b = 0; b < 8; b++) if (b != band - 1) S(84 + b, 1);
                S(81, 0);
            }
            S(84 + band - 1, ext ? 1 : 0);
        }
        if (on is { } onv) S(o, onv ? 1 : 0);
        int ty = Math.Clamp((int)Math.Round(E.DeviceGetParam(trackId, deviceIndex, o + 1)), 0, 5);
        int md = Math.Clamp((int)Math.Round(E.DeviceGetParam(trackId, deviceIndex, o + 5)), 0, 2);
        bool gain = ty is 1 or 2 or 4;
        float Pm(int p) => E.DeviceGetParam(trackId, deviceIndex, p);
        bool isDyn = Pm(83) >= 0.5f && Pm(o) > 0.5f && gain && md != 0;
        return new DynEqBand(band, Pm(o) > 0.5f, DynEqTypes[ty], Math.Round(Pm(o + 2)), gain ? Math.Round(Pm(o + 3), 1) : 0, Math.Round(Pm(o + 4), 2),
            gain ? DynEqModes[md] : "Static", Math.Round(Pm(o + 6), 1), Math.Round(Pm(o + 7), 1), Math.Round(Pm(o + 8), 1), Math.Round(Pm(o + 9)),
            Pm(81) >= 0.5f || Pm(84 + band - 1) >= 0.5f ? "Ext" : "Self", isDyn, -120, 0, gain ? Math.Round(Pm(o + 3), 1) : 0);
    });

    public sealed record Eq3Band(string Band, double GainDb, bool Killed, double LevelDb, double GainNowDb);
    public sealed record Eq3Reading(string Summary, string Live, string Range, double MinGainDb, double MaxGainDb, double LowMidHz, double MidHighHz,
        int SlopeDbPerOct, double OutputDb, double InputPeakDb, double OutputPeakDb, double SampleRate, Eq3Band[] Bands,
        bool SpectrumValid, DynEqSpectrumBand[] Spectrum);

    // Nota EQ-3 mappings (Eq3.h): band gain by the Range law, crossovers exp, output ±24 dB.
    private static double Eq3GainDb(double v, bool iso) => iso ? -24 + 30 * v : (v - 0.5) * 30;
    private static double Eq3GainNorm(double db, bool iso) => Math.Clamp(iso ? (db + 24) / 30 : db / 30 + 0.5, 0, 1);

    private Eq3Reading Eq3Read(int trackId, int deviceIndex, bool spectrum)
    {
        const int tele = 16, spec = 96;
        var sc = new float[tele + spec];
        int n = E.DeviceScope(trackId, deviceIndex, sc, spectrum ? sc.Length : tele);
        float V(int i) => n > i ? sc[i] : 0f;
        float Db(int i) => n > i ? sc[i] : -120f;
        static double R1(double v) => Math.Round(v, 1);
        int pc = E.DeviceParamCount(trackId, deviceIndex);
        float Pm(int p) => p < pc ? E.DeviceGetParam(trackId, deviceIndex, p) : 0f;
        bool iso = pc > 10 && Pm(10) >= 0.5f;
        string[] names = { "Low", "Mid", "High" };
        var bands = new Eq3Band[3];
        for (int b = 0; b < 3; b++)
        {
            double now = V(11 + b);
            bands[b] = new Eq3Band(names[b], R1(Eq3GainDb(Pm(b), iso)), Pm(3 + b) >= 0.5f, R1(Db(2 + b)),
                now > 1e-6 ? R1(Math.Max(-120, 20 * Math.Log10(now))) : -120);
        }
        var sp = new List<DynEqSpectrumBand>();
        bool valid = spectrum && n >= sc.Length && V(7) > 0.5f;
        if (valid)
            for (int k = 0; k < 24; k++)
            {
                double pw = 0;
                for (int b = 4 * k; b < 4 * k + 4; b++) pw += Math.Pow(10, sc[tele + b] / 10);
                sp.Add(new DynEqSpectrumBand(Math.Round(20 * Math.Pow(1000, (k + 0.5) / 24)), R1(Math.Max(-120, 10 * Math.Log10(Math.Max(1e-12, pw))))));
            }
        return new Eq3Reading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1),
            iso ? "Isolator" : "Classic", iso ? -24 : -15, iso ? 6 : 15,
            Math.Round(50 * Math.Pow(40, Pm(6))), Math.Round(500 * Math.Pow(36, Pm(7))), Pm(8) >= 0.5f ? 48 : 24, R1((Pm(9) - 0.5) * 48),
            R1(Db(0)), R1(Db(1)), V(5), bands, valid, sp.ToArray());
    }

    [McpServerTool(Name = "read_eq3"), Description(
        "Read a Nota EQ-3 (built-in effect kind 16 — three-band DJ isolator: Low / Mid / High split by two Linkwitz-Riley "
        + "crossovers, each band a gain fader and a kill): the fader range (Isolator −24 … +6 dB, or Classic ±15 dB on older "
        + "projects), the low/mid and mid/high crossovers in Hz, the slope (24 or 48 dB/oct), output; per band: its gain (dB), "
        + "whether it is killed, its level after the gain (dBFS, peak with a 300 ms fall) and the gain in effect now (dB, "
        + "−120 while killed — kills glide over 5 ms); in / out peaks; plus the output spectrum in 24 log bands (dB). Levels read "
        + "−120 while silent; they move only while audio runs through the track. set_eq3 edits it in dB and Hz.")]
    public Task<Eq3Reading> ReadEq3(int trackId, int deviceIndex) => Read(() => Eq3Read(trackId, deviceIndex, true));

    [McpServerTool(Name = "set_eq3"), Description(
        "Play a Nota EQ-3 (built-in effect kind 16) in one call — only the fields you pass change; each change is recorded like a "
        + "hand edit (so it lands in automation while recording). Gains are in dB within the fader range (Isolator −24 … +6, "
        + "Classic ±15; values outside are clamped); a kill removes the band outright (a gain move does not un-kill it — pass "
        + "kill false). lowMidHz 50 … 2000, midHighHz 500 … 18000 (the low/mid crossover is kept at most half the mid/high one). "
        + "slope 24 or 48 dB/oct. outputDb ±24. range \"Isolator\" | \"Classic\" switches the fader law and keeps each band's dB "
        + "(clamped to the new range). Returns the device as read back (without the spectrum).")]
    public Task<Eq3Reading> SetEq3(int trackId, int deviceIndex,
        [Description("Low band gain, dB")] double? lowDb = null, [Description("Mid band gain, dB")] double? midDb = null,
        [Description("High band gain, dB")] double? highDb = null, bool? lowKill = null, bool? midKill = null, bool? highKill = null,
        [Description("Low/mid crossover, 50..2000 Hz")] double? lowMidHz = null, [Description("Mid/high crossover, 500..18000 Hz")] double? midHighHz = null,
        [Description("24 or 48 dB/oct")] int? slope = null, [Description("Output, −24..+24 dB")] double? outputDb = null,
        [Description("\"Isolator\" or \"Classic\"")] string? range = null) => Mutate(() =>
    {
        if (E.TrackDeviceBuiltinKind(trackId, deviceIndex) != 16) throw new ArgumentException("not a Nota EQ-3 (kind 16)");
        int pc = E.DeviceParamCount(trackId, deviceIndex);
        float Pm(int p) => p < pc ? E.DeviceGetParam(trackId, deviceIndex, p) : 0f;
        void S(int p, double v)
        {
            E.BeginAutomationWrite(trackId, AutomationTarget.DeviceParam, deviceIndex, p, "");
            E.DeviceSetParam(trackId, deviceIndex, p, (float)Math.Clamp(v, 0, 1));
            E.EndAutomationWrite(trackId, AutomationTarget.DeviceParam, deviceIndex, p, "");
        }
        bool iso = pc > 10 && Pm(10) >= 0.5f;
        if (range is not null && pc > 10)
        {
            bool want = range.Trim().ToLowerInvariant() switch
            {
                "isolator" => true, "classic" => false,
                _ => throw new ArgumentException("range must be Isolator or Classic"),
            };
            if (want != iso)
            {
                var dbs = new double[3];
                for (int b = 0; b < 3; b++) dbs[b] = Eq3GainDb(Pm(b), iso);
                S(10, want ? 1 : 0);
                for (int b = 0; b < 3; b++) S(b, Eq3GainNorm(dbs[b], want));
                iso = want;
            }
        }
        double?[] gains = { lowDb, midDb, highDb };
        bool?[] kills = { lowKill, midKill, highKill };
        for (int b = 0; b < 3; b++)
        {
            if (gains[b] is { } g) S(b, Eq3GainNorm(g, iso));
            if (kills[b] is { } k) S(3 + b, k ? 1 : 0);
        }
        double f2 = midHighHz is { } mh ? Math.Clamp(mh, 500, 18000) : 500 * Math.Pow(36, Pm(7));
        double f1 = lowMidHz is { } lm ? Math.Clamp(lm, 50, 2000) : 50 * Math.Pow(40, Pm(6));
        if (midHighHz is not null) S(7, Math.Log(f2 / 500) / Math.Log(36));
        if (lowMidHz is not null || (midHighHz is not null && f1 > f2 / 2))
            S(6, Math.Log(Math.Clamp(Math.Min(f1, f2 / 2), 50, 2000) / 50) / Math.Log(40));
        if (slope is { } sl)
        {
            if (sl != 24 && sl != 48) throw new ArgumentException("slope is 24 or 48");
            S(8, sl == 48 ? 1 : 0);
        }
        if (outputDb is { } o) S(9, 0.5 + Math.Clamp(o, -24, 24) / 48);
        return Eq3Read(trackId, deviceIndex, false);
    });

    public sealed record Eq8Band(int Band, bool On, string Type, double FreqHz, double GainDb, double EffectiveGainDb, double Q,
        int SlopeDbPerOct, string Channel);
    public sealed record Eq8Reading(string Summary, string Live, double ScalePercent, double OutputDb, bool AutoGain, double AutoGainNowDb,
        string Analyzer, double InputPeakDb, double OutputPeakDb, double SampleRate, Eq8Band[] Bands,
        bool SpectrumValid, DynEqSpectrumBand[] InputSpectrum, DynEqSpectrumBand[] OutputSpectrum);

    private static readonly string[] Eq8Channels = { "Stereo", "Mid", "Side", "Left", "Right" };
    private static readonly string[] Eq8Analyzer = { "Pre", "Post", "Off" };

    private Eq8Band Eq8BandRead(int trackId, int deviceIndex, int b)
    {
        int pc = E.DeviceParamCount(trackId, deviceIndex);
        float Pm(int p) => p < pc ? E.DeviceGetParam(trackId, deviceIndex, p) : p == 56 ? 100f : 0f;
        int o = b * 5, ty = Math.Clamp((int)Math.Round(Pm(o + 1)), 0, 5);
        bool gain = ty is 1 or 2 or 4, cut = ty is 0 or 5;
        double scale = Math.Clamp(Pm(56), 0, 200) / 100;
        return new Eq8Band(b + 1, Pm(o) > 0.5f, DynEqTypes[ty], Math.Round(Pm(o + 2)), gain ? Math.Round(Pm(o + 3), 1) : 0,
            gain ? Math.Round(Pm(o + 3) * scale, 1) : 0, Math.Round(Pm(o + 4), 2),
            cut ? 12 << Math.Clamp((int)Math.Round(Pm(40 + b)), 0, 2) : 12, Eq8Channels[Math.Clamp((int)Math.Round(Pm(48 + b)), 0, 4)]);
    }

    private Eq8Reading Eq8Read(int trackId, int deviceIndex, bool spectrum)
    {
        if (E.TrackDeviceBuiltinKind(trackId, deviceIndex) != 0) throw new ArgumentException("not a Nota EQ-8 (kind 0)");
        const int tele = 16, spec = 96;
        var sc = new float[tele + 2 * spec];
        int n = E.DeviceScope(trackId, deviceIndex, sc, spectrum ? sc.Length : tele);
        float V(int i) => n > i ? sc[i] : 0f;
        float Db(int i) => n > i ? sc[i] : -120f;
        static double R1(double v) => Math.Round(v, 1);
        int pc = E.DeviceParamCount(trackId, deviceIndex);
        float Pm(int p) => p < pc ? E.DeviceGetParam(trackId, deviceIndex, p) : p == 56 ? 100f : p == 59 ? 1f : 0f;
        var bands = new Eq8Band[8];
        for (int b = 0; b < 8; b++) bands[b] = Eq8BandRead(trackId, deviceIndex, b);
        bool valid = spectrum && n >= sc.Length && V(5) > 0.5f;
        DynEqSpectrumBand[] Spec(int at)
        {
            var sp = new List<DynEqSpectrumBand>();
            if (valid)
                for (int k = 0; k < 24; k++)
                {
                    double pw = 0;
                    for (int b = 4 * k; b < 4 * k + 4; b++) pw += Math.Pow(10, sc[at + b] / 10);
                    sp.Add(new DynEqSpectrumBand(Math.Round(20 * Math.Pow(1000, (k + 0.5) / 24)), R1(Math.Max(-120, 10 * Math.Log10(Math.Max(1e-12, pw))))));
                }
            return sp.ToArray();
        }
        return new Eq8Reading(E.DeviceText(trackId, deviceIndex, 0), E.DeviceText(trackId, deviceIndex, 1), R1(Pm(56)), R1(Pm(57)),
            Pm(58) >= 0.5f, R1(V(2)), Eq8Analyzer[Math.Clamp((int)Math.Round(Pm(59)), 0, 2)], R1(Db(0)), R1(Db(1)), V(3), bands,
            valid, Spec(tele), Spec(tele + spec));
    }

    [McpServerTool(Name = "read_eq8"), Description(
        "Read a Nota EQ-8 (built-in effect kind 0 — eight-band parametric EQ): Scale (% applied to every shelf and bell gain), "
        + "output, whether Auto gain is on and the gain it adds now, the analyzer mode the card shows (Pre / Post / Off), in / out "
        + "peaks; per band: on, type, frequency, gain, the effective gain after Scale, Q (the resonance on the cuts), slope "
        + "(12 / 24 / 48 dB/oct, cuts only) and channel (Stereo / Mid / Side / Left / Right); plus the input and output spectra "
        + "in 24 log bands (dB). Levels read −120 while silent; they move only while audio runs through the track. "
        + "set_eq8_band edits a band, set_eq8 the globals.")]
    public Task<Eq8Reading> ReadEq8(int trackId, int deviceIndex) => Read(() => Eq8Read(trackId, deviceIndex, true));

    [McpServerTool(Name = "set_eq8_band"), Description(
        "Edit one band of a Nota EQ-8 (built-in effect kind 0) in one call — only the fields you pass change; each change is "
        + "recorded like a hand edit. type: \"Low cut\" | \"Low shelf\" | \"Bell\" | \"Notch\" | \"High shelf\" | \"High cut\" "
        + "(only shelves and bells take gain). slope: 12, 24 or 48 dB/oct (cuts only). channel: \"Stereo\" | \"Mid\" | \"Side\" | "
        + "\"Left\" | \"Right\". Setting any field of an off band does not switch it on — pass on true. Returns the band as read back.")]
    public Task<Eq8Band> SetEq8Band(int trackId, int deviceIndex, [Description("Band 1..8")] int band,
        bool? on = null, string? type = null, [Description("20..20000 Hz")] double? freqHz = null, [Description("−18..+18 dB")] double? gainDb = null,
        [Description("0.1..18 (0.71 = flat on the cuts)")] double? q = null, [Description("12, 24 or 48")] int? slope = null,
        string? channel = null) => Mutate(() =>
    {
        if (E.TrackDeviceBuiltinKind(trackId, deviceIndex) != 0) throw new ArgumentException("not a Nota EQ-8 (kind 0)");
        if (band < 1 || band > 8) throw new ArgumentException("band is 1..8");
        int o = (band - 1) * 5, pc = E.DeviceParamCount(trackId, deviceIndex);
        void S(int p, double v)
        {
            if (p >= pc) return;
            E.BeginAutomationWrite(trackId, AutomationTarget.DeviceParam, deviceIndex, p, "");
            E.DeviceSetParam(trackId, deviceIndex, p, (float)v);
            E.EndAutomationWrite(trackId, AutomationTarget.DeviceParam, deviceIndex, p, "");
        }
        int Idx(string[] names, string v, string what)
        {
            int i = Array.FindIndex(names, s => string.Equals(s, v.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0) throw new ArgumentException($"{what} must be one of: {string.Join(", ", names)}");
            return i;
        }
        if (type is not null) S(o + 1, Idx(DynEqTypes, type, "type"));
        if (freqHz is { } f) S(o + 2, Math.Clamp(f, 20, 20000));
        if (gainDb is { } g) S(o + 3, Math.Clamp(g, -18, 18));
        if (q is { } qq) S(o + 4, Math.Clamp(qq, 0.1, 18));
        if (slope is { } sl)
        {
            if (sl is not (12 or 24 or 48)) throw new ArgumentException("slope is 12, 24 or 48");
            S(40 + band - 1, sl == 12 ? 0 : sl == 24 ? 1 : 2);
        }
        if (channel is not null) S(48 + band - 1, Idx(Eq8Channels, channel, "channel"));
        if (on is { } onv) S(o, onv ? 1 : 0);
        return Eq8BandRead(trackId, deviceIndex, band - 1);
    });

    [McpServerTool(Name = "set_eq8"), Description(
        "Set the global controls of a Nota EQ-8 (built-in effect kind 0) — only the fields you pass change; each change is recorded "
        + "like a hand edit. scalePercent 0..200 multiplies every shelf and bell gain (100 = as set, 0 = flat). outputDb ±12. "
        + "autoGain adds the opposite of the average level of the shelves and bells (cuts and notches do not count; ±12 dB max). analyzer \"Pre\" | \"Post\" | \"Off\" picks the "
        + "spectrum the card draws. Returns the device as read back (without the spectra).")]
    public Task<Eq8Reading> SetEq8(int trackId, int deviceIndex, [Description("0..200 %")] double? scalePercent = null,
        [Description("−12..+12 dB")] double? outputDb = null, bool? autoGain = null, string? analyzer = null) => Mutate(() =>
    {
        if (E.TrackDeviceBuiltinKind(trackId, deviceIndex) != 0) throw new ArgumentException("not a Nota EQ-8 (kind 0)");
        int pc = E.DeviceParamCount(trackId, deviceIndex);
        void S(int p, double v)
        {
            if (p >= pc) return;
            E.BeginAutomationWrite(trackId, AutomationTarget.DeviceParam, deviceIndex, p, "");
            E.DeviceSetParam(trackId, deviceIndex, p, (float)v);
            E.EndAutomationWrite(trackId, AutomationTarget.DeviceParam, deviceIndex, p, "");
        }
        if (scalePercent is { } sp) S(56, Math.Clamp(sp, 0, 200));
        if (outputDb is { } od) S(57, Math.Clamp(od, -12, 12));
        if (autoGain is { } ag) S(58, ag ? 1 : 0);
        if (analyzer is not null)
        {
            int i = Array.FindIndex(Eq8Analyzer, s => string.Equals(s, analyzer.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0) throw new ArgumentException("analyzer must be Pre, Post or Off");
            S(59, i);
        }
        return Eq8Read(trackId, deviceIndex, false);
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
