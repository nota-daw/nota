// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Ceiling — a look-ahead brick-wall limiter. True-peak detection runs on the shared dsp::TruePeakMeter (SIMD
// dot16, double-buffered history); detector/K-weighting biquads stay verbatim
// float (measurement path drives loudness gates); per-sample dB mapping uses
// FastMath downstream (tanhPrecise); silent stretches auto-sleep via isIdle()
// (look-ahead line advances through zeros, loudness windows stay live).
// Params and defaults unchanged.

#pragma once

#include "devices/Device.h"
#include "dsp/primitives/TruePeakMeter.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace nota {

class Ceiling : public Device {
  public:
    enum {
        Ceil = 0,
        Gain,
        Release,
        AutoRelease,
        Character,
        Lookahead,
        StereoLink,
        TruePeak,
        Delta,
        ScHp,
        Target,
        kNumParams
    };

    // Telemetry slots (scopeRead). 0..6 keep the original layout.
    enum {
        S_InPeak = 0,  // input peak after Gain, this block, dBFS
        S_OutPeak,     // output peak, this block, dBFS
        S_Gr,          // gain reduction, this block, dB (>= 0)
        S_LufsM,       // momentary loudness (400 ms), LUFS
        S_LufsS,       // short-term loudness (3 s), LUFS
        S_LufsI,       // integrated (gated) loudness since the loudness reset, LUFS
        S_TruePeak,    // output true peak (4×), this block, dBTP
        S_PeakInHold,  // highest input peak since the peak reset, dBFS
        S_PeakOutHold, // highest output peak since the peak reset
        S_MaxGrHold,   // deepest reduction since the peak reset, dB
        S_TpHold,      // highest output true peak since the peak reset, dBTP
        S_Lra,         // loudness range, LU (0 until there is enough)
        S_Plr,         // peak-to-loudness ratio: true-peak hold − integrated, dB
        S_OverPct,     // share of the level window the input was over the ceiling, 0..1
        S_AvgGr,       // mean reduction over the level window (while there is signal), dB
        S_WinMaxGr,    // deepest reduction in the level window, dB
        S_RelNow,      // the release in use now, ms (character and auto stage included)
        S_RelMin,      // shortest release used in the window, ms
        S_RelMax,      // longest release used in the window, ms
        S_Clips,       // transients that reached the clip stage since the peak reset
        S_SampleRate,
        S_Cpu,         // share of real time spent in process()
        S_Latency,     // samples
        S_WinInPeak,   // highest input peak in the level window, dBFS
        S_WinOutPeak,  // highest output peak in the level window, dBFS
        S_LvlCells,    // kLvl
        S_LoudCells,   // kLoud
        S_LvlSeconds,  // the level window's length, s
        S_LoudSeconds, // the loudness window's length, s
        S_Delta,       // 1 while Delta plays the difference
        S_Measured,    // seconds of audio in the integrated reading
        S_MaxM,        // highest momentary loudness since the loudness reset
        S_MaxS,        // highest short-term loudness since the loudness reset
        kTele = 40
    };
    static constexpr int kLvl = 64, kLvlSeries = 7, kLoud = 120, kLoudSeries = 3;
    static constexpr double kLvlSeconds = 4.0, kLoudSeconds = 60.0;
    // Level window series (each kLvl long, after kTele).
    enum { L_In = 0, L_Out, L_GrMax, L_Clip, L_Over, L_Rel, L_GrAvg };
    static constexpr int kLvlAt = kTele;
    static constexpr int kLoudAt = kTele + kLvlSeries * kLvl;
    static constexpr int kScope = kLoudAt + kLoudSeries * kLoud;

    Ceiling();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: look-ahead delay plus release ballistics — the
    // engine-visible tail. Settles after the hangover of quiet blocks.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    float gainReductionDb() const override { return gr_.load(std::memory_order_relaxed); }
    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    int32_t latencySamples() const override { return lookaheadSamples(); }

    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void setSidechain(const float* interleaved, int32_t frames) override {
        scBuf_ = interleaved;
        scFrames_ = frames;
    }
    bool acceptsSidechain() const override { return true; }

    const char* displayName() const override { return "Nota Ceiling"; }
    int32_t builtinKind() const override { return 14; }
    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t i) const override;
    float paramMax(int32_t i) const override;
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kHistBins = 800; // −70.0 … +10.0 LUFS at 0.1 LU
    static constexpr double kHistLo = -70.0;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep
    // True-peak interpolator delay (the FIR centre); the look-ahead covers it.
    static constexpr int kTpDelay = 8;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    int lookaheadSamples() const;

    static float softClip(float x, float ceil, float amt);
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float lufs(double ms) { return ms > 1e-10 ? (float)(-0.691 + 10.0 * std::log10(ms)) : -120.0f; }

    // Output true-peak meter.
    float truePeak(int c, float x);
    // Detector true peak (before the look-ahead, so the gain is down before the over arrives).
    float tpDetect(int c, float x);

    void computeHp(float hz);
    float hp(int c, float x);
    // Cascaded K-weighting biquads (Transposed Direct Form II), per channel.
    float kWeight(int c, float x);
    void computeKWeighting();

    void onHop();
    void pushLevelCell();
    void pushSilentCell(float relEff);
    void clearLevel();
    void clearLoudness();

    double sr_ = 44100.0;
    std::vector<float> laBuf_;
    int laMax_ = 0, laW_ = 0;
    float env_ = 1.0f, grPrev_ = 0.0f;

    // K-weighting state (2 stages × 2 channels); true-peak interpolators; side-chain HP.
    // Verbatim float: measurement path drives loudness gates — exactness first.
    float b0_[2] = {}, b1_[2] = {}, b2_[2] = {}, a1_[2] = {}, a2_[2] = {};
    float z1_[2][2] = {}, z2_[2][2] = {};
    dsp::TruePeakMeter tpOut_[2], tpDet_[2];
    float hb0_ = 1.0f, hb1_ = 0.0f, hb2_ = 0.0f, ha1_ = 0.0f, ha2_ = 0.0f, hpHz_ = -1.0f;
    std::array<std::array<float, 2>, 4> hpZ_{};
    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;

    // Loudness.
    std::vector<float> msRing_;
    int msW_ = 0, momN_ = 0, shortN_ = 0;
    double momSum_ = 0.0, shortSum_ = 0.0;
    int hopN_ = 0, hop_ = 0, hopInCell_ = 0;
    int64_t loudSamples_ = 0;
    std::vector<double> histCount_, histSum_, lraHist_;
    float maxM_ = -120.0f, maxS_ = -120.0f;

    // Level window cell accumulators, holds, clip counting.
    int lvlCellN_ = 1, cN_ = 0, cOver_ = 0, clipHoldN_ = 1, clipHold_ = 0;
    float cIn_ = 0.0f, cOut_ = 0.0f, cGrMax_ = 0.0f, cClip_ = 0.0f;
    double cGrSum_ = 0.0, cRelSum_ = 0.0;
    float pkInHold_ = -120.0f, pkOutHold_ = -120.0f, maxGrHold_ = 0.0f, tpHold_ = -120.0f;
    int64_t clips_ = 0;
    double cpuS_ = 0.0;

    std::array<std::array<float, kLvl>, kLvlSeries> lvl_{};
    std::array<std::array<float, kLoud>, kLoudSeries> loud_{};
    std::atomic<int> lvlHead_{0}, loudHead_{0};

    std::atomic<float> inPeak_{-120.0f}, outPeak_{-120.0f}, truePeak_{-120.0f}, gr_{0.0f};
    std::atomic<float> lufsM_{-120.0f}, lufsS_{-120.0f}, lufsI_{-120.0f}, lraA_{0.0f};
    std::atomic<float> pkInHoldA_{-120.0f}, pkOutHoldA_{-120.0f}, maxGrHoldA_{-120.0f}, tpHoldA_{-120.0f},
        clipsA_{0.0f};
    std::atomic<float> relNowA_{120.0f}, deltaA_{0.0f}, srA_{44100.0f}, cpuA_{0.0f}, measuredA_{0.0f}, maxMA_{-120.0f},
        maxSA_{-120.0f};
    std::atomic<bool> resetPeaksReq_{false}, resetLoudReq_{false};
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
    std::atomic<float> p_[kNumParams] = {};
};

} // namespace nota
