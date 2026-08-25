// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Transport / musical clock (AR-9). The single source of time. Owned and
// mutated by the audio thread; UI-visible state is mirrored into atomics that
// the UI polls.

#pragma once

#include <atomic>
#include <cstdint>

#include "TransportInfo.h"

namespace nota {

class Transport {
public:
    void setSampleRate(double sr) { sampleRate_ = sr; }
    double sampleRate() const { return sampleRate_; }

    // --- control (applied on the audio thread) ---
    void play()  { playing_ = true;  publish(); }
    // Stop returns the playhead to the start anchor (the last seeked / insert-marker
    // position), so the next Play begins where the user launched from — not where it
    // happened to halt. Classic transport behaviour.
    void stop()  { playing_ = false; playheadSamples_ = startAnchorSamples_; publish(); }
    void setBpm(double bpm) { if (bpm > 0) bpm_ = bpm; publish(); }
    double bpm() const { return bpm_; }
    void setTimeSignature(int num, int denom) { if (num > 0 && denom > 0) { tsNum_ = num; tsDenom_ = denom; } }
    void setMetronome(bool on) { metronome_ = on; }
    void setLoop(bool enabled, double startBeat, double endBeat) {
        loopEnabled_ = enabled && endBeat > startBeat;
        loopStartBeat_ = startBeat;
        loopEndBeat_ = endBeat;
    }
    // Seeking sets the insert marker: the playhead moves here and this becomes the
    // start anchor that Stop rewinds to and the next Play launches from.
    void seekBeats(double beat) { playheadSamples_ = beat * samplesPerBeat(); startAnchorSamples_ = playheadSamples_; publish(); }

    // --- queries ---
    double samplesPerBeat() const { return sampleRate_ * 60.0 / bpm_; }
    double positionBeats() const { return playheadSamples_ / samplesPerBeat(); }
    double playheadSamples() const { return playheadSamples_; }
    bool   isPlaying() const { return playing_; }
    bool   metronomeEnabled() const { return metronome_; }
    int    beatsPerBar() const { return tsNum_; }

    // Advance the clock by `frames` (no loop wrap — processBlock splits blocks at
    // loop boundaries and calls wrapToLoopStart() at the wrap, so looping is
    // sample-accurate and the playhead lands exactly on loopStart).
    void advanceBy(int32_t frames) {
        if (playing_) playheadSamples_ += frames;
        publish();
    }
    // Reset the playhead to the loop start (called at a sample-accurate wrap).
    void wrapToLoopStart() { playheadSamples_ = loopStartBeat_ * samplesPerBeat(); publish(); }

    bool   isLooping() const { return loopEnabled_; }
    double loopStartSamples() const { return loopStartBeat_ * samplesPerBeat(); }
    double loopEndSamples() const { return loopEndBeat_ * samplesPerBeat(); }
    int    timeSigDenom() const { return tsDenom_; }

    // Per-block transport snapshot for hosted plugins (AudioPlayHead sync). Built
    // from this block's start position (samples); everything else is live state.
    // Audio thread, called by the engine right before each track renders.
    TransportInfo transportInfo(double blockStartSamples) const {
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

    // Atomic mirror for the UI (poll these; never touch the private fields).
    double uiPositionBeats() const { return uiBeats_.load(std::memory_order_relaxed); }
    bool   uiIsPlaying() const { return uiPlaying_.load(std::memory_order_relaxed); }

private:
    void publish() {
        uiBeats_.store(positionBeats(), std::memory_order_relaxed);
        uiPlaying_.store(playing_, std::memory_order_relaxed);
    }

    double sampleRate_ = 44100.0;
    double bpm_ = 120.0;
    int    tsNum_ = 4, tsDenom_ = 4;
    bool   playing_ = false;
    double playheadSamples_ = 0.0;
    double startAnchorSamples_ = 0.0;   // launch position Stop rewinds to (insert marker)
    bool   loopEnabled_ = false;
    double loopStartBeat_ = 0.0, loopEndBeat_ = 0.0;
    bool   metronome_ = false;

    std::atomic<double> uiBeats_{0.0};
    std::atomic<bool>   uiPlaying_{false};
};

} // namespace nota
