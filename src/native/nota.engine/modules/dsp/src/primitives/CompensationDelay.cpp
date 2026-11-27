// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/primitives/CompensationDelay.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>

namespace nota::dsp {

void CompensationDelay::setDelay(int32_t samples, bool resetIfChanged) {
    if (samples > 0 && cap_ == 0) {
        constexpr int32_t kCap = 65536;
        ring_.assign(static_cast<std::size_t>(kCap) * 2u, 0.0f);
        pos_ = 0;
        cap_ = kCap;
        validFrames_ = kCap;
        ready_.store(true, std::memory_order_release);
    }
    if (samples < 0) samples = 0;
    if (cap_ > 0 && samples > cap_ - 1) samples = cap_ - 1;
    const int32_t previous = delay_.load(std::memory_order_relaxed);
    delay_.store(samples, std::memory_order_relaxed);
    if (resetIfChanged && samples != previous) resetGeneration_.fetch_add(1u, std::memory_order_release);
}

bool CompensationDelay::isIdle() const NOTA_RT_SAFE {
    return !ready_.load(std::memory_order_acquire) || delay_.load(std::memory_order_relaxed) <= 0;
}

void CompensationDelay::process(float* buf, int32_t frames) NOTA_RT_SAFE {
    if (!ready_.load(std::memory_order_acquire)) return;
    const uint32_t generation = resetGeneration_.load(std::memory_order_acquire);
    if (generation != renderGeneration_) {
        renderGeneration_ = generation;
        validFrames_ = 0;
    }
    const int32_t d = delay_.load(std::memory_order_relaxed);
    if (d <= 0 || buf == nullptr || frames <= 0) return;
    const int32_t missing = std::clamp(d - validFrames_, 0, frames);
    for (int32_t i = 0; i < missing; ++i) {
        const std::size_t p = static_cast<std::size_t>(pos_);
        ring_[p * 2u] = buf[i * 2];
        ring_[p * 2u + 1u] = buf[i * 2 + 1];
        buf[i * 2] = 0.0f;
        buf[i * 2 + 1] = 0.0f;
        if (++pos_ >= cap_) pos_ = 0;
    }
    for (int32_t i = missing; i < frames; ++i) {
        int32_t rd = pos_ - d;
        if (rd < 0) rd += cap_;
        const std::size_t p = static_cast<std::size_t>(pos_);
        const std::size_t r = static_cast<std::size_t>(rd);
        const float il = buf[i * 2];
        const float ir = buf[i * 2 + 1];
        buf[i * 2] = ring_[r * 2u];
        buf[i * 2 + 1] = ring_[r * 2u + 1u];
        ring_[p * 2u] = il;
        ring_[p * 2u + 1u] = ir;
        if (++pos_ >= cap_) pos_ = 0;
    }
    validFrames_ = std::min(cap_, validFrames_ + frames);
}

} // namespace nota::dsp
