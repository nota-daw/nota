// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — a uniform view over a rack (Instrument Rack via Rack*, Audio
// Effect Rack via RackDev*), so the one card renders both. HasInstrumentChains hides
// the per-chain instrument selector for effect racks; AutomationDeviceIndex is the
// plugin-param device index used when driving/recording the macros.

using Nota.Application;

namespace Nota.App;

internal interface IRackAccess
{
    bool HasInstrumentChains { get; }
    int AutomationDeviceIndex { get; }
    int ChainCount();
    int AddChain(int instKind);
    bool RemoveChain(int chain);
    bool SetChainInstrument(int chain, int instKind);
    int ChainInstrumentKind(int chain);
    string ChainInstrumentName(int chain);
    int ChainInstrumentParamCount(int chain);
    string ChainInstrumentParamName(int chain, int param);
    float ChainInstrumentParamGet(int chain, int param);
    void ChainInstrumentParamSet(int chain, int param, float v);
    int ChainDeviceCount(int chain);
    int AddChainDevice(int chain, int kind);
    int AddPluginInstrumentChain(int catalogIndex);   // hosted plugin instrument as a new chain (-1 for effect racks)
    int AddPluginChainDevice(int chain, int catalogIndex);   // hosted plugin effect into a chain
    bool RemoveChainDevice(int chain, int dev);
    void MoveChainDevice(int chain, int from, int to);
    string ChainDeviceName(int chain, int dev);
    int ChainDeviceParamCount(int chain, int dev);
    string ChainDeviceParamName(int chain, int dev, int p);
    float ChainDeviceParamMin(int chain, int dev, int p);
    float ChainDeviceParamMax(int chain, int dev, int p);
    float ChainDeviceParamGet(int chain, int dev, int p);
    void ChainDeviceParamSet(int chain, int dev, int p, float v);
    bool ChainDeviceBypassed(int chain, int dev);
    void SetChainDeviceBypassed(int chain, int dev, bool b);
    int ChainDeviceBuiltinKind(int chain, int dev);   // -1 = hosted plug-in
    void OpenChainDeviceEditor(int chain, int dev);   // hosted-plugin effect native GUI
    float ChainGain(int chain); void SetChainGain(int chain, float v);
    float ChainPan(int chain); void SetChainPan(int chain, float v);
    bool ChainMute(int chain); void SetChainMute(int chain, bool b);
    bool ChainSolo(int chain); void SetChainSolo(int chain, bool b);
    int ChainTriggerNote(int chain); void SetChainTriggerNote(int chain, int note);
    int AddMacroMapping(int macro, int chain, int targetDevice, int param, float lo, float hi);
    int MappingCount();
    bool TryGetMapping(int index, out RackMacroMapping m);
    bool RemoveMapping(int index);
    // Shared macro-name + meter + mapping-edit surface (di-agnostic), so the macro strip
    // and macro-map view render for both rack kinds.
    string MacroName(int i); void SetMacroName(int i, string name);
    float ChainMeter(int chain);
    bool SetMappingRange(int index, float lo, float hi);
    int MappingCurve(int index); bool SetMappingCurve(int index, int curve);
    /// <summary>Whether a chain can take a hosted plug-in effect (not Nota Rhythm's voices).</summary>
    bool HostsPlugins => true;
    // Live telemetry of a chain device (meters, scopes, curves) for its pop-out card; none by default.
    float ChainDeviceGainReduction(int chain, int dev) => 0f;
    int ChainDeviceScope(int chain, int dev, float[] outSamples, int maxSamples) => 0;
    int ChainDeviceLayerWave(int chain, int dev, int layer, float[] outSamples, int maxSamples) => 0;
    string ChainDeviceText(int chain, int dev, int id) => "";
    void ChainDeviceAction(int chain, int dev, int id, int iarg, float farg) { }
}

internal sealed class InstrumentRackAccess(IAudioEngine e, int t) : IRackAccess
{
    public bool HasInstrumentChains => true;
    public int AutomationDeviceIndex => -1;
    public int ChainCount() => e.RackChainCount(t);
    public int AddChain(int k) => e.RackAddChain(t, k);
    public bool RemoveChain(int c) => e.RackRemoveChain(t, c);
    public bool SetChainInstrument(int c, int k) => e.RackSetChainInstrument(t, c, k);
    public int ChainInstrumentKind(int c) => e.RackChainInstrumentKind(t, c);
    public string ChainInstrumentName(int c) => e.RackChainInstrumentName(t, c);
    public int ChainInstrumentParamCount(int c) => e.RackChainInstrumentParamCount(t, c);
    public string ChainInstrumentParamName(int c, int p) => e.RackChainInstrumentParamName(t, c, p);
    public float ChainInstrumentParamGet(int c, int p) => e.RackChainInstrumentParamGet(t, c, p);
    public void ChainInstrumentParamSet(int c, int p, float v) => e.RackChainInstrumentParamSet(t, c, p, v);
    public int ChainDeviceCount(int c) => e.RackChainDeviceCount(t, c);
    public int AddChainDevice(int c, int k) => e.RackAddChainDevice(t, c, k);
    public int AddPluginInstrumentChain(int catalogIndex) => e.RackAddPluginInstrumentChain(t, catalogIndex);
    public int AddPluginChainDevice(int c, int catalogIndex) => e.RackAddPluginChainDevice(t, c, catalogIndex);
    public bool RemoveChainDevice(int c, int d) => e.RackRemoveChainDevice(t, c, d);
    public void MoveChainDevice(int c, int f, int to) => e.RackMoveChainDevice(t, c, f, to);
    public string ChainDeviceName(int c, int d) => e.RackChainDeviceName(t, c, d);
    public int ChainDeviceParamCount(int c, int d) => e.RackChainDeviceParamCount(t, c, d);
    public string ChainDeviceParamName(int c, int d, int p) => e.RackChainDeviceParamName(t, c, d, p);
    public float ChainDeviceParamMin(int c, int d, int p) => e.RackChainDeviceParamMin(t, c, d, p);
    public float ChainDeviceParamMax(int c, int d, int p) => e.RackChainDeviceParamMax(t, c, d, p);
    public float ChainDeviceParamGet(int c, int d, int p) => e.RackChainDeviceParamGet(t, c, d, p);
    public void ChainDeviceParamSet(int c, int d, int p, float v) => e.RackChainDeviceParamSet(t, c, d, p, v);
    public bool ChainDeviceBypassed(int c, int d) => e.RackChainDeviceBypassed(t, c, d);
    public void SetChainDeviceBypassed(int c, int d, bool b) => e.RackSetChainDeviceBypassed(t, c, d, b);
    public int ChainDeviceBuiltinKind(int c, int d) => e.RackChainDeviceBuiltinKind(t, c, d);
    public void OpenChainDeviceEditor(int c, int d) => e.RackOpenChainDeviceEditor(t, c, d);
    public float ChainDeviceGainReduction(int c, int d) => e.RackDevChainDeviceGainReduction(t, -1, c, d);
    public int ChainDeviceScope(int c, int d, float[] o, int n) => e.RackDevChainDeviceScope(t, -1, c, d, o, n);
    public int ChainDeviceLayerWave(int c, int d, int l, float[] o, int n) => e.RackDevChainDeviceLayerWave(t, -1, c, d, l, o, n);
    public string ChainDeviceText(int c, int d, int id) => e.RackDevChainDeviceText(t, -1, c, d, id);
    public void ChainDeviceAction(int c, int d, int id, int iarg, float farg) => e.RackDevChainDeviceAction(t, -1, c, d, id, iarg, farg);
    public float ChainGain(int c) => e.RackChainGain(t, c);
    public void SetChainGain(int c, float v) => e.RackSetChainGain(t, c, v);
    public float ChainPan(int c) => e.RackChainPan(t, c);
    public void SetChainPan(int c, float v) => e.RackSetChainPan(t, c, v);
    public bool ChainMute(int c) => e.RackChainMute(t, c);
    public void SetChainMute(int c, bool b) => e.RackSetChainMute(t, c, b);
    public bool ChainSolo(int c) => e.RackChainSolo(t, c);
    public void SetChainSolo(int c, bool b) => e.RackSetChainSolo(t, c, b);
    public int ChainTriggerNote(int c) => e.RackChainTriggerNote(t, c);
    public void SetChainTriggerNote(int c, int note) => e.RackSetChainTriggerNote(t, c, note);
    public int AddMacroMapping(int m, int c, int td, int p, float lo, float hi) => e.RackAddMacroMapping(t, m, c, td, p, lo, hi);
    public int MappingCount() => e.RackMappingCount(t);
    public bool TryGetMapping(int i, out RackMacroMapping m) => e.RackTryGetMapping(t, i, out m);
    public bool RemoveMapping(int i) => e.RackRemoveMapping(t, i);
    public string MacroName(int i) => e.RackMacroName(t, i);
    public void SetMacroName(int i, string name) => e.RackSetMacroName(t, i, name);
    public float ChainMeter(int c) => e.RackChainMeter(t, c);
    public bool SetMappingRange(int i, float lo, float hi) => e.RackSetMappingRange(t, i, lo, hi);
    public int MappingCurve(int i) => e.RackMappingCurve(t, i);
    public bool SetMappingCurve(int i, int curve) => e.RackSetMappingCurve(t, i, curve);
}

internal sealed class EffectRackAccess(IAudioEngine e, int t, int di) : IRackAccess
{
    public bool HasInstrumentChains => false;
    public int AutomationDeviceIndex => di;
    public int ChainCount() => e.RackDevChainCount(t, di);
    public int AddChain(int k) => e.RackDevAddChain(t, di, k);
    public bool RemoveChain(int c) => e.RackDevRemoveChain(t, di, c);
    public bool SetChainInstrument(int c, int k) => e.RackDevSetChainInstrument(t, di, c, k);
    public int ChainInstrumentKind(int c) => e.RackDevChainInstrumentKind(t, di, c);
    public string ChainInstrumentName(int c) => e.RackDevChainInstrumentName(t, di, c);
    public int ChainInstrumentParamCount(int c) => e.RackDevChainInstrumentParamCount(t, di, c);
    public string ChainInstrumentParamName(int c, int p) => e.RackDevChainInstrumentParamName(t, di, c, p);
    public float ChainInstrumentParamGet(int c, int p) => e.RackDevChainInstrumentParamGet(t, di, c, p);
    public void ChainInstrumentParamSet(int c, int p, float v) => e.RackDevChainInstrumentParamSet(t, di, c, p, v);
    public int ChainDeviceCount(int c) => e.RackDevChainDeviceCount(t, di, c);
    public int AddChainDevice(int c, int k) => e.RackDevAddChainDevice(t, di, c, k);
    public int AddPluginInstrumentChain(int catalogIndex) => -1;   // effect-rack chains hold no instrument
    public int AddPluginChainDevice(int c, int catalogIndex) => e.RackDevAddPluginChainDevice(t, di, c, catalogIndex);
    public bool RemoveChainDevice(int c, int d) => e.RackDevRemoveChainDevice(t, di, c, d);
    public void MoveChainDevice(int c, int f, int to) => e.RackDevMoveChainDevice(t, di, c, f, to);
    public string ChainDeviceName(int c, int d) => e.RackDevChainDeviceName(t, di, c, d);
    public int ChainDeviceParamCount(int c, int d) => e.RackDevChainDeviceParamCount(t, di, c, d);
    public string ChainDeviceParamName(int c, int d, int p) => e.RackDevChainDeviceParamName(t, di, c, d, p);
    public float ChainDeviceParamMin(int c, int d, int p) => e.RackDevChainDeviceParamMin(t, di, c, d, p);
    public float ChainDeviceParamMax(int c, int d, int p) => e.RackDevChainDeviceParamMax(t, di, c, d, p);
    public float ChainDeviceParamGet(int c, int d, int p) => e.RackDevChainDeviceParamGet(t, di, c, d, p);
    public void ChainDeviceParamSet(int c, int d, int p, float v) => e.RackDevChainDeviceParamSet(t, di, c, d, p, v);
    public bool ChainDeviceBypassed(int c, int d) => e.RackDevChainDeviceBypassed(t, di, c, d);
    public void SetChainDeviceBypassed(int c, int d, bool b) => e.RackDevSetChainDeviceBypassed(t, di, c, d, b);
    public int ChainDeviceBuiltinKind(int c, int d) => e.RackDevChainDeviceBuiltinKind(t, di, c, d);
    public void OpenChainDeviceEditor(int c, int d) => e.RackDevOpenChainDeviceEditor(t, di, c, d);
    public float ChainDeviceGainReduction(int c, int d) => e.RackDevChainDeviceGainReduction(t, di, c, d);
    public int ChainDeviceScope(int c, int d, float[] o, int n) => e.RackDevChainDeviceScope(t, di, c, d, o, n);
    public int ChainDeviceLayerWave(int c, int d, int l, float[] o, int n) => e.RackDevChainDeviceLayerWave(t, di, c, d, l, o, n);
    public string ChainDeviceText(int c, int d, int id) => e.RackDevChainDeviceText(t, di, c, d, id);
    public void ChainDeviceAction(int c, int d, int id, int iarg, float farg) => e.RackDevChainDeviceAction(t, di, c, d, id, iarg, farg);
    public float ChainGain(int c) => e.RackDevChainGain(t, di, c);
    public void SetChainGain(int c, float v) => e.RackDevSetChainGain(t, di, c, v);
    public float ChainPan(int c) => e.RackDevChainPan(t, di, c);
    public void SetChainPan(int c, float v) => e.RackDevSetChainPan(t, di, c, v);
    public bool ChainMute(int c) => e.RackDevChainMute(t, di, c);
    public void SetChainMute(int c, bool b) => e.RackDevSetChainMute(t, di, c, b);
    public bool ChainSolo(int c) => e.RackDevChainSolo(t, di, c);
    public void SetChainSolo(int c, bool b) => e.RackDevSetChainSolo(t, di, c, b);
    public int ChainTriggerNote(int c) => e.RackDevChainTriggerNote(t, di, c);
    public void SetChainTriggerNote(int c, int note) => e.RackDevSetChainTriggerNote(t, di, c, note);
    public int AddMacroMapping(int m, int c, int td, int p, float lo, float hi) => e.RackDevAddMacroMapping(t, di, m, c, td, p, lo, hi);
    public int MappingCount() => e.RackDevMappingCount(t, di);
    public bool TryGetMapping(int i, out RackMacroMapping m) => e.RackDevTryGetMapping(t, di, i, out m);
    public bool RemoveMapping(int i) => e.RackDevRemoveMapping(t, di, i);
    public string MacroName(int i) => e.RackDevMacroName(t, di, i);
    public void SetMacroName(int i, string name) => e.RackDevSetMacroName(t, di, i, name);
    public float ChainMeter(int c) => e.RackDevChainMeter(t, di, c);
    public bool SetMappingRange(int i, float lo, float hi) => e.RackDevSetMappingRange(t, di, i, lo, hi);
    public int MappingCurve(int i) => e.RackDevMappingCurve(t, di, i);
    public bool SetMappingCurve(int i, int curve) => e.RackDevSetMappingCurve(t, di, i, curve);
}

// Nota Rhythm's eight voices as effect-only "chains": each voice's insert chain through the
// same surface a rack chain has, so the rack's device slots, add menu and full device cards
// work on a voice unchanged. The instrument / mix / macro members have no Rhythm meaning.
internal sealed class RhythmVoiceAccess(IAudioEngine e, int t) : IRackAccess
{
    public bool HasInstrumentChains => false;
    public int AutomationDeviceIndex => -1;
    public bool HostsPlugins => false;
    public int ChainCount() => RhythmModel.Voices;
    public int AddChain(int k) => -1;
    public bool RemoveChain(int c) => false;
    public bool SetChainInstrument(int c, int k) => false;
    public int ChainInstrumentKind(int c) => -2;
    public string ChainInstrumentName(int c) => "";
    public int ChainInstrumentParamCount(int c) => 0;
    public string ChainInstrumentParamName(int c, int p) => "";
    public float ChainInstrumentParamGet(int c, int p) => 0f;
    public void ChainInstrumentParamSet(int c, int p, float v) { }
    public int ChainDeviceCount(int c) => e.RhythmVoiceDeviceCount(t, c);
    public int AddChainDevice(int c, int k) => e.RhythmAddVoiceDevice(t, c, k);
    public int AddPluginInstrumentChain(int catalogIndex) => -1;
    public int AddPluginChainDevice(int c, int catalogIndex) => -1;
    public bool RemoveChainDevice(int c, int d) => e.RhythmRemoveVoiceDevice(t, c, d);
    public void MoveChainDevice(int c, int f, int to) => e.RhythmMoveVoiceDevice(t, c, f, to);
    public string ChainDeviceName(int c, int d) => e.RhythmVoiceDeviceName(t, c, d);
    public int ChainDeviceParamCount(int c, int d) => e.RhythmVoiceDeviceParamCount(t, c, d);
    public string ChainDeviceParamName(int c, int d, int p) => e.RhythmVoiceDeviceParamName(t, c, d, p);
    public float ChainDeviceParamMin(int c, int d, int p) => e.RhythmVoiceDeviceParamMin(t, c, d, p);
    public float ChainDeviceParamMax(int c, int d, int p) => e.RhythmVoiceDeviceParamMax(t, c, d, p);
    public float ChainDeviceParamGet(int c, int d, int p) => e.RhythmVoiceDeviceParamGet(t, c, d, p);
    public void ChainDeviceParamSet(int c, int d, int p, float v) => e.RhythmVoiceDeviceParamSet(t, c, d, p, v);
    public bool ChainDeviceBypassed(int c, int d) => e.RhythmVoiceDeviceBypassed(t, c, d);
    public void SetChainDeviceBypassed(int c, int d, bool b) => e.RhythmSetVoiceDeviceBypassed(t, c, d, b);
    public int ChainDeviceBuiltinKind(int c, int d) => e.RhythmVoiceDeviceBuiltinKind(t, c, d);
    public void OpenChainDeviceEditor(int c, int d) { }
    public float ChainDeviceGainReduction(int c, int d) => e.RhythmVoiceDeviceGainReduction(t, c, d);
    public int ChainDeviceScope(int c, int d, float[] o, int n) => e.RhythmVoiceDeviceScope(t, c, d, o, n);
    public int ChainDeviceLayerWave(int c, int d, int l, float[] o, int n) => e.RhythmVoiceDeviceLayerWave(t, c, d, l, o, n);
    public string ChainDeviceText(int c, int d, int id) => e.RhythmVoiceDeviceText(t, c, d, id);
    public void ChainDeviceAction(int c, int d, int id, int iarg, float farg) => e.RhythmVoiceDeviceAction(t, c, d, id, iarg, farg);
    public float ChainGain(int c) => 1f; public void SetChainGain(int c, float v) { }
    public float ChainPan(int c) => 0f; public void SetChainPan(int c, float v) { }
    public bool ChainMute(int c) => false; public void SetChainMute(int c, bool b) { }
    public bool ChainSolo(int c) => false; public void SetChainSolo(int c, bool b) { }
    public int ChainTriggerNote(int c) => c is >= 0 and < RhythmModel.Voices ? RhythmModel.MidiNotes[c] : -1;
    public void SetChainTriggerNote(int c, int note) { }
    public int AddMacroMapping(int m, int c, int td, int p, float lo, float hi) => -1;
    public int MappingCount() => 0;
    public bool TryGetMapping(int i, out RackMacroMapping m) { m = default; return false; }
    public bool RemoveMapping(int i) => false;
    public string MacroName(int i) => "";
    public void SetMacroName(int i, string name) { }
    public float ChainMeter(int c) => 0f;
    public bool SetMappingRange(int i, float lo, float hi) => false;
    public int MappingCurve(int i) => 0;
    public bool SetMappingCurve(int i, int curve) => false;
}
