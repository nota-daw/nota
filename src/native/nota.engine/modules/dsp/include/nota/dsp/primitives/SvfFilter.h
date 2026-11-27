// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SvfFilter & Lr4Splitter — Zavalishin TPT state-variable filter and 4th-order LR4 crossover.

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <cstdint>

namespace nota::dsp {

class NOTA_DSP_API SvfFilter {
  public:
    enum class Mode : uint8_t {
        LowPass = 0,
        BandPass = 1,
        HighPass = 2,
        Notch = 3,
        Peak = 4,
        Allpass = 5,
        Bell = 6,
        LowShelf = 7,
        HighShelf = 8,
    };

    SvfFilter() = default;

    void setSampleRate(double sr) noexcept;
    void setParams(float fc, float q, Mode mode, float gainDb = 0.0f) NOTA_RT_SAFE;
    void reset() noexcept;

    float process(float x) NOTA_RT_SAFE;
    void processBlock(float* buf, int32_t n) NOTA_RT_SAFE;
    void processAll(float x, float& lp, float& bp, float& hp, float& notch) NOTA_RT_SAFE;
    void setCoeffs(double a1, double a2, double a3, double k) NOTA_RT_SAFE;

    float cutoff() const noexcept { return fc_; }
    float resonance() const noexcept { return q_; }

  private:
    double sr_ = 44100.0;
    float fc_ = 1000.0f;
    float q_ = 0.70710678f;
    float gainDb_ = 0.0f;
    Mode mode_ = Mode::LowPass;
    double a1_ = 0.0;
    double a2_ = 0.0;
    double a3_ = 0.0;
    double k_ = 1.41421356;
    float linGain_ = 1.0f;
    double ic1_ = 0.0;
    double ic2_ = 0.0;

    void update() NOTA_RT_SAFE;
};

class NOTA_DSP_API Lr4Splitter {
  public:
    Lr4Splitter() = default;

    void setSampleRate(double sr) noexcept;
    void setCutoff(float fc) NOTA_RT_SAFE;
    void reset() noexcept;

    float processLow(float x) NOTA_RT_SAFE;
    float processHigh(float x) NOTA_RT_SAFE;

  private:
    SvfFilter lp1_, lp2_, hp1_, hp2_;
};

inline void SvfFilter::processAll(float x, float& lp, float& bp, float& hp, float& notch) NOTA_RT_SAFE {
    const double xd = static_cast<double>(x);
    const double v3 = xd - ic2_;
    const double v1 = a1_ * ic1_ + a2_ * v3;
    const double v2 = ic2_ + a2_ * ic1_ + a3_ * v3;
    ic1_ = 2.0 * v1 - ic1_;
    ic2_ = 2.0 * v2 - ic2_;
    const double hpd = xd - k_ * v1 - v2;
    bp = static_cast<float>(v1);
    lp = static_cast<float>(v2);
    hp = static_cast<float>(hpd);
    notch = static_cast<float>(v2 + hpd);
}

inline float SvfFilter::process(float x) NOTA_RT_SAFE {
    float lp = 0.0f;
    float bp = 0.0f;
    float hp = 0.0f;
    float notch = 0.0f;
    processAll(x, lp, bp, hp, notch);
    switch (mode_) {
    case Mode::LowPass: return lp;
    case Mode::BandPass: return bp;
    case Mode::HighPass: return hp;
    case Mode::Notch: return notch;
    case Mode::Peak: return lp - hp;
    case Mode::Allpass: return x - 2.0f * static_cast<float>(k_) * bp;
    case Mode::Bell: return x + (linGain_ - 1.0f) * (lp - hp);
    case Mode::LowShelf: return x + (linGain_ - 1.0f) * lp;
    case Mode::HighShelf: return x + (linGain_ - 1.0f) * hp;
    }
    return lp;
}

inline void SvfFilter::processBlock(float* buf, int32_t n) NOTA_RT_SAFE {
    if (buf == nullptr || n <= 0) return;
    for (int32_t i = 0; i < n; ++i) buf[i] = process(buf[i]);
}

} // namespace nota::dsp
