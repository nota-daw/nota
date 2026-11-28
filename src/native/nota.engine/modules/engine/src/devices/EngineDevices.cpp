// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DeviceOps — instrument/effect chain CRUD, Aurora/Mosaic/Grain/Sampler, cloning.

#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "devices/DeviceOps.h"
#include "model/EngineTypes.h"
#include "model/Graph.h"
#include "model/Track.h"
#include "nota/devices/DeviceIds.h"
#include "nota/devices/racks/RackDevice.h"
#include "nota/dsp/DeviceRegistry.h"
#include "nota/dsp/capabilities/IMosaicInstrument.h"
#include "nota/dsp/capabilities/IRhythmInstrument.h"
#include "nota/dsp/capabilities/ISampleInstrument.h"
#include "nota/dsp/capabilities/IWavetableInstrument.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/pluginhost/PluginHostModule.h"
#include "render/HybridBufferController.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

int32_t DeviceOps::trackInstrumentKind(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return -2;
    return t->instrument ? t->instrument->kind() : -2;
}

bool DeviceOps::trackInstrumentSupportsMpe(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t && t->instrument && t->instrument->supportsMpe();
}

int32_t DeviceOps::trackDeviceBuiltinKind(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return -1;
    return t->devices[static_cast<size_t>(deviceIndex)]->builtinKind();
}

// The device's lineage: kept by the copies an edit swaps in, by a move, and by undo (which puts
// the same objects back).
int64_t DeviceOps::trackDeviceId(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size()) ||
        !t->devices[static_cast<size_t>(deviceIndex)])
        return 0;
    return static_cast<int64_t>(t->devices[static_cast<size_t>(deviceIndex)]->lineage());
}

std::string DeviceOps::trackInstrumentPluginId(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return (t && t->instrument) ? t->instrument->pluginIdentifier() : std::string{};
}

std::string DeviceOps::trackDevicePluginId(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return {};
    return t->devices[static_cast<size_t>(deviceIndex)]->pluginIdentifier();
}

// --- structural edits: instruments & MIDI (M2) -----------------------------
static int32_t addBuiltinTrackOfKind(std::shared_ptr<Graph>& authoring, int32_t& nextTrackId, double sr,
    devices::InstrumentKind kind, const std::function<void(std::shared_ptr<Graph>)>& publishFn) {
    auto g = std::make_shared<Graph>(*authoring);
    const int32_t id = nextTrackId++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = dsp::DeviceRegistry::createInstrument(static_cast<int32_t>(kind));
    if (!inst) return 0;
    inst->setSampleRate(sr);
    track->instrument = std::move(inst);
    g->tracks.push_back(track);
    publishFn(g);
    return id;
}

int32_t DeviceOps::addInstrumentTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::Synth, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addPhysicalSynthTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::PhysicalSynth, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addWavetableSynthTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::WavetableSynth, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addVoltSynthTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::VoltSynth, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addBassSynthTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::BassSynth, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addPendulumSynthTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::PendulumSynth, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addOperatorSynthTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::OperatorSynth, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addGrainSynthTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::GrainSynth, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addFluxSynthTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::FluxSynth, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addRhythmSynthTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::RhythmMachine, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addMonolithTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::Monolith, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addPentadTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::Pentad, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addConsortTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::Consort, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addKeysTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::Keys, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addTwinTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::Twin, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addMosaicTrack() {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        devices::InstrumentKind::Mosaic, [this](auto g) { rt_.publish(std::move(g)); });
}
int32_t DeviceOps::addBuiltinTrack(int32_t kind) {
    return addBuiltinTrackOfKind(rt_.authoring_, rt_.nextTrackId_, rt_.transport_.sampleRate(),
        static_cast<devices::InstrumentKind>(kind), [this](auto g) { rt_.publish(std::move(g)); });
}

// Nota Mosaic's program lives in the instrument, which is shared across snapshots; setting
// it is lock-free for the audio thread (Mosaic publishes it itself once loaded).
static dsp::capabilities::IMosaicInstrument* mosaicOf(const std::shared_ptr<Track>& t) {
    return t && t->instrument ? dynamic_cast<dsp::capabilities::IMosaicInstrument*>(t->instrument.get()) : nullptr;
}
bool DeviceOps::mosaicSetProgram(int32_t trackId, const std::string& text, bool checkpoint) {
    auto old = rt_.findTrackAuthoring(trackId);
    auto* m = mosaicOf(old);
    if (!m) return false;
    if (!checkpoint) {
        m->setProgram(text);
        return true;
    } // live edit: in place, voices keep going
    // An undoable edit publishes a fresh Mosaic (params + the samples it already holds, so a
    // zone edit is ready at once) — undo brings the old instrument, with its program, back.
    auto fresh = old->instrument->clone();
    auto* freshMosaic = dynamic_cast<dsp::capabilities::IMosaicInstrument*>(fresh.get());
    if (!fresh || !freshMosaic) return false;
    freshMosaic->setProgram(text);
    fresh->presetTag = old->instrument->presetTag; // still the preset it was loaded from (specs/048)
    auto nt = rt_.cloneTrack(*old);
    nt->instrument = std::move(fresh);
    rt_.republishWithTrack(trackId, nt);
    return true;
}
std::string DeviceOps::mosaicProgram(int32_t trackId) const {
    auto* m = mosaicOf(rt_.findTrackAuthoring(trackId));
    return m ? m->programText() : std::string{};
}
bool DeviceOps::mosaicStatus(int32_t trackId, MosaicStatus* out) const {
    auto* m = mosaicOf(rt_.findTrackAuthoring(trackId));
    if (!m || !out) return false;
    const auto s = m->mosaicStatus();
    out->state = s.state;
    out->filesDone = s.filesDone;
    out->filesTotal = s.filesTotal;
    out->missing = s.missing;
    out->zones = s.zones;
    out->serial = s.serial;
    out->diskDone = s.diskDone;
    out->diskTotal = s.diskTotal;
    out->ramBytes = s.ram;
    return true;
}
int64_t DeviceOps::mosaicZoneSampleId(int32_t trackId, int32_t zone) const {
    auto* m = mosaicOf(rt_.findTrackAuthoring(trackId));
    if (!m) return 0;
    auto b = m->zoneSample(zone);
    return b ? b->id : 0;
}

// UI editing channel for the track's instrument (e.g. Nota Rhythm step patterns). The
// instrument object is stable across snapshots, so this mutates it in place (message thread).
void DeviceOps::instrumentAction(int32_t trackId, int32_t id, int32_t iarg, float farg) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument) return;
    instrumentActionUndoable(*t->instrument, trackId, -1, -1, id, iarg, farg);
}

// The instrument an edit-state undo step addresses: chain >= 0 is chain `chain` of the rack at
// deviceIndex (MixChange's deviceIndex / paramIndex), -1 the track's own instrument.
Instrument* DeviceOps::editStateInstrument(int32_t trackId, int32_t deviceIndex, int32_t chain) const {
    if (chain >= 0) {
        auto* r = rt_.racks().coreAt(trackId, deviceIndex);
        return r ? r->chainInstrument(chain) : nullptr;
    }
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? t->instrument.get() : nullptr;
}

// A change of an instrument's edit state is one mix-only undo step swapping it (as setEqBandText
// does); nothing when it didn't change, or the instrument keeps none.
void DeviceOps::pushInstrumentEdit(int32_t trackId, int32_t deviceIndex, int32_t chain, std::vector<uint8_t> before,
    std::vector<uint8_t> after, MixKind kind) {
    if (before.empty() || after == before) return;
    rt_.redoStack_.clear();
    // Inside an open undo group (a whole pattern written step by step), the edits of one
    // instrument fold into the group's entry for it: the first `before`, the latest `after`.
    // So a long run of actions is one entry and can't push the group's start out of the history.
    if (rt_.undoGroupDepth_ > 0 && rt_.undoStack_.size() > rt_.undoGroupBase_) {
        for (auto it = rt_.undoStack_.begin() + static_cast<std::ptrdiff_t>(rt_.undoGroupBase_);
            it != rt_.undoStack_.end(); ++it)
            for (auto& m : it->mix)
                if (m.kind == kind && m.trackId == trackId && m.deviceIndex == deviceIndex && m.paramIndex == chain) {
                    rt_.noteStates_[static_cast<size_t>(m.after)] = std::move(after);
                    return;
                }
    }
    rt_.noteStates_.push_back(std::move(before));
    rt_.noteStates_.push_back(std::move(after));
    const auto at = static_cast<float>(rt_.noteStates_.size() - 2);
    rt_.undoStack_.push_back({nullptr, {MixChange{kind, trackId, at, at + 1.0f, deviceIndex, chain}}});
    if (rt_.undoStack_.size() > EngineRuntime::kMaxUndoDepth) {
        rt_.undoStack_.erase(rt_.undoStack_.begin());
        if (rt_.undoGroupDepth_ > 0 && rt_.undoGroupBase_ > 0) --rt_.undoGroupBase_;
    }
    compactNoteStates();
}

// The saved states (EQ-8 notes, instrument edit states) only the undo and redo history refers to:
// once the store has grown well past what the history still holds, the rest is dropped and the
// history's indices are renumbered. Index 0 stays the empty state.
void DeviceOps::compactNoteStates(bool force) {
    if (!force && rt_.noteStates_.size() < std::max<size_t>(1024, 2 * rt_.noteStatesKept_)) return;
    std::vector<std::vector<uint8_t>> kept(1);
    std::unordered_map<size_t, size_t> remap;
    auto keep = [&](float& v) {
        const auto at = static_cast<size_t>(v);
        if (at == 0 || at >= rt_.noteStates_.size()) return;
        auto [it, fresh] = remap.try_emplace(at, kept.size());
        if (fresh) kept.push_back(std::move(rt_.noteStates_[at]));
        v = static_cast<float>(it->second);
    };
    for (auto* stack : {&rt_.undoStack_, &rt_.redoStack_})
        for (auto& e : *stack)
            for (auto& m : e.mix)
                if (m.kind == MixKind::InstrumentEdit || m.kind == MixKind::InstrumentState ||
                    m.kind == MixKind::DeviceNotes)
                    keep(m.before), keep(m.after);
    rt_.noteStates_ = std::move(kept);
    rt_.noteStatesKept_ = rt_.noteStates_.size();
}

void DeviceOps::instrumentActionUndoable(
    Instrument& inst, int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t id, int32_t iarg, float farg) {
    auto before = inst.editState();
    inst.action(id, iarg, farg);
    if (!before.empty()) pushInstrumentEdit(trackId, deviceIndex, chain, std::move(before), inst.editState());
}

// An edit of a rack chain instrument made by plain param writes (a Twin's side or part copy, a
// preset): its params have no undo target of their own, so the step swaps the whole state the
// caller read before the edit for the one it has now.
bool DeviceOps::recordInstrumentState(
    int32_t trackId, int32_t deviceIndex, int32_t chain, const uint8_t* before, int32_t size) {
    Instrument* inst = chain >= 0 ? editStateInstrument(trackId, deviceIndex, chain) : nullptr;
    if (!inst || !before || size <= 0) return false;
    pushInstrumentEdit(trackId, deviceIndex, chain, std::vector<uint8_t>(before, before + size), inst->getState(),
        MixKind::InstrumentState);
    return true;
}

// Loads a state blob into an instrument (a preset's patterns) with what it changes in the edit
// state as an undo step, so undoing the preset brings the old sequence back with the old sound.
bool DeviceOps::instrumentSetStateUndoable(
    int32_t trackId, int32_t deviceIndex, int32_t chain, const uint8_t* data, int32_t size) {
    Instrument* inst = editStateInstrument(trackId, deviceIndex, chain);
    if (!inst || !data || size <= 0) return false;
    auto before = inst->editState();
    inst->setState(data, size);
    pushInstrumentEdit(trackId, deviceIndex, chain, std::move(before), inst->editState());
    return true;
}

// Nota Rhythm Phase 2: load a one-shot into one drum voice. Clones the instrument (so the
// shared_ptr swap never races the audio thread), loads the buffer, republishes.
bool DeviceOps::setRhythmVoiceSample(int32_t trackId, int32_t voice, const std::string& path) {
    auto old = rt_.findTrackAuthoring(trackId);
    auto* rh =
        old && old->instrument ? dynamic_cast<dsp::capabilities::IRhythmInstrument*>(old->instrument.get()) : nullptr;
    if (!rh) return false;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return false;
    auto freshInst = old->instrument->clone(); // copies params/patterns/samples
    auto* fresh = dynamic_cast<dsp::capabilities::IRhythmInstrument*>(freshInst.get());
    if (!freshInst || !fresh) return false;
    freshInst->adoptUid(*old->instrument); // an edit, not a new instrument: voice keys keep pointing here (specs/050)
    fresh->setVoiceSample(voice, sample);
    freshInst->presetTag = old->instrument->presetTag; // still the preset it was loaded from (specs/048)
    auto nt = rt_.cloneTrack(*old);
    nt->instrument = std::move(freshInst);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

// ---- Nota Aurora custom wavetables (specs/053) --------------------------------
std::shared_ptr<const dsp::capabilities::IWavetableInstrument> DeviceOps::auroraOf(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument) return nullptr;
    return std::dynamic_pointer_cast<const dsp::capabilities::IWavetableInstrument>(t->instrument);
}

bool DeviceOps::auroraSetTable(
    int32_t trackId, int32_t osc, std::shared_ptr<const AuroraTable> table, bool checkpoint) {
    if (osc < 0 || osc > 1 || !table) return false;
    auto old = rt_.findTrackAuthoring(trackId);
    auto* w = old && old->instrument ? dynamic_cast<dsp::capabilities::IWavetableInstrument*>(old->instrument.get())
                                     : nullptr;
    if (!w) return false;
    if (!checkpoint) {
        w->setWavetable(osc, std::move(table)); // a project load: in place, through the table history
        return true;
    }
    // Undoable: a fresh Aurora with the table (the clone shares the other slot), so undo
    // brings the old instrument, and its tables, back.
    auto freshInst = old->instrument->clone();
    auto* fresh = dynamic_cast<dsp::capabilities::IWavetableInstrument*>(freshInst.get());
    if (!freshInst || !fresh) return false;
    fresh->setWavetable(osc, std::move(table));
    freshInst->presetTag = old->instrument->presetTag;
    auto nt = rt_.cloneTrack(*old);
    nt->instrument = std::move(freshInst);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool DeviceOps::auroraClearTable(int32_t trackId, int32_t osc) {
    if (osc < 0 || osc > 1) return false;
    auto old = rt_.findTrackAuthoring(trackId);
    auto* w = old && old->instrument ? dynamic_cast<dsp::capabilities::IWavetableInstrument*>(old->instrument.get())
                                     : nullptr;
    if (!w) return false;
    auto freshInst = old->instrument->clone();
    auto* fresh = dynamic_cast<dsp::capabilities::IWavetableInstrument*>(freshInst.get());
    if (!freshInst || !fresh) return false;
    fresh->clearWavetable(osc);
    freshInst->presetTag = old->instrument->presetTag;
    auto nt = rt_.cloneTrack(*old);
    nt->instrument = std::move(freshInst);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool DeviceOps::auroraSetSource(int32_t trackId, int32_t osc, bool custom, bool checkpoint) {
    auto t = rt_.findTrackAuthoring(trackId);
    auto* w =
        t && t->instrument ? dynamic_cast<dsp::capabilities::IWavetableInstrument*>(t->instrument.get()) : nullptr;
    if (!w || osc < 0 || osc > 1) return false;
    if (!checkpoint) {
        w->setWavetableSource(osc, custom); // a load: in place, lock-free
        t->instrument->wake();
        return true;
    }
    if (w->usesCustomWavetable(osc) == custom) return true; // nothing to undo
    // Undoable, like set_table: a fresh Aurora with the flag flipped, so undo brings back the
    // instrument that still holds the old flag (and redo the new one).
    auto freshInst = t->instrument->clone();
    auto* fresh = dynamic_cast<dsp::capabilities::IWavetableInstrument*>(freshInst.get());
    if (!freshInst || !fresh) return false;
    fresh->setWavetableSource(osc, custom);
    freshInst->presetTag = t->instrument->presetTag;
    auto nt = rt_.cloneTrack(*t);
    nt->instrument = std::move(freshInst);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool DeviceOps::auroraSetMissing(int32_t trackId, int32_t osc, const std::string& name) {
    auto t = rt_.findTrackAuthoring(trackId);
    auto* w =
        t && t->instrument ? dynamic_cast<dsp::capabilities::IWavetableInstrument*>(t->instrument.get()) : nullptr;
    if (!w || osc < 0 || osc > 1) return false;
    w->setWavetableMissing(osc, name);
    return true;
}

bool DeviceOps::setTrackGrainSample(int32_t trackId, const std::string& path, int32_t rootNote) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || !old->instrument ||
        old->instrument->kind() != static_cast<int32_t>(devices::InstrumentKind::GrainSynth))
        return false;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return false;
    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    auto fresh = dsp::DeviceRegistry::createInstrument(static_cast<int32_t>(devices::InstrumentKind::GrainSynth));
    auto* freshSmp = dynamic_cast<dsp::capabilities::ISampleInstrument*>(fresh.get());
    if (!fresh || !freshSmp) return false;
    fresh->setSampleRate(sr);
    for (int i = 0; i < old->instrument->pluginParamCount(); ++i)
        fresh->pluginParamSet(i, old->instrument->pluginParamGet(i));
    freshSmp->setSample(sample, rootNote, false);
    fresh->presetTag = old->instrument->presetTag; // still the preset it was loaded from (specs/048)
    auto nt = rt_.cloneTrack(*old);
    nt->instrument = std::move(fresh);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool DeviceOps::setTrackGrainRoot(int32_t trackId, int32_t rootNote) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument || t->instrument->kind() != static_cast<int32_t>(devices::InstrumentKind::GrainSynth))
        return false;
    auto* gr = dynamic_cast<dsp::capabilities::ISampleInstrument*>(t->instrument.get());
    if (!gr) return false;
    gr->setRoot(rootNote); // lock-free atomic; the synth is shared across snapshots
    return true;
}

int32_t DeviceOps::addSamplerTrack(const std::string& path, int32_t rootNote, bool loop) {
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return 0;
    auto g = std::make_shared<Graph>(*rt_.authoring_);
    const int32_t id = rt_.nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto inst = dsp::DeviceRegistry::createInstrument(static_cast<int32_t>(devices::InstrumentKind::Sampler));
    auto* sampler = dynamic_cast<dsp::capabilities::ISampleInstrument*>(inst.get());
    if (!inst || !sampler) return 0;
    inst->setSampleRate(rt_.transport_.sampleRate());
    sampler->setSample(sample, rootNote, loop);
    track->instrument = std::move(inst);
    g->tracks.push_back(track);
    rt_.publish(g);
    return id;
}

int32_t DeviceOps::addSamplerInstrumentTrack() {
    auto g = std::make_shared<Graph>(*rt_.authoring_);
    const int32_t id = rt_.nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto sampler = dsp::DeviceRegistry::createInstrument(
        static_cast<int32_t>(devices::InstrumentKind::Sampler)); // empty — a sample is loaded later
    if (!sampler) return 0;
    sampler->setSampleRate(rt_.transport_.sampleRate());
    track->instrument = std::move(sampler);
    g->tracks.push_back(track);
    rt_.publish(g);
    return id;
}

bool DeviceOps::setTrackSamplerSample(int32_t trackId, const std::string& path, int32_t rootNote) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || !old->instrument || old->instrument->kind() != static_cast<int32_t>(devices::InstrumentKind::Sampler))
        return false;
    auto* sm = dynamic_cast<const dsp::capabilities::ISampleInstrument*>(old->instrument.get());
    if (!sm) return false;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return false;
    // Publish a fresh Sampler (new sample + copied params) rather than mutating the live
    // one in place — the audio thread reads the instrument's shared_ptr, so replacing it
    // via republishWithTrack keeps the load race-free. Voices reset (fine on load).
    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    auto fresh = dsp::DeviceRegistry::createInstrument(static_cast<int32_t>(devices::InstrumentKind::Sampler));
    auto* freshSmp = dynamic_cast<dsp::capabilities::ISampleInstrument*>(fresh.get());
    if (!fresh || !freshSmp) return false;
    fresh->setSampleRate(sr);
    for (int i = 0; i < old->instrument->pluginParamCount(); ++i)
        fresh->pluginParamSet(i, old->instrument->pluginParamGet(i));
    freshSmp->setSample(sample, rootNote, sm->loopEnabled());
    fresh->presetTag = old->instrument->presetTag; // still the preset it was loaded from (specs/048)
    auto nt = rt_.cloneTrack(*old);
    nt->instrument = std::move(fresh);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool DeviceOps::setTrackSamplerRoot(int32_t trackId, int32_t rootNote) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument || t->instrument->kind() != static_cast<int32_t>(devices::InstrumentKind::Sampler))
        return false;
    auto* sm = dynamic_cast<dsp::capabilities::ISampleInstrument*>(t->instrument.get());
    if (!sm) return false;
    sm->setRoot(rootNote); // lock-free atomic; the sampler is shared across snapshots
    return true;
}

float DeviceOps::samplerPlayPosition(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || !t->instrument || t->instrument->kind() != static_cast<int32_t>(devices::InstrumentKind::Sampler))
        return -1.0f;
    auto* sm = dynamic_cast<const dsp::capabilities::ISampleInstrument*>(t->instrument.get());
    return sm ? sm->playPosition() : -1.0f;
}

// --- hosted plugins (M3-3) -------------------------------------------------

int32_t DeviceOps::addPluginInstrumentTrack(int32_t catalogIndex) {
    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    auto inst = pluginhost::sharedPluginHost().createInstrument(catalogIndex, sr, kMaxBlock);
    if (!inst) return 0;
    auto g = std::make_shared<Graph>(*rt_.authoring_);
    const int32_t id = rt_.nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    track->instrument = inst;
    g->tracks.push_back(track);
    rt_.publish(g);
    recomputePdc();
    return id;
}

bool DeviceOps::setTrackInstrumentPlugin(int32_t trackId, int32_t catalogIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    auto inst = pluginhost::sharedPluginHost().createInstrument(catalogIndex, sr, kMaxBlock);
    if (!inst) return false;
    return replaceTrackInstrument(trackId, std::move(inst));
}

int32_t DeviceOps::addTrackEffectPlugin(int32_t trackId, int32_t catalogIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    auto dev = pluginhost::sharedPluginHost().createEffect(catalogIndex, sr, kMaxBlock);
    if (!dev) return -1;
    auto nt = rt_.cloneTrack(*old);
    nt->devices.push_back(dev);
    const int32_t idx = static_cast<int32_t>(nt->devices.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    recomputePdc();
    return idx;
}

int32_t DeviceOps::trackDeviceCount(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->devices.size()) : 0;
}

// Offline analysis (audio→MIDI): copy an audio clip's played source region, down-mixed to
// mono. out == nullptr returns the available frame count; otherwise writes up to maxFrames and
// returns how many were written. outSr (optional) receives the sample's source sample rate.
int32_t DeviceOps::clipAudioMono(
    int32_t trackId, int32_t clipIndex, float* out, int32_t maxFrames, double* outSr) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || clipIndex < 0 || clipIndex >= static_cast<int32_t>(t->clips.size())) return 0;
    const AudioClip& c = t->clips[static_cast<size_t>(clipIndex)];
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
            const float* p = &sb.samples[static_cast<size_t>(f * ch)];
            for (int k = 0; k < ch; ++k) acc += p[k];
            acc /= static_cast<float>(ch);
        }
        out[i] = acc;
    }
    return n;
}

// --- built-in devices + generic params (M4-4/5) ----------------------------

int32_t DeviceOps::addTrackBuiltinDevice(int32_t trackId, int32_t kind) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    auto dev = dsp::DeviceRegistry::createAudioEffect(kind);
    if (!dev) return -1;
    dev->setSampleRate(rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0, kMaxBlock);
    if (kind == static_cast<int32_t>(devices::AudioEffectKind::EffectRack))
        if (auto* rk = dynamic_cast<RackDevice*>(dev.get())) rk->addChain(-1); // one pass-through chain
    auto nt = rt_.cloneTrack(*old);
    nt->devices.push_back(dev);
    const int32_t idx = static_cast<int32_t>(nt->devices.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    recomputePdc();
    return idx;
}

std::shared_ptr<Instrument> DeviceOps::cloneInstrument(const Track& src) const {
    return src.instrument ? cloneInstrumentFrom(*src.instrument) : nullptr;
}

std::shared_ptr<Instrument> DeviceOps::cloneInstrumentFrom(const Instrument& src) const {
    auto c = cloneInstrumentBody(src);
    if (c) c->presetTag = src.presetTag; // the preset it was loaded from (specs/048)
    return c;
}

std::shared_ptr<Instrument> DeviceOps::cloneInstrumentBody(const Instrument& src) const {
    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    if (auto c = src.clone()) {
        c->setSampleRate(sr);
        c->editorPreview = src.editorPreview; // its window's picture comes along (specs/046)
        return c;
    } // hosted plugin
    auto inst = dsp::DeviceRegistry::createInstrument(src.kind());
    if (!inst) return nullptr;
    inst->setSampleRate(sr);
    if (auto* dstSm = dynamic_cast<dsp::capabilities::ISampleInstrument*>(inst.get())) {
        if (auto* srcSm = dynamic_cast<const dsp::capabilities::ISampleInstrument*>(&src))
            dstSm->setSample(srcSm->sample(), srcSm->rootNote(), srcSm->loopEnabled());
    }
    return inst;
}

std::shared_ptr<Device> DeviceOps::cloneDevice(const Device& src) const {
    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    // A built-in starts fresh and takes the source's values below; a rack or hosted plugin
    // copies itself (its whole state).
    std::shared_ptr<Device> d = src.asRack() ? nullptr : dsp::DeviceRegistry::createChainEffect(src.builtinKind());
    const bool builtin = d != nullptr;
    if (!d) d = src.clone();
    if (!d) return nullptr;
    d->setSampleRate(sr, kMaxBlock);
    if (builtin) { // copy generic params
        const int32_t pc = src.paramCount();
        for (int32_t p = 0; p < pc; ++p) d->setParam(p, src.getParam(p));
        auto st = src.getState(); // + any state blob (looper PCM)
        if (!st.empty()) d->setState(st.data(), static_cast<int32_t>(st.size()));
    }
    d->setBypassed(src.bypassed());
    d->setFolded(src.folded());
    d->setPresetTag(src.presetTag());                           // the preset it was loaded from (specs/048)
    d->setEditorPreview(src.editorPreview());                   // shared bytes: the same picture (specs/046)
    d->setSidechainSourceTrackId(src.sidechainSourceTrackId()); // a keyed device keeps its key
    d->setSidechainPart(src.sidechainPart());
    d->setSidechainTapPre(src.sidechainTapPre());
    d->setSidechainGainDb(src.sidechainGainDb());
    d->setSidechainMix(src.sidechainMix());
    return d;
}

bool DeviceOps::moveDevice(int32_t trackId, int32_t fromIndex, int32_t toIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    const int32_t n = static_cast<int32_t>(old->devices.size());
    if (fromIndex < 0 || fromIndex >= n) return false;
    if (toIndex < 0) toIndex = 0;
    if (toIndex >= n) toIndex = n - 1;
    if (toIndex == fromIndex) return true;
    auto nt = rt_.cloneTrack(*old);
    auto dev = nt->devices[static_cast<size_t>(fromIndex)];
    nt->devices.erase(nt->devices.begin() + fromIndex);
    nt->devices.insert(nt->devices.begin() + toIndex, dev);
    rt_.automation().remapAutomationAfterDeviceChange(*nt, false, -1, fromIndex, toIndex); // lanes follow their device
    rt_.republishWithTrack(trackId, nt);
    rt_.automation().remapCvLinksAfterDeviceChange(trackId, -1, fromIndex, toIndex); // keep CV-link targets valid
    return true; // order change doesn't affect total chain latency (PDC unchanged)
}

bool DeviceOps::removeDevice(int32_t trackId, int32_t deviceIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(old->devices.size())) return false;
    // Close the device's plugin editor (if any) so its native window doesn't linger
    // on screen after the device is removed from the chain.
    if (old->devices[static_cast<size_t>(deviceIndex)]) old->devices[static_cast<size_t>(deviceIndex)]->closeEditor();
    auto nt = rt_.cloneTrack(*old);
    nt->devices.erase(nt->devices.begin() + deviceIndex);
    rt_.automation().remapAutomationAfterDeviceChange(*nt, false, deviceIndex, -1, -1); // drop/shift its lanes
    rt_.republishWithTrack(trackId, nt);
    rt_.automation().remapCvLinksAfterDeviceChange(trackId, deviceIndex, -1, -1); // drop/shift CV-link targets
    recomputePdc();
    return true;
}

Device* DeviceOps::deviceAt(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(t->devices.size())) return nullptr;
    return t->devices[static_cast<size_t>(deviceIndex)].get();
}

// Fresh built-in factories live in devices/DeviceFactory.cpp.

// Replace an instrument track's instrument with a fresh built-in of `kind` (browser drag onto
// an existing track, Replace… in the instrument's menu). 3 and 4 are an empty Instrument Rack /
// Drum Rack (specs/043), as a new rack track starts. Unknown kinds return false so the caller
// can add a new track.
bool DeviceOps::setTrackBuiltinInstrument(int32_t trackId, int32_t kind) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument) return false;
    std::shared_ptr<Instrument> inst = dsp::DeviceRegistry::createInstrument(kind);
    if (!inst) return false;
    inst->setSampleRate(rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0);
    return replaceTrackInstrument(trackId, std::move(inst));
}

int64_t DeviceOps::trackInstrumentId(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t && t->instrument ? static_cast<int64_t>(t->instrument->uid()) : 0;
}

bool DeviceOps::replaceTrackInstrument(int32_t trackId, std::shared_ptr<Instrument> inst) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || old->type() != TrackType::Instrument || !inst) return false;
    // The old instrument survives in undo history, so only its window goes.
    if (old->instrument) old->instrument->closeEditor();
    auto nt = rt_.cloneTrack(*old);
    nt->instrument = std::move(inst);
    rt_.republishWithTrack(trackId, nt);
    recomputePdc();
    return true;
}

std::shared_ptr<MidiDevice> DeviceOps::cloneMidiDevice(const MidiDevice& src) const {
    std::shared_ptr<MidiDevice> d = dsp::DeviceRegistry::createMidiEffect(src.midiKind());
    if (!d) d = src.clone();
    if (!d) return nullptr;
    d->setSampleRate(rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0, kMaxBlock);
    if (src.midiKind() >= 0) {
        const int32_t pc = src.paramCount();
        for (int32_t p = 0; p < pc; ++p) d->setParam(p, src.getParam(p));
        auto st = src.getState();
        if (!st.empty()) d->setState(st.data(), static_cast<int32_t>(st.size()));
    }
    d->setCcDest(src.ccDestDevice(), src.ccDestParam()); // the Arp's CC modulation (#238)
    d->setCcDestNested(src.ccDestChainUid(), src.ccDestSlot());
    d->setCcDepth(src.ccDepth());
    d->setBypassed(src.bypassed());
    d->setFolded(src.folded());
    d->setPresetTag(src.presetTag()); // the preset it was loaded from (specs/048)
    return d;                         // live state starts fresh
}

} // namespace nota::detail
