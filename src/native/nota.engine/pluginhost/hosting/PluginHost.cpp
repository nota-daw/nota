// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginHost implementation — module lifetime, test window, Linux pump, catalog
// factories and the device workgroup hand-off. The scan
// section now lives in scanner/abi_pluginscanner.cpp and the adapters in
// PluginInstrument.cpp / PluginEffect.cpp.

#include "PluginHost.h"

#include "nota/nota_engine.h"
#include "PluginCommon.h"
#include "PluginEffect.h"
#include "PluginInstrument.h"

#include <juce_audio_processors/juce_audio_processors.h>
#include <juce_gui_basics/juce_gui_basics.h>

#include <algorithm>
#include <atomic>
#include <memory>
#include <mutex>
#include <vector>

#if JUCE_MAC
#import <AppKit/AppKit.h>
#include <AudioToolbox/AudioToolbox.h>
#include <Block.h>
#include <objc/runtime.h>
#include <os/workgroup.h>
#endif

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

#if JUCE_MAC
// JUCE plug-ins identify their host with PluginHostType::getHostType(). For a host it doesn't
// know by path — Nota — that ends in [[NSRunningApplication currentApplication] localizedName]:
// a fresh object whose first property read is a synchronous LaunchServices round trip
// (measured: median 67 us, p99 6 ms, p99.9 31 ms). Some plug-ins ask on the audio thread every
// block (Splice Bridge does), and those tails are dropouts at any small buffer (#436). So
// currentApplication returns one instance, and its name is a copy taken here, once (median
// 0.08 us); a long-lived instance alone still re-fetches its "dynamic" properties now and then.
// Every other application is untouched.
void cacheCurrentApplication() {
    static std::once_flag once;
    std::call_once(once, [] {
        Class cls = [NSRunningApplication class];
        Method current = class_getClassMethod(cls, @selector(currentApplication));
        Method name = class_getInstanceMethod(cls, @selector(localizedName));
        if (current == nullptr || name == nullptr) return;
        using CurrentFn = NSRunningApplication* (*)(id, SEL);
        using NameFn = NSString* (*)(id, SEL);
        const auto originalCurrent = (CurrentFn)method_getImplementation(current);
        static const auto originalName = (NameFn)method_getImplementation(name);
        static NSRunningApplication* own = [originalCurrent(cls, @selector(currentApplication)) retain];
        static NSString* ownName = [originalName(own, @selector(localizedName)) copy];
        method_setImplementation(current, imp_implementationWithBlock(^NSRunningApplication*(id) { return own; }));
        method_setImplementation(name, imp_implementationWithBlock(^NSString*(NSRunningApplication* app) {
            return app == own ? ownName : originalName(app, @selector(localizedName));
        }));
    });
}
#endif

void ensureJuceGui() {
    if (g_juceGui == nullptr) g_juceGui = new juce::ScopedJuceInitialiser_GUI();
#if JUCE_MAC
    cacheCurrentApplication(); // before any plug-in is created
#endif
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

// ---- Device workgroup hand-off (#436) ---------------------------------------
// A plug-in like Maschine renders on worker threads of its own and waits for them inside its
// render call. Outside the device's workgroup the OS schedules those threads without the
// deadline and, under sustained load, demotes them: dropouts that keep coming until the
// transport stops. The current workgroup and a generation the render threads compare against.

namespace {
std::atomic<void*> gWorkgroup{nullptr};
std::atomic<uint32_t> gWorkgroupGen{1}; // a link starts at 0, so its first render hands over
} // namespace

void setPluginAudioWorkgroup(void* osWorkgroup) {
#if JUCE_MAC
    if (osWorkgroup != nullptr) {
        // Plug-in threads may still be joined to a workgroup after the device that owned it
        // closed, so every one handed out stays retained (one per device start, never many).
        static std::mutex mx;
        static std::vector<void*> kept;
        const std::lock_guard<std::mutex> lock(mx);
        if (std::find(kept.begin(), kept.end(), osWorkgroup) == kept.end()) {
            if (@available(macOS 11.0, *)) os_retain(static_cast<os_workgroup_t>(osWorkgroup));
            kept.push_back(osWorkgroup);
        }
    }
    gWorkgroup.store(osWorkgroup, std::memory_order_release);
    gWorkgroupGen.fetch_add(1, std::memory_order_release);
#else
    (void)osWorkgroup;
#endif
}

namespace hosting {

WorkgroupLink::~WorkgroupLink() {
#if JUCE_MAC
    if (observer_) Block_release(observer_);
#endif
}

void WorkgroupLink::bind(juce::AudioPluginInstance& p) {
#if JUCE_MAC
    if (observer_) {
        Block_release(observer_);
        observer_ = nullptr;
    }
    seen_ = 0;
    const auto* client = p.getAudioUnitClient();
    if (client == nullptr) return; // not an Audio Unit
    if (@available(macOS 12.0, *)) {
        AudioUnit au = client->getAudioUnitHandle();
        if (au == nullptr) return;
        AURenderContextObserver obs = nullptr;
        UInt32 size = sizeof(obs);
        if (AudioUnitGetProperty(au, kAudioUnitProperty_RenderContextObserver, kAudioUnitScope_Global, 0, &obs,
                &size) == noErr &&
            obs != nullptr)
            observer_ = (void*)Block_copy(obs); // a block pointer kept untyped in the header
    }
#else
    (void)p;
#endif
}

void WorkgroupLink::update() {
#if JUCE_MAC
    if (observer_ == nullptr) return;
    const uint32_t gen = gWorkgroupGen.load(std::memory_order_acquire);
    if (gen == seen_) return;
    seen_ = gen;
    if (@available(macOS 12.0, *)) {
        AudioUnitRenderContext ctx{};
        ctx.workgroup = static_cast<os_workgroup_t>(gWorkgroup.load(std::memory_order_acquire));
        ((AURenderContextObserver)observer_)(&ctx);
    }
#endif
}

} // namespace hosting

} // namespace nota
