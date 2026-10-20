// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SimdAudio.h — vectorized audio DSP operations and telemetry primitives.
// Provides interleaved stereo scaling, bus summing, silence detection,
// peak & sum-of-squares calculation, and real-time stereo meter accumulation.

#pragma once

#include "dsp/math/Simd.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace nota {
namespace simd_audio {

// Fast, early-exiting silence detection for flat or interleaved float audio buffers.
// Returns true if all samples have an absolute amplitude <= threshold (default 1e-5f ≈ -100 dBFS).
inline bool isSilent(const float* src, int32_t count, float threshold = 1.0e-5f) noexcept {
    if (!src || count <= 0) return true;
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    const float32x4_t vThresh = vdupq_n_f32(threshold);
    for (; i + 16 <= count; i += 16) {
        const float32x4_t s0 = vabsq_f32(vld1q_f32(src + i));
        const float32x4_t s1 = vabsq_f32(vld1q_f32(src + i + 4));
        const float32x4_t s2 = vabsq_f32(vld1q_f32(src + i + 8));
        const float32x4_t s3 = vabsq_f32(vld1q_f32(src + i + 12));
        const uint32x4_t m0 = vcgtq_f32(s0, vThresh);
        const uint32x4_t m1 = vcgtq_f32(s1, vThresh);
        const uint32x4_t m2 = vcgtq_f32(s2, vThresh);
        const uint32x4_t m3 = vcgtq_f32(s3, vThresh);
        const uint32x4_t or01 = vorrq_u32(m0, m1);
        const uint32x4_t or23 = vorrq_u32(m2, m3);
        const uint32x4_t combined = vorrq_u32(or01, or23);
        if (vmaxvq_u32(combined) != 0) return false;
    }
    for (; i + 4 <= count; i += 4) {
        const float32x4_t s = vabsq_f32(vld1q_f32(src + i));
        const uint32x4_t m = vcgtq_f32(s, vThresh);
        if (vmaxvq_u32(m) != 0) return false;
    }
#elif defined(NOTA_SIMD_SSE)
    static const __m128 absMask = _mm_castsi128_ps(_mm_set1_epi32(0x7fffffff));
    const __m128 vThresh = _mm_set1_ps(threshold);
    for (; i + 16 <= count; i += 16) {
        const __m128 s0 = _mm_and_ps(_mm_loadu_ps(src + i), absMask);
        const __m128 s1 = _mm_and_ps(_mm_loadu_ps(src + i + 4), absMask);
        const __m128 s2 = _mm_and_ps(_mm_loadu_ps(src + i + 8), absMask);
        const __m128 s3 = _mm_and_ps(_mm_loadu_ps(src + i + 12), absMask);
        const __m128 m0 = _mm_cmpgt_ps(s0, vThresh);
        const __m128 m1 = _mm_cmpgt_ps(s1, vThresh);
        const __m128 m2 = _mm_cmpgt_ps(s2, vThresh);
        const __m128 m3 = _mm_cmpgt_ps(s3, vThresh);
        const __m128 comb = _mm_or_ps(_mm_or_ps(m0, m1), _mm_or_ps(m2, m3));
        if (_mm_movemask_ps(comb) != 0) return false;
    }
    for (; i + 4 <= count; i += 4) {
        const __m128 s = _mm_and_ps(_mm_loadu_ps(src + i), absMask);
        const __m128 m = _mm_cmpgt_ps(s, vThresh);
        if (_mm_movemask_ps(m) != 0) return false;
    }
#endif
    for (; i < count; ++i) {
        if (std::fabs(src[i]) > threshold) return false;
    }
    return true;
}

// Single-pass peak absolute amplitude finder and sum-of-squares accumulator for float audio buffers.
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
    float32x4_t sq0 = vdupq_n_f32(0.0f);
    float32x4_t sq1 = vdupq_n_f32(0.0f);
    float32x4_t sq2 = vdupq_n_f32(0.0f);
    float32x4_t sq3 = vdupq_n_f32(0.0f);
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
    const float32x4_t sqSum = vaddq_f32(vaddq_f32(sq0, sq1), vaddq_f32(sq2, sq3));
    sumSq = static_cast<double>(vaddvq_f32(sqSum));
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
    const __m128 sqSum = _mm_add_ps(_mm_add_ps(sq0, sq1), _mm_add_ps(sq2, sq3));
    alignas(16) float sqArr[4];
    _mm_store_ps(sqArr, sqSum);
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

// Vectorized stereo buffer scaling: dst[i*2] = src[i*2] * gl, dst[i*2+1] = src[i*2+1] * gr
inline void scaleStereo(float* dst, const float* src, float gl, float gr, int32_t frames) noexcept {
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    const float32x4_t vgains = {gl, gr, gl, gr};
    for (; i + 4 <= frames; i += 4) {
        float32x4_t s0 = vmulq_f32(vld1q_f32(src + i * 2), vgains);
        float32x4_t s1 = vmulq_f32(vld1q_f32(src + i * 2 + 4), vgains);
        vst1q_f32(dst + i * 2, s0);
        vst1q_f32(dst + i * 2 + 4, s1);
    }
    for (; i + 2 <= frames; i += 2) {
        float32x4_t s0 = vmulq_f32(vld1q_f32(src + i * 2), vgains);
        vst1q_f32(dst + i * 2, s0);
    }
#elif defined(NOTA_SIMD_SSE)
    const __m128 vgains = _mm_set_ps(gr, gl, gr, gl);
    for (; i + 4 <= frames; i += 4) {
        __m128 s0 = _mm_mul_ps(_mm_loadu_ps(src + i * 2), vgains);
        __m128 s1 = _mm_mul_ps(_mm_loadu_ps(src + i * 2 + 4), vgains);
        _mm_storeu_ps(dst + i * 2, s0);
        _mm_storeu_ps(dst + i * 2 + 4, s1);
    }
    for (; i + 2 <= frames; i += 2) {
        __m128 s0 = _mm_mul_ps(_mm_loadu_ps(src + i * 2), vgains);
        _mm_storeu_ps(dst + i * 2, s0);
    }
#endif
    for (; i < frames; ++i) {
        dst[i * 2] = src[i * 2] * gl;
        dst[i * 2 + 1] = src[i * 2 + 1] * gr;
    }
}

// Vectorized stereo bus add: dst[i*2] += src[i*2] * gl, dst[i*2+1] += src[i*2+1] * gr
inline void addStereoScaled(float* dst, const float* src, float gl, float gr, int32_t frames) noexcept {
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    const float32x4_t vgains = {gl, gr, gl, gr};
    for (; i + 4 <= frames; i += 4) {
        float32x4_t s0 = vmulq_f32(vld1q_f32(src + i * 2), vgains);
        float32x4_t s1 = vmulq_f32(vld1q_f32(src + i * 2 + 4), vgains);
        float32x4_t d0 = vaddq_f32(vld1q_f32(dst + i * 2), s0);
        float32x4_t d1 = vaddq_f32(vld1q_f32(dst + i * 2 + 4), s1);
        vst1q_f32(dst + i * 2, d0);
        vst1q_f32(dst + i * 2 + 4, d1);
    }
    for (; i + 2 <= frames; i += 2) {
        float32x4_t s0 = vmulq_f32(vld1q_f32(src + i * 2), vgains);
        float32x4_t d0 = vaddq_f32(vld1q_f32(dst + i * 2), s0);
        vst1q_f32(dst + i * 2, d0);
    }
#elif defined(NOTA_SIMD_SSE)
    const __m128 vgains = _mm_set_ps(gr, gl, gr, gl);
    for (; i + 4 <= frames; i += 4) {
        __m128 s0 = _mm_mul_ps(_mm_loadu_ps(src + i * 2), vgains);
        __m128 s1 = _mm_mul_ps(_mm_loadu_ps(src + i * 2 + 4), vgains);
        __m128 d0 = _mm_add_ps(_mm_loadu_ps(dst + i * 2), s0);
        __m128 d1 = _mm_add_ps(_mm_loadu_ps(dst + i * 2 + 4), s1);
        _mm_storeu_ps(dst + i * 2, d0);
        _mm_storeu_ps(dst + i * 2 + 4, d1);
    }
    for (; i + 2 <= frames; i += 2) {
        __m128 s0 = _mm_mul_ps(_mm_loadu_ps(src + i * 2), vgains);
        __m128 d0 = _mm_add_ps(_mm_loadu_ps(dst + i * 2), s0);
        _mm_storeu_ps(dst + i * 2, d0);
    }
#endif
    for (; i < frames; ++i) {
        dst[i * 2] += src[i * 2] * gl;
        dst[i * 2 + 1] += src[i * 2 + 1] * gr;
    }
}

// Lightweight SIMD & scalar telemetry accumulator for stereo audio streams.
// Tracks peak L/R, sum-of-squares L/R (for RMS), and L*R cross-product (for phase/stereo correlation).
// Real-time audio safe: zero heap allocations, fully inlined, branchless inner loops.
struct StereoMeterAccumulator {
#if defined(NOTA_SIMD_NEON)
    float32x4_t vpk = vdupq_n_f32(0.0f);
    float32x4_t vsq = vdupq_n_f32(0.0f);
    float32x4_t vprod = vdupq_n_f32(0.0f);

    inline void accumulateSimd(float32x4_t v) noexcept {
        vpk = vmaxq_f32(vpk, vabsq_f32(v));
        vsq = vmlaq_f32(vsq, v, v);
        const float32x4_t rev = vrev64q_f32(v);
        vprod = vmlaq_f32(vprod, v, rev);
    }
#elif defined(NOTA_SIMD_SSE)
    __m128 vpk = _mm_setzero_ps();
    __m128 vsq = _mm_setzero_ps();
    __m128 vprod = _mm_setzero_ps();

    inline void accumulateSimd(__m128 v) noexcept {
        static const __m128 absMask = _mm_castsi128_ps(_mm_set1_epi32(0x7fffffff));
        vpk = _mm_max_ps(vpk, _mm_and_ps(v, absMask));
#if defined(NOTA_SIMD_FMA)
        vsq = _mm_fmadd_ps(v, v, vsq);
        const __m128 rev = _mm_shuffle_ps(v, v, _MM_SHUFFLE(2, 3, 0, 1));
        vprod = _mm_fmadd_ps(v, rev, vprod);
#else
        vsq = _mm_add_ps(vsq, _mm_mul_ps(v, v));
        const __m128 rev = _mm_shuffle_ps(v, v, _MM_SHUFFLE(2, 3, 0, 1));
        vprod = _mm_add_ps(vprod, _mm_mul_ps(v, rev));
#endif
    }
#endif

    float peakL = 0.0f;
    float peakR = 0.0f;
    double sumSquaresL = 0.0;
    double sumSquaresR = 0.0;
    double sumProductLR = 0.0;

    inline void accumulateTail(float l, float r) noexcept {
        const float al = std::fabs(l);
        const float ar = std::fabs(r);
        if (al > peakL) peakL = al;
        if (ar > peakR) peakR = ar;
        sumSquaresL += static_cast<double>(l) * static_cast<double>(l);
        sumSquaresR += static_cast<double>(r) * static_cast<double>(r);
        sumProductLR += static_cast<double>(l) * static_cast<double>(r);
    }

    inline void finalize(float& outPkL, float& outPkR, double& outSqL, double& outSqR, double& outSLR) noexcept {
#if defined(NOTA_SIMD_NEON)
        alignas(16) float pkBuf[4];
        alignas(16) float sqBuf[4];
        alignas(16) float prodBuf[4];
        vst1q_f32(pkBuf, vpk);
        vst1q_f32(sqBuf, vsq);
        vst1q_f32(prodBuf, vprod);

        peakL = std::max(peakL, std::max(pkBuf[0], pkBuf[2]));
        peakR = std::max(peakR, std::max(pkBuf[1], pkBuf[3]));
        sumSquaresL += static_cast<double>(sqBuf[0]) + static_cast<double>(sqBuf[2]);
        sumSquaresR += static_cast<double>(sqBuf[1]) + static_cast<double>(sqBuf[3]);
        sumProductLR += static_cast<double>(prodBuf[0] + prodBuf[2]);
#elif defined(NOTA_SIMD_SSE)
        alignas(16) float pkBuf[4];
        alignas(16) float sqBuf[4];
        alignas(16) float prodBuf[4];
        _mm_store_ps(pkBuf, vpk);
        _mm_store_ps(sqBuf, vsq);
        _mm_store_ps(prodBuf, vprod);

        peakL = std::max(peakL, std::max(pkBuf[0], pkBuf[2]));
        peakR = std::max(peakR, std::max(pkBuf[1], pkBuf[3]));
        sumSquaresL += static_cast<double>(sqBuf[0]) + static_cast<double>(sqBuf[2]);
        sumSquaresR += static_cast<double>(sqBuf[1]) + static_cast<double>(sqBuf[3]);
        sumProductLR += static_cast<double>(prodBuf[0] + prodBuf[2]);
#endif
        outPkL = peakL;
        outPkR = peakR;
        outSqL = sumSquaresL;
        outSqR = sumSquaresR;
        outSLR = sumProductLR;
    }
};

// Split interleaved stereo into planar L/R (Oversampler, Chamber, Plugin adapters).
inline void deinterleaveStereo(float* dstL, float* dstR, const float* srcInterleaved, int32_t frames) noexcept {
    if (!dstL || !dstR || !srcInterleaved || frames <= 0) return;
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    for (; i + 4 <= frames; i += 4) {
        float32x4x2_t v = vld2q_f32(srcInterleaved + i * 2);
        vst1q_f32(dstL + i, v.val[0]);
        vst1q_f32(dstR + i, v.val[1]);
    }
#elif defined(NOTA_SIMD_SSE)
    for (; i + 4 <= frames; i += 4) {
        __m128 s0 = _mm_loadu_ps(srcInterleaved + i * 2);
        __m128 s1 = _mm_loadu_ps(srcInterleaved + i * 2 + 4);
        __m128 l = _mm_shuffle_ps(s0, s1, _MM_SHUFFLE(2, 0, 2, 0));
        __m128 r = _mm_shuffle_ps(s0, s1, _MM_SHUFFLE(3, 1, 3, 1));
        _mm_storeu_ps(dstL + i, l);
        _mm_storeu_ps(dstR + i, r);
    }
#endif
    for (; i < frames; ++i) {
        dstL[i] = srcInterleaved[i * 2];
        dstR[i] = srcInterleaved[i * 2 + 1];
    }
}

// Merge planar L/R into interleaved stereo.
inline void interleaveStereo(float* dstInterleaved, const float* srcL, const float* srcR, int32_t frames) noexcept {
    if (!dstInterleaved || !srcL || !srcR || frames <= 0) return;
    int32_t i = 0;
#if defined(NOTA_SIMD_NEON)
    for (; i + 4 <= frames; i += 4) {
        float32x4x2_t v;
        v.val[0] = vld1q_f32(srcL + i);
        v.val[1] = vld1q_f32(srcR + i);
        vst2q_f32(dstInterleaved + i * 2, v);
    }
#elif defined(NOTA_SIMD_SSE)
    for (; i + 4 <= frames; i += 4) {
        __m128 l = _mm_loadu_ps(srcL + i);
        __m128 r = _mm_loadu_ps(srcR + i);
        _mm_storeu_ps(dstInterleaved + i * 2, _mm_unpacklo_ps(l, r));
        _mm_storeu_ps(dstInterleaved + i * 2 + 4, _mm_unpackhi_ps(l, r));
    }
#endif
    for (; i < frames; ++i) {
        dstInterleaved[i * 2] = srcL[i];
        dstInterleaved[i * 2 + 1] = srcR[i];
    }
}

// Additive interleave: dst[i*2] += srcL[i], dst[i*2+1] += srcR[i].
inline void interleaveStereoAdd(float* dstInterleaved, const float* srcL, const float* srcR, int32_t frames) noexcept {
    if (!dstInterleaved || !srcL || !srcR || frames <= 0) return;
    int32_t i = 0;
#if defined(NOTA_SIMD_SSE)
    for (; i + 4 <= frames; i += 4) {
        __m128 l = _mm_loadu_ps(srcL + i);
        __m128 r = _mm_loadu_ps(srcR + i);
        __m128 lo = _mm_unpacklo_ps(l, r);
        __m128 hi = _mm_unpackhi_ps(l, r);
        _mm_storeu_ps(dstInterleaved + i * 2, _mm_add_ps(_mm_loadu_ps(dstInterleaved + i * 2), lo));
        _mm_storeu_ps(dstInterleaved + i * 2 + 4, _mm_add_ps(_mm_loadu_ps(dstInterleaved + i * 2 + 4), hi));
    }
#endif
    for (; i < frames; ++i) {
        dstInterleaved[i * 2] += srcL[i];
        dstInterleaved[i * 2 + 1] += srcR[i];
    }
}

// Wet/dry interleave mix: dst = dry*dst + wet*src (PluginEffect sidechain-safe).
inline void interleaveStereoWetDryMix(
    float* dstInterleaved, const float* srcL, const float* srcR, int32_t frames, float wet, float dry) noexcept {
    if (!dstInterleaved || !srcL || !srcR || frames <= 0) return;
    for (int32_t i = 0; i < frames; ++i) {
        dstInterleaved[i * 2] = dstInterleaved[i * 2] * dry + srcL[i] * wet;
        dstInterleaved[i * 2 + 1] = dstInterleaved[i * 2 + 1] * dry + srcR[i] * wet;
    }
}

// Linear gain ramp over interleaved stereo (fade-out, loop declick).
inline void applyLinearGainRampStereo(float* buf, int32_t frames, float startGain, float endGain) noexcept {
    if (buf == nullptr || frames <= 0) return;
    if (startGain == endGain) {
        if (startGain != 1.0f) {
            const int32_t n = frames * 2;
            for (int32_t i = 0; i < n; ++i) buf[i] *= startGain;
        }
        return;
    }
    const float step = (endGain - startGain) / static_cast<float>(frames);
    for (int32_t i = 0; i < frames; ++i) {
        const float g = startGain + step * static_cast<float>(i);
        buf[i * 2] *= g;
        buf[i * 2 + 1] *= g;
    }
}

} // namespace simd_audio

// --- Convenient nota namespace aliases for SIMD audio
using simd_audio::addStereoScaled;
using simd_audio::applyLinearGainRampStereo;
using simd_audio::deinterleaveStereo;
using simd_audio::interleaveStereo;
using simd_audio::interleaveStereoAdd;
using simd_audio::interleaveStereoWetDryMix;
using simd_audio::isSilent;
using simd_audio::peakAndSumSquares;
using simd_audio::scaleStereo;
using simd_audio::StereoMeterAccumulator;

} // namespace nota
