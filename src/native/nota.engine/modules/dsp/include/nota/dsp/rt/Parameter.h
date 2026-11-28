// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Parameter — unified normalized parameters, mappings and smoothing (AR-6 safe).

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota::dsp {

enum class ParamMapping : uint8_t { Linear = 0, Exponential = 1, Logarithmic = 2, Bipolar = 3 };

NOTA_DSP_API float paramDenormalize(float norm, float minV, float maxV, ParamMapping mapping) NOTA_RT_SAFE;
NOTA_DSP_API float paramNormalize(float plain, float minV, float maxV, ParamMapping mapping) NOTA_RT_SAFE;

class NOTA_DSP_API OnePoleSmoother {
  public:
    OnePoleSmoother() = default;
    explicit OnePoleSmoother(float initial) noexcept : value_(initial), target_(initial) {}

    void setSampleRate(double sr, double timeMs = 10.0) NOTA_RT_SAFE;
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

class BlockRamp {
  public:
    static constexpr int32_t kMaxFrames = 256;

    void snap(float v) noexcept {
        cur_ = target_ = v;
        inc_ = 0.0f;
        left_ = 0;
        primed_ = true;
    }

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

    float next() noexcept {
        if (left_ > 0) { cur_ = --left_ == 0 ? target_ : cur_ + inc_; }
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

class NOTA_DSP_API ParameterChangeTracker {
  public:
    void watch(std::atomic<float>* param) noexcept { param_ = param; }
    bool hasChanged() NOTA_RT_SAFE;
    void acknowledge() noexcept;

  private:
    std::atomic<float>* param_ = nullptr;
    float lastSeen_ = 0.0f;
    bool initialized_ = false;
};

inline float OnePoleSmoother::tick() NOTA_RT_SAFE {
    value_ += coeff_ * (target_ - value_);
    return value_;
}

} // namespace nota::dsp
