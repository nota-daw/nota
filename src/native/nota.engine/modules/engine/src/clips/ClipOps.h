// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ClipOps — arrangement clip geometry, editing, clipboard, waveform peaks, warp cache,
// MIDI clip/MPE store, and track UI metadata.

#pragma once

#include "clips/ClipTypes.h"
#include "core/EnginePrelude.h"
#include "model/MidiClip.h"
#include "model/Track.h"
#include "nota/engine/Types.h"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

namespace nota::detail {

class EngineRuntime;
struct Graph;
struct Section;
struct TimeRegion;

class ClipOps {
  public:
    explicit ClipOps(EngineRuntime& rt) noexcept : rt_(rt) {}
    ~ClipOps();

    ClipOps(const ClipOps&) = delete;
    ClipOps& operator=(const ClipOps&) = delete;

    // --- audio persistence & waveform peaks ---------------------------------
    std::shared_ptr<SampleBuffer> findSampleById(int64_t sampleId) const;
    int32_t addAudioClip(int32_t trackId, const std::string& path, double startBeat);
    int32_t addAudioClipBuffer(int32_t trackId, std::shared_ptr<SampleBuffer> sample, double startBeat);
    int32_t addAudioClipEx(int32_t trackId, const std::string& path, double startBeat, double sourceOffsetFrames,
        int64_t lengthFrames, float gain);
    int32_t pasteAudioFrames(
        int32_t trackId, const float* interleaved, int64_t frames, double startBeat, const std::string& name);
    bool audioClipInfo(int32_t trackId, int32_t clipIndex, AudioClipInfo* out) const;
    bool setClipGain(int32_t trackId, int32_t clipIndex, float gain);
    bool clipAdsr(int32_t trackId, int32_t clipIndex, engine::ClipAdsr* out) const;
    bool setClipAdsr(int32_t trackId, int32_t clipIndex, const engine::ClipAdsr& adsr);
    bool setClipActive(int32_t trackId, int32_t clipIndex, bool active);
    int32_t getClipPeaks(
        int32_t trackId, int32_t clipIndex, float* outMinMax, int32_t maxPoints, float* outRms = nullptr) const;
    int32_t getClipSourcePeaks(
        int32_t trackId, int32_t clipIndex, float* outMinMax, int32_t maxPoints, float* outRms = nullptr) const;
    int32_t getClipWarpFullPeaks(
        int32_t trackId, int32_t clipIndex, float* outMinMax, int32_t maxPoints, float* outRms = nullptr) const;
    int32_t getClipWarpPeaksRange(int32_t trackId, int32_t clipIndex, double beat0, double beat1, float* outMinMax,
        int32_t maxPoints, float* outRms = nullptr) const;
    int32_t getClipSourcePeaksRange(int32_t trackId, int32_t clipIndex, double frame0, double frame1, float* outMinMax,
        int32_t maxPoints, float* outRms = nullptr) const;

    // --- arrangement geometry & editing -------------------------------------
    bool moveClip(int32_t trackId, int32_t clipIndex, double newStartBeat);
    bool moveClipToTrack(int32_t srcTrackId, int32_t clipIndex, int32_t sourceTrackId, double newStartBeat);
    bool moveClipBlock(const std::vector<ClipMoveReq>& moves);
    bool trimClip(int32_t trackId, int32_t clipIndex, double newStartBeat, double newLengthBeats);
    bool resizeAudioClip(int32_t trackId, int32_t clipIndex, double newStartBeat, double newLengthBeats);
    bool setClipSourceRegion(int32_t trackId, int32_t clipIndex, double offsetFrames, int64_t lengthFrames);
    int32_t splitClip(int32_t trackId, int32_t clipIndex, double atBeat);
    int32_t duplicateClip(int32_t trackId, int32_t clipIndex);

    // --- time-range, insert time, section edits & consolidate ---------------
    bool deleteClipsInRange(
        const std::vector<int32_t>& trackIds, double start, double end, bool withAutomation = false);
    double duplicateRange(const std::vector<int32_t>& trackIds, double start, double end);
    bool insertTime(double at, double len, bool duplicate);
    bool splitClipsAtRange(const std::vector<int32_t>& trackIds, double start, double end);
    bool fadeTouchingSplits(const std::vector<std::pair<int32_t, int32_t>>& sel);
    void setSplitFades(bool on) { splitFades_ = on; }
    bool splitFades() const { return splitFades_; }
    bool consolidateRange(const std::vector<int32_t>& trackIds, double start, double end);
    bool moveSection(int32_t index, int32_t toBoundary);
    bool duplicateSectionWithContent(int32_t index);
    bool deleteSectionWithContent(int32_t index);
    bool sectionOverlaps(int32_t index) const;

    // --- clip & block clipboard / automation-follows-clips ------------------
    void setAutomationLock(bool locked) { automationLock_ = locked; }
    bool automationLock() const { return automationLock_; }
    bool lastMoveKeptDeviceAutomation() const { return lastMoveKeptDeviceAuto_; }
    bool copyClip(int32_t trackId, int32_t clipIndex);
    bool cutClip(int32_t trackId, int32_t clipIndex);
    int32_t pasteClip(int32_t sourceTrackId, double atBeat);
    int32_t clipboardClipKind() const;
    bool copyClipBlock(const std::vector<std::pair<int32_t, int32_t>>& sel);
    bool cutClipBlock(const std::vector<std::pair<int32_t, int32_t>>& sel);
    int32_t pasteClipBlock(double atBeat, int32_t sourceTrackId);
    double duplicateClipBlock(const std::vector<std::pair<int32_t, int32_t>>& sel);
    bool copyRange(const std::vector<int32_t>& trackIds, double start, double end);
    bool cutRange(const std::vector<int32_t>& trackIds, double start, double end);
    int32_t clipboardBlockCount() const { return static_cast<int32_t>(clipboardBlock_.size()); }
    const std::vector<std::pair<int32_t, int32_t>>& lastPlaced() const { return lastPlaced_; }
    const std::vector<BlockClip>& clipboardBlock() const { return clipboardBlock_; }
    int32_t clipboardBlockAnchor() const { return clipboardBlockAnchor_; }
    void clearLastPlaced() { lastPlaced_.clear(); }
    std::vector<BlockClip> captureBlock(const std::vector<std::pair<int32_t, int32_t>>& sel, double& blockLen) const;
    void placeBlock(const std::vector<BlockClip>& items, double placedStart, bool overwrite = false);
    std::vector<BlockClip> remapBlock(
        const std::vector<BlockClip>& items, int32_t destTrackId, int32_t anchorTrackId = -1) const;

    // --- overwrite & slice bridges (recording / session pass) ---------------
    void overwriteAudioClipsInRange(std::vector<AudioClip>& clips, double ns, double ne, double spb);
    static void overwriteMidiClipsInRange(std::vector<MidiClip>& clips, double ns, double ne);
    AudioClip sliceAudioClip(const AudioClip& c, double ns, double ne) const;

    // --- clip / track metadata, MIDI notes, MPE store & articulation --------
    bool setClipName(int32_t trackId, int32_t clipIndex, const std::string& name);
    int32_t clipName(int32_t trackId, int32_t clipIndex, char* out, int32_t cap) const;
    bool setTrackName(int32_t trackId, const std::string& name);
    int32_t trackName(int32_t trackId, char* out, int32_t cap) const;
    bool setTrackColorIndex(int32_t trackId, int32_t colorIndex);
    int32_t trackColorIndex(int32_t trackId) const;
    bool setTrackPianoFold(int32_t trackId, bool on);
    int32_t trackPianoFold(int32_t trackId) const;
    bool setTrackEditLink(int32_t trackId, bool on);
    int32_t trackEditLink(int32_t trackId) const;
    int32_t addMidiClip(int32_t trackId, double startBeat, double lengthBeats);
    bool setClipNotes(int32_t trackId, int32_t clipIndex, const NoteData* notes, int32_t count);
    bool setClipNotesLive(int32_t trackId, int32_t clipIndex, const NoteData* notes, int32_t count);
    int32_t getClipNotes(int32_t trackId, int32_t clipIndex, NoteData* out, int32_t maxNotes) const;
    int32_t clipNoteCount(int32_t trackId, int32_t clipIndex) const;
    int32_t createNoteExpr(const ExprPointData* points, int32_t count);
    int32_t storeNoteExpr(std::shared_ptr<const NoteExpr> e);
    int32_t noteExprPoints(int32_t id, ExprPointData* out, int32_t maxPoints) const;
    Note noteFromData(const NoteData& d) const;
    static void noteToData(const Note& n, NoteData& d);
    bool setTrackArticulationMap(int32_t trackId, int32_t defaultId, const ArticulationEntryData* entries,
        int32_t entryCount, const ArticulationActionData* actions, int32_t actionCount, const char* json,
        int32_t jsonLen);
    bool clearTrackArticulationMap(int32_t trackId);
    int32_t trackArticulationMapJson(int32_t trackId, char* out, int32_t maxLen) const;
    std::shared_ptr<const ArticulationTable> trackArticulationTable(int32_t trackId) const;

    // --- pitch, reverse & warp cache pipeline -------------------------------
    bool setClipPitch(int32_t trackId, int32_t clipIndex, float semitones);
    bool setClipReverse(int32_t trackId, int32_t clipIndex, bool reversed);
    bool setClipWarp(int32_t trackId, int32_t clipIndex, bool enabled, int32_t mode);
    double autoWarpClip(int32_t trackId, int32_t clipIndex, double knownBpm = 0.0);
    double beatWarpClip(int32_t trackId, int32_t clipIndex);
    bool setClipWarpLength(int32_t trackId, int32_t clipIndex, double beats);
    bool setClipWarpMarkers(
        int32_t trackId, int32_t clipIndex, const double* srcFrames, const double* beats, int32_t count);
    bool setClipWarpTrim(int32_t trackId, int32_t clipIndex, double playStart, double playEnd);
    int32_t clipWarpMarkers(
        int32_t trackId, int32_t clipIndex, double* outSrc, double* outBeat, int32_t maxCount) const;
    void setDeferWarpBuild(bool defer);
    int64_t warpBuildStep(int32_t maxFrames);
    int64_t warpBuildRemainingFrames() const;
    int64_t warpBuildPoll();
    void configureClipWarp(AudioClip& c, double spb, double devSR);
    void configureTempoWarp(AudioClip& c, const std::shared_ptr<const TempoMap>& map, double devSR, bool force = false);
    void reconfigureAllWarpStreams();
    void reconfigureAllWarpStreams(double spb, double devSR);
    bool warpNeedsBuild(const AudioClip& c, bool arrangement = true) const;
    void cancelWarpJob();
    void resetWarpCursor() { wb_ = {}; }

  private:
    static std::shared_ptr<SampleBuffer> chainSampleById(const RackCore* rack, int64_t sampleId, int depth);
    std::vector<TrackLane> captureClipAutomation(const Track& t, double start, double end) const;
    void applyClipAutomation(Track& nt, int32_t trackId, const std::vector<TrackLane>& snips, double placedStart,
        double len, bool sameTrack, ClipAutomationPass pass = ClipAutomationPass::Replace) const;
    static void removeAutomationInRange(Track& t, double start, double end);
    static bool tracksDeviceLayoutMatch(const Track& a, const Track& b);
    double splitFadeBeats() const;
    AudioClip consolidateAudioClips(const std::vector<AudioClip>& clips, double start, double end);
    void remapTimeline(double lo, double hi, const std::vector<TimeRegion>& regions, std::vector<Section> sections);
    bool insertTimeInto(Graph& g, double at, double len, bool duplicate);
    const AudioClip* warpBuildTargetClip() const;
    const AudioClip* nextWarpBuildTarget(int32_t& trackId, int32_t& clipIndex) const;

    EngineRuntime& rt_;

    int32_t clipboardKind_ = -1;
    MidiClip clipboardMidi_;
    AudioClip clipboardAudio_;
    std::vector<TrackLane> clipboardAuto_;
    int32_t clipboardAutoTrack_ = -1;

    std::vector<BlockClip> clipboardBlock_;
    double clipboardBlockLen_ = 0.0;
    bool clipboardBlockOverwrite_ = false;
    int32_t clipboardBlockAnchor_ = -1;
    std::vector<std::pair<int32_t, int32_t>> lastPlaced_;

    bool automationLock_ = false;
    bool splitFades_ = true;
    bool lastMoveKeptDeviceAuto_ = false;

    mutable std::mutex exprMx_;
    std::unordered_map<int32_t, std::shared_ptr<const NoteExpr>> exprStore_;
    int32_t nextExprId_ = 1;

    bool deferWarpBuild_ = false;
    WarpBuildCursor wb_;
    std::unique_ptr<WarpJob> wj_;
};

void seedNeutralWarp(AudioClip& c, double spb, double devSR);

} // namespace nota::detail
