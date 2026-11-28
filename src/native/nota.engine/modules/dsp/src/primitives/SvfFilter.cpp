// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/primitives/SvfFilter.h"

#include "nota/dsp/rt/RtAttributes.h"

namespace nota::dsp {

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
