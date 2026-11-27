// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PolyVoiceScheduler — deterministic parallel polyphonic voice batch partitioning and scratch bank.

#pragma once

#include "nota/math/BufferOps.h"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <utility>
#include <vector>

namespace nota::dsp {

class PolyVoiceScratchBank {
  public:
    static constexpr int32_t kMaxBatches = 8;

    PolyVoiceScratchBank() = default;
    explicit PolyVoiceScratchBank(int32_t initialFrames) { ensureFrames(initialFrames); }

    PolyVoiceScratchBank(const PolyVoiceScratchBank& other) {
        if (other.frameCap_ > 0) ensureFrames(other.frameCap_);
    }
    PolyVoiceScratchBank& operator=(const PolyVoiceScratchBank& other) {
        if (this != &other && other.frameCap_ > 0) ensureFrames(other.frameCap_);
        return *this;
    }
    PolyVoiceScratchBank(PolyVoiceScratchBank&& other) noexcept
        : storage_(std::move(other.storage_)), strideFloats_(other.strideFloats_), frameCap_(other.frameCap_) {
        recomputePointers();
        other.frameCap_ = 0;
        other.strideFloats_ = 0;
        other.left_.fill(nullptr);
        other.right_.fill(nullptr);
    }
    PolyVoiceScratchBank& operator=(PolyVoiceScratchBank&& other) noexcept {
        if (this != &other) {
            storage_ = std::move(other.storage_);
            strideFloats_ = other.strideFloats_;
            frameCap_ = other.frameCap_;
            recomputePointers();
            other.frameCap_ = 0;
            other.strideFloats_ = 0;
            other.left_.fill(nullptr);
            other.right_.fill(nullptr);
        }
        return *this;
    }

    void ensureFrames(int32_t minFrames) {
        const int32_t target = std::max<int32_t>(128, minFrames);
        if (target <= frameCap_ && left_[0] != nullptr) return;
        const std::size_t stride = ((static_cast<std::size_t>(target) + 15u) / 16u) * 16u;
        storage_.assign(static_cast<std::size_t>(kMaxBatches) * 2u * stride + 16u, 0.0f);
        strideFloats_ = stride;
        frameCap_ = target;
        recomputePointers();
    }

    int32_t capacityFrames() const noexcept { return frameCap_; }

    float* left(int32_t batch) noexcept { return left_[static_cast<std::size_t>(batch)]; }
    float* right(int32_t batch) noexcept { return right_[static_cast<std::size_t>(batch)]; }
    const float* left(int32_t batch) const noexcept { return left_[static_cast<std::size_t>(batch)]; }
    const float* right(int32_t batch) const noexcept { return right_[static_cast<std::size_t>(batch)]; }

    void clearBatch(int32_t batch, int32_t samples) noexcept {
        const int32_t n = std::min(samples, frameCap_);
        if (n <= 0) return;
        const std::size_t bytes = static_cast<std::size_t>(n) * sizeof(float);
        std::memset(left_[static_cast<std::size_t>(batch)], 0, bytes);
        std::memset(right_[static_cast<std::size_t>(batch)], 0, bytes);
    }

  private:
    void recomputePointers() noexcept {
        if (storage_.empty() || strideFloats_ == 0) {
            left_.fill(nullptr);
            right_.fill(nullptr);
            return;
        }
        const std::uintptr_t raw = reinterpret_cast<std::uintptr_t>(storage_.data());
        const std::uintptr_t aligned = (raw + 63u) & ~std::uintptr_t{63u};
        float* base = reinterpret_cast<float*>(aligned);
        for (int32_t b = 0; b < kMaxBatches; ++b) {
            left_[static_cast<std::size_t>(b)] = base + static_cast<std::size_t>(b * 2) * strideFloats_;
            right_[static_cast<std::size_t>(b)] = base + static_cast<std::size_t>(b * 2 + 1) * strideFloats_;
        }
    }

    std::vector<float> storage_;
    std::array<float*, kMaxBatches> left_{};
    std::array<float*, kMaxBatches> right_{};
    std::size_t strideFloats_ = 0;
    int32_t frameCap_ = 0;
};

struct PolyVoiceBatchRange {
    int32_t start = 0;
    int32_t count = 0;
};

class PolyVoiceScheduler {
  public:
    static constexpr int32_t kMaxBatches = PolyVoiceScratchBank::kMaxBatches;

    static int32_t planBatches(
        int32_t activeVoices, int32_t maxBatches, std::array<PolyVoiceBatchRange, kMaxBatches>& out) noexcept {
        if (activeVoices <= 0) return 0;
        const int32_t batches = std::clamp(maxBatches, 1, std::min(activeVoices, kMaxBatches));
        const int32_t base = activeVoices / batches;
        const int32_t rem = activeVoices % batches;
        int32_t cursor = 0;
        for (int32_t b = 0; b < batches; ++b) {
            const int32_t len = base + (b < rem ? 1 : 0);
            out[static_cast<std::size_t>(b)] = {cursor, len};
            cursor += len;
        }
        return batches;
    }

    static void accumulateMonoAdd(float* dst, const float* src, int32_t samples) noexcept {
        math::add(dst, src, samples);
    }
};

} // namespace nota::dsp
