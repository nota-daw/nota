// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// FastMath — high-performance, real-time safe math approximations for DSP.
// Consolidated from Volt/Bass/Pentad synths.
// All functions are AR-6 safe: no allocation, no locks, IEEE-754 float only.

#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace fast_math {

// Fast base-2 exponential 2^x (~1.5e-5 relative error on [-60, 60]).
NOTA_ALWAYS_INLINE float exp2f(float x) NOTA_RT_SAFE {
    x = std::clamp(x, -60.0f, 60.0f);
    const float fi = std::floor(x);
    const float f = x - fi;
    const float p =
        1.0f + f * (0.69314718f +
                       f * (0.24022651f + f * (0.05550411f + f * (0.00961813f + f * (0.00133336f + f * 0.00015404f)))));
    const int32_t e = static_cast<int32_t>(fi) + 127;
    const uint32_t bits = static_cast<uint32_t>(e) << 23;
    float s;
    std::memcpy(&s, &bits, sizeof s);
    return p * s;
}

// Fast base-2 logarithm log2(x). Clamps <= 1e-15f to -50.0f (no -inf/NaN).
NOTA_ALWAYS_INLINE float log2f(float x) NOTA_RT_SAFE {
    if (x <= 1e-15f) return -50.0f;
    uint32_t bits;
    std::memcpy(&bits, &x, sizeof bits);
    const int32_t e = static_cast<int32_t>(bits >> 23) - 127;
    bits = (bits & 0x007FFFFFu) | 0x3F800000u;
    float m;
    std::memcpy(&m, &bits, sizeof m);
    m -= 1.0f;
    const float p = m * (1.44174030f + m * (-0.70777018f + m * (0.41234422f + m * (-0.19031903f + m * 0.04400469f))));
    return static_cast<float>(e) + p;
}

// Decibels to linear gain (10^(db/20)).
NOTA_ALWAYS_INLINE float dbToLin(float db) NOTA_RT_SAFE {
    return exp2f(db * 0.166096404744f);
}

// Linear amplitude to decibels. Returns -301.03 dB for silence.
NOTA_ALWAYS_INLINE float linToDb(float lin) NOTA_RT_SAFE {
    return 6.02059991328f * log2f(lin);
}

// Fast rational tanh(v)/v with monotonic +/-1 saturation for |v| >= 3.
NOTA_ALWAYS_INLINE float tanhRatio(float v) NOTA_RT_SAFE {
    const float a = std::fabs(v);
    if (a >= 3.0f) return 1.0f / a;
    const float v2 = v * v;
    return (27.0f + v2) / (27.0f + 9.0f * v2);
}

// Fast rational tanh(x).
NOTA_ALWAYS_INLINE float tanh(float v) NOTA_RT_SAFE {
    return v * tanhRatio(v);
}

// High-precision tanh via Pade [7/6] (< 0.005% error).
NOTA_ALWAYS_INLINE float tanhPrecise(float x) NOTA_RT_SAFE {
    if (x <= -4.2f) return -1.0f;
    if (x >= 4.2f) return 1.0f;
    const float x2 = x * x;
    const float num = x * (135135.0f + x2 * (17325.0f + x2 * (378.0f + x2)));
    const float den = 135135.0f + x2 * (62370.0f + x2 * (3150.0f + 28.0f * x2));
    return num / den;
}

// Fast branchless sine on normalized phase [0, 1). Max error < 0.0007.
NOTA_ALWAYS_INLINE float sin(float x) NOTA_RT_SAFE {
    const float ph = x - std::floor(x) - 0.5f;
    const float x2 = ph * ph;
    return -ph * (6.27974393f + x2 * (-41.13684264f + x2 * (78.33317205f - 57.13347787f * x2)));
}

// Fast branchless cosine on normalized phase [0, 1).
NOTA_ALWAYS_INLINE float cos(float x) NOTA_RT_SAFE {
    return sin(x + 0.25f);
}

// Pade tan for bilinear prewarp, x in [0, ~1.4].
NOTA_ALWAYS_INLINE float tanPrewarp(float x) NOTA_RT_SAFE {
    x = std::clamp(x, 0.0f, 1.45f);
    const float x2 = x * x;
    return x * (15.0f - x2) / (15.0f - 6.0f * x2);
}

} // namespace fast_math
} // namespace nota
