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

bool Engine::Device::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasDevice(p->runtime, trackId_, index_, family_);
}
int32_t Engine::Device::kind() const {
    auto p = impl_.lock();
    if (!p) return -1;
    switch (family_) {
    case engine::DeviceFamily::AudioEffect: return p->runtime.devices().trackDeviceBuiltinKind(trackId_, index_);
    case engine::DeviceFamily::MidiEffect: return p->runtime.devices().midiEffectKind(trackId_, index_);
    case engine::DeviceFamily::Instrument: return p->runtime.devices().trackInstrumentKind(trackId_);
    }
    return -1;
}
std::string Engine::Device::name() const {
    auto p = impl_.lock();
    if (!p) return {};
    if (family_ == engine::DeviceFamily::AudioEffect) {
        const char* s = p->runtime.devices().deviceName(trackId_, index_);
        return s ? std::string(s) : std::string{};
    }
    if (family_ == engine::DeviceFamily::MidiEffect) {
        const char* s = p->runtime.devices().midiEffectName(trackId_, index_);
        return s ? std::string(s) : std::string{};
    }
    const char* s = p->runtime.devices().deviceName(trackId_, -1);
    return s ? std::string(s) : std::string{};
}
int64_t Engine::Device::deviceId() const {
    auto p = impl_.lock();
    if (!p) return 0;
    if (family_ == engine::DeviceFamily::Instrument) return p->runtime.devices().trackInstrumentId(trackId_);
    if (family_ == engine::DeviceFamily::AudioEffect) return p->runtime.devices().trackDeviceId(trackId_, index_);
    return 0;
}
std::string Engine::Device::pluginId() const {
    auto p = impl_.lock();
    if (!p) return {};
    return family_ == engine::DeviceFamily::Instrument ? p->runtime.devices().trackInstrumentPluginId(trackId_)
                                                       : p->runtime.devices().trackDevicePluginId(trackId_, index_);
}
bool Engine::Device::supportsMpe() const {
    auto p = impl_.lock();
    return p && family_ == engine::DeviceFamily::Instrument ? p->runtime.devices().trackInstrumentSupportsMpe(trackId_)
                                                            : false;
}
int32_t Engine::Device::pluginLoadState(int32_t* reasonCode, std::string* reasonArg) const {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.devices().devicePluginLoadState(trackId_, dev, reasonCode, reasonArg) : -1;
}
bool Engine::Device::pluginLoadAction(int32_t action) {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.devices().devicePluginLoadAction(trackId_, dev, action) : false;
}
int32_t Engine::Device::paramCount() const {
    auto p = impl_.lock();
    if (!p) return 0;
    if (family_ == engine::DeviceFamily::MidiEffect) return p->runtime.devices().midiEffectParamCount(trackId_, index_);
    if (family_ == engine::DeviceFamily::AudioEffect) return p->runtime.devices().deviceParamCount(trackId_, index_);
    return p->runtime.automation().pluginParamCount(trackId_, -1);
}
Engine::Parameter Engine::Device::parameter(int32_t paramIndex) const {
    return Parameter(impl_, trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_, paramIndex, family_,
        family_ == engine::DeviceFamily::Instrument);
}
int32_t Engine::Device::pluginParamCount() const {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.automation().pluginParamCount(trackId_, dev) : 0;
}
Engine::Parameter Engine::Device::pluginParameter(int32_t paramIndex) const {
    return Parameter(
        impl_, trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_, paramIndex, family_, true);
}
int32_t Engine::Device::pluginParamIndexOfId(const std::string& id) const {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.automation().pluginParamIndexOfId(trackId_, dev, id) : -1;
}
int32_t Engine::Device::lastTouchedPluginParam() {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.automation().lastTouchedPluginParam(trackId_, dev) : -1;
}
void Engine::Device::setBypassed(bool bypassed) {
    auto p = impl_.lock();
    if (!p) return;
    if (family_ == engine::DeviceFamily::MidiEffect)
        p->runtime.devices().setMidiEffectBypassed(trackId_, index_, bypassed);
    else
        p->runtime.devices().setTrackDeviceBypassed(
            trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_, bypassed);
}
bool Engine::Device::bypassed() const {
    auto p = impl_.lock();
    if (!p) return false;
    if (family_ == engine::DeviceFamily::MidiEffect) return p->runtime.devices().midiEffectBypassed(trackId_, index_);
    return p->runtime.devices().trackDeviceBypassed(
        trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_);
}
void Engine::Device::setFolded(bool folded) {
    auto p = impl_.lock();
    if (!p) return;
    if (family_ == engine::DeviceFamily::MidiEffect) p->runtime.devices().setMidiEffectFolded(trackId_, index_, folded);
    else
        p->runtime.devices().setTrackDeviceFolded(
            trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_, folded);
}
bool Engine::Device::folded() const {
    auto p = impl_.lock();
    if (!p) return false;
    if (family_ == engine::DeviceFamily::MidiEffect) return p->runtime.devices().midiEffectFolded(trackId_, index_);
    return p->runtime.devices().trackDeviceFolded(trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_);
}
bool Engine::Device::setSleepMode(int32_t mode) {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.devices().setPluginSleepMode(trackId_, dev, mode) : false;
}
int32_t Engine::Device::sleepMode() const {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.devices().pluginSleepMode(trackId_, dev) : 0;
}
void Engine::Device::openEditor() {
    if (auto p = impl_.lock())
        p->runtime.devices().openTrackEditor(trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_);
}
void Engine::Device::closeEditor() {
    if (auto p = impl_.lock())
        p->runtime.devices().closeTrackEditor(trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_);
}
bool Engine::Device::editorOpen() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().trackEditorOpen(trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_)
             : false;
}
dsp::EditorPreview Engine::Device::editorPreview() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().trackEditorPreview(
                   trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_)
             : dsp::EditorPreview{};
}
bool Engine::Device::setEditorPreview(dsp::EditorPreview preview) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setTrackEditorPreview(
                   trackId_, family_ == engine::DeviceFamily::Instrument ? -1 : index_, std::move(preview))
             : false;
}
int32_t Engine::Device::getState(uint8_t* out, int32_t cap) const {
    auto p = impl_.lock();
    if (!p) return 0;
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    const int32_t pluginSize = p->runtime.devices().getPluginState(trackId_, dev, out, cap);
    if (pluginSize > 0 || family_ == engine::DeviceFamily::Instrument) return pluginSize;
    return p->runtime.devices().deviceGetState(trackId_, index_, out, cap);
}
int32_t Engine::Device::pluginGetState(uint8_t* out, int32_t cap) const {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.devices().getPluginState(trackId_, dev, out, cap) : 0;
}
std::vector<uint8_t> Engine::Device::midiGetState() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().midiEffectGetState(trackId_, index_) : std::vector<uint8_t>{};
}
bool Engine::Device::setState(const uint8_t* data, int32_t size, bool checkpoint) {
    auto p = impl_.lock();
    if (!p) return false;
    if (family_ == engine::DeviceFamily::MidiEffect)
        return p->runtime.devices().midiEffectSetState(trackId_, index_, data, size, checkpoint);
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    if (p->runtime.devices().setPluginState(trackId_, dev, data, size)) return true;
    p->runtime.devices().deviceSetState(trackId_, index_, data, size);
    return true;
}
bool Engine::Device::pluginSetState(const uint8_t* data, int32_t size) {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.devices().setPluginState(trackId_, dev, data, size) : false;
}
bool Engine::Device::setInstrumentStateUndoable(int32_t chain, const uint8_t* data, int32_t size) {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.devices().instrumentSetStateUndoable(trackId_, dev, chain, data, size) : false;
}
bool Engine::Device::recordInstrumentState(int32_t chain, const uint8_t* before, int32_t size) {
    auto p = impl_.lock();
    const int32_t dev = family_ == engine::DeviceFamily::Instrument ? -1 : index_;
    return p ? p->runtime.devices().recordInstrumentState(trackId_, dev, chain, before, size) : false;
}
float Engine::Device::gainReduction() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().deviceGainReduction(trackId_, index_) : 0.0f;
}
int32_t Engine::Device::scope(float* out, int32_t maxSamples) const {
    auto p = impl_.lock();
    if (!p) return 0;
    if (family_ == engine::DeviceFamily::Instrument)
        return p->runtime.tracks().instrumentScope(trackId_, out, maxSamples);
    if (family_ == engine::DeviceFamily::MidiEffect)
        return p->runtime.devices().midiEffectScope(trackId_, index_, out, maxSamples);
    return p->runtime.devices().deviceScope(trackId_, index_, out, maxSamples);
}
void Engine::Device::action(int32_t id, int32_t iarg, float farg) {
    auto p = impl_.lock();
    if (!p) return;
    if (family_ == engine::DeviceFamily::Instrument) p->runtime.devices().instrumentAction(trackId_, id, iarg, farg);
    else
        p->runtime.devices().deviceAction(trackId_, index_, id, iarg, farg);
}
void Engine::Device::midiCommand(int32_t cmd) {
    if (auto p = impl_.lock()) p->runtime.devices().midiEffectCommand(trackId_, index_, cmd);
}
int32_t Engine::Device::midiLastIn() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().midiEffectLastIn(trackId_, index_) : -1;
}
int32_t Engine::Device::midiLastOut() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().midiEffectLastOut(trackId_, index_) : -1;
}
int32_t Engine::Device::layerWave(int32_t layer, float* out, int32_t maxSamples) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().deviceLayerWave(trackId_, index_, layer, out, maxSamples) : 0;
}
bool Engine::Device::loadFile(const std::string& path) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().deviceLoadFile(trackId_, index_, path) : false;
}
std::string Engine::Device::text(int32_t id) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().deviceText(trackId_, index_, id) : std::string{};
}
bool Engine::Device::setEqBandText(int32_t band, const std::string& label, const std::string& note) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setEqBandText(trackId_, index_, band, label, note) : false;
}
std::string Engine::Device::eqBandText(int32_t band, int32_t which) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().eqBandText(trackId_, index_, band, which) : std::string{};
}
int32_t Engine::Device::activeVoices() const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().instrumentVoiceCount(trackId_) : -1;
}
int32_t Engine::Device::heldNotes(int32_t* out, int32_t maxCount) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().instrumentHeldNotes(trackId_, out, maxCount) : 0;
}
int32_t Engine::Device::instrumentOutput(float* out, int32_t maxCount) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().instrumentOutput(trackId_, out, maxCount) : 0;
}
int32_t Engine::Device::presetTag() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().presetTagOf(trackId_, presetSurface(family_), index_) : -1;
}
bool Engine::Device::setPresetTagIndex(int32_t tag) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setPresetTagIndex(trackId_, presetSurface(family_), index_, tag) : false;
}
int32_t Engine::Device::setPresetTag(const char* id, const char* name, const float* base, int32_t count) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setPresetTag(trackId_, presetSurface(family_), index_, id, name, base, count) : -1;
}
int32_t Engine::Device::presetModified() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().presetModified(trackId_, presetSurface(family_), index_) : -1;
}
bool Engine::Device::acceptsSidechain() const {
    auto p = impl_.lock();
    if (!p) return false;
    return family_ == engine::DeviceFamily::Instrument ? p->runtime.devices().instrumentAcceptsSidechain(trackId_)
                                                       : p->runtime.devices().deviceAcceptsSidechain(trackId_, index_);
}
void Engine::Device::setSidechainSource(int32_t sourceTrackId) {
    auto p = impl_.lock();
    if (!p) return;
    if (family_ == engine::DeviceFamily::Instrument)
        p->runtime.devices().setInstrumentSidechainSource(trackId_, sourceTrackId);
    else
        p->runtime.devices().setDeviceSidechainSource(trackId_, index_, sourceTrackId);
}
int32_t Engine::Device::sidechainSource() const {
    auto p = impl_.lock();
    if (!p) return -1;
    return family_ == engine::DeviceFamily::Instrument ? p->runtime.devices().instrumentSidechainSource(trackId_)
                                                       : p->runtime.devices().deviceSidechainSource(trackId_, index_);
}
void Engine::Device::setSidechainGain(float db) {
    if (auto p = impl_.lock()) p->runtime.devices().setDeviceSidechainGain(trackId_, index_, db);
}
float Engine::Device::sidechainGain() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().deviceSidechainGain(trackId_, index_) : 0.0f;
}
void Engine::Device::setSidechainMix(float mix) {
    if (auto p = impl_.lock()) p->runtime.devices().setDeviceSidechainMix(trackId_, index_, mix);
}
float Engine::Device::sidechainMix() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().deviceSidechainMix(trackId_, index_) : 1.0f;
}
void Engine::Device::setSidechainTapPre(int32_t pre) {
    if (auto p = impl_.lock()) p->runtime.devices().setDeviceSidechainTapPre(trackId_, index_, pre);
}
int32_t Engine::Device::sidechainTapPre() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().deviceSidechainTapPre(trackId_, index_) : 0;
}
int32_t Engine::Device::setSidechainKey(int32_t sourceTrackId, int32_t part) {
    auto p = impl_.lock();
    if (!p) return -1;
    return family_ == engine::DeviceFamily::Instrument
               ? p->runtime.devices().setInstrumentSidechainKey(trackId_, sourceTrackId, part)
               : p->runtime.devices().setDeviceSidechainKey(trackId_, index_, sourceTrackId, part);
}
bool Engine::Device::sidechainKey(int32_t* source, int32_t* part) const {
    auto p = impl_.lock();
    if (!p) return false;
    return family_ == engine::DeviceFamily::Instrument
               ? p->runtime.devices().instrumentSidechainKey(trackId_, source, part)
               : p->runtime.devices().deviceSidechainKey(trackId_, index_, source, part);
}
int32_t Engine::Device::partCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().trackPartCount(trackId_) : 0;
}
std::string Engine::Device::partName(int32_t index) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().trackPartName(trackId_, index) : std::string{};
}
int32_t Engine::Device::partIndexOf(uint64_t partId) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().partIndexOf(trackId_, partId) : -1;
}
bool Engine::Device::partIdAt(int32_t index, uint64_t* partId) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().partIdAt(trackId_, index, partId) : false;
}
void Engine::Device::setCcDest(int32_t destDevice, int32_t destParam) {
    if (auto p = impl_.lock()) p->runtime.devices().setMidiEffectCcDest(trackId_, index_, destDevice, destParam);
}
void Engine::Device::setCcDepth(float depth) {
    if (auto p = impl_.lock()) p->runtime.devices().setMidiEffectCcDepth(trackId_, index_, depth);
}
int32_t Engine::Device::ccDestDevice() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().midiEffectCcDestDevice(trackId_, index_) : -1;
}
int32_t Engine::Device::ccDestParam() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().midiEffectCcDestParam(trackId_, index_) : -1;
}
float Engine::Device::ccDepth() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().midiEffectCcDepth(trackId_, index_) : 0.0f;
}
bool Engine::Device::ccDestPath(int32_t* chainIndex, int32_t* slot) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().midiEffectCcDestPath(trackId_, index_, chainIndex, slot) : false;
}
bool Engine::Device::setCcDestPath(int32_t chainIndex, int32_t slot) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setMidiEffectCcDestPath(trackId_, index_, chainIndex, slot) : false;
}
Engine::Rack Engine::Device::asRack() const {
    if (family_ == engine::DeviceFamily::Instrument) return Rack(impl_, trackId_, -1);
    if (family_ == engine::DeviceFamily::MidiEffect) return Rack(impl_, trackId_, engine::kMidiRackBase - index_);
    return Rack(impl_, trackId_, index_);
}

// ---- Parameter -------------------------------------------------------------

bool Engine::Parameter::isValid() const noexcept {
    auto p = impl_.lock();
    if (!p || paramIndex_ < 0) return false;
    if (plugin_) return paramIndex_ < p->runtime.automation().pluginParamCount(trackId_, deviceIndex_);
    if (family_ == engine::DeviceFamily::MidiEffect)
        return paramIndex_ < p->runtime.devices().midiEffectParamCount(trackId_, deviceIndex_);
    return paramIndex_ < p->runtime.devices().deviceParamCount(trackId_, deviceIndex_);
}
float Engine::Parameter::get() const {
    auto p = impl_.lock();
    if (!p) return 0.0f;
    if (plugin_) return p->runtime.automation().pluginParamGet(trackId_, deviceIndex_, paramIndex_);
    if (family_ == engine::DeviceFamily::MidiEffect)
        return p->runtime.devices().midiEffectGetParam(trackId_, deviceIndex_, paramIndex_);
    return p->runtime.devices().deviceGetParam(trackId_, deviceIndex_, paramIndex_);
}
void Engine::Parameter::set(float value) {
    auto p = impl_.lock();
    if (!p) return;
    if (plugin_) p->runtime.automation().pluginParamSet(trackId_, deviceIndex_, paramIndex_, value);
    else if (family_ == engine::DeviceFamily::MidiEffect)
        p->runtime.devices().midiEffectSetParam(trackId_, deviceIndex_, paramIndex_, value);
    else
        p->runtime.devices().deviceSetParam(trackId_, deviceIndex_, paramIndex_, value);
}
float Engine::Parameter::min() const {
    auto p = impl_.lock();
    if (!p || plugin_) return 0.0f;
    return family_ == engine::DeviceFamily::MidiEffect
               ? p->runtime.devices().midiEffectParamMin(trackId_, deviceIndex_, paramIndex_)
               : p->runtime.devices().deviceParamMin(trackId_, deviceIndex_, paramIndex_);
}
float Engine::Parameter::max() const {
    auto p = impl_.lock();
    if (!p || plugin_) return 1.0f;
    return family_ == engine::DeviceFamily::MidiEffect
               ? p->runtime.devices().midiEffectParamMax(trackId_, deviceIndex_, paramIndex_)
               : p->runtime.devices().deviceParamMax(trackId_, deviceIndex_, paramIndex_);
}
float Engine::Parameter::defaultValue() const {
    auto p = impl_.lock();
    if (!p) return 0.0f;
    if (plugin_) return p->runtime.automation().pluginParamDefault(trackId_, deviceIndex_, paramIndex_);
    if (family_ == engine::DeviceFamily::MidiEffect)
        return p->runtime.devices().midiEffectParamDefault(trackId_, deviceIndex_, paramIndex_);
    return p->runtime.devices().deviceParamDefault(trackId_, deviceIndex_, paramIndex_);
}
std::string Engine::Parameter::id() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().pluginParamId(trackId_, deviceIndex_, paramIndex_) : std::string{};
}
std::string Engine::Parameter::name() const {
    auto p = impl_.lock();
    if (!p) return {};
    if (plugin_) return p->runtime.automation().pluginParamName(trackId_, deviceIndex_, paramIndex_);
    const char* n = family_ == engine::DeviceFamily::MidiEffect
                        ? p->runtime.devices().midiEffectParamName(trackId_, deviceIndex_, paramIndex_)
                        : p->runtime.devices().deviceParamName(trackId_, deviceIndex_, paramIndex_);
    return n ? std::string(n) : std::string{};
}
std::string Engine::Parameter::text() const {
    auto p = impl_.lock();
    return p && plugin_ ? p->runtime.automation().pluginParamText(trackId_, deviceIndex_, paramIndex_) : std::string{};
}
int32_t Engine::Parameter::steps() const {
    auto p = impl_.lock();
    return p && plugin_ ? p->runtime.automation().pluginParamSteps(trackId_, deviceIndex_, paramIndex_) : 0;
}
std::string Engine::Parameter::stepText(int32_t step) const {
    auto p = impl_.lock();
    return p && plugin_ ? p->runtime.automation().pluginParamStepText(trackId_, deviceIndex_, paramIndex_, step)
                        : std::string{};
}
int32_t Engine::Parameter::indexFromV1() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().pluginParamIndexFromV1(trackId_, deviceIndex_, paramIndex_) : paramIndex_;
}
bool Engine::Parameter::isModulated() const {
    auto p = impl_.lock();
    return p ? p->runtime.automation().deviceParamModulated(trackId_, deviceIndex_, paramIndex_) : false;
}

} // namespace nota
