// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginScannerModule — public entry points for libnota_pluginscanner and nota-scanworker.

#pragma once

#include "nota/pluginscanner/Export.h"
#include "nota/pluginscanner/PluginCatalogImpl.h"
#include "nota/pluginscanner/PluginFormatRegistry.h"
#include "nota/pluginscanner/PluginScannerImpl.h"

#include <memory>
#include <string>

namespace nota::pluginscanner {

// Registers built-in format backends (AudioUnit on macOS + VST3) once in PluginFormatRegistry::instance().
NOTA_PLUGINSCANNER_API void registerBuiltinFormatBackends();

// Resolves the per-user support directory (override -> NOTA_DATA_DIR -> platform fallback if applicable).
NOTA_PLUGINSCANNER_API std::string supportDirPath();

// Process-wide catalog and scanner instances sharing PluginFormatRegistry::instance().
NOTA_PLUGINSCANNER_API std::shared_ptr<PluginCatalogImpl> sharedCatalogPtr();
NOTA_PLUGINSCANNER_API PluginCatalogImpl& sharedCatalog();
NOTA_PLUGINSCANNER_API PluginScannerImpl& sharedScanner();

// Isolated factory helpers (for dependency injection or tests).
[[nodiscard]] NOTA_PLUGINSCANNER_API std::shared_ptr<PluginCatalogImpl> createCatalog(
    const PluginFormatRegistry* registry = nullptr, std::string supportDirOverride = {});
[[nodiscard]] NOTA_PLUGINSCANNER_API std::shared_ptr<PluginScannerImpl> createScanner(
    std::shared_ptr<PluginCatalogImpl> catalog, const PluginFormatRegistry* registry = nullptr,
    std::string workerPath = {});

// Entry point for the out-of-process nota-scanworker executable.
NOTA_PLUGINSCANNER_API int runScannerWorker(int argc, char** argv);

} // namespace nota::pluginscanner

namespace nota {
namespace scanner = pluginscanner;
} // namespace nota
