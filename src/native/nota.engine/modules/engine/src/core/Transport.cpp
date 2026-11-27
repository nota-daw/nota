// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "core/Transport.h"
#include "core/EnginePrelude.h"

#include <atomic>
#include <chrono>
#include <algorithm>
#include <cmath>
#include <cstdint>

namespace nota::detail {

double Transport::mappedBpm() const {
    return map_ ? map_->clockBpm(mappedBeatAtSamples(playheadSamples_)) : uiBpm();
}

double Transport::mappedSamplesAtBeat(double beat) const {
    return map_->secondsAt(beat) * sampleRate_;
}

double Transport::mappedBeatAtSamples(double sample) const {
    return map_->beatAt(sample / sampleRate_);
}

void Transport::setLoop(bool enabled, double startBeat, double endBeat) {
    ++presentationEpoch_;
    const bool on = enabled && endBeat > startBeat;
    const bool changed = on != loopEnabled_ || startBeat != loopStartBeat_ || endBeat != loopEndBeat_;
    loopEnabled_ = on;
    loopStartBeat_ = startBeat;
    loopEndBeat_ = endBeat;
    // A loop switched on or moved so that the playhead is already past its end lets it play
    // on (#981). An unchanged resend keeps the state: the playhead may sit exactly on the end
    // it rolled into, waiting for the wrap.
    if (changed) updatePastLoopEnd();
}

void Transport::advanceBy(int32_t frames) {
    if (playing_) playheadSamples_ += frames;
    publish();
}

TransportInfo Transport::transportInfo(double blockStartSamples) const {
    TransportInfo ti;
    ti.bpm = bpm();
    ti.tsNum = tsNum_;
    ti.tsDenom = tsDenom_;
    ti.ppqPosition = beatAtSamples(blockStartSamples);
    ti.timeInSamples = static_cast<int64_t>(blockStartSamples);
    ti.timeInSeconds = sampleRate_ > 0.0 ? blockStartSamples / sampleRate_ : 0.0;
    ti.isPlaying = playing_;
    ti.isLooping = wrapsAtLoopEnd(); // played on past the loop: plug-ins see a straight clock (#981)
    ti.ppqLoopStart = loopStartBeat_;
    ti.ppqLoopEnd = loopEndBeat_;
    return ti;
}

void Transport::publish() {
    uiPastLoopEnd_.store(loopEnabled_ && pastLoopEnd_, std::memory_order_relaxed);
    if (pastLoopEnd_) uiPastLoopFrom_.store(beatAtSamples(pastLoopFromSamples_), std::memory_order_relaxed);
    // The ordinary clock needs one conversion factor, without querying mapped
    // state separately for each published value.
    if (!map_ && !lastMap_) {
        uiBpm_.store(bpm_, std::memory_order_relaxed);
        uiBeats_.store(playheadSamples_ / fixedSpb_, std::memory_order_relaxed);
        uiPlaying_.store(playing_, std::memory_order_relaxed);
        uiAnchorBeats_.store(startAnchorSamples_ / fixedSpb_, std::memory_order_relaxed);
        return;
    }
    uiBpm_.store(bpm(), std::memory_order_relaxed);
    uiBeats_.store(positionBeats(), std::memory_order_relaxed);
    uiPlaying_.store(playing_, std::memory_order_relaxed);
    uiAnchorBeats_.store(beatAtSamples(startAnchorSamples_), std::memory_order_relaxed);
}

static int64_t presentationNow() {
    return std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now().time_since_epoch())
        .count();
}

void Transport::publishPresentation(
    double startSamples, int32_t playingFrames, int32_t leadingFrames, int32_t windowFrames) {
    static_assert(std::atomic<double>::is_always_lock_free && std::atomic<int64_t>::is_always_lock_free &&
                  std::atomic<uint32_t>::is_always_lock_free && std::atomic<int32_t>::is_always_lock_free);
    if (presentationPublishedEpoch_ != presentationEpoch_ || !playing_) {
        presentationPublishedEpoch_ = presentationEpoch_;
        presentationFrames_ = 0;
        presentationOrigin_ = startSamples;
    }
    // Loop seams round to whole samples. Re-anchor the history at each real block
    // start so fractional-sample loop lengths cannot accumulate display drift.
    if (presentationFrames_ > 0 && playingFrames > 0) {
        double predicted = presentationOrigin_ + static_cast<double>(presentationFrames_);
        const double actual = startSamples;
        const double loopStart = samplesAtBeat(loopStartBeat_);
        const double loopEnd = samplesAtBeat(loopEndBeat_);
        const double loopLength = loopEnd - loopStart;
        if (wrapsAtLoopEnd() && loopLength > 0 && predicted >= loopEnd)
            predicted = loopStart + std::fmod(predicted - loopStart, loopLength);
        double correction = actual - predicted;
        if (wrapsAtLoopEnd() && loopLength > 0 && actual >= loopStart && predicted >= loopStart)
            correction = std::remainder(correction, loopLength);
        presentationOrigin_ += correction;
    }
    presentationFrames_ += playingFrames;
    const auto span = std::min<int64_t>(presentationFrames_, std::max(windowFrames, playingFrames));
    double startBeat = beatAtSamples(presentationOrigin_ + static_cast<double>(presentationFrames_ - span));
    if (wrapsAtLoopEnd() && startBeat >= loopEndBeat_)
        startBeat = loopStartBeat_ + std::fmod(startBeat - loopStartBeat_, loopEndBeat_ - loopStartBeat_);
    presentationSequence_.fetch_add(1, std::memory_order_relaxed);
    presentationStart_.store(startBeat, std::memory_order_release);
    presentationEnd_.store(positionBeats(), std::memory_order_release);
    presentationBpm_.store(bpm(), std::memory_order_release);
    presentationLoopStart_.store(loopStartBeat_, std::memory_order_release);
    presentationLoopEnd_.store(loopEndBeat_, std::memory_order_release);
    presentationDuration_.store(
        sampleRate_ > 0 ? static_cast<double>(span) / sampleRate_ : 0.0, std::memory_order_release);
    presentationDelay_.store(
        sampleRate_ > 0 ? static_cast<double>(leadingFrames) / sampleRate_ : 0.0, std::memory_order_release);
    presentationPlaying_.store(playing_ ? 1 : 0, std::memory_order_release);
    presentationLooping_.store(wrapsAtLoopEnd() ? 1 : 0, std::memory_order_release); // past it: no fold (#981)
    presentationStamp_.store(presentationNow(), std::memory_order_release);
    presentationSequence_.fetch_add(1, std::memory_order_release);
}

bool Transport::presentationSnapshot(TransportSnapshot& out) const {
    for (int attempt = 0; attempt < 3; ++attempt) {
        const auto before = presentationSequence_.load(std::memory_order_acquire);
        if (before & 1) continue;
        TransportSnapshot s{};
        s.startBeat = presentationStart_.load(std::memory_order_acquire);
        s.positionBeats = presentationEnd_.load(std::memory_order_acquire);
        s.bpm = presentationBpm_.load(std::memory_order_acquire);
        s.loopStart = presentationLoopStart_.load(std::memory_order_acquire);
        s.loopEnd = presentationLoopEnd_.load(std::memory_order_acquire);
        s.durationSeconds = presentationDuration_.load(std::memory_order_acquire);
        const auto delay = presentationDelay_.load(std::memory_order_acquire);
        s.playing = presentationPlaying_.load(std::memory_order_acquire);
        s.looping = presentationLooping_.load(std::memory_order_acquire);
        const auto stamp = presentationStamp_.load(std::memory_order_acquire);
        if (before != presentationSequence_.load(std::memory_order_acquire)) continue;
        if (stamp == 0) return false;
        s.elapsedSeconds = static_cast<double>(presentationNow() - stamp) * 1.0e-9 - delay;
        out = s;
        return true;
    }
    return false;
}

} // namespace nota::detail
