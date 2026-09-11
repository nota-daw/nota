// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M1/M2 engine: transport, an immutable audio graph of audio + instrument
// tracks, a mixer, metronome, audio-file playback, and sample-accurate MIDI
// (built-in synth/sampler, live input, recording). Structural edits happen on
// the message thread and publish a new graph snapshot (Graph.h); the audio
// thread reads the current snapshot once per block and never allocates or
// locks (AR-6).

#pragma once

#include "GamepadInput.h"
#include "AudioBackend.h"
#include "AudioConfig.h"
#include "AudioInput.h"
#include "CommandQueue.h"
#include "Graph.h"
#include "MidiConfig.h"
#include "MidiInput.h"
#include "GamepadInput.h"
#include "SampleBuffer.h"
#include "Transport.h"
#include "nota/nota_engine.h"   // NotaNoteData

#include <array>
#include <atomic>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace nota {

// Live MIDI event (UI/CoreMIDI -> audio), lock-free down-queue.
class RackCore;   // shared rack guts (RackInstrument / RackDevice; see RackCore.h)
class ClipWarpStream;   // offline warp-cache builder (WarpStream.h); held by unique_ptr in wb_

struct MidiEvent { bool on; int32_t pitch; float velocity; };
using MidiQueue = SpscRingBuffer<MidiEvent, 1024>;

// Recorded note (audio -> message), lock-free up-queue. startBeat is absolute.
struct RecordedNote { int32_t pitch; double startBeat; double lengthBeats; float velocity; };
using RecordedQueue = SpscRingBuffer<RecordedNote, 1024>;

// Captured input frame (input thread -> message), lock-free up-queue (M4-3).
struct InputFrame { float l; float r; };
using InputQueue = SpscRingBuffer<InputFrame, 1u << 17>;  // ~3 s at 44.1 kHz — headroom for UI-thread drain stalls

// Incoming MIDI control event surfaced to the UI for MIDI-learn (MIDI thread ->
// message thread, lock-free up-queue). kind: 0 = control-change, 1 = note-on.
// For CC, number is the controller and value its 0..127 data; for notes, number
// is the pitch and value the velocity. The engine still routes notes to instruments.
struct ControlEvent { int32_t kind; int32_t channel; int32_t number; int32_t value; };
using ControlEventQueue = SpscRingBuffer<ControlEvent, 1024>;

class Engine {
public:
    Engine();
    ~Engine();

    bool   start();
    void   stop();
    double sampleRate() const { return backend_ ? backend_->sampleRate() : 0.0; }

    // --- audio device settings (M7-1) ---
    // The chosen device/rate/buffer live in config_ (persisted to audio.json)
    // and are applied on start(). Enumeration + apply are thin passthroughs.
    const AudioConfig& audioConfig() const { return config_; }
    void   setAudioOutputDevice(const std::string& uid);
    void   setAudioInputDevice(const std::string& uid);
    void   setAudioSampleRate(double sr);
    void   setAudioBufferFrames(int32_t frames);
    void   setAudioWasapiExclusive(bool on);   // Windows/WASAPI only
    // Persist current config and restart the backend so it takes effect.
    bool   applyAudioConfig();
    // True if the last backend start() requested WASAPI exclusive mode but the
    // device refused it and the backend fell back to shared mode. Always false
    // on platforms without exclusive mode. Read after applyAudioConfig() / start().
    bool   audioExclusiveFallback() const { return backend_ ? backend_->exclusiveFallback() : false; }
    int32_t bufferFrames() const { return backend_ ? backend_->bufferFrames() : 0; }

    // --- MIDI device settings (M7-2) ---
    // Which inputs the engine listens to; persisted as a blocklist of disabled
    // uids in midi.json (empty = all on). Setters stage midiConfig_; apply
    // reopens the CoreMIDI port (no audio-backend restart needed).
    const MidiConfig& midiConfig() const { return midiConfig_; }
    void setMidiInputEnabled(const std::string& uid, bool enabled);
    bool midiInputEnabled(const std::string& uid) const;
    bool applyMidiConfig();

    // MIDI-learn: drain up to `max` incoming CC/note-on events captured since the
    // last call into `out`. Returns the count written. Called from the UI tick.
    int32_t pollControlEvents(ControlEvent* out, int32_t max);

    // --- gamepad input (live note source) ---
    // The pad runs on its own IOKit thread and queues button edges; the UI
    // polls them and turns them into note_on/note_off calls (same path as the
    // computer keyboard). Start/stop mirror the engine lifecycle.
    void               startGamepadInput();
    void               stopGamepadInput();
    int32_t            gamepadCount() const;
    void               gamepadInfo(int32_t i, const char** outUid, const char** outName) const;
    int32_t            pollGamepadEvents(GamepadInput::ButtonEvent* out, int32_t max);

    // --- audio preview / audition (M7-4a) ---
    // Decode a file and mix it into the live output without a track, so the
    // browser can audition samples. One preview at a time.
    bool previewFile(const std::string& path);
    void stopPreview();
    bool isPreviewActive() const { return previewActive_.load(std::memory_order_relaxed); }
    bool previewSelfTest();   // feed a synthetic buffer, render offline, no device

    // --- xrun / dropout telemetry (M7-8) ---
    // The backend's overload listener bumps this off the RT thread; the UI polls
    // the running total each tick and surfaces a non-fatal message.
    void    notifyXrun() { xruns_.fetch_add(1, std::memory_order_relaxed); }
    int32_t xrunCount() const { return xruns_.load(std::memory_order_relaxed); }
    bool    xrunSelfTest();   // device-free: notifyXrun bumps the count by one

    // --- DSP load (Phase 11): RT render time / block budget, smoothed 0..1 ---
    float   cpuLoad() const { return cpuLoad_.load(std::memory_order_relaxed); }

    // --- parameter automation (M9) ---
    // Device-free: interpolation + block-rate apply write the target atomic.
    bool    automationSelfTest();
    bool    automationCurveSelfTest();   // M9-D: per-segment curvature shaping

    // --- CV modulation (Phase 3, Modular editor) ---
    // Modulator sources (LFO) + edges to built-in device params on the same track.
    // Add/remove are structural (clone + republish); field/depth/mode edits hit the
    // live object's atomics. Applied block-rate in mixGraph around the mix (base is
    // the device atomic, restored each block so the UI keeps seeing it).
    int32_t addModulator(int32_t trackId, int32_t kind);
    bool    removeModulator(int32_t trackId, int32_t modId);
    int32_t modulatorCount(int32_t trackId) const;
    int32_t modulatorIdAt(int32_t trackId, int32_t index) const;
    int32_t modulatorKind(int32_t trackId, int32_t modId) const;
    float   modulatorGet(int32_t trackId, int32_t modId, int32_t field) const;
    void    modulatorSet(int32_t trackId, int32_t modId, int32_t field, float value);
    float   modulatorValue(int32_t trackId, int32_t modId) const;  // live output for viz
    int32_t modulatorScope(int32_t trackId, int32_t modId, float* out, int32_t cap) const;  // CV scope history
    int32_t addCvLink(int32_t trackId, int32_t modId, int32_t device, int32_t param);
    // Cross-track: the modulator lives on trackId; the target param is on targetTrack
    // (-1 = trackId itself). The link is owned by the modulator's track.
    int32_t addCvLinkTo(int32_t trackId, int32_t modId, int32_t targetTrack, int32_t device, int32_t param);
    // Param → param: a source param (srcDevice/srcParam on trackId) drives a target param.
    int32_t addCvLinkFromParam(int32_t trackId, int32_t srcDevice, int32_t srcParam,
                               int32_t targetTrack, int32_t targetDevice, int32_t targetParam);
    // Target kind (0 device, 1 instrument, 2 MIDI-FX) variants for modulator/param sources.
    int32_t addCvLinkToTarget(int32_t trackId, int32_t modId, int32_t targetKind,
                              int32_t targetTrack, int32_t targetDevice, int32_t targetParam);
    int32_t addCvLinkFromParamToTarget(int32_t trackId, int32_t srcDevice, int32_t srcParam,
                                       int32_t targetKind, int32_t targetTrack, int32_t targetDevice, int32_t targetParam);
    bool    paramModulated(int32_t targetKind, int32_t trackId, int32_t device, int32_t param) const;
    bool    removeCvLink(int32_t trackId, int32_t index);
    int32_t cvLinkCount(int32_t trackId) const;
    int32_t cvLinkSource(int32_t trackId, int32_t index) const;
    int32_t cvLinkSourceKind(int32_t trackId, int32_t index) const;
    int32_t cvLinkSourceDevice(int32_t trackId, int32_t index) const;
    int32_t cvLinkSourceParam(int32_t trackId, int32_t index) const;
    int32_t cvLinkTargetKind(int32_t trackId, int32_t index) const;
    int32_t cvLinkTargetTrack(int32_t trackId, int32_t index) const;
    int32_t cvLinkDevice(int32_t trackId, int32_t index) const;
    int32_t cvLinkParam(int32_t trackId, int32_t index) const;
    float   cvLinkDepth(int32_t trackId, int32_t index) const;
    int32_t cvLinkMode(int32_t trackId, int32_t index) const;
    float   cvLinkBase(int32_t trackId, int32_t index) const;      // modulation centre value
    void    setCvLinkDepth(int32_t trackId, int32_t index, float depth);
    void    setCvLinkMode(int32_t trackId, int32_t index, int32_t mode);
    void    setCvLinkBase(int32_t trackId, int32_t index, float base);
    bool    deviceParamModulated(int32_t trackId, int32_t device, int32_t param) const;
    bool    modulationSelfTest();
    bool    rackSelfTest();              // Instrument Rack: chains sum, macros map, blob round-trips
    bool    rackDeviceSelfTest();        // Audio Effect Rack: pass-through, parallel sum, blob round-trips
    bool    drumRackSelfTest();          // Drum Rack: notes route only to the matching pad chain
    // Lane CRUD — structural (snapshot + undo). addAutomationLane returns the lane
    // index, reusing an existing lane on the same target. get/set use the ABI point
    // struct; getAutomationPoints(out=null) returns the count.
    int32_t addAutomationLane(int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex);
    int32_t automationLaneCount(int32_t trackId) const;
    bool    automationLaneInfo(int32_t trackId, int32_t laneIndex, int32_t* outTarget,
                int32_t* outDeviceIndex, int32_t* outParamIndex, int32_t* outPointCount) const;
    int32_t getAutomationPoints(int32_t trackId, int32_t laneIndex, NotaAutomationPoint* out, int32_t cap) const;
    bool    setAutomationPoints(int32_t trackId, int32_t laneIndex, const NotaAutomationPoint* pts, int32_t count);
    bool    setAutomationPointsLive(int32_t trackId, int32_t laneIndex, const NotaAutomationPoint* pts, int32_t count);
    bool    setAutomationPointsImpl(int32_t trackId, int32_t laneIndex, const NotaAutomationPoint* pts, int32_t count, bool undoCheckpoint);
    bool    removeAutomationLane(int32_t trackId, int32_t laneIndex);

    // Master-volume automation (graph-level, not tied to a track). One lane applied
    // to the master gain in applyAutomation; persisted in `.nota` v5.
    int32_t masterVolumeAutomationCount() const;
    int32_t getMasterVolumeAutomation(NotaAutomationPoint* out, int32_t cap) const;
    bool    setMasterVolumeAutomation(const NotaAutomationPoint* pts, int32_t count);

    // Per-clip volume envelope (M9 follow-up): 0..1 curve in clip-local beats that
    // multiplies the audio clip's output. get(out=null) returns the point count.
    int32_t getAudioClipVolumeEnvelope(int32_t trackId, int32_t clipIndex, NotaAutomationPoint* out, int32_t cap) const;
    bool    setAudioClipVolumeEnvelope(int32_t trackId, int32_t clipIndex, const NotaAutomationPoint* pts, int32_t count);
    int32_t getAudioClipPanEnvelope(int32_t trackId, int32_t clipIndex, NotaAutomationPoint* out, int32_t cap) const;
    bool    setAudioClipPanEnvelope(int32_t trackId, int32_t clipIndex, const NotaAutomationPoint* pts, int32_t count);
    // MIDI clip envelopes (kind: 0 = velocity, 1 = volume; 0..1 in clip-local beats).
    int32_t getMidiClipEnvelope(int32_t trackId, int32_t clipIndex, int32_t kind, NotaAutomationPoint* out, int32_t cap) const;
    bool    setMidiClipEnvelope(int32_t trackId, int32_t clipIndex, int32_t kind, const NotaAutomationPoint* pts, int32_t count);

    // --- hosted-plugin parameter automation (M9-B) ---
    // deviceIndex < 0 = the track's instrument; otherwise the effect at that index.
    // Params are normalized 0..1, identified by a stable string paramId.
    bool        pluginAutomationSelfTest();     // device-free structural check
    int32_t     pluginParamCount(int32_t trackId, int32_t deviceIndex) const;
    std::string pluginParamId(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    std::string pluginParamName(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    float       pluginParamGet(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    void        pluginParamSet(int32_t trackId, int32_t deviceIndex, int32_t paramIndex, float normalized);
    int32_t     pluginParamIndexOfId(int32_t trackId, int32_t deviceIndex, const std::string& id) const;
    int32_t     lastTouchedPluginParam(int32_t trackId, int32_t deviceIndex); // "Learn" (M9-B3)
    // Add (or reuse) a PluginParam lane; resolves paramId -> current index now.
    int32_t     addPluginAutomationLane(int32_t trackId, int32_t deviceIndex, const char* paramId);
    std::string automationLaneParamId(int32_t trackId, int32_t laneIndex) const;

    // --- automation write / record (M9-C) ---
    // Mode gates recording: Read=0 (default), Touch=1, Latch=2, Write=3. begin/end
    // bracket a control gesture; arm marks a target for Write (records from play).
    // Sampling happens in poll() while playing; suppressRead lets the control lead.
    void    setAutomationWriteMode(int32_t mode);
    int32_t automationWriteMode() const { return autoWriteMode_.load(std::memory_order_relaxed); }
    void    beginAutomationWrite(int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId);
    void    endAutomationWrite(int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId);
    void    setAutomationArm(int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId, bool armed);
    bool    automationArmed(int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId) const;
    bool    automationWriteSelfTest();   // device-free: Touch gesture + Write-via-arm

    // --- debug oscillator (M0) ---
    void setToneEnabled(bool enabled);
    void setFrequency(float hz);

    // --- transport (M1) ---
    void transportPlay();
    void transportStop();
    // Session vs Arrangement (M5): the Arrangement is "active" when the main Play (or an
    // arrangement record) started it. Launching a Session clip starts the clock WITHOUT
    // activating the Arrangement, so non-session tracks stay silent (session-only jam).
    // backToArrangement stops every session clip and hands all tracks back to the timeline.
    bool arrangementActive() const { return arrangementActive_.load(std::memory_order_relaxed); }
    void backToArrangement();
    void setBpm(double bpm);
    double bpm() const;
    void setTimeSignature(int num, int denom);
    void setLoop(bool enabled, double startBeat, double endBeat);
    void setMetronome(bool on);
    void seekBeats(double beat);
    double positionBeats() const { return transport_.uiPositionBeats(); }
    bool   isPlaying() const { return transport_.uiIsPlaying(); }
    // Message-thread mirror of the loop region (for UI display; the audio thread
    // gets the range via the command queue).
    bool   loopEnabled() const { return loopEnabled_; }
    double loopStart() const { return loopStart_; }
    double loopEnd() const { return loopEnd_; }

    // --- mixer ---
    void setMasterVolume(float v) { masterVolume_.store(v, std::memory_order_relaxed); }
    // Reserved id for the master effect chain (not a real track in the graph list). The
    // device/name/colour ops accept it via findTrackAuthoring, so the UI drives the master
    // chain through the same path as any track. Large so it never collides with real ids.
    static constexpr int32_t kMasterTrackId = 1000000;
    int32_t masterTrackId() const { return kMasterTrackId; }

    // --- structural edits: audio (M1) ---
    int32_t addAudioTrack();
    int32_t addAudioClip(int32_t trackId, const std::string& path, double startBeat);
    void    setTrackVolume(int32_t trackId, float v);
    void    setTrackPan(int32_t trackId, float p);
    void    setTrackMute(int32_t trackId, bool m);
    void    setTrackSolo(int32_t trackId, bool s);
    int32_t trackCount() const;
    // Track lifecycle (context menu). duplicateTrack deep-copies everything
    // (mixer, sends, clips, session, automation, fresh instrument/devices —
    // plugins re-instantiate with state) and inserts the copy after the source;
    // returns the new id (or -1). removeTrack drops it. Both checkpoint undo.
    int32_t duplicateTrack(int32_t trackId);
    bool    removeTrack(int32_t trackId);
    // Reorders a non-return track to toIndex within the regular (non-return) track list;
    // returns/master keep their slots. Checkpoints undo. No-op for return tracks.
    bool    moveTrack(int32_t trackId, int32_t toIndex);

    // --- freeze (M7) --------------------------------------------------------
    // Freezing bounces a track's post-device, pre-fader audio to a buffer and plays
    // it back instead of the live instrument+device chain (CPU relief); the mixer
    // strip stays live. beginFreeze allocates + arms the capture — the CALLER must
    // have the backend stopped and then pump renderOffline() for the returned frame
    // count (the capture is filled inside mixGraph). endFreeze publishes the frozen
    // track; cancelFreeze aborts without freezing. Not undoable (a view toggle).
    int64_t beginFreeze(int32_t trackId, double lengthBeats);  // capture frames, or 0 on failure
    void    endFreeze(int32_t trackId);
    void    cancelFreeze();
    void    unfreeze(int32_t trackId);                         // drop the buffer, go back to live
    bool    trackFrozen(int32_t trackId) const;
    // Opaque frozen-audio blob for project save/restore (header + interleaved PCM).
    // get copies up to `cap` bytes and returns the full size (out=NULL probes size);
    // set restores + marks the track frozen. Empty when the track isn't frozen.
    int32_t freezeGetState(int32_t trackId, uint8_t* out, int32_t cap) const;
    void    freezeSetState(int32_t trackId, const uint8_t* data, int32_t size);

    // --- track groups (submix buses) ---
    // A Group track sums its child tracks (leaves or nested groups) and runs them through
    // its own device chain + fader/pan before the master. Membership is the child's groupId.
    int32_t addGroupTrack();                             // empty top-level group
    int32_t createGroup(const int32_t* ids, int32_t n);  // group these tracks; returns group id
    bool    ungroup(int32_t groupId);                    // dissolve; children reparent up one level
    bool    setTrackGroup(int32_t trackId, int32_t groupId); // move track into groupId (-1 = top-level)

    // --- send/return buses (M6-1) ---
    int32_t addReturnTrack();                            // aux bus: device chain -> master
    void    setTrackSend(int32_t trackId, int32_t bus, float level); // post-fader send (atomic)
    float   trackSend(int32_t trackId, int32_t bus) const;
    int32_t returnTrackCount() const;                    // number of return buses in use
    int32_t trackReturnIndex(int32_t trackId) const;     // bus slot for a return track, else -1
    int32_t getClipPeaks(int32_t trackId, int32_t clipIndex, float* outMinMax, int32_t maxPoints) const;
    int32_t getClipSourcePeaks(int32_t trackId, int32_t clipIndex, float* outMinMax, int32_t maxPoints) const;
    int32_t getClipWarpFullPeaks(int32_t trackId, int32_t clipIndex, float* outMinMax, int32_t maxPoints) const;

    // --- incremental warp-cache build (project load, with a progress bar) ---
    void    setDeferWarpBuild(bool defer);               // load path: skip cache builds, fill them via warpBuildStep
    int64_t warpBuildStep(int32_t maxFrames);            // build ~maxFrames of warped audio; returns frames remaining
    int64_t warpBuildRemainingFrames() const;            // frames of warped audio still needing a cache (progress)

    // --- arrangement geometry (M4) ---
    bool trackInfo(int32_t index, NotaTrackInfo* out) const;
    bool clipInfo(int32_t trackId, int32_t clipIndex, NotaClipInfo* out) const;

    // --- Session view (M5) ---
    void    setLaunchQuant(double beats);
    void    launchSlot(int32_t trackId, int32_t scene);   // quantized; empty slot = stop track
    void    launchScene(int32_t scene);                   // launch every track's slot in this row (empty = stop that track)
    void    stopSlot(int32_t trackId);
    void    stopScene(int32_t scene);                     // stop tracks whose playing/queued slot is this row
    void    stopAllSession();
    void    recordSessionSlot(int32_t trackId, int32_t scene); // M5-4: launch + overdub-record MIDI into the slot
    void    stopSessionRecord();                          // stop recording; the slot keeps playing
    // M5-6: move clips between Session and Arrangement (MIDI, instrument tracks).
    int32_t sessionSlotToArrangement(int32_t trackId, int32_t scene, double startBeat); // -> new clip index or -1
    bool    arrangementClipToSession(int32_t trackId, int32_t clipIndex, int32_t scene);
    bool    arrangementAudioClipToSession(int32_t trackId, int32_t clipIndex, int32_t scene); // audio clip -> session slot
    int32_t sceneCount() const;
    bool    clearSessionSlot(int32_t trackId, int32_t scene);   // empty a slot (stops it if playing)
    int32_t addScene();                                         // append a scene; returns its index
    bool    removeScene(int32_t scene);                         // drop a scene row (keeps at least one)
    bool    addSessionMidiClip(int32_t trackId, int32_t scene, double lengthBeats);
    double  sessionSlotLength(int32_t trackId, int32_t scene) const;              // loop length in beats, 0 if none (M5-5)
    bool    setSessionSlotLength(int32_t trackId, int32_t scene, double lengthBeats); // change loop length (M5-5)
    float   sessionSlotGain(int32_t trackId, int32_t scene) const;                // audio-slot playback gain
    bool    setSessionSlotGain(int32_t trackId, int32_t scene, float gain);
    int32_t sessionSlotState(int32_t trackId, int32_t scene) const; // 0 empty, 1 filled (M5-1)
    bool    setSessionNotes(int32_t trackId, int32_t scene, const NotaNoteData* notes, int32_t count);
    int32_t getSessionNotes(int32_t trackId, int32_t scene, NotaNoteData* out, int32_t maxNotes) const;
    int32_t sessionNoteCount(int32_t trackId, int32_t scene) const;

    // --- clip editing (M4-2) ---
    bool    moveClip(int32_t trackId, int32_t clipIndex, double newStartBeat);
    // Move a clip to another same-type track at newStartBeat (instrument→instrument
    // or audio→audio; atomic — both tracks change in one publish). Same-track falls
    // back to moveClip.
    bool    moveClipToTrack(int32_t srcTrackId, int32_t clipIndex, int32_t sourceTrackId, double newStartBeat);
    bool    trimClip(int32_t trackId, int32_t clipIndex, double newStartBeat, double newLengthBeats);
    // Grid resize for an audio clip (grid-relative): warped clips (or unwarped
    // clips dragged past their source length) stretch to newLengthBeats; unwarped
    // clips within source bounds trim. Auto-enables warp when a stretch is needed.
    bool    resizeAudioClip(int32_t trackId, int32_t clipIndex, double newStartBeat, double newLengthBeats);
    bool    setClipSourceRegion(int32_t trackId, int32_t clipIndex, double offsetFrames, int64_t lengthFrames);
    bool    setClipWarpTrim(int32_t trackId, int32_t clipIndex, double playStart, double playEnd);
    int32_t splitClip(int32_t trackId, int32_t clipIndex, double atBeat);   // -> new clip index or -1
    int32_t duplicateClip(int32_t trackId, int32_t clipIndex);              // -> new clip index or -1
    bool    deleteClip(int32_t trackId, int32_t clipIndex);

    // Time-range clip ops (arrangement time-selection). Both split clips at the range
    // boundaries so only the covered content is affected, in one undo step. No ripple.
    // deleteClipsInRange carves [start,end) out of every listed track. duplicateRange
    // copies that slice to [end, end+(end-start)) and returns the range length (>0), or -1.
    bool    deleteClipsInRange(const std::vector<int32_t>& trackIds, double start, double end);
    double  duplicateRange(const std::vector<int32_t>& trackIds, double start, double end);
    // splitClipsAtRange cuts every listed track's clips at both `start` and `end` (keeping
    // all content) so the covered slice becomes its own clip(s). One undo step; no ripple.
    bool    splitClipsAtRange(const std::vector<int32_t>& trackIds, double start, double end);
    // consolidateRange (Consolidate, ⌘J) replaces every listed track's content in
    // [start,end) with ONE clip spanning the range; the parts of clips outside it survive.
    // MIDI merges the covered notes (tails cut by a clip end stay cut, velocity envelopes
    // baked into the notes, volume envelopes stitched into one). Audio renders what the
    // covered clips play — gain, pitch, warp, edge fades, clip envelopes — into a new
    // sample (warped when any source was, so it keeps following tempo). Deactivated
    // clips contribute silence. One undo step; lastPlaced() reports the new clips.
    bool    consolidateRange(const std::vector<int32_t>& trackIds, double start, double end);

    // Automation-follows-clips (req 8.3). When unlocked (default), moving a clip carries the
    // track automation in its span with it: same-track moves take everything; cross-track moves
    // take only the common params (Volume/Pan) and leave device/plugin automation on the source
    // (their indices are positional). setAutomationLock(true) freezes envelopes in place.
    void setAutomationLock(bool locked) { automationLock_ = locked; }
    bool automationLock() const { return automationLock_; }
    // True when the last cross-track move left device/plugin automation behind on the source
    // (so the UI can say so, req 8.3.4). Reset at the start of each move.
    bool lastMoveKeptDeviceAutomation() const { return lastMoveKeptDeviceAuto_; }

    // Clip clipboard (copy/cut/paste, incl. cross-track). copyClip stores a clone of
    // the clip; pasteClip inserts it into a type-matching track at (or after) atBeat,
    // shifting right so it never overlaps an existing clip. clipboardClipKind: -1 empty,
    // 0 audio, 1 midi.
    bool    copyClip(int32_t trackId, int32_t clipIndex);
    // Cut = copy (clip + its automation) then remove both the clip and the track
    // automation lying in the clip's span. Delete leaves automation untouched.
    bool    cutClip(int32_t trackId, int32_t clipIndex);
    int32_t pasteClip(int32_t sourceTrackId, double atBeat);                  // -> new clip index or -1
    int32_t clipboardClipKind() const;

    // Block clip clipboard (multi-selection copy/cut/paste + duplicate). A "block" is a
    // set of clips across tracks captured with their track ids, beats relative to the
    // block start, and each clip's automation. Paste re-lands the whole block onto the
    // same tracks at atBeat, preserving the internal geometry; duplicate places it right
    // after itself. Overlap is avoided by shifting the WHOLE block by one shared delta so
    // relative positions never change. Each op is a single undo step. lastPlaced() reports
    // the (trackId, clipIndex) of every clip the last paste/duplicate created, so the UI
    // can re-select them.
    bool    copyClipBlock(const std::vector<std::pair<int32_t,int32_t>>& sel);
    bool    cutClipBlock(const std::vector<std::pair<int32_t,int32_t>>& sel);
    int32_t pasteClipBlock(double atBeat, int32_t sourceTrackId);            // -> clips pasted
    double  duplicateClipBlock(const std::vector<std::pair<int32_t,int32_t>>& sel); // -> block length (>0), or -1
    int32_t clipboardBlockCount() const { return static_cast<int32_t>(clipboardBlock_.size()); }
    const std::vector<std::pair<int32_t,int32_t>>& lastPlaced() const { return lastPlaced_; }

    // Clip / track UI metadata (name + track colour). Names are user-facing; colorIndex
    // is a palette slot (-1 = auto). clipName/trackName write into out (up to cap-1 chars
    // + NUL) and return the full length.
    bool    setClipName(int32_t trackId, int32_t clipIndex, const std::string& name);
    int32_t clipName(int32_t trackId, int32_t clipIndex, char* out, int32_t cap) const;
    bool    setTrackName(int32_t trackId, const std::string& name);
    int32_t trackName(int32_t trackId, char* out, int32_t cap) const;
    bool    setTrackColorIndex(int32_t trackId, int32_t colorIndex);
    int32_t trackColorIndex(int32_t trackId) const;

    // Track clipboard (copy/cut/paste). copyTrack stores a fully independent clone;
    // pasteTrack appends a fresh copy (new id + cloned DSP). Cut = copy + removeTrack.
    bool    copyTrack(int32_t trackId);
    int32_t pasteTrack();                      // -> new track id, or -1
    bool    hasTrackClipboard() const;

    // --- project load (M7-6) ---
    // Clear the whole session to an empty project (stop transport, drop all
    // tracks, reset track-id counter and undo/redo). Message thread only.
    void reset();
    // Instrument identity for save: 0=Synth, 1=Sampler, -1=plugin/unknown,
    // -2=track has no instrument (audio/return). Message thread only.
    int32_t trackInstrumentKind(int32_t trackId) const;
    // Built-in device kind for save (see Device::builtinKind), or -1 for a
    // hosted plugin. Message thread only.
    int32_t trackDeviceBuiltinKind(int32_t trackId, int32_t deviceIndex) const;
    // Stable plugin identifier of a track's instrument / effect (M7-6c), or ""
    // if it isn't a hosted plugin. Message thread only.
    std::string trackInstrumentPluginId(int32_t trackId) const;
    std::string trackDevicePluginId(int32_t trackId, int32_t deviceIndex) const;

    // --- audio persistence (M7-6b) ---
    // Full geometry of an audio clip (false if the clip isn't audio).
    bool    audioClipInfo(int32_t trackId, int32_t clipIndex, NotaAudioClipInfo* out) const;
    bool    setClipGain(int32_t trackId, int32_t clipIndex, float gain);       // audio clip runtime gain
    bool    setClipActive(int32_t trackId, int32_t clipIndex, bool active);    // clip deactivate (key 0): audio+MIDI
    bool    setClipPitch(int32_t trackId, int32_t clipIndex, float semitones); // audio clip varispeed transpose
    bool    setClipWarp(int32_t trackId, int32_t clipIndex, bool enabled, int32_t mode); // toggle/mode + rebuild cache
    double  autoWarpClip(int32_t trackId, int32_t clipIndex); // detect tempo, enable warp, snap length to grid; returns detected BPM (0 = failed)
    double  beatWarpClip(int32_t trackId, int32_t clipIndex); // Beats mode: detect transients, pin grid-snapped markers at each hit; returns detected BPM (0 = failed)
    bool    setClipWarpLength(int32_t trackId, int32_t clipIndex, double beats);          // warped clip target length
    bool    setClipWarpMarkers(int32_t trackId, int32_t clipIndex, const double* srcFrames, const double* beats, int32_t count);
    int32_t clipWarpMarkers(int32_t trackId, int32_t clipIndex, double* outSrc, double* outBeat, int32_t maxCount) const;
    // Add an audio clip with restored offset/length/gain. Returns clip index or -1.
    int32_t addAudioClipEx(int32_t trackId, const std::string& path, double startBeat,
                           double sourceOffsetFrames, int64_t lengthFrames, float gain);
    // Decoded-sample metadata / raw interleaved read, keyed by sample id.
    bool    sampleInfo(int64_t sampleId, NotaSampleInfo* out) const;
    // Copies up to `cap` interleaved floats into `out`; returns the total count
    // (frames*channels). Pass out=null to query the size.
    int64_t sampleRead(int64_t sampleId, float* out, int64_t cap) const;
    // Built-in Sampler settings (false if the track's instrument isn't a Sampler).
    bool    samplerInfo(int32_t trackId, NotaSamplerInfo* out) const;
    bool    grainInfo(int32_t trackId, NotaSamplerInfo* out) const;   // Nota Grain sample id/root
    int32_t grainPlayPositions(int32_t trackId, float* out, int32_t maxN) const;  // live grain read positions (0..1)
    int32_t instrumentVoiceCount(int32_t trackId) const;   // active synth voices, or -1 if unsupported
    int32_t instrumentHeldNotes(int32_t trackId, int32_t* out, int32_t maxN) const;   // held chord pitches (Nota Pendulum)
    int32_t liveHeldNotes(int32_t* out, int32_t maxN) const;   // currently-pressed live-input pitches (keyboard + MIDI)
    int32_t instrumentScope(int32_t trackId, float* out, int32_t maxN) const;   // live viz telemetry (Nota Operator spectrum)
    // Captured session audio take (false if the slot has no audio).
    bool    sessionAudioSlotInfo(int32_t trackId, int32_t scene, NotaSessionAudioSlot* out) const;
    // Load a file into a session audio slot (looped over lengthBeats). Returns false on failure.
    bool    addSessionAudioClip(int32_t trackId, int32_t scene, const std::string& path,
                                double lengthBeats, double sourceOffsetFrames,
                                int64_t lengthFrames, float gain);
    // Drop a whole audio file into a session slot (M7-5): decodes, auto-computes
    // the loop length in beats at the current tempo. False if track/file invalid.
    bool    addSessionAudioFile(int32_t trackId, int32_t scene, const std::string& path);

    // --- structural edits: instruments & MIDI (M2) ---
    int32_t addInstrumentTrack();                       // built-in Nota Synth
    int32_t addPhysicalSynthTrack();                    // built-in Nota Physical
    int32_t addWavetableSynthTrack();                   // built-in Nota Aurora
    int32_t addVoltSynthTrack();                        // built-in Nota Volt (virtual analog)
    int32_t addBassSynthTrack();                        // built-in Nota Bass (bass synthesizer)
    int32_t addPendulumSynthTrack();                    // built-in Nota Pendulum (generative keys)
    int32_t addOperatorSynthTrack();                    // built-in Nota Operator (4-op FM)
    int32_t addGrainSynthTrack();                       // built-in Nota Grain (granular)
    int32_t addFluxSynthTrack();                        // built-in Nota Flux (vector-morph, sidechain React)
    int32_t addRhythmSynthTrack();                      // built-in Nota Rhythm (drum machine, kind 12)
    int32_t addMonolithTrack();                         // built-in Nota Monolith (mono Model-D synth, kind 13)
    int32_t addPentadTrack();                           // built-in Nota Pentad (5-voice Prophet-5 poly, kind 14)
    void    instrumentAction(int32_t trackId, int32_t id, int32_t iarg, float farg); // UI editing channel
    bool    setRhythmVoiceSample(int32_t trackId, int32_t voice, const std::string& path);  // Phase 2
    bool    rhythmVoiceInfo(int32_t trackId, int32_t voice, NotaSamplerInfo* out) const;     // sample id per voice
    int32_t rhythmVoiceSource(int32_t trackId, int32_t voice) const;                          // 0 Synth, 1 Sample
    bool    setTrackGrainSample(int32_t trackId, const std::string& path, int32_t rootNote);
    int32_t addSamplerTrack(const std::string& path, int32_t rootNote, bool loop);
    int32_t addSamplerInstrumentTrack();                                        // empty Sampler (sample loaded later)
    bool    setTrackSamplerSample(int32_t trackId, const std::string& path, int32_t rootNote);
    bool    setTrackSamplerRoot(int32_t trackId, int32_t rootNote);
    float   samplerPlayPosition(int32_t trackId) const;   // 0..1 of the sample, -1 = silent

    // --- hosted plugins (M3-3) ---
    int32_t addPluginInstrumentTrack(int32_t catalogIndex);        // new track, plugin as instrument
    bool    setTrackInstrumentPlugin(int32_t trackId, int32_t catalogIndex);
    bool    setTrackBuiltinInstrument(int32_t trackId, int32_t kind);  // replace with a built-in synth
    int32_t addTrackEffectPlugin(int32_t trackId, int32_t catalogIndex); // returns device index or -1
    int32_t trackDeviceCount(int32_t trackId) const;
    // Offline analysis (audio→MIDI): an audio clip's played region, mono. out==null → frame count.
    int32_t clipAudioMono(int32_t trackId, int32_t clipIndex, float* out, int32_t maxFrames, double* outSr) const;

    // --- built-in devices + generic params (M4-4/5) ---
    int32_t     addTrackBuiltinDevice(int32_t trackId, int32_t kind); // 0=EQ, 1=Compressor
    bool        moveDevice(int32_t trackId, int32_t fromIndex, int32_t toIndex);
    bool        removeDevice(int32_t trackId, int32_t deviceIndex);
    const char* deviceName(int32_t trackId, int32_t deviceIndex) const;
    int32_t     deviceScope(int32_t trackId, int32_t deviceIndex, float* out, int32_t maxSamples) const;
    // Interactive-device command channel + per-layer waveform (the looper).
    void        deviceAction(int32_t trackId, int32_t deviceIndex, int32_t id, int32_t iarg, float farg);
    int32_t     deviceLayerWave(int32_t trackId, int32_t deviceIndex, int32_t layer, float* out, int32_t maxSamples) const;
    // Opaque device state blob (looper PCM etc.) for project save/restore.
    int32_t     deviceGetState(int32_t trackId, int32_t deviceIndex, uint8_t* out, int32_t cap) const;
    void        deviceSetState(int32_t trackId, int32_t deviceIndex, const uint8_t* data, int32_t size);
    // Load an auxiliary file into a device (the Chamber's user IR) / read its resource text.
    bool        deviceLoadFile(int32_t trackId, int32_t deviceIndex, const std::string& path);
    std::string deviceText(int32_t trackId, int32_t deviceIndex, int32_t id) const;
    int32_t     deviceParamCount(int32_t trackId, int32_t deviceIndex) const;
    const char* deviceParamName(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    float       deviceParamMin(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    float       deviceParamMax(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    float       deviceGetParam(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    void        deviceSetParam(int32_t trackId, int32_t deviceIndex, int32_t paramIndex, float value);
    // Factory-default param values (double-click reset). Built-ins → a fresh instance's
    // value; hosted plugins → the current value (no reset).
    float       deviceParamDefault(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    float       instrumentParamDefault(int32_t trackId, int32_t paramIndex) const;
    float       midiEffectParamDefault(int32_t trackId, int32_t index, int32_t paramIndex) const;
    // Live gain reduction (dB) of a dynamics device (built-in Compressor); 0 otherwise.
    float       deviceGainReduction(int32_t trackId, int32_t deviceIndex) const;
    // Sidechain / signal routing (Phase B): point a device's detector at another
    // track's signal. sourceTrackId -1 clears it. Re-derives the routing table.
    void        setDeviceSidechainSource(int32_t trackId, int32_t deviceIndex, int32_t sourceTrackId);
    int32_t     deviceSidechainSource(int32_t trackId, int32_t deviceIndex) const;
    bool        deviceAcceptsSidechain(int32_t trackId, int32_t deviceIndex) const;  // Phase C
    // Sidechain shaping (Phase D): detector gain (dB), dry/wet mix (0..1), tap point (0 post, 1 pre).
    void        setDeviceSidechainGain(int32_t trackId, int32_t deviceIndex, float db);
    float       deviceSidechainGain(int32_t trackId, int32_t deviceIndex) const;
    void        setDeviceSidechainMix(int32_t trackId, int32_t deviceIndex, float mix);
    float       deviceSidechainMix(int32_t trackId, int32_t deviceIndex) const;
    void        setDeviceSidechainTapPre(int32_t trackId, int32_t deviceIndex, int32_t pre);
    int32_t     deviceSidechainTapPre(int32_t trackId, int32_t deviceIndex) const;
    // Instrument sidechain ("React", Nota Flux): point a listening instrument at another
    // track's signal. sourceTrackId -1 clears it. Re-derives the routing table.
    void        setInstrumentSidechainSource(int32_t trackId, int32_t sourceTrackId);
    int32_t     instrumentSidechainSource(int32_t trackId) const;
    bool        instrumentAcceptsSidechain(int32_t trackId) const;
    void        recomputeRouting();   // message thread: assign route-bus slots to source tracks
    // Open/close a hosted plugin's native GUI. deviceIndex < 0 = the instrument;
    // otherwise the effect at that chain position. Message thread only.
    void    openTrackEditor(int32_t trackId, int32_t deviceIndex);
    void    closeTrackEditor(int32_t trackId, int32_t deviceIndex);
    // Plugin state (M3-5). getPluginState writes up to `cap` bytes into `out`
    // (pass out=null to query size) and returns the full state size.
    int32_t getPluginState(int32_t trackId, int32_t deviceIndex, uint8_t* out, int32_t cap) const;
    bool    setPluginState(int32_t trackId, int32_t deviceIndex, const uint8_t* data, int32_t size);
    // Device bypass (M3-6). Atomic on the stable Device — no snapshot republish.
    void    setTrackDeviceBypassed(int32_t trackId, int32_t deviceIndex, bool bypassed);
    bool    trackDeviceBypassed(int32_t trackId, int32_t deviceIndex) const;
    // Plugin delay compensation (M3-7).
    int32_t trackLatencySamples(int32_t trackId) const;

    // --- MIDI effects (before the instrument): a parallel chain of MidiDevice ---
    int32_t     addTrackMidiEffect(int32_t trackId, int32_t kind);   // 0 = Arp; returns index or -1
    bool        moveMidiEffect(int32_t trackId, int32_t fromIndex, int32_t toIndex);
    bool        removeMidiEffect(int32_t trackId, int32_t index);
    int32_t     trackMidiEffectCount(int32_t trackId) const;
    int32_t     midiEffectKind(int32_t trackId, int32_t index) const;   // midiKind(), -1 if none
    int32_t     midiEffectLastIn(int32_t trackId, int32_t index) const;   // last remapped note-on IN value (Scale pitch / Velocity vel), -1 = none
    int32_t     midiEffectLastOut(int32_t trackId, int32_t index) const;  // last remapped note-on OUT value, -1 = none
    int32_t     midiEffectScope(int32_t trackId, int32_t index, float* out, int32_t maxN) const;   // float scope (Nota Velocity in/out pairs)
    const char* midiEffectName(int32_t trackId, int32_t index) const;
    int32_t     midiEffectParamCount(int32_t trackId, int32_t index) const;
    const char* midiEffectParamName(int32_t trackId, int32_t index, int32_t paramIndex) const;
    float       midiEffectParamMin(int32_t trackId, int32_t index, int32_t paramIndex) const;
    float       midiEffectParamMax(int32_t trackId, int32_t index, int32_t paramIndex) const;
    float       midiEffectGetParam(int32_t trackId, int32_t index, int32_t paramIndex) const;
    void        midiEffectSetParam(int32_t trackId, int32_t index, int32_t paramIndex, float value);
    void        setMidiEffectBypassed(int32_t trackId, int32_t index, bool bypassed);
    bool        midiEffectBypassed(int32_t trackId, int32_t index) const;
    // Map/CC routing: the effect's CC lane modulates an audio-device param on the track.
    void        setMidiEffectCcDest(int32_t trackId, int32_t index, int32_t destDevice, int32_t destParam);
    void        setMidiEffectCcDepth(int32_t trackId, int32_t index, float depth);
    int32_t     midiEffectCcDestDevice(int32_t trackId, int32_t index) const;
    int32_t     midiEffectCcDestParam(int32_t trackId, int32_t index) const;
    float       midiEffectCcDepth(int32_t trackId, int32_t index) const;

    // --- Instrument / Audio-Effect Rack (nested device containers) ---
    // A rack (RackInstrument on an instrument track, or RackDevice in a device
    // chain) hosts N parallel chains + 8 macros. Address it with (trackId,
    // deviceIndex): deviceIndex < 0 = the track's instrument rack, >= 0 = the
    // effect-rack device at that chain position. Structural edits publish the
    // rack's own snapshot (thread-safe); they're outside engine undo in v1.
    int32_t   addInstrumentRackTrack();                          // new track, one default Synth chain
    int32_t   addDrumRackTrack();                                // new track, empty Drum Rack (pads added in UI)
    // Adds a chain whose instrument is a Sampler loaded from `path` (decoded here —
    // RackCore can't decode audio; the sample rides the rack blob). Returns the
    // chain index or -1. Used for drum pads / sampler chains dropped from the browser.
    int32_t   rackAddSamplerChain(int32_t trackId, int32_t deviceIndex, const std::string& path, int32_t rootNote, bool loop);
    bool      rackSetChainSamplerSample(int32_t trackId, int32_t deviceIndex, int32_t chain, const std::string& path, int32_t rootNote);
    // Hosted plugins inside a rack (VST3/AU): instrument as a new chain, or effect
    // into an existing chain. catalogIndex is the browser catalog entry. Returns
    // the new chain / device index, or -1.
    int32_t   rackAddPluginInstrumentChain(int32_t trackId, int32_t deviceIndex, int32_t catalogIndex);
    int32_t   rackAddPluginChainDevice(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t catalogIndex);
    RackCore* rackCoreAt(int32_t trackId, int32_t deviceIndex) const;   // null if not a rack
    // Drum Rack pad note per chain (-1 = all notes; instrument/effect racks ignore it).
    int32_t   rackChainTriggerNote(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void      rackSetChainTriggerNote(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t note);
    int32_t   rackChainCount(int32_t trackId, int32_t deviceIndex) const;
    int32_t   rackAddChain(int32_t trackId, int32_t deviceIndex, int32_t instKind);   // 0=Synth, 2=Physical, <0=none
    bool      rackRemoveChain(int32_t trackId, int32_t deviceIndex, int32_t chain);
    bool      rackSetChainInstrument(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t instKind);
    int32_t   rackChainInstrumentKind(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    const char* rackChainInstrumentName(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    int32_t   rackChainInstrumentParamCount(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    std::string rackChainInstrumentParamName(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t param) const;
    std::string rackChainInstrumentParamId(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t param) const;
    float     rackChainInstrumentParamGet(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t param) const;
    void      rackChainInstrumentParamSet(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t param, float normalized);
    float     rackChainInstrumentParamDefault(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t param) const;
    // Chain instrument GUI + preset capture (hosted plugins): open the native editor,
    // read the stable identifier + opaque state. Message thread.
    void        rackOpenChainInstrumentEditor(int32_t trackId, int32_t deviceIndex, int32_t chain);
    void        rackOpenChainDeviceEditor(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev);   // hosted-plugin effect GUI
    std::string rackChainInstrumentPluginId(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    int32_t     rackChainInstrumentGetState(int32_t trackId, int32_t deviceIndex, int32_t chain, uint8_t* out, int32_t cap) const;
    // Built-in Sampler in a chain (false / -1 if the chain instrument isn't a Sampler).
    bool      rackChainSamplerInfo(int32_t trackId, int32_t deviceIndex, int32_t chain, NotaSamplerInfo* out) const;
    bool      rackSetChainSamplerRoot(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t rootNote);
    float     rackChainSamplerPlayPosition(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    int32_t   rackChainDeviceCount(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    int32_t   rackAddChainDevice(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t deviceKind); // 0=EQ..4=Utility
    bool      rackRemoveChainDevice(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev);
    bool      rackMoveChainDevice(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t from, int32_t to);
    const char* rackChainDeviceName(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev) const;
    int32_t   rackChainDeviceBuiltinKind(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev) const;
    int32_t   rackChainDeviceParamCount(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev) const;
    const char* rackChainDeviceParamName(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t param) const;
    float     rackChainDeviceParamMin(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t param) const;
    float     rackChainDeviceParamMax(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t param) const;
    float     rackChainDeviceParamGet(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t param) const;
    void      rackChainDeviceParamSet(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t param, float value);
    void      rackSetChainDeviceBypassed(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, bool bypassed);
    bool      rackChainDeviceBypassed(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev) const;
    void  rackSetChainGain(int32_t trackId, int32_t deviceIndex, int32_t chain, float v);
    void  rackSetChainPan (int32_t trackId, int32_t deviceIndex, int32_t chain, float v);
    void  rackSetChainMute(int32_t trackId, int32_t deviceIndex, int32_t chain, bool b);
    void  rackSetChainSolo(int32_t trackId, int32_t deviceIndex, int32_t chain, bool b);
    float rackChainGain(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    float rackChainPan (int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    bool  rackChainMute(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    bool  rackChainSolo(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    float rackMacroGet(int32_t trackId, int32_t deviceIndex, int32_t macro) const;
    void  rackMacroSet(int32_t trackId, int32_t deviceIndex, int32_t macro, float v);
    int32_t rackAddMacroMapping(int32_t trackId, int32_t deviceIndex, int32_t macro, int32_t chain,
                                int32_t targetDevice, int32_t paramIndex, float rangeMin, float rangeMax);
    int32_t rackMappingCount(int32_t trackId, int32_t deviceIndex) const;
    bool    rackMappingInfo(int32_t trackId, int32_t deviceIndex, int32_t index, int32_t& macro, int32_t& chain,
                            int32_t& targetDevice, int32_t& paramIndex, float& rangeMin, float& rangeMax) const;
    bool    rackRemoveMapping(int32_t trackId, int32_t deviceIndex, int32_t index);
    // Instrument-Rack extras (mockup 2p): key/velocity zones, per-chain meter, named
    // macros, rack output (volume/glide), macro-map range/curve editing.
    void    rackChainZone(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const;
    void    rackSetChainZone(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi);
    float   rackChainMeter(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    std::string rackMacroName(int32_t trackId, int32_t deviceIndex, int32_t macro) const;
    void    rackSetMacroName(int32_t trackId, int32_t deviceIndex, int32_t macro, const std::string& name);
    float   rackVolume(int32_t trackId, int32_t deviceIndex) const;
    void    rackSetVolume(int32_t trackId, int32_t deviceIndex, float v);
    float   rackGlide(int32_t trackId, int32_t deviceIndex) const;
    void    rackSetGlide(int32_t trackId, int32_t deviceIndex, float v);
    bool    rackSetMappingRange(int32_t trackId, int32_t deviceIndex, int32_t index, float rangeMin, float rangeMax);
    int32_t rackMappingCurve(int32_t trackId, int32_t deviceIndex, int32_t index) const;
    bool    rackSetMappingCurve(int32_t trackId, int32_t deviceIndex, int32_t index, int32_t curve);
    // Drum Rack per-pad shaping (choke group / tune semitones / decay 0..1) + kit swing/humanize.
    void    rackSetChainChoke(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t group);
    int32_t rackChainChoke(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void    rackSetChainTune(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t semitones);
    int32_t rackChainTune(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void    rackSetChainDecay(int32_t trackId, int32_t deviceIndex, int32_t chain, float v);
    float   rackChainDecay(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void    rackSetSwing(int32_t trackId, int32_t deviceIndex, float v);
    float   rackSwing(int32_t trackId, int32_t deviceIndex) const;
    void    rackSetHumanize(int32_t trackId, int32_t deviceIndex, float v);
    float   rackHumanize(int32_t trackId, int32_t deviceIndex) const;
    // Audio Effect Rack routing + output stage (mode / dry-wet / gain=rackVolume / pdc / chain selector).
    void    rackSetMode(int32_t trackId, int32_t deviceIndex, int32_t mode);
    int32_t rackMode(int32_t trackId, int32_t deviceIndex) const;
    void    rackSetDryWet(int32_t trackId, int32_t deviceIndex, float v);
    float   rackDryWet(int32_t trackId, int32_t deviceIndex) const;
    void    rackSetPdc(int32_t trackId, int32_t deviceIndex, bool on);
    bool    rackPdc(int32_t trackId, int32_t deviceIndex) const;
    void    rackSetChainSelect(int32_t trackId, int32_t deviceIndex, float v);
    float   rackChainSelect(int32_t trackId, int32_t deviceIndex) const;
    void    rackSetSelFollow(int32_t trackId, int32_t deviceIndex, bool on);
    bool    rackSelFollow(int32_t trackId, int32_t deviceIndex) const;
    float   rackLiveSelector(int32_t trackId, int32_t deviceIndex) const;
    // Level metering (M6-2). Post-fader block peak/RMS per channel, updated on
    // the audio thread. Reads the live graph (== authoring graph between edits).
    bool trackMeter(int32_t trackId, float& peakL, float& peakR, float& rmsL, float& rmsR) const;
    void masterMeter(float& peakL, float& peakR, float& rmsL, float& rmsR) const;
    int32_t addMidiClip(int32_t trackId, double startBeat, double lengthBeats);
    bool    setClipNotes(int32_t trackId, int32_t clipIndex, const NotaNoteData* notes, int32_t count);
    // Same, but a LIVE update with no undo checkpoint — for a piano-roll drag pushing every
    // frame so playback follows instantly. The UI seeds one undo entry at the gesture start.
    bool    setClipNotesLive(int32_t trackId, int32_t clipIndex, const NotaNoteData* notes, int32_t count);
    int32_t getClipNotes(int32_t trackId, int32_t clipIndex, NotaNoteData* out, int32_t maxNotes) const;
    int32_t clipNoteCount(int32_t trackId, int32_t clipIndex) const;

    // --- live MIDI, arming, recording (M2) ---
    void setTrackArmed(int32_t trackId, bool armed);
    // Record input source for an audio track (internal resampling): 0 = hardware input,
    // -1 = master bus, >0 = another track's post-fader output (by id).
    void setTrackRecordInput(int32_t trackId, int32_t source);
    int32_t trackRecordInput(int32_t trackId) const;
    // MIDI routing: forward an instrument track's MIDI to another instrument track (-1 = off).
    void setTrackMidiSource(int32_t trackId, int32_t sourceTrackId);
    int32_t trackMidiSource(int32_t trackId) const;
    void noteOn(int32_t pitch, float velocity);         // lock-free
    void noteOff(int32_t pitch);                         // lock-free
    // Audition target: live notes also reach this track even when it isn't armed
    // (for clicking rack/drum pads). -1 = none. Lock-free.
    void setAuditionTrack(int32_t trackId) { auditionTrackId_.store(trackId, std::memory_order_relaxed); }
    void setRecording(bool on);
    bool isRecording() const { return recording_.load(std::memory_order_relaxed) || audioRecording_; }
    // Why the last setRecording(true) did (not) start: 0 ok, 1 no armed track, 2 audio input failed.
    int32_t recordStartStatus() const { return recordStartStatus_; }
    void poll();                                         // message thread: materialise recorded notes/audio

    // --- audio input recording (M4-3) ---
    bool startAudioRecording(int32_t trackId);           // opens the input device; false if unavailable
    void stopAudioRecording();                           // materialise the captured clip
    bool isAudioRecording() const { return audioRecording_; }
    // Live audio-capture geometry so the UI can draw a growing take (M-fix): the
    // armed track id and the beat where the take began. Zero id => not capturing.
    int32_t audioRecordTrackId() const { return audioRecording_ ? audioRecordTrackId_ : 0; }
    double  audioRecordStartBeat() const { return audioRecordStartBeat_; }
    // Length (beats) captured so far, so the live take draws a box that grows by the
    // real recorded duration (monotonic) instead of following the looping playhead.
    double  audioRecordLengthBeats() const;
    // Live min/max peaks over the in-progress capture buffer so the take can draw
    // a growing waveform (not just a flat box). 0 when not capturing. UI-thread
    // only — serialised with poll()'s drain, which is the sole writer.
    int32_t audioRecordPeaks(float* outMinMax, int32_t maxPoints) const;
    void pushInputFramesForTest(const float* interleavedStereo, int32_t frames); // feed the ring without a device
    bool audioRecordSelfTest();                          // ring -> accumulate -> clip, no device
    bool sessionAudioRecordSelfTest();                   // ring -> slot.audio -> looped playback, no device (M5-4)

    // --- undo/redo (M6-6) ---
    // Every structural edit publishes a fresh, immutable Graph snapshot, so the
    // previous authoring_ is already a complete restore point. Undo/redo just
    // swap the authoring/live pointer between retained snapshots (audio sample
    // buffers are shared via shared_ptr, so a snapshot costs only metadata).
    // Scope: structural graph edits. Atomic params (volume/pan/mute/bypass/
    // device params) and transport live outside the Graph and are not covered.
    bool undo();
    bool redo();
    bool canUndo() const { return !undoStack_.empty(); }
    bool canRedo() const { return !redoStack_.empty(); }

    // --- offline render (tests / export) ---
    void renderOffline(float* out, int32_t frames);
    // Render at a specific sample rate (WAV export, M6-4). Sets the transport
    // sample rate first, so the backend must be stopped before calling.
    void renderOffline(float* out, int32_t frames, double sampleRate);

private:
    // audio thread
    void render(float* out, int32_t numFrames);
    void processBlock(float* out, int32_t numFrames);
    // Mixes the graph for one contiguous musical segment (no loop wrap inside):
    // automation + solo + send buses + track pass + return pass. processBlock
    // splits a block at loop boundaries and calls this per segment.
    void mixGraph(Graph* g, float* out, int32_t frames, double blockStartSamples, bool playing, double spb);
    void drainCommands();
    void drainLiveMidi(double blockStartBeat, bool playing);
    void renderInstrumentRaw(Graph* g, Track& t, float* dst, int32_t frames,
                             double blockStart, double spb, bool playing);
    // Append a track's own note events (its MIDI clips while playing + live input when armed/
    // auditioned) to evs; returns the new count. Shared by a track's own render and by MIDI
    // routing (a destination gathers its sources' notes the same way).
    int  gatherInstrumentNotes(Track& t, MidiEv* evs, int n, int32_t frames,
                               double blockStart, double spb, bool playing);
    // A track's own post-MIDI-FX events (gather + drum swing + its MIDI FX chain), run once.
    int  computeInstrumentMidi(Track& t, MidiEv* evs, int32_t frames,
                               double blockStart, double spb, bool playing, bool arrangementActive);
    // Pre-pass: fill blockMidi_/blockMidiN_ for every non-session instrument track.
    void computeBlockMidi(Graph* g, int32_t frames, double blockStart, double spb, bool playing, bool arrangementActive);
    void renderSessionSlotRaw(Track& t, float* dst, int32_t frames, double spb);
    int  applyMidiEffects(Track& t, MidiEv* evs, int n, MidiEv* scratch,
                          int32_t frames, double beatStart, double spb, bool playing);
    void applyMidiCcRouting(Track& t);
    void renderSessionAudioSlotRaw(Track& t, float* dst, int32_t frames, double spb); // M5-4
    void renderAudioClipsRaw(const std::vector<AudioClip>& clips, float* dst, int32_t frames,
                             double blockStart, double spb);
    // Consolidate (audio): bounce the clips covering [start,end) into one new clip.
    AudioClip consolidateAudioClips(const std::vector<AudioClip>& clips, double start, double end);
    // Freeze playback (M7): fill dst with the track's frozen buffer for this segment,
    // sampling by beat (frame = beat·frozenSpb) with linear interpolation.
    void fillFrozen(Track& t, float* dst, int32_t frames, double blockStart, double spb);
    void renderMetronome(float* out, int32_t numFrames, double blockStartSamples);
    void renderTone(float* out, int32_t numFrames);
    void applyAutomation(Graph* g, double beat); // M9: eval lanes -> target atomics

    // CV modulation (Phase 3): swing modulated device params around their base for
    // this block, then restore the base afterwards (RT; called inside mixGraph).
    void applyModulation(Graph* g, double beat, double timeSec);
    void updateModulators(Graph* g, double beat, double timeSec, double dt);  // advance stateful + Math
    float modOutput(Track& t, int32_t modId, double beat, double timeSec);
    void restoreLinkTarget(int32_t ownerTrack, const CvLink& l);   // drive target param back to base
    void routeCvBaseEdit(int32_t targetKind, int32_t trackId, int32_t device, int32_t param, float value);
    int32_t addCvLinkFull(int32_t trackId, int32_t sourceKind, int32_t modId, int32_t srcDev, int32_t srcParam,
                          int32_t targetKind, int32_t targetTrack, int32_t targetDevice, int32_t targetParam);
    Modulator* modulatorPtr(Track& t, int32_t modId);
    // Keep CV-link targetDevice indices valid after a device on `track` was removed
    // (removedIndex>=0) or moved (from/to). Clones the affected owner tracks + republishes.
    void remapCvLinksAfterDeviceChange(int32_t track, int32_t removedIndex, int32_t from, int32_t to);

    // automation write (M9-C, message thread)
    void sampleAutomationWritesAt(double beat);  // grow active lanes to (beat, control-value)
    void finishAllWrites();                      // clear active writes + suppressRead
    void setLaneSuppressRead(int32_t trackId, int32_t laneIndex, bool suppress); // raw republish
    float currentTargetValue(const Track& t, int32_t target, int32_t deviceIndex, int32_t paramIndex) const;

    // message thread
    void installRackPluginFactory();   // wire RackCore::pluginFactory to the plugin host bridge
    std::shared_ptr<Track> findTrackAuthoring(int32_t id) const;
    std::shared_ptr<Track> cloneTrack(const Track& t) const;
    std::shared_ptr<SampleBuffer> findSampleById(int64_t sampleId) const; // M7-6b
    void publish(std::shared_ptr<Graph> g);              // checkpoints (undo), then publishRaw
    void publishRaw(std::shared_ptr<Graph> g);           // swap live/authoring only, no undo entry
    void configureClipWarp(AudioClip& c, double spb, double devSR);   // build the clip's warp cache (unless deferred)
    const AudioClip* warpBuildTargetClip() const;        // in-progress build target, revalidated (null = abandon)
    void reconfigureAllWarpStreams();                    // rebuild warp caches for the current tempo + device SR
    void reconfigureAllWarpStreams(double spb, double devSR); // …for an explicit tempo (tempo-change path)
    void propagateSampleRate(double sr);                 // re-prepare every instrument/device/MIDI-FX (incl. master) at sr
    void republishWithTrack(int32_t trackId, std::shared_ptr<Track> nt);    // checkpoints
    void republishWithTrackRaw(int32_t trackId, std::shared_ptr<Track> nt); // no undo entry
    void pushUndo();                 // snapshot authoring_ onto the undo stack, clear redo
    static int32_t trackLatency(const Track& t);
    std::shared_ptr<Instrument> cloneInstrument(const Track& src) const; // duplicateTrack helper
    std::shared_ptr<Device>     cloneDevice(const Device& src) const;
    std::shared_ptr<MidiDevice> cloneMidiDevice(const MidiDevice& src) const;
    void recomputePdc();             // message thread: realign all tracks
    Device* deviceAt(int32_t trackId, int32_t deviceIndex) const;
    MidiDevice* midiDeviceAt(int32_t trackId, int32_t index) const;
    void openMidiInput();            // (re)connect MIDI sources per midiConfig_
    void renderPreview(float* out, int32_t numFrames); // mix the audition voice

    std::unique_ptr<AudioBackend> backend_;
    AudioConfig                   config_;   // chosen device/rate/buffer (M7-1)
    MidiConfig                    midiConfig_; // enabled MIDI inputs (M7-2)
    std::unique_ptr<MidiInput>    midiInput_;
    std::unique_ptr<GamepadInput> gamepadInput_; // game controller note input
    CommandQueue                  commands_;
    MidiQueue                     liveMidi_;
    RecordedQueue                 recorded_;
    ControlEventQueue             midiControl_;   // incoming CC/note-on for MIDI-learn (AR-6)
    Transport                     transport_;

    std::shared_ptr<Graph>              authoring_;
    std::vector<std::shared_ptr<Graph>> retired_;
    std::atomic<Graph*>                 liveGraph_{nullptr};
    int32_t                             nextTrackId_ = 1;

    // Clip clipboard (copy/cut/paste). Holds a clone of the copied clip; kind -1 empty.
    int32_t                             clipboardKind_ = -1;   // -1 none, 0 audio, 1 midi
    MidiClip                            clipboardMidi_;
    AudioClip                           clipboardAudio_;
    // Track automation carried with the clipboard clip: one lane per bound target with
    // the points inside the clip's span, beats stored RELATIVE to the clip start.
    // Reapplied on paste/duplicate. clipboardAutoTrack_ = the source track (so a paste
    // back onto it can restore device/plugin lanes, whose indices are track-positional).
    std::vector<AutomationLane>         clipboardAuto_;
    int32_t                             clipboardAutoTrack_ = -1;

    // Block clip clipboard (multi-selection). Each entry is one captured clip with its
    // source track, kind, position relative to the block start, display length, payload,
    // and clip-relative automation. clipboardBlockLen_ = the block's total span.
    struct BlockClip {
        int32_t                     trackId   = -1;
        int32_t                     kind      = -1;   // 0 audio, 1 midi
        double                      relStart  = 0.0;  // beats from the block start
        double                      len       = 0.0;  // display length in beats
        bool                        sameTrack = true; // false once remapped to another track
        AudioClip                   audio;
        MidiClip                    midi;
        std::vector<AutomationLane> autom;            // clip-relative automation snippet
    };
    std::vector<BlockClip>              clipboardBlock_;
    double                              clipboardBlockLen_ = 0.0;
    // Clips created by the last block paste/duplicate — (trackId, clipIndex) — so the UI
    // can re-select exactly what it produced.
    std::vector<std::pair<int32_t,int32_t>> lastPlaced_;

    // Track clipboard: a fully independent clone of the copied track (null = empty).
    std::shared_ptr<Track>              trackClipboard_;

    // Automation carried with clips (copy/cut/paste/duplicate). captureClipAutomation
    // pulls the points in [start,end] out of every lane, offset to clip-relative beats.
    // applyClipAutomation re-lands them at placedStart (clearing that span first on each
    // matching lane); non-Volume/Pan lanes are applied only on the source track (sameTrack)
    // since device/param indices are positional. removeAutomationInRange clears a span.
    std::vector<AutomationLane> captureClipAutomation(const Track& t, double start, double end) const;
    void applyClipAutomation(Track& nt, int32_t trackId, const std::vector<AutomationLane>& snips,
                             double placedStart, double len, bool sameTrack) const;
    static void removeAutomationInRange(Track& t, double start, double end);
    // Two tracks have an identical device layout (same instrument kind + same device kinds in
    // order), so device/plugin automation can safely follow a clip between them (req 8.3.4).
    static bool tracksDeviceLayoutMatch(const Track& a, const Track& b);

    // Block clipboard internals. captureBlock pulls the selected clips (+ automation) into
    // BlockClip entries rebased to the block start, returning the block's total length.
    // placeBlock re-lands a captured block at placedStart onto the same tracks, shifting the
    // WHOLE block right by a single shared delta to clear existing clips, and publishes all
    // touched tracks in one undo step; it records the placed clips in lastPlaced_.
    std::vector<BlockClip> captureBlock(const std::vector<std::pair<int32_t,int32_t>>& sel,
                                        double& blockLen) const;
    void placeBlock(const std::vector<BlockClip>& items, double placedStart);

    // Build a fully independent copy of a track (fresh id + cloned instrument/devices).
    std::shared_ptr<Track> deepCloneTrack(const Track& src, int32_t newId);
    // Carve [ns, ne) out of a track's audio clips (overwrite/comp on record + drag).
    void overwriteAudioClipsInRange(std::vector<AudioClip>& clips, double ns, double ne);

    // Loop region mirror (see loopEnabled()/loopStart()/loopEnd()).
    bool                                loopEnabled_ = false;
    double                              loopStart_ = 0.0, loopEnd_ = 0.0;

    // Automation-follows-clips state (req 8.3).
    bool                                automationLock_ = false;
    bool                                lastMoveKeptDeviceAuto_ = false;

    // Audio preview / audition (M7-4a): the message thread publishes a decoded
    // buffer via previewLive_ (raw ptr), keeping it alive in previewHold_ and
    // retiring the old one — same lifetime dance as the graph snapshots, so the
    // audio thread never reads a freed buffer.
    std::shared_ptr<SampleBuffer>              previewHold_;
    std::vector<std::shared_ptr<SampleBuffer>> previewRetired_;
    std::atomic<SampleBuffer*> previewLive_{nullptr};
    std::atomic<bool>          previewActive_{false};
    std::atomic<bool>          previewRestart_{false};
    double                     previewPos_ = 0.0;   // audio-thread only, source frames

    // Undo/redo snapshot stacks (M6-6). Hold retained Graph snapshots; entries
    // are cheap (metadata only — sample buffers are shared via shared_ptr).
    std::vector<std::shared_ptr<Graph>> undoStack_;
    std::vector<std::shared_ptr<Graph>> redoStack_;
    static constexpr size_t kMaxUndoDepth = 128;

    std::atomic<float> masterVolume_{1.0f};
    std::atomic<float> masterPeakL_{0.0f};
    std::atomic<float> masterPeakR_{0.0f};
    std::atomic<float> masterRmsL_{0.0f};
    std::atomic<float> masterRmsR_{0.0f};
    std::atomic<bool>  recording_{false};
    std::atomic<int32_t> xruns_{0};           // device dropouts since launch (M7-8)
    std::atomic<float>   cpuLoad_{0.0f};      // smoothed RT DSP load 0..1 (Phase 11)
    std::atomic<double> launchQuant_{4.0};   // Session launch quantize, beats (M5-2)
    // Arrangement playback active (M5): true after main Play / arrangement record; false in a
    // session-only jam. Read on the audio thread to gate arrangement clip content per track.
    std::atomic<bool>   arrangementActive_{false};
    void playClock();   // push a TransportPlay command without touching arrangementActive_
    void finalizeSessionRecord(bool relaunch);   // materialise an in-progress session take
    // Build a fresh graph with a new scene count, cloning tracks so slot vectors resize safely.
    std::shared_ptr<Graph> rebuildScenes(int32_t newCount,
                                         const std::function<void(std::vector<SessionSlot>&)>& mutate);

    // recording target (message thread)
    int32_t recordTrackId_ = 0;
    int32_t recordClipIndex_ = -1;
    int32_t recordStartStatus_ = 0;   // 0 ok, 1 no armed track, 2 audio input failed
    // When recording auto-starts the transport, stopping the take returns the
    // playhead here so re-recording doesn't drift take-to-take (M-fix).
    bool    recordStartedTransport_ = false;
    double  recordReturnBeat_ = 0.0;

    // automation write/record state (M9-C, message thread only)
    std::atomic<int32_t> autoWriteMode_{0};   // AutomationWriteMode
    struct ActiveWrite {
        int32_t trackId; int32_t target; int32_t deviceIndex; int32_t paramIndex;
        std::string paramId; int32_t laneIndex; double lastBeat;
    };
    struct ArmTarget {
        int32_t trackId; int32_t target; int32_t deviceIndex; int32_t paramIndex; std::string paramId;
    };
    std::vector<ActiveWrite> activeWrites_;
    std::vector<ArmTarget>   armedWrites_;
    bool wasPlaying_ = false;                 // poll() edge detection for Write auto-begin
    bool renderWasPlaying_ = false;           // audio-thread play→stop edge (flush stuck notes)

    // session-slot recording target (M5-4). recordSessionScene_ >= 0 selects slot
    // mode; recordSlotPlayer_/recordSlotLen_ are read on the audio thread to time
    // captured notes against the slot's own loop clock (SessionPlayer is stable
    // across snapshots, so the raw pointer stays valid while the track lives).
    int32_t                        recordSessionTrackId_ = 0;
    int32_t                        recordSessionScene_ = -1;
    bool                           recordSessionAudio_ = false; // audio take into slot (M5-4)
    std::atomic<SessionPlayer*>    recordSlotPlayer_{nullptr};
    std::atomic<double>            recordSlotLen_{4.0};

    // audio-thread-owned state
    bool   toneEnabled_ = false;
    float  frequency_   = 440.0f;
    double tonePhase_   = 0.0;
    int64_t lastBeatEmitted_ = -1;
    int    clickRemaining_ = 0;
    double clickPhase_ = 0.0;
    double clickFreq_ = 1000.0;

    // recording capture (audio thread): pending note-ons awaiting note-off.
    bool   prevRecording_ = false;
    bool   pendingActive_[128] = {};
    double pendingStart_[128] = {};
    float  pendingVel_[128] = {};
    // Note-pairing state for capturing routed-in MIDI into the record track's take
    // (recording an instrument track fed via "MIDI In"). Separate from the live-input
    // pending arrays so keyboard input and routed MIDI don't clobber each other.
    bool   recRoutedActive_[128] = {};
    double recRoutedStart_[128] = {};
    float  recRoutedVel_[128] = {};
    void   captureRoutedNotes(const MidiEv* evs, int n, double blockStart, double spb);

    // audio input recording (M4-3): input thread -> ring -> message-thread buffer.
    std::unique_ptr<AudioInput> input_;
    InputQueue          inputQueue_;
    std::vector<float>  audioCaptureBuf_;      // interleaved stereo, message-thread owned
    std::vector<float>  captureScratch_;       // per-drain batch scratch (message thread)
    size_t              captureWritePos_ = 0;  // frame cursor into audioCaptureBuf_
    // Loop-punch capture: while the transport loops, capture is a fixed loop-region buffer
    // whose cursor advances by the captured-frame count (a steady clock, immune to transport
    // jitter) and wraps at the loop length, so each pass overwrites the previous in place.
    // Activated when loop is on at record start OR the moment loop is enabled mid-take.
    bool                captureLoop_ = false;
    bool                captureWrapped_ = false;
    size_t              captureLoopFrames_ = 0;   // loop length in capture-SR frames
    size_t              captureOrigin_ = 0;       // cursor position where this take started (for partial clips)
    double              captureLoopStartBeat_ = 0.0;
    void beginCaptureLayout();                 // decide linear vs loop capture at take start
    void activateLoopPunch(double atBeat);     // switch capture to loop punch (start or mid-take)
    bool                audioRecording_ = false;
    int32_t             audioRecordTrackId_ = 0;
    double              audioRecordStartBeat_ = 0.0;
    double              audioRecordSampleRate_ = 0.0;
    // Internal-resampling source for the active take (0 = hardware/off, -1 = master,
    // >0 = source track id). Read on the audio thread in mixGraph to tap the ring.
    std::atomic<int32_t> internalRecordSource_{0};
    // Frames the audio thread had to drop because the capture ring was full (the
    // UI-thread drain stalled). Drained into the take as an equal run of silence so
    // a dropout stays a bounded local gap instead of shifting everything after it —
    // otherwise every lost frame permanently desyncs the rest of the take.
    std::atomic<int64_t> inputDroppedFrames_{0};
    void drainInputQueue();                    // ring -> audioCaptureBuf_

    // per-block live events (audio thread, drained from liveMidi_).
    static constexpr int kMaxLive = 256;
    MidiEvent liveEvents_[kMaxLive];
    int       liveCount_ = 0;
    std::atomic<int32_t> auditionTrackId_{-1};   // live notes also play this track (pad audition)
    // Currently-held live-input pitches (computer keyboard + MIDI), independent of track
    // routing, so the piano roll can highlight the key you're pressing. 128 bits, set/cleared
    // on noteOn/noteOff (message/CoreMIDI thread), read lock-free by the UI.
    std::atomic<uint32_t> liveHeld_[4]{};

    // instrument render scratch (pre-allocated, big enough for any device block).
    static constexpr int32_t kMaxBlock = 16384;
    std::vector<float> scratch_;
    // Freeze capture (M7): while armed, mixGraph stashes the target track's post-device
    // pre-fader signal into freezeCaptureBuf_. Filled sequentially from a seek(0) render;
    // only touched on the render thread, which is the message thread while capturing (the
    // backend is stopped), so no cross-thread sync beyond the arm flag is needed.
    std::atomic<int32_t> freezeCaptureTrackId_{-1};
    std::vector<float>   freezeCaptureBuf_;   // interleaved stereo target
    int64_t              freezeCapFrames_ = 0; // capacity in frames
    int64_t              freezeCursor_ = 0;    // frames written so far
    double               freezeCaptureSpb_ = 0.0; // samples-per-beat at capture
    // Per-block MIDI output of each instrument track (post its own MIDI FX), keyed by
    // position in the live graph's track list. Filled once per block by computeBlockMidi;
    // the render pass feeds each instrument its own buffer plus the buffers of any tracks
    // routing MIDI to it, so a source's MIDI FX (arp) drive the destination too.
    std::vector<std::vector<MidiEv>> blockMidi_;
    std::vector<int>                 blockMidiN_;
    // Sample rate every node was last prepared at (propagateSampleRate). The offline render
    // re-prepares only when it changes, so a chunked export doesn't reset DSP state per block.
    double preparedSR_ = 0.0;
    // Warp-cache build deferral (project load): when set, configureClipWarp skips the heavy
    // stretch so Apply stays fast; warpBuildStep then fills caches incrementally with a bar.
    bool deferWarpBuild_ = false;
    // Cursor over the ONE warp clip currently being built incrementally (warpBuildStep):
    // its partial cache + stretcher + how far it's filled. Identified by (trackId, clipIndex)
    // and re-fetched+revalidated each step, so a user edit that republishes the track between
    // steps is handled (the build abandons that clip and rescans) rather than dangling.
    struct WarpBuildCursor {
        bool building = false;
        int32_t trackId = -1;
        int32_t clipIndex = -1;
        std::shared_ptr<WarpCache> cache;
        std::unique_ptr<ClipWarpStream> stream;
        int64_t offset = 0;
    } wb_;
    // Per-clip scratch for a warped clip carrying a volume envelope (render there,
    // then fold into the mix with the per-sample envelope gain). M9 follow-up.
    std::vector<float> clipEnvScratch_;

    // Return-bus accumulators (M6-1): interleaved stereo, one per return slot.
    // Regular tracks sum their post-fader signal in; return tracks read theirs.
    std::array<std::vector<float>, kMaxReturns> returnBus_;

    // Group submix accumulators: interleaved stereo, one per active Group track (mapped by
    // enumeration order each block). Leaves sum their post-fader signal into their parent's
    // slot; a group reads its slot, runs its device chain, then folds into its own parent's
    // slot (nested) or the master. Fixed pool → no RT allocation.
    static constexpr int32_t kMaxGroups = 16;
    std::array<std::vector<float>, kMaxGroups> groupBus_;

    // Signal routing (Phase B): a pool of route buses that source tracks tap their
    // post-fader signal into, for sidechain / inter-track routing. Double-buffered:
    // consumers (a compressor's detector) read routeBus_ (previous block) while
    // sources fill routeBusNext_ this block; swapped at block end. This makes the
    // routing order-independent (no track-ordering dependency, no double render) at
    // the cost of one block of detector latency — inaudible after attack/release.
    // Each source slot mirrors two tap points so consumers can pick (Phase D):
    // routeBus{Post,Pre}_ = post-FX post-fader and pre-FX pre-fader signal.
    static constexpr int32_t kMaxRoutes = 8;
    std::array<std::vector<float>, kMaxRoutes> routeBus_, routeBusNext_;         // post-FX (post-fader)
    std::array<std::vector<float>, kMaxRoutes> routeBusPre_, routeBusPreNext_;   // pre-FX (pre-fader)
    std::atomic<int32_t> routeSlotTrackId_[kMaxRoutes];   // which source track each slot mirrors (-1 = free)
    int32_t routeSlotForTrack(int32_t trackId) const {    // audio + message thread; scan is tiny
        for (int32_t s = 0; s < kMaxRoutes; ++s)
            if (routeSlotTrackId_[s].load(std::memory_order_relaxed) == trackId) return s;
        return -1;
    }
};

} // namespace nota
