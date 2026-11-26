// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SimdDispatch — clean runtime SIMD kernel backend layered over SimdAudio.h.
// Selects AVX2+FMA kernels at startup on capable x86_64 CPUs for high-work block
// operations (convolution CMAC and large-buffer peak/RMS scans), while routing short
// buffers and ARM64 NEON directly to zero-overhead inlined SimdAudio primitives.

#pragma once

#include "dsp/math/SimdAudio.h"

#include <cstdint>

namespace nota {
namespace simd {

enum class SimdIsa : uint8_t { Scalar, Sse2, Avx2Fma, Neon };

struct SimdKernels {
    SimdIsa isa = SimdIsa::Scalar;
    const char* name = "scalar";

    void (*convCmac)(float* __restrict ar, float* __restrict ai, const float* __restrict xr,
        const float* __restrict xi, const float* __restrict hr, const float* __restrict hi, int32_t bins) noexcept =
        nullptr;
    float (*peakAbs)(const float* src, int32_t count) noexcept = nullptr;
    void (*peakAndSumSquares)(const float* src, int32_t count, float& outPeak, double& outSumSq) noexcept = nullptr;

    // Runtime-selected backend (AVX2+FMA on capable portable x86_64, else baseline).
    static const SimdKernels& active() noexcept;
    // Compile-time baseline backend (NEON / SSE2 / Scalar), exposed cleanly for A/B self-tests.
    static const SimdKernels& baseline() noexcept;
};

// Crossover sizes on portable x86_64 where 256-bit AVX2+FMA beats an inlined SSE2 loop.
inline constexpr int32_t kAvx2ConvCmacMinBins = 33;
inline constexpr int32_t kAvx2BufferMinFloats = 512;

inline void convCmac(float* __restrict ar, float* __restrict ai, const float* __restrict xr,
    const float* __restrict xi, const float* __restrict hr, const float* __restrict hi, int32_t bins) noexcept {
#if !defined(NOTA_SIMD_NEON) && (defined(__x86_64__) || defined(_M_X64)) && !defined(NOTA_SIMD_AVX2)
    if (bins >= kAvx2ConvCmacMinBins) {
        SimdKernels::active().convCmac(ar, ai, xr, xi, hr, hi, bins);
        return;
    }
#endif
    nota::simd_audio::convCmac(ar, ai, xr, xi, hr, hi, bins);
}

inline float peakAbs(const float* src, int32_t count) noexcept {
#if !defined(NOTA_SIMD_NEON) && (defined(__x86_64__) || defined(_M_X64)) && !defined(NOTA_SIMD_AVX2)
    if (count >= kAvx2BufferMinFloats) return SimdKernels::active().peakAbs(src, count);
#endif
    return nota::simd_audio::peakAbs(src, count);
}

inline void peakAndSumSquares(const float* src, int32_t count, float& outPeak, double& outSumSquares) noexcept {
#if !defined(NOTA_SIMD_NEON) && (defined(__x86_64__) || defined(_M_X64)) && !defined(NOTA_SIMD_AVX2)
    if (count >= kAvx2BufferMinFloats) {
        SimdKernels::active().peakAndSumSquares(src, count, outPeak, outSumSquares);
        return;
    }
#endif
    nota::simd_audio::peakAndSumSquares(src, count, outPeak, outSumSquares);
}

#if defined(__x86_64__) || defined(_M_X64)
namespace avx2 {
void bind(SimdKernels& k) noexcept;
}
#endif

} // namespace simd
} // namespace nota
