// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Built-in guitar-amp emulation (Amp, device kind 6) — a classic guitar-amp-style
// insert. A preamp waveshaper (tanh cascade, per-model drive + bias) feeds a
// Fender/Marshall-ish tone stack (Bass/Middle/Treble + Presence) and a simple
// speaker-cabinet band-pass, then a master Output and Dry/Wet. Seven voicings
// from Clean to Heavy plus a Bass amp. All processing is memoryless per sample
// except the biquad filter states (per channel); coefficients are recomputed at
// block rate from the atomic params. A core Device (JUCE-free), lock-free params.

#pragma once

#include "Device.h"
#include "Oversampler.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota {

class Amp : public Device {
public:
    // Appended for the mockup-2m rework (old projects default these to neutral / model-cab).
    enum { Model = 0, Gain, Bass, Middle, Treble, Presence, Output, Mix,
           CabOn, CabType, Mic, Axis, Gate,
           Oversampling,   // 0..1 → Off/2×/4×/8× (appended last: keeps saved indices stable)
           kNumParams };

    Amp() {
        p_[Model].store(2.0f);      // Blues
        p_[Gain].store(3.0f);
        p_[Bass].store(5.0f);
        p_[Middle].store(5.0f);
        p_[Treble].store(5.0f);
        p_[Presence].store(5.0f);
        p_[Output].store(5.0f);
        p_[Mix].store(1.0f);
        p_[CabOn].store(1.0f);      // cabinet on = the pre-rework behaviour
        p_[CabType].store(0.0f);    // 0 = model-matched cab (no change)
        p_[Mic].store(0.0f);        // 0 = dynamic (no change)
        p_[Axis].store(0.0f);       // on-axis (no change)
        p_[Gate].store(0.0f);       // gate off
    }

    void setSampleRate(double sr, int32_t maxBlock) override {
        sr_ = sr > 0 ? sr : 44100.0;
        for (int c = 0; c < 2; ++c) {
            preHP_[c].reset(); bassSh_[c].reset(); midPk_[c].reset();
            trebSh_[c].reset(); presSh_[c].reset(); cabLP_[c].reset(); cabHP_[c].reset();
            gateEnv_[c] = 0.0f; gateGain_[c] = 1.0f;
        }
        maxFrames_ = maxBlock > 0 ? maxBlock : 4096;
        os_.prepare(maxFrames_);
        dry_.assign(maxFrames_ * 2, 0.0f);
    }

    void process(float* buf, int32_t frames) override {
        const int   m       = std::clamp((int)std::lround(get(Model)), 0, kModels - 1);
        const float g01     = std::clamp(get(Gain) / 10.0f, 0.0f, 1.0f);
        const AmpModel& md  = kModel[m];

        // Drive into the preamp: squared taper so the knob's top half is where the
        // real breakup lives. Makeup counteracts the tanh loudness so voicings match.
        const float drive   = md.driveMul * (0.35f + g01 * g01 * 3.0f);
        const float makeup  = std::clamp(1.6f / std::sqrt(std::max(drive, 1e-3f)), 0.2f, 2.5f);
        const float biasDC  = std::tanh(md.bias);

        const float bassDb  = (get(Bass) - 5.0f) / 5.0f * 12.0f;
        const float midDb   = (get(Middle) - 5.0f) / 5.0f * 10.0f;
        const float trebDb  = (get(Treble) - 5.0f) / 5.0f * 12.0f;
        const float presDb  = (get(Presence) - 5.0f) / 5.0f * 8.0f;
        const float outGain = std::pow(10.0f, (get(Output) - 5.0f) / 5.0f * 12.0f / 20.0f);
        const float mix     = std::clamp(get(Mix), 0.0f, 1.0f);

        // Cabinet: model-matched band-pass shifted by the chosen cab size, mic and off-axis.
        const bool  cabOn   = get(CabOn) >= 0.5f;
        const int   cabType = std::clamp((int)std::lround(get(CabType)), 0, kCabs - 1);
        const int   mic     = std::clamp((int)std::lround(get(Mic)), 0, kMics - 1);
        const float axis    = std::clamp(get(Axis), 0.0f, 1.0f);
        const double cabLpHz = std::clamp(md.cabLpHz * kCabLp[cabType] * kMicLp[mic] * (1.0 - axis * 0.45), 800.0, sr_ * 0.45);
        const double cabHpHz = md.cabHpHz * kCabHp[cabType];

        // Gate: threshold + smoothing coefficients (0 = off).
        const float gateAmt = std::clamp(get(Gate), 0.0f, 1.0f);
        const float gateThr = gateAmt > 0.001f ? std::pow(10.0f, (-75.0f + gateAmt * 55.0f) / 20.0f) : 0.0f;

        // Recompute coefficients once per block (shared L/R; states stay per channel).
        Biquad::Coef preHP  = Biquad::highpass (sr_, md.preHpHz, 0.707);
        Biquad::Coef bassSh = Biquad::lowShelf (sr_, 110.0,  bassDb);
        Biquad::Coef midPk  = Biquad::peak     (sr_, 650.0,  midDb, 0.70);
        Biquad::Coef trebSh = Biquad::highShelf(sr_, 3000.0, trebDb);
        Biquad::Coef presSh = Biquad::highShelf(sr_, 4500.0, presDb);
        Biquad::Coef cabHP  = Biquad::highpass (sr_, cabHpHz, 0.707);
        Biquad::Coef cabLP  = Biquad::lowpass  (sr_, cabLpHz, 0.707);
        for (int c = 0; c < 2; ++c) {
            preHP_[c].set(preHP); bassSh_[c].set(bassSh); midPk_[c].set(midPk);
            trebSh_[c].set(trebSh); presSh_[c].set(presSh); cabHP_[c].set(cabHP); cabLP_[c].set(cabLP);
        }

        // Oversample the preamp waveshaper (the only aliasing source — the tone stack
        // and cabinet are linear and stay at base rate; the pre-distortion high-pass
        // runs at base rate before the up-sampling).
        const int osIdx = std::clamp((int)std::lround(get(Oversampling) * 3.0f), 0, 3);
        os_.setActive(1 << osIdx);

        if (frames > maxFrames_) frames = maxFrames_;

        // Base-rate pre: stash the dry input, then replace the buffer with the
        // pre-HP'd signal that feeds the (oversampled) shaper.
        for (int32_t i = 0; i < frames; ++i) {
            for (int c = 0; c < 2; ++c) {
                const float in = buf[i * 2 + c];
                dry_[i * 2 + c] = in;
                buf[i * 2 + c] = preHP_[c].process(in);
            }
        }

        // Oversampled memoryless preamp: drive + bias + cascaded soft clipping.
        const int stages = md.stages;
        os_.process(buf, frames, [&](float& l, float& r, int /*i*/) {
            auto shape = [&](float x) {
                x = x * drive + md.bias;
                for (int s = 0; s < stages; ++s) { x = std::tanh(x); if (s < stages - 1) x *= 2.0f; }
                return x - biasDC;
            };
            l = shape(l); r = shape(r);
        });

        // Base-rate post: tone stack, cabinet, makeup/output, gate, dry/wet.
        for (int32_t i = 0; i < frames; ++i) {
            for (int c = 0; c < 2; ++c) {
                float x = buf[i * 2 + c];
                x = bassSh_[c].process(x);
                x = midPk_[c].process(x);
                x = trebSh_[c].process(x);
                x = presSh_[c].process(x);
                if (cabOn) { x = cabHP_[c].process(x); x = cabLP_[c].process(x); }
                float wet = x * makeup * outGain;
                const float in = dry_[i * 2 + c];
                if (gateThr > 0.0f) {   // noise gate keyed on the input level
                    const float lvl = std::fabs(in);
                    gateEnv_[c] += (lvl - gateEnv_[c]) * (lvl > gateEnv_[c] ? 0.30f : 0.0015f);
                    const float target = gateEnv_[c] > gateThr ? 1.0f : 0.0f;
                    gateGain_[c] += (target - gateGain_[c]) * (target > gateGain_[c] ? 0.30f : 0.02f);
                    wet *= gateGain_[c];
                }
                buf[i * 2 + c] = in * (1.0f - mix) + wet * mix;
            }
        }
    }

    const char* displayName() const override { return "Nota Valve"; }
    int32_t     builtinKind() const override { return 6; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Model: return "Model"; case Gain: return "Gain"; case Bass: return "Bass";
            case Middle: return "Middle"; case Treble: return "Treble"; case Presence: return "Presence";
            case Output: return "Output"; case Mix: return "Mix";
            case CabOn: return "Cab On"; case CabType: return "Cabinet"; case Mic: return "Mic";
            case Axis: return "Axis"; case Gate: return "Gate";
            case Oversampling: return "Oversampling"; default: return "";
        }
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t i) const override {
        if (i == Model) return (float)(kModels - 1);
        if (i == CabType) return (float)(kCabs - 1);
        if (i == Mic) return (float)(kMics - 1);
        if (i == Mix || i == CabOn || i == Axis || i == Gate || i == Oversampling) return 1.0f;
        return 10.0f;
    }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(v, std::memory_order_relaxed);
    }

private:
    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }

    // RBJ biquad (transposed direct form II). Coeffs computed off-thread of state.
    struct Biquad {
        struct Coef { float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0; };
        float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0, z1 = 0, z2 = 0;
        void reset() { z1 = z2 = 0; }
        void set(const Coef& c) { b0 = c.b0; b1 = c.b1; b2 = c.b2; a1 = c.a1; a2 = c.a2; }
        inline float process(float x) {
            const float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
        static Coef norm(double b0, double b1, double b2, double a0, double a1, double a2) {
            Coef c; c.b0 = (float)(b0 / a0); c.b1 = (float)(b1 / a0); c.b2 = (float)(b2 / a0);
            c.a1 = (float)(a1 / a0); c.a2 = (float)(a2 / a0); return c;
        }
        static double w0(double sr, double f) {
            f = std::clamp(f, 10.0, sr * 0.45);
            return 2.0 * 3.14159265358979323846 * f / sr;
        }
        static Coef lowpass(double sr, double f, double q) {
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * q);
            return norm((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + a, -2 * cw, 1 - a);
        }
        static Coef highpass(double sr, double f, double q) {
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * q);
            return norm((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + a, -2 * cw, 1 - a);
        }
        static Coef peak(double sr, double f, double dB, double q) {
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * q), A = std::pow(10.0, dB / 40.0);
            return norm(1 + a * A, -2 * cw, 1 - a * A, 1 + a / A, -2 * cw, 1 - a / A);
        }
        static Coef lowShelf(double sr, double f, double dB) {
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * 0.707), A = std::pow(10.0, dB / 40.0), s = 2 * std::sqrt(A) * a;
            return norm(A * ((A + 1) - (A - 1) * cw + s), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - s),
                        (A + 1) + (A - 1) * cw + s, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - s);
        }
        static Coef highShelf(double sr, double f, double dB) {
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * 0.707), A = std::pow(10.0, dB / 40.0), s = 2 * std::sqrt(A) * a;
            return norm(A * ((A + 1) + (A - 1) * cw + s), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - s),
                        (A + 1) - (A - 1) * cw + s, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - s);
        }
    };

    // Per-voicing character: preamp drive, clipping asymmetry + stage count, the
    // pre-distortion low-cut (tightness) and the speaker-cabinet band edges.
    struct AmpModel { float driveMul; float bias; int stages; float preHpHz; float cabHpHz; float cabLpHz; };
    static constexpr int kModels = 7;
    // Cabinet / mic voicing: multipliers on the model's band-pass edges. Index 0 = neutral
    // (model-matched cab / dynamic mic) so old projects at defaults sound identical.
    static constexpr int kCabs = 5, kMics = 3;   // Match · 1×12 · 2×12 · 4×12 · 1×15  /  Dynamic · Condenser · Ribbon
    static constexpr float kCabLp[kCabs] = { 1.00f, 0.90f, 1.00f, 1.15f, 0.72f };
    static constexpr float kCabHp[kCabs] = { 1.00f, 1.10f, 1.00f, 0.90f, 0.70f };
    static constexpr float kMicLp[kMics] = { 1.00f, 1.28f, 0.80f };
    static constexpr AmpModel kModel[kModels] = {
        /* Clean */ { 1.5f, 0.00f, 1,  20.0f,  70.0f, 7000.0f },
        /* Boost */ { 3.0f, 0.05f, 1,  30.0f,  75.0f, 6500.0f },
        /* Blues */ { 6.0f, 0.10f, 1,  45.0f,  80.0f, 6000.0f },
        /* Rock  */ { 12.0f, 0.15f, 2, 70.0f,  85.0f, 5200.0f },
        /* Lead  */ { 22.0f, 0.20f, 2, 95.0f,  90.0f, 5000.0f },
        /* Heavy */ { 40.0f, 0.28f, 3, 120.0f, 95.0f, 4500.0f },
        /* Bass  */ { 4.0f, 0.00f, 1,  20.0f,  40.0f, 3800.0f },
    };

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    Biquad preHP_[2], bassSh_[2], midPk_[2], trebSh_[2], presSh_[2], cabLP_[2], cabHP_[2];
    float gateEnv_[2] = {0, 0}, gateGain_[2] = {1, 1};

    Oversampler os_;                 // preamp-waveshaper anti-aliasing
    int32_t maxFrames_ = 4096;
    std::vector<float> dry_;         // interleaved dry stash (sized in setSampleRate)
};

} // namespace nota
