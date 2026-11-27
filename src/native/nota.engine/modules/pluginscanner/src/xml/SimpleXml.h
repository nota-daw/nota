// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Minimal pure C++20 XML DOM + JUCE-compatible PluginDescriptor ID helpers.
// Strictly private to nota_pluginscanner so zero third-party XML or JUCE headers are needed.

#pragma once

#include "nota/dsp/PluginContracts.h"

#include <cstdint>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace nota::pluginscanner::detail {

struct XmlNode {
    std::string tag;
    std::vector<std::pair<std::string, std::string>> attributes;
    std::string text;
    std::vector<XmlNode> children;

    std::string_view attr(std::string_view key, std::string_view fallback = {}) const noexcept;
    void setAttr(std::string_view key, std::string value);
    void setAttrInt(std::string_view key, int32_t value);
    void setAttrBool(std::string_view key, bool value);
    const XmlNode* findChild(std::string_view childTag) const noexcept;
    std::string toString(int indent = 0) const;
};

bool parseXmlDocument(std::string_view xml, XmlNode& outRoot);

// Exact JUCE String::toHexString(int32_t) and String::hashCode() compatibility
// for lossless migration of legacy plugins.xml entries without JUCE linked.
std::string toHexString32(int32_t value);
int32_t parseHex32(std::string_view hex, int32_t fallback = 0) noexcept;
int32_t parseInt32(std::string_view dec, int32_t fallback = 0) noexcept;
uint32_t juceUtf8HashCode32(std::string_view utf8) noexcept;
std::string computePluginIdentifier(const dsp::PluginDescriptor& desc);
dsp::PluginFormatKind inferFormatKind(std::string_view formatName) noexcept;

// Converts a `<PLUGIN .../>` XML node into PluginDescriptor (filling serializedXml verbatim if missing).
bool pluginDescriptorFromXmlNode(const XmlNode& node, dsp::PluginDescriptor& out);
XmlNode pluginDescriptorToXmlNode(const dsp::PluginDescriptor& desc);

} // namespace nota::pluginscanner::detail
