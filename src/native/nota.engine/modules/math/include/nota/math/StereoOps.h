// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// StereoOps — interleaved/planar stereo SIMD primitives and inline metering (AR-6 safe).

#pragma once

#include "nota/math/BufferOps.h"
#include "nota/math/Export.h"
#include "nota/math/SimdVec.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace nota::math {

struct StereoMeterAccumulator {
    Vec4f peakL = Vec4f::zero();
    Vec4f peakR = Vec4f::zero();
    Vec4f sqL = Vec4f::zero();
    Vec4f sqR = Vec4f::zero();
    float scalarPeakL = 0.0f;
    float scalarPeakR = 0.0f;
    double scalarSqL = 0.0;
    double scalarSqR = 0.0;

    NOTA_ALWAYS_INLINE void reset() noexcept {
        peakL = Vec4f::zero();
        peakR = Vec4f::zero();
        sqL = Vec4f::zero();
        sqR = Vec4f::zero();
        scalarPeakL = 0.0f;
        scalarPeakR = 0.0f;
        scalarSqL = 0.0;
        scalarSqR = 0.0;
    }

    NOTA_ALWAYS_INLINE void feed4(Vec4f l, Vec4f r) noexcept {
        peakL = Vec4f::max(peakL, Vec4f::abs(l));
        peakR = Vec4f::max(peakR, Vec4f::abs(r));
        sqL = Vec4f::fmadd(l, l, sqL);
        sqR = Vec4f::fmadd(r, r, sqR);
    }

    NOTA_ALWAYS_INLINE void feedScalar(float l, float r) noexcept {
        scalarPeakL = std::max(scalarPeakL, std::fabs(l));
        scalarPeakR = std::max(scalarPeakR, std::fabs(r));
        scalarSqL += static_cast<double>(l) * static_cast<double>(l);
        scalarSqR += static_cast<double>(r) * static_cast<double>(r);
    }

    NOTA_ALWAYS_INLINE void finalize(
        float& outPeakL, float& outPeakR, double& outSumSqL, double& outSumSqR) const noexcept {
        outPeakL = std::max(scalarPeakL, peakL.horizontalMax());
        outPeakR = std::max(scalarPeakR, peakR.horizontalMax());
        alignas(16) float bufL[4];
        alignas(16) float bufR[4];
        sqL.storeAligned(bufL);
        sqR.storeAligned(bufR);
        outSumSqL = scalarSqL + static_cast<double>(bufL[0]) + static_cast<double>(bufL[1]) +
                    static_cast<double>(bufL[2]) + static_cast<double>(bufL[3]);
        outSumSqR = scalarSqR + static_cast<double>(bufR[0]) + static_cast<double>(bufR[1]) +
                    static_cast<double>(bufR[2]) + static_cast<double>(bufR[3]);
    }
};

NOTA_ALWAYS_INLINE float peakAbs(const float* buf, int32_t count) noexcept {
    return maxAbs(buf, count);
}

NOTA_ALWAYS_INLINE void deinterleave(
    float* NOTA_RESTRICT l, float* NOTA_RESTRICT r, const float* NOTA_RESTRICT interleaved, int32_t frames) noexcept {
    if (!l || !r || !interleaved || frames <= 0) return;
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) {
        Vec4f vl, vr;
        Vec4f::deinterleave2(interleaved + i * 2, vl, vr);
        vl.store(l + i);
        vr.store(r + i);
    }
    for (; i < frames; ++i) {
        l[i] = interleaved[i * 2];
        r[i] = interleaved[i * 2 + 1];
    }
}

NOTA_ALWAYS_INLINE void interleave(float* NOTA_RESTRICT interleaved, const float* NOTA_RESTRICT l,
    const float* NOTA_RESTRICT r, int32_t frames) noexcept {
    if (!interleaved || !l || !r || frames <= 0) return;
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) { Vec4f::interleave2(Vec4f::load(l + i), Vec4f::load(r + i), interleaved + i * 2); }
    for (; i < frames; ++i) {
        interleaved[i * 2] = l[i];
        interleaved[i * 2 + 1] = r[i];
    }
}

NOTA_ALWAYS_INLINE void interleaveAdd(float* NOTA_RESTRICT interleaved, const float* NOTA_RESTRICT l,
    const float* NOTA_RESTRICT r, int32_t frames) noexcept {
    if (!interleaved || !l || !r || frames <= 0) return;
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) {
        Vec4f dl, dr;
        Vec4f::deinterleave2(interleaved + i * 2, dl, dr);
        Vec4f::interleave2(dl + Vec4f::load(l + i), dr + Vec4f::load(r + i), interleaved + i * 2);
    }
    for (; i < frames; ++i) {
        interleaved[i * 2] += l[i];
        interleaved[i * 2 + 1] += r[i];
    }
}

NOTA_ALWAYS_INLINE void scaleStereo(float* dst, const float* src, float gainL, float gainR, int32_t frames) noexcept {
    if (!dst || !src || frames <= 0) return;
    const Vec4f gains = Vec4f::set(gainL, gainR, gainL, gainR);
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) {
        (Vec4f::load(src + i * 2) * gains).store(dst + i * 2);
        (Vec4f::load(src + i * 2 + 4) * gains).store(dst + i * 2 + 4);
    }
    for (; i < frames; ++i) {
        dst[i * 2] = src[i * 2] * gainL;
        dst[i * 2 + 1] = src[i * 2 + 1] * gainR;
    }
}

NOTA_ALWAYS_INLINE void addStereoScaled(
    float* NOTA_RESTRICT dst, const float* NOTA_RESTRICT src, float gainL, float gainR, int32_t frames) noexcept {
    if (!dst || !src || frames <= 0) return;
    if (gainL == 0.0f && gainR == 0.0f) return;
    if (gainL == 1.0f && gainR == 1.0f) {
        add(dst, src, frames * 2);
        return;
    }
    const Vec4f gains = Vec4f::set(gainL, gainR, gainL, gainR);
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) {
        Vec4f::fmadd(Vec4f::load(src + i * 2), gains, Vec4f::load(dst + i * 2)).store(dst + i * 2);
        Vec4f::fmadd(Vec4f::load(src + i * 2 + 4), gains, Vec4f::load(dst + i * 2 + 4)).store(dst + i * 2 + 4);
    }
    for (; i < frames; ++i) {
        dst[i * 2] += src[i * 2] * gainL;
        dst[i * 2 + 1] += src[i * 2 + 1] * gainR;
    }
}

NOTA_ALWAYS_INLINE void interleaveWetDryMix(float* NOTA_RESTRICT interleaved, const float* NOTA_RESTRICT wetL,
    const float* NOTA_RESTRICT wetR, int32_t frames, float wetMix, float dryMix) noexcept {
    if (!interleaved || !wetL || !wetR || frames <= 0) return;
    const Vec4f vw = Vec4f::splat(wetMix);
    const Vec4f vd = Vec4f::splat(dryMix);
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) {
        Vec4f dl, dr;
        Vec4f::deinterleave2(interleaved + i * 2, dl, dr);
        const Vec4f ml = Vec4f::fmadd(Vec4f::load(wetL + i), vw, dl * vd);
        const Vec4f mr = Vec4f::fmadd(Vec4f::load(wetR + i), vw, dr * vd);
        Vec4f::interleave2(ml, mr, interleaved + i * 2);
    }
    for (; i < frames; ++i) {
        interleaved[i * 2] = interleaved[i * 2] * dryMix + wetL[i] * wetMix;
        interleaved[i * 2 + 1] = interleaved[i * 2 + 1] * dryMix + wetR[i] * wetMix;
    }
}

NOTA_ALWAYS_INLINE void mixPlanarWetDry(float* NOTA_RESTRICT dstL, float* NOTA_RESTRICT dstR,
    const float* NOTA_RESTRICT wetL, const float* NOTA_RESTRICT wetR, int32_t frames, float wetMix,
    float dryMix) noexcept {
    if (!dstL || !dstR || !wetL || !wetR || frames <= 0) return;
    const Vec4f vw = Vec4f::splat(wetMix);
    const Vec4f vd = Vec4f::splat(dryMix);
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) {
        Vec4f::fmadd(Vec4f::load(wetL + i), vw, Vec4f::load(dstL + i) * vd).store(dstL + i);
        Vec4f::fmadd(Vec4f::load(wetR + i), vw, Vec4f::load(dstR + i) * vd).store(dstR + i);
    }
    for (; i < frames; ++i) {
        dstL[i] = dstL[i] * dryMix + wetL[i] * wetMix;
        dstR[i] = dstR[i] * dryMix + wetR[i] * wetMix;
    }
}

NOTA_ALWAYS_INLINE void applyLinearGainRamp(
    float* dst, float startGainL, float endGainL, float startGainR, float endGainR, int32_t frames) noexcept {
    if (!dst || frames <= 0) return;
    const float invN = 1.0f / static_cast<float>(frames);
    const float stepL = (endGainL - startGainL) * invN;
    const float stepR = (endGainR - startGainR) * invN;
    Vec4f g = Vec4f::set(startGainL, startGainR, startGainL + stepL, startGainR + stepR);
    const Vec4f gStep = Vec4f::set(stepL * 2.0f, stepR * 2.0f, stepL * 2.0f, stepR * 2.0f);
    int32_t i = 0;
    for (; i + 2 <= frames; i += 2) {
        (Vec4f::load(dst + i * 2) * g).store(dst + i * 2);
        g += gStep;
    }
    if (i < frames) {
        const float gl = startGainL + stepL * static_cast<float>(i);
        const float gr = startGainR + stepR * static_cast<float>(i);
        dst[i * 2] *= gl;
        dst[i * 2 + 1] *= gr;
    }
}

NOTA_ALWAYS_INLINE void applyLinearGainRamp(float* dst, float startGain, float endGain, int32_t frames) noexcept {
    applyLinearGainRamp(dst, startGain, endGain, startGain, endGain, frames);
}

NOTA_ALWAYS_INLINE void scaleStereoMeter(float* dst, const float* src, float gainL, float gainR, int32_t frames,
    float& outPeakL, float& outPeakR, double& outSumSqL, double& outSumSqR) noexcept {
    StereoMeterAccumulator acc;
    if (!dst || !src || frames <= 0) {
        acc.finalize(outPeakL, outPeakR, outSumSqL, outSumSqR);
        return;
    }
    const Vec4f vgl = Vec4f::splat(gainL);
    const Vec4f vgr = Vec4f::splat(gainR);
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) {
        Vec4f l, r;
        Vec4f::deinterleave2(src + i * 2, l, r);
        l *= vgl;
        r *= vgr;
        acc.feed4(l, r);
        Vec4f::interleave2(l, r, dst + i * 2);
    }
    for (; i < frames; ++i) {
        const float l = src[i * 2] * gainL;
        const float r = src[i * 2 + 1] * gainR;
        dst[i * 2] = l;
        dst[i * 2 + 1] = r;
        acc.feedScalar(l, r);
    }
    acc.finalize(outPeakL, outPeakR, outSumSqL, outSumSqR);
}

NOTA_ALWAYS_INLINE void addStereoScaledMeter(float* NOTA_RESTRICT dst, const float* NOTA_RESTRICT src, float gainL,
    float gainR, int32_t frames, float& outPeakL, float& outPeakR, double& outSumSqL, double& outSumSqR) noexcept {
    StereoMeterAccumulator acc;
    if (!dst || !src || frames <= 0) {
        acc.finalize(outPeakL, outPeakR, outSumSqL, outSumSqR);
        return;
    }
    const Vec4f vgl = Vec4f::splat(gainL);
    const Vec4f vgr = Vec4f::splat(gainR);
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) {
        Vec4f sl, sr, dl, dr;
        Vec4f::deinterleave2(src + i * 2, sl, sr);
        Vec4f::deinterleave2(dst + i * 2, dl, dr);
        const Vec4f ol = sl * vgl;
        const Vec4f orr = sr * vgr;
        acc.feed4(ol, orr);
        Vec4f::interleave2(dl + ol, dr + orr, dst + i * 2);
    }
    for (; i < frames; ++i) {
        const float l = src[i * 2] * gainL;
        const float r = src[i * 2 + 1] * gainR;
        dst[i * 2] += l;
        dst[i * 2 + 1] += r;
        acc.feedScalar(l, r);
    }
    acc.finalize(outPeakL, outPeakR, outSumSqL, outSumSqR);
}

NOTA_ALWAYS_INLINE void addStereoScaledMeterMap(float* NOTA_RESTRICT dst, const float* NOTA_RESTRICT src, float gainLL,
    float gainRL, float gainLR, float gainRR, int32_t frames, float& outPeakL, float& outPeakR, double& outSumSqL,
    double& outSumSqR) noexcept {
    StereoMeterAccumulator acc;
    if (!dst || !src || frames <= 0) {
        acc.finalize(outPeakL, outPeakR, outSumSqL, outSumSqR);
        return;
    }
    const Vec4f gll = Vec4f::splat(gainLL);
    const Vec4f grl = Vec4f::splat(gainRL);
    const Vec4f glr = Vec4f::splat(gainLR);
    const Vec4f grr = Vec4f::splat(gainRR);
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) {
        Vec4f sl, sr, dl, dr;
        Vec4f::deinterleave2(src + i * 2, sl, sr);
        Vec4f::deinterleave2(dst + i * 2, dl, dr);
        const Vec4f ol = Vec4f::fmadd(sl, gll, sr * grl);
        const Vec4f orr = Vec4f::fmadd(sl, glr, sr * grr);
        acc.feed4(ol, orr);
        Vec4f::interleave2(dl + ol, dr + orr, dst + i * 2);
    }
    for (; i < frames; ++i) {
        const float sl = src[i * 2];
        const float sr = src[i * 2 + 1];
        const float ol = sl * gainLL + sr * gainRL;
        const float orr = sl * gainLR + sr * gainRR;
        dst[i * 2] += ol;
        dst[i * 2 + 1] += orr;
        acc.feedScalar(ol, orr);
    }
    acc.finalize(outPeakL, outPeakR, outSumSqL, outSumSqR);
}

NOTA_ALWAYS_INLINE void processMasterBus(float* interleaved, int32_t frames, float gainL, float gainR, bool hardClip,
    float clipLimit, float& outPeakL, float& outPeakR, double& outSumSqL, double& outSumSqR) noexcept {
    StereoMeterAccumulator acc;
    if (!interleaved || frames <= 0) {
        acc.finalize(outPeakL, outPeakR, outSumSqL, outSumSqR);
        return;
    }
    const Vec4f vgl = Vec4f::splat(gainL);
    const Vec4f vgr = Vec4f::splat(gainR);
    const Vec4f vLo = Vec4f::splat(-clipLimit);
    const Vec4f vHi = Vec4f::splat(clipLimit);
    int32_t i = 0;
    for (; i + 4 <= frames; i += 4) {
        Vec4f l, r;
        Vec4f::deinterleave2(interleaved + i * 2, l, r);
        l = Vec4f::squashNonFinite(l * vgl);
        r = Vec4f::squashNonFinite(r * vgr);
        if (hardClip) {
            l = Vec4f::clamp(l, vLo, vHi);
            r = Vec4f::clamp(r, vLo, vHi);
        }
        acc.feed4(l, r);
        Vec4f::interleave2(l, r, interleaved + i * 2);
    }
    for (; i < frames; ++i) {
        float l = interleaved[i * 2] * gainL;
        float r = interleaved[i * 2 + 1] * gainR;
        if (!std::isfinite(l)) l = 0.0f;
        if (!std::isfinite(r)) r = 0.0f;
        if (hardClip) {
            l = std::clamp(l, -clipLimit, clipLimit);
            r = std::clamp(r, -clipLimit, clipLimit);
        }
        interleaved[i * 2] = l;
        interleaved[i * 2 + 1] = r;
        acc.feedScalar(l, r);
    }
    acc.finalize(outPeakL, outPeakR, outSumSqL, outSumSqR);
}

} // namespace nota::math