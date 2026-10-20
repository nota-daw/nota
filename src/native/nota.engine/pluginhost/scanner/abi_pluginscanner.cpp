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

#include <string>

namespace {

// AU first (macOS priority), then VST3 — see ARCHITECTURE.md § Plugin hosting.
const char* const kFormatOrder[] = {"AudioUnit", "VST3"};

juce::AudioPluginFormat* findFormat(const juce::String& name) {
    for (auto* f : nota::scanner::sharedFormatManager().getFormats())
        if (f->getName() == name) return f;
    return nullptr;
}

} // namespace

extern "C" NOTA_API int32_t nota_pluginhost_scan(const char* worker_path) {
    // No JUCE GUI init here on purpose: scanning (format enumeration + child
    // processes + XML) needs no MessageManager, and this may run off the main
    // thread. The MessageManager stays owned by the UI thread (editor windows).
    if (worker_path == nullptr) return -1;
    auto& catalog = nota::scanner::PluginCatalog::instance();
    catalog.loadOnce();
    catalog.loadScanPathsOnce();

    nota::scanner::OutOfProcessScanner worker(worker_path);
    if (!worker.workerAvailable()) return -2;

    // Forget VST3 bundles that are gone from disk (uninstalled, e.g. via Get Plug-ins),
    // so the browser stops listing them. AU lives in the system registry — left alone.
    catalog.removeMissingVst3OnDisk();

    for (const char* formatName : kFormatOrder) {
        auto* format = findFormat(formatName);
        if (format == nullptr) continue;

        // Default OS locations plus any user-added dirs (file-based formats).
        juce::FileSearchPath search = format->getDefaultLocationsToSearch();
        for (const auto& p : catalog.scanPaths())
            search.addIfNotAlreadyThere(juce::File(juce::CharPointer_UTF8(p.c_str())));

        const auto ids = format->searchPathsForPlugins(search, /*recursive*/ true, /*allowAsync*/ false);

        std::vector<std::string> discovered;
        for (const auto& id : ids) discovered.push_back(id.toStdString());
        for (const auto& id : catalog.unknownIds(discovered)) {
            for (auto& desc : worker.scanDescriptions(formatName, id)) catalog.addType(*desc);
        }
    }

    catalog.save();
    return catalog.count();
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
