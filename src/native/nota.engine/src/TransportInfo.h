// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Per-block transport snapshot handed to instruments/devices right before they
// render, so hosted plugins can drive their own tempo-synced behaviour (arps,
// synced delays/LFOs, loopers, Splice Bridge). A plain JUCE-free POD: the plugin
// adapters translate it into a juce::AudioPlayHead. Built-ins ignore it.

#pragma once

#include <cstdint>

namespace nota {

struct TransportInfo {
    double  bpm = 120.0;
    int32_t tsNum = 4;             // time signature numerator
    int32_t tsDenom = 4;           // time signature denominator
    double  ppqPosition = 0.0;     // block-start position in quarter notes (beats)
    double  timeInSeconds = 0.0;   // block-start position in seconds
    int64_t timeInSamples = 0;     // block-start position in samples from the timeline origin
    bool    isPlaying = false;
    bool    isLooping = false;
    double  ppqLoopStart = 0.0;    // loop bounds in quarter notes (valid when isLooping)
    double  ppqLoopEnd = 0.0;
};

} // namespace nota
