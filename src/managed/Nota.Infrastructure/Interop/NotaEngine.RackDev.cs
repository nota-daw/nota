// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;
using Nota.Application;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- Audio Effect Rack (a RackDevice in a track's device chain) ---------
    // Same rack surface as the Instrument Rack, addressed by (trackId, deviceIndex).

    public bool RackDeviceSelfTest()
    { ThrowIfDisposed(); return NativeMethods.RackDeviceSelfTest(_handle) != 0; }

    public bool DrumRackSelfTest()
    { ThrowIfDisposed(); return NativeMethods.DrumRackSelfTest(_handle) != 0; }

    public int RackDevAddPluginChainDevice(int trackId, int deviceIndex, int chain, int catalogIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevAddPluginChainDevice(_handle, trackId, deviceIndex, chain, catalogIndex); }

    public int RackDevChainTriggerNote(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainTriggerNote(_handle, trackId, deviceIndex, chain); }

    public void RackDevSetChainTriggerNote(int trackId, int deviceIndex, int chain, int note)
    { ThrowIfDisposed(); NativeMethods.RackDevSetChainTriggerNote(_handle, trackId, deviceIndex, chain, note); }

    public int RackDevChainCount(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainCount(_handle, trackId, deviceIndex); }

    public int RackDevAddChain(int trackId, int deviceIndex, int instKind)
    { ThrowIfDisposed(); return NativeMethods.RackDevAddChain(_handle, trackId, deviceIndex, instKind); }

    public bool RackDevRemoveChain(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackDevRemoveChain(_handle, trackId, deviceIndex, chain) != 0; }

    public bool RackDevSetChainInstrument(int trackId, int deviceIndex, int chain, int instKind)
    { ThrowIfDisposed(); return NativeMethods.RackDevSetChainInstrument(_handle, trackId, deviceIndex, chain, instKind) != 0; }

    public int RackDevChainInstrumentKind(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainInstrumentKind(_handle, trackId, deviceIndex, chain); }

    public string RackDevChainInstrumentName(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackDevChainInstrumentName(_handle, trackId, deviceIndex, chain)) ?? ""; }

    public int RackDevChainInstrumentParamCount(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainInstrumentParamCount(_handle, trackId, deviceIndex, chain); }

    public string RackDevChainInstrumentParamName(int trackId, int deviceIndex, int chain, int param)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackDevChainInstrumentParamName(_handle, trackId, deviceIndex, chain, param)) ?? ""; }

    public float RackDevChainInstrumentParamGet(int trackId, int deviceIndex, int chain, int param)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainInstrumentParamGet(_handle, trackId, deviceIndex, chain, param); }

    public void RackDevChainInstrumentParamSet(int trackId, int deviceIndex, int chain, int param, float normalized)
    { ThrowIfDisposed(); NativeMethods.RackDevChainInstrumentParamSet(_handle, trackId, deviceIndex, chain, param, normalized); }

    public int RackDevChainDeviceCount(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainDeviceCount(_handle, trackId, deviceIndex, chain); }

    public int RackDevAddChainDevice(int trackId, int deviceIndex, int chain, int deviceKind)
    { ThrowIfDisposed(); return NativeMethods.RackDevAddChainDevice(_handle, trackId, deviceIndex, chain, deviceKind); }

    public bool RackDevRemoveChainDevice(int trackId, int deviceIndex, int chain, int dev)
    { ThrowIfDisposed(); return NativeMethods.RackDevRemoveChainDevice(_handle, trackId, deviceIndex, chain, dev) != 0; }

    public bool RackDevMoveChainDevice(int trackId, int deviceIndex, int chain, int fromIndex, int toIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevMoveChainDevice(_handle, trackId, deviceIndex, chain, fromIndex, toIndex) != 0; }

    public string RackDevChainDeviceName(int trackId, int deviceIndex, int chain, int dev)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackDevChainDeviceName(_handle, trackId, deviceIndex, chain, dev)) ?? ""; }

    public int RackDevChainDeviceBuiltinKind(int trackId, int deviceIndex, int chain, int dev)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainDeviceBuiltinKind(_handle, trackId, deviceIndex, chain, dev); }

    public void RackDevOpenChainDeviceEditor(int trackId, int deviceIndex, int chain, int dev)
    { ThrowIfDisposed(); NativeMethods.RackDevOpenChainDeviceEditor(_handle, trackId, deviceIndex, chain, dev); }

    public int RackDevChainDeviceParamCount(int trackId, int deviceIndex, int chain, int dev)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainDeviceParamCount(_handle, trackId, deviceIndex, chain, dev); }

    public string RackDevChainDeviceParamName(int trackId, int deviceIndex, int chain, int dev, int param)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackDevChainDeviceParamName(_handle, trackId, deviceIndex, chain, dev, param)) ?? ""; }

    public float RackDevChainDeviceParamMin(int trackId, int deviceIndex, int chain, int dev, int param)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainDeviceParamMin(_handle, trackId, deviceIndex, chain, dev, param); }

    public float RackDevChainDeviceParamMax(int trackId, int deviceIndex, int chain, int dev, int param)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainDeviceParamMax(_handle, trackId, deviceIndex, chain, dev, param); }

    public float RackDevChainDeviceParamGet(int trackId, int deviceIndex, int chain, int dev, int param)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainDeviceParamGet(_handle, trackId, deviceIndex, chain, dev, param); }

    public void RackDevChainDeviceParamSet(int trackId, int deviceIndex, int chain, int dev, int param, float value)
    { ThrowIfDisposed(); NativeMethods.RackDevChainDeviceParamSet(_handle, trackId, deviceIndex, chain, dev, param, value); }

    public void RackDevSetChainDeviceBypassed(int trackId, int deviceIndex, int chain, int dev, bool bypassed)
    { ThrowIfDisposed(); NativeMethods.RackDevSetChainDeviceBypassed(_handle, trackId, deviceIndex, chain, dev, bypassed ? 1 : 0); }

    public bool RackDevChainDeviceBypassed(int trackId, int deviceIndex, int chain, int dev)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainDeviceBypassed(_handle, trackId, deviceIndex, chain, dev) != 0; }

    public float RackDevChainDeviceGainReduction(int trackId, int deviceIndex, int chain, int dev)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainDeviceGainReduction(_handle, trackId, deviceIndex, chain, dev); }
    public int RackDevChainDeviceScope(int trackId, int deviceIndex, int chain, int dev, float[] outSamples, int maxSamples)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainDeviceScope(_handle, trackId, deviceIndex, chain, dev, outSamples, maxSamples); }
    public int RackDevChainDeviceLayerWave(int trackId, int deviceIndex, int chain, int dev, int layer, float[] outSamples, int maxSamples)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainDeviceLayerWave(_handle, trackId, deviceIndex, chain, dev, layer, outSamples, maxSamples); }
    public void RackDevChainDeviceAction(int trackId, int deviceIndex, int chain, int dev, int id, int iarg, float farg)
    { ThrowIfDisposed(); NativeMethods.RackDevChainDeviceAction(_handle, trackId, deviceIndex, chain, dev, id, iarg, farg); }
    public string RackDevChainDeviceText(int trackId, int deviceIndex, int chain, int dev, int id)
    {
        ThrowIfDisposed();
        int n = NativeMethods.RackDevChainDeviceText(_handle, trackId, deviceIndex, chain, dev, id, null, 0);
        if (n <= 0) return "";
        var buf = new byte[n + 1];
        NativeMethods.RackDevChainDeviceText(_handle, trackId, deviceIndex, chain, dev, id, buf, buf.Length);
        return System.Text.Encoding.UTF8.GetString(buf, 0, n);
    }

    public void RackDevSetChainGain(int trackId, int deviceIndex, int chain, float v)
    { ThrowIfDisposed(); NativeMethods.RackDevSetChainGain(_handle, trackId, deviceIndex, chain, v); }

    public void RackDevSetChainPan(int trackId, int deviceIndex, int chain, float v)
    { ThrowIfDisposed(); NativeMethods.RackDevSetChainPan(_handle, trackId, deviceIndex, chain, v); }

    public void RackDevSetChainMute(int trackId, int deviceIndex, int chain, bool mute)
    { ThrowIfDisposed(); NativeMethods.RackDevSetChainMute(_handle, trackId, deviceIndex, chain, mute ? 1 : 0); }

    public void RackDevSetChainSolo(int trackId, int deviceIndex, int chain, bool solo)
    { ThrowIfDisposed(); NativeMethods.RackDevSetChainSolo(_handle, trackId, deviceIndex, chain, solo ? 1 : 0); }

    public float RackDevChainGain(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainGain(_handle, trackId, deviceIndex, chain); }

    public float RackDevChainPan(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainPan(_handle, trackId, deviceIndex, chain); }

    public bool RackDevChainMute(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainMute(_handle, trackId, deviceIndex, chain) != 0; }

    public bool RackDevChainSolo(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainSolo(_handle, trackId, deviceIndex, chain) != 0; }

    public float RackDevMacroGet(int trackId, int deviceIndex, int macro)
    { ThrowIfDisposed(); return NativeMethods.RackDevMacroGet(_handle, trackId, deviceIndex, macro); }

    public void RackDevMacroSet(int trackId, int deviceIndex, int macro, float v)
    { ThrowIfDisposed(); NativeMethods.RackDevMacroSet(_handle, trackId, deviceIndex, macro, v); }

    public int RackDevAddMacroMapping(int trackId, int deviceIndex, int macro, int chain, int targetDevice, int paramIndex, float rangeMin, float rangeMax)
    { ThrowIfDisposed(); return NativeMethods.RackDevAddMacroMapping(_handle, trackId, deviceIndex, macro, chain, targetDevice, paramIndex, rangeMin, rangeMax); }

    public int RackDevMappingCount(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevMappingCount(_handle, trackId, deviceIndex); }

    public bool RackDevTryGetMapping(int trackId, int deviceIndex, int index, out RackMacroMapping mapping)
    {
        ThrowIfDisposed();
        int ok = NativeMethods.RackDevMappingInfo(_handle, trackId, deviceIndex, index,
            out int macro, out int chain, out int dev, out int param, out float lo, out float hi);
        mapping = new RackMacroMapping(macro, chain, dev, param, lo, hi);
        return ok != 0;
    }

    public bool RackDevRemoveMapping(int trackId, int deviceIndex, int index)
    { ThrowIfDisposed(); return NativeMethods.RackDevRemoveMapping(_handle, trackId, deviceIndex, index) != 0; }

    // ---- Audio Effect Rack: rack-out, routing, meter, zone, macro-name, mapping edit ----
    public float RackDevChainMeter(int trackId, int deviceIndex, int chain)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainMeter(_handle, trackId, deviceIndex, chain); }
    public void RackDevChainZone(int trackId, int deviceIndex, int chain, out int velLo, out int velHi)
    { ThrowIfDisposed(); NativeMethods.RackDevChainZone(_handle, trackId, deviceIndex, chain, out velLo, out velHi); }
    public void RackDevSetChainZone(int trackId, int deviceIndex, int chain, int velLo, int velHi)
    { ThrowIfDisposed(); NativeMethods.RackDevSetChainZone(_handle, trackId, deviceIndex, chain, velLo, velHi); }
    public string RackDevMacroName(int trackId, int deviceIndex, int macro)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.RackDevMacroName(_handle, trackId, deviceIndex, macro)) ?? ""; }
    public void RackDevSetMacroName(int trackId, int deviceIndex, int macro, string name)
    { ThrowIfDisposed(); NativeMethods.RackDevSetMacroName(_handle, trackId, deviceIndex, macro, name); }
    public bool RackDevSetMappingRange(int trackId, int deviceIndex, int index, float rangeMin, float rangeMax)
    { ThrowIfDisposed(); return NativeMethods.RackDevSetMappingRange(_handle, trackId, deviceIndex, index, rangeMin, rangeMax) != 0; }
    public int RackDevMappingCurve(int trackId, int deviceIndex, int index)
    { ThrowIfDisposed(); return NativeMethods.RackDevMappingCurve(_handle, trackId, deviceIndex, index); }
    public bool RackDevSetMappingCurve(int trackId, int deviceIndex, int index, int curve)
    { ThrowIfDisposed(); return NativeMethods.RackDevSetMappingCurve(_handle, trackId, deviceIndex, index, curve) != 0; }
    public float RackDevVolume(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevVolume(_handle, trackId, deviceIndex); }
    public void RackDevSetVolume(int trackId, int deviceIndex, float v)
    { ThrowIfDisposed(); NativeMethods.RackDevSetVolume(_handle, trackId, deviceIndex, v); }
    public int RackDevMode(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevMode(_handle, trackId, deviceIndex); }
    public void RackDevSetMode(int trackId, int deviceIndex, int mode)
    { ThrowIfDisposed(); NativeMethods.RackDevSetMode(_handle, trackId, deviceIndex, mode); }
    public float RackDevDryWet(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevDryWet(_handle, trackId, deviceIndex); }
    public void RackDevSetDryWet(int trackId, int deviceIndex, float v)
    { ThrowIfDisposed(); NativeMethods.RackDevSetDryWet(_handle, trackId, deviceIndex, v); }
    public bool RackDevPdc(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevPdc(_handle, trackId, deviceIndex) != 0; }
    public void RackDevSetPdc(int trackId, int deviceIndex, bool on)
    { ThrowIfDisposed(); NativeMethods.RackDevSetPdc(_handle, trackId, deviceIndex, on ? 1 : 0); }
    public float RackDevChainSelect(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevChainSelect(_handle, trackId, deviceIndex); }
    public void RackDevSetChainSelect(int trackId, int deviceIndex, float v)
    { ThrowIfDisposed(); NativeMethods.RackDevSetChainSelect(_handle, trackId, deviceIndex, v); }
    public bool RackDevSelFollow(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevSelFollow(_handle, trackId, deviceIndex) != 0; }
    public void RackDevSetSelFollow(int trackId, int deviceIndex, bool on)
    { ThrowIfDisposed(); NativeMethods.RackDevSetSelFollow(_handle, trackId, deviceIndex, on ? 1 : 0); }
    public float RackDevLiveSelector(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.RackDevLiveSelector(_handle, trackId, deviceIndex); }
}
