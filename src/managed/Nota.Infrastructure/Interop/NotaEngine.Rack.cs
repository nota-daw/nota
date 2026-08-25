// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System.Runtime.InteropServices;
using Nota.Application;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- Instrument Rack ----------------------------------------------------
    // Parallel chains (instrument + insert devices), per-chain controls and 8
    // macros with parameter mappings. Addressed by (trackId, chain, dev, param).

    public int AddInstrumentRackTrack()
    {
        ThrowIfDisposed();
        int id = NativeMethods.AddInstrumentRackTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to create an Instrument Rack track.");
        return id;
    }

    public int AddDrumRackTrack()
    {
        ThrowIfDisposed();
        int id = NativeMethods.AddDrumRackTrack(_handle);
        if (id <= 0) throw new NotaEngineException("Failed to create a Drum Rack track.");
        return id;
    }

    public int RackAddSamplerChain(int trackId, string path, int rootNote, bool loop)
    { ThrowIfDisposed(); return NativeMethods.RackAddSamplerChain(_handle, trackId, path, rootNote, loop ? 1 : 0); }

    public bool RackSetChainSamplerSample(int trackId, int chain, string path, int rootNote)
    { ThrowIfDisposed(); return NativeMethods.RackSetChainSamplerSample(_handle, trackId, chain, path, rootNote) != 0; }

    public void RackOpenChainInstrumentEditor(int trackId, int chain)
    { ThrowIfDisposed(); NativeMethods.RackOpenChainInstrumentEditor(_handle, trackId, chain); }

    public void RackOpenChainDeviceEditor(int trackId, int chain, int dev)
    { ThrowIfDisposed(); NativeMethods.RackOpenChainDeviceEditor(_handle, trackId, chain, dev); }

    public string RackChainInstrumentPluginId(int trackId, int chain)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackChainInstrumentPluginId(_handle, trackId, chain)) ?? ""; }

    public byte[] RackGetChainInstrumentState(int trackId, int chain)
    {
        ThrowIfDisposed();
        int size = NativeMethods.RackChainInstrumentGetState(_handle, trackId, chain, null, 0);
        if (size <= 0) return System.Array.Empty<byte>();
        var buf = new byte[size];
        NativeMethods.RackChainInstrumentGetState(_handle, trackId, chain, buf, size);
        return buf;
    }

    public int RackAddPluginInstrumentChain(int trackId, int catalogIndex)
    { ThrowIfDisposed(); return NativeMethods.RackAddPluginInstrumentChain(_handle, trackId, catalogIndex); }

    public int RackAddPluginChainDevice(int trackId, int chain, int catalogIndex)
    { ThrowIfDisposed(); return NativeMethods.RackAddPluginChainDevice(_handle, trackId, chain, catalogIndex); }

    public int RackChainTriggerNote(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainTriggerNote(_handle, trackId, chain); }

    public void RackSetChainTriggerNote(int trackId, int chain, int note)
    { ThrowIfDisposed(); NativeMethods.RackSetChainTriggerNote(_handle, trackId, chain, note); }

    public int RackChainCount(int trackId)
    { ThrowIfDisposed(); return NativeMethods.RackChainCount(_handle, trackId); }

    public int RackAddChain(int trackId, int instKind)
    { ThrowIfDisposed(); return NativeMethods.RackAddChain(_handle, trackId, instKind); }

    public bool RackRemoveChain(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackRemoveChain(_handle, trackId, chain) != 0; }

    public bool RackSetChainInstrument(int trackId, int chain, int instKind)
    { ThrowIfDisposed(); return NativeMethods.RackSetChainInstrument(_handle, trackId, chain, instKind) != 0; }

    public int RackChainInstrumentKind(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainInstrumentKind(_handle, trackId, chain); }

    public string RackChainInstrumentName(int trackId, int chain)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackChainInstrumentName(_handle, trackId, chain)) ?? ""; }

    public int RackChainInstrumentParamCount(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainInstrumentParamCount(_handle, trackId, chain); }

    public string RackChainInstrumentParamName(int trackId, int chain, int param)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackChainInstrumentParamName(_handle, trackId, chain, param)) ?? ""; }

    public string RackChainInstrumentParamId(int trackId, int chain, int param)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackChainInstrumentParamId(_handle, trackId, chain, param)) ?? ""; }

    public float RackChainInstrumentParamGet(int trackId, int chain, int param)
    { ThrowIfDisposed(); return NativeMethods.RackChainInstrumentParamGet(_handle, trackId, chain, param); }

    public void RackChainInstrumentParamSet(int trackId, int chain, int param, float normalized)
    { ThrowIfDisposed(); NativeMethods.RackChainInstrumentParamSet(_handle, trackId, chain, param, normalized); }

    public float RackChainInstrumentParamDefault(int trackId, int chain, int param)
    { ThrowIfDisposed(); return NativeMethods.RackChainInstrumentParamDefault(_handle, trackId, chain, param); }

    public bool RackChainSamplerInfo(int trackId, int chain, out NotaSamplerInfo info)
    { ThrowIfDisposed(); return NativeMethods.RackChainSamplerInfo(_handle, trackId, chain, out info) != 0; }

    public bool RackSetChainSamplerRoot(int trackId, int chain, int rootNote)
    { ThrowIfDisposed(); return NativeMethods.RackSetChainSamplerRoot(_handle, trackId, chain, rootNote) != 0; }

    public float RackChainSamplerPlayPosition(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainSamplerPlayPosition(_handle, trackId, chain); }

    public int RackChainDeviceCount(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainDeviceCount(_handle, trackId, chain); }

    public int RackAddChainDevice(int trackId, int chain, int deviceKind)
    { ThrowIfDisposed(); return NativeMethods.RackAddChainDevice(_handle, trackId, chain, deviceKind); }

    public bool RackRemoveChainDevice(int trackId, int chain, int dev)
    { ThrowIfDisposed(); return NativeMethods.RackRemoveChainDevice(_handle, trackId, chain, dev) != 0; }

    public bool RackMoveChainDevice(int trackId, int chain, int fromIndex, int toIndex)
    { ThrowIfDisposed(); return NativeMethods.RackMoveChainDevice(_handle, trackId, chain, fromIndex, toIndex) != 0; }

    public string RackChainDeviceName(int trackId, int chain, int dev)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackChainDeviceName(_handle, trackId, chain, dev)) ?? ""; }

    public int RackChainDeviceBuiltinKind(int trackId, int chain, int dev)
    { ThrowIfDisposed(); return NativeMethods.RackChainDeviceBuiltinKind(_handle, trackId, chain, dev); }

    public int RackChainDeviceParamCount(int trackId, int chain, int dev)
    { ThrowIfDisposed(); return NativeMethods.RackChainDeviceParamCount(_handle, trackId, chain, dev); }

    public string RackChainDeviceParamName(int trackId, int chain, int dev, int param)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackChainDeviceParamName(_handle, trackId, chain, dev, param)) ?? ""; }

    public float RackChainDeviceParamMin(int trackId, int chain, int dev, int param)
    { ThrowIfDisposed(); return NativeMethods.RackChainDeviceParamMin(_handle, trackId, chain, dev, param); }

    public float RackChainDeviceParamMax(int trackId, int chain, int dev, int param)
    { ThrowIfDisposed(); return NativeMethods.RackChainDeviceParamMax(_handle, trackId, chain, dev, param); }

    public float RackChainDeviceParamGet(int trackId, int chain, int dev, int param)
    { ThrowIfDisposed(); return NativeMethods.RackChainDeviceParamGet(_handle, trackId, chain, dev, param); }

    public void RackChainDeviceParamSet(int trackId, int chain, int dev, int param, float value)
    { ThrowIfDisposed(); NativeMethods.RackChainDeviceParamSet(_handle, trackId, chain, dev, param, value); }

    public void RackSetChainDeviceBypassed(int trackId, int chain, int dev, bool bypassed)
    { ThrowIfDisposed(); NativeMethods.RackSetChainDeviceBypassed(_handle, trackId, chain, dev, bypassed ? 1 : 0); }

    public bool RackChainDeviceBypassed(int trackId, int chain, int dev)
    { ThrowIfDisposed(); return NativeMethods.RackChainDeviceBypassed(_handle, trackId, chain, dev) != 0; }

    public void RackSetChainGain(int trackId, int chain, float v)
    { ThrowIfDisposed(); NativeMethods.RackSetChainGain(_handle, trackId, chain, v); }

    public void RackSetChainPan(int trackId, int chain, float v)
    { ThrowIfDisposed(); NativeMethods.RackSetChainPan(_handle, trackId, chain, v); }

    public void RackSetChainMute(int trackId, int chain, bool mute)
    { ThrowIfDisposed(); NativeMethods.RackSetChainMute(_handle, trackId, chain, mute ? 1 : 0); }

    public void RackSetChainSolo(int trackId, int chain, bool solo)
    { ThrowIfDisposed(); NativeMethods.RackSetChainSolo(_handle, trackId, chain, solo ? 1 : 0); }

    public float RackChainGain(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainGain(_handle, trackId, chain); }

    public float RackChainPan(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainPan(_handle, trackId, chain); }

    public bool RackChainMute(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainMute(_handle, trackId, chain) != 0; }

    public bool RackChainSolo(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainSolo(_handle, trackId, chain) != 0; }

    public float RackMacroGet(int trackId, int macro)
    { ThrowIfDisposed(); return NativeMethods.RackMacroGet(_handle, trackId, macro); }

    public void RackMacroSet(int trackId, int macro, float v)
    { ThrowIfDisposed(); NativeMethods.RackMacroSet(_handle, trackId, macro, v); }

    public int RackAddMacroMapping(int trackId, int macro, int chain, int deviceIndex, int paramIndex, float rangeMin, float rangeMax)
    { ThrowIfDisposed(); return NativeMethods.RackAddMacroMapping(_handle, trackId, macro, chain, deviceIndex, paramIndex, rangeMin, rangeMax); }

    public int RackMappingCount(int trackId)
    { ThrowIfDisposed(); return NativeMethods.RackMappingCount(_handle, trackId); }

    public bool RackTryGetMapping(int trackId, int index, out RackMacroMapping mapping)
    {
        ThrowIfDisposed();
        int ok = NativeMethods.RackMappingInfo(_handle, trackId, index,
            out int macro, out int chain, out int dev, out int param, out float lo, out float hi);
        mapping = new RackMacroMapping(macro, chain, dev, param, lo, hi);
        return ok != 0;
    }

    public bool RackRemoveMapping(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.RackRemoveMapping(_handle, trackId, index) != 0; }

    public void RackChainZone(int trackId, int chain, out int keyLo, out int keyHi, out int velLo, out int velHi)
    { ThrowIfDisposed(); NativeMethods.RackChainZone(_handle, trackId, chain, out keyLo, out keyHi, out velLo, out velHi); }

    public void RackSetChainZone(int trackId, int chain, int keyLo, int keyHi, int velLo, int velHi)
    { ThrowIfDisposed(); NativeMethods.RackSetChainZone(_handle, trackId, chain, keyLo, keyHi, velLo, velHi); }

    public float RackChainMeter(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainMeter(_handle, trackId, chain); }

    public string RackMacroName(int trackId, int macro)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackMacroName(_handle, trackId, macro)) ?? ""; }

    public void RackSetMacroName(int trackId, int macro, string name)
    { ThrowIfDisposed(); NativeMethods.RackSetMacroName(_handle, trackId, macro, name ?? ""); }

    public float RackVolume(int trackId)
    { ThrowIfDisposed(); return NativeMethods.RackVolume(_handle, trackId); }

    public void RackSetVolume(int trackId, float v)
    { ThrowIfDisposed(); NativeMethods.RackSetVolume(_handle, trackId, v); }

    public float RackGlide(int trackId)
    { ThrowIfDisposed(); return NativeMethods.RackGlide(_handle, trackId); }

    public void RackSetGlide(int trackId, float v)
    { ThrowIfDisposed(); NativeMethods.RackSetGlide(_handle, trackId, v); }

    public bool RackSetMappingRange(int trackId, int index, float rangeMin, float rangeMax)
    { ThrowIfDisposed(); return NativeMethods.RackSetMappingRange(_handle, trackId, index, rangeMin, rangeMax) != 0; }

    public int RackMappingCurve(int trackId, int index)
    { ThrowIfDisposed(); return NativeMethods.RackMappingCurve(_handle, trackId, index); }

    public bool RackSetMappingCurve(int trackId, int index, int curve)
    { ThrowIfDisposed(); return NativeMethods.RackSetMappingCurve(_handle, trackId, index, curve) != 0; }

    // ---- Drum Rack per-pad shaping + kit-level swing/humanize ----
    public void RackSetChainChoke(int trackId, int chain, int group)
    { ThrowIfDisposed(); NativeMethods.RackSetChainChoke(_handle, trackId, chain, group); }
    public int RackChainChoke(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainChoke(_handle, trackId, chain); }
    public void RackSetChainTune(int trackId, int chain, int semitones)
    { ThrowIfDisposed(); NativeMethods.RackSetChainTune(_handle, trackId, chain, semitones); }
    public int RackChainTune(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainTune(_handle, trackId, chain); }
    public void RackSetChainDecay(int trackId, int chain, float v)
    { ThrowIfDisposed(); NativeMethods.RackSetChainDecay(_handle, trackId, chain, v); }
    public float RackChainDecay(int trackId, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackChainDecay(_handle, trackId, chain); }
    public void RackSetSwing(int trackId, float v)
    { ThrowIfDisposed(); NativeMethods.RackSetSwing(_handle, trackId, v); }
    public float RackSwing(int trackId)
    { ThrowIfDisposed(); return NativeMethods.RackSwing(_handle, trackId); }
    public void RackSetHumanize(int trackId, float v)
    { ThrowIfDisposed(); NativeMethods.RackSetHumanize(_handle, trackId, v); }
    public float RackHumanize(int trackId)
    { ThrowIfDisposed(); return NativeMethods.RackHumanize(_handle, trackId); }
}
