// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SpscRingBuffer & CommandQueue — lock-free single-producer / single-consumer ring buffer (AR-4).

#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>

namespace nota::dsp {

enum class CommandType : uint32_t {
    None = 0,
    SetToneEnabled,
    SetFrequency,
    TransportPlay,
    TransportStop,
    SetBpm,
    SetTimeSignature,
    SetLoop,
    SetMetronome,
    Seek,
    ReleaseNote,
    TrackNotesOff,
    TakeRestart,
    CancelSessionLaunch,
};

struct Command {
    CommandType type = CommandType::None;
    int32_t i0 = 0;
    int32_t i1 = 0;
    double d0 = 0.0;
    double d1 = 0.0;
    float f0 = 0.0f;
};

template <typename T, std::size_t Capacity> class SpscRingBuffer {
    static_assert((Capacity & (Capacity - 1u)) == 0u, "Capacity must be a power of two");

  public:
    bool push(const T& item) noexcept {
        const std::size_t head = head_.load(std::memory_order_relaxed);
        const std::size_t next = (head + 1u) & kMask;
        if (next == tail_.load(std::memory_order_acquire)) return false;
        buffer_[head] = item;
        head_.store(next, std::memory_order_release);
        return true;
    }

    bool pushBatch(const T* items, std::size_t n) noexcept {
        if (n == 0u) return true;
        if (items == nullptr) return false;
        const std::size_t head = head_.load(std::memory_order_relaxed);
        const std::size_t room = (tail_.load(std::memory_order_acquire) - head - 1u) & kMask;
        if (n > room) return false;
        for (std::size_t i = 0u; i < n; ++i) buffer_[(head + i) & kMask] = items[i];
        head_.store((head + n) & kMask, std::memory_order_release);
        return true;
    }

    [[nodiscard]] bool pop(T& out) noexcept {
        const std::size_t tail = tail_.load(std::memory_order_relaxed);
        if (tail == head_.load(std::memory_order_acquire)) return false;
        out = buffer_[tail];
        tail_.store((tail + 1u) & kMask, std::memory_order_release);
        return true;
    }

    std::size_t size() const noexcept {
        return (head_.load(std::memory_order_acquire) - tail_.load(std::memory_order_relaxed)) & kMask;
    }

  private:
    static constexpr std::size_t kMask = Capacity - 1u;
    T buffer_[Capacity]{};
    std::atomic<std::size_t> head_{0};
    std::atomic<std::size_t> tail_{0};
};

using CommandQueue = SpscRingBuffer<Command, 1024>;

} // namespace nota::dsp
