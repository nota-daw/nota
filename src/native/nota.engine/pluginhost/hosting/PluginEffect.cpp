// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginEffect implementation.
// except process() uses simd_audio::deinterleaveStereo (main input),
// fast_math::dbToLin (sidechain gain, no std::pow on the audio thread) and
// simd_audio::interleaveStereoWetDryMix (dry/wet output mix).

#include "PluginEffect.h"

#include "dsp/math/FastMath.h"
#include "dsp/math/SimdAudio.h"

namespace nota {
namespace hosting {

PluginEffect::PluginEffect(std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock)
    : plugin_(std::move(p)) {
    if (plugin_) name_ = plugin_->getName().toStdString();
    empty_.ensureSize(256);
    setSampleRate(sr, maxBlock);
    if (plugin_) plugin_->addListener(this); // "Learn" (M9-B3)
}

PluginEffect::~PluginEffect() {
    if (plugin_) plugin_->removeListener(this);
}

void PluginEffect::setSampleRate(double sr, int32_t maxBlock) {
    maxBlock_ = maxBlock;
    // Prefer stereo main I/O plus a sidechain input bus if the plugin exposes
    // one (input bus index 1, Phase C). Fall back to plain stereo I/O.
    hasSidechain_ = false;
    scChanOffset_ = -1;
    scNumChans_ = 0;
    if (plugin_->getBusCount(true) >= 2) {
        auto layout = plugin_->getBusesLayout();
        if (plugin_->getBusCount(true) > 0) layout.inputBuses.getReference(0) = juce::AudioChannelSet::stereo();
        if (plugin_->getBusCount(false) > 0) layout.outputBuses.getReference(0) = juce::AudioChannelSet::stereo();
        layout.inputBuses.getReference(1) = juce::AudioChannelSet::stereo();
        if (plugin_->setBusesLayout(layout)) hasSidechain_ = true;
        else {
            layout.inputBuses.getReference(1) = juce::AudioChannelSet::mono();
            if (plugin_->setBusesLayout(layout)) hasSidechain_ = true;
        }
    }
    if (!hasSidechain_) { // stereo main I/O
        if (plugin_->getBusCount(true) > 0) plugin_->setChannelLayoutOfBus(true, 0, juce::AudioChannelSet::stereo());
        if (plugin_->getBusCount(false) > 0) plugin_->setChannelLayoutOfBus(false, 0, juce::AudioChannelSet::stereo());
    }
    enableEveryBus(*plugin_); // an unused sidechain input is fed silence (storage_ cleared)
    logBuses(*plugin_);
    plugin_->setRateAndBufferSizeDetails(sr, maxBlock_);
    plugin_->prepareToPlay(sr, maxBlock_);
    const int chans =
        juce::jmax(2, juce::jmax(plugin_->getTotalNumInputChannels(), plugin_->getTotalNumOutputChannels()));
    storage_.setSize(chans, maxBlock_);
    stereoIn_ = plugin_->getBusCount(true) > 0 && plugin_->getChannelCountOfBus(true, 0) > 1;
    stereoOut_ = plugin_->getBusCount(false) > 0 && plugin_->getChannelCountOfBus(false, 0) > 1;
    spare_.assign((size_t)maxBlock_, 0.0f);
    chanFloor_ = minProcessChannels(*plugin_);
    if (hasSidechain_ && plugin_->getBus(true, 1) != nullptr) {
        scChanOffset_ = plugin_->getChannelIndexInProcessBlockBuffer(true, 1, 0);
        scNumChans_ = plugin_->getBus(true, 1)->getNumberOfChannels();
    }
    plugin_->setPlayHead(&playHead_); // DAW transport sync
}

int32_t PluginEffect::latencySamples() const {
    return plugin_ ? plugin_->getLatencySamples() : 0;
}

void PluginEffect::process(float* buf, int32_t frames) {
    if (frames <= 0 || frames > maxBlock_ || plugin_ == nullptr) return;
    const int nc = juce::jmin(storage_.getNumChannels(), kMaxChans);
    storage_.clear();
    float* l = storage_.getWritePointer(0);
    float* r = stereoIn_ ? storage_.getWritePointer(1) : spare_.data(); // mono main: drop R
    simd_audio::deinterleaveStereo(l, r, buf, frames);

    // Feed the sidechain input bus (Phase C) if the engine handed us a source
    // this block; otherwise it stays silent (cleared above).
    if (hasSidechain_ && scBuf_ != nullptr && scFrames_ >= frames && scChanOffset_ >= 0) {
        const float scGain = fast_math::dbToLin(sidechainGainDb()); // Phase D (was std::pow)
        if (scNumChans_ >= 2 && scChanOffset_ + 1 < nc) {
            float* sl = storage_.getWritePointer(scChanOffset_);
            float* sr = storage_.getWritePointer(scChanOffset_ + 1);
            for (int i = 0; i < frames; ++i) {
                sl[i] = scBuf_[i * 2] * scGain;
                sr[i] = scBuf_[i * 2 + 1] * scGain;
            }
        } else if (scNumChans_ == 1 && scChanOffset_ < nc) {
            float* sm = storage_.getWritePointer(scChanOffset_);
            for (int i = 0; i < frames; ++i) sm[i] = 0.5f * (scBuf_[i * 2] + scBuf_[i * 2 + 1]) * scGain;
        }
    }
    scBuf_ = nullptr; // consume once; the engine re-hands it each block if a source is set

    float* ptrs[kMaxChans];
    juce::AudioBuffer<float> block(
        ptrs, pluginChannelPtrs(*plugin_, storage_, spare_.data(), frames, chanFloor_, ptrs), frames);
    empty_.clear();
    plugin_->processBlock(block, empty_);

    const float* ol = block.getReadPointer(0);
    const float* orr = stereoOut_ ? block.getReadPointer(1) : ol;
    simd_audio::interleaveStereoWetDryMix(buf, ol, orr, frames, sidechainMix(), 1.0f - sidechainMix());
}

void PluginEffect::openEditor() {
    if (plugin_) editor_.open(*plugin_);
}

void PluginEffect::closeEditor() {
    editor_.close();
}

std::vector<uint8_t> PluginEffect::getState() const {
    return readState(plugin_.get());
}

void PluginEffect::setState(const uint8_t* data, int32_t size) {
    writeState(plugin_.get(), data, size);
}

std::string PluginEffect::pluginIdentifier() const {
    return plugin_ ? plugin_->getPluginDescription().createIdentifierString().toStdString() : std::string{};
}

bool PluginEffect::hasTail() const {
    return plugin_ != nullptr && plugin_->getTailLengthSeconds() > 0.0;
}

int32_t PluginEffect::pluginParamCount() const {
    return paramCountImpl(plugin_.get());
}
std::string PluginEffect::pluginParamId(int32_t i) const {
    return paramIdImpl(plugin_.get(), i);
}
std::string PluginEffect::pluginParamName(int32_t i) const {
    return paramNameImpl(plugin_.get(), i);
}
std::string PluginEffect::pluginParamText(int32_t i) const {
    return paramTextImpl(plugin_.get(), i);
}
float PluginEffect::pluginParamDefault(int32_t i) const {
    return paramDefaultImpl(plugin_.get(), i);
}
float PluginEffect::pluginParamGet(int32_t i) const {
    return paramGetImpl(plugin_.get(), i);
}
void PluginEffect::pluginParamSet(int32_t i, float v) {
    paramSetImpl(plugin_.get(), i, v);
}
int32_t PluginEffect::pluginParamIndexOfId(const std::string& id) const {
    return paramIndexOfIdImpl(plugin_.get(), id);
}
int32_t PluginEffect::lastTouchedPluginParam() {
    return lastTouched_.exchange(-1, std::memory_order_relaxed);
}
int32_t PluginEffect::takePluginGestureBegin() {
    return gestureBegin_.exchange(-1, std::memory_order_relaxed);
}
int32_t PluginEffect::takePluginGestureEnd() {
    return gestureEnd_.exchange(-1, std::memory_order_relaxed);
}

int32_t PluginEffect::sidechainSourceTrackId() const {
    return scTrackId_.load(std::memory_order_relaxed);
}

void PluginEffect::setSidechainSourceTrackId(int32_t id) {
    scTrackId_.store(id, std::memory_order_relaxed);
}

void PluginEffect::setSidechain(const float* interleaved, int32_t frames) {
    scBuf_ = interleaved;
    scFrames_ = frames;
}

std::shared_ptr<nota::Device> PluginEffect::clone() const {
    if (!plugin_) return nullptr;
    double sr = plugin_->getSampleRate() > 0 ? plugin_->getSampleRate() : 44100.0;
    auto inst = createInstance(plugin_->getPluginDescription(), sr, maxBlock_);
    if (!inst) return nullptr;
    juce::MemoryBlock mb;
    plugin_->getStateInformation(mb);
    inst->setStateInformation(mb.getData(), (int)mb.getSize());
    auto d = std::make_shared<PluginEffect>(std::move(inst), sr, maxBlock_);
    d->setSidechainSourceTrackId(scTrackId_.load(std::memory_order_relaxed));
    return d;
}

void PluginEffect::audioProcessorParameterChanged(juce::AudioProcessor*, int index, float) {
    lastTouched_.store(index, std::memory_order_relaxed);
}

void PluginEffect::audioProcessorParameterChangeGestureBegin(juce::AudioProcessor*, int index) {
    gestureBegin_.store(index, std::memory_order_relaxed);
}

void PluginEffect::audioProcessorParameterChangeGestureEnd(juce::AudioProcessor*, int index) {
    gestureEnd_.store(index, std::memory_order_relaxed);
}

} // namespace hosting
} // namespace nota
