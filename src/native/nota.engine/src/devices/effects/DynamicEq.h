// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Dynamic EQ-8 (device kind 13) — a static 8-band EQ where each band can
// also react to level. Audio path runs
// through shared dsp::BiquadCascade sections (detector included); dB mapping
// and the per-sample detector level use FastMath (linToDb/exp2f); silent
// stretches auto-sleep via the isIdle() contract. Params and defaults
// unchanged.

#pragma once

#include "devices/Device.h"
#include "dsp/primitives/BiquadCascade.h"

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

class DynamicEq : public Device {
  public:
    static constexpr int kBands = 8;
    static constexpr int kPerBand = 10;
    // Per-band field offsets.
    enum { On = 0, Type = 1, Freq = 2, Gain = 3, Q = 4, Mode = 5, Thresh = 6, Range = 7, Attack = 8, Release = 9 };
    // Filter types (mirror Eq.h).
    enum { LowCut = 0, LowShelf = 1, Bell = 2, Notch = 3, HighShelf = 4, HighCut = 5, kNumTypes = 6 };
    // Dynamic modes: Static, Duck (above the threshold), Lift (below it).
    enum { DynOff = 0, DynAbove = 1, DynBelow = 2 };

    static constexpr int kBandParams = kBands * kPerBand; // 80
    enum {
        Output = kBandParams,
        Sidechain = kBandParams + 1,
        Solo = kBandParams + 2,
        Dynamics = kBandParams + 3,
        KeyBase = kBandParams + 4
    };
    static constexpr int kNumParams = KeyBase + kBands; // 92

    // Telemetry slots (scopeRead).
    enum {
        S_Gain = 0,  // 8 × momentary dynamic gain, dB (signed)
        S_Level = 8, // 8 × detector level, dBFS (−120 silent / off)
        S_SampleRate = 16,
        S_Cpu,      // share of real time spent in process()
        S_Analysed, // 1 when the spectrum is valid
        S_InPeak,   // input peak (300 ms fall), dBFS
        S_OutPeak,  // output peak (300 ms fall), dBFS
        S_KeyLive,  // 1 while a sidechain buffer arrives
        S_Bands,    // kSpec
        S_Latency,  // samples (always 0)
        kTele = 32
    };
    static constexpr int kSpec = 96;
    static constexpr double kSpecLo = 20.0, kSpecHi = 20000.0;
    static constexpr int kSpecAt = kTele;
    static constexpr int kScope = kTele + kSpec;

    DynamicEq();
    void setSampleRate(double sr, int32_t maxBlock) override;

    bool acceptsSidechain() const override { return true; }
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void setSidechain(const float* interleaved, int32_t frames) override {
        scBuf_ = interleaved;
        scFrames_ = frames;
    }

    void process(float* buf, int32_t frames) override;

    // Sleep contract: biquad states plus detector envelopes only — gain
    // modulation of silence stays silent. No audible tail.
    bool hasTail() const override { return false; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    const char* displayName() const override { return "Nota Dynamic EQ-8"; }
    int32_t builtinKind() const override { return 13; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t i) const override;
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    float gainReductionDb() const override;
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr int kCtrl = 16;                // control-rate coeff refresh (samples)
    static constexpr double kGlideS = 0.02;         // Freq / Gain / Q glide time constant
    static constexpr double kKneeDb = 6.0;          // detection range from threshold to full engagement
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    float bp(int b, int f) const { return p_[b * kPerBand + f].load(std::memory_order_relaxed); }
    int typeOf(int b) const { return std::clamp(static_cast<int>(std::lround(bp(b, Type))), 0, kNumTypes - 1); }
    int modeOf(int b) const { return std::clamp(static_cast<int>(std::lround(bp(b, Mode))), 0, 2); }
    bool bandOn(int b) const { return bp(b, On) > 0.5f; }
    bool keyExt(int b) const { return get(Sidechain) >= 0.5f || get(KeyBase + b) >= 0.5f; }
    bool bandDyn(int b) const;
    void band(int b, float on, int type, float freq, float gain, float q, int mode = DynOff, float thr = -24.0f,
        float range = -6.0f, float atk = 10.0f, float rel = 120.0f);

    static double clampd(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
    static double settle(double v, double target, double eps) { return std::fabs(v - target) < eps ? target : v; }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }

    // Single-section apply/detector coefficients via the shared cascade math.
    static dsp::BiquadCoeffs buildOne(int type, double sr, double f0, double gainDb, double q);

    bool analyse() const;
    static double nowSeconds() {
        return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    dsp::BiquadCascade bands_[kBands];   // apply biquads, one single-section cascade per band
    dsp::BiquadSection detSect_[kBands]; // mono detector bandpass states
    double detEnv_[kBands] = {};         // detector envelope (linear)
    double dynDb_[kBands] = {};          // smoothed dynamic gain per band (dB)
    double glF_[kBands] = {}, glG_[kBands] = {}, glQ_[kBands] = {}; // gliding log2 Hz / dB / log2 Q
    bool wasOn_[kBands] = {}; // band was on last block (a fresh band doesn't glide in)
    bool primed_ = false;
    double lvlDisp_[kBands] = {};     // peak-held detector level (dB)
    std::atomic<float> gr_[kBands];   // published momentary gain (dB)
    std::atomic<float> lvlA_[kBands]; // published detector level (dBFS)
    float inPk_ = 0.0f, outPk_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{-120.0f}, outPkA_{-120.0f}, cpuA_{0.0f}, srA_{44100.0f}, keyLive_{0.0f};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;

    // Sidechain (the device's key source; the engine hands the buffer right before process).
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;

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
