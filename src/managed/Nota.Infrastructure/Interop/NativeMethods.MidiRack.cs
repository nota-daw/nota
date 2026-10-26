// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

/// <summary>MIDI Effect Rack (MIDI kind 6, specs/040) and the MIDI effects in an Instrument Rack chain (#329).</summary>
/// <remarks>Part of <see cref="NotaEngine"/>'s P/Invoke surface; see nota_engine.h. Boolean ABI calls return 1/0.</remarks>
internal static partial class NativeMethods
{
    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_count")]
    internal static partial int RackChainMidiCount(IntPtr engine, int trackId, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_add")]
    internal static partial int RackChainMidiAdd(IntPtr engine, int trackId, int chain, int midiKind);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_remove")]
    internal static partial int RackChainMidiRemove(IntPtr engine, int trackId, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_move")]
    internal static partial int RackChainMidiMove(IntPtr engine, int trackId, int chain, int fromIndex, int toIndex);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_kind")]
    internal static partial int RackChainMidiKind(IntPtr engine, int trackId, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_name")]
    internal static partial IntPtr RackChainMidiName(IntPtr engine, int trackId, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_param_count")]
    internal static partial int RackChainMidiParamCount(IntPtr engine, int trackId, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_param_name")]
    internal static partial IntPtr RackChainMidiParamName(IntPtr engine, int trackId, int chain, int index, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_param_min")]
    internal static partial float RackChainMidiParamMin(IntPtr engine, int trackId, int chain, int index, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_param_max")]
    internal static partial float RackChainMidiParamMax(IntPtr engine, int trackId, int chain, int index, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_param_get")]
    internal static partial float RackChainMidiParamGet(IntPtr engine, int trackId, int chain, int index, int param);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_param_set")]
    internal static partial void RackChainMidiParamSet(IntPtr engine, int trackId, int chain, int index, int param, float value);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_set_bypassed")]
    internal static partial void RackChainMidiSetBypassed(IntPtr engine, int trackId, int chain, int index, int bypassed);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_bypassed")]
    internal static partial int RackChainMidiBypassed(IntPtr engine, int trackId, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_last_in")]
    internal static partial int RackChainMidiLastIn(IntPtr engine, int trackId, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_last_out")]
    internal static partial int RackChainMidiLastOut(IntPtr engine, int trackId, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_scope")]
    internal static partial int RackChainMidiScope(IntPtr engine, int trackId, int chain, int index, [Out] float[] outv, int maxN);

    [LibraryImport(Lib, EntryPoint = "nota_rack_chain_midi_command")]
    internal static partial void RackChainMidiCommand(IntPtr engine, int trackId, int chain, int index, int cmd);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_count")]
    internal static partial int MidiRackChainDeviceCount(IntPtr engine, int trackId, int midiIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_add")]
    internal static partial int MidiRackChainDeviceAdd(IntPtr engine, int trackId, int midiIndex, int chain, int midiKind);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_remove")]
    internal static partial int MidiRackChainDeviceRemove(IntPtr engine, int trackId, int midiIndex, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_move")]
    internal static partial int MidiRackChainDeviceMove(IntPtr engine, int trackId, int midiIndex, int chain, int fromIndex, int toIndex);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_kind")]
    internal static partial int MidiRackChainDeviceKind(IntPtr engine, int trackId, int midiIndex, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_name")]
    internal static partial IntPtr MidiRackChainDeviceName(IntPtr engine, int trackId, int midiIndex, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_param_count")]
    internal static partial int MidiRackChainDeviceParamCount(IntPtr engine, int trackId, int midiIndex, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_param_name")]
    internal static partial IntPtr MidiRackChainDeviceParamName(IntPtr engine, int trackId, int midiIndex, int chain, int index, int param);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_param_min")]
    internal static partial float MidiRackChainDeviceParamMin(IntPtr engine, int trackId, int midiIndex, int chain, int index, int param);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_param_max")]
    internal static partial float MidiRackChainDeviceParamMax(IntPtr engine, int trackId, int midiIndex, int chain, int index, int param);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_param_get")]
    internal static partial float MidiRackChainDeviceParamGet(IntPtr engine, int trackId, int midiIndex, int chain, int index, int param);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_param_set")]
    internal static partial void MidiRackChainDeviceParamSet(IntPtr engine, int trackId, int midiIndex, int chain, int index, int param, float value);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_set_bypassed")]
    internal static partial void MidiRackChainDeviceSetBypassed(IntPtr engine, int trackId, int midiIndex, int chain, int index, int bypassed);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_bypassed")]
    internal static partial int MidiRackChainDeviceBypassed(IntPtr engine, int trackId, int midiIndex, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_last_in")]
    internal static partial int MidiRackChainDeviceLastIn(IntPtr engine, int trackId, int midiIndex, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_last_out")]
    internal static partial int MidiRackChainDeviceLastOut(IntPtr engine, int trackId, int midiIndex, int chain, int index);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_scope")]
    internal static partial int MidiRackChainDeviceScope(IntPtr engine, int trackId, int midiIndex, int chain, int index, [Out] float[] outv, int maxN);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_device_command")]
    internal static partial void MidiRackChainDeviceCommand(IntPtr engine, int trackId, int midiIndex, int chain, int index, int cmd);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_count")]
    internal static partial int MidiRackChainCount(IntPtr engine, int trackId, int midiIndex);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_add_chain")]
    internal static partial int MidiRackAddChain(IntPtr engine, int trackId, int midiIndex);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_remove_chain")]
    internal static partial int MidiRackRemoveChain(IntPtr engine, int trackId, int midiIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_duplicate_chain")]
    internal static partial int MidiRackDuplicateChain(IntPtr engine, int trackId, int midiIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_name")]
    internal static partial IntPtr MidiRackChainName(IntPtr engine, int trackId, int midiIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_chain_name", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void MidiRackSetChainName(IntPtr engine, int trackId, int midiIndex, int chain, string name);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_chain_mute")]
    internal static partial void MidiRackSetChainMute(IntPtr engine, int trackId, int midiIndex, int chain, int on);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_mute")]
    internal static partial int MidiRackChainMute(IntPtr engine, int trackId, int midiIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_chain_solo")]
    internal static partial void MidiRackSetChainSolo(IntPtr engine, int trackId, int midiIndex, int chain, int on);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_solo")]
    internal static partial int MidiRackChainSolo(IntPtr engine, int trackId, int midiIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_chain_zone")]
    internal static partial void MidiRackSetChainZone(IntPtr engine, int trackId, int midiIndex, int chain, int keyLo, int keyHi, int velLo, int velHi);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_zone")]
    internal static partial void MidiRackChainZone(IntPtr engine, int trackId, int midiIndex, int chain, out int keyLo, out int keyHi, out int velLo, out int velHi);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_chain_select")]
    internal static partial void MidiRackSetChainSelect(IntPtr engine, int trackId, int midiIndex, int chain, int lo, int hi);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_select_range")]
    internal static partial void MidiRackChainSelectRange(IntPtr engine, int trackId, int midiIndex, int chain, out int lo, out int hi);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_chain_transpose")]
    internal static partial void MidiRackSetChainTranspose(IntPtr engine, int trackId, int midiIndex, int chain, int semitones);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_transpose")]
    internal static partial int MidiRackChainTranspose(IntPtr engine, int trackId, int midiIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_chain_vel_scale")]
    internal static partial void MidiRackSetChainVelScale(IntPtr engine, int trackId, int midiIndex, int chain, float scale);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_vel_scale")]
    internal static partial float MidiRackChainVelScale(IntPtr engine, int trackId, int midiIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_chain_activity")]
    internal static partial float MidiRackChainActivity(IntPtr engine, int trackId, int midiIndex, int chain);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_mode")]
    internal static partial void MidiRackSetMode(IntPtr engine, int trackId, int midiIndex, int mode);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_mode")]
    internal static partial int MidiRackMode(IntPtr engine, int trackId, int midiIndex);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_selector")]
    internal static partial void MidiRackSetSelector(IntPtr engine, int trackId, int midiIndex, float v);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_selector")]
    internal static partial float MidiRackSelector(IntPtr engine, int trackId, int midiIndex);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_merge")]
    internal static partial void MidiRackSetMerge(IntPtr engine, int trackId, int midiIndex, int on);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_merge")]
    internal static partial int MidiRackMerge(IntPtr engine, int trackId, int midiIndex);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_thru")]
    internal static partial void MidiRackSetThru(IntPtr engine, int trackId, int midiIndex, int on);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_thru")]
    internal static partial int MidiRackThru(IntPtr engine, int trackId, int midiIndex);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_macro_get")]
    internal static partial float MidiRackMacroGet(IntPtr engine, int trackId, int midiIndex, int macro);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_macro_set")]
    internal static partial void MidiRackMacroSet(IntPtr engine, int trackId, int midiIndex, int macro, float v);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_macro_name")]
    internal static partial IntPtr MidiRackMacroName(IntPtr engine, int trackId, int midiIndex, int macro);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_macro_name", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void MidiRackSetMacroName(IntPtr engine, int trackId, int midiIndex, int macro, string name);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_add_macro_mapping")]
    internal static partial int MidiRackAddMacroMapping(IntPtr engine, int trackId, int midiIndex, int macro, int chain, int device, int param, float lo, float hi);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_mapping_count")]
    internal static partial int MidiRackMappingCount(IntPtr engine, int trackId, int midiIndex);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_mapping_info")]
    internal static partial int MidiRackMappingInfo(IntPtr engine, int trackId, int midiIndex, int index, out int macro, out int chain, out int device, out int param, out float lo, out float hi, out int curve);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_remove_mapping")]
    internal static partial int MidiRackRemoveMapping(IntPtr engine, int trackId, int midiIndex, int index);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_mapping_range")]
    internal static partial int MidiRackSetMappingRange(IntPtr engine, int trackId, int midiIndex, int index, float lo, float hi);

    [LibraryImport(Lib, EntryPoint = "nota_midirack_set_mapping_curve")]
    internal static partial int MidiRackSetMappingCurve(IntPtr engine, int trackId, int midiIndex, int index, int curve);
}
