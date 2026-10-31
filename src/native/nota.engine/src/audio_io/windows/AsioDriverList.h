// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Installed ASIO drivers (specs/049, research R3/R10), read from
// HKEY_LOCAL_MACHINE\SOFTWARE\ASIO in the process's native registry view — a 64-bit build
// never sees the 32-bit drivers under WOW6432Node, and an ARM64 build sees ARM64 ones.

#pragma once

#include <string>
#include <vector>

namespace nota::asio {

struct AsioDriverInfo {
    std::string clsid;       // "{xxxxxxxx-…}" as stored in the registry
    std::string name;        // the registry key name
    std::string displayName; // name, with " (2)", " (3)" added to duplicates
};

std::vector<AsioDriverInfo> enumerateAsioDrivers();

// The driver matching `clsid`, else one whose name matches `name` (a reinstalled driver
// may come back with a new CLSID). nullptr if neither matches.
const AsioDriverInfo* findAsioDriver(const std::vector<AsioDriverInfo>& drivers, const std::string& clsid,
                                     const std::string& name);

} // namespace nota::asio
