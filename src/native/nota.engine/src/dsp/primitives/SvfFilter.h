// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SvfFilter — Cytomic/Zavalishin TPT state-variable filter.
// Shared by AutoFilter, Prism, Synth, VoltSynth, BassSynth, FluxSynth and
// WavetableSynth — replaces the per-device copies. LP/BP/HP/Notch/Peak/Allpass
// are exact TPT outputs; Bell/LowShelf/HighShelf use the standard dry +
// resonant-filter construction (proportional-Q bell, first-order shelves).
// Lr4Splitter is a 4th-order Linkwitz-Riley crossover (2× Butterworth Q stages).

#pragma once

#include <cstdint>

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

class SvfFilter {
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
        HighShelf = 8
    };

    SvfFilter() = default;

    void setSampleRate(double sr) noexcept;
    // fc in Hz, q = resonance Q (>0), mode selects the output tap.
    // gainDb only affects Bell/Shelf modes.
    void setParams(float fc, float q, Mode mode, float gainDb = 0.0f) NOTA_RT_SAFE;
    void reset() noexcept;

    float process(float x) NOTA_RT_SAFE;
    void processBlock(float* buf, int32_t n) NOTA_RT_SAFE;
    // All four taps at once (for morphing/blending between types).
    void processAll(float x, float& lp, float& bp, float& hp, float& notch) NOTA_RT_SAFE;
    // Direct TPT coefficient install (for hosts computing their own warping).
    void setCoeffs(double a1, double a2, double a3, double k) NOTA_RT_SAFE;

    float cutoff() const noexcept { return fc_; }
    float resonance() const noexcept { return q_; }

  private:
    double sr_ = 44100.0;
    float fc_ = 1000.0f;
    float q_ = 0.70710678f;
    float gainDb_ = 0.0f;
    Mode mode_ = Mode::LowPass;
    // TPT coefficients (Zavalishin): v3 = in - ic2; v1 = a1*ic1 + a2*v3;
    // v2 = ic2 + a2*ic1 + a3*v3.
    double a1_ = 0.0, a2_ = 0.0, a3_ = 0.0, k_ = 1.41421356;
    float linGain_ = 1.0f; // 10^(gainDb/40) for bell, 10^(gainDb/20) for shelves
    double ic1_ = 0.0, ic2_ = 0.0;

    void update() NOTA_RT_SAFE;
};

// 4th-order Linkwitz-Riley crossover: two cascaded Butterworth (Q = 1/sqrt(2))
// SVF low-pass stages; the high band is the mirrored high-pass cascade.
// In-phase at the crossover (LR property) — shared by Prism's splitter.
class Lr4Splitter {
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

// Per-sample hot path, inline: the engine builds without LTO, so an out-of-line call
// here costs every sample of every caller. Math runs in double like the state.
inline void SvfFilter::processAll(float x, float& lp, float& bp, float& hp, float& notch) NOTA_RT_SAFE {
    const double xd = x;
    const double v3 = xd - ic2_;
    const double v1 = a1_ * ic1_ + a2_ * v3;
    const double v2 = ic2_ + a2_ * ic1_ + a3_ * v3;
    ic1_ = 2.0 * v1 - ic1_;
    ic2_ = 2.0 * v2 - ic2_;
    const double hpd = xd - k_ * v1 - v2;
    bp = (float)v1;
    lp = (float)v2;
    hp = (float)hpd;
    notch = (float)(v2 + hpd);
}

inline float SvfFilter::process(float x) NOTA_RT_SAFE {
    float lp, bp, hp, notch;
    processAll(x, lp, bp, hp, notch);
    switch (mode_) {
    case Mode::LowPass: return lp;
    case Mode::BandPass: return bp;
    case Mode::HighPass: return hp;
    case Mode::Notch: return notch;
    case Mode::Peak: return lp - hp;
    case Mode::Allpass: return x - 2.0f * (float)k_ * bp;
    case Mode::Bell: {
        const float peak = lp - hp;
        return x + (linGain_ - 1.0f) * peak; // proportional-Q bell (boost & cut)
    }
    case Mode::LowShelf: return x + (linGain_ - 1.0f) * lp;
    case Mode::HighShelf: return x + (linGain_ - 1.0f) * hp;
    }
    return lp;
}

inline void SvfFilter::processBlock(float* buf, int32_t n) NOTA_RT_SAFE {
    if (buf == nullptr || n <= 0) return;
    for (int32_t i = 0; i < n; ++i) buf[i] = process(buf[i]);
}

} // namespace dsp
} // namespace nota
