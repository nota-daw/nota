// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SilenceGate — lock-free, epoch-guarded auto-sleep gate with single-process counters.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota::dsp {

enum class PluginSleepMode : int32_t {
    Auto = 0,
    Never = 1,
};

NOTA_DSP_API bool anyOpenPluginEditor() noexcept;
NOTA_DSP_API bool anyNeverSleepDevice() noexcept;
NOTA_DSP_API void adjustOpenPluginEditors(int32_t delta) noexcept;
NOTA_DSP_API void adjustNeverSleepDevices(int32_t delta) noexcept;

class NOTA_DSP_API SilenceGate {
  public:
    static constexpr float kDefaultReleaseThreshold = 1.0e-5f;
    static constexpr double kDefaultHangoverSeconds = 0.25;
    static constexpr double kMaxAutoHangoverSeconds = 10.0;

    explicit SilenceGate(uint64_t initialHangoverSamples = 11025) noexcept : hangoverSamples_(initialHangoverSamples) {}
    ~SilenceGate();

    SilenceGate(const SilenceGate&) = delete;
    SilenceGate& operator=(const SilenceGate&) = delete;

    void prepare(double sampleRate, double hangoverSeconds = kDefaultHangoverSeconds,
        float releaseThreshold = kDefaultReleaseThreshold) noexcept;
    void setHangoverSeconds(double seconds) noexcept;
    void setHangoverSamples(uint64_t samples) noexcept;
    void setPluginTailSeconds(double tailSeconds) noexcept;
    void setNeverSleep(bool never) noexcept;
    bool neverSleep() const noexcept { return (flags_.load(std::memory_order_relaxed) & kNeverSleep) != 0u; }
    void setSleepMode(PluginSleepMode mode) noexcept { setNeverSleep(mode == PluginSleepMode::Never); }

    void setHoldAwake(bool hold) noexcept;
    bool holdAwake() const noexcept { return (flags_.load(std::memory_order_acquire) & kHoldAwake) != 0u; }
    PluginSleepMode sleepMode() const noexcept { return neverSleep() ? PluginSleepMode::Never : PluginSleepMode::Auto; }

    void wake() noexcept {
        wakeEpoch_.fetch_add(1u, std::memory_order_seq_cst);
        sleeping_.store(false, std::memory_order_seq_cst);
    }

    bool isSleeping() const noexcept {
        if (!sleeping_.load(std::memory_order_acquire)) return false;
        if ((flags_.load(std::memory_order_acquire) & kKeepAwakeMask) != 0u) return false;
        return wakeEpoch_.load(std::memory_order_acquire) == seenEpoch_.load(std::memory_order_acquire);
    }

    uint32_t beginBlock(bool* outSkip = nullptr) noexcept {
        if (!sleeping_.load(std::memory_order_relaxed)) [[likely]] {
            if (outSkip) *outSkip = false;
            return wakeEpoch_.load(std::memory_order_relaxed);
        }
        return beginBlockSlow(outSkip);
    }

    bool shouldSkip(uint32_t* outBlockEpoch = nullptr) noexcept {
        bool skip = false;
        const uint32_t epoch = beginBlock(&skip);
        if (outBlockEpoch) *outBlockEpoch = epoch;
        return skip;
    }

    void observe(bool stillActive, int32_t frames, uint32_t blockEpoch) noexcept {
        if (stillActive) [[likely]] {
            quietSamples_ = 0;
            return;
        }
        observeQuietSlow(frames, blockEpoch);
    }

    void observe(bool stillActive, int32_t frames) noexcept {
        observe(stillActive, frames, seenEpoch_.load(std::memory_order_relaxed));
    }

    bool isBufferSilent(const float* samples, int32_t sampleCount) const noexcept {
        return isBufferSilent(samples, sampleCount, releaseThreshold_);
    }

    static bool isBufferSilent(const float* samples, int32_t sampleCount, float threshold) noexcept {
        if (!samples || sampleCount <= 0) return true;
        for (int32_t i = 0; i < sampleCount; ++i) {
            if (!(std::fabs(samples[i]) <= threshold)) return false;
        }
        return true;
    }

    void reset() noexcept {
        quietSamples_ = 0;
        wake();
    }

  private:
    static constexpr uint32_t kNeverSleep = 1u << 0;
    static constexpr uint32_t kInfiniteTail = 1u << 1;
    static constexpr uint32_t kHoldAwake = 1u << 2;
    static constexpr uint32_t kKeepAwakeMask = kNeverSleep | kInfiniteTail | kHoldAwake;

    NOTA_NOINLINE uint32_t beginBlockSlow(bool* outSkip) noexcept;
    NOTA_NOINLINE void observeQuietSlow(int32_t frames, uint32_t blockEpoch) noexcept;

    double sampleRate_ = 44100.0;
    uint64_t hangoverSamples_ = 11025;
    float releaseThreshold_ = kDefaultReleaseThreshold;
    uint64_t quietSamples_ = 0;

    std::atomic<uint32_t> wakeEpoch_{1};
    std::atomic<uint32_t> seenEpoch_{0};
    std::atomic<uint32_t> flags_{0};
    std::atomic<bool> sleeping_{false};
};

} // namespace nota::dsp
