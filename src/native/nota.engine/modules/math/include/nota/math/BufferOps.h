// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// BufferOps — unified 4-lane SIMD + 4x unrolled buffer kernels over `Vec4f`
// (zero per-architecture loop duplication; header-inline for zero DLL call overhead).

#pragma once

#include "nota/math/Export.h"
#include "nota/math/SimdVec.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <span>

namespace nota::math {

NOTA_ALWAYS_INLINE void clear(float* dst, int32_t count) noexcept {
    if (!dst || count <= 0) return;
    const Vec4f z = Vec4f::zero();
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        z.store(dst + i);
        z.store(dst + i + 4);
        z.store(dst + i + 8);
        z.store(dst + i + 12);
    }
    for (; i + 4 <= count; i += 4) z.store(dst + i);
    for (; i < count; ++i) dst[i] = 0.0f;
}

NOTA_ALWAYS_INLINE void fill(float* dst, float value, int32_t count) noexcept {
    if (!dst || count <= 0) return;
    const Vec4f v = Vec4f::splat(value);
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        v.store(dst + i);
        v.store(dst + i + 4);
        v.store(dst + i + 8);
        v.store(dst + i + 12);
    }
    for (; i + 4 <= count; i += 4) v.store(dst + i);
    for (; i < count; ++i) dst[i] = value;
}

NOTA_ALWAYS_INLINE void copy(float* dst, const float* src, int32_t count) noexcept {
    if (!dst || !src || count <= 0 || dst == src) return;
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        Vec4f::load(src + i).store(dst + i);
        Vec4f::load(src + i + 4).store(dst + i + 4);
        Vec4f::load(src + i + 8).store(dst + i + 8);
        Vec4f::load(src + i + 12).store(dst + i + 12);
    }
    for (; i + 4 <= count; i += 4) Vec4f::load(src + i).store(dst + i);
    for (; i < count; ++i) dst[i] = src[i];
}

NOTA_ALWAYS_INLINE void add(float* dst, const float* src, int32_t count) noexcept {
    if (!dst || !src || count <= 0) return;
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        (Vec4f::load(dst + i) + Vec4f::load(src + i)).store(dst + i);
        (Vec4f::load(dst + i + 4) + Vec4f::load(src + i + 4)).store(dst + i + 4);
        (Vec4f::load(dst + i + 8) + Vec4f::load(src + i + 8)).store(dst + i + 8);
        (Vec4f::load(dst + i + 12) + Vec4f::load(src + i + 12)).store(dst + i + 12);
    }
    for (; i + 4 <= count; i += 4) (Vec4f::load(dst + i) + Vec4f::load(src + i)).store(dst + i);
    for (; i < count; ++i) dst[i] += src[i];
}

NOTA_ALWAYS_INLINE void sub(float* dst, const float* src, int32_t count) noexcept {
    if (!dst || !src || count <= 0) return;
    int32_t i = 0;
    for (; i + 4 <= count; i += 4) (Vec4f::load(dst + i) - Vec4f::load(src + i)).store(dst + i);
    for (; i < count; ++i) dst[i] -= src[i];
}

NOTA_ALWAYS_INLINE void addScaled(float* dst, const float* src, float gain, int32_t count) noexcept {
    if (!dst || !src || count <= 0 || gain == 0.0f) return;
    if (gain == 1.0f) {
        add(dst, src, count);
        return;
    }
    const Vec4f g = Vec4f::splat(gain);
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        Vec4f::fmadd(Vec4f::load(src + i), g, Vec4f::load(dst + i)).store(dst + i);
        Vec4f::fmadd(Vec4f::load(src + i + 4), g, Vec4f::load(dst + i + 4)).store(dst + i + 4);
        Vec4f::fmadd(Vec4f::load(src + i + 8), g, Vec4f::load(dst + i + 8)).store(dst + i + 8);
        Vec4f::fmadd(Vec4f::load(src + i + 12), g, Vec4f::load(dst + i + 12)).store(dst + i + 12);
    }
    for (; i + 4 <= count; i += 4) Vec4f::fmadd(Vec4f::load(src + i), g, Vec4f::load(dst + i)).store(dst + i);
    for (; i < count; ++i) dst[i] += src[i] * gain;
}

NOTA_ALWAYS_INLINE void scale(float* dst, const float* src, float gain, int32_t count) noexcept {
    if (!dst || !src || count <= 0) return;
    const Vec4f g = Vec4f::splat(gain);
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        (Vec4f::load(src + i) * g).store(dst + i);
        (Vec4f::load(src + i + 4) * g).store(dst + i + 4);
        (Vec4f::load(src + i + 8) * g).store(dst + i + 8);
        (Vec4f::load(src + i + 12) * g).store(dst + i + 12);
    }
    for (; i + 4 <= count; i += 4) (Vec4f::load(src + i) * g).store(dst + i);
    for (; i < count; ++i) dst[i] = src[i] * gain;
}

NOTA_ALWAYS_INLINE void mul(float* dst, const float* a, const float* b, int32_t count) noexcept {
    if (!dst || !a || !b || count <= 0) return;
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        (Vec4f::load(a + i) * Vec4f::load(b + i)).store(dst + i);
        (Vec4f::load(a + i + 4) * Vec4f::load(b + i + 4)).store(dst + i + 4);
        (Vec4f::load(a + i + 8) * Vec4f::load(b + i + 8)).store(dst + i + 8);
        (Vec4f::load(a + i + 12) * Vec4f::load(b + i + 12)).store(dst + i + 12);
    }
    for (; i + 4 <= count; i += 4) (Vec4f::load(a + i) * Vec4f::load(b + i)).store(dst + i);
    for (; i < count; ++i) dst[i] = a[i] * b[i];
}

NOTA_ALWAYS_INLINE void fma(float* dst, const float* a, const float* b, const float* c, int32_t count) noexcept {
    if (!dst || !a || !b || !c || count <= 0) return;
    int32_t i = 0;
    for (; i + 4 <= count; i += 4)
        Vec4f::fmadd(Vec4f::load(a + i), Vec4f::load(b + i), Vec4f::load(c + i)).store(dst + i);
    for (; i < count; ++i) dst[i] = a[i] * b[i] + c[i];
}

NOTA_ALWAYS_INLINE void clip(float* dst, const float* src, float minVal, float maxVal, int32_t count) noexcept {
    if (!dst || !src || count <= 0) return;
    const Vec4f lo = Vec4f::splat(minVal);
    const Vec4f hi = Vec4f::splat(maxVal);
    int32_t i = 0;
    for (; i + 4 <= count; i += 4) Vec4f::clamp(Vec4f::load(src + i), lo, hi).store(dst + i);
    for (; i < count; ++i) dst[i] = std::clamp(src[i], minVal, maxVal);
}

NOTA_ALWAYS_INLINE float dot16(const float* a, const float* b) noexcept {
    Vec4f s0 = Vec4f::load(a) * Vec4f::load(b);
    Vec4f s1 = Vec4f::load(a + 4) * Vec4f::load(b + 4);
    s0 = Vec4f::fmadd(Vec4f::load(a + 8), Vec4f::load(b + 8), s0);
    s1 = Vec4f::fmadd(Vec4f::load(a + 12), Vec4f::load(b + 12), s1);
    return (s0 + s1).horizontalSum();
}

NOTA_ALWAYS_INLINE float dot(const float* a, const float* b, int32_t count) noexcept {
    if (!a || !b || count <= 0) return 0.0f;
    Vec4f s0 = Vec4f::zero();
    Vec4f s1 = Vec4f::zero();
    int32_t i = 0;
    for (; i + 8 <= count; i += 8) {
        s0 = Vec4f::fmadd(Vec4f::load(a + i), Vec4f::load(b + i), s0);
        s1 = Vec4f::fmadd(Vec4f::load(a + i + 4), Vec4f::load(b + i + 4), s1);
    }
    for (; i + 4 <= count; i += 4) s0 = Vec4f::fmadd(Vec4f::load(a + i), Vec4f::load(b + i), s0);
    float acc = (s0 + s1).horizontalSum();
    for (; i < count; ++i) acc += a[i] * b[i];
    return acc;
}

NOTA_ALWAYS_INLINE float maxAbs(const float* src, int32_t count) noexcept {
    if (!src || count <= 0) return 0.0f;
    Vec4f vmx = Vec4f::zero();
    int32_t i = 0;
    for (; i + 4 <= count; i += 4) vmx = Vec4f::max(vmx, Vec4f::abs(Vec4f::load(src + i)));
    float mx = vmx.horizontalMax();
    for (; i < count; ++i) {
        const float a = std::fabs(src[i]);
        if (a > mx) mx = a;
    }
    return mx;
}

NOTA_ALWAYS_INLINE void peakAndSumSquares(
    const float* src, int32_t count, float& outPeak, double& outSumSquares) noexcept {
    if (!src || count <= 0) {
        outPeak = 0.0f;
        outSumSquares = 0.0;
        return;
    }
    Vec4f vpk = Vec4f::zero();
    Vec4f vsq = Vec4f::zero();
    int32_t i = 0;
    for (; i + 4 <= count; i += 4) {
        const Vec4f v = Vec4f::load(src + i);
        vpk = Vec4f::max(vpk, Vec4f::abs(v));
        vsq = Vec4f::fmadd(v, v, vsq);
    }
    float pk = vpk.horizontalMax();
    double sq = 0.0;
    alignas(16) float sqBuf[4];
    vsq.storeAligned(sqBuf);
    sq = static_cast<double>(sqBuf[0]) + static_cast<double>(sqBuf[1]) + static_cast<double>(sqBuf[2]) +
         static_cast<double>(sqBuf[3]);
    for (; i < count; ++i) {
        const float x = src[i];
        const float a = std::fabs(x);
        if (a > pk) pk = a;
        sq += static_cast<double>(x) * static_cast<double>(x);
    }
    outPeak = pk;
    outSumSquares = sq;
}

NOTA_ALWAYS_INLINE double sumSquares(const float* src, int32_t count) noexcept {
    float unusedPeak = 0.0f;
    double sq = 0.0;
    peakAndSumSquares(src, count, unusedPeak, sq);
    return sq;
}

NOTA_ALWAYS_INLINE void squashNonFinite(float* dst, int32_t count) noexcept {
    if (!dst || count <= 0) return;
    int32_t i = 0;
    for (; i + 4 <= count; i += 4) Vec4f::squashNonFinite(Vec4f::load(dst + i)).store(dst + i);
    for (; i < count; ++i) {
        if (!std::isfinite(dst[i])) dst[i] = 0.0f;
    }
}

NOTA_ALWAYS_INLINE bool isSilent(const float* src, int32_t count, float threshold = 1.0e-5f) noexcept {
    if (!src || count <= 0) return true;
    const Vec4f thr = Vec4f::splat(threshold);
    const Vec4u allOnes = Vec4u::splat(~0u);
    int32_t i = 0;
    for (; i + 4 <= count; i += 4) {
        const Vec4f a = Vec4f::abs(Vec4f::load(src + i));
        const Vec4u ok = Vec4f::lessEqual(a, thr).toUintBits();
        if ((ok ^ allOnes).anyNonZero()) return false;
    }
    for (; i < count; ++i) {
        if (!(std::fabs(src[i]) <= threshold)) return false;
    }
    return true;
}

NOTA_ALWAYS_INLINE void clear(std::span<float> dst) noexcept {
    clear(dst.data(), static_cast<int32_t>(dst.size()));
}
NOTA_ALWAYS_INLINE void fill(std::span<float> dst, float value) noexcept {
    fill(dst.data(), value, static_cast<int32_t>(dst.size()));
}
NOTA_ALWAYS_INLINE void copy(std::span<float> dst, std::span<const float> src) noexcept {
    copy(dst.data(), src.data(), static_cast<int32_t>(std::min(dst.size(), src.size())));
}
NOTA_ALWAYS_INLINE void add(std::span<float> dst, std::span<const float> src) noexcept {
    add(dst.data(), src.data(), static_cast<int32_t>(std::min(dst.size(), src.size())));
}
NOTA_ALWAYS_INLINE void addScaled(std::span<float> dst, std::span<const float> src, float gain) noexcept {
    addScaled(dst.data(), src.data(), gain, static_cast<int32_t>(std::min(dst.size(), src.size())));
}
NOTA_ALWAYS_INLINE void scale(std::span<float> dst, std::span<const float> src, float gain) noexcept {
    scale(dst.data(), src.data(), gain, static_cast<int32_t>(std::min(dst.size(), src.size())));
}
NOTA_ALWAYS_INLINE float maxAbs(std::span<const float> src) noexcept {
    return maxAbs(src.data(), static_cast<int32_t>(src.size()));
}
NOTA_ALWAYS_INLINE bool isSilent(std::span<const float> src, float threshold = 1.0e-5f) noexcept {
    return isSilent(src.data(), static_cast<int32_t>(src.size()), threshold);
}

} // namespace nota::math
