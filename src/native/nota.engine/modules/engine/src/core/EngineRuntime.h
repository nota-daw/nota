// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Core audio engine runtime: transport, immutable graph snapshots, mixer,
// recording/monitoring/session coordination, and subsystem facades.

#pragma once

#include "model/Track.h"
#include "model/MidiClip.h"
#include "model/ArticulationTable.h"
#include "model/ArticulationSchedule.h"
#include "core/TempoMap.h"
#include "nota/dsp/rt/DeviceTelemetry.h"
#include "core/AudioThreadPool.h"
#include "recording/CaptureRing.h"
#include "render/RenderBusState.h"
#include "session/SessionPass.h"
#include "model/EngineTypes.h"
#include "model/Graph.h"
#include "render/HybridBufferController.h"
#include "core/Transport.h"
#include "core/EnginePrelude.h"
#include "session/SessionPlayer.h"
#include "automation/AutomationOps.h"
#include "automation/AutomationTypes.h"
#include "devices/DeviceTypes.h"
#include "clips/ClipOps.h"
#include "devices/DeviceOps.h"
#include "recording/RecordingOps.h"
#include "session/SessionOps.h"
#include "io/IoOps.h"
#include "tracks/TrackOps.h"
#include "devices/RackOps.h"
#include "recording/RecordingTypes.h"
#include "render/RenderTypes.h"
#include "session/SessionTypes.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstddef>
#include <cmath>
#include <limits>
#include <memory>
#include <unordered_map>
#include <utility>
#include <vector>

namespace nota::detail {

// Live MIDI event (UI/CoreMIDI -> audio), lock-free down-queue.
class RenderGraphExecutor;
class HybridBufferController;
struct EngineApiAccess;

class EngineRuntime {
    friend class RenderGraphExecutor;
    friend class HybridBufferController;
    friend struct EngineApiAccess;
    friend class RackOps;
    friend class AutomationOps;
    friend class DeviceOps;
    friend class ClipOps;
    friend class RecordingOps;
    friend class SessionOps;
    friend class TrackOps;
    friend class IoOps;

  public:
    EngineRuntime();
    ~EngineRuntime();

    RackOps& racks() noexcept { return racks_; }
    const RackOps& racks() const noexcept { return racks_; }
    AutomationOps& automation() noexcept { return automation_; }
    const AutomationOps& automation() const noexcept { return automation_; }
    DeviceOps& devices() noexcept { return devices_; }
    const DeviceOps& devices() const noexcept { return devices_; }
    ClipOps& clips() noexcept { return clips_; }
    const ClipOps& clips() const noexcept { return clips_; }
    SessionOps& session() noexcept { return session_; }
    const SessionOps& session() const noexcept { return session_; }
    RecordingOps& recorder() noexcept { return recorder_; }
    const RecordingOps& recorder() const noexcept { return recorder_; }
    TrackOps& tracks() noexcept { return tracks_; }
    const TrackOps& tracks() const noexcept { return tracks_; }
    IoOps& io() noexcept { return io_; }
    const IoOps& io() const noexcept { return io_; }

    // One block through the device callback's path, without a device (tests/benchmarks).
    void renderDeviceBlock(float* out, int32_t frames) { render(out, nullptr, frames); }
    // The same with the Cue bus (specs/089): `cue` may be null.
    void renderDeviceBlock(float* main, float* cue, int32_t frames) { render(main, cue, frames); }

    // --- xrun / dropout telemetry (M7-8) ---
    void notifyXrun() { xruns_.fetch_add(1, std::memory_order_relaxed); }
    int32_t xrunCount() const { return xruns_.load(std::memory_order_relaxed); }
    bool graphRetireSelfTest(); // device-free: a replaced graph is freed two renders later

    // --- DSP load (Phase 11): RT render time / block budget, smoothed 0..1 ---
    float cpuLoad() const { return cpuLoad_.load(std::memory_order_relaxed); }

    // --- built-in device CPU telemetry mode (message thread writes, RT reads atomically) ---
    void setTelemetryMode(dsp::TelemetryMode mode);
    dsp::TelemetryMode telemetryMode() const { return telemetryMode_.load(std::memory_order_relaxed); }
    // Parallel render threads (the audio thread helps too): n < 0 = default, 0 = none.
    // Device stopped only (message thread); false while it runs.
    bool setRenderThreads(int n);
    int renderThreads() const { return pool_->workerCount(); }
    double renderThreadsCpuSeconds() const { return pool_->workersCpuSeconds(); }

    // --- Streaming disk audio sources & hybrid anticipative buffering (Phase 6, #1050) ---
    void setStreamThresholdBytes(int64_t bytes);
    int64_t streamThresholdBytes() const { return streamThresholdBytes_.load(std::memory_order_acquire); }
    void setSampleLoadPolicy(SampleLoadPolicy policy);
    SampleLoadPolicy sampleLoadPolicy() const {
        return static_cast<SampleLoadPolicy>(sampleLoadPolicy_.load(std::memory_order_acquire));
    }
    AudioDecodeOptions decodeOptions() const noexcept { return {sampleLoadPolicy(), streamThresholdBytes()}; }
    bool isSampleStreaming(int64_t sampleId) const;
    void streamingStats(StreamingStats& out) const;

    void setHybridBufferingMode(HybridBufferMode mode);
    HybridBufferMode hybridBufferingMode() const { return hybrid_ ? hybrid_->mode() : HybridBufferMode::Off; }
    HybridBufferStats hybridStats() const { return hybrid_ ? hybrid_->stats() : HybridBufferStats{}; }

    bool timedEventsSelfTest();     // #436: a hosted plug-in renders once per block, events at their offsets
    int32_t articulationSelfTest(); // specs/085: articulation scheduling + delivery; 0 = pass, else the failed check
    // --- debug oscillator (M0) ---
    void setToneEnabled(bool enabled);
    void setFrequency(float hz);

    // --- transport (M1) ---
    void transportPlay();
    void transportStop();
    // Count-in: N bars of clicks before the transport rolls (0 = off). Only a user Play
    // (transportPlayCountIn) and a Record from stop use it — exports / freezes / session
    // launches roll immediately. The clicks sound whether or not the metronome is on.
    void setCountInBars(int32_t bars) { countInBars_.store(std::clamp(bars, 0, 8), std::memory_order_relaxed); }
    // Loop record mode (#29): false = overdub (every loop pass adds to the take), true = replace
    // (the first fresh note of a pass removes what earlier passes of this take recorded).
    // Arrangement MIDI takes only; latched by the audio thread at take start and at each wrap.
    void setLoopRecordReplace(bool on) { loopRecordReplace_.store(on, std::memory_order_relaxed); }
    bool loopRecordReplace() const { return loopRecordReplace_.load(std::memory_order_relaxed); }
    int32_t countInBars() const { return countInBars_.load(std::memory_order_relaxed); }
    void transportPlayCountIn();
    // Beats of count-in still to click (0 = not counting in). UI poll.
    double countInBeatsRemaining() const { return uiCountIn_.load(std::memory_order_relaxed); }
    // Session vs Arrangement (M5): the Arrangement is "active" when the main Play (or an
    // arrangement record) started it. Launching a Session clip starts the clock WITHOUT
    // activating the Arrangement, so non-session tracks stay silent (session-only jam).
    // backToArrangement stops every session clip and hands all tracks back to the timeline.
    bool arrangementActive() const { return arrangementActive_.load(std::memory_order_relaxed); }
    void backToArrangement();
    // False (nothing changes) while the master Tempo lane drives the tempo (specs/099).
    bool setBpm(double bpm);
    double bpm() const;
    void setTimeSignature(int num, int denom);
    void setLoop(bool enabled, double startBeat, double endBeat);
    void setMetronome(bool on);
    void seekBeats(double beat);
    double positionBeats() const { return transport_.uiPositionBeats(); }
    bool transportSnapshot(TransportSnapshot& out) const { return transport_.presentationSnapshot(out); }
    bool isPlaying() const { return transport_.uiIsPlaying(); }
    // Message-thread mirror of the loop region (for UI display; the audio thread
    // gets the range via the command queue).
    bool loopEnabled() const { return loopEnabled_; }
    double loopStart() const { return loopStart_; }
    double loopEnd() const { return loopEnd_; }

    // --- mixer ---
    void setMasterVolume(float v) { masterVolume_.store(v, std::memory_order_relaxed); }
    // Master pan (specs/099), -1..1: applied after the master chain.
    void setMasterPan(float p) { tracks_.setTrackPan(kMasterTrackId, std::clamp(p, -1.0f, 1.0f)); }
    int32_t masterTrackId() const { return kMasterTrackId; }

    // --- cue & click (specs/089) ---
    void setCueVolume(float gain) { cueGain_.store(std::clamp(gain, 0.0f, 2.0f), std::memory_order_relaxed); }
    float cueVolume() const { return cueGain_.load(std::memory_order_relaxed); }
    void cueMeter(float& pkL, float& pkR, float& rmsL, float& rmsR) const {
        pkL = cuePeakL_.load(std::memory_order_relaxed);
        pkR = cuePeakR_.load(std::memory_order_relaxed);
        rmsL = cueRmsL_.load(std::memory_order_relaxed);
        rmsR = cueRmsR_.load(std::memory_order_relaxed);
    }
    void setClickDestination(int32_t d) { clickDest_.store(std::clamp(d, 0, 2), std::memory_order_relaxed); }
    int32_t clickDestination() const { return clickDest_.load(std::memory_order_relaxed); }
    void setClickVolume(float gain) { clickGain_.store(std::clamp(gain, 0.0f, 2.0f), std::memory_order_relaxed); }

    // --- project load (M7-6) ---
    void reset();

    // Level metering (M6-2). Post-fader block peak/RMS per channel.
    bool trackMeter(int32_t trackId, float& peakL, float& peakR, float& rmsL, float& rmsR) const;
    int32_t readTrackMeters(TrackMeterEntry* out, int32_t capacity) const;
    void masterMeter(float& peakL, float& peakR, float& rmsL, float& rmsR) const;

    // Audition target: live notes reach this track even when unarmed (-1 = none).
    void setAuditionTrack(int32_t trackId) { auditionTrackId_.store(trackId, std::memory_order_relaxed); }
    void poll(); // message thread: materialise recorded notes/audio

    // --- undo/redo & value gestures (M6-6, specs/009, specs/016) ---
    bool undo();
    bool redo();
    bool canUndo() const { return !undoStack_.empty(); }
    bool canRedo() const { return !redoStack_.empty(); }
    void beginUndoGroup();
    void endUndoGroup();
    void clearUndoHistory();
    int32_t beginMixGesture(const MixTarget* targets, int32_t count); // handle > 0, or 0
    void endMixGesture(int32_t handle);
    float masterVolume() const { return masterVolume_.load(std::memory_order_relaxed); }
    bool mixUndoSelfTest();

    // --- stereo measurements, pan law, sections & tempo map ---
    int32_t stereoMeasures(StereoMeasure* out, int32_t cap) const; // specs/008
    bool setPanLaw(PanLaw law, bool checkpoint);                   // specs/006
    void setSections(std::vector<Section> sections, bool checkpoint);
    void deriveTempoMap(Graph& g) const; // specs/099: rebuilds g.tempoMap on change
    // The master Tempo lane, or nullptr.
    static const TrackLane* masterTempoLane(const Graph& g);
    bool tempoAutomated() const;
    const TempoMap* tempoMap() const { return authoring_ ? authoring_->tempoMap.get() : nullptr; }
    double beatToSeconds(double beat) const {
        return tempoMap() ? tempoMap()->secondsAt(beat) : beat * 60.0 / authoringBpm_;
    }
    double secondsToBeat(double seconds) const {
        return tempoMap() ? tempoMap()->beatAt(seconds) : seconds * authoringBpm_ / 60.0;
    }
    const std::vector<Section>& sections() const {
        static const std::vector<Section> none;
        return authoring_ ? authoring_->sections : none;
    }
    PanLaw panLaw() const { return authoring_ ? authoring_->panLaw : PanLaw::Balance; }

    // --- offline render (tests / export) ---
    void renderOffline(float* out, int32_t frames);
    // Render at a specific sample rate (WAV export, M6-4). Sets the transport
    // sample rate first, so the backend must be stopped before calling.
    void renderOffline(float* out, int32_t frames, double sampleRate);

  private:
    // audio thread
    void render(float* main, float* cue, int32_t numFrames);
    void processBlock(float* out, float* cue, int32_t numFrames); // cue may be null
    void mixGraph(Graph* g, float* out, int32_t frames, double blockStartSamples, double elapsedStartSamples,
        bool playing, double spb);
    void renderMonitorInput(Graph* g, const Track& t, float* dst, int32_t frames);
    void drainCommands(Graph* g);
    void applyLaunchCancel(Graph* g, const Command& c); // #480: cancel queued Session launches
    static bool isUnderGroup(const Graph& g, const Track& t, int32_t groupId);
    void drainLiveMidi(double blockStartBeat, bool playing);
    bool renderInstrumentRaw(Graph* g, Track& t, float* dst, int32_t frames, double blockStart, double spb,
        bool playing, size_t trackIdx = SIZE_MAX);
    int gatherInstrumentNotes(Track& t, MidiEv* evs, int n, int32_t frames, double blockStart, double spb, bool playing,
        ExprEv* xev = nullptr, int* xn = nullptr);
    int computeInstrumentMidi(Track& t, MidiEv* evs, int32_t frames, double blockStart, double spb, bool playing,
        bool arrangementActive, ExprEv* xev = nullptr, int* xn = nullptr, const Graph* g = nullptr,
        size_t trackIdx = SIZE_MAX);
    void computeBlockMidi(
        Graph* g, int32_t frames, double blockStart, double spb, bool playing, bool arrangementActive);
    void feedMidiKey(Graph* g, Device& d, int32_t srcTrackId);
    void feedDeviceKey(Graph* g, Device& d, int32_t ownTrackId, int32_t frames); // specs/044
    void feedInstrumentKeys(Graph* g, Track& t, int32_t frames);
    void feedRackChildKeys(Graph* g, const RackCore& r, int32_t ownTrackId, int32_t frames);
    void renderSessionSlotRaw(Track& t, float* dst, int32_t frames, double spb);
    int applyMidiEffects(
        Track& t, MidiEv* evs, int n, MidiEv* scratch, int32_t frames, double beatStart, double spb, bool playing);
    void applyMidiCcRouting(Track& t, const Graph* g = nullptr, size_t trackIdx = SIZE_MAX);
    void renderSessionAudioSlotRaw(Track& t, float* dst, int32_t frames, double spb);
    void renderAudioClipsRaw(
        const std::vector<AudioClip>& clips, float* dst, int32_t frames, double blockStart, double spb);
    void renderTrackAudioClipsRaw(const Track& t, float* dst, int32_t frames, double blockStart, double spb);
    void renderTrackAudioClipsUnmapped(
        const Track& t, float* dst, int32_t frames, double blockStart, double spb, double sr);
    void renderAnticipativeInstrumentBlock(Graph* g, Track& t, size_t trackIdx, float* dst, int32_t frames,
        double blockStart, double spb, double sr, MidiEv* midiScratch, ExprEv* exprScratch, ArtEv* artScratch);
    void renderAudioClipsAt(const std::vector<AudioClip>& clips, float* dst, int32_t frames, const Transport& clock);
    void fillFrozen(Track& t, float* dst, int32_t frames, double blockStart, double spb); // M7 freeze playback
    void renderMetronome(float* out, int32_t numFrames, double blockStartSamples);
    bool renderCountIn(float* out, int32_t numFrames, double spb);
    void endCountIn();
    void launchCountInSlot();
    void renderClick(float* out, float* cue, int32_t i, int clickLen, double sr, int32_t dest, float gain);
    void finishCue(float* cue, int32_t numFrames); // specs/089
    void renderTone(float* out, int32_t numFrames);
    void masterPanStage(float* out, int32_t frames, float pan, PanLaw law); // specs/099
    void masterPanSnap(float pan, PanLaw law);
    void processMasterChain(Graph& g, float* out, int32_t numFrames, double spb, int32_t nAnchors); // specs/099

    // message thread
    std::shared_ptr<Track> findTrackAuthoring(int32_t id) const;
    double renderSampleAtBeat(double beat, double blockStart, double spb) const {
        return transport_.hasTempoMap() ? blockStart + transport_.samplesAtBeat(beat) - transport_.playheadSamples()
                                        : beat * spb;
    }
    std::shared_ptr<Track> cloneTrack(const Track& t) const;
    static bool articulationsCurrent(const Track& t); // specs/085
    static void settleArticulations(Track& t);
    void publish(std::shared_ptr<Graph> g); // checkpoints (undo), then publishRaw
    void publishRaw(std::shared_ptr<Graph> g, bool restored = false);
    static void applyTelemetryMode(const Graph& g, dsp::TelemetryMode mode);
    void releaseVanishedClipNotes(const Graph& before, const Graph& after); // #649
    void propagateSampleRate(double sr);
    void republishWithTrack(int32_t trackId, std::shared_ptr<Track> nt);
    void republishWithTrackRaw(int32_t trackId, std::shared_ptr<Track> nt);
    void pushUndo();
    void reapplyRackMacros();
    void keepLiveSends(Graph& restored, const Graph& live) const; // specs/069
    bool ccRoutesUsed_ = false;
    bool ccDestsSet_ = false;
    size_t noteStatesKept_ = 1;
    ClipOps clips_{*this};
    SessionOps session_{*this};
    RackOps racks_{*this};
    DeviceOps devices_{*this};
    AutomationOps automation_{*this};
    TrackOps tracks_{*this};
    std::shared_ptr<Device> clipDevice_;
    std::shared_ptr<MidiDevice> clipMidi_;
    std::shared_ptr<Instrument> clipInstrument_;
    std::vector<ClipEntry> clipMany_;
    CommandQueue commands_;
    MidiQueue liveMidi_;
    MidiQueue remoteMidi_;
    RecordedQueue recorded_;
    RecordedExprQueue recordedExpr_;
    RecordedCtlQueue recordedCtl_;
    std::atomic<uint32_t> passSerial_{0};
    uint32_t rtPassSerial_ = 0;
    double rtPassEndBeat_ = 0.0;
    SessionPass sessionPass_;
    size_t passUndoBase_ = 0;
    ControlEventQueue midiControl_;
    Transport transport_;

    std::shared_ptr<Graph> authoring_;
    SnapshotHistory<Graph> graphHist_; // retired once 2 renders pass after replacement
    int32_t nextTrackId_ = 1;
    int32_t nextTakeId_ = 0; // AudioTake::id (specs/067); never reused
    bool joinedEdgesRestored_ = false;
    int32_t nextTakeGroup_ = 0;
    double authoringBpm_ = 120.0; // UI/authoring BPM prior to audio-thread apply
    double beatsPerSourceFrame(double sampleRate) const;
    double authoringSamplesPerBeat() const;
    bool loopEnabled_ = false;
    double loopStart_ = 0.0, loopEnd_ = 0.0;

    // Undo/redo stacks & mix gestures (M6-6, specs/009)
    std::vector<UndoEntry> undoStack_;
    std::vector<UndoEntry> redoStack_;
    uint64_t undoPushCount_ = 0;
    std::vector<OpenMixGesture> mixGestures_;
    int32_t nextMixGesture_ = 1;
    bool mixValue(const MixTarget& t, float& v) const;
    bool paramTargetExists(MixKind kind, int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    std::vector<PresetTagData> presetTags_ = std::vector<PresetTagData>(1);               // specs/048 (0 = none)
    std::vector<std::vector<uint8_t>> noteStates_ = std::vector<std::vector<uint8_t>>(1); // specs/049
    bool applyMix(const MixChange& c, bool after);
    static void mergeMix(std::vector<MixChange>& into, const std::vector<MixChange>& later);
    static constexpr size_t kMaxUndoDepth = 128;
    int32_t undoGroupDepth_ = 0;
    size_t undoGroupBase_ = 0;

    std::atomic<float> masterVolume_{1.0f};
    std::atomic<float> masterPeakL_{0.0f};
    std::atomic<float> masterPeakR_{0.0f};
    std::atomic<float> masterRmsL_{0.0f};
    std::atomic<float> masterRmsR_{0.0f};
    // Master stereo measurement (specs/008): running L², R², L·R after the master volume.
    std::atomic<float> masterStereoL_{0.0f}, masterStereoR_{0.0f}, masterStereoLR_{0.0f};
    std::atomic<bool> recording_{false};
    std::atomic<int32_t> xruns_{0};    // device dropouts since launch (M7-8)
    std::atomic<float> cpuLoad_{0.0f}; // smoothed RT DSP load 0..1 (Phase 11)
    std::atomic<dsp::TelemetryMode> telemetryMode_{dsp::TelemetryMode::OnDemand};
    std::atomic<bool> renderSuspended_{false}; // silence while start() re-prepares nodes
    std::atomic<int32_t> rendersInFlight_{0};
    std::atomic<double> launchQuant_{4.0};       // Session launch quantize, beats (M5-2)
    std::atomic<bool> arrangementActive_{false}; // false in session-only jam (M5)
    std::atomic<int32_t> countInBars_{0};
    std::atomic<bool> loopRecordReplace_{false};
    int32_t recordPass_ = 0;
    bool passReplace_ = false;
    bool passTriggered_ = false;
    std::atomic<int32_t> replaceUpTo_{0};
    void triggerPassReplace();                 // audio thread
    std::array<CtlFold, kCtlCount> ctlFold_{}; // specs/020 recorded controller fold state
    std::vector<RecordedCtl> ctlBatch_;
    bool ctlStopPending_ = false;
    double ctlStopAt_ = 0.0;
    std::vector<TakeCtl> takeCtls_;
    std::atomic<double> uiCountIn_{0.0};
    std::atomic<bool> captureGate_{true}; // false during clicks-only pre-roll
    bool recordCountIn_ = false;
    void playClock();
    bool enqueueTransportStart(int32_t countInBars);
    void startCountIn(int32_t bars);
    bool sessionCountIn_ = false; // #158
    std::atomic<int32_t> countInLaunchScene_{-1};
    bool sessionCountInFresh_ = false;
    uint64_t sessionCountInUndo_ = 0;
    std::vector<std::pair<int32_t, int32_t>> landedTakes_; // (track, scene)
    int32_t sceneFollowScene_ = -1;
    bool recordSessionGrowing_ = false;
    double recordSessionLen_ = 0.0; // #503 resolved length, beats (0 = open-ended)
    bool recordSessionOwnLen_ = false;
    bool takeLengthReached_ = false;
    template <bool Mapped = false>
    void mixAudioClip(const AudioClip& clip, float* dst, int32_t frames, double blockStart, double spb, float extraGain,
        const Transport& clock, bool forceSyncFill = false);
    std::array<std::atomic<int32_t>, kMaxMidiTakeLanes> midiLaneTrack_{}; // specs/086
    std::array<std::atomic<int32_t>, kMaxMidiTakeLanes> midiLaneSrc_{};
    std::array<std::atomic<int32_t>, kMaxMidiTakeLanes> midiLaneCh_{};
    std::atomic<int32_t> midiLaneCount_{0};
    std::vector<MidiTakeLane> midiLanes_;
    SnapshotHistory<SessionLaneSlots> laneSlotsHist_;
    double rtLaneNow_[16] = {}, rtLaneSlotLen_[16] = {};
    bool rtLaneSlot_[16] = {}, rtLaneLive_[16] = {};
    bool rtLaneWasLive_[16] = {}, rtLaneJustLive_[16] = {};
    int32_t recordStartStatus_ = 0; // 0 ok, 1 no armed track, 2 audio input failed

    TakeAttempt takeAttempt_; // discard & retry snapshot (specs/078)
    bool takePushing_ = false;
    struct TakePushScope {
        EngineRuntime& e;
        bool was;
        explicit TakePushScope(EngineRuntime& en) : e(en), was(en.takePushing_) { e.takePushing_ = true; }
        ~TakePushScope() { e.takePushing_ = was; }
    };
    bool forceSlotLaunch_ = false;
    double retryStartBeat_ = std::numeric_limits<double>::quiet_NaN();
    std::atomic<uint32_t> captureEpoch_{0}; // retry epoch marker (specs/078)
    uint32_t rtCaptureEpoch_ = 0;
    void markCaptureEpoch(uint32_t& last, uint32_t ep, bool hardware) {
        if (ep == last) return;
        last = ep;
        if (hardware) {
            recordRing_.pushMarker();
            return;
        }
        const float nan = std::numeric_limits<float>::quiet_NaN();
        internalQueue_.push(InputFrame{nan, nan});
    }
    uint32_t rtPendingEpoch_ = 0;
    int32_t rtWaitScene_ = -1;
    uint32_t rtTakeEpoch_ = 0;
    bool rtTakeRestart_ = false;
    void pushRecorded(RecordedNote n) {
        n.epoch = rtTakeEpoch_;
        recorded_.push(n);
    }
    void pushRecordedCtl(RecordedCtl x) {
        x.epoch = rtTakeEpoch_;
        recordedCtl_.push(x);
    }
    void pushRecordedExpr(RecordedExpr x) {
        x.epoch = rtTakeEpoch_;
        recordedExpr_.push(x);
    }

    // automation write/record state (M9-C, message thread)
    std::atomic<bool> autoRecord_{false};
    std::vector<ActiveWrite> activeWrites_;
    std::vector<TouchGesture> touchGestures_;
    std::vector<OverrideTarget> overrides_;
    bool wasPlaying_ = false;
    bool renderWasPlaying_ = false;
    PanLaw panLawFrom_ = PanLaw::Balance, panLawTo_ = PanLaw::Balance; // specs/006
    float panLawPos_ = 1.0f;
    std::atomic<bool> panLawSnap_{false};
    int32_t cachedStereoFrames_ = 0;
    double cachedStereoSr_ = 0.0;
    float cachedStereoStepA_ = 0.0f;
    float cachedStereoBlockStep(int32_t frames, double sr) noexcept {
        const double rate = sr > 0.0 ? sr : 44100.0;
        if (frames != cachedStereoFrames_ || rate != cachedStereoSr_) {
            cachedStereoFrames_ = frames;
            cachedStereoSr_ = rate;
            cachedStereoStepA_ = static_cast<float>(1.0 - std::exp(-frames / (0.3 * rate)));
        }
        return cachedStereoStepA_;
    }
    bool seekedThisBlock_ = false;
    bool seekedWhileRolling_ = false;
    bool chaseNotes_ = false;
    double articulationChaseStart_ = -1.0;
    bool seamOffs_ = false; // loop-wrap note release (#29)
    double seamEndSamples_ = 0.0;
    void onLoopWrap(Graph* g, double loopStartBeat, double loopEndBeat);
    int32_t releaseTrack_[16] = {-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1}; // specs/086
    uint32_t releasePitches_[16][4] = {};
    int32_t notesOffTracks_[8] = {};
    int32_t notesOffCount_ = 0;

    // session-slot recording target (M5-4)
    int32_t recordSessionTrackId_ = 0;
    int32_t recordSessionScene_ = -1;
    bool recordSessionAudio_ = false;
    std::atomic<SessionPlayer*> recordSlotPlayer_{nullptr};
    SnapshotHistory<SessionPlayer> recordSlotHist_;
    std::atomic<double> recordSlotLen_{4.0};

    // audio-thread-owned state
    bool toneEnabled_ = false;
    float frequency_ = 440.0f;
    double tonePhase_ = 0.0;
    float toneLevel_ = 0.0f; // 0..1 fade so start/stop don't click
    int64_t lastBeatEmitted_ = -1;
    bool countInActive_ = false; // clicking the count-in; the transport starts when it runs out
    double countInPos_ = 0.0;    // beats clicked so far
    double countInTotal_ = 0.0;  // beats to click
    int64_t lastCountInBeat_ = -1;
    int clickRemaining_ = 0;
    double clickPhase_ = 0.0;
    double clickFreq_ = 1000.0;

    // recording capture (audio thread)
    bool prevRecording_ = false;
    std::array<RtMidiLane, 16> rtMidiLanes_{}; // specs/086
    int32_t rtMidiLaneCount_ = 0;
    int32_t rtLaneSrc_[16] = {}, rtLaneCh_[16] = {};
    bool laneTakes(int32_t lane, const MidiEvent& e) const;
    int32_t midiLaneOfRt(int32_t trackId) const;
    void captureRoutedNotes(int32_t lane, const MidiEv* evs, int n, double blockStart, double spb, double clockOffset);
    void captureLiveExpr();
    bool liveDown_[128] = {}; // live keys held across record-arm (#56)
    double livePressBeat_[128] = {};
    float livePressVel_[128] = {};
    int32_t livePressTrack_[128] = {};
    int8_t livePressSource_[128] = {}, livePressChannel_[128] = {}; // specs/086
    bool routedDown_[128] = {};                                     // routed keys held on routedOwner_
    double routedPressBeat_[128] = {};
    float routedPressVel_[128] = {};
    int32_t routedOwner_ = -1;
    bool routedSeedFromNow_ = false; // specs/078
    bool routedSeedPending_ = false;
    void trackRoutedDown(int32_t ownerId, const MidiEv* evs, int n, double blockStart, double spb, bool timed);
    double countInClockOffset() const {
        const double denom = static_cast<double>(std::max(1, transport_.timeSigDenom()));
        return countInActive_ ? -(countInTotal_ - countInPos_) * 4.0 / denom : 0.0;
    }
    std::atomic<uint32_t> heldSeq_{0}; // held-note preview seqlock (UI reads, RT writes)
    std::atomic<int32_t> heldCount_{0};
    std::atomic<double> heldNow_{0.0};
    HeldSnapSlot heldSnap_[kMaxHeldSnap];
    double captureNow_ = 0.0;
    void publishHeldSnapshot(double nowBeat);

    // Clip controller & note expression helpers (specs/020)
    static void clipCtlEvents(const MidiClip& clip, double clipStartS, double clipEndS, double blockStart,
        int32_t frames, double spb, bool chase, ExprEv* out, int& n, int cap, const TempoMap* map = nullptr,
        double sr = 0, double sampleStart = 0);
    static void clipCtlEndResets(const MidiClip& clip, double clipStartS, double clipEndS, double blockStart,
        int32_t frames, ExprEv* out, int& n, int cap);
    static void noteExprEvents(const Note& note, double onS, double offS, int32_t frames, double spb, ExprEv* out,
        int& n, int cap, const TempoMap* map = nullptr, double sr = 0, double sampleStart = 0, double startBeat = 0,
        double shift = 0);

    CaptureLayoutSnapshot layoutAudio_;
    float* cueSeg_ = nullptr; // Cue buffer slice for current segment (specs/089)
    std::atomic<float> cueGain_{1.0f};
    std::atomic<int32_t> clickDest_{0};
    std::atomic<float> clickGain_{1.0f};
    std::atomic<float> cuePeakL_{0.0f}, cuePeakR_{0.0f}, cueRmsL_{0.0f}, cueRmsR_{0.0f};
    std::vector<float> monitorHwBuf_;
    std::atomic<bool> monitorSharedClock_{false};
    std::atomic<bool> hwMonitorTap_{false};
    std::atomic<bool> hwRecordTap_{false};
    bool hwMonitorWanted_ = false;

    // audio input recording (M4-3)
    RecordingOps recorder_{*this};
    std::unique_ptr<AudioInput> input_;
    CaptureRecordRing recordRing_;
    InputQueue internalQueue_;
    std::vector<CaptureLane> captureLanes_; // specs/086
    bool loopWrapsFrom(double beat) const { return loopEnabled_ && loopEnd_ > loopStart_ && beat < loopEnd_ - 1e-6; }
    bool audioRecording_ = false;
    double audioRecordStartBeat_ = 0.0;
    std::atomic<int32_t> internalRecordSource_{0}; // 0 hw/off, -1 master, >0 track id
    std::atomic<int64_t> inputDroppedFrames_{0};

    // per-block live MIDI events (audio thread)
    MidiEvent liveEvents_[kMaxLive];
    int liveCount_ = 0;
    MidiEvent liveCarry_{};
    bool liveCarried_ = false;
    MidiEvent liveExpr_[kMaxLiveExpr];
    int liveExprCount_ = 0;
    int32_t liveRelease_[kMaxLiveRelease] = {};
    int liveReleaseCount_ = 0;
    int liveNotesFor(const Track& t, bool untargeted, bool targeted, bool stillTakes, MidiEv* evs, int n);
    static int releaseRouted(Track::LiveHeld& held, MidiEv* evs, int n);
    bool takesLiveEvent(const Track& t, const MidiEvent& e) const;
    bool passesMidiInput(const Track& t, const MidiEvent& e) const;
    void applyLiveExpression(Graph* g, const Track& t, Instrument& inst) const;
    std::atomic<int32_t> auditionTrackId_{-1};
    std::atomic<float> liveCtl_[kCtlCount]{};
    std::atomic<uint32_t> liveCtlTouch_[kCtlCount]{};
    LiveCtlIn liveCtlIn_[kCtlInputs][kCtlCount]; // input 0 = none, 1 + s = MIDI source s
    LiveCtlIn liveCtlTrack_[kCtlCount];          // channel holds track id
    std::atomic<uint32_t> liveCtlStamp_{0};
    void noteLiveController(int32_t ctl, float value, int32_t source, int32_t channel, int32_t trackId = -1) {
        liveCtl_[ctl].store(value, std::memory_order_relaxed);
        liveCtlTouch_[ctl].fetch_add(1, std::memory_order_relaxed);
        LiveCtlIn& in = trackId >= 0                                       ? liveCtlTrack_[ctl]
                        : (source >= 0 && source < MidiInput::kMaxSources) ? liveCtlIn_[1 + source][ctl]
                                                                           : liveCtlIn_[0][ctl];
        in.value.store(value, std::memory_order_relaxed);
        in.channel.store(trackId >= 0 ? trackId : std::clamp(channel, 0, 15), std::memory_order_relaxed);
        in.stamp.store(liveCtlStamp_.fetch_add(1, std::memory_order_relaxed) + 1, std::memory_order_relaxed);
    }
    float laneStartCtl(int32_t lane, int32_t ctl) const;

    // render scratch, parallel pool & routing state
    std::vector<float> scratch_;
    std::vector<float> mixTmp_;
    int64_t leafWorkNs_ = 0; // parallel leaf pool adaptive hysteresis state
    int64_t parWorkNs_ = 0;
    int64_t sizeWorkNs_ = 0;
    double overrunShare_ = 0.0;
    double phaseNs_ = 0.0;
    double serialNs_ = 0.0;
    void adjustPoolSizing(double totalNs, double periodNs);
    int64_t leafPeakNs_ = 0;
    bool deviceBlock_ = false;
    bool rtParallel_ = false;
    double rtParallelLeftNs_ = 0;
    double rtProbeLeftNs_ = 0;
    double rtHoldNs_ = kRtHoldSeconds * 1e9;
    int32_t rtOverBudgetStreak_ = 0;
    SnapshotHistory<RenderScratch> scratchHist_;
    RenderScratch* rs_ = nullptr; // audio thread block scratch (AR-6)
    float* leafBase(size_t li, size_t stride) { return rs_->leafBuf.data() + li * stride * 2; }
    void ensureRenderScratch(size_t tracks, int32_t minFrames = 0);
    void ensureBlockCapacity(int32_t frames);
    bool renderLeafDry(Track& t, float* dst, const LeafWork& w, Graph* g, int32_t frames, double blockStart, double spb,
        bool playing, const TransportInfo& ti, bool arrangementActive, size_t trackIdx = SIZE_MAX);
    void processLeafDevices(Track& t, float* buf, const LeafWork& w, Graph* g, int32_t frames, double blockStart,
        double spb, bool playing, const TransportInfo& ti);
    std::unique_ptr<AudioThreadPool> pool_ = std::make_unique<AudioThreadPool>();
    std::unique_ptr<HybridBufferController> hybrid_;
    std::atomic<int64_t> streamThresholdBytes_{kDefaultStreamThresholdBytes};
    std::atomic<int32_t> sampleLoadPolicy_{static_cast<int32_t>(SampleLoadPolicy::Auto)};
    double pendingSeekBeat_ = -1.0;
    void syncStreamingPlaybackState(bool playing, double beatHint = -1.0) const;

    std::atomic<int32_t> freezeCaptureTrackId_{-1}; // M7 freeze capture
    std::vector<float> freezeCaptureBuf_;
    int64_t freezeCapFrames_ = 0;
    int64_t freezeCursor_ = 0;
    double freezeCaptureSpb_ = 0.0;
    std::shared_ptr<const Track::FrozenTiming> freezeCaptureTiming_;
    double preparedSR_ = 0.0;
    int32_t preparedBlockCap_ = 2048;
    std::vector<float> clipEnvScratch_;

    std::array<std::vector<float>, kMaxReturns> returnBus_; // M6-1 send/return buses
    std::array<std::vector<float>, kMaxGroups> groupBus_;   // group submix buses
    mutable RenderBusState<kMaxGroups, kMaxReturns> busState_;

    // Double-buffered route & pre-FX buses (Phase B/D, specs/050) + tap rings (#563)
    std::array<std::vector<float>, kMaxRoutes> routeBus_, routeBusNext_;
    std::array<std::vector<float>, kMaxRoutes> routeBusPre_, routeBusPreNext_;
    std::atomic<int32_t> routeSlotTrackId_[kMaxRoutes];
    std::atomic<uint64_t> routeSlotPart_[kMaxRoutes];
    std::atomic<int32_t> routeSlotCount_{0};
    std::atomic<uint64_t> routingEpoch_{1};
    std::vector<float> silentKey_;
    std::array<std::vector<float>, kMaxRoutes> trackTapRing_;
    int32_t trackTapRingTrack_[kMaxRoutes];
    int32_t trackTapRingHighWater_ = 0;
    int64_t trackTapRingPos_ = 0;
    int32_t monitorLag_ = 0, monitorLagPeak_ = 0;
    int64_t monitorLagWindow_ = 0;
    int32_t monitorLagFrom_ = 0, monitorFadePos_ = 0, monitorFadeLen_ = 0;
    void advanceMonitorLag(int32_t frames);
    void publishTrackTapRings(int32_t frames);
    void feedPartTaps(const Track& t);
    const float* keyBuffer(int32_t src, uint64_t part, bool pre) const;
    int32_t routeSlotFor(int32_t trackId, uint64_t part) const {
        const int32_t n = routeSlotCount_.load(std::memory_order_acquire);
        for (int32_t s = 0; s < n; ++s)
            if (routeSlotTrackId_[s].load(std::memory_order_relaxed) == trackId &&
                routeSlotPart_[s].load(std::memory_order_relaxed) == part)
                return s;
        return -1;
    }
    int32_t routeSlotForTrack(int32_t trackId) const { return routeSlotFor(trackId, 0); }
    bool trackFeedsRoute(int32_t trackId) const {
        const int32_t n = routeSlotCount_.load(std::memory_order_acquire);
        for (int32_t s = 0; s < n; ++s)
            if (routeSlotTrackId_[s].load(std::memory_order_relaxed) == trackId) return true;
        return false;
    }

    IoOps io_{*this};
    mutable std::unordered_map<int32_t, std::vector<float>> instrumentDefaults_;

    // specs/099 audio-thread state, kept last to preserve buffer alignment
    float masterPanG_[4] = {1.0f, 0.0f, 0.0f, 1.0f};
    SegAnchor segAnchors_[kMaxSegAnchors] = {};
    bool routeFresh_[kSidechainRoutes] = {};
};

} // namespace nota::detail
