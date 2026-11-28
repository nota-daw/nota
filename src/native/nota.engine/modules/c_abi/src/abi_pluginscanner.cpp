// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// C ABI adapter for plugin discovery and catalog (AR-7).
// Delegates strictly to nota::pluginscanner (JUCE-free interface).

#include "nota/nota_engine.h"

#include "nota/pluginhost/PluginHostModule.h"
#include "nota/pluginscanner/PluginScannerModule.h"

#include <cstddef>
#include <cstdint>
#include <string>

extern "C" NOTA_API int32_t nota_pluginhost_scan(const char* worker_path) {
    return nota_pluginhost_scan_ex(worker_path, nullptr, 0);
}

extern "C" NOTA_API int32_t nota_pluginhost_scan_ex(const char* worker_path, const char* only_dir, int32_t flags) {
    if (worker_path == nullptr) return -1;
    try {
        nota::pluginhost::sharedPluginHost().setCatalog(nota::pluginscanner::sharedCatalogPtr());
        return nota::pluginscanner::sharedScanner().scanEx(worker_path, only_dir, flags);
    } catch (...) {
        return -3; // AR-7: nothing unwinds across the C ABI
    }
}

extern "C" NOTA_API int32_t nota_pluginhost_scan_progress(int32_t* done, int32_t* total) {
    const auto prog = nota::pluginscanner::sharedScanner().progress();
    if (done != nullptr) *done = prog.done;
    if (total != nullptr) *total = prog.total;
    return prog.scanning ? 1 : 0;
}

extern "C" NOTA_API const char* nota_pluginhost_scan_current(void) {
    thread_local std::string name;
    name = nota::pluginscanner::sharedScanner().currentPlugin();
    return name.c_str();
}

extern "C" NOTA_API int32_t nota_pluginhost_scan_failed_count(void) {
    auto& catalog = nota::pluginscanner::sharedCatalog();
    catalog.loadFailedOnce();
    return catalog.failedCount();
}

extern "C" NOTA_API int32_t nota_pluginhost_plugin_count(void) {
    auto& catalog = nota::pluginscanner::sharedCatalog();
    catalog.loadOnce();
    return catalog.count();
}

extern "C" NOTA_API const char* nota_pluginhost_plugin_desc(int32_t index) {
    auto& catalog = nota::pluginscanner::sharedCatalog();
    catalog.loadOnce();
    thread_local std::string line;
    line = catalog.descLine(index);
    return line.empty() ? nullptr : line.c_str();
}

extern "C" NOTA_API const char* nota_pluginhost_plugin_id(int32_t index) {
    auto& catalog = nota::pluginscanner::sharedCatalog();
    catalog.loadOnce();
    thread_local std::string id;
    id = catalog.idAt(index);
    return id.empty() ? nullptr : id.c_str();
}

extern "C" NOTA_API int32_t nota_pluginhost_index_of_id(const char* identifier) {
    auto& catalog = nota::pluginscanner::sharedCatalog();
    catalog.loadOnce();
    return catalog.indexOfId(identifier);
}

extern "C" NOTA_API NotaResult nota_pluginhost_add_scan_path(const char* dir) {
    if (dir == nullptr) return NOTA_ERR_INVALID_ARG;
    auto& catalog = nota::pluginscanner::sharedCatalog();
    catalog.loadScanPathsOnce();
    catalog.addScanPath(dir);
    return NOTA_OK;
}

extern "C" NOTA_API NotaResult nota_pluginhost_remove_scan_path(int32_t index) {
    auto& catalog = nota::pluginscanner::sharedCatalog();
    catalog.loadScanPathsOnce();
    return catalog.removeScanPath(index) ? NOTA_OK : NOTA_ERR_INVALID_ARG;
}

extern "C" NOTA_API int32_t nota_pluginhost_scan_path_count(void) {
    auto& catalog = nota::pluginscanner::sharedCatalog();
    catalog.loadScanPathsOnce();
    return catalog.scanPathCount();
}

extern "C" NOTA_API const char* nota_pluginhost_scan_path(int32_t index) {
    auto& catalog = nota::pluginscanner::sharedCatalog();
    catalog.loadScanPathsOnce();
    thread_local std::string path;
    path = catalog.scanPathAt(index);
    return path.empty() ? nullptr : path.c_str();
}

extern "C" NOTA_API int32_t nota_pluginhost_default_scan_path_count(const char* format) {
    try {
        return static_cast<int32_t>(nota::pluginscanner::sharedCatalog().defaultScanPathsFor(format).size());
    } catch (...) {
        return 0; // AR-7: nothing unwinds across the C ABI
    }
}

extern "C" NOTA_API const char* nota_pluginhost_default_scan_path(const char* format, int32_t index) {
    try {
        const auto all = nota::pluginscanner::sharedCatalog().defaultScanPathsFor(format);
        if (index < 0 || index >= static_cast<int32_t>(all.size())) return nullptr;
        thread_local std::string path;
        path = all[static_cast<size_t>(index)];
        return path.c_str();
    } catch (...) {
        return nullptr; // AR-7: nothing unwinds across the C ABI
    }
}

extern "C" NOTA_API int32_t nota_pluginhost_restore_default_scan_paths(const char* format) {
    if (format == nullptr) return 0;
    try {
        auto& catalog = nota::pluginscanner::sharedCatalog();
        catalog.loadScanPathsOnce();
        return catalog.restoreDefaultScanPaths(format);
    } catch (...) {
        return 0; // AR-7: nothing unwinds across the C ABI
    }
}

extern "C" NOTA_API int32_t nota_pluginhost_is_scan_path_removed(const char* dir) {
    if (dir == nullptr) return 0;
    try {
        auto& catalog = nota::pluginscanner::sharedCatalog();
        catalog.loadScanPathsOnce();
        return catalog.isScanPathRemoved(dir) ? 1 : 0;
    } catch (...) {
        return 0; // AR-7: nothing unwinds across the C ABI
    }
}
