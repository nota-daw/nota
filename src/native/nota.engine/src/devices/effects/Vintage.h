// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Vintage (device kind 8) — a multi-era signal-degradation + saturation
// insert in the spirit of classic vinyl-distortion units, widened into six voicings:
// Vinyl / Cassette / Reel-to-Reel / VHS / Tube / Analog. Each Mode sets a character
// table (saturation shape + drive, tone tilt, head bump, band-limit, and the baseline
// amounts of hiss, crackle/dust and wow/flutter). On top sit global knobs — Drive,
// Tone, Wow, Flutter, Noise, Crackle, Wear (an age macro), Mix and Output — and the
// finer controls: a Warm / Flat / Dark tone model with low + high shelves, Character
// (the era's voicing on / off), wow and flutter rates (wow free or tempo-synced), a hiss
// high-pass, wear that follows the input, stereo drift, a Tube / Analog output stage,
// an even-harmonics-only saturation and automatic gain compensation.
//
//   in ─▶ wow/flutter delay ─▶ saturate(shape) ─▶ stage ─▶ tone/bump/shelves/band/hp
//      ─▶ +hiss +crackle ─▶ auto-comp ─▶ mix ─▶ out
//
// The memoryless saturation + stage run on fast_math::tanhPrecise
// with a hoisted kTanh06; the wow/flutter LFO sin/cos stay std::-exact (they steer the
// delay read pointer). Silent stretches auto-sleep via isIdle(). Params unchanged.

#pragma once

#include "devices/Device.h"
#include "dsp/primitives/Oversampler.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Vintage : public Device {
  public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum {
        Mode = 0,
        Drive,
        Tone,
        Wow,
        Flutter,
        Noise,
        Crackle,
        Wear,
        Mix,
        Output,
        Oversampling, // 0..1 → Off/2×/4×/8×
        ToneLow,      // low shelf 120 Hz, 0.5 = 0 dB, ±12 dB
        ToneHigh,     // high shelf 6 kHz, 0.5 = 0 dB, ±12 dB
        ToneModel,    // 0 Warm (the era's tilt + head bump), 0.5 Flat, 1 Dark
        Character,    // the Mode's voicing on (1) / off (0)
        AutoComp,     // automatic gain compensation
        WowRate,      // free 0.1·40^v Hz; synced round(v·5) → 4 bars … 1/8
        FlutterRate,  // 2·10^v Hz
        WowSync,      // wow rate follows the tempo
        HissHp,       // hiss high-pass 20·100^v Hz
        WearFollow,   // hiss + crackle follow the input level
        StereoDrift,  // right channel wow/flutter a quarter-cycle apart, own crackle
        OutputStage,  // 0 off, 0.5 Tube, 1 Analog
        EvenOnly,     // saturation adds even harmonics only
        kNumParams
    };

    // Telemetry slots (scopeRead).
    enum {
        S_InPk = 0, // input peak (decaying), linear
        S_OutPk,    // output peak (decaying), linear
        S_Cpu,      // share of real time spent in process()
        S_SampleRate,
        S_Bpm,       // 0 until the transport has run
        S_Delay,     // the wow/flutter tape delay in samples (0 when off)
        S_WowHz,     // effective wow rate
        S_FlutHz,    // flutter rate
        S_WowCents,  // wow depth, ± cents
        S_FlutCents, // flutter depth, ± cents
        S_BandHz,    // band-limit corner (0 = open)
        S_HissDb,    // hiss level, dBFS RMS (−120 = none)
        S_Thd,       // THD of the static curve at the test level, 0..1
        S_H1,        // harmonic levels 1..7, dB relative to the fundamental (H1 = 0)
        S_H7 = S_H1 + 6,
        S_TestLvl, // the level the THD was measured at (the input peak, or −6 dBFS)
        S_Asym,    // curve asymmetry 0..1 (|f(a)+f(−a)| / |f(a)|)
        S_CompDb,  // auto-comp gain now, dB
        S_Mode,
        S_Crackles, // crackle clicks since load
        S_HistLen,  // points per history (kHist)
        S_HistMs,   // ms per history point
        S_WowPhase, // wow phase 0..1
        S_Reserved0,
        S_Reserved1,
        S_Reserved2,
        S_Reserved3,
        kTele = 32
    };
    static constexpr int kCurve = 48;
    static constexpr int kHist = 1024; // 4 ms per point → ~4 s
    static constexpr int kScopeTotal = kTele + kCurve + 2 * kHist;
    enum { A_ResetWear = 0 }; // deviceAction ids

    Vintage();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void setTransport(double beatStart, double spb, bool playing) override;
    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: the wow/flutter delay line flushes past silent input — the
    // engine-visible tail. Sleep engages only after the hangover (see the .cpp).
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // Telemetry block, the transfer curve, then the pitch histories (see the header note).
    // Writes as much of that layout as fits in maxSamples. Lock-free; torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    const char* displayName() const override { return "Nota Vintage"; }
    int32_t builtinKind() const override { return 8; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kDelay = 2048; // power of two (pitch-mod ring)
    static constexpr double kBaseDelayS = 0.006;
    static constexpr double kHistMs = 4.0;
    static constexpr float kWowRateDefault = 0.4621f;  // log(5.5)/log(40)  → 0.55 Hz
    static constexpr float kFlutRateDefault = 0.5441f; // log10(3.5)        → 7 Hz
    static constexpr double kSyncBeats[6] = {16.0, 8.0, 4.0, 2.0, 1.0, 0.5};
    static constexpr const char* kModeName[6] = {"Vinyl", "Cassette", "Reel", "VHS", "Tube", "Analog"};
    static constexpr const char* kModelName[3] = {"warm", "flat", "dark"};
    static constexpr const char* kOsName[4] = {"off", "2x", "4x", "8x"};
    static constexpr const char* kSyncName[6] = {"4 bars", "2 bars", "1 bar", "1/2", "1/4", "1/8"};
    // tanh(0.6): the even-harmonics generator's operating point, hoisted out of the
    // per-sample path (precomputing a constant is always safe).
    static constexpr float kTanh06 = 0.53704957f;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep
    static constexpr uint64_t kMaxFlight = kDelay;  // longest internal delay (the tape ring)

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static std::string hzText(double hz);

    // Per-mode character: saturation shape + drive/bias, tone tilt, low head bump, the
    // band-limit and low-cut corners, and the baseline hiss / crackle / wow / flutter.
    struct ModeChar {
        int shape;
        float driveMul;
        float bias;
        float tilt;
        float bumpDb;
        double bandHz;
        double hpHz;
        float noiseMul;
        float crackleMul;
        float wowMul;
        float flutMul;
    };
    static constexpr int kModes = 6;
    static constexpr ModeChar kMode[kModes] = {
        /* Vinyl    */ {0, 3.0f, 0.05f, -0.10f, 1.5f, 13000.0, 35.0, 0.5f, 1.3f, 1.0f, 0.3f},
        /* Cassette */ {2, 4.0f, 0.08f, -0.15f, 0.0f, 10000.0, 45.0, 1.2f, 0.2f, 0.6f, 1.0f},
        /* Reel     */ {2, 6.0f, 0.06f, 0.00f, 3.0f, 15000.0, 30.0, 0.4f, 0.1f, 0.5f, 0.5f},
        /* VHS      */ {0, 3.5f, 0.10f, -0.25f, 0.0f, 7000.0, 60.0, 1.0f, 0.5f, 0.8f, 1.4f},
        /* Tube     */ {1, 8.0f, 0.18f, 0.05f, 0.0f, 16000.0, 20.0, 0.15f, 0.0f, 0.0f, 0.0f},
        /* Analog   */ {0, 5.0f, 0.03f, 0.00f, 0.0f, 18000.0, 20.0, 0.10f, 0.0f, 0.0f, 0.0f},
    };
    // Character off: a plain soft saturator with the wear knobs at face value.
    static constexpr ModeChar kNeutral = {0, 4.0f, 0.02f, 0.0f, 0.0f, 0.0, 20.0, 1.0f, 1.0f, 1.0f, 1.0f};

    // A snapshot of the params in musical terms (process() and the message thread alike).
    struct Params {
        int mode, model, stage;
        ModeChar md;
        float drive, tone, wow, flutter, noise, crackle, wear, mix, low, high;
        bool character, autoComp, wowSync, follow, stereo, even;
    };
    Params params() const;
    double wowHzFor(const Params& P) const;
    double flutHzFor(const Params& P) const { return expMap(get(FlutterRate), 2.0, 20.0); }
    std::string wowRateText(const Params& P) const;
    // The band-limit corner (0 = open — the neutral voicing has none).
    double bandFor(const Params& P) const;
    // ± cents of a sine delay modulation of `seconds` depth at `hz`.
    static double depthCents(double seconds, double hz);

    // The memoryless curve: saturation shape (or the even-only generator) + output stage.
    struct Curve {
        int shape = 0, stage = 0;
        bool even = false;
        float g = 1.0f, bias = 0.0f, biasDC = 0.0f, makeup = 1.0f, evenAmt = 0.0f;
        float eval(float x) const;
    };
    Curve curveFor(const Params& P) const;

    // Memoryless saturation shape: 0 soft (tanh), 1 tube (asymmetric, even harmonics),
    // 2 tape (algebraic soft knee with gentle compression).
    static float saturate(int shape, float pre, float bias, float biasDC);

    // Harmonic amplitudes 1..7 of the curve driven by a sine of amplitude `a` (DFT, 128 pts).
    static void harmonics(const Curve& cv, float a, double* h);

    // Hiss: white noise through a one-pole high-pass.
    float hiss(int c, float a);

    // Linear-interpolated read `tap` samples behind the write head.
    float readFrac(int c, double tap) const;

    float whiteUni() {
        rng_ = rng_ * 1664525u + 1013904223u;
        return (float)((rng_ >> 8) & 0xFFFFFF) / 16777216.0f;
    }

    // RBJ biquad (transposed direct form II) — coeffs computed off the state thread.
    struct Biquad {
        struct Coef {
            float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
        };
        float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0, z1 = 0, z2 = 0;
        void reset() { z1 = z2 = 0; }
        void set(const Coef& c) {
            b0 = c.b0;
            b1 = c.b1;
            b2 = c.b2;
            a1 = c.a1;
            a2 = c.a2;
        }
        float process(float x);
        static Coef norm(double b0, double b1, double b2, double a0, double a1, double a2);
        static double w0(double sr, double f);
        static Coef lowpass(double sr, double f, double q);
        static Coef highpass(double sr, double f, double q);
        static Coef lowShelf(double sr, double f, double dB);
        static Coef highShelf(double sr, double f, double dB);
    };

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    Biquad toneSh_[2], bumpSh_[2], bandLp_[2], hp_[2], lowSh_[2], highSh_[2];
    float dl_[2][kDelay] = {};
    int dw_ = 0;
    double wowPh_ = 0.0, flutPh_ = 0.0;
    float flutNoise_ = 0.0f, flutNoiseT_ = 0.0f;
    float clickEnv_[2] = {};
    float hissHp_[2] = {}, hissPrev_[2] = {};
    float inEnv_ = 0.0f;
    double inMs_ = 0.0, wetMs_ = 0.0;
    float compG_ = 1.0f;
    uint32_t rng_ = 0x9E3779B9u;

    double beatStart_ = 0.0, spb_ = 0.0; // transport (for wow sync)
    bool playing_ = false;
    std::atomic<bool> resetReq_{false};

    // Telemetry.
    float histWow_[kHist] = {}, histFlut_[kHist] = {};
    std::atomic<uint32_t> histW_{0};
    int histCount_ = 0;
    std::atomic<uint32_t> crackles_{0};
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{0}, outPkA_{0}, cpuA_{0}, srA_{44100.0f}, bpmA_{0}, compA_{1}, wowPhA_{0}, delayA_{0};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
    float feedPk_ = 0.0f; // peak delay-tap push (blocks sleep while the line flushes)

    dsp::Oversampler os_; // saturation anti-aliasing
    int32_t maxFrames_ = 4096;
    std::vector<float> dry_, click_, follow_; // base-rate stashes (sized in setSampleRate)
};

} // namespace nota
