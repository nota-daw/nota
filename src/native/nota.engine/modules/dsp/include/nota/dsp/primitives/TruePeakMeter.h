// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TruePeakMeter — 4x oversampled 16-tap SIMD FIR true-peak detector.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "nota/math/BufferOps.h"

#include <array>
#include <cstddef>
#include <cstdint>

namespace nota::dsp {

class NOTA_DSP_API TruePeakMeter {
  public:
    static constexpr int32_t kTaps = 16;
    static constexpr int32_t kDelay = 8;

    TruePeakMeter() = default;

    void reset() noexcept;
    float push(float x) NOTA_RT_SAFE;
    float detect(float x) NOTA_RT_SAFE;

    static const std::array<std::array<float, kTaps>, 3>& coefs() noexcept;

  private:
    alignas(16) std::array<float, 32> hist_{};
    int32_t w_ = 0;
};

inline float TruePeakMeter::push(float x) NOTA_RT_SAFE {
    const std::size_t idx = static_cast<std::size_t>(w_);
    hist_[idx] = x;
    hist_[idx + 16u] = x;
    w_ = (w_ + 1) & 15;
    const float* win = hist_.data() + w_;
    const auto& c = coefs();
    float pk = win[kDelay - 1] < 0.0f ? -win[kDelay - 1] : win[kDelay - 1];
    for (std::size_t p = 0; p < 3u; ++p) {
        const float acc = math::dot16(c[p].data(), win);
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

} // namespace nota::dsp
