// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RenderBusState — per-block bus activity tracking for group and return submix buses.
// Parallel workers mark buses via lock-free atomic byte stores; joined passes skip
// zero-filling and processing buses that received no audio and hold settled chains.

#pragma once

#include <algorithm>
#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>

namespace nota::detail {

template <size_t Capacity> class ActiveBusMask {
  public:
    ActiveBusMask() noexcept {
        for (auto& a : active_) a.store(0, std::memory_order_relaxed);
        clearedFrames_.fill(0);
    }

    // Audio/driver thread: zeroes only buses written during the previous block or buses
    // whose cleared frame high-water mark is smaller than `frames`.
    template <typename ZeroBusFn> void beginBlock(size_t count, int32_t frames, ZeroBusFn&& zeroBus) noexcept {
        if (invalidateReq_.load(std::memory_order_relaxed) && invalidateReq_.exchange(false, std::memory_order_acquire))
            clearedFrames_.fill(0);
        const size_t limit = std::min(count, Capacity);
        for (size_t i = 0; i < limit; ++i) {
            if (active_[i].load(std::memory_order_relaxed) != 0 || clearedFrames_[i] < frames) {
                zeroBus(i);
                active_[i].store(0, std::memory_order_relaxed);
                clearedFrames_[i] = frames;
            }
        }
    }

    // Thread-safe: forces all slots to clear once on the next block (e.g. graph publish).
    void requestInvalidate() noexcept { invalidateReq_.store(true, std::memory_order_release); }

    // Thread-safe during parallel leaf and group-level passes before their pool barrier.
    void markActive(size_t slot) noexcept {
        if (slot < Capacity) active_[slot].store(1, std::memory_order_relaxed);
    }

    bool isActive(size_t slot) const noexcept {
        return slot < Capacity && active_[slot].load(std::memory_order_relaxed) != 0;
    }

  private:
    std::array<std::atomic<uint8_t>, Capacity> active_{};
    std::array<int32_t, Capacity> clearedFrames_{};
    std::atomic<bool> invalidateReq_{false};
};

template <size_t MaxGroups, size_t MaxReturns> struct RenderBusState {
    ActiveBusMask<MaxGroups> groups;
    ActiveBusMask<MaxReturns> returns;

    void requestInvalidate() noexcept {
        groups.requestInvalidate();
        returns.requestInvalidate();
    }
};

} // namespace nota::detail
