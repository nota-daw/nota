// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "codecs/PathUtils.h"

#include <algorithm>
#include <cctype>
#include <string>
#ifdef _WIN32
#include <filesystem>
#endif

namespace nota::media::detail {

std::string normalizeExtension(const std::string& pathOrExt) {
    if (pathOrExt.empty()) return {};
    const auto slash = pathOrExt.find_last_of("/\\");
    const auto dot = pathOrExt.find_last_of('.');
    std::string ext;
    if (dot == std::string::npos) {
        if (slash != std::string::npos) return {};
        ext = pathOrExt;
    } else if (slash == std::string::npos || dot > slash) {
        ext = pathOrExt.substr(dot + 1);
    } else {
        return {};
    }
    std::transform(
        ext.begin(), ext.end(), ext.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    return ext;
}

#ifdef _WIN32
std::wstring widePath(const std::string& utf8) {
    return std::filesystem::path(reinterpret_cast<const char8_t*>(utf8.c_str())).wstring();
}
#endif

} // namespace nota::media::detail
