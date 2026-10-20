// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Oscillator implementation.

#include "dsp/primitives/Oscillator.h"

#include "dsp/math/FastMath.h"

namespace nota {
namespace dsp {

void BleOscillator::setSampleRate(double sr) noexcept {
    sr_ = sr > 0.0 ? sr : 44100.0;
}

void BleOscillator::setParams(float freq, int wave, float pulseWidth) NOTA_RT_SAFE {
    const float nyq = static_cast<float>(sr_ * 0.5 * 0.999);
    freq_ = freq < 0.1f ? 0.1f : (freq > nyq ? nyq : freq);
    wave_ = wave < 0 ? 0 : (wave > 3 ? 3 : wave);
    pw_ = pulseWidth < 0.02f ? 0.02f : (pulseWidth > 0.98f ? 0.98f : pulseWidth);
}

void BleOscillator::reset(float phase) noexcept {
    phase_ = phase - static_cast<float>(static_cast<int>(phase));
    if (phase_ < 0.0f) phase_ += 1.0f;
}

float BleOscillator::polyBlep(float t, float dt) NOTA_RT_SAFE {
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

float BleOscillator::tick() NOTA_RT_SAFE {
    const float dt = freq_ / static_cast<float>(sr_);
    float v = 0.0f;
    switch (wave_) {
    case 0: { // saw: 2*phase - 1 with BLEP at wrap
        v = 2.0f * phase_ - 1.0f;
        v -= polyBlep(phase_, dt);
        break;
    }
    case 1: { // square/pulse with BLEP at both edges
        v = phase_ < pw_ ? 1.0f : -1.0f;
        v += polyBlep(phase_, dt);
        float t = phase_ - pw_;
        if (t < 0.0f) t += 1.0f;
        v -= polyBlep(t, dt);
        break;
    }
    case 2: { // triangle: naive (matches Synth.h — the repo's proven shape)
        const float d = phase_ < 0.5f ? phase_ : 1.0f - phase_;
        v = 4.0f * d - 1.0f;
        break;
    }
    default: v = fast_math::sin(phase_); break; // sine
    }
    phase_ += dt;
    if (phase_ >= 1.0f) phase_ -= 1.0f;
    return v;
}

void LfoGenerator::setSampleRate(double sr) noexcept {
    sr_ = sr > 0.0 ? sr : 44100.0;
}

void LfoGenerator::setRateHz(float hz) NOTA_RT_SAFE {
    hz = hz < 0.01f ? 0.01f : (hz > static_cast<float>(sr_ * 0.5) ? static_cast<float>(sr_ * 0.5) : hz);
    inc_ = hz / static_cast<float>(sr_);
}

void LfoGenerator::setTempoSync(float bpm, float beatsPerCycle) NOTA_RT_SAFE {
    if (bpm <= 0.0f) bpm = 120.0f;
    if (beatsPerCycle <= 0.0f) beatsPerCycle = 1.0f;
    setRateHz(bpm / 60.0f / beatsPerCycle);
}

void LfoGenerator::reset(float phase) noexcept {
    phase_ = phase - static_cast<float>(static_cast<int>(phase));
    if (phase_ < 0.0f) phase_ += 1.0f;
}

float LfoGenerator::tick() NOTA_RT_SAFE {
    float v = 0.0f;
    switch (wave_) {
    case LfoWave::Sine: v = fast_math::sin(phase_); break;
    case LfoWave::Triangle: v = phase_ < 0.5f ? phase_ * 4.0f - 1.0f : 3.0f - phase_ * 4.0f; break;
    case LfoWave::Saw: v = phase_ * 2.0f - 1.0f; break;
    case LfoWave::Square: v = phase_ < 0.5f ? 1.0f : -1.0f; break;
    case LfoWave::SampleHold: {
        if (phase_ < inc_) {
            holdSeed_ ^= holdSeed_ << 13;
            holdSeed_ ^= holdSeed_ >> 17;
            holdSeed_ ^= holdSeed_ << 5;
            holdValue_ = static_cast<float>(holdSeed_ >> 8) * (1.0f / 16777216.0f) * 2.0f - 1.0f;
        }
        v = holdValue_;
        break;
    }
    }
    phase_ += inc_;
    if (phase_ >= 1.0f) phase_ -= 1.0f;
    return v;
}

} // namespace dsp
} // namespace nota
