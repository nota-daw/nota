// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Offline analysis of a decoded sample for the browser's sample index: tempo (via
// TempoDetect), musical key (chroma against the Krumhansl–Kessler key profiles), a short
// timbre fingerprint for "similar sounds", and the envelope facts that tell a loop from a
// one-shot. Worker-thread only; allocates freely, touches no engine state.

#pragma once

#include "SampleBuffer.h"
#include <array>
#include <cstdint>

namespace nota {

inline constexpr int kTimbreDims = 16;

struct SampleFeatures {
    double bpm = 0.0;            // 0 = no steady pulse
    int32_t keyTonic = -1;       // -1 = atonal (drums, noise), else 0..11 (C..B)
    int32_t keyMode = -1;        // 0 major, 1 minor, -1 tonic only (mode unclear)
    float keyConfidence = 0.0f;  // 0..1 (profile correlation of the winning key)
    float tailRatio = 0.0f;      // RMS of the last tenth over the whole RMS: ~1 loops, ~0 hits
    float peakDb = -120.0f;
    float rmsDb = -120.0f;
    std::array<float, kTimbreDims> timbre{};   // see SampleAnalysis.cpp for the layout
};

// Analyses the first `frames` frames of `src` (the decoded prefix; ≤ src.frames).
SampleFeatures analyzeSample(const SampleBuffer& src, int64_t frames);

// Key estimate only: tonic −1..11, mode −1/0/1, confidence 0..1.
void detectKey(const SampleBuffer& src, int64_t offset, int64_t len, double sampleRate,
               int32_t& tonic, int32_t& mode, float& confidence);

} // namespace nota
