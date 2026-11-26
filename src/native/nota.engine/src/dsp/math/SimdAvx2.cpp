// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Runtime-dispatched x86_64 AVX2+FMA kernels for convolution complex MAC and large-buffer
// peak/RMS analysis. Uses per-function target attributes on Clang/GCC and per-TU /arch:AVX2
// on MSVC — keep zero global C++ initializers in this TU so MSVC never emits AVX2 before
// SimdKernels::active() verifies hardware + XCR0 support.

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

NOTA_TARGET_AVX2_FMA inline float hmax256(__m256 v) noexcept {
    const __m128 lo = _mm256_castps256_ps128(v);
    const __m128 hi = _mm256_extractf128_ps(v, 1);
    __m128 r = _mm_max_ps(lo, hi);
    r = _mm_max_ps(r, _mm_movehl_ps(r, r));
    r = _mm_max_ss(r, _mm_shuffle_ps(r, r, 1));
    return _mm_cvtss_f32(r);
}

NOTA_TARGET_AVX2_FMA void convCmacAvx2(float* __restrict ar, float* __restrict ai, const float* __restrict xr,
    const float* __restrict xi, const float* __restrict hr, const float* __restrict hi, int32_t bins) noexcept {
    if (!ar || !ai || !xr || !xi || !hr || !hi || bins <= 0) return;
    ar[0] += xr[0] * hr[0]; // bin 0 packs DC (re) and Nyquist (im): two independent real products
    ai[0] += xi[0] * hi[0];

    int32_t i = 1;
    for (; i + 16 <= bins; i += 16) {
        const __m256 ar0 = _mm256_loadu_ps(ar + i), ar1 = _mm256_loadu_ps(ar + i + 8);
        const __m256 ai0 = _mm256_loadu_ps(ai + i), ai1 = _mm256_loadu_ps(ai + i + 8);
        const __m256 xr0 = _mm256_loadu_ps(xr + i), xr1 = _mm256_loadu_ps(xr + i + 8);
        const __m256 xi0 = _mm256_loadu_ps(xi + i), xi1 = _mm256_loadu_ps(xi + i + 8);
        const __m256 hr0 = _mm256_loadu_ps(hr + i), hr1 = _mm256_loadu_ps(hr + i + 8);
        const __m256 hi0 = _mm256_loadu_ps(hi + i), hi1 = _mm256_loadu_ps(hi + i + 8);

        // Real: ar + xr*hr - xi*hi = fmadd(xr, hr, fnmadd(xi, hi, ar))
        _mm256_storeu_ps(ar + i, _mm256_fmadd_ps(xr0, hr0, _mm256_fnmadd_ps(xi0, hi0, ar0)));
        _mm256_storeu_ps(ar + i + 8, _mm256_fmadd_ps(xr1, hr1, _mm256_fnmadd_ps(xi1, hi1, ar1)));
        // Imag: ai + xr*hi + xi*hr = fmadd(xi, hr, fmadd(xr, hi, ai))
        _mm256_storeu_ps(ai + i, _mm256_fmadd_ps(xi0, hr0, _mm256_fmadd_ps(xr0, hi0, ai0)));
        _mm256_storeu_ps(ai + i + 8, _mm256_fmadd_ps(xi1, hr1, _mm256_fmadd_ps(xr1, hi1, ai1)));
    }
    for (; i + 8 <= bins; i += 8) {
        const __m256 ar0 = _mm256_loadu_ps(ar + i), ai0 = _mm256_loadu_ps(ai + i);
        const __m256 xr0 = _mm256_loadu_ps(xr + i), xi0 = _mm256_loadu_ps(xi + i);
        const __m256 hr0 = _mm256_loadu_ps(hr + i), hi0 = _mm256_loadu_ps(hi + i);
        _mm256_storeu_ps(ar + i, _mm256_fmadd_ps(xr0, hr0, _mm256_fnmadd_ps(xi0, hi0, ar0)));
        _mm256_storeu_ps(ai + i, _mm256_fmadd_ps(xi0, hr0, _mm256_fmadd_ps(xr0, hi0, ai0)));
    }
    for (; i < bins; ++i) {
        ar[i] += xr[i] * hr[i] - xi[i] * hi[i];
        ai[i] += xr[i] * hi[i] + xi[i] * hr[i];
    }
}

NOTA_TARGET_AVX2_FMA float peakAbsAvx2(const float* src, int32_t count) noexcept {
    if (!src || count <= 0) return 0.0f;
    const __m256 absMask = _mm256_castsi256_ps(_mm256_set1_epi32(0x7fffffff));
    __m256 mv0 = _mm256_setzero_ps(), mv1 = _mm256_setzero_ps();
    int32_t i = 0;
    for (; i + 16 <= count; i += 16) {
        mv0 = _mm256_max_ps(mv0, _mm256_and_ps(_mm256_loadu_ps(src + i), absMask));
        mv1 = _mm256_max_ps(mv1, _mm256_and_ps(_mm256_loadu_ps(src + i + 8), absMask));
    }
    for (; i + 8 <= count; i += 8) mv0 = _mm256_max_ps(mv0, _mm256_and_ps(_mm256_loadu_ps(src + i), absMask));

    float peak = hmax256(_mm256_max_ps(mv0, mv1));
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
    float peak = hmax256(mv);

    alignas(32) float arr[8]{};
    _mm256_store_ps(arr, _mm256_add_ps(sq0, sq1));
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

void bind(SimdKernels& k) noexcept {
    k.isa = SimdIsa::Avx2Fma;
    k.name = "avx2+fma";
    k.convCmac = &convCmacAvx2;
    k.peakAbs = &peakAbsAvx2;
    k.peakAndSumSquares = &peakAndSumSquaresAvx2;
}

} // namespace avx2
} // namespace simd
} // namespace nota

#endif
