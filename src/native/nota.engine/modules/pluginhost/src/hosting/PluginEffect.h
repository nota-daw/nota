// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "hosting/NotaPlayHead.h"
#include "hosting/PluginCommon.h"
#include "hosting/PluginEditorWindow.h"
#include "nota/dsp/Device.h"
#include "nota/dsp/PluginContracts.h"
#include "nota/dsp/rt/AudioSpan.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/dsp/rt/TransportInfo.h"

#include <juce_audio_processors/juce_audio_processors.h>

#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::pluginhost::detail {

class LoadTicket;

class PluginEffect final : public dsp::Device, private juce::AudioProcessorListener {
  public:
    PluginEffect(std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock,
        std::weak_ptr<dsp::IPluginCatalog> catalog = {});
    PluginEffect(
        const juce::PluginDescription& desc, double sr, int maxBlock, std::weak_ptr<dsp::IPluginCatalog> catalog = {});
    ~PluginEffect() override;

    const char* displayName() const override { return name_.empty() ? "Plugin" : name_.c_str(); }
    bool supportsParallelRender() const override { return parallelRender_.load(std::memory_order_relaxed); }
    bool prefersPlanar() const override { return true; }

    int32_t pluginLoadState() const override;
    int32_t pluginLoadReasonCode() const override;
    std::string pluginLoadReasonArg() const override;
    void pluginLoadAction(int32_t action) override;

    void setSampleRate(double sr, int32_t maxBlock) override;
    int32_t latencySamples() const override;

    void process(float* buf, int32_t frames) override;
    void processPlanar(dsp::PlanarAudioSpan io, int32_t frames) override;
    void setTransportInfo(const dsp::TransportInfo& ti) override { playHead_.info = ti; }

    void openEditor() override;
    void closeEditor() override;
    bool editorOpen() const override { return editor_.isOpen(); }
    void setEditorOpenForTest(bool open) override { editor_.setSimulatedOpenForTest(open); }

    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;

    std::string pluginIdentifier() const override;

    bool hasTail() const override {
        return live_.load(std::memory_order_acquire) && hasTail_.load(std::memory_order_relaxed);
    }
    bool isIdle() const override { return gate_.isSleeping(); }
    void wake() override { gate_.wake(); }
    void setSleepMode(dsp::PluginSleepMode mode) override {
        dsp::Device::setSleepMode(mode);
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

    int32_t sidechainSourceTrackId() const override;
    void setSidechainSourceTrackId(int32_t id) override;
    void setSidechain(const float* interleaved, int32_t frames) override;
    bool acceptsSidechain() const override { return hasSidechain_; }

    std::shared_ptr<dsp::Device> clone() const override;

  private:
    bool attach(std::unique_ptr<juce::AudioPluginInstance> p);
    void audioProcessorParameterChanged(juce::AudioProcessor*, int index, float) override;
    void audioProcessorParameterChangeGestureBegin(juce::AudioProcessor*, int index) override;
    void audioProcessorParameterChangeGestureEnd(juce::AudioProcessor*, int index) override;
    void audioProcessorChanged(juce::AudioProcessor*, const juce::AudioProcessorListener::ChangeDetails&) override {}

    template <typename PieceFn>
    void runProcessGate(bool mainInSilent, int32_t frames, const float* sc, int32_t scFrames, PieceFn&& runPiece);
    bool processPiece(float* buf, int32_t frames, const float* scBuf, int32_t scFrames);
    bool processPlanarPiece(float* dstL, float* dstR, int32_t frames, const float* scBuf, int32_t scFrames);
    void invokePluginBlock(int32_t frames, const float* scBuf, int32_t scFrames, float* (&ptrs)[kMaxChans],
        const float*& outL, const float*& outR);
    void refreshGateConfig(double sr) noexcept;

    juce::PluginDescription desc_;
    std::weak_ptr<dsp::IPluginCatalog> catalog_;
    std::vector<uint8_t> pendingState_;
    std::shared_ptr<LoadTicket> load_;
    std::unique_ptr<juce::AudioPluginInstance> plugin_;
    double sr_ = 44100.0;
    int requestedBlock_ = 512;
    int maxBlock_ = 512;
    std::string name_;
    juce::AudioBuffer<float> storage_;
    std::vector<float> spare_;
    bool stereoIn_ = true;
    bool stereoOut_ = true;
    int chanFloor_ = 0;
    bool hasSidechain_ = false;
    int scChanOffset_ = -1;
    int scNumChans_ = 0;
    std::atomic<int32_t> scTrackId_{-1};
    const float* scBuf_ = nullptr;
    int32_t scFrames_ = 0;
    juce::MidiBuffer empty_;
    EditorHost editor_;
    NotaPlayHead playHead_;
    WorkgroupLink workgroup_;
    dsp::SilenceGate gate_;
    std::atomic<bool> live_{false};
    std::atomic<bool> hasTail_{false};
    std::atomic<bool> parallelRender_{false};
    std::atomic<int32_t> lastTouched_{-1};
    std::atomic<int32_t> gestureBegin_{-1};
    std::atomic<int32_t> gestureEnd_{-1};
};

} // namespace nota::pluginhost::detail
