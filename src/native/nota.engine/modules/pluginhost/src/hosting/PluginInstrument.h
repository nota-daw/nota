// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "hosting/NotaPlayHead.h"
#include "hosting/PluginCommon.h"
#include "hosting/PluginEditorWindow.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/PluginContracts.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/dsp/rt/TransportInfo.h"

#include <juce_audio_processors/juce_audio_processors.h>

#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace nota::pluginhost::detail {

class LoadTicket;

class PluginInstrument final : public dsp::Instrument, private juce::AudioProcessorListener {
  public:
    PluginInstrument(std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock,
        std::weak_ptr<dsp::IPluginCatalog> catalog = {});
    PluginInstrument(
        const juce::PluginDescription& desc, double sr, int maxBlock, std::weak_ptr<dsp::IPluginCatalog> catalog = {});
    ~PluginInstrument() override;

    const char* displayName() const override { return name_.empty() ? "Plugin" : name_.c_str(); }
    bool handlesSustain() const override { return true; }
    bool supportsParallelRender() const override { return parallelRender_.load(std::memory_order_relaxed); }

    int32_t pluginLoadState() const override;
    int32_t pluginLoadReasonCode() const override;
    std::string pluginLoadReasonArg() const override;
    void pluginLoadAction(int32_t action) override;
    bool takesTimedEvents() const override { return true; }

    void setSampleRate(double sr) override;

    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;
    void noteExpression(int32_t pitch, int32_t dim, float value) override;
    void controller(int32_t ctl, float value) override;
    void programChange(int32_t program) override;
    bool takesArticulations() const override { return true; }

    void render(float* out, int32_t frames) override;
    void setTransportInfo(const dsp::TransportInfo& ti) override { playHead_.info = ti; }

    void openEditor() override;
    void closeEditor() override;
    bool editorOpen() const override { return editor_.isOpen(); }
    void setEditorOpenForTest(bool open) override { editor_.setSimulatedOpenForTest(open); }

    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;

    std::string pluginIdentifier() const override;
    int32_t latencySamples() const override;

    bool hasTail() const override {
        return live_.load(std::memory_order_acquire) && hasTail_.load(std::memory_order_relaxed);
    }
    bool isIdle() const override { return gate_.isSleeping(); }
    void wake() override { gate_.wake(); }
    void setSleepMode(dsp::PluginSleepMode mode) override {
        dsp::Instrument::setSleepMode(mode);
        gate_.setSleepMode(mode);
    }

    int32_t pluginParamCount() const override;
    std::string pluginParamId(int32_t i) const override;
    std::string pluginParamName(int32_t i) const override;
    std::string pluginParamText(int32_t i) const override;
    float pluginParamDefault(int32_t i) const override;
    float pluginParamGet(int32_t i) const override;
    void pluginParamSet(int32_t i, float v) override;
    int32_t pluginParamIndexOfId(const std::string& id) const override;
    int32_t lastTouchedPluginParam() override;
    int32_t takePluginGestureBegin() override;
    int32_t takePluginGestureEnd() override;

    std::shared_ptr<dsp::Instrument> clone() const override;

    static int u7(float v) { return juce::jlimit(0, 127, static_cast<int>(std::lround(v * 127.0f))); }
    int chan() {
        const int c = eventChannel >= 1 && eventChannel <= 16 ? eventChannel : 1;
        usedChannels_ |= static_cast<uint16_t>(1u << (c - 1));
        return c;
    }

  private:
    bool attach(std::unique_ptr<juce::AudioPluginInstance> p);
    void audioProcessorParameterChanged(juce::AudioProcessor*, int index, float) override;
    void audioProcessorParameterChangeGestureBegin(juce::AudioProcessor*, int index) override;
    void audioProcessorParameterChangeGestureEnd(juce::AudioProcessor*, int index) override;
    void audioProcessorChanged(juce::AudioProcessor*, const juce::AudioProcessorListener::ChangeDetails&) override {}

    void trackNoteOn(int32_t pitch, float velocity) noexcept;
    void trackNoteOff(int32_t pitch) noexcept;
    void prepare(double sr);
    void refreshGateConfig(double sr) noexcept;
    bool renderPiece(float* out, int32_t frames, juce::MidiBuffer& midi);

    juce::PluginDescription desc_;
    std::weak_ptr<dsp::IPluginCatalog> catalog_;
    std::vector<uint8_t> pendingState_;
    std::shared_ptr<LoadTicket> load_;
    std::unique_ptr<juce::AudioPluginInstance> plugin_;
    double sr_ = 44100.0;
    int maxBlock_ = 512;
    std::string name_;
    juce::AudioBuffer<float> storage_;
    std::vector<float> spare_;
    bool stereoOut_ = true;
    int chanFloor_ = 0;
    juce::MidiBuffer pending_;
    juce::MidiBuffer piece_;
    uint16_t usedChannels_ = 1u;
    GuiNoteQueue guiNotes_;
    EditorHost editor_;
    NotaPlayHead playHead_;
    WorkgroupLink workgroup_;
    dsp::SilenceGate gate_;
    uint8_t noteCounts_[128]{};
    int32_t heldNotesCount_ = 0;
    bool sustainDown_ = false;
    std::atomic<bool> live_{false};
    std::atomic<bool> hasTail_{false};
    std::atomic<bool> parallelRender_{false};
    std::atomic<int32_t> lastTouched_{-1};
    std::atomic<int32_t> gestureBegin_{-1};
    std::atomic<int32_t> gestureEnd_{-1};
};

} // namespace nota::pluginhost::detail
