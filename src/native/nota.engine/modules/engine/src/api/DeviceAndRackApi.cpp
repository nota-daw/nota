// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "api/ApiImpl.h"

#include "automation/AutomationOps.h"
#include "devices/DeviceOps.h"
#include "tracks/TrackOps.h"

#include "nota/dsp/EditorPreview.h"
#include "nota/engine/Device.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Rack.h"
#include "nota/engine/Types.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota {

namespace {
constexpr int32_t presetSurface(engine::DeviceFamily family) noexcept {
    switch (family) {
    case engine::DeviceFamily::AudioEffect: return 0;
    case engine::DeviceFamily::Instrument: return 1;
    case engine::DeviceFamily::MidiEffect: return 2;
    }
    return 0;
}
} // namespace

// ---- Device ----------------------------------------------------------------

Engine::Device::Device(
    std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, engine::DeviceFamily family) noexcept
    : impl_(std::move(impl)), trackId_(trackId), index_(deviceIndex), family_(family) {
    if (auto p = impl_.lock()) {
        if (family_ == engine::DeviceFamily::AudioEffect && engine::isNestedRackAddress(index_)) {
            deviceId_ = detail::EngineApiAccess::rackIdentity(p->runtime, trackId_, index_);
        } else {
            deviceId_ = detail::EngineApiAccess::deviceIdentity(p->runtime, trackId_, index_, family_);
        }
    }
}

Engine::Device::Device(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex,
    engine::DeviceFamily family, uint64_t deviceId) noexcept
    : impl_(std::move(impl)), trackId_(trackId), index_(deviceIndex), family_(family), deviceId_(deviceId) {}

bool Engine::Device::resolve(std::shared_ptr<Engine::Impl>& outImpl, int32_t& outIndex) const noexcept {
    outImpl = impl_.lock();
    if (!outImpl) return false;
    if (family_ == engine::DeviceFamily::AudioEffect && engine::isNestedRackAddress(index_)) {
        return detail::EngineApiAccess::resolveRack(outImpl->runtime, trackId_, index_, deviceId_, outIndex);
    }
    return detail::EngineApiAccess::resolveDevice(outImpl->runtime, trackId_, family_, deviceId_, outIndex);
}

int32_t Engine::Device::index() const noexcept {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    return resolve(p, idx) ? idx : -1;
}

bool Engine::Device::isValid() const noexcept {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    return resolve(p, idx);
}

int32_t Engine::Device::kind() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return -1;
    switch (family_) {
    case engine::DeviceFamily::AudioEffect: return p->runtime.devices().trackDeviceBuiltinKind(trackId_, idx);
    case engine::DeviceFamily::MidiEffect: return p->runtime.devices().midiEffectKind(trackId_, idx);
    case engine::DeviceFamily::Instrument: return p->runtime.devices().trackInstrumentKind(trackId_);
    }
    return -1;
}
std::string Engine::Device::name() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return {};
    if (family_ == engine::DeviceFamily::AudioEffect) {
        const char* s = p->runtime.devices().deviceName(trackId_, idx);
        return s ? std::string(s) : std::string{};
    }
    if (family_ == engine::DeviceFamily::MidiEffect) {
        const char* s = p->runtime.devices().midiEffectName(trackId_, idx);
        return s ? std::string(s) : std::string{};
    }
    const char* s = p->runtime.devices().deviceName(trackId_, -1);
    return s ? std::string(s) : std::string{};
}
int64_t Engine::Device::deviceId() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return 0;
    if (family_ == engine::DeviceFamily::Instrument) return p->runtime.devices().trackInstrumentId(trackId_);
    if (family_ == engine::DeviceFamily::AudioEffect) return p->runtime.devices().trackDeviceId(trackId_, idx);
    return static_cast<int64_t>(deviceId_);
}
std::string Engine::Device::pluginId() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return {};
    return family_ == engine::DeviceFamily::Instrument ? p->runtime.devices().trackInstrumentPluginId(trackId_)
                                                       : p->runtime.devices().trackDevicePluginId(trackId_, idx);
}
bool Engine::Device::supportsMpe() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    return resolve(p, idx) && family_ == engine::DeviceFamily::Instrument &&
           p->runtime.devices().trackInstrumentSupportsMpe(trackId_);
}
int32_t Engine::Device::pluginLoadState(int32_t* reasonCode, std::string* reasonArg) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return 0;
    return p->runtime.devices().devicePluginLoadState(trackId_, idx, reasonCode, reasonArg);
}
bool Engine::Device::pluginLoadAction(int32_t action) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return false;
    return p->runtime.devices().devicePluginLoadAction(trackId_, idx, action);
}
int32_t Engine::Device::paramCount() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return 0;
    if (family_ == engine::DeviceFamily::MidiEffect) return p->runtime.devices().midiEffectParamCount(trackId_, idx);
    if (family_ == engine::DeviceFamily::AudioEffect) return p->runtime.devices().deviceParamCount(trackId_, idx);
    return p->runtime.automation().pluginParamCount(trackId_, -1);
}
Engine::Parameter Engine::Device::parameter(int32_t paramIndex) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    const int32_t resolvedIdx = resolve(p, idx) ? idx : -1;
    return Parameter(
        impl_, trackId_, resolvedIdx, paramIndex, family_, family_ == engine::DeviceFamily::Instrument, deviceId_);
}
int32_t Engine::Device::pluginParamCount() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return 0;
    return p->runtime.automation().pluginParamCount(trackId_, idx);
}
Engine::Parameter Engine::Device::pluginParameter(int32_t paramIndex) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    const bool valid = resolve(p, idx) && family_ != engine::DeviceFamily::MidiEffect;
    return Parameter(impl_, trackId_, valid ? idx : -1, paramIndex, family_, true, valid ? deviceId_ : 0);
}
int32_t Engine::Device::pluginParamIndexOfId(const std::string& id) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return -1;
    return p->runtime.automation().pluginParamIndexOfId(trackId_, idx, id);
}
int32_t Engine::Device::lastTouchedPluginParam() {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return -1;
    return p->runtime.automation().lastTouchedPluginParam(trackId_, idx);
}
void Engine::Device::setBypassed(bool bypassed) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return;
    if (family_ == engine::DeviceFamily::MidiEffect)
        p->runtime.devices().setMidiEffectBypassed(trackId_, idx, bypassed);
    else
        p->runtime.devices().setTrackDeviceBypassed(trackId_, idx, bypassed);
}
bool Engine::Device::bypassed() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return false;
    if (family_ == engine::DeviceFamily::MidiEffect) return p->runtime.devices().midiEffectBypassed(trackId_, idx);
    return p->runtime.devices().trackDeviceBypassed(trackId_, idx);
}
void Engine::Device::setFolded(bool folded) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return;
    if (family_ == engine::DeviceFamily::MidiEffect) p->runtime.devices().setMidiEffectFolded(trackId_, idx, folded);
    else
        p->runtime.devices().setTrackDeviceFolded(trackId_, idx, folded);
}
bool Engine::Device::folded() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return false;
    if (family_ == engine::DeviceFamily::MidiEffect) return p->runtime.devices().midiEffectFolded(trackId_, idx);
    return p->runtime.devices().trackDeviceFolded(trackId_, idx);
}
bool Engine::Device::setSleepMode(int32_t mode) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return false;
    return p->runtime.devices().setPluginSleepMode(trackId_, idx, mode);
}
int32_t Engine::Device::sleepMode() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return 0;
    return p->runtime.devices().pluginSleepMode(trackId_, idx);
}
void Engine::Device::openEditor() {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (resolve(p, idx) && family_ != engine::DeviceFamily::MidiEffect)
        p->runtime.devices().openTrackEditor(trackId_, idx);
}
void Engine::Device::closeEditor() {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (resolve(p, idx) && family_ != engine::DeviceFamily::MidiEffect)
        p->runtime.devices().closeTrackEditor(trackId_, idx);
}
bool Engine::Device::editorOpen() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return false;
    return p->runtime.devices().trackEditorOpen(trackId_, idx);
}
dsp::EditorPreview Engine::Device::editorPreview() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return {};
    return p->runtime.devices().trackEditorPreview(trackId_, idx);
}
bool Engine::Device::setEditorPreview(dsp::EditorPreview preview) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return false;
    return p->runtime.devices().setTrackEditorPreview(trackId_, idx, std::move(preview));
}
int32_t Engine::Device::getState(uint8_t* out, int32_t cap) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return 0;
    const int32_t pluginSize = p->runtime.devices().getPluginState(trackId_, idx, out, cap);
    if (pluginSize > 0 || family_ == engine::DeviceFamily::Instrument) return pluginSize;
    return p->runtime.devices().deviceGetState(trackId_, idx, out, cap);
}
int32_t Engine::Device::pluginGetState(uint8_t* out, int32_t cap) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return 0;
    return p->runtime.devices().getPluginState(trackId_, idx, out, cap);
}
std::vector<uint8_t> Engine::Device::midiGetState() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::MidiEffect) return {};
    return p->runtime.devices().midiEffectGetState(trackId_, idx);
}
bool Engine::Device::setState(const uint8_t* data, int32_t size, bool checkpoint) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return false;
    if (family_ == engine::DeviceFamily::MidiEffect)
        return p->runtime.devices().midiEffectSetState(trackId_, idx, data, size, checkpoint);
    if (p->runtime.devices().setPluginState(trackId_, idx, data, size)) return true;
    if (family_ == engine::DeviceFamily::Instrument) return false;
    p->runtime.devices().deviceSetState(trackId_, idx, data, size);
    return true;
}
bool Engine::Device::pluginSetState(const uint8_t* data, int32_t size) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return false;
    return p->runtime.devices().setPluginState(trackId_, idx, data, size);
}
bool Engine::Device::setInstrumentStateUndoable(int32_t chain, const uint8_t* data, int32_t size) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return false;
    return p->runtime.devices().instrumentSetStateUndoable(trackId_, idx, chain, data, size);
}
bool Engine::Device::recordInstrumentState(int32_t chain, const uint8_t* before, int32_t size) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return false;
    return p->runtime.devices().recordInstrumentState(trackId_, idx, chain, before, size);
}
float Engine::Device::gainReduction() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::AudioEffect) return 0.0f;
    return p->runtime.devices().deviceGainReduction(trackId_, idx);
}
int32_t Engine::Device::scope(float* out, int32_t maxSamples) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return 0;
    if (family_ == engine::DeviceFamily::Instrument)
        return p->runtime.tracks().instrumentScope(trackId_, out, maxSamples);
    if (family_ == engine::DeviceFamily::MidiEffect)
        return p->runtime.devices().midiEffectScope(trackId_, idx, out, maxSamples);
    return p->runtime.devices().deviceScope(trackId_, idx, out, maxSamples);
}
void Engine::Device::action(int32_t id, int32_t iarg, float farg) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return;
    if (family_ == engine::DeviceFamily::Instrument) p->runtime.devices().instrumentAction(trackId_, id, iarg, farg);
    else if (family_ == engine::DeviceFamily::AudioEffect)
        p->runtime.devices().deviceAction(trackId_, idx, id, iarg, farg);
}
void Engine::Device::midiCommand(int32_t cmd) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (resolve(p, idx) && family_ == engine::DeviceFamily::MidiEffect)
        p->runtime.devices().midiEffectCommand(trackId_, idx, cmd);
}
int32_t Engine::Device::midiLastIn() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::MidiEffect) return -1;
    return p->runtime.devices().midiEffectLastIn(trackId_, idx);
}
int32_t Engine::Device::midiLastOut() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::MidiEffect) return -1;
    return p->runtime.devices().midiEffectLastOut(trackId_, idx);
}
int32_t Engine::Device::layerWave(int32_t layer, float* out, int32_t maxSamples) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::AudioEffect) return 0;
    return p->runtime.devices().deviceLayerWave(trackId_, idx, layer, out, maxSamples);
}
bool Engine::Device::loadFile(const std::string& path) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::AudioEffect) return false;
    return p->runtime.devices().deviceLoadFile(trackId_, idx, path);
}
std::string Engine::Device::text(int32_t id) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::AudioEffect) return {};
    return p->runtime.devices().deviceText(trackId_, idx, id);
}
bool Engine::Device::setEqBandText(int32_t band, const std::string& label, const std::string& note) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::AudioEffect) return false;
    return p->runtime.devices().setEqBandText(trackId_, idx, band, label, note);
}
std::string Engine::Device::eqBandText(int32_t band, int32_t which) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::AudioEffect) return {};
    return p->runtime.devices().eqBandText(trackId_, idx, band, which);
}
int32_t Engine::Device::activeVoices() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::Instrument) return -1;
    return p->runtime.tracks().instrumentVoiceCount(trackId_);
}
int32_t Engine::Device::heldNotes(int32_t* out, int32_t maxCount) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::Instrument) return 0;
    return p->runtime.tracks().instrumentHeldNotes(trackId_, out, maxCount);
}
int32_t Engine::Device::instrumentOutput(float* out, int32_t maxCount) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::Instrument) return 0;
    return p->runtime.tracks().instrumentOutput(trackId_, out, maxCount);
}
int32_t Engine::Device::presetTag() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return -1;
    return p->runtime.devices().presetTagOf(
        trackId_, presetSurface(family_), family_ == engine::DeviceFamily::Instrument ? 0 : idx);
}
bool Engine::Device::setPresetTagIndex(int32_t tag) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return false;
    return p->runtime.devices().setPresetTagIndex(
        trackId_, presetSurface(family_), family_ == engine::DeviceFamily::Instrument ? 0 : idx, tag);
}
int32_t Engine::Device::setPresetTag(const char* id, const char* name, const float* base, int32_t count) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return -1;
    return p->runtime.devices().setPresetTag(
        trackId_, presetSurface(family_), family_ == engine::DeviceFamily::Instrument ? 0 : idx, id, name, base, count);
}
int32_t Engine::Device::presetModified() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx)) return -1;
    return p->runtime.devices().presetModified(
        trackId_, presetSurface(family_), family_ == engine::DeviceFamily::Instrument ? 0 : idx);
}
bool Engine::Device::acceptsSidechain() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return false;
    return family_ == engine::DeviceFamily::Instrument ? p->runtime.devices().instrumentAcceptsSidechain(trackId_)
                                                       : p->runtime.devices().deviceAcceptsSidechain(trackId_, idx);
}
void Engine::Device::setSidechainSource(int32_t sourceTrackId) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return;
    if (family_ == engine::DeviceFamily::Instrument)
        p->runtime.devices().setInstrumentSidechainSource(trackId_, sourceTrackId);
    else
        p->runtime.devices().setDeviceSidechainSource(trackId_, idx, sourceTrackId);
}
int32_t Engine::Device::sidechainSource() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return -1;
    return family_ == engine::DeviceFamily::Instrument ? p->runtime.devices().instrumentSidechainSource(trackId_)
                                                       : p->runtime.devices().deviceSidechainSource(trackId_, idx);
}
void Engine::Device::setSidechainGain(float db) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (resolve(p, idx) && family_ == engine::DeviceFamily::AudioEffect)
        p->runtime.devices().setDeviceSidechainGain(trackId_, idx, db);
}
float Engine::Device::sidechainGain() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::AudioEffect) return 0.0f;
    return p->runtime.devices().deviceSidechainGain(trackId_, idx);
}
void Engine::Device::setSidechainMix(float mix) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (resolve(p, idx) && family_ == engine::DeviceFamily::AudioEffect)
        p->runtime.devices().setDeviceSidechainMix(trackId_, idx, mix);
}
float Engine::Device::sidechainMix() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::AudioEffect) return 1.0f;
    return p->runtime.devices().deviceSidechainMix(trackId_, idx);
}
void Engine::Device::setSidechainTapPre(int32_t pre) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (resolve(p, idx) && family_ == engine::DeviceFamily::AudioEffect)
        p->runtime.devices().setDeviceSidechainTapPre(trackId_, idx, pre);
}
int32_t Engine::Device::sidechainTapPre() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::AudioEffect) return 0;
    return p->runtime.devices().deviceSidechainTapPre(trackId_, idx);
}
int32_t Engine::Device::setSidechainKey(int32_t sourceTrackId, int32_t part) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return -1;
    return family_ == engine::DeviceFamily::Instrument
               ? p->runtime.devices().setInstrumentSidechainKey(trackId_, sourceTrackId, part)
               : p->runtime.devices().setDeviceSidechainKey(trackId_, idx, sourceTrackId, part);
}
bool Engine::Device::sidechainKey(int32_t* source, int32_t* part) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ == engine::DeviceFamily::MidiEffect) return false;
    return family_ == engine::DeviceFamily::Instrument
               ? p->runtime.devices().instrumentSidechainKey(trackId_, source, part)
               : p->runtime.devices().deviceSidechainKey(trackId_, idx, source, part);
}
int32_t Engine::Device::partCount() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::Instrument) return 0;
    return p->runtime.devices().trackPartCount(trackId_);
}
std::string Engine::Device::partName(int32_t index) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::Instrument) return {};
    return p->runtime.devices().trackPartName(trackId_, index);
}
int32_t Engine::Device::partIndexOf(uint64_t partId) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::Instrument) return -1;
    return p->runtime.devices().partIndexOf(trackId_, partId);
}
bool Engine::Device::partIdAt(int32_t index, uint64_t* partId) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::Instrument) return false;
    return p->runtime.devices().partIdAt(trackId_, index, partId);
}
void Engine::Device::setCcDest(int32_t destDevice, int32_t destParam) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (resolve(p, idx) && family_ == engine::DeviceFamily::MidiEffect)
        p->runtime.devices().setMidiEffectCcDest(trackId_, idx, destDevice, destParam);
}
void Engine::Device::setCcDepth(float depth) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (resolve(p, idx) && family_ == engine::DeviceFamily::MidiEffect)
        p->runtime.devices().setMidiEffectCcDepth(trackId_, idx, depth);
}
int32_t Engine::Device::ccDestDevice() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::MidiEffect) return -2;
    return p->runtime.devices().midiEffectCcDestDevice(trackId_, idx);
}
int32_t Engine::Device::ccDestParam() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::MidiEffect) return -1;
    return p->runtime.devices().midiEffectCcDestParam(trackId_, idx);
}
float Engine::Device::ccDepth() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::MidiEffect) return 0.0f;
    return p->runtime.devices().midiEffectCcDepth(trackId_, idx);
}
bool Engine::Device::ccDestPath(int32_t* chainIndex, int32_t* slot) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::MidiEffect) return false;
    return p->runtime.devices().midiEffectCcDestPath(trackId_, idx, chainIndex, slot);
}
bool Engine::Device::setCcDestPath(int32_t chainIndex, int32_t slot) {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    if (!resolve(p, idx) || family_ != engine::DeviceFamily::MidiEffect) return false;
    return p->runtime.devices().setMidiEffectCcDestPath(trackId_, idx, chainIndex, slot);
}
Engine::Rack Engine::Device::asRack() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t idx = -1;
    const bool valid = resolve(p, idx);
    if (family_ == engine::DeviceFamily::Instrument) return Rack(impl_, trackId_, -1, valid ? deviceId_ : 0);
    if (family_ == engine::DeviceFamily::MidiEffect)
        return Rack(
            impl_, trackId_, valid ? engine::kMidiRackBase - idx : engine::kMidiRackBase, valid ? deviceId_ : 0);
    return Rack(impl_, trackId_, valid ? idx : -1, valid ? deviceId_ : 0);
}

// ---- Parameter -------------------------------------------------------------

Engine::Parameter::Parameter(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, int32_t paramIndex,
    engine::DeviceFamily family, bool pluginParam) noexcept
    : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex), paramIndex_(paramIndex), family_(family),
      plugin_(pluginParam) {
    if (auto p = impl_.lock()) {
        deviceId_ = detail::EngineApiAccess::deviceIdentity(
            p->runtime, trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : deviceIndex_, family_);
    }
}

Engine::Parameter::Parameter(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, int32_t paramIndex,
    engine::DeviceFamily family, bool pluginParam, uint64_t deviceId) noexcept
    : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex), paramIndex_(paramIndex), family_(family),
      plugin_(pluginParam), deviceId_(deviceId) {}

bool Engine::Parameter::resolve(std::shared_ptr<Engine::Impl>& outImpl, int32_t& outDevIdx) const noexcept {
    outImpl = impl_.lock();
    if (!outImpl || paramIndex_ < 0 ||
        !detail::EngineApiAccess::resolveDevice(outImpl->runtime, trackId_, family_, deviceId_, outDevIdx)) {
        return false;
    }
    if (plugin_ && family_ == engine::DeviceFamily::MidiEffect) return false;
    return true;
}

bool Engine::Parameter::isValid() const noexcept {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    if (!resolve(p, dev)) return false;
    if (plugin_) return paramIndex_ < p->runtime.automation().pluginParamCount(trackId_, dev);
    if (family_ == engine::DeviceFamily::MidiEffect)
        return paramIndex_ < p->runtime.devices().midiEffectParamCount(trackId_, dev);
    return paramIndex_ < p->runtime.devices().deviceParamCount(trackId_, dev);
}
float Engine::Parameter::get() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    if (!resolve(p, dev)) return 0.0f;
    if (plugin_) return p->runtime.automation().pluginParamGet(trackId_, dev, paramIndex_);
    if (family_ == engine::DeviceFamily::MidiEffect)
        return p->runtime.devices().midiEffectGetParam(trackId_, dev, paramIndex_);
    return p->runtime.devices().deviceGetParam(trackId_, dev, paramIndex_);
}
void Engine::Parameter::set(float value) {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    if (!resolve(p, dev)) return;
    if (plugin_) p->runtime.automation().pluginParamSet(trackId_, dev, paramIndex_, value);
    else if (family_ == engine::DeviceFamily::MidiEffect)
        p->runtime.devices().midiEffectSetParam(trackId_, dev, paramIndex_, value);
    else
        p->runtime.devices().deviceSetParam(trackId_, dev, paramIndex_, value);
}
float Engine::Parameter::min() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    if (!resolve(p, dev) || plugin_) return 0.0f;
    return family_ == engine::DeviceFamily::MidiEffect
               ? p->runtime.devices().midiEffectParamMin(trackId_, dev, paramIndex_)
               : p->runtime.devices().deviceParamMin(trackId_, dev, paramIndex_);
}
float Engine::Parameter::max() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    if (!resolve(p, dev) || plugin_) return 1.0f;
    return family_ == engine::DeviceFamily::MidiEffect
               ? p->runtime.devices().midiEffectParamMax(trackId_, dev, paramIndex_)
               : p->runtime.devices().deviceParamMax(trackId_, dev, paramIndex_);
}
float Engine::Parameter::defaultValue() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    if (!resolve(p, dev)) return 0.0f;
    if (plugin_) return p->runtime.automation().pluginParamDefault(trackId_, dev, paramIndex_);
    if (family_ == engine::DeviceFamily::MidiEffect)
        return p->runtime.devices().midiEffectParamDefault(trackId_, dev, paramIndex_);
    return p->runtime.devices().deviceParamDefault(trackId_, dev, paramIndex_);
}
std::string Engine::Parameter::id() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    if (!resolve(p, dev) || family_ == engine::DeviceFamily::MidiEffect) return {};
    return p->runtime.automation().pluginParamId(trackId_, dev, paramIndex_);
}
std::string Engine::Parameter::name() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    if (!resolve(p, dev)) return {};
    if (plugin_) return p->runtime.automation().pluginParamName(trackId_, dev, paramIndex_);
    const char* n = family_ == engine::DeviceFamily::MidiEffect
                        ? p->runtime.devices().midiEffectParamName(trackId_, dev, paramIndex_)
                        : p->runtime.devices().deviceParamName(trackId_, dev, paramIndex_);
    return n ? std::string(n) : std::string{};
}
std::string Engine::Parameter::text() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    return (resolve(p, dev) && plugin_) ? p->runtime.automation().pluginParamText(trackId_, dev, paramIndex_)
                                        : std::string{};
}
int32_t Engine::Parameter::steps() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    return (resolve(p, dev) && plugin_) ? p->runtime.automation().pluginParamSteps(trackId_, dev, paramIndex_) : 0;
}
std::string Engine::Parameter::stepText(int32_t step) const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    return (resolve(p, dev) && plugin_) ? p->runtime.automation().pluginParamStepText(trackId_, dev, paramIndex_, step)
                                        : std::string{};
}
int32_t Engine::Parameter::indexFromV1() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    if (!resolve(p, dev) || family_ == engine::DeviceFamily::MidiEffect) return paramIndex_;
    return p->runtime.automation().pluginParamIndexFromV1(trackId_, dev, paramIndex_);
}
bool Engine::Parameter::isModulated() const {
    std::shared_ptr<Engine::Impl> p;
    int32_t dev = -1;
    if (!resolve(p, dev) || family_ != engine::DeviceFamily::AudioEffect) return false;
    return p->runtime.automation().deviceParamModulated(trackId_, dev, paramIndex_);
}

} // namespace nota
