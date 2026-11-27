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

namespace nota::media {
struct SampleBuffer;
} // namespace nota::media

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Track {
  public:
    Track() = default;
    Track(std::weak_ptr<Engine::Impl> impl, int32_t trackId) noexcept : impl_(std::move(impl)), id_(trackId) {}

    int32_t id() const noexcept { return id_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    std::optional<engine::TrackInfo> info() const;
    std::string name() const;
    bool setName(const std::string& name);
    int32_t colorIndex() const;
    bool setColorIndex(int32_t colorIndex);
    int32_t pianoFold() const;
    bool setPianoFold(bool on);
    int32_t editLink() const;
    bool setEditLink(bool on);
    bool setGroup(int32_t groupId);

    void setVolume(float volume);
    void setPan(float pan);
    void setMute(bool muted);
    void setSolo(bool soloed);
    bool setCue(bool on);
    int32_t cue() const;
    void setArmed(bool armed);
    void setSend(int32_t bus, float level);
    float send(int32_t bus) const;
    int32_t returnIndex() const;
    bool meter(float& pkL, float& pkR, float& rmsL, float& rmsR) const;
    int32_t latencySamples() const;

    bool isFrozen() const;
    int64_t beginFreeze(double lengthBeats, double tailSec = 2.0, double startBeat = 0.0);
    void endFreeze();
    void cancelFreeze();
    int64_t takeFreezeCapture(float* out, int64_t capFrames);
    void unfreeze();
    int32_t freezeGetState(uint8_t* out, int32_t cap) const;
    void freezeSetState(const uint8_t* data, int32_t size);

    DeviceChain devices();
    const DeviceChain devices() const;

    int32_t clipCount() const;
    Clip clip(int32_t clipIndex) const;
    int32_t addAudioClip(const std::string& path, double startBeat);
    int32_t addAudioClipEx(
        const std::string& path, double startBeat, double offsetFrames, int64_t lenFrames, float gain);
    int32_t addAudioClipBuffer(const std::shared_ptr<media::SampleBuffer>& sample, double startBeat);
    int32_t pasteAudioFrames(const float* interleaved, int64_t frames, double startBeat, const std::string& name);
    int32_t addMidiClip(double startBeat, double lengthBeats);
    bool copyClip(int32_t clipIndex);
    bool cutClip(int32_t clipIndex);
    int32_t pasteClip(double atBeat);

    int32_t takeCount() const;
    bool takeInfo(int32_t index, engine::TakeInfo& out) const;
    int32_t takePeaks(int32_t index, float* outMinMax, int32_t maxPoints) const;
    bool compTake(int32_t takeId, double startBeat, double endBeat);
    bool deleteTake(int32_t takeId);
    bool flattenTakes();
    int32_t restoreTake(const std::string& path, int32_t number, int32_t group, double originBeat,
        double sourceOffsetFrames, int64_t lengthFrames);
    bool restoreJoinedEdges(const int32_t* edges, int32_t count);

    void setRecordInput(int32_t source);
    int32_t recordInput() const;
    void setInputChannels(int32_t channels);
    int32_t inputChannels() const;
    bool setInput(int32_t first, int32_t width);
    int32_t input() const;
    bool inputAvailable() const;
    void setMonitor(bool on);
    bool monitor() const;
    bool setMidiInput(int32_t source, int32_t channel);
    bool setMidiInputUid(const std::string& uid, int32_t channel);
    std::string midiInputUid() const;
    int32_t midiInputSource() const;
    int32_t midiInputChannel() const;
    void setMidiSource(int32_t sourceTrackId);
    int32_t midiSource() const;
    void setMidiOffsetMs(double ms);
    double midiOffsetMs() const;

    bool setArticulationMap(int32_t defaultId, const engine::ArticulationEntryData* entries, int32_t entryCount,
        const engine::ArticulationActionData* actions, int32_t actionCount, const char* json, int32_t jsonLen);
    bool clearArticulationMap();
    int32_t articulationMapJson(char* out, int32_t maxLen) const;

    void noteOn(int32_t pitch, float velocity);
    void noteOff(int32_t pitch, float releaseVelocity = 0.0f);
    void noteExpression(int32_t pitch, int32_t dim, float value);
    void controller(int32_t ctl, float value);

    int32_t automationLaneCount() const;
    AutomationLane automationLane(int32_t laneIndex) const;
    int32_t addAutomationLane(int32_t target, int32_t deviceIndex, int32_t paramIndex);
    int32_t addPluginAutomationLane(int32_t deviceIndex, const char* paramId);
    int32_t addNestedAutomationLane(
        int32_t target, int32_t host, int32_t chainIndex, int32_t slot, int32_t paramIndex, const char* paramId);
    bool removeAutomationLane(int32_t laneIndex);
    float deliveredController(int32_t ctl) const;

    int32_t addModulator(int32_t kind);
    bool removeModulator(int32_t modId);
    int32_t modulatorCount() const;
    int32_t modulatorIdAt(int32_t index) const;
    Modulator modulator(int32_t modId) const;
    int32_t addCvLink(int32_t modId, int32_t device, int32_t param);
    int32_t addCvLinkTo(int32_t modId, int32_t targetTrack, int32_t device, int32_t param);
    int32_t addCvLinkFromParam(int32_t srcDevice, int32_t srcParam, int32_t targetTrack, int32_t device, int32_t param);
    int32_t addCvLinkToTarget(int32_t modId, int32_t targetKind, int32_t targetTrack, int32_t device, int32_t param);
    int32_t addCvLinkFromParamToTarget(
        int32_t srcDev, int32_t srcParam, int32_t targetKind, int32_t targetTrack, int32_t device, int32_t param);
    int32_t addNestedCvLink(int32_t sourceKind, int32_t modId, int32_t srcDev, int32_t srcChain, int32_t srcSlot,
        int32_t srcParam, int32_t targetKind, int32_t targetTrack, int32_t targetDevice, int32_t tgtChain,
        int32_t tgtSlot, int32_t targetParam);
    bool removeCvLink(int32_t index);
    int32_t cvLinkCount() const;
    CvLink cvLink(int32_t index) const;
    bool paramModulated(int32_t targetKind, int32_t device, int32_t param) const;

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t id_ = -1;
};

} // namespace nota
