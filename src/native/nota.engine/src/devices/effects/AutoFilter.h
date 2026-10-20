// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Auto Filter (device kind 7) — a classic envelope/LFO-driven dynamic
// filter. The SVF core runs through shared
// dsp::SvfFilter (all-taps form for morphing), saturation and the LFO sine use
// FastMath, the scope ring index is block-decimated, and silent stretches
// auto-sleep via the isIdle() contract. Params and defaults unchanged.

#pragma once

#include "devices/Device.h"
#include "dsp/primitives/SvfFilter.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>

namespace nota {

class AutoFilter : public Device {
  public:
    enum {
        Freq = 0, // base cutoff (exp 30..18000 Hz)
        Res,      // resonance (Q 0.5..15)
        Type,     // filter type: 0 LP, 1 BP, 2 HP, 3 Notch
        Slope,    // 0 = 12 dB/oct, 1 = 24 dB/oct
        Morph,    // blend from Type toward the next type (continuous)
        EnvAmt,   // bipolar env depth (0.5 = 0; ±6 oct on Freq, ±80 % on Res)
        EnvAtt,   // envelope attack (exp 0.1..500 ms)
        EnvRel,   // envelope release (exp 1..2000 ms)
        EnvHold,  // peak hold on/off (>=0.5); how long = EnvHoldTime
        LfoAmt,   // LFO depth (0..4 oct on Freq, ±50 % on Res)
        LfoRate,  // LFO rate (exp 0.01..40 Hz; in Sync picks a division)
        LfoWave,  // 0 Sine, 1 Tri, 2 Saw, 3 Square, 4 S&H (random)
        LfoMorph, // LFO shape skew / pulse-width (0..1)
        Drive,    // pre-filter drive (0..1)
        DryWet,   // 0 = dry, 1 = wet
        EnvOn,    // envelope enable (>=0.5)
        LfoOn,    // LFO enable (>=0.5)
        Gain,     // output gain (0.5 = 0 dB, ±24 dB)
        Circuit,  // 0 = Clean, 1 = Analog (mild saturation)
        LfoSync,  // 0 = Free (Hz), 1 = tempo-synced (Rate selects a division)
        LfoPhase, // stereo LFO phase offset for the right channel (0..180°)
        // ---- appended for the almanac redesign — keep the order, append only ----
        ModTarget,   // what ENV + LFO move: 0 Freq, 0.5 Reso, 1 Both
        EnvHoldTime, // hold length when Hold is on: 400·v² ms; the top (≥ 0.995) = ∞ (freeze)
        ModSmooth,   // slew on the modulation: 120·v² ms (0 = none)
        LfoOffset,   // LFO start phase (0..360°)
        LfoRetrig,   // restart the LFO on each input onset and at play (>=0.5)
        kNumParams
    };
    // Scope telemetry layout (read by the card and MCP).
    enum {
        S_Cut = 0,  // live modulated cutoff, normalized over 30..18000 Hz (left channel)
        S_CutR,     // right channel (differs with LFO stereo phase)
        S_Res,      // live modulated resonance, normalized
        S_Env,      // envelope follower level, linear 0..1
        S_Lfo,      // LFO value (−1..1, left)
        S_LfoPhase, // LFO phase 0..1 (left, incl. start offset)
        S_InPk,     // input peak (decaying), linear
        S_OutPk,    // output peak (decaying), linear
        S_Cpu,      // share of real time spent in process()
        S_SampleRate,
        S_Bpm,     // 0 until the transport has run
        S_Onsets,  // input onsets counted since load (retrig events)
        S_EnvHeld, // 1 while the envelope is held at its peak
        S_HistLen, // points per history (kHist)
        S_HistMs,  // ms per history point
        S_Reserved,
        kTele
    };
    enum { A_RetrigLfo = 0, A_ResetEnv = 1 }; // deviceAction ids
    static constexpr int kHist = 2048;        // 1 ms per point → ~2 s
    static constexpr int kScope = 2048;       // pre-filter ring (power of two)
    static constexpr int kScopeTotal = kTele + 2 * kHist + kScope;
    static constexpr float kOnsetOn = 0.063f;   // −24 dBFS — an onset (Retrig, event count)
    static constexpr float kOnsetOff = 0.0316f; // −30 dBFS — re-arms

    // Tempo-synced LFO divisions (beats per cycle), fastest → slowest by Rate.
    static constexpr double kLfoDiv[8] = {8.0, 4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 0.0625};

    AutoFilter();
    void setSampleRate(double sr, int32_t maxBlock) override;

    // Musical clock for the tempo-synced LFO (engine calls this before process()).
    void setTransport(double beatStart, double spb, bool playing) override;

    // Sidechain: the envelope follower keys off the source track when one is set.
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void setSidechain(const float* interleaved, int32_t frames) override {
        scBuf_ = interleaved;
        scFrames_ = frames;
    }
    bool acceptsSidechain() const override { return true; }

    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    float gainReductionDb() const override { return cutA_.load(std::memory_order_relaxed); }
    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: SVF states plus follower/LFO only — filtering silence
    // stays silent even while the LFO runs (it modulates coefficients, not
    // signal). Free-LFO phase keeps advancing across skipped blocks.
    bool hasTail() const override { return false; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    const char* displayName() const override { return "Nota Auto Filter"; }
    int32_t builtinKind() const override { return 7; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    std::string deviceText(int32_t id) const override;

  private:
    static constexpr int32_t kCtrl = 16;            // control-rate block for coefficient updates
    static constexpr double kGlideS = 0.02;         // Freq / Res / Morph glide time constant
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    static double settle(double v, double target, double eps) { return std::fabs(v - target) < eps ? target : v; }

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi);
    static std::string hzText(double hz);
    std::string holdText() const;

    float filterOne(int ch, float xin, float drive, float driveGain, float driveComp, int type, float morph, float wet,
        float outGain, bool analog);

    // One control-chunk update (LFO phase, smoothed modulation, gliding knobs,
    // modulated cutoff/resonance). Shared by the audio loop and the idle-skip
    // path so frozen-then-woken modulation matches continuous rendering.
    struct CtlOut {
        double phase = 0.0, lfoL = 0.0, lfoR = 0.0, fcL = 440.0, fcR = 440.0, resN = 0.0;
    };
    void controlChunk(CtlOut& o, double beat, bool sync, bool& doRetrig, double envBip, double offset, double stPhase,
        int lfoWave, float lfoMorph, double divBeats, bool retrig, double lfoDep, double smCoef, double baseOct,
        double resBase, double glide, bool toFreq, bool toRes, double nyq);
    // Shared telemetry tail (normal path and idle-skip path).
    void publishTelemetry(std::chrono::steady_clock::time_point t0, double fcLast, double fcRLast, double resLast,
        double lastLfoL, double lastPhase, bool held, bool lfoOn, float inPk, float outPk, int32_t frames, double sr);

    // LFO shape at a given phase; morph skews the waveform (pulse width / slope).
    double lfoValue(int wave, float morph, double ph) const;

    float whiteNoise() {
        rng_ = rng_ * 1664525u + 1013904223u;
        return (float)((int32_t)(rng_ >> 8) / 8388608.0 - 1.0);
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    dsp::SvfFilter svf_[4];     // [ch*2 + stage], mode-agnostic (processAll taps)
    bool slope24cache_ = false; // current block's slope (set per control block)
    float env_ = 0.0f;
    int64_t holdLeft_ = 0; // samples of hold left at the current peak
    bool armed_ = true;    // onset detector armed
    double lfoPhase_ = 0.0;
    double anchorBeat_ = 0.0;                          // synced LFO restarts here after a retrig
    double smOct_ = 0.0, smOctR_ = 0.0, smRes_ = 0.0;  // smoothed modulation
    double glOct_ = 0.0, glRes_ = 0.0, glMorph_ = 0.0; // gliding knobs: log2 Hz, 0..1, 0..1
    bool primed_ = false;                              // the glides start at the set values
    float shHeld_ = 0.0f;
    double shCycle_ = -1.0;              // last synced S&H cycle index
    double beatStart_ = 0.0, spb_ = 0.0; // transport (for LFO sync)
    bool playing_ = false, playStarted_ = false;
    uint32_t rng_ = 0x1234567u;
    std::atomic<bool> retrigReq_{false}, envResetReq_{false};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;

    // Telemetry.
    mutable float scope_[kScope] = {}; // pre-filter mono ring (UI spectrum)
    std::atomic<uint32_t> scopeW_{0};
    float histEnv_[kHist] = {}, histCut_[kHist] = {}; // 1 ms history rings
    std::atomic<uint32_t> histW_{0};
    double histAcc_ = 0.0;
    float histEnvMax_ = 0.0f;
    std::atomic<uint32_t> onsets_{0};
    double cpuS_ = 0.0;
    std::atomic<float> cutA_{0.5f}, cutRA_{0.5f}, resA_{0.1f}, envA_{0}, lfoA_{0}, lfoPhA_{0}, inPkA_{0}, outPkA_{0},
        cpuA_{0}, srA_{44100.0f}, bpmA_{0}, heldA_{0};

    // Sidechain (engine hands the source buffer just before process()).
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
};

} // namespace nota
