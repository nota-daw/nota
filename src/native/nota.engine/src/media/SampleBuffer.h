// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Decoded audio held in RAM (M1). Interleaved 32-bit float. Long files will be
// streamed from disk in a later pass (M1-9); the clip API stays the same.

#pragma once

#include "AudioSource.h"
#include <algorithm>
#include <atomic>
#include <cstdint>
#include <memory>
#include <vector>

namespace nota {

struct SampleBuffer {
    std::vector<float> samples; // interleaved, size == frames * channels (empty when disk-streamed)
    std::shared_ptr<AudioSource> source; // non-null when backed by StreamingAudioSource (or explicit source)
    int32_t channels = 0;
    int64_t frames = 0;
    double sourceSampleRate = 0.0;
    // Process-unique handle (M7-6b), used by project save to dedup shared
    // buffers and name their file in `samples/`. Not persisted — a reload mints
    // fresh buffers with fresh ids.
    int64_t id = nextId();

    bool empty() const { return frames <= 0 || channels <= 0 || (samples.empty() && !source); }
    bool isStreaming() const noexcept { return source && source->isStreaming(); }
    int64_t memoryBytes() const noexcept {
        int64_t b = static_cast<int64_t>(samples.capacity() * sizeof(float));
        if (source) b += source->memoryBytes();
        b += static_cast<int64_t>(peakTable.capacity() * sizeof(float));
        b += static_cast<int64_t>(rmsTable.capacity() * sizeof(float));
        return b;
    }

    static int64_t nextId();

    // One sample at (frame, channel); 0 out of range. For per-channel processing.
    float at(int64_t frame, int ch) const {
        if (frame < 0 || frame >= frames || ch < 0 || ch >= channels) return 0.0f;
        if (!samples.empty()) return samples[static_cast<size_t>(frame) * static_cast<size_t>(channels) + ch];
        return source ? source->at(frame, ch) : 0.0f;
    }

    // Read one frame at integer position, up-mixing/down-mixing to stereo.
    // Out-of-range positions yield silence.
    void readStereo(int64_t frame, float& l, float& r) const noexcept {
        if (frame < 0 || frame >= frames || channels <= 0) {
            l = 0.0f;
            r = 0.0f;
            return;
        }
        if (!samples.empty()) {
            const float* p = &samples[static_cast<size_t>(frame) * static_cast<size_t>(channels)];
            if (channels == 1) {
                l = r = p[0];
            } else {
                l = p[0];
                r = p[1];
            }
            return;
        }
        readStereoFromSource(frame, l, r);
    }

    void readStereoFromSource(int64_t frame, float& l, float& r) const;

    // Hot-path stereo read: inline memory indexing when resident; wait-free hinted lookup when streamed.
    void readStereoFast(
        int64_t frame, int32_t& slotHint, bool allowSyncFill, float& l, float& r) const noexcept {
        if (frame < 0 || frame >= frames || channels <= 0) {
            l = 0.0f;
            r = 0.0f;
            return;
        }
        if (!samples.empty()) {
            const float* p = &samples[static_cast<size_t>(frame) * static_cast<size_t>(channels)];
            if (channels == 1) {
                l = r = p[0];
            } else {
                l = p[0];
                r = p[1];
            }
            return;
        }
        if (source) {
            source->readStereoHinted(frame, slotHint, allowSyncFill, l, r);
            return;
        }
        l = 0.0f;
        r = 0.0f;
    }

    // Copies up to `capFloats` interleaved f32 samples (blocking full decode when streamed).
    int64_t readAllInterleaved(float* out, int64_t capFloats) const;

    // --- waveform overview ---------------------------------------------------
    // Min/max of the stereo mid per kPeakBlock frames, interleaved [min0,max0,min1,max1,…].
    // Built when a file is decoded so waveform queries over long spans read blocks instead
    // of every frame (a 10-minute file is ~29M frames). Empty = not built; peakRange then
    // scans frames. Blocks not computed yet hold the (1,-1) "no data" sentinel.
    static constexpr int64_t kPeakBlock = 512;
    std::vector<float> peakTable;
    // Mean square of the stereo mid per block, alongside peakTable, for the waveform's RMS
    // body. Not part of the peak cache: a seeded table has none until the decode finishes.
    std::vector<float> rmsTable;

    int64_t peakBlocks() const { return (frames + kPeakBlock - 1) / kPeakBlock; }
    bool hasPeakTable() const { return static_cast<int64_t>(peakTable.size()) == peakBlocks() * 2; }
    bool hasRmsTable() const { return static_cast<int64_t>(rmsTable.size()) == peakBlocks(); }

    void buildPeakTable();

    // (Re)compute the blocks overlapping frames [f0, f1). Used incrementally while a file
    // decodes block by block; a block straddling f0 is recomputed whole.
    void updatePeakTable(int64_t f0, int64_t f1);

    void fillSentinel(int64_t b0, int64_t b1);

    // Min/max of the stereo mid over frames [f0, f1): whole table blocks in the middle,
    // exact frame scans for the partial blocks at either end. (0,0) for an empty range.
    void peakRange(int64_t f0, int64_t f1, float& mn, float& mx) const;

    // RMS of the stereo mid over frames [f0, f1), read the same way. 0 for an empty range.
    float rmsRange(int64_t f0, int64_t f1) const;

  private:
    void scanRange(int64_t f0, int64_t f1, float& mn, float& mx) const;
    double scanSquares(int64_t f0, int64_t f1) const;
};

} // namespace nota
