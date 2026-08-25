// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Built-in Nota Auto Pan (device kind 9) — a classic LFO-panner-style modulation
// insert: one LFO drives per-channel amplitude, and a Phase offset between the left
// and right LFOs sweeps continuously from tremolo (0°, both channels dip together)
// to auto-pan (180°, one channel loud while the other is quiet). Waveform selectable
// (Sine / Triangle / Saw / Square / S&H), a Shape control sharpens the smooth waves
// toward square, Amount sets depth, Mix blends dry/wet.
//
// The live stereo position is published via gainReductionDb() (a free per-device
// scalar the UI reads lock-free) so the editor's viz dot tracks the pan in real time.
// Header-only, allocation-free, JUCE-free. Params normalized 0..1 → persist / clone /
// automation flow generically through the base Device.

#pragma once

#include "Device.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class AutoPan : public Device {
public:
    // Param layout — order is the persisted/automation layout. APPEND ONLY.
    enum { Rate = 0, Amount, Waveform, Shape, Phase, Mix, kNumParams };

    AutoPan() {
        p_[Rate].store(0.60f);      // ~1.5 Hz
        p_[Amount].store(0.70f);
        p_[Waveform].store(0.0f);   // Sine
        p_[Shape].store(0.0f);      // smooth
        p_[Phase].store(0.5f);      // 180° → classic auto-pan
        p_[Mix].store(1.0f);
    }

    void setSampleRate(double sr, int32_t /*maxBlock*/) override {
        sr_ = sr > 0 ? sr : 44100.0;
        phase_ = 0.0; shHeldL_ = shHeldR_ = 0.0f; lastCycle_ = -1;
    }

    // Live stereo position (0 = hard left, 0.5 = centre, 1 = hard right) for the UI dot.
    float gainReductionDb() const override { return panPos_.load(std::memory_order_relaxed); }

    void process(float* buf, int32_t frames) override {
        const double rateHz = expMap(get(Rate), 0.01, 40.0);
        const float  amount = std::clamp(get(Amount), 0.0f, 1.0f);
        const int    wave   = std::clamp((int)std::lround(get(Waveform) * 4.0f), 0, 4);
        const float  shape  = std::clamp(get(Shape), 0.0f, 1.0f);
        const double phOff  = std::clamp(get(Phase), 0.0f, 1.0f);   // 0..1 cycle → 0..360°
        const float  mix    = std::clamp(get(Mix), 0.0f, 1.0f);
        const double inc    = rateHz / sr_;

        for (int32_t i = 0; i < frames; ++i) {
            // Sample & hold refreshes once per LFO cycle (independent L/R draws).
            const int cyc = (int)phase_;
            if (wave == 4 && cyc != lastCycle_) { shHeldL_ = whiteBip(); shHeldR_ = whiteBip(); lastCycle_ = cyc; }

            const double phL = frac(phase_);
            const double phR = frac(phase_ + phOff);
            const float lfoL = lfo(wave, phL, shape, shHeldL_);
            const float lfoR = lfo(wave, phR, shape, shHeldR_);

            // Amplitude gain per channel: lfo=+1 → full, lfo=-1 → (1 - amount).
            const float gL = 1.0f - amount * 0.5f * (1.0f - lfoL);
            const float gR = 1.0f - amount * 0.5f * (1.0f - lfoR);

            const float dryL = buf[i * 2], dryR = buf[i * 2 + 1];
            buf[i * 2]     = dryL * (1.0f - mix) + dryL * gL * mix;
            buf[i * 2 + 1] = dryR * (1.0f - mix) + dryR * gR * mix;

            phase_ += inc; if (phase_ >= 1.0e7) phase_ = frac(phase_);   // keep the cycle counter bounded
            panPos_.store(0.5f + 0.5f * (gR - gL), std::memory_order_relaxed);
        }
    }

    const char* displayName() const override { return "Nota Orbit"; }
    int32_t     builtinKind() const override { return 9; }

    int32_t     paramCount() const override { return kNumParams; }
    const char* paramName(int32_t i) const override {
        switch (i) {
            case Rate: return "Rate"; case Amount: return "Amount"; case Waveform: return "Waveform";
            case Shape: return "Shape"; case Phase: return "Phase"; case Mix: return "Mix"; default: return "";
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

    float get(int i) const { return p_[i].load(std::memory_order_relaxed); }
    static double expMap(float v, double lo, double hi) { return lo * std::pow(hi / lo, std::clamp((double)v, 0.0, 1.0)); }
    static double frac(double x) { return x - std::floor(x); }
    float whiteBip() { rng_ = rng_ * 1664525u + 1013904223u; return (float)((rng_ >> 8) & 0xFFFFFF) / 8388608.0f - 1.0f; }

    // LFO value in [-1,1] at cycle phase `ph` (0..1). Shape sharpens the smooth waves
    // toward a square; Square/S&H ignore it (S&H uses the pre-drawn held value).
    static float lfo(int wave, double ph, float shape, float held) {
        double v;
        switch (wave) {
            case 1:  v = 4.0 * std::fabs(ph - 0.5) - 1.0; break;      // triangle
            case 2:  v = 2.0 * ph - 1.0; break;                       // saw
            case 3:  return ph < 0.5 ? 1.0f : -1.0f;                  // square
            case 4:  return held;                                     // sample & hold
            default: v = std::sin(2.0 * kPi * ph); break;             // sine
        }
        if (shape > 1.0e-3f) {
            const double k = 1.0 + shape * 12.0;
            const double sharp = std::tanh(k * v) / std::tanh(k);
            v = v * (1.0 - shape) + sharp * shape;
        }
        return (float)v;
    }

    double sr_ = 44100.0;
    std::atomic<float> p_[kNumParams] = {};
    double phase_ = 0.0;
    int lastCycle_ = -1;
    float shHeldL_ = 0.0f, shHeldR_ = 0.0f;
    uint32_t rng_ = 0x53A9C1u;
    std::atomic<float> panPos_{0.5f};
};

} // namespace nota
