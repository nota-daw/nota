// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RtAttributes — compiler annotations for real-time audio safety, SIMD barriers & cache alignment.

#pragma once

#include <cstddef>

#if defined(__x86_64__) || defined(_M_X64) || defined(__i386__) || defined(_M_IX86)
#if defined(_MSC_VER)
#include <intrin.h>
#else
#include <immintrin.h>
#endif
#endif

#ifndef NOTA_RT_SAFE
#if defined(__clang__) && defined(__has_cpp_attribute)
#if __has_cpp_attribute(clang::nonblocking)
#define NOTA_RT_SAFE noexcept [[clang::nonblocking]]
#else
#define NOTA_RT_SAFE noexcept
#endif
#else
#define NOTA_RT_SAFE noexcept
#endif
#endif

#ifndef NOTA_HOT
#if defined(__GNUC__) || defined(__clang__)
#define NOTA_HOT [[gnu::hot]]
#else
#define NOTA_HOT
#endif
#endif

#ifndef NOTA_NOINLINE
#if defined(_MSC_VER)
#define NOTA_NOINLINE __declspec(noinline)
#elif defined(__GNUC__) || defined(__clang__)
#define NOTA_NOINLINE __attribute__((noinline))
#else
#define NOTA_NOINLINE
#endif
#endif

#ifndef NOTA_ALWAYS_INLINE
#if defined(__SANITIZE_ADDRESS__) || defined(__SANITIZE_UNDEFINED__)
#define NOTA_ALWAYS_INLINE inline
#elif defined(__has_feature)
#if __has_feature(address_sanitizer) || __has_feature(undefined_behavior_sanitizer)
#define NOTA_ALWAYS_INLINE inline
#elif defined(__GNUC__) || defined(__clang__)
#define NOTA_ALWAYS_INLINE inline __attribute__((always_inline))
#else
#define NOTA_ALWAYS_INLINE inline
#endif
#elif defined(_MSC_VER)
#define NOTA_ALWAYS_INLINE __forceinline
#elif defined(__GNUC__) || defined(__clang__)
#define NOTA_ALWAYS_INLINE inline __attribute__((always_inline))
#else
#define NOTA_ALWAYS_INLINE inline
#endif
#endif

#ifndef NOTA_FORCE_INLINE_LAMBDA
#if defined(__GNUC__) || defined(__clang__)
#define NOTA_FORCE_INLINE_LAMBDA __attribute__((always_inline))
#else
#define NOTA_FORCE_INLINE_LAMBDA
#endif
#endif

namespace nota::dsp {
inline constexpr std::size_t kCacheLineSize = 64;
} // namespace nota::dsp

#ifndef NOTA_CACHE_ALIGNED
#define NOTA_CACHE_ALIGNED alignas(nota::dsp::kCacheLineSize)
#endif

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
