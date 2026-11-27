// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginFormatRegistry — ordered thread-safe registry of plugin format backends.

#pragma once

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/Export.h"
#include "nota/pluginscanner/IPluginFormatBackend.h"

#include <memory>
#include <shared_mutex>
#include <string_view>
#include <vector>

namespace nota::pluginscanner {

class NOTA_PLUGINSCANNER_API PluginFormatRegistry {
  public:
    PluginFormatRegistry();
    ~PluginFormatRegistry();

    PluginFormatRegistry(const PluginFormatRegistry&) = delete;
    PluginFormatRegistry& operator=(const PluginFormatRegistry&) = delete;

    static PluginFormatRegistry& instance();

    void registerBackend(std::shared_ptr<IPluginFormatBackend> backend);
    bool unregisterBackend(std::string_view formatId);
    void clear();

    std::shared_ptr<IPluginFormatBackend> find(std::string_view formatId) const;
    std::vector<std::shared_ptr<IPluginFormatBackend>> orderedBackends() const;
    std::vector<dsp::PluginFormatDescriptor> listFormats() const;

  private:
    mutable std::shared_mutex mutex_;
    std::vector<std::shared_ptr<IPluginFormatBackend>> backends_;
};

} // namespace nota::pluginscanner

namespace nota {
namespace scanner = pluginscanner;
} // namespace nota
