// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginInstrument — JUCE AudioPluginInstance behind nota::Instrument
// . All audio-thread methods are allocation-free after
// construction (buffers pre-sized). MIDI/DSP state is audio-thread-owned;
// prepare happens on the message thread before publish. Stereo accumulation
// uses vectorized simd_audio::interleaveStereoAdd.

#pragma once

#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "devices/Instrument.h"
#include "NotaPlayHead.h"
#include "PluginCommon.h"
#include "PluginEditorWindow.h"

#include <juce_audio_processors/juce_audio_processors.h>

namespace nota {
namespace hosting {

class PluginInstrument : public nota::Instrument, private juce::AudioProcessorListener {
  public:
    PluginInstrument(std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock);
    ~PluginInstrument() override;

    const char* displayName() const override { return name_.empty() ? "Plugin" : name_.c_str(); }
    bool handlesSustain() const override { return true; } // the plug-in gets CC64 itself

    void setSampleRate(double sr) override;

    void noteOn(int32_t pitch, float velocity) override;
    void noteOff(int32_t pitch) override;
    void allNotesOff() override;
    void noteExpression(int32_t pitch, int32_t dim, float value) override;
    void controller(int32_t ctl, float value) override;

    void render(float* out, int32_t frames) override;
    void setTransportInfo(const nota::TransportInfo& ti) override { playHead_.info = ti; }

    void openEditor() override;
    void closeEditor() override;

    std::vector<uint8_t> getState() const override;
    void setState(const uint8_t* data, int32_t size) override;

    std::string pluginIdentifier() const override;

    int32_t latencySamples() const override;

    // Sleep contract : plugin voice state is opaque to the host, so a
    // hosted instrument never reports idle — the engine always renders it when
    // the track is live. hasTail() still reports the plugin's tail length.
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

    std::shared_ptr<nota::Instrument> clone() const override;

    static int u7(float v) { return juce::jlimit(0, 127, (int)std::lround(v * 127.0f)); }

  private:
    void audioProcessorParameterChanged(juce::AudioProcessor*, int index, float) override;
    void audioProcessorParameterChangeGestureBegin(juce::AudioProcessor*, int index) override;
    void audioProcessorParameterChangeGestureEnd(juce::AudioProcessor*, int index) override;
    void audioProcessorChanged(juce::AudioProcessor*, const juce::AudioProcessorListener::ChangeDetails&) override {}

    void prepare(double sr);

    NotaPlayHead playHead_; // declared first: outlives plugin_ (which points at it)
    std::unique_ptr<juce::AudioPluginInstance> plugin_;
    juce::AudioBuffer<float> storage_;
    std::vector<float> spare_; // the channels past storage_ (see pluginChannelPtrs)
    int chanFloor_ = 0;        // pluginChannelPtrs floor (see minProcessChannels)
    juce::MidiBuffer pending_;
    int maxBlock_;
    bool stereoOut_ = true;                                  // main output bus has a channel 1 (see prepare)
    std::string name_;                                       // cached plugin name for displayName()
    std::atomic<int32_t> lastTouched_{-1};                   // "Learn" (M9-B3)
    std::atomic<int32_t> gestureBegin_{-1}, gestureEnd_{-1}; // write gestures (M9-C)
    GuiNoteQueue guiNotes_; // editor-window computer keyboard -> render() (message->audio)
    EditorHost editor_;     // declared last: destroyed before plugin_ (detaches editor)
};

} // namespace hosting
} // namespace nota
