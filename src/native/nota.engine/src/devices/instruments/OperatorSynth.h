// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Operator — the built-in 4-operator FM synth (kind 9), in the spirit of classic 4-operator FM synths
// Operator: four operators (A B C D), each a phase-modulated oscillator with its own
// coarse/fine frequency ratio, output level, waveform and ADSR envelope. Eight feed-
// forward algorithms route who modulates whom (chains, stacks, parallel/additive), with
// self-feedback on operator A. The summed carriers pass a subtractive TPT filter
// (LP/HP/BP, with optional keyboard tracking) before the master volume — FM timbre +
// classic filter sculpting in one. Two performance wheels ride on top: pitch bend (range
// 1..12 semitones) and a mod wheel on the modulation index.
//
//   A ─┐            (per algorithm) modulators bend the phase of their target;
//   B ─┼─▶ [algo] ─▶ carriers ─▶ filter ─▶ volume ─▶ out
//   C ─┤            op A can feed back into itself
//   D ─┘
//
// All params ride the Instrument plugin-param interface (normalized 0..1, stable ids)
// → automation / persist / clone for free. Allocation-free after
// construction; per-sample voice loop keeps FM + envelopes smooth. Name & DSP are Nota's.
//
// DSP notes: the FM operator sine stays std::-exact — it DEFINES the spectrum through
// the FM stack, so fast_math::sin is NOT used in the audio path. The spectrum-analysis
// ring is block-decimated (every 4th sample): it feeds only the message-thread scope,
// so the decimation is transparent to the PCM output. Sleep: release tails ring past
// note-off (hasTail), auto-sleep after a silent hangover (engine skips render while
// idle; noteOn/noteOff/setParam wake).

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
#include <vector>

namespace nota {

class OperatorSynth final : public Instrument {
  public:
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    // Per-op block = Coarse, Fine, Level, Wave, Attack, Decay, Sustain, Release.
    enum Param {
        Algo = 0,
        Feedback,
        ACoarse,
        AFine,
        ALevel,
        AWave,
        AA,
        AD,
        AS,
        AR,
        BCoarse,
        BFine,
        BLevel,
        BWave,
        BA,
        BD,
        BS,
        BR,
        CCoarse,
        CFine,
        CLevel,
        CWave,
        CA,
        CD,
        CS,
        CR,
        DCoarse,
        DFine,
        DLevel,
        DWave,
        DA,
        DD,
        DS,
        DR,
        FilterType,
        FilterFreq,
        FilterReso,
        Volume,
        // --- appended for the mockup-3g rework (old projects default these to neutral) ---
        FmDepth,  // global modulation-index macro (0.5 = ×1, matches the pre-macro fixed depth)
        Glide,    // portamento time (0 = off)
        VelToFm,  // velocity → FM depth amount (0 = off, classic FM dynamics)
        KeyLevel, // keyboard tracking of modulator level (0 = off; higher notes → less FM)
        Mono,     // voice mode: <0.5 poly, ≥0.5 monophonic (legato with glide)
        // --- appended for the almanac rework (old projects default these to neutral) ---
        Bend,       // pitch-bend wheel, 0.5 = centre (the card's left Wheels rail)
        BendRange,  // bend range in semitones: 1 + v*11, rounded (default ±2 st)
        ModWheel,   // mod wheel 0..1 — the classic FM hand control: more modulation index
        FilKeyTrk,  // filter cutoff keyboard tracking: 0 / ½ / 1 octave per octave
        VelToLevel, // velocity → output level (1 = the classic behaviour, 0 = flat)
        kNumParams
    };
    static constexpr int kNumAlgos = 11;
    // Field offsets within an op block (op base = ACoarse + op*8).
    enum { F_Coarse = 0, F_Fine, F_Level, F_Wave, F_A, F_D, F_S, F_R };

    OperatorSynth();

    int32_t kind() const override { return 9; }
    const char* displayName() const override { return "Nota Operator"; }

    // The mod wheel (CC1) moves the Mod Wheel control, as a learned knob would (specs/020).
    void controller(int32_t ctl, float value) override;
    void setSampleRate(double sr) override;

    // MPE: bend → pitch (a keyboard's wheel uses the patch's Bend Range), pressure → level +
    // modulation index (up to ×2), slide → modulation index (×½ … ×2) — the FM timbre.
    bool supportsMpe() const override { return true; }
    void noteExpression(int32_t pitch, int32_t dim, float value) override;

    // Sleep contract: release envelopes ring past note-off (the engine-visible tail).
    // Settles after the hangover; noteOn/noteOff/setParam wake.
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

    // ---- project state ----
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    // ---- note events ----
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    void render(float* out, int32_t frames) override;

    // Harmonic spectrum of the current patch at the played note (message-thread read):
    // Goertzel magnitude at k·f0 for k = 1..N over the analysis ring, normalized 0..1.
    int32_t scopeRead(float* outv, int32_t maxN) const override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr int kVoices = 12;
    static constexpr double kFmIndex = 6.0; // modulator output → phase-mod depth (cycles)

    // Feed-forward FM algorithms: tgt[op] = op index it modulates (must be > op) or 4 = output.
    // 11 topologies (mockup 3g); the managed card mirrors this table + its descriptions.
    static constexpr int8_t kAlgo[kNumAlgos][4] = {
        {1, 2, 3, 4}, //  0: A→B→C→D→out (single chain)
        {2, 2, 3, 4}, //  1: A·B → C → D (dual stack)
        {1, 3, 3, 4}, //  2: A→B→D, C→D (two mods, one carrier)
        {1, 3, 4, 4}, //  3: A→B→D; C,D carriers (bell)
        {3, 3, 3, 4}, //  4: A·B·C → D (triple modulator)
        {1, 4, 3, 4}, //  5: A→B, C→D (two 2-op voices; B,D carriers)
        {3, 4, 4, 4}, //  6: A→D; B,C,D carriers (additive + mod)
        {1, 2, 4, 4}, //  7: A→B→C; C,D carriers (bright)
        {4, 3, 4, 4}, //  8: B→D; A,C,D carriers
        {2, 4, 4, 4}, //  9: A→C; B,C,D carriers
        {4, 4, 4, 4}, // 10: all parallel (additive / organ)
    };
    static constexpr double kRatio[16] = {0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16};

    enum class Stage { Attack, Decay, Sustain, Release, Off };
    struct Voice {
        bool active = false;
        int32_t pitch = 0;
        float vel = 0.0f;
        double freq = 0.0;    // target frequency
        double curFreq = 0.0; // glide-ramped frequency actually used
        double phase[4] = {0, 0, 0, 0};
        float env[4] = {0, 0, 0, 0};
        Stage stage[4] = {Stage::Off, Stage::Off, Stage::Off, Stage::Off};
        double fbLast = 0.0;
        double s1 = 0.0, s2 = 0.0; // filter state
        VoiceExpr ex;              // MPE bend / pressure / slide
        float exBendAt = 0.0f;
        double exBendMul = 1.0;
    };
    Voice voices_[kVoices];
    float wheelRange() const { return static_cast<float>(std::round(1.0 + get(BendRange) * 11.0)); } // semitones

    void set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rateOf(Param p, double lo, double hi) const {
        return static_cast<float>(1.0 / (expMap(get(p), lo, hi) * sampleRate_));
    }

    static double waveform(int w, double ph);
    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel);
    Voice* findFreeVoice();

    double sampleRate_ = 44100.0;
    float exprCoef_ = exprSmoothCoef(44100.0);
    GlobalExpr gexpr_;      // pitch wheel / channel pressure (pitch −1)
    double lastFreq_ = 0.0; // last note-on target, for glide
    std::atomic<float> pn_[kNumParams];

    // Spectrum analysis (lock-free best-effort, viz only).
    static constexpr int kRing = 2048;   // power of two
    static constexpr int kSpecBins = 32; // harmonic bars reported
    float ring_[kRing] = {};
    std::atomic<uint32_t> ringW_{0};
    std::atomic<float> specF0_{0.0f};

    // Auto-sleep hangover (flag only — the engine skips render while idle).
    static constexpr uint64_t kIdleSamples = 16384;
    uint64_t quietSamples_ = 0;
    bool idle_ = false;
};

} // namespace nota
