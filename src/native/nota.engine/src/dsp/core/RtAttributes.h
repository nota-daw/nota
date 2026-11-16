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
#define NOTA_NOINLINE [[gnu::noinline]]
#elif defined(_MSC_VER)
#define NOTA_HOT
#define NOTA_ALWAYS_INLINE __forceinline
#define NOTA_NOINLINE __declspec(noinline)
#else
#define NOTA_HOT
#define NOTA_ALWAYS_INLINE inline
#define NOTA_NOINLINE
#endif
// NOTA_NOINLINE keeps a rare path (a muted Drum Rack pad, #650) out of a hot loop, so the
// loop's code — and the instruction gate's count for it — stays as it was.

// NOTA_FORCE_INLINE_LAMBDA: a lambda body that must inline into every call site — the
// shared per-sample DSP bodies split into a glide head and a settled loop (#559), where
// the instruction gate of specs/034 budgets the settled loop. Placed after the parameter
// list it needs the GNU spelling ([[gnu::always_inline]] there appertains to the closure
// type and is ignored). MSVC has no force-inline for lambdas; its inliner is trusted
// there (the instruction gate runs on Linux only).
#if defined(__GNUC__) || defined(__clang__)
#define NOTA_FORCE_INLINE_LAMBDA __attribute__((always_inline))
#else
#define NOTA_FORCE_INLINE_LAMBDA
#endif

namespace nota {

inline constexpr std::size_t kCacheLineSize = 64;

} // namespace nota

#define NOTA_CACHE_ALIGNED alignas(nota::kCacheLineSize)

// NOTA_CPU_PAUSE: spin-wait hint for AudioThreadPool barriers (AR-6 safe).
#if !defined(NOTA_CPU_PAUSE)
#if defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
#define NOTA_CPU_PAUSE() _mm_pause()
#elif defined(_M_ARM64) && defined(_MSC_VER) && !defined(__clang__)
#include <intrin.h>
#define NOTA_CPU_PAUSE() __isb(_ARM64_BARRIER_SY)
#elif defined(__aarch64__) || defined(_M_ARM64)
#define NOTA_CPU_PAUSE() __asm__ volatile("isb sy" : : : "memory")
#else
#define NOTA_CPU_PAUSE()                                                                                               \
    do {                                                                                                               \
    } while (0)
#endif
#endif
