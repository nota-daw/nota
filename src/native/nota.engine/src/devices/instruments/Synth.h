// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Synth — the built-in polyphonic subtractive synth. One oscillator
// (saw/square+pulse-width/triangle/sine) with octave, detune and up to seven
// unison voices → linear ADSR amplitude envelope → per-voice TPT state-variable
// filter (Off/LP/HP/BP with an envelope amount) → pan + master gain. Poly 16,
// Mono or Legato, with glide and velocity→volume tracking. Every parameter is
// exposed through the Instrument plugin-parameter interface (normalized 0..1 +
// stable string ids), so they automate and record exactly like a hosted plugin's.
//
// The first eight parameters keep their original index and meaning, so projects
// saved before the oscillator/voice sections existed load unchanged (setState
// reads whatever floats are present and leaves the rest at their defaults).
// The last one, View, is editor state (the card's S / L size) — it persists and
// clones with the patch but the managed layer keeps it out of presets/automation.
//
// Adds the isIdle()/hasTail()/wake() sleep contract (the ADSR voices are the only tail). DSP is otherwise verbatim —
// bit-identical null vs the flat header.

#pragma once

#include "devices/Instrument.h"
#include "engine/NoteExpression.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota {

class Synth final : public Instrument {
  public:
    // Parameter layout (normalized 0..1). The order is the persisted state layout —
    // append only.
    enum Param {
        Wave = 0,
        Attack,
        Decay,
        Sustain,
        Release,
        Cutoff,
        Resonance,
        Gain,
        FilType,
        FilEnv,
        PulseWidth,
        Detune,
        Octave,
        Unison,
        Spread,
        Glide,
        VelAmp,
        Pan,
        VoiceMode,
        View, // editor state, not sound: the card size (0 = L, 1 = S)
        kNumParams
    };

    Synth();

    int32_t kind() const override { return 0; } // built-in Synth (M7-6) — project compat
    const char* displayName() const override { return "Nota Synth"; }

    void setSampleRate(double sr) override {
        sampleRate_ = sr > 0 ? sr : 44100.0;
        exprCoef_ = exprSmoothCoef(sampleRate_);
    }

    // MPE: bend → pitch, pressure → level + brightness, slide → cutoff (±2 octaves).
    bool supportsMpe() const override { return true; }
    void noteExpression(int32_t pitch, int32_t dim, float value) override;

    // ---- parameters (automatable via the plugin-param interface) ----------
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

    // ---- project state (the normalized floats, little-endian) -------------
    // Reads however many floats the blob carries, so a project written by an older
    // build (eight params) loads and the parameters added since keep their defaults.
    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;
    std::shared_ptr<Instrument> clone() const override;

    void noteOn(int32_t pitch, float velocity) override;

    void noteOff(int32_t pitch) override;

    void allNotesOff() override {
        heldCount_ = 0;
        gexpr_.reset();
        for (auto& v : voices_) v.active = false;
        wake();
    }

    // Sleep contract: voices are the only tail (ADSR release, no delay line).
    // Idle only when no voice is sounding — then the render loop provably
    // writes nothing, so the block is silent by construction.
    bool hasTail() const override { return true; }
    bool isIdle() const override { return idle_; }
    void wake() override;

    void render(float* out, int32_t frames) override;

  private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kSqrt2 = 1.41421356237309504880; // equal-power pan back to unity at centre
    static constexpr int kMaxUnison = 7;
    static constexpr int kHeldStack = 16;
    static constexpr float kWheelRange = 2.0f; // a keyboard's pitch wheel, semitones

    // Naive (aliased) shapes — deliberately modest, as the rest of the synth.
    static double oscSample(int wave, double phase, double pw);

    struct Svf {
        double ic1 = 0.0, ic2 = 0.0;
    };

    // TPT state-variable filter (Zavalishin): low = v2, band = v1, high = in − k·v1 − v2.
    static double svf(Svf& s, double in, double a1, double a2, double a3, double k, int type);

    // Exponential (perceptual) map from a normalized value to [lo, hi].
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    // Denormalised time (seconds) for an ADSR param, clamped to [lo, hi].
    double secOf(Param p, double lo, double hi) const { return expMap(pn_[p].load(std::memory_order_relaxed), lo, hi); }

    static double freqOf(int32_t pitch) { return 440.0 * std::pow(2.0, (pitch - 69) / 12.0); }

    int filterType() const {
        return static_cast<int>(std::lround(pn_[FilType].load(std::memory_order_relaxed) * 3.0f));
    }
    int voiceMode() const {
        return static_cast<int>(std::lround(pn_[VoiceMode].load(std::memory_order_relaxed) * 2.0f));
    }
    int octaveShift() const {
        return static_cast<int>(std::lround((pn_[Octave].load(std::memory_order_relaxed) - 0.5f) * 4.0f));
    }
    int unisonCount() const;
    // Glide time: squared so the low end of the slider is finely graded. 0 → off.
    double glideSeconds() const;
    double glideCoef() const;

    enum class Stage { Attack, Decay, Sustain, Release };
    struct Voice {
        bool active = false;
        int32_t pitch = 0;
        double freq = 0.0, target = 0.0;
        double phase[kMaxUnison] = {0.0};
        float velocity = 0.0f, env = 0.0f;
        Svf fl, fr; // one filter per channel (unison is stereo)
        Stage stage = Stage::Attack;
        VoiceExpr ex; // MPE bend / pressure / slide
    };

    void resetExpr(Voice& v) {
        float add[kExprDims];
        gexpr_.offsets(kWheelRange, add);
        v.ex.reset(add);
    }

    // Retrigger: a fresh note resets the phases and the filter; a legato slide does not.
    void startEnvelope(Voice& v, bool fresh);

    Voice* findFreeVoice();

    // Held-note stack for Mono / Legato: releasing the top note falls back to the one under
    // it. A fixed array — the audio thread must not allocate.
    void pushHeld(int32_t pitch);
    void popHeld(int32_t pitch);

    static constexpr int kVoices = 16;
    Voice voices_[kVoices];
    int32_t held_[kHeldStack] = {};
    int heldCount_ = 0;
    double sampleRate_ = 44100.0;
    float exprCoef_ = exprSmoothCoef(44100.0);
    GlobalExpr gexpr_;                  // pitch wheel / channel pressure (pitch −1)
    std::atomic<float> pn_[kNumParams]; // normalized param values
    bool idle_ = false;                 // sleep flag: set when no voice sounds
};

} // namespace nota
