// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "api/ApiImpl.h"

#include "devices/RackOps.h"

#include "nota/devices/racks/RackCore.h"
#include "nota/dsp/Device.h"
#include "nota/dsp/MidiDevice.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Rack.h"
#include "nota/engine/RackChain.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>

namespace nota {

// ---- Macro & Rack ----------------------------------------------------------

Engine::Macro::Macro(
    std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, int32_t macroIndex) noexcept
    : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex), index_(macroIndex) {
    if (auto p = impl_.lock()) {
        deviceId_ = detail::EngineApiAccess::rackIdentity(p->runtime, trackId_, deviceIndex_);
    }
}

Engine::Macro::Macro(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, int32_t macroIndex,
    uint64_t deviceId) noexcept
    : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex), index_(macroIndex), deviceId_(deviceId) {}

bool Engine::Macro::resolve(std::shared_ptr<Engine::Impl>& outImpl, int32_t& outDevIdx) const noexcept {
    outImpl = impl_.lock();
    return outImpl && index_ >= 0 && index_ < 8 &&
           detail::EngineApiAccess::resolveRack(outImpl->runtime, trackId_, deviceIndex_, deviceId_, outDevIdx);
}

int32_t Engine::Macro::deviceIndex() const noexcept {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? di : -1;
}

bool Engine::Macro::isValid() const noexcept {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di);
}
float Engine::Macro::get() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().macroGet(trackId_, di, index_) : 0.0f;
}
void Engine::Macro::set(float value) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().macroSet(trackId_, di, index_, value);
}
std::string Engine::Macro::name() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().macroName(trackId_, di, index_) : std::string{};
}
void Engine::Macro::setName(const std::string& name) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setMacroName(trackId_, di, index_, name);
}

Engine::Rack::Rack(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex) noexcept
    : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex) {
    if (auto p = impl_.lock()) {
        deviceId_ = detail::EngineApiAccess::rackIdentity(p->runtime, trackId_, deviceIndex_);
    }
}

Engine::Rack::Rack(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, uint64_t deviceId) noexcept
    : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex), deviceId_(deviceId) {}

bool Engine::Rack::resolve(std::shared_ptr<Engine::Impl>& outImpl, int32_t& outDevIdx) const noexcept {
    outImpl = impl_.lock();
    return outImpl &&
           detail::EngineApiAccess::resolveRack(outImpl->runtime, trackId_, deviceIndex_, deviceId_, outDevIdx);
}

int32_t Engine::Rack::deviceIndex() const noexcept {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? di : -1;
}

bool Engine::Rack::isValid() const noexcept {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di);
}
bool Engine::Rack::kindAllowed(int32_t kind) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().kindAllowed(trackId_, di, kind) : false;
}
int32_t Engine::Rack::chainCount() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().chainCount(trackId_, di) : 0;
}
Engine::RackChain Engine::Rack::chain(int32_t chainIndex) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (!resolve(p, di)) return RackChain(impl_, trackId_, deviceIndex_, chainIndex, 0, 0);
    const uint64_t uid = detail::EngineApiAccess::rackChainIdentity(p->runtime, trackId_, di, chainIndex);
    return RackChain(impl_, trackId_, di, chainIndex, deviceId_, uid);
}
int32_t Engine::Rack::addChain(int32_t instrumentKind) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().addChain(trackId_, di, instrumentKind) : -1;
}
int32_t Engine::Rack::addSamplerChain(const std::string& path, int32_t rootNote, bool loop) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().addSamplerChain(trackId_, di, path, rootNote, loop) : -1;
}
int32_t Engine::Rack::addPluginInstrumentChain(int32_t catalogIndex) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().addPluginInstrumentChain(trackId_, di, catalogIndex) : -1;
}
bool Engine::Rack::removeChain(int32_t chainIndex) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().removeChain(trackId_, di, chainIndex) : false;
}
int32_t Engine::Rack::duplicateChain(int32_t chainIndex) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().duplicateChain(trackId_, di, chainIndex) : -1;
}
bool Engine::Rack::checkpoint() {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().checkpoint(trackId_, di) : false;
}
int32_t Engine::Rack::nestedChainIndex(uint64_t uid) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (!resolve(p, di)) return -1;
    const auto* r = p->runtime.racks().coreAt(trackId_, di);
    return r ? r->chainIndexOfUid(uid) : -1;
}
Engine::Macro Engine::Rack::macro(int32_t macroIndex) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    const bool valid = resolve(p, di);
    return Macro(impl_, trackId_, valid ? di : deviceIndex_, macroIndex, valid ? deviceId_ : 0);
}
int32_t Engine::Rack::addMacroMapping(
    int32_t macro, int32_t chain, int32_t targetDevice, int32_t paramIndex, float rangeMin, float rangeMax) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().addMacroMapping(
                                trackId_, di, macro, chain, targetDevice, paramIndex, rangeMin, rangeMax)
                          : -1;
}
int32_t Engine::Rack::mappingCount() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().mappingCount(trackId_, di) : 0;
}
bool Engine::Rack::mappingInfo(int32_t index, int32_t& macro, int32_t& chain, int32_t& targetDevice,
    int32_t& paramIndex, float& rangeMin, float& rangeMax) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().mappingInfo(
                                trackId_, di, index, macro, chain, targetDevice, paramIndex, rangeMin, rangeMax)
                          : false;
}
bool Engine::Rack::removeMapping(int32_t index) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().removeMapping(trackId_, di, index) : false;
}
bool Engine::Rack::setMappingRange(int32_t index, float rangeMin, float rangeMax) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().setMappingRange(trackId_, di, index, rangeMin, rangeMax) : false;
}
int32_t Engine::Rack::mappingCurve(int32_t index) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().mappingCurve(trackId_, di, index) : 0;
}
bool Engine::Rack::setMappingCurve(int32_t index, int32_t curve) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().setMappingCurve(trackId_, di, index, curve) : false;
}
bool Engine::Rack::mono() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().mono(trackId_) : false;
}
void Engine::Rack::setMono(bool on) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setMono(trackId_, on);
}
bool Engine::Rack::merge() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().merge(trackId_, di) : false;
}
void Engine::Rack::setMerge(bool on) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setMerge(trackId_, di, on);
}
bool Engine::Rack::thru() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().thru(trackId_, di) : false;
}
void Engine::Rack::setThru(bool on) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setThru(trackId_, di, on);
}
float Engine::Rack::volume() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().volume(trackId_, di) : 1.0f;
}
void Engine::Rack::setVolume(float v) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setVolume(trackId_, di, v);
}
float Engine::Rack::glide() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().glide(trackId_, di) : 0.0f;
}
void Engine::Rack::setGlide(float v) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setGlide(trackId_, di, v);
}
void Engine::Rack::setMode(int32_t mode) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setMode(trackId_, di, mode);
}
int32_t Engine::Rack::mode() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().mode(trackId_, di) : 0;
}
void Engine::Rack::setDryWet(float v) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setDryWet(trackId_, di, v);
}
float Engine::Rack::dryWet() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().dryWet(trackId_, di) : 1.0f;
}
void Engine::Rack::setPdc(bool on) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setPdc(trackId_, di, on);
}
bool Engine::Rack::pdc() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().pdc(trackId_, di) : false;
}
void Engine::Rack::setChainSelect(float v) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setChainSelect(trackId_, di, v);
}
float Engine::Rack::chainSelect() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().chainSelect(trackId_, di) : 0.0f;
}
void Engine::Rack::setSelFollow(bool on) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setSelFollow(trackId_, di, on);
}
bool Engine::Rack::selFollow() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().selFollow(trackId_, di) : false;
}
float Engine::Rack::liveSelector() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().liveSelector(trackId_, di) : 0.0f;
}
void Engine::Rack::setSwing(float v) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setSwing(trackId_, di, v);
}
float Engine::Rack::swing() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().swing(trackId_, di) : 0.0f;
}
void Engine::Rack::setHumanize(float v) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    if (resolve(p, di)) p->runtime.racks().setHumanize(trackId_, di, v);
}
float Engine::Rack::humanize() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    return resolve(p, di) ? p->runtime.racks().humanize(trackId_, di) : 0.0f;
}

// ---- RackChain -------------------------------------------------------------

Engine::RackChain::RackChain(
    std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, int32_t chainIndex) noexcept
    : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex), chainIndex_(chainIndex) {
    if (auto p = impl_.lock()) {
        rackDeviceId_ = detail::EngineApiAccess::rackIdentity(p->runtime, trackId_, deviceIndex_);
        if (rackDeviceId_ != 0)
            chainUid_ = detail::EngineApiAccess::rackChainIdentity(p->runtime, trackId_, deviceIndex_, chainIndex_);
    }
}

Engine::RackChain::RackChain(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, int32_t chainIndex,
    uint64_t rackDeviceId, uint64_t chainUid) noexcept
    : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex), chainIndex_(chainIndex),
      rackDeviceId_(rackDeviceId), chainUid_(chainUid) {}

bool Engine::RackChain::resolve(
    std::shared_ptr<Engine::Impl>& outImpl, int32_t& outDevIdx, int32_t& outChainIdx) const noexcept {
    outImpl = impl_.lock();
    return outImpl && detail::EngineApiAccess::resolveRackChain(
                          outImpl->runtime, trackId_, deviceIndex_, rackDeviceId_, chainUid_, outDevIdx, outChainIdx);
}

int32_t Engine::RackChain::deviceIndex() const noexcept {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? di : -1;
}

int32_t Engine::RackChain::index() const noexcept {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? ci : -1;
}

bool Engine::RackChain::isValid() const noexcept {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci);
}
int64_t Engine::RackChain::chainId() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainId(trackId_, di, ci) : 0;
}
std::string Engine::RackChain::name() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainName(trackId_, di, ci) : std::string{};
}
void Engine::RackChain::setName(const std::string& name) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainName(trackId_, di, ci, name);
}
int32_t Engine::RackChain::triggerNote() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainTriggerNote(trackId_, di, ci) : -1;
}
void Engine::RackChain::setTriggerNote(int32_t note) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainTriggerNote(trackId_, di, ci, note);
}
void Engine::RackChain::setVolume(float v) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainGain(trackId_, di, ci, v);
}
void Engine::RackChain::setPan(float v) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainPan(trackId_, di, ci, v);
}
void Engine::RackChain::setMute(bool m) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainMute(trackId_, di, ci, m);
}
void Engine::RackChain::setSolo(bool s) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainSolo(trackId_, di, ci, s);
}
float Engine::RackChain::volume() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainGain(trackId_, di, ci) : 1.0f;
}
float Engine::RackChain::pan() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainPan(trackId_, di, ci) : 0.0f;
}
bool Engine::RackChain::mute() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMute(trackId_, di, ci) : false;
}
bool Engine::RackChain::solo() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSolo(trackId_, di, ci) : false;
}
float Engine::RackChain::meter() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMeter(trackId_, di, ci) : 0.0f;
}
void Engine::RackChain::zone(int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().chainZone(trackId_, di, ci, keyLo, keyHi, velLo, velHi);
}
void Engine::RackChain::setZone(int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainZone(trackId_, di, ci, keyLo, keyHi, velLo, velHi);
}
void Engine::RackChain::selectRange(int32_t& lo, int32_t& hi) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().chainSelectRange(trackId_, di, ci, lo, hi);
}
void Engine::RackChain::setSelectRange(int32_t lo, int32_t hi) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainSelectRange(trackId_, di, ci, lo, hi);
}
int32_t Engine::RackChain::transpose() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainTranspose(trackId_, di, ci) : 0;
}
void Engine::RackChain::setTranspose(int32_t st) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainTranspose(trackId_, di, ci, st);
}
float Engine::RackChain::velScale() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainVelScale(trackId_, di, ci) : 1.0f;
}
void Engine::RackChain::setVelScale(float v) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainVelScale(trackId_, di, ci, v);
}
void Engine::RackChain::setChoke(int32_t group) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainChoke(trackId_, di, ci, group);
}
int32_t Engine::RackChain::choke() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainChoke(trackId_, di, ci) : 0;
}
void Engine::RackChain::setTune(int32_t semitones) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainTune(trackId_, di, ci, semitones);
}
int32_t Engine::RackChain::tune() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainTune(trackId_, di, ci) : 0;
}
void Engine::RackChain::setDecay(float v) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainDecay(trackId_, di, ci, v);
}
float Engine::RackChain::decay() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDecay(trackId_, di, ci) : 1.0f;
}
bool Engine::RackChain::setSamplerSample(const std::string& path, int32_t rootNote) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().setChainSamplerSample(trackId_, di, ci, path, rootNote) : false;
}
bool Engine::RackChain::samplerInfo(engine::SamplerInfo& out) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSamplerInfo(trackId_, di, ci, &out) : false;
}
bool Engine::RackChain::setSamplerRoot(int32_t rootNote) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().setChainSamplerRoot(trackId_, di, ci, rootNote) : false;
}
float Engine::RackChain::samplerPlayPosition() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSamplerPlayPosition(trackId_, di, ci) : -1.0f;
}
bool Engine::RackChain::mosaicSetProgram(const std::string& text, bool checkpoint) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMosaicSetProgram(trackId_, di, ci, text, checkpoint) : false;
}
std::string Engine::RackChain::mosaicProgram() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMosaicProgram(trackId_, di, ci) : std::string{};
}
bool Engine::RackChain::mosaicStatus(engine::MosaicStatus& out) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMosaicStatus(trackId_, di, ci, &out) : false;
}
int64_t Engine::RackChain::mosaicZoneSampleId(int32_t zone) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMosaicZoneSampleId(trackId_, di, ci, zone) : 0;
}
bool Engine::RackChain::setInstrument(int32_t kind) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().setChainInstrument(trackId_, di, ci, kind) : false;
}
int32_t Engine::RackChain::instrumentKind() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentKind(trackId_, di, ci) : -1;
}
std::string Engine::RackChain::instrumentName() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (!resolve(p, di, ci)) return {};
    const char* s = p->runtime.racks().chainInstrumentName(trackId_, di, ci);
    return s ? std::string(s) : std::string{};
}
std::string Engine::RackChain::instrumentPluginId() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentPluginId(trackId_, di, ci) : std::string{};
}
int32_t Engine::RackChain::instrumentGetState(uint8_t* out, int32_t cap) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentGetState(trackId_, di, ci, out, cap) : 0;
}
void Engine::RackChain::openInstrumentEditor() {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().openChainInstrumentEditor(trackId_, di, ci);
}
int32_t Engine::RackChain::instrumentParamCount() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentParamCount(trackId_, di, ci) : 0;
}
std::string Engine::RackChain::instrumentParamName(int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentParamName(trackId_, di, ci, param) : std::string{};
}
std::string Engine::RackChain::instrumentParamId(int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentParamId(trackId_, di, ci, param) : std::string{};
}
float Engine::RackChain::instrumentParamGet(int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentParamGet(trackId_, di, ci, param) : 0.0f;
}
void Engine::RackChain::instrumentParamSet(int32_t param, float normalized) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().chainInstrumentParamSet(trackId_, di, ci, param, normalized);
}
float Engine::RackChain::instrumentParamDefault(int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentParamDefault(trackId_, di, ci, param) : 0.0f;
}
int32_t Engine::RackChain::instrumentScope(float* out, int32_t maxN) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentScope(trackId_, di, ci, out, maxN) : 0;
}
int32_t Engine::RackChain::instrumentVoiceCount() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentVoiceCount(trackId_, di, ci) : -1;
}
int32_t Engine::RackChain::instrumentHeldNotes(int32_t* out, int32_t maxN) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainInstrumentHeldNotes(trackId_, di, ci, out, maxN) : 0;
}
void Engine::RackChain::instrumentAction(int32_t id, int32_t iarg, float farg) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().chainInstrumentAction(trackId_, di, ci, id, iarg, farg);
}
int32_t Engine::RackChain::addDevice(int32_t kind) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().addChainDevice(trackId_, di, ci, kind) : -1;
}
int32_t Engine::RackChain::addPluginDevice(int32_t catalogIndex) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().addPluginChainDevice(trackId_, di, ci, catalogIndex) : -1;
}
bool Engine::RackChain::removeDevice(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().removeChainDevice(trackId_, di, ci, slot) : false;
}
bool Engine::RackChain::moveDevice(int32_t from, int32_t to) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().moveChainDevice(trackId_, di, ci, from, to) : false;
}
void Engine::RackChain::openDeviceEditor(int32_t dev) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().openChainDeviceEditor(trackId_, di, ci, dev);
}
int32_t Engine::RackChain::deviceCount() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceCount(trackId_, di, ci) : 0;
}
int32_t Engine::RackChain::deviceKind(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceBuiltinKind(trackId_, di, ci, slot) : -1;
}
std::string Engine::RackChain::deviceName(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (!resolve(p, di, ci)) return {};
    const char* s = p->runtime.racks().chainDeviceName(trackId_, di, ci, slot);
    return s ? std::string(s) : std::string{};
}
int32_t Engine::RackChain::deviceParamCount(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceParamCount(trackId_, di, ci, slot) : 0;
}
std::string Engine::RackChain::deviceParamName(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (!resolve(p, di, ci)) return {};
    const char* s = p->runtime.racks().chainDeviceParamName(trackId_, di, ci, slot, param);
    return s ? std::string(s) : std::string{};
}
std::string Engine::RackChain::deviceParamId(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (!resolve(p, di, ci)) return {};
    auto* d = p->runtime.racks().coreAt(trackId_, di);
    if (!d) return {};
    if (auto* cd = d->chainDeviceAt(ci, slot)) return cd->pluginParamId(param);
    return {};
}
float Engine::RackChain::deviceParamMin(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceParamMin(trackId_, di, ci, slot, param) : 0.0f;
}
float Engine::RackChain::deviceParamMax(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceParamMax(trackId_, di, ci, slot, param) : 1.0f;
}
float Engine::RackChain::deviceParamGet(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceParamGet(trackId_, di, ci, slot, param) : 0.0f;
}
void Engine::RackChain::deviceParamSet(int32_t slot, int32_t param, float value) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().chainDeviceParamSet(trackId_, di, ci, slot, param, value);
}
void Engine::RackChain::setDeviceBypassed(int32_t slot, bool bypassed) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainDeviceBypassed(trackId_, di, ci, slot, bypassed);
}
bool Engine::RackChain::deviceBypassed(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceBypassed(trackId_, di, ci, slot) : false;
}
float Engine::RackChain::deviceGainReduction(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceGainReduction(trackId_, di, ci, slot) : 0.0f;
}
int32_t Engine::RackChain::deviceScope(int32_t slot, float* out, int32_t maxSamples) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceScope(trackId_, di, ci, slot, out, maxSamples) : 0;
}
int32_t Engine::RackChain::deviceLayerWave(int32_t slot, int32_t layer, float* out, int32_t maxSamples) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceLayerWave(trackId_, di, ci, slot, layer, out, maxSamples)
                              : 0;
}
std::string Engine::RackChain::deviceText(int32_t slot, int32_t id) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceText(trackId_, di, ci, slot, id) : std::string{};
}
void Engine::RackChain::deviceAction(int32_t slot, int32_t id, int32_t iarg, float farg) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().chainDeviceAction(trackId_, di, ci, slot, id, iarg, farg);
}
int32_t Engine::RackChain::addMidiEffect(int32_t kind) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().addChainMidi(trackId_, di, ci, kind) : -1;
}
bool Engine::RackChain::removeMidiEffect(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().removeChainMidi(trackId_, di, ci, slot) : false;
}
bool Engine::RackChain::moveMidiEffect(int32_t from, int32_t to) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().moveChainMidi(trackId_, di, ci, from, to) : false;
}
int32_t Engine::RackChain::midiCount() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMidiCount(trackId_, di, ci) : 0;
}
int32_t Engine::RackChain::midiKind(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return m ? m->midiKind() : -1;
}
std::string Engine::RackChain::midiName(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return (m && m->displayName()) ? std::string(m->displayName()) : std::string{};
}
int32_t Engine::RackChain::midiParamCount(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return m ? m->paramCount() : 0;
}
std::string Engine::RackChain::midiParamName(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    const char* n = (m && param >= 0 && param < m->paramCount()) ? m->paramName(param) : nullptr;
    return n ? std::string(n) : std::string{};
}
float Engine::RackChain::midiParamMin(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return (m && param >= 0 && param < m->paramCount()) ? m->paramMin(param) : 0.0f;
}
float Engine::RackChain::midiParamMax(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return (m && param >= 0 && param < m->paramCount()) ? m->paramMax(param) : 1.0f;
}
float Engine::RackChain::midiParamGet(int32_t slot, int32_t param) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return (m && param >= 0 && param < m->paramCount()) ? m->getParam(param) : 0.0f;
}
bool Engine::RackChain::grainSetSample(const std::string& path, int32_t rootNote) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainGrainSetSample(trackId_, di, ci, path, rootNote) : false;
}
int32_t Engine::RackChain::midiLastIn(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return m ? m->midiLastIn() : -1;
}
int32_t Engine::RackChain::midiLastOut(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return m ? m->midiLastOut() : -1;
}
int32_t Engine::RackChain::midiScope(int32_t slot, float* out, int32_t maxSamples) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return (m && out) ? m->midiScope(out, maxSamples) : 0;
}
void Engine::RackChain::midiCommand(int32_t slot, int32_t cmd) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr)
        m->midiCommand(cmd);
}
void Engine::RackChain::midiParamSet(int32_t slot, int32_t param, float value) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().chainMidiParamSet(trackId_, di, ci, slot, param, value);
}
void Engine::RackChain::setMidiBypassed(int32_t slot, bool bypassed) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    if (resolve(p, di, ci)) p->runtime.racks().setChainMidiBypassed(trackId_, di, ci, slot, bypassed);
}
bool Engine::RackChain::midiBypassed(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    auto* m = resolve(p, di, ci) ? p->runtime.racks().chainMidiAt(trackId_, di, ci, slot) : nullptr;
    return m ? m->bypassed() : false;
}
bool Engine::RackChain::copyDevice(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceCopy(trackId_, di, ci, slot) : false;
}
bool Engine::RackChain::copyMidiEffect(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMidiCopy(trackId_, di, ci, slot) : false;
}
int32_t Engine::RackChain::pasteDevice(int32_t at) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDevicePaste(trackId_, di, ci, at) : -1;
}
int32_t Engine::RackChain::pasteMidiEffect(int32_t at) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMidiPaste(trackId_, di, ci, at) : -1;
}
int32_t Engine::RackChain::pasteDevices(
    int32_t midiAt, int32_t fxAt, int32_t* outSkipped, int32_t* outFirstMidi, int32_t* outFirstFx) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainPasteDevices(
                                    trackId_, di, ci, midiAt, fxAt, outSkipped, outFirstMidi, outFirstFx)
                              : -1;
}
int32_t Engine::RackChain::duplicateDevice(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceDuplicate(trackId_, di, ci, slot) : -1;
}
int32_t Engine::RackChain::duplicateMidiEffect(int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMidiDuplicate(trackId_, di, ci, slot) : -1;
}
int32_t Engine::RackChain::moveDeviceTo(int32_t slot, int32_t toChain) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainDeviceMoveTo(trackId_, di, ci, slot, toChain) : -1;
}
int32_t Engine::RackChain::moveMidiTo(int32_t slot, int32_t toChain) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainMidiMoveTo(trackId_, di, ci, slot, toChain) : -1;
}
bool Engine::RackChain::midiCcDest(
    int32_t slot, int32_t* dev, int32_t* destChain, int32_t* destSlot, int32_t* param, float* depth) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci)
               ? p->runtime.racks().chainMidiCcDest(trackId_, di, ci, slot, dev, destChain, destSlot, param, depth)
               : false;
}
bool Engine::RackChain::setMidiCcDest(
    int32_t slot, int32_t dev, int32_t destChain, int32_t destSlot, int32_t param, float depth) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci)
               ? p->runtime.racks().chainMidiSetCcDest(trackId_, di, ci, slot, dev, destChain, destSlot, param, depth)
               : false;
}
bool Engine::RackChain::acceptsSidechain(int32_t slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainAcceptsSidechain(trackId_, di, ci, slot) : false;
}
bool Engine::RackChain::sidechain(int32_t slot, int32_t* src, int32_t* tapPre, float* gainDb, float* mix) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSidechain(trackId_, di, ci, slot, src, tapPre, gainDb, mix)
                              : false;
}
int32_t Engine::RackChain::setSidechain(int32_t slot, int32_t src, int32_t tapPre, float gainDb, float mix) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSetSidechain(trackId_, di, ci, slot, src, tapPre, gainDb, mix)
                              : -1;
}
bool Engine::RackChain::sidechainPart(int32_t slot, int32_t* part) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSidechainPart(trackId_, di, ci, slot, part) : false;
}
int32_t Engine::RackChain::setSidechainPart(int32_t slot, int32_t part) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSetSidechainPart(trackId_, di, ci, slot, part) : -1;
}
int32_t Engine::RackChain::setKey(int32_t slot, int32_t src, int32_t part, int32_t tapPre, float gainDb, float mix) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainSetKey(trackId_, di, ci, slot, src, part, tapPre, gainDb, mix)
                              : -1;
}

} // namespace nota
