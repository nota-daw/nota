// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include <string>

namespace nota::media::detail {

std::string normalizeExtension(const std::string& pathOrExt);

#ifdef _WIN32
std::wstring widePath(const std::string& utf8);
#endif

} // namespace nota::media::detail
