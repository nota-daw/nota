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

namespace {

// The loop gain at which the ladder starts to self-oscillate, against its one-pole coefficient
// (measured: −50 dB steady oscillation from a −100 dB noise floor, one sub-step). A digital
// one-pole adds phase lag as its coefficient grows, so the edge climbs from ≈ 4.2 (the analog
// value) towards the top of the range, then falls again near Nyquist; a fixed feedback would
// leave high cutoffs unable to sing.
double criticalFeedback(double f) {
    static const double kF[] = {0.0, 0.0088, 0.026, 0.085, 0.162, 0.298, 0.438, 0.507, 0.618, 0.654, 0.757, 0.829};
    static const double kG[] = {4.56, 4.21, 4.16, 4.39, 4.82, 5.88, 7.73, 9.17, 13.08, 15.1, 7.29, 3.98};
    constexpr int n = sizeof kF / sizeof kF[0];
    if (f >= kF[n - 1]) return kG[n - 1];
    int i = 1;
    while (f > kF[i]) ++i;
    const double t = (f - kF[i - 1]) / (kF[i] - kF[i - 1]);
    return kG[i - 1] + (kG[i] - kG[i - 1]) * t;
}

} // namespace

void LadderFilter::setSampleRate(double sr) noexcept {
    sr_ = sr > 0.0 ? sr : 44100.0;
}

void LadderFilter::setParams(float fc, float reso) NOTA_RT_SAFE {
    const double srOs = sr_ * os_;
    // Poles sit a touch above the knob so the passband stays open while the
    // resonant peak tracks the cutoff (proven in Monolith).
    const double fpole = std::min<double>(fc * 1.35, srOs * 0.45);
    // fast exp2 (≈ 0.03 cent): this runs per sample when a caller sweeps the cutoff.
    double f = 1.0 - fast_math::exp2f(static_cast<float>(-2.0 * 3.14159265358979 * 1.4426950408889634 * fpole / srOs));
    f_ = std::clamp(f, 0.0, 0.995);
    reso_ = std::clamp<double>(reso, 0.0, 1.0);
    fb_ = reso_ * criticalFeedback(f_) * 1.04; // just past the edge at max: self-oscillates at any pitch
}

void LadderFilter::reset() noexcept {
    l1_ = l2_ = l3_ = l4_ = 0.0;
}

} // namespace dsp
} // namespace nota
