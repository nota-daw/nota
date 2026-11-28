// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session launch landing rules and live slot player table types.

#pragma once

#include "session/SessionPlayer.h"

#include <array>
#include <cstdint>
#include <memory>
#include <vector>

namespace nota::detail {

struct RuleWait {
    int32_t gen = 0;
    int32_t trackId = 0;
    int32_t scene = -1;
};

struct SessionLaneSlots {
    std::array<SessionPlayer*, 16> player{};
    std::array<int32_t, 16> scene{};
    std::array<double, 16> len{};
    std::vector<std::shared_ptr<SessionPlayer>> keep;
};

} // namespace nota::detail
