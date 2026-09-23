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

    [McpServerTool(Name = "get_device_text"), Description("Read a device's resource text. Nota Chamber: id 0 = current IR name, 1 = its category, 2 = the loaded user IR's name, 10 = the built-in IR list (name, category, seconds per line; the IR param selects entry round(v × 16), 1.0 = the user IR). Nota Lens: id 0 = the analysis summary, 1 = the third-octave band table, 2 = the scope measurements, 3 = the strongest spectral peaks, 4 = the A/B cursor measurements — or use read_analyzer, which returns all of it parsed. Delay (kind 3): id 0 = a one-line summary of what it is doing now (sync division or free times, ping-pong, feedback, mix, or that it is frozen). Reverb (kind 2): id 0 = a one-line summary (algorithm, RT60, pre-delay, diffusion, mix, early reflections / vintage, or that the tail is frozen). Compressor (kind 1): id 0 = a one-line summary (character, threshold, ratio, knee, attack / release, detector, range, mix, the reduction right now, external key / unlinked / Listen) — or use read_dynamics for the numbers. Auto Filter (kind 7): id 0 = a one-line summary (type, slope, cutoff, Q, what the envelope and LFO drive and by how much, drive, mix, sidechain key), 1 = the live reading (modulated cutoff with its note, resonance, envelope level, LFO value and phase, onsets counted), 2 = a guide to what each 0..1 parameter value means — or use read_filter_motion for the numbers.")]
    public Task<string> GetDeviceText(int trackId, int deviceIndex, int id) => Read(() => E.DeviceText(trackId, deviceIndex, id));

    [McpServerTool(Name = "device_action"), Description("Run a device's own command — the few things that are actions rather than parameters. Delay (kind 3): id 0 clears the loop (empties the delay buffer, so whatever is still circulating stops; useful after Freeze). Reverb (kind 2): id 0 kills the tail (empties the reverb's buffers, so whatever is still ringing — or frozen — stops). Auto Filter (kind 7): id 0 restarts the LFO (from its start phase; in Sync the cycle is re-anchored to the current beat), id 1 resets the envelope follower (drops a held peak). Devices without a command ignore the call.")]
    public Task DeviceAction(int trackId, int deviceIndex, [Description("Command id — see the device's list in this description")] int id,
        int intArg = 0, float floatArg = 0) => Mutate(() => E.DeviceAction(trackId, deviceIndex, id, intArg, floatArg));

    public sealed record Sidechain(bool Accepts, int SourceTrackId, bool TapPre, float GainDb, float Mix);

    [McpServerTool(Name = "get_device_sidechain"), Description("Read a device's sidechain routing: whether it can take a key at all (the Compressor, Ceiling, and plugins with a sidechain bus), the source track id (-1 = the device keys off its own track), whether the source is tapped pre-FX (true) or post-fader (false), the detector gain in dB and the effect's sidechain dry/wet mix (0..1).")]
    public Task<Sidechain> GetDeviceSidechain(int trackId, int deviceIndex) => Read(() => new Sidechain(
        E.DeviceAcceptsSidechain(trackId, deviceIndex), E.DeviceSidechainSource(trackId, deviceIndex),
        E.DeviceSidechainTapPre(trackId, deviceIndex), E.DeviceSidechainGain(trackId, deviceIndex), E.DeviceSidechainMix(trackId, deviceIndex)));

    [McpServerTool(Name = "set_device_sidechain"), Description("Route a sidechain key into a device — e.g. duck a bass or pad under the kick with a Compressor (kind 1): pass the kick track as sourceTrackId, then shape the key with the Compressor's params (SC HP / SC LP / SC Q / SC Gain / Hold; External Key must be on, which is its default). sourceTrackId -1 clears the source (the device keys off its own track). Optional: tapPre (true = the source before its effects and fader, false = after), gainDb (extra detector gain, ±24 dB). Leave an optional argument out to keep its current value. No effect on devices that take no sidechain.")]
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
