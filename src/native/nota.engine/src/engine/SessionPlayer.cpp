// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "SessionPlayer.h"

namespace nota {

void SessionPlayer::requestLaunch(int32_t slot, double quant, bool legato, float g) {
    pendingQuant.store(quant, std::memory_order_relaxed);
    pendingLegato.store(legato, std::memory_order_relaxed);
    pendingGain.store(g, std::memory_order_relaxed);
    pending.store(slot, std::memory_order_relaxed);
}

bool SessionPlayer::maybeApply(double blockStartBeats, double blockBeats, double globalQuant) {
    const int32_t p = pending.load(std::memory_order_relaxed);
    if (p == kNone) return false;

    const double pq = pendingQuant.load(std::memory_order_relaxed);
    const double quant = (p == kStop || p == kStopNow || pq < 0.0) ? globalQuant : pq;
    bool boundary;
    if (p == kStopNow || quant <= 0.0) {
        boundary = true;
    } else {
        const double b1 = blockStartBeats + blockBeats;
        boundary = blockStartBeats <= 1e-9 || std::floor(blockStartBeats / quant) != std::floor(b1 / quant);
    }
    if (!boundary) return false;

    // A launch may have replaced the request between our two loads; only consume it if not.
    int32_t expect = p;
    if (!pending.compare_exchange_strong(expect, kNone, std::memory_order_relaxed)) return false;
    if (p == kStop || p == kStopNow) {
        playing.store(-1, std::memory_order_relaxed);
    } else {
        const bool legato =
            pendingLegato.load(std::memory_order_relaxed) && playing.load(std::memory_order_relaxed) >= 0;
        playing.store(p, std::memory_order_relaxed);
        if (!legato) localBeats = 0.0;
        gain = pendingGain.load(std::memory_order_relaxed);
        playedBeats.store(0.0, std::memory_order_relaxed);
    }
    return true;
}

void SessionPlayer::publish(double blockBeats) {
    playedBeats.store(playedBeats.load(std::memory_order_relaxed) + blockBeats, std::memory_order_relaxed);
    uiLocalBeats.store(localBeats, std::memory_order_relaxed);
}

} // namespace nota
