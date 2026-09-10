// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Pentad as a standalone VST3 — for debugging the synth in third-party hosts. A thin
// JUCE AudioProcessor over the same header-only DSP the app runs (src/Pentad.h): the
// parameter list (ids, names, defaults) comes from the instrument itself, MIDI notes /
// pitch bend / mod wheel (CC1) / channel pressure are applied sample-accurately by
// rendering between events, the host transport drives the tempo-synced LFO, and the
// plugin state is the instrument's own normalized-parameter blob (the vintage seed
// included), so a preset moves between the app and a host unchanged. The editor is JUCE's
// generic one (requirements § 11.5: the full panel lives in the Avalonia card).
//
// Built only with -DNOTA_BUILD_PENTAD_VST3=ON (see CMakeLists.txt); the app never links it.

#include "Pentad.h"

#include <juce_audio_processors/juce_audio_processors.h>

#include <memory>
#include <vector>

namespace {

class PentadProcessor final : public juce::AudioProcessor {
public:
    PentadProcessor()
        : AudioProcessor(BusesProperties().withOutput("Output", juce::AudioChannelSet::stereo(), true)) {
        synth_ = std::make_unique<nota::Pentad>();
        for (int i = 0; i < nota::Pentad::kNumParams; ++i) {
            auto* p = new juce::AudioParameterFloat(juce::ParameterID{ nota::Pentad::paramId(i), 1 },
                                                    nota::Pentad::paramName(i),
                                                    juce::NormalisableRange<float>(0.0f, 1.0f),
                                                    synth_->pluginParamGet(i));
            params_.push_back(p);
            addParameter(p);
        }
        bendIdx_ = synth_->pluginParamIndexOfId("bend");
        modIdx_ = synth_->pluginParamIndexOfId("modwheel");
        atIdx_ = synth_->pluginParamIndexOfId("aftertouch");
    }

    const juce::String getName() const override { return "Nota Pentad"; }
    bool acceptsMidi() const override { return true; }
    bool producesMidi() const override { return false; }
    bool isMidiEffect() const override { return false; }
    double getTailLengthSeconds() const override { return 4.0; }
    int getNumPrograms() override { return 1; }
    int getCurrentProgram() override { return 0; }
    void setCurrentProgram(int) override {}
    const juce::String getProgramName(int) override { return "Init"; }
    void changeProgramName(int, const juce::String&) override {}
    bool hasEditor() const override { return true; }
    juce::AudioProcessorEditor* createEditor() override { return new juce::GenericAudioProcessorEditor(*this); }

    bool isBusesLayoutSupported(const BusesLayout& layouts) const override {
        return layouts.getMainOutputChannelSet() == juce::AudioChannelSet::stereo();
    }

    void prepareToPlay(double sampleRate, int maximumBlock) override {
        synth_->setSampleRate(sampleRate);
        scratch_.assign(static_cast<size_t>(std::max(1, maximumBlock)) * 2, 0.0f);
    }
    void releaseResources() override {}
    void reset() override { synth_->allNotesOff(); }   // transport stop / re-prepare

    void processBlock(juce::AudioBuffer<float>& buffer, juce::MidiBuffer& midi) override {
        juce::ScopedNoDenormals noDenormals;
        const int n = buffer.getNumSamples();
        buffer.clear();
        if (static_cast<int>(scratch_.size()) < n * 2) scratch_.assign(static_cast<size_t>(n) * 2, 0.0f);
        std::fill(scratch_.begin(), scratch_.begin() + n * 2, 0.0f);

        for (int i = 0; i < nota::Pentad::kNumParams; ++i) synth_->pluginParamSet(i, params_[static_cast<size_t>(i)]->get());

        if (auto* ph = getPlayHead()) {
            if (auto pos = ph->getPosition()) {
                const double bpm = pos->getBpm().orFallback(120.0);
                const double spb = getSampleRate() * 60.0 / std::max(1.0, bpm);
                synth_->setTransport(pos->getPpqPosition().orFallback(0.0), spb, pos->getIsPlaying());
            }
        }

        int done = 0;
        for (const auto meta : midi) {
            const auto m = meta.getMessage();
            const int at = juce::jlimit(0, n, meta.samplePosition);
            if (at > done) { synth_->render(scratch_.data() + done * 2, at - done); done = at; }
            if (m.isNoteOn()) synth_->noteOn(m.getNoteNumber(), m.getFloatVelocity());
            else if (m.isNoteOff()) synth_->noteOff(m.getNoteNumber());
            else if (m.isAllNotesOff() || m.isAllSoundOff()) synth_->allNotesOff();
            else if (m.isPitchWheel()) setController(bendIdx_, m.getPitchWheelValue() / 16383.0f);
            else if (m.isControllerOfType(1)) setController(modIdx_, m.getControllerValue() / 127.0f);
            else if (m.isChannelPressure()) setController(atIdx_, m.getChannelPressureValue() / 127.0f);
            else if (m.isAftertouch()) setController(atIdx_, m.getAfterTouchValue() / 127.0f);
        }
        if (done < n) synth_->render(scratch_.data() + done * 2, n - done);

        auto* l = buffer.getWritePointer(0);
        auto* r = buffer.getWritePointer(1);
        for (int i = 0; i < n; ++i) { l[i] = scratch_[static_cast<size_t>(i) * 2]; r[i] = scratch_[static_cast<size_t>(i) * 2 + 1]; }
    }

    void getStateInformation(juce::MemoryBlock& dest) override {
        for (int i = 0; i < nota::Pentad::kNumParams; ++i) synth_->pluginParamSet(i, params_[static_cast<size_t>(i)]->get());
        const auto blob = synth_->getState();
        dest.replaceAll(blob.data(), blob.size());
    }
    void setStateInformation(const void* data, int size) override {
        synth_->setState(static_cast<const uint8_t*>(data), size);
        for (int i = 0; i < nota::Pentad::kNumParams; ++i)
            params_[static_cast<size_t>(i)]->setValueNotifyingHost(synth_->pluginParamGet(i));
    }

private:
    // Wheels / pressure are plugin params in Pentad; MIDI moves the host-visible param too.
    void setController(int idx, float v) {
        if (idx < 0) return;
        synth_->pluginParamSet(idx, v);
        *params_[static_cast<size_t>(idx)] = v;
    }

    std::unique_ptr<nota::Pentad> synth_;
    std::vector<juce::AudioParameterFloat*> params_;
    std::vector<float> scratch_;
    int bendIdx_ = -1, modIdx_ = -1, atIdx_ = -1;
};

} // namespace

juce::AudioProcessor* JUCE_CALLTYPE createPluginFilter() { return new PentadProcessor(); }
