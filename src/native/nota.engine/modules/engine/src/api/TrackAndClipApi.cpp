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

// ---- TrackCollection -------------------------------------------------------

int32_t Engine::TrackCollection::count() const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().trackCount() : 0;
}
Engine::Track Engine::TrackCollection::at(int32_t index) const {
    engine::TrackInfo ti{};
    if (!infoAt(index, ti)) return {};
    return Track(impl_, ti.id);
}
Engine::Track Engine::TrackCollection::byId(int32_t trackId) const {
    auto p = impl_.lock();
    if (!p || !detail::EngineApiAccess::hasTrack(p->runtime, trackId)) return {};
    return Track(impl_, trackId);
}
Engine::Track Engine::TrackCollection::master() const {
    return Track(impl_, engine::kMasterTrackId);
}
bool Engine::TrackCollection::infoAt(int32_t index, engine::TrackInfo& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().trackInfo(index, &out) : false;
}
Engine::Track Engine::TrackCollection::addAudio() {
    auto p = impl_.lock();
    return p ? Track(impl_, p->runtime.tracks().addAudioTrack()) : Track{};
}
Engine::Track Engine::TrackCollection::addGroup() {
    auto p = impl_.lock();
    return p ? Track(impl_, p->runtime.tracks().addGroupTrack()) : Track{};
}
Engine::Track Engine::TrackCollection::addReturn() {
    auto p = impl_.lock();
    return p ? Track(impl_, p->runtime.tracks().addReturnTrack()) : Track{};
}
Engine::Track Engine::TrackCollection::addInstrument(std::string_view slug) {
    const int32_t kind = devices::instrumentKindFromSlug(slug);
    if (kind < 0) return {};
    return addBuiltinInstrument(kind);
}
Engine::Track Engine::TrackCollection::addBuiltinInstrument(int32_t kind) {
    auto p = impl_.lock();
    if (!p) return {};
    const int32_t id = p->runtime.devices().addBuiltinTrack(kind);
    return id > 0 ? Track(impl_, id) : Track{};
}
Engine::Track Engine::TrackCollection::addSampler(const std::string& path, int32_t rootNote, bool loop) {
    auto p = impl_.lock();
    if (!p) return {};
    const int32_t id = path.empty() ? p->runtime.devices().addSamplerInstrumentTrack()
                                    : p->runtime.devices().addSamplerTrack(path, rootNote, loop);
    return id > 0 ? Track(impl_, id) : Track{};
}
Engine::Track Engine::TrackCollection::addInstrumentRack() {
    auto p = impl_.lock();
    if (!p) return {};
    const int32_t id = p->runtime.racks().addInstrumentTrack();
    return id > 0 ? Track(impl_, id) : Track{};
}
Engine::Track Engine::TrackCollection::addDrumRack() {
    auto p = impl_.lock();
    if (!p) return {};
    const int32_t id = p->runtime.racks().addDrumTrack();
    return id > 0 ? Track(impl_, id) : Track{};
}
Engine::Track Engine::TrackCollection::addPluginInstrument(int32_t catalogIndex) {
    auto p = impl_.lock();
    if (!p) return {};
    const int32_t id = p->runtime.devices().addPluginInstrumentTrack(catalogIndex);
    return id > 0 ? Track(impl_, id) : Track{};
}
int32_t Engine::TrackCollection::duplicate(int32_t trackId) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().duplicateTrack(trackId) : -1;
}
int32_t Engine::TrackCollection::duplicateMany(const int32_t* ids, int32_t count, int32_t* outIds, int32_t cap) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().duplicateTracks(ids, count, outIds, cap) : -1;
}
int32_t Engine::TrackCollection::addLike(int32_t trackId) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().addTrackLike(trackId) : -1;
}
bool Engine::TrackCollection::remove(int32_t trackId) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().removeTrack(trackId) : false;
}
bool Engine::TrackCollection::removeMany(const int32_t* ids, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().removeTracks(ids, count) : false;
}
bool Engine::TrackCollection::move(int32_t trackId, int32_t toIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().moveTrack(trackId, toIndex) : false;
}
int32_t Engine::TrackCollection::createGroup(const int32_t* ids, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().createGroup(ids, count) : -1;
}
bool Engine::TrackCollection::ungroup(int32_t groupId) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().ungroup(groupId) : false;
}
bool Engine::TrackCollection::copy(int32_t trackId) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().copyTrack(trackId) : false;
}
bool Engine::TrackCollection::copyMany(const int32_t* ids, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().copyTracks(ids, count) : false;
}
int32_t Engine::TrackCollection::paste() {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().pasteTrack() : -1;
}
int32_t Engine::TrackCollection::pasteAfter(int32_t afterTrackId, int32_t* outIds, int32_t cap) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().pasteTracks(afterTrackId, outIds, cap) : -1;
}
bool Engine::TrackCollection::hasClipboard() const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().hasTrackClipboard() : false;
}
bool Engine::TrackCollection::moveClipBlock(const std::vector<engine::ClipMoveSpec>& moves) {
    auto p = impl_.lock();
    if (!p) return false;
    std::vector<detail::ClipMoveReq> reqs;
    reqs.reserve(moves.size());
    for (const auto& m : moves) reqs.push_back({m.srcTrackId, m.clipIndex, m.dstTrackId, m.newStartBeat});
    return p->runtime.clips().moveClipBlock(reqs);
}
bool Engine::TrackCollection::copyClipBlock(const std::vector<std::pair<int32_t, int32_t>>& selection) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().copyClipBlock(selection) : false;
}
bool Engine::TrackCollection::cutClipBlock(const std::vector<std::pair<int32_t, int32_t>>& selection) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().cutClipBlock(selection) : false;
}
int32_t Engine::TrackCollection::pasteClipBlock(double atBeat, int32_t destTrackId) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().pasteClipBlock(atBeat, destTrackId) : -1;
}
double Engine::TrackCollection::duplicateClipBlock(const std::vector<std::pair<int32_t, int32_t>>& selection) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().duplicateClipBlock(selection) : -1.0;
}
const std::vector<std::pair<int32_t, int32_t>>& Engine::TrackCollection::lastPlacedClips() const {
    static const std::vector<std::pair<int32_t, int32_t>> kEmpty;
    auto p = impl_.lock();
    return p ? detail::EngineApiAccess::lastPlaced(p->runtime) : kEmpty;
}
bool Engine::TrackCollection::deleteClipsInRange(
    const std::vector<int32_t>& trackIds, double start, double end, bool withAuto) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().deleteClipsInRange(trackIds, start, end, withAuto) : false;
}
double Engine::TrackCollection::duplicateRange(const std::vector<int32_t>& trackIds, double start, double end) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().duplicateRange(trackIds, start, end) : -1.0;
}
bool Engine::TrackCollection::insertTime(double atBeat, double lengthBeats, bool duplicate) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().insertTime(atBeat, lengthBeats, duplicate) : false;
}
bool Engine::TrackCollection::splitClipsAtRange(const std::vector<int32_t>& trackIds, double start, double end) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().splitClipsAtRange(trackIds, start, end) : false;
}
bool Engine::TrackCollection::fadeTouchingSplits(const std::vector<std::pair<int32_t, int32_t>>& selection) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().fadeTouchingSplits(selection) : false;
}
void Engine::TrackCollection::setSplitFades(bool on) {
    if (auto p = impl_.lock()) p->runtime.clips().setSplitFades(on);
}
bool Engine::TrackCollection::splitFades() const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().splitFades() : true;
}
bool Engine::TrackCollection::consolidateRange(const std::vector<int32_t>& trackIds, double start, double end) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().consolidateRange(trackIds, start, end) : false;
}
bool Engine::TrackCollection::copyRange(const std::vector<int32_t>& trackIds, double start, double end) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().copyRange(trackIds, start, end) : false;
}
bool Engine::TrackCollection::cutRange(const std::vector<int32_t>& trackIds, double start, double end) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().cutRange(trackIds, start, end) : false;
}
int32_t Engine::TrackCollection::clipboardBlockCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().clipboardBlockCount() : 0;
}
int32_t Engine::TrackCollection::clipboardClipKind() const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().clipboardClipKind() : -1;
}
void Engine::TrackCollection::setAutomationLock(bool locked) {
    if (auto p = impl_.lock()) p->runtime.clips().setAutomationLock(locked);
}
bool Engine::TrackCollection::automationLock() const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().automationLock() : false;
}
bool Engine::TrackCollection::lastMoveKeptDeviceAutomation() const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().lastMoveKeptDeviceAutomation() : false;
}
bool Engine::TrackCollection::sampleInfo(int64_t sampleId, engine::SampleInfo& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().sampleInfo(sampleId, &out) : false;
}
int64_t Engine::TrackCollection::sampleRead(int64_t sampleId, float* out, int64_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().sampleRead(sampleId, out, cap) : 0;
}
int32_t Engine::TrackCollection::createNoteExpr(const engine::ExprPointData* points, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().createNoteExpr(points, count) : 0;
}
int32_t Engine::TrackCollection::noteExprPoints(int32_t id, engine::ExprPointData* out, int32_t maxPoints) const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().noteExprPoints(id, out, maxPoints) : 0;
}
std::optional<engine::PresetTagInfo> Engine::TrackCollection::presetTagData(int32_t tag) const {
    auto p = impl_.lock();
    if (!p) return std::nullopt;
    const auto* d = detail::EngineApiAccess::presetTagData(p->runtime, tag);
    if (!d) return std::nullopt;
    return engine::PresetTagInfo{d->id, d->name, d->base};
}

// ---- Track -----------------------------------------------------------------

bool Engine::Track::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasTrack(p->runtime, id_);
}
std::optional<engine::TrackInfo> Engine::Track::info() const {
    auto p = impl_.lock();
    if (!p) return std::nullopt;
    const int32_t n = p->runtime.tracks().trackCount();
    for (int32_t i = 0; i < n; ++i) {
        engine::TrackInfo ti{};
        if (p->runtime.tracks().trackInfo(i, &ti) && ti.id == id_) return ti;
    }
    return std::nullopt;
}
std::string Engine::Track::name() const {
    auto p = impl_.lock();
    if (!p) return {};
    char buf[512]{};
    const int32_t len = p->runtime.clips().trackName(id_, buf, sizeof(buf));
    if (len <= 0) return {};
    if (len < static_cast<int32_t>(sizeof(buf))) return std::string(buf, static_cast<size_t>(len));
    std::string s(static_cast<size_t>(len) + 1, '\0');
    p->runtime.clips().trackName(id_, s.data(), len + 1);
    s.resize(static_cast<size_t>(len));
    return s;
}
bool Engine::Track::setName(const std::string& name) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setTrackName(id_, name) : false;
}
int32_t Engine::Track::colorIndex() const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().trackColorIndex(id_) : -1;
}
bool Engine::Track::setColorIndex(int32_t colorIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setTrackColorIndex(id_, colorIndex) : false;
}
int32_t Engine::Track::pianoFold() const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().trackPianoFold(id_) : -1;
}
bool Engine::Track::setPianoFold(bool on) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setTrackPianoFold(id_, on) : false;
}
int32_t Engine::Track::editLink() const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().trackEditLink(id_) : -1;
}
bool Engine::Track::setEditLink(bool on) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setTrackEditLink(id_, on) : false;
}
bool Engine::Track::setGroup(int32_t groupId) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().setTrackGroup(id_, groupId) : false;
}
void Engine::Track::setVolume(float volume) {
    if (auto p = impl_.lock()) p->runtime.tracks().setTrackVolume(id_, volume);
}
void Engine::Track::setPan(float pan) {
    if (auto p = impl_.lock()) p->runtime.tracks().setTrackPan(id_, pan);
}
void Engine::Track::setMute(bool muted) {
    if (auto p = impl_.lock()) p->runtime.tracks().setTrackMute(id_, muted);
}
void Engine::Track::setSolo(bool soloed) {
    if (auto p = impl_.lock()) p->runtime.tracks().setTrackSolo(id_, soloed);
}
bool Engine::Track::setCue(bool on) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().setTrackCue(id_, on) : false;
}
int32_t Engine::Track::cue() const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().trackCue(id_) : -1;
}
void Engine::Track::setArmed(bool armed) {
    if (auto p = impl_.lock()) p->runtime.recorder().setTrackArmed(id_, armed);
}
void Engine::Track::setSend(int32_t bus, float level) {
    if (auto p = impl_.lock()) p->runtime.tracks().setTrackSend(id_, bus, level);
}
float Engine::Track::send(int32_t bus) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().trackSend(id_, bus) : 0.0f;
}
int32_t Engine::Track::returnIndex() const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().trackReturnIndex(id_) : -1;
}
bool Engine::Track::meter(float& pkL, float& pkR, float& rmsL, float& rmsR) const {
    auto p = impl_.lock();
    if (!p) {
        pkL = pkR = rmsL = rmsR = 0.0f;
        return false;
    }
    return p->runtime.trackMeter(id_, pkL, pkR, rmsL, rmsR);
}
int32_t Engine::Track::latencySamples() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().trackLatencySamples(id_) : 0;
}
bool Engine::Track::isFrozen() const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().trackFrozen(id_) : false;
}
int64_t Engine::Track::beginFreeze(double lengthBeats, double tailSec, double startBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().beginFreeze(id_, lengthBeats, tailSec, startBeat) : 0;
}
void Engine::Track::endFreeze() {
    if (auto p = impl_.lock()) p->runtime.tracks().endFreeze(id_);
}
void Engine::Track::cancelFreeze() {
    if (auto p = impl_.lock()) p->runtime.tracks().cancelFreeze();
}
int64_t Engine::Track::takeFreezeCapture(float* out, int64_t capFrames) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().takeFreezeCapture(out, capFrames) : 0;
}
void Engine::Track::unfreeze() {
    if (auto p = impl_.lock()) p->runtime.tracks().unfreeze(id_);
}
int32_t Engine::Track::freezeGetState(uint8_t* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().freezeGetState(id_, out, cap) : 0;
}
void Engine::Track::freezeSetState(const uint8_t* data, int32_t size) {
    if (auto p = impl_.lock()) p->runtime.tracks().freezeSetState(id_, data, size);
}
Engine::DeviceChain Engine::Track::devices() {
    return DeviceChain(impl_, id_);
}
const Engine::DeviceChain Engine::Track::devices() const {
    return DeviceChain(impl_, id_);
}
int32_t Engine::Track::clipCount() const {
    if (auto ti = info()) return ti->clipCount;
    return 0;
}
Engine::Clip Engine::Track::clip(int32_t clipIndex) const {
    return Clip(impl_, id_, clipIndex);
}
int32_t Engine::Track::addAudioClip(const std::string& path, double startBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().addAudioClip(id_, path, startBeat) : -1;
}
int32_t Engine::Track::addAudioClipEx(
    const std::string& path, double startBeat, double offsetFrames, int64_t lenFrames, float gain) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().addAudioClipEx(id_, path, startBeat, offsetFrames, lenFrames, gain) : -1;
}
int32_t Engine::Track::addAudioClipBuffer(const std::shared_ptr<media::SampleBuffer>& sample, double startBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().addAudioClipBuffer(id_, sample, startBeat) : -1;
}
int32_t Engine::Track::pasteAudioFrames(
    const float* interleaved, int64_t frames, double startBeat, const std::string& name) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().pasteAudioFrames(id_, interleaved, frames, startBeat, name) : -1;
}
int32_t Engine::Track::addMidiClip(double startBeat, double lengthBeats) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().addMidiClip(id_, startBeat, lengthBeats) : -1;
}
bool Engine::Track::copyClip(int32_t clipIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().copyClip(id_, clipIndex) : false;
}
bool Engine::Track::cutClip(int32_t clipIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().cutClip(id_, clipIndex) : false;
}
int32_t Engine::Track::pasteClip(double atBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().pasteClip(id_, atBeat) : -1;
}
int32_t Engine::Track::takeCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().takeCount(id_) : 0;
}
bool Engine::Track::takeInfo(int32_t index, engine::TakeInfo& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().takeInfo(id_, index, &out) : false;
}
int32_t Engine::Track::takePeaks(int32_t index, float* outMinMax, int32_t maxPoints) const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().takePeaks(id_, index, outMinMax, maxPoints) : 0;
}
bool Engine::Track::compTake(int32_t takeId, double startBeat, double endBeat) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().compTake(id_, takeId, startBeat, endBeat) : false;
}
bool Engine::Track::deleteTake(int32_t takeId) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().deleteTake(id_, takeId) : false;
}
bool Engine::Track::flattenTakes() {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().flattenTakes(id_) : false;
}
int32_t Engine::Track::restoreTake(const std::string& path, int32_t number, int32_t group, double originBeat,
    double sourceOffsetFrames, int64_t lengthFrames) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().restoreTake(id_, path, number, group, originBeat, sourceOffsetFrames, lengthFrames)
             : -1;
}
bool Engine::Track::restoreJoinedEdges(const int32_t* edges, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().restoreJoinedEdges(id_, edges, count) : false;
}
void Engine::Track::setRecordInput(int32_t source) {
    if (auto p = impl_.lock()) p->runtime.recorder().setTrackRecordInput(id_, source);
}
int32_t Engine::Track::recordInput() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().trackRecordInput(id_) : 0;
}
void Engine::Track::setInputChannels(int32_t channels) {
    if (auto p = impl_.lock()) p->runtime.recorder().setTrackInputChannels(id_, channels);
}
int32_t Engine::Track::inputChannels() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().trackInputChannels(id_) : 0;
}
bool Engine::Track::setInput(int32_t first, int32_t width) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().setTrackInput(id_, first, width) : false;
}
int32_t Engine::Track::input() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().trackInput(id_) : -1;
}
bool Engine::Track::inputAvailable() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().trackInputAvailable(id_) : false;
}
void Engine::Track::setMonitor(bool on) {
    if (auto p = impl_.lock()) p->runtime.recorder().setTrackMonitor(id_, on);
}
bool Engine::Track::monitor() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().trackMonitor(id_) : false;
}
bool Engine::Track::setMidiInput(int32_t source, int32_t channel) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().setTrackMidiInput(id_, source, channel) : false;
}
bool Engine::Track::setMidiInputUid(const std::string& uid, int32_t channel) {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().setTrackMidiInputUid(id_, uid, channel) : false;
}
std::string Engine::Track::midiInputUid() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().trackMidiInputUid(id_) : std::string{};
}
int32_t Engine::Track::midiInputSource() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().trackMidiInputSource(id_) : -1;
}
int32_t Engine::Track::midiInputChannel() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().trackMidiInputChannel(id_) : -1;
}
void Engine::Track::setMidiSource(int32_t sourceTrackId) {
    if (auto p = impl_.lock()) p->runtime.recorder().setTrackMidiSource(id_, sourceTrackId);
}
int32_t Engine::Track::midiSource() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().trackMidiSource(id_) : -1;
}
void Engine::Track::setMidiOffsetMs(double ms) {
    if (auto p = impl_.lock()) p->runtime.recorder().setTrackMidiOffsetMs(id_, ms);
}
double Engine::Track::midiOffsetMs() const {
    auto p = impl_.lock();
    return p ? p->runtime.recorder().trackMidiOffsetMs(id_) : 0.0;
}
bool Engine::Track::setArticulationMap(int32_t defaultId, const engine::ArticulationEntryData* entries,
    int32_t entryCount, const engine::ArticulationActionData* actions, int32_t actionCount, const char* json,
    int32_t jsonLen) {
    auto p = impl_.lock();
    return p ? p->runtime.clips().setTrackArticulationMap(
                   id_, defaultId, entries, entryCount, actions, actionCount, json, jsonLen)
             : false;
}
bool Engine::Track::clearArticulationMap() {
    auto p = impl_.lock();
    return p ? p->runtime.clips().clearTrackArticulationMap(id_) : false;
}
int32_t Engine::Track::articulationMapJson(char* out, int32_t maxLen) const {
    auto p = impl_.lock();
    return p ? p->runtime.clips().trackArticulationMapJson(id_, out, maxLen) : 0;
}
void Engine::Track::noteOn(int32_t pitch, float velocity) {
    if (auto p = impl_.lock()) p->runtime.recorder().trackNoteOn(id_, pitch, velocity);
}
void Engine::Track::noteOff(int32_t pitch, float releaseVelocity) {
    if (auto p = impl_.lock()) p->runtime.recorder().trackNoteOff(id_, pitch, releaseVelocity);
}
void Engine::Track::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (auto p = impl_.lock()) p->runtime.recorder().trackNoteExpression(id_, pitch, dim, value);
}
void Engine::Track::controller(int32_t ctl, float value) {
    if (auto p = impl_.lock()) p->runtime.recorder().trackController(id_, ctl, value);
}
int32_t Engine::Track::automationLaneCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().automationLaneCount(id_) : 0;
}
Engine::AutomationLane Engine::Track::automationLane(int32_t laneIndex) const {
    return AutomationLane(impl_, id_, laneIndex);
}
int32_t Engine::Track::addAutomationLane(int32_t target, int32_t deviceIndex, int32_t paramIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().addAutomationLane(id_, target, deviceIndex, paramIndex) : -1;
}
int32_t Engine::Track::addPluginAutomationLane(int32_t deviceIndex, const char* paramId) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().addPluginAutomationLane(id_, deviceIndex, paramId) : -1;
}
int32_t Engine::Track::addNestedAutomationLane(
    int32_t target, int32_t host, int32_t chainIndex, int32_t slot, int32_t paramIndex, const char* paramId) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().addNestedAutomationLane(id_, target, host, chainIndex, slot, paramIndex, paramId)
             : -1;
}
bool Engine::Track::removeAutomationLane(int32_t laneIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().removeAutomationLane(id_, laneIndex) : false;
}
float Engine::Track::deliveredController(int32_t ctl) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().deliveredController(id_, ctl) : 0.0f;
}
int32_t Engine::Track::addModulator(int32_t kind) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().addModulator(id_, kind) : -1;
}
bool Engine::Track::removeModulator(int32_t modId) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().removeModulator(id_, modId) : false;
}
int32_t Engine::Track::modulatorCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().modulatorCount(id_) : 0;
}
int32_t Engine::Track::modulatorIdAt(int32_t index) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().modulatorIdAt(id_, index) : 0;
}
Engine::Modulator Engine::Track::modulator(int32_t modId) const {
    return Modulator(impl_, id_, modId);
}
int32_t Engine::Track::addCvLink(int32_t modId, int32_t device, int32_t param) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().addCvLink(id_, modId, device, param) : -1;
}
int32_t Engine::Track::addCvLinkTo(int32_t modId, int32_t targetTrack, int32_t device, int32_t param) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().addCvLinkTo(id_, modId, targetTrack, device, param) : -1;
}
int32_t Engine::Track::addCvLinkFromParam(
    int32_t srcDevice, int32_t srcParam, int32_t targetTrack, int32_t device, int32_t param) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().addCvLinkFromParam(id_, srcDevice, srcParam, targetTrack, device, param) : -1;
}
int32_t Engine::Track::addCvLinkToTarget(
    int32_t modId, int32_t targetKind, int32_t targetTrack, int32_t device, int32_t param) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().addCvLinkToTarget(id_, modId, targetKind, targetTrack, device, param) : -1;
}
int32_t Engine::Track::addCvLinkFromParamToTarget(
    int32_t srcDev, int32_t srcParam, int32_t targetKind, int32_t targetTrack, int32_t device, int32_t param) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().addCvLinkFromParamToTarget(
                   id_, srcDev, srcParam, targetKind, targetTrack, device, param)
             : -1;
}
int32_t Engine::Track::addNestedCvLink(int32_t sourceKind, int32_t modId, int32_t srcDev, int32_t srcChain,
    int32_t srcSlot, int32_t srcParam, int32_t targetKind, int32_t targetTrack, int32_t targetDevice, int32_t tgtChain,
    int32_t tgtSlot, int32_t targetParam) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().addNestedCvLink(id_, sourceKind, modId, srcDev, srcChain, srcSlot, srcParam,
                   targetKind, targetTrack, targetDevice, tgtChain, tgtSlot, targetParam)
             : -1;
}
bool Engine::Track::removeCvLink(int32_t index) {
    auto p = impl_.lock();
    return p ? p->runtime.automation().removeCvLink(id_, index) : false;
}
int32_t Engine::Track::cvLinkCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().cvLinkCount(id_) : 0;
}
Engine::CvLink Engine::Track::cvLink(int32_t index) const {
    return CvLink(impl_, id_, index);
}
bool Engine::Track::paramModulated(int32_t targetKind, int32_t device, int32_t param) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().paramModulated(targetKind, id_, device, param) : false;
}

} // namespace nota
