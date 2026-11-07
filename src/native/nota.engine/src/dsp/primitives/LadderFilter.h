// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// LadderFilter — 4-pole zero-delay-feedback transistor ladder.
// Monolith's tanh state-difference model (2x internally oversampled by default,
// resonant passband makeup + optional bass compensation), lifted into the shared
// primitive with fast_math tanh. The resonance feedback follows the measured edge of
// self-oscillation for the current coefficient, so full resonance sings at any pitch.
// Nota Twin uses it, with the 2-pole tap; Monolith, Consort and Pentad still run their own
// copies of the model.

#pragma once

#include <algorithm>
#include <cmath>

#include "dsp/core/RtAttributes.h"
#include "dsp/math/FastMath.h"

namespace nota {
namespace dsp {

class LadderFilter {
  public:
    LadderFilter() = default;

    void setSampleRate(double sr) noexcept;
    // The ladder runs 2 sub-steps per sample by default; a caller that already oversamples
    // (Nota Twin's voice runs at ×2) can take 1, at its own oversampled rate.
    void setInternalOversampling(int n) noexcept { os_ = n > 1 ? 2 : 1; }
    // fc in Hz, reso in [0, 1] (1 = self-oscillation).
    void setParams(float fc, float reso) NOTA_RT_SAFE;
    void reset() noexcept;

    float process(float x, bool bassComp = false) NOTA_RT_SAFE;
    // The same ladder with its output taken after the second pole: a 12 dB/oct slope that keeps
    // the 4-pole feedback loop, so resonance and self-oscillation behave as in process()
    // (Nota Twin's 12/24 switch).
    float process12(float x, bool bassComp = false) NOTA_RT_SAFE;

  private:
    void step(float x) NOTA_RT_SAFE;
    float makeup(double tap, float x, bool bassComp) const NOTA_RT_SAFE;

    double sr_ = 44100.0;
    int os_ = 2;      // internal sub-steps per sample
    double f_ = 0.0;  // one-pole coefficient at the internal rate
    double fb_ = 0.0; // resonance feedback amount
    double reso_ = 0.0;
    double l1_ = 0.0, l2_ = 0.0, l3_ = 0.0, l4_ = 0.0;
};

// Per-sample hot path: inline, so callers in other TUs inline it (no LTO).

// One sample through the four poles (os_ sub-steps), feedback taken from the last pole.
inline void LadderFilter::step(float x) NOTA_RT_SAFE {
    const double f = f_, fb = fb_;
    for (int os = 0; os < os_; ++os) {
        const double in = x - fb * fast_math::tanhPrecise(static_cast<float>(l4_));
        l1_ += f * (fast_math::tanhPrecise(static_cast<float>(in)) - fast_math::tanhPrecise(static_cast<float>(l1_)));
        l2_ += f * (fast_math::tanhPrecise(static_cast<float>(l1_)) - fast_math::tanhPrecise(static_cast<float>(l2_)));
        l3_ += f * (fast_math::tanhPrecise(static_cast<float>(l2_)) - fast_math::tanhPrecise(static_cast<float>(l3_)));
        l4_ += f * (fast_math::tanhPrecise(static_cast<float>(l3_)) - fast_math::tanhPrecise(static_cast<float>(l4_)));
    }
}

inline float LadderFilter::makeup(double tap, float x, bool bassComp) const NOTA_RT_SAFE {
    double y = tap * (1.0 + reso_ * 0.7);  // makeup for resonant passband loss
    if (bassComp) y += x * (reso_ * 0.20); // restore low end lost to resonance
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

} // namespace dsp
} // namespace nota
