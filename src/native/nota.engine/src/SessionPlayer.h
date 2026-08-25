// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M5-2: per-track Session playback state. A stable object shared across graph
// snapshots (like Instrument/CompensationDelay) so playback survives edits.
// Launch/stop requests come from the message thread (atomics); the audio thread
// applies them at the next quantize boundary and advances the looping position.

#pragma once

#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class SessionPlayer {
public:
    static constexpr int32_t kNone = -2; // no pending request
    static constexpr int32_t kStop = -1; // pending stop

    // Audio thread reads; message thread writes.
    std::atomic<int32_t> playing{-1};      // playing slot, or -1
    std::atomic<int32_t> pending{kNone};   // kNone / kStop / slot index

    // Audio-thread-only.
    double localBeats = 0.0;               // absolute position within the loop clock

    void requestLaunch(int32_t slot) { pending.store(slot, std::memory_order_relaxed); }
    void requestStop() { pending.store(kStop, std::memory_order_relaxed); }

    // Audio thread: if a pending request exists and a quantize boundary falls in
    // this block (or quant<=0), apply it. Returns true if `playing` changed
    // (so the caller can flush hanging notes).
    bool maybeApply(double blockStartBeats, double blockBeats, double quant) {
        const int32_t p = pending.load(std::memory_order_relaxed);
        if (p == kNone) return false;

        bool boundary;
        if (quant <= 0.0) {
            boundary = true;
        } else {
            const double b1 = blockStartBeats + blockBeats;
            boundary = blockStartBeats <= 1e-9
                    || std::floor(blockStartBeats / quant) != std::floor(b1 / quant);
        }
        if (!boundary) return false;

        pending.store(kNone, std::memory_order_relaxed);
        if (p == kStop) {
            playing.store(-1, std::memory_order_relaxed);
        } else {
            playing.store(p, std::memory_order_relaxed);
            localBeats = 0.0;
        }
        return true;
    }
};

} // namespace nota
