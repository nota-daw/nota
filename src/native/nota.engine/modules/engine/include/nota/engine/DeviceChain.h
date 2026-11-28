// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/Types.h"

#include <cstdint>
#include <memory>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace nota::dsp {
struct WavetableAsset;
} // namespace nota::dsp

namespace nota {

class NOTA_ENGINE_CORE_API Engine::DeviceChain {
  public:
    DeviceChain() = default;
    DeviceChain(std::weak_ptr<Engine::Impl> impl, int32_t trackId) noexcept
        : impl_(std::move(impl)), trackId_(trackId) {}

    int32_t trackId() const noexcept { return trackId_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    int32_t count() const;
    Device device(int32_t deviceIndex) const;
    Device instrument() const;
    Device midiEffect(int32_t index) const;

    int32_t add(std::string_view slug);
    int32_t addBuiltin(int32_t kind);
    int32_t addPlugin(int32_t catalogIndex);
    float builtinParamDefault(int32_t kind, int32_t paramIndex) const;
    bool move(int32_t fromIndex, int32_t toIndex);
    bool remove(int32_t deviceIndex);
    bool copy(int32_t deviceIndex);
    int32_t paste(int32_t atIndex);
    int32_t duplicate(int32_t deviceIndex);

    bool setBuiltinInstrument(int32_t kind);
    bool setInstrument(std::string_view slug);
    bool setPluginInstrument(int32_t catalogIndex);
    bool copyInstrument();
    int32_t pasteInstrument();

    int32_t midiEffectCount() const;
    int32_t addMidiEffect(std::string_view slug);
    int32_t addBuiltinMidiEffect(int32_t kind);
    bool moveMidiEffect(int32_t fromIndex, int32_t toIndex);
    bool removeMidiEffect(int32_t index);
    bool copyMidiEffect(int32_t index);
    int32_t pasteMidiEffect(int32_t atIndex);
    int32_t duplicateMidiEffect(int32_t index);

    int32_t copyDevices(
        const int32_t* midiIdx, int32_t midiCount, bool withInstrument, const int32_t* fxIdx, int32_t fxCount);
    int32_t pasteDevices(int32_t midiAt, int32_t fxAt, int32_t* outSkipped);
    int32_t clipboardKind() const;
    int32_t clipboardCount() const;
    bool clipboardHasSidechain() const;

    int32_t transferDevice(engine::DeviceFamily family, int32_t index, int32_t dstTrackId, int32_t atIndex,
        bool keepSource, int32_t* outFlags);
    int32_t groupDevices(int32_t midiFirst, int32_t midiCount, bool withInstrument, int32_t fxFirst, int32_t fxCount);
    int32_t ungroupRack(engine::DeviceFamily family, int32_t index, int32_t* outDropped);
    int32_t wrapInRack(int32_t deviceIndex);
    int32_t unwrapRack(int32_t deviceIndex);

    bool setGrainSample(const std::string& path, int32_t rootNote);
    bool setGrainRoot(int32_t rootNote);
    bool setSamplerSample(const std::string& path, int32_t rootNote);
    bool setSamplerRoot(int32_t rootNote);
    float samplerPlayPosition() const;
    bool samplerInfo(engine::SamplerInfo& out) const;
    bool grainInfo(engine::SamplerInfo& out) const;
    int32_t grainPlayPositions(float* out, int32_t maxCount) const;

    bool mosaicSetProgram(const std::string& text, bool checkpoint);
    std::string mosaicProgram() const;
    bool mosaicStatus(engine::MosaicStatus& out) const;
    int64_t mosaicZoneSampleId(int32_t zone) const;

    bool auroraSetTable(int32_t osc, std::shared_ptr<const dsp::WavetableAsset> table, bool checkpoint);
    bool auroraClearTable(int32_t osc);
    bool auroraSetSource(int32_t osc, bool custom, bool checkpoint);
    bool auroraSetMissing(int32_t osc, const std::string& name);
    std::shared_ptr<const dsp::WavetableAsset> auroraTable(int32_t osc) const;
    bool auroraUsesCustom(int32_t osc) const;
    std::string auroraMissingName(int32_t osc) const;

    bool setRhythmVoiceSample(int32_t voice, const std::string& path);
    bool rhythmVoiceInfo(int32_t voice, engine::SamplerInfo& out) const;
    bool hasAurora() const;
    std::vector<uint8_t> auroraState(bool includeTables) const;

    int32_t rhythmVoiceSource(int32_t voice) const;
    int32_t rhythmVoiceDeviceCount(int32_t voice) const;
    int32_t rhythmAddVoiceDevice(int32_t voice, int32_t kind);
    bool rhythmRemoveVoiceDevice(int32_t voice, int32_t dev);
    bool rhythmMoveVoiceDevice(int32_t voice, int32_t from, int32_t to);
    std::string rhythmVoiceDeviceName(int32_t voice, int32_t dev) const;
    int32_t rhythmVoiceDeviceBuiltinKind(int32_t voice, int32_t dev) const;
    int32_t rhythmVoiceDeviceParamCount(int32_t voice, int32_t dev) const;
    std::string rhythmVoiceDeviceParamName(int32_t voice, int32_t dev, int32_t param) const;
    float rhythmVoiceDeviceParamMin(int32_t voice, int32_t dev, int32_t param) const;
    float rhythmVoiceDeviceParamMax(int32_t voice, int32_t dev, int32_t param) const;
    float rhythmVoiceDeviceParamGet(int32_t voice, int32_t dev, int32_t param) const;
    void rhythmVoiceDeviceParamSet(int32_t voice, int32_t dev, int32_t param, float value);
    bool rhythmVoiceDeviceBypassed(int32_t voice, int32_t dev) const;
    void rhythmSetVoiceDeviceBypassed(int32_t voice, int32_t dev, bool bypassed);
    float rhythmVoiceDeviceGainReduction(int32_t voice, int32_t dev) const;
    int32_t rhythmVoiceDeviceScope(int32_t voice, int32_t dev, float* out, int32_t maxSamples) const;
    int32_t rhythmVoiceDeviceLayerWave(int32_t voice, int32_t dev, int32_t layer, float* out, int32_t maxSamples) const;
    std::string rhythmVoiceDeviceText(int32_t voice, int32_t dev, int32_t id) const;
    void rhythmVoiceDeviceAction(int32_t voice, int32_t dev, int32_t id, int32_t iarg, float farg);
    std::string rhythmKitName() const;
    void setRhythmKitName(const std::string& name);
    std::string rhythmMacroName(int32_t macro) const;
    void setRhythmMacroName(int32_t macro, const std::string& name);
    int32_t rhythmAddMacroMapping(int32_t macro, int32_t voice, int32_t device, int32_t param, float lo, float hi);
    int32_t rhythmMacroMappingCount() const;
    bool rhythmMacroMappingInfo(int32_t index, int32_t& macro, int32_t& voice, int32_t& device, int32_t& param,
        float& lo, float& hi, int32_t& curve) const;
    bool rhythmRemoveMacroMapping(int32_t index);
    bool rhythmSetMacroMappingRange(int32_t index, float lo, float hi);
    bool rhythmSetMacroMappingCurve(int32_t index, int32_t curve);
    void rhythmClearMacros();

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
};

} // namespace nota
