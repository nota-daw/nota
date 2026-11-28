// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/core/ConfigFile.h"

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <ios>
#include <mutex>
#include <sstream>
#include <string>
#include <string_view>
#include <system_error>

namespace nota::core::cfgfile {
namespace {

std::mutex g_dataDirMutex;
std::string g_dataDirOverride;

} // namespace

void setDataDirOverride(std::string_view dir) {
    std::lock_guard<std::mutex> lock(g_dataDirMutex);
    g_dataDirOverride.assign(dir);
}

std::string dataDirOverride() {
    std::lock_guard<std::mutex> lock(g_dataDirMutex);
    return g_dataDirOverride;
}

std::string dataDir() {
    {
        std::lock_guard<std::mutex> lock(g_dataDirMutex);
        if (!g_dataDirOverride.empty()) {
            std::error_code ec;
            std::filesystem::create_directories(g_dataDirOverride, ec);
            return g_dataDirOverride;
        }
    }
    if (const char* env = std::getenv("NOTA_DATA_DIR"); env && *env) {
        std::error_code ec;
        std::filesystem::create_directories(env, ec);
        return env;
    }
    return {};
}

std::string readFile(std::string_view path) {
    std::ifstream in(std::filesystem::path(path), std::ios::binary);
    if (!in) return {};
    std::ostringstream ss;
    ss << in.rdbuf();
    return ss.str();
}

bool writeFileAtomic(std::string_view path, std::string_view content) {
    std::string p(path);
    std::string tmp = p + ".tmp";
    {
        std::ofstream out(tmp, std::ios::binary | std::ios::trunc);
        if (!out) return false;
        out.write(content.data(), static_cast<std::streamsize>(content.size()));
        if (!out) return false;
    }
    std::error_code ec;
    std::filesystem::rename(tmp, p, ec);
    if (ec) {
        std::filesystem::remove(p, ec);
        std::filesystem::rename(tmp, p, ec);
    }
    return !ec;
}

} // namespace nota::core::cfgfile
