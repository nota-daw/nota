// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DeviceOps — device clipboard, track-to-track transfer, multi-paste, MIDI FX & CC.

#include "automation/AutomationOps.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "devices/DeviceOps.h"
#include "devices/RackOps.h"
#include "model/EngineTypes.h"
#include "model/Graph.h"
#include "model/ParamPath.h"
#include "model/Track.h"
#include "nota/devices/DeviceIds.h"
#include "nota/devices/racks/MidiRack.h"
#include "nota/dsp/DeviceRegistry.h"
#include "render/HybridBufferController.h"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

// ---- device clipboard (#238) ---------------------------------------------
// Copy holds a clone, not the device itself: a cut device can still be pasted, and later
// edits to the source don't reach the copy. Paste inserts another clone, so the clipboard
// can be pasted again; each paste is one undo step.
bool DeviceOps::copyDevice(int32_t trackId, int32_t deviceIndex) {
    Device* src = deviceAt(trackId, deviceIndex);
    auto c = src ? cloneDevice(*src) : nullptr;
    if (!c) return false;
    rt_.clipDevice_ = std::move(c);
    rt_.clipMidi_.reset();
    rt_.clipInstrument_.reset();
    rt_.clipMany_.clear();
    return true;
}

bool DeviceOps::copyMidiEffect(int32_t trackId, int32_t index) {
    MidiDevice* src = midiDeviceAt(trackId, index);
    auto c = src ? cloneMidiDevice(*src) : nullptr;
    if (!c) return false;
    rt_.clipMidi_ = std::move(c);
    rt_.clipDevice_.reset();
    rt_.clipInstrument_.reset();
    rt_.clipMany_.clear();
    return true;
}

bool DeviceOps::copyInstrument(int32_t trackId) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Instrument || !t->instrument) return false;
    auto c = cloneInstrumentFrom(*t->instrument);
    if (!c) return false;
    c->setBypassed(t->instrument->bypassed());
    rt_.clipInstrument_ = std::move(c);
    rt_.clipDevice_.reset();
    rt_.clipMidi_.reset();
    rt_.clipMany_.clear();
    return true;
}

int32_t DeviceOps::pasteInstrument(int32_t trackId) {
    if (!rt_.clipInstrument_) return -1;
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || t->type() != TrackType::Instrument) return -2;
    auto c = cloneInstrumentFrom(*rt_.clipInstrument_);
    if (!c) return -3;
    c->setBypassed(rt_.clipInstrument_->bypassed());
    return replaceTrackInstrument(trackId, std::move(c)) ? 0 : -2;
}

// ---- device transfer between tracks (specs/043) ----------------------------
// Both tracks change in one published graph, so the device never plays on two tracks (or
// none) and one undo puts both back. A move hands the device itself over (a plug-in that won't
// load twice still moves, a Reverb keeps its tail); a copy, or a device whose settings must
// change on the way (a key on the target itself, an Arp's CC target), gets a clone, so the
// object the undo history holds is never edited.
int32_t DeviceOps::transferDevice(
    int32_t srcId, int32_t family, int32_t index, int32_t dstId, int32_t atIndex, bool keepSource, int32_t* outFlags) {
    if (outFlags) *outFlags = 0;
    if (srcId == dstId || family < 0 || family > 2) return -1;
    auto src = rt_.findTrackAuthoring(srcId), dst = rt_.findTrackAuthoring(dstId);
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
        for (auto& ot : rt_.authoring_->tracks)
            for (auto& l : ot->cvLinks) {
                const int32_t tgt = l.targetTrack < 0 ? ot->id() : l.targetTrack;
                if ((tgt == srcId && l.targetKind == tk && l.targetDevice == index) ||
                    (!midi && ot->id() == srcId && l.sourceKind == static_cast<int32_t>(CvSourceKind::Param) &&
                        l.sourceDevice == index))
                    flags |= kTransferLinksDropped;
            }
    }

    auto ns = (keepSource || inst) ? nullptr : rt_.cloneTrack(*src);
    auto nd = rt_.cloneTrack(*dst);
    std::vector<TrackLane> lanes;
    if (!keepSource) lanes = rt_.automation().deviceLanes(*src, family, index);
    int32_t at = 0;

    if (family == 0) {
        auto& sd = src->devices[static_cast<size_t>(index)];
        std::shared_ptr<Device> dev = keepSource ? cloneDevice(*sd) : sd;
        if (!dev) return -3;
        if (dev->sidechainSourceTrackId() == dstId) {
            if (dev == sd) {
                dev = cloneDevice(*sd);
                if (dev) dev->inheritLineage(sd->lineage()); // the moved device itself, not a copy
            }
            if (!dev) return -3;
            dev->setSidechainSourceTrackId(-1);
            dev->setSidechainPart(0);
            flags |= kTransferSidechainCleared;
        }
        if (ns) {
            ns->devices.erase(ns->devices.begin() + index);
            rt_.automation().remapAutomationAfterDeviceChange(*ns, false, index, -1, -1);
            // An Arp's CC lane addresses the chain by index: drop it from the moved device,
            // shift it past the gap.
            for (auto& m : ns->midiEffects) {
                const int32_t d = m->ccDestDevice();
                if (d < index) continue;
                auto c = cloneMidiDevice(*m);
                if (!c) continue;
                c->inheritLineage(m->lineage()); // the same Arp, retargeted (take retry finds it)
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
        if (at != end) rt_.automation().remapAutomationAfterDeviceChange(*nd, false, -1, end, at);
    } else if (midi) {
        auto& sm = src->midiEffects[static_cast<size_t>(index)];
        std::shared_ptr<MidiDevice> md = keepSource ? cloneMidiDevice(*sm) : sm;
        if (!md) return -3;
        if (md->ccDestDevice() >= 0) { // its CC target is a device on the old track
            if (md == sm) {
                md = cloneMidiDevice(*sm);
                if (md) md->inheritLineage(sm->lineage()); // the moved device itself, not a copy
            }
            if (!md) return -3;
            md->setCcDest(-1, -1);
            flags |= kTransferLinksDropped;
        }
        if (ns) {
            ns->midiEffects.erase(ns->midiEffects.begin() + index);
            rt_.automation().remapAutomationAfterDeviceChange(*ns, true, index, -1, -1);
        }
        const int32_t end = static_cast<int32_t>(nd->midiEffects.size());
        at = (atIndex < 0 || atIndex > end) ? end : atIndex;
        nd->midiEffects.insert(nd->midiEffects.begin() + at, md);
        if (at != end) rt_.automation().remapAutomationAfterDeviceChange(*nd, true, -1, end, at);
    } else {
        std::shared_ptr<Instrument> in = keepSource ? cloneInstrumentFrom(*src->instrument) : src->instrument;
        if (!in) return -3;
        if (keepSource) in->setBypassed(src->instrument->bypassed());
        if (dst->instrument) dst->instrument->closeEditor();
        nd->instrument = in;
    }
    for (auto& l : lanes) {
        if (!inst)
            l.deviceIndex =
                l.nested() && family == 1 ? midiHost(at) : at; // a MIDI rack's nested lanes keep its encoding
        // An instrument lane for a parameter the new track already automates takes that lane's
        // place, so there is one lane per parameter (edits and recording find the first one).
        TrackLane* same = nullptr;
        auto& dstLanes = nd->automation.write();
        if (inst && !l.nested())
            for (auto& d : dstLanes)
                if (!d.nested() && d.target == AutomationTarget::PluginParam && d.deviceIndex < 0 &&
                    (l.paramId.empty() ? d.paramId.empty() && d.paramIndex == l.paramIndex : d.paramId == l.paramId)) {
                    same = &d;
                    break;
                }
        if (same) {
            same->paramIndex = l.paramIndex;
            same->points = std::move(l.points);
        } else
            dstLanes.push_back(std::move(l));
    }

    const bool dropSource = inst && !keepSource;
    if (dropSource) { // the track goes with its effects; it survives in undo history
        for (auto& d : src->devices)
            if (d) d->closeEditor();
        flags |= kTransferSourceRemoved;
    }
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    if (srcId == kMasterTrackId && ns) g->masterTrack = ns;
    if (dstId == kMasterTrackId) g->masterTrack = nd;
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        if (t->id() == srcId) {
            if (dropSource) continue;
            g->tracks.push_back(ns ? ns : t);
        } else
            g->tracks.push_back(t->id() == dstId ? nd : t);
    }
    rt_.publish(std::move(g));
    // CV links into either chain follow the indices: links to the moved device are dropped on the
    // old track, the devices after it shift, and on the new track the ones after the insert shift.
    if (!inst) {
        const int32_t last = static_cast<int32_t>(midi ? nd->midiEffects.size() : nd->devices.size()) - 1;
        if (ns) rt_.automation().remapCvLinksAfterDeviceChange(srcId, index, -1, -1, midi);
        if (at != last) rt_.automation().remapCvLinksAfterDeviceChange(dstId, -1, last, at, midi);
    }
    // Voices belong to the audio thread: ask it to silence them (AR-4). The old track loses the
    // arp-driven notes of a moved MIDI effect; a moved instrument drops the notes it held there.
    auto notesOff = [this](int32_t trackId) {
        Command c;
        c.type = CommandType::TrackNotesOff;
        c.i0 = trackId;
        rt_.commands_.push(c);
    };
    if (midi && ns) notesOff(srcId);
    if (inst && !keepSource) notesOff(dstId);
    recomputePdc();
    if (outFlags) *outFlags = flags;
    return inst ? 0 : at;
}

int32_t DeviceOps::deviceClipboardKind() const {
    if (!rt_.clipMany_.empty()) return 4;
    return rt_.clipDevice_ ? 1 : rt_.clipMidi_ ? 2 : rt_.clipInstrument_ ? 3 : 0;
}

int32_t DeviceOps::deviceClipboardCount() const {
    if (!rt_.clipMany_.empty()) return static_cast<int32_t>(rt_.clipMany_.size());
    return rt_.clipDevice_ || rt_.clipMidi_ || rt_.clipInstrument_ ? 1 : 0;
}

// ---- several devices on the clipboard (specs/044) ----------------------------
int32_t DeviceOps::copyDevices(
    int32_t trackId, const int32_t* midiIdx, int32_t midiN, bool withInstrument, const int32_t* fxIdx, int32_t fxN) {
    midiN = midiIdx ? std::max(0, midiN) : 0;
    fxN = fxIdx ? std::max(0, fxN) : 0;
    const int32_t total = midiN + fxN + (withInstrument ? 1 : 0);
    if (total == 0) return -1;
    if (total == 1) { // one device: the single clipboard, as before
        const bool ok = midiN ? copyMidiEffect(trackId, midiIdx[0])
                        : fxN ? copyDevice(trackId, fxIdx[0])
                              : copyInstrument(trackId);
        return ok ? 1 : -1;
    }
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return -1;
    std::vector<int32_t> mi(midiIdx, midiIdx + midiN), fi(fxIdx, fxIdx + fxN);
    std::sort(mi.begin(), mi.end());
    std::sort(fi.begin(), fi.end());
    std::vector<ClipEntry> out;
    for (int32_t i : mi) {
        MidiDevice* m = i >= 0 && i < static_cast<int32_t>(t->midiEffects.size())
                            ? t->midiEffects[static_cast<size_t>(i)].get()
                            : nullptr;
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
        Device* d =
            i >= 0 && i < static_cast<int32_t>(t->devices.size()) ? t->devices[static_cast<size_t>(i)].get() : nullptr;
        auto c = d ? cloneDevice(*d) : nullptr;
        if (!c) return -1;
        out.push_back({nullptr, nullptr, std::move(c)});
    }
    rt_.clipMany_ = std::move(out);
    rt_.clipDevice_.reset();
    rt_.clipMidi_.reset();
    rt_.clipInstrument_.reset();
    return total;
}

int32_t DeviceOps::pasteDevices(int32_t trackId, int32_t midiAt, int32_t fxAt, int32_t* outSkipped) {
    if (outSkipped) *outSkipped = 0;
    std::vector<ClipEntry> src = rt_.clipMany_;
    if (src.empty()) {
        if (rt_.clipMidi_) src.push_back({rt_.clipMidi_, nullptr, nullptr});
        else if (rt_.clipInstrument_)
            src.push_back({nullptr, rt_.clipInstrument_, nullptr});
        else if (rt_.clipDevice_)
            src.push_back({nullptr, nullptr, rt_.clipDevice_});
    }
    if (src.empty()) return -1;
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    const bool instTrack = old->type() == TrackType::Instrument;
    // Clone everything first, so a plug-in that won't load leaves the track untouched.
    std::vector<std::shared_ptr<MidiDevice>> mids;
    std::shared_ptr<Instrument> inst;
    std::vector<std::shared_ptr<Device>> devs;
    int32_t skipped = 0;
    for (auto& e : src) {
        if (e.midi) {
            if (!instTrack) {
                ++skipped;
                continue;
            }
            auto c = cloneMidiDevice(*e.midi);
            if (!c) return -3;
            c->setCcDest(-1, -1); // its CC target named a device on the track it came from
            mids.push_back(std::move(c));
        } else if (e.inst) {
            if (!instTrack) {
                ++skipped;
                continue;
            }
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
    auto nt = rt_.cloneTrack(*old);
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
    rt_.racks().publishWithRefMap(trackId, nt, map);
    recomputePdc();
    return pasted;
}

bool DeviceOps::deviceClipboardSidechain() const {
    return rt_.clipDevice_ && rt_.clipDevice_->sidechainSourceTrackId() >= 0;
}

int32_t DeviceOps::pasteDevice(int32_t trackId, int32_t atIndex) {
    return rt_.clipDevice_ ? insertDeviceClone(trackId, cloneDevice(*rt_.clipDevice_), atIndex) : -1;
}

int32_t DeviceOps::duplicateDevice(int32_t trackId, int32_t deviceIndex) {
    Device* src = deviceAt(trackId, deviceIndex);
    return src ? insertDeviceClone(trackId, cloneDevice(*src), deviceIndex + 1) : -1;
}

int32_t DeviceOps::insertDeviceClone(int32_t trackId, std::shared_ptr<Device> dev, int32_t atIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || !dev) return -1;
    auto nt = rt_.cloneTrack(*old);
    const int32_t end = static_cast<int32_t>(nt->devices.size());
    const int32_t at = (atIndex < 0 || atIndex > end) ? end : atIndex;
    nt->devices.insert(nt->devices.begin() + at, dev);
    // The lanes, CV links and Arp CC targets on the devices after it shift up; the copy has none.
    rt_.racks().publishWithRefMap(trackId, nt, [at](const DeviceRef& r) -> DeviceRef {
        return r.fam == 0 && r.idx >= at ? DeviceRef{0, r.idx + 1, r.uid, r.slot} : r;
    });
    recomputePdc();
    return at;
}

int32_t DeviceOps::pasteMidiEffect(int32_t trackId, int32_t atIndex) {
    return rt_.clipMidi_ ? insertMidiEffectClone(trackId, cloneMidiDevice(*rt_.clipMidi_), atIndex) : -1;
}

int32_t DeviceOps::duplicateMidiEffect(int32_t trackId, int32_t index) {
    MidiDevice* src = midiDeviceAt(trackId, index);
    return src ? insertMidiEffectClone(trackId, cloneMidiDevice(*src), index + 1) : -1;
}

int32_t DeviceOps::insertMidiEffectClone(int32_t trackId, std::shared_ptr<MidiDevice> md, int32_t atIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || !md || old->type() != TrackType::Instrument) return -1;
    auto nt = rt_.cloneTrack(*old);
    const int32_t end = static_cast<int32_t>(nt->midiEffects.size());
    const int32_t at = (atIndex < 0 || atIndex > end) ? end : atIndex;
    nt->midiEffects.insert(nt->midiEffects.begin() + at, md);
    // As for an audio effect; a duplicated Arp's own target is rewritten with the rest.
    rt_.racks().publishWithRefMap(trackId, nt, [at](const DeviceRef& r) -> DeviceRef {
        return r.fam == 1 && r.idx >= at ? DeviceRef{1, r.idx + 1, r.uid, r.slot} : r;
    });
    return at;
}

int32_t DeviceOps::addTrackMidiEffect(int32_t trackId, int32_t kind) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    auto md = dsp::DeviceRegistry::createMidiEffect(kind);
    if (!md) return -1;
    md->setSampleRate(rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0, kMaxBlock);
    auto nt = rt_.cloneTrack(*old);
    nt->midiEffects.push_back(md);
    const int32_t idx = static_cast<int32_t>(nt->midiEffects.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    return idx;
}

bool DeviceOps::moveMidiEffect(int32_t trackId, int32_t fromIndex, int32_t toIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    const int32_t n = static_cast<int32_t>(old->midiEffects.size());
    if (fromIndex < 0 || fromIndex >= n) return false;
    if (toIndex < 0) toIndex = 0;
    if (toIndex >= n) toIndex = n - 1;
    if (toIndex == fromIndex) return true;
    auto nt = rt_.cloneTrack(*old);
    auto md = nt->midiEffects[static_cast<size_t>(fromIndex)];
    nt->midiEffects.erase(nt->midiEffects.begin() + fromIndex);
    nt->midiEffects.insert(nt->midiEffects.begin() + toIndex, md);
    rt_.automation().remapAutomationAfterDeviceChange(*nt, true, -1, fromIndex, toIndex);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

bool DeviceOps::removeMidiEffect(int32_t trackId, int32_t index) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || index < 0 || index >= static_cast<int32_t>(old->midiEffects.size())) return false;
    auto nt = rt_.cloneTrack(*old);
    nt->midiEffects.erase(nt->midiEffects.begin() + index);
    rt_.automation().remapAutomationAfterDeviceChange(*nt, true, index, -1, -1);
    rt_.republishWithTrack(trackId, nt);
    if (nt->instrument) nt->instrument->allNotesOff(); // drop any arp-driven voices
    return true;
}

int32_t DeviceOps::trackMidiEffectCount(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->midiEffects.size()) : 0;
}

MidiDevice* DeviceOps::midiDeviceAt(int32_t trackId, int32_t index) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->midiEffects.size())) return nullptr;
    return t->midiEffects[static_cast<size_t>(index)].get();
}

int32_t DeviceOps::midiEffectKind(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->midiKind() : -1;
}
const char* DeviceOps::midiEffectName(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->displayName() : "";
}
int32_t DeviceOps::midiEffectParamCount(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->paramCount() : 0;
}
const char* DeviceOps::midiEffectParamName(int32_t t, int32_t i, int32_t p) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->paramName(p) : "";
}
float DeviceOps::midiEffectParamMin(int32_t t, int32_t i, int32_t p) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->paramMin(p) : 0.0f;
}
float DeviceOps::midiEffectParamMax(int32_t t, int32_t i, int32_t p) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->paramMax(p) : 1.0f;
}
float DeviceOps::midiEffectGetParam(int32_t t, int32_t i, int32_t p) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->getParam(p) : 0.0f;
}
void DeviceOps::midiEffectSetParam(int32_t t, int32_t i, int32_t p, float v) {
    rt_.automation().routeCvBaseEdit(2, t, i, p, v);
    if (auto* m = midiDeviceAt(t, i)) m->setParam(p, v);
    if (rt_.hybrid_) rt_.hybrid_->invalidate(true);
}
std::vector<uint8_t> DeviceOps::midiEffectGetState(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->getState() : std::vector<uint8_t>{};
}
bool DeviceOps::midiEffectSetState(int32_t t, int32_t i, const uint8_t* data, int32_t size, bool checkpoint) {
    auto old = rt_.findTrackAuthoring(t);
    if (!old || !data || size <= 0 || i < 0 || i >= static_cast<int32_t>(old->midiEffects.size()) ||
        !old->midiEffects[static_cast<size_t>(i)])
        return false;
    auto fresh = cloneMidiDevice(*old->midiEffects[static_cast<size_t>(i)]);
    if (!fresh) return false;
    fresh->setState(data, size);
    fresh->inheritLineage(old->midiEffects[static_cast<size_t>(i)]->lineage()); // the same device to take retry
    // A MIDI Effect Rack's copy plays on with the held-note tables, so it ends what the old one
    // started instead of leaving it hanging.
    if (auto* to = dynamic_cast<MidiRack*>(fresh.get()))
        if (auto* from = dynamic_cast<const MidiRack*>(old->midiEffects[static_cast<size_t>(i)].get()))
            to->shareRuntimeWith(*from);
    auto nt = rt_.cloneTrack(*old);
    nt->midiEffects[static_cast<size_t>(i)] = std::move(fresh);
    if (checkpoint) rt_.republishWithTrack(t, nt);
    else
        rt_.republishWithTrackRaw(t, nt);
    return true;
}
void DeviceOps::setMidiEffectBypassed(int32_t t, int32_t i, bool b) {
    if (auto* m = midiDeviceAt(t, i)) m->setBypassed(b);
}
bool DeviceOps::midiEffectBypassed(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->bypassed() : false;
}
void DeviceOps::setMidiEffectFolded(int32_t t, int32_t i, bool f) {
    if (auto* m = midiDeviceAt(t, i)) m->setFolded(f);
}
bool DeviceOps::midiEffectFolded(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->folded() : false;
}
int32_t DeviceOps::midiEffectLastIn(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->midiLastIn() : -1;
}
int32_t DeviceOps::midiEffectLastOut(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->midiLastOut() : -1;
}
int32_t DeviceOps::midiEffectScope(int32_t t, int32_t i, float* out, int32_t maxN) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->midiScope(out, maxN) : 0;
}
void DeviceOps::midiEffectCommand(int32_t t, int32_t i, int32_t cmd) {
    if (auto* m = midiDeviceAt(t, i)) m->midiCommand(cmd);
}
void DeviceOps::setMidiEffectCcDest(int32_t t, int32_t i, int32_t dev, int32_t param) {
    auto* m = midiDeviceAt(t, i);
    if (!m) return;
    m->setCcDest(dev, param);
    republishCcRoutes(t);
}

// The audio thread plays the routes of the published track (Track::ccRoutes): after a live CC
// target change, publish the track again so the route follows (no undo step, as before).
void DeviceOps::republishCcRoutes(int32_t trackId) {
    // The first target ever set turns route syncing on. Until then no Arp has a target, so the
    // tracks skipped meanwhile have no route to lose (their empty lists stay as they are).
    rt_.ccDestsSet_ = true;
    if (auto old = rt_.findTrackAuthoring(trackId)) rt_.republishWithTrackRaw(trackId, rt_.cloneTrack(*old));
}
// The Arp's CC target inside a rack (specs/044), for save / load: (chain index, slot) in the
// rack at its ccDestDevice; set it after setMidiEffectCcDest.
bool DeviceOps::midiEffectCcDestPath(int32_t t, int32_t i, int32_t* chainIndex, int32_t* slot) const {
    auto* m = midiDeviceAt(t, i);
    if (!m) return false;
    const uint64_t uid = m->ccDestChainUid();
    if (chainIndex) *chainIndex = uid ? rt_.automation().nestedChainIndex(t, m->ccDestDevice(), uid) : -1;
    if (slot) *slot = uid ? m->ccDestSlot() : -1;
    return true;
}
bool DeviceOps::setMidiEffectCcDestPath(int32_t t, int32_t i, int32_t chainIndex, int32_t slot) {
    auto* m = midiDeviceAt(t, i);
    if (!m) return false;
    const uint64_t uid = rt_.automation().nestedChainUid(t, m->ccDestDevice(), chainIndex);
    if (!uid) return false;
    m->setCcDestNested(uid, slot);
    republishCcRoutes(t);
    return true;
}
void DeviceOps::setMidiEffectCcDepth(int32_t t, int32_t i, float d) {
    if (auto* m = midiDeviceAt(t, i)) m->setCcDepth(d);
}
int32_t DeviceOps::midiEffectCcDestDevice(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->ccDestDevice() : -2;
}
int32_t DeviceOps::midiEffectCcDestParam(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->ccDestParam() : -1;
}
float DeviceOps::midiEffectCcDepth(int32_t t, int32_t i) const {
    auto* m = midiDeviceAt(t, i);
    return m ? m->ccDepth() : 0.0f;
}

} // namespace nota::detail
