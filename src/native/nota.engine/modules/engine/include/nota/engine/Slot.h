// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Types.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Slot {
  public:
    Slot() = default;
    Slot(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t sceneIndex) noexcept
        : impl_(std::move(impl)), trackId_(trackId), scene_(sceneIndex) {}

    int32_t trackId() const noexcept { return trackId_; }
    int32_t scene() const noexcept { return scene_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    int32_t state() const;
    double length() const;
    bool setLength(double beats);
    float gain() const;
    bool setGain(float gain);

    void launch(float velocity = 1.0f);
    void release();
    void stop();
    bool cancelLaunch();
    bool isQueued() const;
    bool isRecording() const;
    void record();
    void stopRecord();
    void backToArrangement();
    int32_t playingSlotOnTrack() const;
    double positionOnTrack() const;

    bool addMidiClip(double lengthBeats);
    bool addAudioClip(
        const std::string& path, double lengthBeats, double offsetFrames, int64_t lengthFrames, float gain);
    bool addAudioFile(const std::string& path);
    bool clear();

    bool audioInfo(engine::SessionAudioSlotInfo& out) const;
    int32_t noteCount() const;
    int32_t notes(engine::NoteData* out, int32_t maxNotes) const;
    bool setNotes(const engine::NoteData* notes, int32_t count);

    bool props(engine::SessionClipProps& out) const;
    bool setProps(const engine::SessionClipProps& p);
    std::string name() const;
    bool setName(const std::string& name);
    bool stopButton() const;
    bool setStopButton(bool on);
    bool takeLength(engine::SlotTakeLength& out) const;
    bool setTakeLength(const engine::SlotTakeLength& v);
    double takeBeats() const;
    bool landingRule(engine::SlotLandingRule& out) const;
    bool setLandingRule(const engine::SlotLandingRule& v);

    bool copyTo(int32_t dstTrackId, int32_t dstScene) const;
    int32_t peaks(float* outMinMax, int32_t maxPoints) const;
    int32_t toArrangement(double startBeat);
    bool fromArrangementClip(int32_t clipIndex);
    bool fromArrangementAudioClip(int32_t clipIndex);

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
    int32_t scene_ = -1;
};

} // namespace nota
