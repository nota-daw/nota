// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginCommon — shared JUCE hosting helpers (channel buffers, workgroup link, params, state, catalog lookup).

#pragma once

#include "nota/dsp/PluginContracts.h"
#include "nota/dsp/rt/TransportInfo.h"

#include <juce_audio_processors/juce_audio_processors.h>

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::pluginhost::detail {

constexpr int kMaxChans = 64;
constexpr int kNoAllocChans = 31;
constexpr int kPluginMaxBlock = 4096;

int pluginChannelPtrs(const juce::AudioProcessor& p, juce::AudioBuffer<float>& storage, float* spare, int frames,
    int minChans, float* (&ptrs)[kMaxChans]);

int minProcessChannels(const juce::AudioPluginInstance& p);
bool isParallelRenderSafe(const juce::AudioPluginInstance* p);
void enableEveryBus(juce::AudioPluginInstance& p);
void logBuses(const juce::AudioPluginInstance& p);

class WorkgroupLink {
  public:
    WorkgroupLink() = default;
    WorkgroupLink(const WorkgroupLink&) = delete;
    WorkgroupLink& operator=(const WorkgroupLink&) = delete;
    ~WorkgroupLink();
    void bind(juce::AudioPluginInstance& p);
    void update();

  private:
    void* observer_ = nullptr;
    uint32_t seen_ = 0;
};

void setCurrentAudioWorkgroup(void* osWorkgroup);

std::vector<uint8_t> readState(juce::AudioPluginInstance* p);
void writeState(juce::AudioPluginInstance* p, const uint8_t* data, int32_t size);

int32_t paramCountImpl(juce::AudioPluginInstance* p);
std::string paramIdImpl(juce::AudioPluginInstance* p, int32_t i);
std::string paramNameImpl(juce::AudioPluginInstance* p, int32_t i);
std::string paramTextImpl(juce::AudioPluginInstance* p, int32_t i);
float paramDefaultImpl(juce::AudioPluginInstance* p, int32_t i);
float paramGetImpl(juce::AudioPluginInstance* p, int32_t i);
void paramSetImpl(juce::AudioPluginInstance* p, int32_t i, float v);
int32_t paramIndexOfIdImpl(juce::AudioPluginInstance* p, const std::string& id);

void advanceTransport(dsp::TransportInfo& ti, int32_t frames, double sr);

bool juceDescFromDescriptor(const dsp::PluginDescriptor& pd, juce::PluginDescription& out);
bool catalogDescriptionByIndex(const std::shared_ptr<dsp::IPluginCatalog>& catalog, int32_t catalogIndex,
    bool instrumentOnly, juce::PluginDescription& out);
bool catalogDescriptionById(const std::shared_ptr<dsp::IPluginCatalog>& catalog, const std::string& identifier,
    bool instrumentOnly, juce::PluginDescription& out);
juce::PluginDescription catalogDescriptionOf(
    const std::shared_ptr<dsp::IPluginCatalog>& catalog, const juce::AudioPluginInstance& p);

std::unique_ptr<juce::AudioPluginInstance> createPluginInstance(
    const juce::PluginDescription& desc, double sr, int maxBlock, std::string* error = nullptr);

} // namespace nota::pluginhost::detail
