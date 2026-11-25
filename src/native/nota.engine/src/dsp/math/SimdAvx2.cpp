// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Runtime-dispatched x86_64 AVX2+FMA vector kernels. Compiled with per-function
// target attributes on GCC/Clang so universal binaries keep baseline SSE2 compatibility.

#include "dsp/math/SimdDispatch.h"

#if defined(__x86_64__) || defined(_M_X64)

#include <algorithm>
#include <cmath>
#include <immintrin.h>

#if defined(__GNUC__) || defined(__clang__)
#define NOTA_TARGET_AVX2_FMA __attribute__((target("avx2,fma")))
#else
#define NOTA_TARGET_AVX2_FMA
#endif

namespace nota {
namespace simd {
namespace avx2 {
namespace {

NOTA_TARGET_AVX2_FMA void addAvx2(float* dst, const float* src, int32_t count) noexcept {
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        const __m256 d0 = _mm256_loadu_ps(dst + i);
        const __m256 d1 = _mm256_loadu_ps(dst + i + 8);
        const __m256 s0 = _mm256_loadu_ps(src + i);
        const __m256 s1 = _mm256_loadu_ps(src + i + 8);
        _mm256_storeu_ps(dst + i, _mm256_add_ps(d0, s0));
        _mm256_storeu_ps(dst + i + 8, _mm256_add_ps(d1, s1));
    }
    for (; i + 8 <= count; i += 8) {
        _mm256_storeu_ps(dst + i, _mm256_add_ps(_mm256_loadu_ps(dst + i), _mm256_loadu_ps(src + i)));
    }
    for (; i < count; ++i) dst[i] += src[i];
}

NOTA_TARGET_AVX2_FMA void addScaledAvx2(float* dst, const float* src, float scale, int32_t count) noexcept {
    const __m256 vs = _mm256_set1_ps(scale);
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        const __m256 d0 = _mm256_loadu_ps(dst + i);
        const __m256 d1 = _mm256_loadu_ps(dst + i + 8);
        const __m256 s0 = _mm256_loadu_ps(src + i);
        const __m256 s1 = _mm256_loadu_ps(src + i + 8);
        _mm256_storeu_ps(dst + i, _mm256_fmadd_ps(s0, vs, d0));
        _mm256_storeu_ps(dst + i + 8, _mm256_fmadd_ps(s1, vs, d1));
    }
    for (; i + 8 <= count; i += 8) {
        _mm256_storeu_ps(dst + i, _mm256_fmadd_ps(_mm256_loadu_ps(src + i), vs, _mm256_loadu_ps(dst + i)));
    }
    for (; i < count; ++i) dst[i] += src[i] * scale;
}

NOTA_TARGET_AVX2_FMA void scaleAvx2(float* dst, const float* src, float scale, int32_t count) noexcept {
    const __m256 vs = _mm256_set1_ps(scale);
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        _mm256_storeu_ps(dst + i, _mm256_mul_ps(_mm256_loadu_ps(src + i), vs));
        _mm256_storeu_ps(dst + i + 8, _mm256_mul_ps(_mm256_loadu_ps(src + i + 8), vs));
    }
    for (; i + 8 <= count; i += 8) {
        _mm256_storeu_ps(dst + i, _mm256_mul_ps(_mm256_loadu_ps(src + i), vs));
    }
    for (; i < count; ++i) dst[i] = src[i] * scale;
}

NOTA_TARGET_AVX2_FMA void mulAvx2(float* dst, const float* a, const float* b, int32_t count) noexcept {
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        _mm256_storeu_ps(dst + i, _mm256_mul_ps(_mm256_loadu_ps(a + i), _mm256_loadu_ps(b + i)));
        _mm256_storeu_ps(dst + i + 8, _mm256_mul_ps(_mm256_loadu_ps(a + i + 8), _mm256_loadu_ps(b + i + 8)));
    }
    for (; i + 8 <= count; i += 8) {
        _mm256_storeu_ps(dst + i, _mm256_mul_ps(_mm256_loadu_ps(a + i), _mm256_loadu_ps(b + i)));
    }
    for (; i < count; ++i) dst[i] = a[i] * b[i];
}

NOTA_TARGET_AVX2_FMA float maxAbsAvx2(const float* src, int32_t count) noexcept {
    const __m256 absMask = _mm256_castsi256_ps(_mm256_set1_epi32(0x7fffffff));
    __m256 mv0 = _mm256_setzero_ps(), mv1 = _mm256_setzero_ps();
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        mv0 = _mm256_max_ps(mv0, _mm256_and_ps(_mm256_loadu_ps(src + i), absMask));
        mv1 = _mm256_max_ps(mv1, _mm256_and_ps(_mm256_loadu_ps(src + i + 8), absMask));
    }
    for (; i + 8 <= count; i += 8) {
        mv0 = _mm256_max_ps(mv0, _mm256_and_ps(_mm256_loadu_ps(src + i), absMask));
    }
    __m256 m = _mm256_max_ps(mv0, mv1);
    __m128 lo = _mm256_castps256_ps128(m);
    __m128 hi = _mm256_extractf128_ps(m, 1);
    __m128 r = _mm_max_ps(lo, hi);
    r = _mm_max_ps(r, _mm_movehl_ps(r, r));
    r = _mm_max_ss(r, _mm_shuffle_ps(r, r, 1));
    float peak = _mm_cvtss_f32(r);
    for (; i < count; ++i) peak = std::max(peak, std::fabs(src[i]));
    return peak;
}

NOTA_TARGET_AVX2_FMA void peakAndSumSquaresAvx2(
    const float* src, int32_t count, float& outPeak, double& outSumSq) noexcept {
    if (!src || count <= 0) {
        outPeak = 0.0f;
        outSumSq = 0.0;
        return;
    }
    const __m256 absMask = _mm256_castsi256_ps(_mm256_set1_epi32(0x7fffffff));
    __m256 mv = _mm256_setzero_ps();
    __m256 sq0 = _mm256_setzero_ps(), sq1 = _mm256_setzero_ps();
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        const __m256 r0 = _mm256_loadu_ps(src + i);
        const __m256 r1 = _mm256_loadu_ps(src + i + 8);
        mv = _mm256_max_ps(mv, _mm256_and_ps(r0, absMask));
        mv = _mm256_max_ps(mv, _mm256_and_ps(r1, absMask));
        sq0 = _mm256_fmadd_ps(r0, r0, sq0);
        sq1 = _mm256_fmadd_ps(r1, r1, sq1);
    }
    for (; i + 8 <= count; i += 8) {
        const __m256 r = _mm256_loadu_ps(src + i);
        mv = _mm256_max_ps(mv, _mm256_and_ps(r, absMask));
        sq0 = _mm256_fmadd_ps(r, r, sq0);
    }
    __m128 mlo = _mm256_castps256_ps128(mv);
    __m128 mhi = _mm256_extractf128_ps(mv, 1);
    __m128 mr = _mm_max_ps(mlo, mhi);
    mr = _mm_max_ps(mr, _mm_movehl_ps(mr, mr));
    mr = _mm_max_ss(mr, _mm_shuffle_ps(mr, mr, 1));
    float peak = _mm_cvtss_f32(mr);

    const __m256 sqv = _mm256_add_ps(sq0, sq1);
    alignas(32) float arr[8]{};
    _mm256_store_ps(arr, sqv);
    double sumSq = 0.0;
    for (float v : arr) sumSq += static_cast<double>(v);
    for (; i < count; ++i) {
        const float v = src[i];
        peak = std::max(peak, std::fabs(v));
        sumSq += static_cast<double>(v) * static_cast<double>(v);
    }
    outPeak = peak;
    outSumSq = sumSq;
}

} // namespace

void bind(DispatchTable& tbl) noexcept {
    tbl.add = &addAvx2;
    tbl.addScaled = &addScaledAvx2;
    tbl.scale = &scaleAvx2;
    tbl.mul = &mulAvx2;
    tbl.maxAbs = &maxAbsAvx2;
    tbl.peakAndSumSquares = &peakAndSumSquaresAvx2;
}

} // namespace avx2
} // namespace simd
} // namespace nota

#endif
