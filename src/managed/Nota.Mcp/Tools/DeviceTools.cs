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
        (16, "EQ-3"), (17, "Forge"), (18, "Nota Level"), (19, "Nota Shutter"),
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
}
