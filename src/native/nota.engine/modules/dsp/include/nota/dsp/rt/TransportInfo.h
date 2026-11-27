// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TransportInfo — per-block transport snapshot handed to instruments/devices/plugins.

#pragma once

#include <cstdint>

namespace nota::dsp {

struct TransportInfo {
    double bpm = 120.0;
    int32_t tsNum = 4;
    int32_t tsDenom = 4;
    double ppqPosition = 0.0;
    double timeInSeconds = 0.0;
    int64_t timeInSamples = 0;
    bool isPlaying = false;
    bool isLooping = false;
    double ppqLoopStart = 0.0;
    double ppqLoopEnd = 0.0;
};

} // namespace nota::dsp
