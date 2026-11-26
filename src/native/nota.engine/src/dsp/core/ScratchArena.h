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

// Dynamically sized bank of aligned interleaved stereo buffers for multi-chain racks.
// Allocated on non-RT chain preparation/capacity checks so RT chain fan-out never allocates.
class ChainScratchBank {
  public:
    static constexpr int32_t kDefaultLiveFrames = 2048;
    static constexpr int32_t kMaxFrames = 16384;
    static constexpr int32_t kMaxSlots = 64;

    ChainScratchBank() = default;
    ChainScratchBank(const ChainScratchBank& other) {
        if (other.slotCap_ > 0 && other.frameCap_ > 0) prepare(other.slotCap_, other.frameCap_);
    }
    ChainScratchBank& operator=(const ChainScratchBank& other) {
        if (this != &other && other.slotCap_ > 0 && other.frameCap_ > 0) prepare(other.slotCap_, other.frameCap_);
        return *this;
    }
    ChainScratchBank(ChainScratchBank&& other) noexcept
        : storage_(std::move(other.storage_)),
          slots_(std::move(other.slots_)),
          strideFloats_(other.strideFloats_),
          slotCap_(other.slotCap_),
          frameCap_(other.frameCap_) {
        other.strideFloats_ = 0;
        other.slotCap_ = 0;
        other.frameCap_ = 0;
    }
    ChainScratchBank& operator=(ChainScratchBank&& other) noexcept {
        if (this != &other) {
            storage_ = std::move(other.storage_);
            slots_ = std::move(other.slots_);
            strideFloats_ = other.strideFloats_;
            slotCap_ = other.slotCap_;
            frameCap_ = other.frameCap_;
            other.strideFloats_ = 0;
            other.slotCap_ = 0;
            other.frameCap_ = 0;
        }
        return *this;
    }

    void prepare(int32_t minSlots, int32_t minFrames) {
        const int32_t wantSlots = std::clamp(minSlots, 0, kMaxSlots);
        const int32_t wantFrames = std::clamp(minFrames, kDefaultLiveFrames, kMaxFrames);
        if (wantSlots <= 0) return;
        if (wantSlots <= slotCap_ && wantFrames <= frameCap_ && !slots_.empty()) return;
        const int32_t finalSlots = std::max(wantSlots, slotCap_);
        const int32_t finalFrames = std::max(wantFrames, frameCap_);
        const size_t stride = ((static_cast<size_t>(finalFrames) * 2 + 15) / 16) * 16;
        storage_.assign(static_cast<size_t>(finalSlots) * stride + 16, 0.0f);
        const uintptr_t raw = reinterpret_cast<uintptr_t>(storage_.data());
        const uintptr_t aligned = (raw + 63u) & ~uintptr_t{63u};
        float* base = reinterpret_cast<float*>(aligned);
        slots_.resize(static_cast<size_t>(finalSlots));
        for (int32_t i = 0; i < finalSlots; ++i) slots_[static_cast<size_t>(i)] = base + static_cast<size_t>(i) * stride;
        strideFloats_ = stride;
        slotCap_ = finalSlots;
        frameCap_ = finalFrames;
    }

    int32_t slotCount() const noexcept { return slotCap_; }
    int32_t capacityFrames() const noexcept { return frameCap_; }
    float* buffer(int32_t slot) noexcept {
        return slot >= 0 && slot < slotCap_ ? slots_[static_cast<size_t>(slot)] : nullptr;
    }

  private:
    std::vector<float> storage_;
    std::vector<float*> slots_;
    size_t strideFloats_ = 0;
    int32_t slotCap_ = 0;
    int32_t frameCap_ = 0;
};

} // namespace dsp
} // namespace nota
