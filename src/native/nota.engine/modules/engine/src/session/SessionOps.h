// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// SessionOps — Session scenes/slots, quantized launch/stop, follow actions, Session <-> Arrangement
// transfers, Session pass recording, and audio/MIDI slot metadata.

#pragma once

#include "clips/ClipTypes.h"
#include "core/EnginePrelude.h"
#include "model/Track.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "nota/engine/Types.h"
#include "session/SessionPass.h"
#include "session/SessionTypes.h"

#include <atomic>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

class EngineRuntime;
struct AudioClip;
struct Graph;
struct MidiClip;
struct MidiTakeLane;

class SessionOps {
  public:
    explicit SessionOps(EngineRuntime& rt) noexcept : rt_(rt) {}

    SessionOps(const SessionOps&) = delete;
    SessionOps& operator=(const SessionOps&) = delete;

    struct LandingMute {
        SessionOps& s;
        explicit LandingMute(SessionOps& ops) noexcept : s(ops) { ++s.landingMuted_; }
        ~LandingMute() { --s.landingMuted_; }
    };

    // --- scenes & launch / stop ---------------------------------------------
    int32_t sceneCount() const;
    bool clearSessionSlot(int32_t trackId, int32_t scene);
    int32_t addScene();
    bool removeScene(int32_t scene);
    void setLaunchQuant(double beats);
    double launchQuant() const;
    void setSessionFollow(bool on) { sessionFollow_.store(on, std::memory_order_relaxed); }
    bool sessionFollow() const { return sessionFollow_.load(std::memory_order_relaxed); }
    void setSessionRecordLength(double beats) { sessionRecordLen_ = beats > 0.0 ? beats : 0.0; }
    double sessionRecordLength() const { return sessionRecordLen_; }
    void launchSlot(int32_t trackId, int32_t scene);
    void launchSlotVel(int32_t trackId, int32_t scene, float velocity);
    void releaseSlot(int32_t trackId, int32_t scene);
    void launchScene(int32_t scene, bool fromFollow = false, double quantOverride = -1.0);
    void stopSlot(int32_t trackId);
    void stopScene(int32_t scene);
    void stopAllSession();
    bool sessionSlotLaunchQueued(int32_t trackId, int32_t scene) const;
    bool sessionSceneQueued(int32_t scene) const;
    bool cancelSlotLaunch(int32_t trackId, int32_t scene);
    bool cancelSceneLaunch(int32_t scene);
    bool sceneLaunchWaiting();
    void trackBackToArrangement(int32_t trackId);
    int32_t sessionPlayingSlot(int32_t trackId) const;
    double sessionSlotPosition(int32_t trackId) const;

    // --- slot recording & Arrangement <-> Session transfers -----------------
    void recordSessionSlot(int32_t trackId, int32_t scene);
    void stopSessionRecord();
    void stopSessionRecordSlot(int32_t trackId);
    bool sessionSlotRecording(int32_t trackId, int32_t scene) const;
    void finalizeSessionRecord(bool relaunch);
    void cancelSessionCountIn();
    int32_t sessionSlotToArrangement(int32_t trackId, int32_t scene, double startBeat);
    bool arrangementClipToSession(int32_t trackId, int32_t clipIndex, int32_t scene);
    bool arrangementAudioClipToSession(int32_t trackId, int32_t clipIndex, int32_t scene);
    int32_t arrangementClipsToSession(const std::vector<std::pair<int32_t, int32_t>>& sel, int32_t startScene);
    int32_t pasteClipBlockToSession(int32_t destTrackId, int32_t startScene);
    int32_t sessionSlotsToArrangement(
        const std::vector<std::pair<int32_t, int32_t>>& slots, double atBeat, int32_t destTrackId);

    // --- MIDI & audio slots, properties & copy ------------------------------
    bool sessionAudioSlotInfo(int32_t trackId, int32_t scene, SessionAudioSlotInfo* out) const;
    bool addSessionAudioClip(int32_t trackId, int32_t scene, const std::string& path, double lengthBeats,
        double sourceOffsetFrames, int64_t lengthFrames, float gain);
    bool addSessionAudioFile(int32_t trackId, int32_t scene, const std::string& path);
    bool addSessionMidiClip(int32_t trackId, int32_t scene, double lengthBeats);
    double sessionSlotLength(int32_t trackId, int32_t scene) const;
    bool setSessionSlotLength(int32_t trackId, int32_t scene, double lengthBeats);
    float sessionSlotGain(int32_t trackId, int32_t scene) const;
    bool setSessionSlotGain(int32_t trackId, int32_t scene, float gain);
    int32_t sessionSlotState(int32_t trackId, int32_t scene) const;
    bool setSessionNotes(int32_t trackId, int32_t scene, const NoteData* notes, int32_t count);
    int32_t getSessionNotes(int32_t trackId, int32_t scene, NoteData* out, int32_t maxNotes) const;
    int32_t sessionNoteCount(int32_t trackId, int32_t scene) const;
    void syncSessionAudioSlot(Track& t, int32_t clipIndex) const;
    bool sessionClipProps(int32_t trackId, int32_t scene, SessionClipProps* out) const;
    bool setSessionClipProps(int32_t trackId, int32_t scene, const SessionClipProps& p);
    bool sessionClipName(int32_t trackId, int32_t scene, std::string& out) const;
    bool setSessionClipName(int32_t trackId, int32_t scene, const std::string& name);
    bool sessionSlotStopButton(int32_t trackId, int32_t scene) const;
    bool setSessionSlotStopButton(int32_t trackId, int32_t scene, bool on);
    bool sessionSlotTakeLength(int32_t trackId, int32_t scene, NextTakeLength& out) const;
    bool setSessionSlotTakeLength(int32_t trackId, int32_t scene, const NextTakeLength& in);
    double sessionSlotTakeBeats(int32_t trackId, int32_t scene) const;
    bool sessionSlotLandingRule(int32_t trackId, int32_t scene, LandingRule& out) const;
    bool setSessionSlotLandingRule(int32_t trackId, int32_t scene, const LandingRule& v);
    int32_t sessionPassSpans(PassSpan* out, int32_t cap) const;
    bool copySessionSlot(int32_t srcTrack, int32_t srcScene, int32_t dstTrack, int32_t dstScene);

    // --- scenes, scene recording, follow & RT apply/post --------------------
    bool sceneProps(int32_t scene, SceneProps* out) const;
    bool setSceneProps(int32_t scene, const SceneProps& p);
    bool sceneName(int32_t scene, std::string& out) const;
    bool setSceneName(int32_t scene, const std::string& name);
    int32_t insertScene(int32_t at);
    int32_t duplicateScene(int32_t scene);
    int32_t captureScene(int32_t at);
    bool moveScene(int32_t from, int32_t to);
    int32_t recordSessionScene(int32_t scene);
    bool sessionRecordTarget(int32_t* trackId, int32_t* scene, double* elapsed) const;
    bool takeSceneTempoChange(double* bpm, int32_t* num, int32_t* den);
    void serviceSessionFollow();
    int32_t sessionSlotPeaks(int32_t trackId, int32_t scene, float* outMinMax, int32_t maxPoints) const;
    void sessionPreApply(Track& t, double blockBeats);
    void sessionPostRender(Track& t, double blockBeats);

    // --- cross-module recording / session pass coordinators -----------------
    static bool ownTakeLength(const SessionSlot* s) {
        return s && !s->hasClip && s->nextTake.mode == SlotTakeMode::Fixed;
    }
    double resolveTakeBeats(const SessionSlot* s) const;
    double roundedTakeBeats(double beats) const;
    void noteTakeLanded(int32_t trackId, int32_t scene);
    void noteMidiLaneLanded(const MidiTakeLane& l);
    void beginSessionPass(bool performanceOnly);
    void drainSessionPass();
    bool finishSessionPass(double stopBeat);
    void abandonSessionPass();
    bool sessionPlayingOrQueued() const;
    void pushPassEvent(const LaunchEvent& e) {
        if (!launchEvents_.push(e)) passDropped_.fetch_add(1, std::memory_order_relaxed);
    }
    bool passRecordsInput(const Track& t) const;
    void reportPassEvents(Graph* g, double segBeat, double segBeats);
    void writePassTempo();

  private:
    std::shared_ptr<Graph> rebuildScenes(
        int32_t newCount, const std::function<void(std::vector<SessionSlot>&)>& mutate);
    void launchSlotImpl(
        Track& t, int32_t scene, float velocity, bool fromScene, int32_t gen = 0, double quantOverride = -1.0);
    template <typename Match> bool aimLaunchCancel(bool wholeScene, Match match);
    SessionSlot slotFromMidiClip(const MidiClip& m) const;
    SessionSlot slotFromAudioClip(const AudioClip& a);
    double audioClipBeats(const AudioClip& c) const;
    static void replaceSlotContent(SessionSlot& dst, SessionSlot src);
    void serviceLandingRules();
    void remapLandingScenes(Graph& g, const std::function<int32_t(int32_t)>& map);
    int32_t landBlockInSession(const std::vector<BlockClip>& items, int32_t startScene);
    void remapPlayers(const std::function<int32_t(int32_t)>& map);
    int32_t insertSceneRow(int32_t at, int32_t copyFrom, bool capturePlaying);
    void applySceneTempo();
    int32_t followTarget(const Track& t, int32_t cur, FollowAction a);
    void writePassTrack(int32_t trackId, const std::vector<const PlayedSpan*>& spans);

    EngineRuntime& rt_;

    uint32_t followRng_ = 0x9E3779B9u;
    RuleWait ruleWait_;
    int32_t landingMuted_ = 0;
    std::atomic<bool> sessionFollow_{true};
    double sessionRecordLen_ = 0.0;
    double sceneFollowStart_ = 0.0;
    int32_t sceneLaunchGen_ = 0;
    int32_t waitSceneGen_ = 0;
    int32_t waitScene_ = -1;
    int32_t prevFollowScene_ = -1;
    double prevFollowStart_ = 0.0;
    bool waitFromFollow_ = false;
    bool sceneTempoPending_ = false;
    double sceneTempo_ = 0.0;
    double sceneTempoAt_ = 0.0;
    int32_t sceneSigNum_ = 0;
    int32_t sceneSigDen_ = 0;
    bool sceneTempoApplied_ = false;

    SpscRingBuffer<LaunchEvent, 1024> launchEvents_;
    std::atomic<int32_t> passDropped_{0};
    uint32_t passSerialNext_ = 0;
    std::vector<std::shared_ptr<Track>> passBase_;
    std::shared_ptr<Graph> passGraph_;
    std::vector<int32_t> passArmed_;
};

} // namespace nota::detail
