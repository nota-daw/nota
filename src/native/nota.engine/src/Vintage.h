// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Built-in Nota Vintage (device kind 8) — a multi-era signal-degradation + saturation
// insert in the spirit of classic vinyl-distortion units, widened into six voicings:
// Vinyl / Cassette / Reel-to-Reel / VHS / Tube / Analog. Each Mode sets a character
// table (saturation shape + drive, tone tilt, head bump, band-limit, and the baseline
// amounts of hiss, crackle/dust and wow/flutter). On top sit global knobs — Drive,
// Tone, Wow, Flutter, Noise, Crackle, Wear (an age macro), Mix and Output.
//
//   in ─▶ wow/flutter delay ─▶ saturate(shape) ─▶ tone/bump/band-limit/hp ─▶ +hiss +crackle ─▶ mix ─▶ out
//
// Header-only, allocation-free after construction (fixed delay ring + per-channel
// biquad state). Params are normalized 0..1 and denormalized in process(), so
// persistence / automation / clone flow generically through the base Device. JUCE-free.

#pragma once

#include "Device.h"
#include "Oversampler.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <vector>

namespace nota {

class Vintage : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Mode = 0, Drive, Tone, Wow, Flutter, Noise, Crackle, Wear, Mix, Output,
           Oversampling,   // 0..1 → Off/2×/4×/8× (appended last)
           kNumParams };

    Vintage() {
        p_[Mode].store(0.0f);      // Vinyl
        p_[Drive].store(0.35f);
        p_[Tone].store(0.5f);      // neutral tilt
        p_[Wow].store(0.30f);
        p_[Flutter].store(0.25f);
        p_[Noise].store(0.30f);
        p_[Crackle].store(0.45f);
        p_[Wear].store(0.20f);
        p_[Mix].store(1.0f);
        p_[Output].store(0.5f);    // 0 dB
    }

    void setSampleRate(double sr, int32_t maxBlock) override {
        sr_ = sr > 0 ? sr : 44100.0;
        for (int c = 0; c < 2; ++c) {
            toneSh_[c].reset(); bumpSh_[c].reset(); bandLp_[c].reset(); hp_[c].reset();
            for (int i = 0; i < kDelay; ++i) dl_[c][i] = 0.0f;
        }
        dw_ = 0; wowPh_ = 0.0; flutPh_ = 0.0; clickEnv_ = 0.0f;
        maxFrames_ = maxBlock > 0 ? maxBlock : 4096;
        os_.prepare(maxFrames_);
        dry_.assign(maxFrames_ * 2, 0.0f); click_.assign(maxFrames_, 0.0f);
    }

    void process(float* buf, int32_t frames) override {
        const int   m       = std::clamp((int)std::lround(get(Mode) * 5.0f), 0, kModes - 1);
        const ModeChar& md  = kMode[m];
        const float drive   = std::clamp(get(Drive), 0.0f, 1.0f);
        const float tone    = std::clamp(get(Tone), 0.0f, 1.0f);
        const float wow     = std::clamp(get(Wow), 0.0f, 1.0f);
        const float flutter = std::clamp(get(Flutter), 0.0f, 1.0f);
        const float noise   = std::clamp(get(Noise), 0.0f, 1.0f);
        const float crackle = std::clamp(get(Crackle), 0.0f, 1.0f);
        const float wear    = std::clamp(get(Wear), 0.0f, 1.0f);
        const float mix     = std::clamp(get(Mix), 0.0f, 1.0f);
        const float outGain = std::pow(10.0f, (get(Output) - 0.5f) * 24.0f / 20.0f);   // ±12 dB

        // Saturation drive + asymmetric bias (bias grows with drive → even harmonics).
        const float g       = 1.0f + md.driveMul * drive * 3.0f;
        const float bias    = md.bias * (0.3f + drive);
        const float biasDC  = std::tanh(bias);
        const float makeup  = 1.0f / (0.7f + 0.5f * md.driveMul * drive);

        // Tone tilt (mode voicing + user Tone) as a high shelf; static head bump; the
        // band-limit narrows with Wear; the low cut models the era's limited low end.
        const double tiltDb = (md.tilt + (tone - 0.5)) * 14.0;
        const double bandHz = std::clamp(md.bandHz * (1.0 - 0.55 * wear), 1500.0, sr_ * 0.45);
        Biquad::Coef toneC = Biquad::highShelf(sr_, 3500.0, tiltDb);
        Biquad::Coef bumpC = Biquad::lowShelf (sr_, 90.0, md.bumpDb);
        Biquad::Coef bandC = Biquad::lowpass  (sr_, bandHz, 0.707);
        Biquad::Coef hpC   = Biquad::highpass (sr_, md.hpHz, 0.707);
        for (int c = 0; c < 2; ++c) { toneSh_[c].set(toneC); bumpSh_[c].set(bumpC); bandLp_[c].set(bandC); hp_[c].set(hpC); }

        // Wow (slow) + flutter (fast) pitch modulation via a fractional delay line.
        const double baseDelay = 0.006 * sr_;
        const double wowSamp   = md.wowMul   * wow     * 0.0040 * sr_;
        const double flutSamp  = md.flutMul  * flutter * 0.0015 * sr_;
        const bool   modOn     = (wowSamp + flutSamp) > 0.25;
        const double wowInc    = 0.55 / sr_;    // ~0.55 Hz
        const double flutInc   = 7.0 / sr_;     // ~7 Hz

        const float hissLvl    = noise   * md.noiseMul   * (1.0f + wear) * 0.05f;
        const float crkAmt     = crackle * md.crackleMul * (1.0f + 2.0f * wear);
        const float crkProb    = crkAmt * 0.0012f;

        // Oversample the memoryless saturation only. Wow/flutter (a pitch-mod delay)
        // runs at base rate before the up-sampling; the tone/bump/band-limit filters
        // and the additive hiss + crackle stay at base rate after it.
        const int osIdx = std::clamp((int)std::lround(get(Oversampling) * 3.0f), 0, 3);
        os_.setActive(1 << osIdx);
        const int shape = md.shape;

        if (frames > maxFrames_) frames = maxFrames_;

        // --- Base-rate pre: wow/flutter tap into buf, crackle per sample, stash dry. ---
        for (int32_t i = 0; i < frames; ++i) {
            const float dryL = buf[i * 2], dryR = buf[i * 2 + 1];
            dry_[i * 2] = dryL; dry_[i * 2 + 1] = dryR;

            dl_[0][dw_] = dryL; dl_[1][dw_] = dryR;
            float wl = dryL, wr = dryR;
            if (modOn) {
                const double lfo = wowSamp * std::sin(2.0 * kPi * wowPh_) + flutSamp * std::sin(2.0 * kPi * flutPh_);
                const double tap = baseDelay + lfo;
                wl = readFrac(0, tap); wr = readFrac(1, tap);
            }
            dw_ = (dw_ + 1) & (kDelay - 1);
            wowPh_ += wowInc; if (wowPh_ >= 1.0) wowPh_ -= 1.0;
            flutPh_ += flutInc; if (flutPh_ >= 1.0) flutPh_ -= 1.0;

            float click = 0.0f;
            if (crkAmt > 0.0f) {
                clickEnv_ *= 0.55f;
                if (whiteUni() < crkProb) clickEnv_ = (whiteUni() * 0.7f + 0.2f) * (whiteUni() < 0.5f ? -1.0f : 1.0f);
                click = clickEnv_ * 0.6f;
            }
            click_[i] = click;
            buf[i * 2] = wl; buf[i * 2 + 1] = wr;
        }

        // --- Oversampled saturation (memoryless). ---
        os_.process(buf, frames, [&](float& l, float& r, int /*i*/) {
            l = saturate(shape, l * g, bias, biasDC) * makeup;
            r = saturate(shape, r * g, bias, biasDC) * makeup;
        });

        // --- Base-rate post: tone/bump/band/hp, + hiss + crackle, then dry/wet. ---
        for (int32_t i = 0; i < frames; ++i) {
            float yL = buf[i * 2], yR = buf[i * 2 + 1];
            yL = toneSh_[0].process(yL); yL = bumpSh_[0].process(yL); yL = bandLp_[0].process(yL); yL = hp_[0].process(yL);
            yR = toneSh_[1].process(yR); yR = bumpSh_[1].process(yR); yR = bandLp_[1].process(yR); yR = hp_[1].process(yR);
            if (hissLvl > 0.0f) { yL += (whiteUni() * 2.0f - 1.0f) * hissLvl; yR += (whiteUni() * 2.0f - 1.0f) * hissLvl; }
            yL += click_[i]; yR += click_[i];
            buf[i * 2]     = dry_[i * 2]     * (1.0f - mix) + yL * outGain * mix;
            buf[i * 2 + 1] = dry_[i * 2 + 1] * (1.0f - mix) + yR * outGain * mix;
        }
    }

    const char* displayName() const override { return "Nota Vintage"; }
    int32_t     builtinKind() const override { return 8; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Mode: return "Mode"; case Drive: return "Drive"; case Tone: return "Tone";
            case Wow: return "Wow"; case Flutter: return "Flutter"; case Noise: return "Noise";
            case Crackle: return "Crackle"; case Wear: return "Wear"; case Mix: return "Mix";
            case Output: return "Output"; case Oversampling: return "Oversampling"; default: return "";
        }
    }
    float paramMin(int32_t /*i*/) const override { return 0.0f; }
    float paramMax(int32_t /*i*/) const override { return 1.0f; }
    float getParam(int32_t i) const override {
        return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
    }
    void setParam(int32_t i, float v) override {
        if (i >= 0 && i < kNumParams) p_[i].store(std::clamp(v, 0.0f, 1.0f), std::memory_order_relaxed);
    }

private:
    static constexpr double kPi = 3.14159265358979323846;
    static constexpr int kDelay = 2048;   // power of two (pitch-mod ring)

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }

    // Memoryless saturation shape: 0 soft (tanh), 1 tube (asymmetric, even harmonics),
    // 2 tape (algebraic soft knee with gentle compression).
    static inline float saturate(int shape, float pre, float bias, float biasDC) {
        switch (shape) {
            case 1:  return std::tanh(pre + bias) - biasDC;          // tube: DC-corrected asymmetry
            case 2:  return (pre / (1.0f + std::fabs(pre))) * 1.2f;  // tape: soft knee
            default: return std::tanh(pre);                          // soft
        }
    }

    // Full per-channel chain after the pitch-mod tap: saturate → tone/bump/band/hp,
    // then add per-channel hiss and the shared crackle click.
    inline float channel(int c, float x, float g, float bias, float biasDC, float makeup,
                          int shape, float hissLvl, float click) {
        float y = saturate(shape, x * g, bias, biasDC) * makeup;
        y = toneSh_[c].process(y);
        y = bumpSh_[c].process(y);
        y = bandLp_[c].process(y);
        y = hp_[c].process(y);
        if (hissLvl > 0.0f) y += (whiteUni() * 2.0f - 1.0f) * hissLvl;
        return y + click;
    }

    // Linear-interpolated read `tap` samples behind the write head.
    inline float readFrac(int c, double tap) const {
        double rp = (double)dw_ - tap;
        while (rp < 0.0) rp += kDelay;
        const int i0 = (int)rp;
        const double fr = rp - i0;
        const int i1 = (i0 + 1) & (kDelay - 1);
        return (float)(dl_[c][i0 & (kDelay - 1)] * (1.0 - fr) + dl_[c][i1] * fr);
    }

    float whiteUni() { rng_ = rng_ * 1664525u + 1013904223u; return (float)((rng_ >> 8) & 0xFFFFFF) / 16777216.0f; }

    // RBJ biquad (transposed direct form II) — coeffs computed off the state thread.
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
        static double w0(double sr, double f) { f = std::clamp(f, 10.0, sr * 0.45); return 2.0 * kPi * f / sr; }
        static Coef lowpass(double sr, double f, double q) {
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * q);
            return norm((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + a, -2 * cw, 1 - a);
        }
        static Coef highpass(double sr, double f, double q) {
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * q);
            return norm((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + a, -2 * cw, 1 - a);
        }
        static Coef lowShelf(double sr, double f, double dB) {
            if (std::fabs(dB) < 1e-3) return Coef{};
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * 0.707), A = std::pow(10.0, dB / 40.0), s = 2 * std::sqrt(A) * a;
            return norm(A * ((A + 1) - (A - 1) * cw + s), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - s),
                        (A + 1) + (A - 1) * cw + s, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - s);
        }
        static Coef highShelf(double sr, double f, double dB) {
            if (std::fabs(dB) < 1e-3) return Coef{};
            double w = w0(sr, f), cw = std::cos(w), a = std::sin(w) / (2.0 * 0.707), A = std::pow(10.0, dB / 40.0), s = 2 * std::sqrt(A) * a;
            return norm(A * ((A + 1) + (A - 1) * cw + s), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - s),
                        (A + 1) - (A - 1) * cw + s, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - s);
        }
    };

    // Per-mode character: saturation shape + drive/bias, tone tilt, low head bump, the
    // band-limit and low-cut corners, and the baseline hiss / crackle / wow / flutter.
    struct ModeChar { int shape; float driveMul; float bias; float tilt; float bumpDb;
                      double bandHz; double hpHz; float noiseMul; float crackleMul; float wowMul; float flutMul; };
    static constexpr int kModes = 6;
    static constexpr ModeChar kMode[kModes] = {
        /* Vinyl    */ { 0, 3.0f, 0.05f, -0.10f, 1.5f, 13000.0, 35.0, 0.5f, 1.3f, 1.0f, 0.3f },
        /* Cassette */ { 2, 4.0f, 0.08f, -0.15f, 0.0f, 10000.0, 45.0, 1.2f, 0.2f, 0.6f, 1.0f },
        /* Reel     */ { 2, 6.0f, 0.06f,  0.00f, 3.0f, 15000.0, 30.0, 0.4f, 0.1f, 0.5f, 0.5f },
        /* VHS      */ { 0, 3.5f, 0.10f, -0.25f, 0.0f,  7000.0, 60.0, 1.0f, 0.5f, 0.8f, 1.4f },
        /* Tube     */ { 1, 8.0f, 0.18f,  0.05f, 0.0f, 16000.0, 20.0, 0.15f,0.0f, 0.0f, 0.0f },
        /* Analog   */ { 0, 5.0f, 0.03f,  0.00f, 0.0f, 18000.0, 20.0, 0.10f,0.0f, 0.0f, 0.0f },
    };

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    Biquad toneSh_[2], bumpSh_[2], bandLp_[2], hp_[2];
    float dl_[2][kDelay] = {};
    int dw_ = 0;
    double wowPh_ = 0.0, flutPh_ = 0.0;
    float clickEnv_ = 0.0f;
    uint32_t rng_ = 0x9E3779B9u;

    Oversampler os_;                       // saturation anti-aliasing
    int32_t maxFrames_ = 4096;
    std::vector<float> dry_, click_;       // base-rate stashes (sized in setSampleRate)
};

} // namespace nota
