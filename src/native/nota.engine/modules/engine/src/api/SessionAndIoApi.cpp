// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "api/ApiImpl.h"

#include "nota/dsp/rt/OutputRouting.h"
#include "model/Track.h"
#include "model/EngineTypes.h"
#include "io/MidiSurface.h"
#include "core/EngineRuntime.h"
#include "io/IoOps.h"
#include "recording/RecordingOps.h"
#include "session/SessionOps.h"
#include "nota/engine/AudioIo.h"
#include "nota/engine/Engine.h"
#include "nota/engine/MidiIo.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Session.h"
#include "nota/engine/Slot.h"
#include "nota/io/midi/MidiTypes.h"
#include "nota/io/gamepad/GamepadTypes.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/engine/Types.h"

#include <string>
#include <cstdint>
#include <cstddef>
#include <utility>
#include <vector>

namespace nota {

// ---- Scene -----------------------------------------------------------------

bool Engine::Scene::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasScene(p->runtime, index_);
}
void Engine::Scene::launch(bool fromFollow, double quantOverride) {
    if (auto p = impl_.lock()) p->runtime.session().launchScene(index_, fromFollow, quantOverride);
}
void Engine::Scene::stop() {
    if (auto p = impl_.lock()) p->runtime.session().stopScene(index_);
}
bool Engine::Scene::cancelLaunch() {
    auto p = impl_.lock();
    return p ? p->runtime.session().cancelSceneLaunch(index_) : false;
}
bool Engine::Scene::isQueued() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSceneQueued(index_) : false;
}
int32_t Engine::Scene::record() {
    auto p = impl_.lock();
    return p ? p->runtime.session().recordSessionScene(index_) : 0;
}
bool Engine::Scene::props(engine::SceneProps& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sceneProps(index_, &out) : false;
}
bool Engine::Scene::setProps(const engine::SceneProps& pr) {
    auto p = impl_.lock();
    return p ? p->runtime.session().setSceneProps(index_, pr) : false;
}
std::string Engine::Scene::name() const {
    auto p = impl_.lock();
    std::string out;
    if (p) p->runtime.session().sceneName(index_, out);
    return out;
}
bool Engine::Scene::setName(const std::string& name) {
    auto p = impl_.lock();
    return p ? p->runtime.session().setSceneName(index_, name) : false;
}

// ---- Slot ------------------------------------------------------------------

bool Engine::Slot::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasSlot(p->runtime, trackId_, scene_);
}
int32_t Engine::Slot::state() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotState(trackId_, scene_) : 0;
}
double Engine::Slot::length() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotLength(trackId_, scene_) : 0.0;
}
bool Engine::Slot::setLength(double beats) {
    auto p = impl_.lock();
    return p ? p->runtime.session().setSessionSlotLength(trackId_, scene_, beats) : false;
}
float Engine::Slot::gain() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotGain(trackId_, scene_) : 1.0f;
}
bool Engine::Slot::setGain(float gain) {
    auto p = impl_.lock();
    return p ? p->runtime.session().setSessionSlotGain(trackId_, scene_, gain) : false;
}
void Engine::Slot::launch(float velocity) {
    if (auto p = impl_.lock()) p->runtime.session().launchSlotVel(trackId_, scene_, velocity);
}
void Engine::Slot::release() {
    if (auto p = impl_.lock()) p->runtime.session().releaseSlot(trackId_, scene_);
}
void Engine::Slot::stop() {
    if (auto p = impl_.lock()) p->runtime.session().stopSlot(trackId_);
}
bool Engine::Slot::cancelLaunch() {
    auto p = impl_.lock();
    return p ? p->runtime.session().cancelSlotLaunch(trackId_, scene_) : false;
}
bool Engine::Slot::isQueued() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotLaunchQueued(trackId_, scene_) : false;
}
bool Engine::Slot::isRecording() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotRecording(trackId_, scene_) : false;
}
void Engine::Slot::record() {
    if (auto p = impl_.lock()) p->runtime.session().recordSessionSlot(trackId_, scene_);
}
void Engine::Slot::stopRecord() {
    if (auto p = impl_.lock()) p->runtime.session().stopSessionRecordSlot(trackId_);
}
void Engine::Slot::backToArrangement() {
    if (auto p = impl_.lock()) p->runtime.session().trackBackToArrangement(trackId_);
}
int32_t Engine::Slot::playingSlotOnTrack() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionPlayingSlot(trackId_) : -1;
}
double Engine::Slot::positionOnTrack() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotPosition(trackId_) : 0.0;
}
bool Engine::Slot::addMidiClip(double lengthBeats) {
    auto p = impl_.lock();
    return p ? p->runtime.session().addSessionMidiClip(trackId_, scene_, lengthBeats) : false;
}
bool Engine::Slot::addAudioClip(
    const std::string& path, double lengthBeats, double offsetFrames, int64_t lengthFrames, float gain) {
    auto p = impl_.lock();
    return p ? p->runtime.session().addSessionAudioClip(
                   trackId_, scene_, path, lengthBeats, offsetFrames, lengthFrames, gain)
             : false;
}
bool Engine::Slot::addAudioFile(const std::string& path) {
    auto p = impl_.lock();
    return p ? p->runtime.session().addSessionAudioFile(trackId_, scene_, path) : false;
}
bool Engine::Slot::clear() {
    auto p = impl_.lock();
    return p ? p->runtime.session().clearSessionSlot(trackId_, scene_) : false;
}
bool Engine::Slot::audioInfo(engine::SessionAudioSlotInfo& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionAudioSlotInfo(trackId_, scene_, &out) : false;
}
int32_t Engine::Slot::noteCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionNoteCount(trackId_, scene_) : 0;
}
int32_t Engine::Slot::notes(engine::NoteData* out, int32_t maxNotes) const {
    auto p = impl_.lock();
    return p ? p->runtime.session().getSessionNotes(trackId_, scene_, out, maxNotes) : 0;
}
bool Engine::Slot::setNotes(const engine::NoteData* notes, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.session().setSessionNotes(trackId_, scene_, notes, count) : false;
}
bool Engine::Slot::props(engine::SessionClipProps& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionClipProps(trackId_, scene_, &out) : false;
}
bool Engine::Slot::setProps(const engine::SessionClipProps& pr) {
    auto p = impl_.lock();
    return p ? p->runtime.session().setSessionClipProps(trackId_, scene_, pr) : false;
}
std::string Engine::Slot::name() const {
    auto p = impl_.lock();
    std::string out;
    if (p) p->runtime.session().sessionClipName(trackId_, scene_, out);
    return out;
}
bool Engine::Slot::setName(const std::string& name) {
    auto p = impl_.lock();
    return p ? p->runtime.session().setSessionClipName(trackId_, scene_, name) : false;
}
bool Engine::Slot::stopButton() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotStopButton(trackId_, scene_) : false;
}
bool Engine::Slot::setStopButton(bool on) {
    auto p = impl_.lock();
    return p ? p->runtime.session().setSessionSlotStopButton(trackId_, scene_, on) : false;
}
bool Engine::Slot::takeLength(engine::SlotTakeLength& out) const {
    auto p = impl_.lock();
    if (!p) return false;
    detail::NextTakeLength nt{};
    if (!p->runtime.session().sessionSlotTakeLength(trackId_, scene_, nt)) return false;
    out = {static_cast<int32_t>(nt.mode), nt.bars, nt.beats};
    return true;
}
bool Engine::Slot::setTakeLength(const engine::SlotTakeLength& v) {
    auto p = impl_.lock();
    if (!p) return false;
    detail::NextTakeLength nt{static_cast<detail::SlotTakeMode>(v.mode), v.bars, v.beats};
    return p->runtime.session().setSessionSlotTakeLength(trackId_, scene_, nt);
}
double Engine::Slot::takeBeats() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotTakeBeats(trackId_, scene_) : 0.0;
}
bool Engine::Slot::landingRule(engine::SlotLandingRule& out) const {
    auto p = impl_.lock();
    if (!p) return false;
    detail::LandingRule lr{};
    if (!p->runtime.session().sessionSlotLandingRule(trackId_, scene_, lr)) return false;
    out = {static_cast<int32_t>(lr.action), lr.scene, lr.quantBeats};
    return true;
}
bool Engine::Slot::setLandingRule(const engine::SlotLandingRule& v) {
    auto p = impl_.lock();
    if (!p) return false;
    detail::LandingRule lr{static_cast<detail::LandingAction>(v.action), v.scene, v.quantBeats};
    return p->runtime.session().setSessionSlotLandingRule(trackId_, scene_, lr);
}
bool Engine::Slot::copyTo(int32_t dstTrackId, int32_t dstScene) const {
    auto p = impl_.lock();
    return p ? p->runtime.session().copySessionSlot(trackId_, scene_, dstTrackId, dstScene) : false;
}
int32_t Engine::Slot::peaks(float* outMinMax, int32_t maxPoints) const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotPeaks(trackId_, scene_, outMinMax, maxPoints) : 0;
}
int32_t Engine::Slot::toArrangement(double startBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotToArrangement(trackId_, scene_, startBeat) : -1;
}
bool Engine::Slot::fromArrangementClip(int32_t clipIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.session().arrangementClipToSession(trackId_, clipIndex, scene_) : false;
}
bool Engine::Slot::fromArrangementAudioClip(int32_t clipIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.session().arrangementAudioClipToSession(trackId_, clipIndex, scene_) : false;
}

// ---- Session ---------------------------------------------------------------

int32_t Engine::Session::sceneCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sceneCount() : 0;
}
Engine::Scene Engine::Session::scene(int32_t sceneIndex) const {
    return Scene(impl_, sceneIndex);
}
Engine::Slot Engine::Session::slot(int32_t trackId, int32_t sceneIndex) const {
    return Slot(impl_, trackId, sceneIndex);
}
int32_t Engine::Session::addScene() {
    auto p = impl_.lock();
    return p ? p->runtime.session().addScene() : -1;
}
int32_t Engine::Session::insertScene(int32_t at) {
    auto p = impl_.lock();
    return p ? p->runtime.session().insertScene(at) : -1;
}
int32_t Engine::Session::duplicateScene(int32_t sceneIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.session().duplicateScene(sceneIndex) : -1;
}
int32_t Engine::Session::captureScene(int32_t at) {
    auto p = impl_.lock();
    return p ? p->runtime.session().captureScene(at) : -1;
}
bool Engine::Session::removeScene(int32_t sceneIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.session().removeScene(sceneIndex) : false;
}
bool Engine::Session::moveScene(int32_t from, int32_t to) {
    auto p = impl_.lock();
    return p ? p->runtime.session().moveScene(from, to) : false;
}
void Engine::Session::stopAll() {
    if (auto p = impl_.lock()) p->runtime.session().stopAllSession();
}
void Engine::Session::setLaunchQuant(double beats) {
    if (auto p = impl_.lock()) p->runtime.session().setLaunchQuant(beats);
}
double Engine::Session::launchQuant() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().launchQuant() : 4.0;
}
void Engine::Session::setFollow(bool on) {
    if (auto p = impl_.lock()) p->runtime.session().setSessionFollow(on);
}
bool Engine::Session::follow() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionFollow() : false;
}
void Engine::Session::setRecordLength(double beats) {
    if (auto p = impl_.lock()) p->runtime.session().setSessionRecordLength(beats);
}
double Engine::Session::recordLength() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionRecordLength() : 0.0;
}
void Engine::Session::stopRecord() {
    if (auto p = impl_.lock()) p->runtime.session().stopSessionRecord();
}
bool Engine::Session::recordTarget(int32_t* trackId, int32_t* scene, double* elapsed) const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionRecordTarget(trackId, scene, elapsed) : false;
}
bool Engine::Session::takeSceneTempoChange(double* bpm, int32_t* num, int32_t* den) {
    auto p = impl_.lock();
    return p ? p->runtime.session().takeSceneTempoChange(bpm, num, den) : false;
}
int32_t Engine::Session::passSpans(engine::PassSpan* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionPassSpans(out, cap) : 0;
}
bool Engine::Session::sceneLaunchWaiting() const {
    auto p = impl_.lock();
    return p ? p->runtime.session().sceneLaunchWaiting() : false;
}
int32_t Engine::Session::arrangementClipsToSession(
    const std::vector<std::pair<int32_t, int32_t>>& sel, int32_t startScene) {
    auto p = impl_.lock();
    return p ? p->runtime.session().arrangementClipsToSession(sel, startScene) : -1;
}
int32_t Engine::Session::pasteClipBlockToSession(int32_t destTrackId, int32_t startScene) {
    auto p = impl_.lock();
    return p ? p->runtime.session().pasteClipBlockToSession(destTrackId, startScene) : -1;
}
int32_t Engine::Session::slotsToArrangement(
    const std::vector<std::pair<int32_t, int32_t>>& slots, double atBeat, int32_t destTrackId) {
    auto p = impl_.lock();
    return p ? p->runtime.session().sessionSlotsToArrangement(slots, atBeat, destTrackId) : 0;
}

// ---- AudioIo ---------------------------------------------------------------

bool Engine::AudioIo::start() {
    auto p = impl_.lock();
    return p ? p->runtime.io().start() : false;
}
void Engine::AudioIo::stop() {
    if (auto p = impl_.lock()) p->runtime.io().stop();
}
double Engine::AudioIo::sampleRate() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().sampleRate() : 0.0;
}
bool Engine::AudioIo::isRunning() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().isRunning() : false;
}
int32_t Engine::AudioIo::bufferFrames() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().bufferFrames() : 0;
}
bool Engine::AudioIo::exclusiveFallback() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().audioExclusiveFallback() : false;
}
bool Engine::AudioIo::silentFallback() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().audioSilentFallback() : false;
}
void Engine::AudioIo::renderDeviceBlock(float* out, int32_t frames) {
    if (auto p = impl_.lock()) p->runtime.renderDeviceBlock(out, frames);
}
void Engine::AudioIo::renderDeviceBlock(float* mainOut, float* cueOut, int32_t frames) {
    if (auto p = impl_.lock()) p->runtime.renderDeviceBlock(mainOut, cueOut, frames);
}
io::audio::AudioConfig Engine::AudioIo::config() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().audioConfig() : io::audio::AudioConfig{};
}
void Engine::AudioIo::setOutputDevice(const std::string& uid) {
    if (auto p = impl_.lock()) p->runtime.io().setAudioOutputDevice(uid);
}
void Engine::AudioIo::setInputDevice(const std::string& uid) {
    if (auto p = impl_.lock()) p->runtime.io().setAudioInputDevice(uid);
}
void Engine::AudioIo::setSampleRate(double sr) {
    if (auto p = impl_.lock()) p->runtime.io().setAudioSampleRate(sr);
}
void Engine::AudioIo::setBufferFrames(int32_t frames) {
    if (auto p = impl_.lock()) p->runtime.io().setAudioBufferFrames(frames);
}
void Engine::AudioIo::setWasapiExclusive(bool on) {
    if (auto p = impl_.lock()) p->runtime.io().setAudioWasapiExclusive(on);
}
bool Engine::AudioIo::applyConfig() {
    auto p = impl_.lock();
    return p ? p->runtime.io().applyAudioConfig() : false;
}
void Engine::AudioIo::setSystem(io::audio::AudioSystem system) {
    if (auto p = impl_.lock()) p->runtime.io().setAudioSystem(system);
}
io::audio::AudioSystem Engine::AudioIo::activeSystem() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().activeAudioSystem() : io::audio::AudioSystem::Wasapi;
}
std::string Engine::AudioIo::fallbackReason() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().audioFallbackReason() : std::string{};
}
void Engine::AudioIo::setAsioDriver(const std::string& clsid, const std::string& name) {
    if (auto p = impl_.lock()) p->runtime.io().setAsioDriver(clsid, name);
}
void Engine::AudioIo::setAsioPair(bool input, int32_t firstChannel) {
    if (auto p = impl_.lock()) p->runtime.io().setAsioPair(input, firstChannel);
}
bool Engine::AudioIo::openControlPanel() {
    auto p = impl_.lock();
    return p ? p->runtime.io().openDriverControlPanel() : false;
}
bool Engine::AudioIo::isControlPanelOpen() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().isDriverControlPanelOpen() : false;
}
bool Engine::AudioIo::setOutputRouting(const dsp::OutputRouting& routing) {
    auto p = impl_.lock();
    return p ? p->runtime.io().setOutputRouting(routing) : false;
}
dsp::OutputRouting Engine::AudioIo::outputRouting() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().outputRouting() : dsp::OutputRouting{};
}
int32_t Engine::AudioIo::outputChannelCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().outputChannelCount() : 0;
}
std::string Engine::AudioIo::outputChannelName(int32_t index) const {
    auto p = impl_.lock();
    return p ? p->runtime.io().outputChannelName(index) : std::string{};
}
int32_t Engine::AudioIo::outputRoutingState() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().outputRoutingState() : 0;
}
void Engine::AudioIo::setTestOutputDevice(int32_t channels) {
    if (auto p = impl_.lock()) p->runtime.io().setTestOutputDevice(channels);
}
int32_t Engine::AudioIo::inputChannelCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().inputChannelCount() : 0;
}
std::string Engine::AudioIo::inputChannelName(int32_t index) const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().inputChannelName(index) : std::string{};
}
int32_t Engine::AudioIo::probeInput() {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().probeAudioInput() : 0;
}
int32_t Engine::AudioIo::inputLatencyFrames() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().audioInputLatencyFrames() : 0;
}
int32_t Engine::AudioIo::outputLatencyFrames() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().audioOutputLatencyFrames() : 0;
}
int32_t Engine::AudioIo::driverChannelCount(bool isInput) const {
    auto p = impl_.lock();
    return p ? p->runtime.io().driverChannelCount(isInput) : 0;
}
std::string Engine::AudioIo::driverChannelName(bool isInput, int32_t index) const {
    auto p = impl_.lock();
    return p ? p->runtime.io().driverChannelName(isInput, index) : std::string{};
}
int32_t Engine::AudioIo::pollDeviceEvent() {
    auto p = impl_.lock();
    return p ? p->runtime.io().pollAudioDeviceEvent() : 0;
}
double Engine::AudioIo::latencyCompensatedStart(
    double startBeat, int32_t latencyFrames, double samplesPerBeat, double* trimFrames) noexcept {
    return detail::RecordingOps::latencyCompensatedStart(startBeat, latencyFrames, samplesPerBeat, trimFrames);
}

// ---- MidiIo & GamepadIo ----------------------------------------------------

io::midi::MidiConfig Engine::MidiIo::config() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().midiConfig() : io::midi::MidiConfig{};
}
void Engine::MidiIo::setInputEnabled(const std::string& uid, bool enabled) {
    if (auto p = impl_.lock()) p->runtime.io().setMidiInputEnabled(uid, enabled);
}
bool Engine::MidiIo::inputEnabled(const std::string& uid) const {
    auto p = impl_.lock();
    return p ? p->runtime.io().midiInputEnabled(uid) : false;
}
void Engine::MidiIo::setMpe(bool enabled, int32_t bendRange) {
    if (auto p = impl_.lock()) p->runtime.io().setMpe(enabled, bendRange);
}
bool Engine::MidiIo::applyConfig() {
    auto p = impl_.lock();
    return p ? p->runtime.io().applyMidiConfig() : false;
}
std::vector<engine::MidiPortRow> Engine::MidiIo::ports() {
    auto p = impl_.lock();
    std::vector<engine::MidiPortRow> out;
    if (!p) return out;
    for (const auto& port : p->runtime.io().midiPorts())
        out.push_back({port.source, port.uid, port.name, port.outputUid});
    return out;
}
std::vector<engine::MidiPortRow> Engine::MidiIo::portRows() const {
    auto p = impl_.lock();
    std::vector<engine::MidiPortRow> out;
    if (!p) return out;
    for (const auto& port : p->runtime.io().midiPortRows())
        out.push_back({port.source, port.uid, port.name, port.outputUid});
    return out;
}
int32_t Engine::MidiIo::sourceOfUid(const std::string& uid) const {
    auto p = impl_.lock();
    return p ? p->runtime.io().midiSourceOfUid(uid) : -1;
}
bool Engine::MidiIo::send(const std::string& outputUid, const uint8_t* data, int32_t len) {
    auto p = impl_.lock();
    return p ? p->runtime.io().midiSend(outputUid, data, len) : false;
}
int32_t Engine::MidiIo::pollControlEvents(engine::ControlEventData* out, int32_t max) {
    auto p = impl_.lock();
    if (!p || !out || max <= 0) return 0;
    std::vector<detail::ControlEvent> tmp(static_cast<size_t>(max));
    const int32_t n = p->runtime.io().pollControlEvents(tmp.data(), max);
    for (int32_t i = 0; i < n; ++i) {
        const size_t idx = static_cast<size_t>(i);
        out[i] = {tmp[idx].kind, tmp[idx].channel, tmp[idx].number, tmp[idx].value};
    }
    return n;
}
bool Engine::MidiIo::setClaims(const engine::MidiClaimSpec* claims, int32_t count) {
    auto p = impl_.lock();
    if (!p) return false;
    if (!claims || count <= 0) return p->runtime.io().setMidiClaims(nullptr, 0);
    std::vector<detail::MidiClaimEntry> native;
    native.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) {
        const auto& c = claims[i];
        native.push_back({c.source, c.wholePort, c.status, c.channel, c.data1});
    }
    return p->runtime.io().setMidiClaims(native.data(), count);
}
void Engine::MidiIo::setLearnListening(bool on) {
    if (auto p = impl_.lock()) p->runtime.io().setMidiLearnListening(on);
}
int32_t Engine::MidiIo::pollSurfaceEvents(engine::SurfaceEventData* out, int32_t max) {
    auto p = impl_.lock();
    if (!p || !out || max <= 0) return 0;
    std::vector<detail::SurfaceEvent> tmp(static_cast<size_t>(max));
    const int32_t n = p->runtime.io().pollSurfaceEvents(tmp.data(), max);
    for (int32_t i = 0; i < n; ++i) {
        const size_t idx = static_cast<size_t>(i);
        out[i] = {tmp[idx].source, tmp[idx].status, tmp[idx].channel, tmp[idx].d1, tmp[idx].d2};
    }
    return n;
}
uint32_t Engine::MidiIo::surfaceDropped() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().surfaceDropped() : 0;
}
int32_t Engine::MidiIo::pollSysex(int32_t* outSource, uint8_t* out, int32_t cap) {
    auto p = impl_.lock();
    return p ? p->runtime.io().pollMidiSysex(outSource, out, cap) : 0;
}
int32_t Engine::MidiIo::addTestPort(const std::string& name) {
    auto p = impl_.lock();
    return p ? p->runtime.io().addTestMidiPort(name) : -1;
}
bool Engine::MidiIo::injectTestMidi(int32_t source, const uint8_t* data, int32_t len) {
    auto p = impl_.lock();
    return p ? p->runtime.io().injectTestMidi(source, data, len) : false;
}
int32_t Engine::MidiIo::takeTestMidiSent(int32_t source, uint8_t* out, int32_t cap) {
    auto p = impl_.lock();
    return p ? p->runtime.io().takeTestMidiSent(source, out, cap) : 0;
}
void Engine::MidiIo::pushMidiForTest(int32_t source, int32_t status, int32_t channel, int32_t d1, int32_t d2) {
    if (auto p = impl_.lock()) p->runtime.io().pushMidiForTest(source, status, channel, d1, d2);
}
void Engine::MidiIo::noteOn(int32_t pitch, float velocity, int32_t source, int32_t channel) {
    if (auto p = impl_.lock()) p->runtime.recorder().noteOn(pitch, velocity, source, channel);
}
void Engine::MidiIo::noteOff(int32_t pitch, float releaseVelocity, int32_t source, int32_t channel) {
    if (auto p = impl_.lock()) p->runtime.recorder().noteOff(pitch, releaseVelocity, source, channel);
}
void Engine::MidiIo::noteExpression(int32_t pitch, int32_t dim, float value, int32_t source, int32_t channel) {
    if (auto p = impl_.lock()) p->runtime.recorder().noteExpression(pitch, dim, value, source, channel);
}
void Engine::MidiIo::controller(int32_t ctl, float value, int32_t source, int32_t channel) {
    if (auto p = impl_.lock()) p->runtime.recorder().controller(ctl, value, source, channel);
}
int32_t Engine::MidiIo::liveHeldNotes(int32_t* out, int32_t maxCount) const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().liveHeldNotes(out, maxCount) : 0;
}

void Engine::GamepadIo::start() {
    if (auto p = impl_.lock()) p->runtime.io().startGamepadInput();
}
void Engine::GamepadIo::stop() {
    if (auto p = impl_.lock()) p->runtime.io().stopGamepadInput();
}
int32_t Engine::GamepadIo::count() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().gamepadCount() : 0;
}
void Engine::GamepadIo::info(int32_t index, const char** outUid, const char** outName) const {
    if (auto p = impl_.lock()) p->runtime.io().gamepadInfo(index, outUid, outName);
}
int32_t Engine::GamepadIo::pollEvents(io::gamepad::GamepadButtonEvent* out, int32_t max) {
    auto p = impl_.lock();
    return p ? p->runtime.io().pollGamepadEvents(out, max) : 0;
}
int32_t Engine::GamepadIo::axisValues(int32_t pad, int32_t* out, int32_t max) const {
    auto p = impl_.lock();
    return p ? p->runtime.io().gamepadAxisValues(pad, out, max) : 0;
}

} // namespace nota
