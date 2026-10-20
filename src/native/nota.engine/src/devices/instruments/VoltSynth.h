// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Volt — the built-in virtual-analog synth (kind 6), a dual-path subtractive
// voice modelled on classic two-oscillator analogs. Signal
// flow per voice:
//
//   Osc1 ┐                         ┌─ Filter 1 ─→ Amp1 (pan/level) ─┐
//   Osc2 ┼─ route (F1↔F2) per src ─┤                                ├─→ out
//   Noise┘                         └─ Filter 2 ─→ Amp2 (pan/level) ─┘
//                                     (Fil1 can also feed Fil2 via "To F2")
//
// Two TPT state-variable filters (LP/HP/BP/Notch, switchable 12/24 dB slope), each
// with cutoff modulated by a shared filter envelope, keytracking and one of two
// global LFOs. Shared amp ADSR with velocity depth. Per-oscillator octave/semi/
// detune/start-phase, unison spread, portamento (glide), a dedicated vibrato, and
// mono/poly voice mode.
//
// On top of the fixed routings sits a generic MODULATION MATRIX (mockup 2a/2e):
// 7 sources (Amp Env, Filter Env, LFO 1, LFO 2, Velocity, Key, Mod Wheel) × 6
// destinations (Pitch, Osc2 Pitch, Cutoff, Reso, Level, Pan), each a bipolar amount.
// Eight MACROS add manual offsets into a wider list of twelve destinations — the six
// above plus the three source levels, Filter 2's cutoff and either LFO's rate. Two
// full-shape LFOs (sin/tri/sqr/S&H) with depth, per-note fade-in and optional tempo
// sync round it out, and two performance wheels — pitch bend with a selectable range
// (1..12 semitones) and a mod wheel that is both a matrix source and, when the patch
// asks for it, the hand on the vibrato.
//
// Appended in v6 so Volt covers what Nota Bass and Nota Synth did (specs/026): a Shape knob
// per oscillator that morphs the chosen wave toward the next one (sine → triangle → saw →
// square), pulse width for the square, a drive before the filters and one on the output,
// a unison stack of 2 / 4 / 7 copies per oscillator spread across the stereo field (the
// filters then run per channel), a Poly / Mono / Legato voice mode with a held-note stack,
// and Wheel → LFO, which lets the mod wheel open each filter's LFO onto its cutoff. All of
// them default to neutral, so a patch saved before renders exactly as it did.
//
// All parameters ride the Instrument plugin-param interface (normalized 0..1, stable
// ids) → automation / persist / clone for free. Allocation-free after
// construction. A per-sample loop keeps modulation smooth; filter coefficients update
// at control rate (every 16 samples). Name & DSP are Nota's own.
//
// Adds the isIdle()/hasTail()/wake() sleep contract (voices are the only
// tail). The LFO sine uses fast_math::sin and the
// macro-rate and control-rate cutoff use fast_math::exp2f; oscillator, vibrato,
// pitch, tuning, filter-coefficient and envelope paths stay std-exact (reverted
// where the null test cost more than -60 dB — see the .cpp). Param order unchanged.

#pragma once

#include "devices/Instrument.h"
#include "engine/NoteExpression.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>

namespace nota {

class VoltSynth final : public Instrument {
  public:
    // Modulation matrix / macro dimensions.
    static constexpr int kSources = 7; // Amp Env, Flt Env, LFO1, LFO2, Vel, Key, Mod Whl
    static constexpr int kDests = 6;   // Pitch, Osc2 Pitch, Cutoff, Reso, Level, Pan
    static constexpr int kMacros = 8;
    // Macros reach further than the matrix: the six voice-bus destinations above sit at
    // indices 0, 2, 4, 7, 9 and 11 of this twelve-entry list, exactly where a six-entry
    // normalized value rounds to — so a project saved against the old list still points
    // at the same destination. The six new ones act on the block snapshot instead.
    //   0 Pitch · 1 Osc 1 Level · 2 Osc 2 Pitch · 3 Osc 2 Level · 4 Cutoff · 5 Noise
    //   6 Filter 2 · 7 Reso · 8 LFO 1 Rate · 9 Level · 10 LFO 2 Rate · 11 Pan
    static constexpr int kMacroDests = 12;

    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    // Scalar block is 0..63; the matrix (64..105), macros (106..129) and the two
    // performance wheels (130..132) follow.
    enum Param {
        Osc1Wave = 0,
        Osc1Octave,
        Osc1Semi,
        Osc1Detune,
        Osc1Level,
        Osc1Route,
        Osc2Wave,
        Osc2Octave,
        Osc2Semi,
        Osc2Detune,
        Osc2Level,
        Osc2Route,
        NoiseLevel,
        NoiseColor,
        NoiseRoute,
        Fil1Type,
        Fil1Freq,
        Fil1Reso,
        Fil1EnvAmt,
        Fil1LfoAmt,
        Fil1KeyAmt,
        Fil1ToF2,
        Fil2Type,
        Fil2Freq,
        Fil2Reso,
        Fil2EnvAmt,
        Fil2LfoAmt,
        Fil2KeyAmt,
        FAttack,
        FDecay,
        FSustain,
        FRelease,
        Amp1Pan,
        Amp1Level,
        Amp2Pan,
        Amp2Level,
        Attack,
        Decay,
        Sustain,
        Release,
        Lfo1Rate,
        Lfo2Rate,
        Volume,
        VibRate,
        VibAmt,
        Unison,
        Glide,
        VelAmp,
        VelFilter, // ends at 48
        // ---- appended (v4, mockup 2a/2e rework) ----
        Osc1Phase,
        Osc2Phase, // 49,50
        Lfo1Depth,
        Lfo1Shape,
        Lfo1Sync,
        Lfo1Fade, // 51..54
        Lfo2Depth,
        Lfo2Shape,
        Lfo2Sync,
        Lfo2Fade, // 55..58
        Fil1Slope,
        Fil2Slope, // 59,60
        ModWheel,
        MonoMode,
        OutPan,                                     // 61,62,63
        MatrixBase,                                 // 64 (42 entries)
        MacroBase = MatrixBase + kSources * kDests, // 106 (24 entries)
        // ---- appended (v5, almanac rework): the two performance wheels ----
        ExtraBase = MacroBase + kMacros * 3, // 130
        Bend = ExtraBase,
        BendRange,
        VibWheel, // 130,131,132
        // ---- appended (v6, specs/026: what Nota Bass and Nota Synth had) ----
        Osc1Shape,
        Osc2Shape,
        Osc1Pw,
        Osc2Pw, // 133..136
        FilDrive,
        Drive,
        UniVoices,
        UniSpread,
        Legato,
        WheelLfo,  // 137..142
        kNumParams // 143
    };
    static constexpr int kV6Base = Osc1Shape;

    static int matrixIdx(int s, int d) { return MatrixBase + s * kDests + d; }
    static int macroIdx(int m, int field) { return MacroBase + m * 3 + field; } // field: 0 val, 1 dest, 2 amt

    VoltSynth();

    int32_t kind() const override { return 6; }
    const char* displayName() const override { return "Nota Volt"; }

    // The mod wheel (CC1) moves the Mod Wheel control, as a learned knob would (specs/020).
    void controller(int32_t ctl, float value) override;
    void setSampleRate(double sr) override {
        sampleRate_ = sr > 0 ? sr : 44100.0;
        exprCoef_ = exprSmoothCoef(sampleRate_);
    }

    // MPE: bend → pitch (a keyboard's wheel uses the patch's Bend Range), pressure → level +
    // both cutoffs, slide → both cutoffs (±2 octaves).
    bool supportsMpe() const override { return true; }
    void noteExpression(int32_t pitch, int32_t dim, float value) override;
    void setTransport(double /*beatStart*/, double samplesPerBeat, bool /*playing*/) override {
        if (samplesPerBeat > 0.0) samplesPerBeat_ = samplesPerBeat;
    }
    // Active (sounding) voice count, for the editor's voice meter. Message thread read
    // of an audio-thread-updated snapshot; approximate is fine.
    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

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
        heldCount_ = 0;
        gexpr_.reset();
        for (auto& v : voices_) v.active = false;
        wake();
    }

    // Sleep contract: voices are the only tail (amp/filter release, no delay
    // line). Idle only when no voice is sounding — then the voice loop writes
    // nothing and the output stage maps exact silence to silence.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    void render(float* out, int32_t frames) override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr float kSqrt2 = 1.41421356f; // restore unity for centred out-pan

    static constexpr int kMaxStack = 7; // unison voices: 1 / 2 / 4 / 7
    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct Voice {
        bool active = false;
        int32_t pitch = 0;
        double freqTarget = 0.0, freqCur = 0.0, ktOct = 0.0;
        double ph1 = 0.0, ph1u = 0.0, ph2 = 0.0, ph2u = 0.0;
        float vel = 0.0f, aEnv = 0.0f, fEnv = 0.0f, fade1 = 1.0f, fade2 = 1.0f;
        Stage aStage = Stage::Attack, fStage = Stage::Attack;
        double s1[2] = {0, 0}, s2[2] = {0, 0};   // SVF integrator state (ic1, ic2)
        double s1b[2] = {0, 0}, s2b[2] = {0, 0}; // 2nd stage for 24 dB slope
        double s1R[2] = {0, 0}, s2R[2] = {0, 0}; // right-channel filters, for a spread stack
        double s1bR[2] = {0, 0}, s2bR[2] = {0, 0};
        double st1[kMaxStack] = {}, st2[kMaxStack] = {}; // unison stack phases
        double f1a1 = 0, f1a2 = 0, f1a3 = 0, f2a1 = 0, f2a2 = 0, f2a3 = 0;
        double k1 = 1.0, k2 = 1.0;
        int32_t modCount = 0;
        VoiceExpr ex;          // MPE bend / pressure / slide
        float exBendAt = 0.0f; // bend the multiplier was built for
        double exBendMul = 1.0;
    };

    double wheelRange() const { return std::round(1.0 + get(BendRange) * 11.0); } // semitones
    void resetExpr(Voice& v);

    void set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    double bip(Param p) const { return (get(p) - 0.5f) * 2.0; } // 0..1 → -1..+1
    float bipF(Param p) const { return (get(p) - 0.5f) * 2.0f; }
    int waveOf(Param p) const { return std::clamp((int)std::lround(get(p) * 3.0f), 0, 3); }
    int filtTypeOf(Param p) const { return std::clamp((int)std::lround(get(p) * 3.0f), 0, 3); }
    int shapeOf(Param p) const { return std::clamp((int)std::lround(get(p) * 3.0f), 0, 3); }
    int uniCount() const {
        static constexpr int n[4] = {1, 2, 4, 7};
        return n[std::clamp((int)std::lround(get(UniVoices) * 3.0f), 0, 3)];
    }
    // Pulse width 5..95 %; the centre is exactly 50 % so a plain square renders as before.
    double pwOf(Param p) const {
        const float v = get(p);
        return v == 0.5f ? 0.5 : 0.05 + 0.9 * v;
    }
    static int macroDestOf(float v) { return std::clamp((int)std::lround(v * (kMacroDests - 1)), 0, kMacroDests - 1); }
    static bool amActive(const int* d, int n, int dest) {
        for (int k = 0; k < n; ++k)
            if (d[k] == dest) return true;
        return false;
    }
    static double resoToK(double reso) { return std::clamp(2.0 - 1.9 * std::clamp(reso, 0.0, 1.0), 0.05, 2.0); }

    void startVoice(Voice& v, int32_t pitch, float velocity);
    // 0 = free running (leave previous phase); >0 = retrigger to that start phase.
    double phaseStart(Param p) const {
        float ph = get(p);
        return ph > 0.001f ? ph : 0.0;
    }
    static double wrap(double x) { return x >= 1.0 ? x - 1.0 : x; }

    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel);

    void setSvf(double& a1, double& a2, double& a3, double fc, double k) const;
    // TPT SVF (Cytomic). type: 0 LP, 1 HP, 2 BP, 3 Notch. state[0]=ic1, state[1]=ic2.
    static double svf(double* st, double a1, double a2, double a3, double k, double in, int type);

    // LFO shape: 0 sin, 1 tri, 2 sqr, 3 sample & hold. Returns -1..+1.
    static double lfoValue(int shape, double ph, float shHold);
    // Free-run increment, or tempo-synced division when Sync param > 0.
    double lfoInc(Param rate, Param sync) const;
    float fadeInc(Param p) const;

    // Wave chip (0 saw, 1 square, 2 tri, 3 sine) with Shape blending it toward the next wave
    // in sine → triangle → saw → square order; Shape 0 is the chip itself.
    static double osc(int wave, double shape, double pw, double ph, double dt);
    static double oscBlep(int wave, double ph, double dt, double pw);
    static double polyBlep(double t, double dt);
    float noise();

    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(Param p, double lo, double hi) const {
        return static_cast<float>(1.0 / (expMap(get(p), lo, hi) * sampleRate_));
    }
    // Octave (±3) + semi (±12) + detune (±50 cents) → frequency multiplier.
    double pitchMult(Param oct, Param semi, Param det) const;

    void pushHeld(int32_t pitch);
    bool popHeld(int32_t pitch);
    Voice* findFreeVoice();
    int countActive() const {
        int n = 0;
        for (const auto& v : voices_)
            if (v.active) ++n;
        return n;
    }

    static constexpr int kVoices = 16;
    Voice voices_[kVoices];
    double sampleRate_ = 44100.0;
    float exprCoef_ = exprSmoothCoef(44100.0);
    GlobalExpr gexpr_;            // pitch wheel / channel pressure (pitch −1)
    double samplesPerBeat_ = 0.0; // from setTransport, for LFO sync
    double lfoPhase1_ = 0.0, lfoPhase2_ = 0.0, vibPhase_ = 0.0;
    float lfoSh1_ = 0.0f, lfoSh2_ = 0.0f; // sample & hold values
    double lastFreq_ = 0.0;               // portamento reference
    int32_t held_[16] = {};               // keys down, oldest first (mono falls back through them)
    int heldCount_ = 0;
    float nlp_ = 0.0f; // noise color one-pole state
    uint32_t rng_ = 0x9E3779B9u;
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> pn_[kNumParams];
    bool idle_ = false; // sleep flag: set when no voice sounds
};

} // namespace nota
