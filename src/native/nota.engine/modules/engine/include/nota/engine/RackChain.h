// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/engine/Engine.h"
#include "nota/engine/Export.h"
#include "nota/engine/Types.h"

#include <cstdint>
#include <memory>
#include <string>
#include <utility>

namespace nota {

class NOTA_ENGINE_CORE_API Engine::RackChain {
  public:
    RackChain() = default;
    RackChain(std::weak_ptr<Engine::Impl> impl, int32_t trackId, int32_t deviceIndex, int32_t chainIndex) noexcept
        : impl_(std::move(impl)), trackId_(trackId), deviceIndex_(deviceIndex), chainIndex_(chainIndex) {}

    int32_t trackId() const noexcept { return trackId_; }
    int32_t deviceIndex() const noexcept { return deviceIndex_; }
    int32_t index() const noexcept { return chainIndex_; }
    bool isValid() const noexcept;
    explicit operator bool() const noexcept { return isValid(); }

    int64_t chainId() const;
    std::string name() const;
    void setName(const std::string& name);
    int32_t triggerNote() const;
    void setTriggerNote(int32_t note);

    void setVolume(float v);
    void setPan(float v);
    void setMute(bool m);
    void setSolo(bool s);
    float volume() const;
    float pan() const;
    bool mute() const;
    bool solo() const;
    float meter() const;

    void zone(int32_t& keyLo, int32_t& keyHi, int32_t& velLo, int32_t& velHi) const;
    void setZone(int32_t keyLo, int32_t keyHi, int32_t velLo, int32_t velHi);
    void selectRange(int32_t& lo, int32_t& hi) const;
    void setSelectRange(int32_t lo, int32_t hi);
    int32_t transpose() const;
    void setTranspose(int32_t st);
    float velScale() const;
    void setVelScale(float v);
    void setChoke(int32_t group);
    int32_t choke() const;
    void setTune(int32_t semitones);
    int32_t tune() const;
    void setDecay(float v);
    float decay() const;

    bool setSamplerSample(const std::string& path, int32_t rootNote);
    bool samplerInfo(engine::SamplerInfo& out) const;
    bool setSamplerRoot(int32_t rootNote);
    float samplerPlayPosition() const;
    bool mosaicSetProgram(const std::string& text, bool checkpoint);
    std::string mosaicProgram() const;
    bool mosaicStatus(engine::MosaicStatus& out) const;
    int64_t mosaicZoneSampleId(int32_t zone) const;

    bool setInstrument(int32_t kind);
    int32_t instrumentKind() const;
    std::string instrumentName() const;
    std::string instrumentPluginId() const;
    int32_t instrumentGetState(uint8_t* out, int32_t cap) const;
    void openInstrumentEditor();
    int32_t instrumentParamCount() const;
    std::string instrumentParamName(int32_t param) const;
    std::string instrumentParamId(int32_t param) const;
    float instrumentParamGet(int32_t param) const;
    void instrumentParamSet(int32_t param, float normalized);
    float instrumentParamDefault(int32_t param) const;
    int32_t instrumentScope(float* out, int32_t maxN) const;
    int32_t instrumentVoiceCount() const;
    int32_t instrumentHeldNotes(int32_t* out, int32_t maxN) const;
    void instrumentAction(int32_t id, int32_t iarg, float farg);

    int32_t addDevice(int32_t kind);
    int32_t addPluginDevice(int32_t catalogIndex);
    bool removeDevice(int32_t slot);
    bool moveDevice(int32_t from, int32_t to);
    void openDeviceEditor(int32_t dev);
    int32_t deviceCount() const;
    int32_t deviceKind(int32_t slot) const;
    std::string deviceName(int32_t slot) const;
    int32_t deviceParamCount(int32_t slot) const;
    std::string deviceParamName(int32_t slot, int32_t param) const;
    std::string deviceParamId(int32_t slot, int32_t param) const;
    float deviceParamMin(int32_t slot, int32_t param) const;
    float deviceParamMax(int32_t slot, int32_t param) const;
    float deviceParamGet(int32_t slot, int32_t param) const;
    void deviceParamSet(int32_t slot, int32_t param, float value);
    void setDeviceBypassed(int32_t slot, bool bypassed);
    bool deviceBypassed(int32_t slot) const;
    float deviceGainReduction(int32_t slot) const;
    int32_t deviceScope(int32_t slot, float* out, int32_t maxSamples) const;
    int32_t deviceLayerWave(int32_t slot, int32_t layer, float* out, int32_t maxSamples) const;
    std::string deviceText(int32_t slot, int32_t id) const;
    void deviceAction(int32_t slot, int32_t id, int32_t iarg, float farg);

    int32_t addMidiEffect(int32_t kind);
    bool removeMidiEffect(int32_t slot);
    bool moveMidiEffect(int32_t from, int32_t to);
    int32_t midiCount() const;
    int32_t midiKind(int32_t slot) const;
    std::string midiName(int32_t slot) const;
    int32_t midiParamCount(int32_t slot) const;
    std::string midiParamName(int32_t slot, int32_t param) const;
    float midiParamMin(int32_t slot, int32_t param) const;
    float midiParamMax(int32_t slot, int32_t param) const;
    float midiParamGet(int32_t slot, int32_t param) const;
    bool grainSetSample(const std::string& path, int32_t rootNote = 60);
    int32_t midiLastIn(int32_t slot) const;
    int32_t midiLastOut(int32_t slot) const;
    int32_t midiScope(int32_t slot, float* out, int32_t maxSamples) const;
    void midiCommand(int32_t slot, int32_t cmd);

    void midiParamSet(int32_t slot, int32_t param, float value);
    void setMidiBypassed(int32_t slot, bool bypassed);
    bool midiBypassed(int32_t slot) const;

    bool copyDevice(int32_t slot);
    bool copyMidiEffect(int32_t slot);
    int32_t pasteDevice(int32_t at);
    int32_t pasteMidiEffect(int32_t at);
    int32_t pasteDevices(int32_t midiAt, int32_t fxAt, int32_t* outSkipped, int32_t* outFirstMidi, int32_t* outFirstFx);
    int32_t duplicateDevice(int32_t slot);
    int32_t duplicateMidiEffect(int32_t slot);
    int32_t moveDeviceTo(int32_t slot, int32_t toChain);
    int32_t moveMidiTo(int32_t slot, int32_t toChain);

    bool midiCcDest(
        int32_t slot, int32_t* dev, int32_t* destChain, int32_t* destSlot, int32_t* param, float* depth) const;
    bool setMidiCcDest(int32_t slot, int32_t dev, int32_t destChain, int32_t destSlot, int32_t param, float depth);
    bool acceptsSidechain(int32_t slot) const;
    bool sidechain(int32_t slot, int32_t* src, int32_t* tapPre, float* gainDb, float* mix) const;
    int32_t setSidechain(int32_t slot, int32_t src, int32_t tapPre, float gainDb, float mix);
    bool sidechainPart(int32_t slot, int32_t* part) const;
    int32_t setSidechainPart(int32_t slot, int32_t part);
    int32_t setKey(int32_t slot, int32_t src, int32_t part, int32_t tapPre, float gainDb, float mix);

  private:
    std::weak_ptr<Engine::Impl> impl_;
    int32_t trackId_ = -1;
    int32_t deviceIndex_ = -1;
    int32_t chainIndex_ = -1;
};

} // namespace nota
