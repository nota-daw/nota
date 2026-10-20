// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AudioBufferUtils — high-performance, real-time safe audio buffer inspection and utilities.
// Vectorized stereo silence gating, RMS energy calculation, post-fader gain, and bus summing.

#pragma once

#include "dsp/math/SimdAudio.h"
#include "dsp/math/Simd.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace nota {

// Fast, early-exiting silence detection for interleaved stereo audio buffers.
// Returns true if all samples have an absolute amplitude <= threshold (default 1e-5f ≈ -100 dBFS).
inline bool isBufferSilent(const float* interleaved, int32_t frames, float threshold = 1.0e-5f) noexcept {
    if (!interleaved || frames <= 0) return true;
    return simd_audio::isSilent(interleaved, frames * 2, threshold);
}

// Vectorized stereo post-fader gain and metering:
// Scales scratch in-place: scratch[i*2] *= gl, scratch[i*2+1] *= gr
// and computes pkL, pkR, sqL, sqR, sLR in a single SIMD pass.
inline void scaleStereoMeter(float* scratch, int32_t frames, float gl, float gr, float& outPkL, float& outPkR,
    double& outSqL, double& outSqR, double& outSLR) noexcept {
    StereoMeterAccumulator meter;
    int32_t i = 0;

#if defined(NOTA_SIMD_NEON)
    const float32x4_t vgains = {gl, gr, gl, gr};
    for (; i + 4 <= frames; i += 4) {
        float32x4_t s0 = vmulq_f32(vld1q_f32(scratch + i * 2), vgains);
        float32x4_t s1 = vmulq_f32(vld1q_f32(scratch + i * 2 + 4), vgains);
        vst1q_f32(scratch + i * 2, s0);
        vst1q_f32(scratch + i * 2 + 4, s1);
        meter.accumulateSimd(s0);
        meter.accumulateSimd(s1);
    }
    for (; i + 2 <= frames; i += 2) {
        float32x4_t s0 = vmulq_f32(vld1q_f32(scratch + i * 2), vgains);
        vst1q_f32(scratch + i * 2, s0);
        meter.accumulateSimd(s0);
    }
#elif defined(NOTA_SIMD_SSE)
    const __m128 vgains = _mm_set_ps(gr, gl, gr, gl);
    for (; i + 4 <= frames; i += 4) {
        __m128 s0 = _mm_mul_ps(_mm_loadu_ps(scratch + i * 2), vgains);
        __m128 s1 = _mm_mul_ps(_mm_loadu_ps(scratch + i * 2 + 4), vgains);
        _mm_storeu_ps(scratch + i * 2, s0);
        _mm_storeu_ps(scratch + i * 2 + 4, s1);
        meter.accumulateSimd(s0);
        meter.accumulateSimd(s1);
    }
    for (; i + 2 <= frames; i += 2) {
        __m128 s0 = _mm_mul_ps(_mm_loadu_ps(scratch + i * 2), vgains);
        _mm_storeu_ps(scratch + i * 2, s0);
        meter.accumulateSimd(s0);
    }
#endif
    for (; i < frames; ++i) {
        const float l = scratch[i * 2] * gl;
        const float r = scratch[i * 2 + 1] * gr;
        scratch[i * 2] = l;
        scratch[i * 2 + 1] = r;
        meter.accumulateTail(l, r);
    }

    meter.finalize(outPkL, outPkR, outSqL, outSqR, outSLR);
}

// Vectorized stereo bus add to destination with full stereo map metering:
// dest[i*2] += src[i*2] * gl, dest[i*2+1] += src[i*2+1] * gr
// and computes pkL, pkR, sqL, sqR, sLR in a single SIMD pass.
inline void addStereoScaledMeterMap(float* dest, const float* src, int32_t frames, float gl, float gr, float& outPkL,
    float& outPkR, double& outSqL, double& outSqR, double& outSLR) noexcept {
    StereoMeterAccumulator meter;
    int32_t i = 0;

#if defined(NOTA_SIMD_NEON)
    const float32x4_t vgains = {gl, gr, gl, gr};
    for (; i + 4 <= frames; i += 4) {
        float32x4_t s0 = vmulq_f32(vld1q_f32(src + i * 2), vgains);
        float32x4_t s1 = vmulq_f32(vld1q_f32(src + i * 2 + 4), vgains);
        float32x4_t d0 = vaddq_f32(vld1q_f32(dest + i * 2), s0);
        float32x4_t d1 = vaddq_f32(vld1q_f32(dest + i * 2 + 4), s1);
        vst1q_f32(dest + i * 2, d0);
        vst1q_f32(dest + i * 2 + 4, d1);
        meter.accumulateSimd(s0);
        meter.accumulateSimd(s1);
    }
    for (; i + 2 <= frames; i += 2) {
        float32x4_t s0 = vmulq_f32(vld1q_f32(src + i * 2), vgains);
        float32x4_t d0 = vaddq_f32(vld1q_f32(dest + i * 2), s0);
        vst1q_f32(dest + i * 2, d0);
        meter.accumulateSimd(s0);
    }
#elif defined(NOTA_SIMD_SSE)
    const __m128 vgains = _mm_set_ps(gr, gl, gr, gl);
    for (; i + 4 <= frames; i += 4) {
        __m128 s0 = _mm_mul_ps(_mm_loadu_ps(src + i * 2), vgains);
        __m128 s1 = _mm_mul_ps(_mm_loadu_ps(src + i * 2 + 4), vgains);
        __m128 d0 = _mm_add_ps(_mm_loadu_ps(dest + i * 2), s0);
        __m128 d1 = _mm_add_ps(_mm_loadu_ps(dest + i * 2 + 4), s1);
        _mm_storeu_ps(dest + i * 2, d0);
        _mm_storeu_ps(dest + i * 2 + 4, d1);
        meter.accumulateSimd(s0);
        meter.accumulateSimd(s1);
    }
    for (; i + 2 <= frames; i += 2) {
        __m128 s0 = _mm_mul_ps(_mm_loadu_ps(src + i * 2), vgains);
        __m128 d0 = _mm_add_ps(_mm_loadu_ps(dest + i * 2), s0);
        _mm_storeu_ps(dest + i * 2, d0);
        meter.accumulateSimd(s0);
    }
#endif
    for (; i < frames; ++i) {
        const float l = src[i * 2] * gl;
        const float r = src[i * 2 + 1] * gr;
        dest[i * 2] += l;
        dest[i * 2 + 1] += r;
        meter.accumulateTail(l, r);
    }

    meter.finalize(outPkL, outPkR, outSqL, outSqR, outSLR);
}

// Vectorized stereo bus add and metering (for group tracks):
// dest[i*2] += src[i*2] * gl, dest[i*2+1] += src[i*2+1] * gr
// and computes pkL, pkR, sqL, sqR in a single SIMD pass.
inline void addStereoScaledMeter(float* dest, const float* src, int32_t frames, float gl, float gr, float& outPkL,
    float& outPkR, double& outSqL, double& outSqR) noexcept {
    double dummySLR = 0.0;
    addStereoScaledMeterMap(dest, src, frames, gl, gr, outPkL, outPkR, outSqL, outSqR, dummySLR);
}

// Vectorized stereo bus add (legacy parameter order): dest[i*2] += src[i*2] * gl, dest[i*2+1] += src[i*2+1] * gr
inline void addStereoScaled(float* dest, const float* src, int32_t frames, float gl, float gr) noexcept {
    simd_audio::addStereoScaled(dest, src, gl, gr, frames);
}

// Vectorized master bus fader, safety net (NaN/Inf suppression), clipping, and telemetry:
// Multiplies out by mv, squashes any non-finite float to 0.0f, clamps to [-1.0f, 1.0f],
// and computes pkL, pkR, sqL, sqR, sLR in a single unified SIMD pass.
inline void processMasterBus(float* out, int32_t frames, float mv, float& outPkL, float& outPkR, double& outSqL,
    double& outSqR, double& outSLR) noexcept {
    StereoMeterAccumulator meter;
    int32_t i = 0;

#if defined(NOTA_SIMD_NEON)
    const float32x4_t vmv = vdupq_n_f32(mv);
    const float32x4_t vmin = vdupq_n_f32(-1.0f);
    const float32x4_t vmax = vdupq_n_f32(1.0f);

    for (; i + 4 <= frames; i += 4) {
        float32x4_t v0 = simd::squashNonFinite(vmulq_f32(vld1q_f32(out + i * 2), vmv));
        float32x4_t v1 = simd::squashNonFinite(vmulq_f32(vld1q_f32(out + i * 2 + 4), vmv));
        v0 = vminq_f32(vmaxq_f32(v0, vmin), vmax);
        v1 = vminq_f32(vmaxq_f32(v1, vmin), vmax);
        vst1q_f32(out + i * 2, v0);
        vst1q_f32(out + i * 2 + 4, v1);
        meter.accumulateSimd(v0);
        meter.accumulateSimd(v1);
    }
    for (; i + 2 <= frames; i += 2) {
        float32x4_t v0 = simd::squashNonFinite(vmulq_f32(vld1q_f32(out + i * 2), vmv));
        v0 = vminq_f32(vmaxq_f32(v0, vmin), vmax);
        vst1q_f32(out + i * 2, v0);
        meter.accumulateSimd(v0);
    }
#elif defined(NOTA_SIMD_SSE)
    const __m128 vmv = _mm_set1_ps(mv);
    const __m128 vmin = _mm_set1_ps(-1.0f);
    const __m128 vmax = _mm_set1_ps(1.0f);

    for (; i + 4 <= frames; i += 4) {
        __m128 v0 = simd::squashNonFinite(_mm_mul_ps(_mm_loadu_ps(out + i * 2), vmv));
        __m128 v1 = simd::squashNonFinite(_mm_mul_ps(_mm_loadu_ps(out + i * 2 + 4), vmv));
        v0 = _mm_min_ps(_mm_max_ps(v0, vmin), vmax);
        v1 = _mm_min_ps(_mm_max_ps(v1, vmin), vmax);
        _mm_storeu_ps(out + i * 2, v0);
        _mm_storeu_ps(out + i * 2 + 4, v1);
        meter.accumulateSimd(v0);
        meter.accumulateSimd(v1);
    }
    for (; i + 2 <= frames; i += 2) {
        __m128 v0 = simd::squashNonFinite(_mm_mul_ps(_mm_loadu_ps(out + i * 2), vmv));
        v0 = _mm_min_ps(_mm_max_ps(v0, vmin), vmax);
        _mm_storeu_ps(out + i * 2, v0);
        meter.accumulateSimd(v0);
    }
#endif

    for (; i < frames; ++i) {
        float l = std::clamp(simd::squashNonFinite(out[i * 2] * mv), -1.0f, 1.0f);
        float r = std::clamp(simd::squashNonFinite(out[i * 2 + 1] * mv), -1.0f, 1.0f);
        out[i * 2] = l;
        out[i * 2 + 1] = r;
        meter.accumulateTail(l, r);
    }

    meter.finalize(outPkL, outPkR, outSqL, outSqR, outSLR);
}

enum class SignalState : uint8_t { Active, PossiblySilent, Silent };

// SilenceDetector: stateful audio activity detector with dual-threshold hysteresis (Schmitt trigger)
// and sample-accurate hold time to prevent chattering around the noise floor or zero crossings.
// Real-time audio safe: 0 heap allocations, 0 locks.
struct SilenceDetector {
    double attackThreshold = 1.0e-4;  // RMS ~ -80 dBFS (wake-up threshold)
    double releaseThreshold = 3.0e-5; // RMS ~ -90.5 dBFS (sleep entry threshold)
    uint64_t holdSamples = 6615;      // ~150 ms at 44.1 kHz default
    uint64_t silentSamples = 6615;
    SignalState state = SignalState::Silent;
    float lastMaxPeak = 0.0f;

    SilenceDetector() = default;
    explicit SilenceDetector(double sr, double holdMs = 150.0) {
        setSampleRate(sr, holdMs);
        reset(SignalState::Silent);
    }

    void setSampleRate(double sr, double holdMs = 150.0) {
        const double rate = sr > 0.0 ? sr : 44100.0;
        holdSamples = static_cast<uint64_t>((holdMs * 0.001) * rate);
        if (state == SignalState::Silent) silentSamples = holdSamples;
    }

    void reset(SignalState initialState = SignalState::Silent) {
        state = initialState;
        silentSamples = (initialState == SignalState::Silent) ? holdSamples : 0;
        lastMaxPeak = 0.0f;
    }

    bool isSilent() const { return state == SignalState::Silent; }
    bool isActive() const { return state == SignalState::Active; }
    bool isPeakSilent(float threshold = 1.0e-5f) const noexcept { return lastMaxPeak <= threshold; }

    SignalState process(const float* interleaved, int32_t frames) {
        if (!interleaved || frames <= 0) {
            lastMaxPeak = 0.0f;
            return state;
        }

        const int32_t totalSamples = frames * 2;
        double sumSquares = 0.0;
        float maxPeak = 0.0f;
        peakAndSumSquares(interleaved, totalSamples, maxPeak, sumSquares);
        lastMaxPeak = maxPeak;

        const double rms = std::sqrt(sumSquares / totalSamples);

        // Immediate wake-up on onset: peak or RMS exceeds attack threshold
        if (rms >= attackThreshold || static_cast<double>(maxPeak) >= attackThreshold) {
            state = SignalState::Active;
            silentSamples = 0;
        } else if (rms <= releaseThreshold && static_cast<double>(maxPeak) <= attackThreshold) {
            // Signal below release threshold: accumulate quiet samples towards hold time
            silentSamples += frames;
            state = (silentSamples >= holdSamples) ? SignalState::Silent : SignalState::PossiblySilent;
        } else {
            // Within deadband between releaseThreshold and attackThreshold:
            // Hysteresis maintains current Active state, or continues decaying if already quiet.
            if (state == SignalState::PossiblySilent) {
                silentSamples += frames;
                if (silentSamples >= holdSamples) state = SignalState::Silent;
            }
        }

        return state;
    }
};

} // namespace nota
