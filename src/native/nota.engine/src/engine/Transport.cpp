// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "Transport.h"

namespace nota {

void Transport::setLoop(bool enabled, double startBeat, double endBeat) {
    loopEnabled_ = enabled && endBeat > startBeat;
    loopStartBeat_ = startBeat;
    loopEndBeat_ = endBeat;
}

void Transport::advanceBy(int32_t frames) {
    if (playing_) playheadSamples_ += frames;
    publish();
}

TransportInfo Transport::transportInfo(double blockStartSamples) const {
    const double spb = samplesPerBeat();
    TransportInfo ti;
    ti.bpm = bpm_;
    ti.tsNum = tsNum_;
    ti.tsDenom = tsDenom_;
    ti.ppqPosition = spb > 0.0 ? blockStartSamples / spb : 0.0;
    ti.timeInSamples = static_cast<int64_t>(blockStartSamples);
    ti.timeInSeconds = sampleRate_ > 0.0 ? blockStartSamples / sampleRate_ : 0.0;
    ti.isPlaying = playing_;
    ti.isLooping = loopEnabled_;
    ti.ppqLoopStart = loopStartBeat_;
    ti.ppqLoopEnd = loopEndBeat_;
    return ti;
}

void Transport::publish() {
    uiBeats_.store(positionBeats(), std::memory_order_relaxed);
    uiPlaying_.store(playing_, std::memory_order_relaxed);
}

} // namespace nota
