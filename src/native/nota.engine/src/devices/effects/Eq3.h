// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota EQ-3 (device kind 16) — a three-band DJ-style isolator
// dB mapping uses FastMath;
// silent stretches auto-sleep via the isIdle() contract (short SVF memory,
// no audible tail). Params, blob-irrelevant text and defaults unchanged.

#pragma once

#include "devices/Device.h"

#include <signalsmith-linear/fft.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <complex>
#include <cstdint>
#include <cstdio>
#include <mutex>
#include <string>
#include <vector>

namespace nota {

class Eq3 : public Device {
  public:
    enum {
        Low = 0,  // low-band gain   (law: Range)
        Mid,      // mid-band gain
        High,     // high-band gain
        LowKill,  // low band killed  (>= 0.5)
        MidKill,  // mid band killed
        HighKill, // high band killed
        FreqLo,   // low/mid crossover  (exp 50..2000 Hz, default 250 Hz)
        FreqHi,   // mid/high crossover (exp 500..18000 Hz, default 2.5 kHz)
        Slope,    // 0 = 24 dB/oct (LR4), 1 = 48 dB/oct (LR8)
        Gain,     // output gain (0.5 = 0 dB, ±24 dB)
        Range,    // fader law: 0 = Classic ±15 dB, 1 = Isolator −24 … +6 dB (appended)
        kNumParams
    };
    static constexpr int kLegacyParams = 10; // the layout before Range

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0, // input peak (300 ms fall), dBFS
        S_OutPeak,    // output peak (300 ms fall), dBFS
        S_Level,      // 3 × band peak after its gain (300 ms fall), dBFS: low, mid, high
        S_SampleRate = 5,
        S_Cpu,        // share of real time spent in process()
        S_Analysed,   // 1 when the spectrum is valid
        S_Latency,    // samples (always 0)
        S_F1,         // low/mid crossover in use, Hz
        S_F2,         // mid/high crossover in use, Hz
        S_Gain,       // 3 × gain in effect now (linear, after the kill glide)
        S_Bands = 14, // kSpec
        kTele = 16
    };
    static constexpr int kSpec = 96;
    static constexpr double kSpecLo = 20.0, kSpecHi = 20000.0;
    static constexpr int kScope = kTele + kSpec;

    Eq3();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void paramsRestored(int32_t count) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: short SVF memory only — no audible tail.
    bool hasTail() const override { return false; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    const char* displayName() const override { return "Nota EQ-3"; }
    int32_t builtinKind() const override { return 16; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr int32_t kMaxSec = 4;           // cascade depth for LR8 (48 dB/oct)
    static constexpr int32_t kGroups = 6;           // per channel: f1 LP / HP, f2 LP / HP, the low band's f2 allpass
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    // Musical mappings, shared with the UI / MCP (they mirror these).
    static float gainDb(float v, bool isolator) { return isolator ? -24.0f + 30.0f * v : (v - 0.5f) * 30.0f; }
    static float gainNorm(float db, bool isolator) {
        return std::clamp(isolator ? (db + 24.0f) / 30.0f : db / 30.0f + 0.5f, 0.0f, 1.0f);
    }
    static double freqLoHz(float v) { return expMap(v, 50.0, 2000.0); }
    static double freqHiHz(float v) { return expMap(v, 500.0, 18000.0); }

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi);
    static float dbToLin(float db);
    static float db(float lin) { return lin > 1e-6f ? std::max(-120.0f, 20.0f * std::log10(lin)) : -120.0f; }
    static std::string hz(double f);

    struct Coeffs {
        double a1, a2, a3, k;
    };
    Coeffs coeffsFor(double fc, double k) const;

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
            lp = v2;
            hp = x - c.k * v1 - v2;
        }
    };

    // Cascade `n` sections taking the lowpass (or highpass) output forward — n identical
    // Butterworth-2 stages give a 2n-th-order Linkwitz-Riley response.
    static inline double cascadeLP(Svf* s, int n, double x, const Coeffs* c) {
        double lp, hp;
        for (int i = 0; i < n; ++i) {
            s[i].tick(x, c[i], lp, hp);
            x = lp;
        }
        return x;
    }
    static inline double cascadeHP(Svf* s, int n, double x, const Coeffs* c) {
        double lp, hp;
        for (int i = 0; i < n; ++i) {
            s[i].tick(x, c[i], lp, hp);
            x = hp;
        }
        return x;
    }

    // Split one sample into the three bands (serial LR crossover tree); the low band passes the
    // mid/high split summed back — an allpass that keeps it in phase with mid + high.
    inline void splitOne(int ch, float x, int nSec, const Coeffs* c1, const Coeffs* c2, double* band);

    bool analyse() const;
    static double nowSeconds() {
        return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    // Per channel: kGroups cascade groups × kMaxSec sections.
    Svf sec_[2 * kMaxSec * kGroups];
    float gLcur_ = 1.0f, gMcur_ = 1.0f, gHcur_ = 1.0f; // smoothed band gains (click-free)
    bool primed_ = false;

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;

    // Meters (audio thread) and their published values.
    float inPk_ = 0.0f, outPk_ = 0.0f, bandPk_[3] = {};
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{-120.0f}, outPkA_{-120.0f}, cpuA_{0.0f}, srA_{44100.0f}, f1A_{250.0f}, f2A_{2500.0f};
    std::atomic<float> lvlA_[3];
    std::atomic<float> gA_[3];
    std::atomic<bool> resetPk_{false};

    // Spectrum: a ring written by the audio thread, the analysis state under mx_.
    std::vector<float> ring_;
    uint32_t ringMask_ = 0;
    std::atomic<uint32_t> ringW_{0};
    int fftN_ = 0;
    mutable std::mutex mx_;
    mutable signalsmith::linear::RealFFT<float> fft_;
    std::vector<float> win_;
    mutable std::vector<float> tBuf_;
    mutable std::vector<std::complex<float>> fBuf_;
    mutable double aSpec_[kSpec] = {};
    mutable float spec_[kSpec] = {};
    mutable double lastAnaS_ = -1.0;
    mutable uint32_t lastAnaW_ = 0;
    mutable bool anaValid_ = false;
};

} // namespace nota
