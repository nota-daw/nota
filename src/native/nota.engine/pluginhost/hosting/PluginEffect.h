// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginEffect — JUCE AudioPluginInstance behind nota::Device.
// Stereo deinterleaving uses vectorized simd_audio::deinterleaveStereo, the
// sidechain gain uses fast_math::dbToLin (no std::pow on the audio thread),
// and the wet/dry mix uses simd_audio::interleaveStereoWetDryMix.

#pragma once

#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "devices/Device.h"
#include "NotaPlayHead.h"
#include "PluginCommon.h"
#include "PluginEditorWindow.h"

#include <juce_audio_processors/juce_audio_processors.h>

namespace nota {
namespace hosting {

class PluginEffect : public nota::Device, private juce::AudioProcessorListener {
  public:
    PluginEffect(std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock);
    ~PluginEffect() override;

    const char* displayName() const override { return name_.empty() ? "Plugin" : name_.c_str(); }

    void setSampleRate(double sr, int32_t maxBlock) override;
    int32_t latencySamples() const override;

    void process(float* buf, int32_t frames) override;
    void setTransportInfo(const nota::TransportInfo& ti) override { playHead_.info = ti; }

    void openEditor() override;
    void closeEditor() override;
    bool editorOpen() const override { return editor_.isOpen(); }

    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;

    std::string pluginIdentifier() const override;

    // Sleep contract : tail length comes from the plugin; idle is
    // never reported — tail decay state is opaque to the host, so the engine
    // keeps rendering (safe against dropped reverb/delay tails).
    bool hasTail() const override;
    bool isIdle() const override { return false; }
    void wake() override {}

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

    // Sidechain / routing (Phase C). The engine drives these generically for any
    // Device; the aux input bus is fed in process() above.
    int32_t sidechainSourceTrackId() const override;
    void setSidechainSourceTrackId(int32_t id) override;
    void setSidechain(const float* interleaved, int32_t frames) override;
    bool acceptsSidechain() const override { return hasSidechain_; }

    std::shared_ptr<nota::Device> clone() const override;

  private:
    void audioProcessorParameterChanged(juce::AudioProcessor*, int index, float) override;
    void audioProcessorParameterChangeGestureBegin(juce::AudioProcessor*, int index) override;
    void audioProcessorParameterChangeGestureEnd(juce::AudioProcessor*, int index) override;
    void audioProcessorChanged(juce::AudioProcessor*, const juce::AudioProcessorListener::ChangeDetails&) override {}

    NotaPlayHead playHead_; // declared first: outlives plugin_ (which points at it)
    std::unique_ptr<juce::AudioPluginInstance> plugin_;
    std::string name_;
    std::atomic<int32_t> lastTouched_{-1};                   // "Learn" (M9-B3)
    std::atomic<int32_t> gestureBegin_{-1}, gestureEnd_{-1}; // write gestures (M9-C)
    std::atomic<int32_t> scTrackId_{-1};                     // sidechain source track (Phase C)
    const float* scBuf_ = nullptr;                           // engine-handed sidechain buffer (audio thread)
    int32_t scFrames_ = 0;
    bool hasSidechain_ = false;              // plugin exposes a usable sidechain input bus
    int scChanOffset_ = -1, scNumChans_ = 0; // where its channels sit in the process buffer
    juce::AudioBuffer<float> storage_;
    std::vector<float> spare_; // the channels past storage_ (see pluginChannelPtrs)
    int chanFloor_ = 0;        // pluginChannelPtrs floor (see minProcessChannels)
    juce::MidiBuffer empty_;
    int maxBlock_ = 0;
    bool stereoIn_ = true, stereoOut_ = true; // main buses have a channel 1 (see setSampleRate)
    EditorHost editor_;                       // declared last: destroyed before plugin_
};

} // namespace hosting
} // namespace nota
