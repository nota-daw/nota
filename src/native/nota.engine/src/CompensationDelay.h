// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Per-track plugin-delay-compensation line (M3-7). A hosted plugin can report
// processing latency; to keep tracks time-aligned we delay every other track by
// (maxLatency - itsOwnLatency). This is that per-track delay.
//
// Lock-free by construction: the ring is allocated once on the message thread
// (the first time a non-zero delay is needed) and published via the `ready`
// release/acquire flag; the audio thread only touches the ring once `ready` is
// true, after which the buffer is never reallocated. The delay amount is a plain
// atomic, so recompute (message thread) never races the audio thread.

#pragma once

#include <atomic>
#include <cstdint>
#include <vector>

namespace nota {

class CompensationDelay {
public:
    // Message thread: set the compensation delay in samples. Allocates the ring
    // (once) the first time a positive delay is requested.
    void setDelay(int32_t samples) {
        if (samples > 0 && cap_ == 0) {
            constexpr int32_t kCap = 65536; // ~1.36 s @ 48 kHz; plugin latencies fit
            ring_.assign(static_cast<size_t>(kCap) * 2, 0.0f);
            pos_ = 0;
            cap_ = kCap;
            ready_.store(true, std::memory_order_release);
        }
        if (samples < 0) samples = 0;
        if (cap_ > 0 && samples > cap_ - 1) samples = cap_ - 1;
        delay_.store(samples, std::memory_order_relaxed);
    }

    int32_t delay() const { return delay_.load(std::memory_order_relaxed); }

    // Audio thread: delay `frames` of interleaved stereo in place. No-op until a
    // positive delay has been prepared.
    void process(float* buf, int32_t frames) {
        if (!ready_.load(std::memory_order_acquire)) return;
        const int32_t d = delay_.load(std::memory_order_relaxed);
        if (d <= 0) return;
        for (int32_t i = 0; i < frames; ++i) {
            int32_t rd = pos_ - d;
            if (rd < 0) rd += cap_;
            const float il = buf[i * 2], ir = buf[i * 2 + 1];
            buf[i * 2]     = ring_[rd * 2];
            buf[i * 2 + 1] = ring_[rd * 2 + 1];
            ring_[pos_ * 2]     = il;
            ring_[pos_ * 2 + 1] = ir;
            if (++pos_ >= cap_) pos_ = 0;
        }
    }

private:
    std::vector<float> ring_;
    int32_t            cap_ = 0;   // published via ready_
    int32_t            pos_ = 0;   // audio thread only
    std::atomic<int32_t> delay_{0};
    std::atomic<bool>    ready_{false};
};

} // namespace nota
