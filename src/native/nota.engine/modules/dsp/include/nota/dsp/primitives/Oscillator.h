// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Oscillator — band-limited polyBLEP oscillator & tempo-synced LFOs (AR-6 safe).

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "nota/math/FastMath.h"

#include <cstdint>

namespace nota::dsp {

class NOTA_DSP_API BleOscillator {
  public:
    BleOscillator() = default;

    void setSampleRate(double sr) noexcept;
    void setParams(float freq, int32_t wave, float pulseWidth = 0.5f) NOTA_RT_SAFE;
    void reset(float phase = 0.0f) noexcept;

    float tick() NOTA_RT_SAFE;
    float phase() const noexcept { return phase_; }

  private:
    static float polyBlep(float t, float dt) NOTA_RT_SAFE;

    double sr_ = 44100.0;
    float freq_ = 440.0f;
    int32_t wave_ = 0;
    float pw_ = 0.5f;
    float phase_ = 0.0f;
};

enum class LfoWave : uint8_t { Sine = 0, Triangle = 1, Saw = 2, Square = 3, SampleHold = 4 };

class NOTA_DSP_API LfoGenerator {
  public:
    LfoGenerator() = default;

    void setSampleRate(double sr) noexcept;
    void setWave(LfoWave w) noexcept { wave_ = w; }
    void setRateHz(float hz) NOTA_RT_SAFE;
    void setTempoSync(float bpm, float beatsPerCycle) NOTA_RT_SAFE;
    void reset(float phase = 0.0f) noexcept;

    float tick() NOTA_RT_SAFE;

  private:
    double sr_ = 44100.0;
    LfoWave wave_ = LfoWave::Sine;
    float phase_ = 0.0f;
    float inc_ = 0.01f;
    uint32_t holdSeed_ = 0x9E3779B9u;
    float holdValue_ = 0.0f;
};

inline float BleOscillator::polyBlep(float t, float dt) NOTA_RT_SAFE {
    if (t < dt) {
        t /= dt;
        return t + t - t * t - 1.0f;
    }
    if (t > 1.0f - dt) {
        t = (t - 1.0f) / dt;
        return t * t + t + t + 1.0f;
    }
    return 0.0f;
}

inline float BleOscillator::tick() NOTA_RT_SAFE {
    const float dt = freq_ / static_cast<float>(sr_);
    float v = 0.0f;
    switch (wave_) {
    case 0:
        v = 2.0f * phase_ - 1.0f;
        v -= polyBlep(phase_, dt);
        break;
    case 1: {
        v = phase_ < pw_ ? 1.0f : -1.0f;
        v += polyBlep(phase_, dt);
        float t = phase_ - pw_;
        if (t < 0.0f) t += 1.0f;
        v -= polyBlep(t, dt);
        break;
    }
    case 2: {
        const float d = phase_ < 0.5f ? phase_ : 1.0f - phase_;
        v = 4.0f * d - 1.0f;
        break;
    }
    default: v = math::sinPhase(phase_); break;
    }
    phase_ += dt;
    if (phase_ >= 1.0f) phase_ -= 1.0f;
    return v;
}

inline float LfoGenerator::tick() NOTA_RT_SAFE {
    float v = 0.0f;
    switch (wave_) {
    case LfoWave::Sine: v = math::sinPhase(phase_); break;
    case LfoWave::Triangle: v = phase_ < 0.5f ? phase_ * 4.0f - 1.0f : 3.0f - phase_ * 4.0f; break;
    case LfoWave::Saw: v = phase_ * 2.0f - 1.0f; break;
    case LfoWave::Square: v = phase_ < 0.5f ? 1.0f : -1.0f; break;
    case LfoWave::SampleHold:
        if (phase_ < inc_) {
            holdSeed_ ^= holdSeed_ << 13u;
            holdSeed_ ^= holdSeed_ >> 17u;
            holdSeed_ ^= holdSeed_ << 5u;
            holdValue_ = static_cast<float>(holdSeed_ >> 8u) * (1.0f / 16777216.0f) * 2.0f - 1.0f;
        }
        v = holdValue_;
        break;
    }
    phase_ += inc_;
    if (phase_ >= 1.0f) phase_ -= 1.0f;
    return v;
}

} // namespace nota::dsp
