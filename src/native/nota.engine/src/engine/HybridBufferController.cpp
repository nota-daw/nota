// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "engine/HybridBufferController.h"
#include "engine/Engine.h"
#include "engine/RenderGraphExecutor.h"
#include "devices/Device.h"
#include "devices/Instrument.h"
#include "devices/racks/RackCore.h"

#include <algorithm>
#include <chrono>
#include <cmath>

namespace nota {

namespace {

bool deviceHasDynamicKeyOrNonParallel(const Device& d) {
    if (d.bypassed()) return false;
    if (!d.supportsParallelRender()) return true;
    if (d.sidechainSourceTrackId() >= 0 || d.wantsMidiKey() || d.wantsOwnMidiKey() || d.wantsMidiKeyWhenSilent())
        return true;
    if (const auto* r = d.asRack()) {
        bool blocked = false;
        r->forEachChildDevice([&](const Device& c) {
            if (!c.bypassed() && (!c.supportsParallelRender() || c.sidechainSourceTrackId() >= 0 ||
                                     c.wantsMidiKey() || c.wantsOwnMidiKey() || c.wantsMidiKeyWhenSilent()))
                blocked = true;
        });
        r->forEachChildInstrument([&](const Instrument& i) {
            if (!i.supportsParallelRender() || i.sidechainSourceTrackId() >= 0) blocked = true;
        });
        if (blocked) return true;
    }
    return false;
}

bool trackHasNonMixerAutomation(const Track& t) {
    for (const TrackLane& lane : t.automation) {
        if (lane.points.empty() || lane.suppressRead) continue;
        if (lane.target != AutomationTarget::Volume && lane.target != AutomationTarget::Pan &&
            lane.target != AutomationTarget::Tempo && lane.target != AutomationTarget::Send)
            return true;
    }
    return false;
}

bool trackParticipatesInModulation(const Graph& g, const Track& t) {
    if (!t.modulators.empty() || !t.cvLinks.empty()) return true;
    for (const auto& src : g.tracks) {
        if (!src) continue;
        for (const CvLink& link : src->cvLinks) {
            const int32_t targetTid = link.targetTrack < 0 ? src->id() : link.targetTrack;
            if (targetTid == t.id()) return true;
        }
    }
    return false;
}

} // namespace

HybridBufferController::HybridBufferController(Engine& eng)
    : eng_(eng),
      wMidi_(1024),
      wExpr_(static_cast<size_t>(Engine::kMaxBlockExpr)),
      wArt_(static_cast<size_t>(kMaxBlockArt)),
      wDry_(static_cast<size_t>(kChunkFrames) * 2, 0.0f),
      wPost_(static_cast<size_t>(kChunkFrames) * 2, 0.0f) {
    wPlanar_.ensureFrames(kChunkFrames);
    ensureCapacity(16);
}

HybridBufferController::~HybridBufferController() {
    {
        std::lock_guard<std::mutex> lk(mu_);
        stop_ = true;
        wakeDirty_ = true;
    }
    cv_.notify_all();
    if (worker_.joinable()) worker_.join();
}

void HybridBufferController::ensureWorkerStarted() {
    if (!worker_.joinable() && !stop_) worker_ = std::thread([this] { workerLoop(); });
}

void HybridBufferController::ensureCapacity(size_t trackCount) {
    if (trackCount <= slots_.size()) return;
    std::vector<std::unique_ptr<TrackSlot>> created;
    created.reserve(trackCount - slots_.size());
    for (size_t i = slots_.size(); i < trackCount; ++i) {
        auto sl = std::make_unique<TrackSlot>();
        sl->ringBuf.assign(static_cast<size_t>(kRingFrames) * 2, 0.0f);
        created.push_back(std::move(sl));
    }
    std::lock_guard<std::mutex> lk(mu_);
    for (auto& s : created) {
        if (slots_.size() < trackCount) slots_.push_back(std::move(s));
    }
}

void HybridBufferController::fenceWorkersLocked() {
    epoch_.fetch_add(1, std::memory_order_acq_rel);
    invalidations_.fetch_add(1, std::memory_order_relaxed);
    fenceRequested_.store(true, std::memory_order_seq_cst);
    for (auto& s : slots_) {
        if (!s) continue;
        s->owner.store(static_cast<uint8_t>(Owner::Realtime), std::memory_order_seq_cst);
        while (s->workerBusy.load(std::memory_order_acquire)) std::this_thread::yield();
        s->epoch.store(0, std::memory_order_relaxed);
        s->readStartSample.store(0, std::memory_order_relaxed);
        s->writeEndSample.store(0, std::memory_order_relaxed);
        s->blockServed.store(false, std::memory_order_relaxed);
    }
    fenceRequested_.store(false, std::memory_order_release);
}

void HybridBufferController::setMode(HybridBufferMode m) {
    {
        std::lock_guard<std::mutex> lk(mu_);
        mode_.store(static_cast<int32_t>(m), std::memory_order_release);
        fenceWorkersLocked();
        if (m == HybridBufferMode::Auto) ensureWorkerStarted();
        wakeDirty_ = true;
    }
    cv_.notify_one();
}

void HybridBufferController::onGraphChanged(const std::shared_ptr<Graph>& g) {
    if (mode() == HybridBufferMode::Off && !worker_.joinable()) return;
    if (g) ensureCapacity(g->tracks.size());
    {
        std::lock_guard<std::mutex> lk(mu_);
        fenceWorkersLocked();
        snapshot_ = g;
        if (g) {
            for (size_t i = 0; i < g->tracks.size() && i < slots_.size(); ++i)
                slots_[i]->trackId = g->tracks[i] ? g->tracks[i]->id() : -1;
            for (size_t i = g->tracks.size(); i < slots_.size(); ++i) slots_[i]->trackId = -1;
        }
        wakeDirty_ = true;
    }
    cv_.notify_one();
}

void HybridBufferController::invalidate(bool fenceWorkers) {
    if (fenceWorkers) {
        std::lock_guard<std::mutex> lk(mu_);
        fenceWorkersLocked();
        wakeDirty_ = true;
        cv_.notify_one();
        return;
    }
    epoch_.fetch_add(1, std::memory_order_acq_rel);
    invalidations_.fetch_add(1, std::memory_order_relaxed);
    // Notify only if off-RT callers call invalidate(false) or rely on periodic playing check;
    // wait: message thread callers like seekBeats/setBpm/setLoop pass fenceWorkers=false ONLY or true?
    // Non-RT callers can wake without RT blocking! Wait: only increment atomics cleanly when called on RT.
}

void HybridBufferController::onPlayRequested() {
    {
        std::lock_guard<std::mutex> lk(mu_);
        playGracePolls_ = 40;
        wakeDirty_ = true;
    }
    cv_.notify_one();
}

void HybridBufferController::setStopped() {
    {
        std::lock_guard<std::mutex> lk(mu_);
        playGracePolls_ = 0;
        rtPlaying_.store(false, std::memory_order_release);
        rtBlockActive_.store(false, std::memory_order_release);
        fenceWorkersLocked();
        wakeDirty_ = true;
    }
    cv_.notify_all();
}

HybridBufferStats HybridBufferController::stats() const noexcept {
    HybridBufferStats st;
    st.active = (mode_.load(std::memory_order_acquire) == static_cast<int32_t>(HybridBufferMode::Auto) &&
                    rtBlockActive_.load(std::memory_order_acquire))
                    ? 1
                    : 0;
    st.liveTracks = liveTracks_.load(std::memory_order_relaxed);
    st.anticipativeTracks = anticipativeTracks_.load(std::memory_order_relaxed);
    st.lookaheadFrames = kLookaheadFrames;
    st.chunkFrames = kChunkFrames;
    st.cacheHits = cacheHits_.load(std::memory_order_relaxed);
    st.cacheMisses = cacheMisses_.load(std::memory_order_relaxed);
    st.invalidations = invalidations_.load(std::memory_order_relaxed);
    return st;
}

bool HybridBufferController::isTrackAnticipativeEligible(
    const Graph& g, const Track& t, int32_t capTrackId, int32_t recSrc, uint32_t keyNeeds, bool feedsKey) noexcept {
    if (g.tempoMap) return false; // dynamic tempo maps render exact block boundaries on RT
    if (t.type() != TrackType::Audio && t.type() != TrackType::Instrument) return false;
    if (t.armed() || t.monitor()) return false;
    if (t.id() == capTrackId || t.id() == recSrc) return false;
    if (t.sessionPlayer && t.sessionPlayer->playing.load(std::memory_order_relaxed) >= 0) return false;
    if (t.midiFromTrackId() >= 0) return false;
    if (t.liveHeld && ((t.liveHeld->bits[0] | t.liveHeld->bits[1]) != 0 ||
                          (t.liveHeld->routedBits[0] | t.liveHeld->routedBits[1]) != 0 || t.liveHeld->release))
        return false;
    if (keyNeeds != 0 || feedsKey) return false;
    if (t.pdc && !t.pdc->isIdle()) return false;
    if (trackHasNonMixerAutomation(t) || trackParticipatesInModulation(g, t)) return false;
    if (t.instrument) {
        if (!t.instrument->supportsParallelRender()) return false;
        if (t.instrument->editorOpen()) return false;
        if (t.instrument->sidechainSourceTrackId() >= 0) return false;
        if (const auto* r = t.instrument->asRack()) {
            if (r->chainsHaveOpenEditor(true)) return false;
            bool blocked = false;
            r->forEachChildDevice([&](const Device& c) {
                if (!c.bypassed() &&
                    (!c.supportsParallelRender() || c.sidechainSourceTrackId() >= 0 || c.editorOpen()))
                    blocked = true;
            });
            r->forEachChildInstrument([&](const Instrument& i) {
                if (!i.supportsParallelRender() || i.sidechainSourceTrackId() >= 0 || i.editorOpen()) blocked = true;
            });
            if (blocked) return false;
        }
    }
    for (const auto& d : t.devices)
        if (d && (deviceHasDynamicKeyOrNonParallel(*d) || d->editorOpen() ||
                     (d->asRack() && d->asRack()->chainsHaveOpenEditor(false))))
            return false;
    return true;
}

bool HybridBufferController::beginRtBlock(const Graph* g, double blockStart, int32_t frames, double spb, double sr,
    bool playingActive, bool loopEnabled, double loopStartSamples, double loopEndSamples, bool poolAllowed) noexcept {
    if (mode_.load(std::memory_order_acquire) != static_cast<int32_t>(HybridBufferMode::Auto) || !poolAllowed) {
        if (!rtWasActive_) return false;
        rtWasActive_ = false;
        rtPlaying_.store(false, std::memory_order_release);
        rtBlockActive_.store(false, std::memory_order_release);
        liveTracks_.store(g ? static_cast<int32_t>(g->tracks.size()) : 0, std::memory_order_relaxed);
        anticipativeTracks_.store(0, std::memory_order_relaxed);
        for (size_t i = 0; i < slots_.size(); ++i) {
            if (!slots_[i]) continue;
            slots_[i]->blockServed.store(false, std::memory_order_relaxed);
            if (slots_[i]->owner.load(std::memory_order_relaxed) == static_cast<uint8_t>(Owner::Worker) &&
                !slots_[i]->workerBusy.load(std::memory_order_acquire)) {
                slots_[i]->owner.store(static_cast<uint8_t>(Owner::Realtime), std::memory_order_release);
            }
        }
        return false;
    }

    const bool enabled = (g != nullptr && playingActive && spb > 0.0 && sr > 0.0 && frames > 0 && !g->tempoMap);
    rtSpb_.store(spb, std::memory_order_relaxed);
    rtSr_.store(sr, std::memory_order_relaxed);
    rtLoopEnabled_.store(loopEnabled, std::memory_order_relaxed);
    rtLoopStart_.store(loopStartSamples, std::memory_order_relaxed);
    rtLoopEnd_.store(loopEndSamples, std::memory_order_relaxed);
    rtCursorSample_.store(static_cast<int64_t>(std::llround(blockStart)), std::memory_order_release);
    rtPlaying_.store(enabled, std::memory_order_release);
    rtBlockActive_.store(enabled, std::memory_order_release);

    if (!enabled) {
        if (rtWasActive_) {
            rtWasActive_ = false;
            liveTracks_.store(g ? static_cast<int32_t>(g->tracks.size()) : 0, std::memory_order_relaxed);
            anticipativeTracks_.store(0, std::memory_order_relaxed);
            for (size_t i = 0; i < slots_.size(); ++i) {
                if (!slots_[i]) continue;
                slots_[i]->blockServed.store(false, std::memory_order_relaxed);
                if (slots_[i]->owner.load(std::memory_order_relaxed) == static_cast<uint8_t>(Owner::Worker) &&
                    !slots_[i]->workerBusy.load(std::memory_order_acquire)) {
                    slots_[i]->owner.store(static_cast<uint8_t>(Owner::Realtime), std::memory_order_release);
                }
            }
        }
        return false;
    }
    rtWasActive_ = true;

    const uint64_t curEpoch = epoch_.load(std::memory_order_acquire);
    const int64_t segStart = static_cast<int64_t>(std::llround(blockStart));
    const int64_t segEnd = segStart + frames;
    const size_t nt = std::min(g->tracks.size(), slots_.size());
    for (size_t i = 0; i < nt; ++i) {
        TrackSlot& sl = *slots_[i];
        sl.blockServed.store(false, std::memory_order_relaxed);
        const Track* t = g->tracks[i].get();
        if (!t || sl.trackId != t->id()) continue;
        // Guard against marking live tracks early before classifyLeaves runs.
        if (t->armed() || t->monitor() || t->midiFromTrackId() >= 0 ||
            (t->sessionPlayer && t->sessionPlayer->playing.load(std::memory_order_relaxed) >= 0))
            continue;
        // Mark early if Worker currently owns this track cleanly for `curEpoch` covering [segStart, segEnd)
        // so computeBlockMidi doesn't double-step MIDI FX on it.
        const bool isW = sl.owner.load(std::memory_order_acquire) == static_cast<uint8_t>(Owner::Worker);
        const uint64_t sEpoch = sl.epoch.load(std::memory_order_acquire);
        const int64_t r0 = sl.readStartSample.load(std::memory_order_acquire);
        const int64_t w1 = sl.writeEndSample.load(std::memory_order_acquire);
        if (isW && sEpoch == curEpoch && segStart >= r0 && segEnd <= w1)
            sl.blockServed.store(true, std::memory_order_release);
    }
    return true;
}

bool HybridBufferController::isRtBlockWorkerServed(size_t li, int32_t trackId) const noexcept {
    if (!rtBlockActive_.load(std::memory_order_relaxed) || li >= slots_.size()) return false;
    const TrackSlot& sl = *slots_[li];
    return sl.trackId == trackId && sl.blockServed.load(std::memory_order_acquire);
}

bool HybridBufferController::tryConsumePreRendered(
    size_t li, int32_t trackId, double blockStart, int32_t frames, float* dstPost, bool& outDropTick) noexcept {
    outDropTick = false;
    if (!rtBlockActive_.load(std::memory_order_acquire) || li >= slots_.size() || !dstPost || frames <= 0)
        return false;
    TrackSlot& sl = *slots_[li];
    if (sl.trackId != trackId) return false;
    const uint64_t curEpoch = epoch_.load(std::memory_order_acquire);
    if (sl.owner.load(std::memory_order_acquire) != static_cast<uint8_t>(Owner::Worker)) {
        return false;
    }

    const int64_t segStart = static_cast<int64_t>(std::llround(blockStart));
    const int64_t segEnd = segStart + frames;
    const uint64_t s1 = sl.seq.load(std::memory_order_acquire);
    if ((s1 & 1u) != 0) {
        cacheMisses_.fetch_add(1, std::memory_order_relaxed);
        sl.cooldownBlocks = 8;
        outDropTick = !reclaimOwnershipRt(li, trackId);
        return false;
    }
    const uint64_t slEpoch = sl.epoch.load(std::memory_order_acquire);
    const int64_t r0 = sl.readStartSample.load(std::memory_order_acquire);
    const int64_t w1 = sl.writeEndSample.load(std::memory_order_acquire);
    if (slEpoch != curEpoch || segStart < r0 || segEnd > w1) {
        cacheMisses_.fetch_add(1, std::memory_order_relaxed);
        sl.cooldownBlocks = 8;
        outDropTick = !reclaimOwnershipRt(li, trackId);
        return false;
    }

    // Lock-free circular copy out of kRingFrames power-of-two ring.
    constexpr int64_t kMask = kRingFrames - 1;
    for (int32_t i = 0; i < frames; ++i) {
        const size_t idx = static_cast<size_t>((segStart + i) & kMask) * 2;
        dstPost[static_cast<size_t>(i) * 2] = sl.ringBuf[idx];
        dstPost[static_cast<size_t>(i) * 2 + 1] = sl.ringBuf[idx + 1];
    }
    std::atomic_thread_fence(std::memory_order_acquire);
    if (sl.seq.load(std::memory_order_acquire) != s1 || sl.epoch.load(std::memory_order_acquire) != curEpoch ||
        sl.readStartSample.load(std::memory_order_acquire) > segStart) {
        cacheMisses_.fetch_add(1, std::memory_order_relaxed);
        sl.cooldownBlocks = 8;
        outDropTick = !reclaimOwnershipRt(li, trackId);
        return false;
    }
    cacheHits_.fetch_add(1, std::memory_order_relaxed);
    return true;
}

bool HybridBufferController::reclaimOwnershipRt(size_t li, int32_t trackId) noexcept {
    if (li >= slots_.size()) return true;
    TrackSlot& sl = *slots_[li];
    if (sl.trackId != trackId) return true;
    sl.owner.store(static_cast<uint8_t>(Owner::Realtime), std::memory_order_seq_cst);
    return !sl.workerBusy.load(std::memory_order_acquire);
}

void HybridBufferController::handoffToWorkerAfterRtBlock(size_t li, int32_t trackId, double nextSample) noexcept {
    if (!rtBlockActive_.load(std::memory_order_acquire) || li >= slots_.size()) return;
    TrackSlot& sl = *slots_[li];
    if (sl.trackId != trackId) return;
    if (sl.cooldownBlocks > 0) {
        --sl.cooldownBlocks;
        return;
    }
    if (sl.workerBusy.load(std::memory_order_acquire)) return;
    const uint64_t curEpoch = epoch_.load(std::memory_order_acquire);
    const int64_t nextF = static_cast<int64_t>(std::llround(nextSample));
    sl.epoch.store(curEpoch, std::memory_order_relaxed);
    sl.nextWorkerSample.store(nextF, std::memory_order_relaxed);
    sl.readStartSample.store(nextF, std::memory_order_relaxed);
    sl.writeEndSample.store(nextF, std::memory_order_release);
    sl.owner.store(static_cast<uint8_t>(Owner::Worker), std::memory_order_release);
}

bool HybridBufferController::writeRingChunk(
    TrackSlot& sl, uint64_t epoch, int64_t chunkStart, int32_t frames, const float* src) noexcept {
    if (frames <= 0 || frames > kChunkFrames) return false;
    constexpr int64_t kMask = kRingFrames - 1;
    const uint64_t s = sl.seq.load(std::memory_order_relaxed);
    sl.seq.store(s + 1, std::memory_order_release);
    std::atomic_thread_fence(std::memory_order_release);

    const int64_t newEnd = chunkStart + frames;
    const int64_t minValid = newEnd - kRingFrames;
    if (sl.readStartSample.load(std::memory_order_relaxed) < minValid)
        sl.readStartSample.store(minValid, std::memory_order_release);

    for (int32_t i = 0; i < frames; ++i) {
        const size_t idx = static_cast<size_t>((chunkStart + i) & kMask) * 2;
        sl.ringBuf[idx] = src[static_cast<size_t>(i) * 2];
        sl.ringBuf[idx + 1] = src[static_cast<size_t>(i) * 2 + 1];
    }
    sl.epoch.store(epoch, std::memory_order_relaxed);
    sl.writeEndSample.store(newEnd, std::memory_order_release);
    sl.nextWorkerSample.store(newEnd, std::memory_order_release);
    sl.seq.store(s + 2, std::memory_order_release);
    return true;
}

void HybridBufferController::preRenderSlot(const std::shared_ptr<Graph>& g, size_t slotIdx, uint64_t epoch,
    int64_t chunkStart, int32_t chunkFrames, double spb, double sr) {
    if (!g || slotIdx >= g->tracks.size() || slotIdx >= slots_.size()) return;
    Track& t = *g->tracks[slotIdx];
    TrackSlot& sl = *slots_[slotIdx];
    const double blockStart = static_cast<double>(chunkStart);

    const bool frozenActive = t.frozen() && t.frozenBuf;
    if (frozenActive) {
        eng_.fillFrozen(t, wDry_.data(), chunkFrames, blockStart, spb);
    } else if (t.type() == TrackType::Audio) {
        eng_.renderTrackAudioClipsUnmapped(t, wDry_.data(), chunkFrames, blockStart, spb, sr);
    } else if (t.type() == TrackType::Instrument) {
        eng_.renderAnticipativeInstrumentBlock(g.get(), t, slotIdx, wDry_.data(), chunkFrames, blockStart, spb, sr,
            wMidi_.data(), wExpr_.data(), wArt_.data());
    } else {
        std::fill_n(wDry_.data(), static_cast<size_t>(chunkFrames) * 2, 0.0f);
    }

    std::copy_n(wDry_.data(), static_cast<size_t>(chunkFrames) * 2, wPost_.data());
    TransportInfo ti = eng_.transport_.transportInfo(blockStart);
    RenderGraphExecutor::processDeviceChain(eng_, g.get(), t, wPost_.data(), -1, chunkFrames, blockStart, spb, true, ti,
        frozenActive, &wPlanar_);

    if (sl.owner.load(std::memory_order_acquire) == static_cast<uint8_t>(Owner::Worker) &&
        epoch_.load(std::memory_order_acquire) == epoch && !fenceRequested_.load(std::memory_order_acquire)) {
        writeRingChunk(sl, epoch, chunkStart, chunkFrames, wPost_.data());
    }
}

void HybridBufferController::workerLoop() {
    for (;;) {
        std::shared_ptr<Graph> g;
        bool playing = false;
        HybridBufferMode m = HybridBufferMode::Off;
        {
            std::unique_lock<std::mutex> lk(mu_);
            wakeDirty_ = false;
            m = static_cast<HybridBufferMode>(mode_.load(std::memory_order_acquire));
            playing = rtPlaying_.load(std::memory_order_acquire);
            if (!stop_ && (m != HybridBufferMode::Auto || !playing || !snapshot_ || fenceRequested_.load())) {
                if (m == HybridBufferMode::Auto && playGracePolls_ > 0 && snapshot_ && !fenceRequested_.load()) {
                    --playGracePolls_;
                    cv_.wait_for(lk, std::chrono::milliseconds(1), [this] {
                        return stop_ || wakeDirty_ ||
                               (rtPlaying_.load(std::memory_order_acquire) &&
                                   !fenceRequested_.load(std::memory_order_acquire));
                    });
                } else {
                    cv_.wait(lk, [this] {
                        if (stop_ || wakeDirty_) return true;
                        return static_cast<HybridBufferMode>(mode_.load(std::memory_order_acquire)) ==
                                   HybridBufferMode::Auto &&
                               (rtPlaying_.load(std::memory_order_acquire) || playGracePolls_ > 0) &&
                               snapshot_ != nullptr && !fenceRequested_.load(std::memory_order_acquire);
                    });
                }
            }
            if (stop_) break;
            g = snapshot_;
            m = static_cast<HybridBufferMode>(mode_.load(std::memory_order_acquire));
            playing = rtPlaying_.load(std::memory_order_acquire);
            if (playing) playGracePolls_ = 0;
        }

        if (m != HybridBufferMode::Auto || !playing || !g || fenceRequested_.load(std::memory_order_acquire))
            continue;

        const uint64_t curEpoch = epoch_.load(std::memory_order_acquire);
        const int64_t cursor = rtCursorSample_.load(std::memory_order_acquire);
        const double spb = rtSpb_.load(std::memory_order_relaxed);
        const double sr = rtSr_.load(std::memory_order_relaxed);
        const bool loopOn = rtLoopEnabled_.load(std::memory_order_relaxed);
        const double loopEnd = rtLoopEnd_.load(std::memory_order_relaxed);
        if (spb <= 0.0 || sr <= 0.0) continue;

        bool didWork = false;
        const size_t nt = std::min(g->tracks.size(), slots_.size());
        for (size_t i = 0; i < nt; ++i) {
            if (stop_ || !rtPlaying_.load(std::memory_order_acquire) ||
                epoch_.load(std::memory_order_acquire) != curEpoch || fenceRequested_.load(std::memory_order_acquire))
                break;

            TrackSlot& sl = *slots_[i];
            if (!g->tracks[i] || sl.trackId != g->tracks[i]->id()) continue;
            if (sl.owner.load(std::memory_order_acquire) != static_cast<uint8_t>(Owner::Worker)) continue;
            if (sl.epoch.load(std::memory_order_acquire) != curEpoch) continue;

            const int64_t nextSample = sl.nextWorkerSample.load(std::memory_order_acquire);
            if (nextSample - cursor >= kLookaheadFrames) continue;

            int32_t n = kChunkFrames;
            if (loopOn && static_cast<double>(nextSample) < loopEnd) {
                const double toEnd = loopEnd - static_cast<double>(nextSample);
                if (toEnd < n) n = std::max<int32_t>(1, static_cast<int32_t>(std::llround(toEnd)));
            } else if (loopOn && static_cast<double>(nextSample) >= loopEnd - 0.5) {
                continue; // stop exactly at loop end until audio thread wraps and re-primes
            }

            // Dekker/Peterson ownership claim: publish workerBusy=true then re-check Owner::Worker + epoch.
            sl.workerBusy.store(true, std::memory_order_seq_cst);
            if (sl.owner.load(std::memory_order_seq_cst) == static_cast<uint8_t>(Owner::Worker) &&
                epoch_.load(std::memory_order_acquire) == curEpoch && !fenceRequested_.load(std::memory_order_acquire) &&
                g->tracks[i] != nullptr) {
                preRenderSlot(g, i, curEpoch, nextSample, n, spb, sr);
                didWork = true;
            }
            sl.workerBusy.store(false, std::memory_order_release);
        }

        if (!didWork && rtPlaying_.load(std::memory_order_acquire)) {
            std::unique_lock<std::mutex> lk(mu_);
            if (!stop_ && !wakeDirty_ && rtPlaying_.load(std::memory_order_acquire))
                cv_.wait_for(lk, std::chrono::milliseconds(3));
        }
    }
}

} // namespace nota
