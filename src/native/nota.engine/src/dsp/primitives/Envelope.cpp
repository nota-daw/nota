// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Envelope implementation.

#include "dsp/primitives/Envelope.h"

#include <cmath>

#include "dsp/math/FastMath.h"

namespace nota {
namespace dsp {

void AdsrEnvelope::setSampleRate(double sr) noexcept {
    sr_ = sr > 0.0 ? sr : 44100.0;
    updateCoeffs();
}

void AdsrEnvelope::setParams(float attack, float decay, float sustain, float release) NOTA_RT_SAFE {
    attack_ = attack < 0.0001f ? 0.0001f : attack;
    decay_ = decay < 0.001f ? 0.001f : decay;
    sustain_ = sustain < 0.0f ? 0.0f : (sustain > 1.0f ? 1.0f : sustain);
    release_ = release < 0.001f ? 0.001f : release;
    updateCoeffs();
}

void AdsrEnvelope::updateCoeffs() NOTA_RT_SAFE {
    attackInc_ = 1.0f / (attack_ * static_cast<float>(sr_));
    // One-pole targets: decay heads to sustain, release to zero.
    decayCoef_ = std::exp(-1.0f / (decay_ * static_cast<float>(sr_)));
    releaseCoef_ = std::exp(-1.0f / (release_ * static_cast<float>(sr_)));
}

void AdsrEnvelope::gate(bool on) NOTA_RT_SAFE {
    stage_ = on ? Stage::Attack : (stage_ == Stage::Idle ? Stage::Idle : Stage::Release);
}

void AdsrEnvelope::reset() noexcept {
    value_ = 0.0f;
    stage_ = Stage::Idle;
}

void ArEnvelope::setSampleRate(double sr) noexcept {
    sr_ = sr > 0.0 ? sr : 44100.0;
}

void ArEnvelope::setParams(float attack, float release) NOTA_RT_SAFE {
    attack_ = attack < 0.0001f ? 0.0001f : attack;
    release_ = release < 0.001f ? 0.001f : release;
    attackInc_ = 1.0f / (attack_ * static_cast<float>(sr_));
    releaseCoef_ = std::exp(-1.0f / (release_ * static_cast<float>(sr_)));
}

void ArEnvelope::gate(bool on) NOTA_RT_SAFE {
    stage_ = on ? 1 : (stage_ == 0 ? 0 : 2);
}

void ArEnvelope::reset() noexcept {
    value_ = 0.0f;
    stage_ = 0;
}

void EnvelopeFollower::setSampleRate(double sr) noexcept {
    sr_ = sr > 0.0 ? sr : 44100.0;
}

void EnvelopeFollower::setTimes(float attack, float release) NOTA_RT_SAFE {
    attack = attack < 0.00005f ? 0.00005f : attack;
    release = release < 0.001f ? 0.001f : release;
    attackCoef_ = 1.0f - std::exp(-1.0f / (attack * static_cast<float>(sr_)));
    releaseCoef_ = std::exp(-1.0f / (release * static_cast<float>(sr_)));
}

void EnvelopeFollower::reset(float v) noexcept {
    value_ = v;
}

float EnvelopeFollower::levelDb() const NOTA_RT_SAFE {
    return fast_math::linToDb(value_);
}

} // namespace dsp
} // namespace nota
