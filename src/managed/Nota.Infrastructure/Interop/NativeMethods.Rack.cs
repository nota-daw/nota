// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Instrument Rack: parallel chains (instrument + devices), per-chain controls, 8 macros + mappings.</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h. Boolean ABI calls return 1/0.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_engine_add_instrument_rack_track")]
    internal static partial int AddInstrumentRackTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_engine_add_drum_rack_track")]
    internal static partial int AddDrumRackTrack(IntPtr engine);

    [LibraryImport(Lib, EntryPoint = "nota_rack_add_sampler_chain", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RackAddSamplerChain(IntPtr engine, int trackId, string path, int rootNote, int loop);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_sampler_sample", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RackSetChainSamplerSample(IntPtr engine, int trackId, int chain, string path, int rootNote);

    [LibraryImport(Lib, EntryPoint = "nota_rack_open_chain_instrument_editor")]
    internal static partial void RackOpenChainInstrumentEditor(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_open_chain_device_editor")]
    internal static partial void RackOpenChainDeviceEditor(IntPtr engine, int trackId, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_instrument_plugin_id")]
    internal static partial IntPtr RackChainInstrumentPluginId(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_instrument_get_state")]
    internal static partial int RackChainInstrumentGetState(IntPtr engine, int trackId, int chain, [Out] byte[]? outBuffer, int capacity);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_instrument_param_default")]
    internal static partial float RackChainInstrumentParamDefault(IntPtr engine, int trackId, int chain, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_sampler_info")]
    internal static partial int RackChainSamplerInfo(IntPtr engine, int trackId, int chain, out NotaSamplerInfo info);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_sampler_root")]
    internal static partial int RackSetChainSamplerRoot(IntPtr engine, int trackId, int chain, int rootNote);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_sampler_play_position")]
    internal static partial float RackChainSamplerPlayPosition(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_add_plugin_instrument_chain")]
    internal static partial int RackAddPluginInstrumentChain(IntPtr engine, int trackId, int catalogIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rack_add_plugin_chain_device")]
    internal static partial int RackAddPluginChainDevice(IntPtr engine, int trackId, int chain, int catalogIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_trigger_note")]
    internal static partial int RackChainTriggerNote(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_trigger_note")]
    internal static partial void RackSetChainTriggerNote(IntPtr engine, int trackId, int chain, int note);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_count")]
    internal static partial int RackChainCount(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_rack_add_chain")]
    internal static partial int RackAddChain(IntPtr engine, int trackId, int instKind);

    [LibraryImport(Lib, EntryPoint = "nota_rack_remove_chain")]
    internal static partial int RackRemoveChain(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_instrument")]
    internal static partial int RackSetChainInstrument(IntPtr engine, int trackId, int chain, int instKind);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_instrument_kind")]
    internal static partial int RackChainInstrumentKind(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_instrument_name")]
    internal static partial IntPtr RackChainInstrumentName(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_instrument_param_count")]
    internal static partial int RackChainInstrumentParamCount(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_instrument_param_name")]
    internal static partial IntPtr RackChainInstrumentParamName(IntPtr engine, int trackId, int chain, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_instrument_param_id")]
    internal static partial IntPtr RackChainInstrumentParamId(IntPtr engine, int trackId, int chain, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_instrument_param_get")]
    internal static partial float RackChainInstrumentParamGet(IntPtr engine, int trackId, int chain, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_instrument_param_set")]
    internal static partial void RackChainInstrumentParamSet(IntPtr engine, int trackId, int chain, int param, float normalized);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_device_count")]
    internal static partial int RackChainDeviceCount(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_add_chain_device")]
    internal static partial int RackAddChainDevice(IntPtr engine, int trackId, int chain, int deviceKind);

    [LibraryImport(Lib, EntryPoint = "nota_rack_remove_chain_device")]
    internal static partial int RackRemoveChainDevice(IntPtr engine, int trackId, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rack_move_chain_device")]
    internal static partial int RackMoveChainDevice(IntPtr engine, int trackId, int chain, int fromIndex, int toIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_device_name")]
    internal static partial IntPtr RackChainDeviceName(IntPtr engine, int trackId, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_device_builtin_kind")]
    internal static partial int RackChainDeviceBuiltinKind(IntPtr engine, int trackId, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_device_param_count")]
    internal static partial int RackChainDeviceParamCount(IntPtr engine, int trackId, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_device_param_name")]
    internal static partial IntPtr RackChainDeviceParamName(IntPtr engine, int trackId, int chain, int dev, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_device_param_min")]
    internal static partial float RackChainDeviceParamMin(IntPtr engine, int trackId, int chain, int dev, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_device_param_max")]
    internal static partial float RackChainDeviceParamMax(IntPtr engine, int trackId, int chain, int dev, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_device_param_get")]
    internal static partial float RackChainDeviceParamGet(IntPtr engine, int trackId, int chain, int dev, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_device_param_set")]
    internal static partial void RackChainDeviceParamSet(IntPtr engine, int trackId, int chain, int dev, int param, float value);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_device_bypassed")]
    internal static partial void RackSetChainDeviceBypassed(IntPtr engine, int trackId, int chain, int dev, int bypassed);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_device_bypassed")]
    internal static partial int RackChainDeviceBypassed(IntPtr engine, int trackId, int chain, int dev);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_gain")]
    internal static partial void RackSetChainGain(IntPtr engine, int trackId, int chain, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_pan")]
    internal static partial void RackSetChainPan(IntPtr engine, int trackId, int chain, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_mute")]
    internal static partial void RackSetChainMute(IntPtr engine, int trackId, int chain, int b);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_solo")]
    internal static partial void RackSetChainSolo(IntPtr engine, int trackId, int chain, int b);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_gain")]
    internal static partial float RackChainGain(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_pan")]
    internal static partial float RackChainPan(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_mute")]
    internal static partial int RackChainMute(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_solo")]
    internal static partial int RackChainSolo(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_macro_get")]
    internal static partial float RackMacroGet(IntPtr engine, int trackId, int macro);

    [LibraryImport(Lib, EntryPoint = "nota_rack_macro_set")]
    internal static partial void RackMacroSet(IntPtr engine, int trackId, int macro, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rack_add_macro_mapping")]
    internal static partial int RackAddMacroMapping(IntPtr engine, int trackId, int macro, int chain,
        int deviceIndex, int paramIndex, float rangeMin, float rangeMax);

    [LibraryImport(Lib, EntryPoint = "nota_rack_mapping_count")]
    internal static partial int RackMappingCount(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_rack_mapping_info")]
    internal static partial int RackMappingInfo(IntPtr engine, int trackId, int index,
        out int macro, out int chain, out int deviceIndex, out int paramIndex, out float rangeMin, out float rangeMax);

    [LibraryImport(Lib, EntryPoint = "nota_rack_remove_mapping")]
    internal static partial int RackRemoveMapping(IntPtr engine, int trackId, int index);

    // Instrument-Rack extras (mockup 2p).
    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_zone")]
    internal static partial void RackChainZone(IntPtr engine, int trackId, int chain, out int keyLo, out int keyHi, out int velLo, out int velHi);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_zone")]
    internal static partial void RackSetChainZone(IntPtr engine, int trackId, int chain, int keyLo, int keyHi, int velLo, int velHi);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_meter")]
    internal static partial float RackChainMeter(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_macro_name")]
    internal static partial IntPtr RackMacroName(IntPtr engine, int trackId, int macro);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_macro_name", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void RackSetMacroName(IntPtr engine, int trackId, int macro, string name);

    [LibraryImport(Lib, EntryPoint = "nota_rack_volume")]
    internal static partial float RackVolume(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_volume")]
    internal static partial void RackSetVolume(IntPtr engine, int trackId, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rack_glide")]
    internal static partial float RackGlide(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_glide")]
    internal static partial void RackSetGlide(IntPtr engine, int trackId, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_mapping_range")]
    internal static partial int RackSetMappingRange(IntPtr engine, int trackId, int index, float rangeMin, float rangeMax);

    [LibraryImport(Lib, EntryPoint = "nota_rack_mapping_curve")]
    internal static partial int RackMappingCurve(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_mapping_curve")]
    internal static partial int RackSetMappingCurve(IntPtr engine, int trackId, int index, int curve);

    // ---- Drum Rack per-pad shaping + kit-level swing/humanize ----
    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_choke")]
    internal static partial void RackSetChainChoke(IntPtr engine, int trackId, int chain, int group);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_choke")]
    internal static partial int RackChainChoke(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_tune")]
    internal static partial void RackSetChainTune(IntPtr engine, int trackId, int chain, int semitones);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_tune")]
    internal static partial int RackChainTune(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_chain_decay")]
    internal static partial void RackSetChainDecay(IntPtr engine, int trackId, int chain, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_decay")]
    internal static partial float RackChainDecay(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_swing")]
    internal static partial void RackSetSwing(IntPtr engine, int trackId, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rack_swing")]
    internal static partial float RackSwing(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_rack_set_humanize")]
    internal static partial void RackSetHumanize(IntPtr engine, int trackId, float v);

    [LibraryImport(Lib, EntryPoint = "nota_rack_humanize")]
    internal static partial float RackHumanize(IntPtr engine, int trackId);
}
