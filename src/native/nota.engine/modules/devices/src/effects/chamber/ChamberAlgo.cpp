// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Chamber implementation. The bespoke
// zero-latency partitioned kernel stays (history-transplant morphing has no
// shared-primitive equivalent) while the FDN diffuser runs on the shared
// unrolled dsp::hadamard8 (identical 1/sqrt(8) unitary norm). FastMath only
// downstream: ducking gain (linToDb/dbToLin), wet-net softClip knee (in
// ChamberIrSynth), shimmer windows (sin). Delay reads, loop gains, xfade and
// control rate stay std::-exact. Silent tails auto-sleep (frozen loops
// excluded); kernel hand-off runs before the skip so IR re-shapes land.

#include "Chamber.h"

#include "common/DeviceMath.h"
#include "nota/dsp/primitives/Hadamard.h"
#include "effects/chamber/ChamberIrSynth.h"

#include <cmath>
#include <algorithm>
#include <signalsmith-linear/fft.h>

namespace nota {
namespace chamber {

float PitchShift::process(float x, float ratio) {
    d.push(x);
    phase += (1.0f - ratio) / static_cast<float>(W);
    phase -= std::floor(phase);
    float p2 = phase + 0.5f;
    if (p2 >= 1.0f) p2 -= 1.0f;
    float w1 = fast_math::sin(phase * 0.5f);
    w1 *= w1;
    return d.lin(1.0f + phase * (static_cast<float>(W - 4))) * w1 +
           d.lin(1.0f + p2 * (static_cast<float>(W - 4))) * (1.0f - w1);
}

void Algo::prepare(double sr) {
    sr_ = sr;
    for (auto& l : line_) l.init(static_cast<int>(0.5 * sr) + 64);
    for (int s = 0; s < 4; ++s)
        for (int i = 0; i < N; ++i) diff_[s][i].init(static_cast<int>(0.085 * sr) + 8);
    for (int i = 0; i < N; ++i) ap_[i].init(static_cast<int>(0.012 * sr) + 8);
    shL_.init(sr);
    shR_.init(sr);
    Rng r(0xC4A3B1u);
    for (int s = 0; s < 4; ++s)
        for (int i = 0; i < N; ++i) {
            frac_[s][i] = (static_cast<float>(i) + 0.15f + 0.7f * r.uni()) / static_cast<float>(N);
            flip_[s][i] = r.uni() < 0.5f ? -1.0f : 1.0f;
        }
    for (int i = 0; i < N; ++i) {
        const double ph = 2.0 * kPi * i / N;
        lfoC_[i] = static_cast<float>(std::cos(ph));
        lfoS_[i] = static_cast<float>(std::sin(ph));
    }
    primed_ = false;
    reset();
}

void Algo::reset() {
    for (auto& l : line_) l.clear();
    for (int s = 0; s < 4; ++s)
        for (auto& d : diff_[s]) d.clear();
    for (auto& a : ap_) a.clear();
    shL_.clear();
    shR_.clear();
    for (int i = 0; i < N; ++i) {
        bl1_[i] = bl2_[i] = 0.0f;
        inj_[i] = 0.0f;
    }
    vinL_[0] = vinL_[1] = vinR_[0] = vinR_[1] = 0.0f;
    hcL_ = hcR_ = 0.0f;
    shHpL_ = shHpR_ = shLpL_ = shLpR_ = 0.0f;
}

const Algo::Mode& Algo::mode(int m) {
    static const Mode cfg[4] = {
        {{43.1f, 49.7f, 55.3f, 62.9f, 69.4f, 77.9f, 85.1f, 93.7f}, 1.00f, 0.50f, 0.20f, 9000.0f, 2.40f}, // Dark Hall
        {{9.7f, 12.1f, 14.9f, 17.3f, 20.3f, 23.9f, 27.1f, 31.3f}, 0.40f, 0.62f, 0.00f, 18000.0f, 1.60f}, // Plate
        {{57.1f, 68.3f, 79.9f, 91.7f, 107.3f, 121.9f, 139.1f, 157.7f}, 0.55f, 0.00f, 0.50f, 15000.0f, 1.45f}, // Quartz
        {{47.3f, 56.1f, 63.7f, 71.9f, 81.3f, 89.9f, 98.3f, 109.1f}, 1.15f, 0.55f, 0.12f, 14000.0f, 2.20f},    // Shimmer
    };
    return cfg[std::clamp(m, 0, 3)];
}

void Algo::process(const float* inL, const float* inR, float* outL, float* outR, int n, const Params& p) {
    const Mode& mc = mode(p.mode);
    const double sr = sr_;
    const float size = std::clamp(p.size, 0.3f, 2.6f);
    float dT[N], ddT[4][N];
    for (int i = 0; i < N; ++i) dT[i] = std::max(4.0f, static_cast<float>(mc.dly[i] * 0.001 * sr * size));
    static const float kStage[4] = {5.3f, 10.9f, 21.7f, 41.3f};
    const float sScale = mc.diff * std::sqrt(size);
    for (int s = 0; s < 4; ++s)
        for (int i = 0; i < N; ++i) ddT[s][i] = static_cast<float>(kStage[s] * sScale * 0.001 * sr) * frac_[s][i];
    if (!primed_) { // first block: start at the targets instead of gliding from zero
        for (int i = 0; i < N; ++i) dCur_[i] = dT[i];
        for (int s = 0; s < 4; ++s)
            for (int i = 0; i < N; ++i) ddCur_[s][i] = ddT[s][i];
        primed_ = true;
    }
    // Three-band loop gains from the RT60s (per line, at its current length).
    const float a1 = onePole(250.0, sr), a2 = onePole(std::max(600.0f, p.damp), sr);
    // RT60s, with an empirical allowance for the loop's interpolation / modulation loss
    // (without it long tails measure ~20 % short).
    auto comp = [](float rt) { return rt * (1.0f + 0.028f * std::min(rt, 20.0f)); };
    const float rtM = comp(std::max(0.1f, p.decay)), rtL = comp(std::max(0.1f, p.decay * p.lowMul)),
                rtH = comp(std::max(0.05f, p.decay * p.highMul));
    float gL[N], gM[N], gH[N];
    for (int i = 0; i < N; ++i) {
        const double d = (dCur_[i] + kApMs[i] * 0.001 * sr) / sr;
        gL[i] = p.freeze ? 1.0f : std::min(0.9999f, static_cast<float>(std::pow(10.0, -3.0 * d / rtL)));
        gM[i] = p.freeze ? 1.0f : std::min(0.9999f, static_cast<float>(std::pow(10.0, -3.0 * d / rtM)));
        gH[i] = p.freeze ? 1.0f : std::min(0.9999f, static_cast<float>(std::pow(10.0, -3.0 * d / rtH)));
    }
    const float apG = mc.ap * (0.35f + 0.65f * p.diffuse);
    const float dmix = std::clamp(p.diffuse * (p.mode == 2 ? 0.6f : 1.0f), 0.0f, 1.0f);
    const float glide = static_cast<float>(1.0 - std::exp(-1.0 / (0.12 * sr)));
    const float md = p.depth * static_cast<float>(0.0011 * sr) * (p.vintage ? 2.0f : 1.0f);
    const double w0 = 2.0 * kPi * std::clamp(static_cast<double>(p.rate), 0.01, 20.0) / sr;
    const float rc = static_cast<float>(std::cos(w0)), rs = static_cast<float>(std::sin(w0));
    const float hc = onePole(p.vintage ? std::min(mc.hiCut, 8500.0f) : mc.hiCut, sr);
    const float vin = onePole(6500.0, sr);
    const bool shim = p.mode == 3 && p.shimAmt > 0.001f;
    // Shimmer fed back into the loop: keep the pitched loop gain below one — longer tails
    // ring more, so the injection shrinks with √RT; a frozen (lossless) loop takes none.
    const bool shimFb = p.shimFb && !p.freeze;
    const float shimLoop = 0.5f * std::min(1.0f, std::sqrt(2.5f / std::max(0.1f, p.decay)));
    const float shHp = onePole(250.0, sr), shLp = onePole(7000.0, sr);
    static const float sL[N] = {1, -1, 1, -1, 1, -1, 1, -1}, sR[N] = {1, 1, -1, -1, 1, 1, -1, -1};
    static const float inSign[N] = {1, 1, -1, 1, -1, -1, 1, -1};
    const float inG = 0.42f, outG = 0.30f * mc.gain; // per-mode gain: ~level-matched to the convolution
    const float erG = mc.er * 0.55f;
    int apLen[N];
    for (int i = 0; i < N; ++i) apLen[i] = std::max(1, static_cast<int>(kApMs[i] * 0.001 * sr));

    for (int t = 0; t < n; ++t) {
        float xL = inL[t], xR = inR[t];
        if (p.vintage) {
            vinL_[0] += vin * (xL - vinL_[0]);
            vinL_[1] += vin * (vinL_[0] - vinL_[1]);
            xL = vinL_[1];
            vinR_[0] += vin * (xR - vinR_[0]);
            vinR_[1] += vin * (vinR_[0] - vinR_[1]);
            xR = vinR_[1];
        }
        float ch[N], direct[N];
        for (int i = 0; i < N; ++i) {
            ch[i] = ((i & 1) ? xR : xL) * inSign[i];
            direct[i] = ch[i];
        }
        float eL = 0.0f, eR = 0.0f;
        for (int s = 0; s < 4; ++s) {
            for (int i = 0; i < N; ++i) {
                ddCur_[s][i] += glide * (ddT[s][i] - ddCur_[s][i]);
                diff_[s][i].push(ch[i]);
                ch[i] = diff_[s][i].lin(ddCur_[s][i]);
            }
            dsp::hadamard8(ch);
            for (int i = 0; i < N; ++i) ch[i] *= flip_[s][i];
            if (s == 1) {
                eL = ch[0] + ch[2] + ch[4] + ch[6];
                eR = ch[1] + ch[3] + ch[5] + ch[7];
            }
        }
        // LFO: one quadrature oscillator, eight phase-offset taps.
        const float nc = lfoCos_ * rc - lfoSin_ * rs;
        lfoSin_ = lfoSin_ * rc + lfoCos_ * rs;
        lfoCos_ = nc;
        float y[N], sum = 0.0f;
        for (int i = 0; i < N; ++i) {
            dCur_[i] += glide * (dT[i] - dCur_[i]);
            const float dd = std::max(2.0f, dCur_[i] + md * (lfoSin_ * lfoC_[i] + lfoCos_ * lfoS_[i]));
            float v = p.cubic ? line_[i].cubic(dd - 1.0f) : line_[i].lin(dd - 1.0f);
            // in-loop allpass (g = 0 → a plain short delay, so switching is seamless)
            const float z = ap_[i].tap(apLen[i] - 1);
            const float wv = v + apG * z;
            ap_[i].push(flush(wv));
            v = z - apG * wv;
            // three-band decay
            bl1_[i] += a1 * (v - bl1_[i]);
            bl2_[i] += a2 * (v - bl2_[i]);
            v = gL[i] * bl1_[i] + gM[i] * (bl2_[i] - bl1_[i]) + gH[i] * (v - bl2_[i]);
            y[i] = v;
            sum += v;
        }
        sum *= 2.0f / N;
        for (int i = 0; i < N; ++i) {
            float v = y[i] - sum + (direct[i] + (ch[i] - direct[i]) * dmix) * inG + inj_[i];
            if (p.vintage) v = std::floor(v * 8192.0f + 0.5f) * (1.0f / 8192.0f);
            line_[i].push(flush(loopLimit(v)));
        }
        float oL = 0.0f, oR = 0.0f;
        for (int i = 0; i < N; ++i) {
            oL += y[i] * sL[i];
            oR += y[i] * sR[i];
        }
        oL *= outG;
        oR *= outG;
        if (shim) {
            float a = shL_.process(oL, p.shimRatio), b = shR_.process(oR, p.shimRatio);
            shLpL_ += shLp * (a - shLpL_);
            shHpL_ += shHp * (shLpL_ - shHpL_);
            a = shLpL_ - shHpL_;
            shLpR_ += shLp * (b - shLpR_);
            shHpR_ += shHp * (shLpR_ - shHpR_);
            b = shLpR_ - shHpR_;
            if (shimFb) {
                const float g = p.shimAmt * shimLoop;
                for (int i = 0; i < N; ++i) inj_[i] = ((i & 1) ? b : a) * g * inSign[i];
                oL += a * p.shimAmt * 0.35f;
                oR += b * p.shimAmt * 0.35f;
            } else {
                for (int i = 0; i < N; ++i) inj_[i] = 0.0f;
                oL += a * p.shimAmt;
                oR += b * p.shimAmt;
            }
        } else {
            for (int i = 0; i < N; ++i) inj_[i] = 0.0f;
        }
        oL += eL * erG;
        oR += eR * erG;
        hcL_ += hc * (oL - hcL_);
        hcR_ += hc * (oR - hcR_);
        outL[t] = hcL_;
        outR[t] = hcR_;
    }
    const float mag = std::sqrt(lfoCos_ * lfoCos_ + lfoSin_ * lfoSin_); // keep the phasor on the unit circle
    if (mag > 1e-6f) {
        lfoCos_ /= mag;
        lfoSin_ /= mag;
    } else {
        lfoCos_ = 1.0f;
        lfoSin_ = 0.0f;
    }
}

float Biquad::run(int c, float x) {
    const float y = b0 * x + z1[c];
    z1[c] = flush(b1 * x - a1 * y + z2[c]);
    z2[c] = flush(b2 * x - a2 * y);
    return y;
}
void Biquad::set(double B0, double B1, double B2, double A0, double A1, double A2) {
    b0 = static_cast<float>(B0 / A0);
    b1 = static_cast<float>(B1 / A0);
    b2 = static_cast<float>(B2 / A0);
    a1 = static_cast<float>(A1 / A0);
    a2 = static_cast<float>(A2 / A0);
}
void Biquad::hp(double f, double sr) {
    const double w = 2 * kPi * f / sr, c = std::cos(w), al = std::sin(w) / (2 * 0.7071);
    set((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al);
}
void Biquad::lp(double f, double sr) {
    const double w = 2 * kPi * f / sr, c = std::cos(w), al = std::sin(w) / (2 * 0.7071);
    set((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al);
}
void Biquad::lowShelf(double f, double db, double sr) {
    const double A = std::pow(10.0, db / 40.0), w = 2 * kPi * f / sr, c = std::cos(w),
                 al = std::sin(w) / 2 * std::sqrt(2.0), sa = 2 * std::sqrt(A) * al;
    set(A * ((A + 1) - (A - 1) * c + sa), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) + (A - 1) * c - sa),
        (A + 1) + (A - 1) * c + sa, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sa);
}
void Biquad::highShelf(double f, double db, double sr) {
    const double A = std::pow(10.0, db / 40.0), w = 2 * kPi * f / sr, c = std::cos(w),
                 al = std::sin(w) / 2 * std::sqrt(2.0), sa = 2 * std::sqrt(A) * al;
    set(A * ((A + 1) + (A - 1) * c + sa), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sa),
        (A + 1) - (A - 1) * c + sa, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sa);
}

// ============================================================================
// Zero-latency partitioned convolution
// ============================================================================

} // namespace chamber

} // namespace nota
