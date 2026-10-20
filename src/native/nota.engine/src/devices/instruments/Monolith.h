// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Monolith — the built-in monophonic subtractive synth (kind 13), a musically
// faithful emulation of the Minimoog Model D architecture (three oscillators → mixer
// → Moog transistor-ladder low-pass → two contour generators). It is Nota's own DSP,
// not a schematic clone. Signal flow (single mono voice, optional unison stack):
//
//   Osc1 ┐
//   Osc2 ┼─→ Mixer ─(+ noise / ext / feedback overload)─→ Ladder LP (24 dB/oct) ─→ VCA ─→ out
//   Osc3 ┘     ▲                                              ▲ Filter Contour        ▲ Loudness Contour
//   noise──────┘        Osc3 / noise → Osc Mod (via Mod Wheel)┘  + keytrack + Filter Mod
//
// Model-D authenticity kept here: six waveforms per oscillator (triangle, shark-tooth,
// saw, square, wide/narrow pulse; Osc3 swaps shark-tooth for reverse saw), foot ranges
// (LO / 32'..2'), ±7-semitone detune on Osc2/3, Osc3 keyboard-control defeat (free LFO),
// oscillator modulation and filter modulation driven by the mod wheel, a self-oscillating
// ladder with keyboard tracking (1/3 · 2/3), a single Decay (release) switch shared by both
// contours, glide, note-priority mono logic (low / high / last), single/multi trigger
// (Legato), unison with detune, mixer feedback/overload, and gentle analog drift.
//
// All parameters ride the Instrument plugin-param interface (normalized 0..1, stable ids)
// → automation / persist / clone for free. Allocation-free after construction.
// A per-sample loop keeps modulation smooth; the ladder oversamples ×2 internally and the
// filter coefficients update at control rate.
//
// Adds the isIdle()/hasTail()/wake() sleep contract (the mono voice's contours are the only tail). DSP is otherwise
// verbatim — bit-identical null vs the flat header. Param order unchanged.

#pragma once

#include "devices/Instrument.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota {

class Monolith final : public Instrument {
  public:
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        // --- controllers / global (0..20) ---
        Tune = 0,
        Glide,
        GlideOn,
        ModMix,
        OscModOn,
        FilterModOn,
        Osc3Kbd,
        Drift,
        Legato,
        DecayOn,
        A440,
        BendRange,
        Priority,
        Unison,
        UniDetune,
        Volume,
        BassComp,
        VelVca,
        VelVcf,
        Bend,
        ModWheel,
        // --- oscillators (21..28) ---
        O1Range,
        O1Wave,
        O2Range,
        O2Tune,
        O2Wave,
        O3Range,
        O3Tune,
        O3Wave,
        // --- mixer (29..40) ---
        Mix1Lvl,
        Mix1On,
        Mix2Lvl,
        Mix2On,
        Mix3Lvl,
        Mix3On,
        NoiseLvl,
        NoiseOn,
        NoiseType,
        ExtLvl,
        ExtOn,
        Feedback,
        // --- filter (41..45) ---
        Cutoff,
        Emph,
        Contour,
        Kbd1,
        Kbd2,
        // --- filter contour (46..48) ---
        FAttack,
        FDecay,
        FSustain,
        // --- loudness contour (49..51) ---
        AAttack,
        ADecay,
        ASustain,
        kNumParams
    };

    Monolith();

    int32_t kind() const override { return 13; }
    const char* displayName() const override { return "Nota Monolith"; }

    // The mod wheel (CC1) moves the Mod Wheel control, as a learned knob would (specs/020).
    void controller(int32_t ctl, float value) override;
    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }
    int32_t activeVoiceCount() const override { return active_.load(std::memory_order_relaxed); }
    int32_t heldNotes(int32_t* out, int32_t maxN) const override;

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override;
    std::string pluginParamName(int32_t i) const override;
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        wake();
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override;

    // ---- project state (kNumParams normalized floats, little-endian) -------
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    // ---- note events (monophonic with a held-note priority stack) ---------
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override {
        heldN_ = 0;
        sounding_ = -1;
        aStage_ = Stage::Off;
        fStage_ = Stage::Off;
        aEnv_ = fEnv_ = 0.0f;
    }

    // Sleep contract: the single mono voice is the only tail (Decay/Release;
    // A-440 is editor state and never sounds on its own). Idle only when the
    // amp contour is Off with no held notes — then amp is exactly zero, so the
    // render loop writes exact silence despite always running.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    void render(float* out, int32_t frames) override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr int kUni = 7;

    enum class Stage { Attack, Decay, Sustain, Release, Off };

    void set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(Param p, double lo, double hi) const { return (float)(1.0 / (expMap(get(p), lo, hi) * sampleRate_)); }
    float rateOf2(double sec) const { return (float)(1.0 / (sec * sampleRate_)); }
    double noteHz(int pitch) const { return 440.0 * std::pow(2.0, (pitch - 69) / 12.0); }
    int wave6(Param p) const { return std::clamp((int)std::lround(get(p) * 5.0f), 0, 5); }
    // Range knob: LO / 32' / 16' / 8' / 4' / 2'  →  octave offset vs 8' reference.
    double rangeMult(Param p) const;

    void retrigger() {
        aStage_ = Stage::Attack;
        fStage_ = Stage::Attack;
    }

    void pushHeld(int pitch);
    void removeHeld(int pitch);
    int selectPitch() const;

    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel);

    // ---- oscillators ------------------------------------------------------
    // Osc1/Osc2 waveforms: 0 tri, 1 shark-tooth, 2 saw, 3 square, 4 wide pulse, 5 narrow pulse.
    static double osc12(int w, double ph, double dt);
    // Osc3 waveforms: 0 tri, 1 reverse saw, 2 saw, 3 square, 4 wide pulse, 5 narrow pulse.
    static double osc3(int w, double ph, double dt);
    static double tri(double ph) { return 4.0 * std::fabs(ph - 0.5) - 1.0; }
    static double sawB(double ph, double dt) { return (2.0 * ph - 1.0) - polyBlep(ph, dt); }
    static double pulseB(double ph, double dt, double duty);
    static double polyBlep(double t, double dt);
    float noise();

    // ---- Moog transistor-ladder low-pass (tanh state-difference model, ×2 oversampled) ----
    // Four cascaded non-linear one-poles with a resonance feedback tap; self-oscillates near
    // the top of Emphasis. Poles are tuned a touch above the cutoff knob so the passband stays
    // open (a straight ladder reads dark) while the resonant peak still tracks the knob.
    static double ftanh(double x) { return std::tanh(std::clamp(x, -4.0, 4.0)); }
    void ladderSet(double fc, double reso);
    double ladder(double x, bool bassComp);

    // ---- state ------------------------------------------------------------
    static constexpr int kMaxUni = kUni;
    double sampleRate_ = 44100.0;

    int held_[128] = {0};
    int heldN_ = 0;
    int sounding_ = -1;
    float vel_ = 0.0f;
    double freqTarget_ = 0.0, freqCur_ = 0.0, lastFreq_ = 0.0;

    float aEnv_ = 0.0f, fEnv_ = 0.0f;
    Stage aStage_ = Stage::Off, fStage_ = Stage::Off;

    double ph1_[kUni] = {0}, ph2_[kUni] = {0}, ph3_[1] = {0};
    float pinkState_ = 0.0f;
    double drift_[3] = {0, 0, 0}, driftTarget_[3] = {0, 0, 0}, driftPhase_ = 0.0;

    int modCount_ = 0;
    double cutHz_ = 1000.0;
    double ladderF_ = 0.1, ladderFb_ = 0.0, ladderReso_ = 0.0;
    double l1_ = 0, l2_ = 0, l3_ = 0, l4_ = 0;
    double fbState_ = 0.0;

    uint32_t rng_ = 0x1234567u;
    std::atomic<int32_t> active_{0};
    std::atomic<float> pn_[kNumParams];
    bool idle_ = false; // sleep flag: amp contour Off, nothing held
};

} // namespace nota
