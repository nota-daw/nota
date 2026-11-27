// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ScratchArena — 64-byte aligned pre-allocated stereo scratch banks.

#pragma once

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <utility>
#include <vector>

namespace nota::dsp {

template <std::size_t NumBuffers> class StereoScratchArena {
  public:
    static constexpr int32_t kDefaultLiveFrames = 2048;
    static constexpr int32_t kMaxFrames = 16384;

    explicit StereoScratchArena(int32_t initialFrames = kDefaultLiveFrames) { ensureCapacity(initialFrames); }

    StereoScratchArena(const StereoScratchArena& other) { ensureCapacity(other.frameCap_); }

    StereoScratchArena& operator=(const StereoScratchArena& other) {
        if (this != &other) ensureCapacity(other.frameCap_);
        return *this;
    }

    StereoScratchArena(StereoScratchArena&& other) noexcept
        : storage_(std::move(other.storage_)), strideFloats_(other.strideFloats_), frameCap_(other.frameCap_) {
        recomputePointers();
        other.frameCap_ = 0;
        other.strideFloats_ = 0;
        other.ptrs_.fill(nullptr);
    }

    StereoScratchArena& operator=(StereoScratchArena&& other) noexcept {
        if (this != &other) {
            storage_ = std::move(other.storage_);
            strideFloats_ = other.strideFloats_;
            frameCap_ = other.frameCap_;
            recomputePointers();
            other.frameCap_ = 0;
            other.strideFloats_ = 0;
            other.ptrs_.fill(nullptr);
        }
        return *this;
    }

    void ensureCapacity(int32_t minFrames) {
        const int32_t target = std::clamp(minFrames, kDefaultLiveFrames, kMaxFrames);
        if (target <= frameCap_ && basePtr_ != nullptr) return;
        const std::size_t stride = ((static_cast<std::size_t>(target) * 2u + 15u) / 16u) * 16u;
        storage_.assign(NumBuffers * stride + 16u, 0.0f);
        strideFloats_ = stride;
        frameCap_ = target;
        recomputePointers();
    }

    int32_t capacityFrames() const noexcept { return frameCap_; }

    float* buffer(std::size_t index) noexcept { return ptrs_[index]; }
    const float* buffer(std::size_t index) const noexcept { return ptrs_[index]; }

    void clearBuffer(std::size_t index, int32_t frames) noexcept {
        const int32_t n = std::min(frames, frameCap_);
        if (n > 0 && ptrs_[index]) std::memset(ptrs_[index], 0, static_cast<std::size_t>(n) * 2u * sizeof(float));
    }

  private:
    void recomputePointers() noexcept {
        if (storage_.empty() || strideFloats_ == 0) {
            basePtr_ = nullptr;
            ptrs_.fill(nullptr);
            return;
        }
        const std::uintptr_t raw = reinterpret_cast<std::uintptr_t>(storage_.data());
        const std::uintptr_t aligned = (raw + 63u) & ~std::uintptr_t{63u};
        basePtr_ = reinterpret_cast<float*>(aligned);
        for (std::size_t i = 0; i < NumBuffers; ++i) ptrs_[i] = basePtr_ + i * strideFloats_;
    }

    std::vector<float> storage_;
    float* basePtr_ = nullptr;
    std::array<float*, NumBuffers> ptrs_{};
    std::size_t strideFloats_ = 0;
    int32_t frameCap_ = 0;
};

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
        : storage_(std::move(other.storage_)), strideFloats_(other.strideFloats_), slotCap_(other.slotCap_),
          frameCap_(other.frameCap_) {
        recomputePointers();
        other.slots_.clear();
        other.strideFloats_ = 0;
        other.slotCap_ = 0;
        other.frameCap_ = 0;
    }
    ChainScratchBank& operator=(ChainScratchBank&& other) noexcept {
        if (this != &other) {
            storage_ = std::move(other.storage_);
            strideFloats_ = other.strideFloats_;
            slotCap_ = other.slotCap_;
            frameCap_ = other.frameCap_;
            recomputePointers();
            other.slots_.clear();
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
        const std::size_t stride = ((static_cast<std::size_t>(finalFrames) * 2u + 15u) / 16u) * 16u;
        storage_.assign(static_cast<std::size_t>(finalSlots) * stride + 16u, 0.0f);
        strideFloats_ = stride;
        slotCap_ = finalSlots;
        frameCap_ = finalFrames;
        recomputePointers();
    }

    int32_t slotCount() const noexcept { return slotCap_; }
    int32_t capacityFrames() const noexcept { return frameCap_; }
    float* buffer(int32_t slot) noexcept {
        return slot >= 0 && slot < slotCap_ ? slots_[static_cast<std::size_t>(slot)] : nullptr;
    }
    const float* buffer(int32_t slot) const noexcept {
        return slot >= 0 && slot < slotCap_ ? slots_[static_cast<std::size_t>(slot)] : nullptr;
    }

  private:
    void recomputePointers() {
        if (storage_.empty() || slotCap_ <= 0 || strideFloats_ == 0) {
            slots_.clear();
            return;
        }
        const std::uintptr_t raw = reinterpret_cast<std::uintptr_t>(storage_.data());
        const std::uintptr_t aligned = (raw + 63u) & ~std::uintptr_t{63u};
        float* base = reinterpret_cast<float*>(aligned);
        slots_.resize(static_cast<std::size_t>(slotCap_));
        for (int32_t i = 0; i < slotCap_; ++i)
            slots_[static_cast<std::size_t>(i)] = base + static_cast<std::size_t>(i) * strideFloats_;
    }

    std::vector<float> storage_;
    std::vector<float*> slots_;
    std::size_t strideFloats_ = 0;
    int32_t slotCap_ = 0;
    int32_t frameCap_ = 0;
};

} // namespace nota::dsp
