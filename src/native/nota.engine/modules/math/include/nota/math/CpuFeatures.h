// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// CpuFeatures — compile-time/runtime ISA feature detection, spin-wait pause,
// and RT-safe ScopedNoDenormals guard (AR-6).

#pragma once

#include "nota/math/Export.h"

#include <cstdint>

#if defined(__aarch64__) || defined(_M_ARM64) || defined(__ARM_NEON)
#define NOTA_ARCH_ARM 1
#define NOTA_SIMD_NEON 1
#elif defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
#define NOTA_ARCH_X86 1
#if defined(__SSE2__) || defined(_M_X64) || (defined(_M_IX86_FP) && _M_IX86_FP >= 2)
#define NOTA_SIMD_SSE 1
#define NOTA_SIMD_SSE2 1
#endif
#ifdef __AVX__
#define NOTA_SIMD_AVX 1
#endif
#ifdef __AVX2__
#define NOTA_SIMD_AVX2 1
#endif
#ifdef __FMA__
#define NOTA_SIMD_FMA 1
#endif
#else
#define NOTA_ARCH_GENERIC 1
#endif

#if defined(_MSC_VER)
#include <intrin.h>
#endif

#if defined(NOTA_SIMD_SSE2)
#include <emmintrin.h>
#include <xmmintrin.h>
#endif

namespace nota::math {

struct CpuCapabilities {
    bool neon = false;
    bool sse2 = false;
    bool avx2 = false;
    bool fma = false;
};

NOTA_MATH_API CpuCapabilities detectCpuCapabilities() noexcept;

NOTA_ALWAYS_INLINE void cpuPause() noexcept {
#if defined(NOTA_ARCH_X86)
    _mm_pause();
#elif defined(NOTA_ARCH_ARM)
#if defined(_MSC_VER) && !defined(__clang__)
    __isb(_ARM64_BARRIER_SY);
#else
    __asm__ volatile("isb sy" : : : "memory");
#endif
#endif
}

class ScopedNoDenormals {
  public:
    ScopedNoDenormals() noexcept { enter(); }
    ~ScopedNoDenormals() noexcept { leave(); }

    ScopedNoDenormals(const ScopedNoDenormals&) = delete;
    ScopedNoDenormals& operator=(const ScopedNoDenormals&) = delete;

  private:
#if defined(__aarch64__) || defined(_M_ARM64)
    uint64_t saved_ = 0;

    void enter() noexcept {
        saved_ = readFpcr();
        writeFpcr(saved_ | (1ULL << 24));
    }
    void leave() noexcept { writeFpcr(saved_); }

#if defined(_MSC_VER) && !defined(__clang__)
    static constexpr int kFpcr = 0x5A20;
    static uint64_t readFpcr() noexcept { return static_cast<uint64_t>(_ReadStatusReg(kFpcr)); }
    static void writeFpcr(uint64_t v) noexcept { _WriteStatusReg(kFpcr, static_cast<__int64>(v)); }
#else
    static uint64_t readFpcr() noexcept {
        uint64_t v;
        __asm__ volatile("mrs %0, fpcr" : "=r"(v));
        return v;
    }
    static void writeFpcr(uint64_t v) noexcept { __asm__ volatile("msr fpcr, %0" : : "r"(v)); }
#endif
#elif defined(NOTA_ARCH_X86)
    unsigned saved_ = 0;

    void enter() noexcept {
        saved_ = _mm_getcsr();
        _mm_setcsr(saved_ | 0x8040u); // FTZ | DAZ
    }
    void leave() noexcept { _mm_setcsr(saved_); }
#else
    void enter() noexcept {}
    void leave() noexcept {}
#endif
};

} // namespace nota::math

#ifndef NOTA_CPU_PAUSE
#define NOTA_CPU_PAUSE() ::nota::math::cpuPause()
#endif
