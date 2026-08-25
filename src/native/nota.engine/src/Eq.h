// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// EQ-8 — an eight-band parametric equalizer (mixing-console style). Each band
// has an on/off, a filter type (low cut / low shelf / bell / notch / high shelf /
// high cut), and freq / gain / Q. Four bands are enabled by default. Stereo, in
// place, RBJ biquads; coefficients are recomputed per block on the audio thread
// from atomic params, so setParam stays lock-free. A pre-EQ mono ring buffer is
// published for the UI's real-time spectrum analyzer (scopeRead, like the level
// meters / compressor GR — audio thread writes, UI reads lock-free).

#pragma once

#include "Device.h"

#include <algorithm>
#include <cstdio>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class Eq : public Device {
public:
    static constexpr int kBands = 8;
    static constexpr int kPerBand = 5;                 // On, Type, Freq, Gain, Q
    enum { On = 0, Type = 1, Freq = 2, Gain = 3, Q = 4 };
    // Filter types (per band).
    enum { LowCut = 0, LowShelf = 1, Bell = 2, Notch = 3, HighShelf = 4, HighCut = 5, kNumTypes = 6 };

    static constexpr int kNumParams = kBands * kPerBand;   // 40

    Eq() {
        // Four musically-spread bands enabled and flat; the rest are ready but off.
        setBand(0, 1, LowShelf,   100.0f,  0.0f, 0.70f);
        setBand(1, 1, Bell,       300.0f,  0.0f, 0.70f);
        setBand(2, 1, Bell,      2000.0f,  0.0f, 0.70f);
        setBand(3, 1, HighShelf, 8000.0f,  0.0f, 0.70f);
        setBand(4, 0, Bell,        60.0f,  0.0f, 0.70f);
        setBand(5, 0, Bell,       800.0f,  0.0f, 0.70f);
        setBand(6, 0, Bell,      5000.0f,  0.0f, 0.70f);
        setBand(7, 0, HighCut,  16000.0f,  0.0f, 0.70f);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override { sr_ = sr > 0 ? sr : 44100.0; }

    void process(float* buf, int32_t frames) override {
        const double sr = sr_;
        // Recompute per-band coefficients once per block.
        Coeffs c[kBands];
        bool   on[kBands];
        for (int b = 0; b < kBands; ++b) {
            on[b] = p_[b * kPerBand + On].load(std::memory_order_relaxed) > 0.5f;
            if (!on[b]) continue;
            const int    ty   = typeOf(b);
            const double f0   = clampd(p_[b * kPerBand + Freq].load(std::memory_order_relaxed), 20.0, sr * 0.49);
            const double gDb  = p_[b * kPerBand + Gain].load(std::memory_order_relaxed);
            const double q    = clampd(p_[b * kPerBand + Q].load(std::memory_order_relaxed), 0.1, 18.0);
            c[b] = coeffs(ty, sr, f0, gDb, q);
        }

        for (int32_t i = 0; i < frames; ++i) {
            // Publish the pre-EQ mono signal for the analyzer.
            const float mono = 0.5f * (buf[i * 2] + buf[i * 2 + 1]);
            const uint32_t w = scopeW_.load(std::memory_order_relaxed);
            scope_[w & (kScope - 1)] = mono;
            scopeW_.store(w + 1, std::memory_order_relaxed);

            for (int ch = 0; ch < 2; ++ch) {
                float x = buf[i * 2 + ch];
                for (int b = 0; b < kBands; ++b)
                    if (on[b]) x = s_[ch][b].run(c[b], x);
                buf[i * 2 + ch] = x;
            }
        }
    }

    const char* displayName() const override { return "Nota EQ-8"; }
    int32_t     builtinKind() const override { return 0; } // M7-6 (project compat)

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        if (i < 0 || i >= kNumParams) return "";
        static thread_local char buf[16];
        const int b = i / kPerBand, f = i % kPerBand;
        const char* fld = f == On ? "On" : f == Type ? "Type" : f == Freq ? "Freq" : f == Gain ? "Gain" : "Q";
        std::snprintf(buf, sizeof(buf), "%d %s", b + 1, fld);
        return buf;
    }
    float paramMin(int32_t i) const override {
        switch (i % kPerBand) { case On: return 0.0f; case Type: return 0.0f; case Freq: return 20.0f;
                                case Gain: return -18.0f; default: return 0.1f; }
    }
    float paramMax(int32_t i) const override {
        switch (i % kPerBand) { case On: return 1.0f; case Type: return kNumTypes - 1; case Freq: return 20000.0f;
                                case Gain: return 18.0f; default: return 18.0f; }
    }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed);
    }

    // Real-time analyzer feed: copy up to maxSamples of the pre-EQ mono signal into
    // out, oldest→newest. Returns the number written. Lock-free; torn reads are fine
    // for a visualizer.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        const int32_t n = std::min(maxSamples, kScope);
        const uint32_t w = scopeW_.load(std::memory_order_relaxed);
        const uint32_t start = w - static_cast<uint32_t>(n);
        for (int32_t k = 0; k < n; ++k)
            out[k] = scope_[(start + static_cast<uint32_t>(k)) & (kScope - 1)];
        return n;
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kScope = 4096;   // power of two

    struct Coeffs { double b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0; };
    struct State {
        double z1 = 0, z2 = 0;
        float run(const Coeffs& c, float xf) {
            double x = xf;
            double y = c.b0 * x + z1;
            z1 = c.b1 * x - c.a1 * y + z2;
            z2 = c.b2 * x - c.a2 * y;
            return static_cast<float>(y);
        }
    };

    int typeOf(int b) const {
        return std::clamp(static_cast<int>(std::lround(p_[b * kPerBand + Type].load(std::memory_order_relaxed))), 0, kNumTypes - 1);
    }
    void setBand(int b, float on, int type, float freq, float gain, float q) {
        p_[b * kPerBand + On].store(on);
        p_[b * kPerBand + Type].store(static_cast<float>(type));
        p_[b * kPerBand + Freq].store(freq);
        p_[b * kPerBand + Gain].store(gain);
        p_[b * kPerBand + Q].store(q);
    }

    static double clampd(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
    static Coeffs norm(double b0, double b1, double b2, double a0, double a1, double a2) {
        return {b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0};
    }

    static Coeffs coeffs(int type, double sr, double f0, double gainDb, double q) {
        switch (type) {
            case LowCut:    return highpass(sr, f0, q);
            case LowShelf:  return lowShelf(sr, f0, gainDb);
            case Notch:     return notch(sr, f0, q);
            case HighShelf: return highShelf(sr, f0, gainDb);
            case HighCut:   return lowpass(sr, f0, q);
            default:        return peaking(sr, f0, gainDb, q);
        }
    }
    static Coeffs peaking(double sr, double f0, double gainDb, double q) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return norm(1 + alpha * A, -2 * cw, 1 - alpha * A, 1 + alpha / A, -2 * cw, 1 - alpha / A);
    }
    static Coeffs notch(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return norm(1, -2 * cw, 1, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs highpass(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return norm((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs lowpass(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return norm((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs lowShelf(double sr, double f0, double gainDb) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / 2.0 * std::sqrt(2.0), tsa = 2.0 * std::sqrt(A) * alpha;
        return norm(A * ((A + 1) - (A - 1) * cw + tsa), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - tsa),
                    (A + 1) + (A - 1) * cw + tsa, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - tsa);
    }
    static Coeffs highShelf(double sr, double f0, double gainDb) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / 2.0 * std::sqrt(2.0), tsa = 2.0 * std::sqrt(A) * alpha;
        return norm(A * ((A + 1) + (A - 1) * cw + tsa), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - tsa),
                    (A + 1) - (A - 1) * cw + tsa, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - tsa);
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    State s_[2][kBands] = {};                      // [channel][band]
    mutable float scope_[kScope] = {};             // pre-EQ mono ring
    std::atomic<uint32_t> scopeW_{0};
};

} // namespace nota
