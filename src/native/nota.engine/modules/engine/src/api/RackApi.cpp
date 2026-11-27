// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "api/ApiImpl.h"

#include "tracks/TrackOps.h"

#include "nota/devices/DeviceIds.h"
#include "nota/dsp/Device.h"
#include "nota/dsp/EditorPreview.h"
#include "nota/dsp/MidiDevice.h"
#include "nota/dsp/capabilities/IWavetableInstrument.h"
#include "nota/engine/Device.h"
#include "nota/engine/DeviceChain.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Rack.h"
#include "nota/engine/RackChain.h"
#include "nota/engine/Types.h"

#include <string_view>
#include <string>
#include <memory>
#include <cstdint>
#include <utility>
#include <vector>

namespace nota {

// ---- Macro & Rack ----------------------------------------------------------

bool Engine::Macro::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasRack(p->runtime, trackId_, deviceIndex_) && index_ >= 0 && index_ < 8;
}
float Engine::Macro::get() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().macroGet(trackId_, deviceIndex_, index_) : 0.0f;
}
void Engine::Macro::set(float value) {
    if (auto p = impl_.lock()) p->runtime.racks().macroSet(trackId_, deviceIndex_, index_, value);
}
std::string Engine::Macro::name() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().macroName(trackId_, deviceIndex_, index_) : std::string{};
}
void Engine::Macro::setName(const std::string& name) {
    if (auto p = impl_.lock()) p->runtime.racks().setMacroName(trackId_, deviceIndex_, index_, name);
}

bool Engine::Rack::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasRack(p->runtime, trackId_, deviceIndex_);
}
bool Engine::Rack::kindAllowed(int32_t kind) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().kindAllowed(trackId_, deviceIndex_, kind) : false;
}
int32_t Engine::Rack::chainCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainCount(trackId_, deviceIndex_) : 0;
}
Engine::RackChain Engine::Rack::chain(int32_t chainIndex) const {
    return RackChain(impl_, trackId_, deviceIndex_, chainIndex);
}
int32_t Engine::Rack::addChain(int32_t instrumentKind) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().addChain(trackId_, deviceIndex_, instrumentKind) : -1;
}
int32_t Engine::Rack::addSamplerChain(const std::string& path, int32_t rootNote, bool loop) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().addSamplerChain(trackId_, deviceIndex_, path, rootNote, loop) : -1;
}
int32_t Engine::Rack::addPluginInstrumentChain(int32_t catalogIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().addPluginInstrumentChain(trackId_, deviceIndex_, catalogIndex) : -1;
}
bool Engine::Rack::removeChain(int32_t chainIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().removeChain(trackId_, deviceIndex_, chainIndex) : false;
}
int32_t Engine::Rack::duplicateChain(int32_t chainIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().duplicateChain(trackId_, deviceIndex_, chainIndex) : -1;
}
bool Engine::Rack::checkpoint() {
    auto p = impl_.lock();
    return p ? p->runtime.racks().checkpoint(trackId_, deviceIndex_) : false;
}
int32_t Engine::Rack::nestedChainIndex(uint64_t uid) const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().nestedChainIndex(trackId_, deviceIndex_, uid) : -1;
}
Engine::Macro Engine::Rack::macro(int32_t macroIndex) const {
    return Macro(impl_, trackId_, deviceIndex_, macroIndex);
}
int32_t Engine::Rack::addMacroMapping(
    int32_t macro, int32_t chain, int32_t targetDevice, int32_t paramIndex, float rangeMin, float rangeMax) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().addMacroMapping(
                   trackId_, deviceIndex_, macro, chain, targetDevice, paramIndex, rangeMin, rangeMax)
             : -1;
}
int32_t Engine::Rack::mappingCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().mappingCount(trackId_, deviceIndex_) : 0;
}
bool Engine::Rack::mappingInfo(int32_t index, int32_t& macro, int32_t& chain, int32_t& targetDevice,
    int32_t& paramIndex, float& rangeMin, float& rangeMax) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().mappingInfo(
                   trackId_, deviceIndex_, index, macro, chain, targetDevice, paramIndex, rangeMin, rangeMax)
             : false;
}
bool Engine::Rack::removeMapping(int32_t index) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().removeMapping(trackId_, deviceIndex_, index) : false;
}
bool Engine::Rack::setMappingRange(int32_t index, float rangeMin, float rangeMax) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().setMappingRange(trackId_, deviceIndex_, index, rangeMin, rangeMax) : false;
}
int32_t Engine::Rack::mappingCurve(int32_t index) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().mappingCurve(trackId_, deviceIndex_, index) : 0;
}
bool Engine::Rack::setMappingCurve(int32_t index, int32_t curve) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().setMappingCurve(trackId_, deviceIndex_, index, curve) : false;
}
bool Engine::Rack::mono() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().mono(trackId_) : false;
}
void Engine::Rack::setMono(bool on) {
    if (auto p = impl_.lock()) p->runtime.racks().setMono(trackId_, on);
}
bool Engine::Rack::merge() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().merge(trackId_, deviceIndex_) : false;
}
void Engine::Rack::setMerge(bool on) {
    if (auto p = impl_.lock()) p->runtime.racks().setMerge(trackId_, deviceIndex_, on);
}
bool Engine::Rack::thru() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().thru(trackId_, deviceIndex_) : false;
}
void Engine::Rack::setThru(bool on) {
    if (auto p = impl_.lock()) p->runtime.racks().setThru(trackId_, deviceIndex_, on);
}
float Engine::Rack::volume() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().volume(trackId_, deviceIndex_) : 1.0f;
}
void Engine::Rack::setVolume(float v) {
    if (auto p = impl_.lock()) p->runtime.racks().setVolume(trackId_, deviceIndex_, v);
}
float Engine::Rack::glide() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().glide(trackId_, deviceIndex_) : 0.0f;
}
void Engine::Rack::setGlide(float v) {
    if (auto p = impl_.lock()) p->runtime.racks().setGlide(trackId_, deviceIndex_, v);
}
void Engine::Rack::setMode(int32_t mode) {
    if (auto p = impl_.lock()) p->runtime.racks().setMode(trackId_, deviceIndex_, mode);
}
int32_t Engine::Rack::mode() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().mode(trackId_, deviceIndex_) : 0;
}
void Engine::Rack::setDryWet(float v) {
    if (auto p = impl_.lock()) p->runtime.racks().setDryWet(trackId_, deviceIndex_, v);
}
float Engine::Rack::dryWet() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().dryWet(trackId_, deviceIndex_) : 1.0f;
}
void Engine::Rack::setPdc(bool on) {
    if (auto p = impl_.lock()) p->runtime.racks().setPdc(trackId_, deviceIndex_, on);
}
bool Engine::Rack::pdc() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().pdc(trackId_, deviceIndex_) : false;
}
void Engine::Rack::setChainSelect(float v) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainSelect(trackId_, deviceIndex_, v);
}
float Engine::Rack::chainSelect() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainSelect(trackId_, deviceIndex_) : 0.0f;
}
void Engine::Rack::setSelFollow(bool on) {
    if (auto p = impl_.lock()) p->runtime.racks().setSelFollow(trackId_, deviceIndex_, on);
}
bool Engine::Rack::selFollow() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().selFollow(trackId_, deviceIndex_) : false;
}
float Engine::Rack::liveSelector() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().liveSelector(trackId_, deviceIndex_) : 0.0f;
}
void Engine::Rack::setSwing(float v) {
    if (auto p = impl_.lock()) p->runtime.racks().setSwing(trackId_, deviceIndex_, v);
}
float Engine::Rack::swing() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().swing(trackId_, deviceIndex_) : 0.0f;
}
void Engine::Rack::setHumanize(float v) {
    if (auto p = impl_.lock()) p->runtime.racks().setHumanize(trackId_, deviceIndex_, v);
}
float Engine::Rack::humanize() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().humanize(trackId_, deviceIndex_) : 0.0f;
}

// ---- RackChain -------------------------------------------------------------

bool Engine::RackChain::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasRackChain(p->runtime, trackId_, deviceIndex_, chainIndex_);
}
int64_t Engine::RackChain::chainId() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainId(trackId_, deviceIndex_, chainIndex_) : 0;
}
std::string Engine::RackChain::name() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainName(trackId_, deviceIndex_, chainIndex_) : std::string{};
}
void Engine::RackChain::setName(const std::string& name) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainName(trackId_, deviceIndex_, chainIndex_, name);
}
int32_t Engine::RackChain::triggerNote() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainTriggerNote(trackId_, deviceIndex_, chainIndex_) : -1;
}
void Engine::RackChain::setTriggerNote(int32_t note) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainTriggerNote(trackId_, deviceIndex_, chainIndex_, note);
}
void Engine::RackChain::setVolume(float v) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainGain(trackId_, deviceIndex_, chainIndex_, v);
}
void Engine::RackChain::setPan(float v) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainPan(trackId_, deviceIndex_, chainIndex_, v);
}
void Engine::RackChain::setMute(bool m) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainMute(trackId_, deviceIndex_, chainIndex_, m);
}
void Engine::RackChain::setSolo(bool s) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainSolo(trackId_, deviceIndex_, chainIndex_, s);
}
float Engine::RackChain::volume() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainGain(trackId_, deviceIndex_, chainIndex_) : 1.0f;
}
float Engine::RackChain::pan() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainPan(trackId_, deviceIndex_, chainIndex_) : 0.0f;
}
bool Engine::RackChain::mute() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMute(trackId_, deviceIndex_, chainIndex_) : false;
}
bool Engine::RackChain::solo() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainSolo(trackId_, deviceIndex_, chainIndex_) : false;
}
float Engine::RackChain::meter() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMeter(trackId_, deviceIndex_, chainIndex_) : 0.0f;
}
void Engine::RackChain::zone(int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const {
    if (auto p = impl_.lock())
        p->runtime.racks().chainZone(trackId_, deviceIndex_, chainIndex_, keyLo, keyHi, velLo, velHi);
}
void Engine::RackChain::setZone(int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) {
    if (auto p = impl_.lock())
        p->runtime.racks().setChainZone(trackId_, deviceIndex_, chainIndex_, keyLo, keyHi, velLo, velHi);
}
void Engine::RackChain::selectRange(int32_t& lo, int32_t& hi) const {
    if (auto p = impl_.lock()) p->runtime.racks().chainSelectRange(trackId_, deviceIndex_, chainIndex_, lo, hi);
}
void Engine::RackChain::setSelectRange(int32_t lo, int32_t hi) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainSelectRange(trackId_, deviceIndex_, chainIndex_, lo, hi);
}
int32_t Engine::RackChain::transpose() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainTranspose(trackId_, deviceIndex_, chainIndex_) : 0;
}
void Engine::RackChain::setTranspose(int32_t st) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainTranspose(trackId_, deviceIndex_, chainIndex_, st);
}
float Engine::RackChain::velScale() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainVelScale(trackId_, deviceIndex_, chainIndex_) : 1.0f;
}
void Engine::RackChain::setVelScale(float v) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainVelScale(trackId_, deviceIndex_, chainIndex_, v);
}
void Engine::RackChain::setChoke(int32_t group) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainChoke(trackId_, deviceIndex_, chainIndex_, group);
}
int32_t Engine::RackChain::choke() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainChoke(trackId_, deviceIndex_, chainIndex_) : 0;
}
void Engine::RackChain::setTune(int32_t semitones) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainTune(trackId_, deviceIndex_, chainIndex_, semitones);
}
int32_t Engine::RackChain::tune() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainTune(trackId_, deviceIndex_, chainIndex_) : 0;
}
void Engine::RackChain::setDecay(float v) {
    if (auto p = impl_.lock()) p->runtime.racks().setChainDecay(trackId_, deviceIndex_, chainIndex_, v);
}
float Engine::RackChain::decay() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDecay(trackId_, deviceIndex_, chainIndex_) : 1.0f;
}
bool Engine::RackChain::setSamplerSample(const std::string& path, int32_t rootNote) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().setChainSamplerSample(trackId_, deviceIndex_, chainIndex_, path, rootNote) : false;
}
bool Engine::RackChain::samplerInfo(engine::SamplerInfo& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainSamplerInfo(trackId_, deviceIndex_, chainIndex_, &out) : false;
}
bool Engine::RackChain::setSamplerRoot(int32_t rootNote) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().setChainSamplerRoot(trackId_, deviceIndex_, chainIndex_, rootNote) : false;
}
float Engine::RackChain::samplerPlayPosition() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainSamplerPlayPosition(trackId_, deviceIndex_, chainIndex_) : -1.0f;
}
bool Engine::RackChain::mosaicSetProgram(const std::string& text, bool checkpoint) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMosaicSetProgram(trackId_, deviceIndex_, chainIndex_, text, checkpoint) : false;
}
std::string Engine::RackChain::mosaicProgram() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMosaicProgram(trackId_, deviceIndex_, chainIndex_) : std::string{};
}
bool Engine::RackChain::mosaicStatus(engine::MosaicStatus& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMosaicStatus(trackId_, deviceIndex_, chainIndex_, &out) : false;
}
int64_t Engine::RackChain::mosaicZoneSampleId(int32_t zone) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMosaicZoneSampleId(trackId_, deviceIndex_, chainIndex_, zone) : 0;
}
bool Engine::RackChain::setInstrument(int32_t kind) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().setChainInstrument(trackId_, deviceIndex_, chainIndex_, kind) : false;
}
int32_t Engine::RackChain::instrumentKind() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentKind(trackId_, deviceIndex_, chainIndex_) : -1;
}
std::string Engine::RackChain::instrumentName() const {
    auto p = impl_.lock();
    if (!p) return {};
    const char* s = p->runtime.racks().chainInstrumentName(trackId_, deviceIndex_, chainIndex_);
    return s ? std::string(s) : std::string{};
}
std::string Engine::RackChain::instrumentPluginId() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentPluginId(trackId_, deviceIndex_, chainIndex_) : std::string{};
}
int32_t Engine::RackChain::instrumentGetState(uint8_t* out, int32_t cap) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentGetState(trackId_, deviceIndex_, chainIndex_, out, cap) : 0;
}
void Engine::RackChain::openInstrumentEditor() {
    if (auto p = impl_.lock()) p->runtime.racks().openChainInstrumentEditor(trackId_, deviceIndex_, chainIndex_);
}
int32_t Engine::RackChain::instrumentParamCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentParamCount(trackId_, deviceIndex_, chainIndex_) : 0;
}
std::string Engine::RackChain::instrumentParamName(int32_t param) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentParamName(trackId_, deviceIndex_, chainIndex_, param) : std::string{};
}
std::string Engine::RackChain::instrumentParamId(int32_t param) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentParamId(trackId_, deviceIndex_, chainIndex_, param) : std::string{};
}
float Engine::RackChain::instrumentParamGet(int32_t param) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentParamGet(trackId_, deviceIndex_, chainIndex_, param) : 0.0f;
}
void Engine::RackChain::instrumentParamSet(int32_t param, float normalized) {
    if (auto p = impl_.lock())
        p->runtime.racks().chainInstrumentParamSet(trackId_, deviceIndex_, chainIndex_, param, normalized);
}
float Engine::RackChain::instrumentParamDefault(int32_t param) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentParamDefault(trackId_, deviceIndex_, chainIndex_, param) : 0.0f;
}
int32_t Engine::RackChain::instrumentScope(float* out, int32_t maxN) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentScope(trackId_, deviceIndex_, chainIndex_, out, maxN) : 0;
}
int32_t Engine::RackChain::instrumentVoiceCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentVoiceCount(trackId_, deviceIndex_, chainIndex_) : -1;
}
int32_t Engine::RackChain::instrumentHeldNotes(int32_t* out, int32_t maxN) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainInstrumentHeldNotes(trackId_, deviceIndex_, chainIndex_, out, maxN) : 0;
}
void Engine::RackChain::instrumentAction(int32_t id, int32_t iarg, float farg) {
    if (auto p = impl_.lock())
        p->runtime.racks().chainInstrumentAction(trackId_, deviceIndex_, chainIndex_, id, iarg, farg);
}
int32_t Engine::RackChain::addDevice(int32_t kind) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().addChainDevice(trackId_, deviceIndex_, chainIndex_, kind) : -1;
}
int32_t Engine::RackChain::addPluginDevice(int32_t catalogIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().addPluginChainDevice(trackId_, deviceIndex_, chainIndex_, catalogIndex) : -1;
}
bool Engine::RackChain::removeDevice(int32_t slot) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().removeChainDevice(trackId_, deviceIndex_, chainIndex_, slot) : false;
}
bool Engine::RackChain::moveDevice(int32_t from, int32_t to) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().moveChainDevice(trackId_, deviceIndex_, chainIndex_, from, to) : false;
}
void Engine::RackChain::openDeviceEditor(int32_t dev) {
    if (auto p = impl_.lock()) p->runtime.racks().openChainDeviceEditor(trackId_, deviceIndex_, chainIndex_, dev);
}
int32_t Engine::RackChain::deviceCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceCount(trackId_, deviceIndex_, chainIndex_) : 0;
}
int32_t Engine::RackChain::deviceKind(int32_t slot) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceBuiltinKind(trackId_, deviceIndex_, chainIndex_, slot) : -1;
}
std::string Engine::RackChain::deviceName(int32_t slot) const {
    auto p = impl_.lock();
    if (!p) return {};
    const char* s = p->runtime.racks().chainDeviceName(trackId_, deviceIndex_, chainIndex_, slot);
    return s ? std::string(s) : std::string{};
}
int32_t Engine::RackChain::deviceParamCount(int32_t slot) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceParamCount(trackId_, deviceIndex_, chainIndex_, slot) : 0;
}
std::string Engine::RackChain::deviceParamName(int32_t slot, int32_t param) const {
    auto p = impl_.lock();
    if (!p) return {};
    const char* s = p->runtime.racks().chainDeviceParamName(trackId_, deviceIndex_, chainIndex_, slot, param);
    return s ? std::string(s) : std::string{};
}
std::string Engine::RackChain::deviceParamId(int32_t slot, int32_t param) const {
    auto p = impl_.lock();
    if (!p) return {};
    auto* d = p->runtime.racks().coreAt(trackId_, deviceIndex_);
    if (!d) return {};
    if (auto* cd = d->chainDeviceAt(chainIndex_, slot)) return cd->pluginParamId(param);
    return {};
}
float Engine::RackChain::deviceParamMin(int32_t slot, int32_t param) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceParamMin(trackId_, deviceIndex_, chainIndex_, slot, param) : 0.0f;
}
float Engine::RackChain::deviceParamMax(int32_t slot, int32_t param) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceParamMax(trackId_, deviceIndex_, chainIndex_, slot, param) : 1.0f;
}
float Engine::RackChain::deviceParamGet(int32_t slot, int32_t param) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceParamGet(trackId_, deviceIndex_, chainIndex_, slot, param) : 0.0f;
}
void Engine::RackChain::deviceParamSet(int32_t slot, int32_t param, float value) {
    if (auto p = impl_.lock())
        p->runtime.racks().chainDeviceParamSet(trackId_, deviceIndex_, chainIndex_, slot, param, value);
}
void Engine::RackChain::setDeviceBypassed(int32_t slot, bool bypassed) {
    if (auto p = impl_.lock())
        p->runtime.racks().setChainDeviceBypassed(trackId_, deviceIndex_, chainIndex_, slot, bypassed);
}
bool Engine::RackChain::deviceBypassed(int32_t slot) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceBypassed(trackId_, deviceIndex_, chainIndex_, slot) : false;
}
float Engine::RackChain::deviceGainReduction(int32_t slot) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceGainReduction(trackId_, deviceIndex_, chainIndex_, slot) : 0.0f;
}
int32_t Engine::RackChain::deviceScope(int32_t slot, float* out, int32_t maxSamples) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceScope(trackId_, deviceIndex_, chainIndex_, slot, out, maxSamples) : 0;
}
int32_t Engine::RackChain::deviceLayerWave(int32_t slot, int32_t layer, float* out, int32_t maxSamples) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceLayerWave(
                   trackId_, deviceIndex_, chainIndex_, slot, layer, out, maxSamples)
             : 0;
}
std::string Engine::RackChain::deviceText(int32_t slot, int32_t id) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceText(trackId_, deviceIndex_, chainIndex_, slot, id) : std::string{};
}
void Engine::RackChain::deviceAction(int32_t slot, int32_t id, int32_t iarg, float farg) {
    if (auto p = impl_.lock())
        p->runtime.racks().chainDeviceAction(trackId_, deviceIndex_, chainIndex_, slot, id, iarg, farg);
}
int32_t Engine::RackChain::addMidiEffect(int32_t kind) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().addChainMidi(trackId_, deviceIndex_, chainIndex_, kind) : -1;
}
bool Engine::RackChain::removeMidiEffect(int32_t slot) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().removeChainMidi(trackId_, deviceIndex_, chainIndex_, slot) : false;
}
bool Engine::RackChain::moveMidiEffect(int32_t from, int32_t to) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().moveChainMidi(trackId_, deviceIndex_, chainIndex_, from, to) : false;
}
int32_t Engine::RackChain::midiCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMidiCount(trackId_, deviceIndex_, chainIndex_) : 0;
}
int32_t Engine::RackChain::midiKind(int32_t slot) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    return m ? m->midiKind() : -1;
}
std::string Engine::RackChain::midiName(int32_t slot) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    return (m && m->displayName()) ? std::string(m->displayName()) : std::string{};
}
int32_t Engine::RackChain::midiParamCount(int32_t slot) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    return m ? m->paramCount() : 0;
}
std::string Engine::RackChain::midiParamName(int32_t slot, int32_t param) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    const char* n = (m && param >= 0 && param < m->paramCount()) ? m->paramName(param) : nullptr;
    return n ? std::string(n) : std::string{};
}
float Engine::RackChain::midiParamMin(int32_t slot, int32_t param) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    return (m && param >= 0 && param < m->paramCount()) ? m->paramMin(param) : 0.0f;
}
float Engine::RackChain::midiParamMax(int32_t slot, int32_t param) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    return (m && param >= 0 && param < m->paramCount()) ? m->paramMax(param) : 1.0f;
}
float Engine::RackChain::midiParamGet(int32_t slot, int32_t param) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    return (m && param >= 0 && param < m->paramCount()) ? m->getParam(param) : 0.0f;
}
bool Engine::RackChain::grainSetSample(const std::string& path, int32_t rootNote) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainGrainSetSample(trackId_, deviceIndex_, chainIndex_, path, rootNote) : false;
}
int32_t Engine::RackChain::midiLastIn(int32_t slot) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    return m ? m->midiLastIn() : -1;
}
int32_t Engine::RackChain::midiLastOut(int32_t slot) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    return m ? m->midiLastOut() : -1;
}
int32_t Engine::RackChain::midiScope(int32_t slot, float* out, int32_t maxSamples) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    return (m && out) ? m->midiScope(out, maxSamples) : 0;
}
void Engine::RackChain::midiCommand(int32_t slot, int32_t cmd) {
    auto p = impl_.lock();
    if (auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr)
        m->midiCommand(cmd);
}
void Engine::RackChain::midiParamSet(int32_t slot, int32_t param, float value) {
    if (auto p = impl_.lock())
        p->runtime.racks().chainMidiParamSet(trackId_, deviceIndex_, chainIndex_, slot, param, value);
}
void Engine::RackChain::setMidiBypassed(int32_t slot, bool bypassed) {
    if (auto p = impl_.lock())
        p->runtime.racks().setChainMidiBypassed(trackId_, deviceIndex_, chainIndex_, slot, bypassed);
}
bool Engine::RackChain::midiBypassed(int32_t slot) const {
    auto p = impl_.lock();
    auto* m = p ? p->runtime.racks().chainMidiAt(trackId_, deviceIndex_, chainIndex_, slot) : nullptr;
    return m ? m->bypassed() : false;
}
bool Engine::RackChain::copyDevice(int32_t slot) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceCopy(trackId_, deviceIndex_, chainIndex_, slot) : false;
}
bool Engine::RackChain::copyMidiEffect(int32_t slot) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMidiCopy(trackId_, deviceIndex_, chainIndex_, slot) : false;
}
int32_t Engine::RackChain::pasteDevice(int32_t at) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDevicePaste(trackId_, deviceIndex_, chainIndex_, at) : -1;
}
int32_t Engine::RackChain::pasteMidiEffect(int32_t at) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMidiPaste(trackId_, deviceIndex_, chainIndex_, at) : -1;
}
int32_t Engine::RackChain::pasteDevices(
    int32_t midiAt, int32_t fxAt, int32_t* outSkipped, int32_t* outFirstMidi, int32_t* outFirstFx) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainPasteDevices(
                   trackId_, deviceIndex_, chainIndex_, midiAt, fxAt, outSkipped, outFirstMidi, outFirstFx)
             : -1;
}
int32_t Engine::RackChain::duplicateDevice(int32_t slot) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceDuplicate(trackId_, deviceIndex_, chainIndex_, slot) : -1;
}
int32_t Engine::RackChain::duplicateMidiEffect(int32_t slot) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMidiDuplicate(trackId_, deviceIndex_, chainIndex_, slot) : -1;
}
int32_t Engine::RackChain::moveDeviceTo(int32_t slot, int32_t toChain) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainDeviceMoveTo(trackId_, deviceIndex_, chainIndex_, slot, toChain) : -1;
}
int32_t Engine::RackChain::moveMidiTo(int32_t slot, int32_t toChain) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMidiMoveTo(trackId_, deviceIndex_, chainIndex_, slot, toChain) : -1;
}
bool Engine::RackChain::midiCcDest(
    int32_t slot, int32_t* dev, int32_t* destChain, int32_t* destSlot, int32_t* param, float* depth) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMidiCcDest(
                   trackId_, deviceIndex_, chainIndex_, slot, dev, destChain, destSlot, param, depth)
             : false;
}
bool Engine::RackChain::setMidiCcDest(
    int32_t slot, int32_t dev, int32_t destChain, int32_t destSlot, int32_t param, float depth) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainMidiSetCcDest(
                   trackId_, deviceIndex_, chainIndex_, slot, dev, destChain, destSlot, param, depth)
             : false;
}
bool Engine::RackChain::acceptsSidechain(int32_t slot) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainAcceptsSidechain(trackId_, deviceIndex_, chainIndex_, slot) : false;
}
bool Engine::RackChain::sidechain(int32_t slot, int32_t* src, int32_t* tapPre, float* gainDb, float* mix) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainSidechain(trackId_, deviceIndex_, chainIndex_, slot, src, tapPre, gainDb, mix)
             : false;
}
int32_t Engine::RackChain::setSidechain(int32_t slot, int32_t src, int32_t tapPre, float gainDb, float mix) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainSetSidechain(trackId_, deviceIndex_, chainIndex_, slot, src, tapPre, gainDb, mix)
             : -1;
}
bool Engine::RackChain::sidechainPart(int32_t slot, int32_t* part) const {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainSidechainPart(trackId_, deviceIndex_, chainIndex_, slot, part) : false;
}
int32_t Engine::RackChain::setSidechainPart(int32_t slot, int32_t part) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainSetSidechainPart(trackId_, deviceIndex_, chainIndex_, slot, part) : -1;
}
int32_t Engine::RackChain::setKey(int32_t slot, int32_t src, int32_t part, int32_t tapPre, float gainDb, float mix) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().chainSetKey(trackId_, deviceIndex_, chainIndex_, slot, src, part, tapPre, gainDb, mix)
             : -1;
}

} // namespace nota
