// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Audio Effect Rack: the rack surface for a RackDevice in a track's device chain (addressed by deviceIndex).</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h (nota_rackdev_*). Boolean calls return 1/0.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_engine_rack_device_selftest")]
    internal static partial int RackDeviceSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_drum_rack_selftest")]
    internal static partial int DrumRackSelfTest(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_add_plugin_chain_device")]
    internal static partial int RackDevAddPluginChainDevice(IntPtr engine, int trackId, int deviceIndex, int chain, int catalogIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_trigger_note")]
    internal static partial int RackDevChainTriggerNote(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_chain_trigger_note")]
    internal static partial void RackDevSetChainTriggerNote(IntPtr engine, int trackId, int deviceIndex, int chain, int note);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_count")]
    internal static partial int RackDevChainCount(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_add_chain")]
    internal static partial int RackDevAddChain(IntPtr engine, int trackId, int deviceIndex, int instKind);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_remove_chain")]
    internal static partial int RackDevRemoveChain(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_chain_instrument")]
    internal static partial int RackDevSetChainInstrument(IntPtr engine, int trackId, int deviceIndex, int chain, int instKind);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_instrument_kind")]
    internal static partial int RackDevChainInstrumentKind(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_instrument_name")]
    internal static partial IntPtr RackDevChainInstrumentName(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_instrument_param_count")]
    internal static partial int RackDevChainInstrumentParamCount(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_instrument_param_name")]
    internal static partial IntPtr RackDevChainInstrumentParamName(IntPtr engine, int trackId, int deviceIndex, int chain, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_instrument_param_get")]
    internal static partial float RackDevChainInstrumentParamGet(IntPtr engine, int trackId, int deviceIndex, int chain, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_instrument_param_set")]
    internal static partial void RackDevChainInstrumentParamSet(IntPtr engine, int trackId, int deviceIndex, int chain, int param, float normalized);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_device_count")]
    internal static partial int RackDevChainDeviceCount(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_add_chain_device")]
    internal static partial int RackDevAddChainDevice(IntPtr engine, int trackId, int deviceIndex, int chain, int deviceKind);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_remove_chain_device")]
    internal static partial int RackDevRemoveChainDevice(IntPtr engine, int trackId, int deviceIndex, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_move_chain_device")]
    internal static partial int RackDevMoveChainDevice(IntPtr engine, int trackId, int deviceIndex, int chain, int fromIndex, int toIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_device_name")]
    internal static partial IntPtr RackDevChainDeviceName(IntPtr engine, int trackId, int deviceIndex, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_device_builtin_kind")]
    internal static partial int RackDevChainDeviceBuiltinKind(IntPtr engine, int trackId, int deviceIndex, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_open_chain_device_editor")]
    internal static partial void RackDevOpenChainDeviceEditor(IntPtr engine, int trackId, int deviceIndex, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_device_param_count")]
    internal static partial int RackDevChainDeviceParamCount(IntPtr engine, int trackId, int deviceIndex, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_device_param_name")]
    internal static partial IntPtr RackDevChainDeviceParamName(IntPtr engine, int trackId, int deviceIndex, int chain, int dev, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_device_param_min")]
    internal static partial float RackDevChainDeviceParamMin(IntPtr engine, int trackId, int deviceIndex, int chain, int dev, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_device_param_max")]
    internal static partial float RackDevChainDeviceParamMax(IntPtr engine, int trackId, int deviceIndex, int chain, int dev, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_device_param_get")]
    internal static partial float RackDevChainDeviceParamGet(IntPtr engine, int trackId, int deviceIndex, int chain, int dev, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_device_param_set")]
    internal static partial void RackDevChainDeviceParamSet(IntPtr engine, int trackId, int deviceIndex, int chain, int dev, int param, float value);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_chain_device_bypassed")]
    internal static partial void RackDevSetChainDeviceBypassed(IntPtr engine, int trackId, int deviceIndex, int chain, int dev, int bypassed);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_device_bypassed")]
    internal static partial int RackDevChainDeviceBypassed(IntPtr engine, int trackId, int deviceIndex, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_chain_gain")]
    internal static partial void RackDevSetChainGain(IntPtr engine, int trackId, int deviceIndex, int chain, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_chain_pan")]
    internal static partial void RackDevSetChainPan(IntPtr engine, int trackId, int deviceIndex, int chain, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_chain_mute")]
    internal static partial void RackDevSetChainMute(IntPtr engine, int trackId, int deviceIndex, int chain, int b);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_chain_solo")]
    internal static partial void RackDevSetChainSolo(IntPtr engine, int trackId, int deviceIndex, int chain, int b);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_gain")]
    internal static partial float RackDevChainGain(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_pan")]
    internal static partial float RackDevChainPan(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_mute")]
    internal static partial int RackDevChainMute(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_solo")]
    internal static partial int RackDevChainSolo(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_macro_get")]
    internal static partial float RackDevMacroGet(IntPtr engine, int trackId, int deviceIndex, int macro);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_macro_set")]
    internal static partial void RackDevMacroSet(IntPtr engine, int trackId, int deviceIndex, int macro, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_add_macro_mapping")]
    internal static partial int RackDevAddMacroMapping(IntPtr engine, int trackId, int deviceIndex, int macro, int chain,
        int targetDevice, int paramIndex, float rangeMin, float rangeMax);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_mapping_count")]
    internal static partial int RackDevMappingCount(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_mapping_info")]
    internal static partial int RackDevMappingInfo(IntPtr engine, int trackId, int deviceIndex, int index,
        out int macro, out int chain, out int targetDevice, out int paramIndex, out float rangeMin, out float rangeMax);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_remove_mapping")]
    internal static partial int RackDevRemoveMapping(IntPtr engine, int trackId, int deviceIndex, int index);

    // ---- Audio Effect Rack: rack-out, routing, meter, zone, macro-name, mapping edit ----
    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_meter")]
    internal static partial float RackDevChainMeter(IntPtr engine, int trackId, int deviceIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_zone")]
    internal static partial void RackDevChainZone(IntPtr engine, int trackId, int deviceIndex, int chain, out int velLo, out int velHi);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_chain_zone")]
    internal static partial void RackDevSetChainZone(IntPtr engine, int trackId, int deviceIndex, int chain, int velLo, int velHi);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_macro_name")]
    internal static partial IntPtr RackDevMacroName(IntPtr engine, int trackId, int deviceIndex, int macro);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_macro_name", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void RackDevSetMacroName(IntPtr engine, int trackId, int deviceIndex, int macro, string name);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_mapping_range")]
    internal static partial int RackDevSetMappingRange(IntPtr engine, int trackId, int deviceIndex, int index, float rangeMin, float rangeMax);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_mapping_curve")]
    internal static partial int RackDevMappingCurve(IntPtr engine, int trackId, int deviceIndex, int index);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_mapping_curve")]
    internal static partial int RackDevSetMappingCurve(IntPtr engine, int trackId, int deviceIndex, int index, int curve);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_volume")]
    internal static partial float RackDevVolume(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_volume")]
    internal static partial void RackDevSetVolume(IntPtr engine, int trackId, int deviceIndex, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_mode")]
    internal static partial int RackDevMode(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_mode")]
    internal static partial void RackDevSetMode(IntPtr engine, int trackId, int deviceIndex, int mode);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_drywet")]
    internal static partial float RackDevDryWet(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_drywet")]
    internal static partial void RackDevSetDryWet(IntPtr engine, int trackId, int deviceIndex, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_pdc")]
    internal static partial int RackDevPdc(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_pdc")]
    internal static partial void RackDevSetPdc(IntPtr engine, int trackId, int deviceIndex, int on);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_chain_select")]
    internal static partial float RackDevChainSelect(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_chain_select")]
    internal static partial void RackDevSetChainSelect(IntPtr engine, int trackId, int deviceIndex, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_sel_follow")]
    internal static partial int RackDevSelFollow(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_set_sel_follow")]
    internal static partial void RackDevSetSelFollow(IntPtr engine, int trackId, int deviceIndex, int on);

    [LibraryImport(Lib, EntryPoint = "nota_rackdev_live_selector")]
    internal static partial float RackDevLiveSelector(IntPtr engine, int trackId, int deviceIndex);
}
