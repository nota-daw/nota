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

// ---- DeviceChain -----------------------------------------------------------

bool Engine::DeviceChain::isValid() const noexcept {
    auto p = impl_.lock();
    return p && detail::EngineApiAccess::hasTrack(p->runtime, trackId_);
}
int32_t Engine::DeviceChain::count() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().trackDeviceCount(trackId_) : 0;
}
Engine::Device Engine::DeviceChain::device(int32_t deviceIndex) const {
    return Device(impl_, trackId_, deviceIndex, engine::DeviceFamily::AudioEffect);
}
Engine::Device Engine::DeviceChain::instrument() const {
    return Device(impl_, trackId_, -1, engine::DeviceFamily::Instrument);
}
Engine::Device Engine::DeviceChain::midiEffect(int32_t index) const {
    return Device(impl_, trackId_, index, engine::DeviceFamily::MidiEffect);
}
int32_t Engine::DeviceChain::add(std::string_view slug) {
    const int32_t kind = devices::audioEffectKindFromSlug(slug);
    return kind >= 0 ? addBuiltin(kind) : -1;
}
int32_t Engine::DeviceChain::addBuiltin(int32_t kind) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().addTrackBuiltinDevice(trackId_, kind) : -1;
}
int32_t Engine::DeviceChain::addPlugin(int32_t catalogIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().addTrackEffectPlugin(trackId_, catalogIndex) : -1;
}
float Engine::DeviceChain::builtinParamDefault(int32_t kind, int32_t paramIndex) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().builtinDeviceParamDefault(kind, paramIndex) : 0.0f;
}
bool Engine::DeviceChain::move(int32_t fromIndex, int32_t toIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().moveDevice(trackId_, fromIndex, toIndex) : false;
}
bool Engine::DeviceChain::remove(int32_t deviceIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().removeDevice(trackId_, deviceIndex) : false;
}
bool Engine::DeviceChain::copy(int32_t deviceIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().copyDevice(trackId_, deviceIndex) : false;
}
int32_t Engine::DeviceChain::paste(int32_t atIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().pasteDevice(trackId_, atIndex) : -1;
}
int32_t Engine::DeviceChain::duplicate(int32_t deviceIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().duplicateDevice(trackId_, deviceIndex) : -1;
}
bool Engine::DeviceChain::setBuiltinInstrument(int32_t kind) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setTrackBuiltinInstrument(trackId_, kind) : false;
}
bool Engine::DeviceChain::setInstrument(std::string_view slug) {
    const int32_t kind = devices::instrumentKindFromSlug(slug);
    return kind >= 0 ? setBuiltinInstrument(kind) : false;
}
bool Engine::DeviceChain::setPluginInstrument(int32_t catalogIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setTrackInstrumentPlugin(trackId_, catalogIndex) : false;
}
bool Engine::DeviceChain::copyInstrument() {
    auto p = impl_.lock();
    return p ? p->runtime.devices().copyInstrument(trackId_) : false;
}
int32_t Engine::DeviceChain::pasteInstrument() {
    auto p = impl_.lock();
    return p ? p->runtime.devices().pasteInstrument(trackId_) : -1;
}
int32_t Engine::DeviceChain::midiEffectCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().trackMidiEffectCount(trackId_) : 0;
}
int32_t Engine::DeviceChain::addMidiEffect(std::string_view slug) {
    const int32_t kind = devices::midiEffectKindFromSlug(slug);
    return kind >= 0 ? addBuiltinMidiEffect(kind) : -1;
}
int32_t Engine::DeviceChain::addBuiltinMidiEffect(int32_t kind) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().addTrackMidiEffect(trackId_, kind) : -1;
}
bool Engine::DeviceChain::moveMidiEffect(int32_t fromIndex, int32_t toIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().moveMidiEffect(trackId_, fromIndex, toIndex) : false;
}
bool Engine::DeviceChain::removeMidiEffect(int32_t index) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().removeMidiEffect(trackId_, index) : false;
}
bool Engine::DeviceChain::copyMidiEffect(int32_t index) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().copyMidiEffect(trackId_, index) : false;
}
int32_t Engine::DeviceChain::pasteMidiEffect(int32_t atIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().pasteMidiEffect(trackId_, atIndex) : -1;
}
int32_t Engine::DeviceChain::duplicateMidiEffect(int32_t index) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().duplicateMidiEffect(trackId_, index) : -1;
}
int32_t Engine::DeviceChain::copyDevices(
    const int32_t* midiIdx, int32_t midiCount, bool withInstrument, const int32_t* fxIdx, int32_t fxCount) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().copyDevices(trackId_, midiIdx, midiCount, withInstrument, fxIdx, fxCount) : -1;
}
int32_t Engine::DeviceChain::pasteDevices(int32_t midiAt, int32_t fxAt, int32_t* outSkipped) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().pasteDevices(trackId_, midiAt, fxAt, outSkipped) : -1;
}
int32_t Engine::DeviceChain::clipboardKind() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().deviceClipboardKind() : 0;
}
int32_t Engine::DeviceChain::clipboardCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().deviceClipboardCount() : 0;
}
bool Engine::DeviceChain::clipboardHasSidechain() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().deviceClipboardSidechain() : false;
}
int32_t Engine::DeviceChain::transferDevice(engine::DeviceFamily family, int32_t index, int32_t dstTrackId,
    int32_t atIndex, bool keepSource, int32_t* outFlags) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().transferDevice(
                   trackId_, static_cast<int32_t>(family), index, dstTrackId, atIndex, keepSource, outFlags)
             : -1;
}
int32_t Engine::DeviceChain::groupDevices(
    int32_t midiFirst, int32_t midiCount, bool withInstrument, int32_t fxFirst, int32_t fxCount) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().groupDevices(trackId_, midiFirst, midiCount, withInstrument, fxFirst, fxCount) : -1;
}
int32_t Engine::DeviceChain::ungroupRack(engine::DeviceFamily family, int32_t index, int32_t* outDropped) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().ungroupRack(trackId_, static_cast<int32_t>(family), index, outDropped) : -1;
}
int32_t Engine::DeviceChain::wrapInRack(int32_t deviceIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().wrapDeviceInRack(trackId_, deviceIndex) : -1;
}
int32_t Engine::DeviceChain::unwrapRack(int32_t deviceIndex) {
    auto p = impl_.lock();
    return p ? p->runtime.racks().unwrapRack(trackId_, deviceIndex) : -1;
}
bool Engine::DeviceChain::setGrainSample(const std::string& path, int32_t rootNote) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setTrackGrainSample(trackId_, path, rootNote) : false;
}
bool Engine::DeviceChain::setGrainRoot(int32_t rootNote) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setTrackGrainRoot(trackId_, rootNote) : false;
}
bool Engine::DeviceChain::setSamplerSample(const std::string& path, int32_t rootNote) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setTrackSamplerSample(trackId_, path, rootNote) : false;
}
bool Engine::DeviceChain::setSamplerRoot(int32_t rootNote) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setTrackSamplerRoot(trackId_, rootNote) : false;
}
float Engine::DeviceChain::samplerPlayPosition() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().samplerPlayPosition(trackId_) : -1.0f;
}
bool Engine::DeviceChain::samplerInfo(engine::SamplerInfo& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().samplerInfo(trackId_, &out) : false;
}
bool Engine::DeviceChain::grainInfo(engine::SamplerInfo& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().grainInfo(trackId_, &out) : false;
}
int32_t Engine::DeviceChain::grainPlayPositions(float* out, int32_t maxCount) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().grainPlayPositions(trackId_, out, maxCount) : 0;
}
bool Engine::DeviceChain::mosaicSetProgram(const std::string& text, bool checkpoint) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().mosaicSetProgram(trackId_, text, checkpoint) : false;
}
std::string Engine::DeviceChain::mosaicProgram() const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().mosaicProgram(trackId_) : std::string{};
}
bool Engine::DeviceChain::mosaicStatus(engine::MosaicStatus& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().mosaicStatus(trackId_, &out) : false;
}
int64_t Engine::DeviceChain::mosaicZoneSampleId(int32_t zone) const {
    auto p = impl_.lock();
    return p ? p->runtime.devices().mosaicZoneSampleId(trackId_, zone) : 0;
}
bool Engine::DeviceChain::auroraSetTable(
    int32_t osc, std::shared_ptr<const dsp::WavetableAsset> table, bool checkpoint) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().auroraSetTable(trackId_, osc, std::move(table), checkpoint) : false;
}
bool Engine::DeviceChain::auroraClearTable(int32_t osc) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().auroraClearTable(trackId_, osc) : false;
}
bool Engine::DeviceChain::auroraSetSource(int32_t osc, bool custom, bool checkpoint) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().auroraSetSource(trackId_, osc, custom, checkpoint) : false;
}
bool Engine::DeviceChain::auroraSetMissing(int32_t osc, const std::string& name) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().auroraSetMissing(trackId_, osc, name) : false;
}
std::shared_ptr<const dsp::WavetableAsset> Engine::DeviceChain::auroraTable(int32_t osc) const {
    auto p = impl_.lock();
    if (!p) return nullptr;
    auto a = p->runtime.devices().auroraOf(trackId_);
    return a ? a->wavetable(osc) : nullptr;
}
bool Engine::DeviceChain::auroraUsesCustom(int32_t osc) const {
    auto p = impl_.lock();
    if (!p) return false;
    auto a = p->runtime.devices().auroraOf(trackId_);
    return a ? a->usesCustomWavetable(osc) : false;
}
std::string Engine::DeviceChain::auroraMissingName(int32_t osc) const {
    auto p = impl_.lock();
    if (!p) return {};
    auto a = p->runtime.devices().auroraOf(trackId_);
    return a ? a->missingWavetableName(osc) : std::string{};
}
bool Engine::DeviceChain::hasAurora() const {
    auto p = impl_.lock();
    return p && p->runtime.devices().auroraOf(trackId_) != nullptr;
}
std::vector<uint8_t> Engine::DeviceChain::auroraState(bool includeTables) const {
    auto p = impl_.lock();
    if (!p) return {};
    auto a = p->runtime.devices().auroraOf(trackId_);
    return a ? a->serializeState(includeTables) : std::vector<uint8_t>{};
}
bool Engine::DeviceChain::setRhythmVoiceSample(int32_t voice, const std::string& path) {
    auto p = impl_.lock();
    return p ? p->runtime.devices().setRhythmVoiceSample(trackId_, voice, path) : false;
}
bool Engine::DeviceChain::rhythmVoiceInfo(int32_t voice, engine::SamplerInfo& out) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmVoiceInfo(trackId_, voice, &out) : false;
}
int32_t Engine::DeviceChain::rhythmVoiceSource(int32_t voice) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmVoiceSource(trackId_, voice) : -1;
}
int32_t Engine::DeviceChain::rhythmVoiceDeviceCount(int32_t voice) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmVoiceDeviceCount(trackId_, voice) : 0;
}
int32_t Engine::DeviceChain::rhythmAddVoiceDevice(int32_t voice, int32_t kind) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmAddVoiceDevice(trackId_, voice, kind) : -1;
}
bool Engine::DeviceChain::rhythmRemoveVoiceDevice(int32_t voice, int32_t dev) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmRemoveVoiceDevice(trackId_, voice, dev) : false;
}
bool Engine::DeviceChain::rhythmMoveVoiceDevice(int32_t voice, int32_t from, int32_t to) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmMoveVoiceDevice(trackId_, voice, from, to) : false;
}
std::string Engine::DeviceChain::rhythmVoiceDeviceName(int32_t voice, int32_t dev) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return (d && d->displayName()) ? std::string(d->displayName()) : std::string{};
}
int32_t Engine::DeviceChain::rhythmVoiceDeviceBuiltinKind(int32_t voice, int32_t dev) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return d ? d->builtinKind() : -1;
}
int32_t Engine::DeviceChain::rhythmVoiceDeviceParamCount(int32_t voice, int32_t dev) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return d ? d->paramCount() : 0;
}
std::string Engine::DeviceChain::rhythmVoiceDeviceParamName(int32_t voice, int32_t dev, int32_t param) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    const char* n = d ? d->paramName(param) : nullptr;
    return n ? std::string(n) : std::string{};
}
float Engine::DeviceChain::rhythmVoiceDeviceParamMin(int32_t voice, int32_t dev, int32_t param) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return d ? d->paramMin(param) : 0.0f;
}
float Engine::DeviceChain::rhythmVoiceDeviceParamMax(int32_t voice, int32_t dev, int32_t param) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return d ? d->paramMax(param) : 1.0f;
}
float Engine::DeviceChain::rhythmVoiceDeviceParamGet(int32_t voice, int32_t dev, int32_t param) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return d ? d->getParam(param) : 0.0f;
}
void Engine::DeviceChain::rhythmVoiceDeviceParamSet(int32_t voice, int32_t dev, int32_t param, float value) {
    if (auto p = impl_.lock()) p->runtime.tracks().rhythmVoiceDeviceParamSet(trackId_, voice, dev, param, value);
}
bool Engine::DeviceChain::rhythmVoiceDeviceBypassed(int32_t voice, int32_t dev) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return d ? d->bypassed() : false;
}
void Engine::DeviceChain::rhythmSetVoiceDeviceBypassed(int32_t voice, int32_t dev, bool bypassed) {
    auto p = impl_.lock();
    if (auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr) d->setBypassed(bypassed);
}
float Engine::DeviceChain::rhythmVoiceDeviceGainReduction(int32_t voice, int32_t dev) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return d ? d->gainReductionDb() : 0.0f;
}
int32_t Engine::DeviceChain::rhythmVoiceDeviceScope(int32_t voice, int32_t dev, float* out, int32_t maxSamples) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return d ? d->scopeRead(out, maxSamples) : 0;
}
int32_t Engine::DeviceChain::rhythmVoiceDeviceLayerWave(
    int32_t voice, int32_t dev, int32_t layer, float* out, int32_t maxSamples) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return d ? d->layerWave(layer, out, maxSamples) : 0;
}
std::string Engine::DeviceChain::rhythmVoiceDeviceText(int32_t voice, int32_t dev, int32_t id) const {
    auto p = impl_.lock();
    auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr;
    return d ? d->deviceText(id) : std::string{};
}
void Engine::DeviceChain::rhythmVoiceDeviceAction(int32_t voice, int32_t dev, int32_t id, int32_t iarg, float farg) {
    auto p = impl_.lock();
    if (auto* d = p ? p->runtime.tracks().rhythmVoiceDevice(trackId_, voice, dev) : nullptr)
        d->deviceAction(id, iarg, farg);
}
std::string Engine::DeviceChain::rhythmKitName() const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmKitName(trackId_) : std::string{};
}
void Engine::DeviceChain::setRhythmKitName(const std::string& name) {
    if (auto p = impl_.lock()) p->runtime.tracks().setRhythmKitName(trackId_, name);
}
std::string Engine::DeviceChain::rhythmMacroName(int32_t macro) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmMacroName(trackId_, macro) : std::string{};
}
void Engine::DeviceChain::setRhythmMacroName(int32_t macro, const std::string& name) {
    if (auto p = impl_.lock()) p->runtime.tracks().setRhythmMacroName(trackId_, macro, name);
}
int32_t Engine::DeviceChain::rhythmAddMacroMapping(
    int32_t macro, int32_t voice, int32_t device, int32_t param, float lo, float hi) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmAddMacroMapping(trackId_, macro, voice, device, param, lo, hi) : -1;
}
int32_t Engine::DeviceChain::rhythmMacroMappingCount() const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmMacroMappingCount(trackId_) : 0;
}
bool Engine::DeviceChain::rhythmMacroMappingInfo(int32_t index, int32_t& macro, int32_t& voice, int32_t& device,
    int32_t& param, float& lo, float& hi, int32_t& curve) const {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmMacroMappingInfo(trackId_, index, macro, voice, device, param, lo, hi, curve)
             : false;
}
bool Engine::DeviceChain::rhythmRemoveMacroMapping(int32_t index) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmRemoveMacroMapping(trackId_, index) : false;
}
bool Engine::DeviceChain::rhythmSetMacroMappingRange(int32_t index, float lo, float hi) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmSetMacroMappingRange(trackId_, index, lo, hi) : false;
}
bool Engine::DeviceChain::rhythmSetMacroMappingCurve(int32_t index, int32_t curve) {
    auto p = impl_.lock();
    return p ? p->runtime.tracks().rhythmSetMacroMappingCurve(trackId_, index, curve) : false;
}
void Engine::DeviceChain::rhythmClearMacros() {
    if (auto p = impl_.lock()) p->runtime.tracks().rhythmClearMacros(trackId_);
}

} // namespace nota
