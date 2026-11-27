// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/pluginscanner/PluginScannerModule.h"

#include "backends/juce/JucePluginFormatBackend.h"
#include "nota/dsp/PluginContracts.h"
#include "nota/core/ConfigFile.h"
#include "nota/pluginscanner/wire/ScanWireCodec.h"
#include "nota/pluginscanner/PluginScannerImpl.h"
#include "nota/pluginscanner/PluginFormatRegistry.h"
#include "nota/pluginscanner/PluginCatalogImpl.h"

#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <memory>
#include <mutex>
#include <string>
#include <system_error>
#include <vector>
#include <utility>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#endif

namespace nota::pluginscanner {
namespace {

[[noreturn]] void exitWorkerNow(int code) {
    std::fflush(stdout);
    std::fflush(stderr);
#if defined(_WIN32)
    ::TerminateProcess(::GetCurrentProcess(), static_cast<UINT>(code));
#endif
    std::_Exit(code);
}

} // namespace

void registerBuiltinFormatBackends() {
    static std::once_flag once;
    std::call_once(once, [] { detail::registerJuceFormatBackends(PluginFormatRegistry::instance()); });
}

std::string supportDirPath() {
    if (const std::string over = core::cfgfile::dataDirOverride(); !over.empty()) {
        std::error_code ec;
        std::filesystem::create_directories(over, ec);
        return over;
    }
    if (const char* env = std::getenv("NOTA_DATA_DIR"); env != nullptr && *env != '\0') {
        std::error_code ec;
        std::filesystem::create_directories(env, ec);
        return std::string(env);
    }
#if defined(__APPLE__)
    if (const char* home = std::getenv("HOME"); home != nullptr && *home != '\0') {
        const auto dir = std::filesystem::path(home) / "Library" / "Application Support" / "Nota";
        std::error_code ec;
        std::filesystem::create_directories(dir, ec);
        return dir.string();
    }
#endif
    return core::cfgfile::dataDir();
}

std::shared_ptr<PluginCatalogImpl> sharedCatalogPtr() {
    registerBuiltinFormatBackends();
    static auto catalog = std::make_shared<PluginCatalogImpl>(&PluginFormatRegistry::instance(), std::string{}, true);
    return catalog;
}

PluginCatalogImpl& sharedCatalog() {
    return *sharedCatalogPtr();
}

PluginScannerImpl& sharedScanner() {
    registerBuiltinFormatBackends();
    static auto scanner = std::make_unique<PluginScannerImpl>(sharedCatalogPtr(), &PluginFormatRegistry::instance());
    return *scanner;
}

std::shared_ptr<PluginCatalogImpl> createCatalog(const PluginFormatRegistry* registry, std::string supportDirOverride) {
    return std::make_shared<PluginCatalogImpl>(registry, std::move(supportDirOverride), false);
}

std::shared_ptr<PluginScannerImpl> createScanner(
    std::shared_ptr<PluginCatalogImpl> catalog, const PluginFormatRegistry* registry, std::string workerPath) {
    return std::make_shared<PluginScannerImpl>(std::move(catalog), registry, std::move(workerPath));
}

int runScannerWorker(int argc, char** argv) {
    if (argc < 3) {
        std::cerr << "usage: nota-scanworker <formatName> <fileOrIdentifier>\n";
        return 2;
    }

    registerBuiltinFormatBackends();
    const std::string formatName = argv[1];
    const std::string candidateId = argv[2];
    const auto backend = PluginFormatRegistry::instance().find(formatName);
    if (!backend) {
        std::cerr << "unknown format: " << formatName << '\n';
        return 3;
    }

    std::vector<dsp::PluginDescriptor> found;
    (void)backend->probeInWorker(candidateId, found);
    std::cout << encodeScanResults(found) << std::endl;
    exitWorkerNow(0);
}

} // namespace nota::pluginscanner
