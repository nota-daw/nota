// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — hosted plugins & device chain (M3): load instrument/effect, device CRUD/params/bypass/editor/state, clone helpers, kind/id introspection, PDC latency.

#include "Engine.h"
#include "Amp.h"
#include "AutoFilter.h"
#include "AudioFile.h"
#include "Compressor.h"
#include "CoreAudioBackend.h"
#include "Delay.h"
#include "Eq.h"
#include "AutoPan.h"
#include "AutoShift.h"
#include "BeatRepeat.h"
#include "Crush.h"
#include "DynamicEq.h"
#include "Eq3.h"
#include "Forge.h"
#include "AutoGain.h"
#include "Shutter.h"
#include "Chamber.h"
#include "Lens.h"
#include "Prism.h"
#include "Ceiling.h"
#include "Strata.h"
#include "Vintage.h"
#include "PhysicalSynth.h"
#include "WavetableSynth.h"
#include "VoltSynth.h"
#include "BassSynth.h"
#include "PendulumSynth.h"
#include "OperatorSynth.h"
#include "FluxSynth.h"
#include "RhythmMachine.h"
#include "Monolith.h"
#include "Pentad.h"
#include "Consort.h"
#include "GrainSynth.h"
#include "Arpeggiator.h"
#include "MidiChord.h"
#include "MidiScale.h"
#include "MidiNoteLength.h"
#include "MidiVelocity.h"
#include "MidiRandom.h"
#include "PluginHostBridge.h"
#include "RackDevice.h"
#include "Reverb.h"
#include "Sampler.h"
#include "Synth.h"
#include "Utility.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstring>
#include <vector>

namespace nota {

int32_t Engine::trackInstrumentKind(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return -2;
    return t->instrument ? t->instrument->kind() : -2;
}

int32_t Engine::trackDeviceBuiltinKind(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return -1;
    return t->devices[deviceIndex]->builtinKind();
}

std::string Engine::trackInstrumentPluginId(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return (t && t->instrument) ? t->instrument->pluginIdentifier() : std::string{};
}

std::string Engine::trackDevicePluginId(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return {};
    return t->devices[deviceIndex]->pluginIdentifier();
}

// --- structural edits: instruments & MIDI (M2) -----------------------------
int32_t Engine::addInstrumentTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto synth = std::make_shared<Synth>();
    synth->setSampleRate(transport_.sampleRate());
    track->instrument = synth;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addPhysicalSynthTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<PhysicalSynth>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addWavetableSynthTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<WavetableSynth>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addVoltSynthTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<VoltSynth>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}
int32_t Engine::addBassSynthTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<BassSynth>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addPendulumSynthTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<PendulumSynth>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addOperatorSynthTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<OperatorSynth>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addGrainSynthTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<GrainSynth>();
    inst->setSampleRate(transport_.sampleRate());   // seeds the procedural default sample
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addFluxSynthTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<FluxSynth>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addRhythmSynthTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<RhythmMachine>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addMonolithTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<Monolith>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addPentadTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<Pentad>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addConsortTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<Consort>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

// UI editing channel for the track's instrument (e.g. Nota Rhythm step patterns). The
// instrument object is stable across snapshots, so this mutates it in place (message thread).
void Engine::instrumentAction(int32_t trackId, int32_t id, int32_t iarg, float farg) {
    auto t = findTrackAuthoring(trackId);
    if (t && t->instrument) t->instrument->action(id, iarg, farg);
}

// Nota Rhythm Phase 2: load a one-shot into one drum voice. Clones the instrument (so the
// shared_ptr swap never races the audio thread), loads the buffer, republishes.
bool Engine::setRhythmVoiceSample(int32_t trackId, int32_t voice, const std::string& path) {
    auto old = findTrackAuthoring(trackId);
    auto* rh = old && old->instrument ? dynamic_cast<RhythmMachine*>(old->instrument.get()) : nullptr;
    if (!rh) return false;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return false;
    auto fresh = std::static_pointer_cast<RhythmMachine>(rh->clone());   // copies params/patterns/samples
    fresh->setVoiceSample(voice, sample);
    auto nt = cloneTrack(*old);
    nt->instrument = fresh;
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::setTrackGrainSample(int32_t trackId, const std::string& path, int32_t rootNote) {
    auto old = findTrackAuthoring(trackId);
    auto* gr = old && old->instrument ? dynamic_cast<GrainSynth*>(old->instrument.get()) : nullptr;
    if (!gr) return false;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return false;
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto fresh = std::make_shared<GrainSynth>();
    fresh->setSampleRate(sr);
    for (int i = 0; i < gr->pluginParamCount(); ++i) fresh->pluginParamSet(i, gr->pluginParamGet(i));
    fresh->setSample(sample, rootNote, false);
    auto nt = cloneTrack(*old);
    nt->instrument = fresh;
    republishWithTrack(trackId, nt);
    return true;
}

int32_t Engine::addSamplerTrack(const std::string& path, int32_t rootNote, bool loop) {
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return 0;
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto sampler = std::make_shared<Sampler>();
    sampler->setSampleRate(transport_.sampleRate());
    sampler->setSample(sample, rootNote, loop);
    track->instrument = sampler;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addSamplerInstrumentTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto sampler = std::make_shared<Sampler>();   // empty — a sample is loaded later
    sampler->setSampleRate(transport_.sampleRate());
    track->instrument = sampler;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

bool Engine::setTrackSamplerSample(int32_t trackId, const std::string& path, int32_t rootNote) {
    auto old = findTrackAuthoring(trackId);
    auto* sm = old && old->instrument ? dynamic_cast<Sampler*>(old->instrument.get()) : nullptr;
    if (!sm) return false;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return false;
    // Publish a fresh Sampler (new sample + copied params) rather than mutating the live
    // one in place — the audio thread reads the instrument's shared_ptr, so replacing it
    // via republishWithTrack keeps the load race-free. Voices reset (fine on load).
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto fresh = std::make_shared<Sampler>();
    fresh->setSampleRate(sr);
    for (int i = 0; i < sm->pluginParamCount(); ++i) fresh->pluginParamSet(i, sm->pluginParamGet(i));
    fresh->setSample(sample, rootNote, sm->loopEnabled());
    auto nt = cloneTrack(*old);
    nt->instrument = fresh;
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::setTrackSamplerRoot(int32_t trackId, int32_t rootNote) {
    auto t = findTrackAuthoring(trackId);
    auto* sm = t && t->instrument ? dynamic_cast<Sampler*>(t->instrument.get()) : nullptr;
    if (!sm) return false;
    sm->setRoot(rootNote);   // lock-free atomic; the sampler is shared across snapshots
    return true;
}

float Engine::samplerPlayPosition(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    auto* sm = t && t->instrument ? dynamic_cast<Sampler*>(t->instrument.get()) : nullptr;
    return sm ? sm->playPosition() : -1.0f;
}

// --- hosted plugins (M3-3) -------------------------------------------------

int32_t Engine::addPluginInstrumentTrack(int32_t catalogIndex) {
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto inst = createPluginInstrument(catalogIndex, sr, kMaxBlock);
    if (!inst) return 0;
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    recomputePdc();
    return id;
}

bool Engine::setTrackInstrumentPlugin(int32_t trackId, int32_t catalogIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto inst = createPluginInstrument(catalogIndex, sr, kMaxBlock);
    if (!inst) return false;
    auto nt = cloneTrack(*old);
    nt->instrument = inst;
    republishWithTrack(trackId, nt);
    recomputePdc();
    return true;
}

int32_t Engine::addTrackEffectPlugin(int32_t trackId, int32_t catalogIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto dev = createPluginEffect(catalogIndex, sr, kMaxBlock);
    if (!dev) return -1;
    auto nt = cloneTrack(*old);
    nt->devices.push_back(dev);
    const int32_t idx = static_cast<int32_t>(nt->devices.size()) - 1;
    republishWithTrack(trackId, nt);
    recomputePdc();
    return idx;
}

int32_t Engine::trackDeviceCount(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->devices.size()) : 0;
}

// Offline analysis (audio→MIDI): copy an audio clip's played source region, down-mixed to
// mono. out == nullptr returns the available frame count; otherwise writes up to maxFrames and
// returns how many were written. outSr (optional) receives the sample's source sample rate.
int32_t Engine::clipAudioMono(int32_t trackId, int32_t clipIndex, float* out, int32_t maxFrames, double* outSr) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    const AudioClip& c = t->clips[clipIndex];
    if (!c.sample || c.sample->empty()) return 0;
    const SampleBuffer& sb = *c.sample;
    if (outSr) *outSr = sb.sourceSampleRate > 0.0 ? sb.sourceSampleRate : 44100.0;
    const int64_t off = static_cast<int64_t>(c.sourceOffsetFrames);
    int64_t len = c.effectiveLength();                 // frames in the source region
    if (len <= 0) return 0;
    len = std::min<int64_t>(len, 30000000);            // ~11 min @ 44.1k — bound offline memory
    if (!out) return static_cast<int32_t>(len);
    const int32_t n = static_cast<int32_t>(std::min<int64_t>(len, maxFrames));
    const int ch = sb.channels;
    for (int32_t i = 0; i < n; ++i) {
        const int64_t f = off + i;
        float acc = 0.0f;
        if (f >= 0 && f < sb.frames) {
            const float* p = &sb.samples[f * ch];
            for (int k = 0; k < ch; ++k) acc += p[k];
            acc /= static_cast<float>(ch);
        }
        out[i] = acc;
    }
    return n;
}

// --- built-in devices + generic params (M4-4/5) ----------------------------

int32_t Engine::addTrackBuiltinDevice(int32_t trackId, int32_t kind) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    std::shared_ptr<Device> dev;
    if (kind == 0)      dev = std::make_shared<Eq>();
    else if (kind == 1) dev = std::make_shared<Compressor>();
    else if (kind == 2) dev = std::make_shared<Reverb>();
    else if (kind == 3) dev = std::make_shared<Delay>();
    else if (kind == 4) dev = std::make_shared<Utility>();
    else if (kind == 5) dev = std::make_shared<RackDevice>();   // Audio Effect Rack
    else if (kind == 6) dev = std::make_shared<Amp>();
    else if (kind == 7) dev = std::make_shared<AutoFilter>();
    else if (kind == 8) dev = std::make_shared<Vintage>();
    else if (kind == 9) dev = std::make_shared<AutoPan>();
    else if (kind == 10) dev = std::make_shared<AutoShift>();
    else if (kind == 11) dev = std::make_shared<BeatRepeat>();
    else if (kind == 12) dev = std::make_shared<Crush>();
    else if (kind == 13) dev = std::make_shared<DynamicEq>();
    else if (kind == 14) dev = std::make_shared<Ceiling>();
    else if (kind == 15) dev = std::make_shared<Strata>();
    else if (kind == 16) dev = std::make_shared<Eq3>();
    else if (kind == 17) dev = std::make_shared<Forge>();
    else if (kind == 18) dev = std::make_shared<AutoGain>();
    else if (kind == 19) dev = std::make_shared<Shutter>();
    else if (kind == 20) dev = std::make_shared<Chamber>();
    else if (kind == 21) dev = std::make_shared<Prism>();
    else if (kind == 22) dev = std::make_shared<Lens>();
    else return -1;
    dev->setSampleRate(transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0, kMaxBlock);
    if (kind == 5) if (auto* rk = dynamic_cast<RackDevice*>(dev.get())) rk->addChain(-1);  // one pass-through chain
    auto nt = cloneTrack(*old);
    nt->devices.push_back(dev);
    const int32_t idx = static_cast<int32_t>(nt->devices.size()) - 1;
    republishWithTrack(trackId, nt);
    recomputePdc();
    return idx;
}

std::shared_ptr<Instrument> Engine::cloneInstrument(const Track& src) const {
    if (!src.instrument) return nullptr;
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    if (auto c = src.instrument->clone()) { c->setSampleRate(sr); return c; }  // hosted plugin
    switch (src.instrument->kind()) {                                          // built-ins
        case 0: { auto s = std::make_shared<Synth>();   s->setSampleRate(sr); return s; }
        case 1: {
            auto sm = std::make_shared<Sampler>(); sm->setSampleRate(sr);
            if (auto* srcSm = dynamic_cast<Sampler*>(src.instrument.get()))
                sm->setSample(srcSm->sample(), srcSm->rootNote(), srcSm->loopEnabled());
            return sm;
        }
        case 2: { auto p = std::make_shared<PhysicalSynth>(); p->setSampleRate(sr); return p; }
        case 5: { auto w = std::make_shared<WavetableSynth>(); w->setSampleRate(sr); return w; }
        case 6: { auto v = std::make_shared<VoltSynth>(); v->setSampleRate(sr); return v; }
        case 7: { auto b = std::make_shared<BassSynth>(); b->setSampleRate(sr); return b; }
        case 8: { auto p = std::make_shared<PendulumSynth>(); p->setSampleRate(sr); return p; }
        case 9: { auto op = std::make_shared<OperatorSynth>(); op->setSampleRate(sr); return op; }
        case 10: { auto gr = std::make_shared<GrainSynth>(); gr->setSampleRate(sr); return gr; }
        case 11: { auto fx = std::make_shared<FluxSynth>(); fx->setSampleRate(sr); return fx; }
        case 12: { auto rh = std::make_shared<RhythmMachine>(); rh->setSampleRate(sr); return rh; }
        case 13: { auto mo = std::make_shared<Monolith>(); mo->setSampleRate(sr); return mo; }
        case 14: { auto pe = std::make_shared<Pentad>(); pe->setSampleRate(sr); return pe; }
        case 15: { auto co = std::make_shared<Consort>(); co->setSampleRate(sr); return co; }
        default: return nullptr;
    }
}

std::shared_ptr<Device> Engine::cloneDevice(const Device& src) const {
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    std::shared_ptr<Device> d;
    switch (src.builtinKind()) {                                               // built-ins
        case 0: d = std::make_shared<Eq>(); break;
        case 1: d = std::make_shared<Compressor>(); break;
        case 2: d = std::make_shared<Reverb>(); break;
        case 3: d = std::make_shared<Delay>(); break;
        case 4: d = std::make_shared<Utility>(); break;
        case 6: d = std::make_shared<Amp>(); break;
        case 7: d = std::make_shared<AutoFilter>(); break;
        case 8: d = std::make_shared<Vintage>(); break;
        case 9: d = std::make_shared<AutoPan>(); break;
        case 10: d = std::make_shared<AutoShift>(); break;
        case 11: d = std::make_shared<BeatRepeat>(); break;
        case 12: d = std::make_shared<Crush>(); break;
        case 13: d = std::make_shared<DynamicEq>(); break;
        case 14: d = std::make_shared<Ceiling>(); break;
        case 15: d = std::make_shared<Strata>(); break;
        case 16: d = std::make_shared<Eq3>(); break;
        case 17: d = std::make_shared<Forge>(); break;
        case 18: d = std::make_shared<AutoGain>(); break;
        case 19: d = std::make_shared<Shutter>(); break;
        case 20: d = std::make_shared<Chamber>(); break;
        case 21: d = std::make_shared<Prism>(); break;
        case 22: d = std::make_shared<Lens>(); break;
        default: d = src.clone(); break;                                       // hosted plugin
    }
    if (!d) return nullptr;
    d->setSampleRate(sr, kMaxBlock);
    if (src.builtinKind() >= 0) {                                             // copy generic params
        const int32_t pc = src.paramCount();
        for (int32_t p = 0; p < pc; ++p) d->setParam(p, src.getParam(p));
        auto st = src.getState();                                            // + any state blob (looper PCM)
        if (!st.empty()) d->setState(st.data(), (int32_t)st.size());
    }
    d->setBypassed(src.bypassed());
    return d;
}


bool Engine::moveDevice(int32_t trackId, int32_t fromIndex, int32_t toIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    const int32_t n = static_cast<int32_t>(old->devices.size());
    if (fromIndex < 0 || fromIndex >= n) return false;
    if (toIndex < 0) toIndex = 0;
    if (toIndex >= n) toIndex = n - 1;
    if (toIndex == fromIndex) return true;
    auto nt = cloneTrack(*old);
    auto dev = nt->devices[fromIndex];
    nt->devices.erase(nt->devices.begin() + fromIndex);
    nt->devices.insert(nt->devices.begin() + toIndex, dev);
    republishWithTrack(trackId, nt);
    remapCvLinksAfterDeviceChange(trackId, -1, fromIndex, toIndex);  // keep CV-link targets valid
    return true; // order change doesn't affect total chain latency (PDC unchanged)
}

bool Engine::removeDevice(int32_t trackId, int32_t deviceIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(old->devices.size())) return false;
    // Close the device's plugin editor (if any) so its native window doesn't linger
    // on screen after the device is removed from the chain.
    if (old->devices[deviceIndex]) old->devices[deviceIndex]->closeEditor();
    auto nt = cloneTrack(*old);
    nt->devices.erase(nt->devices.begin() + deviceIndex);
    republishWithTrack(trackId, nt);
    remapCvLinksAfterDeviceChange(trackId, deviceIndex, -1, -1);   // drop/shift CV-link targets
    recomputePdc();
    return true;
}

Device* Engine::deviceAt(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return nullptr;
    return t->devices[deviceIndex].get();
}

// Fresh built-in instances, used to read a parameter's factory default value.
static std::shared_ptr<Device> makeBuiltinDevice(int32_t kind) {
    switch (kind) {
        case 0: return std::make_shared<Eq>();
        case 1: return std::make_shared<Compressor>();
        case 2: return std::make_shared<Reverb>();
        case 3: return std::make_shared<Delay>();
        case 4: return std::make_shared<Utility>();
        case 6: return std::make_shared<Amp>();
        case 7: return std::make_shared<AutoFilter>();
        case 8: return std::make_shared<Vintage>();
        case 9: return std::make_shared<AutoPan>();
        case 10: return std::make_shared<AutoShift>();
        case 11: return std::make_shared<BeatRepeat>();
        case 12: return std::make_shared<Crush>();
        case 13: return std::make_shared<DynamicEq>();
        case 14: return std::make_shared<Ceiling>();
        case 15: return std::make_shared<Strata>();
        case 16: return std::make_shared<Eq3>();
        case 17: return std::make_shared<Forge>();
        case 18: return std::make_shared<AutoGain>();
        case 19: return std::make_shared<Shutter>();
        case 20: return std::make_shared<Chamber>();
        case 21: return std::make_shared<Prism>();
        case 22: return std::make_shared<Lens>();
        default: return nullptr;   // Rack / plugin: no simple per-param default
    }
}
static std::shared_ptr<Instrument> makeBuiltinInstrument(int32_t kind) {
    switch (kind) {
        case 0: return std::make_shared<Synth>();
        case 1: return std::make_shared<Sampler>();      // empty; a sample is loaded later
        case 2: return std::make_shared<PhysicalSynth>();
        case 5: return std::make_shared<WavetableSynth>();
        case 6: return std::make_shared<VoltSynth>();
        case 7: return std::make_shared<BassSynth>();
        case 8: return std::make_shared<PendulumSynth>();
        case 9: return std::make_shared<OperatorSynth>();
        case 10: return std::make_shared<GrainSynth>();  // seeds a procedural default sample
        case 11: return std::make_shared<FluxSynth>();
        case 12: return std::make_shared<RhythmMachine>();
        case 13: return std::make_shared<Monolith>();
        case 14: return std::make_shared<Pentad>();
        case 15: return std::make_shared<Consort>();
        default: return nullptr;                          // Racks (3/4) / unknown: no simple swap
    }
}

// Replace an instrument track's instrument with a fresh built-in of `kind` (browser drag onto
// an existing track). Racks and unknown kinds return false so the caller can add a new track.
bool Engine::setTrackBuiltinInstrument(int32_t trackId, int32_t kind) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    auto inst = makeBuiltinInstrument(kind);
    if (!inst) return false;
    inst->setSampleRate(transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0);
    auto nt = cloneTrack(*old);
    nt->instrument = inst;
    republishWithTrack(trackId, nt);
    recomputePdc();
    return true;
}

// --- MIDI effects (before the instrument) ---------------------------------
static std::shared_ptr<MidiDevice> makeMidiDevice(int32_t kind) {
    switch (kind) {
        case 0: return std::make_shared<Arpeggiator>();
        case 1: return std::make_shared<MidiChord>();
        case 2: return std::make_shared<MidiScale>();
        case 3: return std::make_shared<MidiNoteLength>();
        case 4: return std::make_shared<MidiVelocity>();
        case 5: return std::make_shared<MidiRandom>();
        default: return nullptr;
    }
}

std::shared_ptr<MidiDevice> Engine::cloneMidiDevice(const MidiDevice& src) const {
    std::shared_ptr<MidiDevice> d = makeMidiDevice(src.midiKind());
    if (!d) d = src.clone();
    if (!d) return nullptr;
    d->setSampleRate(transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0, kMaxBlock);
    if (src.midiKind() >= 0) {
        const int32_t pc = src.paramCount();
        for (int32_t p = 0; p < pc; ++p) d->setParam(p, src.getParam(p));
    }
    d->setBypassed(src.bypassed());
    return d;   // live state starts fresh
}

int32_t Engine::addTrackMidiEffect(int32_t trackId, int32_t kind) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    auto md = makeMidiDevice(kind);
    if (!md) return -1;
    md->setSampleRate(transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0, kMaxBlock);
    auto nt = cloneTrack(*old);
    nt->midiEffects.push_back(md);
    const int32_t idx = static_cast<int32_t>(nt->midiEffects.size()) - 1;
    republishWithTrack(trackId, nt);
    return idx;
}

bool Engine::moveMidiEffect(int32_t trackId, int32_t fromIndex, int32_t toIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old) return false;
    const int32_t n = static_cast<int32_t>(old->midiEffects.size());
    if (fromIndex < 0 || fromIndex >= n) return false;
    if (toIndex < 0) toIndex = 0;
    if (toIndex >= n) toIndex = n - 1;
    if (toIndex == fromIndex) return true;
    auto nt = cloneTrack(*old);
    auto md = nt->midiEffects[fromIndex];
    nt->midiEffects.erase(nt->midiEffects.begin() + fromIndex);
    nt->midiEffects.insert(nt->midiEffects.begin() + toIndex, md);
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::removeMidiEffect(int32_t trackId, int32_t index) {
    auto old = findTrackAuthoring(trackId);
    if (!old || index < 0 || index >= static_cast<int32_t>(old->midiEffects.size())) return false;
    auto nt = cloneTrack(*old);
    nt->midiEffects.erase(nt->midiEffects.begin() + index);
    republishWithTrack(trackId, nt);
    if (nt->instrument) nt->instrument->allNotesOff();   // drop any arp-driven voices
    return true;
}

int32_t Engine::trackMidiEffectCount(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->midiEffects.size()) : 0;
}

MidiDevice* Engine::midiDeviceAt(int32_t trackId, int32_t index) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->midiEffects.size())) return nullptr;
    return t->midiEffects[index].get();
}

int32_t     Engine::midiEffectKind(int32_t t, int32_t i) const { auto* m = midiDeviceAt(t, i); return m ? m->midiKind() : -1; }
const char* Engine::midiEffectName(int32_t t, int32_t i) const { auto* m = midiDeviceAt(t, i); return m ? m->displayName() : ""; }
int32_t     Engine::midiEffectParamCount(int32_t t, int32_t i) const { auto* m = midiDeviceAt(t, i); return m ? m->paramCount() : 0; }
const char* Engine::midiEffectParamName(int32_t t, int32_t i, int32_t p) const { auto* m = midiDeviceAt(t, i); return m ? m->paramName(p) : ""; }
float       Engine::midiEffectParamMin(int32_t t, int32_t i, int32_t p) const { auto* m = midiDeviceAt(t, i); return m ? m->paramMin(p) : 0.0f; }
float       Engine::midiEffectParamMax(int32_t t, int32_t i, int32_t p) const { auto* m = midiDeviceAt(t, i); return m ? m->paramMax(p) : 1.0f; }
float       Engine::midiEffectGetParam(int32_t t, int32_t i, int32_t p) const { auto* m = midiDeviceAt(t, i); return m ? m->getParam(p) : 0.0f; }
void        Engine::midiEffectSetParam(int32_t t, int32_t i, int32_t p, float v) { routeCvBaseEdit(2, t, i, p, v); if (auto* m = midiDeviceAt(t, i)) m->setParam(p, v); }
void        Engine::setMidiEffectBypassed(int32_t t, int32_t i, bool b) { if (auto* m = midiDeviceAt(t, i)) m->setBypassed(b); }
bool        Engine::midiEffectBypassed(int32_t t, int32_t i) const { auto* m = midiDeviceAt(t, i); return m ? m->bypassed() : false; }
int32_t     Engine::midiEffectLastIn(int32_t t, int32_t i) const { auto* m = midiDeviceAt(t, i); return m ? m->midiLastIn() : -1; }
int32_t     Engine::midiEffectLastOut(int32_t t, int32_t i) const { auto* m = midiDeviceAt(t, i); return m ? m->midiLastOut() : -1; }
int32_t     Engine::midiEffectScope(int32_t t, int32_t i, float* out, int32_t maxN) const { auto* m = midiDeviceAt(t, i); return m ? m->midiScope(out, maxN) : 0; }
void        Engine::setMidiEffectCcDest(int32_t t, int32_t i, int32_t dev, int32_t param) { if (auto* m = midiDeviceAt(t, i)) m->setCcDest(dev, param); }
void        Engine::setMidiEffectCcDepth(int32_t t, int32_t i, float d) { if (auto* m = midiDeviceAt(t, i)) m->setCcDepth(d); }
int32_t     Engine::midiEffectCcDestDevice(int32_t t, int32_t i) const { auto* m = midiDeviceAt(t, i); return m ? m->ccDestDevice() : -2; }
int32_t     Engine::midiEffectCcDestParam(int32_t t, int32_t i) const { auto* m = midiDeviceAt(t, i); return m ? m->ccDestParam() : -1; }
float       Engine::midiEffectCcDepth(int32_t t, int32_t i) const { auto* m = midiDeviceAt(t, i); return m ? m->ccDepth() : 0.0f; }

// --- factory-default parameter values (double-click reset) -----------------
float Engine::deviceParamDefault(int32_t t, int32_t d, int32_t p) const {
    auto* dev = deviceAt(t, d);
    if (!dev) return 0.0f;
    if (dev->builtinKind() < 0) return dev->getParam(p);          // plugin: keep current
    auto fresh = makeBuiltinDevice(dev->builtinKind());
    return fresh ? fresh->getParam(p) : dev->getParam(p);
}
float Engine::instrumentParamDefault(int32_t t, int32_t p) const {
    auto tr = findTrackAuthoring(t);
    if (!tr || !tr->instrument) return 0.0f;
    if (tr->instrument->kind() < 0) return tr->instrument->pluginParamGet(p);  // plugin
    auto fresh = makeBuiltinInstrument(tr->instrument->kind());
    return fresh ? fresh->pluginParamGet(p) : tr->instrument->pluginParamGet(p);
}
float Engine::midiEffectParamDefault(int32_t t, int32_t i, int32_t p) const {
    auto* m = midiDeviceAt(t, i);
    if (!m) return 0.0f;
    auto fresh = makeMidiDevice(m->midiKind());
    return fresh ? fresh->getParam(p) : m->getParam(p);
}

const char* Engine::deviceName(int32_t trackId, int32_t deviceIndex) const {
    if (deviceIndex < 0) {   // the track's instrument (Synth/Sampler/plugin name)
        auto t = findTrackAuthoring(trackId);
        return (t && t->instrument) ? t->instrument->displayName() : "";
    }
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->displayName() : "";
}
int32_t Engine::deviceParamCount(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->paramCount() : 0;
}
const char* Engine::deviceParamName(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->paramName(paramIndex) : "";
}
float Engine::deviceParamMin(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->paramMin(paramIndex) : 0.0f;
}
float Engine::deviceParamMax(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->paramMax(paramIndex) : 1.0f;
}
float Engine::deviceGetParam(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->getParam(paramIndex) : 0.0f;
}
void Engine::deviceSetParam(int32_t trackId, int32_t deviceIndex, int32_t paramIndex, float value) {
    // A modulated param's UI edit sets the stored base (the atomic is driven by the
    // modulation each block); set the atomic too for immediate feedback.
    routeCvBaseEdit(0, trackId, deviceIndex, paramIndex, value);
    if (auto* d = deviceAt(trackId, deviceIndex)) {
        // A param can change the device's latency (the Compressor's look-ahead): keep PDC in step.
        const int32_t latBefore = d->latencySamples();
        d->setParam(paramIndex, value);
        if (d->latencySamples() != latBefore) recomputePdc();
    }
}
float Engine::deviceGainReduction(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->gainReductionDb() : 0.0f;
}

int32_t Engine::deviceScope(int32_t trackId, int32_t deviceIndex, float* out, int32_t maxSamples) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->scopeRead(out, maxSamples) : 0;
}

void Engine::deviceAction(int32_t trackId, int32_t deviceIndex, int32_t id, int32_t iarg, float farg) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->deviceAction(id, iarg, farg);
}

int32_t Engine::deviceLayerWave(int32_t trackId, int32_t deviceIndex, int32_t layer, float* out, int32_t maxSamples) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->layerWave(layer, out, maxSamples) : 0;
}

int32_t Engine::deviceGetState(int32_t trackId, int32_t deviceIndex, uint8_t* out, int32_t cap) const {
    auto* d = deviceAt(trackId, deviceIndex);
    if (!d) return 0;
    auto s = d->getState();
    const int32_t n = (int32_t)s.size();
    if (out && cap >= n) std::memcpy(out, s.data(), (size_t)n);
    return n;   // returns required size even when out is null / too small (probe then fetch)
}

void Engine::deviceSetState(int32_t trackId, int32_t deviceIndex, const uint8_t* data, int32_t size) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->setState(data, size);
}

bool Engine::deviceLoadFile(int32_t trackId, int32_t deviceIndex, const std::string& path) {
    auto* d = deviceAt(trackId, deviceIndex);
    return d && d->loadFile(path);
}

std::string Engine::deviceText(int32_t trackId, int32_t deviceIndex, int32_t id) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->deviceText(id) : std::string{};
}

void Engine::setDeviceSidechainSource(int32_t trackId, int32_t deviceIndex, int32_t sourceTrackId) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->setSidechainSourceTrackId(sourceTrackId);
    recomputeRouting();
}

int32_t Engine::deviceSidechainSource(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->sidechainSourceTrackId() : -1;
}

bool Engine::deviceAcceptsSidechain(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->acceptsSidechain() : false;
}

void Engine::setDeviceSidechainGain(int32_t trackId, int32_t deviceIndex, float db) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->setSidechainGainDb(db);
}
float Engine::deviceSidechainGain(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->sidechainGainDb() : 0.0f;
}
void Engine::setDeviceSidechainMix(int32_t trackId, int32_t deviceIndex, float mix) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->setSidechainMix(mix);
}
float Engine::deviceSidechainMix(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->sidechainMix() : 1.0f;
}
void Engine::setDeviceSidechainTapPre(int32_t trackId, int32_t deviceIndex, int32_t pre) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->setSidechainTapPre(pre);
}
int32_t Engine::deviceSidechainTapPre(int32_t trackId, int32_t deviceIndex) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->sidechainTapPre() : 0;
}

// Instrument sidechain ("React", Nota Flux): route a listening instrument to a source track.
void Engine::setInstrumentSidechainSource(int32_t trackId, int32_t sourceTrackId) {
    auto t = findTrackAuthoring(trackId);
    if (t && t->instrument) t->instrument->setSidechainSourceTrackId(sourceTrackId);
    recomputeRouting();
}
int32_t Engine::instrumentSidechainSource(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t && t->instrument ? t->instrument->sidechainSourceTrackId() : -1;
}
bool Engine::instrumentAcceptsSidechain(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t && t->instrument ? t->instrument->acceptsSidechain() : false;
}

// Assign a route-bus slot to each distinct sidechain source track referenced by a
// device or a listening instrument. Message thread; scans the authoring graph (device /
// instrument pointers are stable across snapshots, so the table the audio thread reads
// stays valid).
void Engine::recomputeRouting() {
    for (auto& s : routeSlotTrackId_) s.store(-1, std::memory_order_relaxed);
    int32_t next = 0;
    auto claim = [&](int32_t src) {
        if (src < 0 || routeSlotForTrack(src) >= 0) return;   // none / already assigned
        if (next < kMaxRoutes) routeSlotTrackId_[next++].store(src, std::memory_order_relaxed);
    };
    for (auto& t : authoring_->tracks) {
        if (t->instrument) claim(t->instrument->sidechainSourceTrackId());
        for (auto& d : t->devices)
            if (d) claim(d->sidechainSourceTrackId());
    }
}

void Engine::openTrackEditor(int32_t trackId, int32_t deviceIndex) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return;
    if (deviceIndex < 0) {
        if (t->instrument) t->instrument->openEditor();
    } else if (deviceIndex < static_cast<int32_t>(t->devices.size())) {
        if (t->devices[deviceIndex]) t->devices[deviceIndex]->openEditor();
    }
}

void Engine::closeTrackEditor(int32_t trackId, int32_t deviceIndex) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return;
    if (deviceIndex < 0) {
        if (t->instrument) t->instrument->closeEditor();
    } else if (deviceIndex < static_cast<int32_t>(t->devices.size())) {
        if (t->devices[deviceIndex]) t->devices[deviceIndex]->closeEditor();
    }
}

int32_t Engine::getPluginState(int32_t trackId, int32_t deviceIndex, uint8_t* out, int32_t cap) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return 0;
    std::vector<uint8_t> v;
    if (deviceIndex < 0) {
        if (t->instrument) v = t->instrument->getState();
    } else if (deviceIndex < static_cast<int32_t>(t->devices.size())) {
        if (t->devices[deviceIndex]) v = t->devices[deviceIndex]->getState();
    }
    if (out && cap >= static_cast<int32_t>(v.size()))
        std::copy(v.begin(), v.end(), out);
    return static_cast<int32_t>(v.size());
}

bool Engine::setPluginState(int32_t trackId, int32_t deviceIndex, const uint8_t* data, int32_t size) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex < 0) {
        if (!t->instrument) return false;
        t->instrument->setState(data, size);
        return true;
    }
    if (deviceIndex < static_cast<int32_t>(t->devices.size()) && t->devices[deviceIndex]) {
        t->devices[deviceIndex]->setState(data, size);
        return true;
    }
    return false;
}

void Engine::setTrackDeviceBypassed(int32_t trackId, int32_t deviceIndex, bool bypassed) {
    auto t = findTrackAuthoring(trackId);
    if (!t || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return;
    if (t->devices[deviceIndex]) t->devices[deviceIndex]->setBypassed(bypassed);
}

bool Engine::trackDeviceBypassed(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return false;
    return t->devices[deviceIndex] && t->devices[deviceIndex]->bypassed();
}

// --- plugin delay compensation (M3-7) --------------------------------------

int32_t Engine::trackLatency(const Track& t) {
    int32_t lat = t.instrument ? t.instrument->latencySamples() : 0;
    for (const auto& d : t.devices) if (d) lat += d->latencySamples();
    return lat;
}

int32_t Engine::trackLatencySamples(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t ? trackLatency(*t) : 0;
}


// Delay every track by (maxLatency - itsOwnLatency) so all outputs stay aligned.
void Engine::recomputePdc() {
    int32_t maxLat = 0;
    for (auto& t : authoring_->tracks) maxLat = std::max(maxLat, trackLatency(*t));
    for (auto& t : authoring_->tracks)
        if (t->pdc) t->pdc->setDelay(maxLat - trackLatency(*t));
}


} // namespace nota
