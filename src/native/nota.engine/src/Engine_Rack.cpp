// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — Instrument / Audio-Effect Rack (nested device containers). Resolves a
// (trackId, deviceIndex) pair to the shared RackCore (deviceIndex < 0 = the
// track's instrument rack; >= 0 = the effect-rack device at that chain position)
// and delegates to its editing API. Rack-internal edits publish the rack's OWN
// snapshot (thread-safe) and don't republish the graph — the rack object is
// stable across graph snapshots.

#include "Engine.h"
#include "AudioFile.h"
#include "PluginHostBridge.h"
#include "RackCore.h"
#include "RackInstrument.h"
#include "RackDevice.h"
#include "DrumRack.h"
#include "Sampler.h"

#include <algorithm>
#include <cmath>
#include <vector>

// Catalog id -> index resolution lives in the plugin host (C ABI); reused here so
// a saved rack blob can rebuild a plugin child by its stable identifier.
extern "C" int32_t nota_pluginhost_index_of_id(const char* identifier);

namespace nota {

void Engine::installRackPluginFactory() {
    const int32_t mb = kMaxBlock;
    RackCore::pluginFactory().instrumentById = [mb](const std::string& id) -> std::shared_ptr<Instrument> {
        const int32_t idx = nota_pluginhost_index_of_id(id.c_str());
        return idx >= 0 ? createPluginInstrument(idx, 44100.0, mb) : nullptr;   // rack re-rates it via setRates
    };
    RackCore::pluginFactory().effectById = [mb](const std::string& id) -> std::shared_ptr<Device> {
        const int32_t idx = nota_pluginhost_index_of_id(id.c_str());
        return idx >= 0 ? createPluginEffect(idx, 44100.0, mb) : nullptr;
    };
}

int32_t Engine::rackAddPluginInstrumentChain(int32_t t, int32_t di, int32_t catalogIndex) {
    auto* r = rackCoreAt(t, di);
    if (!r) return -1;
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto inst = createPluginInstrument(catalogIndex, sr, kMaxBlock);
    if (!inst) return -1;
    const int32_t idx = r->addChainInstrument(inst);
    recomputePdc();
    return idx;
}

int32_t Engine::rackAddPluginChainDevice(int32_t t, int32_t di, int32_t chain, int32_t catalogIndex) {
    auto* r = rackCoreAt(t, di);
    if (!r) return -1;
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto dev = createPluginEffect(catalogIndex, sr, kMaxBlock);
    if (!dev) return -1;
    const int32_t idx = r->addChainDeviceInstance(chain, dev);
    recomputePdc();
    return idx;
}


int32_t Engine::addInstrumentRackTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto rack = std::make_shared<RackInstrument>();
    rack->setSampleRate(transport_.sampleRate());
    rack->addChain(0);                 // one default Nota Synth chain
    track->instrument = rack;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addDrumRackTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto rack = std::make_shared<DrumRack>();
    rack->setSampleRate(transport_.sampleRate());   // starts empty; the UI adds pads
    track->instrument = rack;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::rackChainTriggerNote(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di); return r ? r->chainTriggerNote(c) : -1;
}
void Engine::rackSetChainTriggerNote(int32_t t, int32_t di, int32_t c, int32_t note) {
    if (auto* r = rackCoreAt(t, di)) r->setChainTriggerNote(c, note);
}

std::string Engine::rackChainName(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di); return r ? r->chainName(c) : std::string{};
}
void Engine::rackSetChainName(int32_t t, int32_t di, int32_t c, const std::string& name) {
    if (auto* r = rackCoreAt(t, di)) r->setChainName(c, name);
}

int32_t Engine::rackAddSamplerChain(int32_t t, int32_t di, const std::string& path, int32_t rootNote, bool loop) {
    auto* r = rackCoreAt(t, di);
    if (!r) return -1;
    auto buf = decodeAudioFile(path);
    if (!buf || buf->empty()) return -1;
    auto sm = std::make_shared<Sampler>();
    sm->setSampleRate(transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0);
    sm->setSample(buf, rootNote, loop);
    return r->addChainInstrument(sm);
}
bool Engine::rackSetChainSamplerSample(int32_t t, int32_t di, int32_t chain, const std::string& path, int32_t rootNote) {
    auto* r = rackCoreAt(t, di);
    if (!r) return false;
    auto buf = decodeAudioFile(path);
    if (!buf || buf->empty()) return false;
    return r->setChainInstrumentSample(chain, buf, rootNote, false);
}

RackCore* Engine::rackCoreAt(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return nullptr;
    if (deviceIndex < 0)
        return t->instrument ? dynamic_cast<RackCore*>(t->instrument.get()) : nullptr;
    if (deviceIndex < static_cast<int32_t>(t->devices.size()))
        return t->devices[deviceIndex] ? dynamic_cast<RackCore*>(t->devices[deviceIndex].get()) : nullptr;
    return nullptr;
}

// ---- chains --------------------------------------------------------------
int32_t Engine::rackChainCount(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r ? r->chainCount() : 0; }
int32_t Engine::rackAddChain(int32_t t, int32_t di, int32_t k) { auto* r = rackCoreAt(t, di); return r ? r->addChain(k) : -1; }
bool Engine::rackRemoveChain(int32_t t, int32_t di, int32_t c) { auto* r = rackCoreAt(t, di); return r && r->removeChain(c); }
bool Engine::rackSetChainInstrument(int32_t t, int32_t di, int32_t c, int32_t k) { auto* r = rackCoreAt(t, di); return r && r->setChainInstrument(c, k); }
int32_t Engine::rackChainInstrumentKind(int32_t t, int32_t di, int32_t c) const { auto* r = rackCoreAt(t, di); return r ? r->chainInstrumentKind(c) : -2; }
const char* Engine::rackChainInstrumentName(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->displayName() : "";
}
int32_t Engine::rackChainInstrumentParamCount(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->pluginParamCount() : 0;
}
std::string Engine::rackChainInstrumentParamName(int32_t t, int32_t di, int32_t c, int32_t p) const {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->pluginParamName(p) : std::string{};
}
std::string Engine::rackChainInstrumentParamId(int32_t t, int32_t di, int32_t c, int32_t p) const {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->pluginParamId(p) : std::string{};
}
float Engine::rackChainInstrumentParamGet(int32_t t, int32_t di, int32_t c, int32_t p) const {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->pluginParamGet(p) : 0.0f;
}
void Engine::rackChainInstrumentParamSet(int32_t t, int32_t di, int32_t c, int32_t p, float v) {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (inst) inst->pluginParamSet(p, v);
}
float Engine::rackChainInstrumentParamDefault(int32_t t, int32_t di, int32_t c, int32_t p) const {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (!inst) return 0.0f;
    if (inst->kind() < 0) return inst->pluginParamGet(p);   // hosted plugin: no fresh default
    auto fresh = RackCore::makeInstrument(inst->kind());    // built-in default == a fresh one's value
    return fresh ? fresh->pluginParamGet(p) : inst->pluginParamGet(p);
}
void Engine::rackOpenChainInstrumentEditor(int32_t t, int32_t di, int32_t c) {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (inst) inst->openEditor();
}
void Engine::rackOpenChainDeviceEditor(int32_t t, int32_t di, int32_t c, int32_t d) {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    if (dev) dev->openEditor();
}
std::string Engine::rackChainInstrumentPluginId(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->pluginIdentifier() : std::string{};
}
int32_t Engine::rackChainInstrumentGetState(int32_t t, int32_t di, int32_t c, uint8_t* out, int32_t cap) const {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (!inst) return 0;
    std::vector<uint8_t> v = inst->getState();
    if (out && cap >= static_cast<int32_t>(v.size())) std::copy(v.begin(), v.end(), out);
    return static_cast<int32_t>(v.size());
}

// ---- chain Sampler (built-in) --------------------------------------------
// A chain's instrument may be a built-in Sampler; these mirror the track-level
// samplerInfo/setTrackSamplerRoot/samplerPlayPosition so the rich Sampler editor
// works per-chain (drum-rack pads + instrument-rack Sampler chains). The 17
// plugin-params + start/end/loop markers ride the generic rackChainInstrumentParam*.
bool Engine::rackChainSamplerInfo(int32_t t, int32_t di, int32_t c, NotaSamplerInfo* out) const {
    if (!out) return false;
    auto* r = rackCoreAt(t, di);
    auto* sm = r ? dynamic_cast<Sampler*>(r->chainInstrument(c)) : nullptr;
    if (!sm) return false;
    out->sample_id = sm->sample() ? sm->sample()->id : 0;
    out->root_note = sm->rootNote();
    out->loop = sm->loopEnabled() ? 1 : 0;
    return true;
}
bool Engine::rackSetChainSamplerRoot(int32_t t, int32_t di, int32_t c, int32_t rootNote) {
    auto* r = rackCoreAt(t, di);
    auto* sm = r ? dynamic_cast<Sampler*>(r->chainInstrument(c)) : nullptr;
    if (!sm) return false;
    sm->setRoot(rootNote);   // lock-free atomic; the sampler is shared across snapshots
    return true;
}
float Engine::rackChainSamplerPlayPosition(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    auto* sm = r ? dynamic_cast<Sampler*>(r->chainInstrument(c)) : nullptr;
    return sm ? sm->playPosition() : -1.0f;
}

// ---- chain devices -------------------------------------------------------
int32_t Engine::rackChainDeviceCount(int32_t t, int32_t di, int32_t c) const { auto* r = rackCoreAt(t, di); return r ? r->chainDeviceCount(c) : 0; }
int32_t Engine::rackAddChainDevice(int32_t t, int32_t di, int32_t c, int32_t k) { auto* r = rackCoreAt(t, di); return r ? r->addChainDevice(c, k) : -1; }
bool Engine::rackRemoveChainDevice(int32_t t, int32_t di, int32_t c, int32_t d) { auto* r = rackCoreAt(t, di); return r && r->removeChainDevice(c, d); }
bool Engine::rackMoveChainDevice(int32_t t, int32_t di, int32_t c, int32_t from, int32_t to) { auto* r = rackCoreAt(t, di); return r && r->moveChainDevice(c, from, to); }
const char* Engine::rackChainDeviceName(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = rackCoreAt(t, di); Device* dev = r ? r->chainDeviceAt(c, d) : nullptr; return dev ? dev->displayName() : "";
}
int32_t Engine::rackChainDeviceBuiltinKind(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = rackCoreAt(t, di); Device* dev = r ? r->chainDeviceAt(c, d) : nullptr; return dev ? dev->builtinKind() : -1;
}
int32_t Engine::rackChainDeviceParamCount(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = rackCoreAt(t, di); Device* dev = r ? r->chainDeviceAt(c, d) : nullptr; return dev ? dev->paramCount() : 0;
}
const char* Engine::rackChainDeviceParamName(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = rackCoreAt(t, di); Device* dev = r ? r->chainDeviceAt(c, d) : nullptr; return dev ? dev->paramName(p) : "";
}
float Engine::rackChainDeviceParamMin(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = rackCoreAt(t, di); Device* dev = r ? r->chainDeviceAt(c, d) : nullptr; return dev ? dev->paramMin(p) : 0.0f;
}
float Engine::rackChainDeviceParamMax(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = rackCoreAt(t, di); Device* dev = r ? r->chainDeviceAt(c, d) : nullptr; return dev ? dev->paramMax(p) : 1.0f;
}
float Engine::rackChainDeviceParamGet(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = rackCoreAt(t, di); Device* dev = r ? r->chainDeviceAt(c, d) : nullptr; return dev ? dev->getParam(p) : 0.0f;
}
void Engine::rackChainDeviceParamSet(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p, float v) {
    auto* r = rackCoreAt(t, di); Device* dev = r ? r->chainDeviceAt(c, d) : nullptr; if (dev) dev->setParam(p, v);
}
void Engine::rackSetChainDeviceBypassed(int32_t t, int32_t di, int32_t c, int32_t d, bool b) {
    auto* r = rackCoreAt(t, di); Device* dev = r ? r->chainDeviceAt(c, d) : nullptr; if (dev) dev->setBypassed(b);
}
bool Engine::rackChainDeviceBypassed(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = rackCoreAt(t, di); Device* dev = r ? r->chainDeviceAt(c, d) : nullptr; return dev && dev->bypassed();
}

// ---- per-chain controls --------------------------------------------------
void  Engine::rackSetChainGain(int32_t t, int32_t di, int32_t c, float v) { if (auto* r = rackCoreAt(t, di)) r->setChainGain(c, v); }
void  Engine::rackSetChainPan (int32_t t, int32_t di, int32_t c, float v) { if (auto* r = rackCoreAt(t, di)) r->setChainPan(c, v); }
void  Engine::rackSetChainMute(int32_t t, int32_t di, int32_t c, bool  b) { if (auto* r = rackCoreAt(t, di)) r->setChainMute(c, b); }
void  Engine::rackSetChainSolo(int32_t t, int32_t di, int32_t c, bool  b) { if (auto* r = rackCoreAt(t, di)) r->setChainSolo(c, b); }
float Engine::rackChainGain(int32_t t, int32_t di, int32_t c) const { auto* r = rackCoreAt(t, di); return r ? r->chainGain(c) : 1.0f; }
float Engine::rackChainPan (int32_t t, int32_t di, int32_t c) const { auto* r = rackCoreAt(t, di); return r ? r->chainPan(c)  : 0.0f; }
bool  Engine::rackChainMute(int32_t t, int32_t di, int32_t c) const { auto* r = rackCoreAt(t, di); return r && r->chainMute(c); }
bool  Engine::rackChainSolo(int32_t t, int32_t di, int32_t c) const { auto* r = rackCoreAt(t, di); return r && r->chainSolo(c); }

// ---- Drum Rack per-pad shaping + kit-level swing/humanize -----------------
void    Engine::rackSetChainChoke(int32_t t, int32_t di, int32_t c, int32_t g) { if (auto* r = rackCoreAt(t, di)) r->setChainChoke(c, g); }
int32_t Engine::rackChainChoke(int32_t t, int32_t di, int32_t c) const { auto* r = rackCoreAt(t, di); return r ? r->chainChoke(c) : 0; }
void    Engine::rackSetChainTune(int32_t t, int32_t di, int32_t c, int32_t st) { if (auto* r = rackCoreAt(t, di)) r->setChainTune(c, st); }
int32_t Engine::rackChainTune(int32_t t, int32_t di, int32_t c) const { auto* r = rackCoreAt(t, di); return r ? r->chainTune(c) : 0; }
void    Engine::rackSetChainDecay(int32_t t, int32_t di, int32_t c, float v) { if (auto* r = rackCoreAt(t, di)) r->setChainDecay(c, v); }
float   Engine::rackChainDecay(int32_t t, int32_t di, int32_t c) const { auto* r = rackCoreAt(t, di); return r ? r->chainDecay(c) : 1.0f; }
void    Engine::rackSetSwing(int32_t t, int32_t di, float v) { if (auto* r = rackCoreAt(t, di)) r->setSwing(v); }
float   Engine::rackSwing(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r ? r->swing() : 0.0f; }
void    Engine::rackSetHumanize(int32_t t, int32_t di, float v) { if (auto* r = rackCoreAt(t, di)) r->setHumanize(v); }
float   Engine::rackHumanize(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r ? r->humanize() : 0.0f; }

// ---- Audio Effect Rack routing + output stage ----------------------------
void    Engine::rackSetMode(int32_t t, int32_t di, int32_t m) { if (auto* r = rackCoreAt(t, di)) r->setRackMode(m); }
int32_t Engine::rackMode(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r ? r->rackMode() : 0; }
void    Engine::rackSetDryWet(int32_t t, int32_t di, float v) { if (auto* r = rackCoreAt(t, di)) r->setRackDryWet(v); }
float   Engine::rackDryWet(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r ? r->rackDryWet() : 1.0f; }
void    Engine::rackSetPdc(int32_t t, int32_t di, bool b) { if (auto* r = rackCoreAt(t, di)) r->setRackPdc(b); }
bool    Engine::rackPdc(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r && r->rackPdc(); }
void    Engine::rackSetChainSelect(int32_t t, int32_t di, float v) { if (auto* r = rackCoreAt(t, di)) r->setChainSelect(v); }
float   Engine::rackChainSelect(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r ? r->chainSelect() : 0.5f; }
void    Engine::rackSetSelFollow(int32_t t, int32_t di, bool b) { if (auto* r = rackCoreAt(t, di)) r->setSelFollow(b); }
bool    Engine::rackSelFollow(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r && r->selFollow(); }
float   Engine::rackLiveSelector(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r ? r->liveSelector() : 0.5f; }

// ---- macros + mappings ---------------------------------------------------
float Engine::rackMacroGet(int32_t t, int32_t di, int32_t m) const { auto* r = rackCoreAt(t, di); return r ? r->macroGet(m) : 0.0f; }
void  Engine::rackMacroSet(int32_t t, int32_t di, int32_t m, float v) { if (auto* r = rackCoreAt(t, di)) r->macroSet(m, v); }
int32_t Engine::rackAddMacroMapping(int32_t t, int32_t di, int32_t macro, int32_t chain,
        int32_t targetDevice, int32_t paramIndex, float rangeMin, float rangeMax) {
    auto* r = rackCoreAt(t, di);
    return r ? r->addMacroMapping(macro, chain, targetDevice, paramIndex, rangeMin, rangeMax) : -1;
}
int32_t Engine::rackMappingCount(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r ? r->mappingCount() : 0; }
bool Engine::rackMappingInfo(int32_t t, int32_t di, int32_t index, int32_t& macro, int32_t& chain,
        int32_t& targetDevice, int32_t& paramIndex, float& rangeMin, float& rangeMax) const {
    auto* r = rackCoreAt(t, di);
    RackCore::MacroMapping m;
    if (!r || !r->mappingInfo(index, m)) return false;
    macro = m.macro; chain = m.chainIndex; targetDevice = m.deviceIndex;
    paramIndex = m.paramIndex; rangeMin = m.rangeMin; rangeMax = m.rangeMax;
    return true;
}
bool Engine::rackRemoveMapping(int32_t t, int32_t di, int32_t index) { auto* r = rackCoreAt(t, di); return r && r->removeMapping(index); }

// ---- Instrument-Rack extras (zones / meter / names / rack out / macro-map) ----
void Engine::rackChainZone(int32_t t, int32_t di, int32_t c, int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const {
    auto* r = rackCoreAt(t, di);
    if (r) r->chainZone(c, keyLo, keyHi, velLo, velHi); else { keyLo = 0; keyHi = 127; velLo = 0; velHi = 127; }
}
void Engine::rackSetChainZone(int32_t t, int32_t di, int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) {
    if (auto* r = rackCoreAt(t, di)) r->setChainZone(c, keyLo, keyHi, velLo, velHi);
}
float Engine::rackChainMeter(int32_t t, int32_t di, int32_t c) const { auto* r = rackCoreAt(t, di); return r ? r->chainMeter(c) : 0.0f; }
std::string Engine::rackMacroName(int32_t t, int32_t di, int32_t m) const { auto* r = rackCoreAt(t, di); return r ? r->macroName(m) : std::string{}; }
void Engine::rackSetMacroName(int32_t t, int32_t di, int32_t m, const std::string& name) { if (auto* r = rackCoreAt(t, di)) r->setMacroName(m, name); }
float Engine::rackVolume(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r ? r->rackVolume() : 1.0f; }
void Engine::rackSetVolume(int32_t t, int32_t di, float v) { if (auto* r = rackCoreAt(t, di)) r->setRackVolume(v); }
float Engine::rackGlide(int32_t t, int32_t di) const { auto* r = rackCoreAt(t, di); return r ? r->rackGlide() : 0.0f; }
void Engine::rackSetGlide(int32_t t, int32_t di, float v) { if (auto* r = rackCoreAt(t, di)) r->setRackGlide(v); }
bool Engine::rackSetMappingRange(int32_t t, int32_t di, int32_t i, float lo, float hi) { auto* r = rackCoreAt(t, di); return r && r->setMappingRange(i, lo, hi); }
int32_t Engine::rackMappingCurve(int32_t t, int32_t di, int32_t i) const { auto* r = rackCoreAt(t, di); return r ? r->mappingCurve(i) : 0; }
bool Engine::rackSetMappingCurve(int32_t t, int32_t di, int32_t i, int32_t curve) { auto* r = rackCoreAt(t, di); return r && r->setMappingCurve(i, curve); }

// Device-free self-test for the rack core (kept from Phase 1; now exercises the
// shared RackCore via a RackInstrument): two synth chains sum to audible output,
// mute silences, a macro mapping drives a child param, and the blob round-trips.
bool Engine::rackSelfTest() {
    RackInstrument rack;
    rack.setSampleRate(44100.0);
    if (rack.addChain(0) != 0) return false;
    if (rack.addChain(0) != 1) return false;
    if (rack.chainCount() != 2) return false;

    const int32_t frames = 512;
    std::vector<float> buf(frames * 2, 0.0f);
    rack.noteOn(69, 1.0f);
    float peak = 0.0f;
    for (int b = 0; b < 8; ++b) {
        std::fill(buf.begin(), buf.end(), 0.0f);
        rack.render(buf.data(), frames);
        for (float s : buf) peak = std::max(peak, std::fabs(s));
    }
    if (!(peak > 0.001f)) return false;

    rack.setChainMute(0, true); rack.setChainMute(1, true);
    std::fill(buf.begin(), buf.end(), 0.0f);
    rack.render(buf.data(), frames);
    for (float s : buf) if (std::fabs(s) > 1e-6f) return false;
    rack.setChainMute(0, false); rack.setChainMute(1, false);

    if (rack.addMacroMapping(0, 0, -1, 5, 0.0f, 1.0f) < 0) return false;
    rack.macroSet(0, 0.25f);
    Instrument* ci = rack.chainInstrument(0);
    if (!ci || std::fabs(ci->pluginParamGet(5) - 0.25f) > 1e-4f) return false;
    rack.macroSet(0, 0.90f);
    if (std::fabs(ci->pluginParamGet(5) - 0.90f) > 1e-4f) return false;

    rack.setChainGain(1, 0.3f);
    auto blob = rack.getState();
    RackInstrument rk2;
    rk2.setSampleRate(44100.0);
    rk2.setState(blob.data(), static_cast<int32_t>(blob.size()));
    if (rk2.chainCount() != 2) return false;
    if (std::fabs(rk2.chainGain(1) - 0.3f) > 1e-4f) return false;
    if (std::fabs(rk2.macroGet(0) - 0.90f) > 1e-4f) return false;
    rk2.macroSet(0, 0.5f);
    Instrument* ci2 = rk2.chainInstrument(0);
    if (!ci2 || std::fabs(ci2->pluginParamGet(5) - 0.5f) > 1e-4f) return false;
    return true;
}

// Device-free self-test for the Audio Effect Rack: an empty rack passes audio
// through; two parallel Utility chains sum; mute silences a chain; the blob
// round-trips chain structure.
bool Engine::rackDeviceSelfTest() {
    RackDevice rack;
    rack.setSampleRate(44100.0, RackCore::kMaxBlock);

    const int32_t frames = 256;
    std::vector<float> buf(frames * 2);
    auto fill = [&](float v) { for (auto& s : buf) s = v; };

    // Empty rack (no chains) is a pass-through.
    fill(0.5f);
    rack.process(buf.data(), frames);
    for (float s : buf) if (std::fabs(s - 0.5f) > 1e-6f) return false;

    // Two Utility chains at gain 1 sum a 0.25 input to ~0.5.
    if (rack.addChain(-1) != 0) return false;         // effect chain (no instrument)
    if (rack.addChain(-1) != 1) return false;
    if (rack.chainCount() != 2) return false;
    if (rack.addChainDevice(0, 4) < 0) return false;  // Utility on each chain
    if (rack.addChainDevice(1, 4) < 0) return false;
    fill(0.25f);
    rack.process(buf.data(), frames);
    if (std::fabs(buf[0] - 0.5f) > 1e-3f) return false;

    // Muting one chain drops the sum back to ~0.25.
    rack.setChainMute(1, true);
    fill(0.25f);
    rack.process(buf.data(), frames);
    if (std::fabs(buf[0] - 0.25f) > 1e-3f) return false;

    // Blob round-trips the two-chain structure onto a fresh rack.
    auto blob = rack.getState();
    RackDevice rk2;
    rk2.setSampleRate(44100.0, RackCore::kMaxBlock);
    rk2.setState(blob.data(), static_cast<int32_t>(blob.size()));
    if (rk2.chainCount() != 2 || rk2.chainDeviceCount(0) != 1 || !rk2.chainMute(1)) return false;
    return true;
}

// Device-free self-test for the Drum Rack: notes route only to the pad chain
// whose triggerNote matches, and the triggerNote survives the blob round-trip.
bool Engine::drumRackSelfTest() {
    DrumRack rack;
    rack.setSampleRate(44100.0);
    // Pad A on note 36, pad B on note 38 (both Synths).
    if (rack.addChain(0) != 0) return false;
    if (rack.addChain(0) != 1) return false;
    rack.setChainTriggerNote(0, 36);
    rack.setChainTriggerNote(1, 38);

    const int32_t frames = 512;
    std::vector<float> buf(frames * 2, 0.0f);
    auto peakAfter = [&](int32_t note) {
        rack.allNotesOff();
        rack.noteOn(note, 1.0f);
        float pk = 0.0f;
        for (int b = 0; b < 8; ++b) {
            std::fill(buf.begin(), buf.end(), 0.0f);
            rack.render(buf.data(), frames);
            for (float s : buf) pk = std::max(pk, std::fabs(s));
        }
        rack.noteOff(note);
        return pk;
    };
    if (!(peakAfter(36) > 0.001f)) return false;   // pad A note plays
    if (!(peakAfter(38) > 0.001f)) return false;   // pad B note plays
    // A note assigned to no pad makes no sound.
    rack.allNotesOff();
    rack.noteOn(60, 1.0f);
    float silent = 0.0f;
    for (int b = 0; b < 4; ++b) { std::fill(buf.begin(), buf.end(), 0.0f); rack.render(buf.data(), frames); for (float s : buf) silent = std::max(silent, std::fabs(s)); }
    if (silent > 1e-6f) return false;

    auto blob = rack.getState();
    DrumRack rk2;
    rk2.setSampleRate(44100.0);
    rk2.setState(blob.data(), static_cast<int32_t>(blob.size()));
    if (rk2.chainTriggerNote(0) != 36 || rk2.chainTriggerNote(1) != 38) return false;
    return true;
}

} // namespace nota
