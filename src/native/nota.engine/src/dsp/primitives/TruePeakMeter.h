// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TruePeakMeter — 4x oversampled 16-tap SIMD FIR true-peak detector
// . Ceiling's proven windowed-sinc interpolator (BS.1770-style:
// the peak is the max of the sample and its 3 inter-sample phases), upgraded to
// a 32-float double-buffered history (no modulo indexing) with simd::dot16 FIR
// evaluation. Shared by Ceiling and AutoGain.

#pragma once

#include <cmath>
#include <array>
#include <cstdint>

#include "dsp/core/RtAttributes.h"
#include "dsp/math/Simd.h"

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

// Per-sample hot path: inline, so callers in other TUs inline it (no LTO).

inline float TruePeakMeter::push(float x) NOTA_RT_SAFE {
    hist_[w_] = x;
    hist_[w_ + 16] = x;
    w_ = (w_ + 1) & 15;
    // Window of the last 16 samples ending at the newest: hist_[w_ .. w_+15].
    const float* win = hist_.data() + w_;
    const auto& c = coefs();
    float pk = win[kDelay - 1] < 0.0f ? -win[kDelay - 1] : win[kDelay - 1];
    for (int p = 0; p < 3; ++p) {
        const float acc = simd::dot16(c[p].data(), win);
        const float a = acc < 0.0f ? -acc : acc;
        if (a > pk) pk = a;
    }
    return pk;
}

inline float TruePeakMeter::detect(float x) NOTA_RT_SAFE {
    const float a = x < 0.0f ? -x : x;
    const float tp = push(x);
    return a > tp ? a : tp;
}

} // namespace dsp
} // namespace nota
