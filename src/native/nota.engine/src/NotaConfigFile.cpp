// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "NotaConfigFile.h"

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <sstream>

namespace nota::cfgfile {
namespace {

std::mutex g_dataDirMutex;
std::string g_dataDirOverride;

} // namespace

void setDataDirOverride(const std::string& dir) {
    std::lock_guard<std::mutex> lock(g_dataDirMutex);
    g_dataDirOverride = dir;
}

std::string dataDirOverride() {
    std::lock_guard<std::mutex> lock(g_dataDirMutex);
    return g_dataDirOverride;
}

std::string dataDir() {
    // The engine never guesses platform paths: the app tells it once via
    // nota_set_data_dir (NotaPaths.DataDir), tests via NOTA_DATA_DIR.
    // Unset (empty) means config persistence is off: loads return defaults,
    // saves and the plugin catalog are skipped.
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

} // namespace nota::cfgfile
