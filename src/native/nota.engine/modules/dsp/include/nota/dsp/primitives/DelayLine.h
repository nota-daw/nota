// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DelayLine — power-of-two circular delay line with inline bitmask wrapping & Hermite reads.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <cstddef>
#include <cstdint>
#include <vector>

namespace nota::dsp {

class NOTA_DSP_API DelayLine {
  public:
    DelayLine() = default;

    void prepare(int32_t maxDelaySamples);
    void reset() noexcept;
    int32_t capacity() const noexcept { return size_; }

    void push(float x) NOTA_RT_SAFE;
    float readSample(int32_t delaySamples) const NOTA_RT_SAFE;
    float readLinear(float delaySamples) const NOTA_RT_SAFE;
    float readHermite4p(float delaySamples) const NOTA_RT_SAFE;
    float processAllpass(float x, float delaySamples, float g) NOTA_RT_SAFE;

  private:
    std::vector<float> buf_;
    int32_t size_ = 0;
    int32_t mask_ = 0;
    int32_t pos_ = 0;
    float apPrev_ = 0.0f;

    const float* at(int32_t delaySamples) const NOTA_RT_SAFE;
};

inline void DelayLine::push(float x) NOTA_RT_SAFE {
    if (size_ <= 0) return;
    buf_[static_cast<std::size_t>(pos_)] = x;
    pos_ = (pos_ + 1) & mask_;
}

inline const float* DelayLine::at(int32_t delaySamples) const NOTA_RT_SAFE {
    if (size_ <= 0) return nullptr;
    if (delaySamples < 0) delaySamples = 0;
    if (delaySamples >= size_) delaySamples = size_ - 1;
    return &buf_[static_cast<std::size_t>((pos_ - 1 - delaySamples) & mask_)];
}

inline float DelayLine::readSample(int32_t delaySamples) const NOTA_RT_SAFE {
    const float* p = at(delaySamples);
    return p != nullptr ? *p : 0.0f;
}

inline float DelayLine::readLinear(float delaySamples) const NOTA_RT_SAFE {
    if (size_ <= 0) return 0.0f;
    if (delaySamples < 0.0f) delaySamples = 0.0f;
    const float maxD = static_cast<float>(size_ - 1);
    if (delaySamples > maxD) delaySamples = maxD;
    const int32_t d0 = static_cast<int32_t>(delaySamples);
    const float frac = delaySamples - static_cast<float>(d0);
    const float a = readSample(d0);
    const float b = readSample(d0 + 1);
    return a + frac * (b - a);
}

inline float DelayLine::readHermite4p(float delaySamples) const NOTA_RT_SAFE {
    if (size_ <= 0) return 0.0f;
    if (delaySamples < 1.0f) delaySamples = 1.0f;
    const float maxD = static_cast<float>(size_ - 2);
    if (delaySamples > maxD) delaySamples = maxD;
    const int32_t d = static_cast<int32_t>(delaySamples);
    const float t = delaySamples - static_cast<float>(d);
    const float ym1 = readSample(d - 1);
    const float y0 = readSample(d);
    const float y1 = readSample(d + 1);
    const float y2 = readSample(d + 2);
    const float t2 = t * t;
    const float t3 = t2 * t;
    return 0.5f * ((2.0f * y0) + (-ym1 + y1) * t + (2.0f * ym1 - 5.0f * y0 + 4.0f * y1 - y2) * t2 +
                      (-ym1 + 3.0f * y0 - 3.0f * y1 + y2) * t3);
}

inline float DelayLine::processAllpass(float x, float delaySamples, float g) NOTA_RT_SAFE {
    const float delayed = readLinear(delaySamples);
    const float y = -g * x + delayed + g * apPrev_;
    apPrev_ = y;
    return y;
}

} // namespace nota::dsp
