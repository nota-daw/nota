// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// IPluginFormatHost — internal dispatch abstraction over in-process JUCE plugin formats.

#pragma once

#include <juce_audio_processors/juce_audio_processors.h>

#include <memory>
#include <string>

namespace nota::pluginhost::detail {

class IPluginFormatHost {
  public:
    virtual ~IPluginFormatHost() = default;

    virtual std::unique_ptr<juce::AudioPluginInstance> createInstance(
        const juce::PluginDescription& desc, double sampleRate, int maxBlock, std::string* error) const = 0;

    virtual bool preloadBinary(const juce::PluginDescription& desc, std::string& error) const = 0;

    virtual bool isParallelRenderSafe(const juce::AudioPluginInstance* instance) const = 0;

    virtual int minProcessChannels(const juce::AudioPluginInstance& instance, int noAllocChans) const = 0;
};

IPluginFormatHost& sharedFormatHost();

} // namespace nota::pluginhost::detail
