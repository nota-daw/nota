// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginCommon — shared hosting internals for PluginInstrument/PluginEffect
// . Channel-pointer mapping, bus enabling, state
// transfer, hosted-parameter access, and catalog instantiation. JUCE lives
// behind this header; the core engine never includes it.

#pragma once

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include <juce_audio_processors/juce_audio_processors.h>

namespace juce {
class PluginDescription;
} // namespace juce

namespace nota {
namespace hosting {

constexpr int kMaxChans = 64;
// Channels a referring juce::AudioBuffer holds without allocating (its preallocated
// space is 32 pointers, one of them the null terminator).
constexpr int kNoAllocChans = 31;

// Channel pointers for one processBlock: `storage`'s channels, then every further channel
// the plugin may address pointing at `spare` (one zeroed, discarded channel). A VST3 plugin
// can widen its layout at any moment, so the buffer is never narrower than its current
// layout, with headroom for a change that lands between this read and processBlock. That
// headroom is VST3-only (`minChans` from minProcessChannels): the AU wrapper copies the
// buffer into one sized for the layout it was prepared with, so a wider buffer asserts
// and reallocates there — an AU gets exactly its layout. Audio thread.
int pluginChannelPtrs(const juce::AudioProcessor& p, juce::AudioBuffer<float>& storage, float* spare, int frames,
    int minChans, float* (&ptrs)[kMaxChans]);

// The floor pluginChannelPtrs gets for this plugin. Message thread, at prepare.
int minProcessChannels(const juce::AudioPluginInstance& p);

// Turn on every bus the plugin has, keeping main buses as set. Message thread.
void enableEveryBus(juce::AudioPluginInstance& p);

// Bus layout on stderr when NOTA_DEBUG_PLUGIN_BUSES is set. Message thread.
void logBuses(const juce::AudioPluginInstance& p);

// Opaque plugin state <-> byte vector (M3-5).
std::vector<uint8_t> readState(juce::AudioPluginInstance* p);
void writeState(juce::AudioPluginInstance* p, const uint8_t* data, int32_t size);

// Hosted-plugin parameter access (M9-B): normalized 0..1 values keyed by stable
// hosted paramID. Audio-thread-safe push via setValue (no host notification).
int32_t paramCountImpl(juce::AudioPluginInstance* p);
std::string paramIdImpl(juce::AudioPluginInstance* p, int32_t i);
std::string paramNameImpl(juce::AudioPluginInstance* p, int32_t i);
std::string paramTextImpl(juce::AudioPluginInstance* p, int32_t i);
float paramDefaultImpl(juce::AudioPluginInstance* p, int32_t i);
float paramGetImpl(juce::AudioPluginInstance* p, int32_t i);
void paramSetImpl(juce::AudioPluginInstance* p, int32_t i, float v);
int32_t paramIndexOfIdImpl(juce::AudioPluginInstance* p, const std::string& id);

// Shared plugin format manager (VST3 everywhere, AU on macOS). Message thread.
juce::AudioPluginFormatManager& sharedFormatManager();

// Instantiate a full description in-process (message thread). Null on failure.
std::unique_ptr<juce::AudioPluginInstance> createInstance(const juce::PluginDescription& desc, double sr, int maxBlock);

// Instantiate catalog entry `catalogIndex` in-process (message thread).
std::unique_ptr<juce::AudioPluginInstance> instantiateCatalog(int catalogIndex, double sr, int maxBlock);

} // namespace hosting
} // namespace nota
