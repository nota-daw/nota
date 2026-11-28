// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "hosting/PluginInstrument.h"

#include "hosting/PluginEditorWindow.h"
#include "hosting/PluginCommon.h"
#include "hosting/PluginLoader.h"
#include "nota/dsp/EditorPreview.h"
#include "nota/dsp/PluginContracts.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/rt/MidiControllerTypes.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/math/StereoOps.h"

#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <string>
#include <memory>
#include <utility>
#include <vector>

namespace nota::pluginhost::detail {
namespace {

constexpr int kMaxBlockArt = 256;

} // namespace

PluginInstrument::PluginInstrument(
    std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock, std::weak_ptr<dsp::IPluginCatalog> catalog)
    : PluginInstrument(juce::PluginDescription{}, sr, maxBlock, std::move(catalog)) {
    if (p) attach(std::move(p));
}

PluginInstrument::PluginInstrument(
    const juce::PluginDescription& desc, double sr, int maxBlock, std::weak_ptr<dsp::IPluginCatalog> catalog)
    : desc_(desc), catalog_(std::move(catalog)), sr_(sr), maxBlock_(juce::jmin(maxBlock, kPluginMaxBlock)) {
    constexpr int kArtBytes = kMaxBlockArt * 9;
    pending_.ensureSize(8192 + kArtBytes);
    piece_.ensureSize(8192 + kArtBytes);
    name_ = desc.name.toStdString();
    editor_.onNoteOn = [this](int32_t pitch, float vel) {
        guiNotes_.push({true, pitch, vel});
        gate_.wake();
    };
    editor_.onNoteOff = [this](int32_t pitch) {
        guiNotes_.push({false, pitch, 0.0f});
        gate_.wake();
    };
    editor_.onPreview = [this](
                            std::vector<uint8_t> jpeg) { editorPreview = dsp::EditorPreview::make(std::move(jpeg)); };
    editor_.onOpenChanged = [this](bool open) { gate_.setHoldAwake(open); };
    if (desc.name.isEmpty()) return;
    load_ = PluginLoader::instance().request(desc_, [this](std::string& error) {
        auto inst = createPluginInstance(desc_, sr_, maxBlock_, &error);
        return inst != nullptr && attach(std::move(inst));
    });
}

PluginInstrument::~PluginInstrument() {
    if (load_) PluginLoader::instance().cancel(load_);
    if (plugin_) plugin_->removeListener(this);
}

bool PluginInstrument::attach(std::unique_ptr<juce::AudioPluginInstance> p) {
    if (!p || plugin_) return false;
    plugin_ = std::move(p);
    name_ = plugin_->getName().toStdString();
    prepare(sr_);
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

int32_t PluginInstrument::pluginLoadState() const {
    return load_ ? static_cast<int32_t>(load_->state()) : 0;
}

int32_t PluginInstrument::pluginLoadReasonCode() const {
    return load_ ? static_cast<int32_t>(load_->reason()) : 0;
}

std::string PluginInstrument::pluginLoadReasonArg() const {
    return load_ ? load_->reasonArg() : std::string{};
}

void PluginInstrument::pluginLoadAction(int32_t action) {
    if (load_) PluginLoader::instance().act(load_, static_cast<LoadAction>(action));
}

void PluginInstrument::setSampleRate(double sr) {
    sr_ = sr;
    if (plugin_) prepare(sr);
}

void PluginInstrument::trackNoteOn(int32_t pitch, float velocity) noexcept {
    if (velocity <= 0.0f) {
        trackNoteOff(pitch);
        return;
    }
    if (pitch < 0 || pitch >= 128) return;
    if (noteCounts_[pitch] < 255 && noteCounts_[pitch]++ == 0) ++heldNotesCount_;
}

void PluginInstrument::trackNoteOff(int32_t pitch) noexcept {
    if (pitch < 0 || pitch >= 128) return;
    if (noteCounts_[pitch] > 0 && --noteCounts_[pitch] == 0) --heldNotesCount_;
}

void PluginInstrument::noteOn(int32_t pitch, float velocity) {
    pending_.addEvent(juce::MidiMessage::noteOn(chan(), pitch, velocity), eventOffset);
    trackNoteOn(pitch, velocity);
    gate_.wake();
}

void PluginInstrument::noteOff(int32_t pitch) {
    pending_.addEvent(juce::MidiMessage::noteOff(chan(), pitch), eventOffset);
    trackNoteOff(pitch);
    gate_.wake();
}

void PluginInstrument::allNotesOff() {
    for (int c = 1; c <= 16; ++c) {
        if (((usedChannels_ >> (c - 1)) & 1u) != 0u) pending_.addEvent(juce::MidiMessage::allNotesOff(c), eventOffset);
    }
    heldNotesCount_ = 0;
    std::memset(noteCounts_, 0, sizeof(noteCounts_));
    sustainDown_ = false;
    gate_.wake();
}

void PluginInstrument::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch >= 0 && dim == 1) {
        pending_.addEvent(juce::MidiMessage::aftertouchChange(chan(), pitch, u7(value)), eventOffset);
    }
    gate_.wake();
}

void PluginInstrument::programChange(int32_t program) {
    if (program >= 0 && program < 128) {
        pending_.addEvent(juce::MidiMessage::programChange(chan(), program), eventOffset);
    }
    gate_.wake();
}

void PluginInstrument::controller(int32_t ctl, float value) {
    if (ctl == dsp::kCtlSustain) sustainDown_ = (value >= 0.5f);
    if (ctl == dsp::kCtlPitchWheel) {
        pending_.addEvent(juce::MidiMessage::pitchWheel(
                              chan(), juce::jlimit(0, 16383, static_cast<int>(std::lround((value + 1.0f) * 8191.5f)))),
            eventOffset);
    } else if (ctl == dsp::kCtlChannelPressure) {
        pending_.addEvent(juce::MidiMessage::channelPressureChange(chan(), u7(value)), eventOffset);
    } else if (ctl >= 0 && ctl < 128) {
        pending_.addEvent(juce::MidiMessage::controllerEvent(chan(), ctl, u7(value)), eventOffset);
    }
    gate_.wake();
}

void PluginInstrument::render(float* out, int32_t frames) {
    if (!live_.load(std::memory_order_acquire)) {
        for (GuiNote g; guiNotes_.pop(g);) {}
        pending_.clear();
        return;
    }
    if (frames <= 0) return;

    for (GuiNote g; guiNotes_.pop(g);) {
        pending_.addEvent(
            g.on ? juce::MidiMessage::noteOn(1, g.pitch, g.velocity) : juce::MidiMessage::noteOff(1, g.pitch), 0);
        if (g.on) trackNoteOn(g.pitch, g.velocity);
        else
            trackNoteOff(g.pitch);
    }

    bool skip = false;
    const uint32_t gateEpoch = gate_.beginBlock(&skip);
    if (skip && pending_.isEmpty()) return;

    bool allPiecesSilent = true;
    if (frames <= maxBlock_) {
        allPiecesSilent = renderPiece(out, frames, pending_);
    } else {
        const double sr = plugin_->getSampleRate();
        for (int32_t off = 0; off < frames; off += maxBlock_) {
            const int32_t n = juce::jmin(frames - off, maxBlock_);
            const bool last = off + n >= frames;
            piece_.clear();
            for (const auto m : pending_) {
                const int pos = juce::jmax(0, m.samplePosition);
                if (pos >= off && (pos < off + n || last)) {
                    piece_.addEvent(m.data, m.numBytes, juce::jmin(pos - off, n - 1));
                }
            }
            if (!renderPiece(out + static_cast<size_t>(off) * 2u, n, piece_)) allPiecesSilent = false;
            advanceTransport(playHead_.info, n, sr);
        }
    }
    pending_.clear();
    gate_.observe(heldNotesCount_ > 0 || sustainDown_ || !allPiecesSilent, frames, gateEpoch);
}

bool PluginInstrument::renderPiece(float* out, int32_t frames, juce::MidiBuffer& midi) {
    storage_.clear(0, frames);
    float* ptrs[kMaxChans];
    juce::AudioBuffer<float> block(
        ptrs, pluginChannelPtrs(*plugin_, storage_, spare_.data(), frames, chanFloor_, ptrs), frames);
    workgroup_.update();
    plugin_->processBlock(block, midi);
    const float* l = block.getReadPointer(0);
    const float* r = stereoOut_ ? block.getReadPointer(1) : l;
    const bool silent =
        dsp::SilenceGate::isBufferSilent(l, frames, dsp::SilenceGate::kDefaultReleaseThreshold) &&
        (!stereoOut_ || dsp::SilenceGate::isBufferSilent(r, frames, dsp::SilenceGate::kDefaultReleaseThreshold));
    math::interleaveAdd(out, l, r, frames);
    return silent;
}

void PluginInstrument::openEditor() {
    if (plugin_) editor_.open(*plugin_);
}

void PluginInstrument::closeEditor() {
    editor_.close();
}

std::vector<uint8_t> PluginInstrument::getState() const {
    return plugin_ ? readState(plugin_.get()) : pendingState_;
}

void PluginInstrument::setState(const uint8_t* data, int32_t size) {
    if (plugin_) {
        writeState(plugin_.get(), data, size);
        const double sr = plugin_->getSampleRate() > 0 ? plugin_->getSampleRate() : sr_;
        refreshGateConfig(sr);
    } else if (data != nullptr && size > 0) {
        pendingState_.assign(data, data + size);
    }
}

std::string PluginInstrument::pluginIdentifier() const {
    if (plugin_) return plugin_->getPluginDescription().createIdentifierString().toStdString();
    return desc_.name.isNotEmpty() ? desc_.createIdentifierString().toStdString() : std::string{};
}

int32_t PluginInstrument::latencySamples() const {
    return live_.load(std::memory_order_acquire) ? plugin_->getLatencySamples() : 0;
}

int32_t PluginInstrument::pluginParamCount() const {
    return live_.load(std::memory_order_acquire) ? paramCountImpl(plugin_.get()) : 0;
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
    return live_.load(std::memory_order_acquire) ? paramGetImpl(plugin_.get(), i) : 0.0f;
}
void PluginInstrument::pluginParamSet(int32_t i, float v) {
    if (live_.load(std::memory_order_acquire)) {
        paramSetImpl(plugin_.get(), i, v);
        gate_.wake();
    }
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

std::shared_ptr<dsp::Instrument> PluginInstrument::clone() const {
    juce::PluginDescription desc = desc_;
    if (desc.name.isEmpty()) {
        if (!plugin_) return nullptr;
        desc = catalogDescriptionOf(catalog_.lock(), *plugin_);
    }
    auto c = std::make_shared<PluginInstrument>(desc, sr_, maxBlock_, catalog_);
    const auto st = getState();
    if (!st.empty()) c->setState(st.data(), static_cast<int32_t>(st.size()));
    c->setSleepMode(sleepMode());
    return c;
}

void PluginInstrument::audioProcessorParameterChanged(juce::AudioProcessor*, int index, float) {
    lastTouched_.store(index, std::memory_order_relaxed);
    gate_.wake();
}

void PluginInstrument::audioProcessorParameterChangeGestureBegin(juce::AudioProcessor*, int index) {
    gestureBegin_.store(index, std::memory_order_relaxed);
}

void PluginInstrument::audioProcessorParameterChangeGestureEnd(juce::AudioProcessor*, int index) {
    gestureEnd_.store(index, std::memory_order_relaxed);
}

void PluginInstrument::prepare(double sr) {
    if (plugin_->getBusCount(false) > 0) plugin_->setChannelLayoutOfBus(false, 0, juce::AudioChannelSet::stereo());
    enableEveryBus(*plugin_);
    logBuses(*plugin_);
    plugin_->setRateAndBufferSizeDetails(sr, maxBlock_);
    plugin_->prepareToPlay(sr, maxBlock_);
    plugin_->setPlayHead(&playHead_);
    workgroup_.bind(*plugin_);
    storage_.setSize(
        juce::jmax(2, plugin_->getTotalNumInputChannels(), plugin_->getTotalNumOutputChannels()), maxBlock_);
    stereoOut_ = plugin_->getBusCount(false) > 0 && plugin_->getChannelCountOfBus(false, 0) > 1;
    spare_.assign(static_cast<size_t>(maxBlock_), 0.0f);
    chanFloor_ = minProcessChannels(*plugin_);
    refreshGateConfig(sr);
}

void PluginInstrument::refreshGateConfig(double sr) noexcept {
    const double tailSec = plugin_ ? plugin_->getTailLengthSeconds() : 0.0;
    hasTail_.store(plugin_ != nullptr && tailSec > 0.0, std::memory_order_relaxed);
    gate_.prepare(sr);
    gate_.setPluginTailSeconds(tailSec);
}

} // namespace nota::pluginhost::detail
