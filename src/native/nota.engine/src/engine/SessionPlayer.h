// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M5-2: per-track Session playback state. A stable object shared across graph
// snapshots (like Instrument/CompensationDelay) so playback survives edits.
// Launch/stop requests come from the message thread (atomics); the audio thread
// applies them at the next quantize boundary and advances the looping position.

#pragma once

#include "ArticulationSchedule.h"

#include <atomic>
#include <cmath>
#include <cstdint>

namespace nota {

class SessionPlayer {
  public:
    // Keep make_shared<SessionPlayer>() from zero-filling unused pending-release storage.
    // Every live field has its own initializer; pending entries are read only below count.
    SessionPlayer() noexcept {}

    static constexpr int32_t kNone = -2;    // no pending request
    static constexpr int32_t kStop = -1;    // pending stop (quantized)
    static constexpr int32_t kStopNow = -3; // pending stop, applied at the next block (per-track "back to arrangement")

    // Audio thread reads; message thread writes.
    std::atomic<int32_t> playing{-1};    // playing slot, or -1
    // The queued request: what happens at the next boundary (kNone / kStop / kStopNow / a slot
    // index, bits 0-31), the generation of the scene launch that queued it (bits 32-55; 0 = a direct
    // launch, a stop, a follow action or a repeat) and the ticket of a cancel aimed at it (bits
    // 56-63; 0 = none) (#480). One atomic, so a request, its generation and its cancel mark are
    // always read, replaced and consumed together: a newer request replaces the whole word, so a
    // cancel meant for the older one can never withdraw it.
    static constexpr uint32_t kGenMask = 0xFFFFFFu; // generations wrap at 24 bits, skipping 0
    static constexpr uint64_t kTicketMask = 0xFFull << 56;
    static constexpr uint64_t pack(int32_t slot, int32_t gen) {
        return (static_cast<uint64_t>(static_cast<uint32_t>(gen) & kGenMask) << 32) | static_cast<uint32_t>(slot);
    }
    static constexpr int32_t slotOf(uint64_t r) { return static_cast<int32_t>(static_cast<uint32_t>(r)); }
    static constexpr int32_t genOf(uint64_t r) { return static_cast<int32_t>((r >> 32) & kGenMask); }
    static constexpr uint32_t ticketOf(uint64_t r) { return static_cast<uint32_t>(r >> 56); }
    static constexpr uint64_t withTicket(uint64_t r, uint32_t t) {
        return (r & ~kTicketMask) | (static_cast<uint64_t>(t & 0xFFu) << 56);
    }
    static constexpr uint64_t kIdle = static_cast<uint32_t>(kNone); // pack(kNone, 0)
    std::atomic<uint64_t> request{kIdle};
    int32_t pendingSlot() const { return slotOf(request.load(std::memory_order_relaxed)); }
    // Launch options travelling with the pending request (Session P0).
    std::atomic<double> pendingQuant{-1.0}; // <0 = the global launch quantum
    std::atomic<bool> pendingLegato{false}; // keep the loop position across the switch
    std::atomic<float> pendingGain{1.0f};   // launch-velocity gain for the incoming slot
    std::atomic<int32_t> repeatSlot{-1};    // Repeat mode: retrigger this slot every quantum while held
    // #480: the generation of the last scene-launch request that applied, so the engine can tell a
    // scene launch that started from one that was cancelled or replaced. Only scene requests write
    // it: a clip follow or a direct launch applying afterwards must not erase that proof before the
    // UI has seen it. applySeq is odd while maybeApply consumes a request and records its
    // generation: a reader that sees it change re-reads later (seqlock).
    std::atomic<int32_t> appliedGen{0};
    std::atomic<uint32_t> applySeq{0};
    // Audio thread writes, UI reads: the playing slot's loop position (progress) and beats
    // played since it launched (follow actions / fixed-length capture).
    std::atomic<double> uiLocalBeats{0.0};
    std::atomic<double> playedBeats{0.0};

    // Audio-thread-only.
    double localBeats = 0.0; // absolute position within the loop clock
    ArticulationReleaseState articulationReleases; // pending latching keys, timed by rendered frames
    float gain = 1.0f;       // launch-velocity gain of the playing slot
    // Follow bypass (#499): the follow check counts playedBeats - followBase. followHeld is set
    // while the playing slot's follow is bypassed; when the bypass ends, followBase moves to the
    // last multiple of the follow time already played, so nothing fires as a catch-up.
    bool followHeld = false;
    double followBase = 0.0;
    void resetFollowCount() {
        playedBeats.store(0.0, std::memory_order_relaxed);
        followBase = 0.0;
        followHeld = false;
    }

    void requestLaunch(int32_t slot, double quant = -1.0, bool legato = false, float g = 1.0f, int32_t gen = 0);
    void requestStop(int32_t gen = 0) {
        repeatSlot.store(-1, std::memory_order_relaxed);
        request.store(pack(kStop, gen));
    }
    void requestStopNow() {
        repeatSlot.store(-1, std::memory_order_relaxed);
        request.store(pack(kStopNow, 0));
    }
    // Audio thread (follow actions, Repeat): queue `slot` only if nothing is queued.
    bool queueIfIdle(int32_t slot) {
        uint64_t expect = kIdle;
        return request.compare_exchange_strong(expect, pack(slot, 0), std::memory_order_relaxed);
    }
    // Message-thread view of whether launch `gen` has started on this track or still waits, read
    // consistently against maybeApply. busy: a request was being consumed — read again later.
    struct GenView { bool applied, queued, busy; };
    GenView viewGen(int32_t gen) const;

    // Cancelling a queued request (#480), in two steps. Message thread: mark the request it sees now
    // with `ticket`, if `match` says it is a target (never a kStopNow). Audio thread, as it drains the
    // cancel: withdraw the requests still carrying that ticket — or, `keep`, just clear the mark (the
    // scene launch had started). A request replaced in between lost the mark, so it stays.
    template <typename Match> bool markCancel(uint32_t ticket, Match match) {
        uint64_t r = request.load();
        while (slotOf(r) != kNone && slotOf(r) != kStopNow && match(r))
            if (request.compare_exchange_weak(r, withTicket(r, ticket))) return true;
        return false;
    }
    bool withdrawTicket(uint32_t ticket, bool keep);

    // Audio thread: if a pending request exists and a quantize boundary falls in
    // this block (or quant<=0), apply it. `loopSeam`: the block starts where the transport
    // loop just wrapped — that counts as a boundary too, or a loop that holds no multiple of
    // the quantum would keep a launch waiting forever (#343). Returns true if `playing`
    // changed (so the caller can flush hanging notes).
    bool maybeApply(double blockStartBeats, double blockBeats, double globalQuant, bool loopSeam = false);

    // Audio thread, after rendering a block: advance the played counter and publish the
    // loop position for the UI.
    void publish(double blockBeats);
};
static_assert(SessionPlayer::kIdle == SessionPlayer::pack(SessionPlayer::kNone, 0));

} // namespace nota
