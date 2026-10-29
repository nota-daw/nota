// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — hosted plugins & device chain (M3): load instrument/effect, device CRUD/params/bypass/editor/state, clone helpers, kind/id introspection, PDC latency.

#include "Engine.h"
#include "ParamResolve.h"
#include "devices/DeviceFactory.h"
#include "devices/racks/MidiRack.h"
#include "devices/racks/RackInstrument.h"
#include "devices/racks/DrumRack.h"
#include "devices/effects/Amp.h"
#include "devices/effects/AutoFilter.h"
#include "media/AudioFile.h"
#include "devices/effects/Compressor.h"
#include "devices/effects/Delay.h"
#include "devices/effects/Eq.h"
#include "devices/effects/AutoPan.h"
#include "devices/effects/AutoShift.h"
#include "devices/effects/BeatRepeat.h"
#include "devices/effects/Crush.h"
#include "devices/effects/DynamicEq.h"
#include "devices/effects/Eq3.h"
#include "devices/effects/Forge.h"
#include "devices/effects/AutoGain.h"
#include "devices/effects/Shutter.h"
#include "devices/effects/Chamber.h"
#include "devices/effects/Lens.h"
#include "devices/effects/Flanger.h"
#include "devices/effects/Phaser.h"
#include "devices/effects/Chorus.h"
#include "devices/effects/Vocoder.h"
#include "devices/effects/Prism.h"
#include "devices/effects/Ceiling.h"
#include "devices/effects/Strata.h"
#include "devices/effects/Vintage.h"
#include "devices/instruments/PhysicalSynth.h"
#include "devices/instruments/WavetableSynth.h"
#include "devices/instruments/VoltSynth.h"
#include "devices/instruments/BassSynth.h"
#include "devices/instruments/PendulumSynth.h"
#include "devices/instruments/OperatorSynth.h"
#include "devices/instruments/FluxSynth.h"
#include "devices/instruments/RhythmMachine.h"
#include "devices/instruments/Monolith.h"
#include "devices/instruments/Pentad.h"
#include "devices/instruments/Consort.h"
#include "devices/instruments/Keys.h"
#include "devices/instruments/Mosaic.h"
#include "devices/instruments/GrainSynth.h"
#include "bridge/PluginHostBridge.h"
#include "devices/racks/RackDevice.h"
#include "devices/effects/Reverb.h"
#include "devices/instruments/Sampler.h"
#include "devices/instruments/Synth.h"
#include "devices/effects/Utility.h"

#include <algorithm>
#include <cstring>
#include <vector>

namespace nota {

int32_t Engine::trackInstrumentKind(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return -2;
    return t->instrument ? t->instrument->kind() : -2;
}

bool Engine::trackInstrumentSupportsMpe(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t && t->instrument && t->instrument->supportsMpe();
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
    inst->setSampleRate(transport_.sampleRate()); // seeds the procedural default sample
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

int32_t Engine::addKeysTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<Keys>();
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::addMosaicTrack() {
    auto g = std::make_shared<Graph>(*authoring_);
    const int32_t id = nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = std::make_shared<Mosaic>(); // empty — a program is set later
    inst->setSampleRate(transport_.sampleRate());
    track->instrument = inst;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

// Nota Mosaic's program lives in the instrument, which is shared across snapshots; setting
// it is lock-free for the audio thread (Mosaic publishes it itself once loaded).
static Mosaic* mosaicOf(const std::shared_ptr<Track>& t) {
    return t && t->instrument ? dynamic_cast<Mosaic*>(t->instrument.get()) : nullptr;
}
bool Engine::mosaicSetProgram(int32_t trackId, const std::string& text, bool checkpoint) {
    auto old = findTrackAuthoring(trackId);
    auto* m = mosaicOf(old);
    if (!m) return false;
    if (!checkpoint) {
        m->setProgram(text);
        return true;
    } // live edit: in place, voices keep going
    // An undoable edit publishes a fresh Mosaic (params + the samples it already holds, so a
    // zone edit is ready at once) — undo brings the old instrument, with its program, back.
    auto fresh = std::static_pointer_cast<Mosaic>(m->clone());
    fresh->setProgram(text);
    fresh->presetTag = m->presetTag; // still the preset it was loaded from (specs/048)
    auto nt = cloneTrack(*old);
    nt->instrument = fresh;
    republishWithTrack(trackId, nt);
    return true;
}
std::string Engine::mosaicProgram(int32_t trackId) const {
    auto* m = mosaicOf(findTrackAuthoring(trackId));
    return m ? m->programText() : std::string{};
}
bool Engine::mosaicStatus(int32_t trackId, NotaMosaicStatus* out) const {
    auto* m = mosaicOf(findTrackAuthoring(trackId));
    if (!m || !out) return false;
    const auto s = m->status();
    out->state = s.state;
    out->files_done = s.filesDone;
    out->files_total = s.filesTotal;
    out->missing = s.missing;
    out->zones = s.zones;
    out->serial = s.serial;
    out->disk_done = s.diskDone;
    out->disk_total = s.diskTotal;
    out->ram_bytes = s.ram;
    return true;
}
int64_t Engine::mosaicZoneSampleId(int32_t trackId, int32_t zone) const {
    auto* m = mosaicOf(findTrackAuthoring(trackId));
    if (!m) return 0;
    auto b = m->zoneSample(zone);
    return b ? b->id : 0;
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
    auto fresh = std::static_pointer_cast<RhythmMachine>(rh->clone()); // copies params/patterns/samples
    fresh->adoptUid(*rh); // an edit, not a new instrument: voice keys keep pointing here (specs/050)
    fresh->setVoiceSample(voice, sample);
    fresh->presetTag = rh->presetTag; // still the preset it was loaded from (specs/048)
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
    fresh->presetTag = gr->presetTag; // still the preset it was loaded from (specs/048)
    auto nt = cloneTrack(*old);
    nt->instrument = fresh;
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::setTrackGrainRoot(int32_t trackId, int32_t rootNote) {
    auto t = findTrackAuthoring(trackId);
    auto* gr = t && t->instrument ? dynamic_cast<GrainSynth*>(t->instrument.get()) : nullptr;
    if (!gr) return false;
    gr->setRoot(rootNote); // lock-free atomic; the synth is shared across snapshots
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
    auto sampler = std::make_shared<Sampler>(); // empty — a sample is loaded later
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
    fresh->presetTag = sm->presetTag; // still the preset it was loaded from (specs/048)
    auto nt = cloneTrack(*old);
    nt->instrument = fresh;
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::setTrackSamplerRoot(int32_t trackId, int32_t rootNote) {
    auto t = findTrackAuthoring(trackId);
    auto* sm = t && t->instrument ? dynamic_cast<Sampler*>(t->instrument.get()) : nullptr;
    if (!sm) return false;
    sm->setRoot(rootNote); // lock-free atomic; the sampler is shared across snapshots
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
    return replaceTrackInstrument(trackId, std::move(inst));
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
    int64_t len = c.effectiveLength(); // frames in the source region
    if (len <= 0) return 0;
    len = std::min<int64_t>(len, 30000000); // ~11 min @ 44.1k — bound offline memory
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
    if (kind == 0) dev = std::make_shared<Eq>();
    else if (kind == 1)
        dev = std::make_shared<Compressor>();
    else if (kind == 2)
        dev = std::make_shared<Reverb>();
    else if (kind == 3)
        dev = std::make_shared<Delay>();
    else if (kind == 4)
        dev = std::make_shared<Utility>();
    else if (kind == 5)
        dev = std::make_shared<RackDevice>(); // Audio Effect Rack
    else if (kind == 6)
        dev = std::make_shared<Amp>();
    else if (kind == 7)
        dev = std::make_shared<AutoFilter>();
    else if (kind == 8)
        dev = std::make_shared<Vintage>();
    else if (kind == 9)
        dev = std::make_shared<AutoPan>();
    else if (kind == 10)
        dev = std::make_shared<AutoShift>();
    else if (kind == 11)
        dev = std::make_shared<BeatRepeat>();
    else if (kind == 12)
        dev = std::make_shared<Crush>();
    else if (kind == 13)
        dev = std::make_shared<DynamicEq>();
    else if (kind == 14)
        dev = std::make_shared<Ceiling>();
    else if (kind == 15)
        dev = std::make_shared<Strata>();
    else if (kind == 16)
        dev = std::make_shared<Eq3>();
    else if (kind == 17)
        dev = std::make_shared<Forge>();
    else if (kind == 18)
        dev = std::make_shared<AutoGain>();
    else if (kind == 19)
        dev = std::make_shared<Shutter>();
    else if (kind == 20)
        dev = std::make_shared<Chamber>();
    else if (kind == 21)
        dev = std::make_shared<Prism>();
    else if (kind == 22)
        dev = std::make_shared<Lens>();
    else if (kind == 23)
        dev = std::make_shared<Flanger>();
    else if (kind == 24)
        dev = std::make_shared<Phaser>();
    else if (kind == 25)
        dev = std::make_shared<Chorus>();
    else if (kind == 26)
        dev = std::make_shared<Vocoder>();
    else
        return -1;
    dev->setSampleRate(transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0, kMaxBlock);
    if (kind == 5)
        if (auto* rk = dynamic_cast<RackDevice*>(dev.get())) rk->addChain(-1); // one pass-through chain
    auto nt = cloneTrack(*old);
    nt->devices.push_back(dev);
    const int32_t idx = static_cast<int32_t>(nt->devices.size()) - 1;
    republishWithTrack(trackId, nt);
    recomputePdc();
    return idx;
}

std::shared_ptr<Instrument> Engine::cloneInstrument(const Track& src) const {
    return src.instrument ? cloneInstrumentFrom(*src.instrument) : nullptr;
}

std::shared_ptr<Instrument> Engine::cloneInstrumentFrom(const Instrument& src) const {
    auto c = cloneInstrumentBody(src);
    if (c) c->presetTag = src.presetTag; // the preset it was loaded from (specs/048)
    return c;
}

std::shared_ptr<Instrument> Engine::cloneInstrumentBody(const Instrument& src) const {
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    if (auto c = src.clone()) {
        c->setSampleRate(sr);
        c->editorPreview = src.editorPreview; // its window's picture comes along (specs/046)
        return c;
    } // hosted plugin
    switch (src.kind()) { // built-ins
    case 0: {
        auto s = std::make_shared<Synth>();
        s->setSampleRate(sr);
        return s;
    }
    case 1: {
        auto sm = std::make_shared<Sampler>();
        sm->setSampleRate(sr);
        if (auto* srcSm = dynamic_cast<const Sampler*>(&src))
            sm->setSample(srcSm->sample(), srcSm->rootNote(), srcSm->loopEnabled());
        return sm;
    }
    case 2: {
        auto p = std::make_shared<PhysicalSynth>();
        p->setSampleRate(sr);
        return p;
    }
    case 5: {
        auto w = std::make_shared<WavetableSynth>();
        w->setSampleRate(sr);
        return w;
    }
    case 6: {
        auto v = std::make_shared<VoltSynth>();
        v->setSampleRate(sr);
        return v;
    }
    case 7: {
        auto b = std::make_shared<BassSynth>();
        b->setSampleRate(sr);
        return b;
    }
    case 8: {
        auto p = std::make_shared<PendulumSynth>();
        p->setSampleRate(sr);
        return p;
    }
    case 9: {
        auto op = std::make_shared<OperatorSynth>();
        op->setSampleRate(sr);
        return op;
    }
    case 10: {
        auto gr = std::make_shared<GrainSynth>();
        gr->setSampleRate(sr);
        return gr;
    }
    case 11: {
        auto fx = std::make_shared<FluxSynth>();
        fx->setSampleRate(sr);
        return fx;
    }
    case 12: {
        auto rh = std::make_shared<RhythmMachine>();
        rh->setSampleRate(sr);
        return rh;
    }
    case 13: {
        auto mo = std::make_shared<Monolith>();
        mo->setSampleRate(sr);
        return mo;
    }
    case 14: {
        auto pe = std::make_shared<Pentad>();
        pe->setSampleRate(sr);
        return pe;
    }
    case 15: {
        auto co = std::make_shared<Consort>();
        co->setSampleRate(sr);
        return co;
    }
    case 16: {
        auto ke = std::make_shared<Keys>();
        ke->setSampleRate(sr);
        return ke;
    }
    default: return nullptr;
    }
}

std::shared_ptr<Device> Engine::cloneDevice(const Device& src) const {
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    // A built-in starts fresh and takes the source's values below; a rack or hosted plugin
    // copies itself (its whole state). The factory is the same one AddBuiltinDevice's
    // defaults come from, so a new built-in kind can't be missed here (#238).
    std::shared_ptr<Device> d = makeBuiltinDevice(src.builtinKind());
    const bool builtin = d != nullptr;
    if (!d) d = src.clone();
    if (!d) return nullptr;
    d->setSampleRate(sr, kMaxBlock);
    if (builtin) { // copy generic params
        const int32_t pc = src.paramCount();
        for (int32_t p = 0; p < pc; ++p) d->setParam(p, src.getParam(p));
        auto st = src.getState(); // + any state blob (looper PCM)
        if (!st.empty()) d->setState(st.data(), (int32_t)st.size());
    }
    d->setBypassed(src.bypassed());
    d->setFolded(src.folded());
    d->setPresetTag(src.presetTag()); // the preset it was loaded from (specs/048)
    d->setEditorPreview(src.editorPreview()); // shared bytes: the same picture (specs/046)
    d->setSidechainSourceTrackId(src.sidechainSourceTrackId()); // a keyed device keeps its key
    d->setSidechainPart(src.sidechainPart());
    d->setSidechainTapPre(src.sidechainTapPre());
    d->setSidechainGainDb(src.sidechainGainDb());
    d->setSidechainMix(src.sidechainMix());
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
    remapAutomationAfterDeviceChange(*nt, false, -1, fromIndex, toIndex); // lanes follow their device
    republishWithTrack(trackId, nt);
    remapCvLinksAfterDeviceChange(trackId, -1, fromIndex, toIndex); // keep CV-link targets valid
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
    remapAutomationAfterDeviceChange(*nt, false, deviceIndex, -1, -1); // drop/shift its lanes
    republishWithTrack(trackId, nt);
    remapCvLinksAfterDeviceChange(trackId, deviceIndex, -1, -1); // drop/shift CV-link targets
    recomputePdc();
    return true;
}

Device* Engine::deviceAt(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return nullptr;
    return t->devices[deviceIndex].get();
}

// Fresh built-in factories live in devices/DeviceFactory.cpp.

// Replace an instrument track's instrument with a fresh built-in of `kind` (browser drag onto
// an existing track, Replace… in the instrument's menu). 3 and 4 are an empty Instrument Rack /
// Drum Rack (specs/043), as a new rack track starts. Unknown kinds return false so the caller
// can add a new track.
bool Engine::setTrackBuiltinInstrument(int32_t trackId, int32_t kind) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    std::shared_ptr<Instrument> inst;
    if (kind == 3) inst = std::make_shared<RackInstrument>();
    else if (kind == 4) inst = std::make_shared<DrumRack>();
    else inst = makeBuiltinInstrument(kind);
    if (!inst) return false;
    inst->setSampleRate(transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0);
    return replaceTrackInstrument(trackId, std::move(inst));
}

int64_t Engine::trackInstrumentId(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t && t->instrument ? static_cast<int64_t>(t->instrument->uid()) : 0;
}

bool Engine::replaceTrackInstrument(int32_t trackId, std::shared_ptr<Instrument> inst) {
    auto old = findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument || !inst) return false;
    // The old instrument survives in undo history, so only its window goes.
    if (old->instrument) old->instrument->closeEditor();
    auto nt = cloneTrack(*old);
    nt->instrument = std::move(inst);
    republishWithTrack(trackId, nt);
    recomputePdc();
    return true;
}

std::shared_ptr<MidiDevice> Engine::cloneMidiDevice(const MidiDevice& src) const {
    std::shared_ptr<MidiDevice> d = makeMidiDevice(src.midiKind());
    if (!d) d = src.clone();
    if (!d) return nullptr;
    d->setSampleRate(transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0, kMaxBlock);
    if (src.midiKind() >= 0) {
        const int32_t pc = src.paramCount();
        for (int32_t p = 0; p < pc; ++p) d->setParam(p, src.getParam(p));
        auto st = src.getState();
        if (!st.empty()) d->setState(st.data(), (int32_t)st.size());
    }
    d->setCcDest(src.ccDestDevice(), src.ccDestParam()); // the Arp's CC modulation (#238)
    d->setCcDestNested(src.ccDestChainUid(), src.ccDestSlot());
    d->setCcDepth(src.ccDepth());
    d->setBypassed(src.bypassed());
    d->setFolded(src.folded());
    d->setPresetTag(src.presetTag()); // the preset it was loaded from (specs/048)
    return d; // live state starts fresh
}

// ---- device clipboard (#238) ---------------------------------------------
// Copy holds a clone, not the device itself: a cut device can still be pasted, and later
// edits to the source don't reach the copy. Paste inserts another clone, so the clipboard
// can be pasted again; each paste is one undo step.
bool Engine::copyDevice(int32_t trackId, int32_t deviceIndex) {
    Device* src = deviceAt(trackId, deviceIndex);
    auto c = src ? cloneDevice(*src) : nullptr;
    if (!c) return false;
    clipDevice_ = std::move(c);
    clipMidi_.reset();
    clipInstrument_.reset();
    clipMany_.clear();
    return true;
}

bool Engine::copyMidiEffect(int32_t trackId, int32_t index) {
    MidiDevice* src = midiDeviceAt(trackId, index);
    auto c = src ? cloneMidiDevice(*src) : nullptr;
    if (!c) return false;
    clipMidi_ = std::move(c);
    clipDevice_.reset();
    clipInstrument_.reset();
    clipMany_.clear();
    return true;
}

bool Engine::copyInstrument(int32_t trackId) {
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Instrument || !t->instrument) return false;
    auto c = cloneInstrumentFrom(*t->instrument);
    if (!c) return false;
    c->setBypassed(t->instrument->bypassed());
    clipInstrument_ = std::move(c);
    clipDevice_.reset();
    clipMidi_.reset();
    clipMany_.clear();
    return true;
}

int32_t Engine::pasteInstrument(int32_t trackId) {
    if (!clipInstrument_) return -1;
    auto t = findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Instrument) return -2;
    auto c = cloneInstrumentFrom(*clipInstrument_);
    if (!c) return -3;
    c->setBypassed(clipInstrument_->bypassed());
    return replaceTrackInstrument(trackId, std::move(c)) ? 0 : -2;
}

// ---- device transfer between tracks (specs/043) ----------------------------
// Both tracks change in one published graph, so the device never plays on two tracks (or
// none) and one undo puts both back. A move hands the device itself over (a plug-in that won't
// load twice still moves, a Reverb keeps its tail); a copy, or a device whose settings must
// change on the way (a key on the target itself, an Arp's CC target), gets a clone, so the
// object the undo history holds is never edited.
int32_t Engine::transferDevice(int32_t srcId, int32_t family, int32_t index, int32_t dstId, int32_t atIndex,
                               bool keepSource, int32_t* outFlags) {
    if (outFlags) *outFlags = 0;
    if (srcId == dstId || family < 0 || family > 2) return -1;
    auto src = findTrackAuthoring(srcId), dst = findTrackAuthoring(dstId);
    if (!src || !dst) return -1;
    const bool midi = family == 1, inst = family == 2;
    if (inst) {
        if (src->type() != TrackType::Instrument || !src->instrument) return -1;
    } else {
        const auto n = midi ? src->midiEffects.size() : src->devices.size();
        if (index < 0 || index >= static_cast<int32_t>(n)) return -1;
    }
    if ((midi || inst) && dst->type() != TrackType::Instrument) return -2;
    if (inst && !keepSource && srcId == kMasterTrackId) return -1;

    int32_t flags = 0;
    // A link to the moved device stays behind: CV links that target it or read it.
    if (!keepSource && !inst) {
        const auto tk = static_cast<int32_t>(midi ? CvTargetKind::MidiFx : CvTargetKind::Device);
        for (auto& ot : authoring_->tracks)
            for (auto& l : ot->cvLinks) {
                const int32_t tgt = l.targetTrack < 0 ? ot->id() : l.targetTrack;
                if ((tgt == srcId && l.targetKind == tk && l.targetDevice == index)
                    || (!midi && ot->id() == srcId && l.sourceKind == static_cast<int32_t>(CvSourceKind::Param) && l.sourceDevice == index))
                    flags |= kTransferLinksDropped;
            }
    }

    auto ns = (keepSource || inst) ? nullptr : cloneTrack(*src);
    auto nd = cloneTrack(*dst);
    std::vector<TrackLane> lanes;
    if (!keepSource) lanes = deviceLanes(*src, family, index);
    int32_t at = 0;

    if (family == 0) {
        auto& sd = src->devices[index];
        std::shared_ptr<Device> dev = keepSource ? cloneDevice(*sd) : sd;
        if (!dev) return -3;
        if (dev->sidechainSourceTrackId() == dstId) {
            if (dev == sd) dev = cloneDevice(*sd);
            if (!dev) return -3;
            dev->setSidechainSourceTrackId(-1);
            dev->setSidechainPart(0);
            flags |= kTransferSidechainCleared;
        }
        if (ns) {
            ns->devices.erase(ns->devices.begin() + index);
            remapAutomationAfterDeviceChange(*ns, false, index, -1, -1);
            // An Arp's CC lane addresses the chain by index: drop it from the moved device,
            // shift it past the gap.
            for (auto& m : ns->midiEffects) {
                const int32_t d = m->ccDestDevice();
                if (d < index) continue;
                auto c = cloneMidiDevice(*m);
                if (!c) continue;
                if (d == index) {
                    c->setCcDest(-1, -1);
                    flags |= kTransferLinksDropped;
                } else {
                    c->setCcDest(d - 1, m->ccDestParam());
                    c->setCcDestNested(m->ccDestChainUid(), m->ccDestSlot()); // a target in a rack keeps its child
                }
                m = c;
            }
        }
        const int32_t end = static_cast<int32_t>(nd->devices.size());
        at = (atIndex < 0 || atIndex > end) ? end : atIndex;
        nd->devices.insert(nd->devices.begin() + at, dev);
        if (at != end) remapAutomationAfterDeviceChange(*nd, false, -1, end, at);
    } else if (midi) {
        auto& sm = src->midiEffects[index];
        std::shared_ptr<MidiDevice> md = keepSource ? cloneMidiDevice(*sm) : sm;
        if (!md) return -3;
        if (md->ccDestDevice() >= 0) { // its CC target is a device on the old track
            if (md == sm) md = cloneMidiDevice(*sm);
            if (!md) return -3;
            md->setCcDest(-1, -1);
            flags |= kTransferLinksDropped;
        }
        if (ns) {
            ns->midiEffects.erase(ns->midiEffects.begin() + index);
            remapAutomationAfterDeviceChange(*ns, true, index, -1, -1);
        }
        const int32_t end = static_cast<int32_t>(nd->midiEffects.size());
        at = (atIndex < 0 || atIndex > end) ? end : atIndex;
        nd->midiEffects.insert(nd->midiEffects.begin() + at, md);
        if (at != end) remapAutomationAfterDeviceChange(*nd, true, -1, end, at);
    } else {
        std::shared_ptr<Instrument> in = keepSource ? cloneInstrumentFrom(*src->instrument) : src->instrument;
        if (!in) return -3;
        if (keepSource) in->setBypassed(src->instrument->bypassed());
        if (dst->instrument) dst->instrument->closeEditor();
        nd->instrument = in;
    }
    for (auto& l : lanes) {
        if (!inst) l.deviceIndex = l.nested() && family == 1 ? midiHost(at) : at; // a MIDI rack's nested lanes keep its encoding
        // An instrument lane for a parameter the new track already automates takes that lane's
        // place, so there is one lane per parameter (edits and recording find the first one).
        TrackLane* same = nullptr;
        if (inst && !l.nested())
            for (auto& d : nd->automation)
                if (!d.nested() && d.target == AutomationTarget::PluginParam && d.deviceIndex < 0
                    && (l.paramId.empty() ? d.paramId.empty() && d.paramIndex == l.paramIndex : d.paramId == l.paramId)) {
                    same = &d;
                    break;
                }
        if (same) {
            same->paramIndex = l.paramIndex;
            same->points = std::move(l.points);
        } else
            nd->automation.push_back(std::move(l));
    }

    const bool dropSource = inst && !keepSource;
    if (dropSource) { // the track goes with its effects; it survives in undo history
        for (auto& d : src->devices)
            if (d) d->closeEditor();
        flags |= kTransferSourceRemoved;
    }
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *authoring_);
    if (srcId == kMasterTrackId && ns) g->masterTrack = ns;
    if (dstId == kMasterTrackId) g->masterTrack = nd;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
        if (t->id() == srcId) {
            if (dropSource) continue;
            g->tracks.push_back(ns ? ns : t);
        } else
            g->tracks.push_back(t->id() == dstId ? nd : t);
    }
    publish(std::move(g));
    // CV links into either chain follow the indices: links to the moved device are dropped on the
    // old track, the devices after it shift, and on the new track the ones after the insert shift.
    if (!inst) {
        const int32_t last = static_cast<int32_t>(midi ? nd->midiEffects.size() : nd->devices.size()) - 1;
        if (ns) remapCvLinksAfterDeviceChange(srcId, index, -1, -1, midi);
        if (at != last) remapCvLinksAfterDeviceChange(dstId, -1, last, at, midi);
    }
    // Voices belong to the audio thread: ask it to silence them (AR-4). The old track loses the
    // arp-driven notes of a moved MIDI effect; a moved instrument drops the notes it held there.
    auto notesOff = [this](int32_t trackId) {
        Command c;
        c.type = CommandType::TrackNotesOff;
        c.i0 = trackId;
        commands_.push(c);
    };
    if (midi && ns) notesOff(srcId);
    if (inst && !keepSource) notesOff(dstId);
    recomputePdc();
    if (outFlags) *outFlags = flags;
    return inst ? 0 : at;
}

int32_t Engine::deviceClipboardKind() const {
    if (!clipMany_.empty()) return 4;
    return clipDevice_ ? 1 : clipMidi_ ? 2 : clipInstrument_ ? 3 : 0;
}

int32_t Engine::deviceClipboardCount() const {
    if (!clipMany_.empty()) return static_cast<int32_t>(clipMany_.size());
    return clipDevice_ || clipMidi_ || clipInstrument_ ? 1 : 0;
}

// ---- several devices on the clipboard (specs/044) ----------------------------
int32_t Engine::copyDevices(int32_t trackId, const int32_t* midiIdx, int32_t midiN, bool withInstrument,
    const int32_t* fxIdx, int32_t fxN) {
    midiN = midiIdx ? std::max(0, midiN) : 0;
    fxN = fxIdx ? std::max(0, fxN) : 0;
    const int32_t total = midiN + fxN + (withInstrument ? 1 : 0);
    if (total == 0) return -1;
    if (total == 1) { // one device: the single clipboard, as before
        const bool ok = midiN ? copyMidiEffect(trackId, midiIdx[0]) : fxN ? copyDevice(trackId, fxIdx[0]) : copyInstrument(trackId);
        return ok ? 1 : -1;
    }
    auto t = findTrackAuthoring(trackId);
    if (!t) return -1;
    std::vector<int32_t> mi(midiIdx, midiIdx + midiN), fi(fxIdx, fxIdx + fxN);
    std::sort(mi.begin(), mi.end());
    std::sort(fi.begin(), fi.end());
    std::vector<ClipEntry> out;
    for (int32_t i : mi) {
        MidiDevice* m = i >= 0 && i < static_cast<int32_t>(t->midiEffects.size()) ? t->midiEffects[i].get() : nullptr;
        auto c = m ? cloneMidiDevice(*m) : nullptr;
        if (!c) return -1;
        out.push_back({std::move(c), nullptr, nullptr});
    }
    if (withInstrument) {
        if (t->type() != TrackType::Instrument || !t->instrument) return -1;
        auto c = cloneInstrumentFrom(*t->instrument);
        if (!c) return -1;
        c->setBypassed(t->instrument->bypassed());
        out.push_back({nullptr, std::move(c), nullptr});
    }
    for (int32_t i : fi) {
        Device* d = i >= 0 && i < static_cast<int32_t>(t->devices.size()) ? t->devices[i].get() : nullptr;
        auto c = d ? cloneDevice(*d) : nullptr;
        if (!c) return -1;
        out.push_back({nullptr, nullptr, std::move(c)});
    }
    clipMany_ = std::move(out);
    clipDevice_.reset();
    clipMidi_.reset();
    clipInstrument_.reset();
    return total;
}

int32_t Engine::pasteDevices(int32_t trackId, int32_t midiAt, int32_t fxAt, int32_t* outSkipped) {
    if (outSkipped) *outSkipped = 0;
    std::vector<ClipEntry> src = clipMany_;
    if (src.empty()) {
        if (clipMidi_) src.push_back({clipMidi_, nullptr, nullptr});
        else if (clipInstrument_) src.push_back({nullptr, clipInstrument_, nullptr});
        else if (clipDevice_) src.push_back({nullptr, nullptr, clipDevice_});
    }
    if (src.empty()) return -1;
    auto old = findTrackAuthoring(trackId);
    if (!old) return -1;
    const bool instTrack = old->type() == TrackType::Instrument;
    // Clone everything first, so a plug-in that won't load leaves the track untouched.
    std::vector<std::shared_ptr<MidiDevice>> mids;
    std::shared_ptr<Instrument> inst;
    std::vector<std::shared_ptr<Device>> devs;
    int32_t skipped = 0;
    for (auto& e : src) {
        if (e.midi) {
            if (!instTrack) { ++skipped; continue; }
            auto c = cloneMidiDevice(*e.midi);
            if (!c) return -3;
            c->setCcDest(-1, -1); // its CC target named a device on the track it came from
            mids.push_back(std::move(c));
        } else if (e.inst) {
            if (!instTrack) { ++skipped; continue; }
            inst = cloneInstrumentFrom(*e.inst);
            if (!inst) return -3;
            inst->setBypassed(e.inst->bypassed());
        } else if (e.dev) {
            auto c = cloneDevice(*e.dev);
            if (!c) return -3;
            if (c->sidechainSourceTrackId() == trackId) { // no key from itself
                c->setSidechainSourceTrackId(-1);
                c->setSidechainPart(0);
            }
            devs.push_back(std::move(c));
        }
    }
    if (outSkipped) *outSkipped = skipped;
    const int32_t pasted = static_cast<int32_t>(mids.size() + devs.size()) + (inst ? 1 : 0);
    if (pasted == 0) return 0;
    auto nt = cloneTrack(*old);
    const int32_t mEnd = static_cast<int32_t>(nt->midiEffects.size()), fEnd = static_cast<int32_t>(nt->devices.size());
    const int32_t ma = midiAt < 0 || midiAt > mEnd ? mEnd : midiAt, fa = fxAt < 0 || fxAt > fEnd ? fEnd : fxAt;
    const int32_t km = static_cast<int32_t>(mids.size()), kf = static_cast<int32_t>(devs.size());
    // References to the devices after each insert shift; the pasted ones have none yet.
    DeviceRefMap map = [=](const DeviceRef& r) -> DeviceRef {
        if (r.fam == 0 && r.idx >= fa) return {0, r.idx + kf, r.uid, r.slot};
        if (r.fam == 1 && r.idx >= ma) return {1, r.idx + km, r.uid, r.slot};
        return r;
    };
    if (inst) {
        if (old->instrument) old->instrument->closeEditor();
        nt->instrument = inst;
    }
    // The pasted MIDI effects carry no CC target, so the map leaves them alone.
    nt->midiEffects.insert(nt->midiEffects.begin() + ma, mids.begin(), mids.end());
    nt->devices.insert(nt->devices.begin() + fa, devs.begin(), devs.end());
    publishWithRefMap(trackId, nt, map);
    recomputePdc();
    return pasted;
}

bool Engine::deviceClipboardSidechain() const { return clipDevice_ && clipDevice_->sidechainSourceTrackId() >= 0; }

int32_t Engine::pasteDevice(int32_t trackId, int32_t atIndex) {
    return clipDevice_ ? insertDeviceClone(trackId, cloneDevice(*clipDevice_), atIndex) : -1;
}

int32_t Engine::duplicateDevice(int32_t trackId, int32_t deviceIndex) {
    Device* src = deviceAt(trackId, deviceIndex);
    return src ? insertDeviceClone(trackId, cloneDevice(*src), deviceIndex + 1) : -1;
}

int32_t Engine::insertDeviceClone(int32_t trackId, std::shared_ptr<Device> dev, int32_t atIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || !dev) return -1;
    auto nt = cloneTrack(*old);
    const int32_t end = static_cast<int32_t>(nt->devices.size());
    const int32_t at = (atIndex < 0 || atIndex > end) ? end : atIndex;
    nt->devices.insert(nt->devices.begin() + at, dev);
    // An insert is a move from just past the end: the lanes and CV links after it shift up.
    if (at != end) remapAutomationAfterDeviceChange(*nt, false, -1, end, at);
    republishWithTrack(trackId, nt);
    if (at != end) remapCvLinksAfterDeviceChange(trackId, -1, end, at);
    recomputePdc();
    return at;
}

int32_t Engine::pasteMidiEffect(int32_t trackId, int32_t atIndex) {
    return clipMidi_ ? insertMidiEffectClone(trackId, cloneMidiDevice(*clipMidi_), atIndex) : -1;
}

int32_t Engine::duplicateMidiEffect(int32_t trackId, int32_t index) {
    MidiDevice* src = midiDeviceAt(trackId, index);
    return src ? insertMidiEffectClone(trackId, cloneMidiDevice(*src), index + 1) : -1;
}

int32_t Engine::insertMidiEffectClone(int32_t trackId, std::shared_ptr<MidiDevice> md, int32_t atIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || !md || old->type() != TrackType::Instrument) return -1;
    auto nt = cloneTrack(*old);
    const int32_t end = static_cast<int32_t>(nt->midiEffects.size());
    const int32_t at = (atIndex < 0 || atIndex > end) ? end : atIndex;
    nt->midiEffects.insert(nt->midiEffects.begin() + at, md);
    if (at != end) remapAutomationAfterDeviceChange(*nt, true, -1, end, at);
    republishWithTrack(trackId, nt);
    return at;
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
    remapAutomationAfterDeviceChange(*nt, true, -1, fromIndex, toIndex);
    republishWithTrack(trackId, nt);
    return true;
}

bool Engine::removeMidiEffect(int32_t trackId, int32_t index) {
    auto old = findTrackAuthoring(trackId);
    if (!old || index < 0 || index >= static_cast<int32_t>(old->midiEffects.size())) return false;
    auto nt = cloneTrack(*old);
    nt->midiEffects.erase(nt->midiEffects.begin() + index);
    remapAutomationAfterDeviceChange(*nt, true, index, -1, -1);
    republishWithTrack(trackId, nt);
    if (nt->instrument) nt->instrument->allNotesOff(); // drop any arp-driven voices
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

int32_t Engine::midiEffectKind(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->midiKind() : -1;
}
const char* Engine::midiEffectName(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->displayName() : "";
}
int32_t Engine::midiEffectParamCount(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->paramCount() : 0;
}
const char* Engine::midiEffectParamName(int32_t t, int32_t i, int32_t p) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->paramName(p) : "";
}
float Engine::midiEffectParamMin(int32_t t, int32_t i, int32_t p) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->paramMin(p) : 0.0f;
}
float Engine::midiEffectParamMax(int32_t t, int32_t i, int32_t p) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->paramMax(p) : 1.0f;
}
float Engine::midiEffectGetParam(int32_t t, int32_t i, int32_t p) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->getParam(p) : 0.0f;
}
void Engine::midiEffectSetParam(int32_t t, int32_t i, int32_t p, float v) {
    routeCvBaseEdit(2, t, i, p, v);
    if (auto* m = midiDeviceAt(t, i)) m->setParam(p, v);
}
std::vector<uint8_t> Engine::midiEffectGetState(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->getState() : std::vector<uint8_t>{};
}
bool Engine::midiEffectSetState(int32_t t, int32_t i, const uint8_t* data, int32_t size, bool checkpoint) {
    auto old = findTrackAuthoring(t);
    if (!old || !data || size <= 0 || i < 0 || i >= static_cast<int32_t>(old->midiEffects.size()) || !old->midiEffects[i])
        return false;
    auto fresh = cloneMidiDevice(*old->midiEffects[i]);
    if (!fresh) return false;
    fresh->setState(data, size);
    // A MIDI Effect Rack's copy plays on with the held-note tables, so it ends what the old one
    // started instead of leaving it hanging.
    if (auto* to = dynamic_cast<MidiRack*>(fresh.get()))
        if (auto* from = dynamic_cast<const MidiRack*>(old->midiEffects[i].get())) to->shareRuntimeWith(*from);
    auto nt = cloneTrack(*old);
    nt->midiEffects[i] = std::move(fresh);
    if (checkpoint) republishWithTrack(t, nt);
    else republishWithTrackRaw(t, nt);
    return true;
}
void Engine::setMidiEffectBypassed(int32_t t, int32_t i, bool b) {
    if (auto* m = midiDeviceAt(t, i)) m->setBypassed(b);
}
bool Engine::midiEffectBypassed(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->bypassed() : false;
}
void Engine::setMidiEffectFolded(int32_t t, int32_t i, bool f) {
    if (auto* m = midiDeviceAt(t, i)) m->setFolded(f);
}
bool Engine::midiEffectFolded(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->folded() : false;
}
int32_t Engine::midiEffectLastIn(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->midiLastIn() : -1;
}
int32_t Engine::midiEffectLastOut(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->midiLastOut() : -1;
}
int32_t Engine::midiEffectScope(int32_t t, int32_t i, float* out, int32_t maxN) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->midiScope(out, maxN) : 0;
}
void Engine::midiEffectCommand(int32_t t, int32_t i, int32_t cmd) {
    if (auto* m = midiDeviceAt(t, i)) m->midiCommand(cmd);
}
void Engine::setMidiEffectCcDest(int32_t t, int32_t i, int32_t dev, int32_t param) {
    auto* m = midiDeviceAt(t, i);
    if (!m) return;
    m->setCcDest(dev, param);
    republishCcRoutes(t);
}

// The audio thread plays the routes of the published track (Track::ccRoutes): after a live CC
// target change, publish the track again so the route follows (no undo step, as before).
void Engine::republishCcRoutes(int32_t trackId) {
    // The first target ever set turns route syncing on. Until then no Arp has a target, so the
    // tracks skipped meanwhile have no route to lose (their empty lists stay as they are).
    ccDestsSet_ = true;
    if (auto old = findTrackAuthoring(trackId)) republishWithTrackRaw(trackId, cloneTrack(*old));
}
// The Arp's CC target inside a rack (specs/044), for save / load: (chain index, slot) in the
// rack at its ccDestDevice; set it after setMidiEffectCcDest.
bool Engine::midiEffectCcDestPath(int32_t t, int32_t i, int32_t* chainIndex, int32_t* slot) const {
    auto* m = midiDeviceAt(t, i);
    if (!m) return false;
    const uint64_t uid = m->ccDestChainUid();
    if (chainIndex) *chainIndex = uid ? nestedChainIndex(t, m->ccDestDevice(), uid) : -1;
    if (slot) *slot = uid ? m->ccDestSlot() : -1;
    return true;
}
bool Engine::setMidiEffectCcDestPath(int32_t t, int32_t i, int32_t chainIndex, int32_t slot) {
    auto* m = midiDeviceAt(t, i);
    if (!m) return false;
    const uint64_t uid = nestedChainUid(t, m->ccDestDevice(), chainIndex);
    if (!uid) return false;
    m->setCcDestNested(uid, slot);
    republishCcRoutes(t);
    return true;
}
void Engine::setMidiEffectCcDepth(int32_t t, int32_t i, float d) {
    if (auto* m = midiDeviceAt(t, i)) m->setCcDepth(d);
}
int32_t Engine::midiEffectCcDestDevice(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->ccDestDevice() : -2;
}
int32_t Engine::midiEffectCcDestParam(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->ccDestParam() : -1;
}
float Engine::midiEffectCcDepth(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->ccDepth() : 0.0f;
}

// --- factory-default parameter values (double-click reset) -----------------
float Engine::deviceParamDefault(int32_t t, int32_t d, int32_t p) const {
    auto* dev = deviceAt(t, d);
    if (!dev) return 0.0f;
    if (dev->builtinKind() < 0) return dev->getParam(p); // plugin: keep current
    auto fresh = makeBuiltinDevice(dev->builtinKind());
    return fresh ? fresh->getParam(p) : dev->getParam(p);
}
// Default of a built-in effect KIND (no instance needed) — for rack chain devices, which
// have no track/device address of their own.
float Engine::builtinDeviceParamDefault(int32_t kind, int32_t p) const {
    auto fresh = makeBuiltinDevice(kind);
    return fresh ? fresh->getParam(p) : 0.0f;
}
float Engine::instrumentParamDefault(int32_t t, int32_t p) const {
    auto tr = findTrackAuthoring(t);
    if (!tr || !tr->instrument) return 0.0f;
    if (tr->instrument->kind() < 0) return tr->instrument->pluginParamGet(p); // plugin
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
    if (deviceIndex < 0) { // the track's instrument (Synth/Sampler/plugin name)
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

int32_t Engine::deviceLayerWave(
    int32_t trackId, int32_t deviceIndex, int32_t layer, float* out, int32_t maxSamples) const {
    auto* d = deviceAt(trackId, deviceIndex);
    return d ? d->layerWave(layer, out, maxSamples) : 0;
}

int32_t Engine::deviceGetState(int32_t trackId, int32_t deviceIndex, uint8_t* out, int32_t cap) const {
    auto* d = deviceAt(trackId, deviceIndex);
    if (!d) return 0;
    auto s = d->getState();
    const int32_t n = (int32_t)s.size();
    if (out && cap >= n) std::memcpy(out, s.data(), (size_t)n);
    return n; // returns required size even when out is null / too small (probe then fetch)
}

void Engine::deviceSetState(int32_t trackId, int32_t deviceIndex, const uint8_t* data, int32_t size) {
    if (auto* d = deviceAt(trackId, deviceIndex)) d->setState(data, size);
}

bool Engine::setEqBandText(
    int32_t trackId, int32_t deviceIndex, int32_t band, const std::string& label, const std::string& note) {
    auto* eq = dynamic_cast<Eq*>(deviceAt(trackId, deviceIndex));
    if (!eq || band < 0 || band >= Eq::kBands) return false;
    auto before = eq->getState();
    eq->setBandText(band, label, note);
    auto after = eq->getState();
    if (after == before) return false;
    // One mix-only undo step that swaps the device's annotation state (specs/049).
    noteStates_.push_back(std::move(before));
    noteStates_.push_back(std::move(after));
    const auto at = static_cast<float>(noteStates_.size() - 2);
    undoStack_.push_back({nullptr, {MixChange{MixKind::DeviceNotes, trackId, at, at + 1.0f, deviceIndex, -1}}});
    if (undoStack_.size() > kMaxUndoDepth) {
        undoStack_.erase(undoStack_.begin());
        if (undoGroupDepth_ > 0 && undoGroupBase_ > 0) --undoGroupBase_;
    }
    redoStack_.clear();
    return true;
}

std::string Engine::eqBandText(int32_t trackId, int32_t deviceIndex, int32_t band, int32_t which) const {
    auto* eq = dynamic_cast<Eq*>(deviceAt(trackId, deviceIndex));
    if (!eq || band < 0 || band >= Eq::kBands) return {};
    return which == 1 ? eq->bandNote(band) : eq->bandLabel(band);
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
    if (auto* d = deviceAt(trackId, deviceIndex)) {
        d->setSidechainSourceTrackId(sourceTrackId);
        d->setSidechainPart(0); // a whole-track key
    }
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
    if (t && t->instrument) {
        t->instrument->setSidechainSourceTrackId(sourceTrackId);
        t->instrument->setSidechainPart(0); // a whole-track key
    }
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
    // Slots at and past the count are always free, so only the used ones need clearing.
    const int32_t used = routeSlotCount_.load(std::memory_order_relaxed);
    routeSlotCount_.store(0, std::memory_order_release);
    for (int32_t s = 0; s < used; ++s) routeSlotTrackId_[s].store(-1, std::memory_order_relaxed);
    int32_t next = 0;
    // A source that is not in the graph (its track was deleted; undo brings it back) gets no
    // slot, so its consumers see no key at all rather than a silent one. A part key whose part
    // is gone (the pad deleted, the rack replaced; specs/050) likewise.
    auto exists = [&](int32_t id, uint64_t part) {
        for (auto& t : authoring_->tracks)
            if (t->id() == id) return part == 0 || (t->instrument && t->instrument->partIndex(part) >= 0);
        return false;
    };
    auto claim = [&](int32_t src, uint64_t part) {
        if (src < 0 || routeSlotFor(src, part) >= 0 || !exists(src, part)) return; // none / already assigned / gone
        if (next < kMaxRoutes) {
            routeSlotPart_[next].store(part, std::memory_order_relaxed);
            routeSlotTrackId_[next++].store(src, std::memory_order_relaxed);
            routeSlotCount_.store(next, std::memory_order_release); // claim() looks up as it goes
        }
    };
    forEachRouteKey([&](int32_t src, uint64_t part) { claim(src, part); });
}

// Every route a consumer asks for, in claim order: track instruments and devices (and keyed
// children inside their racks, specs/044), then a monitoring audio track's input source.
template <typename Fn>
void Engine::forEachRouteKey(Fn&& fn) const {
    for (auto& t : authoring_->tracks) {
        auto children = [&](const RackCore& r) {
            r.forEachChildDevice([&](Device& c) { fn(c.sidechainSourceTrackId(), c.sidechainPart()); });
            r.forEachChildInstrument([&](Instrument& i) { fn(i.sidechainSourceTrackId(), i.sidechainPart()); });
        };
        if (t->instrument) {
            fn(t->instrument->sidechainSourceTrackId(), t->instrument->sidechainPart());
            if (auto* r = t->instrument->asRack()) children(*r);
        }
        for (auto& d : t->devices)
            if (d) {
                fn(d->sidechainSourceTrackId(), d->sidechainPart());
                if (auto* r = d->asRack()) children(*r);
            }
        // A monitoring audio track hears its source track's post-fader tap.
        if (t->type() == TrackType::Audio && t->monitor() && t->recordInputSource() > 0) fn(t->recordInputSource(), uint64_t{0});
    }
}

// ---- Sidechain sub-sources (specs/050) -------------------------------------------------

int32_t Engine::trackPartCount(int32_t trackId) const {
    auto t = findTrackAuthoring(trackId);
    return t && t->instrument ? t->instrument->partCount() : 0;
}

std::string Engine::trackPartName(int32_t trackId, int32_t index) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->instrument || index < 0 || index >= t->instrument->partCount()) return {};
    return t->instrument->partName(index);
}

int32_t Engine::partIndexOf(int32_t sourceTrackId, uint64_t part) const {
    if (part == 0) return -1;
    auto t = findTrackAuthoring(sourceTrackId);
    const int32_t i = t && t->instrument ? t->instrument->partIndex(part) : -1;
    return i >= 0 ? i : -2;
}

bool Engine::partIdAt(int32_t sourceTrackId, int32_t index, uint64_t* part) const {
    if (index == -1) {
        *part = 0;
        return true;
    }
    auto t = findTrackAuthoring(sourceTrackId);
    if (!t || !t->instrument || index < 0 || index >= t->instrument->partCount()) return false;
    *part = t->instrument->partId(index);
    return *part != 0;
}

// Set a key, then keep it only if it got a route and took none from another listener: a full
// pool leaves the previous key in place. Routes are claimed in track and chain order, so a new
// source on an early device could otherwise push out a later device's. Re-deriving the table
// (rather than counting free slots) lets a consumer move to another source while the pool is
// full when it was its old source's only listener.
template <typename Consumer>
int32_t Engine::applySidechainKey(Consumer& c, int32_t src, uint64_t part, bool routed) {
    if (!routed) { // the master's devices are never fed a key (their key is only kept): no route to check
        c.setSidechainSourceTrackId(src);
        c.setSidechainPart(part);
        recomputeRouting();
        return 0;
    }
    const int32_t oldSrc = c.sidechainSourceTrackId();
    const uint64_t oldPart = c.sidechainPart();
    std::pair<int32_t, uint64_t> before[kMaxRoutes];
    const int32_t nBefore = routeSlotCount_.load(std::memory_order_relaxed);
    for (int32_t s = 0; s < nBefore; ++s)
        before[s] = {routeSlotTrackId_[s].load(std::memory_order_relaxed), routeSlotPart_[s].load(std::memory_order_relaxed)};
    c.setSidechainSourceTrackId(src);
    c.setSidechainPart(part);
    recomputeRouting();
    bool kept = routeSlotFor(src, part) >= 0;
    bool oldHeld = false; // does anyone still listen to the old key?
    forEachRouteKey([&](int32_t t, uint64_t p) { oldHeld = oldHeld || (t == oldSrc && p == oldPart); });
    for (int32_t s = 0; kept && s < nBefore; ++s) {
        const auto [t, p] = before[s];
        // The old key's route may go when this was its only listener; every other must stay.
        if (routeSlotFor(t, p) < 0 && (oldHeld || !(t == oldSrc && p == oldPart))) kept = false;
    }
    if (kept) return 0;
    c.setSidechainSourceTrackId(oldSrc);
    c.setSidechainPart(oldPart);
    recomputeRouting();
    return -2;
}

// Rack children (Engine_Rack.cpp) set their keys through the same check.
template int32_t Engine::applySidechainKey<Device>(Device&, int32_t, uint64_t, bool);
template int32_t Engine::applySidechainKey<Instrument>(Instrument&, int32_t, uint64_t, bool);

int32_t Engine::setDeviceSidechainKey(int32_t trackId, int32_t deviceIndex, int32_t sourceTrackId, int32_t part) {
    auto* d = deviceAt(trackId, deviceIndex);
    if (!d) return -1;
    if (sourceTrackId < 0) {
        setDeviceSidechainSource(trackId, deviceIndex, -1);
        return 0;
    }
    // A MIDI key (Nota Auto Shift) follows the source track's notes; a pad has none of its own.
    if (part >= 0 && d->wantsMidiKey()) return -1;
    uint64_t id = 0;
    if (!findTrackAuthoring(sourceTrackId) || !partIdAt(sourceTrackId, part, &id)) return -1;
    return applySidechainKey(*d, sourceTrackId, id, trackId != kMasterTrackId);
}

bool Engine::deviceSidechainKey(int32_t trackId, int32_t deviceIndex, int32_t* source, int32_t* part) const {
    auto* d = deviceAt(trackId, deviceIndex);
    if (!d) return false;
    const int32_t src = d->sidechainSourceTrackId();
    if (source) *source = src;
    if (part) *part = src < 0 ? -1 : partIndexOf(src, d->sidechainPart());
    return true;
}

int32_t Engine::setInstrumentSidechainKey(int32_t trackId, int32_t sourceTrackId, int32_t part) {
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->instrument) return -1;
    if (sourceTrackId < 0) {
        setInstrumentSidechainSource(trackId, -1);
        return 0;
    }
    uint64_t id = 0;
    if (!findTrackAuthoring(sourceTrackId) || !partIdAt(sourceTrackId, part, &id)) return -1;
    return applySidechainKey(*t->instrument, sourceTrackId, id);
}

bool Engine::instrumentSidechainKey(int32_t trackId, int32_t* source, int32_t* part) const {
    auto t = findTrackAuthoring(trackId);
    if (!t || !t->instrument) return false;
    const int32_t src = t->instrument->sidechainSourceTrackId();
    if (source) *source = src;
    if (part) *part = src < 0 ? -1 : partIndexOf(src, t->instrument->sidechainPart());
    return true;
}

int32_t Engine::sidechainRoutesFree() const {
    int32_t n = 0;
    for (auto& s : routeSlotTrackId_)
        if (s.load(std::memory_order_relaxed) < 0) ++n;
    return n;
}

bool Engine::sidechainRouteInUse(int32_t sourceTrackId, int32_t part) const {
    uint64_t id = 0;
    return partIdAt(sourceTrackId, part, &id) && routeSlotFor(sourceTrackId, id) >= 0;
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

bool Engine::trackEditorOpen(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex < 0) return t->instrument && t->instrument->editorOpen();
    if (deviceIndex < static_cast<int32_t>(t->devices.size()) && t->devices[deviceIndex])
        return t->devices[deviceIndex]->editorOpen();
    return false;
}

EditorPreview Engine::trackEditorPreview(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return {};
    if (deviceIndex < 0) return t->instrument ? t->instrument->editorPreview : EditorPreview{};
    if (deviceIndex < static_cast<int32_t>(t->devices.size()) && t->devices[deviceIndex])
        return t->devices[deviceIndex]->editorPreview();
    return {};
}

bool Engine::setTrackEditorPreview(int32_t trackId, int32_t deviceIndex, EditorPreview p) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex < 0) {
        if (!t->instrument) return false;
        t->instrument->editorPreview = std::move(p);
        return true;
    }
    if (deviceIndex >= static_cast<int32_t>(t->devices.size()) || !t->devices[deviceIndex]) return false;
    t->devices[deviceIndex]->setEditorPreview(std::move(p));
    return true;
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
    if (out && cap >= static_cast<int32_t>(v.size())) std::copy(v.begin(), v.end(), out);
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
    if (!t) return;
    if (deviceIndex == -1) {
        if (t->instrument) t->instrument->setBypassed(bypassed);
        return;
    } // the instrument (#148)
    if (deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return;
    if (t->devices[deviceIndex]) t->devices[deviceIndex]->setBypassed(bypassed);
}

bool Engine::trackDeviceBypassed(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex == -1) return t->instrument && t->instrument->bypassed();
    if (deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return false;
    return t->devices[deviceIndex] && t->devices[deviceIndex]->bypassed();
}

void Engine::setTrackDeviceFolded(int32_t trackId, int32_t deviceIndex, bool folded) {
    auto t = findTrackAuthoring(trackId);
    if (!t) return;
    if (deviceIndex < 0) {
        if (t->instrument) t->instrument->folded = folded;
        return;
    }
    if (deviceIndex < static_cast<int32_t>(t->devices.size()) && t->devices[deviceIndex])
        t->devices[deviceIndex]->setFolded(folded);
}

bool Engine::trackDeviceFolded(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return false;
    if (deviceIndex < 0) return t->instrument && t->instrument->folded;
    return deviceIndex < static_cast<int32_t>(t->devices.size()) && t->devices[deviceIndex] &&
           t->devices[deviceIndex]->folded();
}

// --- plugin delay compensation (M3-7) --------------------------------------

int32_t Engine::trackLatency(const Track& t) {
    int32_t lat = t.instrument ? t.instrument->latencySamples() : 0;
    for (const auto& d : t.devices)
        if (d) lat += d->latencySamples();
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
