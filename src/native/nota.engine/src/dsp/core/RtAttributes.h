// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RtAttributes — compiler annotations for real-time audio safety (AR-6).
// NOTA_RT_SAFE marks functions that must never allocate, lock, or block,
// enabling Clang RealtimeSanitizer ([[clang::nonblocking]]) where available.

#pragma once

#include <atomic>
#include <cstddef>

#if defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
#if defined(_MSC_VER)
#include <intrin.h>
#else
#include <immintrin.h>
#endif
#endif

// NOTA_RT_SAFE: noexcept + non-blocking annotation on Clang, plain noexcept elsewhere.
// Apply to every audio-thread entry point: Device::process, Instrument::render,
// Engine::processBlock/mixGraph, FastMath/Simd/SimdAudio helpers.
#if defined(__clang__) && defined(__has_cpp_attribute)
#if __has_cpp_attribute(clang::nonblocking)
#define NOTA_RT_SAFE noexcept [[clang::nonblocking]]
#else
#define NOTA_RT_SAFE noexcept
#endif
#else
#define NOTA_RT_SAFE noexcept
#endif

#if defined(__GNUC__) || defined(__clang__)
#define NOTA_HOT [[gnu::hot]]
#define NOTA_ALWAYS_INLINE [[gnu::always_inline]] inline
#elif defined(_MSC_VER)
#define NOTA_HOT
#define NOTA_ALWAYS_INLINE __forceinline
#else
#define NOTA_HOT
#define NOTA_ALWAYS_INLINE inline
#endif

namespace nota {

inline constexpr std::size_t kCacheLineSize = 64;

} // namespace nota

#define NOTA_CACHE_ALIGNED alignas(nota::kCacheLineSize)

// NOTA_CPU_PAUSE: spin-wait hint for AudioThreadPool barriers (AR-6 safe).
#if !defined(NOTA_CPU_PAUSE)
#if defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
#define NOTA_CPU_PAUSE() _mm_pause()
#elif defined(__aarch64__) || defined(_M_ARM64)
#define NOTA_CPU_PAUSE() __asm__ volatile("isb sy" : : : "memory")
#else
#define NOTA_CPU_PAUSE()                                                                                               \
    do {                                                                                                               \
    } while (0)
#endif
#endif
