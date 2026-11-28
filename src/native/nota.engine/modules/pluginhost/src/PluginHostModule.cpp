// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/pluginhost/PluginHostModule.h"
#include "nota/pluginhost/PluginHostImpl.h"
#include "nota/dsp/PluginContracts.h"

#include <cstdint>
#include <memory>
#include <utility>

namespace nota::pluginhost {

std::shared_ptr<PluginHostImpl> createPluginHost(std::shared_ptr<dsp::IPluginCatalog> catalog) {
    return std::make_shared<PluginHostImpl>(std::move(catalog));
}

std::shared_ptr<PluginHostImpl> sharedPluginHostPtr() {
    static auto host = std::make_shared<PluginHostImpl>();
    return host;
}

PluginHostImpl& sharedPluginHost() {
    return *sharedPluginHostPtr();
}

bool openTestWindow() {
    return sharedPluginHost().openTestWindow();
}

int32_t pumpMessageLoop() {
    return sharedPluginHost().pumpMessages();
}

} // namespace nota::pluginhost
