// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/ModelTypes.h"

#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Recorder {
  public:
    Recorder() = default;
    explicit Recorder(std::weak_ptr<Engine::Impl> impl) noexcept : impl_(std::move(impl)) {}

    bool isValid() const noexcept { return !impl_.expired(); }
    explicit operator bool() const noexcept { return isValid(); }

    void setRecording(bool on);
    bool isRecording() const;
    int32_t retryTake();
    int32_t startStatus() const;
    void poll();

    bool startAudio(int32_t trackId);
    bool startAudioLanes(const std::vector<int32_t>& trackIds);
    void stopAudio();
    bool isAudioRecording() const;
    int32_t audioRecordTrackId() const;
    double audioRecordStartBeat() const;
    double audioRecordLengthBeats(int32_t trackId = 0) const;
    int32_t audioRecordPeaks(float* outMinMax, int32_t maxPoints, int32_t trackId = 0) const;
    int32_t audioRecordingTracks(int32_t* out, int32_t cap) const;
    int32_t skipped(int32_t* trackIds, int32_t* reasons, int32_t cap) const;
    int32_t heldNotes(
        engine::RecordHeldNote* out, int32_t maxN, int32_t* trackId, int32_t* sessionScene, double* nowBeat) const;

    void setDisabledInputs(const int32_t* channels, int32_t count);
    void setTestInputDevice(int32_t channels);
    void pushInputFramesForTest(const float* interleavedStereo, int32_t frames);
    void pushInputFramesForTestN(const float* interleaved, int32_t frames, int32_t channels);
    void feedInputForTest(const float* interleaved, int32_t frames, int32_t channels);
    void inputBlockForTest(const float* interleaved, int32_t frames, int32_t channels);
    void pushMonitorFramesForTest(const float* interleavedStereo, int32_t frames);
    void setMonitorInputForTest(double sampleRate, int32_t blockFrames);

    bool takeRecordTest(int32_t trackId, const float* interleavedStereo, int32_t frames, double startBeat);
    bool takeRecordTestN(int32_t trackId, const float* interleaved, int32_t channels, int32_t frames, double startBeat);
    int32_t takeRecordTestArmed(const float* interleaved, int32_t channels, int32_t frames, double startBeat,
        int32_t disarmTrackId = 0, int32_t disarmAtFrame = -1);

  private:
    std::weak_ptr<Engine::Impl> impl_;
};

} // namespace nota
