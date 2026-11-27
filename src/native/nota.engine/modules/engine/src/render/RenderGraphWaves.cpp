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

void RenderGraphExecutor::writeLeafTaps(MixContext& ctx, size_t leafIndex, const float* pre, const float* post) {
    EngineRuntime& eng = ctx.eng;
    LeafWork& w = eng.rs_->leafWork[leafIndex];
    Track& t = *w.t;
    const int32_t preSlot = eng.routeSlotForTrack(t.id());
    if (preSlot >= 0 && !w.drySilent)
        simd_audio::addStereoScaled(
            eng.routeBusPreNext_[static_cast<size_t>(preSlot)].data(), pre, 1.0f, 1.0f, ctx.frames);
    w.vol = t.volume();
    w.pan = std::clamp(t.pan(), -1.0f, 1.0f);
    w.tapsDone = true;
    float* route = w.route;
    if (!route || w.postTapMuted || w.cueOnly || w.postSilent) return;
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
    EngineRuntime& eng = ctx.eng;
    const size_t ntracks = ctx.g->tracks.size();
    const size_t stride = static_cast<size_t>(ctx.frames) * 2;
    const bool staged = maxLevel > 0;

    int m = 0;
    int levelEnd[kMaxKeyLevels + 1];
    for (int L = 0; L <= maxLevel; ++L) {
        for (size_t li = 0; li < ntracks; ++li) {
            const LeafWork& w = eng.rs_->leafWork[li];
            if (w.t && w.path != LeafPath::Skip && w.eligible && w.level == L)
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
            if (static_cast<double>(eng.parWorkNs_) <= roomNs * kRtExitHysteresis &&
                static_cast<double>(eng.leafWorkNs_) <= roomNs * kRtExitHysteresis) {
                eng.rtParallel_ = false;
                eng.leafWorkNs_ = eng.parWorkNs_;
                eng.rtProbeLeftNs_ = kRtProbeSeconds * 1e9;
            } else {
                eng.rtParallelLeftNs_ = eng.rtHoldNs_;
            }
        }
        if (eng.rtParallel_) {
            const double work = static_cast<double>(std::max(eng.leafWorkNs_, eng.sizeWorkNs_));
            const double target = kRtSizeLoad * roomNs;
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

    // Allow nested rack/synth sub-parallelism only for single-leaf waves;
    // multi-track waves already fan out across workers and would contend on the single SubSlot.
    const bool allowSub = (m == 1) && (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0;
    const dsp::RenderExecutor::ScopedHost execScope(&ctx.execHost, allowSub);

    int64_t poolPhaseNs = 0;
    int off = 0;
    auto runTask = [&](int wid, int j) {
        const size_t i = static_cast<size_t>(eng.rs_->parIdx[static_cast<size_t>(off + j)]);
        LeafWork& w = eng.rs_->leafWork[i];
        const auto t0 = !ctx.offline ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point{};
        w.wid = wid;
        float* base = eng.leafBase(i, stride);
        const bool hasFxOrPdc = !w.t->devices.empty() || (!w.frozenActive && w.t->pdc);
        w.copyPost = hasFxOrPdc;
        const bool dryAudio = eng.renderLeafDry(
            *w.t, base, w, ctx.g, ctx.frames, ctx.blockStart, ctx.spb, ctx.playing, ctx.ti, ctx.arrangementActive, i);
        w.drySilent = !dryAudio;
        w.postSilent = !dryAudio && !hasFxOrPdc;
        if (hasFxOrPdc) {
            std::copy_n(base, stride, base + stride);
            RenderGraphExecutor::processDeviceChain(eng, ctx.g, *w.t, base + stride, wid, ctx.frames, ctx.blockStart,
                ctx.spb, ctx.playing, ctx.ti, w.frozenActive);
        }
        if (!ctx.offline)
            w.workNs =
                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - t0).count();
    };

    auto finishLevel = [&](int L) {
        for (size_t li = 0; li < ntracks; ++li) {
            LeafWork& w = eng.rs_->leafWork[li];
            if (!w.t || w.path == LeafPath::Skip || w.level != L) continue;
            float* base = eng.leafBase(li, stride);
            if (!w.eligible && !w.preRendered) {
                const bool hasFxOrPdc = !w.t->devices.empty() || (!w.frozenActive && w.t->pdc);
                w.copyPost = hasFxOrPdc;
                const bool dryAudio = eng.renderLeafDry(*w.t, base, w, ctx.g, ctx.frames, ctx.blockStart, ctx.spb,
                    ctx.playing, ctx.ti, ctx.arrangementActive, li);
                w.drySilent = !dryAudio;
                w.postSilent = !dryAudio && !hasFxOrPdc;
                if (hasFxOrPdc) {
                    std::copy_n(base, stride, base + stride);
                    RenderGraphExecutor::processDeviceChain(eng, ctx.g, *w.t, base + stride, eng.pool_->workerCount(),
                        ctx.frames, ctx.blockStart, ctx.spb, ctx.playing, ctx.ti, w.frozenActive);
                }
            }
            w.preRendered = true;
        }
        for (size_t li = 0; li < ntracks; ++li) {
            LeafWork& w = eng.rs_->leafWork[li];
            if (!w.t || w.path == LeafPath::Skip || w.level != L) continue;
            float* base = eng.leafBase(li, stride);
            writeLeafTaps(ctx, li, base, w.copyPost ? (base + stride) : base);
        }
        for (int s = 0; s < ctx.nRoutes; ++s)
            if (slotLeaf[s] >= 0 && eng.rs_->leafWork[static_cast<size_t>(slotLeaf[s])].level == L)
                eng.routeFresh_[s] = true;
    };

    for (int L = 0; L <= maxLevel; ++L) {
        const int n = levelEnd[L] - off;
        if (n == 1 && m == 1 && !staged) {
            const auto phase0 =
                !ctx.offline ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point{};
            runTask(eng.pool_->workerCount(), 0);
            if (!ctx.offline)
                poolPhaseNs +=
                    std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - phase0)
                        .count();
        } else if (n > 0) {
            const int h = helpers <= 0 ? helpers : std::min(helpers, std::max(1, n - 1));
            const auto phase0 =
                !ctx.offline ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point{};
            eng.pool_->dispatch(n, [&](int wid, int j) { runTask(wid, j); }, h);
            if (!ctx.offline)
                poolPhaseNs +=
                    std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - phase0)
                        .count();
        }
        off = levelEnd[L];
        if (staged) finishLevel(L);
    }

    if (!ctx.offline && m > 0) {
        int64_t work = 0;
        int64_t peak = 0;
        bool alone = true;
        for (int j = 0; j < m; ++j) {
            const LeafWork& w = eng.rs_->leafWork[static_cast<size_t>(eng.rs_->parIdx[static_cast<size_t>(j)])];
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
            if (probeMissed || eng.rtOverBudgetStreak_ >= 2 ||
                (alone && static_cast<double>(eng.leafWorkNs_) > roomNs)) {
                if (probeMissed) eng.rtHoldNs_ = std::min(2.0 * eng.rtHoldNs_, kRtMaxHoldSeconds * 1e9);
                eng.rtParallel_ = true;
                eng.rtParallelLeftNs_ = eng.rtHoldNs_;
                eng.rtProbeLeftNs_ = 0;
                eng.rtOverBudgetStreak_ = 0;
                eng.leafWorkNs_ = std::max(eng.leafWorkNs_, work);
                eng.parWorkNs_ = eng.leafWorkNs_;
                eng.sizeWorkNs_ = eng.leafWorkNs_;
                eng.overrunShare_ = 0.0;
            } else if (eng.rtProbeLeftNs_ > 0 &&
                       (eng.rtProbeLeftNs_ -= ctx.frames / std::max(1.0, ctx.sr) * 1e9) <= 0) {
                eng.rtHoldNs_ = kRtHoldSeconds * 1e9;
            }
        }
    }
}

void RenderGraphExecutor::reduceLeavesAndMeterInputs(MixContext& ctx, const int32_t* slotLeaf) {
    EngineRuntime& eng = ctx.eng;
    Graph* g = ctx.g;
    const size_t ntracks = g->tracks.size();
    const size_t stride = static_cast<size_t>(ctx.frames) * 2;
    const int callerWid = eng.pool_->workerCount();

    for (size_t li = 0; li < ntracks; ++li) {
        LeafWork& w = eng.rs_->leafWork[li];
        if (!w.t || w.path == LeafPath::Skip) continue;
        Track& t = *w.t;
        float* pre = nullptr;
        float* post = nullptr;
        bool parallelDsp = false;
        bool drySilent = false;
        bool postSilent = false;
        const bool hasFxOrPdc = !t.devices.empty() || (!w.frozenActive && t.pdc);
        if ((w.eligible && ctx.parallelOk) || w.preRendered) {
            float* base = eng.leafBase(li, stride);
            pre = base;
            post = w.copyPost ? (base + stride) : base;
            parallelDsp = true;
            drySilent = w.drySilent;
            postSilent = w.postSilent;
        } else {
            const bool allowSub = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0;
            const dsp::RenderExecutor::ScopedHost execScope(&ctx.execHost, allowSub);
            const bool dryAudio = eng.renderLeafDry(t, eng.scratch_.data(), w, g, ctx.frames, ctx.blockStart, ctx.spb,
                ctx.playing, ctx.ti, ctx.arrangementActive, li);
            pre = post = eng.scratch_.data();
            drySilent = !dryAudio;
            postSilent = !dryAudio && !hasFxOrPdc;
        }

        if (!w.tapsDone && !drySilent) {
            const int32_t preSlot = ctx.cachedRouteSlot(li, t.id());
            if (preSlot >= 0)
                simd_audio::addStereoScaled(
                    eng.routeBusPreNext_[static_cast<size_t>(preSlot)].data(), pre, 1.0f, 1.0f, ctx.frames);
        }
        if (!parallelDsp && hasFxOrPdc) {
            const bool allowSub = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0;
            const dsp::RenderExecutor::ScopedHost execScope(&ctx.execHost, allowSub);
            RenderGraphExecutor::processDeviceChain(eng, g, t, eng.scratch_.data(), callerWid, ctx.frames,
                ctx.blockStart, ctx.spb, ctx.playing, ctx.ti, w.frozenActive);
        }
        if (!ctx.offline && w.hybridEligible && !w.hybridServed && !w.hybridDropTick && eng.hybrid_) {
            eng.hybrid_->handoffToWorkerAfterRtBlock(li, t.id(), ctx.blockStart + ctx.frames);
        }

        if (w.path == LeafPath::Tail) {
            int64_t& quiet = t.tailQuiet();
            if (simd_audio::peakAbs(post, ctx.frames * 2) > kTailFloor || trackRequiresActiveProcessing(t)) quiet = 0;
            else
                quiet += ctx.frames;
            if (quiet > 0) {
                const ChainRest rest = chainRest(t);
                if (rest == ChainRest::Settled ||
                    (rest == ChainRest::Opaque && quiet >= static_cast<int64_t>(kTailQuietSeconds * ctx.sr)) ||
                    quiet >= static_cast<int64_t>(kTailMaxQuietSeconds * ctx.sr))
                    quiet = -1;
            }
        } else if (w.path == LeafPath::AudioClips || w.path == LeafPath::SessionAudio || w.path == LeafPath::Monitor) {
            t.tailQuiet() = 0;
        }

        if (w.capturing) {
            int64_t c = eng.freezeCursor_;
            for (int32_t i = 0; i < ctx.frames && c < eng.freezeCapFrames_; ++i, ++c) {
                eng.freezeCaptureBuf_[static_cast<size_t>(c) * 2] = post[i * 2];
                eng.freezeCaptureBuf_[static_cast<size_t>(c) * 2 + 1] = post[i * 2 + 1];
            }
            eng.freezeCursor_ = c;
        }

        if (w.cue && !postSilent) simd_audio::addStereoScaled(eng.cueSeg_, post, 1.0f, 1.0f, ctx.frames);
        if (w.cueOnly || (postSilent && !w.tapRec)) {
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
                                eng.returnBus_[static_cast<size_t>(b)].data(), m, w.sends[b], w.sends[b], ctx.frames);
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
                            eng.returnBus_[static_cast<size_t>(b)][static_cast<size_t>(i * 2)] += l * w.sends[b];
                            eng.returnBus_[static_cast<size_t>(b)][static_cast<size_t>(i * 2 + 1)] += r * w.sends[b];
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
        t.setMeter(acc.peakL, acc.peakR, render_detail::rmsFromSquareSum(acc.sqL, ctx.frames),
            render_detail::rmsFromSquareSum(acc.sqR, ctx.frames));
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
            if (slot >= 0) in = eng.routeBus_[static_cast<size_t>(slot)].data();
            else
                std::fill_n(eng.scratch_.data(), ctx.frames * 2, 0.0f);
        } else {
            eng.renderMonitorInput(g, t, eng.scratch_.data(), ctx.frames);
        }
        const BusAccumResult m = scanPannedMetrics(in, ctx.frames, false, eng.internalQueue_, false);
        t.setMeter(m.peakL, m.peakR, render_detail::rmsFromSquareSum(m.sqL, ctx.frames),
            render_detail::rmsFromSquareSum(m.sqR, ctx.frames));
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
    int32_t slotOwner[kMaxRoutes];
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
    EngineRuntime& eng = ctx.eng;
    if (ctx.numGroups <= 0) return;

    const size_t stride = static_cast<size_t>(ctx.frames) * 2;
    const bool busScratchOk = eng.rs_->groupScratch.size() >= static_cast<size_t>(ctx.numGroups) * stride &&
                              static_cast<uint64_t>(ctx.frames) * 2 <= eng.mixTmp_.size();
    const bool canParallelWave = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0 && busScratchOk;

    std::array<BusWork, kMaxGroups> activeDepth;
    std::array<int32_t, kMaxGroups> dispatchList{};

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
            BusWork bw = meta;
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
                eng.pool_->dispatch(
                    nDispatched,
                    [&](int wid, int j) {
                        const auto& bw = activeDepth[static_cast<size_t>(dispatchList[static_cast<size_t>(j)])];
                        float* buf = eng.rs_->groupScratch.data() + static_cast<size_t>(bw.busIndex) * stride;
                        std::copy_n(eng.groupBus_[static_cast<size_t>(bw.busIndex)].data(), ctx.frames * 2, buf);
                        RenderGraphExecutor::processDeviceChain(eng, ctx.g, *bw.track, buf, wid, ctx.frames,
                            ctx.blockStart, ctx.spb, ctx.playing, ctx.ti, false);
                    },
                    helpers);
            } else {
                for (int32_t j = 0; j < nDispatched; ++j)
                    activeDepth[static_cast<size_t>(dispatchList[static_cast<size_t>(j)])].parallelRan = false;
            }

            const int callerWid = eng.pool_->workerCount();
            for (int32_t idx = 0; idx < nActive; ++idx) {
                const auto& bw = activeDepth[static_cast<size_t>(idx)];
                if (bw.level != L) continue;
                Track& t = *bw.track;
                const int32_t gi = bw.busIndex;
                float* buf = bw.parallelRan ? (eng.rs_->groupScratch.data() + static_cast<size_t>(gi) * stride)
                                            : eng.scratch_.data();

                // Write pre-FX tap from untouched children sum in groupBus_[gi].
                const int32_t preSlot = eng.routeSlotForTrack(t.id());
                if (preSlot >= 0 && !bw.mute)
                    simd_audio::addStereoScaled(eng.routeBusPreNext_[static_cast<size_t>(preSlot)].data(),
                        eng.groupBus_[static_cast<size_t>(gi)].data(), 1.0f, 1.0f, ctx.frames);

                if (!bw.parallelRan) {
                    std::copy_n(eng.groupBus_[static_cast<size_t>(gi)].data(), ctx.frames * 2, buf);
                    RenderGraphExecutor::processDeviceChain(
                        eng, ctx.g, t, buf, callerWid, ctx.frames, ctx.blockStart, ctx.spb, ctx.playing, ctx.ti, false);
                }

                if (!bw.busActive)
                    settleInactiveBus(t, buf, ctx.frames, ctx.sr, kTailFloor, kTailQuietSeconds, kTailMaxQuietSeconds);
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
                const int32_t routeSlot = ctx.plan ? ctx.cachedRouteSlot(static_cast<size_t>(bw.trackIndex), t.id())
                                                   : eng.routeSlotForTrack(t.id());
                float* route =
                    routeSlot >= 0 && !cueFeedOnly ? eng.routeBusNext_[static_cast<size_t>(routeSlot)].data() : nullptr;
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
                    t.setMeter(acc.peakL, acc.peakR, render_detail::rmsFromSquareSum(acc.sqL, ctx.frames),
                        render_detail::rmsFromSquareSum(acc.sqR, ctx.frames));
                ctx.markFresh(t.id());
            }
        }
    }
}

void RenderGraphExecutor::runReturnWaves(MixContext& ctx) {
    EngineRuntime& eng = ctx.eng;
    if (ctx.numReturns <= 0) return;

    const size_t stride = static_cast<size_t>(ctx.frames) * 2;
    const bool busScratchOk = eng.rs_->returnScratch.size() >= static_cast<size_t>(ctx.numReturns) * stride &&
                              static_cast<uint64_t>(ctx.frames) * 2 <= eng.mixTmp_.size();
    const bool canParallelWave = (ctx.offline || eng.rtParallel_) && eng.pool_->workerCount() > 0 && busScratchOk;

    std::array<BusWork, kMaxReturns> activeRets;
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
        BusWork bw{};
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
            eng.pool_->dispatch(
                nDispatched,
                [&](int wid, int j) {
                    const auto& bw = activeRets[static_cast<size_t>(dispatchList[static_cast<size_t>(j)])];
                    float* buf = eng.rs_->returnScratch.data() + static_cast<size_t>(bw.busIndex) * stride;
                    std::copy_n(eng.returnBus_[static_cast<size_t>(bw.busIndex)].data(), ctx.frames * 2, buf);
                    RenderGraphExecutor::processDeviceChain(eng, ctx.g, *bw.track, buf, wid, ctx.frames, ctx.blockStart,
                        ctx.spb, ctx.playing, ctx.ti, false);
                },
                helpers);
        } else {
            for (int32_t j = 0; j < nDispatched; ++j)
                activeRets[static_cast<size_t>(dispatchList[static_cast<size_t>(j)])].parallelRan = false;
        }

        const int callerWid = eng.pool_->workerCount();
        for (int32_t idx = 0; idx < nActive; ++idx) {
            const auto& bw = activeRets[static_cast<size_t>(idx)];
            if (bw.level != L) continue;
            Track& t = *bw.track;
            const int32_t bus = bw.busIndex;
            float* buf = bw.parallelRan ? (eng.rs_->returnScratch.data() + static_cast<size_t>(bus) * stride)
                                        : eng.scratch_.data();

            const int32_t preSlot = eng.routeSlotForTrack(t.id());
            if (preSlot >= 0 && !t.mute())
                simd_audio::addStereoScaled(eng.routeBusPreNext_[static_cast<size_t>(preSlot)].data(),
                    eng.returnBus_[static_cast<size_t>(bus)].data(), 1.0f, 1.0f, ctx.frames);

            if (!bw.parallelRan) {
                std::copy_n(eng.returnBus_[static_cast<size_t>(bus)].data(), ctx.frames * 2, buf);
                RenderGraphExecutor::processDeviceChain(
                    eng, ctx.g, t, buf, callerWid, ctx.frames, ctx.blockStart, ctx.spb, ctx.playing, ctx.ti, false);
            }

            if (!bw.busActive)
                settleInactiveBus(t, buf, ctx.frames, ctx.sr, kTailFloor, kTailQuietSeconds, kTailMaxQuietSeconds);
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
            float* route = routeSlot >= 0 ? eng.routeBusNext_[static_cast<size_t>(routeSlot)].data() : nullptr;
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
            t.setMeter(acc.peakL, acc.peakR, render_detail::rmsFromSquareSum(acc.sqL, ctx.frames),
                render_detail::rmsFromSquareSum(acc.sqR, ctx.frames));
            ctx.markFresh(t.id());
        }
    }
}

void RenderGraphExecutor::finalizeMixBlock(MixContext& ctx) {
    EngineRuntime& eng = ctx.eng;
    if (ctx.recSrc == -1) {
        int64_t recDropped = 0;
        for (int32_t i = 0; i < ctx.frames; ++i)
            if (!eng.internalQueue_.push(InputFrame{ctx.out[i * 2], ctx.out[i * 2 + 1]})) ++recDropped;
        if (recDropped) eng.inputDroppedFrames_.fetch_add(recDropped, std::memory_order_relaxed);
    }

    eng.publishTrackTapRings(ctx.frames);
    for (int s = 0, n = eng.routeSlotCount_.load(std::memory_order_acquire); s < n; ++s)
        if (eng.routeSlotTrackId_[static_cast<size_t>(s)].load(std::memory_order_relaxed) >= 0) {
            eng.routeBus_[static_cast<size_t>(s)].swap(eng.routeBusNext_[static_cast<size_t>(s)]);
            eng.routeBusPre_[static_cast<size_t>(s)].swap(eng.routeBusPreNext_[static_cast<size_t>(s)]);
        }

    if (eng.panLawPos_ < 1.0f)
        eng.panLawPos_ = std::min(1.0f, eng.panLawPos_ + ctx.panLawStep * static_cast<float>(ctx.frames));
}

} // namespace nota::detail
