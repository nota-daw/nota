// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// OutOfProcessScanner — format-neutral child-process plugin probe coordinator.

#pragma once

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/Export.h"

#include <string>
#include <string_view>
#include <vector>

namespace nota::pluginscanner {

class NOTA_PLUGINSCANNER_API OutOfProcessScanner {
  public:
    static constexpr int kDefaultTimeoutMs = 30000;

    enum class Outcome {
        Ok,          // worker answered (possibly with zero descriptors)
        Timeout,     // no answer within the timeout — worker killed
        NoAnswer,    // worker exited (or crashed) without the sentinel block
        StartFailed, // worker could not be started
    };

    explicit OutOfProcessScanner(std::string workerPath, int timeoutMs = kDefaultTimeoutMs) noexcept;

    std::vector<dsp::PluginDescriptor> scanDescriptions(std::string_view formatId, std::string_view candidateId,
        Outcome* outcome = nullptr, std::string* detail = nullptr) const noexcept;

    bool workerAvailable() const noexcept;
    const std::string& workerPath() const noexcept { return workerPath_; }
    int timeoutMs() const noexcept { return timeoutMs_; }

  private:
    std::string workerPath_;
    int timeoutMs_;
};

} // namespace nota::pluginscanner

namespace nota {
namespace scanner = pluginscanner;
} // namespace nota
