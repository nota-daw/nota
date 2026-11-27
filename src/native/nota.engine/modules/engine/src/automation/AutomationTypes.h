// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Parameter automation write, touch gesture, and override tracking types.

#pragma once

#include <cstdint>
#include <string>

namespace nota::detail {

struct ActiveWrite {
    int32_t trackId;
    int32_t target;
    int32_t deviceIndex;
    int32_t paramIndex;
    std::string paramId;
    int32_t laneIndex;
    double lastBeat;
    bool latch;
    bool sampled = false;
    float before = 0.0f;
};

struct OverrideTarget {
    int32_t trackId;
    int32_t laneIndex;
};

struct TouchGesture {
    int32_t trackId;
    int32_t target;
    int32_t deviceIndex;
    int32_t paramIndex;
    std::string paramId;
    int32_t handle;
};

} // namespace nota::detail
