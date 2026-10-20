// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SilenceDetector — stateful audio activity detector with Schmitt-trigger
// hysteresis and sample-accurate hold time (AR-6 safe, no alloc/locks).
// Attack -80 dBFS wakes instantly; release -90.5 dBFS + 150 ms hold sleeps.

#pragma once

#include <cstdint>

#include "dsp/core/RtAttributes.h"

namespace nota {
namespace dsp {

enum class SignalState : uint8_t { Active = 0, PossiblySilent = 1, Silent = 2 };

class SilenceDetector {
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

    // Single-pass SIMD peak+RMS scan; updates hysteresis + hold counter.
    SignalState process(const float* interleaved, int32_t frames) NOTA_RT_SAFE;

  private:
    double attackThreshold_ = 1.0e-4;  // -80 dBFS wake
    double releaseThreshold_ = 3.0e-5; // -90.5 dBFS sleep
    uint64_t holdSamples_ = 6615;      // ~150 ms @ 44.1 kHz
    uint64_t silentSamples_ = 6615;
    SignalState state_ = SignalState::Silent;
    float lastMaxPeak_ = 0.0f;
};

} // namespace dsp
} // namespace nota
