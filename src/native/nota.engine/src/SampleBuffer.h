// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Decoded audio held in RAM (M1). Interleaved 32-bit float. Long files will be
// streamed from disk in a later pass (M1-9); the clip API stays the same.

#pragma once

#include <atomic>
#include <cstdint>
#include <vector>

namespace nota {

struct SampleBuffer {
    std::vector<float> samples;   // interleaved, size == frames * channels
    int32_t  channels = 0;
    int64_t  frames = 0;
    double   sourceSampleRate = 0.0;
    // Process-unique handle (M7-6b), used by project save to dedup shared
    // buffers and name their file in `samples/`. Not persisted — a reload mints
    // fresh buffers with fresh ids.
    int64_t  id = nextId();

    bool empty() const { return frames == 0 || channels == 0; }

    static int64_t nextId() {
        static std::atomic<int64_t> counter{1};
        return counter.fetch_add(1, std::memory_order_relaxed);
    }

    // One sample at (frame, channel); 0 out of range. For per-channel processing.
    float at(int64_t frame, int ch) const {
        if (frame < 0 || frame >= frames || ch < 0 || ch >= channels) return 0.0f;
        return samples[frame * channels + ch];
    }

    // Read one frame at integer position, up-mixing/down-mixing to stereo.
    // Out-of-range positions yield silence.
    void readStereo(int64_t frame, float& l, float& r) const {
        if (frame < 0 || frame >= frames) { l = r = 0.0f; return; }
        const float* p = &samples[frame * channels];
        if (channels == 1) { l = r = p[0]; }
        else               { l = p[0]; r = p[1]; }
    }
};

} // namespace nota
