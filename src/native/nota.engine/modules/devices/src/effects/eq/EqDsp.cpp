// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota EQ-8 audio-thread DSP: SIMD stereo cascade, parameter glide, and gain/meter stage.

#include "Eq.h"

#include "common/DeviceMath.h"
#include "nota/dsp/primitives/BiquadCascade.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace nota {

void Eq::writeMidRing(float* ring, uint32_t mask, uint32_t w0, const float* buf, int32_t frames) noexcept {
    if (!ring || frames <= 0) return;
    const uint32_t idx = w0 & mask;
    const uint32_t cap = mask + 1u;
    const uint32_t first = std::min<uint32_t>(static_cast<uint32_t>(frames), cap - idx);
    for (uint32_t i = 0; i < first; ++i)
        ring[idx + i] = 0.5f * (buf[static_cast<size_t>(i) * 2] + buf[static_cast<size_t>(i) * 2 + 1]);
    const int32_t rest = frames - static_cast<int32_t>(first);
    for (int32_t i = 0; i < rest; ++i) {
        const size_t s = (static_cast<size_t>(first) + static_cast<size_t>(i)) * 2;
        ring[static_cast<uint32_t>(i) & mask] = 0.5f * (buf[s] + buf[s + 1]);
    }
}

void Eq::runFlatStereoChain(
    float* buf, int32_t frames, dsp::BiquadSection* chL, dsp::BiquadSection* chR, int n) noexcept {
    if (frames <= 0 || n <= 0) return;
#if defined(NOTA_SIMD_NEON)
    alignas(16) float64x2_t z1[kBands * kMaxSec];
    alignas(16) float64x2_t z2[kBands * kMaxSec];
    for (int k = 0; k < n; ++k) {
        const double z1v[2] = {chL[k].z1, chR[k].z1};
        const double z2v[2] = {chL[k].z2, chR[k].z2};
        z1[k] = vld1q_f64(z1v);
        z2[k] = vld1q_f64(z2v);
    }
    for (int32_t i = 0; i < frames; ++i) {
        float64x2_t x = vcvt_f64_f32(vld1_f32(buf + static_cast<size_t>(i) * 2));
        for (int k = 0; k < n; ++k) {
            const dsp::BiquadCoeffs& cf = chL[k].c;
            const float64x2_t y = vfmaq_n_f64(z1[k], x, cf.b0);
            const float64x2_t nz1 = vaddq_f64(vfmsq_n_f64(z2[k], y, cf.a1), vmulq_n_f64(x, cf.b1));
            const float64x2_t nz2 = vfmsq_n_f64(vmulq_n_f64(x, cf.b2), y, cf.a2);
            z1[k] = nz1;
            z2[k] = nz2;
            x = y;
        }
        vst1_f32(buf + static_cast<size_t>(i) * 2, vcvt_f32_f64(x));
    }
    for (int k = 0; k < n; ++k) {
        double z1v[2], z2v[2];
        vst1q_f64(z1v, z1[k]);
        vst1q_f64(z2v, z2[k]);
        chL[k].z1 = z1v[0];
        chR[k].z1 = z1v[1];
        chL[k].z2 = z2v[0];
        chR[k].z2 = z2v[1];
    }
#elif defined(NOTA_SIMD_SSE)
    alignas(16) __m128d z1[kBands * kMaxSec];
    alignas(16) __m128d z2[kBands * kMaxSec];
    for (int k = 0; k < n; ++k) {
        z1[k] = _mm_set_pd(chR[k].z1, chL[k].z1);
        z2[k] = _mm_set_pd(chR[k].z2, chL[k].z2);
    }
    for (int32_t i = 0; i < frames; ++i) {
        __m128d x = _mm_cvtps_pd(
            _mm_castsi128_ps(_mm_loadl_epi64(reinterpret_cast<const __m128i*>(buf + static_cast<size_t>(i) * 2))));
        for (int k = 0; k < n; ++k) {
            const dsp::BiquadCoeffs& cf = chL[k].c;
            const __m128d y = _mm_add_pd(_mm_mul_pd(_mm_set1_pd(cf.b0), x), z1[k]);
            const __m128d nz1 =
                _mm_add_pd(_mm_sub_pd(_mm_mul_pd(_mm_set1_pd(cf.b1), x), _mm_mul_pd(_mm_set1_pd(cf.a1), y)), z2[k]);
            const __m128d nz2 = _mm_sub_pd(_mm_mul_pd(_mm_set1_pd(cf.b2), x), _mm_mul_pd(_mm_set1_pd(cf.a2), y));
            z1[k] = nz1;
            z2[k] = nz2;
            x = y;
        }
        _mm_storel_pi(reinterpret_cast<__m64*>(buf + static_cast<size_t>(i) * 2), _mm_cvtpd_ps(x));
    }
    for (int k = 0; k < n; ++k) {
        alignas(16) double z1v[2], z2v[2];
        _mm_store_pd(z1v, z1[k]);
        _mm_store_pd(z2v, z2[k]);
        chL[k].z1 = z1v[0];
        chR[k].z1 = z1v[1];
        chL[k].z2 = z2v[0];
        chR[k].z2 = z2v[1];
    }
#else
    for (int32_t i = 0; i < frames; ++i) {
        double l = buf[static_cast<size_t>(i) * 2], r = buf[static_cast<size_t>(i) * 2 + 1];
        for (int k = 0; k < n; ++k) {
            l = chL[k].run(l);
            r = chR[k].run(r);
        }
        buf[static_cast<size_t>(i) * 2] = static_cast<float>(l);
        buf[static_cast<size_t>(i) * 2 + 1] = static_cast<float>(r);
    }
#endif
}

void Eq::process(float* buf, int32_t frames) {
    if (frames <= 0) return;
    const auto cpuTimer = blockTimer(frames, sr_);
    const double sr = sr_;
    const double scale = clampd(get(Scale), 0.0, 200.0) / 100.0;

    // Idle skip: silent input on a settled device leaves the buffer (already
    // silent) untouched; analyzer rings still get zeros so spectra decay.
    const bool inSilent = simd_audio::isSilent(buf, frames * 2);
    uint32_t w0 = ringW_.load(std::memory_order_relaxed);
    const bool ringOk0 = !ring_[0].empty();
    if (inSilent && idle_) {
        if (ringOk0) {
            const uint32_t cap = ringMask_ + 1u;
            if (static_cast<uint32_t>(frames) >= cap) {
                std::fill(ring_[0].begin(), ring_[0].end(), 0.0f);
                std::fill(ring_[1].begin(), ring_[1].end(), 0.0f);
            } else {
                const uint32_t idx = w0 & ringMask_;
                const uint32_t first = std::min<uint32_t>(static_cast<uint32_t>(frames), cap - idx);
                std::fill_n(ring_[0].data() + idx, first, 0.0f);
                std::fill_n(ring_[1].data() + idx, first, 0.0f);
                const int32_t rest = frames - static_cast<int32_t>(first);
                if (rest > 0) {
                    std::fill_n(ring_[0].data(), rest, 0.0f);
                    std::fill_n(ring_[1].data(), rest, 0.0f);
                }
            }
            ringW_.store(w0 + static_cast<uint32_t>(frames), std::memory_order_release);
        }
        const double blockS = frames / sr;
        const float fall = static_cast<float>(std::exp(-blockS / 0.3));
        inPk_ *= fall;
        outPk_ *= fall;
        inPkA_.store(db(inPk_), std::memory_order_relaxed);
        outPkA_.store(db(outPk_), std::memory_order_relaxed);
        return;
    }

    // Per-band config + coefficient cascades (cached across blocks when params & scale are static).
    bool on[kBands];
    int ty[kBands], ch[kBands], sl[kBands], nSec[kBands];
    double tF[kBands], tG[kBands], tQ[kBands]; // glide targets: log2 Hz, dB, log2 Q
    bool moving = false;
    for (int b = 0; b < kBands; ++b) {
        BandCache& bc = bandCache_[b];
        const float rawOn = bp(b, On);
        const float rawType = bp(b, Type);
        const float rawFreq = bp(b, Freq);
        const float rawGain = bp(b, Gain);
        const float rawQ = bp(b, Q);
        const float rawSlope = get(SlopeBase + b);
        const float rawCh = get(ChannelBase + b);
        const bool hit = bc.valid && bc.sr == sr && bc.scale == scale && bc.rawOn == rawOn && bc.rawType == rawType &&
                         bc.rawFreq == rawFreq && bc.rawGain == rawGain && bc.rawQ == rawQ && bc.rawSlope == rawSlope &&
                         bc.rawCh == rawCh;
        if (!hit) {
            bc.valid = true;
            bc.settled = false;
            bc.sr = sr;
            bc.scale = scale;
            bc.rawOn = rawOn;
            bc.rawType = rawType;
            bc.rawFreq = rawFreq;
            bc.rawGain = rawGain;
            bc.rawQ = rawQ;
            bc.rawSlope = rawSlope;
            bc.rawCh = rawCh;
            bc.on = rawOn > 0.5f;
            bc.ty = std::clamp(static_cast<int>(std::lround(rawType)), 0, kNumTypes - 1);
            bc.sl = std::clamp(static_cast<int>(std::lround(rawSlope)), 0, 2);
            bc.ch = std::clamp(static_cast<int>(std::lround(rawCh)), 0, kNumChannels - 1);
            bc.tF = fast_math::log2f(static_cast<float>(clampd(rawFreq, 20.0, sr * 0.49)));
            bc.tG = clampd(rawGain, -18.0, 18.0) * scale;
            bc.tQ = fast_math::log2f(static_cast<float>(clampd(rawQ, 0.1, 18.0)));
        }
        on[b] = bc.on;
        if (!on[b]) {
            cfgKey_[b] = -1;
            bc.settled = false;
            continue;
        }
        ty[b] = bc.ty;
        ch[b] = bc.ch;
        sl[b] = bc.sl;
        tF[b] = bc.tF;
        tG[b] = bc.tG;
        tQ[b] = bc.tQ;
        const bool fresh = !primed_ || cfgKey_[b] < 0;
        if (fresh) {
            glF_[b] = tF[b];
            glG_[b] = tG[b];
            glQ_[b] = tQ[b];
            bc.settled = false;
        } // fresh: no glide in
        const bool bandMoving = glF_[b] != tF[b] || glG_[b] != tG[b] || glQ_[b] != tQ[b];
        glMoving_[b] = bandMoving;
        moving |= bandMoving;
        if (bc.settled && !bandMoving && !fresh) {
            nSec[b] = bc.nSec;
            continue;
        }
        dsp::BiquadCoeffs cb[kMaxSec];
        nSec[b] = buildBand(cb, ty[b], sl[b], sr, fast_math::exp2f(static_cast<float>(glF_[b])), glG_[b],
            fast_math::exp2f(static_cast<float>(glQ_[b])));
        bc.nSec = nSec[b];
        // A band whose cascade length or channel changes starts clean (its old state means
        // something else); a type or frequency move keeps it, as the original EQ-8 did.
        const int key = nSec[b] * 10 + ch[b];
        if (key != cfgKey_[b]) {
            bands_[b].reset();
            cfgKey_[b] = key;
        }
        bands_[b].setStages(nSec[b]);
        for (int k = 0; k < nSec[b]; ++k) bands_[b].setSection(k, cb[k]);
        if (!bandMoving) {
            for (int k = 0; k < nSec[b]; ++k) bc.coeffs[k] = cb[k];
            bc.settled = true;
        } else {
            bc.settled = false;
        }
    }

    // Auto gain: the opposite of the average curve level, re-measured only when a param moves.
    const bool autoOn = get(AutoGain) >= 0.5f;
    if (autoOn) {
        bool moved = autoSr_ != sr;
        for (int i = 0; i < kAutoKeys && !moved; ++i) moved = autoKey_[i] != get(i);
        if (moved) {
            for (int i = 0; i < kAutoKeys; ++i) autoKey_[i] = get(i);
            autoSr_ = sr;
            dsp::BiquadCoeffs ct[kBands][kMaxSec]; // measured at the targets, not mid-glide
            for (int b = 0; b < kBands; ++b) {
                if (!on[b]) continue;
                const BandCache& bc = bandCache_[b];
                if (bc.settled) {
                    for (int k = 0; k < bc.nSec; ++k) ct[b][k] = bc.coeffs[k];
                } else {
                    buildBand(ct[b], ty[b], sl[b], sr, fast_math::exp2f(static_cast<float>(tF[b])), tG[b],
                        fast_math::exp2f(static_cast<float>(tQ[b])));
                }
            }
            autoDb_ = clampd(-averageDb(on, ty, ch, ct, sr), -12.0, 12.0);
        }
    }
    const double autoTarget = autoOn ? fast_math::dbToLin(static_cast<float>(autoDb_)) : 1.0;
    const double outTarget = fast_math::dbToLin(static_cast<float>(clampd(get(Output), -12.0, 12.0)));
    if (!primed_) {
        autoCur_ = autoTarget;
        outCur_ = outTarget;
        primed_ = true;
    }
    const double glide = 1.0 - std::exp(-1.0 / (0.02 * sr));                         // ~20 ms, click-free
    const double glideC = 1.0 - std::exp(-static_cast<double>(kCtrl) / (0.02 * sr)); // the same, per control step

    // Pre-pass: input peak (SIMD) + pre ring (the filter pass overwrites the buffer).
    const float inPk = simd_audio::peakAbs(buf, frames * 2);
    if (ringOk0) writeMidRing(ring_[0].data(), ringMask_, w0, buf, frames);

    // Bands in series, sample-major: every frame runs through all active bands before
    // the next, so the out-of-order core overlaps the bands' recurrences across frames
    // (band-major passes are each bound by one biquad's latency). Gliding bands rebuild
    // their coefficients every kCtrl frames.
    int act[kBands], nAct = 0;
    dsp::BiquadChannel rt[kBands];
    for (int b = 0; b < kBands; ++b)
        if (on[b]) {
            rt[b] = routing(ch[b]);
            act[nAct++] = b;
        }
    bool flat = !moving;
    for (int a = 0; a < nAct && flat; ++a) flat = rt[act[a]] == dsp::BiquadChannel::Stereo;
    if (flat) {
        // Common case — every active band Stereo and none gliding: run L/R in lockstep via
        // double2 SIMD from a contiguous copy, then store the filter state back.
        dsp::BiquadSection chL[kBands * kMaxSec], chR[kBands * kMaxSec];
        int n = 0;
        for (int a = 0; a < nAct; ++a) {
            dsp::BiquadCascade& bc = bands_[act[a]];
            for (int k = 0; k < bc.stages(); ++k, ++n) {
                chL[n] = bc.bank(0)[k];
                chR[n] = bc.bank(1)[k];
            }
        }
        if (n > 0) {
            runFlatStereoChain(buf, frames, chL, chR, n);
            n = 0;
            for (int a = 0; a < nAct; ++a) {
                dsp::BiquadCascade& bc = bands_[act[a]];
                for (int k = 0; k < bc.stages(); ++k, ++n) {
                    bc.bank(0)[k] = chL[n];
                    bc.bank(1)[k] = chR[n];
                }
            }
        }
    }
    for (int32_t off = 0; off < frames && !flat; off += kCtrl) {
        const int32_t m = off + kCtrl <= frames ? kCtrl : frames - off;
        for (int a = 0; a < nAct; ++a) {
            const int b = act[a];
            if (!glMoving_[b]) continue;
            glF_[b] = settle(glF_[b] + (tF[b] - glF_[b]) * glideC, tF[b], 1e-5);
            glG_[b] = settle(glG_[b] + (tG[b] - glG_[b]) * glideC, tG[b], 1e-4);
            glQ_[b] = settle(glQ_[b] + (tQ[b] - glQ_[b]) * glideC, tQ[b], 1e-5);
            dsp::BiquadCoeffs cb[kMaxSec];
            const int n = buildBand(cb, ty[b], sl[b], sr, fast_math::exp2f(static_cast<float>(glF_[b])), glG_[b],
                fast_math::exp2f(static_cast<float>(glQ_[b])));
            bands_[b].setStages(n);
            for (int k = 0; k < n; ++k) bands_[b].setSection(k, cb[k]);
            glMoving_[b] = glF_[b] != tF[b] || glG_[b] != tG[b] || glQ_[b] != tQ[b];
            if (!glMoving_[b]) {
                bandCache_[b].nSec = n;
                for (int k = 0; k < n; ++k) bandCache_[b].coeffs[k] = cb[k];
                bandCache_[b].settled = true;
            }
            moving = moving || glMoving_[b];
        }
        float* p = buf + off * 2;
        for (int32_t i = 0; i < m; ++i) {
            double l = p[i * 2], r = p[i * 2 + 1];
            for (int a = 0; a < nAct; ++a) bands_[act[a]].processFrame(l, r, rt[act[a]]);
            p[i * 2] = static_cast<float>(l);
            p[i * 2 + 1] = static_cast<float>(r);
        }
    }
    (void)moving;

    // Gain stage: smoothed auto/output gains, output peak, post ring.
    float outPk = 0.0f;
    const bool gainRamping = std::fabs(autoTarget - autoCur_) >= 1e-12 || std::fabs(outTarget - outCur_) >= 1e-12;
    if (!gainRamping) {
        autoCur_ = autoTarget;
        outCur_ = outTarget;
        const float g = static_cast<float>(autoCur_ * outCur_);
        if (std::fabs(g - 1.0f) > 1e-7f) simd_audio::scaleStereo(buf, buf, g, g, frames);
        outPk = simd_audio::peakAbs(buf, frames * 2);
        if (ringOk0) writeMidRing(ring_[1].data(), ringMask_, w0, buf, frames);
    } else {
        uint32_t w = w0;
        for (int32_t i = 0; i < frames; ++i) {
            autoCur_ += (autoTarget - autoCur_) * glide;
            outCur_ += (outTarget - outCur_) * glide;
            const double g = autoCur_ * outCur_;
            const float ol = static_cast<float>(buf[i * 2] * g), orr = static_cast<float>(buf[i * 2 + 1] * g);
            buf[i * 2] = ol;
            buf[i * 2 + 1] = orr;
            const float a = std::fabs(ol) > std::fabs(orr) ? std::fabs(ol) : std::fabs(orr);
            if (a > outPk) outPk = a;
            if (ringOk0) ring_[1][w & ringMask_] = 0.5f * (ol + orr);
            ++w;
        }
    }
    if (ringOk0) ringW_.store(w0 + static_cast<uint32_t>(frames), std::memory_order_release);

    // Sleep tracking: silent in + silent out accumulates the hangover.
    if (inSilent && outPk <= 1.0e-5f) quietSamples_ += static_cast<uint64_t>(frames);
    else
        quietSamples_ = 0;
    idle_ = quietSamples_ > kIdleSamples;

    const double blockS = frames > 0 ? frames / sr : 0.0;
    if (resetPk_.exchange(false, std::memory_order_acquire)) inPk_ = outPk_ = 0.0f;
    const float fall = static_cast<float>(std::exp(-blockS / 0.3));
    inPk_ = std::max(inPk, inPk_ * fall);
    outPk_ = std::max(outPk, outPk_ * fall);
    inPkA_.store(db(inPk_), std::memory_order_relaxed);
    outPkA_.store(db(outPk_), std::memory_order_relaxed);
    autoA_.store(autoOn ? static_cast<float>(autoDb_) : 0.0f, std::memory_order_relaxed);
}

} // namespace nota
