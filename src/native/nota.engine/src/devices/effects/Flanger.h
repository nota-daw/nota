// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Flanger (device kind 23, mockup "Nota Flanger") — a comb filter on a short,
// LFO-modulated delay line. Per channel:
//
//   w[n] = x[n] + Feedback · w[n − τ]        (the delay line, fed back)
//   wet  = ±w[n − τ]                          (the wet polarity follows the feedback's sign)
//   out  = (dry · x + wet_gain · wet) / (dry + wet_gain)
//
// so the wet path is ±e^{−jωτ} / (1 − fb·e^{−jωτ}) and, summed with the dry signal, carves a
// harmonic series of notches that the LFO sweeps up and down. Positive feedback sharpens the
// peaks at the multiples of 1/τ (the classic "jet"); negative feedback flips the wet polarity
// with it, so the comb mirrors — peaks on the odd multiples of 1/2τ, notches on the multiples
// of 1/τ (a hollow, tube-like tone). Without the flip, negative feedback against a positive
// wet path would all but cancel the comb. Near ±95 % the comb rings metallically. The polarity
// glides with the other params, so sweeping Feedback through 0 doesn't click.
//
//   τ(t) = Delay · (1 + 0.92 · Depth · lfo(t))      Delay 0.1 … 8 ms (exponential)
//   LFO: Sine / Triangle / Saw, free (Rate 0.02 … 8 Hz) or Sync (Division 2/1 … 1/16 incl. dotted
//   and triplets). In Sync the LFO locks to the song position while the transport plays and
//   restarts on the bar (a cycle longer than a bar restarts on the span of bars that holds it);
//   stopped, it free-runs at the synced rate. Stereo offsets the right channel's LFO 0 … 180°.
//   Mix: 0 = dry, 0.5 = dry and wet equal (the deepest notches), 1 = wet only (a pure vibrato).
//
// The delay is read with 4-point Hermite interpolation (no linear-interp dulling of the highs),
// never shorter than 2 samples. Delay, Depth, Feedback and Mix are smoothed over ~15 ms so
// automation doesn't zipper.
//
// All params are normalized 0..1 and APPEND ONLY. Persistence / automation / clone flow
// generically through the base Device.
// Telemetry (scopeRead): kTele live values (levels, τ now per channel, the LFO's place in its
// two-cycle window, the rate, the tempo, the comb's notch / null / peak).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = restart the LFO (free run: from phase 0), 1 = clear the delay line (stops a
// ringing comb), 2 = reset the meters.
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

class Flanger : public Device {
  public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Rate = 0, Delay, Depth, Feedback, Mix, Waveform, Sync, Division, Stereo, kNumParams };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0, // input peak (300 ms fall), dBFS
        S_OutPeakL,   // output peak, left (300 ms fall), dBFS
        S_OutPeakR,   // output peak, right, dBFS
        S_TauL,       // the left delay now, ms
        S_TauR,       // the right delay now, ms
        S_WinPhase,   // the LFO's place in its two-cycle window, 0..2 (cycles)
        S_RateHz,     // the LFO rate now, Hz (Sync: from the tempo)
        S_Bpm,        // the tempo the Sync rate follows
        S_Playing,    // 1 while the transport plays
        S_Locked,     // 1 while Sync is on and the LFO is locked to the song position
        S_SampleRate,
        S_BaseMs, // the Delay param, ms (the centre of the sweep)
        S_TauMin, // the sweep's shortest / longest delay, ms
        S_TauMax,
        S_NotchHz,    // the first notch of the left comb now, Hz
        S_NotchMinHz, // the first notch's sweep, Hz (at τ max … τ min)
        S_NotchMaxHz,
        S_NullDb,   // how deep the notches go, dB
        S_PeakDb,   // the comb's peaks, dB
        S_DivBeats, // the Sync division, beats per cycle
        S_BarBeats, // beats per bar (the restart span's unit)
        S_Cpu,      // share of real time spent in process()
        S_Signal,   // 1 while signal comes in (above −70 dBFS)
        S_Cycles,   // LFO cycles since load / the last restart (free run) or the song cycle (Sync)
        kTele = 32
    };

    static constexpr int kDivs = 9;
    // Sync divisions, slowest → fastest: beats per cycle, and their names.
    static constexpr double kDivBeats[kDivs] = {8.0, 4.0, 2.0, 1.5, 1.0, 2.0 / 3.0, 0.5, 1.0 / 3.0, 0.25};
    static constexpr const char* kDivNames[kDivs] = {"2/1", "1/1", "1/2", "1/4D", "1/4", "1/4T", "1/8", "1/8T", "1/16"};
    static constexpr int kDefaultDiv = 1; // 1/1

    // Ranges (mirrored by the card and MCP).
    static constexpr double kRateLo = 0.02, kRateHi = 8.0;  // Hz, exponential
    static constexpr double kDelayLo = 0.1, kDelayHi = 8.0; // ms, exponential
    static constexpr double kFbMax = 0.95;                  // feedback ±95 %
    static constexpr double kDepthScale = 0.92;             // τ swings Delay · (1 ± 0.92 · Depth)

    Flanger();
    void setSampleRate(double sr, int32_t maxBlock) override;

    // Musical clock for Sync (engine calls this before process()).
    void setTransport(double beatStart, double spb, bool playing) override;
    void setTransportInfo(const TransportInfo& ti) override;

    void process(float* buf, int32_t frames) override;

    // Verbatim DSP, all std::-exact (the LFO steers the
    // modulated delay reads inside the feedback loop). Silent combs auto-sleep via
    // isIdle(). Params unchanged.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    enum { A_Restart = 0, A_Clear = 1, A_ResetMeters = 2 }; // deviceAction ids
    void deviceAction(int32_t id, int32_t iarg, float farg) override;

    // Telemetry block. Called from the UI / MCP thread; lock-free against audio.
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    const char* displayName() const override { return "Nota Flanger"; }
    int32_t builtinKind() const override { return 23; }

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
    double rateFreeHz() const { return expMap(get(Rate), kRateLo, kRateHi); }
    double baseMs() const { return expMap(get(Delay), kDelayLo, kDelayHi); }
    double feedback() const { return (std::clamp((double)get(Feedback), 0.0, 1.0) - 0.5) * 2.0 * kFbMax; }
    double stereoDeg() const { return std::clamp((double)get(Stereo), 0.0, 1.0) * 180.0; }

    // |dry ± wet·H(θ)| / (dry + wet), H = e^{−jθ} / (1 − fb·e^{−jθ}), θ = ωτ — the comb's magnitude
    // (the wet polarity follows the feedback's sign, as in process()).
    static double combMag(double dry, double wet, double fb, double th);

  private:
    static double combMagSigned(double dry, double wet, double fb, double c, double s);
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kBuf = 16384; // ≥ 15.4 ms at 768 kHz
    static constexpr int kMask = kBuf - 1;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    struct Comb {
        double notchTheta, nullDb, peakDb;
    };
    // The first notch (the phase θ = ωτ in (0, 2π] where the comb dips deepest — π for positive
    // feedback, 2π for negative), its depth and the peak level.
    Comb comb() const;

    const char* modeWord() const;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float sanitize(float v) {
        return std::isfinite(v) && std::fabs(v) > 1e-20f ? std::clamp(v, -64.0f, 64.0f) : 0.0f;
    }

    // LFO in [−1, 1] at phase p (cycles; wraps) — the mockup's shapes: all start at 0 rising.
    static double lfo(int wave, double p);

    // 4-point Hermite read `d` samples behind the write head (d ≥ 2, so every tap is written).
    float readLine(const float* line, double d) const;

    void clearLine();
    void resetMeters() { inPkM_ = outPkLM_ = outPkRM_ = 0.0f; }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float lineL_[kBuf] = {}, lineR_[kBuf] = {};
    int w_ = 0;
    int64_t cycle_ = 0;
    double frac_ = 0.0;
    bool snap_ = true;
    double baseS_ = 2.5, depthS_ = 0.8, fbS_ = 0.7, dryS_ = 1.0, wetS_ = 1.0;
    double beatStart_ = 0.0, spb_ = 0.0, barBeats_ = 4.0;
    bool playing_ = false;
    float inPkM_ = 0.0f, outPkLM_ = 0.0f, outPkRM_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<bool> restartReq_{false}, clearReq_{false}, resetReq_{false};
    std::atomic<float> srA_{44100.0f}, inPkA_{-120.0f}, outPkLA_{-120.0f}, outPkRA_{-120.0f};
    std::atomic<float> tauLA_{0.0f}, tauRA_{0.0f}, fracA_{0.0f}, rateA_{0.2f}, bpmA_{120.0f}, barA_{4.0f}, cpuA_{0.0f};
    std::atomic<int64_t> cycleA_{0};
    std::atomic<int> playingA_{0}, lockedA_{0};
    bool idle_ = false;
    uint64_t quietSamples_ = 0;
    float feedPk_ = 0.0f; // peak loop-feed push (blocks sleep while the comb rings)
};

} // namespace nota
