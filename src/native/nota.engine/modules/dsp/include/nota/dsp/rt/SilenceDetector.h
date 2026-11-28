// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SilenceDetector — stateful hysteresis audio activity detector (AR-6 safe).

#pragma once

#include "nota/dsp/Export.h"
#include "nota/dsp/rt/RtAttributes.h"

#include <cstdint>

namespace nota::dsp {

enum class SignalState : uint8_t { Active = 0, PossiblySilent = 1, Silent = 2 };

class NOTA_DSP_API SilenceDetector {
  public:
    SilenceDetector() = default;
    explicit SilenceDetector(double sampleRate, double holdMs = 150.0) noexcept {
        setSampleRate(sampleRate, holdMs);
        reset(SignalState::Silent);
    }

    void setSampleRate(double sr, double holdMs = 150.0) noexcept;
    void reset(SignalState initialState = SignalState::Silent) noexcept;

    bool isSilent() const noexcept { return state_ == SignalState::Silent; }
    bool isActive() const noexcept { return state_ == SignalState::Active; }
    SignalState state() const noexcept { return state_; }
    float lastMaxPeak() const noexcept { return lastMaxPeak_; }
    bool isPeakSilent(float threshold = 1.0e-5f) const noexcept { return lastMaxPeak_ <= threshold; }

    SignalState process(const float* interleaved, int32_t frames) NOTA_RT_SAFE;

  private:
    double attackThreshold_ = 1.0e-4;
    double releaseThreshold_ = 3.0e-5;
    uint64_t holdSamples_ = 6615;
    uint64_t silentSamples_ = 6615;
    SignalState state_ = SignalState::Silent;
    float lastMaxPeak_ = 0.0f;
};

} // namespace nota::dsp
