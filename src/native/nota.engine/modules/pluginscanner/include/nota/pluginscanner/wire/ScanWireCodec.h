// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ScanWireCodec — format-neutral worker stdout framing for plugin descriptors.

#pragma once

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/Export.h"

#include <string>
#include <string_view>
#include <vector>

namespace nota::pluginscanner {

inline constexpr std::string_view kScanWireBeginSentinel = "<<<NOTA_PLUGINS_BEGIN>>>";
inline constexpr std::string_view kScanWireEndSentinel = "<<<NOTA_PLUGINS_END>>>";

// Frames `descriptors` between stdout sentinels for nota-scanworker -> host transfer.
NOTA_PLUGINSCANNER_API std::string encodeScanResults(const std::vector<dsp::PluginDescriptor>& descriptors);

// Slices sentinel markers from raw worker output and decodes all plugin descriptors.
// Accepts both structured Nota wire payloads and legacy JUCE `<plugins><PLUGIN .../></plugins>` output.
NOTA_PLUGINSCANNER_API bool decodeScanResults(
    std::string_view rawWorkerOutput, std::vector<dsp::PluginDescriptor>& outDescriptors);

} // namespace nota::pluginscanner

namespace nota {
namespace scanner = pluginscanner;
} // namespace nota
