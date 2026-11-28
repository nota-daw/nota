// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// ConfigFile — single-process data directory override & atomic config file IO.

#pragma once

#include "nota/core/Export.h"

#include <string>
#include <string_view>

namespace nota::core::cfgfile {

NOTA_CORE_API void setDataDirOverride(std::string_view dir);
NOTA_CORE_API std::string dataDirOverride();
NOTA_CORE_API std::string dataDir();
NOTA_CORE_API std::string readFile(std::string_view path);
NOTA_CORE_API bool writeFileAtomic(std::string_view path, std::string_view content);

} // namespace nota::core::cfgfile
