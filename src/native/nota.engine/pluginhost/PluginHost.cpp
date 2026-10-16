// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// M3 plugin-hosting module. THE ONLY place that includes JUCE — the core engine
// stays JUCE-free (see ARCHITECTURE.md § Plugin hosting). Built as a separate static
// library (nota_pluginhost) and whole-archive-linked into libnota_engine so its
// C ABI symbols survive.
//
// M3-0 spike: prove that a JUCE GUI/message loop coexists with the host app's
// run loop (Avalonia's NSApp). We initialise JUCE's GUI subsystem on the calling
// (main) thread WITHOUT running our own dispatch loop — JUCE piggybacks on the
// already-running CFRunLoop. If a plain DocumentWindow appears and stays live,
// the main M3 integration risk is retired.

#include "nota/nota_engine.h"
#include "Device.h"
#include "Instrument.h"
#include "PluginHostBridge.h"
#include "CommandQueue.h"   // SpscRingBuffer: message-thread -> audio-thread note injection
#if !defined(__APPLE__) // JUCE_MAC is not defined yet: the JUCE headers come below
#include "NotaConfigFile.h" // cfgfile::dataDir: the Windows/Linux per-user data dir
#endif

#include <juce_audio_processors/juce_audio_processors.h>
#include <juce_gui_basics/juce_gui_basics.h>

#if JUCE_MAC
#import <AppKit/AppKit.h>   // NSEvent monitor for computer-keyboard MIDI (editor windows)
#endif

#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace {

class SpikeWindow : public juce::DocumentWindow {
public:
    SpikeWindow()
        : juce::DocumentWindow("Nota — JUCE spike",
                               juce::Colours::darkgrey,
                               juce::DocumentWindow::allButtons) {
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
std::unique_ptr<SpikeWindow>     g_window;

} // namespace

extern "C" NOTA_API NotaResult nota_pluginhost_open_test_window(void) {
    if (g_juceGui == nullptr)
        g_juceGui = new juce::ScopedJuceInitialiser_GUI();

    if (g_window == nullptr)
        g_window = std::make_unique<SpikeWindow>();
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
namespace juce::detail { bool dispatchNextMessageOnSystemQueue(bool returnIfNoPendingMessages); }
#endif

extern "C" NOTA_API int32_t nota_pluginhost_pump(void) {
#if JUCE_LINUX || JUCE_BSD
    if (g_juceGui == nullptr) return 0;   // no plugin/editor yet: nothing to service
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

// ---- Plugin scanning & catalog (M3-1) --------------------------------------

namespace {

// AU first (macOS priority), then VST3 — see ARCHITECTURE.md § Plugin hosting.
const char* const kFormatOrder[] = {"AudioUnit", "VST3"};
constexpr int kScanTimeoutMs = 30000;

juce::AudioPluginFormatManager& formatManager() {
    // AudioPluginFormatManager is non-copyable; init the static in place.
    static juce::AudioPluginFormatManager fm;
    static const bool once = [] { juce::addDefaultFormatsToManager(fm); return true; }();
    juce::ignoreUnused(once);
    return fm;
}

juce::KnownPluginList& catalog() {
    static juce::KnownPluginList list;
    return list;
}

// Per-user Nota data dir (mirrors NotaPaths.DataDir / cfgfile::dataDir):
//   macOS   -> ~/Library/Application Support/Nota
//   Windows -> %APPDATA%\Nota
//   Linux   -> $XDG_CONFIG_HOME/nota or ~/.config/nota
juce::File notaSupportDir() {
    // NOTA_DATA_DIR (tests) relocates the catalog + scan paths, like NotaPaths.DataDir.
    // Needed because on macOS JUCE resolves the home folder via NSHomeDirectory(), not $HOME.
    if (const char* over = std::getenv("NOTA_DATA_DIR"); over != nullptr && *over != '\0') {
        juce::File dir{juce::CharPointer_UTF8(over)};
        dir.createDirectory();
        return dir;
    }
#if JUCE_MAC
    auto dir = juce::File::getSpecialLocation(juce::File::userApplicationDataDirectory)
                   .getChildFile("Application Support")
                   .getChildFile("Nota");
    dir.createDirectory();
    return dir;
#else
    // Windows + Linux share the engine's config dir (same folder as audio.json / midi.json).
    return juce::File{juce::CharPointer_UTF8(nota::cfgfile::dataDir().c_str())};
#endif
}

juce::File catalogFile()   { return notaSupportDir().getChildFile("plugins.xml"); }
juce::File scanPathsFile() { return notaSupportDir().getChildFile("scanpaths.txt"); }

// User-added search directories (one per line in scanpaths.txt).
juce::StringArray& extraScanPaths() {
    static juce::StringArray paths;
    return paths;
}

void loadScanPathsOnce() {
    static bool loaded = false;
    if (loaded) return;
    loaded = true;
    const auto f = scanPathsFile();
    if (!f.existsAsFile()) return;
    extraScanPaths().addLines(f.loadFileAsString());
    extraScanPaths().removeEmptyStrings();
}

void saveScanPaths() {
    scanPathsFile().replaceWithText(extraScanPaths().joinIntoString("\n"));
}

void loadCatalogOnce() {
    static bool loaded = false;
    if (loaded) return;
    loaded = true;
    if (auto xml = juce::parseXML(catalogFile()))
        catalog().recreateFromXml(*xml);
}

void saveCatalog() {
    if (auto xml = catalog().createXml())
        xml->writeTo(catalogFile());
}

juce::AudioPluginFormat* findFormat(const juce::String& name) {
    for (auto* f : formatManager().getFormats())
        if (f->getName() == name)
            return f;
    return nullptr;
}

// Run the worker for one plugin id; parse its PluginDescriptions into the
// catalog. Any failure (bad start, timeout, crash, garbage output) is swallowed
// so the scan moves on to the next plugin.
void scanOne(const juce::String& workerPath, const juce::String& formatName,
             const juce::String& fileOrId) {
    juce::ChildProcess proc;
    juce::StringArray cmd{workerPath, formatName, fileOrId};
    if (!proc.start(cmd, juce::ChildProcess::wantStdOut))
        return;

    juce::MemoryOutputStream captured;
    char buf[8192];
    const auto deadline = juce::Time::getMillisecondCounter() + (juce::uint32)kScanTimeoutMs;
    while (proc.isRunning()) {
        const auto n = proc.readProcessOutput(buf, (int)sizeof(buf));
        if (n > 0) captured.write(buf, (size_t)n);
        if (juce::Time::getMillisecondCounter() > deadline) { proc.kill(); return; }
        if (n <= 0) juce::Thread::sleep(5);
    }
    for (;;) {
        const auto n = proc.readProcessOutput(buf, (int)sizeof(buf));
        if (n <= 0) break;
        captured.write(buf, (size_t)n);
    }

    // The worker brackets its XML with sentinels because some plugins print their
    // own log lines to stdout while loading (e.g. Maschine's "[maschine_lib_logger]
    // … Logfile created"), which would corrupt a raw parse and silently drop an
    // otherwise-valid plugin. Slice out the bracketed text; fall back to the XML
    // prolog/root if the markers are missing (older worker / clean output).
    const juce::String out = captured.toString();
    juce::String xmlText;
    const int b = out.indexOf("<<<NOTA_PLUGINS_BEGIN>>>");
    const int e = out.lastIndexOf("<<<NOTA_PLUGINS_END>>>");
    if (b >= 0 && e > b) {
        xmlText = out.substring(b + (int)juce::String("<<<NOTA_PLUGINS_BEGIN>>>").length(), e);
    } else {
        int x = out.indexOf("<?xml");
        if (x < 0) x = out.indexOf("<plugins");
        if (x >= 0) xmlText = out.substring(x);
    }
    auto xml = juce::parseXML(xmlText);
    if (xml == nullptr) return;
    for (auto* child : xml->getChildIterator()) {
        juce::PluginDescription desc;
        if (desc.loadFromXml(*child))
            catalog().addType(desc);
    }
}

} // namespace

extern "C" NOTA_API int32_t nota_pluginhost_scan(const char* worker_path) {
    // No JUCE GUI init here on purpose: scanning (format enumeration + child
    // processes + XML) needs no MessageManager, and this may run off the main
    // thread. The MessageManager stays owned by the UI thread (editor windows).
    if (worker_path == nullptr) return -1;
    loadCatalogOnce();
    loadScanPathsOnce();

    const juce::String workerPath{juce::CharPointer_UTF8(worker_path)};
    if (!juce::File(workerPath).existsAsFile()) return -2;

    // Forget VST3 bundles that are gone from disk (uninstalled, e.g. via Get Plug-ins),
    // so the browser stops listing them. AU lives in the system registry — left alone.
    for (const auto& t : catalog().getTypes())
        if (t.pluginFormatName == "VST3" && !juce::File(t.fileOrIdentifier).exists())
            catalog().removeType(t);

    for (const char* formatName : kFormatOrder) {
        auto* format = findFormat(formatName);
        if (format == nullptr) continue;

        // Default OS locations plus any user-added dirs (file-based formats).
        juce::FileSearchPath search = format->getDefaultLocationsToSearch();
        for (const auto& p : extraScanPaths())
            search.addIfNotAlreadyThere(juce::File(p));

        const auto ids = format->searchPathsForPlugins(
            search, /*recursive*/ true, /*allowAsync*/ false);

        for (const auto& id : ids) {
            // Skip ids we already know (cheap re-scan; full rescan is a later task).
            bool known = false;
            for (const auto& t : catalog().getTypes())
                if (t.fileOrIdentifier == id) { known = true; break; }
            if (!known)
                scanOne(workerPath, formatName, id);
        }
    }

    saveCatalog();
    return catalog().getNumTypes();
}

extern "C" NOTA_API int32_t nota_pluginhost_plugin_count(void) {
    loadCatalogOnce();
    return catalog().getNumTypes();
}

extern "C" NOTA_API const char* nota_pluginhost_plugin_desc(int32_t index) {
    loadCatalogOnce();
    const auto types = catalog().getTypes();
    if (index < 0 || index >= types.size())
        return nullptr;

    const auto& d = types.getReference(index);
    static std::string line; // owned by the engine, valid until next call
    line = (d.name + " | " + d.pluginFormatName + " | "
            + (d.isInstrument ? "inst" : "fx") + " | "
            + (d.manufacturerName.isEmpty() ? juce::String("?") : d.manufacturerName)
            // The scan's category — VST3 sub-categories ("Fx|Reverb", '|' turned into '/' so the
            // line still splits on '|') or the AU type; the command palette classifies by it.
            + " | " + d.category.replaceCharacter('|', '/'))
               .toStdString();
    return line.c_str();
}

extern "C" NOTA_API const char* nota_pluginhost_plugin_id(int32_t index) {
    loadCatalogOnce();
    const auto types = catalog().getTypes();
    if (index < 0 || index >= types.size())
        return nullptr;
    static std::string id; // owned by the engine, valid until next call
    id = types.getReference(index).createIdentifierString().toStdString();
    return id.c_str();
}

extern "C" NOTA_API int32_t nota_pluginhost_index_of_id(const char* identifier) {
    if (identifier == nullptr) return -1;
    loadCatalogOnce();
    const juce::String want{juce::CharPointer_UTF8(identifier)};
    const auto types = catalog().getTypes();
    for (int i = 0; i < types.size(); ++i)
        if (types.getReference(i).createIdentifierString() == want) return i;

    // Fallback: JUCE ids are "<format>-<name>-<hex path hash>-<hex uid>", so the same
    // plugin at another path (another machine, or Nota's managed plugin folder, whose
    // path holds the user name) never matches exactly. Match format + name + uid instead.
    const int uidDash = want.lastIndexOfChar('-');
    const int hashDash = uidDash > 0 ? want.substring(0, uidDash).lastIndexOfChar('-') : -1;
    if (hashDash <= 0) return -1;
    const auto prefix = want.substring(0, hashDash);
    const auto uid = want.substring(uidDash + 1);
    for (int i = 0; i < types.size(); ++i) {
        const auto& t = types.getReference(i);
        if (t.pluginFormatName + "-" + t.name == prefix
            && (juce::String::toHexString(t.uniqueId) == uid
                || juce::String::toHexString(t.deprecatedUid) == uid))
            return i;
    }
    return -1;
}

extern "C" NOTA_API NotaResult nota_pluginhost_add_scan_path(const char* dir) {
    if (dir == nullptr) return NOTA_ERR_INVALID_ARG;
    loadScanPathsOnce();
    const juce::String d{juce::CharPointer_UTF8(dir)};
    if (d.isNotEmpty() && !extraScanPaths().contains(d)) {
        extraScanPaths().add(d);
        saveScanPaths();
    }
    return NOTA_OK;
}

extern "C" NOTA_API NotaResult nota_pluginhost_remove_scan_path(int32_t index) {
    loadScanPathsOnce();
    if (index < 0 || index >= extraScanPaths().size())
        return NOTA_ERR_INVALID_ARG;
    extraScanPaths().remove(index);
    saveScanPaths();
    return NOTA_OK;
}

extern "C" NOTA_API int32_t nota_pluginhost_scan_path_count(void) {
    loadScanPathsOnce();
    return extraScanPaths().size();
}

extern "C" NOTA_API const char* nota_pluginhost_scan_path(int32_t index) {
    loadScanPathsOnce();
    if (index < 0 || index >= extraScanPaths().size())
        return nullptr;
    static std::string path; // owned by the engine, valid until next call
    path = extraScanPaths()[index].toStdString();
    return path.c_str();
}

// ---- Hosted-plugin adapters (M3-3) -----------------------------------------
// PluginInstrument/PluginEffect wrap a juce::AudioPluginInstance behind the
// core JUCE-free interfaces (Instrument/Device). All audio-thread methods are
// allocation-free after construction (buffers pre-sized). MIDI/DSP state is
// audio-thread-owned; prepare happens on the message thread before publish.

namespace {

constexpr int kMaxChans = 64;
// Channels a referring juce::AudioBuffer holds without allocating (its preallocated
// space is 32 pointers, one of them the null terminator).
constexpr int kNoAllocChans = 31;

// Channel pointers for one processBlock: `storage`'s channels, then every further channel
// the plugin may address pointing at `spare` (one zeroed, discarded channel). JUCE maps the
// plugin's buses with buffer.getWritePointer(ch), which is null past the buffer's width, and a
// VST3 plugin can widen its layout at any moment (restartComponent kIoChanged → JUCE
// re-prepares it on the message thread, e.g. Reaktor switching Play/Patch/Build) — so the
// buffer is never narrower than the plugin's current layout, with headroom for a change that
// lands between this read and processBlock. Returns the channel count. Audio thread.
int pluginChannelPtrs(const juce::AudioProcessor& p, juce::AudioBuffer<float>& storage,
                      float* spare, int frames, float* (&ptrs)[kMaxChans]) {
    const int have = juce::jmin(storage.getNumChannels(), kMaxChans);
    const int want = juce::jmin(kMaxChans, juce::jmax(kNoAllocChans, have,
                                                      p.getTotalNumInputChannels(), p.getTotalNumOutputChannels()));
    if (want > have) juce::FloatVectorOperations::clear(spare, frames);
    for (int c = 0; c < want; ++c) ptrs[c] = c < have ? storage.getWritePointer(c) : spare;
    return want;
}

// Turn on every bus the plugin has, keeping the main buses as the caller set them. JUCE hands
// an inactive VST3 bus to process() with null channel pointers (HostBufferMapper), and some
// multi-output instruments (Vienna Synchron Pianos) write to every output bus regardless —
// a null write on the first block. Active buses get real channels in the process buffer;
// Nota mixes only the main stereo pair and discards the rest, like a host with unrouted
// outputs. Never use setPlayConfigDetails after this: it disables non-main buses again.
// Message thread, before prepareToPlay.
void enableEveryBus(juce::AudioPluginInstance& p) {
    if (p.enableAllBuses()) return;
    for (const bool isInput : { true, false })   // all-or-nothing failed: take what it accepts
        for (int i = 1; i < p.getBusCount(isInput); ++i)
            if (auto* bus = p.getBus(isInput, i); bus != nullptr && !bus->isEnabled()) bus->enable(true);
}

// Bus layout on stderr when NOTA_DEBUG_PLUGIN_BUSES is set — the first thing to ask for
// when a hosted plugin crashes in process().
void logBuses(const juce::AudioPluginInstance& p) {
    static const bool dbg = std::getenv("NOTA_DEBUG_PLUGIN_BUSES") != nullptr;
    if (!dbg) return;
    for (const bool isInput : { true, false })
        for (int i = 0; i < p.getBusCount(isInput); ++i)
            if (auto* bus = p.getBus(isInput, i))
                std::fprintf(stderr, "[nota buses] %s %s %d '%s' chans=%d enabled=%d\n",
                             p.getName().toRawUTF8(), isInput ? "in" : "out", i, bus->getName().toRawUTF8(),
                             bus->getLastEnabledLayout().size(), (int)bus->isEnabled());
}

// Message-thread producer -> audio-thread consumer. The editor window's computer
// keyboard pushes note events here; the plugin adapter drains them in its audio
// callback (keeps the plugin's own MidiBuffer audio-thread-only — no data race).
struct GuiNote { bool on; int32_t pitch; float velocity; };
using GuiNoteQueue = nota::SpscRingBuffer<GuiNote, 256>;

// Typing-keyboard row -> MIDI pitch, matching the app's own
// KeyToPitch map (W = C#4 = 61 … K = C5 = 72). Keyed by macOS ANSI virtual
// keycodes so it works regardless of which control has JUCE key focus. -1 = not
// a note key.
int keycodeToPitch(unsigned short kc) {
    switch (kc) {
        case 0x0D: return 61; // W
        case 0x01: return 62; // S
        case 0x0E: return 63; // E
        case 0x02: return 64; // D
        case 0x03: return 65; // F
        case 0x11: return 66; // T
        case 0x05: return 67; // G
        case 0x10: return 68; // Y
        case 0x04: return 69; // H
        case 0x20: return 70; // U
        case 0x26: return 71; // J
        case 0x28: return 72; // K
        default:   return -1;
    }
}

// A separate native window hosting a plugin's editor (M3-4). Mirrors JUCE's own
// AudioPluginHost idiom: setContentOwned(createEditorAndMakeActive()) +
// clearContentComponent() on teardown; falls back to a generic editor.
class PluginEditorWindow : public juce::DocumentWindow {
public:
    // Set by the owning adapter (message thread) before/at open; called from the
    // key monitor to inject computer-keyboard MIDI into THIS plugin.
    std::function<void(int32_t, float)> onNoteOn;
    std::function<void(int32_t)>        onNoteOff;

    explicit PluginEditorWindow(juce::AudioPluginInstance& p)
        : juce::DocumentWindow(p.getName().isNotEmpty() ? p.getName() : juce::String("Plugin"),
                               juce::Colours::black,
                               juce::DocumentWindow::minimiseButton | juce::DocumentWindow::closeButton) {
        setUsingNativeTitleBar(true);
        setSize(400, 300);
        juce::AudioProcessorEditor* ui = nullptr;
        if (p.hasEditor()) ui = p.createEditorAndMakeActive();
        if (ui == nullptr) ui = new juce::GenericAudioProcessorEditor(p);
        setContentOwned(ui, true);
        setResizable(ui->isResizable(), false);
        setTopLeftPosition(80, 80);
        // Float above the Nota main window and bring it to the foreground so the
        // user doesn't have to hunt for it behind the app (req: always on top).
        // Under `dotnet run` (no .app bundle) the process starts as a non-foreground
        // app, so a fresh native window opens *behind* Avalonia and never takes key
        // focus — promote the process and activate the app first, then order front.
        juce::Process::makeForegroundProcess();
#if JUCE_MAC
        [NSApp activateIgnoringOtherApps:YES];
#endif
        setAlwaysOnTop(true);
        setVisible(true);
        toFront(true);
        installKeyMonitor();
    }
    ~PluginEditorWindow() override { removeKeyMonitor(); clearContentComponent(); }
    void closeButtonPressed() override { setVisible(false); }

private:
    // Local NSEvent monitor: while this plugin window is key, map the piano-row
    // keys to MIDI and inject them, so a plugin whose native view swallows JUCE
    // key events still responds to the computer keyboard. Non-note keys and
    // modifier combos pass straight through to the plugin.
    // On Windows JUCE key events reach the editor normally, so the workaround is
    // macOS-only (native NSViews there swallow JUCE key events).
    void installKeyMonitor() {
#if JUCE_MAC
        keyMonitor_ = [[NSEvent addLocalMonitorForEventsMatchingMask:(NSEventMaskKeyDown | NSEventMaskKeyUp)
            handler:^NSEvent* (NSEvent* ev) {
                void* handle = this->getWindowHandle();
                if (handle == nullptr || ev.window != ((NSView*)handle).window)
                    return ev;   // not our window — leave the event alone
                if (ev.modifierFlags & (NSEventModifierFlagCommand | NSEventModifierFlagControl
                                        | NSEventModifierFlagOption))
                    return ev;   // shortcut chord, not a note
                if (!onNoteOn && !onNoteOff) return ev;   // no note sink (e.g. effect plugin)
                const int pitch = keycodeToPitch(ev.keyCode);
                if (pitch < 0) return ev;
                if (ev.type == NSEventTypeKeyDown) {
                    if (!ev.isARepeat && onNoteOn) onNoteOn(pitch, 0.85f);
                } else if (onNoteOff) {
                    onNoteOff(pitch);
                }
                return nil;      // consume the note key (no system beep, no double-handle)
            }] retain];
#endif
    }
    void removeKeyMonitor() {
#if JUCE_MAC
        if (keyMonitor_ != nil) { [NSEvent removeMonitor:keyMonitor_]; [keyMonitor_ release]; keyMonitor_ = nil; }
#endif
    }

#if JUCE_MAC
    id keyMonitor_ = nil;
#endif
};

// Owns (at most one) editor window for a plugin; open() lazily creates it and
// re-shows on subsequent calls. Message thread only. The adapter wires
// onNoteOn/onNoteOff so the window's computer keyboard reaches its plugin.
struct EditorHost {
    std::unique_ptr<PluginEditorWindow> window;
    std::function<void(int32_t, float)> onNoteOn;
    std::function<void(int32_t)>        onNoteOff;

    void open(juce::AudioPluginInstance& p) {
        if (juce::MessageManager::getInstanceWithoutCreating() == nullptr) return;
        if (window == nullptr) {
            window = std::make_unique<PluginEditorWindow>(p);
            window->onNoteOn  = onNoteOn;
            window->onNoteOff = onNoteOff;
        } else {
            window->setVisible(true);
            window->toFront(true);
        }
    }
    void close() { if (window) window->setVisible(false); }
};

// Opaque plugin state <-> byte vector (M3-5).
std::vector<uint8_t> readState(juce::AudioPluginInstance* p) {
    if (p == nullptr) return {};
    juce::MemoryBlock mb;
    p->getStateInformation(mb);
    const auto* bytes = static_cast<const uint8_t*>(mb.getData());
    return std::vector<uint8_t>(bytes, bytes + mb.getSize());
}

void writeState(juce::AudioPluginInstance* p, const uint8_t* data, int32_t size) {
    if (p != nullptr && data != nullptr && size > 0)
        p->setStateInformation(data, size);
}

// --- hosted-plugin parameter access (M9-B) ---------------------------------
// Shared by both adapters. Values are normalized 0..1; identity is the stable
// hosted paramID (juce::HostedAudioProcessorParameter::getParameterID), which
// survives plugin version changes where the bare index would not.
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

// Bridges Nota's JUCE-free TransportInfo to a juce::AudioPlayHead so hosted
// plugins (synced delays/LFOs, arps, loopers) follow the DAW clock. Both adapters
// own one and hand it to their plugin via setPlayHead(). `info` is written by the
// audio thread in setTransportInfo() and read by the same thread inside the
// plugin's processBlock() (via getPosition()) — no cross-thread sharing.
class NotaPlayHead : public juce::AudioPlayHead {
public:
    nota::TransportInfo info;

    juce::Optional<PositionInfo> getPosition() const override {
        PositionInfo p;
        p.setBpm(info.bpm);
        p.setTimeSignature(TimeSignature{ info.tsNum, info.tsDenom });
        p.setPpqPosition(info.ppqPosition);
        // Start of the current bar (quarter notes): many tempo-synced plugins
        // beat-align their playback to this rather than to the raw ppq.
        const double beatsPerBar = info.tsDenom > 0 ? info.tsNum * (4.0 / info.tsDenom)
                                                    : static_cast<double>(info.tsNum);
        if (beatsPerBar > 0.0)
            p.setPpqPositionOfLastBarStart(std::floor(info.ppqPosition / beatsPerBar) * beatsPerBar);
        p.setTimeInSamples(info.timeInSamples);
        p.setTimeInSeconds(info.timeInSeconds);
        p.setIsPlaying(info.isPlaying);
        p.setIsLooping(info.isLooping);
        if (info.isLooping)
            p.setLoopPoints(LoopPoints{ info.ppqLoopStart, info.ppqLoopEnd });

        // Diagnostic (NOTA_DEBUG_PLAYHEAD=1): proves the plugin is actually querying
        // our playhead and shows the values it sees. If nothing ever prints while a
        // plugin runs, that plugin ignores host transport (its own problem, not ours).
        static const bool dbg = std::getenv("NOTA_DEBUG_PLAYHEAD") != nullptr;
        if (dbg) {
            static std::atomic<int> n{0};
            if ((n.fetch_add(1, std::memory_order_relaxed) % 200) == 0)
                std::fprintf(stderr, "[nota playhead] bpm=%.2f ppq=%.3f playing=%d looping=%d\n",
                             info.bpm, info.ppqPosition, (int)info.isPlaying, (int)info.isLooping);
        }
        return p;
    }
};

class PluginInstrument : public nota::Instrument, private juce::AudioProcessorListener {
public:
    PluginInstrument(std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock)
        : plugin_(std::move(p)), maxBlock_(maxBlock) {
        pending_.ensureSize(8192);
        if (plugin_) name_ = plugin_->getName().toStdString();   // cache for displayName()
        prepare(sr);
        if (plugin_) plugin_->addListener(this);   // "Learn" (M9-B3)
        // Editor-window computer keyboard -> this plugin (drained on the audio thread).
        editor_.onNoteOn  = [this](int32_t pitch, float vel) { guiNotes_.push({true, pitch, vel}); };
        editor_.onNoteOff = [this](int32_t pitch)            { guiNotes_.push({false, pitch, 0.0f}); };
    }
    const char* displayName() const override { return name_.empty() ? "Plugin" : name_.c_str(); }
    ~PluginInstrument() override { if (plugin_) plugin_->removeListener(this); }

    void setSampleRate(double sr) override { prepare(sr); }

    void noteOn(int32_t pitch, float velocity) override {
        pending_.addEvent(juce::MidiMessage::noteOn(1, pitch, (float)velocity), 0);
    }
    void noteOff(int32_t pitch) override {
        pending_.addEvent(juce::MidiMessage::noteOff(1, pitch), 0);
    }
    void allNotesOff() override {
        pending_.addEvent(juce::MidiMessage::allNotesOff(1), 0);
    }
    // Expression reaches a plugin as plain channel-1 MIDI: a note's pressure as polyphonic
    // aftertouch. (Per-note bend and slide would need the plugin's own MPE channel layout —
    // not sent.) The instrument-wide wheel / pressure / CCs arrive through controller().
    void noteExpression(int32_t pitch, int32_t dim, float value) override {
        if (pitch >= 0 && dim == 1)
            pending_.addEvent(juce::MidiMessage::aftertouchChange(1, pitch, u7(value)), 0);
    }
    // Every controller as the MIDI message itself (specs/020): any CC with its real value
    // (the sustain pedal too, so a piano can half-pedal), the 14-bit wheel, channel pressure.
    void controller(int32_t ctl, float value) override {
        if (ctl == nota::kCtlPitchWheel)
            pending_.addEvent(juce::MidiMessage::pitchWheel(1, juce::jlimit(0, 16383, (int)std::lround((value + 1.0f) * 8191.5f))), 0);
        else if (ctl == nota::kCtlChannelPressure)
            pending_.addEvent(juce::MidiMessage::channelPressureChange(1, u7(value)), 0);
        else if (ctl >= 0 && ctl < 128)
            pending_.addEvent(juce::MidiMessage::controllerEvent(1, ctl, u7(value)), 0);
    }
    static int u7(float v) { return juce::jlimit(0, 127, (int)std::lround(v * 127.0f)); }

    void render(float* out, int32_t frames) override {
        if (frames <= 0 || frames > maxBlock_ || plugin_ == nullptr) return;
        // Drain computer-keyboard notes from the editor window (message thread) into
        // this block's MIDI buffer — audio thread only touches pending_ here.
        for (GuiNote g; guiNotes_.pop(g); )
            pending_.addEvent(g.on ? juce::MidiMessage::noteOn(1, g.pitch, g.velocity)
                                   : juce::MidiMessage::noteOff(1, g.pitch), 0);
        storage_.clear();
        float* ptrs[kMaxChans];
        juce::AudioBuffer<float> block(ptrs, pluginChannelPtrs(*plugin_, storage_, spare_.data(), frames, ptrs), frames);
        plugin_->processBlock(block, pending_);
        pending_.clear();
        const float* l = block.getReadPointer(0);
        const float* r = stereoOut_ ? block.getReadPointer(1) : l;   // channel 1 of a mono main is another bus
        for (int i = 0; i < frames; ++i) { out[i * 2] += l[i]; out[i * 2 + 1] += r[i]; }
    }

    // DAW transport sync (AudioPlayHead): store this block's snapshot; the plugin
    // reads it via getPosition() during processBlock. Audio thread.
    void setTransportInfo(const nota::TransportInfo& ti) override { playHead_.info = ti; }

    void openEditor() override  { if (plugin_) editor_.open(*plugin_); }
    void closeEditor() override { editor_.close(); }

    std::vector<uint8_t> getState() const override { return readState(plugin_.get()); }
    void setState(const uint8_t* data, int32_t size) override { writeState(plugin_.get(), data, size); }

    std::string pluginIdentifier() const override {
        return plugin_ ? plugin_->getPluginDescription().createIdentifierString().toStdString() : std::string{};
    }

    int32_t latencySamples() const override { return plugin_ ? plugin_->getLatencySamples() : 0; }

    // Hosted-plugin parameter automation (M9-B).
    int32_t     pluginParamCount() const override { return paramCountImpl(plugin_.get()); }
    std::string pluginParamId(int32_t i) const override { return paramIdImpl(plugin_.get(), i); }
    std::string pluginParamName(int32_t i) const override { return paramNameImpl(plugin_.get(), i); }
    std::string pluginParamText(int32_t i) const override { return paramTextImpl(plugin_.get(), i); }
    float       pluginParamDefault(int32_t i) const override { return paramDefaultImpl(plugin_.get(), i); }
    float       pluginParamGet(int32_t i) const override { return paramGetImpl(plugin_.get(), i); }
    void        pluginParamSet(int32_t i, float v) override { paramSetImpl(plugin_.get(), i, v); }
    int32_t     pluginParamIndexOfId(const std::string& id) const override { return paramIndexOfIdImpl(plugin_.get(), id); }
    int32_t     lastTouchedPluginParam() override { return lastTouched_.exchange(-1, std::memory_order_relaxed); }
    int32_t     takePluginGestureBegin() override { return gestureBegin_.exchange(-1, std::memory_order_relaxed); }
    int32_t     takePluginGestureEnd() override { return gestureEnd_.exchange(-1, std::memory_order_relaxed); }

    std::shared_ptr<nota::Instrument> clone() const override {
        if (!plugin_) return nullptr;
        juce::String err;
        double sr = plugin_->getSampleRate() > 0 ? plugin_->getSampleRate() : 44100.0;
        auto inst = formatManager().createPluginInstance(plugin_->getPluginDescription(), sr, maxBlock_, err);
        if (!inst) return nullptr;
        juce::MemoryBlock mb; plugin_->getStateInformation(mb);
        inst->setStateInformation(mb.getData(), (int)mb.getSize());
        return std::make_shared<PluginInstrument>(std::move(inst), sr, maxBlock_);
    }

private:
    // AudioProcessorListener: GUI edits call setValueNotifyingHost -> here; our
    // automation uses setValue (no notify), so this reflects only user gestures.
    void audioProcessorParameterChanged(juce::AudioProcessor*, int index, float) override {
        lastTouched_.store(index, std::memory_order_relaxed);
    }
    void audioProcessorParameterChangeGestureBegin(juce::AudioProcessor*, int index) override {
        gestureBegin_.store(index, std::memory_order_relaxed);
    }
    void audioProcessorParameterChangeGestureEnd(juce::AudioProcessor*, int index) override {
        gestureEnd_.store(index, std::memory_order_relaxed);
    }
    void audioProcessorChanged(juce::AudioProcessor*, const juce::AudioProcessorListener::ChangeDetails&) override {}

    void prepare(double sr) {
        // Stereo main output (instruments get no input of their own), then every other bus on.
        if (plugin_->getBusCount(false) > 0) plugin_->setChannelLayoutOfBus(false, 0, juce::AudioChannelSet::stereo());
        enableEveryBus(*plugin_);
        logBuses(*plugin_);
        plugin_->setRateAndBufferSizeDetails(sr, maxBlock_);
        plugin_->prepareToPlay(sr, maxBlock_);
        plugin_->setPlayHead(&playHead_);   // DAW transport sync
        // In and out share the buffer, so it spans the wider side.
        storage_.setSize(juce::jmax(2, plugin_->getTotalNumInputChannels(), plugin_->getTotalNumOutputChannels()), maxBlock_);
        stereoOut_ = plugin_->getBusCount(false) > 0 && plugin_->getChannelCountOfBus(false, 0) > 1;
        spare_.assign((size_t)maxBlock_, 0.0f);
    }

    NotaPlayHead playHead_;   // declared first: outlives plugin_ (which points at it)
    std::unique_ptr<juce::AudioPluginInstance> plugin_;
    juce::AudioBuffer<float> storage_;
    std::vector<float> spare_;   // the channels past storage_ (see pluginChannelPtrs)
    juce::MidiBuffer pending_;
    int maxBlock_;
    bool stereoOut_ = true;   // main output bus has a channel 1 (see prepare)
    std::string name_;   // cached plugin name for displayName()
    std::atomic<int32_t> lastTouched_{-1};   // "Learn" (M9-B3)
    std::atomic<int32_t> gestureBegin_{-1}, gestureEnd_{-1};   // write gestures (M9-C)
    GuiNoteQueue guiNotes_;   // editor-window computer keyboard -> render() (message->audio thread)
    EditorHost editor_; // declared last: destroyed before plugin_ (detaches editor)
};

class PluginEffect : public nota::Device, private juce::AudioProcessorListener {
public:
    PluginEffect(std::unique_ptr<juce::AudioPluginInstance> p, double sr, int maxBlock)
        : plugin_(std::move(p)) {
        if (plugin_) name_ = plugin_->getName().toStdString();
        empty_.ensureSize(256);
        setSampleRate(sr, maxBlock);
        if (plugin_) plugin_->addListener(this);   // "Learn" (M9-B3)
    }
    ~PluginEffect() override { if (plugin_) plugin_->removeListener(this); }

    const char* displayName() const override { return name_.empty() ? "Plugin" : name_.c_str(); }

    void setSampleRate(double sr, int32_t maxBlock) override {
        maxBlock_ = maxBlock;
        // Prefer stereo main I/O plus a sidechain input bus if the plugin exposes
        // one (input bus index 1, Phase C). Fall back to plain stereo I/O.
        hasSidechain_ = false; scChanOffset_ = -1; scNumChans_ = 0;
        if (plugin_->getBusCount(true) >= 2) {
            auto layout = plugin_->getBusesLayout();
            if (plugin_->getBusCount(true)  > 0) layout.inputBuses.getReference(0)  = juce::AudioChannelSet::stereo();
            if (plugin_->getBusCount(false) > 0) layout.outputBuses.getReference(0) = juce::AudioChannelSet::stereo();
            layout.inputBuses.getReference(1) = juce::AudioChannelSet::stereo();
            if (plugin_->setBusesLayout(layout)) hasSidechain_ = true;
            else {
                layout.inputBuses.getReference(1) = juce::AudioChannelSet::mono();
                if (plugin_->setBusesLayout(layout)) hasSidechain_ = true;
            }
        }
        if (!hasSidechain_) {   // stereo main I/O
            if (plugin_->getBusCount(true)  > 0) plugin_->setChannelLayoutOfBus(true,  0, juce::AudioChannelSet::stereo());
            if (plugin_->getBusCount(false) > 0) plugin_->setChannelLayoutOfBus(false, 0, juce::AudioChannelSet::stereo());
        }
        enableEveryBus(*plugin_);   // an unused sidechain input is fed silence (storage_ cleared)
        logBuses(*plugin_);
        plugin_->setRateAndBufferSizeDetails(sr, maxBlock_);
        plugin_->prepareToPlay(sr, maxBlock_);
        const int chans = juce::jmax(2, juce::jmax(plugin_->getTotalNumInputChannels(),
                                                   plugin_->getTotalNumOutputChannels()));
        storage_.setSize(chans, maxBlock_);
        stereoIn_  = plugin_->getBusCount(true)  > 0 && plugin_->getChannelCountOfBus(true,  0) > 1;
        stereoOut_ = plugin_->getBusCount(false) > 0 && plugin_->getChannelCountOfBus(false, 0) > 1;
        spare_.assign((size_t)maxBlock_, 0.0f);
        if (hasSidechain_ && plugin_->getBus(true, 1) != nullptr) {
            scChanOffset_ = plugin_->getChannelIndexInProcessBlockBuffer(true, 1, 0);
            scNumChans_   = plugin_->getBus(true, 1)->getNumberOfChannels();
        }
        plugin_->setPlayHead(&playHead_);   // DAW transport sync
    }

    int32_t latencySamples() const override { return plugin_ ? plugin_->getLatencySamples() : 0; }

    void process(float* buf, int32_t frames) override {
        if (frames <= 0 || frames > maxBlock_ || plugin_ == nullptr) return;
        const int nc = juce::jmin(storage_.getNumChannels(), kMaxChans);
        storage_.clear();
        float* l = storage_.getWritePointer(0);
        float* r = stereoIn_ ? storage_.getWritePointer(1) : spare_.data();   // mono main: drop R, don't feed the next bus
        for (int i = 0; i < frames; ++i) { l[i] = buf[i * 2]; r[i] = buf[i * 2 + 1]; }

        // Feed the sidechain input bus (Phase C) if the engine handed us a source
        // this block; otherwise it stays silent (cleared above).
        if (hasSidechain_ && scBuf_ != nullptr && scFrames_ >= frames && scChanOffset_ >= 0) {
            const float scGain = std::pow(10.0f, sidechainGainDb() / 20.0f);   // Phase D
            if (scNumChans_ >= 2 && scChanOffset_ + 1 < nc) {
                float* sl = storage_.getWritePointer(scChanOffset_);
                float* sr = storage_.getWritePointer(scChanOffset_ + 1);
                for (int i = 0; i < frames; ++i) { sl[i] = scBuf_[i * 2] * scGain; sr[i] = scBuf_[i * 2 + 1] * scGain; }
            } else if (scNumChans_ == 1 && scChanOffset_ < nc) {
                float* sm = storage_.getWritePointer(scChanOffset_);
                for (int i = 0; i < frames; ++i) sm[i] = 0.5f * (scBuf_[i * 2] + scBuf_[i * 2 + 1]) * scGain;
            }
        }
        scBuf_ = nullptr;   // consume once; the engine re-hands it each block if a source is set

        float* ptrs[kMaxChans];
        juce::AudioBuffer<float> block(ptrs, pluginChannelPtrs(*plugin_, storage_, spare_.data(), frames, ptrs), frames);
        empty_.clear();
        plugin_->processBlock(block, empty_);

        const float* ol = block.getReadPointer(0);
        const float* orr = stereoOut_ ? block.getReadPointer(1) : ol;
        const float mix = sidechainMix();   // dry/wet of the plugin (Phase D)
        for (int i = 0; i < frames; ++i) {
            const float dl = buf[i * 2], dr = buf[i * 2 + 1];
            buf[i * 2]     = dl * (1.0f - mix) + ol[i]  * mix;
            buf[i * 2 + 1] = dr * (1.0f - mix) + orr[i] * mix;
        }
    }

    // DAW transport sync (AudioPlayHead): store this block's snapshot; the plugin
    // reads it via getPosition() during processBlock. Audio thread.
    void setTransportInfo(const nota::TransportInfo& ti) override { playHead_.info = ti; }

    void openEditor() override  { if (plugin_) editor_.open(*plugin_); }
    void closeEditor() override { editor_.close(); }

    std::vector<uint8_t> getState() const override { return readState(plugin_.get()); }
    void setState(const uint8_t* data, int32_t size) override { writeState(plugin_.get(), data, size); }

    std::string pluginIdentifier() const override {
        return plugin_ ? plugin_->getPluginDescription().createIdentifierString().toStdString() : std::string{};
    }

    // Hosted-plugin parameter automation (M9-B).
    int32_t     pluginParamCount() const override { return paramCountImpl(plugin_.get()); }
    std::string pluginParamId(int32_t i) const override { return paramIdImpl(plugin_.get(), i); }
    std::string pluginParamName(int32_t i) const override { return paramNameImpl(plugin_.get(), i); }
    std::string pluginParamText(int32_t i) const override { return paramTextImpl(plugin_.get(), i); }
    float       pluginParamDefault(int32_t i) const override { return paramDefaultImpl(plugin_.get(), i); }
    float       pluginParamGet(int32_t i) const override { return paramGetImpl(plugin_.get(), i); }
    void        pluginParamSet(int32_t i, float v) override { paramSetImpl(plugin_.get(), i, v); }
    int32_t     pluginParamIndexOfId(const std::string& id) const override { return paramIndexOfIdImpl(plugin_.get(), id); }
    int32_t     lastTouchedPluginParam() override { return lastTouched_.exchange(-1, std::memory_order_relaxed); }
    int32_t     takePluginGestureBegin() override { return gestureBegin_.exchange(-1, std::memory_order_relaxed); }
    int32_t     takePluginGestureEnd() override { return gestureEnd_.exchange(-1, std::memory_order_relaxed); }

    // Sidechain / routing (Phase C). The engine drives these generically for any
    // Device; the aux input bus is fed in process() above.
    int32_t sidechainSourceTrackId() const override { return scTrackId_.load(std::memory_order_relaxed); }
    void    setSidechainSourceTrackId(int32_t id) override { scTrackId_.store(id, std::memory_order_relaxed); }
    void    setSidechain(const float* interleaved, int32_t frames) override { scBuf_ = interleaved; scFrames_ = frames; }
    bool    acceptsSidechain() const override { return hasSidechain_; }

    std::shared_ptr<nota::Device> clone() const override {
        if (!plugin_) return nullptr;
        juce::String err;
        double sr = plugin_->getSampleRate() > 0 ? plugin_->getSampleRate() : 44100.0;
        auto inst = formatManager().createPluginInstance(plugin_->getPluginDescription(), sr, maxBlock_, err);
        if (!inst) return nullptr;
        juce::MemoryBlock mb; plugin_->getStateInformation(mb);
        inst->setStateInformation(mb.getData(), (int)mb.getSize());
        auto d = std::make_shared<PluginEffect>(std::move(inst), sr, maxBlock_);
        d->setSidechainSourceTrackId(scTrackId_.load(std::memory_order_relaxed));
        return d;
    }

private:
    void audioProcessorParameterChanged(juce::AudioProcessor*, int index, float) override {
        lastTouched_.store(index, std::memory_order_relaxed);
    }
    void audioProcessorParameterChangeGestureBegin(juce::AudioProcessor*, int index) override {
        gestureBegin_.store(index, std::memory_order_relaxed);
    }
    void audioProcessorParameterChangeGestureEnd(juce::AudioProcessor*, int index) override {
        gestureEnd_.store(index, std::memory_order_relaxed);
    }
    void audioProcessorChanged(juce::AudioProcessor*, const juce::AudioProcessorListener::ChangeDetails&) override {}

    NotaPlayHead playHead_;   // declared first: outlives plugin_ (which points at it)
    std::unique_ptr<juce::AudioPluginInstance> plugin_;
    std::string name_;
    std::atomic<int32_t> lastTouched_{-1};   // "Learn" (M9-B3)
    std::atomic<int32_t> gestureBegin_{-1}, gestureEnd_{-1};   // write gestures (M9-C)
    std::atomic<int32_t> scTrackId_{-1};     // sidechain source track (Phase C)
    const float* scBuf_ = nullptr;           // engine-handed sidechain buffer (audio thread)
    int32_t scFrames_ = 0;
    bool hasSidechain_ = false;              // plugin exposes a usable sidechain input bus
    int  scChanOffset_ = -1, scNumChans_ = 0; // where its channels sit in the process buffer
    juce::AudioBuffer<float> storage_;
    std::vector<float> spare_;   // the channels past storage_ (see pluginChannelPtrs)
    juce::MidiBuffer empty_;
    int maxBlock_ = 0;
    bool stereoIn_ = true, stereoOut_ = true;   // main buses have a channel 1 (see setSampleRate)
    EditorHost editor_; // declared last: destroyed before plugin_
};

// In-process instantiation of a catalog entry (message thread).
std::unique_ptr<juce::AudioPluginInstance> instantiate(int catalogIndex, double sr, int maxBlock) {
    loadCatalogOnce();
    if (g_juceGui == nullptr)
        g_juceGui = new juce::ScopedJuceInitialiser_GUI();
    const auto types = catalog().getTypes();
    if (catalogIndex < 0 || catalogIndex >= types.size())
        return nullptr;
    juce::String err;
    auto inst = formatManager().createPluginInstance(types.getReference(catalogIndex), sr, maxBlock, err);
    return inst; // null on failure
}

} // namespace

namespace nota {

std::shared_ptr<Instrument> createPluginInstrument(int32_t catalogIndex, double sr, int32_t maxBlock) {
    const auto types = catalog().getTypes();
    if (catalogIndex >= 0 && catalogIndex < types.size() && !types.getReference(catalogIndex).isInstrument)
        return nullptr; // not an instrument
    auto inst = instantiate(catalogIndex, sr, maxBlock);
    if (!inst) return nullptr;
    return std::make_shared<PluginInstrument>(std::move(inst), sr, maxBlock);
}

std::shared_ptr<Device> createPluginEffect(int32_t catalogIndex, double sr, int32_t maxBlock) {
    auto inst = instantiate(catalogIndex, sr, maxBlock);
    if (!inst) return nullptr;
    return std::make_shared<PluginEffect>(std::move(inst), sr, maxBlock);
}

} // namespace nota
