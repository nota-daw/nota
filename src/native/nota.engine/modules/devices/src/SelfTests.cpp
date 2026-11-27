// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SelfTests — runtime verification helpers for built-in effects, SIMD math & ring-buffer wrapping.

#include "nota/devices/Register.h"

#include "effects/compressor/Compressor.h"
#include "effects/delay/Delay.h"
#include "effects/eq/Eq.h"
#include "effects/forge/Forge.h"
#include "effects/reverb/Reverb.h"
#include "effects/utility/Utility.h"
#include "nota/math/BufferOps.h"

#include <nota/dsp/primitives/BiquadCascade.h>
#include <nota/dsp/primitives/Convolution.h>
#include <nota/math/ComplexOps.h>
#include <nota/math/FastMath.h>
#include <nota/math/SimdVec.h>
#include <nota/math/StereoOps.h>

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdio>
#include <vector>

namespace nota::devices {

bool reverbCombWrapSelfTest() {
    return Reverb::combWrapSelfTest();
}

bool runBuiltinFxSelfTest() {
    constexpr int N = 2048;
    constexpr double sr = 44100.0;
    constexpr double kPi = 3.14159265358979323846;

    auto sine = [&](std::vector<float>& b, double f) {
        b.assign(static_cast<size_t>(N) * 2u, 0.0f);
        for (int i = 0; i < N; ++i) {
            const float s = 0.3f * static_cast<float>(std::sin(2.0 * kPi * f * i / sr));
            b[static_cast<size_t>(i) * 2u] = s;
            b[static_cast<size_t>(i) * 2u + 1u] = s;
        }
    };
    auto rms = [&](const std::vector<float>& b) {
        double s = 0.0;
        for (float v : b) s += static_cast<double>(v) * static_cast<double>(v);
        return std::sqrt(s / static_cast<double>(b.size()));
    };

    std::vector<float> a;
    sine(a, 440.0);
    const double in = rms(a);

    {
        Eq eq;
        eq.setSampleRate(sr, N);
        eq.process(a.data(), N);
    }
    const double flat = rms(a);

    std::vector<float> b2;
    sine(b2, 440.0);
    {
        Eq eq;
        eq.setSampleRate(sr, N);
        eq.setParam(2 * Eq::kPerBand + Eq::Freq, 440.0f);
        eq.setParam(2 * Eq::kPerBand + Eq::Gain, -18.0f);
        eq.setParam(2 * Eq::kPerBand + Eq::Q, 2.0f);
        eq.process(b2.data(), N);
    }
    const double cut = rms(b2);

    std::vector<float> c;
    sine(c, 440.0);
    float compGr = 0.0f;
    {
        Compressor comp;
        comp.setSampleRate(sr, N);
        comp.setParam(Compressor::Threshold, -24.0f);
        comp.process(c.data(), N);
        compGr = comp.gainReductionDb();
    }
    const double comp = rms(c);

    std::vector<float> rv;
    sine(rv, 440.0);
    {
        Reverb r;
        r.setSampleRate(sr, N);
        r.setParam(Reverb::PreDelay, 0.0f);
        r.setParam(Reverb::DryWet, 1.0f);
        r.process(rv.data(), N);
    }
    const double rev = rms(rv);

    std::vector<float> dl;
    sine(dl, 440.0);
    {
        Delay d;
        d.setSampleRate(sr, N);
        d.setParam(Delay::DryWet, 0.5f);
        d.setParam(Delay::SyncMode, 0.0f);
        d.setParam(Delay::TimeL, 0.01f);
        d.setParam(Delay::Feedback, 0.5f);
        d.process(dl.data(), N);
    }
    const double del = rms(dl);

    std::vector<float> ut;
    sine(ut, 440.0);
    {
        Utility u;
        u.setSampleRate(sr, N);
        u.setParam(Utility::Gain, 6.0f);
        u.process(ut.data(), N);
    }
    const double util = rms(ut);

    const double aliasHz = sr - 3.0 * (0.3 * sr);
    auto goertzel = [&](const std::vector<float>& b, double f) {
        const double w = 2.0 * kPi * f / sr;
        const double cw = std::cos(w);
        const double coef = 2.0 * cw;
        const double sw = std::sin(w);
        double s1 = 0.0;
        double s2 = 0.0;
        for (int i = 512; i < N; ++i) {
            const double x = b[static_cast<size_t>(i) * 2u];
            const double s0 = x + coef * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        const double re = s1 - s2 * cw;
        const double im = s2 * sw;
        return std::sqrt(re * re + im * im) / (N - 512);
    };
    auto forgeClip = [&](std::vector<float>& out, float os) {
        sine(out, 0.3 * sr);
        Forge f;
        f.setSampleRate(sr, N);
        f.setParam(Forge::Amount, 1.0f);
        f.setParam(Forge::Wet, 1.0f);
        f.setParam(Forge::Output, 0.5f);
        f.setParam(Forge::Bias, 0.5f);
        f.setParam(Forge::Routing, 0.0f);
        f.setParam(Forge::S1Type, 4.0f / 5.0f);
        f.setParam(Forge::S1Drive, 1.0f);
        f.setParam(Forge::S1On, 1.0f);
        f.setParam(Forge::S2On, 0.0f);
        f.setParam(Forge::S3On, 0.0f);
        f.setParam(Forge::Oversampling, os);
        f.process(out.data(), N);
    };
    std::vector<float> fg1;
    std::vector<float> fg8;
    forgeClip(fg1, 0.0f);
    forgeClip(fg8, 1.0f);
    const double alias1 = goertzel(fg1, aliasHz);
    const double alias8 = goertzel(fg8, aliasHz);
    const double forgeRms = rms(fg8);
    const double aliasDrop = alias1 > 1e-9 ? 20.0 * std::log10(alias8 / alias1) : 0.0;
    std::fprintf(stderr, "  Forge oversampling: alias 1x=%.5f  8x=%.5f  (%.1f dB)\n", alias1, alias8, aliasDrop);

    bool p4Ok = [&]() -> bool {
        for (int bins : {17, 513}) {
            std::vector<float> ar0(static_cast<size_t>(bins), 0.125f);
            std::vector<float> ai0(static_cast<size_t>(bins), -0.25f);
            std::vector<float> ar1 = ar0, ai1 = ai0, arRef = ar0, aiRef = ai0;
            std::vector<float> arRaw = ar0, aiRaw = ai0, arRawRef = ar0, aiRawRef = ai0;
            std::vector<float> xr(static_cast<size_t>(bins)), xi(static_cast<size_t>(bins));
            std::vector<float> hr(static_cast<size_t>(bins)), hi(static_cast<size_t>(bins));
            for (int i = 0; i < bins; ++i) {
                const size_t k = static_cast<size_t>(i);
                xr[k] = static_cast<float>((i % 53) - 26) * 0.02f;
                xi[k] = static_cast<float>(31 - (i % 61)) * 0.015f;
                hr[k] = static_cast<float>((i % 37) - 18) * 0.01f;
                hi[k] = static_cast<float>((i % 43) - 21) * 0.012f;
            }
            arRef[0] += xr[0] * hr[0];
            aiRef[0] += xi[0] * hi[0];
            for (int i = 1; i < bins; ++i) {
                const size_t k = static_cast<size_t>(i);
                arRef[k] += xr[k] * hr[k] - xi[k] * hi[k];
                aiRef[k] += xr[k] * hi[k] + xi[k] * hr[k];
            }
            for (int i = 0; i < bins; ++i) {
                const size_t k = static_cast<size_t>(i);
                arRawRef[k] += xr[k] * hr[k] - xi[k] * hi[k];
                aiRawRef[k] += xr[k] * hi[k] + xi[k] * hr[k];
            }
            dsp::accumulatePackedSpectrum(ar1.data(), ai1.data(), xr.data(), xi.data(), hr.data(), hi.data(), bins);
            math::complexMulAdd(arRaw.data(), aiRaw.data(), xr.data(), xi.data(), hr.data(), hi.data(), bins);
            for (size_t k = 0; k < static_cast<size_t>(bins); ++k) {
                if (std::fabs(ar1[k] - arRef[k]) > 1e-5f || std::fabs(ai1[k] - aiRef[k]) > 1e-5f ||
                    std::fabs(arRaw[k] - arRawRef[k]) > 1e-5f || std::fabs(aiRaw[k] - aiRawRef[k]) > 1e-5f) {
                    return false;
                }
            }
        }

        for (int count : {17, 64, 4096}) {
            std::vector<float> va(static_cast<size_t>(count)), vb(static_cast<size_t>(count));
            float pkRef = 0.0f;
            double sqRef = 0.0;
            double dotRef = 0.0;
            for (int i = 0; i < count; ++i) {
                const size_t k = static_cast<size_t>(i);
                va[k] = static_cast<float>((i % 97) - 48) * 0.015f;
                vb[k] = static_cast<float>(97 - (i % 97)) * 0.01f;
                pkRef = std::max(pkRef, std::fabs(va[k]));
                sqRef += static_cast<double>(va[k]) * static_cast<double>(va[k]);
                dotRef += static_cast<double>(va[k]) * static_cast<double>(vb[k]);
            }
            const float pkSimd = math::maxAbs(va.data(), count);
            const float pkAudio = math::peakAbs(va.data(), count);
            float p0 = 0.0f, p1 = 0.0f;
            double s0 = 0.0, s1 = 0.0;
            math::peakAndSumSquares(va.data(), count, p0, s0);
            math::peakAndSumSquares(va.data(), count, p1, s1);
            const float dotGot = math::dot(va.data(), vb.data(), count);
            const double sqTol = std::max(1e-3, sqRef * 1e-5);
            if (std::fabs(pkSimd - pkRef) > 1e-6f || std::fabs(pkAudio - pkRef) > 1e-6f ||
                std::fabs(p0 - pkRef) > 1e-6f || std::fabs(p1 - pkRef) > 1e-6f || std::fabs(s0 - sqRef) > sqTol ||
                std::fabs(s1 - sqRef) > sqTol ||
                std::fabs(static_cast<double>(dotGot) - dotRef) > std::max(1e-4, std::fabs(dotRef) * 1e-5)) {
                return false;
            }
        }
        return true;
    }();

    {
        Eq eqP4;
        eqP4.setSampleRate(sr, 512);
        auto cfgBand = [&](int b, float on, int ty, float freq, float gain, float q, int slope) {
            eqP4.setParam(b * Eq::kPerBand + Eq::On, on);
            eqP4.setParam(b * Eq::kPerBand + Eq::Type, static_cast<float>(ty));
            eqP4.setParam(b * Eq::kPerBand + Eq::Freq, freq);
            eqP4.setParam(b * Eq::kPerBand + Eq::Gain, gain);
            eqP4.setParam(b * Eq::kPerBand + Eq::Q, q);
            eqP4.setParam(Eq::SlopeBase + b, static_cast<float>(slope));
            eqP4.setParam(Eq::ChannelBase + b, static_cast<float>(Eq::ChStereo));
        };
        cfgBand(0, 1.0f, Eq::LowCut, 120.0f, 0.0f, 1.0f, 1);
        cfgBand(1, 1.0f, Eq::LowShelf, 250.0f, -3.5f, 0.7f, 0);
        cfgBand(2, 1.0f, Eq::Bell, 1200.0f, 5.5f, 1.4f, 0);
        cfgBand(3, 1.0f, Eq::Notch, 3500.0f, 0.0f, 3.0f, 0);
        cfgBand(4, 1.0f, Eq::HighShelf, 7500.0f, 2.5f, 0.7f, 0);
        cfgBand(5, 1.0f, Eq::HighCut, 14000.0f, 0.0f, 0.707f, 1);
        cfgBand(6, 0.0f, Eq::Bell, 800.0f, 0.0f, 0.7f, 0);
        cfgBand(7, 0.0f, Eq::Bell, 5000.0f, 0.0f, 0.7f, 0);

        std::vector<dsp::BiquadSection> refL, refR;
        auto pushBandRef = [&](int ty, double f, double g, double q, int slope) {
            const float f0 = math::exp2f(math::log2f(static_cast<float>(f)));
            const float q0 = math::exp2f(math::log2f(static_cast<float>(q)));
            if ((ty == Eq::LowCut || ty == Eq::HighCut) && slope > 0) {
                double bq[4];
                const int n = dsp::BiquadCascade::butterworthQs(bq, slope);
                const double res = q0 / 0.70710678;
                for (int k = 0; k < n; ++k) {
                    const double qk = std::clamp(k == n - 1 ? bq[k] * res : bq[k], 0.1, 40.0);
                    dsp::BiquadSection s;
                    s.c = ty == Eq::LowCut ? dsp::BiquadCascade::highpass(sr, f0, qk)
                                           : dsp::BiquadCascade::lowpass(sr, f0, qk);
                    refL.push_back(s);
                    refR.push_back(s);
                }
            } else {
                dsp::BiquadSection s;
                switch (ty) {
                case Eq::LowCut: s.c = dsp::BiquadCascade::highpass(sr, f0, q0); break;
                case Eq::LowShelf: s.c = dsp::BiquadCascade::lowShelf(sr, f0, g); break;
                case Eq::Notch: s.c = dsp::BiquadCascade::notch(sr, f0, q0); break;
                case Eq::HighShelf: s.c = dsp::BiquadCascade::highShelf(sr, f0, g); break;
                case Eq::HighCut: s.c = dsp::BiquadCascade::lowpass(sr, f0, q0); break;
                default: s.c = dsp::BiquadCascade::bell(sr, f0, g, q0); break;
                }
                refL.push_back(s);
                refR.push_back(s);
            }
        };
        pushBandRef(Eq::LowCut, 120.0, 0.0, 1.0, 1);
        pushBandRef(Eq::LowShelf, 250.0, -3.5, 0.7, 0);
        pushBandRef(Eq::Bell, 1200.0, 5.5, 1.4, 0);
        pushBandRef(Eq::Notch, 3500.0, 0.0, 3.0, 0);
        pushBandRef(Eq::HighShelf, 7500.0, 2.5, 0.7, 0);
        pushBandRef(Eq::HighCut, 14000.0, 0.0, 0.707, 1);

        double maxDiff = 0.0;
        for (int blk = 0; blk < 4; ++blk) {
            std::vector<float> test(512u * 2u), ref(512u * 2u);
            for (int i = 0; i < 512; ++i) {
                const int idx = blk * 512 + i;
                const float sl = 0.25f * static_cast<float>(std::sin(2.0 * kPi * 1100.0 * idx / sr)) +
                                 0.15f * static_cast<float>(std::sin(2.0 * kPi * 240.0 * idx / sr));
                const float rr = 0.20f * static_cast<float>(std::cos(2.0 * kPi * 6000.0 * idx / sr)) +
                                 0.10f * static_cast<float>(std::sin(2.0 * kPi * 3500.0 * idx / sr));
                test[static_cast<size_t>(i) * 2u] = sl;
                test[static_cast<size_t>(i) * 2u + 1u] = rr;
                double l = sl;
                double r = rr;
                for (auto& sec : refL) l = sec.run(l);
                for (auto& sec : refR) r = sec.run(r);
                ref[static_cast<size_t>(i) * 2u] = static_cast<float>(l);
                ref[static_cast<size_t>(i) * 2u + 1u] = static_cast<float>(r);
            }
            eqP4.process(test.data(), 512);
            for (size_t k = 0; k < test.size(); ++k) {
                if (!std::isfinite(test[k])) p4Ok = false;
                maxDiff = std::max(maxDiff, static_cast<double>(std::fabs(test[k] - ref[k])));
            }
        }
        p4Ok = p4Ok && maxDiff < 1.0e-6;
    }

    return p4Ok && flat > in * 0.7 && flat < in * 1.3 && cut < in * 0.7 && cut > in * 0.02 && comp > in * 0.1 &&
           compGr > 0.5f && rev > in * 0.02 && del > in * 0.3 && util > in * 1.5 && forgeRms > 0.05 &&
           std::isfinite(forgeRms) && alias8 < alias1 * 0.3;
}

} // namespace nota::devices
