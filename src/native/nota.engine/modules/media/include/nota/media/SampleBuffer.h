// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/media/AudioSource.h"
#include "nota/media/Export.h"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <vector>

namespace nota::media {

struct NOTA_MEDIA_API SampleBuffer {
    std::vector<float> samples; // interleaved, size == frames * channels (empty when disk-streamed)
    std::shared_ptr<AudioSource> source;
    int32_t channels = 0;
    int64_t frames = 0;
    double sourceSampleRate = 0.0;
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

    static void computeBlockPeakAndMeanSq(const float* interleaved, int32_t channels, int64_t frameCount, float& outMin,
        float& outMax, float& outMeanSq) noexcept;

    float at(int64_t frame, int32_t ch) const {
        if (frame < 0 || frame >= frames || ch < 0 || ch >= channels) return 0.0f;
        if (!samples.empty())
            return samples[static_cast<size_t>(frame) * static_cast<size_t>(channels) + static_cast<size_t>(ch)];
        return source ? source->at(frame, ch) : 0.0f;
    }

    void readStereo(int64_t frame, float& l, float& r) const noexcept {
        if (frame < 0 || frame >= frames || channels <= 0) {
            l = 0.0f;
            r = 0.0f;
            return;
        }
        if (!samples.empty()) [[likely]] {
            const float* p = &samples[static_cast<size_t>(frame) * static_cast<size_t>(channels)];
            l = p[0];
            r = channels == 1 ? l : p[1];
            return;
        }
        if (source) {
            source->readStereo(frame, l, r);
            return;
        }
        l = 0.0f;
        r = 0.0f;
    }

    void readStereoFast(int64_t frame, int32_t& slotHint, bool allowSyncFill, float& l, float& r) const noexcept {
        if (frame < 0 || frame >= frames || channels <= 0) {
            l = 0.0f;
            r = 0.0f;
            return;
        }
        if (!samples.empty()) [[likely]] {
            const float* p = &samples[static_cast<size_t>(frame) * static_cast<size_t>(channels)];
            l = p[0];
            r = channels == 1 ? l : p[1];
            return;
        }
        if (source) {
            source->readStereoHinted(frame, slotHint, allowSyncFill, l, r);
            return;
        }
        l = 0.0f;
        r = 0.0f;
    }

    int64_t readAllInterleaved(float* out, int64_t capFloats) const;

    static constexpr int64_t kPeakBlock = 512;
    std::vector<float> peakTable;
    std::vector<float> rmsTable;

    int64_t peakBlocks() const { return (frames + kPeakBlock - 1) / kPeakBlock; }
    bool hasPeakTable() const { return static_cast<int64_t>(peakTable.size()) == peakBlocks() * 2; }
    bool hasRmsTable() const { return static_cast<int64_t>(rmsTable.size()) == peakBlocks(); }

    void buildPeakTable();
    void updatePeakTable(int64_t f0, int64_t f1);
    void fillSentinel(int64_t b0, int64_t b1);
    void peakRange(int64_t f0, int64_t f1, float& mn, float& mx) const;
    float rmsRange(int64_t f0, int64_t f1) const;

  private:
    void scanRange(int64_t f0, int64_t f1, float& mn, float& mx) const;
    double scanSquares(int64_t f0, int64_t f1) const;
};

} // namespace nota::media
