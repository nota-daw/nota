// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "NotaConfigFile.h"

#include <cstdlib>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <sstream>

namespace nota::cfgfile {
namespace {

// Skip whitespace starting at pos; returns index of first non-ws (or npos-safe end).
size_t skipWs(const std::string& s, size_t pos) {
    while (pos < s.size() && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\n' || s[pos] == '\r'))
        ++pos;
    return pos;
}

// Parse a JSON string literal whose opening quote is at s[pos] == '"'.
// On success sets out to the unescaped value and pos to just past the closing quote.
bool parseString(const std::string& s, size_t& pos, std::string& out) {
    if (pos >= s.size() || s[pos] != '"') return false;
    ++pos;
    out.clear();
    while (pos < s.size()) {
        char c = s[pos++];
        if (c == '"') return true;
        if (c == '\\' && pos < s.size()) {
            char e = s[pos++];
            switch (e) {
                case '"':  out.push_back('"');  break;
                case '\\': out.push_back('\\'); break;
                case '/':  out.push_back('/');  break;
                case 'b':  out.push_back('\b'); break;
                case 'f':  out.push_back('\f'); break;
                case 'n':  out.push_back('\n'); break;
                case 'r':  out.push_back('\r'); break;
                case 't':  out.push_back('\t'); break;
                case 'u': {
                    if (pos + 4 > s.size()) return false;
                    unsigned code = 0;
                    for (int i = 0; i < 4; ++i) {
                        char h = s[pos++];
                        code <<= 4;
                        if (h >= '0' && h <= '9')      code |= unsigned(h - '0');
                        else if (h >= 'a' && h <= 'f') code |= unsigned(h - 'a' + 10);
                        else if (h >= 'A' && h <= 'F') code |= unsigned(h - 'A' + 10);
                        else return false;
                    }
                    // Minimal UTF-8 encode of the BMP code point (config ids are ASCII/BMP).
                    if (code < 0x80) out.push_back(char(code));
                    else if (code < 0x800) {
                        out.push_back(char(0xC0 | (code >> 6)));
                        out.push_back(char(0x80 | (code & 0x3F)));
                    } else {
                        out.push_back(char(0xE0 | (code >> 12)));
                        out.push_back(char(0x80 | ((code >> 6) & 0x3F)));
                        out.push_back(char(0x80 | (code & 0x3F)));
                    }
                    break;
                }
                default: out.push_back(e); break;
            }
        } else {
            out.push_back(c);
        }
    }
    return false; // unterminated
}

// Find the value position just after `"key" :` at the top level. Returns npos if
// not found. (Sufficient for our flat one-object config files.)
size_t findValue(const std::string& json, const std::string& key) {
    std::string needle = "\"" + key + "\"";
    size_t k = json.find(needle);
    if (k == std::string::npos) return std::string::npos;
    size_t p = skipWs(json, k + needle.size());
    if (p >= json.size() || json[p] != ':') return std::string::npos;
    return skipWs(json, p + 1);
}

} // namespace

std::string dataDir() {
    // Resolve the per-user config root, then append "Nota". Windows: %APPDATA%.
    // Linux: $XDG_CONFIG_HOME, else $HOME/.config (XDG Base Directory spec) —
    // mirrors NotaPaths.DataDir on the managed side.
    std::filesystem::path root;
#if defined(_WIN32)
    if (const char* appdata = std::getenv("APPDATA"); appdata && *appdata)
        root = appdata;
#else
    if (const char* xdg = std::getenv("XDG_CONFIG_HOME"); xdg && *xdg)
        root = xdg;
    else if (const char* home = std::getenv("HOME"); home && *home)
        root = std::filesystem::path(home) / ".config";
#endif
    if (root.empty()) return {};
    std::filesystem::path dir = root / "Nota";
    std::error_code ec;
    std::filesystem::create_directories(dir, ec);
    return dir.string();
}

std::string readFile(const std::string& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in) return {};
    std::ostringstream ss;
    ss << in.rdbuf();
    return ss.str();
}

bool writeFileAtomic(const std::string& path, const std::string& content) {
    std::string tmp = path + ".tmp";
    {
        std::ofstream out(tmp, std::ios::binary | std::ios::trunc);
        if (!out) return false;
        out.write(content.data(), std::streamsize(content.size()));
        if (!out) return false;
    }
    std::error_code ec;
    std::filesystem::rename(tmp, path, ec);          // atomic replace on same volume
    if (ec) {
        // rename fails if the destination exists on some setups; fall back.
        std::filesystem::remove(path, ec);
        std::filesystem::rename(tmp, path, ec);
    }
    return !ec;
}

std::string jsonEscape(const std::string& s) {
    std::string out;
    out.reserve(s.size() + 8);
    for (char c : s) {
        switch (c) {
            case '"':  out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\b': out += "\\b";  break;
            case '\f': out += "\\f";  break;
            case '\n': out += "\\n";  break;
            case '\r': out += "\\r";  break;
            case '\t': out += "\\t";  break;
            default:
                if (static_cast<unsigned char>(c) < 0x20) {
                    char buf[8];
                    std::snprintf(buf, sizeof(buf), "\\u%04x", c & 0xFF);
                    out += buf;
                } else {
                    out.push_back(c);
                }
        }
    }
    return out;
}

bool jsonGetString(const std::string& json, const std::string& key, std::string& out) {
    size_t p = findValue(json, key);
    if (p == std::string::npos) return false;
    return parseString(json, p, out);
}

bool jsonGetNumber(const std::string& json, const std::string& key, double& out) {
    size_t p = findValue(json, key);
    if (p == std::string::npos) return false;
    try {
        size_t consumed = 0;
        out = std::stod(json.substr(p), &consumed);
        return consumed > 0;
    } catch (...) {
        return false;
    }
}

bool jsonGetStringArray(const std::string& json, const std::string& key,
                        std::vector<std::string>& out) {
    size_t p = findValue(json, key);
    if (p == std::string::npos) return false;
    if (p >= json.size() || json[p] != '[') return false;
    p = skipWs(json, p + 1);
    if (p < json.size() && json[p] == ']') return true; // empty array
    while (p < json.size()) {
        std::string v;
        if (!parseString(json, p, v)) return false;
        out.push_back(std::move(v));
        p = skipWs(json, p);
        if (p < json.size() && json[p] == ',') { p = skipWs(json, p + 1); continue; }
        if (p < json.size() && json[p] == ']') return true;
        return false;
    }
    return false;
}

} // namespace nota::cfgfile
