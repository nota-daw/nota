// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RenderGraphExecutor — parallel wave execution and deterministic bus reduction.

#include "render/RenderGraphExecutor.h"
#include "render/RenderGraphContext.h"

#include "model/Track.h"
#include "model/EngineTypes.h"
#include "nota/dsp/Device.h"
#include "nota/dsp/Instrument.h"
#include "nota/devices/racks/RackCore.h"
#include "nota/dsp/rt/AudioSpan.h"
#include "nota/dsp/rt/PlanarStereoBuffer.h"
#include "nota/dsp/rt/RenderExecutor.h"
#include "core/EnginePrelude.h"
#include "core/EngineRuntime.h"
#include "recording/RecordingOps.h"
#include "render/RenderTypes.h"
#include "session/SessionOps.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "render/RenderRoutingPlan.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace nota::detail {

using namespace render_detail;

uint32_t RenderGraphExecutor::collectChainSidechainNeeds(
    const EngineRuntime& eng, const Track& t, bool includeInstrument, bool includeDevices) {
    uint32_t mask = 0;
    auto add = [&](int32_t src, uint64_t part) {
        if (src < 0) return;
        const int32_t s = eng.routeSlotFor(src, part);
        if (s >= 0 && s < kMaxRoutes) mask |= (uint32_t{1} << s);
    };
    auto walkRackChildren = [&](const RackCore& r) {
        if (!r.hasSidechainChild()) return;
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

void RenderGraphExecutor::runLeafDeviceList(EngineRuntime& eng, Graph* g, Track& t, float* buf, int32_t wid,
    int32_t frames, double blockStart, double spb, bool playing, const TransportInfo& ti,
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

bool RenderGraphExecutor::instrumentLeafCanSkip(
    const MixContext& ctx, const Track& t, size_t trackPos, bool capturing) {
    if (capturing || trackRequiresActiveProcessing(t, ctx.anyOpenEditor, ctx.anyNeverSleep)) return false;
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
    EngineRuntime& eng = ctx.eng;
    Graph* g = ctx.g;
    const RenderRoutingPlan* plan = ctx.plan;
    const size_t ntracks = g->tracks.size();

    if (eng.rs_->cachedRouteGraph != g ||
        eng.rs_->cachedRoutingEpoch != eng.routingEpoch_.load(std::memory_order_acquire)) {
        eng.rs_->cachedRouteGraph = g;
        eng.rs_->cachedRoutingEpoch = eng.routingEpoch_.load(std::memory_order_acquire);
        const size_t cap = std::min(ntracks, eng.rs_->trackRouteSlot.size());
        for (size_t i = 0; i < cap; ++i)
            eng.rs_->trackRouteSlot[i] = g->tracks[i] ? eng.routeSlotForTrack(g->tracks[i]->id()) : -1;
    }

    eng.recorder().pullMonitorInput(ctx.frames);
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
    if (ctx.nRoutes > 0) {
        std::fill_n(eng.routeFresh_, ctx.nRoutes, false);
        for (int s = 0; s < ctx.nRoutes; ++s)
            if (eng.routeSlotTrackId_[static_cast<size_t>(s)].load(std::memory_order_relaxed) >= 0) {
                std::fill_n(eng.routeBusNext_[static_cast<size_t>(s)].data(), ctx.frames * 2, 0.0f);
                std::fill_n(eng.routeBusPreNext_[static_cast<size_t>(s)].data(), ctx.frames * 2, 0.0f);
            }
    }

    auto& gw = eng.rs_->groupWork;
    if (plan) {
        ctx.numGroups = std::min<int32_t>(kMaxGroups, plan->groupCount());
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
            if (tptr->type() != TrackType::Group || ctx.numGroups >= kMaxGroups) continue;
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
    EngineRuntime& eng = ctx.eng;
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
                if (auto* rack = d->asRack(); rack && rack->hasSidechainChild()) rack->forEachChildDevice(feed);
            }
    };

    for (size_t li = 0; li < ntracks; ++li) {
        Track& t = *g->tracks[li];
        LeafWork& w = eng.rs_->leafWork[li];
        auto markSkipped = [&] {
            w.t = nullptr;
            w.path = LeafPath::Skip;
            w.level = 0;
            w.keyNeeds = 0;
            w.preRendered = false;
            w.hybridEligible = false;
            w.hybridServed = false;
            w.hybridDropTick = false;
        };
        if (t.type() == TrackType::Return || t.type() == TrackType::Group) {
            markSkipped();
            continue;
        }

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
            if (ctx.playing) eng.session().sessionPreApply(t, ctx.frames / ctx.spb);
            if (sp->maybeApply(ctx.blockStart / ctx.spb, ctx.frames / ctx.spb,
                    eng.launchQuant_.load(std::memory_order_relaxed), eng.seamOffs_)) {
                if (t.instrument) t.instrument->allNotesOff();
                if (t.liveHeld) t.liveHeld->forgetRouted();
                for (auto& md : t.midiEffects)
                    if (md) md->reset();
            }
            sessionActive =
                !frozenActive && !capturing && !monitoring && sp->playing.load(std::memory_order_relaxed) >= 0;
        }
        const bool keepEditorAlive = !frozenActive && trackHasOpenEditor(t, ctx.anyOpenEditor);
        if (!audible && !capturing && !feeding && !keying && !cueing && !keepEditorAlive) {
            if (sessionActive) {
                if (t.instrument && !t.sessionSilentReleased) {
                    t.instrument->allNotesOff();
                    if (t.liveHeld) t.liveHeld->forgetRouted();
                }
                t.sessionPlayer->articulationReleases.count = 0;
                t.sessionSilentReleased = true;
                t.sessionPlayer->localBeats += ctx.frames / ctx.spb;
                eng.session().sessionPostRender(t, ctx.frames / ctx.spb);
            }
            t.tailQuiet() = -1;
            if (!frozenActive) feedSilentMidiKeys(t);
            t.setMeter(0, 0, 0, 0);
            t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
            markSkipped();
            continue;
        }

        t.sessionSilentReleased = false;
        LeafPath path = LeafPath::Skip;
        if (frozenActive) {
            if (!ctx.playing) {
                t.setMeter(0, 0, 0, 0);
                t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
                markSkipped();
                continue;
            }
            path = LeafPath::Frozen;
        } else if (monitoring) {
            path = LeafPath::Monitor;
        } else if (sessionActive && t.type() == TrackType::Instrument) {
            path = LeafPath::SessionInst;
        } else if (sessionActive && t.type() == TrackType::Audio) {
            path = LeafPath::SessionAudio;
        } else if (t.type() == TrackType::Instrument) {
            if (instrumentLeafCanSkip(ctx, t, li, capturing)) {
                t.tailQuiet() = -1;
                feedSilentMidiKeys(t);
                t.setMeter(0, 0, 0, 0);
                t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
                markSkipped();
                continue;
            }
            path = LeafPath::Instrument;
        } else if (ctx.playing && ctx.arrangementActive) {
            path = LeafPath::AudioClips;
        } else if (trackRequiresActiveProcessing(t, ctx.anyOpenEditor, ctx.anyNeverSleep) ||
                   (t.tailQuiet() >= 0 && hasLiveDevice(t))) {
            path = LeafPath::Tail;
        } else {
            t.tailQuiet() = -1;
            feedSilentMidiKeys(t);
            t.setMeter(0, 0, 0, 0);
            t.stereoFold(0, 0, 0, ctx.frames, ctx.stereoA);
            markSkipped();
            continue;
        }

        w.t = &t;
        w.path = path;
        w.audible = audible;
        w.capturing = capturing;
        w.feeding = feeding;
        w.toMix = audible || capturing;
        w.monitoring = monitoring;
        w.frozenActive = frozenActive;
        w.sessionActive = sessionActive;
        w.cue = cueing;
        w.cueOnly = cueing && !audible && !capturing && !feeding && !keying;
        w.postTapMuted = postTapMuted;
        w.wid = -1;
        w.workNs = 0;
        w.keyNeeds = 0;
        w.level = 0;
        w.preRendered = false;
        w.tapsDone = false;
        w.copyPost = true;
        w.drySilent = false;
        w.postSilent = false;
        w.hybridEligible = false;
        w.hybridServed = false;
        w.hybridDropTick = false;
        w.vol = 1.0f;
        w.pan = 0.0f;

        const int destSlot = ctx.plan ? ctx.plan->leafParentGroupSlot(li) : ctx.groupSlotOf(t.groupId());
        w.destSlot = static_cast<int8_t>(destSlot);
        w.dest = destSlot >= 0 ? eng.groupBus_[static_cast<size_t>(destSlot)].data() : ctx.out;
        const int32_t routeSlot = (audible || keying || feeding) ? ctx.cachedRouteSlot(li, t.id()) : -1;
        w.route = routeSlot >= 0 ? eng.routeBusNext_[static_cast<size_t>(routeSlot)].data() : nullptr;
        w.anySend = false;
        const int nRet = std::min(ctx.numReturns, kMaxReturns);
        for (int b = 0; b < nRet; ++b) {
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
        const bool needParallelCheck = ctx.offline || eng.rtParallel_ || !eng.pool_->allAsleep();
        if (eligible && needParallelCheck && !canProcessTrackParallel(t)) eligible = false;
        w.eligible = eligible;
    }

    outMaxLevel = 0;
    if (ctx.nRoutes > 0) {
        for (int s = 0; s < ctx.nRoutes; ++s) slotLeaf[s] = -1;
        for (size_t li = 0; li < ntracks; ++li) {
            LeafWork& w = eng.rs_->leafWork[li];
            if (!w.t || w.path == LeafPath::Skip) continue;
            for (int s = 0; s < ctx.nRoutes; ++s)
                if (eng.routeSlotTrackId_[s].load(std::memory_order_relaxed) == w.t->id())
                    slotLeaf[s] = static_cast<int>(li);
        }
        if (ctx.parallelOk) {
            if (ctx.plan) {
                for (int32_t si : ctx.plan->sidechainConsumerLeaves()) {
                    LeafWork& w = eng.rs_->leafWork[static_cast<size_t>(si)];
                    if (w.t && w.path != LeafPath::Skip) {
                        const bool inst = w.path == LeafPath::Instrument || w.path == LeafPath::SessionInst;
                        w.keyNeeds = collectChainSidechainNeeds(eng, *w.t, inst, !w.frozenActive);
                    }
                }
            } else {
                for (size_t li = 0; li < ntracks; ++li) {
                    LeafWork& w = eng.rs_->leafWork[li];
                    if (w.t && w.path != LeafPath::Skip) {
                        const bool inst = w.path == LeafPath::Instrument || w.path == LeafPath::SessionInst;
                        w.keyNeeds = collectChainSidechainNeeds(eng, *w.t, inst, !w.frozenActive);
                    }
                }
            }
            for (int pass = 0; pass < kMaxKeyLevels; ++pass) {
                bool changed = false;
                for (size_t li = 0; li < ntracks; ++li) {
                    LeafWork& w = eng.rs_->leafWork[li];
                    if (w.keyNeeds == 0) continue;
                    for (int s = 0; s < ctx.nRoutes; ++s) {
                        const int p = slotLeaf[s];
                        if (!((w.keyNeeds >> s) & 1u) || p < 0 || p == static_cast<int>(li)) continue;
                        const int nl = eng.rs_->leafWork[static_cast<size_t>(p)].level + 1;
                        if (nl > w.level && nl <= kMaxKeyLevels) {
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
            LeafWork& w = eng.rs_->leafWork[li];
            const Track* tr = ctx.g->tracks[li].get();
            if (!tr || tr->type() == TrackType::Group || tr->type() == TrackType::Return) continue;
            if (!w.t || w.path == LeafPath::Skip) {
                ++liveCount;
                eng.hybrid_->reclaimOwnershipRt(li, tr->id());
                continue;
            }
            const bool canAnt =
                (w.path == LeafPath::AudioClips || w.path == LeafPath::Instrument || w.path == LeafPath::Frozen);
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
                    if (eng.hybrid_->tryConsumePreRendered(
                            li, w.t->id(), ctx.blockStart, ctx.frames, base + stride, dropTick)) {
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

void RenderGraphExecutor::processDeviceChain(EngineRuntime& eng, Graph* g, Track& t, float* buf, int32_t wid,
    int32_t frames, double blockStart, double spb, bool playing, const TransportInfo& ti, bool frozenActive,
    dsp::PlanarStereoBuffer* overridePlanar) {
    if (!frozenActive && !t.devices.empty())
        runLeafDeviceList(eng, g, t, buf, wid, frames, blockStart, spb, playing, ti, overridePlanar);
    if (!frozenActive && t.pdc && !t.pdc->isIdle()) t.pdc->process(buf, frames);
}

void RenderGraphExecutor::mixGraph(EngineRuntime& eng, Graph* g, float* out, int32_t frames, double blockStart,
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
    if (spb > 0.0) {
        if (g->hasAutomation) eng.automation().applyAutomation(g, blockStart / spb, (blockStart + frames) / spb);
        if (g->hasModulators || g->hasCvLinks) {
            const double beat = blockStart / spb;
            const double tSec = elapsedStart / srLaw;
            if (g->hasModulators) eng.automation().updateModulators(g, beat, tSec, frames / srLaw);
            if (g->hasCvLinks) eng.automation().applyModulation(g, beat, tSec);
        }
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
    ctx.anyOpenEditor = dsp::anyOpenPluginEditor();
    ctx.anyNeverSleep = dsp::anyNeverSleepDevice();
    ctx.stereoA = eng.cachedStereoBlockStep(frames, srLaw);
    ctx.panLawStep = static_cast<float>(1.0 / (0.010 * srLaw));
    ctx.capTrackId = eng.freezeCaptureTrackId_.load(std::memory_order_relaxed);
    ctx.ti = eng.transport_.transportInfo(eng.transport_.hasTempoMap() ? eng.transport_.playheadSamples() : blockStart);
    ctx.execHost = eng.pool_->executorHost();

    initGroupsAndBuses(ctx);
    eng.computeBlockMidi(g, frames, blockStart, spb, playing, ctx.arrangementActive);

    int32_t slotLeaf[kMaxRoutes];
    int32_t maxLevel = 0;
    classifyLeaves(ctx, slotLeaf, maxLevel);
    // A lone offline leaf has no sibling tracks to parallelize against; skipping the
    // wave scratch avoids an 8 KB pre/post copy while sub-parallelism still fans out.
    if (ctx.offline && g->tracks.size() == 1 && maxLevel == 0) ctx.parallelOk = false;
    if (ctx.parallelOk) runLeafWaves(ctx, slotLeaf, maxLevel);
    reduceLeavesAndMeterInputs(ctx, slotLeaf);
    runGroupWaves(ctx);
    runReturnWaves(ctx);
    finalizeMixBlock(ctx);
}

} // namespace nota::detail
