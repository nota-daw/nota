// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Envelope — ADSR, AR, and envelope follower primitives (AR-6 safe, inline tick).

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <cmath>
#include <cstdint>

namespace nota::dsp {

class NOTA_DSP_API AdsrEnvelope {
  public:
    AdsrEnvelope() = default;

    void setSampleRate(double sr) noexcept;
    void setParams(float attack, float decay, float sustain, float release) NOTA_RT_SAFE;
    void gate(bool on) NOTA_RT_SAFE;
    void reset() noexcept;

    float tick() NOTA_RT_SAFE;
    float value() const noexcept { return value_; }
    bool active() const noexcept { return stage_ != Stage::Idle; }

  private:
    enum class Stage : uint8_t { Idle = 0, Attack = 1, Decay = 2, Sustain = 3, Release = 4 };

    double sr_ = 44100.0;
    float attack_ = 0.01f;
    float decay_ = 0.1f;
    float sustain_ = 0.8f;
    float release_ = 0.2f;
    float value_ = 0.0f;
    float attackInc_ = 0.01f;
    float decayCoef_ = 0.999f;
    float releaseCoef_ = 0.999f;
    Stage stage_ = Stage::Idle;

    void updateCoeffs() NOTA_RT_SAFE;
};

class NOTA_DSP_API ArEnvelope {
  public:
    ArEnvelope() = default;

    void setSampleRate(double sr) noexcept;
    void setParams(float attack, float release) NOTA_RT_SAFE;
    void gate(bool on) NOTA_RT_SAFE;
    void reset() noexcept;

    float tick() NOTA_RT_SAFE;
    float value() const noexcept { return value_; }
    bool active() const noexcept { return stage_ != 0; }

  private:
    double sr_ = 44100.0;
    float attack_ = 0.005f;
    float release_ = 0.1f;
    float value_ = 0.0f;
    float attackInc_ = 0.01f;
    float releaseCoef_ = 0.999f;
    int32_t stage_ = 0;
};

class NOTA_DSP_API EnvelopeFollower {
  public:
    EnvelopeFollower() = default;

    void setSampleRate(double sr) noexcept;
    void setTimes(float attack, float release) NOTA_RT_SAFE;
    void reset(float v = 0.0f) noexcept;

    float process(float x) NOTA_RT_SAFE;
    float value() const noexcept { return value_; }
    float levelDb() const NOTA_RT_SAFE;

  private:
    double sr_ = 44100.0;
    float attackCoef_ = 0.1f;
    float releaseCoef_ = 0.999f;
    float value_ = 0.0f;
};

inline float AdsrEnvelope::tick() NOTA_RT_SAFE {
    switch (stage_) {
    case Stage::Idle: value_ = 0.0f; break;
    case Stage::Attack:
        value_ += attackInc_ * (1.0f - value_);
        if (value_ >= 0.999f) {
            value_ = 1.0f;
            stage_ = Stage::Decay;
        }
        break;
    case Stage::Decay:
        value_ = sustain_ + decayCoef_ * (value_ - sustain_);
        if (std::fabs(value_ - sustain_) < 0.0005f) {
            value_ = sustain_;
            stage_ = Stage::Sustain;
        }
        break;
    case Stage::Sustain: value_ = sustain_; break;
    case Stage::Release:
        value_ *= releaseCoef_;
        if (value_ < 0.0005f) {
            value_ = 0.0f;
            stage_ = Stage::Idle;
        }
        break;
    }
    return value_;
}

inline float ArEnvelope::tick() NOTA_RT_SAFE {
    if (stage_ == 1) {
        value_ += attackInc_ * (1.0f - value_);
        if (value_ >= 0.999f) {
            value_ = 1.0f;
            stage_ = 2;
        }
    } else if (stage_ == 2) {
        value_ *= releaseCoef_;
        if (value_ < 0.0005f) {
            value_ = 0.0f;
            stage_ = 0;
        }
    } else {
        value_ = 0.0f;
    }
    return value_;
}

inline float EnvelopeFollower::process(float x) NOTA_RT_SAFE {
    const float a = x < 0.0f ? -x : x;
    if (a > value_) value_ += attackCoef_ * (a - value_);
    else
        value_ *= releaseCoef_;
    return value_;
}

} // namespace nota::dsp
