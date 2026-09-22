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

    [McpServerTool(Name = "get_device_text"), Description("Read a device's resource text. Nota Chamber: id 0 = current IR name, 1 = its category, 2 = the loaded user IR's name, 10 = the built-in IR list (name, category, seconds per line; the IR param selects entry round(v × 16), 1.0 = the user IR). Nota Lens: id 0 = the analysis summary, 1 = the third-octave band table, 2 = the scope measurements, 3 = the strongest spectral peaks, 4 = the A/B cursor measurements — or use read_analyzer, which returns all of it parsed. Delay (kind 3): id 0 = a one-line summary of what it is doing now (sync division or free times, ping-pong, feedback, mix, or that it is frozen).")]
    public Task<string> GetDeviceText(int trackId, int deviceIndex, int id) => Read(() => E.DeviceText(trackId, deviceIndex, id));

    [McpServerTool(Name = "device_action"), Description("Run a device's own command — the few things that are actions rather than parameters. Delay (kind 3): id 0 clears the loop (empties the delay buffer, so whatever is still circulating stops; useful after Freeze). Devices without a command ignore the call.")]
    public Task DeviceAction(int trackId, int deviceIndex, [Description("Command id — see the device's list in this description")] int id,
        int intArg = 0, float floatArg = 0) => Mutate(() => E.DeviceAction(trackId, deviceIndex, id, intArg, floatArg));

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
