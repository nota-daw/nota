// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginInstrument implementation. render() accumulates via vectorized
// simd_audio::interleaveStereoAdd instead of a scalar sample loop.

#include "PluginInstrument.h"

#include "engine/MidiController.h"

#include "dsp/math/SimdAudio.h"

namespace nota {
namespace hosting {

PluginInstrument::PluginInstrument(std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock)
    : plugin_(std::move(p)), maxBlock_(maxBlock) {
    pending_.ensureSize(8192);
    if (plugin_) name_ = plugin_->getName().toStdString(); // cache for displayName()
    prepare(sr);
    if (plugin_) plugin_->addListener(this); // "Learn" (M9-B3)
    // Editor-window computer keyboard -> this plugin (drained on the audio thread).
    editor_.onNoteOn = [this](int32_t pitch, float vel) { guiNotes_.push({true, pitch, vel}); };
    editor_.onNoteOff = [this](int32_t pitch) { guiNotes_.push({false, pitch, 0.0f}); };
}

PluginInstrument::~PluginInstrument() {
    if (plugin_) plugin_->removeListener(this);
}

void PluginInstrument::setSampleRate(double sr) {
    prepare(sr);
}

void PluginInstrument::noteOn(int32_t pitch, float velocity) {
    pending_.addEvent(juce::MidiMessage::noteOn(1, pitch, (float)velocity), 0);
}

void PluginInstrument::noteOff(int32_t pitch) {
    pending_.addEvent(juce::MidiMessage::noteOff(1, pitch), 0);
}

void PluginInstrument::allNotesOff() {
    pending_.addEvent(juce::MidiMessage::allNotesOff(1), 0);
}

void PluginInstrument::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch >= 0 && dim == 1) pending_.addEvent(juce::MidiMessage::aftertouchChange(1, pitch, u7(value)), 0);
}

void PluginInstrument::controller(int32_t ctl, float value) {
    if (ctl == nota::kCtlPitchWheel)
        pending_.addEvent(
            juce::MidiMessage::pitchWheel(1, juce::jlimit(0, 16383, (int)std::lround((value + 1.0f) * 8191.5f))), 0);
    else if (ctl == nota::kCtlChannelPressure)
        pending_.addEvent(juce::MidiMessage::channelPressureChange(1, u7(value)), 0);
    else if (ctl >= 0 && ctl < 128)
        pending_.addEvent(juce::MidiMessage::controllerEvent(1, ctl, u7(value)), 0);
}

void PluginInstrument::render(float* out, int32_t frames) {
    if (frames <= 0 || frames > maxBlock_ || plugin_ == nullptr) return;
    // Drain computer-keyboard notes from the editor window (message thread) into
    // this block's MIDI buffer — audio thread only touches pending_ here.
    for (GuiNote g; guiNotes_.pop(g);)
        pending_.addEvent(
            g.on ? juce::MidiMessage::noteOn(1, g.pitch, g.velocity) : juce::MidiMessage::noteOff(1, g.pitch), 0);
    storage_.clear();
    float* ptrs[kMaxChans];
    juce::AudioBuffer<float> block(
        ptrs, pluginChannelPtrs(*plugin_, storage_, spare_.data(), frames, chanFloor_, ptrs), frames);
    plugin_->processBlock(block, pending_);
    pending_.clear();
    const float* l = block.getReadPointer(0);
    const float* r = stereoOut_ ? block.getReadPointer(1) : l; // channel 1 of a mono main is another bus
    simd_audio::interleaveStereoAdd(out, l, r, frames);
}

void PluginInstrument::openEditor() {
    if (plugin_) editor_.open(*plugin_);
}

void PluginInstrument::closeEditor() {
    editor_.close();
}

std::vector<uint8_t> PluginInstrument::getState() const {
    return readState(plugin_.get());
}

void PluginInstrument::setState(const uint8_t* data, int32_t size) {
    writeState(plugin_.get(), data, size);
}

std::string PluginInstrument::pluginIdentifier() const {
    return plugin_ ? plugin_->getPluginDescription().createIdentifierString().toStdString() : std::string{};
}

int32_t PluginInstrument::latencySamples() const {
    return plugin_ ? plugin_->getLatencySamples() : 0;
}

bool PluginInstrument::hasTail() const {
    return plugin_ != nullptr && plugin_->getTailLengthSeconds() > 0.0;
}

int32_t PluginInstrument::pluginParamCount() const {
    return paramCountImpl(plugin_.get());
}
std::string PluginInstrument::pluginParamId(int32_t i) const {
    return paramIdImpl(plugin_.get(), i);
}
std::string PluginInstrument::pluginParamName(int32_t i) const {
    return paramNameImpl(plugin_.get(), i);
}
std::string PluginInstrument::pluginParamText(int32_t i) const {
    return paramTextImpl(plugin_.get(), i);
}
float PluginInstrument::pluginParamDefault(int32_t i) const {
    return paramDefaultImpl(plugin_.get(), i);
}
float PluginInstrument::pluginParamGet(int32_t i) const {
    return paramGetImpl(plugin_.get(), i);
}
void PluginInstrument::pluginParamSet(int32_t i, float v) {
    paramSetImpl(plugin_.get(), i, v);
}
int32_t PluginInstrument::pluginParamIndexOfId(const std::string& id) const {
    return paramIndexOfIdImpl(plugin_.get(), id);
}
int32_t PluginInstrument::lastTouchedPluginParam() {
    return lastTouched_.exchange(-1, std::memory_order_relaxed);
}
int32_t PluginInstrument::takePluginGestureBegin() {
    return gestureBegin_.exchange(-1, std::memory_order_relaxed);
}
int32_t PluginInstrument::takePluginGestureEnd() {
    return gestureEnd_.exchange(-1, std::memory_order_relaxed);
}

std::shared_ptr<nota::Instrument> PluginInstrument::clone() const {
    if (!plugin_) return nullptr;
    double sr = plugin_->getSampleRate() > 0 ? plugin_->getSampleRate() : 44100.0;
    auto inst = createInstance(plugin_->getPluginDescription(), sr, maxBlock_);
    if (!inst) return nullptr;
    juce::MemoryBlock mb;
    plugin_->getStateInformation(mb);
    inst->setStateInformation(mb.getData(), (int)mb.getSize());
    return std::make_shared<PluginInstrument>(std::move(inst), sr, maxBlock_);
}

void PluginInstrument::audioProcessorParameterChanged(juce::AudioProcessor*, int index, float) {
    lastTouched_.store(index, std::memory_order_relaxed);
}

void PluginInstrument::audioProcessorParameterChangeGestureBegin(juce::AudioProcessor*, int index) {
    gestureBegin_.store(index, std::memory_order_relaxed);
}

void PluginInstrument::audioProcessorParameterChangeGestureEnd(juce::AudioProcessor*, int index) {
    gestureEnd_.store(index, std::memory_order_relaxed);
}

void PluginInstrument::prepare(double sr) {
    // Stereo main output (instruments get no input of their own), then every other bus on.
    if (plugin_->getBusCount(false) > 0) plugin_->setChannelLayoutOfBus(false, 0, juce::AudioChannelSet::stereo());
    enableEveryBus(*plugin_);
    logBuses(*plugin_);
    plugin_->setRateAndBufferSizeDetails(sr, maxBlock_);
    plugin_->prepareToPlay(sr, maxBlock_);
    plugin_->setPlayHead(&playHead_); // DAW transport sync
    // In and out share the buffer, so it spans the wider side.
    storage_.setSize(
        juce::jmax(2, plugin_->getTotalNumInputChannels(), plugin_->getTotalNumOutputChannels()), maxBlock_);
    stereoOut_ = plugin_->getBusCount(false) > 0 && plugin_->getChannelCountOfBus(false, 0) > 1;
    spare_.assign((size_t)maxBlock_, 0.0f);
    chanFloor_ = minProcessChannels(*plugin_);
}

} // namespace hosting
} // namespace nota
