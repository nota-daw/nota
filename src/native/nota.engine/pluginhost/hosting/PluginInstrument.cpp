// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginInstrument implementation. render() accumulates via vectorized
// simd_audio::interleaveStereoAdd instead of a scalar sample loop.

#include "PluginInstrument.h"

#include "PluginLoader.h"
#include "engine/ArticulationSchedule.h"
#include "engine/MidiController.h"

#include "dsp/math/SimdAudio.h"

namespace nota {
namespace hosting {

PluginInstrument::PluginInstrument(std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock)
    : PluginInstrument(juce::PluginDescription{}, sr, maxBlock) {
    if (p) attach(std::move(p));
}

PluginInstrument::PluginInstrument(const juce::PluginDescription& desc, double sr, int maxBlock)
    : desc_(desc), sr_(sr), maxBlock_(juce::jmin(maxBlock, kPluginMaxBlock)) {
    // Room reserved up front so the audio thread doesn't grow the buffers. On top of the notes' room,
    // a block's articulation MIDI (specs/085): at most kMaxBlockArt messages, 9 bytes each in a
    // MidiBuffer (sample position, size, 3 data bytes).
    constexpr int kArtBytes = nota::kMaxBlockArt * 9;
    pending_.ensureSize(8192 + kArtBytes);
    piece_.ensureSize(8192 + kArtBytes);
    name_ = desc.name.toStdString(); // the instance's own name replaces it at attach
    // Editor-window computer keyboard -> this plugin (drained on the audio thread).
    editor_.onNoteOn = [this](int32_t pitch, float vel) { guiNotes_.push({true, pitch, vel}); };
    editor_.onNoteOff = [this](int32_t pitch) { guiNotes_.push({false, pitch, 0.0f}); };
    // The window's picture, taken each time it is hidden, goes on the card (specs/046).
    editor_.onPreview = [this](std::vector<uint8_t> jpeg) { editorPreview = nota::EditorPreview::make(std::move(jpeg)); };
    if (desc.name.isEmpty()) return; // the instance comes from the other constructor
    // The ticket's finish runs on the message thread in PluginLoader::service(); the destructor
    // cancels it first, so `this` is alive whenever it runs.
    load_ = PluginLoader::instance().request(desc_, [this](std::string& error) {
        auto inst = createInstance(desc_, sr_, maxBlock_, &error);
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
    name_ = plugin_->getName().toStdString(); // cache for displayName()
    prepare(sr_);
    if (!pendingState_.empty()) {
        writeState(plugin_.get(), pendingState_.data(), (int32_t)pendingState_.size());
        std::vector<uint8_t>().swap(pendingState_);
    }
    plugin_->addListener(this); // "Learn" (M9-B3)
    // VST3 process calls may run on audio workers. All MIDI, channel storage and
    // playhead state belong to this instance; the engine joins before the next
    // block and never processes an instance concurrently. Lifecycle/editor work
    // stays on the message thread. Other formats retain the serial fallback.
    parallelRender_.store(plugin_->getPluginDescription().pluginFormatName == "VST3", std::memory_order_relaxed);
    live_.store(true, std::memory_order_release); // everything above is visible to the audio thread
    return true;
}

int32_t PluginInstrument::pluginLoadState() const {
    return load_ ? static_cast<int32_t>(load_->state()) : 0;
}

std::string PluginInstrument::pluginLoadReason() const {
    return load_ ? load_->reason() : std::string{};
}

void PluginInstrument::pluginLoadAction(int32_t action) {
    if (load_) PluginLoader::instance().act(load_, static_cast<LoadAction>(action));
}

void PluginInstrument::setSampleRate(double sr) {
    sr_ = sr;
    if (plugin_) prepare(sr); // else prepared at attach, for this rate
}

// Every engine-driven event goes out on chan(): channel 1, or an articulation's explicit channel
// (specs/085). The editor window's own keyboard notes (render) stay on channel 1.
void PluginInstrument::noteOn(int32_t pitch, float velocity) {
    pending_.addEvent(juce::MidiMessage::noteOn(chan(), pitch, (float)velocity), eventOffset);
}

void PluginInstrument::noteOff(int32_t pitch) {
    pending_.addEvent(juce::MidiMessage::noteOff(chan(), pitch), eventOffset);
}

void PluginInstrument::allNotesOff() {
    // A flush (stop, seek, a voice cut): channel 1 as always, plus any channel an articulation
    // sent on, so a note held on it can't hang. A plug-in never driven off channel 1 sees one message.
    for (int c = 1; c <= 16; ++c)
        if ((usedChannels_ >> (c - 1)) & 1u) pending_.addEvent(juce::MidiMessage::allNotesOff(c), eventOffset);
}

void PluginInstrument::noteExpression(int32_t pitch, int32_t dim, float value) {
    if (pitch >= 0 && dim == 1) pending_.addEvent(juce::MidiMessage::aftertouchChange(chan(), pitch, u7(value)), eventOffset);
}

void PluginInstrument::programChange(int32_t program) {
    if (program >= 0 && program < 128) pending_.addEvent(juce::MidiMessage::programChange(chan(), program), eventOffset);
}

void PluginInstrument::controller(int32_t ctl, float value) {
    if (ctl == nota::kCtlPitchWheel)
        pending_.addEvent(
            juce::MidiMessage::pitchWheel(chan(), juce::jlimit(0, 16383, (int)std::lround((value + 1.0f) * 8191.5f))), eventOffset);
    else if (ctl == nota::kCtlChannelPressure)
        pending_.addEvent(juce::MidiMessage::channelPressureChange(chan(), u7(value)), eventOffset);
    else if (ctl >= 0 && ctl < 128)
        pending_.addEvent(juce::MidiMessage::controllerEvent(chan(), ctl, u7(value)), eventOffset);
}

void PluginInstrument::render(float* out, int32_t frames) {
    if (!live_.load(std::memory_order_acquire)) { // not loaded yet (or couldn't load): silent
        for (GuiNote g; guiNotes_.pop(g);) {
        }
        pending_.clear();
        return;
    }
    if (frames <= 0) return;
    // Drain computer-keyboard notes from the editor window (message thread) into
    // this block's MIDI buffer — audio thread only touches pending_ here.
    for (GuiNote g; guiNotes_.pop(g);)
        pending_.addEvent(
            g.on ? juce::MidiMessage::noteOn(1, g.pitch, g.velocity) : juce::MidiMessage::noteOff(1, g.pitch), 0);
    if (frames <= maxBlock_) {
        renderPiece(out, frames, pending_);
    } else {
        // Longer than the plug-in was prepared for (#447): in pieces, each with its own events
        // (moved to the piece's start) and the transport where the piece begins.
        const double sr = plugin_->getSampleRate();
        for (int32_t off = 0; off < frames; off += maxBlock_) {
            const int32_t n = juce::jmin(frames - off, maxBlock_);
            const bool last = off + n >= frames;
            piece_.clear();
            for (const auto m : pending_) {
                const int pos = juce::jmax(0, m.samplePosition);
                if (pos >= off && (pos < off + n || last)) piece_.addEvent(m.data, m.numBytes, juce::jmin(pos - off, n - 1));
            }
            renderPiece(out + (size_t)off * 2, n, piece_);
            advanceTransport(playHead_.info, n, sr);
        }
    }
    pending_.clear();
}

void PluginInstrument::renderPiece(float* out, int32_t frames, juce::MidiBuffer& midi) {
    storage_.clear(0, frames); // only what this block uses: a multi-out plug-in has many channels
    float* ptrs[kMaxChans];
    juce::AudioBuffer<float> block(
        ptrs, pluginChannelPtrs(*plugin_, storage_, spare_.data(), frames, chanFloor_, ptrs), frames);
    workgroup_.update();
    plugin_->processBlock(block, midi);
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
    // Before the instance exists the saved state is kept as it came (saved back byte for byte).
    return plugin_ ? readState(plugin_.get()) : pendingState_;
}

void PluginInstrument::setState(const uint8_t* data, int32_t size) {
    if (plugin_) writeState(plugin_.get(), data, size);
    else if (data != nullptr && size > 0) pendingState_.assign(data, data + size);
}

std::string PluginInstrument::pluginIdentifier() const {
    if (plugin_) return plugin_->getPluginDescription().createIdentifierString().toStdString();
    return desc_.name.isNotEmpty() ? desc_.createIdentifierString().toStdString() : std::string{};
}

int32_t PluginInstrument::latencySamples() const {
    return live_.load(std::memory_order_acquire) ? plugin_->getLatencySamples() : 0;
}

bool PluginInstrument::hasTail() const {
    return live_.load(std::memory_order_acquire) && plugin_->getTailLengthSeconds() > 0.0;
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
// Get / set also run block-rate on the audio thread (automation): only once the instance is live.
float PluginInstrument::pluginParamGet(int32_t i) const {
    return live_.load(std::memory_order_acquire) ? paramGetImpl(plugin_.get(), i) : 0.0f;
}
void PluginInstrument::pluginParamSet(int32_t i, float v) {
    if (live_.load(std::memory_order_acquire)) paramSetImpl(plugin_.get(), i, v);
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
    // The copy loads like any insert (specs/113): unloaded, with this one's state to apply.
    // The catalog entry that loaded this one is the description to load again (#454).
    juce::PluginDescription desc = desc_;
    if (desc.name.isEmpty()) {
        if (!plugin_) return nullptr;
        desc = catalogDescriptionOf(*plugin_);
    }
    auto c = std::make_shared<PluginInstrument>(desc, sr_, maxBlock_);
    const auto st = getState();
    if (!st.empty()) c->setState(st.data(), (int32_t)st.size());
    return c;
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
    workgroup_.bind(*plugin_);
    // In and out share the buffer, so it spans the wider side.
    storage_.setSize(
        juce::jmax(2, plugin_->getTotalNumInputChannels(), plugin_->getTotalNumOutputChannels()), maxBlock_);
    stereoOut_ = plugin_->getBusCount(false) > 0 && plugin_->getChannelCountOfBus(false, 0) > 1;
    spare_.assign((size_t)maxBlock_, 0.0f);
    chanFloor_ = minProcessChannels(*plugin_);
}

} // namespace hosting
} // namespace nota
