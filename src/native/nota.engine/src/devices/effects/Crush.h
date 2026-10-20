// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Crush (device kind 12) — a bit crusher: bit-depth reduction and sample-rate
// reduction with three characters (Digital hard truncate / Analog soft clip / Fold wavefold),
// a GRIT stage (dither / jitter / noise floor), an anti-alias pre-filter, a post filter,
// dry/wet, auto gain and a DC filter.
//
//   in ─▶ drive ─▶ [anti-alias LP] ─▶ sample & hold (± jitter) ─▶ + dither ─▶ mode shape ─▶
//         quantise ─▶ + noise floor ─▶ post LP ─▶ [DC filter] ─▶ dry/wet ─▶ [auto gain] ─▶ out
//
// Anti-alias is a 4th-order Butterworth low-pass at the reduced rate's Nyquist, so what the
// hold would fold back is taken out first. Auto gain rides the output back to the input's
// loudness (300 ms RMS, ±24 dB). DC filter is a 10 Hz high-pass on the crushed path.
//
// The Analog mode shape runs on fast_math::tanhPrecise; the Fold
// fmod, the hold timing and all control rate stay std::-exact. Silent stretches
// auto-sleep via isIdle(). Params unchanged (all normalized 0..1, APPEND ONLY).

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

class Crush : public Device {
  public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum {
        Bits = 0,
        Rate,
        Mode,
        Dither,
        Jitter,
        NoiseFloor,
        PostFilter,
        DryWet,
        AntiAlias,
        Output,
        Drive,
        AutoGain,
        DcFilter,
        kNumParams
    };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0, // dry input peak (300 ms fall), dBFS
        S_OutPeak,    // output peak (300 ms fall), dBFS
        S_InRms,      // dry input RMS (300 ms), dBFS
        S_OutRms,     // output RMS (300 ms), dBFS
        S_Crest,      // output crest factor: peak hold over 1 s − RMS, dB
        S_SampleRate,
        S_RateHz,      // the reduced sample rate, Hz
        S_Hold,        // samples each value is held (sr / rate, fractional)
        S_Bits,        // effective bit depth, 1..24
        S_Levels,      // quantiser levels, 2^bits
        S_QuantNoise,  // theoretical quantisation noise of a full-scale sine, dB (−6.02·b − 1.76)
        S_ThdN,        // content added by the crush vs the level-matched input, ratio (0.046 = 4.6 %)
        S_Images,      // added content above the reduced Nyquist vs the whole output, dB (−120 none)
        S_AutoGain,    // the auto-gain correction applied now, dB
        S_Cpu,         // share of real time spent in process()
        S_Latency,     // samples
        S_DrivenPeak,  // peak after Drive (1 s hold), linear
        S_Folds,       // folds the driven peak makes in Fold mode (0 in the others)
        S_Mode,        // 0 Digital, 1 Analog, 2 Fold
        S_Bands,       // kBands
        S_Nyquist,     // the reduced rate's Nyquist, Hz
        S_FilterHz,    // the post filter's cutoff, Hz
        S_Analysed,    // 1 when the spectrum is valid
        S_Signal,      // 1 while signal comes in (above −70 dBFS)
        S_OutPeakHold, // highest output peak since the meter reset, dBFS
        S_InPeakHold,  // highest input peak since the meter reset, dBFS
        kTele = 32
    };
    static constexpr int kBands = 40;
    static constexpr double kBandLo = 20.0, kBandHi = 20000.0;
    static constexpr int kSpecAt = kTele; // input bands, then output bands
    static constexpr int kScope = kTele + 2 * kBands;

    Crush();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void process(float* buf, int32_t frames) override;
    void deviceAction(int32_t id, int32_t iarg, float farg) override;

    // Sleep contract: short hold/filter memory only — no audible tail. Settles after
    // the hangover (see the .cpp).
    bool hasTail() const override { return false; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // Telemetry block, then the spectrum (input bands, output bands). Writes as much of that
    // layout as fits in maxSamples. Called from the UI / MCP thread; lock-free against audio.
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    const char* displayName() const override { return "Nota Crush"; }
    int32_t builtinKind() const override { return 12; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override;

    // ---- mappings (shared with the card through the guide above) -----------------------
    static float bitsOf(float v) { return 1.0f + std::clamp(v, 0.0f, 1.0f) * 23.0f; }
    double rateHz(float v) const { return expMap(v, 500.0, sr_ * 0.48); }
    float driveDb() const { return -12.0f + get(Drive) * 36.0f; }
    float driveGain() const { return std::pow(10.0f, driveDb() / 20.0f); }
    int modeIndex() const { return std::clamp((int)std::lround(get(Mode) * 2.0f), 0, 2); }

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep
    static constexpr uint64_t kMaxFlight = 4096;    // hold + post/DC filter ring across rates

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float dbMs(double ms) { return ms > 1e-12 ? (float)(10.0 * std::log10(ms)) : -120.0f; }

    static float shape(int mode, float x);
    // A triangle fold: straight through ±threshold, then reflected back and forth for as far as
    // the input goes (a 4·threshold period), normalised to ±1. More drive, more folds.
    static float wavefold(float x, float threshold);
    // How many times a peak of `pk` (after Drive) folds over in Fold mode.
    static int foldCount(float pk);

    // RBJ Butterworth sections (Q 0.5412 / 1.3066) for the 4th-order anti-alias low-pass.
    struct Biquad {
        float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
    };
    struct BqState {
        float z1 = 0, z2 = 0;
    };
    void computeAa(double hz);
    float aaRun(int c, float x);

    void resetMeters();

    // ---- spectrum analysis (reading thread, rate-limited, under mx_) ---------------------
    static double nowSeconds();
    bool analyse() const;

    float whiteUni() {
        rng_ = rng_ * 1664525u + 1013904223u;
        return (float)((rng_ >> 8) & 0xFFFFFF) / 16777216.0f;
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float holdL_ = 0.0f, holdR_ = 0.0f;
    double phL_ = 1.0, phR_ = 1.0, perL_ = 1.0, perR_ = 1.0;
    float lpL_ = 0.0f, lpR_ = 0.0f;
    Biquad aaC_[2];
    BqState aa_[4];
    double aaHz_ = -1.0;
    float dcX_[2] = {}, dcY_[2] = {};
    double msIn_ = 0.0, msOut_ = 0.0;
    float autoG_ = 1.0f;
    uint32_t rng_ = 0xDEADBEEFu;

    // Meters (audio thread) and their published copies.
    float inPkM_ = 0.0f, outPkM_ = 0.0f, outPkHold1s_ = 0.0f, drvPkHold_ = 0.0f, inHold_ = 0.0f, outHold_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<float> inPkA_{-120.0f}, outPkA_{-120.0f}, inRmsA_{-120.0f}, outRmsA_{-120.0f}, outHold1sA_{-120.0f};
    std::atomic<float> drvPkA_{0.0f}, inHoldA_{-120.0f}, outHoldA_{-120.0f}, autoGA_{0.0f}, cpuA_{0.0f}, srA_{44100.0f};
    std::atomic<bool> resetReq_{false};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
    float feedPk_ = 0.0f; // peak crushed-path push (blocks sleep while the filters ring)

    // Spectrum: rings written by the audio thread, the analysis state under mx_.
    std::vector<float> ringIn_, ringOut_;
    uint32_t ringMask_ = 0;
    std::atomic<uint32_t> ringW_{0};
    int fftN_ = 0;
    mutable std::mutex mx_;
    mutable signalsmith::linear::RealFFT<float> fft_;
    std::vector<float> win_;
    mutable std::vector<float> tIn_, tOut_;
    mutable std::vector<std::complex<float>> fIn_, fOut_;
    mutable double aIn_[kBands] = {}, aOut_[kBands] = {};
    mutable float bandIn_[kBands] = {}, bandOut_[kBands] = {};
    mutable float thdN_ = 0.0f, images_ = -120.0f;
    mutable double lastAnaS_ = -1.0;
    mutable uint32_t lastAnaW_ = 0;
    mutable bool anaValid_ = false;
};

} // namespace nota
