// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/pluginscanner/wire/ScanWireCodec.h"

#include "nota/dsp/PluginContracts.h"
#include "xml/SimpleXml.h"

#include <string_view>
#include <string>
#include <cstddef>
#include <utility>
#include <vector>

namespace nota::pluginscanner {

std::string encodeScanResults(const std::vector<dsp::PluginDescriptor>& descriptors) {
    detail::XmlNode root;
    root.tag = "plugins";
    root.children.reserve(descriptors.size());
    for (const auto& desc : descriptors) {
        detail::XmlNode node = detail::pluginDescriptorToXmlNode(desc);
        if (!desc.serializedXml.empty()) {
            detail::XmlNode backendXml;
            backendXml.tag = "BACKEND_XML";
            backendXml.text = desc.serializedXml;
            node.children.push_back(std::move(backendXml));
        }
        root.children.push_back(std::move(node));
    }

    std::string out;
    out.reserve(kScanWireBeginSentinel.size() + kScanWireEndSentinel.size() + descriptors.size() * 256 + 64);
    out += kScanWireBeginSentinel;
    out.push_back('\n');
    out += root.toString(0);
    out.push_back('\n');
    out += kScanWireEndSentinel;
    return out;
}

bool decodeScanResults(std::string_view rawWorkerOutput, std::vector<dsp::PluginDescriptor>& outDescriptors) {
    outDescriptors.clear();
    const size_t begin = rawWorkerOutput.find(kScanWireBeginSentinel);
    if (begin == std::string_view::npos) return false;
    const size_t end = rawWorkerOutput.rfind(kScanWireEndSentinel);
    if (end == std::string_view::npos || end <= begin) return false;

    const size_t bodyStart = begin + kScanWireBeginSentinel.size();
    const std::string_view payload = rawWorkerOutput.substr(bodyStart, end - bodyStart);

    detail::XmlNode root;
    if (!detail::parseXmlDocument(payload, root)) return false;

    for (const auto& child : root.children) {
        dsp::PluginDescriptor desc;
        if (detail::pluginDescriptorFromXmlNode(child, desc)) { outDescriptors.push_back(std::move(desc)); }
    }
    return true;
}

} // namespace nota::pluginscanner
