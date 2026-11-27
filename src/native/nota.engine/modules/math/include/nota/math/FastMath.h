// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// FastMath — scalar & 4-lane SIMD (`Vec4f`) real-time safe DSP approximations (AR-6 safe).

#pragma once

#include "nota/math/Export.h"
#include "nota/math/SimdVec.h"

#include <algorithm>
#include <bit>
#include <cmath>
#include <cstdint>

namespace nota::math {

// --- Scalar FastMath (exact parity with legacy nota::fast_math) ---

NOTA_ALWAYS_INLINE float exp2f(float x) NOTA_RT_SAFE {
    x = std::clamp(x, -60.0f, 60.0f);
    const float fi = std::floor(x);
    const float f = x - fi;
    const float p =
        1.0f + f * (0.69314718f +
                       f * (0.24022651f + f * (0.05550411f + f * (0.00961813f + f * (0.00133336f + f * 0.00015404f)))));
    const int32_t e = static_cast<int32_t>(fi) + 127;
    const uint32_t bits = static_cast<uint32_t>(e) << 23;
    return p * std::bit_cast<float>(bits);
}

NOTA_ALWAYS_INLINE float log2f(float x) NOTA_RT_SAFE {
    if (x <= 1e-15f) return -50.0f;
    uint32_t bits = std::bit_cast<uint32_t>(x);
    const int32_t e = static_cast<int32_t>(bits >> 23) - 127;
    bits = (bits & 0x007FFFFFu) | 0x3F800000u;
    const float m = std::bit_cast<float>(bits) - 1.0f;
    const float p = m * (1.44174030f + m * (-0.70777018f + m * (0.41234422f + m * (-0.19031903f + m * 0.04400469f))));
    return static_cast<float>(e) + p;
}

NOTA_ALWAYS_INLINE float dbToLin(float db) NOTA_RT_SAFE {
    return exp2f(db * 0.166096404744f);
}
NOTA_ALWAYS_INLINE float linToDb(float lin) NOTA_RT_SAFE {
    return 6.02059991328f * log2f(lin);
}

NOTA_ALWAYS_INLINE float tanhRatio(float v) NOTA_RT_SAFE {
    const float a = std::fabs(v);
    if (a >= 3.0f) return 1.0f / a;
    const float v2 = v * v;
    return (27.0f + v2) / (27.0f + 9.0f * v2);
}

NOTA_ALWAYS_INLINE float tanh(float v) NOTA_RT_SAFE {
    return v * tanhRatio(v);
}

NOTA_ALWAYS_INLINE float tanhPrecise(float x) NOTA_RT_SAFE {
    if (x <= -4.2f) return -1.0f;
    if (x >= 4.2f) return 1.0f;
    const float x2 = x * x;
    const float num = x * (135135.0f + x2 * (17325.0f + x2 * (378.0f + x2)));
    const float den = 135135.0f + x2 * (62370.0f + x2 * (3150.0f + 28.0f * x2));
    return num / den;
}

NOTA_ALWAYS_INLINE float sinPhase(float x) NOTA_RT_SAFE {
    const float ph = x - std::floor(x) - 0.5f;
    const float x2 = ph * ph;
    return -ph * (6.27974393f + x2 * (-41.13684264f + x2 * (78.33317205f - 57.13347787f * x2)));
}

NOTA_ALWAYS_INLINE float cosPhase(float x) NOTA_RT_SAFE {
    return sinPhase(x + 0.25f);
}

NOTA_ALWAYS_INLINE float tanPrewarp(float x) NOTA_RT_SAFE {
    x = std::clamp(x, 0.0f, 1.45f);
    const float x2 = x * x;
    return x * (15.0f - x2) / (15.0f - 6.0f * x2);
}

// --- 4-lane SIMD Vec4f FastMath ---

NOTA_ALWAYS_INLINE Vec4f exp2(Vec4f x) NOTA_RT_SAFE {
    x = Vec4f::clamp(x, Vec4f::splat(-60.0f), Vec4f::splat(60.0f));
    const Vec4f fi = Vec4f::floor(x);
    const Vec4f f = x - fi;
    Vec4f p = Vec4f::fmadd(f, Vec4f::splat(0.00015404f), Vec4f::splat(0.00133336f));
    p = Vec4f::fmadd(f, p, Vec4f::splat(0.00961813f));
    p = Vec4f::fmadd(f, p, Vec4f::splat(0.05550411f));
    p = Vec4f::fmadd(f, p, Vec4f::splat(0.24022651f));
    p = Vec4f::fmadd(f, p, Vec4f::splat(0.69314718f));
    p = Vec4f::fmadd(f, p, Vec4f::splat(1.0f));
    const Vec4i e = fi.truncateToInt() + Vec4i::splat(127);
    const Vec4f s = Vec4f::fromIntBits(Vec4i::shiftLeft<23>(e));
    return p * s;
}

NOTA_ALWAYS_INLINE Vec4f log2(Vec4f x) NOTA_RT_SAFE {
    const Vec4f floorVal = Vec4f::splat(-50.0f);
    const Vec4f tooSmall = Vec4f::lessThan(x, Vec4f::splat(1e-15f));
    const Vec4f safeX = Vec4f::select(tooSmall, Vec4f::splat(1.0f), x);

    const Vec4u rawBits = safeX.toUintBits();
    const Vec4i e = Vec4i{Vec4u::shiftRight<23>(rawBits).raw} - Vec4i::splat(127);
    const Vec4u mantBits = (rawBits & Vec4u::splat(0x007FFFFFu)) | Vec4u::splat(0x3F800000u);
    const Vec4f m = Vec4f::fromUintBits(mantBits) - Vec4f::splat(1.0f);

    Vec4f p = Vec4f::fmadd(m, Vec4f::splat(0.04400469f), Vec4f::splat(-0.19031903f));
    p = Vec4f::fmadd(m, p, Vec4f::splat(0.41234422f));
    p = Vec4f::fmadd(m, p, Vec4f::splat(-0.70777018f));
    p = Vec4f::fmadd(m, p, Vec4f::splat(1.44174030f));
    const Vec4f res = e.toFloat() + m * p;
    return Vec4f::select(tooSmall, floorVal, res);
}

NOTA_ALWAYS_INLINE Vec4f dbToLin(Vec4f db) NOTA_RT_SAFE {
    return exp2(db * Vec4f::splat(0.166096404744f));
}
NOTA_ALWAYS_INLINE Vec4f linToDb(Vec4f lin) NOTA_RT_SAFE {
    return Vec4f::splat(6.02059991328f) * log2(lin);
}

NOTA_ALWAYS_INLINE Vec4f tanh(Vec4f v) NOTA_RT_SAFE {
    const Vec4f a = Vec4f::abs(v);
    const Vec4f sat = Vec4f::greaterEqual(a, Vec4f::splat(3.0f));
    const Vec4f v2 = v * v;
    const Vec4f rational = v * (Vec4f::splat(27.0f) + v2) / Vec4f::fmadd(Vec4f::splat(9.0f), v2, Vec4f::splat(27.0f));
    const Vec4f clamped = Vec4f::select(Vec4f::lessThan(v, Vec4f::zero()), Vec4f::splat(-1.0f), Vec4f::splat(1.0f));
    return Vec4f::select(sat, clamped, rational);
}

NOTA_ALWAYS_INLINE Vec4f tanhPrecise(Vec4f x) NOTA_RT_SAFE {
    const Vec4f a = Vec4f::abs(x);
    const Vec4f sat = Vec4f::greaterEqual(a, Vec4f::splat(4.2f));
    const Vec4f x2 = x * x;
    const Vec4f num = x * (Vec4f::splat(135135.0f) + x2 * (Vec4f::splat(17325.0f) + x2 * (Vec4f::splat(378.0f) + x2)));
    const Vec4f den = Vec4f::splat(135135.0f) +
                      x2 * (Vec4f::splat(62370.0f) + x2 * Vec4f::fmadd(Vec4f::splat(28.0f), x2, Vec4f::splat(3150.0f)));
    const Vec4f clamped = Vec4f::select(Vec4f::lessThan(x, Vec4f::zero()), Vec4f::splat(-1.0f), Vec4f::splat(1.0f));
    return Vec4f::select(sat, clamped, num / den);
}

NOTA_ALWAYS_INLINE Vec4f sinPhase(Vec4f x) NOTA_RT_SAFE {
    const Vec4f ph = x - Vec4f::floor(x) - Vec4f::splat(0.5f);
    const Vec4f x2 = ph * ph;
    Vec4f p = Vec4f::fmadd(x2, Vec4f::splat(-57.13347787f), Vec4f::splat(78.33317205f));
    p = Vec4f::fmadd(x2, p, Vec4f::splat(-41.13684264f));
    p = Vec4f::fmadd(x2, p, Vec4f::splat(6.27974393f));
    return -ph * p;
}

NOTA_ALWAYS_INLINE Vec4f cosPhase(Vec4f x) NOTA_RT_SAFE {
    return sinPhase(x + Vec4f::splat(0.25f));
}

NOTA_ALWAYS_INLINE Vec4f tanPrewarp(Vec4f x) NOTA_RT_SAFE {
    x = Vec4f::clamp(x, Vec4f::zero(), Vec4f::splat(1.45f));
    const Vec4f x2 = x * x;
    return x * (Vec4f::splat(15.0f) - x2) / Vec4f::fnmadd(Vec4f::splat(6.0f), x2, Vec4f::splat(15.0f));
}

} // namespace nota::math
