// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/AudioSource.h"
#include "nota/media/Export.h"

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace nota::media {

class IAudioDecoder;
class StreamingAudioSource;

class NOTA_MEDIA_API StreamingIoScheduler {
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

class NOTA_MEDIA_API StreamingAudioSource final : public AudioSource,
                                                  public std::enable_shared_from_this<StreamingAudioSource> {
  private:
    struct PrivateTag {
        explicit PrivateTag() = default;
    };

  public:
    explicit StreamingAudioSource(PrivateTag);
    static constexpr int64_t kPageFrames = 8192;
    static constexpr int32_t kNumSets = 8;
    static constexpr int32_t kWays = 4;
    static constexpr int32_t kRingPages = kNumSets * kWays; // 32 slots
    static constexpr int32_t kHeadPages = 2;
    static constexpr int32_t kLookaheadPages = 16;
    static constexpr int32_t kTrailingPages = 2;

    ~StreamingAudioSource() override;

    [[nodiscard]] static std::shared_ptr<StreamingAudioSource> open(const std::string& path,
        std::vector<float>* outPeakTable = nullptr, std::vector<float>* outRmsTable = nullptr,
        const float* seededPeaks = nullptr, int64_t seededPeakCount = 0);

    [[nodiscard]] static bool probeHeader(
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

    bool servicePrefetchStep();
    bool hasPendingPrefetch() const noexcept;
    bool isPlaybackActive() const noexcept { return playbackActive_.load(std::memory_order_acquire); }

  private:
    struct alignas(64) PageSlot {
        std::atomic<uint64_t> seq{0};
        std::atomic<int64_t> pageIdx{-1};
        std::atomic<uint64_t> stamp{0};
        std::vector<float> data;
    };

    bool findSlotLockFree(int64_t page, int32_t& slotHint, int64_t inPage, float& l, float& r) const noexcept;
    bool findSlotExactCh(int64_t page, int64_t inPage, int32_t ch, float& outVal) const noexcept;
    bool fillPageLocked(IAudioDecoder& dec, int64_t page, bool isAsync);
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
    std::unique_ptr<IAudioDecoder> asyncDec_;
    mutable std::unique_ptr<IAudioDecoder> syncDec_;
    mutable std::vector<float> asyncScratch_;
    mutable std::vector<float> syncScratch_;
    mutable std::mutex ioMu_;
};

} // namespace nota::media
