// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in EQ-3 (device kind 16) — a three-band DJ-style PERFORMANCE EQ, not a
// small EQ-8: three fixed bands (Low / Mid / High) split by two crossover frequencies,
// each band played by a gain fader with a centre (0 dB) detent and a KILL button that
// fully removes it. The split is a Linkwitz-Riley crossover tree (24 or 48 dB/oct) built
// from cascaded TPT state-variable Butterworth sections, so at unity gains the three
// bands reconstruct flat. A core Device (JUCE-free), lock-free params. All params are
// normalized 0..1 and denormalized in process(); persistence/automation/clone flow
// generically through the base Device. A pre-EQ mono scope feeds the UI spectrum.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class Eq3 : public Device {
public:
    enum {
        Low = 0,    // low-band gain   (0.5 = 0 dB, ±15 dB, centre detent)
        Mid,        // mid-band gain
        High,       // high-band gain
        LowKill,    // low band killed  (>= 0.5)
        MidKill,    // mid band killed
        HighKill,   // high band killed
        FreqLo,     // low/mid crossover  (exp 50..2000 Hz, default 250 Hz)
        FreqHi,     // mid/high crossover (exp 500..18000 Hz, default 2.5 kHz)
        Slope,      // 0 = 24 dB/oct (LR4), 1 = 48 dB/oct (LR8)
        Gain,       // output gain (0.5 = 0 dB, ±24 dB)
        kNumParams
    };

    Eq3() {
        p_[Low].store(0.5f);   p_[Mid].store(0.5f);  p_[High].store(0.5f);
        p_[LowKill].store(0.0f); p_[MidKill].store(0.0f); p_[HighKill].store(0.0f);
        p_[FreqLo].store(0.4363f);   // ~250 Hz
        p_[FreqHi].store(0.4491f);   // ~2.5 kHz
        p_[Slope].store(0.0f);       // 24 dB/oct
        p_[Gain].store(0.5f);        // 0 dB
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        for (auto& s : sec_) s.reset();
        gLcur_ = gMcur_ = gHcur_ = 1.0f;
        primed_ = false;
    }

    // Real-time analyzer feed: the pre-EQ mono signal, oldest→newest (like EQ-8 / Auto
    // Filter), for the UI spectrum behind the response curve. Lock-free; torn reads fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        const int32_t n = std::min(maxSamples, kScope);
        const uint32_t w = scopeW_.load(std::memory_order_relaxed);
        const uint32_t start = w - static_cast<uint32_t>(n);
        for (int32_t k = 0; k < n; ++k) out[k] = scope_[(start + (uint32_t)k) & (kScope - 1)];
        return n;
    }

    void process(float* buf, int32_t frames) override {
        // Crossover frequencies (keep f1 below f2 so the mid band stays sane).
        double f1 = expMap(get(FreqLo), 50.0, 2000.0);
        double f2 = expMap(get(FreqHi), 500.0, 18000.0);
        const double nyq = std::min(20000.0, sr_ * 0.45);
        f1 = std::clamp(f1, 20.0, nyq);
        f2 = std::clamp(f2, 20.0, nyq);
        if (f1 > f2 * 0.98) f1 = f2 * 0.98;

        const int nSec = get(Slope) >= 0.5f ? 4 : 2;   // LR4 (24 dB) vs LR8 (48 dB)

        // Butterworth section coeffs (Q = 1/√2) at each crossover.
        const double k = 1.41421356237; // 1/Q
        Coeffs c1 = coeffsFor(f1, k), c2 = coeffsFor(f2, k);

        // Per-band linear target gains (a KILL fully removes the band).
        const float gLtar = get(LowKill)  >= 0.5f ? 0.0f : dbToLin((get(Low)  - 0.5f) * 30.0f);
        const float gMtar = get(MidKill)  >= 0.5f ? 0.0f : dbToLin((get(Mid)  - 0.5f) * 30.0f);
        const float gHtar = get(HighKill) >= 0.5f ? 0.0f : dbToLin((get(High) - 0.5f) * 30.0f);
        if (!primed_) { gLcur_ = gLtar; gMcur_ = gMtar; gHcur_ = gHtar; primed_ = true; }
        const float smooth = (float)(1.0 - std::exp(-1.0 / (0.005 * sr_)));  // ~5 ms glide (click-free kills)
        const float outGain = std::pow(10.0f, (get(Gain) - 0.5f) * 48.0f / 20.0f);

        for (int32_t i = 0; i < frames; ++i) {
            const float l = buf[i * 2], r = buf[i * 2 + 1];

            // Publish the pre-EQ mono signal for the UI spectrum analyzer.
            const uint32_t sw = scopeW_.load(std::memory_order_relaxed);
            scope_[sw & (kScope - 1)] = 0.5f * (l + r);
            scopeW_.store(sw + 1, std::memory_order_relaxed);

            gLcur_ += (gLtar - gLcur_) * smooth;
            gMcur_ += (gMtar - gMcur_) * smooth;
            gHcur_ += (gHtar - gHcur_) * smooth;

            buf[i * 2]     = splitOne(0, l, nSec, c1, c2, outGain);
            buf[i * 2 + 1] = splitOne(1, r, nSec, c1, c2, outGain);
        }
    }

    const char* displayName() const override { return "Nota EQ-3"; }
    int32_t     builtinKind() const override { return 16; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Low: return "Low"; case Mid: return "Mid"; case High: return "High";
            case LowKill: return "Low Kill"; case MidKill: return "Mid Kill"; case HighKill: return "High Kill";
            case FreqLo: return "Low Freq"; case FreqHi: return "High Freq";
            case Slope: return "Slope"; case Gain: return "Gain";
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
    static constexpr int32_t kScope = 4096;   // power of two
    static constexpr int32_t kMaxSec = 4;     // cascade depth for LR8 (48 dB/oct)

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static float dbToLin(float db) { return std::pow(10.0f, db / 20.0f); }

    struct Coeffs { double a1, a2, a3, k; };
    Coeffs coeffsFor(double fc, double k) const {
        const double g = std::tan(3.14159265358979323846 * fc / sr_);
        const double a1 = 1.0 / (1.0 + g * (g + k));
        return { a1, g * a1, g * (g * a1), k };
    }

    // TPT state-variable filter section (one 2nd-order Butterworth stage).
    struct Svf {
        double ic1 = 0, ic2 = 0;
        void reset() { ic1 = ic2 = 0; }
        inline void tick(double x, const Coeffs& c, double& lp, double& hp) {
            const double v3 = x - ic2;
            const double v1 = c.a1 * ic1 + c.a2 * v3;
            const double v2 = ic2 + c.a2 * ic1 + c.a3 * v3;
            ic1 = 2.0 * v1 - ic1;
            ic2 = 2.0 * v2 - ic2;
            lp = v2; hp = x - c.k * v1 - v2;
        }
    };

    // Cascade `n` sections taking the lowpass (or highpass) output forward — n identical
    // Butterworth-2 stages give a 2n-th-order Linkwitz-Riley response.
    inline double cascadeLP(Svf* s, int n, double x, const Coeffs& c) {
        double lp, hp;
        for (int i = 0; i < n; ++i) { s[i].tick(x, c, lp, hp); x = lp; }
        return x;
    }
    inline double cascadeHP(Svf* s, int n, double x, const Coeffs& c) {
        double lp, hp;
        for (int i = 0; i < n; ++i) { s[i].tick(x, c, lp, hp); x = hp; }
        return x;
    }

    // Split one sample into three bands (serial LR crossover tree) and remix.
    inline float splitOne(int ch, float x, int nSec, const Coeffs& c1, const Coeffs& c2, float outGain) {
        Svf* g = sec_ + ch * (kMaxSec * 4);
        const double low  = cascadeLP(g + kMaxSec * 0, nSec, x, c1);
        const double rest = cascadeHP(g + kMaxSec * 1, nSec, x, c1);
        const double mid  = cascadeLP(g + kMaxSec * 2, nSec, rest, c2);
        const double high = cascadeHP(g + kMaxSec * 3, nSec, rest, c2);
        return (float)((gLcur_ * low + gMcur_ * mid + gHcur_ * high) * outGain);
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    // Per channel: 4 cascade groups (f1-LP, f1-HP, f2-LP, f2-HP) × kMaxSec sections.
    Svf sec_[2 * kMaxSec * 4];
    float gLcur_ = 1.0f, gMcur_ = 1.0f, gHcur_ = 1.0f;   // smoothed band gains (click-free)
    bool  primed_ = false;
    mutable float scope_[kScope] = {};                    // pre-EQ mono ring (UI spectrum)
    std::atomic<uint32_t> scopeW_{0};
};

} // namespace nota
