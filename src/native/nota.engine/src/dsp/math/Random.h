// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Random — deterministic audio-thread PRNG for DSP (AR-6 safe).
// xorshift32: no allocation, no locks, trivially copyable state.

#pragma once

#include <cstdint>

namespace nota {
namespace dsp {

class Rng {
  public:
    constexpr Rng() noexcept = default;
    constexpr explicit Rng(uint32_t seed) noexcept : state_(seed != 0 ? seed : 0x9E3779B9u) {}

    void seed(uint32_t s) noexcept { state_ = s != 0 ? s : 0x9E3779B9u; }

    // Next uint32 in [0, 2^32).
    uint32_t nextU32() noexcept {
        uint32_t x = state_;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        state_ = x;
        return x;
    }

    // Next float in [0, 1).
    float next01() noexcept { return static_cast<float>(nextU32() >> 8) * (1.0f / 16777216.0f); }

    // Next float in [-1, 1).
    float nextBipolar() noexcept { return next01() * 2.0f - 1.0f; }

  private:
    uint32_t state_ = 0x9E3779B9u;
};

} // namespace dsp
} // namespace nota
