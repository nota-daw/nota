// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/pluginhost/PluginHostImpl.h"

#include "hosting/MacBridges.h"
#include "hosting/PluginCommon.h"
#include "hosting/PluginEffect.h"
#include "hosting/PluginInstrument.h"
#include "hosting/PluginLoader.h"
#include "nota/dsp/PluginContracts.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/Device.h"

#include <cstdint>
#include <juce_audio_processors/juce_audio_processors.h>
#include <juce_gui_basics/juce_gui_basics.h>

#include <memory>
#include <mutex>
#include <string>
#include <utility>

#if defined(__linux__) || defined(__FreeBSD__)
namespace juce::detail {
bool dispatchNextMessageOnSystemQueue(bool returnIfNoPendingMessages);
}
#endif

namespace nota::pluginhost {
namespace {

class SpikeWindow final : public juce::DocumentWindow {
  public:
    SpikeWindow()
        : juce::DocumentWindow("Nota — JUCE spike", juce::Colours::darkgrey, juce::DocumentWindow::allButtons) {
        auto* label = new juce::Label({}, "JUCE window alive alongside Avalonia.\nM3-0 run-loop spike OK.");
        label->setJustificationType(juce::Justification::centred);
        setContentOwned(label, true);
        setUsingNativeTitleBar(true);
        centreWithSize(380, 140);
        setResizable(true, false);
        setVisible(true);
    }

    void closeButtonPressed() override { setVisible(false); }
};

juce::ScopedJuceInitialiser_GUI* g_juceGui = nullptr;
std::unique_ptr<SpikeWindow> g_spikeWindow;
std::mutex g_guiMutex;

void ensureJuceGui() {
    const std::lock_guard<std::mutex> lock(g_guiMutex);
    if (g_juceGui == nullptr) g_juceGui = new juce::ScopedJuceInitialiser_GUI();
#if defined(__APPLE__)
    detail::mac::cacheCurrentApplication();
#endif
}

} // namespace

PluginHostImpl::PluginHostImpl(std::shared_ptr<dsp::IPluginCatalog> catalog) : catalog_(std::move(catalog)) {}
PluginHostImpl::~PluginHostImpl() = default;

void PluginHostImpl::setCatalog(std::shared_ptr<dsp::IPluginCatalog> catalog) {
    const std::lock_guard<std::mutex> lock(catalogMutex_);
    catalog_ = std::move(catalog);
}

std::shared_ptr<dsp::IPluginCatalog> PluginHostImpl::catalog() const {
    const std::lock_guard<std::mutex> lock(catalogMutex_);
    return catalog_;
}

std::shared_ptr<dsp::Instrument> PluginHostImpl::createInstrument(
    int32_t catalogIndex, double sampleRate, int32_t maxBlock) {
    const auto cat = catalog();
    juce::PluginDescription desc;
    if (!detail::catalogDescriptionByIndex(cat, catalogIndex, true, desc)) return nullptr;
    ensureJuceGui();
    return std::make_shared<detail::PluginInstrument>(desc, sampleRate, maxBlock, cat);
}

std::shared_ptr<dsp::Device> PluginHostImpl::createEffect(int32_t catalogIndex, double sampleRate, int32_t maxBlock) {
    const auto cat = catalog();
    juce::PluginDescription desc;
    if (!detail::catalogDescriptionByIndex(cat, catalogIndex, false, desc)) return nullptr;
    ensureJuceGui();
    return std::make_shared<detail::PluginEffect>(desc, sampleRate, maxBlock, cat);
}

std::shared_ptr<dsp::Instrument> PluginHostImpl::createInstrumentById(
    const std::string& identifier, double sampleRate, int32_t maxBlock) {
    const auto cat = catalog();
    juce::PluginDescription desc;
    if (!detail::catalogDescriptionById(cat, identifier, true, desc)) return nullptr;
    ensureJuceGui();
    return std::make_shared<detail::PluginInstrument>(desc, sampleRate, maxBlock, cat);
}

std::shared_ptr<dsp::Device> PluginHostImpl::createEffectById(
    const std::string& identifier, double sampleRate, int32_t maxBlock) {
    const auto cat = catalog();
    juce::PluginDescription desc;
    if (!detail::catalogDescriptionById(cat, identifier, false, desc)) return nullptr;
    ensureJuceGui();
    return std::make_shared<detail::PluginEffect>(desc, sampleRate, maxBlock, cat);
}

dsp::PluginLoadsServiced PluginHostImpl::servicePluginLoads() {
    const auto s = detail::PluginLoader::instance().service();
    return dsp::PluginLoadsServiced{s.changed, s.attached};
}

int32_t PluginHostImpl::pendingPluginLoads() const {
    return detail::PluginLoader::instance().pending();
}

void PluginHostImpl::setPluginAutoloadScope(bool enabled) {
    detail::PluginLoader::instance().setAutoloadScope(enabled);
}

std::string PluginHostImpl::takeFrozenPluginNotice() {
    return detail::PluginLoader::instance().takeFrozenNotice();
}

void PluginHostImpl::setAudioWorkgroup(void* workgroup) {
    detail::setCurrentAudioWorkgroup(workgroup);
}

void PluginHostImpl::pumpMessageThreadIfNeeded() {
    (void)pumpMessages();
}

bool PluginHostImpl::openTestWindow() {
    ensureJuceGui();
    const std::lock_guard<std::mutex> lock(g_guiMutex);
    if (g_spikeWindow == nullptr) g_spikeWindow = std::make_unique<SpikeWindow>();
    else
        g_spikeWindow->setVisible(true);
    return g_spikeWindow != nullptr;
}

int32_t PluginHostImpl::pumpMessages() {
#if defined(__linux__) || defined(__FreeBSD__)
    {
        const std::lock_guard<std::mutex> lock(g_guiMutex);
        if (g_juceGui == nullptr) return 0;
    }
    auto* mm = juce::MessageManager::getInstanceWithoutCreating();
    if (mm == nullptr || !mm->isThisTheMessageThread()) return 0;
    constexpr int kMaxPerPump = 64;
    int n = 0;
    while (n < kMaxPerPump && juce::detail::dispatchNextMessageOnSystemQueue(true)) ++n;
    return n;
#else
    return 0;
#endif
}

} // namespace nota::pluginhost
