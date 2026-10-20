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
    static constexpr int32_t kNone = -2;    // no pending request
    static constexpr int32_t kStop = -1;    // pending stop (quantized)
    static constexpr int32_t kStopNow = -3; // pending stop, applied at the next block (per-track "back to arrangement")

    // Audio thread reads; message thread writes.
    std::atomic<int32_t> playing{-1};    // playing slot, or -1
    std::atomic<int32_t> pending{kNone}; // kNone / kStop / kStopNow / slot index
    // Launch options travelling with the pending request (Session P0).
    std::atomic<double> pendingQuant{-1.0}; // <0 = the global launch quantum
    std::atomic<bool> pendingLegato{false}; // keep the loop position across the switch
    std::atomic<float> pendingGain{1.0f};   // launch-velocity gain for the incoming slot
    std::atomic<int32_t> repeatSlot{-1};    // Repeat mode: retrigger this slot every quantum while held
    // Audio thread writes, UI reads: the playing slot's loop position (progress) and beats
    // played since it launched (follow actions / fixed-length capture).
    std::atomic<double> uiLocalBeats{0.0};
    std::atomic<double> playedBeats{0.0};

    // Audio-thread-only.
    double localBeats = 0.0; // absolute position within the loop clock
    float gain = 1.0f;       // launch-velocity gain of the playing slot

    void requestLaunch(int32_t slot, double quant = -1.0, bool legato = false, float g = 1.0f);
    void requestStop() {
        repeatSlot.store(-1, std::memory_order_relaxed);
        pending.store(kStop, std::memory_order_relaxed);
    }
    void requestStopNow() {
        repeatSlot.store(-1, std::memory_order_relaxed);
        pending.store(kStopNow, std::memory_order_relaxed);
    }

    // Audio thread: if a pending request exists and a quantize boundary falls in
    // this block (or quant<=0), apply it. Returns true if `playing` changed
    // (so the caller can flush hanging notes).
    bool maybeApply(double blockStartBeats, double blockBeats, double globalQuant);

    // Audio thread, after rendering a block: advance the played counter and publish the
    // loop position for the UI.
    void publish(double blockBeats);
};

} // namespace nota
