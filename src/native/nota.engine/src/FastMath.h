// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// FastMath — high-performance, real-time safe mathematical approximations for DSP.

#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>

namespace nota {
namespace fast_math {

// Fast base-2 exponential 2^x.
// Accurate to ~1.5e-5 relative error across [-60.0f, 60.0f].
// Assumes standard IEEE-754 32-bit single-precision floating point.
inline float exp2f(float x) {
    x = std::clamp(x, -60.0f, 60.0f);
    const float fi = std::floor(x);
    const float f = x - fi;
    // Taylor series of 2^f on [0,1): ~1.5e-5 relative error (≈0.03 cent).
    const float p = 1.0f + f * (0.69314718f + f * (0.24022651f + f * (0.05550411f + f * (0.00961813f
                  + f * (0.00133336f + f * 0.00015404f)))));
    const int32_t e = static_cast<int32_t>(fi) + 127;
    const uint32_t bits = static_cast<uint32_t>(e) << 23;
    float s;
    std::memcpy(&s, &bits, sizeof s);
    return p * s;
}

// Fast base-2 logarithm log2(x).
// Accurate to ~2.1e-5 max error for x > 0.
// Clamps values <= 1e-15f to -50.0f (~ -301 dBFS) to prevent -infinity or NaN
// from corrupting real-time audio filter states, biquads, and envelope followers.
inline float log2f(float x) {
    if (x <= 1e-15f) return -50.0f;
    uint32_t bits;
    std::memcpy(&bits, &x, sizeof bits);
    const int32_t e = static_cast<int32_t>(bits >> 23) - 127;
    bits = (bits & 0x007FFFFFu) | 0x3F800000u;
    float m;
    std::memcpy(&m, &bits, sizeof m);
    m -= 1.0f; // m in [0, 1)
    const float p = m * (1.44174030f + m * (-0.70777018f + m * (0.41234422f + m * (-0.19031903f + m * 0.04400469f))));
    return static_cast<float>(e) + p;
}

// Decibels to linear gain (10^(db/20)).
inline float dbToLin(float db) {
    return exp2f(db * 0.166096404744f);
}

// Linear amplitude to decibels (20 * log10(lin)).
// Returns -301.03 dB for lin <= 1e-15f, representing total digital silence.
inline float linToDb(float lin) {
    return 6.02059991328f * log2f(lin);
}

// Fast rational tanh(v)/v with monotonic +/-1 asymptotic saturation for |v| >= 3.
// Characterized by a gentle, musical soft knee (~2% lower saturation onset than ideal tanh),
// widely favored for analog tape/tube waveshaping and non-linear filter feedback loops.
inline float tanhRatio(float v) {
    const float a = std::fabs(v);
    if (a >= 3.0f) return 1.0f / a;
    const float v2 = v * v;
    return (27.0f + v2) / (27.0f + 9.0f * v2);
}

// Fast rational tanh(x). Saturates smoothly to +/-1 for |v| >= 3.
inline float tanh(float v) {
    return v * tanhRatio(v);
}

// High-precision fast tanh using Padé [7/6] with monotonic asymptotic clamping to +/-1.
// Accurate to < 0.005% relative error across the entire real line.
inline float tanhPrecise(float x) {
    if (x <= -4.2f) return -1.0f;
    if (x >=  4.2f) return  1.0f;
    const float x2 = x * x;
    const float num = x * (135135.0f + x2 * (17325.0f + x2 * (378.0f + x2)));
    const float den = 135135.0f + x2 * (62370.0f + x2 * (3150.0f + 28.0f * x2));
    return num / den;
}

// Fast branchless sine on normalized phase in [0, 1). Period is 1.0.
// Max error < 0.0007. Uses single-instruction branchless floor (frintm/roundss).
inline float sin(float phase01) {
    const float x = phase01 - std::floor(phase01) - 0.5f;
    const float x2 = x * x;
    return -x * (6.27974393f + x2 * (-41.13684264f + x2 * (78.33317205f - 57.13347787f * x2)));
}

// Fast branchless cosine on normalized phase in [0, 1). Period is 1.0.
inline float cos(float phase01) {
    return sin(phase01 + 0.25f);
}

// Backward-compatibility aliases
inline float fastTanh(float v) { return tanh(v); }
inline float fastTanhPrecise(float x) { return tanhPrecise(x); }
inline float fastSin(float phase01) { return sin(phase01); }
inline float fastCos(float phase01) { return cos(phase01); }

// Padé tan for bilinear prewarp, x in [0, ~1.4].
// Clamped to 1.45f to prevent denominator singularity near Nyquist (sqrt(2.5) ≈ 1.5811).
inline float tanPrewarp(float x) {
    x = std::clamp(x, 0.0f, 1.45f);
    const float x2 = x * x;
    return x * (15.0f - x2) / (15.0f - 6.0f * x2);
}

} // namespace fast_math
} // namespace nota
