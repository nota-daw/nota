// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// CompensationDelay — per-track plugin-delay-compensation line (
// ). Moved from src/CompensationDelay.h into the shared primitive with an
// added isIdle() sleep query (idle when no delay is dialed in or nothing was
// ever prepared). Lock-free by construction: the ring is allocated once on the
// message thread and published via the `ready` release/acquire flag.

#pragma once

#include <atomic>
#include <cstdint>
#include <vector>

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

class CompensationDelay {
  public:
    CompensationDelay() = default;

    // Message thread: set the compensation delay in samples. Allocates the ring
    // (once) the first time a positive delay is requested.
    void setDelay(int32_t samples);

    int32_t delay() const { return delay_.load(std::memory_order_relaxed); }

    // Sleep contract: true when the line passes audio through untouched.
    bool isIdle() const NOTA_RT_SAFE;

    // Audio thread: delay `frames` of interleaved stereo in place. No-op until
    // a positive delay has been prepared.
    void process(float* buf, int32_t frames) NOTA_RT_SAFE;

  private:
    std::vector<float> ring_;
    int32_t cap_ = 0; // published via ready_
    int32_t pos_ = 0; // audio thread only
    std::atomic<int32_t> delay_{0};
    std::atomic<bool> ready_{false};
};

} // namespace dsp
} // namespace nota
