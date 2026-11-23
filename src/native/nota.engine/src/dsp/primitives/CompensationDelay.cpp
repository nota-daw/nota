// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// CompensationDelay implementation.

#include "dsp/primitives/CompensationDelay.h"

#include <algorithm>

namespace nota {
namespace dsp {

void CompensationDelay::setDelay(int32_t samples, bool resetIfChanged) {
    if (samples > 0 && cap_ == 0) {
        constexpr int32_t kCap = 65536; // ~1.36 s @ 48 kHz; plugin latencies fit
        ring_.assign(static_cast<size_t>(kCap) * 2, 0.0f);
        pos_ = 0;
        cap_ = kCap;
        validFrames_ = kCap; // The freshly allocated history already contains silence.
        ready_.store(true, std::memory_order_release);
    }
    if (samples < 0) samples = 0;
    if (cap_ > 0 && samples > cap_ - 1) samples = cap_ - 1;
    const int32_t previous = delay_.load(std::memory_order_relaxed);
    delay_.store(samples, std::memory_order_relaxed);
    if (resetIfChanged && samples != previous) resetGeneration_.fetch_add(1, std::memory_order_release);
}

bool CompensationDelay::isIdle() const NOTA_RT_SAFE {
    return !ready_.load(std::memory_order_acquire) || delay_.load(std::memory_order_relaxed) <= 0;
}

void CompensationDelay::process(float* buf, int32_t frames) NOTA_RT_SAFE {
    if (!ready_.load(std::memory_order_acquire)) return;
    const auto generation = resetGeneration_.load(std::memory_order_acquire);
    if (generation != renderGeneration_) {
        renderGeneration_ = generation;
        validFrames_ = 0;
    }
    const int32_t d = delay_.load(std::memory_order_relaxed);
    if (d <= 0 || buf == nullptr || frames <= 0) return;
    const int32_t missing = std::clamp(d - validFrames_, 0, frames);
    for (int32_t i = 0; i < missing; ++i) {
        ring_[pos_ * 2] = buf[i * 2];
        ring_[pos_ * 2 + 1] = buf[i * 2 + 1];
        buf[i * 2] = buf[i * 2 + 1] = 0;
        if (++pos_ >= cap_) pos_ = 0;
    }
    for (int32_t i = missing; i < frames; ++i) {
        int32_t rd = pos_ - d;
        if (rd < 0) rd += cap_;
        const float il = buf[i * 2], ir = buf[i * 2 + 1];
        buf[i * 2] = ring_[rd * 2];
        buf[i * 2 + 1] = ring_[rd * 2 + 1];
        ring_[pos_ * 2] = il;
        ring_[pos_ * 2 + 1] = ir;
        if (++pos_ >= cap_) pos_ = 0;
    }
    validFrames_ = std::min(cap_, validFrames_ + frames);
}

} // namespace dsp
} // namespace nota
