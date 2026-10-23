// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Phaser (device kind 24, mockup "Nota Phaser") — a chain of first-order all-pass
// stages whose corner frequency an LFO sweeps. Per channel:
//
//   A(z)  = Π_N (a + z⁻¹) / (1 + a·z⁻¹),   a = (tan(π·fc/sr) − 1) / (tan(π·fc/sr) + 1)
//   w     = x + Feedback · y                 (the chain's output fed back, solved delay-free)
//   y     = A · w
//   wet   = ±y                               (the wet polarity follows the feedback's sign)
//   out   = (dry · x + wet_gain · wet) / (dry + wet_gain)
//
// Each stage turns the phase 0 → −π, so N stages reach −Nπ and, summed with the dry signal, cut
// N/2 notches where the phase is an odd multiple of π (positive feedback) — or N/2 − 1 where it
// is an even multiple (negative feedback flips the wet polarity with it, so the notches move and
// widen instead of the comb flattening out). The loop is solved without a unit delay: every stage
// is y = a·x + s, so the chain is y = g·w + S (g = aᴺ) and y = (g·x + S) / (1 − fb·g), which
// makes the response exactly A / (1 − fb·A) — the curve the card draws.
//
//   fc(t) = Center · 2^(3 · Depth · lfo(t))     Center 50 Hz … 5 kHz (exponential), ±3 oct at Depth 1
//   LFO: Sine / Triangle / Saw, free (Rate 0.02 … 8 Hz) or Sync (Division 4/1 … 1/16 incl. dotted
//   and triplet). In Sync the LFO locks to the song position while the transport plays and restarts
//   on the bar (a cycle longer than a bar restarts on the span of bars that holds it); stopped, it
//   free-runs at the synced rate. Stereo offsets the right channel's LFO 0 … 180°.
//   Stages: 2 / 4 / 6 / 8 / 12. A change fades the wet path out and back in (~6 ms) so it doesn't click.
//   Mix: 0 = dry, 0.5 = dry and wet equal (the deepest notches), 1 = wet only (phase shift alone).
//
// Center, Depth, Feedback and Mix glide over ~15 ms so automation doesn't zipper; the coefficients
// are recomputed every 4 samples.
//
// All params are normalized 0..1 and APPEND ONLY. Persistence / automation / clone flow
// generically through the base Device.
// Telemetry (scopeRead): kTele live values (levels, fc now per channel, the LFO's place in its
// two-cycle window, the rate, the tempo, the notches / null / peak).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = restart the LFO (free run: from phase 0), 1 = clear the stages (stops a
// whistling loop), 2 = reset the meters.
// Allocation-free, JUCE-free.

#pragma once

#include "devices/Device.h"
#include "engine/TransportInfo.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace nota {

class Phaser : public Device {
  public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Rate = 0, Center, Depth, Feedback, Mix, Waveform, Sync, Division, Stereo, Stages, kNumParams };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0, // input peak (300 ms fall), dBFS
        S_OutPeakL,   // output peak, left (300 ms fall), dBFS
        S_OutPeakR,   // output peak, right, dBFS
        S_FcL,        // the left corner frequency now, Hz
        S_FcR,        // the right corner frequency now, Hz
        S_WinPhase,   // the LFO's place in its two-cycle window, 0..2 (cycles)
        S_RateHz,     // the LFO rate now, Hz (Sync: from the tempo)
        S_Bpm,        // the tempo the Sync rate follows
        S_Playing,    // 1 while the transport plays
        S_Locked,     // 1 while Sync is on and the LFO is locked to the song position
        S_SampleRate,
        S_CenterHz, // the Center param, Hz (the middle of the sweep)
        S_FcMin,    // the sweep's lowest / highest corner, Hz
        S_FcMax,
        S_NotchHz,    // the first notch of the left channel now, Hz
        S_NotchMinHz, // the first notch's sweep, Hz (at fc min … fc max)
        S_NotchMaxHz,
        S_NullDb,   // how deep the notches go, dB
        S_PeakDb,   // the peaks between them, dB
        S_DivBeats, // the Sync division, beats per cycle
        S_BarBeats, // beats per bar (the restart span's unit)
        S_Cpu,      // share of real time spent in process()
        S_Signal,   // 1 while signal comes in (above −70 dBFS)
        S_Cycles,   // LFO cycles since load / the last restart (free run) or the song cycle (Sync)
        S_Stages,   // the all-pass stages running
        S_Notches,  // the notches they cut (N/2, or N/2 − 1 with negative feedback)
        kTele = 32
    };

    static constexpr int kDivs = 9;
    // Sync divisions, slowest → fastest: beats per cycle, and their names.
    static constexpr double kDivBeats[kDivs] = {16.0, 8.0, 4.0, 2.0, 1.5, 1.0, 2.0 / 3.0, 0.5, 0.25};
    static constexpr const char* kDivNames[kDivs] = {"4/1", "2/1", "1/1", "1/2", "1/4D", "1/4", "1/4T", "1/8", "1/16"};
    static constexpr int kDefaultDiv = 2; // 1/1

    static constexpr int kStageOpts = 5;
    static constexpr int kStageCounts[kStageOpts] = {2, 4, 6, 8, 12};
    static constexpr int kMaxStages = 12;

    // Ranges (mirrored by the card and MCP).
    static constexpr double kRateLo = 0.02, kRateHi = 8.0;        // Hz, exponential
    static constexpr double kCenterLo = 50.0, kCenterHi = 5000.0; // Hz, exponential
    static constexpr double kFbMax = 0.95;                        // feedback ±95 %
    static constexpr double kDepthOct = 3.0;                      // fc swings ±3 oct at Depth 1

    Phaser();
    void setSampleRate(double sr, int32_t maxBlock) override;

    // Musical clock for Sync (engine calls this before process()).
    void setTransport(double beatStart, double spb, bool playing) override;
    void setTransportInfo(const TransportInfo& ti) override;

    void process(float* buf, int32_t frames) override;

    // Verbatim DSP, all std::-exact (the LFO steers the
    // all-pass corner inside the solved feedback loop). Silent chains auto-sleep via
    // isIdle(). Params unchanged.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    enum { A_Restart = 0, A_Clear = 1, A_ResetMeters = 2 }; // deviceAction ids
    void deviceAction(int32_t id, int32_t iarg, float farg) override;

    // Telemetry block. Called from the UI / MCP thread; lock-free against audio.
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    const char* displayName() const override { return "Nota Phaser"; }
    int32_t builtinKind() const override { return 24; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override;

    // ---- shared with the card / MCP through the guide above ----------------------------
    int waveIndex() const { return std::clamp((int)std::lround(get(Waveform) * 2.0f), 0, 2); }
    int divIndex() const { return std::clamp((int)std::lround(get(Division) * (kDivs - 1)), 0, kDivs - 1); }
    int stageIndex() const { return std::clamp((int)std::lround(get(Stages) * (kStageOpts - 1)), 0, kStageOpts - 1); }
    int stageCount() const { return kStageCounts[stageIndex()]; }
    double rateFreeHz() const { return expMap(get(Rate), kRateLo, kRateHi); }
    double centerHz() const { return expMap(get(Center), kCenterLo, kCenterHi); }
    double feedback() const { return (std::clamp((double)get(Feedback), 0.0, 1.0) - 0.5) * 2.0 * kFbMax; }
    double stereoDeg() const { return std::clamp((double)get(Stereo), 0.0, 1.0) * 180.0; }
    static int notchCount(int n, double fb) { return fb < 0.0 ? n / 2 - 1 : n / 2; }

    // |dry ± wet·H(θ)| / (dry + wet), H = e^{−jθ} / (1 − fb·e^{−jθ}), θ = the chain's phase lag —
    // the phaser's magnitude (the wet polarity follows the feedback's sign, as in process()).
    static double mag(double dry, double wet, double fb, double th);
    // The chain's phase lag at f for a corner fc: N · 2·atan(tan(πf/sr) / tan(πfc/sr)) (bilinear all-pass).
    static double phaseLag(double f, double fc, int n, double sr);
    // The frequency where the chain's lag reaches θ (inverse of phaseLag).
    static double freqAtLag(double th, double fc, int n, double sr);
    // The first notch: lag π (positive feedback) or 2π (negative).
    static double firstNotchHz(double fc, int n, double fb, double sr);

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    const char* modeWord() const;

    double coef(double fc) const;

    // One channel through the chain with its feedback loop solved delay-free: each stage is
    // y = a·x + s (s' = x − a·y), so the chain is y = g·w + S and w = x + fb·y.
    inline double chain(double* s, double a, double x, double fb) const;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static double sanitize(double v) {
        return std::isfinite(v) && std::fabs(v) > 1e-20 ? std::clamp(v, -64.0, 64.0) : 0.0;
    }

    // LFO in [−1, 1] at phase p (cycles; wraps) — the mockup's shapes: all start at 0 rising.
    static double lfo(int wave, double p);

    void clearStages();
    void resetMeters() { inPkM_ = outPkLM_ = outPkRM_ = 0.0f; }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    double sL_[kMaxStages] = {}, sR_[kMaxStages] = {};
    double aL_ = 0.0, aR_ = 0.0, fcL_ = 800.0, fcR_ = 800.0;
    uint32_t ctl_ = 0;
    int stages_ = 4;
    double stGain_ = 1.0;
    int64_t cycle_ = 0;
    double frac_ = 0.0;
    bool snap_ = true;
    double lcS_ = 9.64, depthS_ = 0.7, fbS_ = 0.4, dryS_ = 1.0, wetS_ = 1.0;
    double beatStart_ = 0.0, spb_ = 0.0, barBeats_ = 4.0;
    bool playing_ = false;
    float inPkM_ = 0.0f, outPkLM_ = 0.0f, outPkRM_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<bool> restartReq_{false}, clearReq_{false}, resetReq_{false};
    std::atomic<float> srA_{44100.0f}, inPkA_{-120.0f}, outPkLA_{-120.0f}, outPkRA_{-120.0f};
    std::atomic<float> fcLA_{0.0f}, fcRA_{0.0f}, fracA_{0.0f}, rateA_{0.4f}, bpmA_{120.0f}, barA_{4.0f}, cpuA_{0.0f};
    std::atomic<int64_t> cycleA_{0};
    std::atomic<int> playingA_{0}, lockedA_{0};
    bool idle_ = false;
    uint64_t quietSamples_ = 0;
    float feedPk_ = 0.0f; // peak chain-output feed (blocks sleep while the loop whistles)
};

} // namespace nota
