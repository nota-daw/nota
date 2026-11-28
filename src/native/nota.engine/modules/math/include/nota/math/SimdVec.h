// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SimdVec — portable 4-lane SIMD register abstractions (`Vec4f`, `Vec4i`, `Vec4u`)
// over ARM NEON, x86 SSE2/FMA, and scalar fallback (AR-6 safe, header-inline).

#pragma once

#include "nota/math/CpuFeatures.h"
#include "nota/math/Export.h"

#include <algorithm> // IWYU pragma: keep
#include <cmath>     // IWYU pragma: keep
#include <cstddef>
#include <cstdint>
#include <cstring>

#if defined(NOTA_SIMD_NEON)
#include <arm_neon.h>
#elif defined(NOTA_SIMD_SSE2)
#include <emmintrin.h>
#include <xmmintrin.h>
#if defined(__SSE4_1__)
#include <smmintrin.h>
#endif
#if defined(NOTA_SIMD_FMA)
#include <immintrin.h>
#endif
#endif

namespace nota::math {

template <typename T, std::size_t Lanes> struct Vec;

using Vec4f = Vec<float, 4>;
using Vec4i = Vec<int32_t, 4>;
using Vec4u = Vec<uint32_t, 4>;

template <> struct Vec<uint32_t, 4> {
    static constexpr int32_t kLanes = 4;

#if defined(NOTA_SIMD_NEON)
    using Raw = uint32x4_t;
#elif defined(NOTA_SIMD_SSE2)
    using Raw = __m128i;
#else
    struct Raw {
        uint32_t d[4]{};
    };
#endif
    Raw raw{};

    NOTA_ALWAYS_INLINE static Vec splat(uint32_t v) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vdupq_n_u32(v)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_set1_epi32(static_cast<int32_t>(v))};
#else
        return {{ { v, v, v, v } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec zero() noexcept { return splat(0u); }

    NOTA_ALWAYS_INLINE friend Vec operator&(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vandq_u32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_and_si128(a.raw, b.raw)};
#else
        return {
            { { a.raw.d[0] & b.raw.d[0],
                a.raw.d[1] & b.raw.d[1],
                a.raw.d[2] & b.raw.d[2],
                a.raw.d[3] & b.raw.d[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE friend Vec operator|(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vorrq_u32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_or_si128(a.raw, b.raw)};
#else
        return {
            { { a.raw.d[0] | b.raw.d[0],
                a.raw.d[1] | b.raw.d[1],
                a.raw.d[2] | b.raw.d[2],
                a.raw.d[3] | b.raw.d[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE friend Vec operator^(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {veorq_u32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_xor_si128(a.raw, b.raw)};
#else
        return {
            { { a.raw.d[0] ^ b.raw.d[0],
                a.raw.d[1] ^ b.raw.d[1],
                a.raw.d[2] ^ b.raw.d[2],
                a.raw.d[3] ^ b.raw.d[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE friend Vec operator==(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vceqq_u32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_cmpeq_epi32(a.raw, b.raw)};
#else
        return {
            { {a.raw.d[0] == b.raw.d[0] ? ~0u : 0u, a.raw.d[1] == b.raw.d[1] ? ~0u : 0u,
                a.raw.d[2] == b.raw.d[2] ? ~0u : 0u, a.raw.d[3] == b.raw.d[3] ? ~0u : 0u} }};
#endif
    }

    NOTA_ALWAYS_INLINE bool anyNonZero() const noexcept {
#if defined(NOTA_SIMD_NEON)
#if defined(__aarch64__)
        return vmaxvq_u32(raw) != 0u;
#else
        const uint64x2_t pair = vreinterpretq_u64_u32(raw);
        return (vgetq_lane_u64(pair, 0) | vgetq_lane_u64(pair, 1)) != 0ULL;
#endif
#elif defined(NOTA_SIMD_SSE2)
        return _mm_movemask_epi8(_mm_cmpeq_epi32(raw, _mm_setzero_si128())) != 0xffff;
#else
        return (raw.d[0] | raw.d[1] | raw.d[2] | raw.d[3]) != 0u;
#endif
    }

    template <int Bits> NOTA_ALWAYS_INLINE static Vec shiftLeft(Vec a) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vshlq_n_u32(a.raw, Bits)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_slli_epi32(a.raw, Bits)};
#else
        return {{ { a.raw.d[0] << Bits, a.raw.d[1] << Bits, a.raw.d[2] << Bits, a.raw.d[3] << Bits } }};
#endif
    }

    template <int Bits> NOTA_ALWAYS_INLINE static Vec shiftRight(Vec a) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vshrq_n_u32(a.raw, Bits)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_srli_epi32(a.raw, Bits)};
#else
        return {{ { a.raw.d[0] >> Bits, a.raw.d[1] >> Bits, a.raw.d[2] >> Bits, a.raw.d[3] >> Bits } }};
#endif
    }
};

template <> struct Vec<int32_t, 4> {
    static constexpr int32_t kLanes = 4;

#if defined(NOTA_SIMD_NEON)
    using Raw = int32x4_t;
#elif defined(NOTA_SIMD_SSE2)
    using Raw = __m128i;
#else
    struct Raw {
        int32_t d[4]{};
    };
#endif
    Raw raw{};

    NOTA_ALWAYS_INLINE static Vec splat(int32_t v) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vdupq_n_s32(v)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_set1_epi32(v)};
#else
        return {{ { v, v, v, v } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec zero() noexcept { return splat(0); }

    NOTA_ALWAYS_INLINE friend Vec operator+(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vaddq_s32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_add_epi32(a.raw, b.raw)};
#else
        return {
            { { a.raw.d[0] + b.raw.d[0],
                a.raw.d[1] + b.raw.d[1],
                a.raw.d[2] + b.raw.d[2],
                a.raw.d[3] + b.raw.d[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE friend Vec operator-(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vsubq_s32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_sub_epi32(a.raw, b.raw)};
#else
        return {
            { { a.raw.d[0] - b.raw.d[0],
                a.raw.d[1] - b.raw.d[1],
                a.raw.d[2] - b.raw.d[2],
                a.raw.d[3] - b.raw.d[3] } }};
#endif
    }

    template <int Bits> NOTA_ALWAYS_INLINE static Vec shiftLeft(Vec a) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vshlq_n_s32(a.raw, Bits)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_slli_epi32(a.raw, Bits)};
#else
        return {
            { { static_cast<int32_t>(static_cast<uint32_t>(a.raw.d[0]) << Bits),
                static_cast<int32_t>(static_cast<uint32_t>(a.raw.d[1]) << Bits),
                static_cast<int32_t>(static_cast<uint32_t>(a.raw.d[2]) << Bits),
                static_cast<int32_t>(static_cast<uint32_t>(a.raw.d[3]) << Bits) } }};
#endif
    }

    NOTA_ALWAYS_INLINE Vec4u asUnsigned() const noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vreinterpretq_u32_s32(raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {raw};
#else
        return {
            { { static_cast<uint32_t>(raw.d[0]),
                static_cast<uint32_t>(raw.d[1]),
                static_cast<uint32_t>(raw.d[2]),
                static_cast<uint32_t>(raw.d[3]) } }};
#endif
    }

    inline Vec4f toFloat() const noexcept;
};

template <> struct Vec<float, 4> {
    static constexpr int32_t kLanes = 4;

#if defined(NOTA_SIMD_NEON)
    using Raw = float32x4_t;
#elif defined(NOTA_SIMD_SSE2)
    using Raw = __m128;
#else
    struct Raw {
        float d[4]{};
    };
#endif
    Raw raw{};

    NOTA_ALWAYS_INLINE static Vec load(const float* p) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vld1q_f32(p)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_loadu_ps(p)};
#else
        return {{ { p[0], p[1], p[2], p[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec loadAligned(const float* p) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vld1q_f32(p)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_load_ps(p)};
#else
        return {{ { p[0], p[1], p[2], p[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE void store(float* p) const noexcept {
#if defined(NOTA_SIMD_NEON)
        vst1q_f32(p, raw);
#elif defined(NOTA_SIMD_SSE2)
        _mm_storeu_ps(p, raw);
#else
        p[0] = raw.d[0];
        p[1] = raw.d[1];
        p[2] = raw.d[2];
        p[3] = raw.d[3];
#endif
    }

    NOTA_ALWAYS_INLINE void storeAligned(float* p) const noexcept {
#if defined(NOTA_SIMD_NEON)
        vst1q_f32(p, raw);
#elif defined(NOTA_SIMD_SSE2)
        _mm_store_ps(p, raw);
#else
        p[0] = raw.d[0];
        p[1] = raw.d[1];
        p[2] = raw.d[2];
        p[3] = raw.d[3];
#endif
    }

    NOTA_ALWAYS_INLINE static Vec splat(float x) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vdupq_n_f32(x)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_set1_ps(x)};
#else
        return {{ { x, x, x, x } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec set(float e0, float e1, float e2, float e3) noexcept {
#if defined(NOTA_SIMD_NEON)
        const float arr[4] = {e0, e1, e2, e3};
        return {vld1q_f32(arr)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_set_ps(e3, e2, e1, e0)};
#else
        return {{ { e0, e1, e2, e3 } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec zero() noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vdupq_n_f32(0.0f)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_setzero_ps()};
#else
        return {{ {0.0f, 0.0f, 0.0f, 0.0f} }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec fromIntBits(Vec4i bits) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vreinterpretq_f32_s32(bits.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_castsi128_ps(bits.raw)};
#else
        Vec out{};
        std::memcpy(out.raw.d, bits.raw.d, sizeof(out.raw.d));
        return out;
#endif
    }

    NOTA_ALWAYS_INLINE static Vec fromUintBits(Vec4u bits) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vreinterpretq_f32_u32(bits.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_castsi128_ps(bits.raw)};
#else
        Vec out{};
        std::memcpy(out.raw.d, bits.raw.d, sizeof(out.raw.d));
        return out;
#endif
    }

    NOTA_ALWAYS_INLINE Vec4i toIntBits() const noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vreinterpretq_s32_f32(raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_castps_si128(raw)};
#else
        Vec4i out{};
        std::memcpy(out.raw.d, raw.d, sizeof(out.raw.d));
        return out;
#endif
    }

    NOTA_ALWAYS_INLINE Vec4u toUintBits() const noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vreinterpretq_u32_f32(raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_castps_si128(raw)};
#else
        Vec4u out{};
        std::memcpy(out.raw.d, raw.d, sizeof(out.raw.d));
        return out;
#endif
    }

    NOTA_ALWAYS_INLINE Vec4i truncateToInt() const noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vcvtq_s32_f32(raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_cvttps_epi32(raw)};
#else
        return {
            { { static_cast<int32_t>(raw.d[0]),
                static_cast<int32_t>(raw.d[1]),
                static_cast<int32_t>(raw.d[2]),
                static_cast<int32_t>(raw.d[3]) } }};
#endif
    }

    NOTA_ALWAYS_INLINE friend Vec operator+(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vaddq_f32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_add_ps(a.raw, b.raw)};
#else
        return {
            { { a.raw.d[0] + b.raw.d[0],
                a.raw.d[1] + b.raw.d[1],
                a.raw.d[2] + b.raw.d[2],
                a.raw.d[3] + b.raw.d[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE friend Vec operator-(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vsubq_f32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_sub_ps(a.raw, b.raw)};
#else
        return {
            { { a.raw.d[0] - b.raw.d[0],
                a.raw.d[1] - b.raw.d[1],
                a.raw.d[2] - b.raw.d[2],
                a.raw.d[3] - b.raw.d[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE friend Vec operator*(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vmulq_f32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_mul_ps(a.raw, b.raw)};
#else
        return {
            { { a.raw.d[0] * b.raw.d[0],
                a.raw.d[1] * b.raw.d[1],
                a.raw.d[2] * b.raw.d[2],
                a.raw.d[3] * b.raw.d[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE friend Vec operator/(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON) && defined(__aarch64__)
        return {vdivq_f32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_NEON)
        float tmp[4]{};
        vst1q_f32(tmp, a.raw);
        float div[4]{};
        vst1q_f32(div, b.raw);
        return set(tmp[0] / div[0], tmp[1] / div[1], tmp[2] / div[2], tmp[3] / div[3]);
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_div_ps(a.raw, b.raw)};
#else
        return {{{a.raw.d[0] / b.raw.d[0], a.raw.d[1] / b.raw.d[1], a.raw.d[2] / b.raw.d[2], a.raw.d[3] / b.raw.d[3]}}};
#endif
    }

    NOTA_ALWAYS_INLINE Vec operator-() const noexcept { return zero() - *this; }
    NOTA_ALWAYS_INLINE Vec& operator+=(Vec rhs) noexcept { return *this = *this + rhs; }
    NOTA_ALWAYS_INLINE Vec& operator-=(Vec rhs) noexcept { return *this = *this - rhs; }
    NOTA_ALWAYS_INLINE Vec& operator*=(Vec rhs) noexcept { return *this = *this * rhs; }

    // a * b + c
    NOTA_ALWAYS_INLINE static Vec fmadd(Vec a, Vec b, Vec c) noexcept {
#if defined(NOTA_SIMD_NEON)
#if defined(__ARM_FEATURE_FMA)
        return {vfmaq_f32(c.raw, a.raw, b.raw)};
#else
        return {vmlaq_f32(c.raw, a.raw, b.raw)};
#endif
#elif defined(NOTA_SIMD_SSE2) && defined(NOTA_SIMD_FMA)
        return {_mm_fmadd_ps(a.raw, b.raw, c.raw)};
#else
        return (a * b) + c;
#endif
    }

    // a * b - c
    NOTA_ALWAYS_INLINE static Vec fmsub(Vec a, Vec b, Vec c) noexcept {
#if defined(NOTA_SIMD_SSE2) && defined(NOTA_SIMD_FMA)
        return {_mm_fmsub_ps(a.raw, b.raw, c.raw)};
#else
        return (a * b) - c;
#endif
    }

    // c - a * b
    NOTA_ALWAYS_INLINE static Vec fnmadd(Vec a, Vec b, Vec c) noexcept {
#if defined(NOTA_SIMD_NEON)
#if defined(__ARM_FEATURE_FMA)
        return {vfmsq_f32(c.raw, a.raw, b.raw)};
#else
        return {vmlsq_f32(c.raw, a.raw, b.raw)};
#endif
#elif defined(NOTA_SIMD_SSE2) && defined(NOTA_SIMD_FMA)
        return {_mm_fnmadd_ps(a.raw, b.raw, c.raw)};
#else
        return c - (a * b);
#endif
    }

    NOTA_ALWAYS_INLINE static Vec abs(Vec v) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vabsq_f32(v.raw)};
#elif defined(NOTA_SIMD_SSE2)
        const __m128 mask = _mm_castsi128_ps(_mm_set1_epi32(0x7fffffff));
        return {_mm_and_ps(v.raw, mask)};
#else
        return {{ { std::fabs(v.raw.d[0]), std::fabs(v.raw.d[1]), std::fabs(v.raw.d[2]), std::fabs(v.raw.d[3]) } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec min(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vminq_f32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_min_ps(a.raw, b.raw)};
#else
        return {
            { { std::min(a.raw.d[0], b.raw.d[0]),
                std::min(a.raw.d[1], b.raw.d[1]),
                std::min(a.raw.d[2], b.raw.d[2]),
                std::min(a.raw.d[3], b.raw.d[3]) } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec max(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vmaxq_f32(a.raw, b.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_max_ps(a.raw, b.raw)};
#else
        return {
            { { std::max(a.raw.d[0], b.raw.d[0]),
                std::max(a.raw.d[1], b.raw.d[1]),
                std::max(a.raw.d[2], b.raw.d[2]),
                std::max(a.raw.d[3], b.raw.d[3]) } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec clamp(Vec v, Vec lo, Vec hi) noexcept { return min(max(v, lo), hi); }

    NOTA_ALWAYS_INLINE static Vec floor(Vec v) noexcept {
#if defined(NOTA_SIMD_NEON) && defined(__aarch64__)
        return {vrndmq_f32(v.raw)};
#elif defined(NOTA_SIMD_SSE2) && defined(__SSE4_1__)
        return {_mm_floor_ps(v.raw)};
#elif defined(NOTA_SIMD_SSE2)
        const __m128 limit = _mm_set1_ps(8388608.0f);
        const __m128 absV = abs(v).raw;
        const __m128 inRange = _mm_cmplt_ps(absV, limit);
        const __m128 trunc = _mm_cvtepi32_ps(_mm_cvttps_epi32(v.raw));
        const __m128 negAdj = _mm_and_ps(_mm_cmplt_ps(v.raw, trunc), _mm_set1_ps(1.0f));
        const __m128 fl = _mm_sub_ps(trunc, negAdj);
        return {_mm_or_ps(_mm_and_ps(inRange, fl), _mm_andnot_ps(inRange, v.raw))};
#else
        return {{{std::floor(v.raw.d[0]), std::floor(v.raw.d[1]), std::floor(v.raw.d[2]), std::floor(v.raw.d[3])}}};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec select(Vec mask, Vec ifTrue, Vec ifFalse) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vbslq_f32(vreinterpretq_u32_f32(mask.raw), ifTrue.raw, ifFalse.raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_or_ps(_mm_and_ps(mask.raw, ifTrue.raw), _mm_andnot_ps(mask.raw, ifFalse.raw))};
#else
        const uint32_t* m = reinterpret_cast<const uint32_t*>(mask.raw.d);
        return {
            { { m[0] ? ifTrue.raw.d[0] : ifFalse.raw.d[0],
                m[1] ? ifTrue.raw.d[1] : ifFalse.raw.d[1],
                m[2] ? ifTrue.raw.d[2] : ifFalse.raw.d[2],
                m[3] ? ifTrue.raw.d[3] : ifFalse.raw.d[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec lessThan(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vreinterpretq_f32_u32(vcltq_f32(a.raw, b.raw))};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_cmplt_ps(a.raw, b.raw)};
#else
        auto maskBit = [](bool c) -> float {
            uint32_t u = c ? ~0u : 0u;
            float f;
            std::memcpy(&f, &u, sizeof(f));
            return f;
        };
        return {
            { { maskBit(a.raw.d[0] < b.raw.d[0]),
                maskBit(a.raw.d[1] < b.raw.d[1]),
                maskBit(a.raw.d[2] < b.raw.d[2]),
                maskBit(a.raw.d[3] < b.raw.d[3]) } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec lessEqual(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vreinterpretq_f32_u32(vcleq_f32(a.raw, b.raw))};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_cmple_ps(a.raw, b.raw)};
#else
        auto maskBit = [](bool c) -> float {
            uint32_t u = c ? ~0u : 0u;
            float f;
            std::memcpy(&f, &u, sizeof(f));
            return f;
        };
        return {
            { { maskBit(a.raw.d[0] <= b.raw.d[0]),
                maskBit(a.raw.d[1] <= b.raw.d[1]),
                maskBit(a.raw.d[2] <= b.raw.d[2]),
                maskBit(a.raw.d[3] <= b.raw.d[3]) } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec greaterThan(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vreinterpretq_f32_u32(vcgtq_f32(a.raw, b.raw))};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_cmpgt_ps(a.raw, b.raw)};
#else
        auto maskBit = [](bool c) -> float {
            uint32_t u = c ? ~0u : 0u;
            float f;
            std::memcpy(&f, &u, sizeof(f));
            return f;
        };
        return {
            { { maskBit(a.raw.d[0] > b.raw.d[0]),
                maskBit(a.raw.d[1] > b.raw.d[1]),
                maskBit(a.raw.d[2] > b.raw.d[2]),
                maskBit(a.raw.d[3] > b.raw.d[3]) } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec greaterEqual(Vec a, Vec b) noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vreinterpretq_f32_u32(vcgeq_f32(a.raw, b.raw))};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_cmpge_ps(a.raw, b.raw)};
#else
        auto maskBit = [](bool c) -> float {
            uint32_t u = c ? ~0u : 0u;
            float f;
            std::memcpy(&f, &u, sizeof(f));
            return f;
        };
        return {
            { { maskBit(a.raw.d[0] >= b.raw.d[0]),
                maskBit(a.raw.d[1] >= b.raw.d[1]),
                maskBit(a.raw.d[2] >= b.raw.d[2]),
                maskBit(a.raw.d[3] >= b.raw.d[3]) } }};
#endif
    }

    NOTA_ALWAYS_INLINE static Vec squashNonFinite(Vec v) noexcept {
        const Vec4u expMask = Vec4u::splat(0x7f800000u);
        const Vec4u bits = v.toUintBits();
        const Vec4u bad = (bits & expMask) == expMask;
#if defined(NOTA_SIMD_NEON)
        return {vreinterpretq_f32_u32(vbicq_u32(bits.raw, bad.raw))};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_castsi128_ps(_mm_andnot_si128(bad.raw, bits.raw))};
#else
        return {
            { { bad.raw.d[0] ? 0.0f : v.raw.d[0],
                bad.raw.d[1] ? 0.0f : v.raw.d[1],
                bad.raw.d[2] ? 0.0f : v.raw.d[2],
                bad.raw.d[3] ? 0.0f : v.raw.d[3] } }};
#endif
    }

    NOTA_ALWAYS_INLINE float horizontalSum() const noexcept {
#if defined(NOTA_SIMD_NEON) && defined(__aarch64__)
        return vaddvq_f32(raw);
#elif defined(NOTA_SIMD_SSE2)
        __m128 shuf = _mm_shuffle_ps(raw, raw, _MM_SHUFFLE(2, 3, 0, 1));
        __m128 sums = _mm_add_ps(raw, shuf);
        shuf = _mm_movehl_ps(shuf, sums);
        sums = _mm_add_ss(sums, shuf);
        return _mm_cvtss_f32(sums);
#else
        alignas(16) float buf[4];
        storeAligned(buf);
        return buf[0] + buf[1] + buf[2] + buf[3];
#endif
    }

    NOTA_ALWAYS_INLINE float horizontalMax() const noexcept {
#if defined(NOTA_SIMD_NEON) && defined(__aarch64__)
        return vmaxvq_f32(raw);
#elif defined(NOTA_SIMD_SSE2)
        __m128 shuf = _mm_shuffle_ps(raw, raw, _MM_SHUFFLE(2, 3, 0, 1));
        __m128 mx = _mm_max_ps(raw, shuf);
        shuf = _mm_movehl_ps(shuf, mx);
        mx = _mm_max_ss(mx, shuf);
        return _mm_cvtss_f32(mx);
#else
        alignas(16) float buf[4];
        storeAligned(buf);
        return std::max(std::max(buf[0], buf[1]), std::max(buf[2], buf[3]));
#endif
    }

    // Swaps pairs: (0, 1, 2, 3) -> (1, 0, 3, 2)
    NOTA_ALWAYS_INLINE Vec swapAdjacent() const noexcept {
#if defined(NOTA_SIMD_NEON)
        return {vrev64q_f32(raw)};
#elif defined(NOTA_SIMD_SSE2)
        return {_mm_shuffle_ps(raw, raw, _MM_SHUFFLE(2, 3, 0, 1))};
#else
        return {{ { raw.d[1], raw.d[0], raw.d[3], raw.d[2] } }};
#endif
    }

    // Interleaved stereo helpers: loads/stores 4 stereo frames (8 floats) as planar L/R registers.
    NOTA_ALWAYS_INLINE static void deinterleave2(const float* interleaved, Vec& outL, Vec& outR) noexcept {
#if defined(NOTA_SIMD_NEON)
        const float32x4x2_t d = vld2q_f32(interleaved);
        outL = {d.val[0]};
        outR = {d.val[1]};
#elif defined(NOTA_SIMD_SSE2)
        const __m128 p0 = _mm_loadu_ps(interleaved);
        const __m128 p1 = _mm_loadu_ps(interleaved + 4);
        outL = {_mm_shuffle_ps(p0, p1, _MM_SHUFFLE(2, 0, 2, 0))};
        outR = {_mm_shuffle_ps(p0, p1, _MM_SHUFFLE(3, 1, 3, 1))};
#else
        outL = set(interleaved[0], interleaved[2], interleaved[4], interleaved[6]);
        outR = set(interleaved[1], interleaved[3], interleaved[5], interleaved[7]);
#endif
    }

    NOTA_ALWAYS_INLINE static void interleave2(Vec l, Vec r, float* outInterleaved) noexcept {
#if defined(NOTA_SIMD_NEON)
        const float32x4x2_t d{{ l.raw, r.raw }};
        vst2q_f32(outInterleaved, d);
#elif defined(NOTA_SIMD_SSE2)
        const __m128 lo = _mm_unpacklo_ps(l.raw, r.raw);
        const __m128 hi = _mm_unpackhi_ps(l.raw, r.raw);
        _mm_storeu_ps(outInterleaved, lo);
        _mm_storeu_ps(outInterleaved + 4, hi);
#else
        outInterleaved[0] = l.raw.d[0];
        outInterleaved[1] = r.raw.d[0];
        outInterleaved[2] = l.raw.d[1];
        outInterleaved[3] = r.raw.d[1];
        outInterleaved[4] = l.raw.d[2];
        outInterleaved[5] = r.raw.d[2];
        outInterleaved[6] = l.raw.d[3];
        outInterleaved[7] = r.raw.d[3];
#endif
    }
};

NOTA_ALWAYS_INLINE Vec4f Vec<int32_t, 4>::toFloat() const noexcept {
#if defined(NOTA_SIMD_NEON)
    return {vcvtq_f32_s32(raw)};
#elif defined(NOTA_SIMD_SSE2)
    return {_mm_cvtepi32_ps(raw)};
#else
    return Vec4f::set(static_cast<float>(raw.d[0]), static_cast<float>(raw.d[1]), static_cast<float>(raw.d[2]),
        static_cast<float>(raw.d[3]));
#endif
}

} // namespace nota::math
