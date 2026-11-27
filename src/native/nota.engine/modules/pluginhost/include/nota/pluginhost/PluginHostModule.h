// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginHostModule — JUCE-free public entry points for libnota_pluginhost.

#pragma once

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginhost/Export.h"
#include "nota/pluginhost/PluginHostImpl.h"

#include <cstdint>
#include <memory>

namespace nota::pluginhost {

[[nodiscard]] NOTA_PLUGINHOST_API std::shared_ptr<PluginHostImpl> createPluginHost(
    std::shared_ptr<dsp::IPluginCatalog> catalog = nullptr);

NOTA_PLUGINHOST_API std::shared_ptr<PluginHostImpl> sharedPluginHostPtr();
NOTA_PLUGINHOST_API PluginHostImpl& sharedPluginHost();

NOTA_PLUGINHOST_API bool openTestWindow();
NOTA_PLUGINHOST_API int32_t pumpMessageLoop();

} // namespace nota::pluginhost
