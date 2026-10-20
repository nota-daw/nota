// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SilenceDetector implementation — single-pass SIMD peak+RMS with hysteresis.

#include "dsp/core/SilenceDetector.h"

#include <cmath>

#include "dsp/math/SimdAudio.h"

namespace nota {
namespace dsp {

void SilenceDetector::setSampleRate(double sr, double holdMs) noexcept {
    const double rate = sr > 0.0 ? sr : 44100.0;
    holdSamples_ = static_cast<uint64_t>((holdMs * 0.001) * rate);
    if (state_ == SignalState::Silent) silentSamples_ = holdSamples_;
}

void SilenceDetector::reset(SignalState initialState) noexcept {
    state_ = initialState;
    silentSamples_ = (initialState == SignalState::Silent) ? holdSamples_ : 0;
    lastMaxPeak_ = 0.0f;
}

SignalState SilenceDetector::process(const float* interleaved, int32_t frames) NOTA_RT_SAFE {
    if (interleaved == nullptr || frames <= 0) return state_;

    float peak = 0.0f;
    double sumSq = 0.0;
    simd_audio::peakAndSumSquares(interleaved, frames * 2, peak, sumSq);
    lastMaxPeak_ = peak;

    const double total = static_cast<double>(frames * 2);
    const double rms = total > 0.0 ? std::sqrt(sumSq / total) : 0.0;

    if (rms >= attackThreshold_ || static_cast<double>(peak) >= attackThreshold_) {
        state_ = SignalState::Active;
        silentSamples_ = 0;
    } else if (rms <= releaseThreshold_ && static_cast<double>(peak) <= attackThreshold_) {
        silentSamples_ += static_cast<uint64_t>(frames);
        state_ = (silentSamples_ >= holdSamples_) ? SignalState::Silent : SignalState::PossiblySilent;
    } else {
        // Deadband between release and attack: hold Active, decay PossiblySilent.
        if (state_ == SignalState::PossiblySilent) {
            silentSamples_ += static_cast<uint64_t>(frames);
            if (silentSamples_ >= holdSamples_) state_ = SignalState::Silent;
        }
    }
    return state_;
}

} // namespace dsp
} // namespace nota
