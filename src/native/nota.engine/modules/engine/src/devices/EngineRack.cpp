// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackOps — core rack resolution, track creation, plugin/sampler chains,
// sidechain/key routing, nested edit propagation, and rack self-tests.

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
#include "nota/dsp/capabilities/ISampleInstrument.h"
#include "nota/dsp/rt/SnapshotHistory.h"
#include "nota/pluginhost/PluginHostModule.h"
#include "render/HybridBufferController.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

void RackOps::installPluginFactory() {
    RackCore::pluginFactory().instrumentById = [](const std::string& id) -> std::shared_ptr<Instrument> {
        return pluginhost::sharedPluginHost().createInstrumentById(id, 44100.0, kMaxBlock);
    };
    RackCore::pluginFactory().effectById = [](const std::string& id) -> std::shared_ptr<Device> {
        return pluginhost::sharedPluginHost().createEffectById(id, 44100.0, kMaxBlock);
    };
}

int32_t RackOps::addPluginInstrumentChain(int32_t t, int32_t di, int32_t catalogIndex) {
    if (!coreAt(t, di)) return -1;
    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    auto inst = pluginhost::sharedPluginHost().createInstrument(catalogIndex, sr, kMaxBlock);
    if (!inst) return -1;
    auto* r = forEdit(t, di);
    const int32_t idx = r->addChainInstrument(inst);
    rt_.devices().recomputePdc();
    return idx;
}

int32_t RackOps::addPluginChainDevice(int32_t t, int32_t di, int32_t chain, int32_t catalogIndex) {
    if (!coreAt(t, di)) return -1;
    const double sr = rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0;
    auto dev = pluginhost::sharedPluginHost().createEffect(catalogIndex, sr, kMaxBlock);
    if (!dev) return -1;
    auto* r = forEdit(t, di);
    const int32_t idx = r->addChainDeviceInstance(chain, dev);
    rt_.devices().recomputePdc();
    return idx;
}

int32_t RackOps::addInstrumentTrack() {
    auto g = std::make_shared<Graph>(*rt_.authoring_);
    const int32_t id = rt_.nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto rack = std::make_shared<RackInstrument>();
    rack->setSampleRate(rt_.transport_.sampleRate());
    // No chains: the card invites an instrument dropped on it (the Volt default of specs/026 is gone).
    track->instrument = rack;
    g->tracks.push_back(track);
    rt_.publish(g);
    return id;
}

int32_t RackOps::addDrumTrack() {
    auto g = std::make_shared<Graph>(*rt_.authoring_);
    const int32_t id = rt_.nextTrackId_++;
    auto track = std::make_shared<Track>(id, TrackType::Instrument);
    auto rack = std::make_shared<DrumRack>();
    rack->setSampleRate(rt_.transport_.sampleRate()); // starts empty; the UI adds pads
    track->instrument = rack;
    g->tracks.push_back(track);
    rt_.publish(g);
    return id;
}

int32_t RackOps::chainTriggerNote(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainTriggerNote(c) : -1;
}
void RackOps::setChainTriggerNote(int32_t t, int32_t di, int32_t c, int32_t note) {
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || r->chainTriggerNote(c) == note) return;
    forEdit(t, di)->setChainTriggerNote(c, note);
}

std::string RackOps::chainName(int32_t t, int32_t di, int32_t c) const {
    auto* r = coreAt(t, di);
    return r ? r->chainName(c) : std::string{};
}
void RackOps::setChainName(int32_t t, int32_t di, int32_t c, const std::string& name) {
    auto* r = coreAt(t, di);
    if (!r || c < 0 || c >= r->chainCount() || r->chainName(c) == name) return;
    forEdit(t, di)->setChainName(c, name);
}

int32_t RackOps::addSamplerChain(int32_t t, int32_t di, const std::string& path, int32_t rootNote, bool loop) {
    if (!coreAt(t, di)) return -1;
    auto buf = decodeAudioFile(path);
    if (!buf || buf->empty()) return -1;
    auto sm = dsp::DeviceRegistry::createInstrument(static_cast<int32_t>(devices::InstrumentKind::Sampler));
    auto* smp = dynamic_cast<dsp::capabilities::ISampleInstrument*>(sm.get());
    if (!sm || !smp) return -1;
    sm->setSampleRate(rt_.transport_.sampleRate() > 0 ? rt_.transport_.sampleRate() : 44100.0);
    smp->setSample(buf, rootNote, loop);
    return forEdit(t, di)->addChainInstrument(std::move(sm));
}
bool RackOps::setChainSamplerSample(int32_t t, int32_t di, int32_t chain, const std::string& path, int32_t rootNote) {
    auto* r = coreAt(t, di);
    if (!r || chain < 0 || chain >= r->chainCount()) return false;
    auto buf = decodeAudioFile(path);
    if (!buf || buf->empty()) return false;
    return forEdit(t, di)->setChainInstrumentSample(chain, buf, rootNote, false);
}

// The track's plain Instrument Rack (not a Drum Rack): the only rack whose chains take a rack.
static RackInstrument* nestingHost(const std::shared_ptr<Track>& t) {
    auto* r = t ? dynamic_cast<RackInstrument*>(t->instrument.get()) : nullptr;
    return (r && !dynamic_cast<DrumRack*>(r)) ? r : nullptr;
}

RackCore* RackOps::coreAt(int32_t trackId, int32_t deviceIndex) const {
    auto t = rt_.findTrackAuthoring(trackId);
    if (!t) return nullptr;
    if (isNestedRack(deviceIndex)) {
        auto* host = nestingHost(t);
        return host ? dynamic_cast<RackCore*>(
                          dynamic_cast<RackInstrument*>(host->chainInstrument(kNestedRackBase - deviceIndex)))
                    : nullptr;
    }
    if (deviceIndex <= kMidiRackBase) {
        const int32_t mi = kMidiRackBase - deviceIndex;
        if (mi >= static_cast<int32_t>(t->midiEffects.size()) || !t->midiEffects[static_cast<size_t>(mi)])
            return nullptr;
        return dynamic_cast<RackCore*>(dynamic_cast<MidiRack*>(t->midiEffects[static_cast<size_t>(mi)].get()));
    }
    if (deviceIndex < 0) return t->instrument ? dynamic_cast<RackCore*>(t->instrument.get()) : nullptr;
    if (deviceIndex < static_cast<int32_t>(t->devices.size()))
        return t->devices[static_cast<size_t>(deviceIndex)]
                   ? dynamic_cast<RackCore*>(t->devices[static_cast<size_t>(deviceIndex)].get())
                   : nullptr;
    return nullptr;
}

// Rack edits used to change the rack in place, and the rack object is shared by every
// graph snapshot, so undo had nothing to go back to (#236). An edit the user can undo
// forks the rack into a new object (chain children shared, so nothing restarts) and
// republishes the track with it; the snapshot on the undo stack keeps the old rack as it was.
// A Drum Rack in a fork of an Instrument Rack chain: a fork of its own, put in its chain.
static std::shared_ptr<RackInstrument> forkRackInstrument(const RackInstrument& src);

RackCore* RackOps::forEdit(int32_t trackId, int32_t deviceIndex) {
    auto old = rt_.findTrackAuthoring(trackId);
    if (!old || !coreAt(trackId, deviceIndex)) return nullptr;
    if (isNestedRack(deviceIndex)) {
        // Fork the outer rack (one undo step: the snapshot keeps the old outer rack, which keeps
        // the old nested one), then the nested rack inside the fork.
        const int32_t c = kNestedRackBase - deviceIndex;
        auto* outer = forEdit(trackId, -1);
        auto* src = outer ? dynamic_cast<RackInstrument*>(outer->chainInstrument(c)) : nullptr;
        if (!src) return nullptr;
        auto r = forkRackInstrument(*src);
        RackCore* fresh = r.get();
        // The fork shares the playing Drum Rack's pads and effects, already prepared: install it
        // without preparing them again (that would clear a reverb's tail mid-play).
        return outer->replaceChainInstrument(c, std::move(r), /*prepare=*/false) ? fresh : nullptr;
    }
    auto nt = rt_.cloneTrack(*old);
    RackCore* fresh = nullptr;
    if (deviceIndex <= kMidiRackBase) {
        const int32_t mi = kMidiRackBase - deviceIndex;
        auto* src = dynamic_cast<MidiRack*>(old->midiEffects[static_cast<size_t>(mi)].get());
        if (!src) return nullptr;
        auto r = std::make_shared<MidiRack>();
        r->forkFrom(*src);
        r->shareRuntimeWith(*src); // held notes play on through the swap
        r->setBypassed(src->bypassed());
        r->setFolded(src->folded());
        r->setPresetTag(src->presetTag());
        r->inheritLineage(src->lineage()); // the same device to take retry
        fresh = r.get();
        nt->midiEffects[static_cast<size_t>(mi)] = std::move(r);
        rt_.republishWithTrack(trackId, nt);
        return fresh;
    }
    if (deviceIndex < 0) {
        auto* src = dynamic_cast<RackInstrument*>(old->instrument.get());
        if (!src) return nullptr;
        auto r = forkRackInstrument(*src);
        fresh = r.get();
        nt->instrument = std::move(r);
    } else {
        auto* src = dynamic_cast<RackDevice*>(old->devices[static_cast<size_t>(deviceIndex)].get());
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
        r->inheritLineage(src->lineage()); // the same device to take retry
        fresh = r.get();
        nt->devices[static_cast<size_t>(deviceIndex)] = std::move(r);
    }
    rt_.republishWithTrack(trackId, nt);
    return fresh;
}

static std::shared_ptr<RackInstrument> forkRackInstrument(const RackInstrument& src) {
    std::shared_ptr<RackInstrument> r;
    if (dynamic_cast<const DrumRack*>(&src)) r = std::make_shared<DrumRack>();
    else
        r = std::make_shared<RackInstrument>();
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
    for (int p = 0; p < 128; ++p) r->pedalRelVel[p].store(src.pedalRelVel[p].load(rl), rl);
    for (int i = 0; i < kCtlCount; ++i) r->deliveredCtl[i].store(src.deliveredCtl[i].load(rl), rl);
    return r;
}

// Racks nest one level (specs/041): an Instrument Rack chain may hold a Drum Rack; nothing
// else takes a rack, and an Instrument Rack is never a chain instrument.
bool RackOps::kindAllowed(int32_t trackId, int32_t deviceIndex, int32_t kind) const {
    if (!RackCore::isRackKind(kind)) return true;
    return kind == 4 && deviceIndex == -1 && nestingHost(rt_.findTrackAuthoring(trackId)) != nullptr;
}

// ---- chain children's links, for save / load (specs/044) --------------------
bool RackOps::chainMidiCcDest(int32_t t, int32_t host, int32_t chain, int32_t idx, int32_t* dev, int32_t* destChain,
    int32_t* destSlot, int32_t* param, float* depth) const {
    auto* r = coreAt(t, host);
    MidiDevice* m = r && !isNestedRack(host) ? r->chainMidiDeviceAt(chain, idx) : nullptr;
    if (!m) return false;
    const uint64_t uid = m->ccDestChainUid();
    if (dev) *dev = m->ccDestDevice();
    if (destChain) *destChain = uid ? rt_.automation().nestedChainIndex(t, m->ccDestDevice(), uid) : -1;
    if (destSlot) *destSlot = uid ? m->ccDestSlot() : -1;
    if (param) *param = m->ccDestParam();
    if (depth) *depth = m->ccDepth();
    return true;
}
bool RackOps::chainMidiSetCcDest(int32_t t, int32_t host, int32_t chain, int32_t idx, int32_t dev, int32_t destChain,
    int32_t destSlot, int32_t param, float depth) {
    auto* r = coreAt(t, host);
    MidiDevice* m = r && !isNestedRack(host) ? r->chainMidiDeviceAt(chain, idx) : nullptr;
    if (!m) return false;
    m->setCcDest(dev, param);
    if (destChain >= 0) {
        const uint64_t uid = rt_.automation().nestedChainUid(t, dev, destChain);
        if (!uid) return false;
        m->setCcDestNested(uid, destSlot);
    }
    m->setCcDepth(depth);
    rt_.devices().republishCcRoutes(t);
    return true;
}
bool RackOps::chainAcceptsSidechain(int32_t t, int32_t host, int32_t chain, int32_t slot) const {
    auto* r = coreAt(t, host);
    if (!r || isNestedRack(host) || slot < 0) return false;
    Device* d = r->chainDeviceAt(chain, slot);
    return d && d->acceptsSidechain();
}
bool RackOps::chainSidechain(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t* src, int32_t* tapPre,
    float* gainDb, float* mix) const {
    auto* r = coreAt(t, host);
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
// and the key, tap, gain and mix are left as they were.
int32_t RackOps::chainSetSidechain(
    int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t src, int32_t tapPre, float gainDb, float mix) {
    return chainSetKey(t, host, chain, slot, src, -1, tapPre, gainDb, mix);
}
// Source and part in one step, so only the final key is checked against the pool (a whole-track
// key on the way to a part could be refused while the part itself already has a route).
int32_t RackOps::chainSetKey(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t src, int32_t part,
    int32_t tapPre, float gainDb, float mix) {
    auto* r = coreAt(t, host);
    if (!r || isNestedRack(host)) return -1;
    Device* d = slot >= 0 ? r->chainDeviceAt(chain, slot) : nullptr;
    Instrument* in = slot < 0 ? r->chainInstrument(chain) : nullptr;
    if (!d && !in) return -1;
    int32_t rc = 0;
    if (src < 0) {
        if (d) {
            d->setSidechainSourceTrackId(-1);
            d->setSidechainPart(0);
        } else {
            in->setSidechainSourceTrackId(-1);
            in->setSidechainPart(0);
        }
        rt_.devices().recomputeRouting();
    } else {
        uint64_t id = 0;
        if (!rt_.findTrackAuthoring(src) || !rt_.devices().partIdAt(src, part, &id)) return -1;
        if (d && part >= 0 && d->wantsMidiKey()) return -1; // a MIDI key takes whole tracks only
        const bool routed = t != kMasterTrackId;
        rc = d ? rt_.devices().applySidechainKey(*d, src, id, routed)
               : rt_.devices().applySidechainKey(*in, src, id, routed);
    }
    if (rc == 0 && d) { // shaping follows the key only once the key is in
        d->setSidechainTapPre(tapPre);
        d->setSidechainGainDb(gainDb);
        d->setSidechainMix(mix);
    }
    return rc;
}
// The part of a chain child's key (specs/050), as an index on its source track (-1 whole
// track, -2 gone). For save / load, after rackChainSetSidechain.
bool RackOps::chainSidechainPart(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t* part) const {
    auto* r = coreAt(t, host);
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
    if (part) *part = src < 0 ? -1 : rt_.devices().partIndexOf(src, id);
    return true;
}
int32_t RackOps::chainSetSidechainPart(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t part) {
    auto* r = coreAt(t, host);
    if (!r || isNestedRack(host)) return -1;
    Device* d = slot >= 0 ? r->chainDeviceAt(chain, slot) : nullptr;
    Instrument* in = slot < 0 ? r->chainInstrument(chain) : nullptr;
    if (!d && !in) return -1;
    const int32_t src = d ? d->sidechainSourceTrackId() : in->sidechainSourceTrackId();
    uint64_t id = 0;
    if (src < 0 || !rt_.devices().partIdAt(src, part, &id)) return -1;
    if (d && part >= 0 && d->wantsMidiKey()) return -1; // a MIDI key takes whole tracks only
    const bool routed = t != kMasterTrackId;
    return d ? rt_.devices().applySidechainKey(*d, src, id, routed)
             : rt_.devices().applySidechainKey(*in, src, id, routed);
}

// ---- nested addresses follow rack edits (specs/044) -----------------------
void RackOps::remapNestedAfterRackEdit(int32_t trackId, int32_t host, const NestedMap& map) {
    if (!rt_.authoring_ || isNestedRack(host)) return; // paths reach one rack level only
    auto into = [&](uint64_t uid, int32_t dev) { return uid != 0 && dev == host; };
    auto g = std::make_shared<Graph>();
    inheritGraphLevel(*g, *rt_.authoring_);
    g->tracks.reserve(rt_.authoring_->tracks.size());
    bool any = false;
    for (auto& ot : rt_.authoring_->tracks) {
        const bool own = ot->id() == trackId;
        auto cvTouches = [&](const CvLink& l) {
            const int32_t tgt = l.targetTrack < 0 ? ot->id() : l.targetTrack;
            return (tgt == trackId && into(l.targetChainUid, l.targetDevice)) ||
                   (own && into(l.sourceChainUid, l.sourceDevice));
        };
        bool needs = own; // the edited track: its lanes, Arps (also inside racks) and links
        for (auto& l : ot->cvLinks) needs |= cvTouches(l);
        if (!needs) {
            g->tracks.push_back(ot);
            continue;
        }
        auto nt = rt_.cloneTrack(*ot);
        if (own) {
            // Lanes: rewrite or drop, keeping in-flight gestures on the right lane index.
            std::vector<int32_t> laneMap(nt->automation.size());
            std::vector<TrackLane> kept;
            kept.reserve(nt->automation.size());
            for (size_t i = 0; i < nt->automation.size(); ++i) {
                TrackLane l = nt->automation[i];
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
            nt->automation.replace(std::move(kept));
            auto laneGone = [&](int32_t& li) {
                if (li < 0 || li >= static_cast<int32_t>(laneMap.size())) return true;
                li = laneMap[static_cast<size_t>(li)];
                return li < 0;
            };
            std::erase_if(
                rt_.activeWrites_, [&](ActiveWrite& aw) { return aw.trackId == trackId && laneGone(aw.laneIndex); });
            std::erase_if(
                rt_.overrides_, [&](OverrideTarget& o) { return o.trackId == trackId && laneGone(o.laneIndex); });
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
        for (CvLink l : nt->cvLinks) {
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
        nt->cvLinks.replace(std::move(keptLinks));
        any = true;
        g->tracks.push_back(nt);
    }
    if (any) rt_.publishRaw(std::move(g)); // no extra undo step: part of the rack edit
}

// Device-free self-test for the rack core (kept from Phase 1; now exercises the
// shared RackCore via a RackInstrument): two synth chains sum to audible output,
// mute silences, a macro mapping drives a child param, and the blob round-trips.
bool RackOps::selfTest() {
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
bool RackOps::deviceSelfTest() {
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
bool RackOps::drumRackSelfTest() {
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

} // namespace nota::detail
