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
#include "ParamPath.h"
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
        r->setPresetTag(src->presetTag());
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
        r->setPresetTag(src->presetTag());
        r->setSidechainSourceTrackId(src->sidechainSourceTrackId());
        r->setSidechainPart(src->sidechainPart());
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
    r->presetTag = src.presetTag;
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

// ---- group devices into a rack / ungroup it (#257, #328, specs/044) ----------
namespace {

// Group and Ungroup are each a function from old references to new ones (Engine::DeviceRef).
using Ref = Engine::DeviceRef;
using RefMap = Engine::DeviceRefMap;

Ref hostRef(int32_t host, uint64_t uid, int32_t slot) {
    if (isMidiHost(host)) return {1, midiHostIndex(host), uid, slot};
    if (host < 0) return {2, -1, uid, slot};
    return {0, host, uid, slot};
}
int32_t hostOf(const Ref& r) { return r.fam == 1 ? midiHost(r.idx) : r.fam == 2 ? -1 : r.idx; }

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
bool sameRef(const Ref& a, const Ref& b) { return a.fam == b.fam && a.idx == b.idx && a.uid == b.uid && a.slot == b.slot; }

// A map result that removes the reference (a lane or link on the ungrouped rack's own macros).
constexpr int32_t kDropRef = -2;

} // namespace

// ---- Arp CC routes in the published snapshot (specs/044) ---------------------
void Engine::syncCcRoutes(Graph& g) {
    auto sync = [&](Track& t) {
        if (!t.ccRoutesDirty) return;   // published before: the audio thread may be reading it
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
        if (auto* r = t.instrument ? t.instrument->asRack() : nullptr) r->forEachChildMidi([&](MidiDevice& c) { add(c); });
        if (t.ccRoutes) ccRoutesUsed_ = true;
    };
    for (auto& t : g.tracks) sync(*t);
    if (g.masterTrack) sync(*g.masterTrack);
}

void Engine::reapplyCcRoutes() {
    if (!ccRoutesUsed_ || !authoring_) return;
    // Every Arp of the restored snapshot takes its route, and one without a route loses its
    // target (an ungroup that dropped it, then redo), or the next publish would revive it.
    auto apply = [](const Track& t) {
        auto one = [&](MidiDevice& m) {
            const Track::CcRoute* rt = nullptr;
            if (t.ccRoutes)
                for (auto& e : *t.ccRoutes)
                    if (e.arp == &m) { rt = &e; break; }
            if (!rt) {
                if (m.ccDestDevice() >= 0 || m.ccDestChainUid() != 0) m.setCcDest(-1, -1);
                return;
            }
            if (m.ccDestDevice() == rt->dev && m.ccDestChainUid() == rt->uid && m.ccDestSlot() == rt->slot
                && m.ccDestParam() == rt->param)
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
    for (auto& t : authoring_->tracks) apply(*t);
    if (authoring_->masterTrack) apply(*authoring_->masterTrack);
}

// Every Arp inside the track's racks (a MIDI Effect Rack, the Instrument Rack's chains).
void Engine::rewriteRackArps(Track& nt, const DeviceRefMap& map, const RackCore* fresh) {
    auto walk = [&](const RackCore& r) {
        if (&r == fresh) return;   // its Arps were rewritten on the way in
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
void Engine::rewriteArpRef(MidiDevice& m, const DeviceRefMap& map) {
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
void Engine::publishWithRefMap(int32_t trackId, std::shared_ptr<Track> nt, const DeviceRefMap& map, const RackCore* fresh) {
    // Lanes.
    std::vector<int32_t> laneMap(nt->automation.size());
    std::vector<TrackLane> kept;
    kept.reserve(nt->automation.size());
    for (size_t i = 0; i < nt->automation.size(); ++i) {
        TrackLane& l = nt->automation[i];
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
    nt->automation = std::move(kept);
    auto laneGone = [&](int32_t& li) {
        if (li < 0 || li >= static_cast<int32_t>(laneMap.size())) return true;
        li = laneMap[li];
        return li < 0;
    };
    std::erase_if(activeWrites_, [&](ActiveWrite& aw) { return aw.trackId == trackId && laneGone(aw.laneIndex); });
    std::erase_if(overrides_, [&](OverrideTarget& o) { return o.trackId == trackId && laneGone(o.laneIndex); });
    for (auto& m : nt->midiEffects)
        if (m) rewriteArpRef(*m, map);
    rewriteRackArps(*nt, map, fresh);
    // CV links, on this track and on any track whose links target it.
    auto rewriteLinks = [&](Track& t, bool own) {
        std::vector<CvLink> keep;
        keep.reserve(t.cvLinks.size());
        for (auto& l : t.cvLinks) {
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
        t.cvLinks = std::move(keep);
    };
    rewriteLinks(*nt, true);
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *authoring_);
    if (trackId == kMasterTrackId) g->masterTrack = nt;
    g->tracks.reserve(authoring_->tracks.size());
    for (auto& t : authoring_->tracks) {
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
        auto ot = cloneTrack(*t);
        rewriteLinks(*ot, false);
        g->tracks.push_back(ot);
    }
    publish(std::move(g));
}

int32_t Engine::groupDevices(int32_t trackId, int32_t midiFirst, int32_t midiCount, bool withInstrument,
    int32_t fxFirst, int32_t fxCount) {
    auto old = findTrackAuthoring(trackId);
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
        auto& d = old->devices[fxFirst + i];
        if (!d) return -2;
        if (dynamic_cast<const RackCore*>(d.get())) return -4;
    }
    for (int32_t i = 0; i < midiCount; ++i) {
        auto& m = old->midiEffects[midiFirst + i];
        if (!m) return -2;
        if (dynamic_cast<const RackCore*>(m.get())) return -4;
        if (m->midiKind() < 0 || m->midiKind() > 5) return -5; // chains take the built-in MIDI effects
    }
    if (withInstrument) {
        auto* ri = dynamic_cast<const RackInstrument*>(old->instrument.get());
        if (ri && !dynamic_cast<const DrumRack*>(ri)) return -6; // an Instrument Rack never nests
    }

    const double sr = transport_.sampleRate() > 0 ? transport_.sampleRate() : 44100.0;
    auto nt = cloneTrack(*old);
    uint64_t uid = 0;
    const RackCore* freshRack = nullptr;   // its Arps are rewritten on the way in
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
            auto& m = nt->midiEffects[midiFirst + i];
            if (m) rewriteArpRef(*m, map); // an Arp going in keeps driving its (maybe moved) target
            rack->addChainMidiDeviceInstance(c, m);
        }
        rack->replaceChainInstrument(c, nt->instrument, /*prepare=*/false);   // no chain name: it shows the instrument's
        for (int32_t i = 0; i < fxCount; ++i) rack->addChainDeviceInstance(c, nt->devices[i], /*prepare=*/false);
        if (midiCount > 0)   // midiFirst is -1 with no MIDI effects: begin() - 1 isn't an iterator
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
            auto& m = nt->midiEffects[a + i];
            if (m) rewriteArpRef(*m, map);
            rack->addChainMidiDeviceInstance(c, m);
        }
        rack->setChainName(c, nt->midiEffects[a]->displayName());   // named after its first device (FR-015)
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
        for (int32_t i = 0; i < n; ++i) rack->addChainDeviceInstance(c, nt->devices[a + i], /*prepare=*/false);
        rack->setChainName(c, nt->devices[a]->displayName());   // named after its first device (FR-015)
        nt->devices.erase(nt->devices.begin() + a, nt->devices.begin() + a + n);
        nt->devices.insert(nt->devices.begin() + a, rack);
        result = a;
    }
    publishWithRefMap(trackId, nt, map, freshRack);
    recomputePdc();
    return result;
}

int32_t Engine::ungroupRack(int32_t trackId, int32_t family, int32_t index, int32_t* outDropped) {
    if (outDropped) *outDropped = 0;
    auto old = findTrackAuthoring(trackId);
    if (!old) return -2;
    RackCore* rack = nullptr;
    if (family == 0 && index >= 0 && index < static_cast<int32_t>(old->devices.size()))
        rack = dynamic_cast<RackDevice*>(old->devices[index].get());
    else if (family == 1 && index >= 0 && index < static_cast<int32_t>(old->midiEffects.size()))
        rack = dynamic_cast<MidiRack*>(old->midiEffects[index].get());
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
        const bool mixDefault = std::fabs(k.gain.load(rl) - 1.0f) < 1e-6f && std::fabs(k.pan.load(rl)) < 1e-6f
            && !k.mute.load(rl) && !k.solo.load(rl) && k.keyLo.load(rl) == 0 && k.keyHi.load(rl) == 127
            && k.velLo.load(rl) == 0 && k.velHi.load(rl) == 127 && k.transpose.load(rl) == 0
            && std::fabs(k.velScale.load(rl) - 1.0f) < 1e-6f && k.selLo.load(rl) == 0 && k.selHi.load(rl) == 127;
        if (!mixDefault) dropped |= 2;
    }
    const uint64_t uid = ch.uid;
    auto devs = ch.devices;
    auto mids = ch.midiDevices;
    auto inst = ch.instrument;

    auto nt = cloneTrack(*old);
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
    recomputePdc();
    if (outDropped) *outDropped = dropped;
    return restored;
}

int32_t Engine::wrapDeviceInRack(int32_t trackId, int32_t deviceIndex) {
    const int32_t r = groupDevices(trackId, -1, 0, false, deviceIndex, 1);
    return r < 0 ? -1 : r;
}

int32_t Engine::unwrapRack(int32_t trackId, int32_t deviceIndex) {
    const int32_t r = ungroupRack(trackId, 0, deviceIndex, nullptr);
    return r < 0 ? -1 : r;
}

// ---- chain children's links, for save / load (specs/044) --------------------
bool Engine::rackChainMidiCcDest(int32_t t, int32_t host, int32_t chain, int32_t idx, int32_t* dev,
    int32_t* destChain, int32_t* destSlot, int32_t* param, float* depth) const {
    auto* r = rackCoreAt(t, host);
    MidiDevice* m = r && !isNestedRack(host) ? r->chainMidiDeviceAt(chain, idx) : nullptr;
    if (!m) return false;
    const uint64_t uid = m->ccDestChainUid();
    if (dev) *dev = m->ccDestDevice();
    if (destChain) *destChain = uid ? nestedChainIndex(t, m->ccDestDevice(), uid) : -1;
    if (destSlot) *destSlot = uid ? m->ccDestSlot() : -1;
    if (param) *param = m->ccDestParam();
    if (depth) *depth = m->ccDepth();
    return true;
}
bool Engine::rackChainMidiSetCcDest(int32_t t, int32_t host, int32_t chain, int32_t idx, int32_t dev,
    int32_t destChain, int32_t destSlot, int32_t param, float depth) {
    auto* r = rackCoreAt(t, host);
    MidiDevice* m = r && !isNestedRack(host) ? r->chainMidiDeviceAt(chain, idx) : nullptr;
    if (!m) return false;
    m->setCcDest(dev, param);
    if (destChain >= 0) {
        const uint64_t uid = nestedChainUid(t, dev, destChain);
        if (!uid) return false;
        m->setCcDestNested(uid, destSlot);
    }
    m->setCcDepth(depth);
    republishCcRoutes(t);
    return true;
}
bool Engine::rackChainSidechain(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t* src,
    int32_t* tapPre, float* gainDb, float* mix) const {
    auto* r = rackCoreAt(t, host);
    if (!r || isNestedRack(host)) return false;
    if (slot >= 0) {
        Device* d = r->chainDeviceAt(chain, slot);
        if (!d) return false;
        if (src) *src = d->sidechainSourceTrackId();
        if (tapPre) *tapPre = d->sidechainTapPre();
        if (gainDb) *gainDb = d->sidechainGainDb();
        if (mix) *mix = d->sidechainMix();
        return true;
    }
    Instrument* in = r->chainInstrument(chain);
    if (!in) return false;
    if (src) *src = in->sidechainSourceTrackId();
    if (tapPre) *tapPre = 0;
    if (gainDb) *gainDb = 0.0f;
    if (mix) *mix = 1.0f;
    return true;
}
// Keys go through the same route-pool check as a track device's (specs/050): one that would
// need a route the pool can't give — or would push out another listener's — is refused with -2
// and the key is left as it was.
int32_t Engine::rackChainSetSidechain(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t src,
    int32_t tapPre, float gainDb, float mix) {
    auto* r = rackCoreAt(t, host);
    if (!r || isNestedRack(host)) return -1;
    if (slot >= 0) {
        Device* d = r->chainDeviceAt(chain, slot);
        if (!d) return -1;
        d->setSidechainTapPre(tapPre);
        d->setSidechainGainDb(gainDb);
        d->setSidechainMix(mix);
        // a whole-track key; rackChainSetSidechainPart narrows it
        if (src < 0) {
            d->setSidechainSourceTrackId(-1);
            d->setSidechainPart(0);
            recomputeRouting();
            return 0;
        }
        return applySidechainKey(*d, src, 0, t != kMasterTrackId);
    }
    Instrument* in = r->chainInstrument(chain);
    if (!in) return -1;
    if (src < 0) {
        in->setSidechainSourceTrackId(-1);
        in->setSidechainPart(0);
        recomputeRouting();
        return 0;
    }
    return applySidechainKey(*in, src, 0, t != kMasterTrackId);
}
// The part of a chain child's key (specs/050), as an index on its source track (-1 whole
// track, -2 gone). For save / load, after rackChainSetSidechain.
bool Engine::rackChainSidechainPart(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t* part) const {
    auto* r = rackCoreAt(t, host);
    if (!r || isNestedRack(host)) return false;
    int32_t src = -1;
    uint64_t id = 0;
    if (slot >= 0) {
        Device* d = r->chainDeviceAt(chain, slot);
        if (!d) return false;
        src = d->sidechainSourceTrackId();
        id = d->sidechainPart();
    } else {
        Instrument* in = r->chainInstrument(chain);
        if (!in) return false;
        src = in->sidechainSourceTrackId();
        id = in->sidechainPart();
    }
    if (part) *part = src < 0 ? -1 : partIndexOf(src, id);
    return true;
}
int32_t Engine::rackChainSetSidechainPart(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t part) {
    auto* r = rackCoreAt(t, host);
    if (!r || isNestedRack(host)) return -1;
    Device* d = slot >= 0 ? r->chainDeviceAt(chain, slot) : nullptr;
    Instrument* in = slot < 0 ? r->chainInstrument(chain) : nullptr;
    if (!d && !in) return -1;
    const int32_t src = d ? d->sidechainSourceTrackId() : in->sidechainSourceTrackId();
    uint64_t id = 0;
    if (src < 0 || !partIdAt(src, part, &id)) return -1;
    if (d && part >= 0 && d->wantsMidiKey()) return -1; // a MIDI key takes whole tracks only
    const bool routed = t != kMasterTrackId;
    return d ? applySidechainKey(*d, src, id, routed) : applySidechainKey(*in, src, id, routed);
}

// ---- nested addresses follow rack edits (specs/044) -----------------------
void Engine::remapNestedAfterRackEdit(int32_t trackId, int32_t host, const NestedMap& map) {
    if (!authoring_ || isNestedRack(host)) return; // paths reach one rack level only
    auto into = [&](uint64_t uid, int32_t dev) { return uid != 0 && dev == host; };
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *authoring_);
    g->tracks.reserve(authoring_->tracks.size());
    bool any = false;
    for (auto& ot : authoring_->tracks) {
        const bool own = ot->id() == trackId;
        auto cvTouches = [&](const CvLink& l) {
            const int32_t tgt = l.targetTrack < 0 ? ot->id() : l.targetTrack;
            return (tgt == trackId && into(l.targetChainUid, l.targetDevice))
                || (own && into(l.sourceChainUid, l.sourceDevice));
        };
        bool needs = own;   // the edited track: its lanes, Arps (also inside racks) and links
        for (auto& l : ot->cvLinks) needs |= cvTouches(l);
        if (!needs) {
            g->tracks.push_back(ot);
            continue;
        }
        auto nt = cloneTrack(*ot);
        if (own) {
            // Lanes: rewrite or drop, keeping in-flight gestures on the right lane index.
            std::vector<int32_t> laneMap(nt->automation.size());
            std::vector<TrackLane> kept;
            kept.reserve(nt->automation.size());
            for (size_t i = 0; i < nt->automation.size(); ++i) {
                TrackLane& l = nt->automation[i];
                if (into(l.chainUid, l.deviceIndex)) {
                    const auto [u, sl] = map(l.chainUid, l.slot);
                    if (u == 0) {
                        laneMap[i] = -1;
                        continue;
                    }
                    l.chainUid = u;
                    l.slot = sl;
                }
                laneMap[i] = static_cast<int32_t>(kept.size());
                kept.push_back(std::move(l));
            }
            nt->automation = std::move(kept);
            auto laneGone = [&](int32_t& li) {
                if (li < 0 || li >= static_cast<int32_t>(laneMap.size())) return true;
                li = laneMap[li];
                return li < 0;
            };
            std::erase_if(activeWrites_, [&](ActiveWrite& aw) { return aw.trackId == trackId && laneGone(aw.laneIndex); });
            std::erase_if(overrides_, [&](OverrideTarget& o) { return o.trackId == trackId && laneGone(o.laneIndex); });
            // The Arps' CC targets, on the track and inside its racks (an Instrument Rack chain
            // often holds both the Arp and its target); the routes go live with nt.
            const DeviceRefMap refMap = [&](const DeviceRef& r) -> DeviceRef {
                if (!r.uid || hostOf(r) != host) return r;
                const auto [u, sl] = map(r.uid, r.slot);
                return u == 0 ? DeviceRef{kDropRef} : DeviceRef{r.fam, r.idx, u, sl};
            };
            for (auto& m : nt->midiEffects)
                if (m) rewriteArpRef(*m, refMap);
            rewriteRackArps(*nt, refMap);
        }
        std::vector<CvLink> keptLinks;
        keptLinks.reserve(nt->cvLinks.size());
        for (auto& l : nt->cvLinks) {
            const int32_t tgt = l.targetTrack < 0 ? nt->id() : l.targetTrack;
            if (tgt == trackId && into(l.targetChainUid, l.targetDevice)) {
                const auto [u, sl] = map(l.targetChainUid, l.targetSlot);
                if (u == 0) continue;
                l.targetChainUid = u;
                l.targetSlot = sl;
            }
            if (own && into(l.sourceChainUid, l.sourceDevice)) {
                const auto [u, sl] = map(l.sourceChainUid, l.sourceSlot);
                if (u == 0) continue;
                l.sourceChainUid = u;
                l.sourceSlot = sl;
            }
            keptLinks.push_back(l);
        }
        nt->cvLinks = std::move(keptLinks);
        any = true;
        g->tracks.push_back(nt);
    }
    if (any) publishRaw(std::move(g)); // no extra undo step: part of the rack edit
}

// Slot maps for the common chain edits: a child removed at `idx`, moved within its chain,
// or a new one inserted at `at` (MIDI slots use RackCore::midiTarget encoding).
static Engine::NestedMap slotRemoved(uint64_t uid, int32_t idx, bool midi) {
    return [=](uint64_t u, int32_t sl) -> std::pair<uint64_t, int32_t> {
        if (u != uid || (midi ? !RackCore::isMidiTarget(sl) : sl < 0)) return {u, sl};
        const int32_t i = midi ? RackCore::midiTargetIndex(sl) : sl;
        if (i == idx) return {0, -1};
        const int32_t ni = i > idx ? i - 1 : i;
        return {u, midi ? RackCore::midiTarget(ni) : ni};
    };
}
static Engine::NestedMap slotInserted(uint64_t uid, int32_t at, bool midi) {
    return [=](uint64_t u, int32_t sl) -> std::pair<uint64_t, int32_t> {
        if (u != uid || (midi ? !RackCore::isMidiTarget(sl) : sl < 0)) return {u, sl};
        const int32_t i = midi ? RackCore::midiTargetIndex(sl) : sl;
        const int32_t ni = i >= at ? i + 1 : i;
        return {u, midi ? RackCore::midiTarget(ni) : ni};
    };
}
static Engine::NestedMap slotMoved(uint64_t uid, int32_t from, int32_t to, bool midi) {
    return [=](uint64_t u, int32_t sl) -> std::pair<uint64_t, int32_t> {
        if (u != uid || (midi ? !RackCore::isMidiTarget(sl) : sl < 0)) return {u, sl};
        int32_t i = midi ? RackCore::midiTargetIndex(sl) : sl;
        if (i == from) i = to;
        else if (from < to && i > from && i <= to) --i;
        else if (from > to && i >= to && i < from) ++i;
        return {u, midi ? RackCore::midiTarget(i) : i};
    };
}
static Engine::NestedMap slotToChain(uint64_t uid, int32_t idx, uint64_t toUid, int32_t toIdx, bool midi) {
    auto removed = slotRemoved(uid, idx, midi);
    return [=](uint64_t u, int32_t sl) -> std::pair<uint64_t, int32_t> {
        if (u == uid && sl == (midi ? RackCore::midiTarget(idx) : idx))
            return {toUid, midi ? RackCore::midiTarget(toIdx) : toIdx};
        return removed(u, sl);
    };
}
static Engine::NestedMap chainDropped(uint64_t uid, bool instrumentOnly) {
    return [=](uint64_t u, int32_t sl) -> std::pair<uint64_t, int32_t> {
        if (u == uid && (!instrumentOnly || sl == -1)) return {0, -1};
        return {u, sl};
    };
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
    clipMany_.clear();
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
    clipMany_.clear();
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
    dev->setSidechainSourceTrackId(-1); // a pasted device starts without a key
    const uint64_t uid = r->chainBaseUid(c);
    const int32_t idx = rackForEdit(t, di)->insertChainDeviceInstance(c, std::move(dev), at);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotInserted(uid, idx, false));
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
    const uint64_t uid = r->chainBaseUid(c);
    const int32_t idx = rackForEdit(t, di)->insertChainMidiDeviceInstance(c, std::move(dev), at);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotInserted(uid, idx, true));
    return idx;
}
int32_t Engine::rackChainPasteDevices(int32_t t, int32_t di, int32_t c, int32_t midiAt, int32_t fxAt,
    int32_t* outSkipped, int32_t* outFirstMidi, int32_t* outFirstFx) {
    if (outSkipped) *outSkipped = 0;
    if (outFirstMidi) *outFirstMidi = -1;
    if (outFirstFx) *outFirstFx = -1;
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount()) return -1;
    std::vector<ClipEntry> src = clipMany_;
    if (src.empty()) {
        if (clipMidi_) src.push_back({clipMidi_, nullptr, nullptr});
        else if (clipInstrument_) src.push_back({nullptr, clipInstrument_, nullptr});
        else if (clipDevice_) src.push_back({nullptr, nullptr, clipDevice_});
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
            auto m = cloneMidiDevice(*e.midi);
            if (!m) return -3;
            m->setCcDest(-1, -1);
            mids.push_back(std::move(m));
        } else if (e.dev && takesAudio && !e.dev->asRack()) {
            auto d = cloneDevice(*e.dev);
            if (!d) return -3;
            d->setSidechainSourceTrackId(-1); // a pasted device starts without a key
            devs.push_back(std::move(d));
        } else
            ++skipped;
    }
    if (outSkipped) *outSkipped = skipped;
    if (mids.empty() && devs.empty()) return 0;
    const uint64_t uid = r->chainBaseUid(c);
    RackCore* fr = rackForEdit(t, di);   // one fork: one undo step for the whole paste
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
    recomputePdc();
    return static_cast<int32_t>(mids.size() + devs.size());
}
int32_t Engine::rackChainDeviceDuplicate(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = rackCoreAt(t, di);
    Device* src = r ? r->chainDeviceAt(c, i) : nullptr;
    auto dev = src ? cloneDevice(*src) : nullptr;
    if (!dev) return -1;
    const uint64_t uid = r->chainBaseUid(c);
    const int32_t idx = rackForEdit(t, di)->insertChainDeviceInstance(c, std::move(dev), i + 1);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotInserted(uid, idx, false));
    recomputePdc();
    return idx;
}
int32_t Engine::rackChainMidiDuplicate(int32_t t, int32_t di, int32_t c, int32_t i) {
    auto* r = rackCoreAt(t, di);
    MidiDevice* src = r ? r->chainMidiDeviceAt(c, i) : nullptr;
    auto dev = src ? cloneMidiDevice(*src) : nullptr;
    if (!dev) return -1;
    const uint64_t uid = r->chainBaseUid(c);
    const int32_t idx = rackForEdit(t, di)->insertChainMidiDeviceInstance(c, std::move(dev), i + 1);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotInserted(uid, idx, true));
    return idx;
}
int32_t Engine::rackChainDeviceMoveTo(int32_t t, int32_t di, int32_t c, int32_t i, int32_t to) {
    auto* r = rackCoreAt(t, di);
    if (!r || !r->chainDeviceAt(c, i) || to < 0 || to >= r->chainCount() || to == c) return -1;
    const uint64_t uid = r->chainBaseUid(c), toUid = r->chainBaseUid(to);
    const int32_t idx = rackForEdit(t, di)->moveChainDeviceToChain(c, i, to);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotToChain(uid, i, toUid, idx, false));
    recomputePdc();
    return idx;
}
int32_t Engine::rackChainMidiMoveTo(int32_t t, int32_t di, int32_t c, int32_t i, int32_t to) {
    auto* r = rackCoreAt(t, di);
    if (!r || !r->chainMidiDeviceAt(c, i) || to < 0 || to >= r->chainCount() || to == c) return -1;
    if (di == -1 && to >= RackInstrument::kMaxMidiChains) return -5;
    const uint64_t uid = r->chainBaseUid(c), toUid = r->chainBaseUid(to);
    const int32_t idx = rackForEdit(t, di)->moveChainMidiDeviceToChain(c, i, to);
    if (idx >= 0) remapNestedAfterRackEdit(t, di, slotToChain(uid, i, toUid, idx, true));
    return idx;
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
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = rackForEdit(t, di)->removeChain(c);
    if (ok) remapNestedAfterRackEdit(t, di, chainDropped(uid, false));
    recomputePdc(); // the chain may have held the rack's longest latency
    return ok;
}
bool Engine::rackSetChainInstrument(int32_t t, int32_t di, int32_t c, int32_t k) {
    auto* r = rackCoreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || !rackKindAllowed(t, di, k) || !RackCore::makeInstrument(k)) return false;
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = rackForEdit(t, di)->setChainInstrument(c, k);
    if (ok) remapNestedAfterRackEdit(t, di, chainDropped(uid, /*instrumentOnly=*/true));
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
    if (!inst) return;
    inst->pluginParamSet(p, v);
    routeNestedCvBaseEdit(t, di, c, -1, p, v);   // a grouped, CV-linked instrument (specs/044)
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
    fresh->presetTag = m->presetTag; // still the preset it was loaded from (specs/048)
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
    fresh->presetTag = gr->presetTag; // still the preset it was loaded from (specs/048)
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
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = rackForEdit(t, di)->removeChainDevice(c, d);
    if (ok) remapNestedAfterRackEdit(t, di, slotRemoved(uid, d, false));
    return ok;
}
bool Engine::rackMoveChainDevice(int32_t t, int32_t di, int32_t c, int32_t from, int32_t to) {
    auto* r = rackCoreAt(t, di);
    if (!r || from < 0 || from >= r->chainDeviceCount(c)) return false;
    to = std::clamp(to, 0, r->chainDeviceCount(c) - 1);
    if (to == from) return true;
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = rackForEdit(t, di)->moveChainDevice(c, from, to);
    if (ok) remapNestedAfterRackEdit(t, di, slotMoved(uid, from, to, false));
    return ok;
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
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = rackForEdit(t, di)->removeChainMidiDevice(c, i);
    if (ok) remapNestedAfterRackEdit(t, di, slotRemoved(uid, i, true));
    return ok;
}
bool Engine::rackMoveChainMidi(int32_t t, int32_t di, int32_t c, int32_t from, int32_t to) {
    auto* r = rackCoreAt(t, di);
    if (!r || from < 0 || from >= r->chainMidiDeviceCount(c)) return false;
    to = std::clamp(to, 0, r->chainMidiDeviceCount(c) - 1);
    if (to == from) return true;
    const uint64_t uid = r->chainBaseUid(c);
    const bool ok = rackForEdit(t, di)->moveChainMidiDevice(c, from, to);
    if (ok) remapNestedAfterRackEdit(t, di, slotMoved(uid, from, to, true));
    return ok;
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
    if (!dev) return;
    dev->setParam(p, v);
    routeNestedCvBaseEdit(t, di, c, d, p, v);   // a grouped, CV-linked device (specs/044)
}
void Engine::rackChainMidiParamSet(int32_t t, int32_t di, int32_t c, int32_t i, int32_t p, float v) {
    MidiDevice* m = rackChainMidiAt(t, di, c, i);
    if (!m) return;
    m->setParam(p, v);
    routeNestedCvBaseEdit(t, di, c, RackCore::midiTarget(i), p, v);
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
