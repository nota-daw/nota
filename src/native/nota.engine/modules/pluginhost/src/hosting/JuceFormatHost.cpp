// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "hosting/IPluginFormatHost.h"

#include "hosting/MacBridges.h"

#include <cstdio>
#include <cstdlib>
#include <memory>
#include <mutex>
#include <string>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#elif !defined(__APPLE__)
#include <dlfcn.h>
#include <sys/utsname.h>
#endif

namespace nota::pluginhost::detail {
namespace {

class JuceFormatHost final : public IPluginFormatHost {
  public:
    std::unique_ptr<juce::AudioPluginInstance> createInstance(
        const juce::PluginDescription& desc, double sampleRate, int maxBlock, std::string* error) const override {
        juce::String err;
        auto inst = formatManager().createPluginInstance(desc, sampleRate, maxBlock, err);
        if (!inst) {
            std::fprintf(stderr, "[nota plugin] could not load '%s' (%s, %s): %s\n", desc.name.toRawUTF8(),
                desc.pluginFormatName.toRawUTF8(), desc.fileOrIdentifier.toRawUTF8(),
                err.isNotEmpty() ? err.toRawUTF8() : "no reason given");
        }
        if (!inst && error != nullptr) *error = err.toStdString();
        return inst;
    }

    bool preloadBinary(const juce::PluginDescription& desc, std::string& error) const override {
        const juce::String path = desc.fileOrIdentifier;
        if (desc.pluginFormatName == "VST3") {
#if defined(_WIN32)
            juce::File f{path};
            if (f.isDirectory()) {
#if defined(_M_ARM64) || defined(_M_ARM64EC)
                const char* arch = "arm64-win";
#else
                const char* arch = "x86_64-win";
#endif
                f = f.getChildFile("Contents").getChildFile(arch).getChildFile(f.getFileName());
            }
            if (::LoadLibraryW(f.getFullPathName().toWideCharPointer()) != nullptr) return true;
            error = "LoadLibrary failed (" + std::to_string(::GetLastError()) + ")";
            return false;
#elif defined(__APPLE__)
            return mac::preloadVst3Bundle(path.toStdString(), juce::File{path}.isDirectory(), error);
#else
            struct utsname u{};
            if (::uname(&u) != 0) return true;
            const juce::File bundle{path};
            const juce::File so = bundle.getChildFile("Contents")
                                      .getChildFile(juce::String(u.machine) + "-linux")
                                      .getChildFile(bundle.getFileNameWithoutExtension() + ".so");
            if (::dlopen(so.getFullPathName().toRawUTF8(), RTLD_LOCAL | RTLD_NOW) != nullptr) return true;
            const char* why = ::dlerror();
            error = why != nullptr ? why : "dlopen failed";
            return false;
#endif
        }
#if defined(__APPLE__)
        if (desc.pluginFormatName == "AudioUnit") { return mac::preloadAudioUnitComponent(path.toStdString(), error); }
#endif
        return true;
    }

    bool isParallelRenderSafe(const juce::AudioPluginInstance* instance) const override {
        if (instance == nullptr) return false;
        const auto desc = instance->getPluginDescription();
        if (desc.pluginFormatName == "VST3") return true;
        if (desc.pluginFormatName != "AudioUnit") return false;
        static const bool serialAu = [] {
            const char* v = std::getenv("NOTA_SERIAL_AUDIOUNIT");
            return v != nullptr && v[0] == '1';
        }();
        if (serialAu) return false;
        const juce::String& id = desc.fileOrIdentifier;
        const juce::String& name = desc.name;
        if (id.containsIgnoreCase("AUNetSend") || id.containsIgnoreCase("AUNetReceive") ||
            id.containsIgnoreCase("AURoundTripAAC") || name.containsIgnoreCase("AUNetSend") ||
            name.containsIgnoreCase("AUNetReceive") || name.containsIgnoreCase("AURoundTripAAC")) {
            return false;
        }
        return true;
    }

    int minProcessChannels(const juce::AudioPluginInstance& instance, int noAllocChans) const override {
        return instance.getPluginDescription().pluginFormatName == "VST3" ? noAllocChans : 0;
    }

  private:
    static juce::AudioPluginFormatManager& formatManager() {
        static juce::AudioPluginFormatManager fm;
        static std::once_flag once;
        std::call_once(once, [] { juce::addDefaultFormatsToManager(fm); });
        return fm;
    }
};

} // namespace

IPluginFormatHost& sharedFormatHost() {
    static JuceFormatHost host;
    return host;
}

} // namespace nota::pluginhost::detail
