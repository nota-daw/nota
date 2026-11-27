// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ComplexOps — SIMD SoA complex arithmetic and packed RealFFT spectrum ops (AR-6 safe).

#pragma once

#include "nota/math/Export.h"
#include "nota/math/SimdVec.h"

#include <cstdint>

namespace nota::math {

// ar[i] += xr[i]*hr[i] - xi[i]*hi[i]; ai[i] += xr[i]*hi[i] + xi[i]*hr[i]
NOTA_ALWAYS_INLINE void complexMulAdd(float* NOTA_RESTRICT ar, float* NOTA_RESTRICT ai, const float* NOTA_RESTRICT xr,
    const float* NOTA_RESTRICT xi, const float* NOTA_RESTRICT hr, const float* NOTA_RESTRICT hi,
    int32_t count) noexcept {
    if (!ar || !ai || !xr || !xi || !hr || !hi || count <= 0) return;
    int32_t i = 0;
    for (; i + 4 <= count; i += 4) {
        const Vec4f vr = Vec4f::load(xr + i);
        const Vec4f vi = Vec4f::load(xi + i);
        const Vec4f kr = Vec4f::load(hr + i);
        const Vec4f ki = Vec4f::load(hi + i);

        Vec4f outR = Vec4f::load(ar + i);
        outR = Vec4f::fmadd(vr, kr, outR);
        outR = Vec4f::fnmadd(vi, ki, outR);
        outR.store(ar + i);

        Vec4f outI = Vec4f::load(ai + i);
        outI = Vec4f::fmadd(vr, ki, outI);
        outI = Vec4f::fmadd(vi, kr, outI);
        outI.store(ai + i);
    }
    for (; i < count; ++i) {
        ar[i] += xr[i] * hr[i] - xi[i] * hi[i];
        ai[i] += xr[i] * hi[i] + xi[i] * hr[i];
    }
}

// dstR[i] = xr[i]*hr[i] - xi[i]*hi[i]; dstI[i] = xr[i]*hi[i] + xi[i]*hr[i]
NOTA_ALWAYS_INLINE void complexMul(float* NOTA_RESTRICT dstR, float* NOTA_RESTRICT dstI, const float* NOTA_RESTRICT xr,
    const float* NOTA_RESTRICT xi, const float* NOTA_RESTRICT hr, const float* NOTA_RESTRICT hi,
    int32_t count) noexcept {
    if (!dstR || !dstI || !xr || !xi || !hr || !hi || count <= 0) return;
    int32_t i = 0;
    for (; i + 4 <= count; i += 4) {
        const Vec4f vr = Vec4f::load(xr + i);
        const Vec4f vi = Vec4f::load(xi + i);
        const Vec4f kr = Vec4f::load(hr + i);
        const Vec4f ki = Vec4f::load(hi + i);

        Vec4f::fnmadd(vi, ki, vr * kr).store(dstR + i);
        Vec4f::fmadd(vi, kr, vr * ki).store(dstI + i);
    }
    for (; i < count; ++i) {
        dstR[i] = xr[i] * hr[i] - xi[i] * hi[i];
        dstI[i] = xr[i] * hi[i] + xi[i] * hr[i];
    }
}

// Packed RealFFT layout: bin 0 stores DC (real) and Nyquist (imag); bins 1..bins-1 are complex pairs.
NOTA_ALWAYS_INLINE void accumulatePackedSpectrum(float* NOTA_RESTRICT ar, float* NOTA_RESTRICT ai,
    const float* NOTA_RESTRICT xr, const float* NOTA_RESTRICT xi, const float* NOTA_RESTRICT hr,
    const float* NOTA_RESTRICT hi, int32_t bins) noexcept {
    if (!ar || !ai || !xr || !xi || !hr || !hi || bins <= 0) return;
    ar[0] += xr[0] * hr[0];
    ai[0] += xi[0] * hi[0];
    if (bins > 1) complexMulAdd(ar + 1, ai + 1, xr + 1, xi + 1, hr + 1, hi + 1, bins - 1);
}

} // namespace nota::math
