// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "api/ApiImpl.h"

#include "devices/RackOps.h"

#include "nota/engine/Engine.h"
#include "nota/engine/RackChain.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>

namespace nota {

// ---- RackChain: lifecycle, mixer, zones, sampler & Mosaic ----------------

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
bool Engine::RackChain::grainSetSample(const std::string& path, int32_t rootNote) {
    std::shared_ptr<Engine::Impl> p;
    int32_t di = -1;
    int32_t ci = -1;
    return resolve(p, di, ci) ? p->runtime.racks().chainGrainSetSample(trackId_, di, ci, path, rootNote) : false;
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

} // namespace nota
