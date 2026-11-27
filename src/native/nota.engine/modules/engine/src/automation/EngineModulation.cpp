// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AutomationOps — CV modulator CRUD, CV link routing, and device-edit fix-ups.

#include "automation/AutomationOps.h"
#include "automation/Modulation.h"
#include "automation/ModulationInternal.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "model/Graph.h"
#include "model/ParamPath.h"
#include "model/ParamResolve.h"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace nota::detail {

// ---- helpers ---------------------------------------------------------------

const Modulator* AutomationOps::modulatorPtr(const Track& t, int32_t modId) {
    for (auto& m : t.modulators)
        if (m.id == modId) return &m;
    return nullptr;
}

// ---- modulator CRUD (message thread, structural) ---------------------------

int32_t AutomationOps::addModulator(int32_t trackId, int32_t kind) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    auto nt = rt_.cloneTrack(*old);
    Modulator m;
    m.id = nt->nextModId++;
    m.kind = static_cast<ModulatorKind>(kind);
    if (m.kind == ModulatorKind::Scope) m.scope = std::make_shared<ScopeHistory>();
    const int32_t id = m.id;
    nt->modulators.write().push_back(m);
    rt_.republishWithTrack(trackId, nt);
    return id;
}

bool AutomationOps::removeModulator(int32_t trackId, int32_t modId) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return false;
    auto nt = rt_.cloneTrack(*old);
    auto& mods = nt->modulators.write();
    const size_t before = mods.size();
    mods.erase(std::remove_if(mods.begin(), mods.end(), [&](const Modulator& m) { return m.id == modId; }), mods.end());
    if (mods.size() == before) return false;
    // Restore + drop links fed by the removed modulator (leave param-source links alone).
    auto fedBy = [&](const CvLink& l) {
        return l.sourceKind == static_cast<int32_t>(CvSourceKind::Modulator) && l.sourceModId == modId;
    };
    for (auto& l : old->cvLinks)
        if (fedBy(l)) restoreLinkTarget(trackId, l);
    auto& links = nt->cvLinks.write();
    links.erase(std::remove_if(links.begin(), links.end(), fedBy), links.end());
    rt_.republishWithTrack(trackId, nt);
    return true;
}

int32_t AutomationOps::modulatorCount(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->modulators.size()) : 0;
}

int32_t AutomationOps::modulatorIdAt(int32_t trackId, int32_t index) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->modulators.size())) return -1;
    return t->modulators[static_cast<size_t>(index)].id;
}

int32_t AutomationOps::modulatorKind(int32_t trackId, int32_t modId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return -1;
    for (auto& m : t->modulators)
        if (m.id == modId) return static_cast<int32_t>(m.kind);
    return -1;
}

float AutomationOps::modulatorGet(int32_t trackId, int32_t modId, int32_t field) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return 0.0f;
    for (auto& m : t->modulators)
        if (m.id == modId) return m.get(static_cast<ModField>(field));
    return 0.0f;
}

void AutomationOps::modulatorSet(int32_t trackId, int32_t modId, int32_t field, float value) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return;
    if (auto* m = modulatorPtr(*t, modId)) m->set(static_cast<ModField>(field), value);
}

float AutomationOps::modulatorValue(int32_t trackId, int32_t modId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return 0.0f;
    const double spb = rt_.transport_.samplesPerBeat();
    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    const double samples = static_cast<double>(rt_.transport_.playheadSamples());
    const double beat = spb > 0 ? samples / spb : 0.0;
    for (auto& m : t->modulators)
        if (m.id == modId) return m.eval(beat, samples / sr);
    return 0.0f;
}

// ---- CV link CRUD ----------------------------------------------------------

// ---- modulation target read/write (device / instrument / MIDI-FX) ----------

// Generalized link creation. Source: modulator or param. Target: device / instrument / MIDI-FX.
int32_t AutomationOps::addCvLinkFull(int32_t trackId, int32_t sourceKind, int32_t modId, int32_t srcDev,
    int32_t srcParam, int32_t targetKind, int32_t targetTrack, int32_t targetDevice, int32_t targetParam) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    if (sourceKind == static_cast<int32_t>(CvSourceKind::Param)) {
        if (srcDev < 0 || srcDev >= static_cast<int32_t>(old->devices.size())) return -1;
        if (const auto* source = old->devices[static_cast<size_t>(srcDev)].get();
            source && source->isLatencyConfigurationParam(srcParam))
            return -1;
    } else if (modulatorKind(trackId, modId) < 0)
        return -1;
    const int32_t tgt = targetTrack < 0 ? trackId : targetTrack;
    auto tgtTrack = rt_.findTrackAuthoring(tgt);
    if (!tgtTrack || !targetValidOrPending(*tgtTrack, targetKind, targetDevice, targetParam)) return -1;
    const int32_t storedTarget = tgt == trackId ? -1 : tgt;
    for (auto& l : old->cvLinks)
        if (l.sourceChainUid == 0 && l.targetChainUid == 0 && l.sourceKind == sourceKind && l.sourceModId == modId &&
            l.sourceDevice == srcDev && l.sourceParam == srcParam && l.targetKind == targetKind &&
            l.targetTrack == storedTarget && l.targetDevice == targetDevice && l.targetParam == targetParam)
            return -1;
    auto nt = rt_.cloneTrack(*old);
    CvLink l;
    l.sourceKind = sourceKind;
    l.sourceModId = modId;
    l.sourceDevice = srcDev;
    l.sourceParam = srcParam;
    l.targetKind = targetKind;
    l.targetTrack = storedTarget;
    l.targetDevice = targetDevice;
    l.targetParam = targetParam;
    float mn, mx;
    l.base.store(targetGet(*tgtTrack, targetKind, targetDevice, targetParam, mn, mx), std::memory_order_relaxed);
    auto& links = nt->cvLinks.write();
    links.push_back(l);
    const int32_t idx = static_cast<int32_t>(links.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    return idx;
}

// A link reaching into a rack (specs/044), for project load: as addCvLinkFull with the source
// and / or target given as (device, chain index, slot); chain index -1 = track level.
int32_t AutomationOps::addNestedCvLink(int32_t trackId, int32_t sourceKind, int32_t modId, int32_t srcDev,
    int32_t srcChain, int32_t srcSlot, int32_t srcParam, int32_t targetKind, int32_t targetTrack, int32_t targetDevice,
    int32_t tgtChain, int32_t tgtSlot, int32_t targetParam) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -1;
    const int32_t tgt = targetTrack < 0 ? trackId : targetTrack;
    auto tgtTrack = rt_.findTrackAuthoring(tgt);
    if (!tgtTrack) return -1;
    const uint64_t su = srcChain >= 0 ? nestedChainUid(trackId, srcDev, srcChain) : 0;
    const uint64_t tu = tgtChain >= 0 ? nestedChainUid(tgt, targetDevice, tgtChain) : 0;
    if ((srcChain >= 0 && !su) || (tgtChain >= 0 && !tu)) return -1;
    if (sourceKind == static_cast<int32_t>(CvSourceKind::Param) && !su &&
        (srcDev < 0 || srcDev >= static_cast<int32_t>(old->devices.size())))
        return -1;
    if (sourceKind == static_cast<int32_t>(CvSourceKind::Param)) {
        const auto source = targetOwner(*old, 0, srcDev, su, srcSlot);
        if (source.device && source.device->isLatencyConfigurationParam(srcParam)) return -1;
    }
    if (!targetValidOrPending(*tgtTrack, targetKind, targetDevice, targetParam, tu, tgtSlot)) return -1;
    auto nt = rt_.cloneTrack(*old);
    CvLink l;
    l.sourceKind = sourceKind;
    l.sourceModId = modId;
    l.sourceDevice = srcDev;
    l.sourceParam = srcParam;
    l.sourceChainUid = su;
    l.sourceSlot = su ? srcSlot : -1;
    l.targetKind = targetKind;
    l.targetTrack = tgt == trackId ? -1 : tgt;
    l.targetDevice = targetDevice;
    l.targetParam = targetParam;
    l.targetChainUid = tu;
    l.targetSlot = tu ? tgtSlot : -1;
    float mn, mx;
    l.base.store(
        targetGet(*tgtTrack, targetKind, targetDevice, targetParam, mn, mx, tu, tgtSlot), std::memory_order_relaxed);
    auto& links = nt->cvLinks.write();
    links.push_back(l);
    const int32_t idx = static_cast<int32_t>(links.size()) - 1;
    rt_.republishWithTrack(trackId, nt);
    return idx;
}

bool AutomationOps::cvLinkPath(
    int32_t trackId, int32_t index, int32_t* srcChain, int32_t* srcSlot, int32_t* tgtChain, int32_t* tgtSlot) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->cvLinks.size())) return false;
    const CvLink& l = t->cvLinks[static_cast<size_t>(index)];
    const int32_t tgt = l.targetTrack < 0 ? trackId : l.targetTrack;
    if (srcChain) *srcChain = l.sourceChainUid ? nestedChainIndex(trackId, l.sourceDevice, l.sourceChainUid) : -1;
    if (srcSlot) *srcSlot = l.sourceChainUid ? l.sourceSlot : -1;
    if (tgtChain) *tgtChain = l.targetChainUid ? nestedChainIndex(tgt, l.targetDevice, l.targetChainUid) : -1;
    if (tgtSlot) *tgtSlot = l.targetChainUid ? l.targetSlot : -1;
    return true;
}

int32_t AutomationOps::addCvLink(int32_t trackId, int32_t modId, int32_t device, int32_t param) {
    return addCvLinkFull(trackId, 0, modId, -1, -1, 0, -1, device, param);
}
int32_t AutomationOps::addCvLinkTo(int32_t trackId, int32_t modId, int32_t targetTrack, int32_t device, int32_t param) {
    return addCvLinkFull(trackId, 0, modId, -1, -1, 0, targetTrack, device, param);
}
int32_t AutomationOps::addCvLinkToTarget(int32_t trackId, int32_t modId, int32_t targetKind, int32_t targetTrack,
    int32_t targetDevice, int32_t targetParam) {
    return addCvLinkFull(trackId, 0, modId, -1, -1, targetKind, targetTrack, targetDevice, targetParam);
}

int32_t AutomationOps::addCvLinkFromParam(int32_t trackId, int32_t srcDevice, int32_t srcParam, int32_t targetTrack,
    int32_t targetDevice, int32_t targetParam) {
    return addCvLinkFull(trackId, 1, 0, srcDevice, srcParam, 0, targetTrack, targetDevice, targetParam);
}
int32_t AutomationOps::addCvLinkFromParamToTarget(int32_t trackId, int32_t srcDevice, int32_t srcParam,
    int32_t targetKind, int32_t targetTrack, int32_t targetDevice, int32_t targetParam) {
    return addCvLinkFull(trackId, 1, 0, srcDevice, srcParam, targetKind, targetTrack, targetDevice, targetParam);
}

// Return a link's target param to its base (so it doesn't stick at the last modulated
// value once the link is gone). Targets are shared across the clone, so this is live.
void AutomationOps::restoreLinkTarget(int32_t ownerTrack, const CvLink& l) {
    const int32_t tgt = l.targetTrack < 0 ? ownerTrack : l.targetTrack;
    if (auto tt = rt_.findTrackAuthoring(tgt))
        if (targetValid(*tt, l.targetKind, l.targetDevice, l.targetParam, l.targetChainUid, l.targetSlot))
            targetSet(*tt, l.targetKind, l.targetDevice, l.targetParam, l.base.load(std::memory_order_relaxed),
                l.targetChainUid, l.targetSlot);
}

bool AutomationOps::removeCvLink(int32_t trackId, int32_t index) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || index < 0 || index >= static_cast<int32_t>(old->cvLinks.size())) return false;
    restoreLinkTarget(trackId, old->cvLinks[static_cast<size_t>(index)]);
    auto nt = rt_.cloneTrack(*old);
    auto& links = nt->cvLinks.write();
    links.erase(links.begin() + index);
    rt_.republishWithTrack(trackId, nt);
    return true;
}

int32_t AutomationOps::cvLinkCount(int32_t trackId) const {
    auto t = rt_.findTrackAuthoring(trackId);
    return t ? static_cast<int32_t>(t->cvLinks.size()) : 0;
}

static const CvLink* linkAt(const std::shared_ptr<Track>& t, int32_t index) {
    if (!t || index < 0 || index >= static_cast<int32_t>(t->cvLinks.size())) return nullptr;
    return &t->cvLinks[static_cast<size_t>(index)];
}

int32_t AutomationOps::cvLinkSource(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->sourceModId : -1;
}
int32_t AutomationOps::cvLinkSourceKind(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->sourceKind : 0;
}
int32_t AutomationOps::cvLinkSourceDevice(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->sourceDevice : -1;
}
int32_t AutomationOps::cvLinkSourceParam(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->sourceParam : -1;
}
int32_t AutomationOps::cvLinkTargetKind(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->targetKind : 0;
}
int32_t AutomationOps::cvLinkTargetTrack(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->targetTrack : -1;
}
int32_t AutomationOps::cvLinkDevice(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->targetDevice : -1;
}
int32_t AutomationOps::cvLinkParam(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->targetParam : -1;
}
float AutomationOps::cvLinkDepth(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->depth.load(std::memory_order_relaxed) : 0.0f;
}
int32_t AutomationOps::cvLinkMode(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->mode.load(std::memory_order_relaxed) : 0;
}
void AutomationOps::setCvLinkDepth(int32_t trackId, int32_t index, float depth) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->cvLinks.size())) return;
    t->cvLinks[static_cast<size_t>(index)].depth.store(std::clamp(depth, -1.0f, 1.0f), std::memory_order_relaxed);
}
void AutomationOps::setCvLinkMode(int32_t trackId, int32_t index, int32_t mode) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->cvLinks.size())) return;
    t->cvLinks[static_cast<size_t>(index)].mode.store(mode, std::memory_order_relaxed);
}
float AutomationOps::cvLinkBase(int32_t trackId, int32_t index) const {
    auto* l = linkAt(rt_.findTrackAuthoring(trackId), index);
    return l ? l->base.load(std::memory_order_relaxed) : 0.0f;
}
void AutomationOps::setCvLinkBase(int32_t trackId, int32_t index, float base) {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t || index < 0 || index >= static_cast<int32_t>(t->cvLinks.size())) return;
    t->cvLinks[static_cast<size_t>(index)].base.store(base, std::memory_order_relaxed);
}

bool AutomationOps::deviceParamModulated(int32_t trackId, int32_t device, int32_t param) const {
    return paramModulated(0, trackId, device, param);
}
bool AutomationOps::paramModulated(int32_t targetKind, int32_t trackId, int32_t device, int32_t param) const {
    if (!rt_.authoring_) return false;
    // Modulated if any track's link targets (kind, trackId, device, param) — including
    // cross-track links owned by another track.
    for (auto& op : rt_.authoring_->tracks) {
        const int32_t owner = op->id();
        for (auto& l : op->cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? owner : l.targetTrack;
            if (tgt == trackId && l.targetChainUid == 0 && l.targetKind == targetKind && l.targetDevice == device &&
                l.targetParam == param)
                return true;
        }
    }
    return false;
}

// A UI edit of a modulated target sets the link's base (the atomic is driven by the
// modulation each block). Called from the param setters; returns nothing.
void AutomationOps::routeCvBaseEdit(int32_t targetKind, int32_t trackId, int32_t device, int32_t param, float value) {
    if (!rt_.authoring_) return;
    for (auto& op : rt_.authoring_->tracks) {
        const int32_t owner = op->id();
        for (auto& l : op->cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? owner : l.targetTrack;
            if (tgt == trackId && l.targetChainUid == 0 && l.targetKind == targetKind && l.targetDevice == device &&
                l.targetParam == param)
                l.base.store(value, std::memory_order_relaxed);
        }
    }
}

void AutomationOps::routeNestedCvBaseEdit(
    int32_t trackId, int32_t rack, int32_t chain, int32_t slot, int32_t param, float value) {
    if (!rt_.authoring_) return;
    const uint64_t uid = nestedChainUid(trackId, rack, chain);
    if (!uid) return;
    for (auto& op : rt_.authoring_->tracks) {
        const int32_t owner = op->id();
        for (auto& l : op->cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? owner : l.targetTrack;
            if (tgt == trackId && l.targetChainUid == uid && l.targetDevice == rack && l.targetSlot == slot &&
                l.targetParam == param)
                l.base.store(value, std::memory_order_relaxed);
        }
    }
}

bool AutomationOps::cvBaseValue(int32_t targetKind, int32_t trackId, int32_t device, int32_t param, float& out) const {
    if (!rt_.authoring_) return false;
    for (auto& op : rt_.authoring_->tracks) {
        const int32_t owner = op->id();
        for (auto& l : op->cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? owner : l.targetTrack;
            if (tgt == trackId && l.targetChainUid == 0 && l.targetKind == targetKind && l.targetDevice == device &&
                l.targetParam == param) {
                out = l.base.load(std::memory_order_relaxed);
                return true;
            }
        }
    }
    return false;
}

// ---- device edit → CV-link index fix-up (message thread) -------------------

// New index of `idx` after the device at `from` is moved to `to` (vector erase+insert).
static int32_t remappedMoveIndex(int32_t idx, int32_t from, int32_t to) {
    if (idx == from) return to;
    if (from < to) {
        if (idx > from && idx <= to) return idx - 1;
    } else {
        if (idx >= to && idx < from) return idx + 1;
    }
    return idx;
}

void AutomationOps::remapCvLinksAfterDeviceChange(
    int32_t track, int32_t removedIndex, int32_t from, int32_t to, bool midi) {
    if (!rt_.authoring_) return;
    const auto chainKind = static_cast<int32_t>(midi ? CvTargetKind::MidiFx : CvTargetKind::Device);
    bool anyChange = false;
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& ot : rt_.authoring_->tracks) {
        const bool ownerIsEdited = ot->id() == track;
        bool needs = false;
        for (auto& l : ot->cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? ot->id() : l.targetTrack;
            const bool paramSrcHere =
                !midi && ownerIsEdited && l.sourceKind == static_cast<int32_t>(CvSourceKind::Param);
            if (tgt == track || paramSrcHere) {
                needs = true;
                break;
            }
        }
        if (!needs) {
            g->tracks.push_back(ot);
            continue;
        }
        auto nt = rt_.cloneTrack(*ot);
        std::vector<CvLink> kept;
        kept.reserve(nt->cvLinks.size());
        for (CvLink l : nt->cvLinks) {
            bool drop = false;
            // Target index fix-up (links into the edited chain of the edited track).
            const int32_t tgt = l.targetTrack < 0 ? nt->id() : l.targetTrack;
            // A target inside a rack (specs/044) follows the rack: targetDevice is its address.
            const bool nestedTgt = l.targetChainUid != 0;
            const bool tgtOnChain =
                nestedTgt ? (midi ? isMidiHost(l.targetDevice) : l.targetDevice >= 0) : l.targetKind == chainKind;
            if (tgt == track && tgtOnChain) {
                int32_t idx = nestedTgt && midi ? midiHostIndex(l.targetDevice) : l.targetDevice;
                if (removedIndex >= 0) {
                    if (idx == removedIndex) drop = true;
                    else if (idx > removedIndex)
                        idx -= 1;
                } else
                    idx = remappedMoveIndex(idx, from, to);
                l.targetDevice = nestedTgt && midi ? midiHost(idx) : idx;
            }
            // Source index fix-up (param source lives on the owner = edited track; a nested source
            // inside the Instrument Rack has no chain index to follow).
            if (!drop && !midi && ownerIsEdited && l.sourceKind == static_cast<int32_t>(CvSourceKind::Param) &&
                (l.sourceChainUid == 0 || l.sourceDevice >= 0)) {
                if (removedIndex >= 0) {
                    if (l.sourceDevice == removedIndex) drop = true;
                    else if (l.sourceDevice > removedIndex)
                        l.sourceDevice -= 1;
                } else
                    l.sourceDevice = remappedMoveIndex(l.sourceDevice, from, to);
            }
            if (!drop) kept.push_back(l);
        }
        nt->cvLinks.replace(std::move(kept));
        anyChange = true;
        g->tracks.push_back(nt);
    }
    if (anyChange) rt_.publishRaw(std::move(g)); // no extra undo step: part of the device edit
}

} // namespace nota::detail
