// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/dsp/rt/Parameter.h"

#include "nota/dsp/rt/RtAttributes.h"
#include "nota/math/FastMath.h"
#include <cstdint>
#include <atomic>

namespace nota::dsp {

float paramDenormalize(float norm, float minV, float maxV, ParamMapping mapping) NOTA_RT_SAFE {
    if (norm < 0.0f) norm = 0.0f;
    if (norm > 1.0f) norm = 1.0f;
    switch (mapping) {
    case ParamMapping::Linear:
    case ParamMapping::Bipolar: return minV + norm * (maxV - minV);
    case ParamMapping::Exponential: {
        if (minV <= 0.0f || maxV <= 0.0f) return minV + norm * (maxV - minV);
        return minV * math::exp2f(norm * math::log2f(maxV / minV));
    }
    case ParamMapping::Logarithmic: {
        if (minV <= 0.0f) return minV + norm * (maxV - minV);
        const float lmin = math::log2f(minV);
        const float lmax = math::log2f(maxV > 0.0f ? maxV : 1.0f);
        return math::exp2f(lmin + norm * (lmax - lmin));
    }
    }
    return minV + norm * (maxV - minV);
}

float paramNormalize(float plain, float minV, float maxV, ParamMapping mapping) NOTA_RT_SAFE {
    const float span = maxV - minV;
    if (!(span > 0.0f)) return 0.0f;
    switch (mapping) {
    case ParamMapping::Linear:
    case ParamMapping::Bipolar: {
        const float n = (plain - minV) / span;
        return n < 0.0f ? 0.0f : (n > 1.0f ? 1.0f : n);
    }
    case ParamMapping::Exponential: {
        if (plain <= 0.0f || minV <= 0.0f || maxV <= 0.0f) return 0.0f;
        const float n = math::log2f(plain / minV) / math::log2f(maxV / minV);
        return n < 0.0f ? 0.0f : (n > 1.0f ? 1.0f : n);
    }
    case ParamMapping::Logarithmic: {
        if (plain <= 0.0f || minV <= 0.0f) return 0.0f;
        const float n =
            (math::log2f(plain) - math::log2f(minV)) / (math::log2f(maxV > 0.0f ? maxV : 1.0f) - math::log2f(minV));
        return n < 0.0f ? 0.0f : (n > 1.0f ? 1.0f : n);
    }
    }
    return 0.0f;
}

void OnePoleSmoother::setSampleRate(double sr, double timeMs) NOTA_RT_SAFE {
    const double rate = sr > 0.0 ? sr : 44100.0;
    const double t = timeMs > 0.0 ? timeMs * 0.001 : 0.010;
    const float x = static_cast<float>(-1.0 / (rate * t) * 1.44269504089);
    coeff_ = 1.0f - math::exp2f(x);
    if (!(coeff_ >= 0.0f && coeff_ <= 1.0f)) coeff_ = 0.01f;
}

void OnePoleSmoother::processBlock(float* dst, int32_t n) NOTA_RT_SAFE {
    if (dst == nullptr || n <= 0) return;
    for (int32_t i = 0; i < n; ++i) {
        value_ += coeff_ * (target_ - value_);
        dst[i] = value_;
    }
}

bool ParameterChangeTracker::hasChanged() NOTA_RT_SAFE {
    if (param_ == nullptr) return false;
    const float cur = param_->load(std::memory_order_relaxed);
    if (!initialized_) {
        lastSeen_ = cur;
        initialized_ = true;
        return true;
    }
    return cur != lastSeen_;
}

void ParameterChangeTracker::acknowledge() noexcept {
    if (param_ != nullptr) {
        lastSeen_ = param_->load(std::memory_order_relaxed);
        initialized_ = true;
    }
}

} // namespace nota::dsp
