// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/io/Export.h"

#include <string>
#include <vector>

namespace nota::io::audio::asio {

struct AsioDriverInfo {
    std::string clsid;
    std::string name;
    std::string displayName;
};

NOTA_IO_API std::vector<AsioDriverInfo> enumerateAsioDrivers();
NOTA_IO_API const AsioDriverInfo* findAsioDriver(
    const std::vector<AsioDriverInfo>& drivers, const std::string& clsid, const std::string& name);

} // namespace nota::io::audio::asio
