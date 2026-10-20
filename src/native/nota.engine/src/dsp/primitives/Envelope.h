// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Envelope — ADSR/AR generators and envelope follower.
// All time constants are sample-rate derived in setSampleRate/gate (message or
// audio thread, allocation-free). Levels are linear; levelDb() converts with
// fast_math for detector readouts (Compressor, DynamicEq, AutoFilter).

#pragma once

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

// Linear-segment ADSR with exponential-shaped attack/decay/release curves.
// gate(true) starts Attack; gate(false) starts Release from the current level.
class AdsrEnvelope {
  public:
    AdsrEnvelope() = default;

    void setSampleRate(double sr) noexcept;
    // Times in seconds, sustain/level linear 0..1.
    void setParams(float attack, float decay, float sustain, float release) NOTA_RT_SAFE;
    void gate(bool on) NOTA_RT_SAFE;
    void reset() noexcept;

    float tick() NOTA_RT_SAFE;
    float value() const noexcept { return value_; }
    bool active() const noexcept { return stage_ != Stage::Idle; }

  private:
    enum class Stage : uint8_t { Idle = 0, Attack = 1, Decay = 2, Sustain = 3, Release = 4 };

    double sr_ = 44100.0;
    float attack_ = 0.01f, decay_ = 0.1f, sustain_ = 0.8f, release_ = 0.2f;
    float value_ = 0.0f;
    float attackInc_ = 0.01f, decayCoef_ = 0.999f, releaseCoef_ = 0.999f;
    Stage stage_ = Stage::Idle;

    void updateCoeffs() NOTA_RT_SAFE;
};

// Attack-release (percussive) envelope: gate(true) restarts the attack.
class ArEnvelope {
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
    float attack_ = 0.005f, release_ = 0.1f;
    float value_ = 0.0f;
    float attackInc_ = 0.01f, releaseCoef_ = 0.999f;
    int stage_ = 0; // 0 = idle, 1 = attack, 2 = release
};

// Peak follower with independent attack/release ballistics. process() takes
// the absolute signal; levelDb() reads the detector in dB for gain computers.
class EnvelopeFollower {
  public:
    EnvelopeFollower() = default;

    void setSampleRate(double sr) noexcept;
    // Times in seconds (attack ~0.001–0.05, release ~0.05–1.0).
    void setTimes(float attack, float release) NOTA_RT_SAFE;
    void reset(float v = 0.0f) noexcept;

    float process(float x) NOTA_RT_SAFE;
    float value() const noexcept { return value_; }
    float levelDb() const NOTA_RT_SAFE;

  private:
    double sr_ = 44100.0;
    float attackCoef_ = 0.1f, releaseCoef_ = 0.999f;
    float value_ = 0.0f;
};

} // namespace dsp
} // namespace nota
