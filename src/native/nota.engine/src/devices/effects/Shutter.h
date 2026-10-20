// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Shutter (device kind 19, mockup "Nota Shutter") — a noise gate / ducker.
// The "shutter" opens when the detector crosses the Threshold and closes when it falls back
// past the Return (hysteresis, stops chatter); Attack / Hold / Release shape each opening,
// with a Shape (Linear ramps, Log = the classic one-pole, Snap = stays open then shuts hard),
// Floor sets how far the closed signal drops (−∞ = mute, higher = a range), Lookahead lets
// the gate open just before a transient, Flip turns the gate into a ducker and Retrigger
// makes every hit fire one attack → hold → release envelope (trigger mode). The detector keys
// off this track or an external sidechain (External Key), through its own 12 dB/oct band-pass
// (Det Filter) with a Listen monitor, as a fast peak follower or with Peak Hold.
//
//   key ─▶ [HP ─▶ LP] ─▶ peak follower ─▶ threshold / return / hold ─▶ envelope (shape)
//   in  ─▶ look-ahead delay ─────────────────────────────────────────▶ × gain ─▶ out
//
// A core Device (JUCE-free), allocation-free after setSampleRate. Params are
// normalized 0..1, denormalized in process(); APPEND ONLY (0..10 are the original layout, the
// appended ones default to the old sound). Persistence / automation / clone flow generically.
// Telemetry (scopeRead): kTele live values, then three kHist-column histories over the
// window (input peak dB, gate gain 0..1, detector dB), oldest first.
// Pattern (appended): a tempo-synced step pattern of gain — a rhythmic gate. Off / Volume /
// Map: Volume multiplies the output by 1 − depth·(1 − step) (in place of the threshold gate,
// or on top of it with Gated), Map leaves the audio alone and writes the pattern into the
// read-only "Pattern Out" param, a CV source for the Modular editor. 8 / 16 / 32 steps live in
// "Step 1".."Step 32" (quantised to 4 / 8 / 16 levels), the pattern spans 1/4 … 4 bars (Sync,
// locked to the song while it plays) or 4 … 0.25 Hz (Free); Smooth ramps into each step,
// Swing delays the off-steps, Retrig restarts it on every MIDI note of this track (or the key
// track). The pattern block (kPat values) follows the histories in scopeRead.
// deviceText: 0 = status line, 1 = live reading, 2 = parameter guide.
// deviceAction: 0 = reset the meters (peak GR, triggers, history), 1 = window (iarg 0 250 ms,
// 1 1 s, 2 4 s).
// Control-rate and envelope math kept std::-exact.
// Silent stretches auto-sleep via isIdle() under the flight-time rule: sleep engages
// only after quiet (input, key, and output ≤ −100 dB) outlasts the look-ahead line
// plus the detector/meter hangover with the gate closed and settled, so no buffered
// transient or ringing envelope is skipped; the pattern phase and trigger clock keep
// advancing through the skip so wake-up timing does not jump.

#pragma once

#include "devices/Device.h"
#include "devices/MidiDevice.h"
#include "engine/TransportInfo.h"

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

class Shutter : public Device {
  public:
    enum {
        Threshold = 0, // open level (0..1 → −70..0 dB)
        Return,        // hysteresis below threshold to close (0..1 → 0..24 dB)
        Attack,        // open time (exp 0.01..100 ms)
        Hold,          // minimum open time after the detector drops (exp 0.1..500 ms)
        Release,       // close time (exp 1..2000 ms)
        Floor,         // closed level (0..1 → −70..0 dB; 0 = full mute / −∞) — the "range"
        Lookahead,     // 0/1/5 ms (discrete)
        Flip,          // 0 = Gate, 1 = Duck (invert)
        DetHP,         // detector high-pass (exp 20..2000 Hz)
        DetLP,         // detector low-pass (exp 200..20000 Hz)
        Listen,        // >=0.5 monitor the detector (band-passed key) instead of the gated audio
        // ---- appended (redesign) ----
        Shape,       // 0 Linear / 0.5 Log (one-pole, the old sound) / 1 Snap
        Retrigger,   // >=0.5 trigger mode: each hit fires one attack → hold → release
        DetFilter,   // >=0.5 the detector band-pass is in (off = full-band key)
        PeakHold,    // >=0.5 the detector holds peaks (~40 ms fall instead of 3 ms)
        ExternalKey, // >=0.5 key off the sidechain source when one is routed
        // ---- appended (Pattern tab) ----
        PatTarget,     // 0 Off / 0.5 Volume / 1 Map
        PatRate,       // 5 steps: Sync 1/4 · 1/2 · 1 Bar · 2 Bars · 4 Bars, Free 4 · 2 · 1 · 0.5 · 0.25 Hz
        PatDepth,      // how far a 0 step pulls the gain down (0..1)
        PatSync,       // >=0.5 tempo-synced (Sync), else Free (Hz)
        PatSmooth,     // ramp into each step, 0..1 → 0..½ step
        PatSwing,      // off-step delay, 0..1 → 0..75 % (of half a step)
        PatSteps,      // 0 / 0.5 / 1 → 8 / 16 / 32 steps
        PatLevels,     // 0 / 0.5 / 1 → 4 / 8 / 16 levels
        PatRetrig,     // >=0.5 a MIDI note-on restarts the pattern
        PatGated,      // >=0.5 the pattern only plays while the threshold gate is open (gate × pattern)
        PatComplexity, // the dice's density (editor; not a sound param)
        PatOut,        // read-only: the pattern's value now 0..1 (written by process; Map's CV source)
        Step1,         // Step 1 .. Step 32, 0..1
        kNumSteps = 32,
        kNumParams = Step1 + kNumSteps
    };

    // Telemetry slots (scopeRead). 0..4 keep the original layout.
    enum {
        S_InDb = 0,   // input level (pre-gate), dBFS
        S_GateGain,   // gate gain now 0..1
        S_GrDb,       // gain reduction now, dB (>= 0)
        S_DetDb,      // detector level (filtered key), dBFS
        S_Open,       // 1 = the detector holds the gate open
        S_State,      // 0 closed, 1 attack, 2 open, 3 hold, 4 release
        S_OutDb,      // output level, dBFS
        S_OpenRatio,  // share of the window the gate was open, 0..1
        S_TrigPerBar, // openings in the last bar
        S_Triggers,   // openings since the last reset
        S_SampleRate,
        S_Cpu,      // share of real time spent in process()
        S_Latency,  // look-ahead, samples
        S_ExtKey,   // 1 = an external key is driving the detector
        S_KeyDb,    // key level before the detector filter, dBFS
        S_ThrDb,    // threshold, dBFS
        S_RetDb,    // close level (threshold − return), dBFS
        S_FloorDb,  // floor, dB (−120 = −∞)
        S_PeakGrDb, // largest reduction since the last reset, dB
        S_Bpm,
        S_WindowSec, // history window, seconds
        S_HistN,     // columns per history
        S_Envelope,  // envelope phase 0..1 (1 = fully engaged)
        kTele = 24
    };
    static constexpr int kHist = 128;
    static constexpr int kHistAt = kTele; // input peak dB · gate gain · detector dB
    // Pattern block after the histories.
    enum { P_Phase = 0, P_Step, P_Gain, P_Value, P_Target, P_Steps, P_Beats, kPat = 8 };
    static constexpr int kPatAt = kTele + 3 * kHist;
    static constexpr int kScope = kPatAt + kPat;
    static constexpr int kPushPattern[16] = {8, 0, 8, 8, 0, 8, 8, 0, 8, 0, 8, 8, 0, 8, 0, 8};

    Shutter();
    void setSampleRate(double sr, int32_t maxBlock) override;

    // Sidechain: external key source (Compressor / Ceiling plumbing).
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void setSidechain(const float* interleaved, int32_t frames) override {
        scBuf_ = interleaved;
        scFrames_ = frames;
    }
    bool acceptsSidechain() const override { return true; }

    void setTransportInfo(const TransportInfo& ti) override;

    // GR (dB, >=0) for the shell titlebar meter, like the Compressor.
    float gainReductionDb() const override { return grDb_.load(std::memory_order_relaxed); }

    int32_t latencySamples() const override { return laSamples(); }

    // Tempo: the pattern locks to the song while it plays.
    void setTransport(double beatStart, double samplesPerBeat, bool playing) override;

    // Pattern retrigger: this track's notes (or the key track's) when Retrig is on.
    bool wantsMidiKey() const override { return get(PatRetrig) >= 0.5f && patTarget() > 0; }
    bool wantsOwnMidiKey() const override { return wantsMidiKey(); }
    void setMidiKey(const MidiEv* evs, int32_t n) override {
        midiEvs_ = evs;
        midiN_ = n;
    }

    void deviceAction(int32_t id, int32_t iarg, float farg) override;

    void process(float* buf, int32_t frames) override;

    // Sleep contract: the look-ahead line plus detector/meter envelopes are the
    // engine-visible tail. Sleep engages only after the flight-time hangover
    // (see the .cpp); setParam/deviceAction wake.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // Telemetry block, then the three histories (oldest first). Writes as much of that layout
    // as fits in maxSamples. Lock-free; torn reads are fine.
    int32_t scopeRead(float* out, int32_t maxSamples) const override;

    const char* displayName() const override { return "Nota Shutter"; }
    int32_t builtinKind() const override { return 19; }

    int32_t paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override;
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override;
    void setParam(int32_t i, float v) override;

    // 0 — a one-line status; 1 — the live reading; 2 — a guide to the parameter values.
    std::string deviceText(int32_t id) const override;

    enum { A_ResetMeters = 0, A_Window = 1 };

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kLookMs[3] = {0, 1, 5};
    // Hangover before sleep: the look-ahead flight time plus detector/meter settling.
    static constexpr uint64_t kIdleSamples = 16384;

    // TPT state-variable filter (Q = 0.707) for the key band-pass.
    struct Svf {
        float g = 0.0f, k = 1.41421356f, a1 = 0.0f, a2 = 0.0f, a3 = 0.0f;
        struct St {
            float ic1 = 0.0f, ic2 = 0.0f;
        };
        float run(float x, St& s, bool high) const;
        float hp(float x, St& s) const { return run(x, s, true); }
        float lp(float x, St& s) const { return run(x, s, false); }
    };
    Svf svf(double hz) const;

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    int patTarget() const { return std::clamp((int)std::lround(get(PatTarget) * 2.0f), 0, 2); }
    int patRateIdx() const { return std::clamp((int)std::lround(get(PatRate) * 4.0f), 0, 4); }
    int patSteps() const {
        static constexpr int n[3] = {8, 16, 32};
        return n[std::clamp((int)std::lround(get(PatSteps) * 2.0f), 0, 2)];
    }
    int patLevels() const {
        static constexpr int n[3] = {4, 8, 16};
        return n[std::clamp((int)std::lround(get(PatLevels) * 2.0f), 0, 2)];
    }

    // The pattern's value at `pos` steps into it: odd steps start `swing` late, and each step
    // ramps over `smooth` from the one before (wrapping, so step 1 ramps from the last).
    static float patternAt(const float* v, int n, float pos, float smooth, float swing);
    int shapeIdx() const { return std::clamp((int)std::lround(get(Shape) * 2.0f), 0, 2); }
    int lookIdx() const { return std::clamp((int)std::lround(get(Lookahead) * 2.0f), 0, 2); }
    static double expMap(float v, double lo, double hi) {
        return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0));
    }
    static float dbToLin(float dbv) { return std::pow(10.0f, dbv / 20.0f); }
    static float db(float lin) { return lin > 1e-6f ? 20.0f * std::log10(lin) : -120.0f; }
    float coef(float ms) const { return (float)std::exp(-1.0 / (std::max(0.01f, ms) * 0.001 * sr_)); }
    int laSamples() const {
        return std::clamp((int)std::lround(kLookMs[lookIdx()] * 0.001 * sr_), 0, std::max(0, laMax_ - 1));
    }
    static std::string msText(double ms);
    static std::string hzText(double hz);
    static std::string dbText(float v);

    void onTrigger();

    void resetState();

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};

    std::vector<float> laBuf_;
    int laMax_ = 0, laW_ = 0;
    float detEnv_ = 0.0f, inEnv_ = 0.0f, outEnv_ = 0.0f, keyEnv_ = 0.0f, g_ = 1.0f, env_ = 0.0f;
    Svf::St hp_, lp_;
    int holdCtr_ = 0;
    bool open_ = false, armed_ = true;
    float openRatio_ = 0.0f, peakGr_ = 0.0f;
    double bpm_ = 120.0;
    int tsNum_ = 4, tsDen_ = 4;
    static constexpr int kTrig = 64;
    std::array<int64_t, kTrig> trigAt_{};
    int trigN_ = 0;
    int64_t clock_ = 0;
    int64_t trigTotal_ = 0;
    float colIn_ = 0.0f, colGate_ = 1.0f, colDet_ = 0.0f;
    int colN_ = 0;
    std::array<std::array<float, kHist>, 3> hist_{};
    std::atomic<int> histW_{0};
    double cpuS_ = 0.0;

    // Auto-sleep hangover (flight-time rule).
    uint64_t quietSamples_ = 0;
    bool idle_ = false;

    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
    std::atomic<bool> resetMetersReq_{false};
    std::atomic<int> windowIdx_{1};

    // Pattern runtime.
    double tBeat_ = 0.0, tSpb_ = 0.0;
    bool tPlaying_ = false, tFed_ = false;
    const MidiEv* midiEvs_ = nullptr;
    int32_t midiN_ = 0;
    double patPhase_ = 0.0;
    float patVal_ = 1.0f;
    std::atomic<float> patPhaseA_{0.0f}, patGainA_{1.0f}, patValA_{1.0f}, patBeatsA_{4.0f};

    std::atomic<float> inDb_{-120.0f}, outDb_{-120.0f}, keyDb_{-120.0f}, gateGain_{1.0f}, grDb_{0.0f}, peakGrA_{0.0f},
        detDb_{-120.0f}, openF_{0.0f}, stateA_{0.0f}, envA_{0.0f}, ratioA_{0.0f}, trigBarA_{0.0f}, trigTotA_{0.0f},
        extA_{0.0f}, bpmA_{120.0f}, winA_{1.0f}, srA_{44100.0f}, cpuA_{0.0f};
};

} // namespace nota
