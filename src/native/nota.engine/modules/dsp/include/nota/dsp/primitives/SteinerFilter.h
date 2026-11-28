// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SteinerFilter — 12 dB/oct ZDF multimode filter in the Steiner-Parker spirit.

#pragma once

#include "nota/dsp/rt/RtAttributes.h"
#include "nota/math/FastMath.h"

#include <algorithm>
#include <cmath>

namespace nota::dsp {

class SteinerFilter {
  public:
    SteinerFilter() = default;

    void setSampleRate(double sr) noexcept { sr_ = sr > 0.0 ? sr : 44100.0; }

    void setParams(float fc, float reso, float mode) NOTA_RT_SAFE {
        setG(prewarp(fc));
        setReso(reso);
        setMode(mode);
    }

    float prewarp(float fc) const NOTA_RT_SAFE {
        const float w = 3.14159265f * std::clamp(fc, 10.0f, static_cast<float>(sr_ * 0.45)) / static_cast<float>(sr_);
        return math::tanPrewarp(w);
    }

    void setG(float g) NOTA_RT_SAFE {
        g_ = g;
        coefs();
    }

    void setReso(float reso) NOTA_RT_SAFE {
        const float r = std::clamp(reso, 0.0f, 1.0f);
        k_ = 2.0f - 2.04f * r;
        coefs();
    }

    void setMode(float mode) NOTA_RT_SAFE {
        const float m = std::clamp(mode, 0.0f, 1.0f) * 3.0f;
        constexpr float kBp = 1.6f;
        if (m < 1.0f) {
            wLp_ = 1.0f - m;
            wBp_ = m * kBp;
            wHp_ = 0.0f;
        } else if (m < 2.0f) {
            const float t = m - 1.0f;
            wLp_ = 0.0f;
            wBp_ = (1.0f - t) * kBp;
            wHp_ = t;
        } else {
            const float t = m - 2.0f;
            wLp_ = t;
            wBp_ = 0.0f;
            wHp_ = 1.0f;
        }
    }

    void reset() noexcept { ic1_ = ic2_ = 0.0f; }

    float process(float x) NOTA_RT_SAFE {
        const float xin = x / (1.0f + 0.25f * std::fabs(x));
        const float k = k_;
        const float a1 = a1_;
        const float a2 = a2_;
        const float a3 = a3_;
        const float v3 = xin - ic2_;
        const float v1 = a1 * ic1_ + a2 * v3;
        const float v2 = ic2_ + a2 * ic1_ + a3 * v3;
        ic1_ = bound(2.0f * v1 - ic1_);
        ic2_ = 2.0f * v2 - ic2_;
        const float hp = xin - k * v1 - v2;
        return wLp_ * v2 + wBp_ * v1 + wHp_ * hp;
    }

  private:
    void coefs() NOTA_RT_SAFE {
        a1_ = 1.0f / (1.0f + g_ * (g_ + k_));
        a2_ = g_ * a1_;
        a3_ = g_ * a2_;
    }

    static float bound(float s) NOTA_RT_SAFE { return s / std::sqrt(1.0f + s * s * (1.0f / 16.0f)); }

    double sr_ = 44100.0;
    float g_ = 0.5f;
    float k_ = 2.0f;
    float a1_ = 1.0f / 3.25f;
    float a2_ = 0.5f / 3.25f;
    float a3_ = 0.25f / 3.25f;
    float wLp_ = 1.0f;
    float wBp_ = 0.0f;
    float wHp_ = 0.0f;
    float ic1_ = 0.0f;
    float ic2_ = 0.0f;
};

} // namespace nota::dsp
