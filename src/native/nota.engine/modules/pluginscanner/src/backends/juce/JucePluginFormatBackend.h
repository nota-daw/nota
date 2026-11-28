// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/pluginscanner/PluginFormatRegistry.h"

namespace nota::pluginscanner::detail {

void registerJuceFormatBackends(PluginFormatRegistry& registry);

} // namespace nota::pluginscanner::detail
