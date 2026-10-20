// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota EQ-8 (device kind 0) — an eight-band parametric equalizer (
// ). Filter math runs through shared dsp::BiquadCascade sections in
// 64-sample blocks (routing branch hoisted out of the loop); pitch/dB mapping
// uses FastMath; silent stretches auto-sleep via the isIdle() contract.
// Analyzer, params, blob layout and defaults are unchanged (: existing
// .nota projects and presets load identically).

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

class Eq : public Device {
  public:
    static constexpr int kBands = 8;
    static constexpr int kPerBand = 5; // On, Type, Freq, Gain, Q
    enum { On = 0, Type = 1, Freq = 2, Gain = 3, Q = 4 };
    // Filter types (per band).
    enum { LowCut = 0, LowShelf = 1, Bell = 2, Notch = 3, HighShelf = 4, HighCut = 5, kNumTypes = 6 };
    // Channels (per band).
    enum { ChStereo = 0, ChMid = 1, ChSide = 2, ChLeft = 3, ChRight = 4, kNumChannels = 5 };

    static constexpr int kBandParams = kBands * kPerBand; // 40 — the original layout
    enum {
        SlopeBase = kBandParams,
        ChannelBase = SlopeBase + kBands,
        Scale = ChannelBase + kBands,
        Output,
        AutoGain,
        Analyzer,
        kNumParams
    }; // 60
    enum { AnaPre = 0, AnaPost = 1, AnaOff = 2 };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0, // input peak (300 ms fall), dBFS
        S_OutPeak,    // output peak (300 ms fall), dBFS
        S_AutoGain,   // the auto gain in effect now, dB
        S_SampleRate,
        S_Cpu,      // share of real time spent in process()
        S_Analysed, // 1 when the spectra are valid
        S_Latency,  // samples (always 0)
        S_Bands,    // kSpec
        kTele = 16
    };
    static constexpr int kSpec = 96;
    static constexpr double kSpecLo = 20.0, kSpecHi = 20000.0;
    static constexpr int kPreAt = kTele, kPostAt = kTele + kSpec;
    static constexpr int kScope = kTele + 2 * kSpec;

    Eq();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: short IIR memory only — no audible tail. isIdle() goes
    // true after the hangover of silent blocks; the engine may then skip
    // processing (and PDC) while the input stays silent. wake() on setParam.
    bool hasTail() const override { return false; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    const char* displayName() const override { return "Nota EQ-8"; }
    int32_t builtinKind() const override { return 0; } // M7-6 (project compat)

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t i) const override;
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr int kMaxSec = 4;
    static constexpr int kCtrl = 16;                // control-rate cascade re-build while a band glides
    static constexpr int kAutoKeys = Scale + 1;     // band params, slopes, channels and Scale shape the curve
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep (~370 ms @ 44.1 kHz)

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    float bp(int b, int f) const { return p_[b * kPerBand + f].load(std::memory_order_relaxed); }
    bool bandOn(int b) const { return bp(b, On) > 0.5f; }
    int typeOf(int b) const { return std::clamp(static_cast<int>(std::lround(bp(b, Type))), 0, kNumTypes - 1); }
    int slopeOf(int b) const { return std::clamp(static_cast<int>(std::lround(get(SlopeBase + b))), 0, 2); }
    int channelOf(int b) const {
        return std::clamp(static_cast<int>(std::lround(get(ChannelBase + b))), 0, kNumChannels - 1);
    }
    static bool hasGain(int ty) { return ty == LowShelf || ty == Bell || ty == HighShelf; }
    void setBand(int b, float on, int type, float freq, float gain, float q);

    static double clampd(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
    static double settle(double v, double target, double eps) { return std::fabs(v - target) < eps ? target : v; }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }

    static dsp::BiquadChannel routing(int ch);
    // Build a band's sections (1 biquad, or a Butterworth cut cascade whose
    // last section carries the resonance). Returns the section count.
    static int buildBand(dsp::BiquadCoeffs* out, int type, int slope, double sr, double f0, double gainDb, double q);
    static dsp::BiquadCoeffs buildOne(int type, double sr, double f0, double gainDb, double q);
    static double magDb(const dsp::BiquadCoeffs& k, double w);
    static double averageDb(
        const bool* on, const int* ty, const int* ch, const dsp::BiquadCoeffs (*c)[kMaxSec], double sr);

    bool analyse() const;
    static double nowSeconds() {
        return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    dsp::BiquadCascade bands_[kBands]; // per-band cascade (Stereo L/R banks inside)
    int cfgKey_[kBands] = {};          // type / slope / channel the state belongs to
    float autoKey_[kAutoKeys] = {};    // auto gain: the params it was measured for
    double autoSr_ = -1.0, autoDb_ = 0.0;
    double autoCur_ = 1.0, outCur_ = 1.0; // smoothed auto / output gains
    bool primed_ = false;
    double glF_[kBands] = {}, glG_[kBands] = {}, glQ_[kBands] = {}; // gliding log2 Hz / dB / log2 Q
    bool glMoving_[kBands] = {};

    // Auto-sleep : quiet-sample hangover, then idle_ skips processing.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;

    // Meters (audio thread) and their published values.
    float inPk_ = 0.0f, outPk_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{-120.0f}, outPkA_{-120.0f}, autoA_{0.0f}, cpuA_{0.0f}, srA_{44100.0f};
    std::atomic<bool> resetPk_{false};

    // Spectra: pre / post rings written by the audio thread, the analysis state under mx_.
    std::vector<float> ring_[2];
    uint32_t ringMask_ = 0;
    std::atomic<uint32_t> ringW_{0};
    int fftN_ = 0;
    mutable std::mutex mx_;
    mutable signalsmith::linear::RealFFT<float> fft_;
    std::vector<float> win_;
    mutable std::vector<float> tBuf_;
    mutable std::vector<std::complex<float>> fBuf_;
    mutable double aSpec_[2][kSpec] = {};
    mutable float spec_[2][kSpec] = {};
    mutable double lastAnaS_ = -1.0;
    mutable uint32_t lastAnaW_ = 0;
    mutable bool anaValid_ = false;
};

} // namespace nota
