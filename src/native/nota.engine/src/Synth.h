// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Synth — the built-in polyphonic synth. Oscillator (saw/square/triangle/
// sine) → linear ADSR amplitude envelope → per-voice TPT state-variable low-pass
// (cutoff + resonance) → master gain. All eight parameters are exposed through the
// Instrument plugin-parameter interface (normalized 0..1 + stable string ids), so
// they automate and record exactly like a hosted plugin's params. Deliberately
// modest DSP (naive/aliased oscillators) — richer modes come later.

#pragma once

#include "Instrument.h"

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
    enum Param { Wave = 0, Attack, Decay, Sustain, Release, Cutoff, Resonance, Gain, kNumParams };

    Synth() {
        // Musical defaults.
        pn_[Wave].store(0.0f);       // Saw
        pn_[Attack].store(0.02f);
        pn_[Decay].store(0.25f);
        pn_[Sustain].store(0.7f);
        pn_[Release].store(0.20f);
        pn_[Cutoff].store(0.62f);
        pn_[Resonance].store(0.12f);
        pn_[Gain].store(0.80f);
    }

    int32_t kind() const override { return 0; } // built-in Synth (M7-6) — project compat
    const char* displayName() const override { return "Nota Synth"; }

    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }

    // ---- parameters (automatable via the plugin-param interface) ----------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[] = { "wave", "attack", "decay", "sustain", "release", "cutoff", "resonance", "gain" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        static const char* nm[] = { "Wave", "Attack", "Decay", "Sustain", "Release", "Cutoff", "Resonance", "Gain" };
        return (i >= 0 && i < kNumParams) ? std::string(nm[i]) : std::string{};
    }
    float pluginParamGet(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? pn_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void pluginParamSet(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }
    int32_t pluginParamIndexOfId(const std::string& id) const override {
        for (int32_t i = 0; i < kNumParams; ++i) if (pluginParamId(i) == id) return i;
        return -1;
    }

    // ---- project state (the eight normalized floats, little-endian) --------
    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b(kNumParams * sizeof(float));
        for (int i = 0; i < kNumParams; ++i) {
            float v = pn_[i].load(std::memory_order_relaxed);
            std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
        }
        return b;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data || size < static_cast<int32_t>(kNumParams * sizeof(float))) return;
        for (int i = 0; i < kNumParams; ++i) {
            float v; std::memcpy(&v, data + i * sizeof(float), sizeof(float));
            if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        }
    }
    std::shared_ptr<Instrument> clone() const override {
        auto s = std::make_shared<Synth>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    void noteOn(int32_t pitch, float velocity) override {
        Voice* v = findFreeVoice();
        v->active = true;
        v->pitch = pitch;
        v->freq = 440.0 * std::pow(2.0, (pitch - 69) / 12.0);
        v->phase = 0.0;
        v->velocity = velocity;
        v->env = 0.0f;
        v->stage = Stage::Attack;
        v->ic1eq = 0.0; v->ic2eq = 0.0;
    }

    void noteOff(int32_t pitch) override {
        for (auto& v : voices_)
            if (v.active && v.pitch == pitch && v.stage != Stage::Release)
                v.stage = Stage::Release;
    }

    void allNotesOff() override { for (auto& v : voices_) v.active = false; }

    void render(float* out, int32_t frames) override {
        // Snapshot params once per block (denormalise to musical units).
        const int   wave    = static_cast<int>(std::lround(pn_[Wave].load(std::memory_order_relaxed) * 3.0f));
        const float atkRate = static_cast<float>(1.0 / (secOf(Attack, 0.001, 2.0) * sampleRate_));
        const float decRate = static_cast<float>(1.0 / (secOf(Decay, 0.002, 2.0) * sampleRate_));
        const float relRate = static_cast<float>(1.0 / (secOf(Release, 0.002, 3.0) * sampleRate_));
        const float sustain = pn_[Sustain].load(std::memory_order_relaxed);
        const float gain    = pn_[Gain].load(std::memory_order_relaxed);

        // TPT state-variable low-pass coefficients (cutoff + resonance).
        const double fc = expMap(pn_[Cutoff].load(std::memory_order_relaxed), 20.0, 18000.0);
        const double g  = std::tan(kPi * std::min(fc, sampleRate_ * 0.49) / sampleRate_);
        const double k  = 2.0 - 1.94 * pn_[Resonance].load(std::memory_order_relaxed); // 2=no res → ~0.06 max res
        const double a1 = 1.0 / (1.0 + g * (g + k));
        const double a2 = g * a1;
        const double a3 = g * a2;

        for (auto& v : voices_) {
            if (!v.active) continue;
            const double inc = v.freq / sampleRate_;
            for (int32_t i = 0; i < frames; ++i) {
                switch (v.stage) {
                    case Stage::Attack:
                        v.env += atkRate;
                        if (v.env >= 1.0f) { v.env = 1.0f; v.stage = Stage::Decay; }
                        break;
                    case Stage::Decay:
                        v.env -= decRate;
                        if (v.env <= sustain) { v.env = sustain; v.stage = Stage::Sustain; }
                        break;
                    case Stage::Sustain: break;
                    case Stage::Release:
                        v.env -= relRate;
                        if (v.env <= 0.0f) { v.env = 0.0f; v.active = false; }
                        break;
                }
                const double osc = oscSample(wave, v.phase);
                v.phase += inc;
                if (v.phase >= 1.0) v.phase -= 1.0;

                // TPT SVF low-pass (Zavalishin). v2 is the low-pass output.
                const double v3 = osc - v.ic2eq;
                const double v1 = a1 * v.ic1eq + a2 * v3;
                const double v2 = v.ic2eq + a2 * v.ic1eq + a3 * v3;
                v.ic1eq = 2.0 * v1 - v.ic1eq;
                v.ic2eq = 2.0 * v2 - v.ic2eq;

                const float s = static_cast<float>(v2) * v.env * v.velocity * gain * 0.25f;
                out[i * 2]     += s;
                out[i * 2 + 1] += s;
                if (!v.active) break;
            }
        }
    }

private:
    static constexpr double kPi = 3.14159265358979323846;

    static double oscSample(int wave, double phase) {
        switch (wave) {
            case 1:  return phase < 0.5 ? 1.0 : -1.0;                 // square
            case 2:  return 4.0 * std::fabs(phase - 0.5) - 1.0;       // triangle
            case 3:  return std::sin(2.0 * kPi * phase);              // sine
            default: return 2.0 * phase - 1.0;                        // saw
        }
    }
    // Exponential (perceptual) map from a normalized value to [lo, hi].
    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    // Denormalised time (seconds) for an ADSR param, clamped to [lo, hi].
    double secOf(Param p, double lo, double hi) const { return expMap(pn_[p].load(std::memory_order_relaxed), lo, hi); }

    enum class Stage { Attack, Decay, Sustain, Release };
    struct Voice {
        bool     active = false;
        int32_t  pitch = 0;
        double   freq = 0.0, phase = 0.0;
        float    velocity = 0.0f, env = 0.0f;
        double   ic1eq = 0.0, ic2eq = 0.0;   // SVF integrator state
        Stage    stage = Stage::Attack;
    };

    Voice* findFreeVoice() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0];
        for (auto& v : voices_) if (v.env < q->env) q = &v;   // steal the quietest
        return q;
    }

    static constexpr int kVoices = 16;
    Voice  voices_[kVoices];
    double sampleRate_ = 44100.0;
    std::atomic<float> pn_[kNumParams];   // normalized param values
};

} // namespace nota
