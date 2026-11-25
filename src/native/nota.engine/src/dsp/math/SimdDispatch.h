// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SimdDispatch — startup CPU capability probe and runtime kernel dispatch table.
// Enables AVX2+FMA kernels on capable x86_64 CPUs in portable universal binaries
// while falling back cleanly to SSE2 or ARM64 NEON.

#pragma once

#include <cstdint>

namespace nota {
namespace simd {

struct CpuCaps {
    bool sse2 = false;
    bool avx2 = false;
    bool fma = false;
    bool neon = false;
    const char* activeBackend = "scalar";
};

struct DispatchTable {
    void (*add)(float* dst, const float* src, int32_t count) noexcept = nullptr;
    void (*addScaled)(float* dst, const float* src, float scale, int32_t count) noexcept = nullptr;
    void (*scale)(float* dst, const float* src, float scale, int32_t count) noexcept = nullptr;
    void (*mul)(float* dst, const float* a, const float* b, int32_t count) noexcept = nullptr;
    float (*maxAbs)(const float* src, int32_t count) noexcept = nullptr;
    void (*peakAndSumSquares)(const float* src, int32_t count, float& outPeak, double& outSumSq) noexcept = nullptr;
};

const CpuCaps& cpuCaps() noexcept;
const DispatchTable& dispatchTable() noexcept;

// Convenience dispatched entry points (for medium/large float buffers >= 64 floats).
inline void dispatchAdd(float* dst, const float* src, int32_t count) noexcept {
    dispatchTable().add(dst, src, count);
}
inline void dispatchAddScaled(float* dst, const float* src, float scale, int32_t count) noexcept {
    dispatchTable().addScaled(dst, src, scale, count);
}
inline void dispatchScale(float* dst, const float* src, float scale, int32_t count) noexcept {
    dispatchTable().scale(dst, src, scale, count);
}
inline void dispatchMul(float* dst, const float* a, const float* b, int32_t count) noexcept {
    dispatchTable().mul(dst, a, b, count);
}
inline float dispatchMaxAbs(const float* src, int32_t count) noexcept {
    return dispatchTable().maxAbs(src, count);
}
inline void dispatchPeakAndSumSquares(
    const float* src, int32_t count, float& outPeak, double& outSumSq) noexcept {
    dispatchTable().peakAndSumSquares(src, count, outPeak, outSumSq);
}

bool simdDispatchSelfTest() noexcept;

#if defined(__x86_64__) || defined(_M_X64)
namespace avx2 {
void bind(DispatchTable& tbl) noexcept;
}
#endif

} // namespace simd
} // namespace nota
