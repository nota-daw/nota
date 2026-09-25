// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Decoded audio held in RAM (M1). Interleaved 32-bit float. Long files will be
// streamed from disk in a later pass (M1-9); the clip API stays the same.

#pragma once

#include <algorithm>
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

    // --- waveform overview ---------------------------------------------------
    // Min/max of the stereo mid per kPeakBlock frames, interleaved [min0,max0,min1,max1,…].
    // Built when a file is decoded so waveform queries over long spans read blocks instead
    // of every frame (a 10-minute file is ~29M frames). Empty = not built; peakRange then
    // scans frames. Blocks not computed yet hold the (1,-1) "no data" sentinel.
    static constexpr int64_t kPeakBlock = 512;
    std::vector<float> peakTable;

    int64_t peakBlocks() const { return (frames + kPeakBlock - 1) / kPeakBlock; }
    bool hasPeakTable() const { return static_cast<int64_t>(peakTable.size()) == peakBlocks() * 2; }

    void buildPeakTable() { updatePeakTable(0, frames); }

    // (Re)compute the blocks overlapping frames [f0, f1). Used incrementally while a file
    // decodes block by block; a block straddling f0 is recomputed whole.
    void updatePeakTable(int64_t f0, int64_t f1) {
        if (!hasPeakTable()) {
            peakTable.assign(static_cast<size_t>(peakBlocks() * 2), 0.0f);
            fillSentinel(0, peakBlocks());
        }
        f0 = std::max<int64_t>(0, f0); f1 = std::min(f1, frames);
        if (f1 <= f0) return;
        for (int64_t b = f0 / kPeakBlock; b <= (f1 - 1) / kPeakBlock; ++b) {
            float mn, mx;
            scanRange(b * kPeakBlock, std::min(frames, (b + 1) * kPeakBlock), mn, mx);
            peakTable[b * 2] = mn; peakTable[b * 2 + 1] = mx;
        }
    }

    void fillSentinel(int64_t b0, int64_t b1) {
        for (int64_t b = b0; b < b1; ++b) { peakTable[b * 2] = 1.0f; peakTable[b * 2 + 1] = -1.0f; }
    }

    // Min/max of the stereo mid over frames [f0, f1): whole table blocks in the middle,
    // exact frame scans for the partial blocks at either end. (0,0) for an empty range.
    void peakRange(int64_t f0, int64_t f1, float& mn, float& mx) const {
        f0 = std::max<int64_t>(0, f0); f1 = std::min(f1, frames);
        mn = 1.0f; mx = -1.0f;
        if (f1 <= f0) { mn = mx = 0.0f; return; }
        if (f1 - f0 < 4 * kPeakBlock || !hasPeakTable()) { scanRange(f0, f1, mn, mx); return; }
        const int64_t b0 = (f0 + kPeakBlock - 1) / kPeakBlock, b1 = f1 / kPeakBlock;   // whole blocks
        float a, z;
        scanRange(f0, b0 * kPeakBlock, a, z); mn = std::min(mn, a); mx = std::max(mx, z);
        for (int64_t b = b0; b < b1; ++b) { mn = std::min(mn, peakTable[b * 2]); mx = std::max(mx, peakTable[b * 2 + 1]); }
        scanRange(b1 * kPeakBlock, f1, a, z); mn = std::min(mn, a); mx = std::max(mx, z);
        if (mn > mx) mn = mx = 0.0f;
    }

private:
    void scanRange(int64_t f0, int64_t f1, float& mn, float& mx) const {
        mn = 1.0f; mx = -1.0f;
        for (int64_t f = f0; f < f1; ++f) {
            float l, r; readStereo(f, l, r);
            const float mid = 0.5f * (l + r);
            mn = std::min(mn, mid); mx = std::max(mx, mid);
        }
    }
};

} // namespace nota
