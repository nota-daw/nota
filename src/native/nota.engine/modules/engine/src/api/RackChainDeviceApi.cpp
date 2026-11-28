// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "api/ApiImpl.h"

#include "devices/RackOps.h"

#include "nota/devices/racks/RackCore.h"
#include "nota/dsp/Device.h"
#include "nota/engine/Engine.h"
#include "nota/engine/RackChain.h"

#include <cstdint>
#include <memory>
#include <string>

namespace nota {

// ---- RackChain: instrument & chain effects -------------------------------

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

} // namespace nota
