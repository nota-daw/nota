// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Parameter — unified normalized parameters, mappings and smoothing (AR-6 safe).
// Atomics carry UI->audio values; OnePoleSmoother de-clicks block-rate changes.

#pragma once

#include <atomic>
#include <cstdint>

#include "dsp/core/RtAttributes.h"

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

} // namespace dsp
} // namespace nota
