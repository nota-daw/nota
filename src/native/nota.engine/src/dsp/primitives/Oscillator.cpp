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

} // namespace dsp
} // namespace nota
