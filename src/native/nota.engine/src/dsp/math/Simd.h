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
#elif defined(_M_ARM64) && defined(_MSC_VER) && !defined(__clang__)
#include <intrin.h>
#define NOTA_CPU_PAUSE() __isb(_ARM64_BARRIER_SY)
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

// Vectorized arbitrary-length dot product (direct-FIR heads, delay interpolation).
inline float dot(const float* a, const float* b, int32_t count) noexcept {
    if (!a || !b || count <= 0) return 0.0f;
    int32_t i = 0;
    float sum = 0.0f;
#if defined(NOTA_SIMD_NEON)
    float32x4_t acc0 = vdupq_n_f32(0.0f), acc1 = vdupq_n_f32(0.0f);
    float32x4_t acc2 = vdupq_n_f32(0.0f), acc3 = vdupq_n_f32(0.0f);
    for (; i + 16 <= count; i += 16) {
        acc0 = vmlaq_f32(acc0, vld1q_f32(a + i), vld1q_f32(b + i));
        acc1 = vmlaq_f32(acc1, vld1q_f32(a + i + 4), vld1q_f32(b + i + 4));
        acc2 = vmlaq_f32(acc2, vld1q_f32(a + i + 8), vld1q_f32(b + i + 8));
        acc3 = vmlaq_f32(acc3, vld1q_f32(a + i + 12), vld1q_f32(b + i + 12));
    }
    for (; i + 4 <= count; i += 4) acc0 = vmlaq_f32(acc0, vld1q_f32(a + i), vld1q_f32(b + i));
    sum = vaddvq_f32(vaddq_f32(vaddq_f32(acc0, acc1), vaddq_f32(acc2, acc3)));
#elif defined(NOTA_SIMD_SSE)
    __m128 acc0 = _mm_setzero_ps(), acc1 = _mm_setzero_ps(), acc2 = _mm_setzero_ps(), acc3 = _mm_setzero_ps();
    for (; i + 16 <= count; i += 16) {
        const __m128 a0 = _mm_loadu_ps(a + i), b0 = _mm_loadu_ps(b + i);
        const __m128 a1 = _mm_loadu_ps(a + i + 4), b1 = _mm_loadu_ps(b + i + 4);
        const __m128 a2 = _mm_loadu_ps(a + i + 8), b2 = _mm_loadu_ps(b + i + 8);
        const __m128 a3 = _mm_loadu_ps(a + i + 12), b3 = _mm_loadu_ps(b + i + 12);
#if defined(NOTA_SIMD_FMA)
        acc0 = _mm_fmadd_ps(a0, b0, acc0);
        acc1 = _mm_fmadd_ps(a1, b1, acc1);
        acc2 = _mm_fmadd_ps(a2, b2, acc2);
        acc3 = _mm_fmadd_ps(a3, b3, acc3);
#else
        acc0 = _mm_add_ps(acc0, _mm_mul_ps(a0, b0));
        acc1 = _mm_add_ps(acc1, _mm_mul_ps(a1, b1));
        acc2 = _mm_add_ps(acc2, _mm_mul_ps(a2, b2));
        acc3 = _mm_add_ps(acc3, _mm_mul_ps(a3, b3));
#endif
    }
    for (; i + 4 <= count; i += 4) {
        const __m128 a0 = _mm_loadu_ps(a + i), b0 = _mm_loadu_ps(b + i);
#if defined(NOTA_SIMD_FMA)
        acc0 = _mm_fmadd_ps(a0, b0, acc0);
#else
        acc0 = _mm_add_ps(acc0, _mm_mul_ps(a0, b0));
#endif
    }
    alignas(16) float r[4];
    _mm_store_ps(r, _mm_add_ps(_mm_add_ps(acc0, acc1), _mm_add_ps(acc2, acc3)));
    sum = r[0] + r[1] + r[2] + r[3];
#endif
    for (; i < count; ++i) sum += a[i] * b[i];
    return sum;
}

// Vectorized peak absolute amplitude finder
inline float maxAbs(const float* src, int32_t count) noexcept {
    if (!src || count <= 0) return 0.0f;
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

// Single-pass peak absolute amplitude finder and sum-of-squares accumulator.
inline void peakAndSumSquares(const float* src, int32_t count, float& outPeak, double& outSumSquares) noexcept {
    if (!src || count <= 0) {
        outPeak = 0.0f;
        outSumSquares = 0.0;
        return;
    }
    int32_t i = 0;
    float peak = 0.0f;
    double sumSq = 0.0;
#if defined(NOTA_SIMD_NEON)
    float32x4_t maxv = vdupq_n_f32(0.0f);
    float32x4_t sq0 = vdupq_n_f32(0.0f), sq1 = vdupq_n_f32(0.0f), sq2 = vdupq_n_f32(0.0f), sq3 = vdupq_n_f32(0.0f);
    for (; i + 16 <= count; i += 16) {
        const float32x4_t r0 = vld1q_f32(src + i);
        const float32x4_t r1 = vld1q_f32(src + i + 4);
        const float32x4_t r2 = vld1q_f32(src + i + 8);
        const float32x4_t r3 = vld1q_f32(src + i + 12);
        maxv = vmaxq_f32(maxv, vabsq_f32(r0));
        maxv = vmaxq_f32(maxv, vabsq_f32(r1));
        maxv = vmaxq_f32(maxv, vabsq_f32(r2));
        maxv = vmaxq_f32(maxv, vabsq_f32(r3));
        sq0 = vmlaq_f32(sq0, r0, r0);
        sq1 = vmlaq_f32(sq1, r1, r1);
        sq2 = vmlaq_f32(sq2, r2, r2);
        sq3 = vmlaq_f32(sq3, r3, r3);
    }
    for (; i + 4 <= count; i += 4) {
        const float32x4_t r = vld1q_f32(src + i);
        maxv = vmaxq_f32(maxv, vabsq_f32(r));
        sq0 = vmlaq_f32(sq0, r, r);
    }
    peak = vmaxvq_f32(maxv);
    sumSq = static_cast<double>(vaddvq_f32(vaddq_f32(vaddq_f32(sq0, sq1), vaddq_f32(sq2, sq3))));
#elif defined(NOTA_SIMD_SSE)
    static const __m128 absMask = _mm_castsi128_ps(_mm_set1_epi32(0x7fffffff));
    __m128 maxv = _mm_setzero_ps();
    __m128 sq0 = _mm_setzero_ps(), sq1 = _mm_setzero_ps(), sq2 = _mm_setzero_ps(), sq3 = _mm_setzero_ps();
    for (; i + 16 <= count; i += 16) {
        const __m128 r0 = _mm_loadu_ps(src + i);
        const __m128 r1 = _mm_loadu_ps(src + i + 4);
        const __m128 r2 = _mm_loadu_ps(src + i + 8);
        const __m128 r3 = _mm_loadu_ps(src + i + 12);
        maxv = _mm_max_ps(maxv, _mm_and_ps(r0, absMask));
        maxv = _mm_max_ps(maxv, _mm_and_ps(r1, absMask));
        maxv = _mm_max_ps(maxv, _mm_and_ps(r2, absMask));
        maxv = _mm_max_ps(maxv, _mm_and_ps(r3, absMask));
#if defined(NOTA_SIMD_FMA)
        sq0 = _mm_fmadd_ps(r0, r0, sq0);
        sq1 = _mm_fmadd_ps(r1, r1, sq1);
        sq2 = _mm_fmadd_ps(r2, r2, sq2);
        sq3 = _mm_fmadd_ps(r3, r3, sq3);
#else
        sq0 = _mm_add_ps(sq0, _mm_mul_ps(r0, r0));
        sq1 = _mm_add_ps(sq1, _mm_mul_ps(r1, r1));
        sq2 = _mm_add_ps(sq2, _mm_mul_ps(r2, r2));
        sq3 = _mm_add_ps(sq3, _mm_mul_ps(r3, r3));
#endif
    }
    for (; i + 4 <= count; i += 4) {
        const __m128 r = _mm_loadu_ps(src + i);
        maxv = _mm_max_ps(maxv, _mm_and_ps(r, absMask));
#if defined(NOTA_SIMD_FMA)
        sq0 = _mm_fmadd_ps(r, r, sq0);
#else
        sq0 = _mm_add_ps(sq0, _mm_mul_ps(r, r));
#endif
    }
    maxv = _mm_max_ps(maxv, _mm_movehl_ps(maxv, maxv));
    maxv = _mm_max_ss(maxv, _mm_shuffle_ps(maxv, maxv, 1));
    peak = _mm_cvtss_f32(maxv);
    alignas(16) float sqArr[4];
    _mm_store_ps(sqArr, _mm_add_ps(_mm_add_ps(sq0, sq1), _mm_add_ps(sq2, sq3)));
    sumSq = static_cast<double>(sqArr[0] + sqArr[1] + sqArr[2] + sqArr[3]);
#endif
    for (; i < count; ++i) {
        const float x = src[i];
        const float ax = std::fabs(x);
        if (ax > peak) peak = ax;
        sumSq += static_cast<double>(x) * static_cast<double>(x);
    }
    outPeak = peak;
    outSumSquares = sumSq;
}

// Vectorized complex multiply-accumulate:
// ar[i] += xr[i] * hr[i] - xi[i] * hi[i]
// ai[i] += xr[i] * hi[i] + xi[i] * hr[i]
inline void complexMulAdd(float* __restrict ar, float* __restrict ai, const float* __restrict xr,
    const float* __restrict xi, const float* __restrict hr, const float* __restrict hi, int32_t count) noexcept {
    if (!ar || !ai || !xr || !xi || !hr || !hi || count <= 0) return;
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    for (; i + 8 <= count; i += 8) {
        const float32x4_t ar0 = vld1q_f32(ar + i), ar1 = vld1q_f32(ar + i + 4);
        const float32x4_t ai0 = vld1q_f32(ai + i), ai1 = vld1q_f32(ai + i + 4);
        const float32x4_t xr0 = vld1q_f32(xr + i), xr1 = vld1q_f32(xr + i + 4);
        const float32x4_t xi0 = vld1q_f32(xi + i), xi1 = vld1q_f32(xi + i + 4);
        const float32x4_t hr0 = vld1q_f32(hr + i), hr1 = vld1q_f32(hr + i + 4);
        const float32x4_t hi0 = vld1q_f32(hi + i), hi1 = vld1q_f32(hi + i + 4);

        vst1q_f32(ar + i, vfmaq_f32(vfmsq_f32(ar0, xi0, hi0), xr0, hr0));
        vst1q_f32(ar + i + 4, vfmaq_f32(vfmsq_f32(ar1, xi1, hi1), xr1, hr1));
        vst1q_f32(ai + i, vfmaq_f32(vfmaq_f32(ai0, xr0, hi0), xi0, hr0));
        vst1q_f32(ai + i + 4, vfmaq_f32(vfmaq_f32(ai1, xr1, hi1), xi1, hr1));
    }
    for (; i + 4 <= count; i += 4) {
        const float32x4_t ar0 = vld1q_f32(ar + i), ai0 = vld1q_f32(ai + i);
        const float32x4_t xr0 = vld1q_f32(xr + i), xi0 = vld1q_f32(xi + i);
        const float32x4_t hr0 = vld1q_f32(hr + i), hi0 = vld1q_f32(hi + i);
        vst1q_f32(ar + i, vfmaq_f32(vfmsq_f32(ar0, xi0, hi0), xr0, hr0));
        vst1q_f32(ai + i, vfmaq_f32(vfmaq_f32(ai0, xr0, hi0), xi0, hr0));
    }
#elif defined(NOTA_SIMD_SSE)
    for (; i + 4 <= count; i += 4) {
        const __m128 ar0 = _mm_loadu_ps(ar + i), ai0 = _mm_loadu_ps(ai + i);
        const __m128 xr0 = _mm_loadu_ps(xr + i), xi0 = _mm_loadu_ps(xi + i);
        const __m128 hr0 = _mm_loadu_ps(hr + i), hi0 = _mm_loadu_ps(hi + i);
#if defined(NOTA_SIMD_FMA)
        _mm_storeu_ps(ar + i, _mm_fmadd_ps(xr0, hr0, _mm_fnmadd_ps(xi0, hi0, ar0)));
        _mm_storeu_ps(ai + i, _mm_fmadd_ps(xi0, hr0, _mm_fmadd_ps(xr0, hi0, ai0)));
#else
        const __m128 pr = _mm_sub_ps(_mm_mul_ps(xr0, hr0), _mm_mul_ps(xi0, hi0));
        const __m128 pi = _mm_add_ps(_mm_mul_ps(xr0, hi0), _mm_mul_ps(xi0, hr0));
        _mm_storeu_ps(ar + i, _mm_add_ps(ar0, pr));
        _mm_storeu_ps(ai + i, _mm_add_ps(ai0, pi));
#endif
    }
#endif
    for (; i < count; ++i) {
        ar[i] += xr[i] * hr[i] - xi[i] * hi[i];
        ai[i] += xr[i] * hi[i] + xi[i] * hr[i];
    }
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
using simd::complexMulAdd;
using simd::dot;
using simd::dot16;
using simd::maxAbs;
using simd::mul;
using simd::peakAndSumSquares;
using simd::scale;
using simd::squashNonFinite;

} // namespace nota
