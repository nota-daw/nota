// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Chorus (device kind 25, mockup "Nota Chorus") — modulated delay voices in three
// modes:
//
//   Classic   two voices, L and R, each on its own channel's delay line; the right voice's LFO
//             runs Offset (0 … 180°) behind the left.     τ = 8 ms ± 5 ms · Amount
//   Ensemble  three voices 120° apart (V1 left, V2 centre, V3 right) — the string-machine sound.
//             τ = 12 ms ± 6 ms · Amount (Offset is not used)
//   Vibrato   the Classic pair on a short line, wet only (Mix is locked to 100 %).
//             τ = 3 ms ± 2.5 ms · Amount
//
// Per voice τ(t) = base + dep · sin(2π(p + φ)); the delay's slope detunes the voice by
// 1200 · log2(1 − dτ/dt) cents, at most ±1200 · log2(1 + dep · 2π · rate). The wet path:
//
//   in → HPF (off, or 20 Hz … 2 kHz, 12 dB/oct) → + Feedback · wet → Warmth → delay line
//   voices → wet L / R → Width (M/S: 0 = mono, 100 % = as is, 200 % = side +6 dB)
//   out = (dry · x + wet_gain · wet) / (dry + wet_gain)   (Mix 0.5 = equal; Vibrato = wet only)
//
// Warmth is a bucket-brigade flavour on the line input: a one-pole low-pass that closes from
// 20 kHz to ~2.5 kHz and a soft tanh saturation that grows with it. Feedback is bipolar ±90 %.
// LFO: a sine, free (Rate 0.02 … 8 Hz) or Sync (Division 4/1 … 1/16 incl. triplets). In Sync the
// LFO locks to the song position while the transport plays and restarts on the bar (a cycle longer
// than a bar restarts on the span of bars that holds it); stopped, it free-runs at the synced rate.
//
// The delay is read with 4-point Hermite interpolation. Amount, Feedback, Offset, Width, Warmth and
// Mix glide over ~15 ms; a Mode change fades the wet out and back in (~8 ms each way) instead of
// jumping the delay.
//
// All params are normalized 0..1 and APPEND ONLY. Persistence / automation / clone flow
// generically through the base Device.
// Telemetry (scopeRead): kTele live values (levels, τ and cents now per voice, the LFO's place in
// its two-cycle window, the rate, the tempo, the sweep and the detune range).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = restart the LFO (free run: from phase 0), 1 = clear the delay lines (stops a
// ringing feedback loop), 2 = reset the meters.
// Header-only, allocation-free, JUCE-free.

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

class Chorus : public Device {
  public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Rate = 0, Mode, Sync, Division, Offset, Hpf, Amount, Feedback, Width, Warmth, Mix, kNumParams };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0, // input peak (300 ms fall), dBFS
        S_OutPeakL,   // output peak, left (300 ms fall), dBFS
        S_OutPeakR,   // output peak, right, dBFS
        S_Tau1,       // each voice's delay now, ms (V3 = 0 outside Ensemble)
        S_Tau2,
        S_Tau3,
        S_Cents1, // each voice's detune now, cents
        S_Cents2,
        S_Cents3,
        S_WinPhase, // the LFO's place in its two-cycle window, 0..2 (cycles)
        S_RateHz,   // the LFO rate now, Hz (Sync: from the tempo)
        S_Bpm,      // the tempo the Sync rate follows
        S_Playing,  // 1 while the transport plays
        S_Locked,   // 1 while Sync is on and the LFO is locked to the song position
        S_SampleRate,
        S_BaseMs, // the mode's centre delay, ms
        S_TauMin, // the sweep's shortest / longest delay, ms
        S_TauMax,
        S_MaxCents, // the largest detune the sweep reaches, ± cents
        S_Voices,   // 2 or 3
        S_Mode,     // 0 Classic, 1 Ensemble, 2 Vibrato
        S_HpHz,     // the wet high-pass corner, Hz (0 = off)
        S_WidthPct, // Width, %
        S_DivBeats, // the Sync division, beats per cycle
        S_BarBeats, // beats per bar (the restart span's unit)
        S_Cpu,      // share of real time spent in process()
        S_Signal,   // 1 while signal comes in (above −70 dBFS)
        S_Cycles,   // LFO cycles since load / the last restart (free run) or the song cycle (Sync)
        kTele = 32
    };

    static constexpr int kModes = 3;
    static constexpr const char* kModeNames[kModes] = {"Classic", "Ensemble", "Vibrato"};
    // Per mode: the centre delay and the swing at Amount 100 %, ms.
    static constexpr double kBaseMs[kModes] = {8.0, 12.0, 3.0};
    static constexpr double kDepMs[kModes] = {5.0, 6.0, 2.5};

    static constexpr int kDivs = 9;
    // Sync divisions, slowest → fastest: beats per cycle, and their names.
    static constexpr double kDivBeats[kDivs] = {16.0, 8.0, 4.0, 2.0, 1.0, 2.0 / 3.0, 0.5, 1.0 / 3.0, 0.25};
    static constexpr const char* kDivNames[kDivs] = {"4/1", "2/1", "1/1", "1/2", "1/4", "1/4T", "1/8", "1/8T", "1/16"};
    static constexpr int kDefaultDiv = 2; // 1/1

    // Ranges (mirrored by the card and MCP).
    static constexpr double kRateLo = 0.02, kRateHi = 8.0; // Hz, exponential
    static constexpr double kHpLo = 20.0, kHpHi = 2000.0;  // Hz, exponential; below 0.01 = off
    static constexpr double kFbMax = 0.90;                 // feedback ±90 %
    static constexpr double kWidthMax = 2.0;               // 200 %

    Chorus();
    void setSampleRate(double sr, int32_t maxBlock) override;
    // Musical clock for Sync (engine calls this before process()).
    void setTransport(double beatStart, double spb, bool playing) override;
    void setTransportInfo(const TransportInfo& ti) override;
    void process(float* buf, int32_t frames) override;

    // Verbatim DSP, all std::-exact (the LFO steers the
    // modulated delay reads, the warmth tanh sits in the feedback loop). Silent
    // feedback loops auto-sleep via isIdle(). Params unchanged.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    enum { A_Restart = 0, A_Clear = 1, A_ResetMeters = 2 }; // deviceAction ids
    void deviceAction(int32_t id, int32_t iarg, float farg) override;

    // Telemetry block. Called from the UI / MCP thread; lock-free against audio.
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    const char* displayName() const override { return "Nota Chorus"; }
    int32_t builtinKind() const override { return 25; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override;

    // ---- shared with the card / MCP through the guide above ----------------------------
    int modeIndex() const { return std::clamp((int)std::lround(get(Mode) * 2.0f), 0, 2); }
    int divIndex() const { return std::clamp((int)std::lround(get(Division) * (kDivs - 1)), 0, kDivs - 1); }
    int voices() const { return modeIndex() == 1 ? 3 : 2; }
    double rateFreeHz() const { return expMap(get(Rate), kRateLo, kRateHi); }
    double hpfHz() const {
        const float v = get(Hpf);
        return v < 0.01f ? 0.0 : expMap(v, kHpLo, kHpHi);
    }
    double feedback() const { return (std::clamp((double)get(Feedback), 0.0, 1.0) - 0.5) * 2.0 * kFbMax; }
    double offsetDeg() const { return std::clamp((double)get(Offset), 0.0, 1.0) * 180.0; }
    double widthPct() const { return std::clamp((double)get(Width), 0.0, 1.0) * kWidthMax * 100.0; }
    double depMs() const { return kDepMs[modeIndex()] * std::clamp((double)get(Amount), 0.0, 1.0); }

    // The detune of a voice whose delay swings ±dep ms at rate Hz, at LFO phase p (cycles): the
    // read head's speed is 1 − dτ/dt.
    static double centsAt(double depMs, double rateHz, double p);
    static double maxCents(double depMs, double rateHz) {
        return 1200.0 * std::log2(1.0 + depMs * 0.001 * 2.0 * kPi * rateHz);
    }

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kCentre = 0.70710678118654752; // the centre voice into each side (−3 dB)
    static constexpr double kEnsNorm = 1.0 / 1.4142135623730951;
    static constexpr int kBuf = 16384; // ≥ 21 ms at 768 kHz
    static constexpr int kMask = kBuf - 1;
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    struct MixW {
        double dry, wet;
    };
    MixW mixWeights(int mode) const;
    // The LFO phase of voice v (cycles): Ensemble spreads three voices 120° apart; the pair runs
    // the right voice `off` cycles behind the left.
    static double voicePhase(int mode, int v, double off);
    static const char* voiceName(int mode, int v);

    // TPT SVF high-pass, one channel.
    struct Svf {
        double s1 = 0.0, s2 = 0.0;
    };
    double hpTick(Svf& f, double x);
    // The warmth stage: a one-pole low-pass and a soft saturation, both fading in with Warmth.
    double warm(double& z, double x) const;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float sanitize(float v) {
        return std::isfinite(v) && std::fabs(v) > 1e-20f ? std::clamp(v, -64.0f, 64.0f) : 0.0f;
    }

    // 4-point Hermite read `d` samples behind the write head (d ≥ 2, so every tap is written).
    inline float readLine(const float* line, double d) const;

    void clearLines();
    void resetMeters() { inPkM_ = outPkLM_ = outPkRM_ = 0.0f; }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    float lineL_[kBuf] = {}, lineR_[kBuf] = {};
    int w_ = 0;
    int64_t cycle_ = 0;
    double frac_ = 0.0;
    bool snap_ = true;
    int mode_ = 0;
    double fade_ = 1.0;
    double amountS_ = 0.4, fbS_ = 0.0, offS_ = 0.5, widthS_ = 1.0, warmS_ = 0.0, dryS_ = 1.0, wetS_ = 1.0;
    double hpS_ = 0.0, hpG_ = 0.0, hpK_ = 1.4142135623730951, hpA1_ = 1.0, lpA_ = 1.0;
    Svf hpL_, hpR_;
    double lpL_ = 0.0, lpR_ = 0.0;
    double beatStart_ = 0.0, spb_ = 0.0, barBeats_ = 4.0;
    bool playing_ = false;
    float inPkM_ = 0.0f, outPkLM_ = 0.0f, outPkRM_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<bool> restartReq_{false}, clearReq_{false}, resetReq_{false};
    std::atomic<float> srA_{44100.0f}, inPkA_{-120.0f}, outPkLA_{-120.0f}, outPkRA_{-120.0f};
    std::atomic<float> tauA_[3] = {}, centsA_[3] = {};
    std::atomic<float> fracA_{0.0f}, rateA_{0.8f}, bpmA_{120.0f}, barA_{4.0f}, cpuA_{0.0f};
    std::atomic<int64_t> cycleA_{0};
    std::atomic<int> playingA_{0}, lockedA_{0}, modeA_{0};
    bool idle_ = false;
    uint64_t quietSamples_ = 0;
    float feedPk_ = 0.0f; // peak loop-feed push (blocks sleep while the tail rings)
};

} // namespace nota
