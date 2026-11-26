// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// StreamingAudioSource: disk-backed audio source using a fixed-capacity lock-free
// seqlock page ring fed asynchronously by a dedicated sleep-on-idle I/O scheduler.
// Strictly AR-6 compliant on real-time threads (zero heap allocations, locks, or I/O).

#pragma once

#include "AudioSource.h"

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace nota {

struct SampleBuffer;
class StreamingIoScheduler;

class StreamingAudioSource final : public AudioSource, public std::enable_shared_from_this<StreamingAudioSource> {
  public:
    static constexpr int64_t kPageFrames = 8192;
    static constexpr int32_t kNumSets = 8;
    static constexpr int32_t kWays = 4;
    static constexpr int32_t kRingPages = kNumSets * kWays; // 32 slots (~2.0 MiB stereo)
    static constexpr int32_t kHeadPages = 2;              // 16384 pinned start frames
    static constexpr int32_t kLookaheadPages = 16;        // ~3.0 s @ 44.1k
    static constexpr int32_t kTrailingPages = 2;

    ~StreamingAudioSource() override;

    // Opens `path` and probes channels/frames/rate. When `outPeakTable` / `outRmsTable` are
    // supplied (512-frame blocks), computes full waveform overviews with bounded 16k-frame
    // scratch while filling pinned head pages. Returns nullptr if unsupported or unseekable.
    static std::shared_ptr<StreamingAudioSource> open(const std::string& path,
        std::vector<float>* outPeakTable = nullptr, std::vector<float>* outRmsTable = nullptr,
        const float* seededPeaks = nullptr, int64_t seededPeakCount = 0);

    // Lightweight metadata probe without retaining file open or building full overview.
    static bool probeHeader(
        const std::string& path, int32_t& outChannels, int64_t& outFrames, double& outSampleRate);

    int32_t channels() const noexcept override { return channels_; }
    int64_t frames() const noexcept override { return frames_; }
    double sampleRate() const noexcept override { return sampleRate_; }
    bool isStreaming() const noexcept override { return true; }
    int64_t memoryBytes() const noexcept override;
    const std::string& path() const noexcept override { return path_; }

    float at(int64_t frame, int32_t ch) const noexcept override;
    void readStereo(int64_t frame, float& l, float& r) const noexcept override;
    void readStereoHinted(
        int64_t frame, int32_t& slotHint, bool allowSyncFill, float& l, float& r) const noexcept override;

    void preparePlaybackWindow(
        int64_t minFrame, int64_t maxFrame, bool reverse, bool allowSyncFill) const noexcept override;
    void primeAround(int64_t frame, bool reverse = false) const override;
    int64_t readAllInterleaved(float* out, int64_t capFloats) const override;

    StreamRuntimeStats runtimeStats() const noexcept override;
    void setPlaybackActive(bool active) const noexcept override;

    // Called exclusively by StreamingIoScheduler background worker.
    bool servicePrefetchStep();
    bool hasPendingPrefetch() const noexcept;
    bool isPlaybackActive() const noexcept { return playbackActive_.load(std::memory_order_acquire); }

  private:
    struct alignas(64) PageSlot {
        std::atomic<uint64_t> seq{0};    // even = readable, odd = writer mutating
        std::atomic<int64_t> pageIdx{-1}; // logical page = frame / kPageFrames
        std::atomic<uint64_t> stamp{0};
        std::vector<float> data;
    };

    struct DecoderHandle;

    StreamingAudioSource() = default;

    bool initDecoder(const std::string& path);
    bool findSlotLockFree(int64_t page, int32_t& slotHint, int64_t inPage, float& l, float& r) const noexcept;
    bool findSlotExactCh(int64_t page, int64_t inPage, int32_t ch, float& outVal) const noexcept;
    bool fillPageLocked(DecoderHandle& dec, int64_t page, bool isAsync);
    bool fillPageSync(int64_t page, int32_t* outSlot = nullptr) const;
    int32_t selectVictimSlot(int64_t page, int64_t focusPage) const noexcept;
    bool isPageResident(int64_t page) const noexcept;

    std::string path_;
    int32_t channels_ = 0;
    int64_t frames_ = 0;
    double sampleRate_ = 0.0;
    int64_t headFrames_ = 0;
    std::vector<float> headSamples_;

    mutable std::vector<PageSlot> slots_;
    mutable std::atomic<int64_t> cursorPage_{0};
    mutable std::atomic<int64_t> targetEndPage_{0};
    mutable std::atomic<bool> cursorReverse_{false};
    mutable std::atomic<bool> playbackActive_{false};
    mutable std::atomic<bool> needsPrefetch_{false};
    mutable std::atomic<uint64_t> accessStamp_{1};

    mutable std::atomic<int64_t> pagesFaulted_{0};
    mutable std::atomic<int64_t> pagesServedAsync_{0};
    mutable std::atomic<int64_t> pagesServedSync_{0};
    mutable std::atomic<int64_t> rtUnderruns_{0};

    std::shared_ptr<StreamingIoScheduler> scheduler_;
    std::unique_ptr<DecoderHandle> asyncDec_;
    mutable std::unique_ptr<DecoderHandle> syncDec_;
    mutable std::vector<float> asyncScratch_;
    mutable std::vector<float> syncScratch_;
    mutable std::mutex ioMu_;
};

class StreamingIoScheduler {
  public:
    static std::shared_ptr<StreamingIoScheduler> shared();
    StreamingIoScheduler();
    ~StreamingIoScheduler();

    StreamingIoScheduler(const StreamingIoScheduler&) = delete;
    StreamingIoScheduler& operator=(const StreamingIoScheduler&) = delete;

    void registerSource(const std::shared_ptr<StreamingAudioSource>& src);
    void unregisterSource(const StreamingAudioSource* src);
    void wake() noexcept;

  private:
    struct Entry {
        std::weak_ptr<StreamingAudioSource> weak;
        const StreamingAudioSource* key = nullptr;
    };

    struct State {
        std::mutex mu;
        std::condition_variable cv;
        std::vector<Entry> sources;
        bool stop = false;
        bool dirty = false;
    };

    static void runLoop(std::shared_ptr<State> st);

    std::shared_ptr<State> state_;
    std::thread worker_;
};

} // namespace nota
