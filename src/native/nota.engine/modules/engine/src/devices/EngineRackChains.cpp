// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackOps — chain CRUD, clipboard copy/paste/duplicate/move, Mosaic/Grain/Sampler,
// and chain audio/MIDI effect devices.

#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "devices/DeviceTypes.h"
#include "devices/RackInternal.h"
#include "devices/RackOps.h"
#include "nota/devices/DeviceIds.h"
#include "nota/devices/racks/DrumRack.h" // IWYU pragma: keep
#include "nota/devices/racks/MidiRack.h"
#include "nota/devices/racks/RackCore.h"
#include "nota/devices/racks/RackDevice.h"
#include "nota/dsp/DeviceRegistry.h"
#include "nota/dsp/capabilities/IMosaicInstrument.h"
#include "nota/dsp/capabilities/ISampleInstrument.h"
#include "render/HybridBufferController.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

// ---- chains --------------------------------------------------------------
int32_t RackOps::chainCount(int32_t t, int32_t di) const {
    auto* r = coreAt(t, di);
    return r ? r->chainCount() : 0;
}
int32_t RackOps::duplicateChain(int32_t t, int32_t di, int32_t c) {
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return -1;
    if (!isNestedRack(di) && di <= kMidiRackBase && r->chainCount() >= MidiRack::kMaxChains) return -1;
    if (di == -1) {
        // The copy goes in after the source and the chains below move down: refuse when that
        // would push a chain's MIDI effects past the first 16 chains, where they don't run.
        const int32_t n = r->chainCount();
        for (int32_t j = c; j < n; ++j)
            if (r->chainMidiDeviceCount(j) > 0 && j + 1 >= RackInstrument::kMaxMidiChains) return -1;
    }
    auto* fork = forEdit(t, di);
    const int32_t at = fork ? fork->duplicateChain(c) : -1;
    if (at < 0) return at;
    // The copy keeps its children's keys and Arp targets: give them their routes.
    rt_.devices().recomputeRouting();
    if (rt_.ccDestsSet_) rt_.devices().republishCcRoutes(t);
    return at;
}
bool RackOps::checkpoint(int32_t t, int32_t di) {
    return forEdit(t, di) != nullptr;
}
int64_t RackOps::chainId(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? static_cast<int64_t>(r->chainUid(c)) : 0;
}

// ---- chain devices on the device clipboard (specs/042) ----------------------------------
namespace {
// Which sections a rack's chains have: a MIDI Effect Rack only MIDI effects, an Audio Effect
// Rack and a Drum Rack only audio effects, an Instrument Rack both.
bool rackTakesAudio(const RackCore* r) {
    return !dynamic_cast<const MidiRack*>(r);
}
bool rackTakesMidi(const RackCore* r) {
    if (dynamic_cast<const MidiRack*>(r)) return true;
    return dynamic_cast<const RackInstrument*>(r) && !dynamic_cast<const DrumRack*>(r);
}
} // namespace

bool RackOps::chainDeviceCopy(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = coreAt(t, di);
    Device* src = r ? r->chainDeviceAt(c, i) : nullptr;
    auto copy = src ? rt_.devices().cloneDevice(*src) : nullptr;
    if (!copy) return false;
    rt_.clipDevice_ = std::move(copy);
    rt_.clipMidi_.reset();
    rt_.clipInstrument_.reset();
    rt_.clipMany_.clear();
    return true;
}
bool RackOps::chainMidiCopy(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = coreAt(t, di);
    MidiDevice* src = r ? r->chainMidiDeviceAt(c, i) : nullptr;
    auto copy = src ? rt_.devices().cloneMidiDevice(*src) : nullptr;
    if (!copy) return false;
    rt_.clipMidi_ = std::move(copy);
    rt_.clipDevice_.reset();
    rt_.clipInstrument_.reset();
    rt_.clipMany_.clear();
    return true;
}
int32_t RackOps::chainDevicePaste(int32_t t, int32_t di, int32_t c, int32_t at) {
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return -1;
    if (!rt_.clipDevice_) return rt_.clipMidi_ ? -2 : -1;
    if (!rackTakesAudio(r)) return -2;
    if (dynamic_cast<const RackDevice*>(rt_.clipDevice_.get())) return -3;
    auto dev = rt_.devices().cloneDevice(*rt_.clipDevice_);
    if (!dev) return -1;
    dev->setSidechainSourceTrackId(-1); // a pasted device starts without a key
    const uint64_t uid = r->chainBaseUid(c);
    const int32_t idx = forEdit(t, di)->insertChainDeviceInstance(c, std::move(dev), at);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotInserted(uid, idx, false));
    rt_.devices().recomputePdc();
    return idx;
}
int32_t RackOps::chainMidiPaste(int32_t t, int32_t di, int32_t c, int32_t at) {
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return -1;
    if (!rt_.clipMidi_) return rt_.clipDevice_ ? -2 : -1;
    if (!rackTakesMidi(r)) return -2;
    if (rt_.clipMidi_->midiKind() < 0 || rt_.clipMidi_->midiKind() > 5) return -3; // a MIDI Effect Rack
    if (di == -1 && c >= RackInstrument::kMaxMidiChains) return -5;
    auto dev = rt_.devices().cloneMidiDevice(*rt_.clipMidi_);
    if (!dev) return -1;
    const uint64_t uid = r->chainBaseUid(c);
    const int32_t idx = forEdit(t, di)->insertChainMidiDeviceInstance(c, std::move(dev), at);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotInserted(uid, idx, true));
    return idx;
}
int32_t RackOps::chainPasteDevices(int32_t t, int32_t di, int32_t c, int32_t midiAt, int32_t fxAt, int32_t* outSkipped,
    int32_t* outFirstMidi, int32_t* outFirstFx) {
    if (outSkipped) *outSkipped = 0;
    if (outFirstMidi) *outFirstMidi = -1;
    if (outFirstFx) *outFirstFx = -1;
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return -1;
    std::vector<ClipEntry> src = rt_.clipMany_;
    if (src.empty()) {
        if (rt_.clipMidi_) src.push_back({rt_.clipMidi_, nullptr, nullptr});
        else if (rt_.clipInstrument_)
            src.push_back({nullptr, rt_.clipInstrument_, nullptr});
        else if (rt_.clipDevice_)
            src.push_back({nullptr, nullptr, rt_.clipDevice_});
    }
    if (src.empty()) return -1;
    const bool takesMidi = rackTakesMidi(r) && !(di == -1 && c >= RackInstrument::kMaxMidiChains);
    const bool takesAudio = rackTakesAudio(r);
    // Clone what fits first: a plug-in that won't load leaves the chain as it was.
    std::vector<std::shared_ptr<MidiDevice>> mids;
    std::vector<std::shared_ptr<Device>> devs;
    int32_t skipped = 0;
    for (auto& e : src) {
        if (e.midi && takesMidi && e.midi->midiKind() >= 0 && e.midi->midiKind() <= 5) {
            auto m = rt_.devices().cloneMidiDevice(*e.midi);
            if (!m) return -3;
            m->setCcDest(-1, -1);
            mids.push_back(std::move(m));
        } else if (e.dev && takesAudio && !e.dev->asRack()) {
            auto d = rt_.devices().cloneDevice(*e.dev);
            if (!d) return -3;
            d->setSidechainSourceTrackId(-1); // a pasted device starts without a key
            devs.push_back(std::move(d));
        } else
            ++skipped;
    }
    if (outSkipped) *outSkipped = skipped;
    if (mids.empty() && devs.empty()) return 0;
    const uint64_t uid = r->chainBaseUid(c);
    RackCore* fr = forEdit(t, di); // one fork: one undo step for the whole paste
    if (!fr) return -1;
    int32_t ma = midiAt < 0 || midiAt > fr->chainMidiDeviceCount(c) ? fr->chainMidiDeviceCount(c) : midiAt;
    int32_t fa = fxAt < 0 || fxAt > fr->chainDeviceCount(c) ? fr->chainDeviceCount(c) : fxAt;
    // Every insert lands in the fork first: a remap republishes the track (and may fork a rack
    // holding an Arp), after which `fr` is no longer the rack in the graph.
    std::vector<std::pair<int32_t, bool>> inserted;
    for (size_t i = 0; i < mids.size(); ++i) {
        const int32_t idx = fr->insertChainMidiDeviceInstance(c, std::move(mids[i]), ma + static_cast<int32_t>(i));
        if (idx < 0) continue;
        if (outFirstMidi && *outFirstMidi < 0) *outFirstMidi = idx;
        inserted.push_back({idx, true});
    }
    for (size_t i = 0; i < devs.size(); ++i) {
        const int32_t idx = fr->insertChainDeviceInstance(c, std::move(devs[i]), fa + static_cast<int32_t>(i));
        if (idx < 0) continue;
        if (outFirstFx && *outFirstFx < 0) *outFirstFx = idx;
        inserted.push_back({idx, false});
    }
    for (auto [idx, midi] : inserted) remapNestedAfterRackEdit(t, di, slotInserted(uid, idx, midi));
    rt_.devices().recomputePdc();
    return static_cast<int32_t>(mids.size() + devs.size());
}
int32_t RackOps::chainDeviceDuplicate(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = coreAt(t, di);
    Device* src = r ? r->chainDeviceAt(c, i) : nullptr;
    auto dev = src ? rt_.devices().cloneDevice(*src) : nullptr;
    if (!dev) return -1;
    const uint64_t uid = r->chainBaseUid(c);
    const int32_t idx = forEdit(t, di)->insertChainDeviceInstance(c, std::move(dev), i + 1);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotInserted(uid, idx, false));
    rt_.devices().recomputePdc();
    return idx;
}
int32_t RackOps::chainMidiDuplicate(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = coreAt(t, di);
    MidiDevice* src = r ? r->chainMidiDeviceAt(c, i) : nullptr;
    auto dev = src ? rt_.devices().cloneMidiDevice(*src) : nullptr;
    if (!dev) return -1;
    const uint64_t uid = r->chainBaseUid(c);
    const int32_t idx = forEdit(t, di)->insertChainMidiDeviceInstance(c, std::move(dev), i + 1);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotInserted(uid, idx, true));
    return idx;
}
int32_t RackOps::chainDeviceMoveTo(int32_t t, int32_t di, int32_t c, int32_t i, int32_t to) {
    auto* r = coreAt(t, di);
    if (!r || !r->chainDeviceAt(c, i) || to < 0 || to >= r->chainCount() || to == c) return -1;
    const uint64_t uid = r->chainBaseUid(c), toUid = r->chainBaseUid(to);
    const int32_t idx = forEdit(t, di)->moveChainDeviceToChain(c, i, to);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotToChain(uid, i, toUid, idx, false));
    rt_.devices().recomputePdc();
    return idx;
}
int32_t RackOps::chainMidiMoveTo(int32_t t, int32_t di, int32_t c, int32_t i, int32_t to) {
    auto* r = coreAt(t, di);
    if (!r || !r->chainMidiDeviceAt(c, i) || to < 0 || to >= r->chainCount() || to == c) return -1;
    if (di == -1 && to >= RackInstrument::kMaxMidiChains) return -5;
    const uint64_t uid = r->chainBaseUid(c), toUid = r->chainBaseUid(to);
    const int32_t idx = forEdit(t, di)->moveChainMidiDeviceToChain(c, i, to);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotToChain(uid, i, toUid, idx, true));
    return idx;
}
bool RackOps::mono(int32_t t) const {
    auto* r = coreAt(t, -1);
    return r && r->rackMono();
}
void RackOps::setMono(int32_t t, bool on) {
    if (auto* r = coreAt(t, -1)) r->setRackMono(on);
}
int32_t RackOps::addChain(int32_t t, int32_t di, int32_t k) {
    // Audio Effect and MIDI Effect Rack chains hold no instrument, whatever kind is asked for
    // (MCP add_effect_rack_chain long passed 4, which is now the Drum Rack).
    if (!isNestedRack(di) && (di >= 0 || di <= kMidiRackBase)) k = -1;
    if (!kindAllowed(t, di, k)) return -1;
    if (auto* cur = coreAt(t, di);
        cur && !isNestedRack(di) && di <= kMidiRackBase && cur->chainCount() >= MidiRack::kMaxChains)
        return -1;
    auto* r = forEdit(t, di);
    return r ? r->addChain(k) : -1;
}
bool RackOps::removeChain(int32_t t, int32_t di, int32_t c) {
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return false;
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = forEdit(t, di)->removeChain(c);
    if (ok) remapNestedAfterRackEdit(t, di, chainDropped(uid, false));
    rt_.devices().recomputePdc(); // the chain may have held the rack's longest latency
    return ok;
}
bool RackOps::setChainInstrument(int32_t t, int32_t di, int32_t c, int32_t k) {
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || !kindAllowed(t, di, k) || !RackCore::makeInstrument(k)) return false;
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = forEdit(t, di)->setChainInstrument(c, k);
    if (ok) remapNestedAfterRackEdit(t, di, chainDropped(uid, /*instrumentOnly=*/true));
    rt_.devices().recomputePdc();
    return ok;
}
int32_t RackOps::chainInstrumentKind(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainInstrumentKind(c) : -2;
}
const char* RackOps::chainInstrumentName(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->displayName() : "";
}
int32_t RackOps::chainInstrumentParamCount(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->pluginParamCount() : 0;
}
std::string RackOps::chainInstrumentParamName(int32_t t, int32_t di, int32_t c, int32_t p) const {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->pluginParamName(p) : std::string{};
}
std::string RackOps::chainInstrumentParamId(int32_t t, int32_t di, int32_t c, int32_t p) const {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->pluginParamId(p) : std::string{};
}
float RackOps::chainInstrumentParamGet(int32_t t, int32_t di, int32_t c, int32_t p) const {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->pluginParamGet(p) : 0.0f;
}
void RackOps::chainInstrumentParamSet(int32_t t, int32_t di, int32_t c, int32_t p, float v) {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (!inst) return;
    inst->pluginParamSet(p, v);
    rt_.automation().routeNestedCvBaseEdit(t, di, c, -1, p, v); // a grouped, CV-linked instrument (specs/044)
}
float RackOps::chainInstrumentParamDefault(int32_t t, int32_t di, int32_t c, int32_t p) const {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (!inst) return 0.0f;
    if (inst->kind() < 0) return inst->pluginParamGet(p); // hosted plugin: no fresh default
    auto fresh = RackCore::makeInstrument(inst->kind());  // built-in default == a fresh one's value
    return fresh ? fresh->pluginParamGet(p) : inst->pluginParamGet(p);
}
int32_t RackOps::chainInstrumentScope(int32_t t, int32_t di, int32_t c, float* out, int32_t n) const {
    if (!out || n <= 0) return 0;
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->scopeRead(out, n) : 0;
}
int32_t RackOps::chainInstrumentVoiceCount(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->activeVoiceCount() : -1;
}
int32_t RackOps::chainInstrumentHeldNotes(int32_t t, int32_t di, int32_t c, int32_t* out, int32_t n) const {
    if (!out || n <= 0) return 0;
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->heldNotes(out, n) : 0;
}
void RackOps::chainInstrumentAction(int32_t t, int32_t di, int32_t c, int32_t id, int32_t iarg, float farg) {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (inst) rt_.devices().instrumentActionUndoable(*inst, t, di, c, id, iarg, farg);
}
void RackOps::openChainInstrumentEditor(int32_t t, int32_t di, int32_t c) {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (inst) {
        inst->openEditor();
        if (rt_.hybrid_ && rt_.hybrid_->mode() == HybridBufferMode::Auto) rt_.hybrid_->invalidate(true);
    }
}
void RackOps::openChainDeviceEditor(int32_t t, int32_t di, int32_t c, int32_t d) {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    if (dev) {
        dev->openEditor();
        if (rt_.hybrid_ && rt_.hybrid_->mode() == HybridBufferMode::Auto) rt_.hybrid_->invalidate(true);
    }
}
std::string RackOps::chainInstrumentPluginId(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    return inst ? inst->pluginIdentifier() : std::string{};
}
int32_t RackOps::chainInstrumentGetState(int32_t t, int32_t di, int32_t c, uint8_t* out, int32_t cap) const {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (!inst) return 0;
    std::vector<uint8_t> v = inst->getState();
    if (out && cap >= static_cast<int32_t>(v.size())) std::copy(v.begin(), v.end(), out);
    return static_cast<int32_t>(v.size());
}

// ---- chain Mosaic (built-in multisample instrument, specs/041) -------------
// Mirrors mosaicSetProgram & co. (Engine_Devices.cpp) for a chain instrument.
bool RackOps::chainMosaicSetProgram(int32_t t, int32_t di, int32_t c, const std::string& text, bool checkpoint) {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    auto* m = dynamic_cast<dsp::capabilities::IMosaicInstrument*>(inst);
    if (!m) return false;
    if (!checkpoint) { // live edit: in place, voices keep going
        m->setProgram(text);
        return true;
    }
    // An undoable edit puts a fresh Mosaic (params + the samples it already holds, so a zone
    // edit is ready at once) into a fork of the rack; undo brings the old one back.
    auto fresh = inst->clone();
    auto* freshMosaic = dynamic_cast<dsp::capabilities::IMosaicInstrument*>(fresh.get());
    if (!fresh || !freshMosaic) return false;
    freshMosaic->setProgram(text);
    fresh->presetTag = inst->presetTag; // still the preset it was loaded from (specs/048)
    auto* fr = forEdit(t, di);
    return fr && fr->replaceChainInstrument(c, std::move(fresh));
}
// ---- chain Grain sample (a factory Grain preset dropped on a rack, specs/041 review) --------
// Mirrors setTrackGrainSample: a fresh Grain with the chain's values and the sample goes into a
// fork of the rack, so undo brings the old one back.
bool RackOps::chainGrainSetSample(int32_t t, int32_t di, int32_t c, const std::string& path, int32_t rootNote) {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (!inst || inst->kind() != static_cast<int32_t>(devices::InstrumentKind::GrainSynth)) return false;
    auto sample = decodeAudioFile(path);
    if (!sample || sample->empty()) return false;
    auto fresh = dsp::DeviceRegistry::createInstrument(static_cast<int32_t>(devices::InstrumentKind::GrainSynth));
    auto* freshSmp = dynamic_cast<dsp::capabilities::ISampleInstrument*>(fresh.get());
    if (!fresh || !freshSmp) return false;
    fresh->setSampleRate(rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0);
    for (int i = 0; i < inst->pluginParamCount(); ++i) fresh->pluginParamSet(i, inst->pluginParamGet(i));
    freshSmp->setSample(sample, rootNote, false);
    fresh->presetTag = inst->presetTag; // still the preset it was loaded from (specs/048)
    auto* fr = forEdit(t, di);
    return fr && fr->replaceChainInstrument(c, std::move(fresh));
}

std::string RackOps::chainMosaicProgram(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    auto* m = r ? dynamic_cast<const dsp::capabilities::IMosaicInstrument*>(r->chainInstrument(c)) : nullptr;
    return m ? m->programText() : std::string{};
}
bool RackOps::chainMosaicStatus(int32_t t, int32_t di, int32_t c, MosaicStatus* out) const {
    auto* r = coreAt(t, di);
    auto* m = r ? dynamic_cast<dsp::capabilities::IMosaicInstrument*>(r->chainInstrument(c)) : nullptr;
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
int64_t RackOps::chainMosaicZoneSampleId(int32_t t, int32_t di, int32_t c, int32_t zone) const {
    auto* r = coreAt(t, di);
    auto* m = r ? dynamic_cast<const dsp::capabilities::IMosaicInstrument*>(r->chainInstrument(c)) : nullptr;
    auto b = m ? m->zoneSample(zone) : nullptr;
    return b ? b->id : 0;
}

// ---- chain Sampler (built-in) --------------------------------------------
// A chain's instrument may be a built-in Sampler; these mirror the track-level
// samplerInfo/setTrackSamplerRoot/samplerPlayPosition so the rich Sampler editor
// works per-chain (drum-rack pads + instrument-rack Sampler chains). The 17
// plugin-params + start/end/loop markers ride the generic rackChainInstrumentParam*.
bool RackOps::chainSamplerInfo(int32_t t, int32_t di, int32_t c, SamplerInfo* out) const {
    if (!out) return false;
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (!inst || inst->kind() != static_cast<int32_t>(devices::InstrumentKind::Sampler)) return false;
    auto* sm = dynamic_cast<const dsp::capabilities::ISampleInstrument*>(inst);
    if (!sm) return false;
    out->sampleId = sm->sample() ? sm->sample()->id : 0;
    out->rootNote = sm->rootNote();
    out->loop = sm->loopEnabled() ? 1 : 0;
    return true;
}
bool RackOps::setChainSamplerRoot(int32_t t, int32_t di, int32_t c, int32_t rootNote) {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (!inst || inst->kind() != static_cast<int32_t>(devices::InstrumentKind::Sampler)) return false;
    auto* sm = dynamic_cast<dsp::capabilities::ISampleInstrument*>(inst);
    if (!sm) return false;
    sm->setRoot(rootNote); // lock-free atomic; the sampler is shared across snapshots
    return true;
}
float RackOps::chainSamplerPlayPosition(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    Instrument* inst = r ? r->chainInstrument(c) : nullptr;
    if (!inst || inst->kind() != static_cast<int32_t>(devices::InstrumentKind::Sampler)) return -1.0f;
    auto* sm = dynamic_cast<const dsp::capabilities::ISampleInstrument*>(inst);
    return sm ? sm->playPosition() : -1.0f;
}

// ---- chain devices -------------------------------------------------------
int32_t RackOps::chainDeviceCount(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainDeviceCount(c) : 0;
}
int32_t RackOps::addChainDevice(int32_t t, int32_t di, int32_t c, int32_t k) {
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || !dsp::DeviceRegistry::hasChainEffect(k)) return -1;
    return forEdit(t, di)->addChainDevice(c, k);
}
bool RackOps::removeChainDevice(int32_t t, int32_t di, int32_t c, int32_t d) {
    auto* r = coreAt(t, di);
    if (!r || d < 0 || d >= r->chainDeviceCount(c)) return false;
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = forEdit(t, di)->removeChainDevice(c, d);
    if (ok) remapNestedAfterRackEdit(t, di, slotRemoved(uid, d, false));
    return ok;
}
bool RackOps::moveChainDevice(int32_t t, int32_t di, int32_t c, int32_t from, int32_t to) {
    auto* r = coreAt(t, di);
    if (!r || from < 0 || from >= r->chainDeviceCount(c)) return false;
    to = std::clamp(to, 0, r->chainDeviceCount(c) - 1);
    if (to == from) return true;
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = forEdit(t, di)->moveChainDevice(c, from, to);
    if (ok) remapNestedAfterRackEdit(t, di, slotMoved(uid, from, to, false));
    return ok;
}
const char* RackOps::chainDeviceName(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->displayName() : "";
}
int32_t RackOps::chainDeviceBuiltinKind(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->builtinKind() : -1;
}
int32_t RackOps::chainDeviceParamCount(int32_t t, int32_t di, int32_t c, int32_t d) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->paramCount() : 0;
}
const char* RackOps::chainDeviceParamName(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->paramName(p) : "";
}
float RackOps::chainDeviceParamMin(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->paramMin(p) : 0.0f;
}
float RackOps::chainDeviceParamMax(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->paramMax(p) : 1.0f;
}
float RackOps::chainDeviceParamGet(int32_t t, int32_t di, int32_t c, int32_t d, int32_t p) const {
    auto* r = coreAt(t, di);
    Device* dev = r ? r->chainDeviceAt(c, d) : nullptr;
    return dev ? dev->getParam(p) : 0.0f;
}
// ---- chain MIDI effects (MIDI Effect Rack chains; Instrument Rack chains, #329) ----------
int32_t RackOps::chainMidiCount(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainMidiDeviceCount(c) : 0;
}
int32_t RackOps::addChainMidi(int32_t t, int32_t di, int32_t c, int32_t kind) {
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || kind < 0 || kind > 5) return -1;
    if (dynamic_cast<DrumRack*>(r)) return -1;                      // pads keep their own note handling
    if (di == -1 && c >= RackInstrument::kMaxMidiChains) return -1; // MIDI effects run in the first 16 chains
    return forEdit(t, di)->addChainMidiDevice(c, kind);
}
bool RackOps::removeChainMidi(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = coreAt(t, di);
    if (!r || i < 0 || i >= r->chainMidiDeviceCount(c)) return false;
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = forEdit(t, di)->removeChainMidiDevice(c, i);
    if (ok) remapNestedAfterRackEdit(t, di, slotRemoved(uid, i, true));
    return ok;
}
bool RackOps::moveChainMidi(int32_t t, int32_t di, int32_t c, int32_t from, int32_t to) {
    auto* r = coreAt(t, di);
    if (!r || from < 0 || from >= r->chainMidiDeviceCount(c)) return false;
    to = std::clamp(to, 0, r->chainMidiDeviceCount(c) - 1);
    if (to == from) return true;
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = forEdit(t, di)->moveChainMidiDevice(c, from, to);
    if (ok) remapNestedAfterRackEdit(t, di, slotMoved(uid, from, to, true));
    return ok;
}
MidiDevice* RackOps::chainMidiAt(int32_t t, int32_t di, int32_t c, int32_t i) const {
    auto* r = coreAt(t, di);
    return r ? r->chainMidiDeviceAt(c, i) : nullptr;
}
void RackOps::setChainMidiBypassed(int32_t t, int32_t di, int32_t c, int32_t i, bool b) {
    if (auto* m = chainMidiAt(t, di, c, i)) m->setBypassed(b);
}

} // namespace nota::detail
