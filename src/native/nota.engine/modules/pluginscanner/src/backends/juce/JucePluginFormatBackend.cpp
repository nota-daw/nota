// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// JucePluginFormatBackend — sole JUCE translation unit inside nota_pluginscanner.
// Exposes VST3 (everywhere) and AudioUnit (macOS) behind IPluginFormatBackend.

#include "backends/juce/JucePluginFormatBackend.h"

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/IPluginFormatBackend.h"
#include "nota/pluginscanner/PluginFormatRegistry.h"

#include <cstddef>
#include <juce_audio_processors/juce_audio_processors.h>

#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace nota::pluginscanner::detail {
namespace {

juce::AudioPluginFormatManager& juceFormatManager() {
    static juce::AudioPluginFormatManager manager;
    static std::once_flag once;
    std::call_once(once, [] { juce::addDefaultFormatsToManager(manager); });
    return manager;
}

juce::AudioPluginFormat* findJuceFormat(const juce::String& name) {
    for (auto* format : juceFormatManager().getFormats()) {
        if (format != nullptr && format->getName() == name) return format;
    }
    return nullptr;
}

void ensureWorkerGuiInit() {
    // AU/VST3 probing inside nota-scanworker touches CoreFoundation / platform run loops.
    static const bool primed = [] {
        struct DeletedAtShutdownPrimer final : juce::DeletedAtShutdown {};
        const DeletedAtShutdownPrimer primer;
        (void)primer;
        return true;
    }();
    (void)primed;
    static juce::ScopedJuceInitialiser_GUI init;
    (void)init;
}

dsp::PluginFormatKind formatKindFromName(const juce::String& name) noexcept {
    if (name == "VST3") return dsp::PluginFormatKind::Vst3;
    if (name == "AudioUnit") return dsp::PluginFormatKind::AudioUnit;
    return dsp::PluginFormatKind::Unknown;
}

dsp::PluginDescriptor toPluginDescriptor(const juce::PluginDescription& jdesc) {
    dsp::PluginDescriptor out;
    out.identifier = jdesc.createIdentifierString().toStdString();
    out.name = jdesc.name.toStdString();
    out.descriptiveName = jdesc.descriptiveName.toStdString();
    out.formatName = jdesc.pluginFormatName.toStdString();
    out.format = formatKindFromName(jdesc.pluginFormatName);
    out.category = jdesc.category.toStdString();
    out.manufacturerName = jdesc.manufacturerName.toStdString();
    out.version = jdesc.version.toStdString();
    out.fileOrIdentifier = jdesc.fileOrIdentifier.toStdString();
    if (auto xml = jdesc.createXml()) {
        out.serializedXml = xml->toString(juce::XmlElement::TextFormat().singleLine()).toStdString();
    }
    out.uniqueId = jdesc.uniqueId;
    out.deprecatedUid = jdesc.deprecatedUid;
    out.isInstrument = jdesc.isInstrument;
    out.numInputChannels = jdesc.numInputChannels;
    out.numOutputChannels = jdesc.numOutputChannels;
    out.hasSharedContainer = jdesc.hasSharedContainer;
    return out;
}

class JucePluginFormatBackend final : public IPluginFormatBackend {
  public:
    explicit JucePluginFormatBackend(
        dsp::PluginFormatKind kind, std::string id, std::string displayName, bool fileBased, bool backfillDefaults)
        : descriptor_{kind, std::move(id), std::move(displayName), false, true}, fileBased_(fileBased),
          backfillDefaults_(backfillDefaults) {}

    dsp::PluginFormatDescriptor descriptor() const override { return descriptor_; }

    bool isFileBased() const override { return fileBased_; }

    bool hasDefaultScanPathBackfill() const override { return backfillDefaults_; }

    std::vector<std::string> defaultSearchPaths() const override {
        std::vector<std::string> out;
        auto* format = findJuceFormat(juce::String::fromUTF8(descriptor_.id.c_str()));
        if (format == nullptr) return out;

        const auto search = format->getDefaultLocationsToSearch();
        for (int i = 0; i < search.getNumPaths(); ++i) {
            const std::string path = search[i].getFullPathName().toStdString();
            if (!path.empty()) out.push_back(path);
        }
        return out;
    }

    std::vector<std::string> discoverCandidates(
        const std::vector<std::string>& searchPaths, const std::string& onlyDir) const override {
        std::vector<std::string> out;
        auto* format = findJuceFormat(juce::String::fromUTF8(descriptor_.id.c_str()));
        if (format == nullptr) return out;

        juce::FileSearchPath search;
        if (!onlyDir.empty()) {
            // Single-folder store scan: only file-based formats apply; registry formats (AU) skip it.
            if (!fileBased_) return out;
            search.add(juce::File(juce::CharPointer_UTF8(onlyDir.c_str())));
        } else {
            search = format->getDefaultLocationsToSearch();
            if (fileBased_) {
                for (const auto& dir : searchPaths) {
                    if (!dir.empty()) { search.addIfNotAlreadyThere(juce::File(juce::CharPointer_UTF8(dir.c_str()))); }
                }
            }
        }

        const auto ids = format->searchPathsForPlugins(search, true, false);
        out.reserve(static_cast<size_t>(ids.size()));
        for (const auto& id : ids) out.push_back(id.toStdString());
        return out;
    }

    bool candidateExists(const std::string& candidateId) const override {
        if (!fileBased_) {
            // AudioUnit lives in the OS component registry — leave catalog entries intact.
            return true;
        }
        return juce::File(juce::CharPointer_UTF8(candidateId.c_str())).exists();
    }

    int64_t candidateTimestampMs(const std::string& candidateId) const override {
        const juce::String path(juce::CharPointer_UTF8(candidateId.c_str()));
        if (!juce::File::isAbsolutePath(path)) return 0;
        const juce::File file(path);
        return file.exists() ? file.getLastModificationTime().toMilliseconds() : 0;
    }

    std::string candidateDisplayName(const std::string& candidateId) const override {
        const juce::String s(juce::CharPointer_UTF8(candidateId.c_str()));
        if (juce::File::isAbsolutePath(s)) return juce::File(s).getFileNameWithoutExtension().toStdString();
        return {};
    }

    bool probeInWorker(
        const std::string& candidateId, std::vector<dsp::PluginDescriptor>& outDescriptors) const override {
        ensureWorkerGuiInit();
        auto* format = findJuceFormat(juce::String::fromUTF8(descriptor_.id.c_str()));
        if (format == nullptr) return false;

        juce::OwnedArray<juce::PluginDescription> found;
        format->findAllTypesForFile(found, juce::String::fromUTF8(candidateId.c_str()));
        outDescriptors.reserve(outDescriptors.size() + static_cast<size_t>(found.size()));
        for (const auto* jdesc : found) {
            if (jdesc != nullptr) outDescriptors.push_back(toPluginDescriptor(*jdesc));
        }
        return true;
    }

  private:
    dsp::PluginFormatDescriptor descriptor_;
    bool fileBased_ = false;
    bool backfillDefaults_ = false;
};

} // namespace

void registerJuceFormatBackends(PluginFormatRegistry& registry) {
#if JUCE_MAC
    // AudioUnit first on macOS for parity with Nota plugin lookup priority.
    if (findJuceFormat("AudioUnit") != nullptr) {
        registry.registerBackend(std::make_shared<JucePluginFormatBackend>(
            dsp::PluginFormatKind::AudioUnit, "AudioUnit", "Audio Unit", false, false));
    }
#endif
    if (findJuceFormat("VST3") != nullptr) {
        registry.registerBackend(
            std::make_shared<JucePluginFormatBackend>(dsp::PluginFormatKind::Vst3, "VST3", "VST3", true, true));
    }
}

} // namespace nota::pluginscanner::detail
