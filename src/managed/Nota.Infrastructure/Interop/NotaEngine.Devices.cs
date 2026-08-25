// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

public sealed partial class NotaEngine
{
    // --- Hosted plugins (M3-3) ---------------------------------------------

    /// <summary>Creates a new instrument track hosting catalog plugin <paramref name="catalogIndex"/>. Returns its id, or throws.</summary>
    public int AddPluginInstrumentTrack(int catalogIndex)
    {
        ThrowIfDisposed();
        int id = NativeMethods.AddPluginInstrumentTrack(_handle, catalogIndex);
        if (id <= 0) throw new NotaEngineException("Failed to load plugin instrument.");
        return id;
    }

    /// <summary>Replaces an instrument track's instrument with a hosted plugin.</summary>
    public void SetTrackInstrumentPlugin(int trackId, int catalogIndex)
    {
        ThrowIfDisposed();
        Check(NativeMethods.SetTrackInstrumentPlugin(_handle, trackId, catalogIndex));
    }

    /// <summary>Replaces an instrument track's instrument with a fresh built-in synth. Returns
    /// false for Racks / unknown kinds or non-instrument tracks (caller adds a new track instead).</summary>
    public bool SetTrackBuiltinInstrument(int trackId, int kind)
    {
        ThrowIfDisposed();
        return NativeMethods.SetTrackBuiltinInstrument(_handle, trackId, kind) == NativeMethods.NotaResult.Ok;
    }

    /// <summary>Copies an audio clip's played region down-mixed to mono (offline audio→MIDI
    /// analysis). Pass null to query the frame count; else fills up to <paramref name="outOrNull"/>.
    /// Returns the frame count. <paramref name="sr"/> receives the sample's source sample rate.</summary>
    public int GetClipAudioMono(int trackId, int clipIndex, float[]? outOrNull, out double sr)
    {
        ThrowIfDisposed();
        return NativeMethods.ClipAudioMono(_handle, trackId, clipIndex, outOrNull, outOrNull?.Length ?? 0, out sr);
    }

    /// <summary>Appends a hosted effect plugin to a track's device chain. Returns the device index, or -1.</summary>
    public int AddTrackEffectPlugin(int trackId, int catalogIndex)
    {
        ThrowIfDisposed();
        return NativeMethods.AddTrackEffectPlugin(_handle, trackId, catalogIndex);
    }

    /// <summary>Number of insert effects on a track's device chain.</summary>
    public int TrackDeviceCount(int trackId)
    {
        ThrowIfDisposed();
        return NativeMethods.TrackDeviceCount(_handle, trackId);
    }

    // --- Built-in devices + params (M4-4/5) --------------------------------

    /// <summary>Appends a built-in device (0=EQ, 1=Compressor, 2=Reverb, 3=Delay, 4=Utility, 5=Rack, 6=Amp). Returns device index, or -1.</summary>
    public int AddBuiltinDevice(int trackId, int kind)
    { ThrowIfDisposed(); return NativeMethods.AddBuiltinDevice(_handle, trackId, kind); }

    /// <summary>Reorders a device within the track chain.</summary>
    public void MoveDevice(int trackId, int fromIndex, int toIndex)
    { ThrowIfDisposed(); Check(NativeMethods.MoveDevice(_handle, trackId, fromIndex, toIndex)); }

    /// <summary>Removes a device from the track chain.</summary>
    public void RemoveDevice(int trackId, int deviceIndex)
    { ThrowIfDisposed(); Check(NativeMethods.RemoveDevice(_handle, trackId, deviceIndex)); }

    public string DeviceName(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.DeviceName(_handle, trackId, deviceIndex)) ?? ""; }

    public int DeviceParamCount(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceParamCount(_handle, trackId, deviceIndex); }

    public string DeviceParamName(int trackId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.DeviceParamName(_handle, trackId, deviceIndex, paramIndex)) ?? ""; }

    public float DeviceParamMin(int trackId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceParamMin(_handle, trackId, deviceIndex, paramIndex); }

    public float DeviceParamMax(int trackId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceParamMax(_handle, trackId, deviceIndex, paramIndex); }

    public float DeviceGetParam(int trackId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceGetParam(_handle, trackId, deviceIndex, paramIndex); }

    public void DeviceSetParam(int trackId, int deviceIndex, int paramIndex, float value)
    { ThrowIfDisposed(); Check(NativeMethods.DeviceSetParam(_handle, trackId, deviceIndex, paramIndex, value)); }

    public float DeviceGainReduction(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceGainReduction(_handle, trackId, deviceIndex); }

    public float DeviceParamDefault(int trackId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceParamDefault(_handle, trackId, deviceIndex, paramIndex); }
    public float InstrumentParamDefault(int trackId, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.InstrumentParamDefault(_handle, trackId, paramIndex); }
    public float MidiEffectParamDefault(int trackId, int index, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.MidiEffectParamDefault(_handle, trackId, index, paramIndex); }

    public int DeviceScope(int trackId, int deviceIndex, float[] outSamples, int maxSamples)
    { ThrowIfDisposed(); return NativeMethods.DeviceScope(_handle, trackId, deviceIndex, outSamples, maxSamples); }

    public void DeviceAction(int trackId, int deviceIndex, int id, int iarg, float farg)
    { ThrowIfDisposed(); NativeMethods.DeviceAction(_handle, trackId, deviceIndex, id, iarg, farg); }
    public int DeviceLayerWave(int trackId, int deviceIndex, int layer, float[] outSamples, int maxSamples)
    { ThrowIfDisposed(); return NativeMethods.DeviceLayerWave(_handle, trackId, deviceIndex, layer, outSamples, maxSamples); }
    public byte[] DeviceGetState(int trackId, int deviceIndex)
    {
        ThrowIfDisposed();
        int n = NativeMethods.DeviceGetState(_handle, trackId, deviceIndex, null, 0);
        if (n <= 0) return System.Array.Empty<byte>();
        var buf = new byte[n];
        NativeMethods.DeviceGetState(_handle, trackId, deviceIndex, buf, n);
        return buf;
    }
    public void DeviceSetState(int trackId, int deviceIndex, byte[] data)
    { ThrowIfDisposed(); if (data is { Length: > 0 }) NativeMethods.DeviceSetState(_handle, trackId, deviceIndex, data, data.Length); }

    // --- MIDI effects ---
    public int AddMidiEffect(int trackId, int kind) { ThrowIfDisposed(); return NativeMethods.AddMidiEffect(_handle, trackId, kind); }
    public int TrackMidiEffectCount(int trackId) { ThrowIfDisposed(); return NativeMethods.TrackMidiEffectCount(_handle, trackId); }
    public void MoveMidiEffect(int trackId, int fromIndex, int toIndex) { ThrowIfDisposed(); NativeMethods.MoveMidiEffect(_handle, trackId, fromIndex, toIndex); }
    public void RemoveMidiEffect(int trackId, int index) { ThrowIfDisposed(); NativeMethods.RemoveMidiEffect(_handle, trackId, index); }
    public int MidiEffectKind(int trackId, int index) { ThrowIfDisposed(); return NativeMethods.MidiEffectKind(_handle, trackId, index); }
    public int MidiEffectLastIn(int trackId, int index) { ThrowIfDisposed(); return NativeMethods.MidiEffectLastIn(_handle, trackId, index); }
    public int MidiEffectLastOut(int trackId, int index) { ThrowIfDisposed(); return NativeMethods.MidiEffectLastOut(_handle, trackId, index); }
    public int MidiEffectScope(int trackId, int index, float[] outv) { ThrowIfDisposed(); return NativeMethods.MidiEffectScope(_handle, trackId, index, outv, outv.Length); }
    public string MidiEffectName(int trackId, int index) { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.MidiEffectName(_handle, trackId, index)) ?? ""; }
    public int MidiEffectParamCount(int trackId, int index) { ThrowIfDisposed(); return NativeMethods.MidiEffectParamCount(_handle, trackId, index); }
    public string MidiEffectParamName(int trackId, int index, int paramIndex) { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.MidiEffectParamName(_handle, trackId, index, paramIndex)) ?? ""; }
    public float MidiEffectParamMin(int trackId, int index, int paramIndex) { ThrowIfDisposed(); return NativeMethods.MidiEffectParamMin(_handle, trackId, index, paramIndex); }
    public float MidiEffectParamMax(int trackId, int index, int paramIndex) { ThrowIfDisposed(); return NativeMethods.MidiEffectParamMax(_handle, trackId, index, paramIndex); }
    public float MidiEffectGetParam(int trackId, int index, int paramIndex) { ThrowIfDisposed(); return NativeMethods.MidiEffectGetParam(_handle, trackId, index, paramIndex); }
    public void MidiEffectSetParam(int trackId, int index, int paramIndex, float value) { ThrowIfDisposed(); NativeMethods.MidiEffectSetParam(_handle, trackId, index, paramIndex, value); }
    public void SetMidiEffectBypassed(int trackId, int index, bool bypassed) { ThrowIfDisposed(); NativeMethods.MidiEffectSetBypassed(_handle, trackId, index, bypassed ? 1 : 0); }
    public bool MidiEffectBypassed(int trackId, int index) { ThrowIfDisposed(); return NativeMethods.MidiEffectBypassed(_handle, trackId, index) != 0; }
    public void SetMidiEffectCcDest(int trackId, int index, int destDevice, int destParam) { ThrowIfDisposed(); NativeMethods.MidiEffectSetCcDest(_handle, trackId, index, destDevice, destParam); }
    public void SetMidiEffectCcDepth(int trackId, int index, float depth) { ThrowIfDisposed(); NativeMethods.MidiEffectSetCcDepth(_handle, trackId, index, depth); }
    public int MidiEffectCcDestDevice(int trackId, int index) { ThrowIfDisposed(); return NativeMethods.MidiEffectCcDestDevice(_handle, trackId, index); }
    public int MidiEffectCcDestParam(int trackId, int index) { ThrowIfDisposed(); return NativeMethods.MidiEffectCcDestParam(_handle, trackId, index); }
    public float MidiEffectCcDepth(int trackId, int index) { ThrowIfDisposed(); return NativeMethods.MidiEffectCcDepth(_handle, trackId, index); }

    public void SetDeviceSidechainSource(int trackId, int deviceIndex, int sourceTrackId)
    { ThrowIfDisposed(); NativeMethods.DeviceSetSidechainSource(_handle, trackId, deviceIndex, sourceTrackId); }

    public int DeviceSidechainSource(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceSidechainSource(_handle, trackId, deviceIndex); }

    public bool DeviceAcceptsSidechain(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceAcceptsSidechain(_handle, trackId, deviceIndex) != 0; }

    public void SetInstrumentSidechainSource(int trackId, int sourceTrackId)
    { ThrowIfDisposed(); NativeMethods.TrackSetInstrumentSidechainSource(_handle, trackId, sourceTrackId); }
    public int InstrumentSidechainSource(int trackId)
    { ThrowIfDisposed(); return NativeMethods.TrackInstrumentSidechainSource(_handle, trackId); }
    public bool InstrumentAcceptsSidechain(int trackId)
    { ThrowIfDisposed(); return NativeMethods.TrackInstrumentAcceptsSidechain(_handle, trackId) != 0; }

    public void SetDeviceSidechainGain(int trackId, int deviceIndex, float gainDb)
    { ThrowIfDisposed(); NativeMethods.DeviceSetSidechainGain(_handle, trackId, deviceIndex, gainDb); }
    public float DeviceSidechainGain(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceSidechainGain(_handle, trackId, deviceIndex); }

    public void SetDeviceSidechainMix(int trackId, int deviceIndex, float mix)
    { ThrowIfDisposed(); NativeMethods.DeviceSetSidechainMix(_handle, trackId, deviceIndex, mix); }
    public float DeviceSidechainMix(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceSidechainMix(_handle, trackId, deviceIndex); }

    public void SetDeviceSidechainTapPre(int trackId, int deviceIndex, bool pre)
    { ThrowIfDisposed(); NativeMethods.DeviceSetSidechainTapPre(_handle, trackId, deviceIndex, pre ? 1 : 0); }
    public bool DeviceSidechainTapPre(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.DeviceSidechainTapPre(_handle, trackId, deviceIndex) != 0; }

    /// <summary>Deterministic DSP self-test of the built-in EQ + Compressor. True if sane.</summary>
    public static bool FxSelfTest() => NativeMethods.FxSelfTest() != 0;

    /// <summary>Opens a hosted plugin's native GUI. deviceIndex &lt; 0 = the instrument.</summary>
    public void OpenPluginEditor(int trackId, int deviceIndex)
    {
        ThrowIfDisposed();
        Check(NativeMethods.PluginOpenEditor(_handle, trackId, deviceIndex));
    }

    /// <summary>Hides a hosted plugin's native GUI.</summary>
    public void ClosePluginEditor(int trackId, int deviceIndex)
    {
        ThrowIfDisposed();
        Check(NativeMethods.PluginCloseEditor(_handle, trackId, deviceIndex));
    }

    /// <summary>Reads a hosted plugin's opaque state blob (for project save). Empty for built-ins.</summary>
    public byte[] GetPluginState(int trackId, int deviceIndex)
    {
        ThrowIfDisposed();
        int size = NativeMethods.PluginGetState(_handle, trackId, deviceIndex, null, 0);
        if (size <= 0) return Array.Empty<byte>();
        var buf = new byte[size];
        NativeMethods.PluginGetState(_handle, trackId, deviceIndex, buf, size);
        return buf;
    }

    /// <summary>Restores a hosted plugin's state from a previously saved blob.</summary>
    public void SetPluginState(int trackId, int deviceIndex, byte[] data)
    {
        ThrowIfDisposed();
        Check(NativeMethods.PluginSetState(_handle, trackId, deviceIndex, data, data.Length));
    }

    /// <summary>Bypasses (or re-enables) the effect at deviceIndex on a track.</summary>
    public void SetDeviceBypassed(int trackId, int deviceIndex, bool bypassed)
    {
        ThrowIfDisposed();
        Check(NativeMethods.SetDeviceBypassed(_handle, trackId, deviceIndex, bypassed ? 1 : 0));
    }

    /// <summary>Whether the effect at deviceIndex is bypassed.</summary>
    public bool DeviceBypassed(int trackId, int deviceIndex)
    {
        ThrowIfDisposed();
        return NativeMethods.DeviceBypassed(_handle, trackId, deviceIndex) != 0;
    }

    /// <summary>Total reported latency (samples) of a track's plugin chain (M3-7).</summary>
    public int TrackLatencySamples(int trackId)
    {
        ThrowIfDisposed();
        return NativeMethods.TrackLatencySamples(_handle, trackId);
    }

    /// <summary>Deterministic self-test of the delay-compensation line. True if correct.</summary>
    public static bool PdcSelfTest() => NativeMethods.PdcSelfTest() != 0;

    /// <summary>Instrument identity: 0=Synth, 1=Sampler, -1=plugin/unknown, -2=no instrument.</summary>
    public int TrackInstrumentKind(int trackId) { ThrowIfDisposed(); return NativeMethods.TrackInstrumentKind(_handle, trackId); }

    /// <summary>Built-in device kind (0=EQ,1=Comp,2=Reverb,3=Delay,4=Utility) or -1 for a plugin.</summary>
    public int TrackDeviceBuiltinKind(int trackId, int deviceIndex) { ThrowIfDisposed(); return NativeMethods.TrackDeviceBuiltinKind(_handle, trackId, deviceIndex); }

    /// <summary>Stable identifier of a track's plugin instrument (empty if it isn't a plugin, M7-6c).</summary>
    public string TrackInstrumentPluginId(int trackId) { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.TrackInstrumentPluginId(_handle, trackId)) ?? ""; }

    /// <summary>Stable identifier of a track's plugin effect (empty if it isn't a plugin, M7-6c).</summary>
    public string TrackDevicePluginId(int trackId, int deviceIndex) { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.TrackDevicePluginId(_handle, trackId, deviceIndex)) ?? ""; }

    // --- hosted-plugin parameters (M9-B). deviceIndex < 0 = the instrument. ---
    public int PluginParamCount(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.PluginParamCount(_handle, trackId, deviceIndex); }
    public string PluginParamId(int trackId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.PluginParamId(_handle, trackId, deviceIndex, paramIndex)) ?? ""; }
    public string PluginParamName(int trackId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.PluginParamName(_handle, trackId, deviceIndex, paramIndex)) ?? ""; }
    public float PluginParamGet(int trackId, int deviceIndex, int paramIndex)
    { ThrowIfDisposed(); return NativeMethods.PluginParamGet(_handle, trackId, deviceIndex, paramIndex); }
    public void PluginParamSet(int trackId, int deviceIndex, int paramIndex, float normalized)
    { ThrowIfDisposed(); Check(NativeMethods.PluginParamSet(_handle, trackId, deviceIndex, paramIndex, normalized)); }
    public int AddPluginAutomationLane(int trackId, int deviceIndex, string paramId)
    { ThrowIfDisposed(); return NativeMethods.AddPluginAutomationLane(_handle, trackId, deviceIndex, paramId); }
    public string AutomationLaneParamId(int trackId, int laneIndex)
    { ThrowIfDisposed(); return Marshal.PtrToStringUTF8(NativeMethods.AutomationLaneParamId(_handle, trackId, laneIndex)) ?? ""; }
    public int PluginLastTouchedParam(int trackId, int deviceIndex)
    { ThrowIfDisposed(); return NativeMethods.PluginLastTouchedParam(_handle, trackId, deviceIndex); }
}
