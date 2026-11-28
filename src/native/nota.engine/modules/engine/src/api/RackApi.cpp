// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "api/ApiImpl.h"

#include "devices/RackOps.h"

#include "nota/devices/racks/RackCore.h"
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

} // namespace nota
