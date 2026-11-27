// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TrackOps — track lifecycle, groups, send/return buses, track clipboard,
// freeze/bounce state, and built-in instrument / Rhythm kit telemetry.

#pragma once

#include "core/EnginePrelude.h"
#include "nota/engine/Types.h"

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::dsp {
class Device;
} // namespace nota::dsp

namespace nota::detail {

class EngineRuntime;
struct Graph;
class Track;

class TrackOps {
  public:
    explicit TrackOps(EngineRuntime& rt) noexcept : rt_(rt) {}

    TrackOps(const TrackOps&) = delete;
    TrackOps& operator=(const TrackOps&) = delete;

    // --- tracks, mixer, groups & returns ------------------------------------
    int32_t addAudioTrack();
    void setTrackVolume(int32_t trackId, float v);
    void setTrackPan(int32_t trackId, float p);
    void setTrackMute(int32_t trackId, bool m);
    void setTrackSolo(int32_t trackId, bool s);
    bool setTrackCue(int32_t trackId, bool on);
    int32_t trackCue(int32_t trackId) const;
    int32_t trackCount() const;
    int32_t duplicateTrack(int32_t trackId);
    int32_t addTrackLike(int32_t trackId);
    bool removeTrack(int32_t trackId);
    bool moveTrack(int32_t trackId, int32_t toIndex);

    int32_t addGroupTrack();
    int32_t createGroup(const int32_t* ids, int32_t n);
    bool ungroup(int32_t groupId);
    bool setTrackGroup(int32_t trackId, int32_t groupId);
    int32_t duplicateTracks(const int32_t* ids, int32_t n, int32_t* outIds, int32_t cap);
    bool removeTracks(const int32_t* ids, int32_t n);

    int32_t addReturnTrack();
    void setTrackSend(int32_t trackId, int32_t bus, float level);
    float trackSend(int32_t trackId, int32_t bus) const;
    int32_t returnTrackCount() const;
    int32_t trackReturnIndex(int32_t trackId) const;

    bool trackInfo(int32_t index, TrackInfo* out) const;
    bool clipInfo(int32_t trackId, int32_t clipIndex, ClipInfo* out) const;
    bool deleteClip(int32_t trackId, int32_t clipIndex);

    bool copyTrack(int32_t trackId);
    int32_t pasteTrack();
    bool copyTracks(const int32_t* ids, int32_t n);
    int32_t pasteTracks(int32_t afterTrackId, int32_t* outIds, int32_t cap);
    bool hasTrackClipboard() const;

    // --- freeze (M7) --------------------------------------------------------
    int64_t beginFreeze(int32_t trackId, double lengthBeats, double tailSec = 2.0, double startBeat = 0.0);
    void endFreeze(int32_t trackId);
    void cancelFreeze();
    int64_t takeFreezeCapture(float* out, int64_t capFrames);
    void unfreeze(int32_t trackId);
    bool trackFrozen(int32_t trackId) const;
    int32_t freezeGetState(int32_t trackId, uint8_t* out, int32_t cap) const;
    void freezeSetState(int32_t trackId, const uint8_t* data, int32_t size);

    // --- decoded samples, instrument telemetry & Rhythm kit -----------------
    bool sampleInfo(int64_t sampleId, SampleInfo* out) const;
    int64_t sampleRead(int64_t sampleId, float* out, int64_t cap) const;
    bool samplerInfo(int32_t trackId, SamplerInfo* out) const;
    bool grainInfo(int32_t trackId, SamplerInfo* out) const;
    int32_t grainPlayPositions(int32_t trackId, float* out, int32_t maxN) const;
    int32_t instrumentVoiceCount(int32_t trackId) const;
    int32_t instrumentHeldNotes(int32_t trackId, int32_t* out, int32_t maxN) const;
    int32_t instrumentScope(int32_t trackId, float* out, int32_t maxN) const;
    int32_t instrumentOutput(int32_t trackId, float* out, int32_t maxN) const;

    bool rhythmVoiceInfo(int32_t trackId, int32_t voice, SamplerInfo* out) const;
    int32_t rhythmVoiceSource(int32_t trackId, int32_t voice) const;
    int32_t rhythmVoiceDeviceCount(int32_t trackId, int32_t voice) const;
    int32_t rhythmAddVoiceDevice(int32_t trackId, int32_t voice, int32_t kind);
    bool rhythmRemoveVoiceDevice(int32_t trackId, int32_t voice, int32_t dev);
    bool rhythmMoveVoiceDevice(int32_t trackId, int32_t voice, int32_t from, int32_t to);
    Device* rhythmVoiceDevice(int32_t trackId, int32_t voice, int32_t dev) const;
    void rhythmVoiceDeviceParamSet(int32_t trackId, int32_t voice, int32_t dev, int32_t param, float value);
    std::string rhythmKitName(int32_t trackId) const;
    void setRhythmKitName(int32_t trackId, const std::string& name);
    std::string rhythmMacroName(int32_t trackId, int32_t macro) const;
    void setRhythmMacroName(int32_t trackId, int32_t macro, const std::string& name);
    int32_t rhythmAddMacroMapping(
        int32_t trackId, int32_t macro, int32_t voice, int32_t device, int32_t param, float lo, float hi);
    int32_t rhythmMacroMappingCount(int32_t trackId) const;
    bool rhythmMacroMappingInfo(int32_t trackId, int32_t index, int32_t& macro, int32_t& voice, int32_t& device,
        int32_t& param, float& lo, float& hi, int32_t& curve) const;
    bool rhythmRemoveMacroMapping(int32_t trackId, int32_t index);
    bool rhythmSetMacroMappingRange(int32_t trackId, int32_t index, float lo, float hi);
    bool rhythmSetMacroMappingCurve(int32_t trackId, int32_t index, int32_t curve);
    void rhythmClearMacros(int32_t trackId);

  private:
    std::shared_ptr<Track> deepCloneTrack(const Track& src, int32_t newId, bool withContent = true);
    std::vector<std::shared_ptr<Track>> expandTrackSet(const int32_t* ids, int32_t n) const;
    void compactReturns(Graph& g) const;
    std::vector<int32_t> insertTrackClones(
        const std::vector<std::shared_ptr<Track>>& src, int32_t afterId, int32_t rootParent, bool keepParent);

    EngineRuntime& rt_;
    std::vector<std::shared_ptr<Track>> trackClipboard_;
};

} // namespace nota::detail
