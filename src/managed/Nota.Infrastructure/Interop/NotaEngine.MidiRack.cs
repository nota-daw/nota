// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// NotaEngine — the MIDI Effect Rack (a MIDI effect, addressed by track + MIDI effect index)
// and the MIDI effects in an Instrument Rack chain (addressed by track + chain).

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    public int RackChainMidiCount(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiCount(_handle, trackId, chain); }

    public int RackChainMidiAdd(int trackId, int chain, int midiKind)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiAdd(_handle, trackId, chain, midiKind); }

    public bool RackChainMidiRemove(int trackId, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiRemove(_handle, trackId, chain, index) != 0; }

    public bool RackChainMidiMove(int trackId, int chain, int fromIndex, int toIndex)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiMove(_handle, trackId, chain, fromIndex, toIndex) != 0; }

    public int RackChainMidiKind(int trackId, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiKind(_handle, trackId, chain, index); }

    public string RackChainMidiName(int trackId, int chain, int index)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackChainMidiName(_handle, trackId, chain, index)) ?? ""; }

    public int RackChainMidiParamCount(int trackId, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiParamCount(_handle, trackId, chain, index); }

    public string RackChainMidiParamName(int trackId, int chain, int index, int param)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackChainMidiParamName(_handle, trackId, chain, index, param)) ?? ""; }

    public float RackChainMidiParamMin(int trackId, int chain, int index, int param)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiParamMin(_handle, trackId, chain, index, param); }

    public float RackChainMidiParamMax(int trackId, int chain, int index, int param)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiParamMax(_handle, trackId, chain, index, param); }

    public float RackChainMidiParamGet(int trackId, int chain, int index, int param)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiParamGet(_handle, trackId, chain, index, param); }

    public void RackChainMidiParamSet(int trackId, int chain, int index, int param, float value)
    { ThrowIfDisposed(); NativeMethods.RackChainMidiParamSet(_handle, trackId, chain, index, param, value); }

    public void RackChainMidiSetBypassed(int trackId, int chain, int index, bool bypassed)
    { ThrowIfDisposed(); NativeMethods.RackChainMidiSetBypassed(_handle, trackId, chain, index, bypassed ? 1 : 0); }

    public bool RackChainMidiBypassed(int trackId, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiBypassed(_handle, trackId, chain, index) != 0; }

    public int RackChainMidiLastIn(int trackId, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiLastIn(_handle, trackId, chain, index); }

    public int RackChainMidiLastOut(int trackId, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiLastOut(_handle, trackId, chain, index); }

    public int RackChainMidiScope(int trackId, int chain, int index, float[] outv)
    { ThrowIfDisposed(); return NativeMethods.RackChainMidiScope(_handle, trackId, chain, index, outv, outv.Length); }

    public void RackChainMidiCommand(int trackId, int chain, int index, int cmd)
    { ThrowIfDisposed(); NativeMethods.RackChainMidiCommand(_handle, trackId, chain, index, cmd); }

    public int MidiRackChainDeviceCount(int trackId, int midiIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceCount(_handle, trackId, midiIndex, chain); }

    public int MidiRackChainDeviceAdd(int trackId, int midiIndex, int chain, int midiKind)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceAdd(_handle, trackId, midiIndex, chain, midiKind); }

    public bool MidiRackChainDeviceRemove(int trackId, int midiIndex, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceRemove(_handle, trackId, midiIndex, chain, index) != 0; }

    public bool MidiRackChainDeviceMove(int trackId, int midiIndex, int chain, int fromIndex, int toIndex)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceMove(_handle, trackId, midiIndex, chain, fromIndex, toIndex) != 0; }

    public int MidiRackChainDeviceKind(int trackId, int midiIndex, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceKind(_handle, trackId, midiIndex, chain, index); }

    public string MidiRackChainDeviceName(int trackId, int midiIndex, int chain, int index)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.MidiRackChainDeviceName(_handle, trackId, midiIndex, chain, index)) ?? ""; }

    public int MidiRackChainDeviceParamCount(int trackId, int midiIndex, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceParamCount(_handle, trackId, midiIndex, chain, index); }

    public string MidiRackChainDeviceParamName(int trackId, int midiIndex, int chain, int index, int param)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.MidiRackChainDeviceParamName(_handle, trackId, midiIndex, chain, index, param)) ?? ""; }

    public float MidiRackChainDeviceParamMin(int trackId, int midiIndex, int chain, int index, int param)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceParamMin(_handle, trackId, midiIndex, chain, index, param); }

    public float MidiRackChainDeviceParamMax(int trackId, int midiIndex, int chain, int index, int param)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceParamMax(_handle, trackId, midiIndex, chain, index, param); }

    public float MidiRackChainDeviceParamGet(int trackId, int midiIndex, int chain, int index, int param)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceParamGet(_handle, trackId, midiIndex, chain, index, param); }

    public void MidiRackChainDeviceParamSet(int trackId, int midiIndex, int chain, int index, int param, float value)
    { ThrowIfDisposed(); NativeMethods.MidiRackChainDeviceParamSet(_handle, trackId, midiIndex, chain, index, param, value); }

    public void MidiRackChainDeviceSetBypassed(int trackId, int midiIndex, int chain, int index, bool bypassed)
    { ThrowIfDisposed(); NativeMethods.MidiRackChainDeviceSetBypassed(_handle, trackId, midiIndex, chain, index, bypassed ? 1 : 0); }

    public bool MidiRackChainDeviceBypassed(int trackId, int midiIndex, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceBypassed(_handle, trackId, midiIndex, chain, index) != 0; }

    public int MidiRackChainDeviceLastIn(int trackId, int midiIndex, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceLastIn(_handle, trackId, midiIndex, chain, index); }

    public int MidiRackChainDeviceLastOut(int trackId, int midiIndex, int chain, int index)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceLastOut(_handle, trackId, midiIndex, chain, index); }

    public int MidiRackChainDeviceScope(int trackId, int midiIndex, int chain, int index, float[] outv)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainDeviceScope(_handle, trackId, midiIndex, chain, index, outv, outv.Length); }

    public void MidiRackChainDeviceCommand(int trackId, int midiIndex, int chain, int index, int cmd)
    { ThrowIfDisposed(); NativeMethods.MidiRackChainDeviceCommand(_handle, trackId, midiIndex, chain, index, cmd); }

    public int MidiRackChainCount(int trackId, int midiIndex)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainCount(_handle, trackId, midiIndex); }

    public int MidiRackAddChain(int trackId, int midiIndex)
    { ThrowIfDisposed(); return NativeMethods.MidiRackAddChain(_handle, trackId, midiIndex); }

    public bool MidiRackRemoveChain(int trackId, int midiIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.MidiRackRemoveChain(_handle, trackId, midiIndex, chain) != 0; }

    public int MidiRackDuplicateChain(int trackId, int midiIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.MidiRackDuplicateChain(_handle, trackId, midiIndex, chain); }

    public string MidiRackChainName(int trackId, int midiIndex, int chain)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.MidiRackChainName(_handle, trackId, midiIndex, chain)) ?? ""; }

    public void MidiRackSetChainName(int trackId, int midiIndex, int chain, string name)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetChainName(_handle, trackId, midiIndex, chain, name ?? ""); }

    public void MidiRackSetChainMute(int trackId, int midiIndex, int chain, bool on)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetChainMute(_handle, trackId, midiIndex, chain, on ? 1 : 0); }

    public bool MidiRackChainMute(int trackId, int midiIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainMute(_handle, trackId, midiIndex, chain) != 0; }

    public void MidiRackSetChainSolo(int trackId, int midiIndex, int chain, bool on)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetChainSolo(_handle, trackId, midiIndex, chain, on ? 1 : 0); }

    public bool MidiRackChainSolo(int trackId, int midiIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainSolo(_handle, trackId, midiIndex, chain) != 0; }

    public void MidiRackSetChainZone(int trackId, int midiIndex, int chain, int keyLo, int keyHi, int velLo, int velHi)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetChainZone(_handle, trackId, midiIndex, chain, keyLo, keyHi, velLo, velHi); }

    public void MidiRackChainZone(int trackId, int midiIndex, int chain, out int keyLo, out int keyHi, out int velLo, out int velHi)
    { ThrowIfDisposed(); NativeMethods.MidiRackChainZone(_handle, trackId, midiIndex, chain, out keyLo, out keyHi, out velLo, out velHi); }

    public void MidiRackSetChainSelect(int trackId, int midiIndex, int chain, int lo, int hi)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetChainSelect(_handle, trackId, midiIndex, chain, lo, hi); }

    public void MidiRackChainSelectRange(int trackId, int midiIndex, int chain, out int lo, out int hi)
    { ThrowIfDisposed(); NativeMethods.MidiRackChainSelectRange(_handle, trackId, midiIndex, chain, out lo, out hi); }

    public void MidiRackSetChainTranspose(int trackId, int midiIndex, int chain, int semitones)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetChainTranspose(_handle, trackId, midiIndex, chain, semitones); }

    public int MidiRackChainTranspose(int trackId, int midiIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainTranspose(_handle, trackId, midiIndex, chain); }

    public void MidiRackSetChainVelScale(int trackId, int midiIndex, int chain, float scale)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetChainVelScale(_handle, trackId, midiIndex, chain, scale); }

    public float MidiRackChainVelScale(int trackId, int midiIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainVelScale(_handle, trackId, midiIndex, chain); }

    public float MidiRackChainActivity(int trackId, int midiIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.MidiRackChainActivity(_handle, trackId, midiIndex, chain); }

    public void MidiRackSetMode(int trackId, int midiIndex, int mode)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetMode(_handle, trackId, midiIndex, mode); }

    public int MidiRackMode(int trackId, int midiIndex)
    { ThrowIfDisposed(); return NativeMethods.MidiRackMode(_handle, trackId, midiIndex); }

    public void MidiRackSetSelector(int trackId, int midiIndex, float v)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetSelector(_handle, trackId, midiIndex, v); }

    public float MidiRackSelector(int trackId, int midiIndex)
    { ThrowIfDisposed(); return NativeMethods.MidiRackSelector(_handle, trackId, midiIndex); }

    public void MidiRackSetMerge(int trackId, int midiIndex, bool on)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetMerge(_handle, trackId, midiIndex, on ? 1 : 0); }

    public bool MidiRackMerge(int trackId, int midiIndex)
    { ThrowIfDisposed(); return NativeMethods.MidiRackMerge(_handle, trackId, midiIndex) != 0; }

    public void MidiRackSetThru(int trackId, int midiIndex, bool on)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetThru(_handle, trackId, midiIndex, on ? 1 : 0); }

    public bool MidiRackThru(int trackId, int midiIndex)
    { ThrowIfDisposed(); return NativeMethods.MidiRackThru(_handle, trackId, midiIndex) != 0; }

    public float MidiRackMacroGet(int trackId, int midiIndex, int macro)
    { ThrowIfDisposed(); return NativeMethods.MidiRackMacroGet(_handle, trackId, midiIndex, macro); }

    public void MidiRackMacroSet(int trackId, int midiIndex, int macro, float v)
    { ThrowIfDisposed(); NativeMethods.MidiRackMacroSet(_handle, trackId, midiIndex, macro, v); }

    public string MidiRackMacroName(int trackId, int midiIndex, int macro)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.MidiRackMacroName(_handle, trackId, midiIndex, macro)) ?? ""; }

    public void MidiRackSetMacroName(int trackId, int midiIndex, int macro, string name)
    { ThrowIfDisposed(); NativeMethods.MidiRackSetMacroName(_handle, trackId, midiIndex, macro, name ?? ""); }

    public int MidiRackAddMacroMapping(int trackId, int midiIndex, int macro, int chain, int device, int param, float lo, float hi)
    { ThrowIfDisposed(); return NativeMethods.MidiRackAddMacroMapping(_handle, trackId, midiIndex, macro, chain, device, param, lo, hi); }

    public int MidiRackMappingCount(int trackId, int midiIndex)
    { ThrowIfDisposed(); return NativeMethods.MidiRackMappingCount(_handle, trackId, midiIndex); }

    public int MidiRackMappingInfo(int trackId, int midiIndex, int index, out int macro, out int chain, out int device, out int param, out float lo, out float hi, out int curve)
    { ThrowIfDisposed(); return NativeMethods.MidiRackMappingInfo(_handle, trackId, midiIndex, index, out macro, out chain, out device, out param, out lo, out hi, out curve); }

    public bool MidiRackRemoveMapping(int trackId, int midiIndex, int index)
    { ThrowIfDisposed(); return NativeMethods.MidiRackRemoveMapping(_handle, trackId, midiIndex, index) != 0; }

    public bool MidiRackSetMappingRange(int trackId, int midiIndex, int index, float lo, float hi)
    { ThrowIfDisposed(); return NativeMethods.MidiRackSetMappingRange(_handle, trackId, midiIndex, index, lo, hi) != 0; }

    public bool MidiRackSetMappingCurve(int trackId, int midiIndex, int index, int curve)
    { ThrowIfDisposed(); return NativeMethods.MidiRackSetMappingCurve(_handle, trackId, midiIndex, index, curve) != 0; }
}
