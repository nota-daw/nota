// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>Hosted plugins & device chain: load, params, bypass, editor, state, PDC, kind/id introspection.</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_engine_add_plugin_instrument_track")]
    internal static partial int AddPluginInstrumentTrack(IntPtr engine, int catalogIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_instrument_plugin")]
    internal static partial NotaResult SetTrackInstrumentPlugin(IntPtr engine, int trackId, int catalogIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_builtin_instrument")]
    internal static partial NotaResult SetTrackBuiltinInstrument(IntPtr engine, int trackId, int kind);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_effect_plugin")]
    internal static partial int AddTrackEffectPlugin(IntPtr engine, int trackId, int catalogIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_device_count")]
    internal static partial int TrackDeviceCount(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_clip_audio_mono")]
    internal static partial int ClipAudioMono(IntPtr engine, int trackId, int clipIndex,
        [Out] float[]? outBuf, int maxFrames, out double outSr);

    [LibraryImport(Lib, EntryPoint = "nota_track_add_builtin_device")]
    internal static partial int AddBuiltinDevice(IntPtr engine, int trackId, int kind);

    [LibraryImport(Lib, EntryPoint = "nota_track_move_device")]
    internal static partial NotaResult MoveDevice(IntPtr engine, int trackId, int fromIndex, int toIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_remove_device")]
    internal static partial NotaResult RemoveDevice(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_name")]
    internal static partial IntPtr DeviceName(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_param_count")]
    internal static partial int DeviceParamCount(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_param_name")]
    internal static partial IntPtr DeviceParamName(IntPtr engine, int trackId, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_param_min")]
    internal static partial float DeviceParamMin(IntPtr engine, int trackId, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_param_max")]
    internal static partial float DeviceParamMax(IntPtr engine, int trackId, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_get_param")]
    internal static partial float DeviceGetParam(IntPtr engine, int trackId, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_set_param")]
    internal static partial NotaResult DeviceSetParam(IntPtr engine, int trackId, int deviceIndex, int paramIndex, float value);

    [LibraryImport(Lib, EntryPoint = "nota_device_gain_reduction")]
    internal static partial float DeviceGainReduction(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_param_default")]
    internal static partial float DeviceParamDefault(IntPtr engine, int trackId, int deviceIndex, int paramIndex);
    [LibraryImport(Lib, EntryPoint = "nota_instrument_param_default")]
    internal static partial float InstrumentParamDefault(IntPtr engine, int trackId, int paramIndex);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_param_default")]
    internal static partial float MidiEffectParamDefault(IntPtr engine, int trackId, int index, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_scope")]
    internal static partial int DeviceScope(IntPtr engine, int trackId, int deviceIndex, [Out] float[] outSamples, int maxSamples);

    [LibraryImport(Lib, EntryPoint = "nota_device_action")]
    internal static partial void DeviceAction(IntPtr engine, int trackId, int deviceIndex, int id, int iarg, float farg);
    [LibraryImport(Lib, EntryPoint = "nota_device_layer_wave")]
    internal static partial int DeviceLayerWave(IntPtr engine, int trackId, int deviceIndex, int layer, [Out] float[] outSamples, int maxSamples);
    [LibraryImport(Lib, EntryPoint = "nota_device_get_state")]
    internal static partial int DeviceGetState(IntPtr engine, int trackId, int deviceIndex, [Out] byte[]? outBytes, int cap);
    [LibraryImport(Lib, EntryPoint = "nota_device_set_state")]
    internal static partial void DeviceSetState(IntPtr engine, int trackId, int deviceIndex, [In] byte[] data, int size);

    // --- MIDI effects ---
    [LibraryImport(Lib, EntryPoint = "nota_track_add_midi_effect")]
    internal static partial int AddMidiEffect(IntPtr engine, int trackId, int kind);
    [LibraryImport(Lib, EntryPoint = "nota_track_midi_effect_count")]
    internal static partial int TrackMidiEffectCount(IntPtr engine, int trackId);
    [LibraryImport(Lib, EntryPoint = "nota_track_move_midi_effect")]
    internal static partial NotaResult MoveMidiEffect(IntPtr engine, int trackId, int fromIndex, int toIndex);
    [LibraryImport(Lib, EntryPoint = "nota_track_remove_midi_effect")]
    internal static partial NotaResult RemoveMidiEffect(IntPtr engine, int trackId, int index);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_kind")]
    internal static partial int MidiEffectKind(IntPtr engine, int trackId, int index);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_last_in")]
    internal static partial int MidiEffectLastIn(IntPtr engine, int trackId, int index);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_last_out")]
    internal static partial int MidiEffectLastOut(IntPtr engine, int trackId, int index);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_scope")]
    internal static partial int MidiEffectScope(IntPtr engine, int trackId, int index, [Out] float[] outv, int maxN);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_name")]
    internal static partial IntPtr MidiEffectName(IntPtr engine, int trackId, int index);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_param_count")]
    internal static partial int MidiEffectParamCount(IntPtr engine, int trackId, int index);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_param_name")]
    internal static partial IntPtr MidiEffectParamName(IntPtr engine, int trackId, int index, int paramIndex);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_param_min")]
    internal static partial float MidiEffectParamMin(IntPtr engine, int trackId, int index, int paramIndex);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_param_max")]
    internal static partial float MidiEffectParamMax(IntPtr engine, int trackId, int index, int paramIndex);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_get_param")]
    internal static partial float MidiEffectGetParam(IntPtr engine, int trackId, int index, int paramIndex);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_set_param")]
    internal static partial NotaResult MidiEffectSetParam(IntPtr engine, int trackId, int index, int paramIndex, float value);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_set_bypassed")]
    internal static partial void MidiEffectSetBypassed(IntPtr engine, int trackId, int index, int bypassed);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_bypassed")]
    internal static partial int MidiEffectBypassed(IntPtr engine, int trackId, int index);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_set_cc_dest")]
    internal static partial void MidiEffectSetCcDest(IntPtr engine, int trackId, int index, int destDevice, int destParam);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_set_cc_depth")]
    internal static partial void MidiEffectSetCcDepth(IntPtr engine, int trackId, int index, float depth);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_cc_dest_device")]
    internal static partial int MidiEffectCcDestDevice(IntPtr engine, int trackId, int index);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_cc_dest_param")]
    internal static partial int MidiEffectCcDestParam(IntPtr engine, int trackId, int index);
    [LibraryImport(Lib, EntryPoint = "nota_midi_effect_cc_depth")]
    internal static partial float MidiEffectCcDepth(IntPtr engine, int trackId, int index);

    [LibraryImport(Lib, EntryPoint = "nota_device_set_sidechain_source")]
    internal static partial void DeviceSetSidechainSource(IntPtr engine, int trackId, int deviceIndex, int sourceTrackId);

    [LibraryImport(Lib, EntryPoint = "nota_device_sidechain_source")]
    internal static partial int DeviceSidechainSource(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_accepts_sidechain")]
    internal static partial int DeviceAcceptsSidechain(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_instrument_sidechain_source")]
    internal static partial void TrackSetInstrumentSidechainSource(IntPtr engine, int trackId, int sourceTrackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_instrument_sidechain_source")]
    internal static partial int TrackInstrumentSidechainSource(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_instrument_accepts_sidechain")]
    internal static partial int TrackInstrumentAcceptsSidechain(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_device_set_sidechain_gain")]
    internal static partial void DeviceSetSidechainGain(IntPtr engine, int trackId, int deviceIndex, float gainDb);

    [LibraryImport(Lib, EntryPoint = "nota_device_sidechain_gain")]
    internal static partial float DeviceSidechainGain(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_set_sidechain_mix")]
    internal static partial void DeviceSetSidechainMix(IntPtr engine, int trackId, int deviceIndex, float mix);

    [LibraryImport(Lib, EntryPoint = "nota_device_sidechain_mix")]
    internal static partial float DeviceSidechainMix(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_device_set_sidechain_tap_pre")]
    internal static partial void DeviceSetSidechainTapPre(IntPtr engine, int trackId, int deviceIndex, int pre);

    [LibraryImport(Lib, EntryPoint = "nota_device_sidechain_tap_pre")]
    internal static partial int DeviceSidechainTapPre(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_fx_selftest")]
    internal static partial int FxSelfTest();

    [LibraryImport(Lib, EntryPoint = "nota_plugin_open_editor")]
    internal static partial NotaResult PluginOpenEditor(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_plugin_close_editor")]
    internal static partial NotaResult PluginCloseEditor(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_plugin_get_state")]
    internal static partial int PluginGetState(IntPtr engine, int trackId, int deviceIndex,
                                               [Out] byte[]? outBuffer, int capacity);

    [LibraryImport(Lib, EntryPoint = "nota_plugin_set_state")]
    internal static partial NotaResult PluginSetState(IntPtr engine, int trackId, int deviceIndex,
                                                      [In] byte[] data, int size);

    [LibraryImport(Lib, EntryPoint = "nota_track_set_device_bypassed")]
    internal static partial NotaResult SetDeviceBypassed(IntPtr engine, int trackId, int deviceIndex, int bypassed);

    [LibraryImport(Lib, EntryPoint = "nota_track_device_bypassed")]
    internal static partial int DeviceBypassed(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_latency_samples")]
    internal static partial int TrackLatencySamples(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_pdc_selftest")]
    internal static partial int PdcSelfTest();

    [LibraryImport(Lib, EntryPoint = "nota_track_instrument_kind")]
    internal static partial int TrackInstrumentKind(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_device_builtin_kind")]
    internal static partial int TrackDeviceBuiltinKind(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_track_instrument_plugin_id")]
    internal static partial IntPtr TrackInstrumentPluginId(IntPtr engine, int trackId);

    [LibraryImport(Lib, EntryPoint = "nota_track_device_plugin_id")]
    internal static partial IntPtr TrackDevicePluginId(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_plugin_param_count")]
    internal static partial int PluginParamCount(IntPtr engine, int trackId, int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "nota_plugin_param_id")]
    internal static partial IntPtr PluginParamId(IntPtr engine, int trackId, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_plugin_param_name")]
    internal static partial IntPtr PluginParamName(IntPtr engine, int trackId, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_plugin_param_get")]
    internal static partial float PluginParamGet(IntPtr engine, int trackId, int deviceIndex, int paramIndex);

    [LibraryImport(Lib, EntryPoint = "nota_plugin_param_set")]
    internal static partial NotaResult PluginParamSet(IntPtr engine, int trackId, int deviceIndex, int paramIndex, float normalized);

    [LibraryImport(Lib, EntryPoint = "nota_plugin_last_touched_param")]
    internal static partial int PluginLastTouchedParam(IntPtr engine, int trackId, int deviceIndex);
}
