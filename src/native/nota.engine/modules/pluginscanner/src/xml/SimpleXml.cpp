// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "xml/SimpleXml.h"
#include "nota/dsp/PluginContracts.h"

#include <cctype>
#include <charconv>
#include <cstddef>
#include <cstdint>
#include <string_view>
#include <string>
#include <system_error>
#include <utility>

namespace nota::pluginscanner::detail {
namespace {

bool isWhitespace(char ch) noexcept {
    return ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n';
}

bool isNameChar(char ch) noexcept {
    const unsigned char u = static_cast<unsigned char>(ch);
    return std::isalnum(u) != 0 || ch == '_' || ch == '-' || ch == ':' || ch == '.';
}

void appendEscaped(std::string& out, std::string_view text, bool forAttribute) {
    for (const char ch : text) {
        switch (ch) {
        case '&': out += "&amp;"; break;
        case '<': out += "&lt;"; break;
        case '>': out += "&gt;"; break;
        case '"': out += forAttribute ? "&quot;" : "\""; break;
        case '\'': out += forAttribute ? "&apos;" : "'"; break;
        default: out.push_back(ch); break;
        }
    }
}

void appendCodepointUtf8(std::string& out, uint32_t cp) {
    if (cp <= 0x7Fu) {
        out.push_back(static_cast<char>(cp));
    } else if (cp <= 0x7FFu) {
        out.push_back(static_cast<char>(0xC0u | (cp >> 6)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    } else if (cp <= 0xFFFFu) {
        out.push_back(static_cast<char>(0xE0u | (cp >> 12)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    } else if (cp <= 0x10FFFFu) {
        out.push_back(static_cast<char>(0xF0u | (cp >> 18)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 12) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu)));
        out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
    }
}

std::string unescapeXml(std::string_view input) {
    std::string out;
    out.reserve(input.size());
    size_t i = 0;
    while (i < input.size()) {
        if (input[i] != '&') {
            out.push_back(input[i++]);
            continue;
        }
        const size_t semi = input.find(';', i + 1);
        if (semi == std::string_view::npos) {
            out.push_back(input[i++]);
            continue;
        }
        const std::string_view ent = input.substr(i + 1, semi - (i + 1));
        if (ent == "amp") {
            out.push_back('&');
        } else if (ent == "lt") {
            out.push_back('<');
        } else if (ent == "gt") {
            out.push_back('>');
        } else if (ent == "quot") {
            out.push_back('"');
        } else if (ent == "apos") {
            out.push_back('\'');
        } else if (ent.size() >= 2 && ent[0] == '#') {
            uint32_t cp = 0;
            const bool isHex = (ent[1] == 'x' || ent[1] == 'X');
            const std::string_view num = isHex ? ent.substr(2) : ent.substr(1);
            const auto res = std::from_chars(num.data(), num.data() + num.size(), cp, isHex ? 16 : 10);
            if (res.ec == std::errc{} && cp > 0) {
                appendCodepointUtf8(out, cp);
            } else {
                out.append(input.substr(i, semi - i + 1));
            }
        } else {
            out.append(input.substr(i, semi - i + 1));
        }
        i = semi + 1;
    }
    return out;
}

class XmlParser {
  public:
    explicit XmlParser(std::string_view src) noexcept : src_(src) {}

    bool parseRoot(XmlNode& outRoot) {
        skipMisc();
        if (pos_ >= src_.size() || src_[pos_] != '<') return false;
        return parseElement(outRoot);
    }

  private:
    void skipWs() noexcept {
        while (pos_ < src_.size() && isWhitespace(src_[pos_])) ++pos_;
    }

    bool startsWith(std::string_view lit) const noexcept {
        return pos_ + lit.size() <= src_.size() && src_.substr(pos_, lit.size()) == lit;
    }

    void skipMisc() noexcept {
        for (;;) {
            skipWs();
            if (startsWith("<?")) {
                const size_t end = src_.find("?>", pos_ + 2);
                if (end == std::string_view::npos) {
                    pos_ = src_.size();
                    return;
                }
                pos_ = end + 2;
            } else if (startsWith("<!--")) {
                const size_t end = src_.find("-->", pos_ + 4);
                if (end == std::string_view::npos) {
                    pos_ = src_.size();
                    return;
                }
                pos_ = end + 3;
            } else if (startsWith("<!")) {
                const size_t end = src_.find('>', pos_ + 2);
                if (end == std::string_view::npos) {
                    pos_ = src_.size();
                    return;
                }
                pos_ = end + 1;
            } else {
                break;
            }
        }
    }

    bool parseElement(XmlNode& out) {
        if (!startsWith("<")) return false;
        ++pos_;
        const size_t nameStart = pos_;
        while (pos_ < src_.size() && isNameChar(src_[pos_])) ++pos_;
        if (pos_ == nameStart) return false;
        out.tag.assign(src_.substr(nameStart, pos_ - nameStart));

        for (;;) {
            skipWs();
            if (pos_ >= src_.size()) return false;
            if (startsWith("/>")) {
                pos_ += 2;
                return true;
            }
            if (src_[pos_] == '>') {
                ++pos_;
                break;
            }
            const size_t keyStart = pos_;
            while (pos_ < src_.size() && isNameChar(src_[pos_])) ++pos_;
            if (pos_ == keyStart) return false;
            const std::string_view key = src_.substr(keyStart, pos_ - keyStart);
            skipWs();
            if (pos_ >= src_.size() || src_[pos_] != '=') return false;
            ++pos_;
            skipWs();
            if (pos_ >= src_.size() || (src_[pos_] != '"' && src_[pos_] != '\'')) return false;
            const char quote = src_[pos_++];
            const size_t valStart = pos_;
            while (pos_ < src_.size() && src_[pos_] != quote) ++pos_;
            if (pos_ >= src_.size()) return false;
            const std::string_view rawVal = src_.substr(valStart, pos_ - valStart);
            ++pos_;
            out.attributes.emplace_back(std::string(key), unescapeXml(rawVal));
        }

        for (;;) {
            if (pos_ >= src_.size()) return false;
            if (startsWith("</")) {
                pos_ += 2;
                const size_t closeStart = pos_;
                while (pos_ < src_.size() && isNameChar(src_[pos_])) ++pos_;
                const std::string_view closeTag = src_.substr(closeStart, pos_ - closeStart);
                skipWs();
                if (pos_ >= src_.size() || src_[pos_] != '>' || closeTag != out.tag) return false;
                ++pos_;
                return true;
            }
            if (startsWith("<!--") || startsWith("<?")) {
                skipMisc();
                continue;
            }
            if (startsWith("<![CDATA[")) {
                pos_ += 9;
                const size_t end = src_.find("]]>", pos_);
                if (end == std::string_view::npos) return false;
                out.text.append(src_.substr(pos_, end - pos_));
                pos_ = end + 3;
                continue;
            }
            if (src_[pos_] == '<') {
                XmlNode child;
                if (!parseElement(child)) return false;
                out.children.push_back(std::move(child));
                continue;
            }
            const size_t textStart = pos_;
            while (pos_ < src_.size() && src_[pos_] != '<') ++pos_;
            const std::string chunk = unescapeXml(src_.substr(textStart, pos_ - textStart));
            if (out.children.empty()) out.text += chunk;
        }
    }

    std::string_view src_;
    size_t pos_ = 0;
};

void writeXmlNode(std::string& out, const XmlNode& node, int indent) {
    out.append(static_cast<size_t>(indent), ' ');
    out.push_back('<');
    out += node.tag;
    for (const auto& [k, v] : node.attributes) {
        out.push_back(' ');
        out += k;
        out += "=\"";
        appendEscaped(out, v, true);
        out.push_back('"');
    }
    if (node.children.empty() && node.text.empty()) {
        out += "/>";
        return;
    }
    out.push_back('>');
    if (!node.text.empty() && node.children.empty()) {
        appendEscaped(out, node.text, false);
        out += "</";
        out += node.tag;
        out.push_back('>');
        return;
    }
    out.push_back('\n');
    for (const auto& child : node.children) {
        writeXmlNode(out, child, indent + 2);
        out.push_back('\n');
    }
    out.append(static_cast<size_t>(indent), ' ');
    out += "</";
    out += node.tag;
    out.push_back('>');
}

std::string_view formatKindToString(dsp::PluginFormatKind kind) noexcept {
    switch (kind) {
    case dsp::PluginFormatKind::Vst3: return "vst3";
    case dsp::PluginFormatKind::AudioUnit: return "au";
    case dsp::PluginFormatKind::Clap: return "clap";
    case dsp::PluginFormatKind::Lv2: return "lv2";
    case dsp::PluginFormatKind::Vst2: return "vst2";
    case dsp::PluginFormatKind::Unknown: return "unknown";
    }
    return "unknown";
}

dsp::PluginFormatKind parseFormatKind(std::string_view text, std::string_view formatName) noexcept {
    if (text == "vst3") return dsp::PluginFormatKind::Vst3;
    if (text == "au") return dsp::PluginFormatKind::AudioUnit;
    if (text == "clap") return dsp::PluginFormatKind::Clap;
    if (text == "lv2") return dsp::PluginFormatKind::Lv2;
    if (text == "vst2") return dsp::PluginFormatKind::Vst2;
    return inferFormatKind(formatName);
}

} // namespace

std::string_view XmlNode::attr(std::string_view key, std::string_view fallback) const noexcept {
    for (const auto& [k, v] : attributes) {
        if (k == key) return v;
    }
    return fallback;
}

void XmlNode::setAttr(std::string_view key, std::string value) {
    for (auto& [k, v] : attributes) {
        if (k == key) {
            v = std::move(value);
            return;
        }
    }
    attributes.emplace_back(std::string(key), std::move(value));
}

void XmlNode::setAttrInt(std::string_view key, int32_t value) {
    setAttr(key, std::to_string(value));
}

void XmlNode::setAttrBool(std::string_view key, bool value) {
    setAttr(key, value ? "1" : "0");
}

const XmlNode* XmlNode::findChild(std::string_view childTag) const noexcept {
    for (const auto& child : children) {
        if (child.tag == childTag) return &child;
    }
    return nullptr;
}

std::string XmlNode::toString(int indent) const {
    std::string out;
    writeXmlNode(out, *this, indent);
    return out;
}

bool parseXmlDocument(std::string_view xml, XmlNode& outRoot) {
    outRoot = XmlNode{};
    XmlParser parser(xml);
    return parser.parseRoot(outRoot);
}

std::string toHexString32(int32_t value) {
    const uint32_t u = static_cast<uint32_t>(value);
    if (u == 0u) return "0";
    static constexpr char kDigits[] = "0123456789abcdef";
    char buf[8];
    int idx = 8;
    uint32_t v = u;
    while (v > 0u) {
        buf[--idx] = kDigits[v & 0xFu];
        v >>= 4;
    }
    return std::string(buf + idx, static_cast<size_t>(8 - idx));
}

int32_t parseHex32(std::string_view hex, int32_t fallback) noexcept {
    while (!hex.empty() && isWhitespace(hex.front())) hex.remove_prefix(1);
    while (!hex.empty() && isWhitespace(hex.back())) hex.remove_suffix(1);
    if (hex.empty()) return fallback;
    uint32_t val = 0;
    const auto res = std::from_chars(hex.data(), hex.data() + hex.size(), val, 16);
    if (res.ec != std::errc{} || res.ptr != hex.data() + hex.size()) return fallback;
    return static_cast<int32_t>(val);
}

int32_t parseInt32(std::string_view dec, int32_t fallback) noexcept {
    while (!dec.empty() && isWhitespace(dec.front())) dec.remove_prefix(1);
    while (!dec.empty() && isWhitespace(dec.back())) dec.remove_suffix(1);
    if (dec.empty()) return fallback;
    int32_t val = fallback;
    const auto res = std::from_chars(dec.data(), dec.data() + dec.size(), val, 10);
    if (res.ec != std::errc{}) return fallback;
    return val;
}

uint32_t juceUtf8HashCode32(std::string_view utf8) noexcept {
    uint32_t h = 0;
    size_t i = 0;
    while (i < utf8.size()) {
        const uint32_t b0 = static_cast<unsigned char>(utf8[i++]);
        uint32_t cp = b0;
        if ((b0 & 0x80u) == 0u) {
            cp = b0;
        } else if ((b0 & 0xE0u) == 0xC0u && i < utf8.size()) {
            const uint32_t b1 = static_cast<unsigned char>(utf8[i++]);
            cp = ((b0 & 0x1Fu) << 6) | (b1 & 0x3Fu);
        } else if ((b0 & 0xF0u) == 0xE0u && i + 1 < utf8.size()) {
            const uint32_t b1 = static_cast<unsigned char>(utf8[i++]);
            const uint32_t b2 = static_cast<unsigned char>(utf8[i++]);
            cp = ((b0 & 0x0Fu) << 12) | ((b1 & 0x3Fu) << 6) | (b2 & 0x3Fu);
        } else if ((b0 & 0xF8u) == 0xF0u && i + 2 < utf8.size()) {
            const uint32_t b1 = static_cast<unsigned char>(utf8[i++]);
            const uint32_t b2 = static_cast<unsigned char>(utf8[i++]);
            const uint32_t b3 = static_cast<unsigned char>(utf8[i++]);
            cp = ((b0 & 0x07u) << 18) | ((b1 & 0x3Fu) << 12) | ((b2 & 0x3Fu) << 6) | (b3 & 0x3Fu);
        }
        h = 31u * h + cp;
    }
    return h;
}

std::string computePluginIdentifier(const dsp::PluginDescriptor& desc) {
    const int32_t idToUse = desc.uniqueId != 0 ? desc.uniqueId : desc.deprecatedUid;
    const uint32_t pathHash = juceUtf8HashCode32(desc.fileOrIdentifier);
    std::string out;
    out.reserve(desc.formatName.size() + desc.name.size() + 24);
    out += desc.formatName;
    out.push_back('-');
    out += desc.name;
    out.push_back('-');
    out += toHexString32(static_cast<int32_t>(pathHash));
    out.push_back('-');
    out += toHexString32(idToUse);
    return out;
}

dsp::PluginFormatKind inferFormatKind(std::string_view formatName) noexcept {
    if (formatName == "VST3") return dsp::PluginFormatKind::Vst3;
    if (formatName == "AudioUnit") return dsp::PluginFormatKind::AudioUnit;
    if (formatName == "CLAP") return dsp::PluginFormatKind::Clap;
    if (formatName == "LV2") return dsp::PluginFormatKind::Lv2;
    if (formatName == "VST" || formatName == "VST2") return dsp::PluginFormatKind::Vst2;
    return dsp::PluginFormatKind::Unknown;
}

bool pluginDescriptorFromXmlNode(const XmlNode& node, dsp::PluginDescriptor& out) {
    if (node.tag != "PLUGIN") return false;

    out = dsp::PluginDescriptor{};
    out.name = std::string(node.attr("name"));
    out.descriptiveName = std::string(node.attr("descriptiveName", out.name));
    out.formatName = std::string(node.attr("format"));
    out.format = parseFormatKind(node.attr("notaFormatKind"), out.formatName);
    out.category = std::string(node.attr("category"));
    out.manufacturerName = std::string(node.attr("manufacturer"));
    out.version = std::string(node.attr("version"));
    out.fileOrIdentifier = std::string(node.attr("file"));
    out.isInstrument = parseInt32(node.attr("isInstrument", "0")) != 0;
    out.numInputChannels = parseInt32(node.attr("numInputs", "0"));
    out.numOutputChannels = parseInt32(node.attr("numOutputs", "0"));
    out.hasSharedContainer = parseInt32(node.attr("isShell", "0")) != 0;
    out.deprecatedUid = parseHex32(node.attr("uid", "0"));
    out.uniqueId = parseHex32(node.attr("uniqueId", "0"));

    const std::string_view explicitId = node.attr("notaId");
    out.identifier = explicitId.empty() ? computePluginIdentifier(out) : std::string(explicitId);

    if (const XmlNode* backendXml = node.findChild("BACKEND_XML"); backendXml != nullptr && !backendXml->text.empty()) {
        out.serializedXml = backendXml->text;
    } else {
        XmlNode clean = node;
        clean.children.clear();
        out.serializedXml = clean.toString(0);
    }
    return true;
}

XmlNode pluginDescriptorToXmlNode(const dsp::PluginDescriptor& desc) {
    XmlNode node;
    if (!desc.serializedXml.empty() && parseXmlDocument(desc.serializedXml, node) && node.tag == "PLUGIN") {
        // Keep exact legacy XML attributes untouched; only preserve custom non-JUCE ID if it differs.
        if (!desc.identifier.empty() && desc.identifier != computePluginIdentifier(desc)) {
            node.setAttr("notaId", desc.identifier);
        }
        return node;
    }

    node = XmlNode{};
    node.tag = "PLUGIN";
    node.setAttr("name", desc.name);
    if (!desc.descriptiveName.empty() && desc.descriptiveName != desc.name) {
        node.setAttr("descriptiveName", desc.descriptiveName);
    }
    node.setAttr("format", desc.formatName);
    if (desc.format != dsp::PluginFormatKind::Unknown) {
        node.setAttr("notaFormatKind", std::string(formatKindToString(desc.format)));
    }
    node.setAttr("category", desc.category);
    node.setAttr("manufacturer", desc.manufacturerName);
    node.setAttr("version", desc.version);
    node.setAttr("file", desc.fileOrIdentifier);
    node.setAttr("uniqueId", toHexString32(desc.uniqueId));
    node.setAttrBool("isInstrument", desc.isInstrument);
    node.setAttrInt("numInputs", desc.numInputChannels);
    node.setAttrInt("numOutputs", desc.numOutputChannels);
    node.setAttrBool("isShell", desc.hasSharedContainer);
    node.setAttr("uid", toHexString32(desc.deprecatedUid));
    if (!desc.identifier.empty() && desc.identifier != computePluginIdentifier(desc)) {
        node.setAttr("notaId", desc.identifier);
    }
    return node;
}

} // namespace nota::pluginscanner::detail
