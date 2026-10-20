// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Beat Repeat (device kind 11, mockup "Nota Beat Repeat") — a tempo-synced
// glitch / stutter effect in the spirit of classic beat-repeat units. Every Interval (bar-
// synced, shifted by Offset) it rolls Chance; when it fires it captures a Grid-length slice
// of the incoming audio (the capture pass plays through as it is) and then loops it for the
// rest of the Gate, each repeat transposed (Pitch, then Pitch Decay per repeat), faded
// (Decay), filtered (Filter On · Type LP / BP / HP · Freq · Width in octaves, optionally
// narrowing and following the pitch down with every repeat) and re-levelled (Volume). Three
// output modes — Mix (repeats over the dry), Insert (repeats replace the dry while active)
// and Gate (only the burst sounds). Variation lets the grid float a few steps per trigger,
// Triplet turns the grid into triplets, Repeat forces a repeat of the current slice for as
// long as it is on (Latch makes the card's button latch instead of momentary).
//
//   in ─▶ ring ──▶ [capture: dry] ─▶ [repeat: read slice · pitch · filter · decay · volume]
//          │                                                   │
//          └──────────────── dry ─▶ mode (Mix / Insert / Gate) ◀┘ ─▶ dry/wet Mix ─▶ out
//
// Needs the musical clock: the engine hands it the block's beat position via setTransport()
// and the time signature via setTransportInfo() right before process().
// Allocation-free after setSampleRate, JUCE-free. Verbatim math throughout (control
// path — no FastMath substitution). Silent stretches auto-sleep via isIdle(), but only once
// the transport is stopped and no burst is sounding; while playing, interval boundaries keep
// firing Chance rolls (which consume the RNG), so the hangover never accrues. Params
// normalized 0..1, APPEND ONLY (0..15 are the original layout; the appended ones default to
// the old sound).
// Telemetry (scopeRead): kTele live values, then the timeline window (kCells columns of
// input peak dB · kind 0 dry / 1 capture / 2 repeat · repeat gain) and the last interval's
// input peaks (kWave columns), both left → right in musical time.
// The interval phase is also published through gainReductionDb() (older viewers).
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = reset (stop the repeat, clear the timeline and meters), 1 = trigger a
// repeat now (as if the interval fired, ignoring Chance).

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
#include <vector>

namespace nota {

class BeatRepeat : public Device {
  public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum {
        Interval = 0, // 1/8 · 1/4 · 1/2 · 1 bar · 2 bars · 4 bars (6 steps)
        Offset,       // trigger point inside the interval, 16ths of it
        Grid,         // slice: 1/4 · 1/8 · 1/16 · 1/32 · (legacy 1/8T · 1/16T) — 6 steps
        Variation,    // the grid floats ±round(6v) steps (×2 / ÷2) per trigger
        Chance,       // probability an interval fires
        Gate,         // burst length, 16ths of the interval
        Pitch,        // repeat transpose ±12 st (bipolar)
        PitchDecay,   // 0..6 st lower per repeat
        Volume,       // repeat level ±12 dB (bipolar)
        Decay,        // per-repeat fade (0..55 %)
        FilterOn,     // >=0.5 the repeats go through the filter
        FilterFreq,   // 50..18000 Hz (exp)
        FilterWidth,  // 0.5..3.5 octaves
        Mode,         // 0 Mix · 0.5 Insert · 1 Gate
        Mix,          // effect dry/wet
        Repeat,       // >=0.5 repeat the current slice now, for as long as it is on (was "Latch")
        // ---- appended (redesign) ----
        Latch,        // >=0.5 the card's Repeat button latches (UI mode, saved with the device)
        Triplet,      // >=0.5 the grid is in triplets (×2/3)
        FilterType,   // 0 LP · 0.5 BP (the old filter) · 1 HP
        FilterNarrow, // >=0.5 each repeat narrows the band and follows the pitch down
        kNumParams
    };

    // Telemetry slots (scopeRead).
    enum {
        S_InDb = 0, // input level, dBFS
        S_RepDb,    // repeat (wet) level, dBFS (−120 when not repeating)
        S_OutDb,    // output level, dBFS
        S_State,    // 0 idle (dry), 1 capture, 2 repeat
        S_Pass,     // pass in the burst (1 = the capture, 2.. = repeats)
        S_Passes,   // passes the burst will take (gate / slice; 0 = until released)
        S_Phase,    // interval phase 0..1 (offset applied)
        S_WinPhase, // timeline window phase 0..1
        S_WinBeats, // timeline window, beats (2 intervals)
        S_IntervalBeats,
        S_SliceBeats, // the current / last burst's slice, beats (after variation)
        S_SliceMs,
        S_Bpm,
        S_Bar,       // 1-based bar
        S_BeatInBar, // 1-based beat within the bar
        S_SampleRate,
        S_Cpu,       // share of real time spent in process()
        S_Latency,   // samples (0)
        S_Forced,    // 1 = Repeat holds the burst
        S_Gain,      // the current repeat's gain (decay × volume), linear
        S_PitchSt,   // the current repeat's pitch, semitones
        S_FilterHz,  // the current repeat's filter centre, Hz
        S_FilterOct, // the current repeat's filter width, octaves
        S_Triggers,  // bursts since the last reset
        S_Playing,
        S_TsNum,
        S_BarBeats, // beats per bar
        S_Cells,    // kCells
        S_WaveN,    // kWave
        S_Skipped,  // intervals Chance skipped since the last reset
        kTele = 32
    };
    static constexpr int kCells = 64;
    static constexpr int kWave = 128;
    static constexpr int kCellAt = kTele;              // level dB · kind · gain
    static constexpr int kWaveAt = kTele + 3 * kCells; // input peak (linear)
    static constexpr int kScope = kWaveAt + kWave;

    enum { A_Reset = 0, A_Trigger = 1 };

    BeatRepeat();
    void setSampleRate(double sr, int32_t maxBlock) override;
    void setTransport(double beatStart, double spb, bool playing) override;
    void setTransportInfo(const TransportInfo& ti) override;
    float gainReductionDb() const override;
    void deviceAction(int32_t id, int32_t iarg, float farg) override;
    void process(float* buf, int32_t frames) override;

    // Sleep contract: repeats outlive silent input, so the tail is real. Settles to
    // idle only once the transport is stopped and no burst is sounding.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    int32_t scopeRead(float* out, int32_t maxSamples) const override;
    const char* displayName() const override;
    int32_t builtinKind() const override;
    int32_t paramCount() const override;
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override;
    float paramMax(int32_t /*i*/) const override;
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;
    std::string deviceText(int32_t id) const override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kGridStraight[4] = {1.0, 0.5, 0.25, 0.125};
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    int idx(int p, int n) const { return std::clamp((int)std::lround(get(p) * (n - 1)), 0, n - 1); }
    int offsetSteps() const { return std::clamp((int)std::lround(get(Offset) * 16.0f), 0, 16) % 16; }
    int gateSteps() const { return std::clamp((int)std::lround(get(Gate) * 16.0f), 0, 16); }
    int varSteps() const { return std::clamp((int)std::lround(get(Variation) * 6.0f), 0, 6); }
    double filterHz() const { return expMap(get(FilterFreq), 50.0, 18000.0); }
    double filterOct() const { return 0.5 + (double)get(FilterWidth) * 3.0; }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    float coef(float ms) const { return (float)std::exp(-1.0 / (std::max(0.01f, ms) * 0.001 * sr_)); }
    float uni() {
        rng_ = rng_ * 1664525u + 1013904223u;
        return (float)((rng_ >> 8) & 0xFFFFFF) / 16777216.0f;
    }
    static std::string hzText(double hz) {
        char b[32];
        if (hz >= 1000.0) std::snprintf(b, sizeof b, "%.1f kHz", hz / 1000.0);
        else
            std::snprintf(b, sizeof b, "%.0f Hz", hz);
        return b;
    }

    double intervalBeatsFor(double barBeats) const;
    double gridBeats() const;
    std::string gridName() const;

    // A new burst: the capture pass (the slice plays through as it is) and then the repeats.
    void startBurst(double intervalBeats, bool forced);
    // Pass p >= 2 is repeat p−1: its gain, pitch and filter.
    void beginPass(int p);

    inline float readFrac(int c, double pos) const {
        double rp = std::fmod(pos, (double)(mask_ + 1));
        if (rp < 0.0) rp += (double)(mask_ + 1);
        const int i0 = (int)rp & mask_;
        const int i1 = (i0 + 1) & mask_;
        const double fr = rp - std::floor(rp);
        return (float)(ring_[c][(size_t)i0] * (1.0 - fr) + ring_[c][(size_t)i1] * fr);
    }
    // TPT state-variable filter: LP / BP (peak ≈ unity) / HP.
    struct SvfSt {
        double ic1 = 0.0, ic2 = 0.0;
    };
    inline float svfRun(int c, float in) {
        SvfSt& s = svf_[c];
        const double v3 = in - s.ic2;
        const double v1 = fA1_ * s.ic1 + fA1_ * fG_ * v3;
        const double v2 = s.ic2 + fG_ * v1;
        s.ic1 = 2.0 * v1 - s.ic1;
        s.ic2 = 2.0 * v2 - s.ic2;
        if (fType_ == 0) return (float)v2;
        if (fType_ == 2) return (float)(in - fK_ * v1 - v2);
        return (float)(fK_ * v1);
    }

    void resetState();

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    std::vector<float> ring_[2];
    int mask_ = 0, wp_ = 0;
    SvfSt svf_[2];
    double fG_ = 0.1, fK_ = 1.0, fA1_ = 1.0;
    int fType_ = 1;
    bool filterOn_ = false;
    // Transport (set each block).
    double beatStart_ = 0.0, spb_ = 22050.0;
    bool playing_ = false;
    int tsNum_ = 4, tsDen_ = 4;
    // Burst state.
    bool repeating_ = false, capturing_ = false, forced_ = false;
    double prevIdx_ = -1e18;
    double captureBase_ = 0.0, repeatPhase_ = 0.0, sliceLen_ = 0.0, gateSamples_ = 0.0, sliceBeats_ = 0.25;
    int64_t capElapsed_ = 0, burstElapsed_ = 0;
    int pass_ = 0, passes_ = 0;
    double passGain_ = 1.0, passPitch_ = 0.0, passRate_ = 1.0, passHz_ = 949.0, passOct_ = 2.0;
    float xf_ = 0.0f, gateDry_ = 0.0f;
    uint32_t rng_ = 0x2F6E10A3u;
    int64_t triggers_ = 0, skipped_ = 0;
    // Meters / telemetry (audio writes, UI reads).
    float inEnv_ = 0.0f, repEnv_ = 0.0f, outEnv_ = 0.0f;
    std::vector<float> cells_[3] = {
        std::vector<float>(kCells, -120.0f), std::vector<float>(kCells, 0.0f), std::vector<float>(kCells, 0.0f)};
    std::vector<float> wave_ = std::vector<float>(kWave, 0.0f);
    int cell_ = -1, waveCol_ = -1, lastKindCell_ = -1;
    float cellPeak_ = 0.0f, winPhase_ = 0.0f, bar_ = 1.0f, beatInBar_ = 1.0f;
    double cpuS_ = 0.0;
    std::atomic<bool> resetReq_{false}, triggerReq_{false};
    std::atomic<float> phasePub_{0.0f};
    std::atomic<float> inDb_{-120.0f}, repDb_{-120.0f}, outDb_{-120.0f}, stateA_{0.0f}, passA_{0.0f}, passesA_{0.0f},
        winPhaseA_{0.0f}, winBeatsA_{8.0f}, intervalA_{4.0f}, sliceBeatsA_{0.25f}, bpmA_{120.0f}, barA_{1.0f},
        beatA_{1.0f}, srA_{44100.0f}, cpuA_{0.0f}, forcedA_{0.0f}, gainA_{0.0f}, pitchA_{0.0f}, fHzA_{949.0f},
        fOctA_{2.0f}, trigA_{0.0f}, skipA_{0.0f}, playA_{0.0f}, tsA_{4.0f}, barBeatsA_{4.0f};

    // Auto-sleep hangover.
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
};

} // namespace nota
