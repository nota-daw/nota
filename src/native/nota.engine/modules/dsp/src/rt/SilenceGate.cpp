// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/rt/SilenceGate.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota::dsp {
namespace {

std::atomic<int32_t> gOpenPluginEditors{0};
std::atomic<int32_t> gNeverSleepDevices{0};

} // namespace

bool anyOpenPluginEditor() noexcept {
    return gOpenPluginEditors.load(std::memory_order_relaxed) > 0;
}

bool anyNeverSleepDevice() noexcept {
    return gNeverSleepDevices.load(std::memory_order_relaxed) > 0;
}

void adjustOpenPluginEditors(int32_t delta) noexcept {
    if (delta != 0) gOpenPluginEditors.fetch_add(delta, std::memory_order_relaxed);
}

void adjustNeverSleepDevices(int32_t delta) noexcept {
    if (delta != 0) gNeverSleepDevices.fetch_add(delta, std::memory_order_relaxed);
}

SilenceGate::~SilenceGate() {
    if ((flags_.fetch_and(~kHoldAwake, std::memory_order_acq_rel) & kHoldAwake) != 0u) { adjustOpenPluginEditors(-1); }
}

void SilenceGate::prepare(double sampleRate, double hangoverSeconds, float releaseThreshold) noexcept {
    sampleRate_ = (std::isfinite(sampleRate) && sampleRate > 0.0) ? sampleRate : 44100.0;
    releaseThreshold_ = releaseThreshold > 0.0f ? releaseThreshold : kDefaultReleaseThreshold;
    setHangoverSeconds(hangoverSeconds);
    wake();
}

void SilenceGate::setHangoverSeconds(double seconds) noexcept {
    if (!std::isfinite(seconds) || seconds >= 1.0e6) {
        flags_.fetch_or(kInfiniteTail, std::memory_order_release);
        hangoverSamples_ = 0;
        wake();
        return;
    }
    flags_.fetch_and(~kInfiniteTail, std::memory_order_release);
    const double clamped = std::max(0.0, seconds);
    hangoverSamples_ = static_cast<uint64_t>(std::llround(sampleRate_ * clamped));
}

void SilenceGate::setHangoverSamples(uint64_t samples) noexcept {
    flags_.fetch_and(~kInfiniteTail, std::memory_order_release);
    hangoverSamples_ = samples;
}

void SilenceGate::setPluginTailSeconds(double tailSeconds) noexcept {
    if (!std::isfinite(tailSeconds) || tailSeconds >= 1.0e6) {
        flags_.fetch_or(kInfiniteTail, std::memory_order_release);
        hangoverSamples_ = 0;
        wake();
        return;
    }
    flags_.fetch_and(~kInfiniteTail, std::memory_order_release);
    const double clamped = tailSeconds > 0.0 ? std::clamp(tailSeconds, kDefaultHangoverSeconds, kMaxAutoHangoverSeconds)
                                             : kDefaultHangoverSeconds;
    hangoverSamples_ = static_cast<uint64_t>(std::llround(sampleRate_ * clamped));
}

void SilenceGate::setNeverSleep(bool never) noexcept {
    if (never) flags_.fetch_or(kNeverSleep, std::memory_order_release);
    else
        flags_.fetch_and(~kNeverSleep, std::memory_order_release);
    wake();
}

void SilenceGate::setHoldAwake(bool hold) noexcept {
    const uint32_t prev = hold ? flags_.fetch_or(kHoldAwake, std::memory_order_acq_rel)
                               : flags_.fetch_and(~kHoldAwake, std::memory_order_acq_rel);
    const bool was = (prev & kHoldAwake) != 0u;
    if (was != hold) adjustOpenPluginEditors(hold ? 1 : -1);
    if (hold) wake();
}

uint32_t SilenceGate::beginBlockSlow(bool* outSkip) noexcept {
    const uint32_t epoch = wakeEpoch_.load(std::memory_order_acquire);
    const uint32_t f = flags_.load(std::memory_order_acquire);
    if ((f & kKeepAwakeMask) == 0u && sleeping_.load(std::memory_order_acquire) &&
        epoch == seenEpoch_.load(std::memory_order_relaxed)) {
        if (outSkip) *outSkip = true;
        return epoch;
    }
    if (outSkip) *outSkip = false;
    sleeping_.store(false, std::memory_order_relaxed);
    seenEpoch_.store(epoch, std::memory_order_relaxed);
    quietSamples_ = 0;
    return epoch;
}

void SilenceGate::observeQuietSlow(int32_t frames, uint32_t blockEpoch) noexcept {
    const uint32_t currentEpoch = wakeEpoch_.load(std::memory_order_acquire);
    const uint32_t f = flags_.load(std::memory_order_acquire);
    if (currentEpoch != blockEpoch || (f & kKeepAwakeMask) != 0u) {
        quietSamples_ = 0;
        seenEpoch_.store(currentEpoch, std::memory_order_release);
        return;
    }
    quietSamples_ += static_cast<uint64_t>(std::max<int32_t>(0, frames));
    if (quietSamples_ > hangoverSamples_) {
        seenEpoch_.store(blockEpoch, std::memory_order_release);
        sleeping_.store(true, std::memory_order_seq_cst);
        if (wakeEpoch_.load(std::memory_order_seq_cst) != blockEpoch ||
            (flags_.load(std::memory_order_acquire) & kKeepAwakeMask) != 0u) {
            sleeping_.store(false, std::memory_order_release);
            quietSamples_ = 0;
        }
    }
}

} // namespace nota::dsp
