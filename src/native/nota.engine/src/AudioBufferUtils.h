// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AudioBufferUtils — high-performance, real-time safe audio buffer inspection and utilities.

#pragma once

#include <cmath>
#include <cstdint>

namespace nota {

// Fast, early-exiting silence detection for interleaved stereo audio buffers.
// Returns true if all samples have an absolute amplitude <= threshold (default 1e-5f ≈ -100 dBFS).
inline bool isBufferSilent(const float* interleaved, int32_t frames, float threshold = 1.0e-5f) {
    if (!interleaved || frames <= 0) return true;
    const int32_t count = frames * 2;
    for (int32_t i = 0; i < count; ++i) {
        if (std::fabs(interleaved[i]) > threshold) return false;
    }
    return true;
}

enum class SignalState : uint8_t {
    Active,
    PossiblySilent,
    Silent
};

// SilenceDetector: stateful audio activity detector with dual-threshold hysteresis (Schmitt trigger)
// and sample-accurate hold time to prevent chattering around the noise floor or zero crossings.
// Real-time audio safe: 0 heap allocations, 0 locks.
struct SilenceDetector {
    double attackThreshold = 1.0e-4;   // RMS ~ -80 dBFS (wake-up threshold)
    double releaseThreshold = 3.0e-5;  // RMS ~ -90.5 dBFS (sleep entry threshold)
    uint64_t holdSamples = 6615;       // ~150 ms at 44.1 kHz default
    uint64_t silentSamples = 6615;
    SignalState state = SignalState::Silent;

    SilenceDetector() = default;
    explicit SilenceDetector(double sr, double holdMs = 150.0) {
        setSampleRate(sr, holdMs);
        reset(SignalState::Silent);
    }

    void setSampleRate(double sr, double holdMs = 150.0) {
        const double rate = sr > 0.0 ? sr : 44100.0;
        holdSamples = static_cast<uint64_t>((holdMs * 0.001) * rate);
        if (state == SignalState::Silent) silentSamples = holdSamples;
    }

    void reset(SignalState initialState = SignalState::Silent) {
        state = initialState;
        silentSamples = (initialState == SignalState::Silent) ? holdSamples : 0;
    }

    bool isSilent() const { return state == SignalState::Silent; }
    bool isActive() const { return state == SignalState::Active; }

    SignalState process(const float* interleaved, int32_t frames) {
        if (!interleaved || frames <= 0) return state;

        double sumSquares = 0.0;
        float maxPeak = 0.0f;
        const int32_t totalSamples = frames * 2;

        for (int32_t i = 0; i < totalSamples; ++i) {
            const float x = interleaved[i];
            sumSquares += static_cast<double>(x) * static_cast<double>(x);
            const float ax = std::fabs(x);
            if (ax > maxPeak) maxPeak = ax;
        }

        const double rms = std::sqrt(sumSquares / totalSamples);

        // Immediate wake-up on onset: peak or RMS exceeds attack threshold
        if (rms >= attackThreshold || static_cast<double>(maxPeak) >= attackThreshold) {
            state = SignalState::Active;
            silentSamples = 0;
        } else if (rms <= releaseThreshold && static_cast<double>(maxPeak) <= attackThreshold) {
            // Signal below release threshold: accumulate quiet samples towards hold time
            silentSamples += frames;
            state = (silentSamples >= holdSamples) ? SignalState::Silent : SignalState::PossiblySilent;
        } else {
            // Within deadband between releaseThreshold and attackThreshold:
            // Hysteresis maintains current Active state, or continues decaying if already quiet.
            if (state == SignalState::PossiblySilent) {
                silentSamples += frames;
                if (silentSamples >= holdSamples) state = SignalState::Silent;
            }
        }

        return state;
    }
};

} // namespace nota

