// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Deterministic real VST3 fixture for the hosted render pool. Independent instances
// own all DSP state. Expensive, bounded DSP makes missed scheduling measurable;
// a reentrant process call poisons the output instead of silently passing a test.

#include <juce_audio_processors/juce_audio_processors.h>
#include <array>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <limits>

class RenderProbe final : public juce::AudioProcessor {
  public:
    RenderProbe() : AudioProcessor(probeBuses()) {
        addParameter(work_ = new juce::AudioParameterInt({"work", 1}, "Work", 0, 4096, 0));
        addParameter(gain_ = new juce::AudioParameterFloat({"gain", 1}, "Gain", 0.0f, 1.0f, 0.5f));
    }
    const juce::String getName() const override { return "Nota Render Probe"; }
    void prepareToPlay(double rate, int) override {
        rate_ = rate; phase_ = 0; state_ = 0.1; notes_.fill(false);
    }
    void releaseResources() override {}
    bool acceptsMidi() const override { return true; }
    bool producesMidi() const override { return false; }
    bool isMidiEffect() const override { return false; }
    double getTailLengthSeconds() const override { return 0; }
    bool hasEditor() const override { return false; }
    juce::AudioProcessorEditor* createEditor() override { return nullptr; }
    int getNumPrograms() override { return 1; }
    int getCurrentProgram() override { return 0; }
    void setCurrentProgram(int) override {}
    const juce::String getProgramName(int) override { return {}; }
    void changeProgramName(int, const juce::String&) override {}
    void getStateInformation(juce::MemoryBlock& out) override {
        const float values[]{float(work_->get()), gain_->get()};
        out.replaceAll(values, sizeof values);
    }
    void setStateInformation(const void* data, int size) override {
        if (size != 2 * sizeof(float)) return;
        float values[2]; std::memcpy(values, data, sizeof values);
        *work_ = juce::roundToInt(values[0]); *gain_ = values[1];
    }
    void processBlock(juce::AudioBuffer<float>& buffer, juce::MidiBuffer& midi) override {
        if (processing_.test_and_set(std::memory_order_acquire)) {
            for (int c = 0; c < buffer.getNumChannels(); ++c)
                juce::FloatVectorOperations::fill(buffer.getWritePointer(c),
                    std::numeric_limits<float>::quiet_NaN(), buffer.getNumSamples());
            return;
        }
        const int work = work_->get();
        const float gain = gain_->get();
        auto event = midi.cbegin();
        for (int i = 0; i < buffer.getNumSamples(); ++i) {
            while (event != midi.cend() && (*event).samplePosition <= i) {
                const auto msg = (*event).getMessage();
                if (msg.isNoteOn()) { notes_[(size_t)msg.getNoteNumber()] = true; pitch_ = msg.getNoteNumber(); }
                if (msg.isNoteOff()) notes_[(size_t)msg.getNoteNumber()] = false;
                if (msg.isAllNotesOff() || msg.isAllSoundOff()) notes_.fill(false);
                ++event;
            }
            // Depend on the result so Release optimizers must retain the DSP work.
            for (int k = 0; k < work; ++k) state_ = std::sin(state_ + 0.01);
            const bool sounding = std::any_of(notes_.begin(), notes_.end(), [](bool n) { return n; });
            const float tone = sounding ? float(0.04 * std::sin(phase_)) : 0.0f;
            phase_ += 6.283185307179586 * juce::MidiMessage::getMidiNoteInHertz(pitch_) / rate_;
            if (phase_ >= 6.283185307179586) phase_ -= 6.283185307179586;
            const float side = buffer.getNumChannels() > 2 ? buffer.getReadPointer(2)[i] * 0.1f : 0.0f;
            for (int c = 0; c < std::min(2, buffer.getNumChannels()); ++c)
                buffer.getWritePointer(c)[i] = buffer.getReadPointer(c)[i] * gain
                    + tone * gain + side + float(state_ * 1e-9);
        }
        // The input-sidechain channels share these output slots. Leave finite
        // data behind: a later callback must initialize its active input range,
        // including after a larger block. Leakage is audible on the main output.
        for (int c = 2; c < buffer.getNumChannels(); ++c)
            juce::FloatVectorOperations::fill(buffer.getWritePointer(c), 0.125f, buffer.getNumSamples());
        processing_.clear(std::memory_order_release);
    }
  private:
    static BusesProperties probeBuses() {
        auto buses = BusesProperties()
            .withInput("Input", juce::AudioChannelSet::stereo(), true)
            .withInput("Sidechain", juce::AudioChannelSet::stereo(), false)
            .withOutput("Output", juce::AudioChannelSet::stereo(), true);
        // A typical large sampler's 28 stereo outputs exercise the host's
        // reserved-vs-active clearing cost without a commercial dependency.
        for (int i = 1; i < 28; ++i)
            buses = buses.withOutput("Aux " + juce::String(i), juce::AudioChannelSet::stereo(), true);
        return buses;
    }
    juce::AudioParameterInt* work_ = nullptr;
    juce::AudioParameterFloat* gain_ = nullptr;
    std::atomic_flag processing_ = ATOMIC_FLAG_INIT;
    std::array<bool, 128> notes_{};
    double rate_ = 48000, phase_ = 0, state_ = 0.1;
    int pitch_ = 60;
};

juce::AudioProcessor* JUCE_CALLTYPE createPluginFilter() { return new RenderProbe(); }
