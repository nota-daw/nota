// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// HybridBufferController: separates low-latency live tracks (record-armed,
// monitored, live/routed MIDI, live Session slots, sidechain-coupled, dynamic
// CV-modulated, or non-parallel devices) from non-live timeline tracks,
// pre-rendering eligible tracks in larger background chunks (kChunkFrames = 1024)
// while keeping mixer controls (volume/pan/mute/solo/sends) at hardware latency.

#pragma once

#include "core/EnginePrelude.h"

#include "model/ArticulationSchedule.h"
#include "model/EngineTypes.h"
#include "nota/dsp/rt/PlanarStereoBuffer.h"

#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

namespace nota::detail {

class EngineRuntime;
struct Graph;
class Track;

enum class HybridBufferMode : int32_t { Off = 0, Auto = 1 };

struct HybridBufferStats {
    int32_t active = 0;
    int32_t liveTracks = 0;
    int32_t anticipativeTracks = 0;
    int32_t lookaheadFrames = 0;
    int32_t chunkFrames = 0;
    int64_t cacheHits = 0;
    int64_t cacheMisses = 0;
    int64_t invalidations = 0;
};

class HybridBufferController {
  public:
    static constexpr int32_t kChunkFrames = 1024;
    static constexpr int32_t kLookaheadFrames = 4096;
    static constexpr int32_t kRingFrames = 8192; // power of two >= kLookaheadFrames + kChunkFrames

    explicit HybridBufferController(EngineRuntime& eng);
    ~HybridBufferController();

    HybridBufferController(const HybridBufferController&) = delete;
    HybridBufferController& operator=(const HybridBufferController&) = delete;

    // --- Configuration & stats (any thread) ---------------------------------
    void setMode(HybridBufferMode m);
    HybridBufferMode mode() const noexcept {
        return static_cast<HybridBufferMode>(mode_.load(std::memory_order_acquire));
    }
    HybridBufferStats stats() const noexcept;

    // --- Control / message thread -------------------------------------------
    void ensureCapacity(size_t trackCount);
    void onGraphChanged(const std::shared_ptr<Graph>& g);
    void invalidate(bool fenceWorkers = false);
    void onPlayRequested();
    void setStopped();

    // --- Audio thread (strictly AR-6 lock-free / alloc-free) ----------------
    // Returns true if anticipative buffering is currently active for this block.
    bool beginRtBlock(const Graph* g, double blockStart, int32_t frames, double spb, double sr, bool playingActive,
        bool loopEnabled, double loopStartSamples, double loopEndSamples, bool poolAllowed) noexcept;
    bool beginRtActive() const noexcept { return rtBlockActive_.load(std::memory_order_acquire); }

    bool isTrackAnticipativeEligible(
        const Graph& g, const Track& t, int32_t capTrackId, int32_t recSrc, uint32_t keyNeeds, bool feedsKey) noexcept;

    // Fast lookup used early in mixGraph (computeBlockMidi / applyAutomation) before leaf render.
    bool isRtBlockWorkerServed(size_t li, int32_t trackId) const noexcept;

    // Attempts to copy pre-rendered post-device stereo into `dstPost` (`frames * 2`).
    // Returns true on cache hit. Sets `*outDropTick = true` cleanly when handing back from busy worker.
    bool tryConsumePreRendered(
        size_t li, int32_t trackId, double blockStart, int32_t frames, float* dstPost, bool& outDropTick) noexcept;

    // Hands track sequential ownership cleanly to worker starting at `nextSample` after RT rendered a block.
    void handoffToWorkerAfterRtBlock(size_t li, int32_t trackId, double nextSample) noexcept;

    // Reclaims ownership on audio thread if worker wasn't in flight (after an invalidation/miss).
    bool reclaimOwnershipRt(size_t li, int32_t trackId) noexcept;

    void setTrackCountsRt(int32_t live, int32_t anticipative) noexcept {
        liveTracks_.store(live, std::memory_order_relaxed);
        anticipativeTracks_.store(anticipative, std::memory_order_relaxed);
    }

  private:
    enum class Owner : uint8_t { Realtime = 0, Worker = 1 };

    struct alignas(64) TrackSlot {
        int32_t trackId = -1;
        bool eligible = false;
        std::atomic<uint8_t> owner{static_cast<uint8_t>(Owner::Realtime)};
        std::atomic<bool> workerBusy{false};
        std::atomic<uint64_t> seq{0};
        std::atomic<uint64_t> epoch{0};
        std::atomic<int64_t> nextWorkerSample{0};
        std::atomic<int64_t> readStartSample{0};
        std::atomic<int64_t> writeEndSample{0};
        std::atomic<bool> blockServed{false};
        int32_t cooldownBlocks = 0; // audio thread only: backs off re-handoff after a miss
        std::vector<float> ringBuf; // kRingFrames * 2 stereo interleaved
    };

    void workerLoop();
    void ensureWorkerStarted();
    void preRenderSlot(const std::shared_ptr<Graph>& g, size_t slotIdx, uint64_t epoch, int64_t chunkStart,
        int32_t chunkFrames, double spb, double sr);
    bool writeRingChunk(TrackSlot& sl, uint64_t epoch, int64_t chunkStart, int32_t frames, const float* src) noexcept;
    void fenceWorkersLocked();

    EngineRuntime& eng_;
    std::atomic<int32_t> mode_{static_cast<int32_t>(HybridBufferMode::Off)};
    std::atomic<uint64_t> epoch_{1};

    // Snapshot published on control/message thread (protected by mu_ for writes; atomic via atomic<shared_ptr> or mu_).
    std::mutex mu_;
    std::condition_variable cv_;
    std::shared_ptr<Graph> snapshot_;
    bool stop_ = false;
    bool wakeDirty_ = false;
    bool rtWasActive_ = false;
    int32_t playGracePolls_ = 0;
    std::atomic<bool> fenceRequested_{false};

    // Pre-allocated slot pool (resized only on control/message thread via ensureCapacity).
    std::vector<std::unique_ptr<TrackSlot>> slots_;

    // Latest RT timeline context (written on RT thread via relaxed/release atomics, read by worker).
    std::atomic<bool> rtBlockActive_{false};
    std::atomic<bool> rtPlaying_{false};
    std::atomic<int64_t> rtCursorSample_{0};
    std::atomic<double> rtSpb_{0.0};
    std::atomic<double> rtSr_{44100.0};
    std::atomic<bool> rtLoopEnabled_{false};
    std::atomic<double> rtLoopStart_{0.0};
    std::atomic<double> rtLoopEnd_{0.0};

    // Cached summary stats updated on block / worker.
    std::atomic<int32_t> liveTracks_{0};
    std::atomic<int32_t> anticipativeTracks_{0};
    std::atomic<int64_t> cacheHits_{0};
    std::atomic<int64_t> cacheMisses_{0};
    std::atomic<int64_t> invalidations_{0};

    // Dedicated background scratch buffers (never touched by audio callback thread).
    std::vector<MidiEv> wMidi_;
    std::vector<ExprEv> wExpr_;
    std::vector<ArtEv> wArt_;
    std::vector<float> wDry_;
    std::vector<float> wPost_;
    dsp::PlanarStereoBuffer wPlanar_;

    std::thread worker_;
};

} // namespace nota::detail
