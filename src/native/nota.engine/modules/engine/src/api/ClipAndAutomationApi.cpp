// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "api/ApiImpl.h"

#include "clips/ClipOps.h"
#include "core/EngineRuntime.h"
#include "tracks/TrackOps.h"
#include "recording/RecordingOps.h"
#include "nota/devices/DeviceIds.h"
#include "nota/engine/Automation.h"
#include "nota/engine/Clip.h"
#include "nota/engine/DeviceChain.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Modulation.h"
#include "nota/engine/Recorder.h"
#include "nota/engine/Track.h"
#include "nota/engine/TrackCollection.h"
#include "nota/engine/Types.h"

#include <string_view>
#include <string>
#include <optional>
#include <memory>
#include <cstdint>
#include <cstddef>
#include <utility>
#include <vector>

namespace nota {

// ---- Clip ------------------------------------------------------------------

bool Engine::Clip::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasClip(p->runtime, trackId_, index_);
}
std::optional<engine::ClipInfo> Engine::Clip::info() const {
    auto p = impl_.lock();
    if (!p) return std::nullopt;
    engine::ClipInfo ci{};
    if (!p->runtime.tracks().clipInfo(trackId_, index_, &ci)) return std::nullopt;
    return ci;
}
std::optional<engine::AudioClipInfo> Engine::Clip::audioInfo() const {
    auto p = impl_.lock();
    if (!p) return std::nullopt;
    engine::AudioClipInfo ai{};
    if (!p->runtime.clips().audioClipInfo(trackId_, index_, &ai)) return std::nullopt;
    return ai;
}
std::string Engine::Clip::name() const {
    auto p = impl_.lock();
    if (!p) return {};
    char buf[512]{};
    const int32_t len = p->runtime.clips().clipName(trackId_, index_, buf, sizeof(buf));
    if (len <= 0) return {};
    if (len < static_cast<int32_t>(sizeof(buf))) return std::string(buf, static_cast<size_t>(len));
    std::string s(static_cast<size_t>(len) + 1, '\0');
    p->runtime.clips().clipName(trackId_, index_, s.data(), len + 1);
    s.resize(static_cast<size_t>(len));
    return s;
}
bool Engine::Clip::setName(const std::string& name) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipName(trackId_, index_, name) : false;
}
bool Engine::Clip::setActive(bool active) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipActive(trackId_, index_, active) : false;
}
bool Engine::Clip::move(double newStartBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().moveClip(trackId_, index_, newStartBeat) : false;
}
bool Engine::Clip::moveToTrack(int32_t destTrackId, double newStartBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().moveClipToTrack(trackId_, index_, destTrackId, newStartBeat) : false;
}
bool Engine::Clip::trim(double newStartBeat, double newLengthBeats) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().trimClip(trackId_, index_, newStartBeat, newLengthBeats) : false;
}
bool Engine::Clip::resizeAudio(double newStartBeat, double newLengthBeats) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().resizeAudioClip(trackId_, index_, newStartBeat, newLengthBeats) : false;
}
bool Engine::Clip::setSourceRegion(double offsetFrames, int64_t lengthFrames) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipSourceRegion(trackId_, index_, offsetFrames, lengthFrames) : false;
}
bool Engine::Clip::setWarpTrim(double playStart, double playEnd) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipWarpTrim(trackId_, index_, playStart, playEnd) : false;
}
int32_t Engine::Clip::split(double atBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().splitClip(trackId_, index_, atBeat) : -1;
}
int32_t Engine::Clip::duplicate() {
    auto p = impl_.lock();
    return p ? p->runtime.clips().duplicateClip(trackId_, index_) : -1;
}
bool Engine::Clip::remove() {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().deleteClip(trackId_, index_) : false;
}
bool Engine::Clip::setGain(float gain) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipGain(trackId_, index_, gain) : false;
}
std::optional<engine::ClipAdsr> Engine::Clip::adsr() const {
    auto p = impl_.lock();
    if (!p) return std::nullopt;
    engine::ClipAdsr a{};
    if (!p->runtime.clips().clipAdsr(trackId_, index_, &a)) return std::nullopt;
    return a;
}
bool Engine::Clip::setAdsr(const engine::ClipAdsr& adsr) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipAdsr(trackId_, index_, adsr) : false;
}
bool Engine::Clip::setPitch(float semitones) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipPitch(trackId_, index_, semitones) : false;
}
bool Engine::Clip::setReverse(bool reversed) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipReverse(trackId_, index_, reversed) : false;
}
bool Engine::Clip::setWarp(bool enabled, int32_t mode) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipWarp(trackId_, index_, enabled, mode) : false;
}
double Engine::Clip::autoWarp(double knownBpm) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().autoWarpClip(trackId_, index_, knownBpm) : 0.0;
}
double Engine::Clip::beatWarp() {
    auto p = impl_.lock();
    return p ? p->runtime.clips().beatWarpClip(trackId_, index_) : 0.0;
}
bool Engine::Clip::setWarpLength(double beats) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipWarpLength(trackId_, index_, beats) : false;
}
bool Engine::Clip::setWarpMarkers(const double* srcFrames, const double* beats, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipWarpMarkers(trackId_, index_, srcFrames, beats, count) : false;
}
int32_t Engine::Clip::warpMarkers(double* outSrc, double* outBeat, int32_t maxCount) const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().clipWarpMarkers(trackId_, index_, outSrc, outBeat, maxCount) : 0;
}
int32_t Engine::Clip::noteCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().clipNoteCount(trackId_, index_) : 0;
}
int32_t Engine::Clip::notes(engine::NoteData* out, int32_t maxNotes) const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().getClipNotes(trackId_, index_, out, maxNotes) : 0;
}
bool Engine::Clip::setNotes(const engine::NoteData* notes, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipNotes(trackId_, index_, notes, count) : false;
}
bool Engine::Clip::setNotesLive(const engine::NoteData* notes, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setClipNotesLive(trackId_, index_, notes, count) : false;
}
int32_t Engine::Clip::peaks(engine::ClipPeakMode mode, float* outMinMax, int32_t maxPoints, float* outRms) const {
    auto p = impl_.lock();
    if (!p) return 0;
    switch (mode) {
    case engine::ClipPeakMode::Played:
        return p->runtime.clips().getClipPeaks(trackId_, index_, outMinMax, maxPoints, outRms);
    case engine::ClipPeakMode::Source:
        return p->runtime.clips().getClipSourcePeaks(trackId_, index_, outMinMax, maxPoints, outRms);
    case engine::ClipPeakMode::WarpFull:
        return p->runtime.clips().getClipWarpFullPeaks(trackId_, index_, outMinMax, maxPoints, outRms);
    default: return 0;
    }
}
int32_t Engine::Clip::warpPeaksRange(
    double beat0, double beat1, float* outMinMax, int32_t maxPoints, float* outRms) const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().getClipWarpPeaksRange(trackId_, index_, beat0, beat1, outMinMax, maxPoints, outRms)
             : 0;
}
int32_t Engine::Clip::sourcePeaksRange(
    double frame0, double frame1, float* outMinMax, int32_t maxPoints, float* outRms) const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().getClipSourcePeaksRange(
                   trackId_, index_, frame0, frame1, outMinMax, maxPoints, outRms)
             : 0;
}
int32_t Engine::Clip::audioMono(float* out, int32_t maxFrames, double* outSr) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().clipAudioMono(trackId_, index_, out, maxFrames, outSr) : 0;
}
int32_t Engine::Clip::takeId() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().clipTakeId(trackId_, index_) : 0;
}
bool Engine::Clip::setTakeId(int32_t takeId) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().setClipTakeId(trackId_, index_, takeId) : false;
}
int32_t Engine::Clip::joinedEdges() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().clipJoinedEdges(trackId_, index_) : 0;
}
int32_t Engine::Clip::getAudioVolumeEnvelope(engine::AutomationPointData* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().getAudioClipVolumeEnvelope(trackId_, index_, out, cap) : 0;
}
bool Engine::Clip::setAudioVolumeEnvelope(const engine::AutomationPointData* pts, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().setAudioClipVolumeEnvelope(trackId_, index_, pts, count) : false;
}
int32_t Engine::Clip::getAudioPanEnvelope(engine::AutomationPointData* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().getAudioClipPanEnvelope(trackId_, index_, out, cap) : 0;
}
bool Engine::Clip::setAudioPanEnvelope(const engine::AutomationPointData* pts, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().setAudioClipPanEnvelope(trackId_, index_, pts, count) : false;
}
int32_t Engine::Clip::getMidiEnvelope(int32_t kind, engine::AutomationPointData* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().getMidiClipEnvelope(trackId_, index_, kind, out, cap) : 0;
}
bool Engine::Clip::setMidiEnvelope(int32_t kind, const engine::AutomationPointData* pts, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().setMidiClipEnvelope(trackId_, index_, kind, pts, count) : false;
}
int32_t Engine::Clip::ctlList(int32_t* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().ctlList(trackId_, index_ < 0, index_ < 0 ? (-1000 - index_) : index_, out, cap)
             : 0;
}
int32_t Engine::Clip::ctlGet(int32_t ctl, engine::AutomationPointData* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().ctlGet(
                   trackId_, index_ < 0, index_ < 0 ? (-1000 - index_) : index_, ctl, out, cap)
             : 0;
}
bool Engine::Clip::ctlSet(int32_t ctl, const engine::AutomationPointData* pts, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().ctlSet(
                   trackId_, index_ < 0, index_ < 0 ? (-1000 - index_) : index_, ctl, pts, count)
             : false;
}

// ---- Recorder --------------------------------------------------------------

void Engine::Recorder::setRecording(bool on) {
    if (auto p = impl_.lock()) p->runtime.recorder().setRecording(on);
}
bool Engine::Recorder::isRecording() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().isRecording() : false;
}
int32_t Engine::Recorder::retryTake() {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().retryTake() : 0;
}
int32_t Engine::Recorder::startStatus() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().recordStartStatus() : 0;
}
void Engine::Recorder::poll() {
    if (auto p = impl_.lock()) p->runtime.poll();
}
bool Engine::Recorder::startAudio(int32_t trackId) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().startAudioRecording(trackId) : false;
}
bool Engine::Recorder::startAudioLanes(const std::vector<int32_t>& trackIds) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().startAudioRecordingLanes(trackIds) : false;
}
void Engine::Recorder::stopAudio() {
    if (auto p = impl_.lock()) p->runtime.recorder().stopAudioRecording();
}
bool Engine::Recorder::isAudioRecording() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().isAudioRecording() : false;
}
int32_t Engine::Recorder::audioRecordTrackId() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().audioRecordTrackId() : 0;
}
double Engine::Recorder::audioRecordStartBeat() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().audioRecordStartBeat() : 0.0;
}
double Engine::Recorder::audioRecordLengthBeats(int32_t trackId) const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().audioRecordLengthBeats(trackId) : 0.0;
}
int32_t Engine::Recorder::audioRecordPeaks(float* outMinMax, int32_t maxPoints, int32_t trackId) const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().audioRecordPeaks(outMinMax, maxPoints, trackId) : 0;
}
int32_t Engine::Recorder::audioRecordingTracks(int32_t* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().audioRecordingTracks(out, cap) : 0;
}
int32_t Engine::Recorder::skipped(int32_t* trackIds, int32_t* reasons, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().recordSkipped(trackIds, reasons, cap) : 0;
}
int32_t Engine::Recorder::heldNotes(
    engine::RecordHeldNote* out, int32_t maxN, int32_t* trackId, int32_t* sessionScene, double* nowBeat) const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().recordHeldNotes(out, maxN, trackId, sessionScene, nowBeat) : 0;
}
void Engine::Recorder::setDisabledInputs(const int32_t* channels, int32_t count) {
    if (auto p = impl_.lock()) p->runtime.recorder().setDisabledInputs(channels, count);
}
void Engine::Recorder::setTestInputDevice(int32_t channels) {
    if (auto p = impl_.lock()) p->runtime.recorder().setTestInputDevice(channels);
}
void Engine::Recorder::pushInputFramesForTest(const float* interleavedStereo, int32_t frames) {
    if (auto p = impl_.lock()) p->runtime.recorder().pushInputFramesForTest(interleavedStereo, frames);
}
void Engine::Recorder::pushInputFramesForTestN(const float* interleaved, int32_t frames, int32_t channels) {
    if (auto p = impl_.lock()) p->runtime.recorder().pushInputFramesForTestN(interleaved, frames, channels);
}
void Engine::Recorder::feedInputForTest(const float* interleaved, int32_t frames, int32_t channels) {
    if (auto p = impl_.lock()) p->runtime.recorder().feedInputForTest(interleaved, frames, channels);
}
void Engine::Recorder::inputBlockForTest(const float* interleaved, int32_t frames, int32_t channels) {
    if (auto p = impl_.lock()) p->runtime.recorder().inputBlockForTest(interleaved, frames, channels);
}
void Engine::Recorder::pushMonitorFramesForTest(const float* interleavedStereo, int32_t frames) {
    if (auto p = impl_.lock()) p->runtime.recorder().pushMonitorFramesForTest(interleavedStereo, frames);
}
void Engine::Recorder::setMonitorInputForTest(double sampleRate, int32_t blockFrames) {
    if (auto p = impl_.lock()) p->runtime.recorder().setMonitorInputForTest(sampleRate, blockFrames);
}
bool Engine::Recorder::takeRecordTest(
    int32_t trackId, const float* interleavedStereo, int32_t frames, double startBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().takeRecordTest(trackId, interleavedStereo, frames, startBeat) : false;
}
bool Engine::Recorder::takeRecordTestN(
    int32_t trackId, const float* interleaved, int32_t channels, int32_t frames, double startBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().takeRecordTestN(trackId, interleaved, channels, frames, startBeat) : false;
}
int32_t Engine::Recorder::takeRecordTestArmed(const float* interleaved, int32_t channels, int32_t frames,
    double startBeat, int32_t disarmTrackId, int32_t disarmAtFrame) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().takeRecordTestArmed(
                   interleaved, channels, frames, startBeat, disarmTrackId, disarmAtFrame)
             : 0;
}

// ---- AutomationLane --------------------------------------------------------

bool Engine::AutomationLane::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasAutomationLane(p->runtime, trackId_, index_);
}
bool Engine::AutomationLane::info(
    int32_t* outTarget, int32_t* outDeviceIndex, int32_t* outParamIndex, int32_t* outPointCount) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().automationLaneInfo(
                   trackId_, index_, outTarget, outDeviceIndex, outParamIndex, outPointCount)
             : false;
}
std::string Engine::AutomationLane::paramId() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().automationLaneParamId(trackId_, index_) : std::string{};
}
bool Engine::AutomationLane::path(int32_t* chainIndex, int32_t* slot) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().automationLanePath(trackId_, index_, chainIndex, slot) : false;
}
int32_t Engine::AutomationLane::points(engine::AutomationPointData* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().getAutomationPoints(trackId_, index_, out, cap) : 0;
}
bool Engine::AutomationLane::setPoints(const engine::AutomationPointData* pts, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().setAutomationPoints(trackId_, index_, pts, count) : false;
}
bool Engine::AutomationLane::setPointsLive(const engine::AutomationPointData* pts, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().setAutomationPointsLive(trackId_, index_, pts, count) : false;
}
bool Engine::AutomationLane::remove() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().removeAutomationLane(trackId_, index_) : false;
}
void Engine::AutomationLane::setRecordEnabled(Engine& engine, bool on) {
    if (engine.impl_) engine.impl_->runtime.automation().setAutomationRecord(on);
}
bool Engine::AutomationLane::recordEnabled(const Engine& engine) {
    return engine.impl_ ? engine.impl_->runtime.automation().automationRecord() : false;
}
void Engine::AutomationLane::beginWrite(Engine& engine, int32_t trackId, int32_t target, int32_t deviceIndex,
    int32_t paramIndex, const char* paramId, bool latch) {
    if (engine.impl_)
        engine.impl_->runtime.automation().beginAutomationWrite(
            trackId, target, deviceIndex, paramIndex, paramId, latch);
}
void Engine::AutomationLane::endWrite(
    Engine& engine, int32_t trackId, int32_t target, int32_t deviceIndex, int32_t paramIndex, const char* paramId) {
    if (engine.impl_)
        engine.impl_->runtime.automation().endAutomationWrite(trackId, target, deviceIndex, paramIndex, paramId);
}
void Engine::AutomationLane::reenableAll(Engine& engine) {
    if (engine.impl_) engine.impl_->runtime.automation().reenableAutomation();
}
bool Engine::AutomationLane::hasOverrides(const Engine& engine) {
    return engine.impl_ ? engine.impl_->runtime.automation().automationOverridden() : false;
}

// ---- Modulator & CvLink ----------------------------------------------------

bool Engine::Modulator::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasModulator(p->runtime, trackId_, modId_);
}
int32_t Engine::Modulator::kind() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().modulatorKind(trackId_, modId_) : -1;
}
float Engine::Modulator::get(int32_t field) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().modulatorGet(trackId_, modId_, field) : 0.0f;
}
void Engine::Modulator::set(int32_t field, float value) {
    if (auto p = impl_.lock()) p->runtime.automation().modulatorSet(trackId_, modId_, field, value);
}
float Engine::Modulator::value() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().modulatorValue(trackId_, modId_) : 0.0f;
}
int32_t Engine::Modulator::scope(float* values, float* dts, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().modulatorScope(trackId_, modId_, values, dts, cap) : 0;
}
bool Engine::Modulator::remove() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().removeModulator(trackId_, modId_) : false;
}

bool Engine::CvLink::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasCvLink(p->runtime, trackId_, index_);
}
int32_t Engine::CvLink::source() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkSource(trackId_, index_) : -1;
}
int32_t Engine::CvLink::sourceKind() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkSourceKind(trackId_, index_) : -1;
}
int32_t Engine::CvLink::sourceDevice() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkSourceDevice(trackId_, index_) : -1;
}
int32_t Engine::CvLink::sourceParam() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkSourceParam(trackId_, index_) : -1;
}
int32_t Engine::CvLink::targetKind() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkTargetKind(trackId_, index_) : -1;
}
int32_t Engine::CvLink::targetTrack() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkTargetTrack(trackId_, index_) : -1;
}
int32_t Engine::CvLink::device() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkDevice(trackId_, index_) : -1;
}
int32_t Engine::CvLink::param() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkParam(trackId_, index_) : -1;
}
float Engine::CvLink::depth() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkDepth(trackId_, index_) : 0.0f;
}
int32_t Engine::CvLink::mode() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkMode(trackId_, index_) : 0;
}
float Engine::CvLink::base() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkBase(trackId_, index_) : 0.0f;
}
void Engine::CvLink::setDepth(float depth) {
    if (auto p = impl_.lock()) p->runtime.automation().setCvLinkDepth(trackId_, index_, depth);
}
void Engine::CvLink::setMode(int32_t mode) {
    if (auto p = impl_.lock()) p->runtime.automation().setCvLinkMode(trackId_, index_, mode);
}
void Engine::CvLink::setBase(float base) {
    if (auto p = impl_.lock()) p->runtime.automation().setCvLinkBase(trackId_, index_, base);
}
bool Engine::CvLink::path(int32_t* srcChain, int32_t* srcSlot, int32_t* tgtChain, int32_t* tgtSlot) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkPath(trackId_, index_, srcChain, srcSlot, tgtChain, tgtSlot) : false;
}
bool Engine::CvLink::remove() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().removeCvLink(trackId_, index_) : false;
}

} // namespace nota
