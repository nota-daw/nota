// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RecordingOps — track arming, capture layout/inputs, live input monitoring + sinc resampler,
// live MIDI / take lanes, audio/loop-punch capture, take lanes & comping, and discard/retry.

#pragma once

#include "core/EnginePrelude.h"
#include "core/TempoMap.h"
#include "model/EngineTypes.h"
#include "model/MidiClip.h"
#include "nota/engine/Types.h"
#include "recording/CaptureRing.h"
#include "recording/RecordingTypes.h"
#include "render/RenderTypes.h"

#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace nota::detail {

class EngineRuntime;
class Track;
struct TakeGeometry;

class RecordingOps {
  public:
    explicit RecordingOps(EngineRuntime& rt) noexcept : rt_(rt) {}

    RecordingOps(const RecordingOps&) = delete;
    RecordingOps& operator=(const RecordingOps&) = delete;

    // --- track arming, inputs & monitoring ----------------------------------
    void setTrackArmed(int32_t trackId, bool armed);
    void setTrackRecordInput(int32_t trackId, int32_t source);
    int32_t trackRecordInput(int32_t trackId) const;
    void setTrackInputChannels(int32_t trackId, int32_t channels);
    int32_t trackInputChannels(int32_t trackId) const;
    bool setTrackInput(int32_t trackId, int32_t first, int32_t width);
    int32_t trackInput(int32_t trackId) const;
    bool trackInputAvailable(int32_t trackId) const;
    int32_t inputChannelCount() const;
    std::string inputChannelName(int32_t index) const;
    void rememberInputChannels();
    void forgetInputChannels();
    int32_t probeAudioInput();
    void setDisabledInputs(const int32_t* channels, int32_t count);
    void setTestInputDevice(int32_t channels);
    void updateCaptureLayout();
    bool syncCaptureLayout();
    const CaptureLayoutSnapshot* layoutFor(uint32_t generation) const;
    void setTrackMonitor(int32_t trackId, bool on);
    bool trackMonitor(int32_t trackId) const;
    bool ensureAudioInput();
    bool openAudioInput();
    void onInputBlock(const float* s, int32_t n, int32_t channels);
    void inputBlockForTest(const float* interleaved, int32_t frames, int32_t channels) {
        if (interleaved && frames > 0 && channels > 0) onInputBlock(interleaved, frames, channels);
    }
    void releaseAudioInputIfIdle();
    void updateMonitorInput();
    void pullMonitorInput(int32_t frames);
    void setMonitorInputForTest(double sampleRate, int32_t blockFrames);
    void pushMonitorFramesForTest(const float* interleavedStereo, int32_t frames);
    bool monitorSharedClockSelfTest();

    // --- live MIDI input & MIDI take lanes ----------------------------------
    void setTrackMidiSource(int32_t trackId, int32_t sourceTrackId);
    double trackMidiOffsetMs(int32_t trackId) const;
    int32_t trackMidiSource(int32_t trackId) const;
    void setTrackMidiOffsetMs(int32_t trackId, double ms);
    void noteOn(int32_t pitch, float velocity, int32_t source = -1, int32_t channel = 0);
    void noteOff(int32_t pitch, float releaseVelocity = 0.0f, int32_t source = -1, int32_t channel = 0);
    void noteExpression(int32_t pitch, int32_t dim, float value, int32_t source = -1, int32_t channel = 0);
    void trackNoteExpression(int32_t trackId, int32_t pitch, int32_t dim, float value);
    void controller(int32_t ctl, float value, int32_t source = -1, int32_t channel = 0);
    bool setTrackMidiInput(int32_t trackId, int32_t source, int32_t channel);
    bool setTrackMidiInputUid(int32_t trackId, const std::string& uid, int32_t channel);
    std::string trackMidiInputUid(int32_t trackId) const;
    void publishMidiLanes(const std::vector<int32_t>& trackIds);
    void closeMidiLane(int32_t lane);
    void publishSessionLaneSlots();
    bool addSessionMidiLane(int32_t trackId, int32_t scene);
    void cutGrowingLane(MidiTakeLane& l);
    void finishSessionMidiLane(int32_t lane);
    int32_t midiLaneOf(int32_t trackId) const;
    int32_t openMidiLaneCount() const;
    int32_t trackMidiInputSource(int32_t trackId) const;
    int32_t trackMidiInputChannel(int32_t trackId) const;
    void trackController(int32_t trackId, int32_t ctl, float value);
    void trackNoteOn(int32_t trackId, int32_t pitch, float velocity);
    void trackNoteOff(int32_t trackId, int32_t pitch, float releaseVelocity = 0.0f);
    int32_t liveHeldNotes(int32_t* out, int32_t maxN) const;
    int32_t recordHeldNotes(
        RecordHeldNote* out, int32_t maxN, int32_t* trackId, int32_t* sessionScene, double* nowBeat) const;

    // --- arrangement / audio recording --------------------------------------
    void setRecording(bool on);
    bool isRecording() const;
    int32_t recordStartStatus() const;
    bool isAudioRecording() const;
    int32_t audioRecordTrackId() const;
    double audioRecordStartBeat() const;
    bool startArrangementTake(bool wasPlaying, double playhead, bool retryCountIn);
    double takePlayhead() const;
    bool takeTransportPlaying() const;
    double takeTransportBeats() const;
    bool takeLoopBypassed() const;
    void beginCaptureLayout();
    void activateLoopPunch(double atBeat);
    void finishLoopPass(size_t to);
    const CaptureLane* captureLaneOf(int32_t trackId) const;
    double audioRecordLengthBeats(int32_t trackId = 0) const;
    void drainInputQueue();
    bool startAudioRecording(int32_t trackId);
    bool startAudioRecordingLanes(const std::vector<int32_t>& trackIds);
    void abortAudioRecording();
    void landCaptureSoFar();
    void stopAudioRecording();
    void finishCaptureLane(int32_t trackId);
    bool addSessionAudioLane(int32_t trackId, int32_t scene);
    void materialiseCaptureLane(CaptureLane& lane, const TakeGeometry& g);
    void materialiseLoopTakes(
        Track& nt, std::vector<LoopPass>& passes, size_t loopFrames, double loopStartBeat, double sampleRate);
    static double latencyCompensatedStart(
        double startBeat, int32_t latencyFrames, double samplesPerBeat, double* trimFrames = nullptr);
    int32_t audioRecordPeaks(float* outMinMax, int32_t maxPoints, int32_t trackId = 0) const;
    int32_t audioRecordingTracks(int32_t* out, int32_t cap) const;
    int32_t recordSkipped(int32_t* trackIds, int32_t* reasons, int32_t cap) const;
    void pushInputFramesForTest(const float* interleavedStereo, int32_t frames);
    void feedInputForTest(const float* interleaved, int32_t frames, int32_t channels);
    void pushInputFramesForTestN(const float* interleaved, int32_t frames, int32_t channels);
    void beginTestLane(int32_t trackId, int32_t first, int32_t width);
    bool audioRecordSelfTest();
    bool sessionAudioRecordSelfTest();

    // --- take lanes, comping, retry & MIDI fold -----------------------------
    std::vector<uint8_t> takeJoinEdges(const Track& t) const;
    bool settleTakeJoins(Track& nt) const;
    bool takeJoinsSettled(const Track& t) const;
    static bool mayHaveTakeJoins(const Track& t);
    void placeTakesForTempo();
    int32_t takeCount(int32_t trackId) const;
    bool takeInfo(int32_t trackId, int32_t index, TakeInfo* out) const;
    int32_t takePeaks(int32_t trackId, int32_t index, float* outMinMax, int32_t maxPoints) const;
    bool compTake(int32_t trackId, int32_t takeId, double startBeat, double endBeat);
    bool deleteTake(int32_t trackId, int32_t takeId);
    bool flattenTakes(int32_t trackId);
    int32_t clipTakeId(int32_t trackId, int32_t clipIndex) const;
    int32_t restoreTake(int32_t trackId, const std::string& path, int32_t number, int32_t group, double originBeat,
        double sourceOffsetFrames, int64_t lengthFrames);
    bool setClipTakeId(int32_t trackId, int32_t clipIndex, int32_t takeId);
    int32_t clipJoinedEdges(int32_t trackId, int32_t clipIndex) const;
    bool restoreJoinedEdges(int32_t trackId, const int32_t* edges, int32_t count);
    bool takeRecordTest(int32_t trackId, const float* interleavedStereo, int32_t frames, double startBeat);
    bool takeRecordTestN(int32_t trackId, const float* interleaved, int32_t channels, int32_t frames, double startBeat);
    bool takeRecordTestImpl(int32_t trackId, const float* interleaved, int32_t channels, int32_t frames,
        double startBeat, int32_t first, int32_t width);
    int32_t takeRecordTestArmed(const float* interleaved, int32_t channels, int32_t frames, double startBeat,
        int32_t disarmTrackId = 0, int32_t disarmAtFrame = -1);

    void openTakeAttempt(bool session);
    void noteTakeTrack(int32_t trackId);
    void keepTakeTrack(int32_t trackId);
    void syncTakeEpoch();
    void closeTakeAttempt();
    void discardTake();
    int32_t retryTake();

    void foldRecordedTake();
    bool foldSessionLane(MidiTakeLane& l, int32_t lane, const std::vector<RecordedNote>& batch,
        const std::vector<RecordedCtl>& ctls, bool stop);
    bool foldMidiLane(MidiTakeLane& l, int32_t lane, const std::vector<RecordedNote>& batch,
        const std::vector<RecordedCtl>& ctls, bool stop, int32_t replaceUpTo, bool replacePending);
    void collectRecordedExpr();
    void attachRecordedExpr(Note& n, int32_t pitch, double startBeat);
    void resetCtlFold();
    bool foldRecordedCtl(MidiClip& clip, double slotLen);
    void commitCtlRun(MidiClip& clip, int32_t ctl, double slotLen, bool final);
    void flushCtlTake();
    void closeCtlRange(MidiClip& clip, int32_t ctl, double atLocal, double slotLen);

  private:
    static int32_t exprKey(int32_t lane, int32_t pitch) { return lane * 128 + pitch; }
    static const float* monitorSincTable();
    void prepareMonitorResampler();
    void resetMonitorResampler(double ratio);
    void resampleMonitorInput(int32_t frames, double ratio);
    size_t readMonitorRing(float* dst, size_t want, bool& adopted, bool& layoutBreak);
    void skipMonitorFrames(size_t frames);

    EngineRuntime& rt_;

    // Live MIDI & recorded fold state
    std::mutex liveMidiMx_;
    std::mutex remoteMidiMx_;
    std::array<std::atomic<uint32_t>, 4> liveHeld_{};
    std::map<std::pair<int32_t, double>, NoteExpr> pendingExpr_;
    std::vector<TakeNote> takeNotes_;
    int32_t appliedReplaceUpTo_ = 0;

    // Recording & retry state
    int32_t recordTrackId_ = 0;
    std::atomic<int32_t> recordTrackRt_{-1};
    int32_t recordClipIndex_ = -1;
    bool recordStartedTransport_ = false;
    double recordReturnBeat_ = 0.0;
    bool retryingTake_ = false;
    uint32_t hwCaptureEpoch_ = 0;
    bool awaitCaptureMarker_ = false;
    int64_t retryKeepFrom_ = -1;
    double takeStartOverride_ = std::numeric_limits<double>::quiet_NaN();
    int takePlayingOverride_ = -1;
    double takeBeatOverride_ = std::numeric_limits<double>::quiet_NaN();
    uint32_t takeEpoch_ = 0;

    // Input channels & capture layout
    std::array<bool, kMaxInputChannels> inputDisabled_{};
    CaptureLayout captureLayout_;
    CaptureLayoutSnapshot layoutMsg_;
    CaptureLayoutSnapshot layoutInput_;
    std::array<CaptureLayoutSnapshot, kLayoutHistory> layoutHistory_{};
    int32_t inputTestChannels_ = 0;
    std::vector<std::string> inputChannelNames_;
    bool inputProbed_ = false;
    int32_t testLayoutChannels_ = 0;
    std::atomic<int32_t> inputBlockFrames_{0};
    bool inputTestMode_ = false;

    // Live input monitoring + sinc resampler
    CaptureMonitorRing monitorRing_;
    bool monitorPrimed_ = false;
    uint32_t monitorBlockGen_ = 0;
    std::atomic<double> monitorInRate_{0.0};
    std::atomic<const float*> monitorSinc_{nullptr};
    std::vector<float> monitorSrc_;
    int32_t monitorSrcFrames_ = 0;
    double monitorSrcPos_ = 0.0;
    double monitorSrcRatio_ = 0.0;
    int32_t monitorSrcHalf_ = 0;
    double monitorSrcCut_ = 1.0;
    double monitorFillAvg_ = 0.0;
    uint32_t monitorSrcGen_ = 0;
    bool monitorConverting_ = false;

    // Capture buffer & loop punch state
    std::vector<float> captureBlock_;
    size_t captureWritePos_ = 0;
    bool captureLoop_ = false;
    bool captureWrapped_ = false;
    size_t captureLoopFrames_ = 0;
    size_t captureOrigin_ = 0;
    double captureLoopStartBeat_ = 0.0;
    std::shared_ptr<const TempoMap> captureTempoMap_;
    size_t capturePassFrom_ = 0;
    bool captureLinearUnderLoop_ = false;
    std::vector<std::pair<int32_t, int32_t>> recordSkipped_;
    double audioRecordSampleRate_ = 48000.0;
    int32_t audioRecordTrackId_ = 0;
    int32_t audioRecordLatencyFrames_ = 0;
};

} // namespace nota::detail
