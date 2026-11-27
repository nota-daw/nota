// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace nota::io::midi {

inline constexpr int32_t kMaxMidiSources = 16;

struct MidiDeviceInfo {
    std::string uid;
    std::string name;
};

struct MidiPortInfo {
    int32_t source = 0;
    std::string uid;
    std::string name;
    std::string outputUid;
};

struct MidiConfig {
    std::vector<std::string> disabledInputUids;
    bool mpe = true;
    int32_t mpeBendRange = 48;
};

} // namespace nota::io::midi
