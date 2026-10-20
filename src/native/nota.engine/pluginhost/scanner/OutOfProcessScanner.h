// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// OutOfProcessScanner — child-process scan coordinator.
// Spawns nota-scanworker per undiscovered plugin id, enforces a per-plugin
// timeout, slices the <<<NOTA_PLUGINS_BEGIN>>>..<<<NOTA_PLUGINS_END>>> sentinel
// (noisy plugins print to stdout), and parses PluginDescriptions. A crashing or
// hanging plugin never takes down the DAW: kill, skip, continue.
// No Engine/Track dependency.

#pragma once

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "bridge/PluginCatalogBridge.h"

namespace juce {
class PluginDescription;
} // namespace juce

namespace nota {
namespace scanner {

struct ScanResult {
    std::vector<catalog::PluginEntry> found;
    int32_t scanned = 0;
    int32_t failed = 0;
};

class OutOfProcessScanner {
  public:
    static constexpr int kDefaultTimeoutMs = 30000;

    explicit OutOfProcessScanner(std::string workerPath, int timeoutMs = kDefaultTimeoutMs) noexcept;

    // Scan one plugin id (format + fileOrIdentifier) via the worker.
    // Never throws; failures increment result.failed.
    void scanOne(const std::string& formatName, const std::string& fileOrId,
        std::vector<catalog::PluginEntry>& out) const noexcept;

    // Lossless variant: full JUCE descriptions (uid, path hash) for catalog
    // storage. Never throws; returns empty on any failure.
    std::vector<std::unique_ptr<juce::PluginDescription>> scanDescriptions(
        const std::string& formatName, const std::string& fileOrId) const noexcept;

    // Returns false when the worker binary is missing.
    bool workerAvailable() const noexcept;

    const std::string& workerPath() const noexcept { return workerPath_; }
    int timeoutMs() const noexcept { return timeoutMs_; }

  private:
    std::string workerPath_;
    int timeoutMs_;
};

} // namespace scanner
} // namespace nota
