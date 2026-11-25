// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RenderGraphExecutor — parallel wave execution and deterministic bus reduction.

#include "engine/RenderGraphExecutor.h"

#include "devices/Device.h"
#include "devices/Instrument.h"
#include "devices/racks/RackCore.h"
#include "dsp/core/AudioBufferView.h"
#include "dsp/core/RenderExecutor.h"
#include "dsp/math/SimdAudio.h"
#include "engine/Engine.h"
#include "engine/RenderRoutingPlan.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstddef>

namespace nota {

namespace {

enum class ChainRest : uint8_t { Settled, Opaque, Busy };

bool hasLiveDevice(const Track& t) noexcept {
    if (t.pdc && !t.pdc->isIdle()) return true;
    for (const auto& d : t.devices)
        if (d && !d->bypassed()) return true;
    return false;
}

ChainRest chainRest(const Track& t) noexcept {
    if (t.pdc && !t.pdc->isIdle()) return ChainRest::Busy;
    ChainRest r = ChainRest::Settled;
    for (const auto& d : t.devices) {
        if (!d || d->bypassed() || d->isIdle()) continue;
        if (d->builtinKind() >= 0) return ChainRest::Busy;
        r = ChainRest::Opaque;
    }
    return r;
}

bool trackRequiresActiveProcessing(const Track& t) noexcept {
    if (t.instrument && !t.instrument->bypassed() && t.instrument->requiresActiveProcessing()) return true;
    for (const auto& d : t.devices)
        if (d && !d->bypassed() && d->requiresActiveProcessing()) return true;
    return false;
}

bool trackHasOpenEditor(const Track& t) noexcept {
    if (t.instrument && !t.instrument->bypassed()) {
        if (t.instrument->editorOpen()) return true;
        if (const auto* r = t.instrument->asRack(); r && r->chainsHaveOpenEditor(true)) return true;
    }
    for (const auto& d : t.devices) {
        if (!d || d->bypassed()) continue;
        if (d->editorOpen()) return true;
        if (const auto* r = d->asRack(); r && r->chainsHaveOpenEditor(false)) return true;
    }
    return false;
}

bool midiEffectsIdle(const Track& t) noexcept {
    for (const auto& d : t.midiEffects)
        if (d && !d->bypassed() && !d->isIdle()) return false;
    return true;
}

bool canSkipInactiveBus(Track& t) noexcept {
    return t.tailQuiet() < 0 && chainRest(t) == ChainRest::Settled && !trackRequiresActiveProcessing(t);
}

void settleInactiveBus(Track& t, const float* buf, int32_t frames, double sr, float tailFloor,
    double tailQuietSec, double tailMaxQuietSec) noexcept {
    int64_t& quiet = t.tailQuiet();
    if (simd_audio::peakAbs(buf, frames * 2) > tailFloor || trackRequiresActiveProcessing(t)) {
        quiet = 0;
        return;
    }
    if (quiet < 0) quiet = 0;
    quiet += frames;
    const ChainRest rest = chainRest(t);
    if (rest == ChainRest::Settled ||
        (rest == ChainRest::Opaque && quiet >= static_cast<int64_t>(tailQuietSec * sr)) ||
        quiet >= static_cast<int64_t>(tailMaxQuietSec * sr))
        quiet = -1;
}

bool canProcessTrackParallel(const Track& t) {
    if (t.instrument && !t.instrument->supportsParallelRender()) return false;
    for (const auto& d : t.devices)
        if (d && !d->bypassed() && !d->supportsParallelRender()) return false;
    return true;
}

struct BusAccumResult {
    float peakL = 0.0f;
    float peakR = 0.0f;
    double sqL = 0.0;
    double sqR = 0.0;
    double sLR = 0.0;
    int64_t recDropped = 0;
};

// Shared meter, internal recording queue, and stereo correlation pass over an already-panned buffer.
template <typename Ring>
inline BusAccumResult scanPannedMetrics(
    const float* m, int32_t frames, bool tapRec, Ring& internalQueue, bool collectStereoProduct) noexcept {
    BusAccumResult res;
    for (int32_t i = 0; i < frames; ++i) {
        const float l = m[i * 2];
        const float r = m[i * 2 + 1];
        if (tapRec && !internalQueue.push(InputFrame{l, r})) ++res.recDropped;
        const float al = std::fabs(l);
        const float ar = std::fabs(r);
        if (al > res.peakL) res.peakL = al;
        if (ar > res.peakR) res.peakR = ar;
        res.sqL += static_cast<double>(l) * static_cast<double>(l);
        res.sqR += static_cast<double>(r) * static_cast<double>(r);
        if (collectStereoProduct) res.sLR += static_cast<double>(l) * static_cast<double>(r);
    }
    return res;
}

} // namespace

struct RenderGraphExecutor::MixContext {
    Engine& eng;
    Graph* g = nullptr;
    const RenderRoutingPlan* plan = nullptr;
    float* out = nullptr;
    int32_t frames = 0;
    double blockStart = 0.0;
    bool playing = false;
    double spb = 0.0;
    double sr = 44100.0;
    bool cueOn = false;
    bool anySolo = false;
    bool arrangementActive = true;
    bool offline = true;
    bool parallelOk = false;
    float stereoA = 0.0f;
    float panLawStep = 0.0f;
    int32_t recSrc = 0;
    int32_t capTrackId = -1;
    int32_t numReturns = 0;
    int32_t numGroups = 0;
    int32_t maxGroupDepth = 0;
    int32_t nRoutes = 0;
    int32_t feedIds[16] = {};
    int32_t numFeeds = 0;
    TransportInfo ti{};
    dsp::RenderExecutorHost execHost{};

    int32_t cachedRouteSlot(size_t li, int32_t tid) const noexcept {
        return li < eng.rs_->trackRouteSlot.size() ? eng.rs_->trackRouteSlot[li] : eng.routeSlotForTrack(tid);
    }

    int groupSlotOf(int32_t id) const noexcept {
        if (id < 0) return -1;
        if (plan) return plan->groupSlotOfId(id);
        for (int i = 0; i < numGroups; ++i)
            if (eng.rs_->groupWork[static_cast<size_t>(i)].track &&
                eng.rs_->groupWork[static_cast<size_t>(i)].track->id() == id)
                return i;
        return -1;
    }

    bool feedsMonitor(int32_t id) const noexcept {
        for (int i = 0; i < numFeeds; ++i)
            if (feedIds[i] == id) return true;
        return false;
    }

    void markFresh(int32_t id) const noexcept {
        const int32_t s = eng.routeSlotForTrack(id);
        if (s >= 0) eng.routeFresh_[s] = true;
    }
};

uint32_t RenderGraphExecutor::collectChainSidechainNeeds(
    const Engine& eng, const Track& t, bool includeInstrument, bool includeDevices) {
    uint32_t mask = 0;
    auto add = [&](int32_t src, uint64_t part) {
        if (src < 0) return;
        const int32_t s = eng.routeSlotFor(src, part);
        if (s >= 0 && s < Engine::kMaxRoutes) mask |= (uint32_t{1} << s);
    };
    auto walkRackChildren = [&](const RackCore& r) {
        r.forEachChildDevice([&](Device& c) {
            if (!c.bypassed() && !c.asRack()) add(c.sidechainSourceTrackId(), c.sidechainPart());
        });
        r.forEachChildInstrument([&](Instrument& i) { add(i.sidechainSourceTrackId(), i.sidechainPart()); });
    };
    if (includeInstrument && t.instrument) {
        add(t.instrument->sidechainSourceTrackId(), t.instrument->sidechainPart());
        if (const auto* r = t.instrument->asRack()) walkRackChildren(*r);
    }
    if (includeDevices) {
        for (const auto& d : t.devices)
            if (d && !d->bypassed()) {
                add(d->sidechainSourceTrackId(), d->sidechainPart());
                if (const auto* r = d->asRack()) walkRackChildren(*r);
            }
    }
    return mask;
}

void RenderGraphExecutor::runLeafDeviceList(Engine& eng, Graph* g, Track& t, float* buf, int32_t wid, int32_t frames,
    double blockStart, double spb, bool playing, const TransportInfo& ti,
    dsp::PlanarStereoBuffer* overridePlanar) {
    const int32_t nd = static_cast<int32_t>(t.devices.size());
    dsp::PlanarStereoBuffer* planar = nullptr;
    if (overridePlanar && overridePlanar->readyFor(frames)) {
        planar = overridePlanar;
    } else if (wid >= 0 && eng.rs_ && static_cast<size_t>(wid) < eng.rs_->workerPlanarCount &&
               eng.rs_->workerPlanar[static_cast<size_t>(wid)].readyFor(frames)) {
        planar = &eng.rs_->workerPlanar[static_cast<size_t>(wid)];
    }

    for (int32_t i = 0; i < nd; ++i) {
        auto& d = t.devices[static_cast<size_t>(i)];
        if (!d || d->bypassed()) continue;

        if (planar && d->prefersPlanar()) {
            int32_t activePlanar = 0;
            int32_t end = i;
            for (; end < nd; ++end) {
                const auto& cand = t.devices[static_cast<size_t>(end)];
                if (!cand || cand->bypassed()) continue;
                if (!cand->prefersPlanar()) break;
                ++activePlanar;
            }
            if (activePlanar >= 2) {
                planar->deinterleaveFrom(buf, frames);
                const dsp::PlanarAudioSpan span = planar->span(frames);
                for (int32_t k = i; k < end; ++k) {
                    auto& dk = t.devices[static_cast<size_t>(k)];
                    if (!dk || dk->bypassed()) continue;
                    eng.feedDeviceKey(g, *dk, t.id(), frames);
                    if (spb > 0.0) dk->setTransport(blockStart / spb, spb, playing);
                    dk->setTransportInfo(ti);
                    dk->processPlanar(span, frames);
                }
                planar->interleaveTo(buf, frames);
                i = end - 1;
                continue;
            }
        }

        eng.feedDeviceKey(g, *d, t.id(), frames);
        if (spb > 0.0) d->setTransport(blockStart / spb, spb, playing);
        d->setTransportInfo(ti);
        d->process(buf, frames);
    }
}

bool RenderGraphExecutor::instrumentHasBlockInput(const MixContext& ctx, const Track& t, size_t trackPos) {
    if (ctx.eng.liveExprCount_ > 0) return true;
    const auto& blockMidiN = ctx.eng.rs_->blockMidiN;
    const auto& blockExprN = ctx.eng.rs_->blockExprN;
    if (trackPos < blockMidiN.size() && blockMidiN[trackPos] > 0) return true;
    if (trackPos < blockExprN.size() && blockExprN[trackPos] > 0) return true;
    if (!midiEffectsIdle(t)) return true;

    const auto& held = t.liveHeld;
    const int32_t srcId = t.midiFromTrackId();
    if (srcId >= 0) {
        if (held && held->routedFrom != srcId) return true;
        if (ctx.plan) {
            const int32_t j = ctx.plan->resolveMidiSourceIndex(trackPos, srcId);
            if (j >= 0 && static_cast<size_t>(j) < ctx.g->tracks.size()) {
                const Track* s = ctx.g->tracks[static_cast<size_t>(j)].get();
                if (s && s->id() == srcId && s->type() == TrackType::Instrument &&
                    ((static_cast<size_t>(j) < blockMidiN.size() && blockMidiN[static_cast<size_t>(j)] > 0) ||
                        (static_cast<size_t>(j) < blockExprN.size() && blockExprN[static_cast<size_t>(j)] > 0)))
                    return true;
            }
        } else {
            for (size_t j = 0; j < ctx.g->tracks.size(); ++j) {
                const Track* s = ctx.g->tracks[j].get();
                if (!s || s->id() != srcId || s->type() != TrackType::Instrument) continue;
                if ((j < blockMidiN.size() && blockMidiN[j] > 0) || (j < blockExprN.size() && blockExprN[j] > 0))
                    return true;
            }
        }
    } else if (held && (held->routedFrom >= 0 || (held->routedBits[0] | held->routedBits[1]) != 0)) {
        return true;
    }
    if (t.articulationTable && ctx.playing && ctx.arrangementActive && t.instrument &&
        t.instrument->takesArticulations())
        return true;
    return false;
}

bool RenderGraphExecutor::instrumentLeafCanSkip(const MixContext& ctx, const Track& t, size_t trackPos, bool capturing) {
    if (capturing || trackRequiresActiveProcessing(t)) return false;
    if (t.instrument) {
        if (t.instrument->bypassed()) {
            if (!t.instrument->offSettled.load(std::memory_order_relaxed)) return false;
        } else {
            if (!t.instrument->isIdle() || t.instrument->needsIdleRender()) return false;
            if (t.instrument->acceptsSidechain() && t.instrument->sidechainSourceTrackId() >= 0) return false;
        }
    }
    if (chainRest(t) != ChainRest::Settled) return false;
    return !instrumentHasBlockInput(ctx, t, trackPos);
}

void RenderGraphExecutor::initGroupsAndBuses(MixContext& ctx) {
    Engine& eng = ctx.eng;
    Graph* g = ctx.g;
    const RenderRoutingPlan* plan = ctx.plan;
    const size_t ntracks = g->tracks.size();

    if (eng.rs_->cachedRouteGraph != g || eng.rs_->cachedRoutingEpoch != eng.routingEpoch_.load(std::memory_order_acquire)) {
        eng.rs_->cachedRouteGraph = g;
        eng.rs_->cachedRoutingEpoch = eng.routingEpoch_.load(std::memory_order_acquire);
        const size_t cap = std::min(ntracks, eng.rs_->trackRouteSlot.size());
        for (size_t i = 0; i < cap; ++i)
            eng.rs_->trackRouteSlot[i] = g->tracks[i] ? eng.routeSlotForTrack(g->tracks[i]->id()) : -1;
    }

    eng.pullMonitorInput(ctx.frames);
    eng.advanceMonitorLag(ctx.frames);

    const int32_t recSrc = (eng.countInActive_ || !eng.captureGate_.load(std::memory_order_acquire))
                               ? 0
                               : eng.internalRecordSource_.load(std::memory_order_relaxed);
    ctx.recSrc = recSrc;
    const uint32_t capEp = eng.captureEpoch_.load(std::memory_order_acquire);
    if (recSrc != 0) eng.markCaptureEpoch(eng.rtCaptureEpoch_, capEp, /*hardware=*/false);
    if (recSrc > 0) ctx.feedIds[ctx.numFeeds++] = recSrc;

    if (plan) {
        for (int32_t ai : plan->audioTrackIndices()) {
            Track* t = g->tracks[static_cast<size_t>(ai)].get();
            if (t && t->monitor() && t->recordInputSource() > 0 && ctx.numFeeds < 16)
                ctx.feedIds[ctx.numFeeds++] = t->recordInputSource();
        }
        ctx.numReturns = plan->returnCount();
        for (const auto& t : g->tracks)
            if (t->solo()) {
                ctx.anySolo = true;
                break;
            }
    } else {
        for (const auto& t : g->tracks) {
            if (t->type() == TrackType::Audio && t->monitor() && t->recordInputSource() > 0 && ctx.numFeeds < 16)
                ctx.feedIds[ctx.numFeeds++] = t->recordInputSource();
            if (t->solo()) ctx.anySolo = true;
            if (t->type() == TrackType::Return) ++ctx.numReturns;
        }
    }
    if (ctx.numReturns > kMaxReturns) ctx.numReturns = kMaxReturns;

    eng.busState_.returns.beginBlock(static_cast<size_t>(ctx.numReturns), ctx.frames,
        [&](size_t b) { std::fill_n(eng.returnBus_[b].data(), ctx.frames * 2, 0.0f); });

    ctx.nRoutes = eng.routeSlotCount_.load(std::memory_order_acquire);
    std::fill_n(eng.routeFresh_, Engine::kMaxRoutes, false);
    for (int s = 0; s < ctx.nRoutes; ++s)
        if (eng.routeSlotTrackId_[s].load(std::memory_order_relaxed) >= 0) {
            std::fill_n(eng.routeBusNext_[s].data(), ctx.frames * 2, 0.0f);
            std::fill_n(eng.routeBusPreNext_[s].data(), ctx.frames * 2, 0.0f);
        }

    auto& gw = eng.rs_->groupWork;
    if (plan) {
        ctx.numGroups = std::min<int32_t>(Engine::kMaxGroups, plan->groupCount());
        ctx.maxGroupDepth = plan->maxGroupDepth();
        for (int i = 0; i < ctx.numGroups; ++i) {
            const auto& gs = plan->groupSlot(i);
            auto& bw = gw[static_cast<size_t>(i)];
            bw = {};
            bw.track = gs.track;
            bw.trackIndex = gs.trackIndex;
            bw.busIndex = i;
            bw.parentGroupSlot = gs.parentGroupSlot;
            bw.level = gs.depth;
            bool m = false, s = false, fm = false;
            for (int8_t k = 0; k < gs.ancestorCount; ++k) {
                const Track* gt = plan->groupSlot(gs.ancestors[static_cast<size_t>(k)]).track;
                m |= gt->mute();
                fm |= gt->mute() && !(ctx.cueOn && gt->cue());
                s |= gt->solo();
            }
            bw.mute = m;
            bw.feedMute = fm;
            bw.solo = s;
        }
    } else {
        for (size_t i = 0; i < ntracks; ++i) {
            Track* tptr = g->tracks[i].get();
            if (tptr->type() != TrackType::Group || ctx.numGroups >= Engine::kMaxGroups) continue;
            auto& bw = gw[static_cast<size_t>(ctx.numGroups)];
            bw = {};
            bw.track = tptr;
            bw.trackIndex = static_cast<int32_t>(i);
            bw.busIndex = ctx.numGroups;
            ++ctx.numGroups;
        }
        for (int i = 0; i < ctx.numGroups; ++i) {
            auto& bw = gw[static_cast<size_t>(i)];
            bw.parentGroupSlot = ctx.groupSlotOf(bw.track->groupId());
            bool m = false, s = false, fm = false;
            int depth = 0;
            for (int32_t a = bw.track->id(); a >= 0;) {
                const int si = ctx.groupSlotOf(a);
                if (si < 0) break;
                const Track* gt = gw[static_cast<size_t>(si)].track;
                m |= gt->mute();
                fm |= gt->mute() && !(ctx.cueOn && gt->cue());
                s |= gt->solo();
                ++depth;
                a = gt->groupId();
            }
            bw.mute = m;
            bw.feedMute = fm;
            bw.solo = s;
            bw.level = depth;
            if (depth > ctx.maxGroupDepth) ctx.maxGroupDepth = depth;
        }
    }

    eng.busState_.groups.beginBlock(static_cast<size_t>(ctx.numGroups), ctx.frames,
        [&](size_t i) { std::fill_n(eng.groupBus_[i].data(), ctx.frames * 2, 0.0f); });
}

void RenderGraphExecutor::classifyLeaves(MixContext& ctx, int32_t* slotLeaf, int32_t& outMaxLevel) {
    Engine& eng = ctx.eng;
    Graph* g = ctx.g;
    const size_t ntracks = g->tracks.size();
    const auto& gw = eng.rs_->groupWork;
    const bool recArmed = eng.recording_.load(std::memory_order_relaxed);
    const size_t stride = static_cast<size_t>(ctx.frames) * 2;
    ctx.parallelOk = eng.rs_->leafBuf.size() >= ntracks * stride * 2 &&
                     static_cast<uint64_t>(ctx.frames) * 2 <= eng.mixTmp_.size() && eng.rs_->leafWork.size() >= ntracks;

    auto leafMute = [&](size_t li, const Track& t) {
        if (t.mute()) return true;
        const int s = ctx.plan ? ctx.plan->leafParentGroupSlot(li) : ctx.groupSlotOf(t.groupId());
        return s >= 0 && gw[static_cast<size_t>(s)].feedMute;
    };
    auto leafSolo = [&](size_t li, const Track& t) {
        if (t.solo()) return true;
        const int s = ctx.plan ? ctx.plan->leafParentGroupSlot(li) : ctx.groupSlotOf(t.groupId());
        return s >= 0 && gw[static_cast<size_t>(s)].solo;
    };
    // Only a device holding a HeldMidiKeyReader wants its key while silent: with none, a
    // silent track's chain is not walked at all (audio-tails-8 in the perf gate).
    const bool silentKeys = heldMidiKeysRead();
    auto feedSilentMidiKeys = [&](Track& t) {
        if (!silentKeys) return;
        const auto feed = [&](Device& d) {
            const int32_t source = d.sidechainSourceTrackId();
            if (source >= 0 && d.wantsMidiKeyWhenSilent() && !d.bypassed()) eng.feedMidiKey(g, d, source);
        };
        for (auto& d : t.devices)
            if (d && !d->bypassed()) {
                feed(*d);
                if (auto* rack = d->asRack()) rack->forEachChildDevice(feed);
            }
    };

    for (size_t li = 0; li < ntracks; ++li) {
        Track& t = *g->tracks[li];
        Engine::LeafWork& w = eng.rs_->leafWork[li];
        w = Engine::LeafWork{};
        if (t.type() == TrackType::Return || t.type() == TrackType::Group) continue;
        w.t = &t;

        const bool frozenActive = t.frozen() && t.frozenBuf;
        const bool capturing = (t.id() == ctx.capTrackId);
        const bool audible = !leafMute(li, t) && (!ctx.anySolo || leafSolo(li, t));
        const bool feeding = !audible && !leafMute(li, t) && ctx.feedsMonitor(t.id());
        const bool keying = !audible && eng.trackFeedsRoute(t.id());
        const bool postTapMuted = keying && leafMute(li, t);
        const bool monitoring = !frozenActive && !capturing && t.type() == TrackType::Audio && t.monitor();
        const bool cueing = ctx.cueOn && t.cue();

        bool sessionActive = false;
        if (auto& sp = t.sessionPlayer) {
            if (ctx.playing) eng.sessionPreApply(t, ctx.frames / ctx.spb);
            if (sp->maybeApply(ctx.blockStart / ctx.spb, ctx.frames / ctx.spb,
                    eng.launchQuant_.load(std::memory_order_relaxed), eng.seamOffs_)) {
                if (t.instrument) t.instrument->allNotesOff();
                if (t.liveHeld) t.liveHeld->forgetRouted();
                for (auto& md : t.midiEffects)
                    if (md) md->reset();
            }
            sessionActive = !frozenActive && !capturing && !monitoring &&
                            sp->playing.load(std::memory_order_relaxed) >= 0;
        }
        const bool keepEditorAlive = !frozenActive && trackHasOpenEditor(t);
        if (!audible && !capturing && !feeding && !keying && !cueing && !keepEditorAlive) {
            if (sessionActive) {
                if (t.instrument && !t.sessionSilentReleased) {
                    t.instrument->allNotesOff();
                    if (t.liveHeld) t.liveHeld->forgetRouted();
                }
                t.sessionPlayer->articulationReleases.count = 0;
                t.sessionSilentReleased = true;
                t.sessionPlayer->localBeats += ctx.frames / ctx.spb;
                eng.sessionPostRender(t, ctx.frames / ctx.spb);
            }
            t.tailQuiet() = -1;
            if (!frozenActive) feedSilentMidiKeys(t);
            t.setMeter(0, 0, 0, 0);
            t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
            continue;
        }

        t.sessionSilentReleased = false;
        w.audible = audible;
        w.capturing = capturing;
        w.feeding = feeding;
        w.toMix = audible || capturing;
        w.postTapMuted = postTapMuted;
        w.monitoring = monitoring;
        w.cue = cueing;
        w.cueOnly = cueing && !audible && !capturing && !feeding && !keying;
        w.frozenActive = frozenActive;
        w.sessionActive = sessionActive;

        if (frozenActive) {
            if (!ctx.playing) {
                t.setMeter(0, 0, 0, 0);
                t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
                continue;
            }
            w.path = Engine::LeafPath::Frozen;
        } else if (w.monitoring) {
            w.path = Engine::LeafPath::Monitor;
        } else if (sessionActive && t.type() == TrackType::Instrument) {
            w.path = Engine::LeafPath::SessionInst;
        } else if (sessionActive && t.type() == TrackType::Audio) {
            w.path = Engine::LeafPath::SessionAudio;
        } else if (t.type() == TrackType::Instrument) {
            if (instrumentLeafCanSkip(ctx, t, li, capturing)) {
                t.tailQuiet() = -1;
                feedSilentMidiKeys(t);
                t.setMeter(0, 0, 0, 0);
                t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
                continue;
            }
            w.path = Engine::LeafPath::Instrument;
        } else if (ctx.playing && ctx.arrangementActive) {
            w.path = Engine::LeafPath::AudioClips;
        } else if (trackRequiresActiveProcessing(t) || (t.tailQuiet() >= 0 && hasLiveDevice(t))) {
            w.path = Engine::LeafPath::Tail;
        } else {
            t.tailQuiet() = -1;
            feedSilentMidiKeys(t);
            t.setMeter(0, 0, 0, 0);
            t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
            continue;
        }

        const int destSlot = ctx.plan ? ctx.plan->leafParentGroupSlot(li) : ctx.groupSlotOf(t.groupId());
        w.destSlot = static_cast<int8_t>(destSlot);
        w.dest = destSlot >= 0 ? eng.groupBus_[destSlot].data() : ctx.out;
        const int32_t routeSlot = (audible || keying || feeding) ? ctx.cachedRouteSlot(li, t.id()) : -1;
        w.route = routeSlot >= 0 ? eng.routeBusNext_[routeSlot].data() : nullptr;
        w.anySend = false;
        for (int b = 0; b < ctx.numReturns; ++b) {
            w.sends[b] = t.send(b);
            if (w.sends[b] != 0.0f) w.anySend = true;
        }
        w.tapRec = (ctx.recSrc > 0 && t.id() == ctx.recSrc);
        if (destSlot >= 0 && gw[static_cast<size_t>(destSlot)].mute && !gw[static_cast<size_t>(destSlot)].feedMute) {
            w.anySend = false;
            w.tapRec = false;
            w.postTapMuted = true;
        }

        bool eligible = ctx.parallelOk;
        const int32_t srcId = t.midiFromTrackId();
        if (srcId >= 0 && eng.midiLaneOfRt(t.id()) >= 0 && (ctx.playing || eng.countInActive_) && recArmed &&
            ctx.spb > 0.0)
            eligible = false;
        if (srcId >= 0 && t.armed()) eligible = false;
        if (eligible && !canProcessTrackParallel(t)) eligible = false;
        w.eligible = eligible;
    }

    outMaxLevel = 0;
    if (ctx.nRoutes > 0) {
        for (int s = 0; s < ctx.nRoutes; ++s) slotLeaf[s] = -1;
        for (size_t li = 0; li < ntracks; ++li) {
            Engine::LeafWork& w = eng.rs_->leafWork[li];
            if (!w.t || w.path == Engine::LeafPath::Skip) continue;
            for (int s = 0; s < ctx.nRoutes; ++s)
                if (eng.routeSlotTrackId_[s].load(std::memory_order_relaxed) == w.t->id())
                    slotLeaf[s] = static_cast<int>(li);
        }
        if (ctx.parallelOk) {
            if (ctx.plan) {
                for (int32_t si : ctx.plan->sidechainConsumerLeaves()) {
                    Engine::LeafWork& w = eng.rs_->leafWork[static_cast<size_t>(si)];
                    if (w.t && w.path != Engine::LeafPath::Skip) {
                        const bool inst =
                            w.path == Engine::LeafPath::Instrument || w.path == Engine::LeafPath::SessionInst;
                        w.keyNeeds = collectChainSidechainNeeds(eng, *w.t, inst, !w.frozenActive);
                    }
                }
            } else {
                for (size_t li = 0; li < ntracks; ++li) {
                    Engine::LeafWork& w = eng.rs_->leafWork[li];
                    if (w.t && w.path != Engine::LeafPath::Skip) {
                        const bool inst =
                            w.path == Engine::LeafPath::Instrument || w.path == Engine::LeafPath::SessionInst;
                        w.keyNeeds = collectChainSidechainNeeds(eng, *w.t, inst, !w.frozenActive);
                    }
                }
            }
            for (int pass = 0; pass < Engine::kMaxKeyLevels; ++pass) {
                bool changed = false;
                for (size_t li = 0; li < ntracks; ++li) {
                    Engine::LeafWork& w = eng.rs_->leafWork[li];
                    if (w.keyNeeds == 0) continue;
                    for (int s = 0; s < ctx.nRoutes; ++s) {
                        const int p = slotLeaf[s];
                        if (!((w.keyNeeds >> s) & 1u) || p < 0 || p == static_cast<int>(li)) continue;
                        const int nl = eng.rs_->leafWork[static_cast<size_t>(p)].level + 1;
                        if (nl > w.level && nl <= Engine::kMaxKeyLevels) {
                            w.level = nl;
                            changed = true;
                        }
                    }
                }
                if (!changed) break;
            }
            for (size_t li = 0; li < ntracks; ++li) outMaxLevel = std::max(outMaxLevel, eng.rs_->leafWork[li].level);
        }
    }

    if (!ctx.offline && eng.hybrid_ && eng.hybrid_->beginRtActive()) {
        int32_t liveCount = 0;
        int32_t antCount = 0;
        for (size_t li = 0; li < ntracks; ++li) {
            Engine::LeafWork& w = eng.rs_->leafWork[li];
            const Track* tr = ctx.g->tracks[li].get();
            if (!tr || tr->type() == TrackType::Group || tr->type() == TrackType::Return) continue;
            if (!w.t || w.path == Engine::LeafPath::Skip) {
                ++liveCount;
                eng.hybrid_->reclaimOwnershipRt(li, tr->id());
                continue;
            }
            const bool canAnt = (w.path == Engine::LeafPath::AudioClips || w.path == Engine::LeafPath::Instrument ||
                                    w.path == Engine::LeafPath::Frozen);
            const bool feedsKey = eng.trackFeedsRoute(w.t->id());
            bool feedsMidiKey = false;
            for (const auto& other : ctx.g->tracks) {
                if (other && other->midiFromTrackId() == w.t->id()) {
                    feedsMidiKey = true;
                    break;
                }
            }
            if (canAnt && eng.hybrid_->isTrackAnticipativeEligible(
                              *ctx.g, *w.t, ctx.capTrackId, ctx.recSrc, w.keyNeeds, feedsKey || feedsMidiKey)) {
                w.hybridEligible = true;
                ++antCount;
                if (ctx.parallelOk) {
                    float* base = eng.leafBase(li, stride);
                    bool dropTick = false;
                    if (eng.hybrid_->tryConsumePreRendered(li, w.t->id(), ctx.blockStart, ctx.frames, base + stride,
                            dropTick)) {
                        w.hybridServed = true;
                        w.preRendered = true;
                        w.eligible = false;
                        std::copy_n(base + stride, stride, base);
                    } else if (dropTick) {
                        w.hybridDropTick = true;
                        w.preRendered = true;
                        w.eligible = false;
                        std::fill_n(base, stride * 2, 0.0f);
                    }
                }
            } else {
                ++liveCount;
                eng.hybrid_->reclaimOwnershipRt(li, w.t->id());
            }
        }
        eng.hybrid_->setTrackCountsRt(liveCount, antCount);
    }
}

void RenderGraphExecutor::writeLeafTaps(MixContext& ctx, size_t leafIndex, const float* pre, const float* post) {
    Engine& eng = ctx.eng;
    Engine::LeafWork& w = eng.rs_->leafWork[leafIndex];
    Track& t = *w.t;
    const int32_t preSlot = eng.routeSlotForTrack(t.id());
    if (preSlot >= 0) simd_audio::addStereoScaled(eng.routeBusPreNext_[preSlot].data(), pre, 1.0f, 1.0f, ctx.frames);
    w.vol = t.volume();
    w.pan = std::clamp(t.pan(), -1.0f, 1.0f);
    w.tapsDone = true;
    float* route = w.route;
    if (!route || w.postTapMuted || w.cueOnly) return;
    const render_detail::PanStage ps(w.pan, w.vol, eng.panLawTo_, eng.panLawFrom_, eng.panLawPos_, ctx.panLawStep);
    if (!ps.cross) {
        simd_audio::scaleStereo(eng.mixTmp_.data(), post, ps.to.ll, ps.to.rr, ctx.frames);
        simd_audio::addStereoScaled(route, eng.mixTmp_.data(), 1.0f, 1.0f, ctx.frames);
    } else {
        for (int32_t i = 0; i < ctx.frames; ++i) {
            float l, r;
            ps.apply(i, post[i * 2], post[i * 2 + 1], l, r);
            route[i * 2] += l;
            route[i * 2 + 1] += r;
        }
    }
}

void RenderGraphExecutor::runLeafWaves(MixContext& ctx, const int32_t* slotLeaf, int32_t maxLevel) {
    if (!ctx.parallelOk) return;
    Engine& eng = ctx.eng;
    const size_t ntracks = ctx.g->tracks.size();
    const size_t stride = static_cast<size_t>(ctx.frames) * 2;
    const bool staged = maxLevel > 0;

    int m = 0;
    int levelEnd[Engine::kMaxKeyLevels + 1];
    for (int L = 0; L <= maxLevel; ++L) {
        for (size_t li = 0; li < ntracks; ++li) {
            const Engine::LeafWork& w = eng.rs_->leafWork[li];
            if (w.t && w.path != Engine::LeafPath::Skip && w.eligible && w.level == L)
                eng.rs_->parIdx[static_cast<size_t>(m++)] = static_cast<int>(li);
        }
        levelEnd[L] = m;
    }

    const double periodNs = 1e9 * ctx.frames / std::max(1.0, ctx.sr);
    const double roomNs = std::max(0.25 * periodNs, periodNs - eng.serialNs_);
    int helpers = -1;
    if (!ctx.offline) {
        helpers = 0;
        if (eng.rtParallel_ && (eng.rtParallelLeftNs_ -= periodNs) <= 0) {
            if (eng.parWorkNs_ <= roomNs * Engine::kRtExitHysteresis &&
                eng.leafWorkNs_ <= roomNs * Engine::kRtExitHysteresis) {
                eng.rtParallel_ = false;
                eng.leafWorkNs_ = eng.parWorkNs_;
                eng.rtProbeLeftNs_ = Engine::kRtProbeSeconds * 1e9;
            } else {
                eng.rtParallelLeftNs_ = eng.rtHoldNs_;
            }
        }
        if (eng.rtParallel_) {
            const double work = static_cast<double>(std::max(eng.leafWorkNs_, eng.sizeWorkNs_));
            const double target = Engine::kRtSizeLoad * roomNs;
            int threads = static_cast<int>(std::ceil(work / target));
            const double peak = std::min(work, static_cast<double>(eng.leafPeakNs_));
            if (peak > 0.0) {
                const int available = std::min(m, eng.pool_->workerCount() + 1);
                if (peak < target) {
                    const double needed = std::ceil((work - peak) / (target - peak));
                    threads = std::max(threads, static_cast<int>(std::min(static_cast<double>(available), needed)));
                } else {
                    threads = std::max(threads, available);
                }
            }
            helpers = std::clamp(threads - 1, 1, std::max(1, m - 1));
        }
    }

    const bool allowSub = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0;
    const dsp::RenderExecutor::ScopedHost execScope(&ctx.execHost, allowSub);

    int64_t poolPhaseNs = 0;
    int off = 0;
    auto runTask = [&](int wid, int j) {
        const size_t i = static_cast<size_t>(eng.rs_->parIdx[static_cast<size_t>(off + j)]);
        Engine::LeafWork& w = eng.rs_->leafWork[i];
        const auto t0 = !ctx.offline ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point{};
        w.wid = wid;
        float* base = eng.leafBase(i, stride);
        eng.renderLeafDry(
            *w.t, base, w, ctx.g, ctx.frames, ctx.blockStart, ctx.spb, ctx.playing, ctx.ti, ctx.arrangementActive, i);
        std::copy_n(base, stride, base + stride);
        RenderGraphExecutor::processDeviceChain(eng, ctx.g, *w.t, base + stride, wid, ctx.frames, ctx.blockStart,
            ctx.spb, ctx.playing, ctx.ti, w.frozenActive);
        if (!ctx.offline)
            w.workNs = std::chrono::duration_cast<std::chrono::nanoseconds>(
                std::chrono::steady_clock::now() - t0).count();
    };

    auto finishLevel = [&](int L) {
        for (size_t li = 0; li < ntracks; ++li) {
            Engine::LeafWork& w = eng.rs_->leafWork[li];
            if (!w.t || w.path == Engine::LeafPath::Skip || w.level != L) continue;
            float* base = eng.leafBase(li, stride);
            if (!w.eligible && !w.preRendered) {
                eng.renderLeafDry(*w.t, base, w, ctx.g, ctx.frames, ctx.blockStart, ctx.spb, ctx.playing, ctx.ti,
                    ctx.arrangementActive, li);
                std::copy_n(base, stride, base + stride);
                RenderGraphExecutor::processDeviceChain(eng, ctx.g, *w.t, base + stride, eng.pool_->workerCount(),
                    ctx.frames, ctx.blockStart, ctx.spb, ctx.playing, ctx.ti, w.frozenActive);
            }
            w.preRendered = true;
        }
        for (size_t li = 0; li < ntracks; ++li) {
            Engine::LeafWork& w = eng.rs_->leafWork[li];
            if (!w.t || w.path == Engine::LeafPath::Skip || w.level != L) continue;
            float* base = eng.leafBase(li, stride);
            writeLeafTaps(ctx, li, base, base + stride);
        }
        for (int s = 0; s < ctx.nRoutes; ++s)
            if (slotLeaf[s] >= 0 && eng.rs_->leafWork[static_cast<size_t>(slotLeaf[s])].level == L)
                eng.routeFresh_[s] = true;
    };

    for (int L = 0; L <= maxLevel; ++L) {
        const int n = levelEnd[L] - off;
        if (n == 1 && m == 1 && !staged) {
            const auto phase0 = !ctx.offline ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point{};
            runTask(eng.pool_->workerCount(), 0);
            if (!ctx.offline)
                poolPhaseNs += std::chrono::duration_cast<std::chrono::nanoseconds>(
                    std::chrono::steady_clock::now() - phase0).count();
        } else if (n > 0) {
            const int h = helpers <= 0 ? helpers : std::min(helpers, std::max(1, n - 1));
            const auto phase0 = !ctx.offline ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point{};
            eng.pool_->dispatch(n, [&](int wid, int j) { runTask(wid, j); }, h);
            if (!ctx.offline)
                poolPhaseNs += std::chrono::duration_cast<std::chrono::nanoseconds>(
                    std::chrono::steady_clock::now() - phase0).count();
        }
        off = levelEnd[L];
        if (staged) finishLevel(L);
    }

    if (!ctx.offline && m > 0) {
        int64_t work = 0;
        int64_t peak = 0;
        bool alone = true;
        for (int j = 0; j < m; ++j) {
            const Engine::LeafWork& w = eng.rs_->leafWork[static_cast<size_t>(eng.rs_->parIdx[static_cast<size_t>(j)])];
            work += w.workNs;
            peak = std::max(peak, w.workNs);
            alone = alone && w.wid == eng.pool_->workerCount();
        }
        eng.leafPeakNs_ += (peak - eng.leafPeakNs_) / 16;
        eng.phaseNs_ += static_cast<double>(poolPhaseNs);
        if (eng.rtParallel_) {
            eng.parWorkNs_ += (work - eng.parWorkNs_) / 16;
        } else {
            const bool probeMissed = eng.rtProbeLeftNs_ > 0 && static_cast<double>(work) > roomNs;
            if (alone) {
                eng.leafWorkNs_ += (work - eng.leafWorkNs_) / 16;
                eng.rtOverBudgetStreak_ = static_cast<double>(work) > roomNs ? eng.rtOverBudgetStreak_ + 1 : 0;
            }
            if (probeMissed || eng.rtOverBudgetStreak_ >= 2 || (alone && eng.leafWorkNs_ > roomNs)) {
                if (probeMissed) eng.rtHoldNs_ = std::min(2.0 * eng.rtHoldNs_, Engine::kRtMaxHoldSeconds * 1e9);
                eng.rtParallel_ = true;
                eng.rtParallelLeftNs_ = eng.rtHoldNs_;
                eng.rtProbeLeftNs_ = 0;
                eng.rtOverBudgetStreak_ = 0;
                eng.leafWorkNs_ = std::max(eng.leafWorkNs_, work);
                eng.parWorkNs_ = eng.leafWorkNs_;
                eng.sizeWorkNs_ = eng.leafWorkNs_;
                eng.overrunShare_ = 0.0;
            } else if (eng.rtProbeLeftNs_ > 0 && (eng.rtProbeLeftNs_ -= ctx.frames / std::max(1.0, ctx.sr) * 1e9) <= 0) {
                eng.rtHoldNs_ = Engine::kRtHoldSeconds * 1e9;
            }
        }
    }
}

void RenderGraphExecutor::reduceLeavesAndMeterInputs(MixContext& ctx, const int32_t* slotLeaf) {
    Engine& eng = ctx.eng;
    Graph* g = ctx.g;
    const size_t ntracks = g->tracks.size();
    const size_t stride = static_cast<size_t>(ctx.frames) * 2;
    const int callerWid = eng.pool_->workerCount();

    for (size_t li = 0; li < ntracks; ++li) {
        Engine::LeafWork& w = eng.rs_->leafWork[li];
        if (!w.t || w.path == Engine::LeafPath::Skip) continue;
        Track& t = *w.t;
        float* pre = nullptr;
        float* post = nullptr;
        bool parallelDsp = false;
        if ((w.eligible && ctx.parallelOk) || w.preRendered) {
            float* base = eng.leafBase(li, stride);
            pre = base;
            post = base + stride;
            parallelDsp = true;
        } else {
            const bool allowSub = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0;
            const dsp::RenderExecutor::ScopedHost execScope(&ctx.execHost, allowSub);
            eng.renderLeafDry(
                t, eng.scratch_.data(), w, g, ctx.frames, ctx.blockStart, ctx.spb, ctx.playing, ctx.ti,
                ctx.arrangementActive, li);
            pre = post = eng.scratch_.data();
        }

        if (!w.tapsDone) {
            const int32_t preSlot = ctx.cachedRouteSlot(li, t.id());
            if (preSlot >= 0)
                simd_audio::addStereoScaled(eng.routeBusPreNext_[preSlot].data(), pre, 1.0f, 1.0f, ctx.frames);
        }
        if (!parallelDsp) {
            const bool allowSub = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0;
            const dsp::RenderExecutor::ScopedHost execScope(&ctx.execHost, allowSub);
            RenderGraphExecutor::processDeviceChain(eng, g, t, eng.scratch_.data(), callerWid, ctx.frames,
                ctx.blockStart, ctx.spb, ctx.playing, ctx.ti, w.frozenActive);
        }
        if (!ctx.offline && w.hybridEligible && !w.hybridServed && !w.hybridDropTick && eng.hybrid_) {
            eng.hybrid_->handoffToWorkerAfterRtBlock(li, t.id(), ctx.blockStart + ctx.frames);
        }

        if (w.path == Engine::LeafPath::Tail) {
            int64_t& quiet = t.tailQuiet();
            if (simd_audio::peakAbs(post, ctx.frames * 2) > Engine::kTailFloor || trackRequiresActiveProcessing(t))
                quiet = 0;
            else
                quiet += ctx.frames;
            if (quiet > 0) {
                const ChainRest rest = chainRest(t);
                if (rest == ChainRest::Settled ||
                    (rest == ChainRest::Opaque && quiet >= static_cast<int64_t>(Engine::kTailQuietSeconds * ctx.sr)) ||
                    quiet >= static_cast<int64_t>(Engine::kTailMaxQuietSeconds * ctx.sr))
                    quiet = -1;
            }
        } else if (w.path == Engine::LeafPath::AudioClips || w.path == Engine::LeafPath::SessionAudio ||
                   w.path == Engine::LeafPath::Monitor) {
            t.tailQuiet() = 0;
        }

        if (w.capturing) {
            int64_t c = eng.freezeCursor_;
            for (int32_t i = 0; i < ctx.frames && c < eng.freezeCapFrames_; ++i, ++c) {
                eng.freezeCaptureBuf_[c * 2] = post[i * 2];
                eng.freezeCaptureBuf_[c * 2 + 1] = post[i * 2 + 1];
            }
            eng.freezeCursor_ = c;
        }

        if (w.cue) simd_audio::addStereoScaled(eng.cueSeg_, post, 1.0f, 1.0f, ctx.frames);
        if (w.cueOnly) {
            t.setMeter(0, 0, 0, 0);
            t.stereoFold(0.0, 0.0, 0.0, ctx.frames, ctx.stereoA);
            continue;
        }

        const float vol = w.tapsDone ? w.vol : t.volume();
        const float pan = w.tapsDone ? w.pan : std::clamp(t.pan(), -1.0f, 1.0f);
        const render_detail::PanStage ps(pan, vol, eng.panLawTo_, eng.panLawFrom_, eng.panLawPos_, ctx.panLawStep);
        float* dest = w.dest;
        float* route = w.tapsDone ? nullptr : w.route;
        BusAccumResult acc;
        if (!ps.cross) {
            simd_audio::scaleStereo(eng.mixTmp_.data(), post, ps.to.ll, ps.to.rr, ctx.frames);
            const float* m = eng.mixTmp_.data();
            if (route && !w.postTapMuted) simd_audio::addStereoScaled(route, m, 1.0f, 1.0f, ctx.frames);
            if (w.toMix) {
                simd_audio::addStereoScaled(dest, m, 1.0f, 1.0f, ctx.frames);
                if (w.anySend)
                    for (int b = 0; b < ctx.numReturns; ++b)
                        if (w.sends[b] != 0.0f)
                            simd_audio::addStereoScaled(
                                eng.returnBus_[b].data(), m, w.sends[b], w.sends[b], ctx.frames);
            }
            acc = scanPannedMetrics(m, ctx.frames, w.tapRec, eng.internalQueue_, w.toMix);
        } else {
            for (int32_t i = 0; i < ctx.frames; ++i) {
                float l, r;
                ps.apply(i, post[i * 2], post[i * 2 + 1], l, r);
                if (w.tapRec && !eng.internalQueue_.push(InputFrame{l, r})) ++acc.recDropped;
                if (route && !w.postTapMuted) {
                    route[i * 2] += l;
                    route[i * 2 + 1] += r;
                }
                if (!w.toMix) continue;
                dest[i * 2] += l;
                dest[i * 2 + 1] += r;
                if (w.anySend)
                    for (int b = 0; b < ctx.numReturns; ++b)
                        if (w.sends[b] != 0.0f) {
                            eng.returnBus_[b][i * 2] += l * w.sends[b];
                            eng.returnBus_[b][i * 2 + 1] += r * w.sends[b];
                        }
                acc.peakL = std::max(acc.peakL, std::fabs(l));
                acc.peakR = std::max(acc.peakR, std::fabs(r));
                acc.sqL += static_cast<double>(l) * l;
                acc.sqR += static_cast<double>(r) * r;
                acc.sLR += static_cast<double>(l) * r;
            }
        }
        if (acc.recDropped) eng.inputDroppedFrames_.fetch_add(acc.recDropped, std::memory_order_relaxed);
        if (!w.toMix) {
            t.setMeter(0, 0, 0, 0);
            t.stereoFold(0.0, 0.0, 0.0, ctx.frames, ctx.stereoA);
            continue;
        }
        if (acc.peakL > 0.0f || acc.peakR > 0.0f) {
            if (w.destSlot >= 0) eng.busState_.groups.markActive(static_cast<size_t>(w.destSlot));
            if (w.anySend)
                for (int b = 0; b < ctx.numReturns; ++b)
                    if (w.sends[b] != 0.0f) eng.busState_.returns.markActive(static_cast<size_t>(b));
        }
        t.stereoFold(acc.sqL, acc.sqR, acc.sLR, ctx.frames, ctx.stereoA);
        t.setMeter(acc.peakL, acc.peakR, static_cast<float>(std::sqrt(acc.sqL / ctx.frames)),
            static_cast<float>(std::sqrt(acc.sqR / ctx.frames)));
    }

    for (int s = 0; s < ctx.nRoutes; ++s)
        if (slotLeaf[s] >= 0) eng.routeFresh_[s] = true;

    auto meterArmedInput = [&](Track& t) {
        if (t.type() != TrackType::Audio || !t.armed() || (t.frozen() && t.frozenBuf)) return;
        if (t.monitor() && t.id() != ctx.capTrackId) return;
        const int32_t src = t.recordInputSource();
        if (src == -1) {
            t.setMeter(eng.masterPeakL_.load(std::memory_order_relaxed),
                eng.masterPeakR_.load(std::memory_order_relaxed), eng.masterRmsL_.load(std::memory_order_relaxed),
                eng.masterRmsR_.load(std::memory_order_relaxed));
            return;
        }
        const float* in = eng.scratch_.data();
        if (src > 0 && src != t.id()) {
            const int32_t slot = ctx.plan ? ctx.cachedRouteSlot(static_cast<size_t>(ctx.plan->findTrackIndex(src)), src)
                                          : eng.routeSlotForTrack(src);
            if (slot >= 0) in = eng.routeBus_[slot].data();
            else std::fill_n(eng.scratch_.data(), ctx.frames * 2, 0.0f);
        } else {
            eng.renderMonitorInput(g, t, eng.scratch_.data(), ctx.frames);
        }
        const BusAccumResult m = scanPannedMetrics(in, ctx.frames, false, eng.internalQueue_, false);
        t.setMeter(m.peakL, m.peakR, static_cast<float>(std::sqrt(m.sqL / ctx.frames)),
            static_cast<float>(std::sqrt(m.sqR / ctx.frames)));
    };
    if (ctx.plan) {
        for (int32_t ai : ctx.plan->audioTrackIndices())
            if (Track* t = g->tracks[static_cast<size_t>(ai)].get()) meterArmedInput(*t);
    } else {
        for (auto& tptr : g->tracks) meterArmedInput(*tptr);
    }
}

// Resolves intra-wave sidechain key edges over `items[0..count-1]` so keyed buses render
// in a subsequent wave within the same pass without breaking same-block sidechain freshness.
template <typename BusWorkT>
int32_t RenderGraphExecutor::assignBusWaves(const MixContext& ctx, BusWorkT* items, int32_t count) {
    if (count <= 0) return 0;
    int32_t slotOwner[Engine::kMaxRoutes];
    for (int32_t s = 0; s < ctx.nRoutes; ++s) slotOwner[s] = -1;
    for (int32_t i = 0; i < count; ++i) {
        items[static_cast<size_t>(i)].keyNeeds = 0;
        items[static_cast<size_t>(i)].level = 0;
        const int32_t tid = items[static_cast<size_t>(i)].track->id();
        for (int32_t s = 0; s < ctx.nRoutes; ++s)
            if (ctx.eng.routeSlotTrackId_[s].load(std::memory_order_relaxed) == tid) slotOwner[s] = i;
    }
    if (ctx.nRoutes > 0) {
        for (int32_t i = 0; i < count; ++i) {
            auto& bw = items[static_cast<size_t>(i)];
            bw.keyNeeds = collectChainSidechainNeeds(ctx.eng, *bw.track, false, true);
        }
        for (int32_t pass = 0; pass < count; ++pass) {
            bool changed = false;
            for (int32_t i = 0; i < count; ++i) {
                auto& bw = items[static_cast<size_t>(i)];
                if (bw.keyNeeds == 0) continue;
                for (int32_t s = 0; s < ctx.nRoutes; ++s) {
                    const int32_t p = slotOwner[s];
                    if (!((bw.keyNeeds >> s) & 1u) || p < 0 || p == i) continue;
                    const int32_t nl = items[static_cast<size_t>(p)].level + 1;
                    if (nl > bw.level && nl < count) {
                        bw.level = nl;
                        changed = true;
                    }
                }
            }
            if (!changed) break;
        }
    }
    int32_t maxLv = 0;
    for (int32_t i = 0; i < count; ++i) maxLv = std::max(maxLv, items[static_cast<size_t>(i)].level);
    return maxLv;
}

void RenderGraphExecutor::runGroupWaves(MixContext& ctx) {
    Engine& eng = ctx.eng;
    if (ctx.numGroups <= 0) return;

    const size_t stride = static_cast<size_t>(ctx.frames) * 2;
    const bool busScratchOk = eng.rs_->groupScratch.size() >= static_cast<size_t>(ctx.numGroups) * stride &&
                              static_cast<uint64_t>(ctx.frames) * 2 <= eng.mixTmp_.size();
    const bool canParallelWave = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0 && busScratchOk;

    std::array<Engine::BusWork, Engine::kMaxGroups> activeDepth;
    std::array<int32_t, Engine::kMaxGroups> dispatchList{};

    for (int depth = ctx.maxGroupDepth; depth >= 1; --depth) {
        int32_t nActive = 0;
        for (int32_t gi = 0; gi < ctx.numGroups; ++gi) {
            const auto& meta = eng.rs_->groupWork[static_cast<size_t>(gi)];
            if (meta.level != depth || !meta.track) continue;
            Track& t = *meta.track;
            if (meta.feedMute) {
                t.setMeter(0, 0, 0, 0);
                ctx.markFresh(t.id());
                continue;
            }
            const bool busActive = eng.busState_.groups.isActive(static_cast<size_t>(gi));
            if (busActive) t.tailQuiet() = 0;
            else if (canSkipInactiveBus(t)) {
                t.setMeter(0, 0, 0, 0);
                ctx.markFresh(t.id());
                continue;
            }
            Engine::BusWork bw = meta;
            bw.busActive = busActive;
            bw.eligible = busScratchOk && canProcessTrackParallel(t);
            bw.parallelRan = false;
            activeDepth[static_cast<size_t>(nActive++)] = bw;
        }
        if (nActive == 0) continue;

        const int32_t maxWave = assignBusWaves(ctx, activeDepth.data(), nActive);
        const bool allowSub = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0;
        const dsp::RenderExecutor::ScopedHost execScope(&ctx.execHost, allowSub);

        for (int32_t L = 0; L <= maxWave; ++L) {
            int32_t nDispatched = 0;
            for (int32_t idx = 0; idx < nActive; ++idx) {
                auto& bw = activeDepth[static_cast<size_t>(idx)];
                if (bw.level == L && bw.eligible) {
                    bw.parallelRan = true;
                    dispatchList[static_cast<size_t>(nDispatched++)] = idx;
                }
            }

            if (canParallelWave && nDispatched >= 2) {
                const int helpers = ctx.offline ? -1 : std::min(eng.pool_->workerCount(), nDispatched - 1);
                eng.pool_->dispatch(nDispatched, [&](int wid, int j) {
                    const auto& bw = activeDepth[static_cast<size_t>(dispatchList[static_cast<size_t>(j)])];
                    float* buf = eng.rs_->groupScratch.data() + static_cast<size_t>(bw.busIndex) * stride;
                    std::copy_n(eng.groupBus_[static_cast<size_t>(bw.busIndex)].data(), ctx.frames * 2, buf);
                    RenderGraphExecutor::processDeviceChain(eng, ctx.g, *bw.track, buf, wid, ctx.frames,
                        ctx.blockStart, ctx.spb, ctx.playing, ctx.ti, false);
                }, helpers);
            } else {
                for (int32_t j = 0; j < nDispatched; ++j) activeDepth[static_cast<size_t>(dispatchList[static_cast<size_t>(j)])].parallelRan = false;
            }

            const int callerWid = eng.pool_->workerCount();
            for (int32_t idx = 0; idx < nActive; ++idx) {
                const auto& bw = activeDepth[static_cast<size_t>(idx)];
                if (bw.level != L) continue;
                Track& t = *bw.track;
                const int32_t gi = bw.busIndex;
                float* buf = bw.parallelRan
                    ? (eng.rs_->groupScratch.data() + static_cast<size_t>(gi) * stride)
                    : eng.scratch_.data();

                // Write pre-FX tap from untouched children sum in groupBus_[gi].
                const int32_t preSlot = eng.routeSlotForTrack(t.id());
                if (preSlot >= 0 && !bw.mute)
                    simd_audio::addStereoScaled(eng.routeBusPreNext_[preSlot].data(),
                        eng.groupBus_[static_cast<size_t>(gi)].data(), 1.0f, 1.0f, ctx.frames);

                if (!bw.parallelRan) {
                    std::copy_n(eng.groupBus_[static_cast<size_t>(gi)].data(), ctx.frames * 2, buf);
                    RenderGraphExecutor::processDeviceChain(eng, ctx.g, t, buf, callerWid, ctx.frames, ctx.blockStart,
                        ctx.spb, ctx.playing, ctx.ti, false);
                }

                if (!bw.busActive)
                    settleInactiveBus(t, buf, ctx.frames, ctx.sr, Engine::kTailFloor, Engine::kTailQuietSeconds,
                        Engine::kTailMaxQuietSeconds);
                if (ctx.cueOn && t.cue()) simd_audio::addStereoScaled(eng.cueSeg_, buf, 1.0f, 1.0f, ctx.frames);
                if (t.mute()) {
                    t.setMeter(0, 0, 0, 0);
                    ctx.markFresh(t.id());
                    continue;
                }

                const bool cueFeedOnly = bw.mute;
                const float vol = t.volume();
                const float pan = std::clamp(t.pan(), -1.0f, 1.0f);
                const float gl = (pan <= 0.0f ? 1.0f : 1.0f - pan) * vol;
                const float gr = (pan >= 0.0f ? 1.0f : 1.0f + pan) * vol;
                const int pslot = bw.parentGroupSlot;
                float* dest = pslot >= 0 ? eng.groupBus_[static_cast<size_t>(pslot)].data() : ctx.out;
                const int32_t routeSlot = ctx.plan
                    ? ctx.cachedRouteSlot(static_cast<size_t>(bw.trackIndex), t.id())
                    : eng.routeSlotForTrack(t.id());
                float* route = routeSlot >= 0 && !cueFeedOnly ? eng.routeBusNext_[routeSlot].data() : nullptr;
                const bool tapRec = (ctx.recSrc > 0 && t.id() == ctx.recSrc) && !cueFeedOnly;

                simd_audio::scaleStereo(eng.mixTmp_.data(), buf, gl, gr, ctx.frames);
                const float* m = eng.mixTmp_.data();
                simd_audio::addStereoScaled(dest, m, 1.0f, 1.0f, ctx.frames);
                if (route) simd_audio::addStereoScaled(route, m, 1.0f, 1.0f, ctx.frames);
                const BusAccumResult acc = scanPannedMetrics(m, ctx.frames, tapRec, eng.internalQueue_, false);
                if (acc.recDropped) eng.inputDroppedFrames_.fetch_add(acc.recDropped, std::memory_order_relaxed);
                if (pslot >= 0 && (acc.peakL > 0.0f || acc.peakR > 0.0f))
                    eng.busState_.groups.markActive(static_cast<size_t>(pslot));
                if (cueFeedOnly) t.setMeter(0, 0, 0, 0);
                else
                    t.setMeter(acc.peakL, acc.peakR, static_cast<float>(std::sqrt(acc.sqL / ctx.frames)),
                        static_cast<float>(std::sqrt(acc.sqR / ctx.frames)));
                ctx.markFresh(t.id());
            }
        }
    }
}

void RenderGraphExecutor::runReturnWaves(MixContext& ctx) {
    Engine& eng = ctx.eng;
    if (ctx.numReturns <= 0) return;

    const size_t stride = static_cast<size_t>(ctx.frames) * 2;
    const bool busScratchOk = eng.rs_->returnScratch.size() >= static_cast<size_t>(ctx.numReturns) * stride &&
                              static_cast<uint64_t>(ctx.frames) * 2 <= eng.mixTmp_.size();
    const bool canParallelWave = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0 && busScratchOk;

    std::array<Engine::BusWork, kMaxReturns> activeRets;
    std::array<int32_t, kMaxReturns> dispatchList{};
    int32_t nActive = 0;

    for (size_t i = 0; i < ctx.g->tracks.size(); ++i) {
        Track& t = *ctx.g->tracks[i];
        if (t.type() != TrackType::Return) continue;
        const int32_t bus = t.returnIndex();
        const bool returnCue = ctx.cueOn && t.cue();
        if (bus < 0 || bus >= ctx.numReturns || (t.mute() && !returnCue)) {
            t.setMeter(0, 0, 0, 0);
            t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
            ctx.markFresh(t.id());
            continue;
        }
        const bool busActive = eng.busState_.returns.isActive(static_cast<size_t>(bus));
        if (busActive) t.tailQuiet() = 0;
        else if (canSkipInactiveBus(t)) {
            t.setMeter(0, 0, 0, 0);
            t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
            ctx.markFresh(t.id());
            continue;
        }
        Engine::BusWork bw{};
        bw.track = &t;
        bw.trackIndex = static_cast<int32_t>(i);
        bw.busIndex = bus;
        bw.busActive = busActive;
        bw.cue = returnCue;
        bw.eligible = busScratchOk && canProcessTrackParallel(t);
        activeRets[static_cast<size_t>(nActive++)] = bw;
    }
    if (nActive == 0) return;

    const int32_t maxWave = assignBusWaves(ctx, activeRets.data(), nActive);
    const bool allowSub = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0;
    const dsp::RenderExecutor::ScopedHost execScope(&ctx.execHost, allowSub);

    for (int32_t L = 0; L <= maxWave; ++L) {
        int32_t nDispatched = 0;
        for (int32_t idx = 0; idx < nActive; ++idx) {
            auto& bw = activeRets[static_cast<size_t>(idx)];
            if (bw.level == L && bw.eligible) {
                bw.parallelRan = true;
                dispatchList[static_cast<size_t>(nDispatched++)] = idx;
            }
        }

        if (canParallelWave && nDispatched >= 2) {
            const int helpers = ctx.offline ? -1 : std::min(eng.pool_->workerCount(), nDispatched - 1);
            eng.pool_->dispatch(nDispatched, [&](int wid, int j) {
                const auto& bw = activeRets[static_cast<size_t>(dispatchList[static_cast<size_t>(j)])];
                float* buf = eng.rs_->returnScratch.data() + static_cast<size_t>(bw.busIndex) * stride;
                std::copy_n(eng.returnBus_[static_cast<size_t>(bw.busIndex)].data(), ctx.frames * 2, buf);
                RenderGraphExecutor::processDeviceChain(eng, ctx.g, *bw.track, buf, wid, ctx.frames, ctx.blockStart,
                    ctx.spb, ctx.playing, ctx.ti, false);
            }, helpers);
        } else {
            for (int32_t j = 0; j < nDispatched; ++j) activeRets[static_cast<size_t>(dispatchList[static_cast<size_t>(j)])].parallelRan = false;
        }

        const int callerWid = eng.pool_->workerCount();
        for (int32_t idx = 0; idx < nActive; ++idx) {
            const auto& bw = activeRets[static_cast<size_t>(idx)];
            if (bw.level != L) continue;
            Track& t = *bw.track;
            const int32_t bus = bw.busIndex;
            float* buf = bw.parallelRan
                ? (eng.rs_->returnScratch.data() + static_cast<size_t>(bus) * stride)
                : eng.scratch_.data();

            const int32_t preSlot = eng.routeSlotForTrack(t.id());
            if (preSlot >= 0 && !t.mute())
                simd_audio::addStereoScaled(eng.routeBusPreNext_[preSlot].data(),
                    eng.returnBus_[static_cast<size_t>(bus)].data(), 1.0f, 1.0f, ctx.frames);

            if (!bw.parallelRan) {
                std::copy_n(eng.returnBus_[static_cast<size_t>(bus)].data(), ctx.frames * 2, buf);
                RenderGraphExecutor::processDeviceChain(eng, ctx.g, t, buf, callerWid, ctx.frames, ctx.blockStart,
                    ctx.spb, ctx.playing, ctx.ti, false);
            }

            if (!bw.busActive)
                settleInactiveBus(t, buf, ctx.frames, ctx.sr, Engine::kTailFloor, Engine::kTailQuietSeconds,
                    Engine::kTailMaxQuietSeconds);
            if (bw.cue) simd_audio::addStereoScaled(eng.cueSeg_, buf, 1.0f, 1.0f, ctx.frames);
            if (t.mute()) {
                t.setMeter(0, 0, 0, 0);
                t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
                ctx.markFresh(t.id());
                continue;
            }

            const float vol = t.volume();
            const float pan = std::clamp(t.pan(), -1.0f, 1.0f);
            const render_detail::PanStage ps(pan, vol, eng.panLawTo_, eng.panLawFrom_, eng.panLawPos_, ctx.panLawStep);
            const int32_t routeSlot = eng.routeSlotForTrack(t.id());
            float* route = routeSlot >= 0 ? eng.routeBusNext_[routeSlot].data() : nullptr;
            const bool tapRec = (ctx.recSrc > 0 && t.id() == ctx.recSrc);
            BusAccumResult acc;
            if (!ps.cross) {
                simd_audio::scaleStereo(eng.mixTmp_.data(), buf, ps.to.ll, ps.to.rr, ctx.frames);
                const float* m = eng.mixTmp_.data();
                simd_audio::addStereoScaled(ctx.out, m, 1.0f, 1.0f, ctx.frames);
                if (route) simd_audio::addStereoScaled(route, m, 1.0f, 1.0f, ctx.frames);
                acc = scanPannedMetrics(m, ctx.frames, tapRec, eng.internalQueue_, true);
            } else {
                for (int32_t i = 0; i < ctx.frames; ++i) {
                    float l, r;
                    ps.apply(i, buf[i * 2], buf[i * 2 + 1], l, r);
                    ctx.out[i * 2] += l;
                    ctx.out[i * 2 + 1] += r;
                    if (tapRec && !eng.internalQueue_.push(InputFrame{l, r})) ++acc.recDropped;
                    if (route) {
                        route[i * 2] += l;
                        route[i * 2 + 1] += r;
                    }
                    acc.peakL = std::max(acc.peakL, std::fabs(l));
                    acc.peakR = std::max(acc.peakR, std::fabs(r));
                    acc.sqL += static_cast<double>(l) * l;
                    acc.sqR += static_cast<double>(r) * r;
                    acc.sLR += static_cast<double>(l) * r;
                }
            }
            if (acc.recDropped) eng.inputDroppedFrames_.fetch_add(acc.recDropped, std::memory_order_relaxed);
            t.stereoFold(acc.sqL, acc.sqR, acc.sLR, ctx.frames, ctx.stereoA);
            t.setMeter(acc.peakL, acc.peakR, static_cast<float>(std::sqrt(acc.sqL / ctx.frames)),
                static_cast<float>(std::sqrt(acc.sqR / ctx.frames)));
            ctx.markFresh(t.id());
        }
    }
}

void RenderGraphExecutor::finalizeMixBlock(MixContext& ctx) {
    Engine& eng = ctx.eng;
    if (ctx.recSrc == -1) {
        int64_t recDropped = 0;
        for (int32_t i = 0; i < ctx.frames; ++i)
            if (!eng.internalQueue_.push(InputFrame{ctx.out[i * 2], ctx.out[i * 2 + 1]})) ++recDropped;
        if (recDropped) eng.inputDroppedFrames_.fetch_add(recDropped, std::memory_order_relaxed);
    }

    eng.publishTrackTapRings(ctx.frames);
    for (int s = 0, n = eng.routeSlotCount_.load(std::memory_order_acquire); s < n; ++s)
        if (eng.routeSlotTrackId_[s].load(std::memory_order_relaxed) >= 0) {
            eng.routeBus_[s].swap(eng.routeBusNext_[s]);
            eng.routeBusPre_[s].swap(eng.routeBusPreNext_[s]);
        }

    if (eng.panLawPos_ < 1.0f)
        eng.panLawPos_ = std::min(1.0f, eng.panLawPos_ + ctx.panLawStep * static_cast<float>(ctx.frames));
}

void RenderGraphExecutor::processDeviceChain(Engine& eng, Graph* g, Track& t, float* buf, int32_t wid, int32_t frames,
    double blockStart, double spb, bool playing, const TransportInfo& ti, bool frozenActive,
    dsp::PlanarStereoBuffer* overridePlanar) {
    if (!frozenActive) runLeafDeviceList(eng, g, t, buf, wid, frames, blockStart, spb, playing, ti, overridePlanar);
    if (!frozenActive && t.pdc) t.pdc->process(buf, frames);
}

void RenderGraphExecutor::mixGraph(Engine& eng, Graph* g, float* out, int32_t frames, double blockStart,
    double elapsedStart, bool playing, double spb) {
    if (eng.panLawSnap_.exchange(false, std::memory_order_acq_rel)) {
        eng.panLawFrom_ = eng.panLawTo_ = g->panLaw;
        eng.panLawPos_ = 1.0f;
    } else if (g->panLaw != eng.panLawTo_) {
        if (eng.panLawPos_ < 1.0f && g->panLaw == eng.panLawFrom_) {
            std::swap(eng.panLawFrom_, eng.panLawTo_);
            eng.panLawPos_ = 1.0f - eng.panLawPos_;
        } else {
            eng.panLawFrom_ = eng.panLawTo_;
            eng.panLawTo_ = g->panLaw;
            eng.panLawPos_ = 0.0f;
        }
    }

    const double srLaw = eng.transport_.sampleRate() > 0 ? eng.transport_.sampleRate() : 44100.0;
    if (eng.hybrid_ && eng.deviceBlock_) {
        const bool hybridPool = eng.pool_ && eng.pool_->workerCount() > 0;
        eng.hybrid_->beginRtBlock(g, blockStart, frames, spb, srLaw,
            playing && eng.arrangementActive_.load(std::memory_order_relaxed) && !eng.countInActive_,
            eng.transport_.isLooping(), eng.transport_.loopStartSamples(), eng.transport_.loopEndSamples(), hybridPool);
    }
    if (spb > 0.0) eng.applyAutomation(g, blockStart / spb, (blockStart + frames) / spb);
    if (spb > 0.0) {
        eng.updateModulators(g, blockStart / spb, elapsedStart / srLaw, frames / srLaw);
        eng.applyModulation(g, blockStart / spb, elapsedStart / srLaw);
    }

    MixContext ctx{eng};
    ctx.g = g;
    ctx.plan = g->routingPlan.get();
    ctx.out = out;
    ctx.frames = frames;
    ctx.blockStart = blockStart;
    ctx.playing = playing;
    ctx.spb = spb;
    ctx.sr = srLaw;
    ctx.cueOn = eng.cueSeg_ != nullptr;
    ctx.arrangementActive = eng.arrangementActive_.load(std::memory_order_relaxed);
    ctx.offline = !eng.deviceBlock_;
    ctx.stereoA = render_detail::stereoBlockStep(frames, srLaw);
    ctx.panLawStep = static_cast<float>(1.0 / (0.010 * srLaw));
    ctx.capTrackId = eng.freezeCaptureTrackId_.load(std::memory_order_relaxed);
    ctx.ti = eng.transport_.transportInfo(eng.transport_.hasTempoMap() ? eng.transport_.playheadSamples() : blockStart);
    ctx.execHost = eng.pool_->executorHost();

    initGroupsAndBuses(ctx);
    eng.computeBlockMidi(g, frames, blockStart, spb, playing, ctx.arrangementActive);

    int32_t slotLeaf[Engine::kMaxRoutes] = {};
    int32_t maxLevel = 0;
    classifyLeaves(ctx, slotLeaf, maxLevel);
    runLeafWaves(ctx, slotLeaf, maxLevel);
    reduceLeavesAndMeterInputs(ctx, slotLeaf);
    runGroupWaves(ctx);
    runReturnWaves(ctx);
    finalizeMixBlock(ctx);
}

} // namespace nota
