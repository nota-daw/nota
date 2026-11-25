// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "dsp/math/SimdDispatch.h"
#include "dsp/math/SimdAudio.h"

#include <array>
#include <cmath>
#include <mutex>
#include <vector>

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

void defaultAdd(float* dst, const float* src, int32_t count) noexcept {
    nota::simd::add(dst, src, count);
}
void defaultAddScaled(float* dst, const float* src, float scale, int32_t count) noexcept {
    nota::simd::addScaled(dst, src, scale, count);
}
void defaultScale(float* dst, const float* src, float scale, int32_t count) noexcept {
    nota::simd::scale(dst, src, scale, count);
}
void defaultMul(float* dst, const float* a, const float* b, int32_t count) noexcept {
    nota::simd::mul(dst, a, b, count);
}
float defaultMaxAbs(const float* src, int32_t count) noexcept {
    return nota::simd::maxAbs(src, count);
}
void defaultPeakAndSumSq(const float* src, int32_t count, float& outPeak, double& outSumSq) noexcept {
    nota::simd_audio::peakAndSumSquares(src, count, outPeak, outSumSq);
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

CpuCaps probeX86Caps() noexcept {
    CpuCaps caps;
    caps.sse2 = true;
    caps.activeBackend = "sse2";

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

    caps.avx2 = ymmOsEnabled && avxBit && avx2Bit;
    caps.fma = caps.avx2 && fmaBit;
    if (caps.avx2) caps.activeBackend = caps.fma ? "avx2+fma" : "avx2";
    return caps;
}
#endif

struct InitState {
    CpuCaps caps;
    DispatchTable table;
};

const InitState& initState() noexcept {
    static InitState state = [] {
        InitState s;
        s.table.add = &defaultAdd;
        s.table.addScaled = &defaultAddScaled;
        s.table.scale = &defaultScale;
        s.table.mul = &defaultMul;
        s.table.maxAbs = &defaultMaxAbs;
        s.table.peakAndSumSquares = &defaultPeakAndSumSq;

#if defined(NOTA_SIMD_NEON)
        s.caps.neon = true;
        s.caps.activeBackend = "neon";
#elif defined(__x86_64__) || defined(_M_X64)
        s.caps = probeX86Caps();
        if (s.caps.avx2 && s.caps.fma) avx2::bind(s.table);
#endif
        return s;
    }();
    return state;
}

} // namespace

const CpuCaps& cpuCaps() noexcept { return initState().caps; }
const DispatchTable& dispatchTable() noexcept { return initState().table; }

bool simdDispatchSelfTest() noexcept {
    const CpuCaps& caps = cpuCaps();
#if defined(NOTA_SIMD_NEON)
    if (!caps.neon) return false;
#elif defined(__x86_64__) || defined(_M_X64)
    if (!caps.sse2) return false;
#endif
    if (!caps.activeBackend || caps.activeBackend[0] == '\0') return false;

    for (int count : {131, 8192}) { // unaligned tail + large buffer sweep
        std::vector<float> a(static_cast<size_t>(count));
        std::vector<float> b(static_cast<size_t>(count));
        std::vector<float> dst(static_cast<size_t>(count));
        for (int i = 0; i < count; ++i) {
            a[static_cast<size_t>(i)] = static_cast<float>((i % 97) - 48) * 0.015f;
            b[static_cast<size_t>(i)] = static_cast<float>(97 - (i % 97)) * 0.01f;
        }

        dst = a;
        dispatchAdd(dst.data(), b.data(), count);
        for (int i = 0; i < count; ++i) {
            if (std::fabs(dst[static_cast<size_t>(i)] - (a[static_cast<size_t>(i)] + b[static_cast<size_t>(i)])) > 1e-6f)
                return false;
        }

        dst = a;
        dispatchAddScaled(dst.data(), b.data(), 0.5f, count);
        for (int i = 0; i < count; ++i) {
            const float expected = a[static_cast<size_t>(i)] + b[static_cast<size_t>(i)] * 0.5f;
            if (std::fabs(dst[static_cast<size_t>(i)] - expected) > 1e-6f) return false;
        }

        dispatchScale(dst.data(), b.data(), 0.75f, count);
        for (int i = 0; i < count; ++i) {
            if (std::fabs(dst[static_cast<size_t>(i)] - b[static_cast<size_t>(i)] * 0.75f) > 1e-6f) return false;
        }

        dispatchMul(dst.data(), a.data(), b.data(), count);
        for (int i = 0; i < count; ++i) {
            if (std::fabs(dst[static_cast<size_t>(i)] - a[static_cast<size_t>(i)] * b[static_cast<size_t>(i)]) > 1e-6f)
                return false;
        }

        const float maxA = dispatchMaxAbs(a.data(), count);
        float pk = 0.0f;
        double sq = 0.0;
        dispatchPeakAndSumSquares(a.data(), count, pk, sq);
        float pkRef = 0.0f;
        double sqRef = 0.0;
        for (int i = 0; i < count; ++i) {
            const float x = a[static_cast<size_t>(i)];
            pkRef = std::max(pkRef, std::fabs(x));
            sqRef += static_cast<double>(x) * static_cast<double>(x);
        }
        if (std::fabs(maxA - pkRef) > 1e-6f || std::fabs(pk - pkRef) > 1e-6f ||
            std::fabs(sq - sqRef) > std::max(1e-3, sqRef * 1e-5))
            return false;
    }
    return true;
}

} // namespace simd
} // namespace nota
