// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/dsp/EditorPreview.h"
#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/Types.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::Device {
  public:
    Device() = default;
    Device(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex,
        engine::DeviceFamily family = engine::DeviceFamily::AudioEffect) noexcept
        : impl_(std::move(impl)), trackId_(trackId), index_(deviceIndex), family_(family) {}

    int32_t trackId() const noexcept { return trackId_; }
    int32_t index() const noexcept { return index_; }
    engine::DeviceFamily family() const noexcept { return family_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    int32_t kind() const;
    std::string name() const;
    int64_t deviceId() const;
    std::string pluginId() const;
    bool supportsMpe() const;
    int32_t pluginLoadState(int32_t* reasonCode, std::string* reasonArg) const;
    bool pluginLoadAction(int32_t action);

    int32_t paramCount() const;
    Parameter parameter(int32_t paramIndex) const;
    int32_t pluginParamCount() const;
    Parameter pluginParameter(int32_t paramIndex) const;
    int32_t pluginParamIndexOfId(const std::string& id) const;
    int32_t lastTouchedPluginParam();

    void setBypassed(bool bypassed);
    bool bypassed() const;
    void setFolded(bool folded);
    bool folded() const;
    bool setSleepMode(int32_t mode);
    int32_t sleepMode() const;

    void openEditor();
    void closeEditor();
    bool editorOpen() const;
    dsp::EditorPreview editorPreview() const;
    bool setEditorPreview(dsp::EditorPreview preview);

    int32_t getState(uint8_t* out, int32_t cap) const;
    int32_t pluginGetState(uint8_t* out, int32_t cap) const;
    std::vector<uint8_t> midiGetState() const;
    bool setState(const uint8_t* data, int32_t size, bool checkpoint = false);
    bool pluginSetState(const uint8_t* data, int32_t size);
    bool setInstrumentStateUndoable(int32_t chain, const uint8_t* data, int32_t size);
    bool recordInstrumentState(int32_t chain, const uint8_t* before, int32_t size);

    float gainReduction() const;
    int32_t scope(float* out, int32_t maxSamples) const;
    void action(int32_t id, int32_t iarg, float farg);
    void midiCommand(int32_t cmd);
    int32_t midiLastIn() const;
    int32_t midiLastOut() const;
    int32_t layerWave(int32_t layer, float* out, int32_t maxSamples) const;
    bool loadFile(const std::string& path);
    std::string text(int32_t id) const;
    bool setEqBandText(int32_t band, const std::string& label, const std::string& note);
    std::string eqBandText(int32_t band, int32_t which) const;

    int32_t activeVoices() const;
    int32_t heldNotes(int32_t* out, int32_t maxCount) const;
    int32_t instrumentOutput(float* out, int32_t maxCount) const;

    int32_t presetTag() const;
    bool setPresetTagIndex(int32_t tag);
    int32_t setPresetTag(const char* id, const char* name, const float* base, int32_t count);
    int32_t presetModified() const;

    bool acceptsSidechain() const;
    void setSidechainSource(int32_t sourceTrackId);
    int32_t sidechainSource() const;
    void setSidechainGain(float db);
    float sidechainGain() const;
    void setSidechainMix(float mix);
    float sidechainMix() const;
    void setSidechainTapPre(int32_t pre);
    int32_t sidechainTapPre() const;
    int32_t setSidechainKey(int32_t sourceTrackId, int32_t part);
    bool sidechainKey(int32_t* source, int32_t* part) const;
    int32_t partCount() const;
    std::string partName(int32_t index) const;
    int32_t partIndexOf(uint64_t partId) const;
    bool partIdAt(int32_t index, uint64_t* partId) const;

    void setCcDest(int32_t destDevice, int32_t destParam);
    void setCcDepth(float depth);
    int32_t ccDestDevice() const;
    int32_t ccDestParam() const;
    float ccDepth() const;
    bool ccDestPath(int32_t* chainIndex, int32_t* slot) const;
    bool setCcDestPath(int32_t chainIndex, int32_t slot);

    Rack asRack() const;

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
    int32_t index_ = -1;
    engine::DeviceFamily family_ = engine::DeviceFamily::AudioEffect;
};

class NOTA_ENGINE_CORE_API Engine::Parameter {
  public:
    Parameter() = default;
    Parameter(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, int32_t paramIndex,
        engine::DeviceFamily family = engine::DeviceFamily::AudioEffect, bool pluginParam = false) noexcept
        : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex), paramIndex_(paramIndex),
          family_(family), plugin_(pluginParam) {}

    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    float get() const;
    void set(float value);
    float min() const;
    float max() const;
    float defaultValue() const;
    std::string id() const;
    std::string name() const;
    std::string text() const;
    int32_t steps() const;
    std::string stepText(int32_t step) const;
    int32_t indexFromV1() const;
    bool isModulated() const;

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
    int32_t deviceIndex_ = -1;
    int32_t paramIndex_ = -1;
    engine::DeviceFamily family_ = engine::DeviceFamily::AudioEffect;
    bool plugin_ = false;
};

} // namespace nota
