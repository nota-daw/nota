// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Engine — Instrument / Audio-Effect / MIDI-Effect Rack (nested device containers). Resolves a
// (trackId, deviceIndex) pair to the shared RackCore (deviceIndex −1 = the track's instrument
// rack; >= 0 = the effect-rack device at that chain position; <= kMidiRackBase = the MIDI
// effect rack at MIDI index kMidiRackBase − deviceIndex, specs/040; <= kNestedRackBase = the
// Drum Rack in chain kNestedRackBase − deviceIndex of the track's Instrument Rack, specs/041)
// and delegates to its
// editing API. Rack-internal edits publish the rack's OWN
// snapshot (thread-safe) and don't republish the graph — the rack object is
// stable across graph snapshots.

#include "Engine.h"
#include "media/AudioFile.h"
#include "bridge/PluginHostBridge.h"
#include "devices/racks/RackCore.h"
#include "devices/racks/RackInstrument.h"
#include "devices/racks/RackDevice.h"
#include "devices/racks/DrumRack.h"
#include "devices/racks/MidiRack.h"
#include "devices/instruments/Sampler.h"
#include "devices/instruments/Mosaic.h"
#include "devices/instruments/GrainSynth.h"
#include "SnapshotHistory.h"

#include <algorithm>
#include <cmath>
#include <memory>
#include <vector>

// Catalog id -> index resolution lives in the plugin host (C ABI); reused here so
// a saved rack blob can rebuild a plugin child by its stable identifier.
extern "C" int32_t nota_pluginhost_index_of_id(const char* identifier);

namespace nota {

void Engine::installRackPluginFactory() {
    const int32_t mb = kMaxBlock;
    RackCore::pluginFactory().instrumentById = [mb](const std::string& id) -> std::shared_ptr<Instrument> {
        const int32_t idx = nota_pluginhost_index_of_id(id.c_str());
        return idx >= 0 ? createPluginInstrument(idx, 44100.0, mb) : nullptr; // rack re-rates it via setRates
    };
    RackCore::pluginFactory().effectById = [mb](const std::string& id) -> std::shared_ptr<Device> {
        const int32_t idx = nota_pluginhost_index_of_id(id.c_str());
        return idx >= 0 ? createPluginEffect(idx, 44100.0, mb) : nullptr;
    };
}

int32_t Engine::rackAddPluginInstrumentChain(int32_t t, int32_t di, int32_t catalogIndex) {
    if (!rackCoreAt(t, di)) return -1;
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto inst = createPluginInstrument(catalogIndex, sr, kMaxBlock);
    if (!inst) return -1;
    auto* r = rackForEdit(t, di);
    const int32_t idx = r->addChainInstrument(inst);
    recomputePdc();
    return idx;
}

int32_t Engine::rackAddPluginChainDevice(int32_t t, int32_t di, int32_t chain, int32_t catalogIndex) {
    if (!rackCoreAt(t, di)) return -1;
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto dev = createPluginEffect(catalogIndex, sr, kMaxBlock);
    if (!dev) return -1;
    auto* r = rackForEdit(t, di);
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
    // No chains: the card invites an instrument dropped on it (the Volt default of specs/026 is gone).
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
    rack->setSampleRate(transport_.sampleRate()); // starts empty; the UI adds pads
    track->instrument = rack;
    g->tracks.push_back(track);
    publish(g);
    return id;
}

int32_t Engine::rackChainTriggerNote(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainTriggerNote(c) : -1;
}
void Engine::rackSetChainTriggerNote(int32_t t, int32_t di, int32_t c, int32_t note) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || r->chainTriggerNote(c) == note) return;
    rackForEdit(t, di)->setChainTriggerNote(c, note);
}

std::string Engine::rackChainName(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainName(c) : std::string{};
}
void Engine::rackSetChainName(int32_t t, int32_t di, int32_t c, const std::string& name) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || r->chainName(c) == name) return;
    rackForEdit(t, di)->setChainName(c, name);
}

int32_t Engine::rackAddSamplerChain(int32_t t, int32_t di, const std::string& path, int32_t rootNote, bool loop) {
    if (!rackCoreAt(t, di)) return -1;
    auto buf = decodeAudioFile(path);
    if (!buf || buf->empty()) return -1;
    auto sm = std::make_shared<Sampler>();
    sm->setSampleRate(transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0);
    sm->setSample(buf, rootNote, loop);
    return rackForEdit(t, di)->addChainInstrument(sm);
}
bool Engine::rackSetChainSamplerSample(
    int32_t t, int32_t di, int32_t chain, const std::string& path, int32_t rootNote) {
    auto* r = rackCoreAt(t, di);
    if (!r || chain < 0 || chain >= r->chainCount()) return false;
    auto buf = decodeAudioFile(path);
    if (!buf || buf->empty()) return false;
    return rackForEdit(t, di)->setChainInstrumentSample(chain, buf, rootNote, false);
}

// The track's plain Instrument Rack (not a Drum Rack): the only rack whose chains take a rack.
static RackInstrument* nestingHost(const std::shared_ptr<Track>& t) {
    auto* r = t ? dynamic_cast<RackInstrument*>(t->instrument.get()) : nullptr;
    return (r && !dynamic_cast<DrumRack*>(r)) ? r : nullptr;
}

RackCore* Engine::rackCoreAt(int32_t trackId, int32_t deviceIndex) const {
    auto t = findTrackAuthoring(trackId);
    if (!t) return nullptr;
    if (isNestedRack(deviceIndex)) {
        auto* host = nestingHost(t);
        return host ? dynamic_cast<RackCore*>(dynamic_cast<RackInstrument*>(host->chainInstrument(kNestedRackBase - deviceIndex)))
                    : nullptr;
    }
    if (deviceIndex <= kMidiRackBase) {
        const int32_t mi = kMidiRackBase - deviceIndex;
        if (mi >= static_cast<int32_t>(t->midiEffects.size()) || !t->midiEffects[mi]) return nullptr;
        return dynamic_cast<RackCore*>(dynamic_cast<MidiRack*>(t->midiEffects[mi].get()));
    }
    if (deviceIndex < 0) return t->instrument ? dynamic_cast<RackCore*>(t->instrument.get()) : nullptr;
    if (deviceIndex < static_cast<int32_t>(t->devices.size()))
        return t->devices[deviceIndex] ? dynamic_cast<RackCore*>(t->devices[deviceIndex].get()) : nullptr;
    return nullptr;
}

// Rack edits used to change the rack in place, and the rack object is shared by every
// graph snapshot, so undo had nothing to go back to (#236). An edit the user can undo
// forks the rack into a new object (chain children shared, so nothing restarts) and
// republishes the track with it; the snapshot on the undo stack keeps the old rack as it was.
// A Drum Rack in a fork of an Instrument Rack chain: a fork of its own, put in its chain.
static std::shared_ptr<RackInstrument> forkRackInstrument(const RackInstrument& src);

RackCore* Engine::rackForEdit(int32_t trackId, int32_t deviceIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || !rackCoreAt(trackId, deviceIndex)) return nullptr;
    if (isNestedRack(deviceIndex)) {
        // Fork the outer rack (one undo step: the snapshot keeps the old outer rack, which keeps
        // the old nested one), then the nested rack inside the fork.
        const int32_t c = kNestedRackBase - deviceIndex;
        auto* outer = rackForEdit(trackId, -1);
        auto* src = outer ? dynamic_cast<RackInstrument*>(outer->chainInstrument(c)) : nullptr;
        if (!src) return nullptr;
        auto r = forkRackInstrument(*src);
        RackCore* fresh = r.get();
        // The fork shares the playing Drum Rack's pads and effects, already prepared: install it
        // without preparing them again (that would clear a reverb's tail mid-play).
        return outer->replaceChainInstrument(c, std::move(r), /*prepare=*/false) ? fresh : nullptr;
    }
    auto nt = cloneTrack(*old);
    RackCore* fresh = nullptr;
    if (deviceIndex <= kMidiRackBase) {
        const int32_t mi = kMidiRackBase - deviceIndex;
        auto* src = dynamic_cast<MidiRack*>(old->midiEffects[mi].get());
        if (!src) return nullptr;
        auto r = std::make_shared<MidiRack>();
        r->forkFrom(*src);
        r->shareRuntimeWith(*src); // held notes play on through the swap
        r->setBypassed(src->bypassed());
        r->setFolded(src->folded());
        fresh = r.get();
        nt->midiEffects[mi] = std::move(r);
        republishWithTrack(trackId, nt);
        return fresh;
    }
    if (deviceIndex < 0) {
        auto* src = dynamic_cast<RackInstrument*>(old->instrument.get());
        if (!src) return nullptr;
        auto r = forkRackInstrument(*src);
        fresh = r.get();
        nt->instrument = std::move(r);
    } else {
        auto* src = dynamic_cast<RackDevice*>(old->devices[deviceIndex].get());
        if (!src) return nullptr;
        auto r = std::make_shared<RackDevice>();
        r->forkFrom(*src);
        r->setBypassed(src->bypassed());
        r->setFolded(src->folded());
        r->setSidechainSourceTrackId(src->sidechainSourceTrackId());
        r->setSidechainTapPre(src->sidechainTapPre());
        r->setSidechainGainDb(src->sidechainGainDb());
        r->setSidechainMix(src->sidechainMix());
        fresh = r.get();
        nt->devices[deviceIndex] = std::move(r);
    }
    republishWithTrack(trackId, nt);
    return fresh;
}

static std::shared_ptr<RackInstrument> forkRackInstrument(const RackInstrument& src) {
    std::shared_ptr<RackInstrument> r;
    if (dynamic_cast<const DrumRack*>(&src)) r = std::make_shared<DrumRack>();
    else r = std::make_shared<RackInstrument>();
    r->forkFrom(src);
    r->adoptUid(src);      // the same rack to the UI: its card keeps its preset label and A/B
    r->shareMonoWith(src); // held keys, the sounding note and a glide in progress play on
    r->setBypassed(src.bypassed());
    r->folded = src.folded;
    // Playback state the audio thread owns: relaxed atomics, carried over so a held pedal
    // still releases its notes after the edit.
    constexpr auto rl = std::memory_order_relaxed;
    r->offSettled.store(src.offSettled.load(rl), rl);
    r->pedalDown.store(src.pedalDown.load(rl), rl);
    r->pedalHeld[0].store(src.pedalHeld[0].load(rl), rl);
    r->pedalHeld[1].store(src.pedalHeld[1].load(rl), rl);
    for (int i = 0; i < kCtlCount; ++i) r->deliveredCtl[i].store(src.deliveredCtl[i].load(rl), rl);
    return r;
}

// Racks nest one level (specs/041): an Instrument Rack chain may hold a Drum Rack; nothing
// else takes a rack, and an Instrument Rack is never a chain instrument.
bool Engine::rackKindAllowed(int32_t trackId, int32_t deviceIndex, int32_t kind) const {
    if (!RackCore::isRackKind(kind)) return true;
    return kind == 4 && deviceIndex == -1 && nestingHost(findTrackAuthoring(trackId)) != nullptr;
}

// ---- wrap a device in a rack / unwrap it (#257) ----------------------------
// Whether anything outside the device points at it by its chain index: automation lanes
// CV links, or an Arp's CC target. Wrapping moves the device into a rack chain, where those can't follow it.
bool Engine::deviceIsReferenced(const Track& t, int32_t deviceIndex) const {
    for (auto& l : t.automation)
        if ((l.target == AutomationTarget::DeviceParam || l.target == AutomationTarget::PluginParam) && l.deviceIndex == deviceIndex)
            return true;
    // An Arp's CC lane drives a device param by chain index (applyMidiCcRouting).
    for (auto& md : t.midiEffects)
        if (md && md->ccDestDevice() == deviceIndex) return true;
    for (auto& l : t.cvLinks)
        if (l.sourceKind == static_cast<int32_t>(CvSourceKind::Param) && l.sourceDevice == deviceIndex) return true;
    for (auto& ot : authoring_->tracks)
        for (auto& l : ot->cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? ot->id() : l.targetTrack;
            if (tgt == t.id() && l.targetKind == static_cast<int32_t>(CvTargetKind::Device) && l.targetDevice == deviceIndex)
                return true;
        }
    return false;
}

int32_t Engine::wrapDeviceInRack(int32_t trackId, int32_t deviceIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(old->devices.size())) return -1;
    auto dev = old->devices[deviceIndex];
    if (!dev || dev->sidechainSourceTrackId() > 0 || deviceIsReferenced(*old, deviceIndex)) return -1;
    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto rack = std::make_shared<RackDevice>();
    rack->setSampleRate(sr, kMaxBlock);
    const int32_t chain = rack->addChain(-1);
    // The device itself moves in, already prepared at this rate: preparing it again would reset
    // it (a Reverb clears its tail) while the published graph may still be playing it.
    if (rack->addChainDeviceInstance(chain, dev, /*prepare=*/false) < 0) return -1;
    auto nt = cloneTrack(*old);
    nt->devices[deviceIndex] = rack;
    republishWithTrack(trackId, nt);
    recomputePdc();
    return deviceIndex;
}

int32_t Engine::unwrapRack(int32_t trackId, int32_t deviceIndex) {
    auto old = findTrackAuthoring(trackId);
    if (!old || deviceIndex < 0 || deviceIndex >= static_cast<int32_t>(old->devices.size())) return -1;
    auto* rack = dynamic_cast<RackDevice*>(old->devices[deviceIndex].get());
    RackCore::RackState* st = rack ? rack->live() : nullptr;
    if (!st || st->chains.size() != 1 || deviceIsReferenced(*old, deviceIndex)) return -1;
    auto devs = st->chains[0].devices;
    // The devices after the rack shift by (n - 1). Lanes and CV links are remapped below; an
    // Arp's CC target lives on the shared MIDI device, outside the undo snapshot, so rather
    // than move it, don't unwrap under one.
    if (devs.size() != 1)
        for (auto& md : old->midiEffects)
            if (md && md->ccDestDevice() > deviceIndex) return -1;
    auto nt = cloneTrack(*old);
    nt->devices.erase(nt->devices.begin() + deviceIndex);
    nt->devices.insert(nt->devices.begin() + deviceIndex, devs.begin(), devs.end());
    // The devices after the rack shift by (n - 1): their lanes and CV links follow.
    const int32_t n = static_cast<int32_t>(devs.size());
    if (n == 0) remapAutomationAfterDeviceChange(*nt, false, deviceIndex, -1, -1);
    for (int32_t i = 1; i < n; ++i)
        remapAutomationAfterDeviceChange(*nt, false, -1, static_cast<int32_t>(nt->devices.size()) - n + i, deviceIndex + i);
    republishWithTrack(trackId, nt);
    if (n == 0) remapCvLinksAfterDeviceChange(trackId, deviceIndex, -1, -1);
    for (int32_t i = 1; i < n; ++i)
        remapCvLinksAfterDeviceChange(trackId, -1, static_cast<int32_t>(nt->devices.size()) - n + i, deviceIndex + i);
    recomputePdc();
    return n;
}

// ---- chains --------------------------------------------------------------
int32_t Engine::rackChainCount(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainCount() : 0;
}
int32_t Engine::rackDuplicateChain(int32_t t, int32_t di, int32_t c) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return -1;
    if (!isNestedRack(di) && di <= kMidiRackBase && r->chainCount() >= MidiRack::kMaxChains) return -1;
    if (di == -1) {
        // The copy goes in after the source and the chains below move down: refuse when that
        // would push a chain's MIDI effects past the first 16 chains, where they don't run.
        const int32_t n = r->chainCount();
        for (int32_t j = c; j < n; ++j)
            if (r->chainMidiDeviceCount(j) > 0 && j + 1 >= RackInstrument::kMaxMidiChains) return -1;
    }
    return rackForEdit(t, di)->duplicateChain(c);
}
bool Engine::rackCheckpoint(int32_t t, int32_t di) { return rackForEdit(t, di) != nullptr; }
int64_t Engine::rackChainId(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? static_cast<int64_t>(r->chainUid(c)) : 0;
}

// ---- chain devices on the device clipboard (specs/042) ----------------------------------
namespace {
// Which sections a rack's chains have: a MIDI Effect Rack only MIDI effects, an Audio Effect
// Rack and a Drum Rack only audio effects, an Instrument Rack both.
bool rackTakesAudio(const RackCore* r) { return !dynamic_cast<const MidiRack*>(r); }
bool rackTakesMidi(const RackCore* r) {
    if (dynamic_cast<const MidiRack*>(r)) return true;
    return dynamic_cast<const RackInstrument*>(r) && !dynamic_cast<const DrumRack*>(r);
}
} // namespace

bool Engine::rackChainDeviceCopy(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = rackCoreAt(t, di);
    Device* src = r ? r->chainDeviceAt(c, i) : nullptr;
    auto copy = src ? cloneDevice(*src) : nullptr;
    if (!copy) return false;
    clipDevice_ = std::move(copy);
    clipMidi_.reset();
    clipInstrument_.reset();
    return true;
}
bool Engine::rackChainMidiCopy(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = rackCoreAt(t, di);
    MidiDevice* src = r ? r->chainMidiDeviceAt(c, i) : nullptr;
    auto copy = src ? cloneMidiDevice(*src) : nullptr;
    if (!copy) return false;
    clipMidi_ = std::move(copy);
    clipDevice_.reset();
    clipInstrument_.reset();
    return true;
}
int32_t Engine::rackChainDevicePaste(int32_t t, int32_t di, int32_t c, int32_t at) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return -1;
    if (!clipDevice_) return clipMidi_ ? -2 : -1;
    if (!rackTakesAudio(r)) return -2;
    if (dynamic_cast<const RackDevice*>(clipDevice_.get())) return -3;
    auto dev = cloneDevice(*clipDevice_);
    if (!dev) return -1;
    dev->setSidechainSourceTrackId(-1); // a chain has no sidechain input
    const int32_t idx = rackForEdit(t, di)->insertChainDeviceInstance(c, std::move(dev), at);
    recomputePdc();
    return idx;
}
int32_t Engine::rackChainMidiPaste(int32_t t, int32_t di, int32_t c, int32_t at) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return -1;
    if (!clipMidi_) return clipDevice_ ? -2 : -1;
    if (!rackTakesMidi(r)) return -2;
    if (clipMidi_->midiKind() < 0 || clipMidi_->midiKind() > 5) return -3; // a MIDI Effect Rack
    if (di == -1 && c >= RackInstrument::kMaxMidiChains) return -5;
    auto dev = cloneMidiDevice(*clipMidi_);
    if (!dev) return -1;
    return rackForEdit(t, di)->insertChainMidiDeviceInstance(c, std::move(dev), at);
}
int32_t Engine::rackChainDeviceDuplicate(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = rackCoreAt(t, di);
    Device* src = r ? r->chainDeviceAt(c, i) : nullptr;
    auto dev = src ? cloneDevice(*src) : nullptr;
    if (!dev) return -1;
    const int32_t idx = rackForEdit(t, di)->insertChainDeviceInstance(c, std::move(dev), i + 1);
    recomputePdc();
    return idx;
}
int32_t Engine::rackChainMidiDuplicate(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = rackCoreAt(t, di);
    MidiDevice* src = r ? r->chainMidiDeviceAt(c, i) : nullptr;
    auto dev = src ? cloneMidiDevice(*src) : nullptr;
    if (!dev) return -1;
    return rackForEdit(t, di)->insertChainMidiDeviceInstance(c, std::move(dev), i + 1);
}
int32_t Engine::rackChainDeviceMoveTo(int32_t t, int32_t di, int32_t c, int32_t i, int32_t to) {
    auto* r = rackCoreAt(t, di);
    if (!r || !r->chainDeviceAt(c, i) || to < 0 || to >= r->chainCount() || to == c) return -1;
    const int32_t idx = rackForEdit(t, di)->moveChainDeviceToChain(c, i, to);
    recomputePdc();
    return idx;
}
int32_t Engine::rackChainMidiMoveTo(int32_t t, int32_t di, int32_t c, int32_t i, int32_t to) {
    auto* r = rackCoreAt(t, di);
    if (!r || !r->chainMidiDeviceAt(c, i) || to < 0 || to >= r->chainCount() || to == c) return -1;
    if (di == -1 && to >= RackInstrument::kMaxMidiChains) return -5;
    return rackForEdit(t, di)->moveChainMidiDeviceToChain(c, i, to);
}
bool Engine::rackMono(int32_t t) const {
    auto* r = rackCoreAt(t, -1);
    return r && r->rackMono();
}
void Engine::rackSetMono(int32_t t, bool on) {
    if (auto* r = rackCoreAt(t, -1)) r->setRackMono(on);
}
int32_t Engine::rackAddChain(int32_t t, int32_t di, int32_t k) {
    // Audio Effect and MIDI Effect Rack chains hold no instrument, whatever kind is asked for
    // (MCP add_effect_rack_chain long passed 4, which is now the Drum Rack).
    if (!isNestedRack(di) && (di >= 0 || di <= kMidiRackBase)) k = -1;
    if (!rackKindAllowed(t, di, k)) return -1;
    if (auto* cur = rackCoreAt(t, di);
        cur && !isNestedRack(di) && di <= kMidiRackBase && cur->chainCount() >= MidiRack::kMaxChains)
        return -1;
    auto* r = rackForEdit(t, di);
    return r ? r->addChain(k) : -1;
}
bool Engine::rackRemoveChain(int32_t t, int32_t di, int32_t c) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return false;
    const bool ok = rackForEdit(t, di)->removeChain(c);
    recomputePdc(); // the chain may have held the rack's longest latency
    return ok;
}
bool Engine::rackSetChainInstrument(int32_t t, int32_t di, int32_t c, int32_t k) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || !rackKindAllowed(t, di, k) || !RackCore::makeInstrument(k)) return false;
    const bool ok = rackForEdit(t, di)->setChainInstrument(c, k);
    recomputePdc();
    return ok;
}
int32_t Engine::rackChainInstrumentKind(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainInstrumentKind(c) : -2;
}
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
    if (inst->kind() < 0) return inst->pluginParamGet(p); // hosted plugin: no fresh default
    auto fresh = RackCore::makeInstrument(inst->kind());  // built-in default == a fresh one's value
    return fresh ? fresh->pluginParamGet(p) : inst->pluginParamGet(p);
}
int32_t Engine::rackChainInstrumentScope(int32_t t, int32_t di, int32_t c, float* out, int32_t n) const {
    if (!out || n <= 0) return 0;
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->scopeRead(out, n) : 0;
}
int32_t Engine::rackChainInstrumentVoiceCount(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->activeVoiceCount() : -1;
}
int32_t Engine::rackChainInstrumentHeldNotes(int32_t t, int32_t di, int32_t c, int32_t* out, int32_t n) const {
    if (!out || n <= 0) return 0;
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->heldNotes(out, n) : 0;
}
void Engine::rackChainInstrumentAction(int32_t t, int32_t di, int32_t c, int32_t id, int32_t iarg, float farg) {
    auto* r = rackCoreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (inst) inst->action(id, iarg, farg);
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

// ---- chain Mosaic (built-in multisample instrument, specs/041) -------------
// Mirrors mosaicSetProgram & co. (Engine_Devices.cpp) for a chain instrument.
bool Engine::rackChainMosaicSetProgram(int32_t t, int32_t di, int32_t c, const std::string& text, bool checkpoint) {
    auto* r = rackCoreAt(t, di);
    auto* m = r ? dynamic_cast<Mosaic*>(r->chainInstrument(c)) : nullptr;
    if (!m) return false;
    if (!checkpoint) { // live edit: in place, voices keep going
        m->setProgram(text);
        return true;
    }
    // An undoable edit puts a fresh Mosaic (params + the samples it already holds, so a zone
    // edit is ready at once) into a fork of the rack; undo brings the old one back.
    auto fresh = std::static_pointer_cast<Mosaic>(m->clone());
    fresh->setProgram(text);
    auto* fr = rackForEdit(t, di);
    return fr && fr->replaceChainInstrument(c, std::move(fresh));
}
// ---- chain Grain sample (a factory Grain preset dropped on a rack, specs/041 review) --------
// Mirrors setTrackGrainSample: a fresh Grain with the chain's values and the sample goes into a
// fork of the rack, so undo brings the old one back.
bool Engine::rackChainGrainSetSample(int32_t t, int32_t di, int32_t c, const std::string& path, int32_t rootNote) {
    auto* r = rackCoreAt(t, di);
    auto* gr = r ? dynamic_cast<GrainSynth*>(r->chainInstrument(c)) : nullptr;
    if (!gr) return false;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return false;
    auto fresh = std::make_shared<GrainSynth>();
    fresh->setSampleRate(transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0);
    for (int i = 0; i < gr->pluginParamCount(); ++i) fresh->pluginParamSet(i, gr->pluginParamGet(i));
    fresh->setSample(sample, rootNote, false);
    auto* fr = rackForEdit(t, di);
    return fr && fr->replaceChainInstrument(c, std::move(fresh));
}

std::string Engine::rackChainMosaicProgram(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    auto* m = r ? dynamic_cast<Mosaic*>(r->chainInstrument(c)) : nullptr;
    return m ? m->programText() : std::string{};
}
bool Engine::rackChainMosaicStatus(int32_t t, int32_t di, int32_t c, NotaMosaicStatus* out) const {
    auto* r = rackCoreAt(t, di);
    auto* m = r ? dynamic_cast<Mosaic*>(r->chainInstrument(c)) : nullptr;
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
int64_t Engine::rackChainMosaicZoneSampleId(int32_t t, int32_t di, int32_t c, int32_t zone) const {
    auto* r = rackCoreAt(t, di);
    auto* m = r ? dynamic_cast<Mosaic*>(r->chainInstrument(c)) : nullptr;
    auto b = m ? m->zoneSample(zone) : nullptr;
    return b ? b->id : 0;
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
    sm->setRoot(rootNote); // lock-free atomic; the sampler is shared across snapshots
    return true;
}
float Engine::rackChainSamplerPlayPosition(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    auto* sm = r ? dynamic_cast<Sampler*>(r->chainInstrument(c)) : nullptr;
    return sm ? sm->playPosition() : -1.0f;
}

// ---- chain devices -------------------------------------------------------
int32_t Engine::rackChainDeviceCount(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainDeviceCount(c) : 0;
}
int32_t Engine::rackAddChainDevice(int32_t t, int32_t di, int32_t c, int32_t k) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || !makeChainDevice(k)) return -1;
    return rackForEdit(t, di)->addChainDevice(c, k);
}
bool Engine::rackRemoveChainDevice(int32_t t, int32_t di, int32_t c, int32_t d) {
    auto* r = rackCoreAt(t, di);
    if (!r || d < 0 || d >= r->chainDeviceCount(c)) return false;
    return rackForEdit(t, di)->removeChainDevice(c, d);
}
bool Engine::rackMoveChainDevice(int32_t t, int32_t di, int32_t c, int32_t from, int32_t to) {
    auto* r = rackCoreAt(t, di);
    if (!r || from < 0 || from >= r->chainDeviceCount(c)) return false;
    if (std::clamp(to, 0, r->chainDeviceCount(c) - 1) == from) return true;
    return rackForEdit(t, di)->moveChainDevice(c, from, to);
}
const char* Engine::rackChainDeviceName(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->displayName() : "";
}
int32_t Engine::rackChainDeviceBuiltinKind(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->builtinKind() : -1;
}
int32_t Engine::rackChainDeviceParamCount(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->paramCount() : 0;
}
const char* Engine::rackChainDeviceParamName(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->paramName(p) : "";
}
float Engine::rackChainDeviceParamMin(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->paramMin(p) : 0.0f;
}
float Engine::rackChainDeviceParamMax(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->paramMax(p) : 1.0f;
}
float Engine::rackChainDeviceParamGet(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->getParam(p) : 0.0f;
}
// ---- chain MIDI effects (MIDI Effect Rack chains; Instrument Rack chains, #329) ----------
int32_t Engine::rackChainMidiCount(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainMidiDeviceCount(c) : 0;
}
int32_t Engine::rackAddChainMidi(int32_t t, int32_t di, int32_t c, int32_t kind) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || kind < 0 || kind > 5) return -1;
    if (dynamic_cast<DrumRack*>(r)) return -1; // pads keep their own note handling
    if (di == -1 && c >= RackInstrument::kMaxMidiChains) return -1; // MIDI effects run in the first 16 chains
    return rackForEdit(t, di)->addChainMidiDevice(c, kind);
}
bool Engine::rackRemoveChainMidi(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = rackCoreAt(t, di);
    if (!r || i < 0 || i >= r->chainMidiDeviceCount(c)) return false;
    return rackForEdit(t, di)->removeChainMidiDevice(c, i);
}
bool Engine::rackMoveChainMidi(int32_t t, int32_t di, int32_t c, int32_t from, int32_t to) {
    auto* r = rackCoreAt(t, di);
    if (!r || from < 0 || from >= r->chainMidiDeviceCount(c)) return false;
    return rackForEdit(t, di)->moveChainMidiDevice(c, from, to);
}
MidiDevice* Engine::rackChainMidiAt(int32_t t, int32_t di, int32_t c, int32_t i) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainMidiDeviceAt(c, i) : nullptr;
}
void Engine::rackSetChainMidiBypassed(int32_t t, int32_t di, int32_t c, int32_t i, bool b) {
    if (auto* m = rackChainMidiAt(t, di, c, i)) m->setBypassed(b);
}

// ---- chain select range / transpose / velocity scale, rack merge / pass-through (v9) ----
void Engine::rackChainSelectRange(int32_t t, int32_t di, int32_t c, int32_t& lo, int32_t& hi) const {
    lo = 0;
    hi = 127;
    if (auto* r = rackCoreAt(t, di)) r->chainSelectRange(c, lo, hi);
}
void Engine::rackSetChainSelectRange(int32_t t, int32_t di, int32_t c, int32_t lo, int32_t hi) {
    if (auto* r = rackCoreAt(t, di)) r->setChainSelectRange(c, lo, hi);
}
int32_t Engine::rackChainTranspose(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainTranspose(c) : 0;
}
void Engine::rackSetChainTranspose(int32_t t, int32_t di, int32_t c, int32_t st) {
    if (auto* r = rackCoreAt(t, di)) r->setChainTranspose(c, st);
}
float Engine::rackChainVelScale(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainVelScale(c) : 1.0f;
}
void Engine::rackSetChainVelScale(int32_t t, int32_t di, int32_t c, float v) {
    if (auto* r = rackCoreAt(t, di)) r->setChainVelScale(c, v);
}
bool Engine::rackMerge(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return !r || r->rackMerge();
}
void Engine::rackSetMerge(int32_t t, int32_t di, bool on) {
    if (auto* r = rackCoreAt(t, di)) r->setRackMerge(on);
}
bool Engine::rackThru(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return !r || r->rackThru();
}
void Engine::rackSetThru(int32_t t, int32_t di, bool on) {
    if (auto* r = rackCoreAt(t, di)) r->setRackThru(on);
}

void Engine::rackChainDeviceParamSet(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p, float v) {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    if (dev) dev->setParam(p, v);
}
void Engine::rackSetChainDeviceBypassed(int32_t t, int32_t di, int32_t c, int32_t d, bool b) {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    if (dev) dev->setBypassed(b);
}
bool Engine::rackChainDeviceBypassed(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev && dev->bypassed();
}
float Engine::rackChainDeviceGainReduction(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->gainReductionDb() : 0.0f;
}
int32_t Engine::rackChainDeviceScope(int32_t t, int32_t di, int32_t c, int32_t d, float* out, int32_t n) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->scopeRead(out, n) : 0;
}
int32_t Engine::rackChainDeviceLayerWave(
    int32_t t, int32_t di, int32_t c, int32_t d, int32_t layer, float* out, int32_t n) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->layerWave(layer, out, n) : 0;
}
std::string Engine::rackChainDeviceText(int32_t t, int32_t di, int32_t c, int32_t d, int32_t id) const {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->deviceText(id) : std::string{};
}
void Engine::rackChainDeviceAction(int32_t t, int32_t di, int32_t c, int32_t d, int32_t id, int32_t iarg, float farg) {
    auto* r = rackCoreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    if (dev) dev->deviceAction(id, iarg, farg);
}

// ---- per-chain controls --------------------------------------------------
void Engine::rackSetChainGain(int32_t t, int32_t di, int32_t c, float v) {
    if (auto* r = rackCoreAt(t, di)) r->setChainGain(c, v);
}
void Engine::rackSetChainPan(int32_t t, int32_t di, int32_t c, float v) {
    if (auto* r = rackCoreAt(t, di)) r->setChainPan(c, v);
}
void Engine::rackSetChainMute(int32_t t, int32_t di, int32_t c, bool b) {
    if (auto* r = rackCoreAt(t, di)) r->setChainMute(c, b);
}
void Engine::rackSetChainSolo(int32_t t, int32_t di, int32_t c, bool b) {
    if (auto* r = rackCoreAt(t, di)) r->setChainSolo(c, b);
}
float Engine::rackChainGain(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainGain(c) : 1.0f;
}
float Engine::rackChainPan(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainPan(c) : 0.0f;
}
bool Engine::rackChainMute(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r && r->chainMute(c);
}
bool Engine::rackChainSolo(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r && r->chainSolo(c);
}

// ---- Drum Rack per-pad shaping + kit-level swing/humanize -----------------
void Engine::rackSetChainChoke(int32_t t, int32_t di, int32_t c, int32_t g) {
    if (auto* r = rackCoreAt(t, di)) r->setChainChoke(c, g);
}
int32_t Engine::rackChainChoke(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainChoke(c) : 0;
}
void Engine::rackSetChainTune(int32_t t, int32_t di, int32_t c, int32_t st) {
    if (auto* r = rackCoreAt(t, di)) r->setChainTune(c, st);
}
int32_t Engine::rackChainTune(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainTune(c) : 0;
}
void Engine::rackSetChainDecay(int32_t t, int32_t di, int32_t c, float v) {
    if (auto* r = rackCoreAt(t, di)) r->setChainDecay(c, v);
}
float Engine::rackChainDecay(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainDecay(c) : 1.0f;
}
void Engine::rackSetSwing(int32_t t, int32_t di, float v) {
    if (auto* r = rackCoreAt(t, di)) r->setSwing(v);
}
float Engine::rackSwing(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->swing() : 0.0f;
}
void Engine::rackSetHumanize(int32_t t, int32_t di, float v) {
    if (auto* r = rackCoreAt(t, di)) r->setHumanize(v);
}
float Engine::rackHumanize(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->humanize() : 0.0f;
}

// ---- Audio Effect Rack routing + output stage ----------------------------
void Engine::rackSetMode(int32_t t, int32_t di, int32_t m) {
    if (auto* r = rackCoreAt(t, di)) r->setRackMode(m);
}
int32_t Engine::rackMode(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->rackMode() : 0;
}
void Engine::rackSetDryWet(int32_t t, int32_t di, float v) {
    if (auto* r = rackCoreAt(t, di)) r->setRackDryWet(v);
}
float Engine::rackDryWet(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->rackDryWet() : 1.0f;
}
void Engine::rackSetPdc(int32_t t, int32_t di, bool b) {
    if (auto* r = rackCoreAt(t, di)) r->setRackPdc(b);
}
bool Engine::rackPdc(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r && r->rackPdc();
}
void Engine::rackSetChainSelect(int32_t t, int32_t di, float v) {
    if (auto* r = rackCoreAt(t, di)) r->setChainSelect(v);
}
float Engine::rackChainSelect(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainSelect() : 0.5f;
}
void Engine::rackSetSelFollow(int32_t t, int32_t di, bool b) {
    if (auto* r = rackCoreAt(t, di)) r->setSelFollow(b);
}
bool Engine::rackSelFollow(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r && r->selFollow();
}
float Engine::rackLiveSelector(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->liveSelector() : 0.5f;
}

// ---- macros + mappings ---------------------------------------------------
float Engine::rackMacroGet(int32_t t, int32_t di, int32_t m) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->macroGet(m) : 0.0f;
}
void Engine::rackMacroSet(int32_t t, int32_t di, int32_t m, float v) {
    if (auto* r = rackCoreAt(t, di)) r->macroSet(m, v);
}
int32_t Engine::rackAddMacroMapping(int32_t t, int32_t di, int32_t macro, int32_t chain, int32_t targetDevice,
    int32_t paramIndex, float rangeMin, float rangeMax) {
    auto* r = rackCoreAt(t, di);
    if (!r || macro < 0 || macro >= RackCore::kNumMacros) return -1;
    if (targetDevice != RackCore::kRackControl && (chain < 0 || chain >= r->chainCount())) return -1;
    return rackForEdit(t, di)->addMacroMapping(macro, chain, targetDevice, paramIndex, rangeMin, rangeMax);
}
int32_t Engine::rackMappingCount(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->mappingCount() : 0;
}
bool Engine::rackMappingInfo(int32_t t, int32_t di, int32_t index, int32_t& macro, int32_t& chain,
    int32_t& targetDevice, int32_t& paramIndex, float& rangeMin, float& rangeMax) const {
    auto* r = rackCoreAt(t, di);
    RackCore::MacroMapping m;
    if (!r || !r->mappingInfo(index, m)) return false;
    macro = m.macro;
    chain = m.chainIndex;
    targetDevice = m.deviceIndex;
    paramIndex = m.paramIndex;
    rangeMin = m.rangeMin;
    rangeMax = m.rangeMax;
    return true;
}
bool Engine::rackRemoveMapping(int32_t t, int32_t di, int32_t index) {
    auto* r = rackCoreAt(t, di);
    if (!r || index < 0 || index >= r->mappingCount()) return false;
    return rackForEdit(t, di)->removeMapping(index);
}

// ---- Instrument-Rack extras (zones / meter / names / rack out / macro-map) ----
void Engine::rackChainZone(
    int32_t t, int32_t di, int32_t c, int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const {
    auto* r = rackCoreAt(t, di);
    if (r) r->chainZone(c, keyLo, keyHi, velLo, velHi);
    else {
        keyLo = 0;
        keyHi = 127;
        velLo = 0;
        velHi = 127;
    }
}
void Engine::rackSetChainZone(
    int32_t t, int32_t di, int32_t c, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return;
    int32_t kl, kh, vl, vh;
    r->chainZone(c, kl, kh, vl, vh);
    auto cl = [](int32_t v) { return std::clamp(v, 0, 127); };
    if (kl == cl(keyLo) && kh == cl(keyHi) && vl == cl(velLo) && vh == cl(velHi)) return;
    rackForEdit(t, di)->setChainZone(c, keyLo, keyHi, velLo, velHi);
}
float Engine::rackChainMeter(int32_t t, int32_t di, int32_t c) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->chainMeter(c) : 0.0f;
}
std::string Engine::rackMacroName(int32_t t, int32_t di, int32_t m) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->macroName(m) : std::string{};
}
void Engine::rackSetMacroName(int32_t t, int32_t di, int32_t m, const std::string& name) {
    auto* r = rackCoreAt(t, di);
    if (!r || m < 0 || m >= RackCore::kNumMacros || r->macroName(m) == name) return;
    rackForEdit(t, di)->setMacroName(m, name); // undoable (specs/040)
}
float Engine::rackVolume(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->rackVolume() : 1.0f;
}
void Engine::rackSetVolume(int32_t t, int32_t di, float v) {
    if (auto* r = rackCoreAt(t, di)) r->setRackVolume(v);
}
float Engine::rackGlide(int32_t t, int32_t di) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->rackGlide() : 0.0f;
}
void Engine::rackSetGlide(int32_t t, int32_t di, float v) {
    if (auto* r = rackCoreAt(t, di)) r->setRackGlide(v);
}
// Range and curve edits fork the rack like any other rack edit, so they undo (specs/040);
// a range drag brackets itself in an undo group so the whole gesture is one step.
bool Engine::rackSetMappingRange(int32_t t, int32_t di, int32_t i, float lo, float hi) {
    auto* r = rackCoreAt(t, di);
    RackCore::MacroMapping m;
    if (!r || !r->mappingInfo(i, m)) return false;
    if (m.rangeMin == lo && m.rangeMax == hi) return true;
    return rackForEdit(t, di)->setMappingRange(i, lo, hi);
}
int32_t Engine::rackMappingCurve(int32_t t, int32_t di, int32_t i) const {
    auto* r = rackCoreAt(t, di);
    return r ? r->mappingCurve(i) : 0;
}
bool Engine::rackSetMappingCurve(int32_t t, int32_t di, int32_t i, int32_t curve) {
    auto* r = rackCoreAt(t, di);
    RackCore::MacroMapping m;
    if (!r || !r->mappingInfo(i, m)) return false;
    if (m.curve == std::clamp(curve, 0, 3)) return true;
    return rackForEdit(t, di)->setMappingCurve(i, curve);
}

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

    rack.setChainMute(0, true);
    rack.setChainMute(1, true);
    std::fill(buf.begin(), buf.end(), 0.0f);
    rack.render(buf.data(), frames);
    for (float s : buf)
        if (std::fabs(s) > 1e-6f) return false;
    rack.setChainMute(0, false);
    rack.setChainMute(1, false);

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
    auto fill = [&](float v) {
        for (auto& s : buf) s = v;
    };

    // Empty rack (no chains) is a pass-through.
    fill(0.5f);
    rack.process(buf.data(), frames);
    for (float s : buf)
        if (std::fabs(s - 0.5f) > 1e-6f) return false;

    // Two Utility chains at gain 1 sum a 0.25 input to ~0.5.
    if (rack.addChain(-1) != 0) return false; // effect chain (no instrument)
    if (rack.addChain(-1) != 1) return false;
    if (rack.chainCount() != 2) return false;
    if (rack.addChainDevice(0, 4) < 0) return false; // Utility on each chain
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
    if (!(peakAfter(36) > 0.001f)) return false; // pad A note plays
    if (!(peakAfter(38) > 0.001f)) return false; // pad B note plays
    // A note assigned to no pad makes no sound.
    rack.allNotesOff();
    rack.noteOn(60, 1.0f);
    float silent = 0.0f;
    for (int b = 0; b < 4; ++b) {
        std::fill(buf.begin(), buf.end(), 0.0f);
        rack.render(buf.data(), frames);
        for (float s : buf) silent = std::max(silent, std::fabs(s));
    }
    if (silent > 1e-6f) return false;

    auto blob = rack.getState();
    DrumRack rk2;
    rk2.setSampleRate(44100.0);
    rk2.setState(blob.data(), static_cast<int32_t>(blob.size()));
    if (rk2.chainTriggerNote(0) != 36 || rk2.chainTriggerNote(1) != 38) return false;

    // Snapshot lifetime (#177): a kit load commits dozens of times inside one audio block.
    // The snapshot a render is walking must outlive all of them, and go two renders later.
    SnapshotHistory<int> h;
    auto first = std::make_shared<int>(0);
    const std::weak_ptr<int> held = first;
    h.publish(std::move(first));
    {
        const SnapshotHistory<int>::RenderScope block(h);
        const int* walking = h.live();
        for (int i = 1; i <= 40; ++i) h.publish(std::make_shared<int>(i));
        if (held.expired() || *walking != 0) return false;
    }
    { const SnapshotHistory<int>::RenderScope block(h); }
    h.publish(std::make_shared<int>(41));
    if (!held.expired()) return false;
    return true;
}

} // namespace nota
