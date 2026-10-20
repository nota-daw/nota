// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Level (device kind 18) — a loudness leveler . Split
// into .h/.cpp; K-weighting runs on shared dsp::BiquadSection stages;
// per-sample dB mapping uses FastMath; silent stretches auto-sleep via
// isIdle() (the device's own silence-hold makes frozen measurements correct;
// the limiter delay advances through zeros). Params and defaults unchanged.

#pragma once

#include "devices/Device.h"
#include "dsp/primitives/BiquadCascade.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class AutoGain : public Device {
  public:
    enum {
        Target = 0, // target loudness (0..1 → −36..0 LUFS)
        Scale,      // 0 Momentary (0.4 s) / 0.5 Short-term (3 s) / 1 Integrated (12 s, gated)
        Auto,       // >= 0.5 Auto (ride to the target); else Manual (the Gain param)
        Trim,       // offset after the correction (bipolar, ±12 dB)
        Response,   // 0 Fast (glide = Window / 4, look-ahead 5 ms) / 1 Slow (glide = Window, 20 ms)
        Window,     // correction glide time (0..1 → exp 0.4..10 s)
        MaxGain,    // maximum |correction| (0..1 → 0..24 dB)
        Safe,       // >= 0.5 look-ahead true-peak limiter on the output
        Ceiling,    // limiter ceiling (0..1 → −6..0 dBTP)
        Gain,       // manual gain (bipolar, ±24 dB, limited to ±Max Gain) — appended
        kNumParams
    };
    // Packed scope telemetry (scopeRead). 0..8 are the original layout.
    enum {
        S_InLufs = 0,  // input, short-term (3 s) LUFS
        S_OutLufs,     // output, short-term LUFS
        S_InMom,       // input, momentary LUFS
        S_Target,      // the target in effect (the reference's loudness when routed)
        S_Applied,     // gain applied: correction + trim − limiter (dB)
        S_TruePeak,    // output true peak (dBTP, slow decay)
        S_Corr,        // output L/R correlation −1..+1
        S_ScLufs,      // reference loudness on the scale (−120 when none)
        S_Desired,     // the correction the target asks for (clamped) + trim
        S_Measured,    // input loudness on the chosen scale (gated) — what the correction reads
        S_OutMeasured, // output loudness on the chosen scale (measured + gain)
        S_OutMom,      // output, momentary LUFS
        S_Level,       // the correction alone (auto ride or manual gain), dB
        S_LimGr,       // limiter reduction now (dB, >= 0)
        S_Clamp,       // 0 none, 1 held at +Max Gain, 2 held at −Max Gain
        S_Silent,      // 1 while the input is gated (silence: holding)
        S_Latency,     // look-ahead latency (samples)
        S_SampleRate,
        S_Primed,  // 1 once a signal has been measured
        S_Delta,   // output − target (LU)
        S_LimHold, // limiter reduction, peak-held over ~1 s (dB)
        kScope
    };

    AutoGain();
    void setSampleRate(double sr, int32_t maxBlock) override;

    // Sidechain: the target may key off another track's loudness (Compressor plumbing).
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override {
        scTrackId_.store(id, std::memory_order_relaxed);
        scPrimed_ = false;
        scRun_ = 0;
    }
    void setSidechain(const float* interleaved, int32_t frames) override {
        scBuf_ = interleaved;
        scFrames_ = frames;
    }
    bool acceptsSidechain() const override { return true; }

    // Latency follows Safe + Response (the engine recomputes PDC when a param moves it).
    int32_t latencySamples() const override { return get(Safe) >= 0.5f ? lookW() + 1 : 0; }

    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    float matchGainDb() const;

    void process(float* buf, int32_t frames) override;

    // Sleep contract: look-ahead limiter plus slow loudness ballistics — the
    // engine-visible tail. The device's own silence-hold keeps frozen gains
    // correct across the sleep.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    const char* displayName() const override { return "Nota Level"; }
    int32_t builtinKind() const override { return 18; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kFloor = 1e-12;
    // −70 LUFS as a K-weighted mean square (LUFS = −0.691 + 10 log10 ms).
    static constexpr double kGateMs = 1.1724653045822964e-7;
    static constexpr double kRelGate = 0.1;   // −10 LU (Integrated, BS.1770)
    static constexpr double kHoldGate = 0.01; // −20 LU (the hold)
    static constexpr float kDbToLn = 0.11512925464970228f;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    int scaleIndex() const { return std::clamp((int)std::lround(get(Scale) * 2.0f), 0, 2); }
    float trimDb() const { return (get(Trim) - 0.5f) * 24.0f; }
    float ceilingDb() const { return -6.0f + get(Ceiling) * 6.0f; }
    int lookW() const { return std::max(1, (int)std::lround((get(Response) < 0.5f ? 0.005 : 0.020) * sr_)); }
    static double expMap(float v, double lo, double hi);
    float coefFor(double tauSec) const { return (float)(1.0 - std::exp(-1.0 / (std::max(1e-4, tauSec) * sr_))); }
    static float db(float lin);
    static float lufs(double ms);

    void resetLimiter(int W);

    // BS.1770 K-weighting state. Verbatim float (measurement path drives
    // threshold gates — bit-exactness beats shared types here).
    struct KW {
        float z1a = 0, z1b = 0, z2a = 0, z2b = 0;
    };
    // BS.1770 K-weighting: high-shelf then 38 Hz high-pass, cascaded biquads (TDF-II).
    float kweight(KW& s, float x);
    void computeKWeighting();

    static float catmull(float y0, float y1, float y2, float y3, float t);
    // Output true-peak meter: 4× Catmull-Rom between the last samples.
    float truePeak(int c, float x);
    // Limiter detector: the peak of the last three samples and the interpolation.
    float tpInterp(int c, float x);

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};

    // BS.1770 K-weighting coefficients (see computeKWeighting).
    float b0_[2] = {}, b1_[2] = {}, b2_[2] = {}, a1_[2] = {}, a2_[2] = {};
    KW kIn_[2], kSc_[2], kOut_[2];
    float coefInt_ = 0.0f;
    float tp0_[2] = {}, tp1_[2] = {}, tp2_[2] = {};
    float ti0_[2] = {}, ti1_[2] = {}, ti2_[2] = {};

    // Loudness EMAs (mean square). *G = gated (hold in silence).
    double msMom_ = kFloor, msShort_ = kFloor, msMomG_ = kFloor, msShortG_ = kFloor, msIntAbs_ = kFloor,
           msInt_ = kFloor;
    double msScMom_ = kFloor, msScMeas_ = kFloor, msOutShort_ = kFloor, msOutMom_ = kFloor;
    double cLR_ = 1e-7, cLL_ = 1e-7, cRR_ = 1e-7;
    bool primed_ = false, scPrimed_ = false;
    int sigRun_ = 0, scRun_ = 0;
    float levelDb_ = 0.0f, limGrSm_ = 0.0f, limHold_ = 0.0f, tpPk_ = 0.0f, curTarget_ = -14.0f;

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;

    // Look-ahead limiter (sized in setSampleRate).
    std::vector<float> dly_, box_, dqV_;
    std::vector<int64_t> dqT_;
    int wMax_ = 0, cap_ = 1, curW_ = -1, boxI_ = 0, dqH_ = 0, dqN_ = 0, dW_ = 0;
    int64_t t_ = 0;
    double boxSum_ = 0.0;
    float env_ = 1.0f;

    std::atomic<bool> reseedReq_{false};

    // Sidechain source buffer (engine hands it in before process()).
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;

    // Published meters.
    std::atomic<float> inLufs_{-120.0f}, outLufs_{-120.0f}, inMom_{-120.0f}, outMom_{-120.0f}, target_{-14.0f},
        applied_{0.0f}, truePeak_{-120.0f}, corr_{0.0f}, scLufs_{-120.0f}, desired_{0.0f}, measured_{-120.0f},
        outMeas_{-120.0f}, level_{0.0f}, limGr_{0.0f}, limHoldA_{0.0f}, srA_{44100.0f};
    std::atomic<int> clamp_{0}, silent_{1}, primedA_{0}, latency_{0};
};

} // namespace nota
