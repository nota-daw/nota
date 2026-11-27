// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ExpressionTypes — per-note MPE expression and instrument-wide smoothers.

#pragma once

#include "nota/dsp/Export.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace nota::dsp {

enum ExprDim : int32_t { ExprBend = 0, ExprPressure = 1, ExprSlide = 2, kExprDims = 3 };

inline constexpr int32_t ExprSustain = 3;
inline constexpr int32_t kExprWireDims = 4;
inline constexpr float kExprNeutral[kExprDims] = {0.0f, 0.0f, 0.5f};

inline float exprSmoothCoef(double sampleRate) noexcept {
    return static_cast<float>(1.0 - std::exp(-1.0 / (0.003 * (sampleRate > 0.0 ? sampleRate : 44100.0))));
}

struct NOTA_DSP_API GlobalExpr {
    float wheel = 0.0f;
    float pressure = 0.0f;
    float slide = 0.5f;

    void set(int32_t dim, float v) noexcept {
        if (dim == ExprBend) wheel = std::clamp(v, -1.0f, 1.0f);
        else if (dim == ExprPressure)
            pressure = std::clamp(v, 0.0f, 1.0f);
        else if (dim == ExprSlide)
            slide = std::clamp(v, 0.0f, 1.0f);
    }

    void reset() noexcept {
        wheel = 0.0f;
        pressure = 0.0f;
        slide = 0.5f;
    }

    void offsets(float bendRangeSemis, float out[kExprDims]) const noexcept {
        out[ExprBend] = wheel * bendRangeSemis;
        out[ExprPressure] = pressure;
        out[ExprSlide] = slide - 0.5f;
    }
};

struct NOTA_DSP_API VoiceExpr {
    float tgt[kExprDims] = {0.0f, 0.0f, 0.5f};
    float cur[kExprDims] = {0.0f, 0.0f, 0.5f};
    bool fresh = true;

    void reset(const float add[kExprDims]) noexcept {
        for (int32_t d = 0; d < kExprDims; ++d) {
            tgt[d] = kExprNeutral[d];
            cur[d] = kExprNeutral[d] + add[d];
        }
        fresh = true;
    }

    void set(int32_t dim, float v, const float add[kExprDims]) noexcept {
        if (dim < 0 || dim >= kExprDims) return;
        tgt[dim] = v;
        if (fresh) cur[dim] = v + add[dim];
    }

    void tick(const float add[kExprDims], float coef) noexcept {
        fresh = false;
        for (int32_t d = 0; d < kExprDims; ++d) cur[d] += (tgt[d] + add[d] - cur[d]) * coef;
    }

    float bend() const noexcept { return cur[ExprBend]; }
    float pressure() const noexcept { return std::clamp(cur[ExprPressure], 0.0f, 1.0f); }
    float slide() const noexcept { return std::clamp(cur[ExprSlide], 0.0f, 1.0f) * 2.0f - 1.0f; }
};

} // namespace nota::dsp
