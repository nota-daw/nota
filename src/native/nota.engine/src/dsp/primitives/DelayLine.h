// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DelayLine — power-of-two circular delay buffer . Size rounds
// up to 2^k so wrapping is a bitmask (& mask_) instead of modulo. Read modes:
// integer, linear-interpolated, 4-point Hermite cubic and first-order allpass.
// Shared by Delay, Chamber, Reverb, Chorus, Flanger, Vintage and Consort.
// Allocation happens in prepare() (message thread) only; the audio thread
// never allocates (AR-6).

#pragma once

#include <cstdint>
#include <vector>

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

class DelayLine {
  public:
    DelayLine() = default;

    // maxDelaySamples reserves the buffer (rounded up to a power of two).
    // Message thread only.
    void prepare(int32_t maxDelaySamples);
    void reset() noexcept;

    int32_t capacity() const noexcept { return size_; }

    // Push one sample (advances the write head). Audio thread.
    void push(float x) NOTA_RT_SAFE;

    // Read `delaySamples` behind the write head (no push). Audio thread.
    float readSample(int32_t delaySamples) const NOTA_RT_SAFE;
    float readLinear(float delaySamples) const NOTA_RT_SAFE;
    float readHermite4p(float delaySamples) const NOTA_RT_SAFE;

    // First-order allpass-interpolated read (for modulated lines). Audio thread.
    float processAllpass(float x, float delaySamples, float g) NOTA_RT_SAFE;

  private:
    std::vector<float> buf_;
    int32_t size_ = 0; // power of two
    int32_t mask_ = 0;
    int32_t pos_ = 0; // write head: next slot to fill
    float apPrev_ = 0.0f;

    const float* at(int32_t delaySamples) const NOTA_RT_SAFE;
};

} // namespace dsp
} // namespace nota
