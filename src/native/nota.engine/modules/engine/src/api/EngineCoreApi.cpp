// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "api/ApiImpl.h"

#include "clips/ClipOps.h"
#include "core/EngineRuntime.h"
#include "io/IoOps.h"
#include "tracks/TrackOps.h"
#include "recording/RecordingOps.h"
#include "core/TempoMap.h"
#include "io/Audition.h"
#include "io/MidiSurface.h"
#include "nota/dsp/rt/DeviceTelemetry.h"
#include "model/Graph.h"
#include "model/EngineTypes.h"
#include "nota/engine/AudioIo.h"
#include "nota/engine/Diagnostics.h"
#include "nota/engine/Engine.h"
#include "nota/engine/History.h"
#include "nota/engine/MidiIo.h"
#include "nota/engine/Mixer.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Preview.h"
#include "nota/engine/Recorder.h"
#include "nota/engine/Session.h"
#include "nota/engine/TrackCollection.h"
#include "nota/engine/Transport.h"
#include "render/HybridBufferController.h"
#include "nota/media/AudioCodecTypes.h"
#include "nota/engine/Types.h"
#include "session/SessionPass.h"

#include <string>
#include <memory>
#include <cstdint>
#include <cstddef>
#include <utility>
#include <vector>

namespace nota {

#ifndef NOTA_VERSION_STRING
#define NOTA_VERSION_STRING "dev"
#endif

Engine::Engine() : impl_(std::make_shared<Impl>()) {}
Engine::~Engine() = default;
Engine::Engine(Engine&&) noexcept = default;
Engine& Engine::operator=(Engine&&) noexcept = default;

const char* Engine::version() noexcept {
    return NOTA_VERSION_STRING;
}

void Engine::reset() {
    if (impl_) impl_->runtime.reset();
}

Engine::Transport Engine::transport() {
    return Transport(impl_);
}
const Engine::Transport Engine::transport() const {
    return Transport(impl_);
}
Engine::Mixer Engine::mixer() {
    return Mixer(impl_);
}
const Engine::Mixer Engine::mixer() const {
    return Mixer(impl_);
}
Engine::TrackCollection Engine::tracks() {
    return TrackCollection(impl_);
}
const Engine::TrackCollection Engine::tracks() const {
    return TrackCollection(impl_);
}
Engine::Session Engine::session() {
    return Session(impl_);
}
const Engine::Session Engine::session() const {
    return Session(impl_);
}
Engine::Recorder Engine::recorder() {
    return Recorder(impl_);
}
const Engine::Recorder Engine::recorder() const {
    return Recorder(impl_);
}
Engine::Preview Engine::preview() {
    return Preview(impl_);
}
const Engine::Preview Engine::preview() const {
    return Preview(impl_);
}
Engine::AudioIo Engine::audio() {
    return AudioIo(impl_);
}
const Engine::AudioIo Engine::audio() const {
    return AudioIo(impl_);
}
Engine::MidiIo Engine::midi() {
    return MidiIo(impl_);
}
const Engine::MidiIo Engine::midi() const {
    return MidiIo(impl_);
}
Engine::GamepadIo Engine::gamepad() {
    return GamepadIo(impl_);
}
const Engine::GamepadIo Engine::gamepad() const {
    return GamepadIo(impl_);
}
Engine::History Engine::history() {
    return History(impl_);
}
const Engine::History Engine::history() const {
    return History(impl_);
}
Engine::Diagnostics Engine::diagnostics() {
    return Diagnostics(impl_);
}
const Engine::Diagnostics Engine::diagnostics() const {
    return Diagnostics(impl_);
}

// ---- Transport -------------------------------------------------------------

void Engine::Transport::play() {
    if (auto p = impl_.lock()) p->runtime.transportPlay();
}
void Engine::Transport::playCountIn() {
    if (auto p = impl_.lock()) p->runtime.transportPlayCountIn();
}
void Engine::Transport::stop() {
    if (auto p = impl_.lock()) p->runtime.transportStop();
}
void Engine::Transport::seekBeats(double beat) {
    if (auto p = impl_.lock()) p->runtime.seekBeats(beat);
}
bool Engine::Transport::setBpm(double bpm) {
    auto p = impl_.lock();
    return p ? p->runtime.setBpm(bpm) : false;
}
double Engine::Transport::bpm() const {
    auto p = impl_.lock();
    return p ? p->runtime.bpm() : 0.0;
}
void Engine::Transport::setTimeSignature(int32_t num, int32_t denom) {
    if (auto p = impl_.lock()) p->runtime.setTimeSignature(num, denom);
}
void Engine::Transport::setLoop(bool enabled, double startBeat, double endBeat) {
    if (auto p = impl_.lock()) p->runtime.setLoop(enabled, startBeat, endBeat);
}
bool Engine::Transport::loopEnabled() const {
    auto p = impl_.lock();
    return p ? p->runtime.loopEnabled() : false;
}
double Engine::Transport::loopStart() const {
    auto p = impl_.lock();
    return p ? p->runtime.loopStart() : 0.0;
}
double Engine::Transport::loopEnd() const {
    auto p = impl_.lock();
    return p ? p->runtime.loopEnd() : 0.0;
}
void Engine::Transport::setMetronome(bool on) {
    if (auto p = impl_.lock()) p->runtime.setMetronome(on);
}
double Engine::Transport::positionBeats() const {
    auto p = impl_.lock();
    return p ? p->runtime.positionBeats() : 0.0;
}
bool Engine::Transport::isPlaying() const {
    auto p = impl_.lock();
    return p ? p->runtime.isPlaying() : false;
}
bool Engine::Transport::snapshot(engine::TransportSnapshot& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.transportSnapshot(out) : false;
}
void Engine::Transport::setCountInBars(int32_t bars) {
    if (auto p = impl_.lock()) p->runtime.setCountInBars(bars);
}
int32_t Engine::Transport::countInBars() const {
    auto p = impl_.lock();
    return p ? p->runtime.countInBars() : 0;
}
double Engine::Transport::countInBeatsRemaining() const {
    auto p = impl_.lock();
    return p ? p->runtime.countInBeatsRemaining() : 0.0;
}
void Engine::Transport::setLoopRecordReplace(bool on) {
    if (auto p = impl_.lock()) p->runtime.setLoopRecordReplace(on);
}
bool Engine::Transport::loopRecordReplace() const {
    auto p = impl_.lock();
    return p ? p->runtime.loopRecordReplace() : false;
}
bool Engine::Transport::arrangementActive() const {
    auto p = impl_.lock();
    return p ? p->runtime.arrangementActive() : false;
}
void Engine::Transport::backToArrangement() {
    if (auto p = impl_.lock()) p->runtime.backToArrangement();
}
bool Engine::Transport::tempoAutomated() const {
    auto p = impl_.lock();
    return p ? p->runtime.tempoAutomated() : false;
}
double Engine::Transport::beatToSeconds(double beat) const {
    auto p = impl_.lock();
    return p ? p->runtime.beatToSeconds(beat) : 0.0;
}
double Engine::Transport::secondsToBeat(double seconds) const {
    auto p = impl_.lock();
    return p ? p->runtime.secondsToBeat(seconds) : 0.0;
}

// ---- Mixer -----------------------------------------------------------------

void Engine::Mixer::setMasterVolume(float volume) {
    if (auto p = impl_.lock()) p->runtime.setMasterVolume(volume);
}
float Engine::Mixer::masterVolume() const {
    auto p = impl_.lock();
    return p ? p->runtime.masterVolume() : 1.0f;
}
double Engine::Transport::bpmAt(double beat) const {
    auto p = impl_.lock();
    if (!p) return 120.0;
    return p->runtime.tempoMap() ? p->runtime.tempoMap()->bpmAt(beat) : p->runtime.bpm();
}

void Engine::Mixer::setMasterPan(float pan) {
    if (auto p = impl_.lock()) p->runtime.setMasterPan(pan);
}
float Engine::Mixer::masterPan() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().masterPan() : 0.0f;
}
void Engine::Mixer::masterMeter(float& peakL, float& peakR, float& rmsL, float& rmsR) const {
    if (auto p = impl_.lock()) {
        p->runtime.masterMeter(peakL, peakR, rmsL, rmsR);
    } else {
        peakL = peakR = rmsL = rmsR = 0.0f;
    }
}
void Engine::Mixer::masterMeter(engine::MeterLevels& out) const {
    masterMeter(out.peakL, out.peakR, out.rmsL, out.rmsR);
}
void Engine::Mixer::setCueVolume(float gain) {
    if (auto p = impl_.lock()) p->runtime.setCueVolume(gain);
}
float Engine::Mixer::cueVolume() const {
    auto p = impl_.lock();
    return p ? p->runtime.cueVolume() : 1.0f;
}
void Engine::Mixer::cueMeter(float& peakL, float& peakR, float& rmsL, float& rmsR) const {
    if (auto p = impl_.lock()) {
        p->runtime.cueMeter(peakL, peakR, rmsL, rmsR);
    } else {
        peakL = peakR = rmsL = rmsR = 0.0f;
    }
}
void Engine::Mixer::cueMeter(engine::MeterLevels& out) const {
    cueMeter(out.peakL, out.peakR, out.rmsL, out.rmsR);
}
void Engine::Mixer::setClickDestination(int32_t destination) {
    if (auto p = impl_.lock()) p->runtime.setClickDestination(destination);
}
int32_t Engine::Mixer::clickDestination() const {
    auto p = impl_.lock();
    return p ? p->runtime.clickDestination() : 0;
}
void Engine::Mixer::setClickVolume(float gain) {
    if (auto p = impl_.lock()) p->runtime.setClickVolume(gain);
}
int32_t Engine::Mixer::returnCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().returnTrackCount() : 0;
}
int32_t Engine::Mixer::addReturn() {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().addReturnTrack() : -1;
}
int32_t Engine::Mixer::readTrackMeters(engine::TrackMeterEntry* out, int32_t capacity) const {
    auto p = impl_.lock();
    return p ? p->runtime.readTrackMeters(out, capacity) : 0;
}
int32_t Engine::Mixer::stereoMeasures(engine::StereoMeasure* out, int32_t capacity) const {
    auto p = impl_.lock();
    return p ? p->runtime.stereoMeasures(out, capacity) : 0;
}
bool Engine::Mixer::setPanLaw(engine::PanLawMode law, bool checkpoint) {
    auto p = impl_.lock();
    if (!p) return false;
    return p->runtime.setPanLaw(static_cast<detail::PanLaw>(static_cast<int32_t>(law)), checkpoint);
}
engine::PanLawMode Engine::Mixer::panLaw() const {
    auto p = impl_.lock();
    return p ? static_cast<engine::PanLawMode>(static_cast<int32_t>(p->runtime.panLaw())) : engine::PanLawMode::Balance;
}
std::vector<engine::SectionInfo> Engine::Mixer::sections() const {
    auto p = impl_.lock();
    if (!p) return {};
    std::vector<engine::SectionInfo> out;
    const auto& src = p->runtime.sections();
    out.reserve(src.size());
    for (const auto& s : src) out.push_back({s.startBeat, s.lengthBeats, s.name});
    return out;
}
void Engine::Mixer::setSections(std::vector<engine::SectionInfo> sections, bool checkpoint) {
    auto p = impl_.lock();
    if (!p) return;
    std::vector<detail::Section> converted;
    converted.reserve(sections.size());
    for (auto& s : sections) converted.push_back({s.startBeat, s.lengthBeats, std::move(s.name)});
    p->runtime.setSections(std::move(converted), checkpoint);
}
bool Engine::Mixer::moveSection(int32_t index, int32_t toBoundary) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().moveSection(index, toBoundary) : false;
}
bool Engine::Mixer::duplicateSectionWithContent(int32_t index) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().duplicateSectionWithContent(index) : false;
}
bool Engine::Mixer::deleteSectionWithContent(int32_t index) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().deleteSectionWithContent(index) : false;
}
bool Engine::Mixer::sectionOverlaps(int32_t index) const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().sectionOverlaps(index) : false;
}
int32_t Engine::Mixer::masterVolumeAutomationCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().masterVolumeAutomationCount() : 0;
}
int32_t Engine::Mixer::getMasterVolumeAutomation(engine::AutomationPointData* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().getMasterVolumeAutomation(out, cap) : 0;
}
bool Engine::Mixer::setMasterVolumeAutomation(const engine::AutomationPointData* pts, int32_t count, bool live) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().setMasterVolumeAutomation(pts, count, live) : false;
}

// ---- History ---------------------------------------------------------------

bool Engine::History::undo() {
    auto p = impl_.lock();
    return p ? p->runtime.undo() : false;
}
bool Engine::History::redo() {
    auto p = impl_.lock();
    return p ? p->runtime.redo() : false;
}
bool Engine::History::canUndo() const {
    auto p = impl_.lock();
    return p ? p->runtime.canUndo() : false;
}
bool Engine::History::canRedo() const {
    auto p = impl_.lock();
    return p ? p->runtime.canRedo() : false;
}
void Engine::History::beginGroup() {
    if (auto p = impl_.lock()) p->runtime.beginUndoGroup();
}
void Engine::History::endGroup() {
    if (auto p = impl_.lock()) p->runtime.endUndoGroup();
}
void Engine::History::clear() {
    if (auto p = impl_.lock()) p->runtime.clearUndoHistory();
}
int32_t Engine::History::beginMixGesture(const engine::MixTargetSpec* targets, int32_t count) {
    auto p = impl_.lock();
    if (!p || !targets || count <= 0) return 0;
    std::vector<detail::MixTarget> native;
    native.reserve(static_cast<size_t>(count));
    for (int32_t i = 0; i < count; ++i) {
        native.push_back({static_cast<detail::MixKind>(static_cast<int32_t>(targets[i].kind)), targets[i].trackId,
            targets[i].deviceIndex, targets[i].paramIndex});
    }
    return p->runtime.beginMixGesture(native.data(), count);
}
void Engine::History::endMixGesture(int32_t handle) {
    if (auto p = impl_.lock()) p->runtime.endMixGesture(handle);
}
bool Engine::History::mixUndoSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.mixUndoSelfTest() : false;
}
bool Engine::History::presetTagSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.devices().presetTagSelfTest() : false;
}

// ---- Preview & AuditionSession ---------------------------------------------

bool Engine::Preview::file(const std::string& path, double startSeconds) {
    auto p = impl_.lock();
    return p ? p->runtime.io().previewFile(path, startSeconds) : false;
}
void Engine::Preview::stop() {
    if (auto p = impl_.lock()) p->runtime.io().stopPreview();
}
void Engine::Preview::setLoop(bool on) {
    if (auto p = impl_.lock()) p->runtime.io().setPreviewLoop(on);
}
void Engine::Preview::setGain(float gain) {
    if (auto p = impl_.lock()) p->runtime.io().setPreviewGain(gain);
}
double Engine::Preview::position() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().previewPosition() : 0.0;
}
bool Engine::Preview::active() const {
    auto p = impl_.lock();
    return p ? p->runtime.io().isPreviewActive() : false;
}
int32_t Engine::Preview::scope(float* out, int32_t count) const {
    auto p = impl_.lock();
    return p ? p->runtime.io().previewScope(out, count) : 0;
}
void Engine::Preview::storeAudition(const std::string& key, const engine::AuditionSession& session) {
    storeAudition(key, session.result());
}
void Engine::Preview::storeAudition(const std::string& key, std::shared_ptr<media::SampleBuffer> buf) {
    if (auto p = impl_.lock()) p->runtime.io().auditionStore(key, std::move(buf));
}
bool Engine::Preview::cachedAudition(const std::string& key) const {
    auto p = impl_.lock();
    return p ? p->runtime.io().auditionCached(key) : false;
}
bool Engine::Preview::playCached(const std::string& key, double startSeconds) {
    auto p = impl_.lock();
    return p ? p->runtime.io().previewCached(key, startSeconds) : false;
}
void Engine::Preview::setAuditionTrack(int32_t trackId) {
    if (auto p = impl_.lock()) p->runtime.setAuditionTrack(trackId);
}
void Engine::Preview::setToneEnabled(bool enabled) {
    if (auto p = impl_.lock()) p->runtime.setToneEnabled(enabled);
}
void Engine::Preview::setToneFrequency(float hz) {
    if (auto p = impl_.lock()) p->runtime.setFrequency(hz);
}
bool Engine::Preview::selfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.io().previewSelfTest() : false;
}

namespace engine {

struct AuditionSession::Impl {
    explicit Impl(double sr) : rig(sr) {}
    detail::AuditionRig rig;
};

AuditionSession::AuditionSession(double sampleRate) : impl_(std::make_unique<Impl>(sampleRate)) {}
AuditionSession::~AuditionSession() = default;
AuditionSession::AuditionSession(AuditionSession&&) noexcept = default;
AuditionSession& AuditionSession::operator=(AuditionSession&&) noexcept = default;

bool AuditionSession::setInstrument(int32_t kind) {
    return impl_ && impl_->rig.setInstrument(kind);
}
bool AuditionSession::instrumentParam(const std::string& id, float value) {
    return impl_ && impl_->rig.instrumentParam(id, value);
}
int32_t AuditionSession::addDevice(int32_t kind) {
    return impl_ ? impl_->rig.addDevice(kind) : -1;
}
bool AuditionSession::deviceParam(int32_t index, const std::string& name, float value) {
    return impl_ && impl_->rig.deviceParam(index, name, value);
}
int32_t AuditionSession::addMidiEffect(int32_t kind) {
    return impl_ ? impl_->rig.addMidiEffect(kind) : -1;
}
bool AuditionSession::midiParam(int32_t index, const std::string& name, float value) {
    return impl_ && impl_->rig.midiParam(index, name, value);
}
bool AuditionSession::setSamplerSample(const std::string& path, int32_t rootNote) {
    return impl_ && impl_->rig.setSamplerSample(path, rootNote);
}
bool AuditionSession::setMosaicProgram(const std::string& text, int32_t timeoutMs) {
    return impl_ && impl_->rig.setMosaicProgram(text, timeoutMs);
}
bool AuditionSession::instrumentState(const uint8_t* data, int32_t size) {
    return impl_ && impl_->rig.instrumentState(data, size);
}
bool AuditionSession::setSourceFile(const std::string& path, double maxSeconds) {
    return impl_ && impl_->rig.setSourceFile(path, maxSeconds);
}
bool AuditionSession::addSourceFrom(const AuditionSession& part, float gain) {
    return impl_ && part.impl_ && impl_->rig.addSourceFrom(part.impl_->rig, gain);
}
bool AuditionSession::useCachedSource(const std::string& key) {
    return impl_ && impl_->rig.useCachedSource(key);
}
bool AuditionSession::cacheSource(const std::string& key, float targetPeak) {
    return impl_ && impl_->rig.cacheSource(key, targetPeak);
}
bool AuditionSession::useKit() {
    return impl_ && impl_->rig.useKit();
}
int32_t AuditionSession::kitAddPad(int32_t note, const std::string& path, float gain, float pan, int32_t choke) {
    return impl_ ? impl_->rig.kitAddPad(note, path, gain, pan, choke) : -1;
}
int32_t AuditionSession::kitPadAddDevice(int32_t pad, int32_t kind) {
    return impl_ ? impl_->rig.kitPadAddDevice(pad, kind) : -1;
}
bool AuditionSession::kitPadDeviceParam(int32_t pad, int32_t device, const std::string& name, float value) {
    return impl_ && impl_->rig.kitPadDeviceParam(pad, device, name, value);
}
int64_t AuditionSession::render(const AuditionNoteSpec* notes, int32_t count, double bpm, double phraseBeats,
    double maxTailSeconds, int32_t flags) {
    if (!impl_) return -1;
    std::vector<detail::AuditionNote> conv;
    if (notes && count > 0) {
        conv.reserve(static_cast<size_t>(count));
        for (int32_t i = 0; i < count; ++i)
            conv.push_back({notes[i].startBeat, notes[i].lengthBeats, notes[i].pitch, notes[i].velocity});
    }
    return impl_->rig.render(conv.empty() ? nullptr : conv.data(), count, bpm, phraseBeats, maxTailSeconds, flags);
}
void AuditionSession::cancel() {
    if (impl_) impl_->rig.cancel();
}
int32_t AuditionSession::peaks(float* outMinMax, int32_t maxPoints) const {
    return impl_ ? impl_->rig.peaks(outMinMax, maxPoints) : 0;
}
double AuditionSession::seconds() const {
    return impl_ ? impl_->rig.seconds() : 0.0;
}
std::shared_ptr<media::SampleBuffer> AuditionSession::result() const {
    return impl_ ? impl_->rig.result() : nullptr;
}

} // namespace engine

// ---- Diagnostics -----------------------------------------------------------

void Engine::Diagnostics::notifyXrun() {
    if (auto p = impl_.lock()) p->runtime.notifyXrun();
}
int32_t Engine::Diagnostics::xrunCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.xrunCount() : 0;
}
float Engine::Diagnostics::cpuLoad() const {
    auto p = impl_.lock();
    return p ? p->runtime.cpuLoad() : 0.0f;
}
void Engine::Diagnostics::setTelemetryMode(dsp::TelemetryMode mode) {
    if (auto p = impl_.lock()) p->runtime.setTelemetryMode(mode);
}
dsp::TelemetryMode Engine::Diagnostics::telemetryMode() const {
    auto p = impl_.lock();
    return p ? p->runtime.telemetryMode() : dsp::TelemetryMode::OnDemand;
}
bool Engine::Diagnostics::setRenderThreads(int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.setRenderThreads(count) : false;
}
int32_t Engine::Diagnostics::renderThreads() const {
    auto p = impl_.lock();
    return p ? p->runtime.renderThreads() : 0;
}
double Engine::Diagnostics::renderThreadsCpuSeconds() const {
    auto p = impl_.lock();
    return p ? p->runtime.renderThreadsCpuSeconds() : 0.0;
}
void Engine::Diagnostics::setStreamThresholdBytes(int64_t bytes) {
    if (auto p = impl_.lock()) p->runtime.setStreamThresholdBytes(bytes);
}
int64_t Engine::Diagnostics::streamThresholdBytes() const {
    auto p = impl_.lock();
    return p ? p->runtime.streamThresholdBytes() : 0;
}
void Engine::Diagnostics::setSampleLoadPolicy(media::SampleLoadPolicy policy) {
    if (auto p = impl_.lock()) p->runtime.setSampleLoadPolicy(policy);
}
media::SampleLoadPolicy Engine::Diagnostics::sampleLoadPolicy() const {
    auto p = impl_.lock();
    return p ? p->runtime.sampleLoadPolicy() : media::SampleLoadPolicy::Auto;
}
media::AudioDecodeOptions Engine::Diagnostics::decodeOptions() const noexcept {
    auto p = impl_.lock();
    return p ? p->runtime.decodeOptions() : media::AudioDecodeOptions{};
}
bool Engine::Diagnostics::isSampleStreaming(int64_t sampleId) const {
    auto p = impl_.lock();
    return p ? p->runtime.isSampleStreaming(sampleId) : false;
}
engine::StreamingStats Engine::Diagnostics::streamingStats() const {
    engine::StreamingStats s{};
    if (auto p = impl_.lock()) p->runtime.streamingStats(s);
    return s;
}
void Engine::Diagnostics::setHybridBufferingMode(engine::HybridMode mode) {
    if (auto p = impl_.lock()) p->runtime.setHybridBufferingMode(static_cast<detail::HybridBufferMode>(mode));
}
engine::HybridMode Engine::Diagnostics::hybridBufferingMode() const {
    auto p = impl_.lock();
    return p ? static_cast<engine::HybridMode>(p->runtime.hybridBufferingMode()) : engine::HybridMode::Off;
}
engine::HybridStats Engine::Diagnostics::hybridStats() const {
    engine::HybridStats out{};
    if (auto p = impl_.lock()) {
        const auto s = p->runtime.hybridStats();
        out = {s.active, s.liveTracks, s.anticipativeTracks, s.lookaheadFrames, s.chunkFrames, s.cacheHits,
            s.cacheMisses, s.invalidations};
    }
    return out;
}
void Engine::Diagnostics::renderOffline(float* out, int32_t frames) {
    if (auto p = impl_.lock()) p->runtime.renderOffline(out, frames);
}
void Engine::Diagnostics::renderOffline(float* out, int32_t frames, double sampleRate) {
    if (auto p = impl_.lock()) p->runtime.renderOffline(out, frames, sampleRate);
}
int32_t Engine::Diagnostics::servicePluginLoads() {
    auto p = impl_.lock();
    return p ? p->runtime.devices().servicePluginLoads() : 0;
}
int32_t Engine::Diagnostics::waitPluginLoads(int32_t timeoutMs) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().waitPluginLoads(timeoutMs) : 0;
}
void Engine::Diagnostics::setDeferWarpBuild(bool defer) {
    if (auto p = impl_.lock()) p->runtime.clips().setDeferWarpBuild(defer);
}
int64_t Engine::Diagnostics::warpBuildStep(int32_t maxFrames) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().warpBuildStep(maxFrames) : 0;
}
int64_t Engine::Diagnostics::warpBuildRemainingFrames() const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().warpBuildRemainingFrames() : 0;
}
int64_t Engine::Diagnostics::warpBuildPoll() {
    auto p = impl_.lock();
    return p ? p->runtime.clips().warpBuildPoll() : 0;
}
void Engine::Diagnostics::recomputeRouting() {
    if (auto p = impl_.lock()) p->runtime.devices().recomputeRouting();
}
int32_t Engine::Diagnostics::sidechainRoutesFree() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().sidechainRoutesFree() : 0;
}
bool Engine::Diagnostics::sidechainRouteInUse(int32_t sourceTrackId, int32_t part) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().sidechainRouteInUse(sourceTrackId, part) : false;
}
int32_t Engine::Diagnostics::pluginEditorSleepSelftest(int32_t catalogIndex) {
    return detail::DeviceOps::pluginEditorSleepSelftest(catalogIndex);
}
bool Engine::Diagnostics::xrunSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.io().xrunSelfTest() : false;
}
bool Engine::Diagnostics::graphRetireSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.graphRetireSelfTest() : false;
}
bool Engine::Diagnostics::automationSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().automationSelfTest() : false;
}
int32_t Engine::Diagnostics::tempoLaneSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().tempoLaneSelfTest() : -1;
}
int32_t Engine::Diagnostics::masterAutomationSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().masterAutomationSelfTest() : -1;
}
bool Engine::Diagnostics::automationCurveSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().automationCurveSelfTest() : false;
}
bool Engine::Diagnostics::automationWriteSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().automationWriteSelfTest() : false;
}
bool Engine::Diagnostics::automationDeviceRemapSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().automationDeviceRemapSelfTest() : false;
}
int32_t Engine::Diagnostics::midiCtlSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().midiCtlSelfTest() : -1;
}
bool Engine::Diagnostics::pluginAutomationSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().pluginAutomationSelfTest() : false;
}
bool Engine::Diagnostics::modulationSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.automation().modulationSelfTest() : false;
}
bool Engine::Diagnostics::rackSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.racks().selfTest() : false;
}
bool Engine::Diagnostics::rackDeviceSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.racks().deviceSelfTest() : false;
}
bool Engine::Diagnostics::timedEventsSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.timedEventsSelfTest() : false;
}
int32_t Engine::Diagnostics::articulationSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.articulationSelfTest() : -1;
}
bool Engine::Diagnostics::drumRackSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.racks().drumRackSelfTest() : false;
}
bool Engine::Diagnostics::audioRecordSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().audioRecordSelfTest() : false;
}
bool Engine::Diagnostics::sessionAudioRecordSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().sessionAudioRecordSelfTest() : false;
}
bool Engine::Diagnostics::monitorSharedClockSelfTest() {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().monitorSharedClockSelfTest() : false;
}
int32_t Engine::Diagnostics::sessionPassSelfTest() {
    return detail::sessionPassSelfTest();
}
int32_t Engine::Diagnostics::midiSurfaceSelfTest() {
    return detail::MidiSurface::selfTest();
}

namespace engine::internal {
bool EngineDirect::undo(Engine& engine) {
    return engine.impl_ ? engine.impl_->runtime.undo() : false;
}
int32_t EngineDirect::splitClip(Engine& engine, int32_t trackId, int32_t clipIndex, double atBeat) {
    return engine.impl_ ? engine.impl_->runtime.clips().splitClip(trackId, clipIndex, atBeat) : -1;
}
} // namespace engine::internal

} // namespace nota
