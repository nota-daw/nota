// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Bass — the built-in bass synthesizer (kind 7), a mono-first subtractive voice
// tuned for low end, in the spirit of classic bass synths. Signal flow per voice:
//
//   Osc (morph sine→tri→saw→pulse, PWM) ┐
//   Sub (sine/tri/square, −1/−2 oct)    ┼─→ pre-drive → Filter (LP/HP/BP/Notch, 12/24)
//                                        ┘        │  ▲ filter env · key · LFO
//                                                 ▼
//                                          Amp (ADSR × vel) → output drive → out
//
// A single morphing oscillator (continuous shape blend with pulse-width for the square
// end), a dedicated sub oscillator, a TPT state-variable filter (optional 24 dB cascade)
// with its own envelope, key-tracking and LFO, plus pre-filter and output saturation for
// grit. Mono mode (note stack, last-note priority) with glide makes it a proper bass;
// every new note retriggers the envelopes unless Legato is on, in which case a note
// played over a held one slides into it without a new attack. Poly mode is available
// too. Two performance wheels ride on top — pitch bend with a selectable range (1..12
// semitones) and a mod wheel that opens the LFO onto the cutoff (a hand-played wobble)
// — plus an output pan. All parameters ride the Instrument plugin-param interface
// (normalized 0..1, stable ids) → automation / persist / clone for free.
//
// Note bookkeeping counts instances, not pitches: a note-off releases the oldest held
// instance of that pitch only. Two overlapping notes of the same pitch (a repeated note
// whose on lands a sample before the previous off) therefore no longer silence the new
// one.
//
// Allocation-free after construction; a per-sample loop keeps filter/LFO
// modulation smooth. Filter coefficients update at
// control rate (every 16 samples). Name & DSP are Nota's own.
//
// Adds the isIdle()/hasTail()/wake() sleep contract (voices are the only tail). Pre-drive makeup is hoisted per block,
// pre/output saturation uses fast_math::tanhPrecise, the morph/sub osc sines use
// fast_math::sin and the control-rate cutoff uses fast_math::exp2f; LFO, pitch,
// tuning, filter-coefficient and envelope paths stay std-exact (reverted where
// the null test cost more than -60 dB — see the .cpp). Param order unchanged.

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

class BassSynth final : public Instrument {
  public:
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        OscShape = 0,
        OscPW,
        OscOctave,
        OscSemi,
        OscLevel,
        SubWave,
        SubOctave,
        SubLevel,
        FilType,
        FilSlope,
        FilFreq,
        FilReso,
        FilDrive,
        FilEnvAmt,
        FilKeyAmt,
        FilLfoAmt,
        FAttack,
        FDecay,
        FSustain,
        FRelease,
        Attack,
        Decay,
        Sustain,
        Release,
        LfoRate,
        LfoWave,
        LfoPitch,
        Glide,
        Unison,
        Drive,
        Volume,
        VelAmp,
        VelFilter,
        Mono,
        // ---- appended (v2, almanac rework): wheels, output pan, legato ----
        Bend,
        BendRange,
        ModWheel,
        OutPan,
        Legato,
        kNumParams
    };

    BassSynth();

    int32_t kind() const override { return 7; }
    const char* displayName() const override { return "Nota Bass"; }

    // The mod wheel (CC1) moves the Mod Wheel control, as a learned knob would (specs/020).
    void controller(int32_t ctl, float value) override;
    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }

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

    // ---- note events ------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override {
        for (auto& v : voices_) v.active = false;
        monoCount_ = 0;
        wake();
    }

    // Sleep contract: voices are the only tail (amp/filter release, no delay
    // line). Idle only when no voice is sounding — then the voice loop adds
    // exact zeros and the output drive maps silence to silence.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    void render(float* out, int32_t frames) override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct Voice {
        bool active = false;
        int32_t pitch = 0;
        double freqTarget = 0.0, freqCur = 0.0, ktOct = 0.0;
        double phM = 0.0, phMu = 0.0, phS = 0.0; // main / unison / sub phase
        float vel = 0.0f, aEnv = 0.0f, fEnv = 0.0f;
        Stage aStage = Stage::Attack, fStage = Stage::Attack;
        double s1[2] = {0, 0}, s2[2] = {0, 0}; // SVF integrator state (two cascaded stages)
        double a1 = 0, a2 = 0, a3 = 0;
        int32_t modCount = 0;
        uint32_t seq = 0; // note-on order: a note-off releases the oldest
    };

    void set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    double bip(Param p) const { return (get(p) - 0.5f) * 2.0; } // 0..1 → -1..+1

    static double mtof(int32_t pitch) { return 440.0 * std::pow(2.0, (pitch - 69) / 12.0); }

    // New attack. A silent voice starts clean — phases at zero, envelopes and filter from
    // rest. A voice that is still sounding (a mono retrigger) keeps its phases, filter
    // state and envelope levels and attacks from where it is, so the retrigger never clicks.
    void startVoice(Voice& v);
    static void release(Voice& v);

    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel);

    void setSvf(double& a1, double& a2, double& a3, double fc, double k) const;
    // TPT SVF (Cytomic). type: 0 LP, 1 HP, 2 BP, 3 Notch. state[0]=ic1, state[1]=ic2.
    static double svf(double* st, double a1, double a2, double a3, double k, double in, int type);

    // Continuous morph: sine → triangle → saw → pulse (with PWM at the square end).
    static double morphOsc(double shape, double ph, double dt, double pw);
    // Sub oscillator: 0 sine, 1 square, 2 triangle (naïve — an octave-low signal).
    static double subOsc(int wave, double ph);
    static double polyBlep(double t, double dt);
    // LFO waveforms: 0 sine, 1 triangle, 2 saw, 3 square, 4 sample & hold (sh held per cycle).
    static double lfoValue(int wave, double ph, float sh);
    float noise();

    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(Param p, double lo, double hi) const {
        return static_cast<float>(1.0 / (expMap(get(p), lo, hi) * sampleRate_));
    }
    // Octave (±2) + semi (±12) → frequency multiplier.
    double pitchMult(Param oct, Param semi) const;

    Voice* findFreeVoice();

    static constexpr int kVoices = 16;
    static constexpr int kMonoStack = 32;
    Voice voices_[kVoices];
    int monoStack_[kMonoStack];
    int monoCount_ = 0;
    double sampleRate_ = 44100.0;
    double lfoPhase_ = 0.0;
    float lfoSH_ = 0.0f;
    double lastFreq_ = 0.0; // portamento reference
    uint32_t noteSeq_ = 0;
    uint32_t rng_ = 0x9E3779B9u;
    std::atomic<float> pn_[kNumParams];
    bool idle_ = false; // sleep flag: set when no voice sounds
};

} // namespace nota
