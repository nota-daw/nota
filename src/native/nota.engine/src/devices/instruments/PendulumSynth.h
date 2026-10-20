// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Pendulum — a built-in generative "warm electric keys" synth (kind 8) with no direct
// equivalent elsewhere. Held notes form a chord; a set of balls swing like pendulums
// across a triangular field (∧∨). A ball's horizontal position maps to a chord degree;
// as it sweeps it crosses degree boundaries and plays that note, so several balls at
// different rates/phases weave an ever-evolving arpeggio out of one held chord. The
// tone is a soft additive-sine "keys" voice with a detuned chorus partner for warmth.
//
//   held chord ─▶ [ball phases → position → chord degree → trigger on crossing] ─▶ warm keys voices ─▶ out
//
// Sync (bar-locked) or Free (seconds); Rate can go negative (reverse); Motion picks one
// of four swing curves (Linear · Pendulum · Ease · Bounce); Quantize snaps triggers to a
// grid; Chord Sort picks up/down; Spread detunes the balls' rates so they drift apart;
// Pan Spread fans them across the stereo field; Humanize adds gentle level/pitch jitter;
// Hold latches the chord and First note restarts the pattern on a new chord; an optional
// Scale snaps generated notes. Needs the musical clock via Instrument::setTransport
// (samples-per-beat for Sync/Quantize) and setTransportInfo (the bar, for the note strip).
// All params ride the plugin-param interface (normalized 0..1) → automation / persist /
// clone free. Alloc-free.
//
// Telemetry (scopeRead, kTele floats) for the editor's lanes / note strip and the MCP
// reader — see the layout above kTele. Mirrored by Nota.Application/PendulumModel.cs.
//
// DSP notes: the additive voice partials use fast_math::sin (amplitude-domain only —
// the partial FREQUENCIES are unchanged, so tuning is exact); the FM modulator sine
// stays std::-exact (it bends phase → pitch), and the ball trajectory stays std::-exact
// (it sets trigger TIMING — a fast cos there would shift onsets). Sleep: voice release
// tails ring past the note length (hasTail); idle only when no voice sounds AND no chord
// is held AND the block is silent — a held chord keeps the pattern generating, so it
// must never sleep (nothing would wake it). noteOn/noteOff/setParam wake.

#pragma once

#include "devices/Instrument.h"
#include "engine/TransportInfo.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota {

class PendulumSynth final : public Instrument {
  public:
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        Balls = 0,
        Rate,
        Sync,
        Division,
        FreeRate,
        Motion,
        Quantize,
        ChordSort,
        Spread,
        NoteLen,
        Tone,
        Attack,
        Release,
        Detune,
        Volume,
        Wave,
        Bright,
        FM,
        // appended for the mockup 2l rework:
        Decay,
        PanSpread,
        Humanize,
        Hold,
        FirstNote,
        Root,
        ScaleMode,
        Reset,
        kNumParams
    };

    PendulumSynth();

    int32_t kind() const override { return 8; }
    const char* displayName() const override { return "Nota Pendulum"; }

    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }
    void setTransport(double beatStart, double spb, bool playing) override;
    void setTransportInfo(const TransportInfo& ti) override;

    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    // ---- telemetry ----------------------------------------------------------
    // Head (kHead): [0] sounding voices · [1] balls · [2] held-chord size · [3] last
    // generated pitch (−1 none yet) · [4] place in the bar 0..1 · [5] steps per bar (1/16
    // grid) · [6] seconds per swing at the base rate (0 = stopped) · [7] ball that fired
    // last (−1) · [8] seconds since the last note · [9] notes fired this bar · [10] beats
    // per bar · [11] transport rolling (0/1).
    // Then kMaxBalls × kBallStride: phase 0..1 · position 0..1 · direction (+1 → high,
    // −1 → low, 0 still) · pitch now (−1) · seconds to the wall ahead (−1 never) · seconds
    // since this ball fired · signed rate (1 = +100 %) · chord degree (−1).
    // Then kMaxSteps × 2 — the bar grid: pitch (−1 empty) · age in bars (0 this bar, 1 the
    // last). Then kMaxVoices level of each sounding voice (0 = silent slot).
    static constexpr int kHead = 12, kBallStride = 8, kMaxSteps = 32, kMaxLevels = 8;
    static constexpr int kBallsAt = kHead;
    static constexpr int kStepsAt = kBallsAt + 6 * kBallStride;
    static constexpr int kLevelsAt = kStepsAt + kMaxSteps * 2;
    static constexpr int kTele = kLevelsAt + kMaxLevels;
    int32_t scopeRead(float* out, int32_t maxN) const override;

    // Sleep contract: voice release tails ring past the note length (the engine-visible
    // tail). Idle only when silent AND voiceless AND chordless — a held chord keeps
    // generating, so it must never sleep (no MIDI would wake it). Settles after the
    // hangover; noteOn/noteOff/setParam wake.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override;
    std::string pluginParamName(int32_t i) const override;
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override;
    int32_t pluginParamIndexOfId(const std::string& id) const override;

    // ---- project state (kNumParams normalized floats, little-endian) -------
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    // ---- note events: held notes form the chord the balls arpeggiate --------
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    // Live held chord (message-thread read of an audio-thread snapshot), for the editor's
    // pendulum field to label its pitch lanes. Returns the count written into out.
    int32_t heldNotes(int32_t* out, int32_t maxN) const override;

    void render(float* out, int32_t frames) override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr double kSustain = 0.62;
    static constexpr int kVoices = 24;
    static constexpr int kMaxBalls = 6;
    static constexpr int kHeld = 16;
    static constexpr double kDivision[5] = {4.0, 2.0, 1.0, 0.5, 0.25}; // 1/1, 1/2, 1/4, 1/8, 1/16 (beats/cycle)

    struct Voice {
        bool active = false;
        double phA = 0.0, phB = 0.0, incA = 0.0, incB = 0.0;
        double phM = 0.0, incM = 0.0; // FM modulator
        double lpA = 0.0, lpB = 0.0;
        float env = 0.0f;
        int gate = 0;
        int stage = 0; // 0 attack, 1 decay, 2 sustain, 3 release
        float gL = 0.707f, gR = 0.707f;
        float lvl = 1.0f;
    };

    void set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }

    void sortHeld() { std::sort(held_, held_ + heldCount_); }
    void resetPhases() {
        for (int i = 0; i < kMaxBalls; ++i) {
            ballPhase_[i] = (double)i / kMaxBalls;
            lastIdx_[i] = -1;
        }
    }
    void publishHeld();

    // Cheap deterministic ±1 noise for Humanize (audio thread, alloc-free).
    double rndBi() {
        rng_ = rng_ * 1664525u + 1013904223u;
        return (double)(rng_ >> 8) / 8388608.0 - 1.0;
    }

    // Snap a pitch to the nearest tone of a root+scale (search outward from the pitch).
    static int snapToScale(int pitch, int root, int scale);

    // Position 0..1 from a ball's phase, one of four swing curves; all go 0→1→0 over the
    // cycle. 0 Linear (constant speed) · 1 Pendulum (sinusoidal, slow at the ends) ·
    // 2 Ease (eased in/out of the linear sweep) · 3 Bounce (settles with a small bounce).
    // Timing-critical (sets trigger times): stays std::-exact.
    static double position(double ph, int type);

    // Voice oscillator with selectable timbre (Wave) and upper-partial gain hi (Bright).
    // 0 Keys (soft sines) · 1 Glass (bright odd partials) · 2 Saw · 3 Square/hollow ·
    // 4 Bell (inharmonic partials). Phase may exceed [0,1) (FM) — sines are periodic;
    // saw/square take the fractional part. Partials use fast_math::sin (amplitude-domain
    // only — frequencies unchanged, tuning exact).
    static double osc(int tb, double ph, double hi);

    // Bar bookkeeping for a generated note: the step it lands on, its bar, who fired it.
    void noteFired(int ball, int pitch, int steps);

    // Once per block: the snapshot the editor and the MCP reader see.
    void publish(int count, double baseInc, double spread, int motion, int steps, double rateMult, double cycleSamp);

    void trigger(int pitch, int gateLen, float pan, float lvl, double detJit);
    Voice* findFreeVoice();

    std::atomic<float> pn_[kNumParams];
    double sampleRate_ = 44100.0, spb_ = 22050.0; // spb default ≈ 120 BPM @ 44.1k
    Voice voices_[kVoices];
    int held_[kHeld] = {};
    int heldCount_ = 0;
    int physCount_ = 0;
    std::atomic<int32_t> heldAtom_[kHeld];
    std::atomic<int32_t> heldN_{0};
    double ballPhase_[kMaxBalls] = {};
    int lastIdx_[kMaxBalls] = {};
    double gridAcc_ = 0.0;
    double detUp_ = 1.0, detDn_ = 1.0;
    uint32_t rng_ = 0x1234567u;

    // Musical clock (follows the transport while it rolls, free-runs when stopped) and
    // the telemetry state behind scopeRead.
    double beat_ = 0.0, beatsPerBar_ = 4.0;
    bool playing_ = false;
    int stepPitch_[kMaxSteps] = {};
    int64_t stepBar_[kMaxSteps] = {};
    int64_t countBar_ = 0;
    int barNotes_ = 0;
    int lastPitch_ = -1, lastBall_ = -1;
    int ballPitch_[kMaxBalls] = {};
    double sinceNote_ = 1e9, ballSince_[kMaxBalls] = {};
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> tele_[kTele];

    // Auto-sleep hangover (flag only — the engine skips render while idle).
    static constexpr uint64_t kIdleSamples = 16384;
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
};

} // namespace nota
