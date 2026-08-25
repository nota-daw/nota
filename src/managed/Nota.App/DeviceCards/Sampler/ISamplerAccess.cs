// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — a uniform view over a built-in Sampler so the one editor drives
// either the track's Sampler (deviceIndex -1) or a rack chain's Sampler. Chain params
// ride the generic rackChainInstrumentParam* surface; the sample/root/playhead use the
// rackChainSampler* API. Chain params follow macros (not direct automation), so their
// gestures are no-ops and they are not registered for automation live-follow.

using Nota.Application;

namespace Nota.App;

internal interface ISamplerAccess
{
    int ParamCount();
    string ParamId(int i);
    float ParamGet(int i);
    void ParamSet(int i, float v);
    float ParamDefault(int i);
    bool Info(out long sampleId, out int root);
    void SetRoot(int note);
    void LoadSample(string path);   // load a new sample file (from a drop)
    float PlayPosition();
    void BeginGesture(string id);
    void EndGesture(string id);
    bool Automatable { get; }   // true → wire automation write + live-follow (track only)
}

internal sealed class TrackSamplerAccess(IAudioEngine engine, int trackId) : ISamplerAccess
{
    public int ParamCount() => engine.PluginParamCount(trackId, -1);
    public string ParamId(int i) => engine.PluginParamId(trackId, -1, i);
    public float ParamGet(int i) => engine.PluginParamGet(trackId, -1, i);
    public void ParamSet(int i, float val) => engine.PluginParamSet(trackId, -1, i, val);
    public float ParamDefault(int i) => engine.InstrumentParamDefault(trackId, i);
    public bool Info(out long sampleId, out int root)
    {
        if (engine.TryGetSamplerInfo(trackId, out var si)) { sampleId = si.SampleId; root = si.RootNote; return true; }
        sampleId = 0; root = 60; return false;
    }
    public void SetRoot(int note) => engine.SetTrackSamplerRoot(trackId, note);
    public void LoadSample(string path) => engine.SetTrackSamplerSample(trackId, path, 60);
    public float PlayPosition() => engine.SamplerPlayPosition(trackId);
    public void BeginGesture(string id) => engine.BeginAutomationWrite(trackId, AutomationTarget.PluginParam, -1, -1, id);
    public void EndGesture(string id) => engine.EndAutomationWrite(trackId, AutomationTarget.PluginParam, -1, -1, id);
    public bool Automatable => true;
}

internal sealed class ChainSamplerAccess(IAudioEngine engine, int trackId, int chain) : ISamplerAccess
{
    public int ParamCount() => engine.RackChainInstrumentParamCount(trackId, chain);
    public string ParamId(int i) => engine.RackChainInstrumentParamId(trackId, chain, i);
    public float ParamGet(int i) => engine.RackChainInstrumentParamGet(trackId, chain, i);
    public void ParamSet(int i, float val) => engine.RackChainInstrumentParamSet(trackId, chain, i, val);
    public float ParamDefault(int i) => engine.RackChainInstrumentParamDefault(trackId, chain, i);
    public bool Info(out long sampleId, out int root)
    {
        if (engine.RackChainSamplerInfo(trackId, chain, out var si)) { sampleId = si.SampleId; root = si.RootNote; return true; }
        sampleId = 0; root = 60; return false;
    }
    public void SetRoot(int note) => engine.RackSetChainSamplerRoot(trackId, chain, note);
    public void LoadSample(string path) => engine.RackSetChainSamplerSample(trackId, chain, path, 60);
    public float PlayPosition() => engine.RackChainSamplerPlayPosition(trackId, chain);
    public void BeginGesture(string id) { }   // chain instrument params follow macros, not direct automation
    public void EndGesture(string id) { }
    public bool Automatable => false;
}
