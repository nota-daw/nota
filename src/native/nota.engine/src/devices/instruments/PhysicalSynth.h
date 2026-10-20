// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Physical — the built-in physical-modeling synth (kind 2), a modal-synthesis
// voice in the spirit of classic modal-resonator instruments. Each voice excites one or two modal
// RESONATOR banks (a set of tuned, damped 2-pole resonators whose partial series is
// set by a material Type — Beam, Marimba, String, Membrane, Plate, Pipe) with a
// MALLET (a stiffness-shaped strike) and/or a filtered NOISE burst (its own ADSR).
//
//   Mallet ┐                     ┌─ Resonator 1 ─┐   (1>2 serial: 1 → 2)
//          ┼─ exciter ───────────┤               ├─→ out
//   Noise  ┘                     └─ Resonator 2 ─┘   (1+2 parallel: 1 and 2 both struck)
//
// Res Mix balances what comes out: resonator 1 against resonator 2 — in 1>2, resonator 1's
// own sound against the body it rings (+12 dB make-up, as only its partials that meet
// resonator 2's pass through).
//
// Mode coefficients (frequency / decay / amplitude, per Type + Decay/Material/Bright/
// Inharm/Ratio/Hit/Tune) are computed at note-on per voice; the exciter and global
// params (Volume/Pan/NoteOff/Tune/Res Mix) apply live. Poly plays 8 voices; Mono chokes
// the sounding note (a ~4 ms fade) whenever a new one strikes. scopeRead() exposes the
// live voice count, the last struck pitch, the output peak and both banks' mode tables
// (ratio / amplitude / ring time) exactly as buildBank freezes them — the editor's
// partial spectrum and the MCP read_physical tool draw on it. All params ride the Instrument plugin-
// param interface (normalized 0..1, stable ids) → automation / persist / clone for free.
// Allocation-free after construction. Name & DSP are Nota's own.
//
// Pitch/position-critical math (bank coefficients, retune, resonator loops) stays
// std::-exact — no FastMath migration. Silent voices auto-sleep via isIdle() after a
// hangover; ringing tails fully decay before sleep.

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

class PhysicalSynth final : public Instrument {
  public:
    static constexpr int kVoices = 8;
    static constexpr int kModes = 16;

    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        MalletVol = 0,
        MalletStiff,
        MalletNoise,
        MalletColor,
        NoiseVol,
        NoiseEnvAmt,
        NoiseType,
        NoiseFreq,
        NoiseRes,
        NoiseA,
        NoiseD,
        NoiseS,
        NoiseR,
        R1Type,
        R1Decay,
        R1Material,
        R1Bright,
        R1Inharm,
        R1Ratio,
        R1Hit,
        R1Tune,
        R2On,
        R2Type,
        R2Decay,
        R2Material,
        R2Bright,
        R2Inharm,
        R2Ratio,
        R2Hit,
        R2Tune,
        Structure,
        Tune,
        Fine,
        NoteOff,
        Pan,
        Volume,
        Mono,
        ResMix, // v2 (redesign): voice mode, Res 1 / Res 2 balance in 1+2
        kNumParams
    };

    PhysicalSynth();

    int32_t kind() const override { return 2; }
    const char* displayName() const override { return "Nota Physical"; }

    void setSampleRate(double sr) override {
        sampleRate_ = sr > 0 ? sr : 44100.0;
        exprCoef_ = exprSmoothCoef(sampleRate_);
        wake();
    }

    // MPE: bend retunes the ringing body, pressure bows it (a continuous noise exciter, so a
    // held, pressed note sustains like a bowed bar), slide sets how long it rings (up = ring
    // ×2, down = damped ×½, like a hand on the bar). A keyboard's wheel bends ±2 semitones.
    bool supportsMpe() const override { return true; }
    void noteExpression(int32_t pitch, int32_t dim, float value) override;

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
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i)
            if (pluginParamId(i) == id) return i;
        return -1;
    }

    // ---- project state (kNumParams normalized floats, little-endian) -------
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    int32_t activeVoiceCount() const override { return activeVoices_.load(std::memory_order_relaxed); }

    // Telemetry for the editor + MCP (message thread). Layout:
    //   [0] sounding voices · [1] mono (0/1) · [2] last struck fundamental in Hz (0 = none
    //   yet) · [3] output peak (linear, held ~300 ms) · then per bank (1, 2) three rows of
    //   kModes: the partial's frequency ratio to the note (bank Tune included), its struck
    //   amplitude, and its ring time in seconds (e-folding) — what buildBank freezes.
    static constexpr int kScopeHead = 4;
    static constexpr int kScopeLen = kScopeHead + 2 * 3 * kModes;
    int32_t scopeRead(float* out, int32_t maxN) const override;

    // ---- note events ------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;

    void render(float* out, int32_t frames) override;

    // Sleep contract: modal tails ring past note-off, so hasTail is true; isIdle engages
    // only when no voice is sounding and the rendered block is silent, after a hangover.
    // noteOn/noteOff/pluginParamSet/setState wake.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override {
        idle_ = false;
        quietSamples_ = 0;
    }

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr float kSerialMakeup = 4.0f;    // +12 dB on bank 2 in 1>2
    static constexpr uint64_t kIdleSamples = 16384; // hangover before sleep

    enum class Stage { Attack, Decay, Sustain, Release, Off };

    // One modal resonator bank: kModes tuned 2-pole resonators (frozen coefficients).
    // bs = the input gain when the bank is fed by resonator 1 (1→2): a struck resonator's
    // gain is ~amp for an impulse, but a sustained sine on its resonance rings it up by
    // ~τ·sr — so in series each mode is normalised to a peak gain of ~amp (a resonant
    // band-pass), and bank 2 colours bank 1 instead of exploding.
    struct Bank {
        double a1[kModes] = {}, a2[kModes] = {}, b0[kModes] = {}, bs[kModes] = {}; // 2R cos w, R², input gains
        double y1[kModes] = {}, y2[kModes] = {};
        double R[kModes] = {}, w[kModes] = {}, amp[kModes] = {}; // as built, for retune()
        float bowNorm = 0.0f;                                    // bow noise gain (see render)
    };

    struct Voice {
        bool active = false, released = false, choke = false;
        int32_t pitch = 0;
        float vel = 0.0f, peak = 0.0f, relEnv = 1.0f;
        // Mallet.
        bool mActive = false, mFirst = true;
        float mEnv = 0.0f, mEnvDec = 0.0f, mLp = 0.0f, mCut = 0.5f;
        // Noise burst.
        Stage nStage = Stage::Off;
        float nEnv = 0.0f;
        double nIc1 = 0.0, nIc2 = 0.0;
        Bank r1, r2;
        VoiceExpr ex;                                      // MPE bend / pressure / slide
        float exBend = 0.0f, exSlide = 0.0f, bowLp = 0.0f; // what the banks were tuned for
        int32_t exAge = 0;
    };
    Voice voices_[kVoices];

    // Re-pitch (mul) and re-damp (R^ring) a bank's modes from their note-on values, keeping
    // the struck normalisation. Modes pushed past Nyquist fall silent.
    void retune(Bank& b, double mul, double ring) const;

    void set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    double bip(Param p) const { return (get(p) - 0.5f) * 2.0; }
    int typeOf(Param p) const { return std::clamp((int)std::lround(get(p) * 5.0f), 0, 5); }

    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rate(Param p, double lo, double hi) const {
        return static_cast<float>(1.0 / (expMap(get(p), lo, hi) * sampleRate_));
    }
    float noise() {
        rng_ ^= rng_ << 13;
        rng_ ^= rng_ >> 17;
        rng_ ^= rng_ << 5;
        return (rng_ & 0xFFFFFF) / 8388608.0f - 1.0f;
    }

    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel);

    // TPT state-variable filter for the noise exciter. type: 0 LP, 1 BP, 2 HP.
    float svf(double& ic1, double& ic2, double fc, double k, double in, int type) const;

    // Run one sample through a modal bank (sum of the tuned resonators).
    static float bank(Bank& b, float x);

    static float bankSerial(Bank& b, float x);

    // Partial ratios for a material Type (mode 0 = the fundamental = 1.0).
    static double ratioOf(int type, int m);

    // One mode of a bank from Type + Decay/Material/Bright/Inharm/Ratio/Hit: its frequency
    // ratio to the fundamental (before the bank's Tune), struck amplitude and ring time.
    struct Mode {
        double ratio, amp, tau;
    };
    static Mode modeOf(
        int type, int m, float decay, float material, float bright, float inharm, float ratioP, float hit);

    // Freeze a bank's coefficients from a fundamental + Type/Decay/Material/Bright/Inharm/Ratio/Hit.
    void buildBank(Bank& b, double f0, int type, float decay, float material, float bright, float inharm, float ratioP,
        float hit) const;

    Voice* findFreeVoice();

    double sampleRate_ = 44100.0;
    static constexpr float kWheelRange = 2.0f; // a keyboard's pitch wheel, semitones
    static constexpr float kBowGain = 1.0f;
    float exprCoef_ = exprSmoothCoef(44100.0);
    GlobalExpr gexpr_; // pitch wheel / channel pressure (pitch −1)
    uint32_t rng_ = 0x2545F491u;
    std::atomic<float> pn_[kNumParams];
    std::atomic<int32_t> activeVoices_{0};
    std::atomic<float> lastF0_{0.0f}, outPeak_{0.0f};

    // Auto-sleep hangover.
    bool idle_ = false;
    uint64_t quietSamples_ = 0;
};

} // namespace nota
