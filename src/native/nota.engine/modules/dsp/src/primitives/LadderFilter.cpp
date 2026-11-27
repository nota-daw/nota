// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/primitives/LadderFilter.h"

#include "nota/dsp/rt/RtAttributes.h"
#include "nota/math/FastMath.h"

#include <algorithm>
#include <cstddef>

namespace nota::dsp {
namespace {

double criticalFeedback(double f) noexcept {
    static constexpr double kF[] = {0.0, 0.0088, 0.026, 0.085, 0.162, 0.298, 0.438, 0.507, 0.618, 0.654, 0.757, 0.829};
    static constexpr double kG[] = {4.56, 4.21, 4.16, 4.39, 4.82, 5.88, 7.73, 9.17, 13.08, 15.1, 7.29, 3.98};
    constexpr std::size_t n = sizeof(kF) / sizeof(kF[0]);
    if (f >= kF[n - 1u]) return kG[n - 1u];
    std::size_t i = 1;
    while (f > kF[i]) ++i;
    const double t = (f - kF[i - 1u]) / (kF[i] - kF[i - 1u]);
    return kG[i - 1u] + (kG[i] - kG[i - 1u]) * t;
}

} // namespace

void LadderFilter::setSampleRate(double sr) noexcept {
    sr_ = sr > 0.0 ? sr : 44100.0;
}

void LadderFilter::setParams(float fc, float reso) NOTA_RT_SAFE {
    const double srOs = sr_ * static_cast<double>(os_);
    const double fpole = std::min<double>(static_cast<double>(fc) * 1.35, srOs * 0.45);
    const double f = 1.0 - static_cast<double>(math::exp2f(
                               static_cast<float>(-2.0 * 3.14159265358979 * 1.4426950408889634 * fpole / srOs)));
    f_ = std::clamp(f, 0.0, 0.995);
    reso_ = std::clamp<double>(static_cast<double>(reso), 0.0, 1.0);
    fb_ = reso_ * criticalFeedback(f_) * 1.04;
}

void LadderFilter::reset() noexcept {
    l1_ = l2_ = l3_ = l4_ = 0.0;
}

} // namespace nota::dsp
