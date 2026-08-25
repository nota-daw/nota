// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Dynamic EQ-8 — an eight-band parametric equalizer where each band can also react
// to level, like a per-band compressor/expander (dynamic-EQ
// style). Static half mirrors Nota EQ-8: On / Type / Freq / Gain / Q, RBJ biquads,
// stereo in place. Dynamic half adds, per band: a Mode (Off / Above / Below), a
// Threshold, a signed Range (the extra dB applied at full engagement — negative
// ducks, positive lifts), and Attack / Release. A per-band bandpass detector tracks
// the energy in that band; the resulting gain (static + dynamic) drives the apply
// biquad, recomputed at control rate. Only shelves/bells take dynamic gain.
//
// scopeRead publishes the eight bands' momentary dynamic gain (dB, signed) so the UI
// can draw the live "momentary" response curve and per-band GR bars — audio thread
// writes, UI reads lock-free. Params are raw musical units (like EQ-8); persist /
// clone / automation are generic through the base Device (builtinKind + params).

#pragma once

#include "Device.h"

#include <algorithm>
#include <cstdio>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class DynamicEq : public Device {
public:
    static constexpr int kBands = 8;
    static constexpr int kPerBand = 10;
    // Per-band field offsets.
    enum { On = 0, Type = 1, Freq = 2, Gain = 3, Q = 4,
           Mode = 5, Thresh = 6, Range = 7, Attack = 8, Release = 9 };
    // Filter types (mirror Eq.h).
    enum { LowCut = 0, LowShelf = 1, Bell = 2, Notch = 3, HighShelf = 4, HighCut = 5, kNumTypes = 6 };
    // Dynamic modes.
    enum { DynOff = 0, DynAbove = 1, DynBelow = 2 };

    static constexpr int kBandParams = kBands * kPerBand;   // 80
    enum { Output = kBandParams, Sidechain = kBandParams + 1, Solo = kBandParams + 2 };
    static constexpr int kNumParams = kBandParams + 3;      // 83
    // Solo param encoding: 0 = none, n = audition band n-1 (others muted).

    DynamicEq() {
        // Neutral default: four musically-spread static bands enabled and flat, plus
        // ready HP/LP — no dynamics engaged, so adding the device is transparent. All
        // bands carry sensible dynamic defaults (thr/range/atk/rel) ready to switch on.
        band(0, 1, LowCut,      30.0f,  0.0f, 0.71f);
        band(1, 1, Bell,       120.0f,  0.0f, 1.00f);
        band(2, 1, Bell,       800.0f,  0.0f, 0.90f);
        band(3, 1, Bell,      3000.0f,  0.0f, 1.50f);
        band(4, 0, Bell,       200.0f,  0.0f, 1.00f);
        band(5, 0, Bell,      6000.0f,  0.0f, 1.20f);
        band(6, 1, HighShelf,10000.0f,  0.0f, 0.71f);
        band(7, 1, HighCut,  20000.0f,  0.0f, 0.71f);
        p_[Output].store(0.0f);
        p_[Sidechain].store(0.0f);
        p_[Solo].store(0.0f);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override { sr_ = sr > 0 ? sr : 44100.0; }

    bool acceptsSidechain() const override { return true; }
    void setSidechain(const float* interleaved, int32_t frames) override {
        scLen_ = 0;
        if (!interleaved || frames <= 0) return;
        const int32_t n = std::min(frames, kMaxSc);
        for (int32_t i = 0; i < n; ++i) sc_[i] = 0.5f * (interleaved[i * 2] + interleaved[i * 2 + 1]);
        scLen_ = n;
    }

    void process(float* buf, int32_t frames) override {
        const double sr = sr_;
        const bool scOn = p_[Sidechain].load(std::memory_order_relaxed) > 0.5f && scLen_ > 0;
        const double outGain = std::pow(10.0, clampd(p_[Output].load(std::memory_order_relaxed), -18.0, 18.0) / 20.0);

        // Per-band static config + detector coefficients (recomputed per block).
        bool   on[kBands], dyn[kBands], hasGain[kBands];
        int    mode[kBands];
        double f0[kBands], gStat[kBands], q[kBands], thr[kBands], range[kBands], atkC[kBands], relC[kBands];
        Coeffs det[kBands];
        for (int b = 0; b < kBands; ++b) {
            on[b]      = p_[b * kPerBand + On].load(std::memory_order_relaxed) > 0.5f;
            const int ty = typeOf(b);
            hasGain[b] = (ty == LowShelf || ty == Bell || ty == HighShelf);
            f0[b]      = clampd(p_[b * kPerBand + Freq].load(std::memory_order_relaxed), 20.0, sr * 0.49);
            gStat[b]   = p_[b * kPerBand + Gain].load(std::memory_order_relaxed);
            q[b]       = clampd(p_[b * kPerBand + Q].load(std::memory_order_relaxed), 0.1, 18.0);
            mode[b]    = modeOf(b);
            dyn[b]     = on[b] && hasGain[b] && mode[b] != DynOff;
            thr[b]     = clampd(p_[b * kPerBand + Thresh].load(std::memory_order_relaxed), -60.0, 6.0);
            range[b]   = clampd(p_[b * kPerBand + Range].load(std::memory_order_relaxed), -18.0, 18.0);
            const double atkMs = clampd(p_[b * kPerBand + Attack].load(std::memory_order_relaxed), 0.1, 300.0);
            const double relMs = clampd(p_[b * kPerBand + Release].load(std::memory_order_relaxed), 5.0, 2000.0);
            atkC[b]    = 1.0 - std::exp(-1.0 / (atkMs * 0.001 * sr));
            relC[b]    = 1.0 - std::exp(-1.0 / (relMs * 0.001 * sr));
            if (dyn[b]) det[b] = bandpass(sr, f0[b], std::max(q[b], 0.5));
            if (!dyn[b]) dynDb_[b] = 0.0;                                // parked bands read flat
        }
        // Solo: audition a single band (mute the rest, force the soloed one on).
        const int solo = std::clamp(static_cast<int>(std::lround(p_[Solo].load(std::memory_order_relaxed))), 0, kBands) - 1;
        if (solo >= 0) {
            for (int b = 0; b < kBands; ++b) { on[b] = (b == solo); if (!on[b]) dyn[b] = false; }
            on[solo] = true;
            dyn[solo] = hasGain[solo] && mode[solo] != DynOff;
        }
        const double detC = 1.0 - std::exp(-1.0 / (0.005 * sr));         // ~5 ms detector envelope

        // Apply coefficients — recomputed at control rate (dynamic gain moves them).
        Coeffs cf[kBands];
        for (int b = 0; b < kBands; ++b)
            if (on[b]) cf[b] = coeffs(typeOf(b), sr, f0[b], gStat[b] + dynDb_[b], q[b]);

        int ctrl = 0;
        for (int32_t i = 0; i < frames; ++i) {
            // --- detection + per-band dynamic gain ---
            const float detMono = scOn ? sc_[std::min(i, scLen_ - 1)]
                                       : 0.5f * (buf[i * 2] + buf[i * 2 + 1]);
            for (int b = 0; b < kBands; ++b) {
                if (!dyn[b]) continue;
                const double d = detState_[b].run(det[b], detMono);
                detEnv_[b] += (std::fabs(d) - detEnv_[b]) * detC;
                const double lvl = 20.0 * std::log10(detEnv_[b] + 1e-9);
                const double over = (mode[b] == DynAbove) ? (lvl - thr[b]) : (thr[b] - lvl);
                const double engage = clampd(over / kKneeDb, 0.0, 1.0);
                const double target = range[b] * engage;
                const double c = (std::fabs(target) > std::fabs(dynDb_[b])) ? atkC[b] : relC[b];
                dynDb_[b] += (target - dynDb_[b]) * c;
            }

            // --- refresh apply coeffs for dynamic bands at control rate ---
            if (--ctrl <= 0) {
                ctrl = kCtrl;
                for (int b = 0; b < kBands; ++b)
                    if (dyn[b]) cf[b] = coeffs(typeOf(b), sr, f0[b], gStat[b] + dynDb_[b], q[b]);
            }

            // --- apply cascade, both channels ---
            for (int ch = 0; ch < 2; ++ch) {
                double x = buf[i * 2 + ch];
                for (int b = 0; b < kBands; ++b)
                    if (on[b]) x = s_[ch][b].run(cf[b], x);
                buf[i * 2 + ch] = static_cast<float>(x * outGain);
            }
        }

        // Publish the momentary dynamic gain for the UI (signed dB, per band).
        for (int b = 0; b < kBands; ++b)
            gr_[b].store(static_cast<float>(dynDb_[b]), std::memory_order_relaxed);
    }

    const char* displayName() const override { return "Nota Dynamic EQ-8"; }
    int32_t     builtinKind() const override { return 13; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        if (i == Output) return "Output";
        if (i == Sidechain) return "Sidechain";
        if (i == Solo) return "Solo";
        if (i < 0 || i >= kBandParams) return "";
        static thread_local char buf[16];
        const int b = i / kPerBand, f = i % kPerBand;
        const char* fld = f == On ? "On" : f == Type ? "Type" : f == Freq ? "Freq" : f == Gain ? "Gain"
                        : f == Q ? "Q" : f == Mode ? "Mode" : f == Thresh ? "Thr" : f == Range ? "Rng"
                        : f == Attack ? "Atk" : "Rel";
        std::snprintf(buf, sizeof(buf), "%d %s", b + 1, fld);
        return buf;
    }
    float paramMin(int32_t i) const override {
        if (i == Output) return -18.0f;
        if (i == Sidechain || i == Solo) return 0.0f;
        switch (i % kPerBand) {
            case On: case Mode: return 0.0f;
            case Type: return 0.0f;
            case Freq: return 20.0f;
            case Gain: return -18.0f;
            case Q: return 0.1f;
            case Thresh: return -60.0f;
            case Range: return -18.0f;
            case Attack: return 0.1f;
            default: return 5.0f;                          // Release
        }
    }
    float paramMax(int32_t i) const override {
        if (i == Output) return 18.0f;
        if (i == Sidechain) return 1.0f;
        if (i == Solo) return kBands;
        switch (i % kPerBand) {
            case On: return 1.0f;
            case Type: return kNumTypes - 1;
            case Freq: return 20000.0f;
            case Gain: return 18.0f;
            case Q: return 18.0f;
            case Mode: return 2.0f;
            case Thresh: return 6.0f;
            case Range: return 18.0f;
            case Attack: return 300.0f;
            default: return 2000.0f;                       // Release
        }
    }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed);
    }

    // Telemetry: eight momentary dynamic-gain values (dB, signed), band 0..7.
    int32_t scopeRead(float* out, int32_t maxSamples) const override {
        if (!out || maxSamples <= 0) return 0;
        const int32_t n = std::min(maxSamples, kBands);
        for (int32_t b = 0; b < n; ++b) out[b] = gr_[b].load(std::memory_order_relaxed);
        return n;
    }

    // Summary GR (largest reduction across bands) for the shell meter.
    float gainReductionDb() const override {
        float g = 0.0f;
        for (int b = 0; b < kBands; ++b) { float v = gr_[b].load(std::memory_order_relaxed); if (v < g) g = v; }
        return g;
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kCtrl = 16;         // control-rate coeff refresh (samples)
    static constexpr double kKneeDb = 6.0;   // detection range from threshold to full engagement
    static constexpr int kMaxSc = 8192;

    struct Coeffs { double b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0; };
    struct State {
        double z1 = 0, z2 = 0;
        double run(const Coeffs& c, double x) {
            double y = c.b0 * x + z1;
            z1 = c.b1 * x - c.a1 * y + z2;
            z2 = c.b2 * x - c.a2 * y;
            return y;
        }
    };

    int typeOf(int b) const {
        return std::clamp(static_cast<int>(std::lround(p_[b * kPerBand + Type].load(std::memory_order_relaxed))), 0, kNumTypes - 1);
    }
    int modeOf(int b) const {
        return std::clamp(static_cast<int>(std::lround(p_[b * kPerBand + Mode].load(std::memory_order_relaxed))), 0, 2);
    }
    void band(int b, float on, int type, float freq, float gain, float q,
              int mode = DynOff, float thr = -24.0f, float range = 0.0f, float atk = 10.0f, float rel = 120.0f) {
        p_[b * kPerBand + On].store(on);
        p_[b * kPerBand + Type].store(static_cast<float>(type));
        p_[b * kPerBand + Freq].store(freq);
        p_[b * kPerBand + Gain].store(gain);
        p_[b * kPerBand + Q].store(q);
        p_[b * kPerBand + Mode].store(static_cast<float>(mode));
        p_[b * kPerBand + Thresh].store(thr);
        p_[b * kPerBand + Range].store(range);
        p_[b * kPerBand + Attack].store(atk);
        p_[b * kPerBand + Release].store(rel);
    }

    static double clampd(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
    static Coeffs normC(double b0, double b1, double b2, double a0, double a1, double a2) {
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
    static Coeffs bandpass(double sr, double f0, double q) {   // constant 0 dB peak BPF (detector)
        const double w0 = 2.0 * kPi * clampd(f0, 20.0, sr * 0.49) / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return normC(alpha, 0.0, -alpha, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs peaking(double sr, double f0, double gainDb, double q) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return normC(1 + alpha * A, -2 * cw, 1 - alpha * A, 1 + alpha / A, -2 * cw, 1 - alpha / A);
    }
    static Coeffs notch(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return normC(1, -2 * cw, 1, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs highpass(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return normC((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs lowpass(double sr, double f0, double q) {
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / (2.0 * q);
        return normC((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + alpha, -2 * cw, 1 - alpha);
    }
    static Coeffs lowShelf(double sr, double f0, double gainDb) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / 2.0 * std::sqrt(2.0), tsa = 2.0 * std::sqrt(A) * alpha;
        return normC(A * ((A + 1) - (A - 1) * cw + tsa), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - tsa),
                     (A + 1) + (A - 1) * cw + tsa, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - tsa);
    }
    static Coeffs highShelf(double sr, double f0, double gainDb) {
        const double A = std::pow(10.0, gainDb / 40.0);
        const double w0 = 2.0 * kPi * f0 / sr, cw = std::cos(w0), sw = std::sin(w0);
        const double alpha = sw / 2.0 * std::sqrt(2.0), tsa = 2.0 * std::sqrt(A) * alpha;
        return normC(A * ((A + 1) + (A - 1) * cw + tsa), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - tsa),
                     (A + 1) - (A - 1) * cw + tsa, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - tsa);
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    State  s_[2][kBands] = {};                 // [channel][band] apply biquad state
    State  detState_[kBands] = {};             // detector bandpass state (mono)
    double detEnv_[kBands] = {};               // detector envelope (linear)
    double dynDb_[kBands] = {};                // smoothed dynamic gain per band (dB)
    std::atomic<float> gr_[kBands] = {};       // published momentary gain (dB)
    float  sc_[kMaxSc] = {};                   // sidechain mono
    int32_t scLen_ = 0;
};

} // namespace nota
