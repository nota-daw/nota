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
//   Noise  ┘                     └─ Resonator 2 ─┘   (1+2 parallel: 1 + 2)
//
// Mode coefficients (frequency / decay / amplitude, per Type + Decay/Material/Bright/
// Inharm/Ratio/Hit/Tune) are computed at note-on per voice; the exciter and global
// params (Volume/Pan/NoteOff/Tune) apply live. All params ride the Instrument plugin-
// param interface (normalized 0..1, stable ids) → automation / persist / clone for free.
// Header-only, allocation-free after construction. Name & DSP are Nota's own.

#pragma once

#include "Instrument.h"

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
    // Parameter layout (normalized 0..1). Order == persisted state layout — APPEND ONLY.
    enum Param {
        MalletVol = 0, MalletStiff, MalletNoise, MalletColor,
        NoiseVol, NoiseEnvAmt, NoiseType, NoiseFreq, NoiseRes, NoiseA, NoiseD, NoiseS, NoiseR,
        R1Type, R1Decay, R1Material, R1Bright, R1Inharm, R1Ratio, R1Hit, R1Tune,
        R2On, R2Type, R2Decay, R2Material, R2Bright, R2Inharm, R2Ratio, R2Hit, R2Tune,
        Structure, Tune, Fine, NoteOff, Pan, Volume,
        kNumParams
    };

    PhysicalSynth() {
        // A bright marimba by default: a medium mallet into a Marimba resonator.
        set(MalletVol, 0.85f); set(MalletStiff, 0.45f); set(MalletNoise, 0.1f); set(MalletColor, 0.4f);
        set(NoiseVol, 0.0f); set(NoiseEnvAmt, 0.5f); set(NoiseType, 0.0f); set(NoiseFreq, 0.5f); set(NoiseRes, 0.2f);
        set(NoiseA, 0.0f); set(NoiseD, 0.3f); set(NoiseS, 0.0f); set(NoiseR, 0.25f);
        set(R1Type, 0.2f); set(R1Decay, 0.5f); set(R1Material, 0.5f); set(R1Bright, 0.6f);
        set(R1Inharm, 0.0f); set(R1Ratio, 0.5f); set(R1Hit, 0.25f); set(R1Tune, 0.5f);
        set(R2On, 0.0f); set(R2Type, 0.0f); set(R2Decay, 0.5f); set(R2Material, 0.5f); set(R2Bright, 0.5f);
        set(R2Inharm, 0.0f); set(R2Ratio, 0.5f); set(R2Hit, 0.3f); set(R2Tune, 0.5f);
        set(Structure, 1.0f);   // 1+2 parallel
        set(Tune, 0.5f); set(Fine, 0.5f); set(NoteOff, 0.3f); set(Pan, 0.5f); set(Volume, 0.8f);
    }

    int32_t kind() const override { return 2; }
    const char* displayName() const override { return "Nota Physical"; }

    void setSampleRate(double sr) override { sampleRate_ = sr > 0 ? sr : 44100.0; }

    // ---- parameters -------------------------------------------------------
    int32_t pluginParamCount() const override { return kNumParams; }
    std::string pluginParamId(int32_t i) const override {
        static const char* ids[] = {
            "malletvol", "malletstiff", "malletnoise", "malletcolor",
            "noisevol", "noiseenv", "noisetype", "noisefreq", "noisereso", "noisea", "noised", "noises", "noiser",
            "r1type", "r1decay", "r1material", "r1bright", "r1inharm", "r1ratio", "r1hit", "r1tune",
            "r2on", "r2type", "r2decay", "r2material", "r2bright", "r2inharm", "r2ratio", "r2hit", "r2tune",
            "structure", "tune", "fine", "noteoff", "pan", "volume" };
        return (i >= 0 && i < kNumParams) ? std::string(ids[i]) : std::string{};
    }
    std::string pluginParamName(int32_t i) const override {
        static const char* nm[] = {
            "Mallet Volume", "Mallet Stiffness", "Mallet Noise", "Mallet Color",
            "Noise Volume", "Noise Env", "Noise Type", "Noise Freq", "Noise Reso", "Noise Attack", "Noise Decay", "Noise Sustain", "Noise Release",
            "Res1 Type", "Res1 Decay", "Res1 Material", "Res1 Bright", "Res1 Inharm", "Res1 Ratio", "Res1 Hit", "Res1 Tune",
            "Res2 On", "Res2 Type", "Res2 Decay", "Res2 Material", "Res2 Bright", "Res2 Inharm", "Res2 Ratio", "Res2 Hit", "Res2 Tune",
            "Structure", "Tune", "Fine", "Note Off", "Pan", "Volume" };
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

    // ---- project state (kNumParams normalized floats, little-endian) -------
    std::vector<uint8_t> getState() const override {
        std::vector<uint8_t> b(kNumParams * sizeof(float));
        for (int i = 0; i < kNumParams; ++i) {
            float v = pn_[i].load(std::memory_order_relaxed);
            std::memcpy(b.data() + i * sizeof(float), &v, sizeof(float));
        }
        return b;
    }
    void setState(const uint8_t* data, int32_t size) override {
        if (!data) return;
        const int n = std::min<int>(kNumParams, size / static_cast<int>(sizeof(float)));
        for (int i = 0; i < n; ++i) {
            float v; std::memcpy(&v, data + i * sizeof(float), sizeof(float));
            if (std::isfinite(v)) pn_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
        }
    }
    std::shared_ptr<Instrument> clone() const override {
        auto s = std::make_shared<PhysicalSynth>();
        for (int i = 0; i < kNumParams; ++i) s->pn_[i].store(pn_[i].load(std::memory_order_relaxed), std::memory_order_relaxed);
        s->setSampleRate(sampleRate_);
        return s;
    }

    // ---- note events ------------------------------------------------------
    void noteOn(int32_t pitch, float velocity) override {
        Voice* v = findFreeVoice();
        const double tuneSemis = (get(Tune) - 0.5f) * 48.0 + (get(Fine) - 0.5f) * 2.0;  // ±24 st + ±1 st fine
        const double f0 = 440.0 * std::pow(2.0, (pitch - 69 + tuneSemis) / 12.0);
        v->active = true; v->released = false;
        v->pitch = pitch; v->vel = std::clamp(velocity, 0.0f, 1.0f);
        v->peak = v->vel;
        v->relEnv = 1.0f;

        // Mallet strike: contact time from Stiffness (soft = long/dark, hard = short/bright).
        const double contact = expMap(1.0 - get(MalletStiff), 0.0004, 0.010) * sampleRate_;   // samples
        v->mEnvDec = static_cast<float>(std::exp(-1.0 / std::max(1.0, contact)));
        v->mCut = std::clamp(0.05f + 0.9f * get(MalletStiff), 0.02f, 0.999f);   // lowpass: hard = brighter
        v->mEnv = 1.0f; v->mLp = 0.0f; v->mFirst = true; v->mActive = get(MalletVol) > 0.0001f;

        // Noise burst envelope reset.
        v->nEnv = 0.0f; v->nStage = get(NoiseVol) > 0.0001f ? Stage::Attack : Stage::Off;
        v->nIc1 = v->nIc2 = 0.0;

        buildBank(v->r1, f0 * std::exp2((get(R1Tune) - 0.5) * 4.0),
                  typeOf(R1Type), get(R1Decay), get(R1Material), get(R1Bright), get(R1Inharm), get(R1Ratio), get(R1Hit));
        if (get(R2On) >= 0.5f)
            buildBank(v->r2, f0 * std::exp2((get(R2Tune) - 0.5) * 4.0),
                      typeOf(R2Type), get(R2Decay), get(R2Material), get(R2Bright), get(R2Inharm), get(R2Ratio), get(R2Hit));
    }
    void noteOff(int32_t pitch) override {
        for (auto& v : voices_) if (v.active && v.pitch == pitch && !v.released) v.released = true;
    }
    void allNotesOff() override { for (auto& v : voices_) v.active = false; }

    void render(float* out, int32_t frames) override {
        const float malletVol = get(MalletVol);
        const float malletNoise = get(MalletNoise);
        const float malletColor = get(MalletColor);
        const float noiseVol = get(NoiseVol);
        const float noiseEnvAmt = bip(NoiseEnvAmt);
        const int   noiseType = std::clamp((int)std::lround(get(NoiseType) * 2.0f), 0, 2);
        const double noiseBase = expMap(get(NoiseFreq), 60.0, 12000.0);
        const double noiseK = std::clamp(2.0 - 1.9 * get(NoiseRes), 0.05, 2.0);
        const float nA = rate(NoiseA, 0.001, 2.0), nD = rate(NoiseD, 0.002, 4.0), nR = rate(NoiseR, 0.002, 5.0);
        const float nS = get(NoiseS);
        const bool  r2on = get(R2On) >= 0.5f;
        const bool  serial = get(Structure) < 0.5f;   // 0 = 1>2 (serial), 1 = 1+2 (parallel)
        const float relCoef = static_cast<float>(std::exp(-1.0 / (expMap(1.0 - get(NoteOff), 0.006, 4.0) * sampleRate_)));
        const float volume = get(Volume);
        const float pan = (get(Pan) - 0.5f) * 2.0f;
        const float gl = std::cos((pan + 1.0f) * 0.25f * (float)kPi);
        const float gr = std::sin((pan + 1.0f) * 0.25f * (float)kPi);

        for (int32_t i = 0; i < frames; ++i) {
            float mono = 0.0f;
            for (auto& v : voices_) {
                if (!v.active) continue;

                // --- exciter: mallet strike + filtered noise burst -----------
                float exc = 0.0f;
                if (v.mActive) {
                    float raw = (v.mFirst ? 1.0f : 0.0f) + malletNoise * noise();
                    v.mFirst = false;
                    v.mLp += v.mCut * (raw - v.mLp);
                    float m = v.mLp + malletColor * (raw - v.mLp);   // Color: dark(lp) .. bright(raw)
                    exc += m * v.mEnv * malletVol;
                    v.mEnv *= v.mEnvDec;
                    if (v.mEnv < 1.0e-4f) v.mActive = false;
                }
                if (v.nStage != Stage::Off) {
                    advanceEnv(v.nEnv, v.nStage, nA, nD, nS, nR);
                    const double cut = std::clamp(noiseBase * std::exp2(noiseEnvAmt * 3.0 * v.nEnv), 20.0, sampleRate_ * 0.49);
                    const float nf = svf(v.nIc1, v.nIc2, cut, noiseK, noise(), noiseType);
                    exc += nf * v.nEnv * noiseVol;
                }

                // --- resonator banks -----------------------------------------
                float o1 = bank(v.r1, exc);
                float wet;
                if (r2on) {
                    float o2 = bank(v.r2, serial ? o1 : exc);
                    wet = serial ? o2 : (o1 + o2);
                } else {
                    wet = o1;
                }
                if (v.released) { wet *= v.relEnv; v.relEnv *= relCoef; }

                const float s = wet * v.vel;
                mono += s;
                v.peak = std::max(v.peak * 0.99997f, std::fabs(s));
                if (v.peak < 8.0e-5f && !v.mActive && v.nStage == Stage::Off) v.active = false;   // rang out
            }
            const float o = mono * volume * 0.4f;
            out[i * 2]     += o * gl;
            out[i * 2 + 1] += o * gr;
        }
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr double kTwoPi = 6.283185307179586;
    static constexpr int kVoices = 8;
    static constexpr int kModes = 16;

    enum class Stage { Attack, Decay, Sustain, Release, Off };

    // One modal resonator bank: kModes tuned 2-pole resonators (frozen coefficients).
    struct Bank {
        double a1[kModes] = {}, a2[kModes] = {}, b0[kModes] = {};   // 2R cos w, R², input gain
        double y1[kModes] = {}, y2[kModes] = {};
    };

    struct Voice {
        bool     active = false, released = false;
        int32_t  pitch = 0;
        float    vel = 0.0f, peak = 0.0f, relEnv = 1.0f;
        // Mallet.
        bool     mActive = false, mFirst = true;
        float    mEnv = 0.0f, mEnvDec = 0.0f, mLp = 0.0f, mCut = 0.5f;
        // Noise burst.
        Stage    nStage = Stage::Off;
        float    nEnv = 0.0f;
        double   nIc1 = 0.0, nIc2 = 0.0;
        Bank     r1, r2;
    };
    Voice voices_[kVoices];

    void  set(Param p, float v) { pn_[p].store(v, std::memory_order_relaxed); }
    float get(Param p) const { return pn_[p].load(std::memory_order_relaxed); }
    double bip(Param p) const { return (get(p) - 0.5f) * 2.0; }
    int    typeOf(Param p) const { return std::clamp((int)std::lround(get(p) * 5.0f), 0, 5); }

    static double expMap(double v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp(v, 0.0, 1.0)); }
    float rate(Param p, double lo, double hi) const { return static_cast<float>(1.0 / (expMap(get(p), lo, hi) * sampleRate_)); }
    float noise() { rng_ ^= rng_ << 13; rng_ ^= rng_ >> 17; rng_ ^= rng_ << 5; return (rng_ & 0xFFFFFF) / 8388608.0f - 1.0f; }

    static void advanceEnv(float& env, Stage& st, float atk, float dec, float sus, float rel) {
        switch (st) {
            case Stage::Attack:  env += atk; if (env >= 1.0f) { env = 1.0f; st = Stage::Decay; } break;
            case Stage::Decay:   env -= dec; if (env <= sus)  { env = sus;  st = (sus <= 1.0e-4f ? Stage::Off : Stage::Sustain); } break;
            case Stage::Sustain: break;
            case Stage::Release: env -= rel; if (env <= 0.0f) { env = 0.0f; st = Stage::Off; } break;
            case Stage::Off:     break;
        }
    }

    // TPT state-variable filter for the noise exciter. type: 0 LP, 1 BP, 2 HP.
    float svf(double& ic1, double& ic2, double fc, double k, double in, int type) const {
        const double g = std::tan(kPi * fc / sampleRate_);
        const double a1 = 1.0 / (1.0 + g * (g + k));
        const double v3 = in - ic2;
        const double v1 = a1 * ic1 + a1 * g * v3;
        const double v2 = ic2 + g * v1;
        ic1 = 2.0 * v1 - ic1;
        ic2 = 2.0 * v2 - ic2;
        switch (type) { case 1: return (float)v1; case 2: return (float)(in - k * v1 - v2); default: return (float)v2; }
    }

    // Run one sample through a modal bank (sum of the tuned resonators).
    static float bank(Bank& b, float x) {
        double acc = 0.0;
        for (int m = 0; m < kModes; ++m) {
            const double y = b.b0[m] * x + b.a1[m] * b.y1[m] - b.a2[m] * b.y2[m];
            b.y2[m] = b.y1[m]; b.y1[m] = y;
            acc += y;
        }
        return static_cast<float>(acc);
    }

    // Partial ratios for a material Type (mode 0 = the fundamental = 1.0).
    static double ratioOf(int type, int m) {
        // Free-free beam (bar): (β_m/β_0)², β ≈ (m+0.5)π with exact low modes.
        static const double beam[kModes] = {
            1.0, 2.7565, 5.4039, 8.9330, 13.3443, 18.6379, 24.8137, 31.8718, 39.8122,
            48.6349, 58.3399, 68.9272, 80.3968, 92.7487, 105.983, 120.099 };
        // Tuned marimba bar (2nd partial ≈ 2 octaves).
        static const double marimba[kModes] = {
            1.0, 3.984, 9.531, 17.65, 28.10, 41.0, 56.4, 74.3, 94.7,
            117.6, 143.0, 170.9, 201.3, 234.2, 269.6, 307.5 };
        // Ideal circular membrane (drum) Bessel ratios.
        static const double membrane[kModes] = {
            1.0, 1.5933, 2.1355, 2.2954, 2.6531, 2.9173, 3.1555, 3.5001, 3.5985,
            3.6521, 3.8452, 4.0602, 4.1057, 4.2323, 4.6018, 4.8321 };
        switch (type) {
            case 0:  return beam[m];
            case 1:  return marimba[m];
            case 2:  return static_cast<double>(m + 1);          // String: harmonic
            case 3:  return membrane[m];
            case 4:  return std::sqrt(static_cast<double>(m + 1)); // Plate: dense metallic (√ series)
            default: return static_cast<double>(2 * m + 1);       // Pipe: odd harmonics
        }
    }

    // Freeze a bank's coefficients from a fundamental + Type/Decay/Material/Bright/Inharm/Ratio/Hit.
    void buildBank(Bank& b, double f0, int type, float decay, float material, float bright,
                   float inharm, float ratioP, float hit) const {
        const double baseDecay = expMap(decay, 0.04, 18.0);        // ring time (s)
        const double matRoll = expMap(material, 1.0, 0.45);        // high-mode decay scaling (metal .. damped)
        const double brightRoll = expMap(bright, 0.55, 1.0);       // high-mode amplitude tilt
        const double ratioExp = 0.5 + ratioP;                      // partial-spacing exponent (0.5 .. 1.5)
        const double stretch = inharm * 0.04;                      // inharmonic stretch per mode
        const double nyq = sampleRate_ * 0.49;
        for (int m = 0; m < kModes; ++m) {
            double r = ratioOf(type, m) * (1.0 + stretch * m);
            r = std::pow(r, ratioExp);
            const double f = f0 * r;
            if (f >= nyq || f <= 0.0) { b.a1[m] = b.a2[m] = b.b0[m] = 0.0; b.y1[m] = b.y2[m] = 0.0; continue; }
            const double tau = std::max(0.01, baseDecay * std::pow(matRoll, m));
            const double R = std::exp(-1.0 / (tau * sampleRate_));
            const double w = kTwoPi * f / sampleRate_;
            b.a1[m] = 2.0 * R * std::cos(w);
            b.a2[m] = R * R;
            // Amplitude: bright tilt × strike-position comb (mode excited ∝ |sin|). The
            // sin(w) input gain normalises the resonator so its struck peak ≈ amp,
            // independent of the (near-1) decay radius.
            const double amp = std::pow(brightRoll, m) * (0.3 + 0.7 * std::fabs(std::sin((m + 1) * kPi * (0.02 + 0.96 * hit))));
            b.b0[m] = amp * std::sin(w);
            b.y1[m] = b.y2[m] = 0.0;
        }
    }

    Voice* findFreeVoice() {
        for (auto& v : voices_) if (!v.active) return &v;
        Voice* q = &voices_[0];
        for (auto& v : voices_) if (v.peak < q->peak) q = &v;   // steal the quietest
        return q;
    }

    double sampleRate_ = 44100.0;
    uint32_t rng_ = 0x2545F491u;
    std::atomic<float> pn_[kNumParams];
};

} // namespace nota
