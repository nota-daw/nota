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
#include "nota/nota_engine.h"

namespace nota {

class Transport {
  public:
    void setSampleRate(double sr) { sampleRate_ = sr; ++presentationEpoch_; }
    double sampleRate() const { return sampleRate_; }

    // --- control (applied on the audio thread) ---
    void play() {
        if (!playing_) ++presentationEpoch_;
        playing_ = true;
        publish();
    }
    // Stop returns the playhead to the start anchor (the last seeked / insert-marker
    // position), so the next Play begins where the user launched from — not where it
    // happened to halt. Classic transport behaviour.
    void stop() {
        ++presentationEpoch_;
        playing_ = false;
        playheadSamples_ = startAnchorSamples_;
        publish();
    }
    void setBpm(double bpm) {
        if (bpm > 0 && bpm != bpm_) { bpm_ = bpm; ++presentationEpoch_; }
        publish();
    }
    double bpm() const { return bpm_; }
    void setTimeSignature(int num, int denom) {
        if (num > 0 && denom > 0) {
            tsNum_ = num;
            tsDenom_ = denom;
        }
    }
    void setMetronome(bool on) { metronome_ = on; }
    void setLoop(bool enabled, double startBeat, double endBeat);
    // Seeking sets the insert marker: the playhead moves here and this becomes the
    // start anchor that Stop rewinds to and the next Play launches from.
    void seekBeats(double beat) {
        ++presentationEpoch_;
        playheadSamples_ = beat * samplesPerBeat();
        startAnchorSamples_ = playheadSamples_;
        publish();
    }

    // --- queries ---
    double samplesPerBeat() const { return sampleRate_ * 60.0 / bpm_; }
    double positionBeats() const { return playheadSamples_ / samplesPerBeat(); }
    double playheadSamples() const { return playheadSamples_; }
    bool isPlaying() const { return playing_; }
    bool metronomeEnabled() const { return metronome_; }
    int beatsPerBar() const { return tsNum_; }

    // Advance the clock by `frames` (no loop wrap — processBlock splits blocks at
    // loop boundaries and calls wrapToLoopStart() at the wrap, so looping is
    // sample-accurate and the playhead lands exactly on loopStart).
    void advanceBy(int32_t frames);
    // Reset the playhead to the loop start (called at a sample-accurate wrap).
    void wrapToLoopStart() {
        playheadSamples_ = loopStartBeat_ * samplesPerBeat();
        publish();
    }

    bool isLooping() const { return loopEnabled_; }
    double loopStartSamples() const { return loopStartBeat_ * samplesPerBeat(); }
    double loopEndSamples() const { return loopEndBeat_ * samplesPerBeat(); }
    int timeSigDenom() const { return tsDenom_; }

    // Per-block transport snapshot for hosted plugins (AudioPlayHead sync). Built
    // from this block's start position (samples); everything else is live state.
    // Audio thread, called by the engine right before each track renders.
    TransportInfo transportInfo(double blockStartSamples) const;

    // Atomic mirror for the UI (poll these; never touch the private fields).
    double uiPositionBeats() const { return uiBeats_.load(std::memory_order_relaxed); }
    bool uiIsPlaying() const { return uiPlaying_.load(std::memory_order_relaxed); }

    // Publish once per completed block; readers may skip a frame, never delay audio.
    void publishPresentation(double startSamples, int32_t playingFrames, int32_t leadingFrames, int32_t windowFrames);
    bool presentationSnapshot(NotaTransportSnapshot& out) const;

  private:
    void publish();

    double sampleRate_ = 44100.0;
    double bpm_ = 120.0;
    int tsNum_ = 4, tsDenom_ = 4;
    bool playing_ = false;
    double playheadSamples_ = 0.0;
    double startAnchorSamples_ = 0.0; // launch position Stop rewinds to (insert marker)
    bool loopEnabled_ = false;
    double loopStartBeat_ = 0.0, loopEndBeat_ = 0.0;
    bool metronome_ = false;

    std::atomic<double> uiBeats_{0.0};
    std::atomic<bool> uiPlaying_{false};

    // Atomic payloads avoid data races even when a reader overlaps the writer.
    // A bounded version check rejects mixed blocks. Release/acquire on each field
    // makes observing any new payload also expose the writer's odd sequence.
    std::atomic<uint32_t> presentationSequence_{0};
    std::atomic<double> presentationStart_{0.0}, presentationEnd_{0.0}, presentationBpm_{120.0};
    std::atomic<double> presentationLoopStart_{0.0}, presentationLoopEnd_{0.0};
    std::atomic<double> presentationDuration_{0.0}, presentationDelay_{0.0};
    std::atomic<int64_t> presentationStamp_{0};
    std::atomic<int32_t> presentationPlaying_{0}, presentationLooping_{0};
    // Audio-thread history spans a hardware period when WASAPI batches smaller
    // client callbacks. Control discontinuities reset it before any interpolation.
    uint64_t presentationEpoch_ = 0, presentationPublishedEpoch_ = 0;
    int64_t presentationFrames_ = 0;
    double presentationOrigin_ = 0.0;
};

} // namespace nota
