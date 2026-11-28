// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PlanarStereoBuffer — 64-byte aligned pre-allocated L/R buffer using nota::math::StereoOps.

#pragma once

#include "nota/dsp/rt/AudioSpan.h"
#include "nota/math/StereoOps.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <utility>
#include <vector>

namespace nota::dsp {

class PlanarStereoBuffer {
  public:
    static constexpr int32_t kDefaultLiveFrames = 2048;
    static constexpr int32_t kMaxFrames = 16384;

    PlanarStereoBuffer() = default;
    explicit PlanarStereoBuffer(int32_t initialFrames) { ensureFrames(initialFrames); }

    PlanarStereoBuffer(const PlanarStereoBuffer& other) {
        if (other.frameCap_ > 0) ensureFrames(other.frameCap_);
    }

    PlanarStereoBuffer& operator=(const PlanarStereoBuffer& other) {
        if (this != &other && other.frameCap_ > 0) ensureFrames(other.frameCap_);
        return *this;
    }

    PlanarStereoBuffer(PlanarStereoBuffer&& other) noexcept
        : storage_(std::move(other.storage_)), strideFloats_(other.strideFloats_), frameCap_(other.frameCap_) {
        recomputePointers();
        other.left_ = other.right_ = nullptr;
        other.frameCap_ = 0;
        other.strideFloats_ = 0;
    }

    PlanarStereoBuffer& operator=(PlanarStereoBuffer&& other) noexcept {
        if (this != &other) {
            storage_ = std::move(other.storage_);
            strideFloats_ = other.strideFloats_;
            frameCap_ = other.frameCap_;
            recomputePointers();
            other.left_ = other.right_ = nullptr;
            other.frameCap_ = 0;
            other.strideFloats_ = 0;
        }
        return *this;
    }

    void ensureFrames(int32_t minFrames) {
        const int32_t target = std::clamp(minFrames, kDefaultLiveFrames, kMaxFrames);
        if (target <= frameCap_ && left_ != nullptr) return;
        const std::size_t stride = ((static_cast<std::size_t>(target) + 15u) / 16u) * 16u;
        storage_.assign(2u * stride + 16u, 0.0f);
        strideFloats_ = stride;
        frameCap_ = target;
        recomputePointers();
    }

    int32_t capacityFrames() const noexcept { return frameCap_; }
    bool readyFor(int32_t frames) const noexcept { return frames > 0 && frames <= frameCap_ && left_ != nullptr; }

    float* left() noexcept { return left_; }
    float* right() noexcept { return right_; }
    const float* left() const noexcept { return left_; }
    const float* right() const noexcept { return right_; }

    PlanarAudioSpan span(int32_t frames) noexcept {
        const int32_t n = std::clamp(frames, 0, frameCap_);
        return {left_, right_, n};
    }

    void clear(int32_t frames) noexcept {
        const int32_t n = std::min(frames, frameCap_);
        if (n <= 0 || !left_ || !right_) return;
        const std::size_t bytes = static_cast<std::size_t>(n) * sizeof(float);
        std::memset(left_, 0, bytes);
        std::memset(right_, 0, bytes);
    }

    void deinterleaveFrom(const float* srcInterleaved, int32_t frames) noexcept {
        const int32_t n = std::min(frames, frameCap_);
        if (n <= 0 || !srcInterleaved || !left_ || !right_) return;
        math::deinterleave(left_, right_, srcInterleaved, n);
    }

    void interleaveTo(float* dstInterleaved, int32_t frames) const noexcept {
        const int32_t n = std::min(frames, frameCap_);
        if (n <= 0 || !dstInterleaved || !left_ || !right_) return;
        math::interleave(dstInterleaved, left_, right_, n);
    }

    void addToInterleaved(float* dstInterleaved, int32_t frames) const noexcept {
        const int32_t n = std::min(frames, frameCap_);
        if (n <= 0 || !dstInterleaved || !left_ || !right_) return;
        math::interleaveAdd(dstInterleaved, left_, right_, n);
    }

  private:
    void recomputePointers() noexcept {
        if (storage_.empty() || strideFloats_ == 0) {
            left_ = right_ = nullptr;
            return;
        }
        const std::uintptr_t raw = reinterpret_cast<std::uintptr_t>(storage_.data());
        const std::uintptr_t aligned = (raw + 63u) & ~std::uintptr_t{63u};
        left_ = reinterpret_cast<float*>(aligned);
        right_ = left_ + strideFloats_;
    }

    std::vector<float> storage_;
    float* left_ = nullptr;
    float* right_ = nullptr;
    std::size_t strideFloats_ = 0;
    int32_t frameCap_ = 0;
};

} // namespace nota::dsp
