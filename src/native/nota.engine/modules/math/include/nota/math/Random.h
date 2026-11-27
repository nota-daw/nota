// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Random — deterministic audio-thread PRNG utilities (AR-6 safe: no alloc, no locks).

#pragma once

#include <cstdint>

namespace nota::math {

class Rng {
  public:
    constexpr Rng() noexcept = default;
    constexpr explicit Rng(uint32_t seed) noexcept : state_(seed != 0 ? seed : 0x9E3779B9u) {}

    void seed(uint32_t s) noexcept { state_ = s != 0 ? s : 0x9E3779B9u; }

    uint32_t nextU32() noexcept {
        uint32_t x = state_;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        state_ = x;
        return x;
    }

    float next01() noexcept { return static_cast<float>(nextU32() >> 8) * (1.0f / 16777216.0f); }
    float nextBipolar() noexcept { return next01() * 2.0f - 1.0f; }

  private:
    uint32_t state_ = 0x9E3779B9u;
};

class SplitMix64 {
  public:
    constexpr SplitMix64() noexcept = default;
    constexpr explicit SplitMix64(uint64_t seed) noexcept : state_(seed) {}

    void seed(uint64_t s) noexcept { state_ = s; }

    uint64_t nextU64() noexcept {
        uint64_t z = (state_ += 0x9E3779B97F4A7C15ULL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
        return z ^ (z >> 31);
    }

  private:
    uint64_t state_ = 0x9E3779B97F4A7C15ULL;
};

} // namespace nota::math
