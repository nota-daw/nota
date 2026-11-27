// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginCatalogImpl — thread-safe, format-agnostic plugin catalog implementing IPluginCatalog.
// Preserves lossless legacy plugins.xml compatibility alongside scanpaths & failed-candidate tracking.

#pragma once

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/Export.h"
#include "nota/pluginscanner/PluginFormatRegistry.h"

#include <cstdint>
#include <map>
#include <shared_mutex>
#include <string>
#include <vector>

namespace nota::pluginscanner {

class NOTA_PLUGINSCANNER_API PluginCatalogImpl final : public dsp::IPluginCatalog {
  public:
    explicit PluginCatalogImpl(const PluginFormatRegistry* registry = nullptr, std::string supportDirOverride = {},
        bool autoLoadOnRead = false);
    ~PluginCatalogImpl() override;

    PluginCatalogImpl(const PluginCatalogImpl&) = delete;
    PluginCatalogImpl& operator=(const PluginCatalogImpl&) = delete;

    // dsp::IPluginCatalog
    int32_t count() const override;
    int32_t indexOfId(const char* identifier) const override;
    std::string descLine(int32_t index) const override;
    std::string idAt(int32_t index) const override;
    bool descriptorAt(int32_t index, dsp::PluginDescriptor& out) const override;
    bool descriptorOfId(const char* identifier, dsp::PluginDescriptor& out) const override;

    std::vector<std::string> scanPaths() const override;
    void addScanPath(const std::string& dir) override;
    bool removeScanPath(int32_t index) override;
    int32_t scanPathCount() const override;
    std::string scanPathAt(int32_t index) const override;
    bool isScanPathRemoved(const std::string& dir) const override;
    int32_t restoreDefaultScanPaths(const char* format) override;

    int32_t failedCount() const override;
    void clearFailed() override;

    // Coordinator operations
    std::vector<std::string> defaultScanPathsFor(const char* format) const;
    void addDescriptor(const dsp::PluginDescriptor& desc);
    void removeMissingByBackends();
    std::vector<std::string> unknownCandidates(
        const std::string& formatId, const std::vector<std::string>& discovered) const;

    void markFailed(const std::string& formatId, const std::string& candidateId);
    void forgetFailed(const std::string& candidateId);

    // Persistence (message/background threads only, never audio RT)
    void loadOnce();
    void save() const;
    void loadScanPathsOnce();
    void saveScanPaths() const;
    void saveRemoved() const;
    void loadFailedOnce();
    void saveFailed() const;

    std::string effectiveSupportDir() const;
    std::string scanLogPath() const;

  private:
    int32_t indexOfIdLocked(const char* identifier) const;
    void ensureCatalogLoadedIfAuto() const;
    void ensureScanPathsLoadedIfAuto() const;
    void ensureFailedLoadedIfAuto() const;

    const PluginFormatRegistry* registry_ = nullptr;
    std::string supportDirOverride_;
    bool autoLoadOnRead_ = false;

    mutable std::shared_mutex mutex_;
    std::vector<dsp::PluginDescriptor> entries_;
    std::vector<std::string> scanPaths_;
    std::vector<std::string> removedScanPaths_;
    std::map<std::string, int64_t> failed_;
    bool catalogLoaded_ = false;
    bool scanPathsLoaded_ = false;
    bool removedLoaded_ = false;
    bool failedLoaded_ = false;
};

} // namespace nota::pluginscanner

namespace nota {
namespace scanner = pluginscanner;
} // namespace nota
