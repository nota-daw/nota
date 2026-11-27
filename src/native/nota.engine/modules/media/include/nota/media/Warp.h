// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/Export.h"
#include "nota/media/SampleBuffer.h"

#include <cstdint>
#include <memory>
#include <vector>

namespace nota::media {

enum class WarpMode : int32_t {
    Beats = 0,
    Tones = 1,
    Texture = 2,
    Complex = 3,
    ComplexPro = 4,
    RePitch = 5,
};

struct WarpMarker {
    double srcFrame = 0.0;
    double beat = 0.0;
    bool operator==(const WarpMarker&) const = default;
};

struct WarpInputs {
    const SampleBuffer* sample = nullptr;
    std::vector<WarpMarker> markers;
    double warpBeats = 0.0;
    double playStart = 0.0;
    double playEnd = 0.0;
    WarpMode mode = WarpMode::Complex;
    float pitch = 0.0f;
    double spb = 0.0;
    double devSR = 0.0;
    bool operator==(const WarpInputs&) const = default;
};

struct WarpCache {
    std::vector<float> samples;
    int64_t frames = 0;
    double spb = 0.0;
    double devSR = 0.0;
    WarpInputs inputs;
    std::shared_ptr<const WarpCache> base;
    int64_t baseOffset = 0;

    const float* data() const { return base ? base->samples.data() + baseOffset * 2 : samples.data(); }
};

class NOTA_MEDIA_API ClipWarpStream {
  public:
    ClipWarpStream();
    ~ClipWarpStream();
    ClipWarpStream(const ClipWarpStream&) = delete;
    ClipWarpStream& operator=(const ClipWarpStream&) = delete;

    void configure(
        int channels, WarpMode mode, float pitchSemitones, double srcSR, double devSR, bool reverseSource = false);
    bool ready() const;

    void render(float* out, int frames, double outStartSamples, const std::vector<WarpMarker>& markers,
        double warpBeats, const SampleBuffer& src, double spb, double devSR, float gain);

  private:
    struct Impl;
    std::unique_ptr<Impl> d_;
};

} // namespace nota::media
