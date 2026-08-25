// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// MCP tools — parameter automation lanes. A point's Value is in the target's native units
// (Volume: 0..1 linear, Pan: -1..1, DeviceParam: the param's own range, PluginParam: 0..1);
// Curve shapes the segment to the next point (0 linear, -1..1).

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class AutomationTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record Lane(int Index, string Target, int DeviceIndex, int ParamIndex, int PointCount);
    public sealed record Point(double Beat, float Value, float Curve);

    private static string TargetName(AutomationTarget t) => t switch
    { AutomationTarget.Volume => "volume", AutomationTarget.Pan => "pan", AutomationTarget.DeviceParam => "device_param",
      AutomationTarget.PluginParam => "plugin_param", AutomationTarget.MidiDeviceParam => "midi_param", _ => t.ToString() };

    [McpServerTool(Name = "list_automation"), Description("List a track's automation lanes (index, target, device/param index, point count).")]
    public Task<Lane[]> ListAutomation(int trackId) => Read(() =>
    {
        int c = E.AutomationLaneCount(trackId);
        var lanes = new Lane[c];
        for (int i = 0; i < c; i++) { var li = E.AutomationLaneInfo(trackId, i); lanes[i] = new Lane(i, TargetName(li.Target), li.DeviceIndex, li.ParamIndex, li.PointCount); }
        return lanes;
    });

    [McpServerTool(Name = "get_automation_points"), Description("Get the breakpoints of an automation lane (beat, value in native units, curve).")]
    public Task<Point[]> GetAutomationPoints(int trackId, int laneIndex)
        => Read(() => Array.ConvertAll(E.GetAutomationPoints(trackId, laneIndex), p => new Point(p.Beat, p.Value, p.Curve)));

    [McpServerTool(Name = "set_automation_points"), Description("Replace an automation lane's breakpoints (sorted by beat).")]
    public Task SetAutomationPoints(int trackId, int laneIndex, Point[] points)
        => Mutate(() => E.SetAutomationPoints(trackId, laneIndex, Array.ConvertAll(points, p => new AutomationPoint(p.Beat, p.Value, p.Curve))));

    [McpServerTool(Name = "add_automation_lane"), Description(
        "Add an automation lane. target: \"volume\", \"pan\", or \"device_param\" (with deviceIndex+paramIndex). "
        + "Returns the lane index.")]
    public Task<int> AddAutomationLane(int trackId, string target, int deviceIndex = -1, int paramIndex = -1) => Mutate(() =>
    {
        var t = target.ToLowerInvariant() switch
        { "pan" => AutomationTarget.Pan, "device_param" => AutomationTarget.DeviceParam, "midi_param" => AutomationTarget.MidiDeviceParam, _ => AutomationTarget.Volume };
        return E.AddAutomationLane(trackId, t, deviceIndex, paramIndex);
    });

    [McpServerTool(Name = "add_instrument_param_automation"), Description("Add an automation lane for a track-instrument parameter (by its stable id from get_instrument_params). Returns the lane index.")]
    public Task<int> AddInstrumentParamAutomation(int trackId, string paramId) => Mutate(() => E.AddPluginAutomationLane(trackId, -1, paramId));

    [McpServerTool(Name = "remove_automation_lane"), Description("Remove an automation lane by index.")]
    public Task RemoveAutomationLane(int trackId, int laneIndex) => Mutate(() => E.RemoveAutomationLane(trackId, laneIndex));
}
