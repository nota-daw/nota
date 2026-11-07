// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SteinerFilter — a 12 dB/oct multimode filter in the Steiner-Parker spirit: one resonant
// 2-pole core whose response sweeps continuously LP → BP → HP → Notch, with a dirty, diode-like
// input stage and a resonance that self-oscillates. Nota Twin's multimode filter (specs/055 R5).
//
// The core is a zero-delay-feedback state-variable filter (Cytomic TPT form). The Steiner
// circuit gets its modes by injecting the input at its low-, band- or high-pass node; for a
// linear 2-pole that equals mixing the three outputs, so the mode is a crossfade of outputs
// and the character comes from the non-linearities instead: the input runs through a soft
// diode pair, and the band-pass state is softly bounded every sample. Past full resonance the
// damping goes slightly negative, so the filter self-oscillates; the bounded state keeps that
// oscillation at a steady level instead of growing.
//
// Real-time safe: no allocation, no tables. The caller may change the cutoff every sample
// (audio-rate FM): setG takes a prewarped coefficient and refreshes the ZDF coefficients; the
// per-sample cost is then one divide and one sqrt.

#pragma once

#include <algorithm>
#include <cmath>

#include "dsp/core/RtAttributes.h"
#include "dsp/math/FastMath.h"

namespace nota {
namespace dsp {

class SteinerFilter {
  public:
    SteinerFilter() = default;

    void setSampleRate(double sr) noexcept { sr_ = sr > 0.0 ? sr : 44100.0; }
    // fc in Hz, reso 0..1 (1 = self-oscillation), mode 0..1 (LP → BP → HP → Notch).
    void setParams(float fc, float reso, float mode) NOTA_RT_SAFE {
        setG(prewarp(fc));
        setReso(reso);
        setMode(mode);
    }
    // The bilinear-prewarped coefficient for fc at this rate.
    float prewarp(float fc) const NOTA_RT_SAFE {
        const float w = 3.14159265f * std::clamp(fc, 10.0f, static_cast<float>(sr_ * 0.45)) / static_cast<float>(sr_);
        return fast_math::tanPrewarp(w);
    }
    void setG(float g) NOTA_RT_SAFE {
        g_ = g;
        coefs();
    }
    void setReso(float reso) NOTA_RT_SAFE {
        const float r = std::clamp(reso, 0.0f, 1.0f);
        k_ = 2.0f - 2.04f * r; // −0.04 at the top: just past the edge, so it sings
        coefs();
    }
    void setMode(float mode) NOTA_RT_SAFE {
        const float m = std::clamp(mode, 0.0f, 1.0f) * 3.0f;
        // Output weights for the three segments; band-pass is lifted so its level sits with the others.
        constexpr float kBp = 1.6f;
        if (m < 1.0f) {
            wLp_ = 1.0f - m, wBp_ = m * kBp, wHp_ = 0.0f;
        } else if (m < 2.0f) {
            const float t = m - 1.0f;
            wLp_ = 0.0f, wBp_ = (1.0f - t) * kBp, wHp_ = t;
        } else {
            const float t = m - 2.0f; // HP → notch (LP + HP)
            wLp_ = t, wBp_ = 0.0f, wHp_ = 1.0f;
        }
    }
    void reset() noexcept { ic1_ = ic2_ = 0.0f; }

    float process(float x) NOTA_RT_SAFE {
        // Diode-pair input: unity for small signals, rounding off the peaks of a hot mix.
        const float xin = x / (1.0f + 0.25f * std::fabs(x));
        const float k = k_, a1 = a1_, a2 = a2_, a3 = a3_;
        const float v3 = xin - ic2_;
        const float v1 = a1 * ic1_ + a2 * v3; // band-pass
        const float v2 = ic2_ + a2 * ic1_ + a3 * v3; // low-pass
        ic1_ = bound(2.0f * v1 - ic1_);
        ic2_ = 2.0f * v2 - ic2_;
        const float hp = xin - k * v1 - v2;
        return wLp_ * v2 + wBp_ * v1 + wHp_ * hp;
    }

  private:
    // The TPT coefficients, recomputed when the cutoff or resonance moves (not per sample).
    void coefs() NOTA_RT_SAFE {
        a1_ = 1.0f / (1.0f + g_ * (g_ + k_));
        a2_ = g_ * a1_;
        a3_ = g_ * a2_;
    }
    // Smooth, odd, monotonic and bounded (±4): within 1 % of linear below 0.6, so ordinary
    // signals stay clean and only a self-oscillating or overdriven state is held in.
    static float bound(float s) NOTA_RT_SAFE { return s / std::sqrt(1.0f + s * s * (1.0f / 16.0f)); }

    double sr_ = 44100.0;
    float g_ = 0.5f, k_ = 2.0f;
    float a1_ = 1.0f / 3.25f, a2_ = 0.5f / 3.25f, a3_ = 0.25f / 3.25f;
    float wLp_ = 1.0f, wBp_ = 0.0f, wHp_ = 0.0f;
    float ic1_ = 0.0f, ic2_ = 0.0f;
};

} // namespace dsp
} // namespace nota
