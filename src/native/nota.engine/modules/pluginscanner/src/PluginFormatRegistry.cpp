// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/pluginscanner/PluginFormatRegistry.h"
#include "nota/pluginscanner/IPluginFormatBackend.h"
#include "nota/dsp/PluginContracts.h"

#include <memory>
#include <mutex>
#include <string_view>
#include <string>
#include <shared_mutex>
#include <utility>
#include <vector>

namespace nota::pluginscanner {

IPluginFormatBackend::~IPluginFormatBackend() = default;

PluginFormatRegistry::PluginFormatRegistry() = default;
PluginFormatRegistry::~PluginFormatRegistry() = default;

PluginFormatRegistry& PluginFormatRegistry::instance() {
    static PluginFormatRegistry registry;
    return registry;
}

void PluginFormatRegistry::registerBackend(std::shared_ptr<IPluginFormatBackend> backend) {
    if (!backend) return;
    const std::string id = backend->descriptor().id;
    if (id.empty()) return;

    std::unique_lock<std::shared_mutex> lock(mutex_);
    for (auto& existing : backends_) {
        if (existing->descriptor().id == id) {
            existing = std::move(backend);
            return;
        }
    }
    backends_.push_back(std::move(backend));
}

bool PluginFormatRegistry::unregisterBackend(std::string_view formatId) {
    if (formatId.empty()) return false;
    std::unique_lock<std::shared_mutex> lock(mutex_);
    for (auto it = backends_.begin(); it != backends_.end(); ++it) {
        if ((*it)->descriptor().id == formatId) {
            backends_.erase(it);
            return true;
        }
    }
    return false;
}

void PluginFormatRegistry::clear() {
    std::unique_lock<std::shared_mutex> lock(mutex_);
    backends_.clear();
}

std::shared_ptr<IPluginFormatBackend> PluginFormatRegistry::find(std::string_view formatId) const {
    if (formatId.empty()) return {};
    std::shared_lock<std::shared_mutex> lock(mutex_);
    for (const auto& backend : backends_) {
        if (backend->descriptor().id == formatId) return backend;
    }
    return {};
}

std::vector<std::shared_ptr<IPluginFormatBackend>> PluginFormatRegistry::orderedBackends() const {
    std::shared_lock<std::shared_mutex> lock(mutex_);
    return backends_;
}

std::vector<dsp::PluginFormatDescriptor> PluginFormatRegistry::listFormats() const {
    std::shared_lock<std::shared_mutex> lock(mutex_);
    std::vector<dsp::PluginFormatDescriptor> out;
    out.reserve(backends_.size());
    for (const auto& backend : backends_) { out.push_back(backend->descriptor()); }
    return out;
}

} // namespace nota::pluginscanner
