// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/Export.h"
#include "nota/media/SampleBuffer.h"

#include <array>
#include <cstdint>

namespace nota::media {

inline constexpr int kTimbreDims = 16;

struct SampleFeatures {
    double bpm = 0.0;
    int32_t keyTonic = -1;
    int32_t keyMode = -1;
    float keyConfidence = 0.0f;
    float tailRatio = 0.0f;
    float peakDb = -120.0f;
    float rmsDb = -120.0f;
    std::array<float, kTimbreDims> timbre{};
};

NOTA_MEDIA_API SampleFeatures analyzeSample(const SampleBuffer& src, int64_t frames);

NOTA_MEDIA_API void detectKey(const SampleBuffer& src, int64_t offset, int64_t len, double sampleRate, int32_t& tonic,
    int32_t& mode, float& confidence);

} // namespace nota::media
