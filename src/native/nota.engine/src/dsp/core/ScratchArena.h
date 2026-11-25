// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ScratchArena — aligned, session-sized stereo scratch buffer bank.
// Keeps live audio-callback memory footprint compact (~2048 frames = 16 KB/channel-pair
// instead of 128 KB fixed kMaxBlock arrays) while growing safely up to kMaxFrames
// during offline preparation before parallel or chain DSP runs (AR-6).

#pragma once

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <vector>

namespace nota {
namespace dsp {

template <size_t NumBuffers>
class StereoScratchArena {
  public:
    static constexpr int32_t kDefaultLiveFrames = 2048;
    static constexpr int32_t kMaxFrames = 16384;

    explicit StereoScratchArena(int32_t initialFrames = kDefaultLiveFrames) {
        ensureCapacity(initialFrames);
    }

    StereoScratchArena(const StereoScratchArena& other) {
        ensureCapacity(other.frameCap_);
    }

    StereoScratchArena& operator=(const StereoScratchArena& other) {
        if (this != &other) ensureCapacity(other.frameCap_);
        return *this;
    }

    StereoScratchArena(StereoScratchArena&&) noexcept = default;
    StereoScratchArena& operator=(StereoScratchArena&&) noexcept = default;

    // Must be called outside the hard-RT hardware callback whenever minFrames exceeds capacity.
    void ensureCapacity(int32_t minFrames) {
        const int32_t target = std::clamp(minFrames, kDefaultLiveFrames, kMaxFrames);
        if (target <= frameCap_ && basePtr_ != nullptr) return;
        const size_t stride = ((static_cast<size_t>(target) * 2 + 15) / 16) * 16; // 64-byte aligned per bank
        storage_.assign(NumBuffers * stride + 16, 0.0f);
        const uintptr_t raw = reinterpret_cast<uintptr_t>(storage_.data());
        const uintptr_t aligned = (raw + 63u) & ~uintptr_t{63u};
        basePtr_ = reinterpret_cast<float*>(aligned);
        strideFloats_ = stride;
        frameCap_ = target;
        for (size_t i = 0; i < NumBuffers; ++i) ptrs_[i] = basePtr_ + i * strideFloats_;
    }

    int32_t capacityFrames() const noexcept { return frameCap_; }

    float* buffer(size_t index) noexcept { return ptrs_[index]; }
    const float* buffer(size_t index) const noexcept { return ptrs_[index]; }

    void clearBuffer(size_t index, int32_t frames) noexcept {
        const int32_t n = std::min(frames, frameCap_);
        if (n > 0 && ptrs_[index]) std::memset(ptrs_[index], 0, static_cast<size_t>(n) * 2 * sizeof(float));
    }

  private:
    std::vector<float> storage_;
    float* basePtr_ = nullptr;
    std::array<float*, NumBuffers> ptrs_{};
    size_t strideFloats_ = 0;
    int32_t frameCap_ = 0;
};

} // namespace dsp
} // namespace nota
