// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// LadderFilter — 4-pole ZDF transistor ladder with 12/24 dB slopes.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "nota/math/FastMath.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>

namespace nota::dsp {

class NOTA_DSP_API LadderFilter {
  public:
    LadderFilter() = default;

    void setSampleRate(double sr) noexcept { sr_ = sr > 0.0 ? sr : 44100.0; }
    void setInternalOversampling(int32_t n) noexcept { os_ = n > 1 ? 2 : 1; }
    void setParams(float fc, float reso) NOTA_RT_SAFE {
        const double srOs = sr_ * static_cast<double>(os_);
        const double fpole = std::min<double>(static_cast<double>(fc) * 1.35, srOs * 0.45);
        const double f = 1.0 - static_cast<double>(math::exp2f(
                                   static_cast<float>(-2.0 * 3.14159265358979 * 1.4426950408889634 * fpole / srOs)));
        f_ = std::clamp(f, 0.0, 0.995);
        reso_ = std::clamp<double>(static_cast<double>(reso), 0.0, 1.0);
        fb_ = reso_ * criticalFeedback(f_) * 1.04;
    }
    void reset() noexcept { l1_ = l2_ = l3_ = l4_ = 0.0; }

    float process(float x, bool bassComp = false) NOTA_RT_SAFE;
    float process12(float x, bool bassComp = false) NOTA_RT_SAFE;

  private:
    static double criticalFeedback(double f) noexcept {
        static constexpr double kF[] = {
            0.0, 0.0088, 0.026, 0.085, 0.162, 0.298, 0.438, 0.507, 0.618, 0.654, 0.757, 0.829};
        static constexpr double kG[] = {4.56, 4.21, 4.16, 4.39, 4.82, 5.88, 7.73, 9.17, 13.08, 15.1, 7.29, 3.98};
        constexpr std::size_t n = sizeof(kF) / sizeof(kF[0]);
        if (f >= kF[n - 1u]) return kG[n - 1u];
        std::size_t i = 1;
        while (f > kF[i]) ++i;
        const double t = (f - kF[i - 1u]) / (kF[i] - kF[i - 1u]);
        return kG[i - 1u] + (kG[i] - kG[i - 1u]) * t;
    }
    void step(float x) NOTA_RT_SAFE;
    float makeup(double tap, float x, bool bassComp) const NOTA_RT_SAFE;

    double sr_ = 44100.0;
    int32_t os_ = 2;
    double f_ = 0.0;
    double fb_ = 0.0;
    double reso_ = 0.0;
    double l1_ = 0.0, l2_ = 0.0, l3_ = 0.0, l4_ = 0.0;
};

inline void LadderFilter::step(float x) NOTA_RT_SAFE {
    const double f = f_;
    const double fb = fb_;
    const double xd = static_cast<double>(x);
    for (int32_t os = 0; os < os_; ++os) {
        const double in = xd - fb * static_cast<double>(math::tanhPrecise(static_cast<float>(l4_)));
        l1_ += f * (static_cast<double>(math::tanhPrecise(static_cast<float>(in))) -
                       static_cast<double>(math::tanhPrecise(static_cast<float>(l1_))));
        l2_ += f * (static_cast<double>(math::tanhPrecise(static_cast<float>(l1_))) -
                       static_cast<double>(math::tanhPrecise(static_cast<float>(l2_))));
        l3_ += f * (static_cast<double>(math::tanhPrecise(static_cast<float>(l2_))) -
                       static_cast<double>(math::tanhPrecise(static_cast<float>(l3_))));
        l4_ += f * (static_cast<double>(math::tanhPrecise(static_cast<float>(l3_))) -
                       static_cast<double>(math::tanhPrecise(static_cast<float>(l4_))));
    }
}

inline float LadderFilter::makeup(double tap, float x, bool bassComp) const NOTA_RT_SAFE {
    double y = tap * (1.0 + reso_ * 0.7);
    if (bassComp) y += static_cast<double>(x) * (reso_ * 0.20);
    return static_cast<float>(y);
}

inline float LadderFilter::process(float x, bool bassComp) NOTA_RT_SAFE {
    step(x);
    return makeup(l4_, x, bassComp);
}

inline float LadderFilter::process12(float x, bool bassComp) NOTA_RT_SAFE {
    step(x);
    return makeup(l2_, x, bassComp);
}

} // namespace nota::dsp
