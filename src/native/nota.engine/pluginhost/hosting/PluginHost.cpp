// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginHost implementation — module lifetime, test window, Linux pump and
// catalog factories. The scan
// section now lives in scanner/abi_pluginscanner.cpp and the adapters in
// PluginInstrument.cpp / PluginEffect.cpp.

#include "PluginHost.h"

#include "nota/nota_engine.h"
#include "PluginCommon.h"
#include "PluginEffect.h"
#include "PluginInstrument.h"

#include <juce_audio_processors/juce_audio_processors.h>
#include <juce_gui_basics/juce_gui_basics.h>

#include <memory>

namespace {

class SpikeWindow : public juce::DocumentWindow {
  public:
    SpikeWindow()
        : juce::DocumentWindow("Nota — JUCE spike", juce::Colours::darkgrey, juce::DocumentWindow::allButtons) {
        auto* label = new juce::Label({}, "JUCE window alive alongside Avalonia.\n"
                                          "M3-0 run-loop spike OK.");
        label->setJustificationType(juce::Justification::centred);
        setContentOwned(label, true);
        setUsingNativeTitleBar(true);
        centreWithSize(380, 140);
        setResizable(true, false);
        setVisible(true);
    }

    // Just hide — keep JUCE alive for the app's lifetime.
    void closeButtonPressed() override { setVisible(false); }
};

// Leaked on purpose: JUCE stays initialised for the whole process (init once).
juce::ScopedJuceInitialiser_GUI* g_juceGui = nullptr;
std::unique_ptr<SpikeWindow> g_window;

void ensureJuceGui() {
    if (g_juceGui == nullptr) g_juceGui = new juce::ScopedJuceInitialiser_GUI();
}

} // namespace

extern "C" NOTA_API NotaResult nota_pluginhost_open_test_window(void) {
    ensureJuceGui();

    if (g_window == nullptr) g_window = std::make_unique<SpikeWindow>();
    else
        g_window->setVisible(true);

    return NOTA_OK;
}

// ---- JUCE message pump (Linux) ---------------------------------------------
// The spike premise above holds on macOS (shared CFRunLoop) and Windows (JUCE's
// message HWND is serviced by Avalonia's Win32 loop), but on Linux JUCE runs its
// own poll()-based loop over the X11 display fd, its message pipe and any fds a
// hosted VST3 registers through the host IRunLoop. Nothing services that loop
// unless we do: editor windows stay black and unclosable, and plugins — which
// hand their message thread to the host's IRunLoop on creation — never run their
// timers/async work and stay silent. The app calls this from a UI-thread timer.

#if JUCE_LINUX || JUCE_BSD
namespace juce::detail {
bool dispatchNextMessageOnSystemQueue(bool returnIfNoPendingMessages);
}
#endif

extern "C" NOTA_API int32_t nota_pluginhost_pump(void) {
#if JUCE_LINUX || JUCE_BSD
    if (g_juceGui == nullptr) return 0; // no plugin/editor yet: nothing to service
    auto* mm = juce::MessageManager::getInstanceWithoutCreating();
    if (mm == nullptr || !mm->isThisTheMessageThread()) return 0;
    // Bounded so a plugin that keeps re-posting messages can't starve Avalonia.
    constexpr int kMaxPerPump = 64;
    int n = 0;
    while (n < kMaxPerPump && juce::detail::dispatchNextMessageOnSystemQueue(true)) ++n;
    return n;
#else
    return 0;
#endif
}

// ---- Catalog factories (message thread) ------------------------------------

namespace nota {

std::shared_ptr<Instrument> createPluginInstrument(int32_t catalogIndex, double sr, int32_t maxBlock) {
    ensureJuceGui();
    auto inst = hosting::instantiateCatalog(catalogIndex, sr, maxBlock, /*instrumentOnly*/ true);
    if (!inst) return nullptr;
    return std::make_shared<hosting::PluginInstrument>(std::move(inst), sr, maxBlock);
}

std::shared_ptr<Device> createPluginEffect(int32_t catalogIndex, double sr, int32_t maxBlock) {
    ensureJuceGui();
    auto inst = hosting::instantiateCatalog(catalogIndex, sr, maxBlock, /*instrumentOnly*/ false);
    if (!inst) return nullptr;
    return std::make_shared<hosting::PluginEffect>(std::move(inst), sr, maxBlock);
}

} // namespace nota
