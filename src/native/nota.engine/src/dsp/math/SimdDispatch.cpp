// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "dsp/math/SimdDispatch.h"

#if defined(__x86_64__) || defined(_M_X64)
#if defined(_MSC_VER)
#include <intrin.h>
#else
#include <cpuid.h>
#endif
#endif

namespace nota {
namespace simd {
namespace {

void baselineConvCmac(float* __restrict ar, float* __restrict ai, const float* __restrict xr,
    const float* __restrict xi, const float* __restrict hr, const float* __restrict hi, int32_t bins) noexcept {
    nota::simd_audio::convCmac(ar, ai, xr, xi, hr, hi, bins);
}

float baselinePeakAbs(const float* src, int32_t count) noexcept {
    return nota::simd_audio::peakAbs(src, count);
}

void baselinePeakAndSumSquares(const float* src, int32_t count, float& outPeak, double& outSumSq) noexcept {
    nota::simd_audio::peakAndSumSquares(src, count, outPeak, outSumSq);
}

SimdKernels makeBaseline() noexcept {
    SimdKernels k;
#if defined(NOTA_SIMD_NEON)
    k.isa = SimdIsa::Neon;
    k.name = "neon";
#elif defined(__x86_64__) || defined(_M_X64)
    k.isa = SimdIsa::Sse2;
    k.name = "sse2";
#else
    k.isa = SimdIsa::Scalar;
    k.name = "scalar";
#endif
    k.convCmac = &baselineConvCmac;
    k.peakAbs = &baselinePeakAbs;
    k.peakAndSumSquares = &baselinePeakAndSumSquares;
    return k;
}

#if defined(__x86_64__) || defined(_M_X64)
uint64_t readXcr0() noexcept {
#if defined(_MSC_VER)
    return _xgetbv(0);
#else
    uint32_t lo = 0, hi = 0;
    __asm__ volatile("xgetbv" : "=a"(lo), "=d"(hi) : "c"(0));
    return (static_cast<uint64_t>(hi) << 32) | lo;
#endif
}

bool hostSupportsAvx2Fma() noexcept {
    int info1[4]{};
    int info7[4]{};
#if defined(_MSC_VER)
    __cpuid(info1, 0);
    const int maxLeaf = info1[0];
    if (maxLeaf >= 1) __cpuidex(info1, 1, 0);
    if (maxLeaf >= 7) __cpuidex(info7, 7, 0);
#else
    uint32_t eax = 0, ebx = 0, ecx = 0, edx = 0;
    const uint32_t maxLeaf = __get_cpuid_max(0, nullptr);
    if (maxLeaf >= 1) {
        __cpuid_count(1, 0, eax, ebx, ecx, edx);
        info1[2] = static_cast<int>(ecx);
    }
    if (maxLeaf >= 7) {
        __cpuid_count(7, 0, eax, ebx, ecx, edx);
        info7[1] = static_cast<int>(ebx);
    }
#endif
    const bool osxsave = (info1[2] & (1 << 27)) != 0;
    const bool avxBit = (info1[2] & (1 << 28)) != 0;
    const bool fmaBit = (info1[2] & (1 << 12)) != 0;
    const bool avx2Bit = (info7[1] & (1 << 5)) != 0;
    const bool ymmOsEnabled = osxsave && ((readXcr0() & 0x6u) == 0x6u);
    return ymmOsEnabled && avxBit && avx2Bit && fmaBit;
}
#endif

} // namespace

const SimdKernels& SimdKernels::baseline() noexcept {
    static const SimdKernels k = makeBaseline();
    return k;
}

const SimdKernels& SimdKernels::active() noexcept {
    static const SimdKernels k = [] {
        SimdKernels s = makeBaseline();
#if !defined(NOTA_SIMD_NEON) && (defined(__x86_64__) || defined(_M_X64))
        if (hostSupportsAvx2Fma()) avx2::bind(s);
#endif
        return s;
    }();
    return k;
}

} // namespace simd
} // namespace nota
