// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginCommon implementation.
// instantiate() now reads the thread-safe PluginCatalog instead of the former
// function-local statics.

#include "PluginCommon.h"

#include "scanner/PluginCatalog.h"

#include <cstdio>

namespace nota {
namespace hosting {

int pluginChannelPtrs(const juce::AudioProcessor& p, juce::AudioBuffer<float>& storage, float* spare, int frames,
    int minChans, float* (&ptrs)[kMaxChans]) {
    const int have = juce::jmin(storage.getNumChannels(), kMaxChans);
    const int want = juce::jmin(kMaxChans, juce::jmax(minChans > 0 ? juce::jmax(minChans, have) : 1,
                                               p.getTotalNumInputChannels(), p.getTotalNumOutputChannels()));
    if (want > have) juce::FloatVectorOperations::clear(spare, frames);
    for (int c = 0; c < want; ++c) ptrs[c] = c < have ? storage.getWritePointer(c) : spare;
    return want;
}

// The floor pluginChannelPtrs gets for this plugin. Message thread, at prepare.
int minProcessChannels(const juce::AudioPluginInstance& p) {
    return p.getPluginDescription().pluginFormatName == "VST3" ? kNoAllocChans : 0;
}

void enableEveryBus(juce::AudioPluginInstance& p) {
    if (p.enableAllBuses()) return;
    for (const bool isInput : {true, false}) // all-or-nothing failed: take what it accepts
        for (int i = 1; i < p.getBusCount(isInput); ++i)
            if (auto* bus = p.getBus(isInput, i); bus != nullptr && !bus->isEnabled()) bus->enable(true);
}

void logBuses(const juce::AudioPluginInstance& p) {
    static const bool dbg = std::getenv("NOTA_DEBUG_PLUGIN_BUSES") != nullptr;
    if (!dbg) return;
    for (const bool isInput : {true, false})
        for (int i = 0; i < p.getBusCount(isInput); ++i)
            if (auto* bus = p.getBus(isInput, i))
                std::fprintf(stderr, "[nota buses] %s %s %d '%s' chans=%d enabled=%d\n", p.getName().toRawUTF8(),
                    isInput ? "in" : "out", i, bus->getName().toRawUTF8(), bus->getLastEnabledLayout().size(),
                    (int)bus->isEnabled());
}

std::vector<uint8_t> readState(juce::AudioPluginInstance* p) {
    if (p == nullptr) return {};
    juce::MemoryBlock mb;
    p->getStateInformation(mb);
    const auto* bytes = static_cast<const uint8_t*>(mb.getData());
    return std::vector<uint8_t>(bytes, bytes + mb.getSize());
}

void writeState(juce::AudioPluginInstance* p, const uint8_t* data, int32_t size) {
    if (p != nullptr && data != nullptr && size > 0) p->setStateInformation(data, size);
}

int32_t paramCountImpl(juce::AudioPluginInstance* p) {
    return p ? p->getParameters().size() : 0;
}
std::string paramIdImpl(juce::AudioPluginInstance* p, int32_t i) {
    if (p == nullptr) return {};
    if (auto* hp = p->getHostedParameter(i)) return hp->getParameterID().toStdString();
    return {};
}
std::string paramNameImpl(juce::AudioPluginInstance* p, int32_t i) {
    if (p == nullptr) return {};
    const auto& params = p->getParameters();
    if (i < 0 || i >= params.size()) return {};
    return params[i]->getName(64).toStdString();
}
std::string paramTextImpl(juce::AudioPluginInstance* p, int32_t i) {
    if (p == nullptr) return {};
    const auto& params = p->getParameters();
    if (i < 0 || i >= params.size()) return {};
    auto* prm = params[i];
    juce::String text = prm->getCurrentValueAsText().trim();
    juce::String unit = prm->getLabel().trim();
    if (text.isNotEmpty() && unit.isNotEmpty() && !text.endsWithIgnoreCase(unit)) text << " " << unit;
    return text.toStdString();
}
float paramDefaultImpl(juce::AudioPluginInstance* p, int32_t i) {
    if (p == nullptr) return 0.0f;
    const auto& params = p->getParameters();
    if (i < 0 || i >= params.size()) return 0.0f;
    return params[i]->getDefaultValue();
}
float paramGetImpl(juce::AudioPluginInstance* p, int32_t i) {
    if (p == nullptr) return 0.0f;
    const auto& params = p->getParameters();
    if (i < 0 || i >= params.size()) return 0.0f;
    return params[i]->getValue();
}
void paramSetImpl(juce::AudioPluginInstance* p, int32_t i, float v) {
    if (p == nullptr) return;
    const auto& params = p->getParameters();
    if (i < 0 || i >= params.size()) return;
    // Audio-thread-safe value push: no host/GUI notification (message-thread only).
    params[i]->setValue(juce::jlimit(0.0f, 1.0f, v));
}
int32_t paramIndexOfIdImpl(juce::AudioPluginInstance* p, const std::string& id) {
    if (p == nullptr) return -1;
    const juce::String want(juce::String::fromUTF8(id.c_str()));
    const int n = p->getParameters().size();
    for (int i = 0; i < n; ++i)
        if (auto* hp = p->getHostedParameter(i))
            if (hp->getParameterID() == want) return i;
    return -1;
}

std::unique_ptr<juce::AudioPluginInstance> createInstance(
    const juce::PluginDescription& desc, double sr, int maxBlock) {
    juce::String err;
    return scanner::sharedFormatManager().createPluginInstance(desc, sr, maxBlock, err);
}

std::unique_ptr<juce::AudioPluginInstance> instantiateCatalog(
    int catalogIndex, double sr, int maxBlock, bool instrumentOnly) {
    auto& catalog = scanner::PluginCatalog::instance();
    catalog.loadOnce();
    juce::PluginDescription desc; // one entry, copied under one lock
    if (!catalog.typeAt(catalogIndex, desc)) return nullptr;
    if (instrumentOnly && !desc.isInstrument) return nullptr;
    return createInstance(desc, sr, maxBlock);
}

} // namespace hosting
} // namespace nota
