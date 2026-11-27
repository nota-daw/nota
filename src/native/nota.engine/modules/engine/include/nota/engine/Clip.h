// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Types.h"

#include <cstdint>
#include <memory>
#include <optional>
#include <string>
#include <utility>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Clip {
  public:
    Clip() = default;
    Clip(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t clipIndex) noexcept
        : impl_(std::move(impl)), trackId_(trackId), index_(clipIndex) {}

    int32_t trackId() const noexcept { return trackId_; }
    int32_t index() const noexcept { return index_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    std::optional<engine::ClipInfo> info() const;
    std::optional<engine::AudioClipInfo> audioInfo() const;

    std::string name() const;
    bool setName(const std::string& name);
    bool setActive(bool active);

    bool move(double newStartBeat);
    bool moveToTrack(int32_t destTrackId, double newStartBeat);
    bool trim(double newStartBeat, double newLengthBeats);
    bool resizeAudio(double newStartBeat, double newLengthBeats);
    bool setSourceRegion(double offsetFrames, int64_t lengthFrames);
    bool setWarpTrim(double playStart, double playEnd);
    int32_t split(double atBeat);
    int32_t duplicate();
    bool remove();

    bool setGain(float gain);
    std::optional<engine::ClipAdsr> adsr() const;
    bool setAdsr(const engine::ClipAdsr& adsr);
    bool setPitch(float semitones);
    bool setReverse(bool reversed);
    bool setWarp(bool enabled, int32_t mode);
    double autoWarp(double knownBpm = 0.0);
    double beatWarp();
    bool setWarpLength(double beats);
    bool setWarpMarkers(const double* srcFrames, const double* beats, int32_t count);
    int32_t warpMarkers(double* outSrc, double* outBeat, int32_t maxCount) const;

    int32_t noteCount() const;
    int32_t notes(engine::NoteData* out, int32_t maxNotes) const;
    bool setNotes(const engine::NoteData* notes, int32_t count);
    bool setNotesLive(const engine::NoteData* notes, int32_t count);

    int32_t peaks(engine::ClipPeakMode mode, float* outMinMax, int32_t maxPoints, float* outRms = nullptr) const;
    int32_t warpPeaksRange(
        double beat0, double beat1, float* outMinMax, int32_t maxPoints, float* outRms = nullptr) const;
    int32_t sourcePeaksRange(
        double frame0, double frame1, float* outMinMax, int32_t maxPoints, float* outRms = nullptr) const;
    int32_t audioMono(float* out, int32_t maxFrames, double* outSr) const;

    int32_t takeId() const;
    bool setTakeId(int32_t takeId);
    int32_t joinedEdges() const;

    int32_t getAudioVolumeEnvelope(engine::AutomationPointData* out, int32_t cap) const;
    bool setAudioVolumeEnvelope(const engine::AutomationPointData* pts, int32_t count);
    int32_t getAudioPanEnvelope(engine::AutomationPointData* out, int32_t cap) const;
    bool setAudioPanEnvelope(const engine::AutomationPointData* pts, int32_t count);
    int32_t getMidiEnvelope(int32_t kind, engine::AutomationPointData* out, int32_t cap) const;
    bool setMidiEnvelope(int32_t kind, const engine::AutomationPointData* pts, int32_t count);

    int32_t ctlList(int32_t* out, int32_t cap) const;
    int32_t ctlGet(int32_t ctl, engine::AutomationPointData* out, int32_t cap) const;
    bool ctlSet(int32_t ctl, const engine::AutomationPointData* pts, int32_t count);

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
    int32_t index_ = -1;
};

} // namespace nota
