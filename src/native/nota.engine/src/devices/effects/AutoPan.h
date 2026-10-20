// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Orbit (device kind 9, formerly Auto Pan) — auto-pan and tremolo on one LFO:
// the LFO drives each channel's amplitude, and a Phase offset between the left and right LFOs
// sweeps continuously from tremolo (0°, both channels dip together) to auto-pan (180°, one
// channel loud while the other is quiet). Waveform Sine / Triangle / Saw / Square / S&H;
// Shape sharpens the smooth waves toward a square and, on S&H, is the glide between steps;
// Amount sets the depth, Mix blends dry / wet.
//
//   rate: Free (Rate, 0.01..40 Hz) or Sync (Division, 4/1 … 1/32 incl. dotted and triplets).
//   In Sync the LFO locks to the song position while the transport plays and restarts on the
//   bar (a cycle longer than a bar restarts on the span of bars that holds it); stopped, it
//   free-runs at the synced rate.
//
// S&H steps come from a hash of the cycle index, so the right channel plays the same random
// line as the left, Phase later — at 0° the steps move both channels together (a random
// tremolo), at 180° they pan. The channel gains are slewed over ~1 ms so a Square or a
// transport jump doesn't click.
//
// All params are normalized 0..1 and APPEND ONLY: 0..5 are the original layout; Sync and
// Division are appended and default to Free, so older projects open unchanged. Persistence /
// automation / clone flow generically through the base Device.
// Telemetry (scopeRead): kTele live values (levels, the gains now, the pan, the LFO's place in
// its two-cycle window, the S&H steps around it, the rate, the tempo).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = restart the LFO (free run: from phase 0), 1 = reset the meters.
// The live stereo position (0 = hard left, 0.5 = centre, 1 = hard right) also stays on gainReductionDb().
// Allocation-free, JUCE-free.
// The sine LFO uses fast_math::sin (the LFO purely
// scales gain — no steering — so the <0.0007 error stays downstream); the Shape
// sharpening uses tanhPrecise with a per-block hoisted 1/tanh(kk); silent blocks
// early-out in process() (no delay lines / filters / envelopes: silent in is
// silent out for any gain; the free-run phase advances, meters decay).

#pragma once

#include "devices/Device.h"
#include "engine/TransportInfo.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <string>

namespace nota {

class AutoPan : public Device {
  public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Rate = 0, Amount, Waveform, Shape, Phase, Mix, Sync, Division, kNumParams };

    // Telemetry slots (scopeRead).
    enum {
        S_InPeak = 0, // input peak (300 ms fall), dBFS
        S_OutPeakL,   // output peak, left (300 ms fall), dBFS
        S_OutPeakR,   // output peak, right, dBFS
        S_GainL,      // the left gain now (after Mix), linear 0..1
        S_GainR,      // the right gain now, linear
        S_Pan,        // the stereo position now, (gR − gL) / (gR + gL): −1 left … +1 right
        S_WinPhase,   // the LFO's place in its two-cycle window, 0..2 (cycles)
        S_RateHz,     // the LFO rate now, Hz (Sync: from the tempo)
        S_Bpm,        // the tempo the Sync rate follows
        S_Playing,    // 1 while the transport plays
        S_Locked,     // 1 while Sync is on and the LFO is locked to the song position
        S_SampleRate,
        S_Sh0, // the S&H steps of the cycles window−1, window, window+1, window+2 (−1..1)
        S_Sh1,
        S_Sh2,
        S_Sh3,
        S_PanMin, // the pan's swing over the window, −1..1
        S_PanMax,
        S_DivBeats, // the Sync division, beats per cycle
        S_BarBeats, // beats per bar (the restart span's unit)
        S_Cpu,      // share of real time spent in process()
        S_Signal,   // 1 while signal comes in (above −70 dBFS)
        S_Floor,    // the lowest gain (1 − Amount·Mix), linear
        S_Cycles,   // LFO cycles since load / the last restart (free run) or the song cycle (Sync)
        kTele = 32
    };

    static constexpr int kDivs = 16;
    // Sync divisions, slowest → fastest: beats per cycle, and their names.
    static constexpr double kDivBeats[kDivs] = {
        16.0, 8.0, 4.0, 3.0, 2.0, 4.0 / 3.0, 1.5, 1.0, 2.0 / 3.0, 0.75, 0.5, 1.0 / 3.0, 0.375, 0.25, 1.0 / 6.0, 0.125};
    static constexpr const char* kDivNames[kDivs] = {"4/1", "2/1", "1/1", "1/2D", "1/2", "1/2T", "1/4D", "1/4", "1/4T",
        "1/8D", "1/8", "1/8T", "1/16D", "1/16", "1/16T", "1/32"};
    static constexpr int kDefaultDiv = 10; // 1/8

    AutoPan();
    void setSampleRate(double sr, int32_t maxBlock) override;

    // Musical clock for Sync (engine calls this before process()).
    void setTransport(double beatStart, double spb, bool playing) override;
    void setTransportInfo(const TransportInfo& ti) override;

    // Live stereo position (0 = hard left, 0.5 = centre, 1 = hard right) — the old pan dot's feed.
    float gainReductionDb() const override { return panPos_.load(std::memory_order_relaxed); }

    void process(float* buf, int32_t frames) override;

    enum { A_Restart = 0, A_ResetMeters = 1 }; // deviceAction ids
    void deviceAction(int32_t id, int32_t iarg, float farg) override;

    // Telemetry block. Called from the UI / MCP thread; lock-free against audio.
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    // In-process silence bypass only (see process()): the free-running LFO is a
    // musical clock, so engine-level sleep would freeze timing — isIdle stays false.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override { idle_ = false; }

    const char* displayName() const override { return "Nota Orbit"; }
    int32_t builtinKind() const override { return 9; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override;

    // ---- shared with the card through the guide above ----------------------------------
    int waveIndex() const { return std::clamp((int)std::lround(get(Waveform) * 4.0f), 0, 4); }
    int divIndex() const { return std::clamp((int)std::lround(get(Division) * (kDivs - 1)), 0, kDivs - 1); }

  private:
    static constexpr double kPi = 3.14159265358979323846;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    static float panOf(float gl, float gr) { return (gr - gl) / std::max(gl + gr, 0.001f); }
    static const char* modeWord(double deg) {
        return deg <= 15.0 || deg >= 345.0 ? "tremolo" : std::fabs(deg - 180.0) <= 15.0 ? "auto-pan" : "offset pan";
    }

    // The S&H step of cycle k, −1..1 — a hash, so the same cycle always draws the same step.
    static float hashBip(int64_t k);

    // LFO value in [−1, 1] at cycle k, phase f (0..1). Shape sharpens the smooth waves toward
    // a square; on S&H it is the glide from the previous step; Square ignores it.
    // invTanhKk is the hoisted 1/tanhPrecise(1+shape*12) for the block (1 when shape ~ 0).
    static float lfoAt(int wave, float shape, int64_t k, double f, float invTanhKk);

    void resetMeters() { inPkM_ = outPkLM_ = outPkRM_ = 0.0f; }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    int64_t cycle_ = 0;
    double frac_ = 0.0;
    float gLs_ = -1.0f, gRs_ = -1.0f;
    double beatStart_ = 0.0, spb_ = 0.0, barBeats_ = 4.0;
    bool playing_ = false;
    float inPkM_ = 0.0f, outPkLM_ = 0.0f, outPkRM_ = 0.0f;
    double cpuS_ = 0.0;
    std::atomic<bool> restartReq_{false}, resetReq_{false};
    std::atomic<float> panPos_{0.5f};
    std::atomic<float> srA_{44100.0f}, inPkA_{-120.0f}, outPkLA_{-120.0f}, outPkRA_{-120.0f};
    std::atomic<float> gainLA_{1.0f}, gainRA_{1.0f}, fracA_{0.0f}, rateA_{1.45f}, bpmA_{120.0f}, barA_{4.0f},
        cpuA_{0.0f};
    std::atomic<int64_t> cycleA_{0};
    std::atomic<int> playingA_{0}, lockedA_{0};
    bool idle_ = false;
    bool snap_ = false; // set by the silence bypass: re-snap the gain slew on wake
};

} // namespace nota
