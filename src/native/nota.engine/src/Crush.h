// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Built-in Nota Crush (device kind 12) — a bit crusher with a drawn quantiser:
// bit-depth reduction + sample-rate reduction, three modes (Digital hard truncate /
// Analog soft clip / Fold wavefold), a GRIT panel (dither / jitter / noise floor),
// an anti-alias toggle, a post filter, and dry/wet. All params normalized 0..1;
// persistence / automation / clone flow generically through the base Device.
// Header-only, allocation-free after construction. JUCE-free.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class Crush : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Bits = 0, Rate, Mode, Dither, Jitter, NoiseFloor, PostFilter, DryWet, AntiAlias, Output, Drive, kNumParams };

    Crush() {
        p_[Bits].store(0.55f);        // ~8 bit
        p_[Rate].store(0.35f);        // ~11 kHz
        p_[Mode].store(0.0f);         // Digital
        p_[Dither].store(0.15f);
        p_[Jitter].store(0.0f);
        p_[NoiseFloor].store(0.0f);
        p_[PostFilter].store(0.85f);  // ~12 kHz
        p_[DryWet].store(1.0f);
        p_[AntiAlias].store(0.0f);    // off
        p_[Output].store(0.5f);       // 0 dB
        p_[Drive].store(0.333f);      // ~0 dB pre-crush
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        holdL_ = holdR_ = 0.0f;
        cntL_ = cntR_ = 0;
        lpL_ = lpR_ = 0.0f;
        antiL_ = antiR_ = 0.0f;
    }

    void process(float* buf, int32_t frames) override {
        const float bitsV   = std::clamp(get(Bits), 0.0f, 1.0f);
        const float rateV   = std::clamp(get(Rate), 0.0f, 1.0f);
        const int   mode    = std::clamp((int)std::lround(get(Mode) * 2.0f), 0, 2);
        const float dither  = std::clamp(get(Dither), 0.0f, 1.0f);
        const float jitter  = std::clamp(get(Jitter), 0.0f, 1.0f);
        const float nf      = std::clamp(get(NoiseFloor), 0.0f, 1.0f);
        const float filtV   = std::clamp(get(PostFilter), 0.0f, 1.0f);
        const float wet     = std::clamp(get(DryWet), 0.0f, 1.0f);
        const bool  aa      = get(AntiAlias) >= 0.5f;
        const float outGain = std::pow(10.0f, (get(Output) - 0.5f) * 24.0f / 20.0f);   // ±12 dB
        const float drive   = std::pow(10.0f, (-12.0f + get(Drive) * 36.0f) / 20.0f);  // -12..+24 dB pre-crush

        // Bit depth: 1..24 bits, exp-mapped so the knob feels natural.
        const float bits = 1.0f + bitsV * 23.0f;
        const float levels = std::pow(2.0f, bits);
        const float scale = levels - 1.0f;
        const float invScale = 1.0f / scale;

        // Sample-rate reduction: hold N samples. rateV 0→0.5 kHz, 1→sr/2.
        const double targetHz = expMap(rateV, 500.0, sr_ * 0.48);
        const int holdMax = std::max(1, (int)std::lround(sr_ / targetHz));

        // Post filter: 1-pole lowpass, exp 200..20000 Hz.
        const double filtHz = expMap(filtV, 200.0, 20000.0);
        const float filtCoef = (float)std::exp(-2.0 * 3.14159265358979323846 * filtHz / sr_);
        const float filtGain = 1.0f - filtCoef;

        // Anti-alias filter: 1-pole lowpass at the Nyquist of the reduced rate.
        const double aaHz = std::min(targetHz * 0.45, sr_ * 0.45);
        const float aaCoef = (float)std::exp(-2.0 * 3.14159265358979323846 * aaHz / sr_);
        const float aaGain = 1.0f - aaCoef;

        // Dither amplitude: triangular PDF, ±0.5 LSB at full dither.
        const float dithAmp = dither * 0.5f * invScale;

        // Noise floor amplitude.
        const float nfAmp = nf * 0.003f;

        for (int32_t i = 0; i < frames; ++i) {
            const float dryL = buf[i * 2], dryR = buf[i * 2 + 1];

            // --- anti-alias filter (before downsampling) ---
            float inL = dryL * drive, inR = dryR * drive;
            if (aa) {
                antiL_ += aaGain * (inL - antiL_);
                antiR_ += aaGain * (inR - antiR_);
                inL = antiL_; inR = antiR_;
            }

            // --- sample-rate reduction: sample & hold ---
            int holdL = holdMax, holdR = holdMax;
            if (jitter > 0.0f) {
                float jit = jitter * 0.5f;
                holdL = holdMax + (int)((whiteUni() * 2.0f - 1.0f) * jit * holdMax);
                holdR = holdMax + (int)((whiteUni() * 2.0f - 1.0f) * jit * holdMax);
                holdL = std::max(1, holdL); holdR = std::max(1, holdR);
            }

            if (--cntL_ <= 0) { cntL_ = holdL; holdL_ = inL; }
            if (--cntR_ <= 0) { cntR_ = holdR; holdR_ = inR; }

            float qL = holdL_, qR = holdR_;

            // --- dither: triangular PDF noise before quantisation ---
            if (dithAmp > 0.0f) {
                float t1 = whiteUni(), t2 = whiteUni();
                qL += (t1 - t2) * dithAmp;
                t1 = whiteUni(); t2 = whiteUni();
                qR += (t1 - t2) * dithAmp;
            }

            // --- mode-dependent shaping before quantisation ---
            switch (mode) {
                case 1: // Analog: soft clip
                    qL = std::tanh(qL * 1.5f) / 0.905f;
                    qR = std::tanh(qR * 1.5f) / 0.905f;
                    break;
                case 2: // Fold: wavefold
                    qL = wavefold(qL * 2.5f, 0.7f);
                    qR = wavefold(qR * 2.5f, 0.7f);
                    break;
                default: break; // Digital: no shaping
            }

            // --- bit-depth reduction: quantise ---
            qL = std::round(qL * scale) * invScale;
            qR = std::round(qR * scale) * invScale;

            // --- noise floor ---
            if (nfAmp > 0.0f) {
                qL += (whiteUni() * 2.0f - 1.0f) * nfAmp;
                qR += (whiteUni() * 2.0f - 1.0f) * nfAmp;
            }

            // --- post filter ---
            lpL_ += filtGain * (qL - lpL_);
            lpR_ += filtGain * (qR - lpR_);

            // --- dry/wet + output gain ---
            buf[i * 2]     = (dryL * (1.0f - wet) + lpL_ * wet) * outGain;
            buf[i * 2 + 1] = (dryR * (1.0f - wet) + lpR_ * wet) * outGain;
        }
    }

    const char* displayName() const override { return "Nota Crush"; }
    int32_t     builtinKind() const override { return 12; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Bits: return "Bits"; case Rate: return "Rate"; case Mode: return "Mode";
            case Dither: return "Dither"; case Jitter: return "Jitter"; case NoiseFloor: return "Noise Floor";
            case PostFilter: return "Post Filter"; case DryWet: return "Dry/Wet";
            case AntiAlias: return "Anti-Alias"; case Output: return "Output"; case Drive: return "Drive";
            default: return "";
        }
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

private:
    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }

    static inline float wavefold(float x, float threshold) {
        const float limit = threshold * 4.0f;
        x = std::clamp(x, -limit, limit);
        int iter = 0;
        while (std::fabs(x) > threshold && iter < 8) {
            if (x > threshold) x = 2.0f * threshold - x;
            else if (x < -threshold) x = -2.0f * threshold - x;
            iter++;
        }
        return x / threshold;
    }

    float whiteUni() { rng_ = rng_ * 1664525u + 1013904223u; return (float)((rng_ >> 8) & 0xFFFFFF) / 16777216.0f; }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float holdL_ = 0.0f, holdR_ = 0.0f;
    int cntL_ = 0, cntR_ = 0;
    float lpL_ = 0.0f, lpR_ = 0.0f;
    float antiL_ = 0.0f, antiR_ = 0.0f;
    uint32_t rng_ = 0xDEADBEEFu;
};

} // namespace nota
