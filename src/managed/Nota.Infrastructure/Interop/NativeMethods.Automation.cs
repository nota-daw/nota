// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Parameter automation (M9): lane CRUD, plugin-param lanes, write recording, self-tests.</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_engine_automation_selftest")]
    internal static partial int AutomationSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_automation_curve_selftest")]
    internal static partial int AutomationCurveSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_plugin_automation_selftest")]
    internal static partial int PluginAutomationSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_rack_selftest")]
    internal static partial int RackSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_plugin_automation_lane", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int AddPluginAutomationLane(IntPtr engine, int trackId, int deviceIndex, string paramId);

    [LibraryImport(Lib, EntryPoint = "nota_track_automation_lane_param_id")]
    internal static partial IntPtr AutomationLaneParamId(IntPtr engine, int trackId, int laneIndex);

    [LibraryImport(Lib, EntryPoint = "nota_engine_automation_write_selftest")]
    internal static partial int AutomationWriteSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_set_automation_record")]
    internal static partial void SetAutomationRecord(IntPtr engine, int on);

    [LibraryImport(Lib, EntryPoint = "nota_engine_automation_record")]
    internal static partial int AutomationRecord(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_begin_automation_write", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void BeginAutomationWrite(IntPtr engine, int trackId, int target, int deviceIndex, int paramIndex, string paramId, int latch);

    [LibraryImport(Lib, EntryPoint = "nota_track_end_automation_write", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void EndAutomationWrite(IntPtr engine, int trackId, int target, int deviceIndex, int paramIndex, string paramId);

    [LibraryImport(Lib, EntryPoint = "nota_engine_reenable_automation")]
    internal static partial void ReenableAutomation(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_automation_overridden")]
    internal static partial int AutomationOverridden(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_automation_lane")]
    internal static partial int AddAutomationLane(IntPtr engine, int trackId, int target, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_automation_lane_count")]
    internal static partial int AutomationLaneCount(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_automation_lane_info")]
    internal static partial NotaResult AutomationLaneInfo(IntPtr engine, int trackId, int laneIndex,
        out int target, out int deviceIndex, out int paramIndex, out int pointCount);

    [LibraryImport(Lib, EntryPoint = "nota_track_automation_get_points")]
    internal static partial int AutomationGetPoints(IntPtr engine, int trackId, int laneIndex,
        [Out] AutomationPoint[]? outPoints, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_track_automation_set_points")]
    internal static partial NotaResult AutomationSetPoints(IntPtr engine, int trackId, int laneIndex,
        [In] AutomationPoint[] points, int count);

    [LibraryImport(Lib, EntryPoint = "nota_track_automation_set_points_live")]
    internal static partial NotaResult AutomationSetPointsLive(IntPtr engine, int trackId, int laneIndex,
        [In] AutomationPoint[] points, int count);

    [LibraryImport(Lib, EntryPoint = "nota_track_remove_automation_lane")]
    internal static partial NotaResult RemoveAutomationLane(IntPtr engine, int trackId, int laneIndex);

    [LibraryImport(Lib, EntryPoint = "nota_engine_master_volume_automation_count")]
    internal static partial int MasterVolumeAutomationCount(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_master_volume_automation_get")]
    internal static partial int MasterVolumeAutomationGet(IntPtr engine, [Out] AutomationPoint[]? outPoints, int cap);

    [LibraryImport(Lib, EntryPoint = "nota_engine_master_volume_automation_set")]
    internal static partial NotaResult MasterVolumeAutomationSet(IntPtr engine, [In] AutomationPoint[] points, int count);
}
