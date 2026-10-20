// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DelayLine implementation.

#include "dsp/primitives/DelayLine.h"

#include <algorithm>

namespace nota {
namespace dsp {

void DelayLine::prepare(int32_t maxDelaySamples) {
    int32_t n = 16;
    while (n <= maxDelaySamples + 8) n <<= 1;
    buf_.assign(static_cast<size_t>(n), 0.0f);
    size_ = n;
    mask_ = n - 1;
    pos_ = 0;
    apPrev_ = 0.0f;
}

void DelayLine::reset() noexcept {
    if (!buf_.empty()) std::fill(buf_.begin(), buf_.end(), 0.0f);
    pos_ = 0;
    apPrev_ = 0.0f;
}

void DelayLine::push(float x) NOTA_RT_SAFE {
    if (size_ <= 0) return;
    buf_[pos_] = x;
    pos_ = (pos_ + 1) & mask_;
}

const float* DelayLine::at(int32_t delaySamples) const NOTA_RT_SAFE {
    // Caller guarantees size_ > 0; guard anyway for standalone use.
    if (size_ <= 0) return nullptr;
    if (delaySamples < 0) delaySamples = 0;
    if (delaySamples >= size_) delaySamples = size_ - 1;
    return &buf_[(pos_ - 1 - delaySamples) & mask_];
}

float DelayLine::readSample(int32_t delaySamples) const NOTA_RT_SAFE {
    const float* p = at(delaySamples);
    return p != nullptr ? *p : 0.0f;
}

float DelayLine::readLinear(float delaySamples) const NOTA_RT_SAFE {
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

float DelayLine::readHermite4p(float delaySamples) const NOTA_RT_SAFE {
    if (size_ <= 0) return 0.0f;
    if (delaySamples < 1.0f) delaySamples = 1.0f;
    const float maxD = static_cast<float>(size_ - 2);
    if (delaySamples > maxD) delaySamples = maxD;
    const int32_t d = static_cast<int32_t>(delaySamples);
    const float t = delaySamples - static_cast<float>(d);
    // 4-point Catmull-Rom Hermite: y[-1], y[0], y[1], y[2] around the tap.
    // Exact at t=0 (y0) and t=1 (y1); C1-continuous across taps.
    const float ym1 = readSample(d - 1);
    const float y0 = readSample(d);
    const float y1 = readSample(d + 1);
    const float y2 = readSample(d + 2);
    const float t2 = t * t, t3 = t2 * t;
    return 0.5f * ((2.0f * y0) + (-ym1 + y1) * t + (2.0f * ym1 - 5.0f * y0 + 4.0f * y1 - y2) * t2 +
                      (-ym1 + 3.0f * y0 - 3.0f * y1 + y2) * t3);
}

float DelayLine::processAllpass(float x, float delaySamples, float g) NOTA_RT_SAFE {
    const float delayed = readLinear(delaySamples);
    const float y = -g * x + delayed + g * apPrev_;
    apPrev_ = y;
    return y;
}

} // namespace dsp
} // namespace nota
