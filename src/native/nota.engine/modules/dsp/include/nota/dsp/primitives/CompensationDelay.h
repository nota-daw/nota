// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// CompensationDelay — per-track plugin-delay-compensation circular delay line.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <atomic>
#include <cstdint>
#include <vector>

namespace nota::dsp {

class NOTA_DSP_API CompensationDelay {
  public:
    CompensationDelay() = default;

    void setDelay(int32_t samples, bool resetIfChanged = false);
    int32_t delay() const noexcept { return delay_.load(std::memory_order_relaxed); }
    bool isIdle() const NOTA_RT_SAFE {
        return !ready_.load(std::memory_order_acquire) || delay_.load(std::memory_order_relaxed) <= 0;
    }
    void process(float* buf, int32_t frames) NOTA_RT_SAFE {
        if (!ready_.load(std::memory_order_acquire)) [[likely]]
            return;
        const int32_t d = delay_.load(std::memory_order_relaxed);
        if (d <= 0 || buf == nullptr || frames <= 0) [[likely]]
            return;
        processActive(buf, frames, d);
    }

  private:
    NOTA_NOINLINE void processActive(float* buf, int32_t frames, int32_t delaySamples) NOTA_RT_SAFE;
    std::vector<float> ring_;
    int32_t cap_ = 0;
    int32_t pos_ = 0;
    int32_t validFrames_ = 0;
    uint32_t renderGeneration_ = 0;
    std::atomic<uint32_t> resetGeneration_{0};
    std::atomic<int32_t> delay_{0};
    std::atomic<bool> ready_{false};
};

} // namespace nota::dsp
