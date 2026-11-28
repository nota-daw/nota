// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// AutomationOps — parameter automation lanes, hosted-plugin automation surface,
// touch/latch write recording, master tempo/volume/pan lanes, clip envelopes/CCs,
// and block-rate CV modulation.

#pragma once

#include "automation/AutomationTypes.h"
#include "model/EngineTypes.h"
#include "nota/dsp/rt/AutomationTypes.h"
#include "nota/engine/ModelTypes.h"

#include <cstdint>
#include <string>
#include <vector>

namespace nota::detail {

class EngineRuntime;
struct CvLink;
struct Graph;
struct Modulator;
class Track;

class AutomationOps {
  public:
    explicit AutomationOps(EngineRuntime& rt) noexcept : rt_(rt) {}

    // --- parameter automation (M9) ---
    // Device-free: interpolation + block-rate apply write the target atomic.
    bool automationSelfTest();
    int32_t tempoLaneSelfTest();        // specs/099: master Tempo lane -> tempo map; 0 = pass, else the failed check
    int32_t masterAutomationSelfTest(); // specs/099: master Pan / device lanes; 0 = pass, else the failed check
    bool automationCurveSelfTest();     // M9-D: per-segment curvature shaping

    // --- CV modulation (Phase 3, Modular editor) ---
    // Modulator sources (LFO) + edges to built-in device params on the same track.
    // Add/remove are structural (clone + republish); field/depth/mode edits hit the
    // live object's atomics. Applied block-rate in mixGraph around the mix (base is
    // the device atomic, restored each block so the UI keeps seeing it).
    int32_t addModulator(int32_t trackId, int32_t kind);
    bool removeModulator(int32_t trackId, int32_t modId);
    int32_t modulatorCount(int32_t trackId) const;
    int32_t modulatorIdAt(int32_t trackId, int32_t index) const;
    int32_t modulatorKind(int32_t trackId, int32_t modId) const;
    float modulatorGet(int32_t trackId, int32_t modId, int32_t field) const;
    void modulatorSet(int32_t trackId, int32_t modId, int32_t field, float value);
    float modulatorValue(int32_t trackId, int32_t modId) const; // live output for viz
    int32_t modulatorScope(int32_t trackId, int32_t modId, float* values, float* dts,
        int32_t cap) const; // CV scope history, newest cap entries
    int32_t addCvLink(int32_t trackId, int32_t modId, int32_t device, int32_t param);
    // Cross-track: the modulator lives on trackId; the target param is on targetTrack
    // (-1 = trackId itself). The link is owned by the modulator's track.
    int32_t addCvLinkTo(int32_t trackId, int32_t modId, int32_t targetTrack, int32_t device, int32_t param);
    // Param → param: a source param (srcDevice/srcParam on trackId) drives a target param.
    int32_t addCvLinkFromParam(int32_t trackId, int32_t srcDevice, int32_t srcParam, int32_t targetTrack,
        int32_t targetDevice, int32_t targetParam);
    // Target kind (0 device, 1 instrument, 2 MIDI-FX) variants for modulator/param sources.
    int32_t addCvLinkToTarget(int32_t trackId, int32_t modId, int32_t targetKind, int32_t targetTrack,
        int32_t targetDevice, int32_t targetParam);
    int32_t addCvLinkFromParamToTarget(int32_t trackId, int32_t srcDevice, int32_t srcParam, int32_t targetKind,
        int32_t targetTrack, int32_t targetDevice, int32_t targetParam);
    bool paramModulated(int32_t targetKind, int32_t trackId, int32_t device, int32_t param) const;
    bool removeCvLink(int32_t trackId, int32_t index);
    int32_t cvLinkCount(int32_t trackId) const;
    int32_t cvLinkSource(int32_t trackId, int32_t index) const;
    int32_t cvLinkSourceKind(int32_t trackId, int32_t index) const;
    int32_t cvLinkSourceDevice(int32_t trackId, int32_t index) const;
    int32_t cvLinkSourceParam(int32_t trackId, int32_t index) const;
    int32_t cvLinkTargetKind(int32_t trackId, int32_t index) const;
    int32_t cvLinkTargetTrack(int32_t trackId, int32_t index) const;
    int32_t cvLinkDevice(int32_t trackId, int32_t index) const;
    int32_t cvLinkParam(int32_t trackId, int32_t index) const;
    float cvLinkDepth(int32_t trackId, int32_t index) const;
    int32_t cvLinkMode(int32_t trackId, int32_t index) const;
    float cvLinkBase(int32_t trackId, int32_t index) const; // modulation centre value
    void setCvLinkDepth(int32_t trackId, int32_t index, float depth);
    void setCvLinkMode(int32_t trackId, int32_t index, int32_t mode);
    void setCvLinkBase(int32_t trackId, int32_t index, float base);
    bool deviceParamModulated(int32_t trackId, int32_t device, int32_t param) const;
    bool modulationSelfTest();
    // Lane CRUD — structural (snapshot + undo). addAutomationLane returns the lane
    // index, reusing an existing lane on the same target. get/set use the ABI point
    // struct; getAutomationPoints(out=null) returns the count.
    int32_t addAutomationLane(int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex);
    int32_t automationLaneCount(int32_t trackId) const;
    bool automationLaneInfo(int32_t trackId, int32_t laneIndex, int32_t* outTarget, int32_t* outDeviceIndex,
        int32_t* outParamIndex, int32_t* outPointCount) const;
    int32_t getAutomationPoints(int32_t trackId, int32_t laneIndex, AutomationPointData* out, int32_t cap) const;
    bool setAutomationPoints(int32_t trackId, int32_t laneIndex, const AutomationPointData* pts, int32_t count);
    bool setAutomationPointsLive(int32_t trackId, int32_t laneIndex, const AutomationPointData* pts, int32_t count);
    bool setAutomationPointsImpl(
        int32_t trackId, int32_t laneIndex, const AutomationPointData* pts, int32_t count, bool undoCheckpoint);
    bool removeAutomationLane(int32_t trackId, int32_t laneIndex);

    // Master-volume automation (graph-level, not tied to a track). One lane applied
    // to the master gain in applyAutomation; persisted in `.nota` v5.
    int32_t masterVolumeAutomationCount() const;
    int32_t getMasterVolumeAutomation(AutomationPointData* out, int32_t cap) const;
    // live: no undo checkpoint (a drag's later moves — the first write checkpointed).
    bool setMasterVolumeAutomation(const AutomationPointData* pts, int32_t count, bool live = false);

    // Per-clip volume envelope (M9 follow-up): 0..1 curve in clip-local beats that
    // multiplies the audio clip's output. get(out=null) returns the point count.
    int32_t getAudioClipVolumeEnvelope(int32_t trackId, int32_t clipIndex, AutomationPointData* out, int32_t cap) const;
    bool setAudioClipVolumeEnvelope(int32_t trackId, int32_t clipIndex, const AutomationPointData* pts, int32_t count);
    int32_t getAudioClipPanEnvelope(int32_t trackId, int32_t clipIndex, AutomationPointData* out, int32_t cap) const;
    bool setAudioClipPanEnvelope(int32_t trackId, int32_t clipIndex, const AutomationPointData* pts, int32_t count);
    // MIDI clip envelopes (kind: 0 = velocity, 1 = volume; 0..1 in clip-local beats).
    int32_t getMidiClipEnvelope(
        int32_t trackId, int32_t clipIndex, int32_t kind, AutomationPointData* out, int32_t cap) const;
    bool setMidiClipEnvelope(
        int32_t trackId, int32_t clipIndex, int32_t kind, const AutomationPointData* pts, int32_t count);
    // Clip controller curves (specs/020). slot: false = arrangement clip `index`, true = the
    // Session slot of scene `index`. set: count 0 removes the curve; one undo step.
    int32_t ctlList(int32_t trackId, bool slot, int32_t index, int32_t* out, int32_t cap) const;
    int32_t ctlGet(int32_t trackId, bool slot, int32_t index, int32_t ctl, AutomationPointData* out, int32_t cap) const;
    bool ctlSet(int32_t trackId, bool slot, int32_t index, int32_t ctl, const AutomationPointData* pts, int32_t count);
    float deliveredController(int32_t trackId, int32_t ctl) const;
    int32_t midiCtlSelfTest();

    // --- hosted-plugin parameter automation (M9-B) ---
    // deviceIndex < 0 = the track's instrument; otherwise the effect at that index.
    // Params are normalized 0..1, identified by a stable string paramId.
    bool pluginAutomationSelfTest(); // device-free structural check
    int32_t pluginParamCount(int32_t trackId, int32_t deviceIndex) const;
    std::string pluginParamId(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    // A param index saved before a built-in's layout grew (Nota Twin, #531), as it is today.
    int32_t pluginParamIndexFromV1(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    std::string pluginParamName(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    std::string pluginParamText(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    float pluginParamDefault(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    int32_t pluginParamSteps(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const; // specs/100
    std::string pluginParamStepText(int32_t trackId, int32_t deviceIndex, int32_t paramIndex, int32_t step) const;
    float pluginParamGet(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    void pluginParamSet(int32_t trackId, int32_t deviceIndex, int32_t paramIndex, float normalized);
    int32_t pluginParamIndexOfId(int32_t trackId, int32_t deviceIndex, const std::string& id) const;
    int32_t lastTouchedPluginParam(int32_t trackId, int32_t deviceIndex); // "Learn" (M9-B3)
    // Add (or reuse) a PluginParam lane; resolves paramId -> current index now.
    int32_t addPluginAutomationLane(int32_t trackId, int32_t deviceIndex, const char* paramId);
    std::string automationLaneParamId(int32_t trackId, int32_t laneIndex) const;
    // Nested addresses for save / load (specs/044): a path is (chain index, slot) in the rack at
    // the lane's / link's / Arp target's device index; chain index -1 = track level.
    bool automationLanePath(int32_t trackId, int32_t laneIndex, int32_t* chainIndex, int32_t* slot) const;
    int32_t addNestedAutomationLane(int32_t trackId, int32_t target, int32_t host, int32_t chainIndex, int32_t slot,
        int32_t paramIndex, const char* paramId);
    bool cvLinkPath(
        int32_t trackId, int32_t index, int32_t* srcChain, int32_t* srcSlot, int32_t* tgtChain, int32_t* tgtSlot) const;
    int32_t addNestedCvLink(int32_t trackId, int32_t sourceKind, int32_t modId, int32_t srcDev, int32_t srcChain,
        int32_t srcSlot, int32_t srcParam, int32_t targetKind, int32_t targetTrack, int32_t targetDevice,
        int32_t tgtChain, int32_t tgtSlot, int32_t targetParam);
    // The uid of chain `chainIndex` of the rack at `host` on trackId (0 if none), and back.
    uint64_t nestedChainUid(int32_t trackId, int32_t host, int32_t chainIndex) const;
    int32_t nestedChainIndex(int32_t trackId, int32_t host, uint64_t uid) const;

    // --- automation write / record (M9-C) ---
    // There are no record modes. Lanes always play back; recording is contextual:
    // with the transport record engaged, a control gesture writes into that target's
    // lane. begin/end bracket the gesture — a mouse gesture stops writing on release,
    // a latching one (hardware knob / MIDI / gamepad) keeps writing until the
    // transport stops. With record off, touching a parameter that already carries
    // automation *overrides* its lane so the hand leads, until reenableAutomation().
    // Sampling happens in poll() while playing; suppressRead lets the control lead.
    void setAutomationRecord(bool on);
    bool automationRecord() const;
    void beginAutomationWrite(
        int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId, bool latch);
    void endAutomationWrite(
        int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId);
    void reenableAutomation(); // hand the overridden lanes back to playback
    bool automationOverridden() const;
    bool automationWriteSelfTest();       // device-free: touch, latch, override/re-enable
    bool automationDeviceRemapSelfTest(); // lanes follow their device on move/remove

    float masterPan() const;
    void applyAutomation(Graph* g, double beat, double beatEnd = -1.0);
    void applyMasterLanes(Track& master, double beat);          // specs/099: the master's lanes, per master chunk
    static const TrackLane* masterPanLane(const Track& master); // the playing master Pan lane, or null
    void applyModulation(Graph* g, double beat, double timeSec);
    void updateModulators(Graph* g, double beat, double timeSec, double dt); // advance stateful + Math
    void routeCvBaseEdit(int32_t targetKind, int32_t trackId, int32_t device, int32_t param, float value);
    void routeNestedCvBaseEdit(int32_t trackId, int32_t rack, int32_t chain, int32_t slot, int32_t param, float value);
    bool cvBaseValue(int32_t targetKind, int32_t trackId, int32_t device, int32_t param, float& out) const;
    void remapCvLinksAfterDeviceChange(
        int32_t track, int32_t removedIndex, int32_t from, int32_t to, bool midi = false);
    void remapAutomationAfterDeviceChange(Track& nt, bool midi, int32_t removedIndex, int32_t from, int32_t to);
    void sampleAutomationWritesAt(double beat); // grow active lanes to (beat, control-value)
    void finishAllWrites();                     // clear active writes + suppressRead
    std::vector<TrackLane> deviceLanes(const Track& t, int32_t family, int32_t index) const;

  private:
    float modOutput(Track& t, int32_t modId, double beat, double timeSec);
    void restoreLinkTarget(int32_t ownerTrack, const CvLink& l); // drive target param back to base
    int32_t addCvLinkFull(int32_t trackId, int32_t sourceKind, int32_t modId, int32_t srcDev, int32_t srcParam,
        int32_t targetKind, int32_t targetTrack, int32_t targetDevice, int32_t targetParam);
    const Modulator* modulatorPtr(const Track& t, int32_t modId);
    void setLaneSuppressRead(int32_t trackId, int32_t laneIndex, bool suppress); // raw republish
    float currentTargetValue(const Track& t, int32_t target, int32_t deviceIndex, int32_t paramIndex) const;
    void recordAutomationClick(const ActiveWrite& aw, double beat); // a discrete edit's step (specs/100)
    void beginParamTouch(
        int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const std::string& id);
    void endParamTouch(int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const std::string& id);

    EngineRuntime& rt_;
};

} // namespace nota::detail
