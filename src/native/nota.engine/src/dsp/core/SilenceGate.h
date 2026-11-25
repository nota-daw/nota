// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SilenceGate — lock-free, epoch-guarded auto-sleep gate for instruments and effects.
// Any thread may call wake() or setNeverSleep(); only the audio thread advances quiet
// sample counts and enters sleep so concurrent UI parameter edits cannot be clobbered.

#pragma once

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota::dsp {

enum class PluginSleepMode : int32_t {
    Auto = 0,
    Never = 1,
};

class SilenceGate {
  public:
    // Threshold aligned with SilenceDetector release (-100 dBFS).
    static constexpr float kDefaultReleaseThreshold = 1.0e-5f;
    static constexpr double kDefaultHangoverSeconds = 0.25;
    static constexpr double kMaxAutoHangoverSeconds = 10.0;

    explicit SilenceGate(uint64_t initialHangoverSamples = 11025) noexcept
        : hangoverSamples_(initialHangoverSamples) {}

    void prepare(double sampleRate, double hangoverSeconds = kDefaultHangoverSeconds,
                 float releaseThreshold = kDefaultReleaseThreshold) noexcept {
        sampleRate_ = (std::isfinite(sampleRate) && sampleRate > 0.0) ? sampleRate : 44100.0;
        releaseThreshold_ = releaseThreshold > 0.0f ? releaseThreshold : kDefaultReleaseThreshold;
        setHangoverSeconds(hangoverSeconds);
        wake();
    }

    void setHangoverSeconds(double seconds) noexcept {
        if (!std::isfinite(seconds) || seconds >= 1.0e6) {
            infiniteTail_.store(true, std::memory_order_release);
            hangoverSamples_ = 0;
            wake();
            return;
        }
        infiniteTail_.store(false, std::memory_order_release);
        const double clamped = std::max(0.0, seconds);
        hangoverSamples_ = static_cast<uint64_t>(std::llround(sampleRate_ * clamped));
    }

    void setHangoverSamples(uint64_t samples) noexcept {
        infiniteTail_.store(false, std::memory_order_release);
        hangoverSamples_ = samples;
    }

    // Configures hangover from a hosted plugin's reported tail length (bounded at 10 s,
    // with a 250 ms safety floor; infinite/huge tails disable auto-sleep).
    void setPluginTailSeconds(double tailSeconds) noexcept {
        if (!std::isfinite(tailSeconds) || tailSeconds >= 1.0e6) {
            infiniteTail_.store(true, std::memory_order_release);
            hangoverSamples_ = 0;
            wake();
            return;
        }
        infiniteTail_.store(false, std::memory_order_release);
        const double clamped = tailSeconds > 0.0
            ? std::clamp(tailSeconds, kDefaultHangoverSeconds, kMaxAutoHangoverSeconds)
            : kDefaultHangoverSeconds;
        hangoverSamples_ = static_cast<uint64_t>(std::llround(sampleRate_ * clamped));
    }

    void setNeverSleep(bool never) noexcept {
        neverSleep_.store(never, std::memory_order_release);
        wake();
    }

    bool neverSleep() const noexcept { return neverSleep_.load(std::memory_order_relaxed); }

    void setSleepMode(PluginSleepMode mode) noexcept { setNeverSleep(mode == PluginSleepMode::Never); }

    PluginSleepMode sleepMode() const noexcept {
        return neverSleep() ? PluginSleepMode::Never : PluginSleepMode::Auto;
    }

    // Thread-safe wake callable from UI, preset, or audio threads.
    void wake() noexcept {
        wakeEpoch_.fetch_add(1, std::memory_order_release);
        sleeping_.store(false, std::memory_order_release);
    }

    // Read by Engine / render path to decide whether processing can be skipped.
    bool isSleeping() const noexcept {
        if (neverSleep_.load(std::memory_order_acquire) || infiniteTail_.load(std::memory_order_acquire))
            return false;
        if (!sleeping_.load(std::memory_order_acquire)) return false;
        return wakeEpoch_.load(std::memory_order_acquire) == seenEpoch_.load(std::memory_order_acquire);
    }

    // Audio-thread block start: returns the epoch token to pass to observe(), and sets
    // *outSkip = true if the gate is currently asleep with no unconsumed wake request.
    uint32_t beginBlock(bool* outSkip = nullptr) noexcept {
        const uint32_t epoch = wakeEpoch_.load(std::memory_order_acquire);
        const bool skip = !neverSleep_.load(std::memory_order_acquire)
            && !infiniteTail_.load(std::memory_order_acquire)
            && sleeping_.load(std::memory_order_acquire)
            && epoch == seenEpoch_.load(std::memory_order_relaxed);
        if (skip) {
            if (outSkip) *outSkip = true;
            return epoch;
        }
        if (outSkip) *outSkip = false;
        sleeping_.store(false, std::memory_order_relaxed);
        seenEpoch_.store(epoch, std::memory_order_relaxed);
        return epoch;
    }

    bool shouldSkip(uint32_t* outBlockEpoch = nullptr) noexcept {
        bool skip = false;
        const uint32_t epoch = beginBlock(&skip);
        if (outBlockEpoch) *outBlockEpoch = epoch;
        return skip;
    }

    // Audio-thread block end: enters sleep only if !stillActive past hangoverSamples_ AND
    // no concurrent wake() occurred since beginBlock().
    void observe(bool stillActive, int32_t frames, uint32_t blockEpoch) noexcept {
        const uint32_t currentEpoch = wakeEpoch_.load(std::memory_order_acquire);
        if (currentEpoch != blockEpoch || stillActive || neverSleep_.load(std::memory_order_acquire)
            || infiniteTail_.load(std::memory_order_acquire)) {
            quietSamples_ = 0;
            sleeping_.store(false, std::memory_order_release);
            seenEpoch_.store(currentEpoch, std::memory_order_release);
            return;
        }
        quietSamples_ += static_cast<uint64_t>(std::max<int32_t>(0, frames));
        if (quietSamples_ > hangoverSamples_) {
            seenEpoch_.store(blockEpoch, std::memory_order_release);
            sleeping_.store(true, std::memory_order_release);
        }
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
    double sampleRate_ = 44100.0;
    uint64_t hangoverSamples_ = 11025; // 0.25 s at 44.1 kHz
    float releaseThreshold_ = kDefaultReleaseThreshold;
    uint64_t quietSamples_ = 0;

    std::atomic<uint32_t> wakeEpoch_{1};
    std::atomic<uint32_t> seenEpoch_{0};
    std::atomic<bool> sleeping_{false};
    std::atomic<bool> neverSleep_{false};
    std::atomic<bool> infiniteTail_{false};
};

} // namespace nota::dsp
