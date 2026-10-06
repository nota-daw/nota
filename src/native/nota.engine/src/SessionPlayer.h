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
    static constexpr int32_t kNone    = -2; // no pending request
    static constexpr int32_t kStop    = -1; // pending stop (quantized)
    static constexpr int32_t kStopNow = -3; // pending stop, applied at the next block (per-track "back to arrangement")

    // Audio thread reads; message thread writes.
    std::atomic<int32_t> playing{-1};      // playing slot, or -1
    std::atomic<int32_t> pending{kNone};   // kNone / kStop / kStopNow / slot index
    // Launch options travelling with the pending request (Session P0).
    std::atomic<double>  pendingQuant{-1.0};   // <0 = the global launch quantum
    std::atomic<bool>    pendingLegato{false}; // keep the loop position across the switch
    std::atomic<float>   pendingGain{1.0f};    // launch-velocity gain for the incoming slot
    std::atomic<int32_t> repeatSlot{-1};       // Repeat mode: retrigger this slot every quantum while held
    // Audio thread writes, UI reads: the playing slot's loop position (progress) and beats
    // played since it launched (follow actions / fixed-length capture).
    std::atomic<double>  uiLocalBeats{0.0};
    std::atomic<double>  playedBeats{0.0};

    // Audio-thread-only.
    double localBeats = 0.0;               // absolute position within the loop clock
    float  gain = 1.0f;                    // launch-velocity gain of the playing slot

    void requestLaunch(int32_t slot, double quant = -1.0, bool legato = false, float g = 1.0f) {
        pendingQuant.store(quant, std::memory_order_relaxed);
        pendingLegato.store(legato, std::memory_order_relaxed);
        pendingGain.store(g, std::memory_order_relaxed);
        pending.store(slot, std::memory_order_relaxed);
    }
    void requestStop() { repeatSlot.store(-1, std::memory_order_relaxed); pending.store(kStop, std::memory_order_relaxed); }
    void requestStopNow() { repeatSlot.store(-1, std::memory_order_relaxed); pending.store(kStopNow, std::memory_order_relaxed); }

    // Audio thread: if a pending request exists and a quantize boundary falls in
    // this block (or quant<=0), apply it. Returns true if `playing` changed
    // (so the caller can flush hanging notes).
    bool maybeApply(double blockStartBeats, double blockBeats, double globalQuant) {
        const int32_t p = pending.load(std::memory_order_relaxed);
        if (p == kNone) return false;

        const double pq = pendingQuant.load(std::memory_order_relaxed);
        const double quant = (p == kStop || p == kStopNow || pq < 0.0) ? globalQuant : pq;
        bool boundary;
        if (p == kStopNow || quant <= 0.0) {
            boundary = true;
        } else {
            const double b1 = blockStartBeats + blockBeats;
            boundary = blockStartBeats <= 1e-9
                    || std::floor(blockStartBeats / quant) != std::floor(b1 / quant);
        }
        if (!boundary) return false;

        // A launch may have replaced the request between our two loads; only consume it if not.
        int32_t expect = p;
        if (!pending.compare_exchange_strong(expect, kNone, std::memory_order_relaxed)) return false;
        if (p == kStop || p == kStopNow) {
            playing.store(-1, std::memory_order_relaxed);
        } else {
            const bool legato = pendingLegato.load(std::memory_order_relaxed)
                             && playing.load(std::memory_order_relaxed) >= 0;
            playing.store(p, std::memory_order_relaxed);
            if (!legato) localBeats = 0.0;
            gain = pendingGain.load(std::memory_order_relaxed);
            playedBeats.store(0.0, std::memory_order_relaxed);
        }
        return true;
    }

    // Audio thread, after rendering a block: advance the played counter and publish the
    // loop position for the UI.
    void publish(double blockBeats) {
        playedBeats.store(playedBeats.load(std::memory_order_relaxed) + blockBeats, std::memory_order_relaxed);
        uiLocalBeats.store(localBeats, std::memory_order_relaxed);
    }
};

} // namespace nota
