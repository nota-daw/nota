// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "hosting/PluginCommon.h"

#include "hosting/IPluginFormatHost.h"
#include "hosting/MacBridges.h"
#include "nota/dsp/rt/TransportInfo.h"
#include "nota/dsp/PluginContracts.h"

#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <vector>
#include <string>
#include <memory>

namespace nota::pluginhost::detail {
namespace {

std::atomic<void*> g_workgroup{nullptr};
std::atomic<uint32_t> g_workgroupGen{1};

} // namespace

int pluginChannelPtrs(const juce::AudioProcessor& p, juce::AudioBuffer<float>& storage, float* spare, int frames,
    int minChans, float* (&ptrs)[kMaxChans]) {
    const int have = juce::jmin(storage.getNumChannels(), kMaxChans);
    const int want = juce::jmin(kMaxChans, juce::jmax(minChans > 0 ? juce::jmax(minChans, have) : 1,
                                               p.getTotalNumInputChannels(), p.getTotalNumOutputChannels()));
    if (want > have) juce::FloatVectorOperations::clear(spare, frames);
    for (int c = 0; c < want; ++c) ptrs[c] = c < have ? storage.getWritePointer(c) : spare;
    return want;
}

int minProcessChannels(const juce::AudioPluginInstance& p) {
    return sharedFormatHost().minProcessChannels(p, kNoAllocChans);
}

bool isParallelRenderSafe(const juce::AudioPluginInstance* p) {
    return sharedFormatHost().isParallelRenderSafe(p);
}

void enableEveryBus(juce::AudioPluginInstance& p) {
    if (p.enableAllBuses()) return;
    for (const bool isInput : {true, false}) {
        for (int i = 1; i < p.getBusCount(isInput); ++i) {
            if (auto* bus = p.getBus(isInput, i); bus != nullptr && !bus->isEnabled()) bus->enable(true);
        }
    }
}

void logBuses(const juce::AudioPluginInstance& p) {
    static const bool dbg = std::getenv("NOTA_DEBUG_PLUGIN_BUSES") != nullptr;
    if (!dbg) return;
    for (const bool isInput : {true, false}) {
        for (int i = 0; i < p.getBusCount(isInput); ++i) {
            if (auto* bus = p.getBus(isInput, i)) {
                std::fprintf(stderr, "[nota buses] %s %s %d '%s' chans=%d enabled=%d\n", p.getName().toRawUTF8(),
                    isInput ? "in" : "out", i, bus->getName().toRawUTF8(), bus->getLastEnabledLayout().size(),
                    static_cast<int>(bus->isEnabled()));
            }
        }
    }
}

void setCurrentAudioWorkgroup(void* osWorkgroup) {
#if defined(__APPLE__)
    mac::retainWorkgroup(osWorkgroup);
    g_workgroup.store(osWorkgroup, std::memory_order_release);
    g_workgroupGen.fetch_add(1, std::memory_order_release);
#else
    (void)osWorkgroup;
#endif
}

WorkgroupLink::~WorkgroupLink() {
#if defined(__APPLE__)
    mac::releaseWorkgroupObserver(observer_);
#endif
}

void WorkgroupLink::bind(juce::AudioPluginInstance& p) {
#if defined(__APPLE__)
    if (observer_ != nullptr) {
        mac::releaseWorkgroupObserver(observer_);
        observer_ = nullptr;
    }
    seen_ = 0;
    const auto* client = p.getAudioUnitClient();
    if (client == nullptr) return;
    observer_ = mac::copyAuWorkgroupObserver(static_cast<void*>(client->getAudioUnitHandle()));
#else
    (void)p;
#endif
}

void WorkgroupLink::update() {
#if defined(__APPLE__)
    if (observer_ == nullptr) return;
    const uint32_t gen = g_workgroupGen.load(std::memory_order_acquire);
    if (gen == seen_) return;
    seen_ = gen;
    mac::invokeWorkgroupObserver(observer_, g_workgroup.load(std::memory_order_acquire));
#endif
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
    return p != nullptr ? p->getParameters().size() : 0;
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
    params[i]->setValue(juce::jlimit(0.0f, 1.0f, v));
}

int32_t paramIndexOfIdImpl(juce::AudioPluginInstance* p, const std::string& id) {
    if (p == nullptr) return -1;
    const juce::String want(juce::String::fromUTF8(id.c_str()));
    const int n = p->getParameters().size();
    for (int i = 0; i < n; ++i) {
        if (auto* hp = p->getHostedParameter(i)) {
            if (hp->getParameterID() == want) return i;
        }
    }
    return -1;
}

void advanceTransport(dsp::TransportInfo& ti, int32_t frames, double sr) {
    if (!ti.isPlaying || frames <= 0 || sr <= 0.0) return;
    const double secs = static_cast<double>(frames) / sr;
    ti.timeInSamples += frames;
    ti.timeInSeconds += secs;
    ti.ppqPosition += secs * ti.bpm / 60.0;
    const double loopLen = ti.ppqLoopEnd - ti.ppqLoopStart;
    if (ti.isLooping && loopLen > 0.0 && ti.ppqPosition >= ti.ppqLoopEnd) ti.ppqPosition -= loopLen;
}

bool juceDescFromDescriptor(const dsp::PluginDescriptor& pd, juce::PluginDescription& out) {
    if (!pd.serializedXml.empty()) {
        if (auto xml = juce::parseXML(juce::String::fromUTF8(pd.serializedXml.c_str()))) {
            if (out.loadFromXml(*xml)) return true;
        }
    }
    out = juce::PluginDescription{};
    out.name = juce::String::fromUTF8(pd.name.c_str());
    out.descriptiveName = juce::String::fromUTF8(pd.descriptiveName.c_str());
    out.pluginFormatName = juce::String::fromUTF8(pd.formatName.c_str());
    out.category = juce::String::fromUTF8(pd.category.c_str());
    out.manufacturerName = juce::String::fromUTF8(pd.manufacturerName.c_str());
    out.version = juce::String::fromUTF8(pd.version.c_str());
    out.fileOrIdentifier = juce::String::fromUTF8(pd.fileOrIdentifier.c_str());
    out.uniqueId = pd.uniqueId;
    out.deprecatedUid = pd.deprecatedUid;
    out.isInstrument = pd.isInstrument;
    out.numInputChannels = pd.numInputChannels;
    out.numOutputChannels = pd.numOutputChannels;
    out.hasSharedContainer = pd.hasSharedContainer;
    return !out.pluginFormatName.isEmpty();
}

bool catalogDescriptionByIndex(const std::shared_ptr<dsp::IPluginCatalog>& catalog, int32_t catalogIndex,
    bool instrumentOnly, juce::PluginDescription& out) {
    if (!catalog) return false;
    dsp::PluginDescriptor pd;
    if (!catalog->descriptorAt(catalogIndex, pd)) return false;
    if (!juceDescFromDescriptor(pd, out)) return false;
    if (instrumentOnly && !out.isInstrument) {
        std::fprintf(stderr, "[nota plugin] '%s' is not listed as an instrument\n", out.name.toRawUTF8());
        return false;
    }
    return true;
}

bool catalogDescriptionById(const std::shared_ptr<dsp::IPluginCatalog>& catalog, const std::string& identifier,
    bool instrumentOnly, juce::PluginDescription& out) {
    if (!catalog || identifier.empty()) return false;
    dsp::PluginDescriptor pd;
    if (!catalog->descriptorOfId(identifier.c_str(), pd)) return false;
    if (!juceDescFromDescriptor(pd, out)) return false;
    if (instrumentOnly && !out.isInstrument) {
        std::fprintf(stderr, "[nota plugin] '%s' is not listed as an instrument\n", out.name.toRawUTF8());
        return false;
    }
    return true;
}

juce::PluginDescription catalogDescriptionOf(
    const std::shared_ptr<dsp::IPluginCatalog>& catalog, const juce::AudioPluginInstance& p) {
    const juce::PluginDescription own = p.getPluginDescription();
    if (!catalog) return own;
    dsp::PluginDescriptor pd;
    juce::PluginDescription desc = own;
    if (!catalog->descriptorOfId(own.createIdentifierString().toRawUTF8(), pd)) return own;
    return juceDescFromDescriptor(pd, desc) ? desc : own;
}

std::unique_ptr<juce::AudioPluginInstance> createPluginInstance(
    const juce::PluginDescription& desc, double sr, int maxBlock, std::string* error) {
    return sharedFormatHost().createInstance(desc, sr, maxBlock, error);
}

} // namespace nota::pluginhost::detail
