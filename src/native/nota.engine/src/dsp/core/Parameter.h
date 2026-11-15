// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Parameter — unified normalized parameters, mappings and smoothing (AR-6 safe).
// Atomics carry UI->audio values; OnePoleSmoother de-clicks block-rate changes.

#pragma once

#include <atomic>
#include <cmath>
#include <cstdint>

#include "dsp/core/RtAttributes.h"
#include "dsp/math/FastMath.h"

namespace nota {
namespace dsp {

enum class ParamMapping : uint8_t { Linear = 0, Exponential = 1, Logarithmic = 2, Bipolar = 3 };

// Maps normalized [0,1] to plain [min,max] following the mapping curve.
float paramDenormalize(float norm, float minV, float maxV, ParamMapping mapping) NOTA_RT_SAFE;
float paramNormalize(float plain, float minV, float maxV, ParamMapping mapping) NOTA_RT_SAFE;

// One-pole low-pass smoother for block-rate parameter changes (de-click).
class OnePoleSmoother {
  public:
    OnePoleSmoother() = default;
    explicit OnePoleSmoother(float initial) noexcept : value_(initial), target_(initial) {}

    void setSampleRate(double sr, double timeMs = 10.0) noexcept;
    void setTarget(float t) noexcept { target_ = t; }
    void snap(float v) noexcept { value_ = target_ = v; }
    float value() const noexcept { return value_; }
    float target() const noexcept { return target_; }

    float tick() NOTA_RT_SAFE;
    void processBlock(float* dst, int32_t n) NOTA_RT_SAFE;

  private:
    float value_ = 0.0f;
    float target_ = 0.0f;
    float coeff_ = 0.0f;
};

// A short linear ramp for a gain-like parameter (#559): when a block's target differs from
// where the last block ended, the value glides there over the block's first frames (at most
// kMaxFrames, ~5 ms at 48 kHz) instead of jumping, so turning, automating or MIDI-mapping an
// Output / Gain / Mix no longer steps once per block at any buffer size. Settled blocks pay
// nothing: begin() returns how many leading frames ramp, the caller processes those in a
// short glide head (next() per frame) and the rest — the common whole block, when nothing
// moved — in a settled loop with the plain value, no per-sample ramp state touched at all
// (per-sample work is what the instruction gate of specs/034 budgets).
class BlockRamp {
  public:
    static constexpr int32_t kMaxFrames = 256;

    void snap(float v) noexcept {
        cur_ = target_ = v;
        inc_ = 0.0f;
        left_ = 0;
        primed_ = true;
    }
    // Starts a block of `frames` toward `target`; returns how many leading frames of this
    // block ramp (0 = already there — the settled loop alone is enough). The ramp always
    // lands exactly on the target within those frames; the first block starts at its target.
    int32_t begin(float target, int32_t frames) noexcept {
        if (!primed_) {
            snap(target);
            return 0;
        }
        target_ = target;
        const float d = target - cur_;
        const bool settled = frames <= 0 || std::fabs(d) <= 1e-6f * std::fmax(1.0f, std::fabs(target));
        if (settled) {
            cur_ = target;
            inc_ = 0.0f;
            left_ = 0;
            return 0;
        }
        left_ = frames < kMaxFrames ? frames : kMaxFrames;
        inc_ = d / static_cast<float>(left_);
        return left_;
    }
    // The value for the next frame of a glide head begun above.
    float next() noexcept {
        if (left_ > 0) {
            cur_ = --left_ == 0 ? target_ : cur_ + inc_;
        }
        return cur_;
    }
    bool ramping() const noexcept { return left_ > 0; }
    float value() const noexcept { return cur_; }

  private:
    float cur_ = 0.0f;
    float target_ = 0.0f;
    float inc_ = 0.0f;
    int32_t left_ = 0;
    bool primed_ = false;
};

// Tracks whether any watched atomic parameter changed since last poll.
// Audio thread polls hasChanged(); UI thread writes via set().
class ParameterChangeTracker {
  public:
    void watch(std::atomic<float>* param) noexcept { param_ = param; }
    bool hasChanged() NOTA_RT_SAFE;
    void acknowledge() noexcept;

  private:
    std::atomic<float>* param_ = nullptr;
    float lastSeen_ = 0.0f;
    bool initialized_ = false;
};

// Per-sample hot path: inline, so callers in other TUs inline it (no LTO).

inline float OnePoleSmoother::tick() NOTA_RT_SAFE {
    value_ += coeff_ * (target_ - value_);
    return value_;
}

} // namespace dsp
} // namespace nota
