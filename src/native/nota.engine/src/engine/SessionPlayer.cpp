// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "SessionPlayer.h"

namespace nota {

void SessionPlayer::requestLaunch(int32_t slot, double quant, bool legato, float g, int32_t gen) {
    pendingQuant.store(quant, std::memory_order_relaxed);
    pendingLegato.store(legato, std::memory_order_relaxed);
    pendingGain.store(g, std::memory_order_relaxed);
    request.store(pack(slot, gen));
}

bool SessionPlayer::cancelSlot(int32_t slot) {
    uint64_t r = request.load();
    if (slot < 0 || slotOf(r) != slot) return false;
    if (!request.compare_exchange_strong(r, kIdle)) return false;
    int32_t rep = slot;
    repeatSlot.compare_exchange_strong(rep, -1, std::memory_order_relaxed);
    return true;
}

bool SessionPlayer::cancelGen(int32_t scene, int32_t gen) {
    uint64_t r = request.load();
    const int32_t p = slotOf(r);
    if (p == kNone || p == kStopNow) return false;
    // Its own scene launch's requests, or a direct launch of the row's slot. A request another
    // scene launch queued for this row (one that has started) is not this cancel's to withdraw.
    const int32_t rg = genOf(r);
    if (!(gen > 0 && rg == gen) && !(p == scene && rg == 0)) return false;
    if (!request.compare_exchange_strong(r, kIdle)) return false;
    if (p >= 0) {
        int32_t rep = p;
        repeatSlot.compare_exchange_strong(rep, -1, std::memory_order_relaxed);
    }
    return true;
}

SessionPlayer::GenView SessionPlayer::viewGen(int32_t gen) const {
    const uint32_t s1 = applySeq.load();
    const uint64_t r = request.load();
    const bool applied = appliedGen.load() == gen;
    const uint32_t s2 = applySeq.load();
    const bool busy = (s1 & 1u) != 0 || s1 != s2;
    const int32_t p = slotOf(r);
    return {applied, !applied && genOf(r) == gen && p != kNone && p != kStopNow, busy};
}

bool SessionPlayer::maybeApply(double blockStartBeats, double blockBeats, double globalQuant, bool loopSeam) {
    uint64_t r = request.load(std::memory_order_relaxed);
    const int32_t p = slotOf(r);
    if (p == kNone) return false;

    const double pq = pendingQuant.load(std::memory_order_relaxed);
    const double quant = (p == kStop || p == kStopNow || pq < 0.0) ? globalQuant : pq;
    bool boundary;
    if (p == kStopNow || quant <= 0.0 || loopSeam) {
        boundary = true;
    } else {
        const double b1 = blockStartBeats + blockBeats;
        boundary = blockStartBeats <= 1e-9 || std::floor(blockStartBeats / quant) != std::floor(b1 / quant);
    }
    if (!boundary) return false;

    // A launch may have replaced the request since we read it; only consume it if not. The
    // generation is the consumed request's own, recorded inside the seqlock (viewGen).
    applySeq.fetch_add(1);
    const bool consumed = request.compare_exchange_strong(r, kIdle);
    if (consumed) appliedGen.store(genOf(r));
    applySeq.fetch_add(1);
    if (!consumed) return false;
    // The caller flushes instrument voices when a launch/stop applies. Discard releases from
    // the outgoing slot so they cannot cut a newly launched slot's switch on the same key.
    articulationReleases.count = 0;
    if (p == kStop || p == kStopNow) {
        playing.store(-1, std::memory_order_relaxed);
    } else {
        const bool legato =
            pendingLegato.load(std::memory_order_relaxed) && playing.load(std::memory_order_relaxed) >= 0;
        playing.store(p, std::memory_order_relaxed);
        if (!legato) localBeats = 0.0;
        gain = pendingGain.load(std::memory_order_relaxed);
        resetFollowCount();
    }
    return true;
}

void SessionPlayer::publish(double blockBeats) {
    playedBeats.store(playedBeats.load(std::memory_order_relaxed) + blockBeats, std::memory_order_relaxed);
    uiLocalBeats.store(localBeats, std::memory_order_relaxed);
}

} // namespace nota
