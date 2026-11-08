// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Multi-channel capture plumbing (specs/086): the input device delivers every channel it
// has, but the engine only carries the channels some track uses — the "capture layout".
// The layout is published by the message thread through a seqlock so the input and audio
// threads read it without locks or allocation (AR-6), and every block in a CaptureRing
// carries the layout generation it was written with, so a consumer never mistakes one
// layout's columns for another's after an arm or a re-pick changed it.
#pragma once

#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>

#include "audio_io/AudioInput.h" // kMaxInputChannels

namespace nota {

// A plain copy of a layout: which device channel each ring column carries.
struct CaptureLayoutSnapshot {
    uint32_t generation = 0;
    int32_t count = 0;                                // columns carried, 0..kMaxInputChannels
    std::array<int8_t, kMaxInputChannels> channel{};  // column -> device channel
    std::array<int8_t, kMaxInputChannels> columnOf{}; // device channel -> column, -1 = not carried

    CaptureLayoutSnapshot() { columnOf.fill(-1); }
    int32_t column(int32_t deviceChannel) const {
        return deviceChannel >= 0 && deviceChannel < kMaxInputChannels ? columnOf[static_cast<size_t>(deviceChannel)]
                                                                        : -1;
    }
};

// The published layout. One writer (message thread), any number of RT readers; a reader that
// catches a write in progress keeps the copy it had and tries again on its next callback.
class CaptureLayout {
  public:
    void publish(const CaptureLayoutSnapshot& s) noexcept {
        const uint32_t q = seq_.load(std::memory_order_relaxed);
        seq_.store(q + 1, std::memory_order_relaxed); // odd: a write is in progress
        std::atomic_thread_fence(std::memory_order_release);
        generation_.store(s.generation, std::memory_order_relaxed);
        count_.store(s.count, std::memory_order_relaxed);
        for (size_t i = 0; i < static_cast<size_t>(kMaxInputChannels); ++i) {
            channel_[i].store(s.channel[i], std::memory_order_relaxed);
            columnOf_[i].store(s.columnOf[i], std::memory_order_relaxed);
        }
        seq_.store(q + 2, std::memory_order_release);
    }

    // RT-safe. True when `out` now holds a consistent copy.
    bool read(CaptureLayoutSnapshot& out) const noexcept {
        const uint32_t q0 = seq_.load(std::memory_order_acquire);
        if (q0 & 1u) return false;
        CaptureLayoutSnapshot s;
        s.generation = generation_.load(std::memory_order_relaxed);
        s.count = count_.load(std::memory_order_relaxed);
        for (size_t i = 0; i < static_cast<size_t>(kMaxInputChannels); ++i) {
            s.channel[i] = channel_[i].load(std::memory_order_relaxed);
            s.columnOf[i] = columnOf_[i].load(std::memory_order_relaxed);
        }
        std::atomic_thread_fence(std::memory_order_acquire);
        if (seq_.load(std::memory_order_relaxed) != q0) return false;
        out = s;
        return true;
    }

    uint32_t generation() const noexcept { return generation_.load(std::memory_order_acquire); }

  private:
    std::atomic<uint32_t> seq_{0};
    std::atomic<uint32_t> generation_{0};
    std::atomic<int32_t> count_{0};
    std::array<std::atomic<int8_t>, kMaxInputChannels> channel_{};
    std::array<std::atomic<int8_t>, kMaxInputChannels> columnOf_{};
};

// Header of a block in a CaptureRing.
struct CaptureBlock {
    uint32_t generation = 0; // layout the columns follow
    int32_t width = 0;       // floats per frame
    int32_t frames = 0;      // 0 for a marker
    uint32_t flags = 0;
    static constexpr uint32_t kMarker = 1u; // a new capture epoch starts here (specs/078)
};

// Single-producer single-consumer ring of capture blocks: a 4-float header, then
// frames × width floats. A push is all or nothing (a full ring drops the block and the caller
// counts the frames as dropped); a consumer may read a block in several pieces.
template <size_t CapacityFloats> class CaptureRing {
    static_assert((CapacityFloats & (CapacityFloats - 1)) == 0, "capacity must be a power of two");

  public:
    static constexpr size_t kHeader = 4;

    // Producer. Gathers `columns` (device channel per column) out of `interleaved`
    // (`deviceChannels` wide) for `frames` frames. A column naming a channel the device lacks
    // carries silence.
    bool pushGather(uint32_t generation, const float* interleaved, int32_t deviceChannels, int32_t frames,
        const int8_t* columns, int32_t width) noexcept {
        if (frames <= 0 || width <= 0) return true;
        const size_t need = kHeader + static_cast<size_t>(frames) * static_cast<size_t>(width);
        size_t head = 0;
        if (!reserve(need, head)) return false;
        head = writeHeader(head, CaptureBlock{generation, width, frames, 0});
        for (int32_t f = 0; f < frames; ++f) {
            const float* src = interleaved + static_cast<size_t>(f) * static_cast<size_t>(deviceChannels);
            for (int32_t c = 0; c < width; ++c) {
                const int32_t ch = columns[c];
                buf_[head] = (ch >= 0 && ch < deviceChannels) ? src[ch] : 0.0f;
                head = (head + 1) & kMask;
            }
        }
        commit(head, static_cast<size_t>(frames));
        return true;
    }

    // Producer: frames already in column order (`width` floats each).
    bool pushFrames(uint32_t generation, const float* frames, int32_t count, int32_t width) noexcept {
        if (count <= 0 || width <= 0) return true;
        const size_t need = kHeader + static_cast<size_t>(count) * static_cast<size_t>(width);
        size_t head = 0;
        if (!reserve(need, head)) return false;
        head = writeHeader(head, CaptureBlock{generation, width, count, 0});
        const size_t n = static_cast<size_t>(count) * static_cast<size_t>(width);
        for (size_t i = 0; i < n; ++i) {
            buf_[head] = frames[i];
            head = (head + 1) & kMask;
        }
        commit(head, static_cast<size_t>(count));
        return true;
    }

    bool pushMarker() noexcept {
        size_t head = 0;
        if (!reserve(kHeader, head)) return false;
        head = writeHeader(head, CaptureBlock{0, 0, 0, CaptureBlock::kMarker});
        commit(head, 0);
        return true;
    }

    // Consumer: the next block's header, or false when the ring is empty. The block's frames
    // must then be consumed with read()/skip() before the next call (`remaining()` says how
    // many are left); a marker has none.
    bool next(CaptureBlock& out) noexcept {
        if (remaining_ > 0) skip(remaining_);
        if (!peek(out)) return false;
        tail_.store((tail_.load(std::memory_order_relaxed) + kHeader) & kMask, std::memory_order_release);
        width_ = out.width;
        remaining_ = out.frames;
        return true;
    }

    // Consumer: the next block's header without taking it (call between blocks only).
    bool peek(CaptureBlock& out) const noexcept {
        const size_t tail = tail_.load(std::memory_order_relaxed);
        if (tail == head_.load(std::memory_order_acquire)) return false;
        size_t t = tail;
        uint32_t h[kHeader];
        for (size_t i = 0; i < kHeader; ++i) {
            std::memcpy(&h[i], &buf_[t], sizeof(float));
            t = (t + 1) & kMask;
        }
        out.generation = h[0];
        out.width = static_cast<int32_t>(h[1]);
        out.frames = static_cast<int32_t>(h[2]);
        out.flags = h[3];
        return true;
    }

    int32_t remaining() const noexcept { return remaining_; }
    int32_t width() const noexcept { return width_; } // the current block's floats per frame

    // Copies up to `frames` frames of the current block (width floats each) to `dst`.
    int32_t read(float* dst, int32_t frames) noexcept {
        const int32_t n = frames < remaining_ ? frames : remaining_;
        if (n <= 0) return 0;
        size_t t = tail_.load(std::memory_order_relaxed);
        const size_t count = static_cast<size_t>(n) * static_cast<size_t>(width_);
        for (size_t i = 0; i < count; ++i) {
            dst[i] = buf_[t];
            t = (t + 1) & kMask;
        }
        tail_.store(t, std::memory_order_release);
        remaining_ -= n;
        framesQueued_.fetch_sub(static_cast<size_t>(n), std::memory_order_relaxed);
        return n;
    }

    void skip(int32_t frames) noexcept {
        const int32_t n = frames < remaining_ ? frames : remaining_;
        if (n <= 0) return;
        const size_t t = (tail_.load(std::memory_order_relaxed) + static_cast<size_t>(n) * static_cast<size_t>(width_)) &
                         kMask;
        tail_.store(t, std::memory_order_release);
        remaining_ -= n;
        framesQueued_.fetch_sub(static_cast<size_t>(n), std::memory_order_relaxed);
    }

    // Consumer: drop everything queued.
    void clear() noexcept {
        CaptureBlock b;
        while (next(b)) {}
        if (remaining_ > 0) skip(remaining_);
    }

    // Frames queued (pushed, not yet read or skipped) — the monitor cushion's measure.
    size_t framesQueued() const noexcept { return framesQueued_.load(std::memory_order_acquire); }

  private:
    static constexpr size_t kMask = CapacityFloats - 1;

    bool reserve(size_t need, size_t& head) const noexcept {
        head = head_.load(std::memory_order_relaxed);
        const size_t tail = tail_.load(std::memory_order_acquire);
        const size_t used = (head - tail) & kMask;
        return need + used < CapacityFloats; // one slot stays empty: head == tail means empty
    }
    size_t writeHeader(size_t head, const CaptureBlock& b) noexcept {
        const uint32_t h[kHeader] = {b.generation, static_cast<uint32_t>(b.width), static_cast<uint32_t>(b.frames), b.flags};
        for (size_t i = 0; i < kHeader; ++i) {
            std::memcpy(&buf_[head], &h[i], sizeof(float));
            head = (head + 1) & kMask;
        }
        return head;
    }
    void commit(size_t head, size_t frames) noexcept {
        framesQueued_.fetch_add(frames, std::memory_order_relaxed);
        head_.store(head, std::memory_order_release);
    }

    float buf_[CapacityFloats]{};
    std::atomic<size_t> head_{0};
    std::atomic<size_t> tail_{0};
    std::atomic<size_t> framesQueued_{0};
    // consumer-only cursor into the current block
    int32_t width_ = 0;
    int32_t remaining_ = 0;
};

// input thread -> message thread (takes): 2^21 floats = 8 MB, ~2.7 s of 16 channels at 48 kHz.
using CaptureRecordRing = CaptureRing<1u << 21>;
// input thread -> audio thread (live monitoring).
using CaptureMonitorRing = CaptureRing<1u << 18>;

} // namespace nota
