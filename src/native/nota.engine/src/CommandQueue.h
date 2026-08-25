// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Lock-free single-producer / single-consumer ring buffer (AR-4).
//
// Producer: the UI/message thread (via the C ABI). Consumer: the audio thread.
// The audio thread must never allocate, lock, or block (AR-6). Commands are
// plain PODs written into a pre-allocated ring; the audio callback drains them
// at the top of each block and applies them to the transport / debug oscillator.
//
// NOTE: track mix params (volume/pan/mute/solo) and master volume are plain
// atomics on their objects — they do NOT go through this queue. Only commands
// that mutate audio-thread-owned state (the Transport, the debug tone) do.

#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>

namespace nota {

enum class CommandType : uint32_t {
    None = 0,
    // Debug oscillator (M0 diagnostics).
    SetToneEnabled,
    SetFrequency,
    // Transport (M1).
    TransportPlay,
    TransportStop,
    SetBpm,
    SetTimeSignature,
    SetLoop,
    SetMetronome,
    Seek,
};

struct Command {
    CommandType type = CommandType::None;
    int32_t i0 = 0, i1 = 0;
    double  d0 = 0.0, d1 = 0.0;
    float   f0 = 0.0f;
};

template <typename T, size_t Capacity>
class SpscRingBuffer {
    static_assert((Capacity & (Capacity - 1)) == 0, "Capacity must be a power of two");

public:
    bool push(const T& item) noexcept {
        const size_t head = head_.load(std::memory_order_relaxed);
        const size_t next = (head + 1) & kMask;
        if (next == tail_.load(std::memory_order_acquire))
            return false; // full — drop rather than block
        buffer_[head] = item;
        head_.store(next, std::memory_order_release);
        return true;
    }

    bool pop(T& out) noexcept {
        const size_t tail = tail_.load(std::memory_order_relaxed);
        if (tail == head_.load(std::memory_order_acquire))
            return false; // empty
        out = buffer_[tail];
        tail_.store((tail + 1) & kMask, std::memory_order_release);
        return true;
    }

private:
    static constexpr size_t kMask = Capacity - 1;
    T buffer_[Capacity]{};
    std::atomic<size_t> head_{0};
    std::atomic<size_t> tail_{0};
};

using CommandQueue = SpscRingBuffer<Command, 1024>;

} // namespace nota
