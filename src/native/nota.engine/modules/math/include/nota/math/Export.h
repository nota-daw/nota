// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#if defined(_WIN32)
#if defined(NOTA_MATH_EXPORTS)
#define NOTA_MATH_API __declspec(dllexport)
#else
#define NOTA_MATH_API __declspec(dllimport)
#endif
#elif defined(__GNUC__) || defined(__clang__)
#define NOTA_MATH_API __attribute__((visibility("default")))
#else
#define NOTA_MATH_API
#endif

#ifndef NOTA_ALWAYS_INLINE
#if defined(__GNUC__) || defined(__clang__)
#define NOTA_ALWAYS_INLINE [[gnu::always_inline]] inline
#elif defined(_MSC_VER)
#define NOTA_ALWAYS_INLINE __forceinline
#else
#define NOTA_ALWAYS_INLINE inline
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

#ifndef NOTA_RESTRICT
#if defined(_MSC_VER)
#define NOTA_RESTRICT __restrict
#elif defined(__GNUC__) || defined(__clang__)
#define NOTA_RESTRICT __restrict__
#else
#define NOTA_RESTRICT
#endif
#endif
