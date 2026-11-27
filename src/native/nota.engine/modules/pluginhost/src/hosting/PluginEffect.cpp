// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "hosting/PluginEffect.h"

#include "hosting/PluginCommon.h"
#include "hosting/PluginLoader.h"
#include "nota/dsp/Device.h"
#include "nota/dsp/EditorPreview.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/dsp/rt/AudioSpan.h"
#include "nota/dsp/PluginContracts.h"
#include "nota/math/FastMath.h"
#include "nota/math/StereoOps.h"

#include <algorithm>
#include <string>
#include <memory>
#include <cstdint>
#include <cstddef>
#include <atomic>
#include <utility>
#include <vector>

namespace nota::pluginhost::detail {

PluginEffect::PluginEffect(
    std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock, std::weak_ptr<dsp::IPluginCatalog> catalog)
    : catalog_(std::move(catalog)) {
    empty_.ensureSize(256);
    editor_.onPreview = [this](
                            std::vector<uint8_t> jpeg) { setEditorPreview(dsp::EditorPreview::make(std::move(jpeg))); };
    editor_.onOpenChanged = [this](bool open) { gate_.setHoldAwake(open); };
    sr_ = sr;
    requestedBlock_ = maxBlock;
    maxBlock_ = juce::jmin(maxBlock, kPluginMaxBlock);
    if (p) attach(std::move(p));
}

PluginEffect::PluginEffect(
    const juce::PluginDescription& desc, double sr, int maxBlock, std::weak_ptr<dsp::IPluginCatalog> catalog)
    : desc_(desc), catalog_(std::move(catalog)) {
    empty_.ensureSize(256);
    editor_.onPreview = [this](
                            std::vector<uint8_t> jpeg) { setEditorPreview(dsp::EditorPreview::make(std::move(jpeg))); };
    editor_.onOpenChanged = [this](bool open) { gate_.setHoldAwake(open); };
    name_ = desc.name.toStdString();
    sr_ = sr;
    requestedBlock_ = maxBlock;
    maxBlock_ = juce::jmin(maxBlock, kPluginMaxBlock);
    load_ = PluginLoader::instance().request(desc_, [this](std::string& error) {
        auto inst = createPluginInstance(desc_, sr_, juce::jmin(requestedBlock_, kPluginMaxBlock), &error);
        return inst != nullptr && attach(std::move(inst));
    });
}

PluginEffect::~PluginEffect() {
    if (load_) PluginLoader::instance().cancel(load_);
    if (plugin_) plugin_->removeListener(this);
}

bool PluginEffect::attach(std::unique_ptr<juce::AudioPluginInstance> p) {
    if (!p || plugin_) return false;
    plugin_ = std::move(p);
    name_ = plugin_->getName().toStdString();
    setSampleRate(sr_, requestedBlock_);
    if (!pendingState_.empty()) {
        writeState(plugin_.get(), pendingState_.data(), static_cast<int32_t>(pendingState_.size()));
        std::vector<uint8_t>().swap(pendingState_);
        refreshGateConfig(sr_);
    }
    plugin_->addListener(this);
    parallelRender_.store(isParallelRenderSafe(plugin_.get()), std::memory_order_relaxed);
    live_.store(true, std::memory_order_release);
    return true;
}

int32_t PluginEffect::pluginLoadState() const {
    return load_ ? static_cast<int32_t>(load_->state()) : 0;
}

int32_t PluginEffect::pluginLoadReasonCode() const {
    return load_ ? static_cast<int32_t>(load_->reason()) : 0;
}

std::string PluginEffect::pluginLoadReasonArg() const {
    return load_ ? load_->reasonArg() : std::string{};
}

void PluginEffect::pluginLoadAction(int32_t action) {
    if (load_) PluginLoader::instance().act(load_, static_cast<LoadAction>(action));
}

void PluginEffect::setSampleRate(double sr, int32_t maxBlock) {
    sr_ = sr;
    requestedBlock_ = maxBlock;
    maxBlock_ = juce::jmin(maxBlock, kPluginMaxBlock);
    if (!plugin_) return;
    hasSidechain_ = false;
    scChanOffset_ = -1;
    scNumChans_ = 0;
    if (plugin_->getBusCount(true) >= 2) {
        auto layout = plugin_->getBusesLayout();
        if (plugin_->getBusCount(true) > 0) layout.inputBuses.getReference(0) = juce::AudioChannelSet::stereo();
        if (plugin_->getBusCount(false) > 0) layout.outputBuses.getReference(0) = juce::AudioChannelSet::stereo();
        layout.inputBuses.getReference(1) = juce::AudioChannelSet::stereo();
        if (plugin_->setBusesLayout(layout)) {
            hasSidechain_ = true;
        } else {
            layout.inputBuses.getReference(1) = juce::AudioChannelSet::mono();
            if (plugin_->setBusesLayout(layout)) hasSidechain_ = true;
        }
    }
    if (!hasSidechain_) {
        if (plugin_->getBusCount(true) > 0) plugin_->setChannelLayoutOfBus(true, 0, juce::AudioChannelSet::stereo());
        if (plugin_->getBusCount(false) > 0) plugin_->setChannelLayoutOfBus(false, 0, juce::AudioChannelSet::stereo());
    }
    enableEveryBus(*plugin_);
    logBuses(*plugin_);
    plugin_->setRateAndBufferSizeDetails(sr, maxBlock_);
    plugin_->prepareToPlay(sr, maxBlock_);
    const int chans =
        juce::jmax(2, juce::jmax(plugin_->getTotalNumInputChannels(), plugin_->getTotalNumOutputChannels()));
    storage_.setSize(chans, maxBlock_);
    stereoIn_ = plugin_->getBusCount(true) > 0 && plugin_->getChannelCountOfBus(true, 0) > 1;
    stereoOut_ = plugin_->getBusCount(false) > 0 && plugin_->getChannelCountOfBus(false, 0) > 1;
    spare_.assign(static_cast<size_t>(maxBlock_), 0.0f);
    chanFloor_ = minProcessChannels(*plugin_);
    if (hasSidechain_ && plugin_->getBus(true, 1) != nullptr) {
        scChanOffset_ = plugin_->getChannelIndexInProcessBlockBuffer(true, 1, 0);
        scNumChans_ = plugin_->getBus(true, 1)->getNumberOfChannels();
    }
    plugin_->setPlayHead(&playHead_);
    workgroup_.bind(*plugin_);
    refreshGateConfig(sr);
}

void PluginEffect::refreshGateConfig(double sr) noexcept {
    const double tailSec = plugin_ ? plugin_->getTailLengthSeconds() : 0.0;
    hasTail_.store(plugin_ != nullptr && tailSec > 0.0, std::memory_order_relaxed);
    gate_.prepare(sr);
    gate_.setPluginTailSeconds(tailSec);
}

int32_t PluginEffect::latencySamples() const {
    return live_.load(std::memory_order_acquire) ? plugin_->getLatencySamples() : 0;
}

template <typename PieceFn>
void PluginEffect::runProcessGate(
    bool mainInSilent, int32_t frames, const float* sc, int32_t scFrames, PieceFn&& runPiece) {
    const bool scInActive = hasSidechain_ && sc != nullptr && scFrames > 0 &&
                            !dsp::SilenceGate::isBufferSilent(
                                sc, std::min(frames, scFrames) * 2, dsp::SilenceGate::kDefaultReleaseThreshold);
    bool skip = false;
    const uint32_t gateEpoch = gate_.beginBlock(&skip);
    if (skip && mainInSilent && !scInActive) return;

    bool outputSilent = true;
    const double sr = plugin_->getSampleRate();
    for (int32_t off = 0; off < frames;) {
        const int32_t n = juce::jmin(frames - off, maxBlock_);
        if (!runPiece(off, n, sc != nullptr ? sc + static_cast<size_t>(off) * 2u : nullptr, scFrames - off)) {
            outputSilent = false;
        }
        off += n;
        if (off < frames) advanceTransport(playHead_.info, n, sr);
    }
    gate_.observe(!mainInSilent || scInActive || !outputSilent, frames, gateEpoch);
}

void PluginEffect::process(float* buf, int32_t frames) {
    const float* sc = scBuf_;
    const int32_t scFrames = scFrames_;
    scBuf_ = nullptr;
    scFrames_ = 0;
    if (frames <= 0 || !live_.load(std::memory_order_acquire)) return;

    const bool mainInSilent =
        dsp::SilenceGate::isBufferSilent(buf, frames * 2, dsp::SilenceGate::kDefaultReleaseThreshold);
    runProcessGate(mainInSilent, frames, sc, scFrames, [&](int32_t off, int32_t n, const float* scPtr, int32_t scRem) {
        return processPiece(buf + static_cast<size_t>(off) * 2u, n, scPtr, scRem);
    });
}

void PluginEffect::processPlanar(dsp::PlanarAudioSpan io, int32_t frames) {
    const float* sc = scBuf_;
    const int32_t scFrames = scFrames_;
    scBuf_ = nullptr;
    scFrames_ = 0;
    if (frames <= 0 || !live_.load(std::memory_order_acquire) || !io.left() || !io.right()) return;

    const bool mainInSilent =
        dsp::SilenceGate::isBufferSilent(io.left(), frames, dsp::SilenceGate::kDefaultReleaseThreshold) &&
        dsp::SilenceGate::isBufferSilent(io.right(), frames, dsp::SilenceGate::kDefaultReleaseThreshold);
    runProcessGate(mainInSilent, frames, sc, scFrames, [&](int32_t off, int32_t n, const float* scPtr, int32_t scRem) {
        return processPlanarPiece(io.left() + off, io.right() + off, n, scPtr, scRem);
    });
}

void PluginEffect::invokePluginBlock(int32_t frames, const float* scBuf, int32_t scFrames, float* (&ptrs)[kMaxChans],
    const float*& outL, const float*& outR) {
    const int nc = juce::jmin(storage_.getNumChannels(), kMaxChans);
    if (hasSidechain_ && scBuf != nullptr && scFrames >= frames && scChanOffset_ >= 0) {
        const float scGain = math::dbToLin(sidechainGainDb());
        if (scNumChans_ >= 2 && scChanOffset_ + 1 < nc) {
            float* sl = storage_.getWritePointer(scChanOffset_);
            float* sr = storage_.getWritePointer(scChanOffset_ + 1);
            for (int i = 0; i < frames; ++i) {
                sl[i] = scBuf[i * 2] * scGain;
                sr[i] = scBuf[i * 2 + 1] * scGain;
            }
        } else if (scNumChans_ == 1 && scChanOffset_ < nc) {
            float* sm = storage_.getWritePointer(scChanOffset_);
            for (int i = 0; i < frames; ++i) sm[i] = 0.5f * (scBuf[i * 2] + scBuf[i * 2 + 1]) * scGain;
        }
    }

    juce::AudioBuffer<float> block(
        ptrs, pluginChannelPtrs(*plugin_, storage_, spare_.data(), frames, chanFloor_, ptrs), frames);
    empty_.clear();
    workgroup_.update();
    plugin_->processBlock(block, empty_);

    outL = block.getReadPointer(0);
    outR = stereoOut_ ? block.getReadPointer(1) : outL;
}

bool PluginEffect::processPiece(float* buf, int32_t frames, const float* scBuf, int32_t scFrames) {
    storage_.clear(0, frames);
    float* l = storage_.getWritePointer(0);
    float* r = stereoIn_ ? storage_.getWritePointer(1) : spare_.data();
    math::deinterleave(l, r, buf, frames);

    float* ptrs[kMaxChans];
    const float* ol = nullptr;
    const float* orr = nullptr;
    invokePluginBlock(frames, scBuf, scFrames, ptrs, ol, orr);
    math::interleaveWetDryMix(buf, ol, orr, frames, sidechainMix(), 1.0f - sidechainMix());
    return dsp::SilenceGate::isBufferSilent(buf, frames * 2, dsp::SilenceGate::kDefaultReleaseThreshold);
}

bool PluginEffect::processPlanarPiece(float* dstL, float* dstR, int32_t frames, const float* scBuf, int32_t scFrames) {
    storage_.clear(0, frames);
    float* l = storage_.getWritePointer(0);
    std::copy_n(dstL, frames, l);
    if (stereoIn_) {
        float* r = storage_.getWritePointer(1);
        std::copy_n(dstR, frames, r);
    }

    float* ptrs[kMaxChans];
    const float* ol = nullptr;
    const float* orr = nullptr;
    invokePluginBlock(frames, scBuf, scFrames, ptrs, ol, orr);
    math::mixPlanarWetDry(dstL, dstR, ol, orr, frames, sidechainMix(), 1.0f - sidechainMix());
    return dsp::SilenceGate::isBufferSilent(dstL, frames, dsp::SilenceGate::kDefaultReleaseThreshold) &&
           dsp::SilenceGate::isBufferSilent(dstR, frames, dsp::SilenceGate::kDefaultReleaseThreshold);
}

void PluginEffect::openEditor() {
    if (plugin_) editor_.open(*plugin_);
}

void PluginEffect::closeEditor() {
    editor_.close();
}

std::vector<uint8_t> PluginEffect::getState() const {
    return plugin_ ? readState(plugin_.get()) : pendingState_;
}

void PluginEffect::setState(const uint8_t* data, int32_t size) {
    if (plugin_) {
        writeState(plugin_.get(), data, size);
        const double sr = plugin_->getSampleRate() > 0 ? plugin_->getSampleRate() : sr_;
        refreshGateConfig(sr);
    } else if (data != nullptr && size > 0) {
        pendingState_.assign(data, data + size);
    }
}

std::string PluginEffect::pluginIdentifier() const {
    if (plugin_) return plugin_->getPluginDescription().createIdentifierString().toStdString();
    return desc_.name.isNotEmpty() ? desc_.createIdentifierString().toStdString() : std::string{};
}

int32_t PluginEffect::pluginParamCount() const {
    return live_.load(std::memory_order_acquire) ? paramCountImpl(plugin_.get()) : 0;
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
    return live_.load(std::memory_order_acquire) ? paramGetImpl(plugin_.get(), i) : 0.0f;
}
void PluginEffect::pluginParamSet(int32_t i, float v) {
    if (live_.load(std::memory_order_acquire)) {
        paramSetImpl(plugin_.get(), i, v);
        gate_.wake();
    }
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

std::shared_ptr<dsp::Device> PluginEffect::clone() const {
    juce::PluginDescription desc = desc_;
    if (desc.name.isEmpty()) {
        if (!plugin_) return nullptr;
        desc = catalogDescriptionOf(catalog_.lock(), *plugin_);
    }
    auto d = std::make_shared<PluginEffect>(desc, sr_, requestedBlock_, catalog_);
    const auto st = getState();
    if (!st.empty()) d->setState(st.data(), static_cast<int32_t>(st.size()));
    d->setSidechainSourceTrackId(scTrackId_.load(std::memory_order_relaxed));
    d->setSleepMode(sleepMode());
    return d;
}

void PluginEffect::audioProcessorParameterChanged(juce::AudioProcessor*, int index, float) {
    lastTouched_.store(index, std::memory_order_relaxed);
    gate_.wake();
}

void PluginEffect::audioProcessorParameterChangeGestureBegin(juce::AudioProcessor*, int index) {
    gestureBegin_.store(index, std::memory_order_relaxed);
}

void PluginEffect::audioProcessorParameterChangeGestureEnd(juce::AudioProcessor*, int index) {
    gestureEnd_.store(index, std::memory_order_relaxed);
}

} // namespace nota::pluginhost::detail
