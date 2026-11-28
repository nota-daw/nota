// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackOps — group/ungroup/wrap/unwrap racks, reference rewriting, and Arp CC routing.

#include "automation/AutomationTypes.h"
#include "automation/Modulation.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "devices/RackInternal.h"
#include "devices/RackOps.h"
#include "model/Graph.h"
#include "model/ParamPath.h"
#include "model/Track.h"
#include "nota/devices/racks/DrumRack.h" // IWYU pragma: keep
#include "nota/devices/racks/MidiRack.h"
#include "nota/devices/racks/RackCore.h"
#include "nota/devices/racks/RackDevice.h"
#include "nota/devices/racks/RackInstrument.h"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace nota::detail {

// ---- group devices into a rack / ungroup it (#257, #328, specs/044) ----------
namespace {

// Group and Ungroup are each a function from old references to new ones (DeviceRef).
using Ref = DeviceRef;
using RefMap = DeviceRefMap;

Ref hostRef(int32_t host, uint64_t uid, int32_t slot) {
    if (isMidiHost(host)) return {1, midiHostIndex(host), uid, slot};
    if (host < 0) return {2, -1, uid, slot};
    return {0, host, uid, slot};
}

Ref laneRef(const TrackLane& l) {
    if (l.nested()) return hostRef(l.deviceIndex, l.chainUid, l.slot);
    switch (l.target) {
    case AutomationTarget::DeviceParam: return {0, l.deviceIndex};
    case AutomationTarget::MidiDeviceParam: return {1, l.deviceIndex};
    case AutomationTarget::PluginParam: return l.deviceIndex < 0 ? Ref{2, -1} : Ref{0, l.deviceIndex};
    default: return {};
    }
}
// The lane's target names the param surface of whatever it points at, and that surface is the
// same before and after a move into or out of a rack, so only the address changes.
void setLaneRef(TrackLane& l, const Ref& r) {
    l.chainUid = r.uid;
    l.slot = r.uid ? r.slot : -1;
    l.deviceIndex = r.uid ? hostOf(r) : r.idx;
}
Ref cvTargetRef(const CvLink& l) {
    if (l.targetChainUid) return hostRef(l.targetDevice, l.targetChainUid, l.targetSlot);
    if (l.targetKind == static_cast<int32_t>(CvTargetKind::Instrument)) return {2, -1};
    return {l.targetKind == static_cast<int32_t>(CvTargetKind::MidiFx) ? 1 : 0, l.targetDevice};
}
void setCvTargetRef(CvLink& l, const Ref& r) {
    l.targetChainUid = r.uid;
    l.targetSlot = r.uid ? r.slot : -1;
    l.targetDevice = r.uid ? hostOf(r) : r.idx;
}
Ref cvSourceRef(const CvLink& l) {
    if (l.sourceKind != static_cast<int32_t>(CvSourceKind::Param)) return {};
    return l.sourceChainUid ? hostRef(l.sourceDevice, l.sourceChainUid, l.sourceSlot) : Ref{0, l.sourceDevice};
}
void setCvSourceRef(CvLink& l, const Ref& r) {
    l.sourceChainUid = r.uid;
    l.sourceSlot = r.uid ? r.slot : -1;
    l.sourceDevice = r.uid ? hostOf(r) : r.idx;
}
Ref ccRef(const MidiDevice& m) {
    if (m.ccDestChainUid()) return hostRef(m.ccDestDevice(), m.ccDestChainUid(), m.ccDestSlot());
    return m.ccDestDevice() >= 0 ? Ref{0, m.ccDestDevice()} : Ref{};
}
bool sameRef(const Ref& a, const Ref& b) {
    return a.fam == b.fam && a.idx == b.idx && a.uid == b.uid && a.slot == b.slot;
}

} // namespace

// ---- Arp CC routes in the published snapshot (specs/044) ---------------------
void RackOps::syncCcRoutes(Graph& g) {
    auto sync = [&](Track& t) {
        if (!t.ccRoutesDirty) return; // published before: the audio thread may be reading it
        t.ccRoutesDirty = false;
        t.ccRoutes.reset();
        auto add = [&](const MidiDevice& m) {
            if (m.ccDestParam() < 0 || (m.ccDestDevice() < 0 && m.ccDestChainUid() == 0)) return;
            if (!t.ccRoutes) t.ccRoutes = std::make_unique<std::vector<Track::CcRoute>>();
            t.ccRoutes->push_back({&m, m.ccDestDevice(), m.ccDestChainUid(), m.ccDestSlot(), m.ccDestParam()});
        };
        for (auto& md : t.midiEffects) {
            if (!md) continue;
            add(*md);
            if (auto* r = md->asRack()) r->forEachChildMidi([&](MidiDevice& c) { add(c); });
        }
        if (auto* r = t.instrument ? t.instrument->asRack() : nullptr)
            r->forEachChildMidi([&](MidiDevice& c) { add(c); });
        if (t.ccRoutes) rt_.ccRoutesUsed_ = true;
    };
    for (auto& t : g.tracks) sync(*t);
    if (g.masterTrack) sync(*g.masterTrack);
}

void RackOps::reapplyCcRoutes() {
    if (!rt_.ccRoutesUsed_ || !rt_.authoring_) return;
    // Every Arp of the restored snapshot takes its route, and one without a route loses its
    // target (an ungroup that dropped it, then redo), or the next publish would revive it.
    auto apply = [](const Track& t) {
        auto one = [&](MidiDevice& m) {
            const Track::CcRoute* rt = nullptr;
            if (t.ccRoutes)
                for (auto& e : *t.ccRoutes)
                    if (e.arp == &m) {
                        rt = &e;
                        break;
                    }
            if (!rt) {
                if (m.ccDestDevice() >= 0 || m.ccDestChainUid() != 0) m.setCcDest(-1, -1);
                return;
            }
            if (m.ccDestDevice() == rt->dev && m.ccDestChainUid() == rt->uid && m.ccDestSlot() == rt->slot &&
                m.ccDestParam() == rt->param)
                return;
            m.setCcDest(rt->dev, rt->param);
            if (rt->uid) m.setCcDestNested(rt->uid, rt->slot);
        };
        for (auto& md : t.midiEffects) {
            if (!md) continue;
            one(*md);
            if (auto* r = md->asRack()) r->forEachChildMidi(one);
        }
        if (auto* r = t.instrument ? t.instrument->asRack() : nullptr) r->forEachChildMidi(one);
    };
    for (auto& t : rt_.authoring_->tracks) apply(*t);
    if (rt_.authoring_->masterTrack) apply(*rt_.authoring_->masterTrack);
}

// Every Arp inside the track's racks (a MIDI Effect Rack, the Instrument Rack's chains).
void RackOps::rewriteRackArps(Track& nt, const DeviceRefMap& map, const RackCore* fresh) {
    auto walk = [&](const RackCore& r) {
        if (&r == fresh) return; // its Arps were rewritten on the way in
        r.forEachChildMidi([&](MidiDevice& m) { rewriteArpRef(m, map); });
    };
    for (auto& md : nt.midiEffects)
        if (auto* r = md ? md->asRack() : nullptr) walk(*r);
    if (auto* r = nt.instrument ? nt.instrument->asRack() : nullptr) walk(*r);
}

// Retargets the Arp's authoring copy in place: a clone would start with no held notes or pending
// note-offs and interrupt a sustained pattern. The audio thread keeps playing the route of the
// snapshot it renders; the new one goes live with the published track (syncCcRoutes), and undo /
// redo copy a restored snapshot's routes back (reapplyCcRoutes).
void RackOps::rewriteArpRef(MidiDevice& m, const DeviceRefMap& map) {
    const Ref r = ccRef(m);
    if (r.fam < 0) return;
    const Ref n = map(r);
    if (sameRef(r, n)) return;
    if (n.fam == kDropRef) m.setCcDest(-1, -1);
    else {
        m.setCcDest(n.uid ? hostOf(n) : n.idx, m.ccDestParam());
        if (n.uid) m.setCcDestNested(n.uid, n.slot);
    }
}

// Rewrite every reference into `trackId` through `map` and publish the result as one undo
// step: lanes and CV sources of `nt` (the track's new state), CV targets on any track
// pointing at it, and the CC targets of the Arps on the track (in place, see rewriteArpRef).
void RackOps::publishWithRefMap(
    int32_t trackId, std::shared_ptr<Track> nt, const DeviceRefMap& map, const RackCore* fresh) {
    // Lanes.
    std::vector<int32_t> laneMap(nt->automation.size());
    std::vector<TrackLane> kept;
    kept.reserve(nt->automation.size());
    for (size_t i = 0; i < nt->automation.size(); ++i) {
        TrackLane l = nt->automation[i];
        const Ref r = laneRef(l);
        if (r.fam >= 0) {
            const Ref n = map(r);
            if (n.fam == kDropRef) {
                laneMap[i] = -1;
                continue;
            }
            setLaneRef(l, n);
        }
        laneMap[i] = static_cast<int32_t>(kept.size());
        kept.push_back(std::move(l));
    }
    nt->automation.replace(std::move(kept));
    auto laneGone = [&](int32_t& li) {
        if (li < 0 || li >= static_cast<int32_t>(laneMap.size())) return true;
        li = laneMap[static_cast<size_t>(li)];
        return li < 0;
    };
    std::erase_if(rt_.activeWrites_, [&](ActiveWrite& aw) { return aw.trackId == trackId && laneGone(aw.laneIndex); });
    std::erase_if(rt_.overrides_, [&](OverrideTarget& o) { return o.trackId == trackId && laneGone(o.laneIndex); });
    for (auto& m : nt->midiEffects)
        if (m) rewriteArpRef(*m, map);
    rewriteRackArps(*nt, map, fresh);
    // CV links, on this track and on any track whose links target it.
    auto rewriteLinks = [&](Track& t, bool own) {
        std::vector<CvLink> keep;
        keep.reserve(t.cvLinks.size());
        for (CvLink l : t.cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? t.id() : l.targetTrack;
            if (tgt == trackId) {
                const Ref n = map(cvTargetRef(l));
                if (n.fam == kDropRef) continue;
                setCvTargetRef(l, n);
            }
            if (own) {
                const Ref r = cvSourceRef(l);
                if (r.fam >= 0) {
                    const Ref n = map(r);
                    if (n.fam == kDropRef) continue;
                    setCvSourceRef(l, n);
                }
            }
            keep.push_back(l);
        }
        t.cvLinks.replace(std::move(keep));
    };
    rewriteLinks(*nt, true);
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    if (trackId == kMasterTrackId) g->masterTrack = nt;
    g->tracks.reserve(rt_.authoring_->tracks.size());
    for (auto& t : rt_.authoring_->tracks) {
        if (t->id() == trackId) {
            g->tracks.push_back(nt);
            continue;
        }
        bool targets = false;
        for (auto& l : t->cvLinks) targets |= l.targetTrack == trackId;
        if (!targets) {
            g->tracks.push_back(t);
            continue;
        }
        auto ot = rt_.cloneTrack(*t);
        rewriteLinks(*ot, false);
        g->tracks.push_back(ot);
    }
    rt_.publish(std::move(g));
}

int32_t RackOps::groupDevices(
    int32_t trackId, int32_t midiFirst, int32_t midiCount, bool withInstrument, int32_t fxFirst, int32_t fxCount) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -2;
    const int32_t nMidi = static_cast<int32_t>(old->midiEffects.size());
    const int32_t nFx = static_cast<int32_t>(old->devices.size());
    midiCount = std::max(0, midiCount);
    fxCount = std::max(0, fxCount);
    if (midiCount + fxCount == 0 && !withInstrument) return -2;
    if (midiCount && (midiFirst < 0 || midiFirst + midiCount > nMidi)) return -2;
    if (fxCount && (fxFirst < 0 || fxFirst + fxCount > nFx)) return -2;
    if ((midiCount || withInstrument) && (old->type() != TrackType::Instrument || !old->instrument)) return -7;
    if (withInstrument) {
        if (midiCount && midiFirst + midiCount != nMidi) return -3; // the MIDI run must reach the instrument
        if (fxCount && fxFirst != 0) return -3;                     // the audio run must start at it
    } else if (midiCount && fxCount)
        return -3; // MIDI and audio effects share a rack only with the instrument between them
    for (int32_t i = 0; i < fxCount; ++i) {
        auto& d = old->devices[static_cast<size_t>(fxFirst + i)];
        if (!d) return -2;
        if (dynamic_cast<const RackCore*>(d.get())) return -4;
    }
    for (int32_t i = 0; i < midiCount; ++i) {
        auto& m = old->midiEffects[static_cast<size_t>(midiFirst + i)];
        if (!m) return -2;
        if (dynamic_cast<const RackCore*>(m.get())) return -4;
        if (m->midiKind() < 0 || m->midiKind() > 5) return -5; // chains take the built-in MIDI effects
    }
    if (withInstrument) {
        auto* ri = dynamic_cast<const RackInstrument*>(old->instrument.get());
        if (ri && !dynamic_cast<const DrumRack*>(ri)) return -6; // an Instrument Rack never nests
    }

    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    auto nt = rt_.cloneTrack(*old);
    uint64_t uid = 0;
    const RackCore* freshRack = nullptr; // its Arps are rewritten on the way in
    int32_t result = 0;
    RefMap map;
    if (withInstrument) {
        // Instrument Rack: chain 0 = MIDI tail → instrument → audio head; the track keeps the rest.
        auto rack = std::make_shared<RackInstrument>();
        freshRack = rack.get();
        rack->setSampleRate(sr);
        const int32_t c = rack->chainCount() > 0 ? 0 : rack->addChain(-1); // a MIDI Effect Rack starts with one
        uid = rack->chainBaseUid(c);
        const int32_t m0 = midiFirst, f0 = fxCount;
        map = [=](const Ref& r) -> Ref {
            if (r.uid) return r.fam == 0 && r.idx >= f0 ? Ref{0, r.idx - f0, r.uid, r.slot} : r;
            if (r.fam == 2) return {2, -1, uid, -1};
            if (r.fam == 1 && midiCount && r.idx >= m0) return {2, -1, uid, RackCore::midiTarget(r.idx - m0)};
            if (r.fam == 0) return r.idx < f0 ? Ref{2, -1, uid, r.idx} : Ref{0, r.idx - f0};
            return r;
        };
        for (int32_t i = 0; i < midiCount; ++i) {
            auto& m = nt->midiEffects[static_cast<size_t>(midiFirst + i)];
            if (m) rewriteArpRef(*m, map); // an Arp going in keeps driving its (maybe moved) target
            rack->addChainMidiDeviceInstance(c, m);
        }
        rack->replaceChainInstrument(c, nt->instrument, /*prepare=*/false); // no chain name: it shows the instrument's
        for (int32_t i = 0; i < fxCount; ++i)
            rack->addChainDeviceInstance(c, nt->devices[static_cast<size_t>(i)], /*prepare=*/false);
        if (midiCount > 0) // midiFirst is -1 with no MIDI effects: begin() - 1 isn't an iterator
            nt->midiEffects.erase(nt->midiEffects.begin() + midiFirst, nt->midiEffects.begin() + midiFirst + midiCount);
        nt->devices.erase(nt->devices.begin(), nt->devices.begin() + fxCount);
        if (old->instrument) old->instrument->closeEditor();
        nt->instrument = rack;
        result = -1;
    } else if (midiCount) {
        auto rack = std::make_shared<MidiRack>();
        freshRack = rack.get();
        rack->setSampleRate(sr, kMaxBlock);
        const int32_t c = rack->chainCount() > 0 ? 0 : rack->addChain(-1); // a MIDI Effect Rack starts with one
        uid = rack->chainBaseUid(c);
        const int32_t a = midiFirst, n = midiCount;
        map = [=](const Ref& r) -> Ref {
            if (r.fam != 1) return r;
            if (!r.uid && r.idx >= a && r.idx < a + n) return {1, a, uid, RackCore::midiTarget(r.idx - a)};
            return r.idx >= a + n ? Ref{1, r.idx - n + 1, r.uid, r.slot} : r;
        };
        for (int32_t i = 0; i < n; ++i) {
            auto& m = nt->midiEffects[static_cast<size_t>(a + i)];
            if (m) rewriteArpRef(*m, map);
            rack->addChainMidiDeviceInstance(c, m);
        }
        rack->setChainName(
            c, nt->midiEffects[static_cast<size_t>(a)]->displayName()); // named after its first device (FR-015)
        nt->midiEffects.erase(nt->midiEffects.begin() + a, nt->midiEffects.begin() + a + n);
        nt->midiEffects.insert(nt->midiEffects.begin() + a, rack);
        result = kMidiRackBase - a;
    } else {
        auto rack = std::make_shared<RackDevice>();
        freshRack = rack.get();
        rack->setSampleRate(sr, kMaxBlock);
        const int32_t c = rack->chainCount() > 0 ? 0 : rack->addChain(-1); // a MIDI Effect Rack starts with one
        uid = rack->chainBaseUid(c);
        const int32_t a = fxFirst, n = fxCount;
        map = [=](const Ref& r) -> Ref {
            if (r.fam != 0) return r;
            if (!r.uid && r.idx >= a && r.idx < a + n) return {0, a, uid, r.idx - a};
            return r.idx >= a + n ? Ref{0, r.idx - n + 1, r.uid, r.slot} : r;
        };
        // The devices move in already prepared at this rate: preparing them again would reset
        // them (a Reverb clears its tail) while the published graph may still be playing them.
        for (int32_t i = 0; i < n; ++i)
            rack->addChainDeviceInstance(c, nt->devices[static_cast<size_t>(a + i)], /*prepare=*/false);
        rack->setChainName(
            c, nt->devices[static_cast<size_t>(a)]->displayName()); // named after its first device (FR-015)
        nt->devices.erase(nt->devices.begin() + a, nt->devices.begin() + a + n);
        nt->devices.insert(nt->devices.begin() + a, rack);
        result = a;
    }
    publishWithRefMap(trackId, nt, map, freshRack);
    rt_.devices().recomputePdc();
    return result;
}

int32_t RackOps::ungroupRack(int32_t trackId, int32_t family, int32_t index, int32_t* outDropped) {
    if (outDropped) *outDropped = 0;
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old) return -2;
    RackCore* rack = nullptr;
    if (family == 0 && index >= 0 && index < static_cast<int32_t>(old->devices.size()))
        rack = dynamic_cast<RackDevice*>(old->devices[static_cast<size_t>(index)].get());
    else if (family == 1 && index >= 0 && index < static_cast<int32_t>(old->midiEffects.size()))
        rack = dynamic_cast<MidiRack*>(old->midiEffects[static_cast<size_t>(index)].get());
    else if (family == 2 && old->instrument) {
        if (dynamic_cast<DrumRack*>(old->instrument.get())) return -4;
        rack = dynamic_cast<RackInstrument*>(old->instrument.get());
    }
    RackCore::RackState* st = rack ? rack->live() : nullptr;
    if (!st) return -2;
    if (st->chains.size() != 1) return -3;
    const RackCore::RackChain& ch = st->chains[0];
    if (family == 2 && !ch.instrument) return -5;
    int32_t dropped = st->mappings.empty() ? 0 : 1;
    {
        const auto& k = *ch.ctl;
        constexpr auto rl = std::memory_order_relaxed;
        const bool mixDefault = std::fabs(k.gain.load(rl) - 1.0f) < 1e-6f && std::fabs(k.pan.load(rl)) < 1e-6f &&
                                !k.mute.load(rl) && !k.solo.load(rl) && k.keyLo.load(rl) == 0 &&
                                k.keyHi.load(rl) == 127 && k.velLo.load(rl) == 0 && k.velHi.load(rl) == 127 &&
                                k.transpose.load(rl) == 0 && std::fabs(k.velScale.load(rl) - 1.0f) < 1e-6f &&
                                k.selLo.load(rl) == 0 && k.selHi.load(rl) == 127;
        if (!mixDefault) dropped |= 2;
    }
    const uint64_t uid = ch.uid;
    auto devs = ch.devices;
    auto mids = ch.midiDevices;
    auto inst = ch.instrument;

    auto nt = rt_.cloneTrack(*old);
    RefMap map;
    int32_t restored = 0;
    if (family == 0) {
        const int32_t a = index, n = static_cast<int32_t>(devs.size());
        map = [=](const Ref& r) -> Ref {
            if (r.fam != 0) return r;
            if (r.idx == a) return r.uid == uid && r.slot >= 0 ? Ref{0, a + r.slot} : Ref{kDropRef}; // its macros go
            return r.idx > a ? Ref{0, r.idx + n - 1, r.uid, r.slot} : r;
        };
        nt->devices.erase(nt->devices.begin() + a);
        nt->devices.insert(nt->devices.begin() + a, devs.begin(), devs.end());
        restored = n;
    } else if (family == 1) {
        const int32_t a = index, n = static_cast<int32_t>(mids.size());
        map = [=](const Ref& r) -> Ref {
            if (r.fam != 1) return r;
            if (r.idx == a)
                return r.uid == uid && RackCore::isMidiTarget(r.slot) ? Ref{1, a + RackCore::midiTargetIndex(r.slot)}
                                                                      : Ref{kDropRef};
            return r.idx > a ? Ref{1, r.idx + n - 1, r.uid, r.slot} : r;
        };
        nt->midiEffects.erase(nt->midiEffects.begin() + a);
        nt->midiEffects.insert(nt->midiEffects.begin() + a, mids.begin(), mids.end());
        restored = n;
    } else {
        const int32_t m0 = static_cast<int32_t>(nt->midiEffects.size()), nA = static_cast<int32_t>(devs.size());
        map = [=](const Ref& r) -> Ref {
            if (r.fam == 2) {
                if (r.uid != uid) return Ref{kDropRef}; // the rack's own macros
                if (r.slot == -1) return {2, -1};
                if (RackCore::isMidiTarget(r.slot)) return {1, m0 + RackCore::midiTargetIndex(r.slot)};
                return {0, r.slot};
            }
            if (r.fam == 0) return {0, r.idx + nA, r.uid, r.slot};
            return r;
        };
        nt->midiEffects.insert(nt->midiEffects.end(), mids.begin(), mids.end());
        nt->devices.insert(nt->devices.begin(), devs.begin(), devs.end());
        old->instrument->closeEditor();
        nt->instrument = inst;
        restored = static_cast<int32_t>(mids.size()) + 1 + nA;
    }
    publishWithRefMap(trackId, nt, map); // the MIDI effects taken out are among nt's now
    rt_.devices().recomputePdc();
    if (outDropped) *outDropped = dropped;
    return restored;
}

int32_t RackOps::wrapDeviceInRack(int32_t trackId, int32_t deviceIndex) {
    const int32_t r = groupDevices(trackId, -1, 0, false, deviceIndex, 1);
    return r < 0 ? -1 : r;
}

int32_t RackOps::unwrapRack(int32_t trackId, int32_t deviceIndex) {
    const int32_t r = ungroupRack(trackId, 0, deviceIndex, nullptr);
    return r < 0 ? -1 : r;
}

} // namespace nota::detail
