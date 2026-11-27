// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginScannerImpl — format-agnostic scan coordinator implementing IPluginScanner.

#pragma once

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/Export.h"
#include "nota/pluginscanner/PluginCatalogImpl.h"
#include "nota/pluginscanner/PluginFormatRegistry.h"

#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <string_view>

namespace nota::pluginscanner {

inline constexpr int32_t kScanRetryFailedFlag = 1;

class NOTA_PLUGINSCANNER_API PluginScannerImpl final : public dsp::IPluginScanner {
  public:
    explicit PluginScannerImpl(std::shared_ptr<PluginCatalogImpl> catalog,
        const PluginFormatRegistry* registry = nullptr, std::string workerPath = {}, int probeTimeoutMs = 30000,
        int finalRetryTimeoutMs = 120000);
    ~PluginScannerImpl() override;

    PluginScannerImpl(const PluginScannerImpl&) = delete;
    PluginScannerImpl& operator=(const PluginScannerImpl&) = delete;

    dsp::IPluginCatalog& catalog() override;
    const dsp::IPluginCatalog& catalog() const override;

    void setWorkerPath(std::string workerPath);

    int32_t scan(bool rescanFailed) override;
    int32_t scanDirectory(const std::string& dir) override;
    int32_t scanEx(std::string_view workerPath, const char* onlyDir, int32_t flags);

    dsp::PluginScanProgress progress() const override;
    std::string currentPlugin() const;
    bool isScanning() const override;
    void stopScan() override;

  private:
    std::shared_ptr<PluginCatalogImpl> catalog_;
    const PluginFormatRegistry* registry_ = nullptr;
    std::string workerPath_;
    int probeTimeoutMs_ = 30000;
    int finalRetryTimeoutMs_ = 120000;

    mutable std::mutex scanMutex_;
    std::atomic<bool> scanning_{false};
    std::atomic<bool> stopRequested_{false};
    std::atomic<int32_t> done_{0};
    std::atomic<int32_t> total_{0};

    mutable std::mutex currentMutex_;
    std::string current_;
};

} // namespace nota::pluginscanner

namespace nota {
namespace scanner = pluginscanner;
} // namespace nota
