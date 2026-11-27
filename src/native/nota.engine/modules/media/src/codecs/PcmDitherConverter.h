// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include <nota/math/Random.h>

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace nota::media::detail {

// Shared float32 -> integer PCM quantization with ±1-LSB TPDF dither for 16-bit targets
// and asymmetric 2^23 - 1 scaling for 24-bit lossless targets (parity with WavWriter).
class PcmDitherConverter {
  public:
    void reset(int32_t bitsPerSample, bool enableDither, uint32_t seed = 0x6d2b79f5u) noexcept {
        bits_ = (bitsPerSample == 16) ? 16 : 24;
        dither_ = enableDither && (bits_ == 16);
        scale_ = (bits_ == 16) ? 32767.0f : 8388607.0f;
        prevUniform_ = 0.0f;
        rng_.seed(seed);
    }

    int32_t bitsPerSample() const noexcept { return bits_; }

    void convertToInt32(const float* src, size_t sampleCount, std::vector<int32_t>& dst) noexcept {
        if (dst.size() < sampleCount) dst.resize(sampleCount);
        for (size_t i = 0; i < sampleCount; ++i) { dst[i] = quantizeSample(src[i]); }
    }

    void convertToInt16(const float* src, size_t sampleCount, std::vector<int16_t>& dst) noexcept {
        if (dst.size() < sampleCount) dst.resize(sampleCount);
        for (size_t i = 0; i < sampleCount; ++i) {
            dst[i] = static_cast<int16_t>(std::clamp<int32_t>(quantizeSample(src[i]), -32768, 32767));
        }
    }

    void convertToPacked24Le(const float* src, size_t sampleCount, std::vector<uint8_t>& dst) noexcept {
        if (dst.size() < sampleCount * 3) dst.resize(sampleCount * 3);
        for (size_t i = 0; i < sampleCount; ++i) {
            const int32_t v = std::clamp<int32_t>(quantizeSample(src[i]), -8388608, 8388607);
            const uint32_t u = static_cast<uint32_t>(v);
            dst[i * 3 + 0] = static_cast<uint8_t>(u & 0xFFu);
            dst[i * 3 + 1] = static_cast<uint8_t>((u >> 8) & 0xFFu);
            dst[i * 3 + 2] = static_cast<uint8_t>((u >> 16) & 0xFFu);
        }
    }

  private:
    int32_t quantizeSample(float raw) noexcept {
        float x = std::clamp(raw, -1.0f, 1.0f);
        if (dither_) {
            const float cur = rng_.next01();
            x = std::clamp(x + (cur - prevUniform_) / scale_, -1.0f, 1.0f);
            prevUniform_ = cur;
        }
        return static_cast<int32_t>(std::lround(x * scale_));
    }

    int32_t bits_ = 24;
    bool dither_ = false;
    float scale_ = 8388607.0f;
    float prevUniform_ = 0.0f;
    nota::math::Rng rng_{0x6d2b79f5u};
};

} // namespace nota::media::detail
