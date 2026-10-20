// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// LadderFilter — 4-pole zero-delay-feedback transistor ladder.
// Monolith's proven tanh state-difference model (2x internally oversampled,
// self-oscillates at max emphasis, resonant passband makeup + optional bass
// compensation), lifted verbatim into the shared primitive with fast_math tanh.
// Shared by Monolith, Consort and Pentad.

#pragma once

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

class LadderFilter {
  public:
    LadderFilter() = default;

    void setSampleRate(double sr) noexcept;
    // fc in Hz, reso in [0, 1] (1 = self-oscillation).
    void setParams(float fc, float reso) NOTA_RT_SAFE;
    void reset() noexcept;

    float process(float x, bool bassComp = false) NOTA_RT_SAFE;

  private:
    double sr_ = 44100.0;
    double f_ = 0.0;  // one-pole coefficient at 2x rate
    double fb_ = 0.0; // resonance feedback amount
    double reso_ = 0.0;
    double l1_ = 0.0, l2_ = 0.0, l3_ = 0.0, l4_ = 0.0;
};

} // namespace dsp
} // namespace nota
