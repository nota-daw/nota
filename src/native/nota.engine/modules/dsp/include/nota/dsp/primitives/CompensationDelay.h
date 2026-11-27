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
    bool isIdle() const NOTA_RT_SAFE;
    void process(float* buf, int32_t frames) NOTA_RT_SAFE;

  private:
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
