// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Simd.h — cross-platform SIMD abstraction and DSP acceleration primitives.
// Provides clean gating macros (NOTA_ARCH_ARM, NOTA_ARCH_X86, NOTA_SIMD_NEON,
// NOTA_SIMD_SSE, NOTA_SIMD_AVX, NOTA_SIMD_FMA) and zero-overhead vector math
// utilities for buffer accumulation, scaling, FIR dot products, and peak
// detection.

#pragma once

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>

// --- Architecture & SIMD Instruction Set Gating ------------------------------

#if defined(__aarch64__) || defined(_M_ARM64)
#define NOTA_ARCH_ARM 1
#define NOTA_SIMD_NEON 1
#include <arm_neon.h>
#elif defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
#define NOTA_ARCH_X86 1
#if defined(__SSE2__) || defined(_M_X64) || (defined(_M_IX86_FP) && _M_IX86_FP >= 2)
#define NOTA_SIMD_SSE 1
#endif
#if defined(__AVX__)
#define NOTA_SIMD_AVX 1
#endif
#if defined(__AVX2__)
#define NOTA_SIMD_AVX2 1
#endif
#if defined(__FMA__)
#define NOTA_SIMD_FMA 1
#endif
#include <immintrin.h>
#else
#define NOTA_ARCH_GENERIC 1
#endif

// --- Real-time CPU Pause / Spin-Wait Hint
// -------------------------------------

#if defined(NOTA_ARCH_X86)
#define NOTA_CPU_PAUSE() _mm_pause()
#elif defined(NOTA_ARCH_ARM)
#define NOTA_CPU_PAUSE() __asm__ volatile("isb sy" : : : "memory")
#else
#define NOTA_CPU_PAUSE()                                                                                               \
    do {                                                                                                               \
    } while (0)
#endif

// --- SIMD Vector Math Utilities
// -----------------------------------------------

namespace nota {
namespace simd {

// Vectorized buffer addition: dst[i] += src[i] for count floats
inline void add(float* dst, const float* src, int32_t count) noexcept {
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    for (; i + 16 <= count; i += 16) {
        float32x4_t d0 = vld1q_f32(dst + i);
        float32x4_t d1 = vld1q_f32(dst + i + 4);
        float32x4_t d2 = vld1q_f32(dst + i + 8);
        float32x4_t d3 = vld1q_f32(dst + i + 12);
        const float32x4_t s0 = vld1q_f32(src + i);
        const float32x4_t s1 = vld1q_f32(src + i + 4);
        const float32x4_t s2 = vld1q_f32(src + i + 8);
        const float32x4_t s3 = vld1q_f32(src + i + 12);
        vst1q_f32(dst + i, vaddq_f32(d0, s0));
        vst1q_f32(dst + i + 4, vaddq_f32(d1, s1));
        vst1q_f32(dst + i + 8, vaddq_f32(d2, s2));
        vst1q_f32(dst + i + 12, vaddq_f32(d3, s3));
    }
    for (; i + 4 <= count; i += 4) {
        float32x4_t d = vld1q_f32(dst + i);
        const float32x4_t s = vld1q_f32(src + i);
        vst1q_f32(dst + i, vaddq_f32(d, s));
    }
#elif defined(NOTA_SIMD_SSE)
    for (; i + 16 <= count; i += 16) {
        __m128 d0 = _mm_loadu_ps(dst + i);
        __m128 d1 = _mm_loadu_ps(dst + i + 4);
        __m128 d2 = _mm_loadu_ps(dst + i + 8);
        __m128 d3 = _mm_loadu_ps(dst + i + 12);
        const __m128 s0 = _mm_loadu_ps(src + i);
        const __m128 s1 = _mm_loadu_ps(src + i + 4);
        const __m128 s2 = _mm_loadu_ps(src + i + 8);
        const __m128 s3 = _mm_loadu_ps(src + i + 12);
        _mm_storeu_ps(dst + i, _mm_add_ps(d0, s0));
        _mm_storeu_ps(dst + i + 4, _mm_add_ps(d1, s1));
        _mm_storeu_ps(dst + i + 8, _mm_add_ps(d2, s2));
        _mm_storeu_ps(dst + i + 12, _mm_add_ps(d3, s3));
    }
    for (; i + 4 <= count; i += 4) {
        __m128 d = _mm_loadu_ps(dst + i);
        const __m128 s = _mm_loadu_ps(src + i);
        _mm_storeu_ps(dst + i, _mm_add_ps(d, s));
    }
#endif
    for (; i < count; ++i) { dst[i] += src[i]; }
}

// Vectorized multiply-accumulate: dst[i] += src[i] * scale for count floats
inline void addScaled(float* dst, const float* src, float scale, int32_t count) noexcept {
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    const float32x4_t vScale = vdupq_n_f32(scale);
    for (; i + 16 <= count; i += 16) {
        float32x4_t d0 = vld1q_f32(dst + i);
        float32x4_t d1 = vld1q_f32(dst + i + 4);
        float32x4_t d2 = vld1q_f32(dst + i + 8);
        float32x4_t d3 = vld1q_f32(dst + i + 12);
        const float32x4_t s0 = vld1q_f32(src + i);
        const float32x4_t s1 = vld1q_f32(src + i + 4);
        const float32x4_t s2 = vld1q_f32(src + i + 8);
        const float32x4_t s3 = vld1q_f32(src + i + 12);
        vst1q_f32(dst + i, vmlaq_f32(d0, s0, vScale));
        vst1q_f32(dst + i + 4, vmlaq_f32(d1, s1, vScale));
        vst1q_f32(dst + i + 8, vmlaq_f32(d2, s2, vScale));
        vst1q_f32(dst + i + 12, vmlaq_f32(d3, s3, vScale));
    }
    for (; i + 4 <= count; i += 4) {
        float32x4_t d = vld1q_f32(dst + i);
        const float32x4_t s = vld1q_f32(src + i);
        vst1q_f32(dst + i, vmlaq_f32(d, s, vScale));
    }
#elif defined(NOTA_SIMD_SSE)
    const __m128 vScale = _mm_set1_ps(scale);
    for (; i + 16 <= count; i += 16) {
        __m128 d0 = _mm_loadu_ps(dst + i);
        __m128 d1 = _mm_loadu_ps(dst + i + 4);
        __m128 d2 = _mm_loadu_ps(dst + i + 8);
        __m128 d3 = _mm_loadu_ps(dst + i + 12);
        const __m128 s0 = _mm_loadu_ps(src + i);
        const __m128 s1 = _mm_loadu_ps(src + i + 4);
        const __m128 s2 = _mm_loadu_ps(src + i + 8);
        const __m128 s3 = _mm_loadu_ps(src + i + 12);
#if defined(NOTA_SIMD_FMA)
        _mm_storeu_ps(dst + i, _mm_fmadd_ps(s0, vScale, d0));
        _mm_storeu_ps(dst + i + 4, _mm_fmadd_ps(s1, vScale, d1));
        _mm_storeu_ps(dst + i + 8, _mm_fmadd_ps(s2, vScale, d2));
        _mm_storeu_ps(dst + i + 12, _mm_fmadd_ps(s3, vScale, d3));
#else
        _mm_storeu_ps(dst + i, _mm_add_ps(d0, _mm_mul_ps(s0, vScale)));
        _mm_storeu_ps(dst + i + 4, _mm_add_ps(d1, _mm_mul_ps(s1, vScale)));
        _mm_storeu_ps(dst + i + 8, _mm_add_ps(d2, _mm_mul_ps(s2, vScale)));
        _mm_storeu_ps(dst + i + 12, _mm_add_ps(d3, _mm_mul_ps(s3, vScale)));
#endif
    }
    for (; i + 4 <= count; i += 4) {
        __m128 d = _mm_loadu_ps(dst + i);
        const __m128 s = _mm_loadu_ps(src + i);
#if defined(NOTA_SIMD_FMA)
        _mm_storeu_ps(dst + i, _mm_fmadd_ps(s, vScale, d));
#else
        _mm_storeu_ps(dst + i, _mm_add_ps(d, _mm_mul_ps(s, vScale)));
#endif
    }
#endif
    for (; i < count; ++i) { dst[i] += src[i] * scale; }
}

// Vectorized buffer scaling: dst[i] = src[i] * scale for count floats
inline void scale(float* dst, const float* src, float scale, int32_t count) noexcept {
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    const float32x4_t vScale = vdupq_n_f32(scale);
    for (; i + 16 <= count; i += 16) {
        const float32x4_t s0 = vld1q_f32(src + i);
        const float32x4_t s1 = vld1q_f32(src + i + 4);
        const float32x4_t s2 = vld1q_f32(src + i + 8);
        const float32x4_t s3 = vld1q_f32(src + i + 12);
        vst1q_f32(dst + i, vmulq_f32(s0, vScale));
        vst1q_f32(dst + i + 4, vmulq_f32(s1, vScale));
        vst1q_f32(dst + i + 8, vmulq_f32(s2, vScale));
        vst1q_f32(dst + i + 12, vmulq_f32(s3, vScale));
    }
    for (; i + 4 <= count; i += 4) {
        const float32x4_t s = vld1q_f32(src + i);
        vst1q_f32(dst + i, vmulq_f32(s, vScale));
    }
#elif defined(NOTA_SIMD_SSE)
    const __m128 vScale = _mm_set1_ps(scale);
    for (; i + 16 <= count; i += 16) {
        const __m128 s0 = _mm_loadu_ps(src + i);
        const __m128 s1 = _mm_loadu_ps(src + i + 4);
        const __m128 s2 = _mm_loadu_ps(src + i + 8);
        const __m128 s3 = _mm_loadu_ps(src + i + 12);
        _mm_storeu_ps(dst + i, _mm_mul_ps(s0, vScale));
        _mm_storeu_ps(dst + i + 4, _mm_mul_ps(s1, vScale));
        _mm_storeu_ps(dst + i + 8, _mm_mul_ps(s2, vScale));
        _mm_storeu_ps(dst + i + 12, _mm_mul_ps(s3, vScale));
    }
    for (; i + 4 <= count; i += 4) {
        const __m128 s = _mm_loadu_ps(src + i);
        _mm_storeu_ps(dst + i, _mm_mul_ps(s, vScale));
    }
#endif
    for (; i < count; ++i) { dst[i] = src[i] * scale; }
}

// Vectorized element-wise multiplication: dst[i] = a[i] * b[i]
inline void mul(float* dst, const float* a, const float* b, int32_t count) noexcept {
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    for (; i + 16 <= count; i += 16) {
        const float32x4_t a0 = vld1q_f32(a + i);
        const float32x4_t a1 = vld1q_f32(a + i + 4);
        const float32x4_t a2 = vld1q_f32(a + i + 8);
        const float32x4_t a3 = vld1q_f32(a + i + 12);
        const float32x4_t b0 = vld1q_f32(b + i);
        const float32x4_t b1 = vld1q_f32(b + i + 4);
        const float32x4_t b2 = vld1q_f32(b + i + 8);
        const float32x4_t b3 = vld1q_f32(b + i + 12);
        vst1q_f32(dst + i, vmulq_f32(a0, b0));
        vst1q_f32(dst + i + 4, vmulq_f32(a1, b1));
        vst1q_f32(dst + i + 8, vmulq_f32(a2, b2));
        vst1q_f32(dst + i + 12, vmulq_f32(a3, b3));
    }
    for (; i + 4 <= count; i += 4) {
        const float32x4_t a0 = vld1q_f32(a + i);
        const float32x4_t b0 = vld1q_f32(b + i);
        vst1q_f32(dst + i, vmulq_f32(a0, b0));
    }
#elif defined(NOTA_SIMD_SSE)
    for (; i + 16 <= count; i += 16) {
        const __m128 a0 = _mm_loadu_ps(a + i);
        const __m128 a1 = _mm_loadu_ps(a + i + 4);
        const __m128 a2 = _mm_loadu_ps(a + i + 8);
        const __m128 a3 = _mm_loadu_ps(a + i + 12);
        const __m128 b0 = _mm_loadu_ps(b + i);
        const __m128 b1 = _mm_loadu_ps(b + i + 4);
        const __m128 b2 = _mm_loadu_ps(b + i + 8);
        const __m128 b3 = _mm_loadu_ps(b + i + 12);
        _mm_storeu_ps(dst + i, _mm_mul_ps(a0, b0));
        _mm_storeu_ps(dst + i + 4, _mm_mul_ps(a1, b1));
        _mm_storeu_ps(dst + i + 8, _mm_mul_ps(a2, b2));
        _mm_storeu_ps(dst + i + 12, _mm_mul_ps(a3, b3));
    }
    for (; i + 4 <= count; i += 4) {
        const __m128 a0 = _mm_loadu_ps(a + i);
        const __m128 b0 = _mm_loadu_ps(b + i);
        _mm_storeu_ps(dst + i, _mm_mul_ps(a0, b0));
    }
#endif
    for (; i < count; ++i) { dst[i] = a[i] * b[i]; }
}

// Vectorized 16-element dot product (e.g. 16-tap FIR interpolation)
inline float dot16(const float* a, const float* b) noexcept {
#if defined(NOTA_SIMD_NEON)
    const float32x4_t a0 = vld1q_f32(a);
    const float32x4_t a1 = vld1q_f32(a + 4);
    const float32x4_t a2 = vld1q_f32(a + 8);
    const float32x4_t a3 = vld1q_f32(a + 12);

    const float32x4_t b0 = vld1q_f32(b);
    const float32x4_t b1 = vld1q_f32(b + 4);
    const float32x4_t b2 = vld1q_f32(b + 8);
    const float32x4_t b3 = vld1q_f32(b + 12);

    float32x4_t m = vmulq_f32(a0, b0);
    m = vmlaq_f32(m, a1, b1);
    m = vmlaq_f32(m, a2, b2);
    m = vmlaq_f32(m, a3, b3);

    return vaddvq_f32(m);
#elif defined(NOTA_SIMD_SSE)
    const __m128 a0 = _mm_loadu_ps(a);
    const __m128 a1 = _mm_loadu_ps(a + 4);
    const __m128 a2 = _mm_loadu_ps(a + 8);
    const __m128 a3 = _mm_loadu_ps(a + 12);

    const __m128 b0 = _mm_loadu_ps(b);
    const __m128 b1 = _mm_loadu_ps(b + 4);
    const __m128 b2 = _mm_loadu_ps(b + 8);
    const __m128 b3 = _mm_loadu_ps(b + 12);

#if defined(NOTA_SIMD_FMA)
    __m128 m = _mm_mul_ps(a0, b0);
    m = _mm_fmadd_ps(a1, b1, m);
    m = _mm_fmadd_ps(a2, b2, m);
    m = _mm_fmadd_ps(a3, b3, m);
#else
    const __m128 m0 = _mm_mul_ps(a0, b0);
    const __m128 m1 = _mm_mul_ps(a1, b1);
    const __m128 m2 = _mm_mul_ps(a2, b2);
    const __m128 m3 = _mm_mul_ps(a3, b3);
    const __m128 m = _mm_add_ps(_mm_add_ps(m0, m1), _mm_add_ps(m2, m3));
#endif
    alignas(16) float r[4];
    _mm_store_ps(r, m);
    return r[0] + r[1] + r[2] + r[3];
#else
    float sum = 0.0f;
    for (int i = 0; i < 16; ++i) sum += a[i] * b[i];
    return sum;
#endif
}

// Vectorized peak absolute amplitude finder
inline float maxAbs(const float* src, int32_t count) noexcept {
    int32_t i = 0;
    float peak = 0.0f;
#if defined(NOTA_SIMD_NEON)
    float32x4_t maxv = vdupq_n_f32(0.0f);
    for (; i + 16 <= count; i += 16) {
        const float32x4_t s0 = vabsq_f32(vld1q_f32(src + i));
        const float32x4_t s1 = vabsq_f32(vld1q_f32(src + i + 4));
        const float32x4_t s2 = vabsq_f32(vld1q_f32(src + i + 8));
        const float32x4_t s3 = vabsq_f32(vld1q_f32(src + i + 12));
        maxv = vmaxq_f32(maxv, s0);
        maxv = vmaxq_f32(maxv, s1);
        maxv = vmaxq_f32(maxv, s2);
        maxv = vmaxq_f32(maxv, s3);
    }
    for (; i + 4 <= count; i += 4) {
        const float32x4_t s = vabsq_f32(vld1q_f32(src + i));
        maxv = vmaxq_f32(maxv, s);
    }
    peak = vmaxvq_f32(maxv);
#elif defined(NOTA_SIMD_SSE)
    static const __m128 absMask = _mm_castsi128_ps(_mm_set1_epi32(0x7fffffff));
    __m128 maxv = _mm_setzero_ps();
    for (; i + 16 <= count; i += 16) {
        const __m128 s0 = _mm_and_ps(_mm_loadu_ps(src + i), absMask);
        const __m128 s1 = _mm_and_ps(_mm_loadu_ps(src + i + 4), absMask);
        const __m128 s2 = _mm_and_ps(_mm_loadu_ps(src + i + 8), absMask);
        const __m128 s3 = _mm_and_ps(_mm_loadu_ps(src + i + 12), absMask);
        maxv = _mm_max_ps(maxv, s0);
        maxv = _mm_max_ps(maxv, s1);
        maxv = _mm_max_ps(maxv, s2);
        maxv = _mm_max_ps(maxv, s3);
    }
    for (; i + 4 <= count; i += 4) {
        const __m128 s = _mm_and_ps(_mm_loadu_ps(src + i), absMask);
        maxv = _mm_max_ps(maxv, s);
    }
    maxv = _mm_max_ps(maxv, _mm_movehl_ps(maxv, maxv));
    maxv = _mm_max_ss(maxv, _mm_shuffle_ps(maxv, maxv, 1));
    peak = _mm_cvtss_f32(maxv);
#endif
    for (; i < count; ++i) {
        const float a = std::fabs(src[i]);
        if (a > peak) peak = a;
    }
    return peak;
}

// Vectorized buffer clamping: dst[i] = std::clamp(src[i], minVal, maxVal)
inline void clip(float* dst, const float* src, float minVal, float maxVal, int32_t count) noexcept {
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    const float32x4_t vMin = vdupq_n_f32(minVal);
    const float32x4_t vMax = vdupq_n_f32(maxVal);
    for (; i + 16 <= count; i += 16) {
        float32x4_t s0 = vminq_f32(vmaxq_f32(vld1q_f32(src + i), vMin), vMax);
        float32x4_t s1 = vminq_f32(vmaxq_f32(vld1q_f32(src + i + 4), vMin), vMax);
        float32x4_t s2 = vminq_f32(vmaxq_f32(vld1q_f32(src + i + 8), vMin), vMax);
        float32x4_t s3 = vminq_f32(vmaxq_f32(vld1q_f32(src + i + 12), vMin), vMax);
        vst1q_f32(dst + i, s0);
        vst1q_f32(dst + i + 4, s1);
        vst1q_f32(dst + i + 8, s2);
        vst1q_f32(dst + i + 12, s3);
    }
    for (; i + 4 <= count; i += 4) {
        float32x4_t s = vminq_f32(vmaxq_f32(vld1q_f32(src + i), vMin), vMax);
        vst1q_f32(dst + i, s);
    }
#elif defined(NOTA_SIMD_SSE)
    const __m128 vMin = _mm_set1_ps(minVal);
    const __m128 vMax = _mm_set1_ps(maxVal);
    for (; i + 16 <= count; i += 16) {
        __m128 s0 = _mm_min_ps(_mm_max_ps(_mm_loadu_ps(src + i), vMin), vMax);
        __m128 s1 = _mm_min_ps(_mm_max_ps(_mm_loadu_ps(src + i + 4), vMin), vMax);
        __m128 s2 = _mm_min_ps(_mm_max_ps(_mm_loadu_ps(src + i + 8), vMin), vMax);
        __m128 s3 = _mm_min_ps(_mm_max_ps(_mm_loadu_ps(src + i + 12), vMin), vMax);
        _mm_storeu_ps(dst + i, s0);
        _mm_storeu_ps(dst + i + 4, s1);
        _mm_storeu_ps(dst + i + 8, s2);
        _mm_storeu_ps(dst + i + 12, s3);
    }
    for (; i + 4 <= count; i += 4) {
        __m128 s = _mm_min_ps(_mm_max_ps(_mm_loadu_ps(src + i), vMin), vMax);
        _mm_storeu_ps(dst + i, s);
    }
#endif
    for (; i < count; ++i) { dst[i] = std::clamp(src[i], minVal, maxVal); }
}

// Branchless non-finite float suppression (replaces NaN and +/-Inf with 0.0f)
#if defined(NOTA_SIMD_NEON)
inline float32x4_t squashNonFinite(float32x4_t v) noexcept {
    const uint32x4_t expMask = vdupq_n_u32(0x7f800000);
    const uint32x4_t b = vreinterpretq_u32_f32(v);
    const uint32x4_t bad = vceqq_u32(vandq_u32(b, expMask), expMask);
    return vreinterpretq_f32_u32(vbicq_u32(b, bad));
}
#elif defined(NOTA_SIMD_SSE)
inline __m128 squashNonFinite(__m128 v) noexcept {
    const __m128i expMask = _mm_set1_epi32(0x7f800000);
    const __m128i b = _mm_castps_si128(v);
    const __m128i bad = _mm_cmpeq_epi32(_mm_and_si128(b, expMask), expMask);
    return _mm_castsi128_ps(_mm_andnot_si128(bad, b));
}
#endif
inline float squashNonFinite(float x) noexcept {
    return std::isfinite(x) ? x : 0.0f;
}

// In-place buffer squash of non-finite floats (NaN / +-Inf) to 0. Master-bus safety.
inline void squashNonFinite(float* dst, int32_t count) noexcept {
    if (!dst || count <= 0) return;
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    for (; i + 4 <= count; i += 4) {
        float32x4_t v = vld1q_f32(dst + i);
        vst1q_f32(dst + i, squashNonFinite(v));
    }
#elif defined(NOTA_SIMD_SSE)
    for (; i + 4 <= count; i += 4) {
        __m128 v = _mm_loadu_ps(dst + i);
        _mm_storeu_ps(dst + i, squashNonFinite(v));
    }
#endif
    for (; i < count; ++i) {
        const float x = dst[i];
        if (!std::isfinite(x)) dst[i] = 0.0f;
    }
}

} // namespace simd

// --- Convenient nota namespace aliases
// ----------------------------------------

using simd::add;
using simd::addScaled;
using simd::clip;
using simd::dot16;
using simd::maxAbs;
using simd::mul;
using simd::scale;
using simd::squashNonFinite;

} // namespace nota
