// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TruePeakMeter — 4x oversampled 16-tap SIMD FIR true-peak detector
// . Ceiling's proven windowed-sinc interpolator (BS.1770-style:
// the peak is the max of the sample and its 3 inter-sample phases), upgraded to
// a 32-float double-buffered history (no modulo indexing) with simd::dot16 FIR
// evaluation. Shared by Ceiling and AutoGain.

#pragma once

#include <array>
#include <cstdint>

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

class TruePeakMeter {
  public:
    static constexpr int kTaps = 16;
    static constexpr int kDelay = 8; // reading trails its input by 8 samples

    TruePeakMeter() = default;

    void reset() noexcept;

    // Push one sample; returns the current true peak (sample + 3 phases).
    float push(float x) NOTA_RT_SAFE;
    // Detector form: max of the plain sample peak now and the inter-sample
    // peak kDelay samples later (for look-ahead gain staging).
    float detect(float x) NOTA_RT_SAFE;

    static const std::array<std::array<float, kTaps>, 3>& coefs() noexcept;

  private:
    // Double-buffered history: sample written at w and w+16, w in [0,16).
    alignas(16) std::array<float, 32> hist_{};
    int w_ = 0;
};

} // namespace dsp
} // namespace nota
