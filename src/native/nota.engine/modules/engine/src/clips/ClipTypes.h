// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Clip arrangement, clipboard, automation-follow, and warp-cache job types
// shared inside nota::detail across EngineRuntime and clips/* translation units.

#pragma once

#include "core/TempoMap.h"
#include "model/MidiClip.h"
#include "model/Track.h"
#include "nota/media/SampleBuffer.h"
#include "nota/media/Warp.h"

#include <atomic>
#include <cstdint>
#include <memory>
#include <thread>
#include <vector>

namespace nota::detail {

struct ClipMoveReq {
    int32_t srcTrackId;
    int32_t clipIndex;
    int32_t dstTrackId;
    double newStartBeat;
};

struct BlockClip {
    int32_t trackId = -1;
    int32_t kind = -1; // 0 audio, 1 midi
    double relStart = 0.0;
    double len = 0.0;
    bool sameTrack = true;
    AudioClip audio;
    MidiClip midi;
    std::vector<TrackLane> autom;
};

enum class ClipAutomationPass { Replace, Clear, Insert };

struct WarpBuildCursor {
    bool building = false;
    int32_t trackId = -1;
    int32_t clipIndex = -1;
    std::shared_ptr<WarpCache> cache;
    std::unique_ptr<ClipWarpStream> stream;
    WarpInputs renderInputs;
    std::shared_ptr<const TempoMap> tempoMap;
    double tempoStartBeat = -1.0;
    bool reverseSource = false;
    int64_t offset = 0;
};

struct WarpJob {
    int32_t trackId = -1;
    int32_t clipIndex = -1;
    std::shared_ptr<SampleBuffer> sample;
    std::shared_ptr<WarpCache> cache;
    WarpInputs renderInputs;
    std::shared_ptr<const TempoMap> tempoMap;
    double tempoStartBeat = -1.0;
    bool reverseSource = false;
    std::atomic<int64_t> done{0};
    std::atomic<bool> cancel{false};
    std::atomic<bool> finished{false};
    std::thread thread;
};

void buildWarpCache(AudioClip& c, double spb, double devSR);

} // namespace nota::detail
