// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// LadderFilter — 4-pole ZDF transistor ladder with 12/24 dB slopes.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "nota/math/FastMath.h"

#include <cstdint>

namespace nota::dsp {

class NOTA_DSP_API LadderFilter {
  public:
    LadderFilter() = default;

    void setSampleRate(double sr) noexcept;
    void setInternalOversampling(int32_t n) noexcept { os_ = n > 1 ? 2 : 1; }
    void setParams(float fc, float reso) NOTA_RT_SAFE;
    void reset() noexcept;

    float process(float x, bool bassComp = false) NOTA_RT_SAFE;
    float process12(float x, bool bassComp = false) NOTA_RT_SAFE;

  private:
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
