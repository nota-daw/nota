// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// LadderFilter implementation — Monolith's model with fast_math::tanh stages.

#include "dsp/primitives/LadderFilter.h"

#include <algorithm>
#include <cmath>

#include "dsp/math/FastMath.h"

namespace nota {
namespace dsp {

void LadderFilter::setSampleRate(double sr) noexcept {
    sr_ = sr > 0.0 ? sr : 44100.0;
}

void LadderFilter::setParams(float fc, float reso) NOTA_RT_SAFE {
    const double srOs = sr_ * 2.0;
    // Poles sit a touch above the knob so the passband stays open while the
    // resonant peak tracks the cutoff (proven in Monolith).
    const double fpole = std::min<double>(fc * 1.35, srOs * 0.45);
    double f = 1.0 - std::exp(-2.0 * 3.14159265358979 * fpole / srOs);
    f_ = std::clamp(f, 0.0, 0.995);
    reso_ = std::clamp<double>(reso, 0.0, 1.0);
    fb_ = reso_ * 4.2; // self-oscillation at max
}

void LadderFilter::reset() noexcept {
    l1_ = l2_ = l3_ = l4_ = 0.0;
}

float LadderFilter::process(float x, bool bassComp) NOTA_RT_SAFE {
    const double f = f_, fb = fb_;
    for (int os = 0; os < 2; ++os) {
        const double in = x - fb * fast_math::tanhPrecise(static_cast<float>(l4_));
        l1_ += f * (fast_math::tanhPrecise(static_cast<float>(in)) - fast_math::tanhPrecise(static_cast<float>(l1_)));
        l2_ += f * (fast_math::tanhPrecise(static_cast<float>(l1_)) - fast_math::tanhPrecise(static_cast<float>(l2_)));
        l3_ += f * (fast_math::tanhPrecise(static_cast<float>(l2_)) - fast_math::tanhPrecise(static_cast<float>(l3_)));
        l4_ += f * (fast_math::tanhPrecise(static_cast<float>(l3_)) - fast_math::tanhPrecise(static_cast<float>(l4_)));
    }
    double y = l4_ * (1.0 + reso_ * 0.7);  // makeup for resonant passband loss
    if (bassComp) y += x * (reso_ * 0.20); // restore low end lost to resonance
    return static_cast<float>(y);
}

} // namespace dsp
} // namespace nota
