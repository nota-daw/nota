// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// abi_pluginscanner — C ABI for plugin discovery and catalog.
// Implements nota_pluginhost_scan*, nota_pluginhost_plugin_* and
// nota_pluginhost_scan_path_* from include/nota/nota_engine.h on top of the
// thread-safe PluginCatalog and OutOfProcessScanner. Behavior matches the
// former pluginhost/PluginHost.cpp (M3-1) exactly; only the locking is new:
// scans take the catalog's unique lock while UI reads take the shared lock,
// and returned C strings live in thread_local buffers (valid until the next
// call on the same thread) instead of shared function-local statics.

#include "nota/nota_engine.h"

#include "OutOfProcessScanner.h"
#include "PluginCatalog.h"

#include <juce_audio_processors/juce_audio_processors.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace {

// AU first (macOS priority), then VST3 — see ARCHITECTURE.md § Plugin hosting.
const char* const kFormatOrder[] = {"AudioUnit", "VST3"};

juce::AudioPluginFormat* findFormat(const juce::String& name) {
    for (auto* f : nota::scanner::sharedFormatManager().getFormats())
        if (f->getName() == name) return f;
    return nullptr;
}

using Outcome = nota::scanner::OutOfProcessScanner::Outcome;

const char* outcomeLabel(Outcome o) {
    switch (o) {
    case Outcome::Ok: return "ok";
    case Outcome::Timeout: return "timed out, worker killed";
    case Outcome::NoAnswer: return "no answer (worker crashed or exited)";
    case Outcome::StartFailed: return "worker failed to start";
    }
    return "?";
}

// plugin-scan.log: each line is flushed as it is written, so a scan that stalls
// still names the plugins it is on. Best-effort — never fails the scan. Probes run
// in parallel, so every line carries its plugin id.
class ScanLog {
  public:
    ScanLog() {
        const auto path = nota::scanner::PluginCatalog::scanLogPath();
        if (path.empty()) return;
        const juce::File file{juce::CharPointer_UTF8(path.c_str())};
        file.getParentDirectory().createDirectory();
        file.deleteFile();
        auto s = std::make_unique<juce::FileOutputStream>(file);
        if (s->openedOk()) stream_ = std::move(s);
    }

    void line(const juce::String& text) {
        std::lock_guard lock(mutex_);
        if (stream_ == nullptr) return;
        stream_->writeText(juce::Time::getCurrentTime().toISO8601(true) + " " + text + "\n", false, false, nullptr);
        stream_->flush();
    }

  private:
    std::mutex mutex_;
    std::unique_ptr<juce::FileOutputStream> stream_;
};

// One scan at a time (Rescan, a store install and the missing-plugins offer can
// overlap); progress belongs to the running one.
std::mutex g_scanMutex;
std::atomic<bool> g_scanning{false};
std::atomic<int32_t> g_done{0};
std::atomic<int32_t> g_total{0};
std::mutex g_currentMutex;
std::string g_current;

void setCurrent(std::string name) {
    std::lock_guard lock(g_currentMutex);
    g_current = std::move(name);
}

// What the UI shows while probing: a VST3 bundle's file name. An AU has no readable
// name until it is loaded (its id is "Synths/aumu,Ni$D,-NI-"), so nothing.
std::string displayName(const std::string& id) {
    const juce::String s{juce::CharPointer_UTF8(id.c_str())};
    if (juce::File::isAbsolutePath(s)) return juce::File(s).getFileNameWithoutExtension().toStdString();
    return {};
}

// Each probe is a separate process, so they parallelise well; a few at a time keeps
// a thousand-plugin first scan in minutes without starving the audio thread.
size_t probeThreads(size_t jobs) {
    const unsigned hw = std::max(2u, std::thread::hardware_concurrency());
    return std::min<size_t>(jobs, std::clamp(hw / 2, 1u, 4u));
}

constexpr int kFinalTimeoutMs = 120000;

struct Probe {
    const char* format;
    std::string id;
};

int32_t runScan(const char* worker_path, const char* only_dir, int32_t flags) {
    // No JUCE GUI init here on purpose: scanning (format enumeration + child
    // processes + XML) needs no MessageManager, and this may run off the main
    // thread. The MessageManager stays owned by the UI thread (editor windows).
    if (worker_path == nullptr) return -1;
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadOnce();
    catalog.loadScanPathsOnce();
    catalog.loadFailedOnce();

    nota::scanner::OutOfProcessScanner worker(worker_path);
    if (!worker.workerAvailable()) return -2;
    // The final, one-at-a-time attempt gets longer: big instruments (Kontakt) can take
    // over 30 s to load on a bad day, and a timeout here hides the plugin until a Rescan.
    nota::scanner::OutOfProcessScanner patient(worker_path, kFinalTimeoutMs);

    std::lock_guard scanLock(g_scanMutex);
    g_done = 0;
    g_total = 0;
    setCurrent({});
    g_scanning = true;
    struct Idle {
        ~Idle() { g_scanning = false; }
    } idle;

    ScanLog log;
    log.line("scan start, worker " + juce::String::fromUTF8(worker_path) +
             (only_dir != nullptr ? ", only " + juce::String::fromUTF8(only_dir) : juce::String()));

    if ((flags & NOTA_SCAN_RETRY_FAILED) != 0) catalog.clearFailed();

    // Forget VST3 bundles that are gone from disk (uninstalled, e.g. via Get Plug-ins),
    // so the browser stops listing them. AU lives in the system registry — left alone.
    catalog.removeMissingVst3OnDisk();

    std::vector<Probe> probes;
    for (const char* formatName : kFormatOrder) {
        auto* format = findFormat(formatName);
        if (format == nullptr) continue;

        juce::FileSearchPath search;
        if (only_dir != nullptr) {
            // One folder (a store install): file-based formats only — AU has no folders.
            if (juce::String(formatName) != "VST3") continue;
            search.add(juce::File(juce::CharPointer_UTF8(only_dir)));
        } else {
            // Default OS locations plus any user-added dirs (file-based formats).
            search = format->getDefaultLocationsToSearch();
            for (const auto& p : catalog.scanPaths())
                search.addIfNotAlreadyThere(juce::File(juce::CharPointer_UTF8(p.c_str())));
        }

        const auto ids = format->searchPathsForPlugins(search, /*recursive*/ true, /*allowAsync*/ false);

        std::vector<std::string> discovered;
        for (const auto& id : ids) discovered.push_back(id.toStdString());
        const auto unknown = catalog.unknownIds(discovered);
        log.line(juce::String(formatName) + ": " + juce::String((int)discovered.size()) + " found, " +
                 juce::String((int)unknown.size()) + " to probe");
        for (const auto& id : unknown) probes.push_back({formatName, id});
    }
    g_total = (int32_t)probes.size();

    // Saved as the scan goes: a scan cut short (the app quit) resumes where it was.
    using clock = std::chrono::steady_clock;
    std::mutex saveMutex;
    auto lastSave = clock::now();
    auto saveNow = [&] {
        catalog.save();
        catalog.saveFailed();
        lastSave = clock::now();
    };

    // One probe. `final`: a timeout or a missing answer counts as the plugin's failure.
    // In the parallel pass it doesn't — heavy plugins (licence daemons, big sample
    // libraries) can miss the deadline only because of their neighbours — so those
    // come back for a serial retry instead. Returns false when it needs one.
    auto probeOne = [&](const Probe& probe, bool final) {
        const auto id = juce::String::fromUTF8(probe.id.c_str());
        setCurrent(displayName(probe.id));
        log.line("probe " + juce::String(probe.format) + " " + id + (final ? " (alone)" : ""));
        const auto t0 = clock::now();
        Outcome outcome = Outcome::StartFailed;
        std::string detail;
        auto descs = (final ? patient : worker).scanDescriptions(probe.format, probe.id, &outcome, &detail);
        for (auto& desc : descs) catalog.addType(*desc);
        const auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(clock::now() - t0);
        log.line("  " + id + ": " + outcomeLabel(outcome) + ", " + juce::String((int)descs.size()) + " types, " +
                 juce::String((int)ms.count()) + " ms" +
                 (detail.empty() ? juce::String() : " (" + juce::String::fromUTF8(detail.c_str()) + ")"));
        const bool retry = !final && (outcome == Outcome::Timeout || outcome == Outcome::NoAnswer);
        // StartFailed is ours (no worker), not the plugin's — don't hold it against it.
        if (!descs.empty()) catalog.forgetFailed(probe.id);
        else if (!retry && outcome != Outcome::StartFailed) catalog.markFailed(probe.id);
        ++g_done;
        std::lock_guard lock(saveMutex);
        if (clock::now() - lastSave > std::chrono::seconds(5)) saveNow();
        return !retry;
    };

    std::vector<Probe> retries;
    std::mutex retriesMutex;
    std::atomic<size_t> next{0};
    auto work = [&] {
        for (size_t i; (i = next++) < probes.size();)
            if (!probeOne(probes[i], probes.size() == 1)) {
                std::lock_guard lock(retriesMutex);
                retries.push_back(probes[i]);
            }
    };
    const size_t threads = probeThreads(probes.size());
    log.line("probing " + juce::String((int)probes.size()) + " with " + juce::String((int)threads) + " workers");
    std::vector<std::thread> pool;
    for (size_t t = 1; t < threads; ++t) pool.emplace_back(work);
    work();
    for (auto& t : pool) t.join();

    if (!retries.empty()) {
        log.line("retrying " + juce::String((int)retries.size()) + " one at a time");
        g_total += (int32_t)retries.size();
        for (const auto& probe : retries) probeOne(probe, true);
    }

    catalog.save();
    catalog.saveFailed();
    log.line("scan done, " + juce::String(catalog.count()) + " plugins known, " + juce::String(catalog.failedCount()) +
             " failed");
    return catalog.count();
}

} // namespace

extern "C" NOTA_API int32_t nota_pluginhost_scan(const char* worker_path) {
    return nota_pluginhost_scan_ex(worker_path, nullptr, 0);
}

extern "C" NOTA_API int32_t nota_pluginhost_scan_ex(const char* worker_path, const char* only_dir, int32_t flags) {
    try {
        return runScan(worker_path, only_dir, flags);
    } catch (...) {
        g_scanning = false; // AR-7: nothing unwinds across the C ABI
        return -3;
    }
}

extern "C" NOTA_API int32_t nota_pluginhost_scan_progress(int32_t* done, int32_t* total) {
    if (done != nullptr) *done = g_done.load();
    if (total != nullptr) *total = g_total.load();
    return g_scanning.load() ? 1 : 0;
}

extern "C" NOTA_API const char* nota_pluginhost_scan_current(void) {
    thread_local std::string name;
    std::lock_guard lock(g_currentMutex);
    name = g_current;
    return name.c_str();
}

extern "C" NOTA_API int32_t nota_pluginhost_scan_failed_count(void) {
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadFailedOnce();
    return catalog.failedCount();
}

extern "C" NOTA_API int32_t nota_pluginhost_plugin_count(void) {
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadOnce();
    return catalog.count();
}

extern "C" NOTA_API const char* nota_pluginhost_plugin_desc(int32_t index) {
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadOnce();
    thread_local std::string line;
    line = catalog.descLine(index);
    return line.empty() ? nullptr : line.c_str();
}

extern "C" NOTA_API const char* nota_pluginhost_plugin_id(int32_t index) {
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadOnce();
    thread_local std::string id;
    id = catalog.idAt(index);
    return id.empty() ? nullptr : id.c_str();
}

extern "C" NOTA_API int32_t nota_pluginhost_index_of_id(const char* identifier) {
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadOnce();
    return catalog.indexOfId(identifier);
}

extern "C" NOTA_API NotaResult nota_pluginhost_add_scan_path(const char* dir) {
    if (dir == nullptr) return NOTA_ERR_INVALID_ARG;
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadScanPathsOnce();
    catalog.addScanPath(dir);
    return NOTA_OK;
}

extern "C" NOTA_API NotaResult nota_pluginhost_remove_scan_path(int32_t index) {
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadScanPathsOnce();
    return catalog.removeScanPath(index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

extern "C" NOTA_API int32_t nota_pluginhost_scan_path_count(void) {
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadScanPathsOnce();
    return catalog.scanPathCount();
}

extern "C" NOTA_API const char* nota_pluginhost_scan_path(int32_t index) {
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadScanPathsOnce();
    thread_local std::string path;
    path = catalog.scanPathAt(index);
    return path.empty() ? nullptr : path.c_str();
}
