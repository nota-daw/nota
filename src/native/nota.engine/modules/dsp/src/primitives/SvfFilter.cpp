// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/primitives/SvfFilter.h"

#include "nota/dsp/rt/RtAttributes.h"
#include "nota/math/FastMath.h"

namespace nota::dsp {

void SvfFilter::setSampleRate(double sr) noexcept {
    sr_ = sr > 0.0 ? sr : 44100.0;
    update();
}

void SvfFilter::setParams(float fc, float q, Mode mode, float gainDb) NOTA_RT_SAFE {
    const float nyq = static_cast<float>(sr_ * 0.5 * 0.999);
    fc_ = fc < 1.0f ? 1.0f : (fc > nyq ? nyq : fc);
    q_ = q < 0.05f ? 0.05f : (q > 40.0f ? 40.0f : q);
    mode_ = mode;
    gainDb_ = gainDb;
    update();
}

void SvfFilter::reset() noexcept {
    ic1_ = ic2_ = 0.0;
}

void SvfFilter::update() NOTA_RT_SAFE {
    const float x = 3.14159265358979f * fc_ / static_cast<float>(sr_);
    const double g = static_cast<double>(math::tanPrewarp(x));
    k_ = 1.0 / static_cast<double>(q_);
    a1_ = 1.0 / (1.0 + g * (g + k_));
    a2_ = g * a1_;
    a3_ = g * a2_;
    if (mode_ == Mode::Bell) linGain_ = math::exp2f(gainDb_ * 0.083048202372f);
    else
        linGain_ = math::dbToLin(gainDb_);
}

void SvfFilter::setCoeffs(double a1, double a2, double a3, double k) NOTA_RT_SAFE {
    a1_ = a1;
    a2_ = a2;
    a3_ = a3;
    k_ = k;
}

void Lr4Splitter::setSampleRate(double sr) noexcept {
    lp1_.setSampleRate(sr);
    lp2_.setSampleRate(sr);
    hp1_.setSampleRate(sr);
    hp2_.setSampleRate(sr);
}

void Lr4Splitter::setCutoff(float fc) NOTA_RT_SAFE {
    constexpr float kButterworthQ = 0.70710678f;
    lp1_.setParams(fc, kButterworthQ, SvfFilter::Mode::LowPass);
    lp2_.setParams(fc, kButterworthQ, SvfFilter::Mode::LowPass);
    hp1_.setParams(fc, kButterworthQ, SvfFilter::Mode::HighPass);
    hp2_.setParams(fc, kButterworthQ, SvfFilter::Mode::HighPass);
}

void Lr4Splitter::reset() noexcept {
    lp1_.reset();
    lp2_.reset();
    hp1_.reset();
    hp2_.reset();
}

float Lr4Splitter::processLow(float x) NOTA_RT_SAFE {
    return lp2_.process(lp1_.process(x));
}

float Lr4Splitter::processHigh(float x) NOTA_RT_SAFE {
    return hp2_.process(hp1_.process(x));
}

} // namespace nota::dsp
